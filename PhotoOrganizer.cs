using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// FileOrganizer - Moves ALL files into year-based folders.
///
/// Date resolution rules:
///   Image files  → EXIF "Date Taken"  (DateTimeOriginal)
///                  fallback: Last Modified date
///   All others   → Last Modified date  (always)
///
/// Duplicate filenames → destination is OVERWRITTEN.
/// </summary>
class FileOrganizer
{
    // ── Configuration ─────────────────────────────────────────────────────────
    // !! Change these two paths before running !!
    private static readonly string SourceRoot      = @"C:\Users\omerf\Pictures\Camera";
    private static readonly string DestinationRoot = @"C:\Users\omerf\Pictures\USA";

    // ── Image extensions — will attempt EXIF Date Taken first ─────────────────
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tiff", ".tif",
        ".heic", ".heif", ".bmp", ".gif",
        ".raw", ".cr2", ".cr3", ".nef", ".nrw",
        ".arw", ".orf", ".rw2", ".dng", ".pef",
        ".srw", ".x3f", ".raf", ".psd"
    };

    // ── Non-image extensions — always use Last Modified date ──────────────────
    // Covers everything visible in your screenshot and common extras.
    private static readonly HashSet<string> OtherExtensions = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        // Video
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".m4v",
        ".3gp", ".3g2", ".vob", ".mpg", ".mpeg", ".ts", ".m2ts",
        // Audio
        ".mp3", ".wav", ".flac", ".aac", ".wma", ".m4a", ".ogg",
        // Documents
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".rtf", ".odt", ".csv",
        // Archives
        ".zip", ".rar", ".7z", ".tar", ".gz",
        // Web / Data
        ".html", ".htm", ".json", ".xml",
        // System / misc (from screenshot: INI, MTA, BUP, IFO, DWZ, etc.)
        ".ini", ".mta", ".bup", ".ifo", ".dwz",
    };

    // EXIF tag for DateTimeOriginal
    private const int    ExifTagDateTimeOriginal = 36867;
    private const string ExifDateFormat          = "yyyy:MM:dd HH:mm:ss";

    // ── Counters ──────────────────────────────────────────────────────────────
    private static int _movedImage        = 0;   // images moved via EXIF date
    private static int _movedExifFallback = 0;   // images moved via Last Modified (no EXIF)
    private static int _movedFilename     = 0;   // files moved via date parsed from filename
    private static int _movedOther        = 0;   // non-images moved via Last Modified
    private static int _overwritten       = 0;   // destination file replaced
    private static int _skipped           = 0;   // same-file, already in place
    private static int _errors            = 0;

    private static readonly List<string> _log = new List<string>();

    // ─────────────────────────────────────────────────────────────────────────
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        PrintBanner();

        string source = args.Length > 0 ? args[0] : SourceRoot;
        string dest   = args.Length > 1 ? args[1] : DestinationRoot;

        if (!Directory.Exists(source))
        {
            ConsoleError($"Source directory not found: {source}");
            return;
        }

        Directory.CreateDirectory(dest);

        Log($"Source      : {source}");
        Log($"Destination : {dest}");
        Log($"Started     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Log(new string('-', 70));

        bool dryRun = PromptDryRun();
        if (dryRun)
            Log("*** DRY-RUN MODE -- no files will be moved ***");

        Log(string.Empty);

        // ── Scan all files recursively ────────────────────────────────────────
        foreach (string filePath in Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(filePath);

            if (ImageExtensions.Contains(ext))
                ProcessImageFile(filePath, dest, dryRun);
            else if (OtherExtensions.Contains(ext))
                ProcessOtherFile(filePath, dest, dryRun, unknownExt: false);
            else
                // Unknown extension: still move it using Last Modified
                // so nothing gets left behind. Tagged in the log as [OTHER-UNK].
                ProcessOtherFile(filePath, dest, dryRun, unknownExt: true);
        }

        // ── Summary ───────────────────────────────────────────────────────────
        Log(string.Empty);
        Log(new string('-', 70));
        Log($"Finished          : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Log($"Images (EXIF)     : {_movedImage,-6}  moved using EXIF Date Taken");
        Log($"Images (fallback) : {_movedExifFallback,-6}  image had no EXIF -- used Last Modified");
        Log($"Filename dates     : {_movedFilename,-6}  moved using date parsed from filename");
        Log($"Other files       : {_movedOther,-6}  moved using Last Modified date");
        Log($"Overwritten       : {_overwritten,-6}  duplicate filename -- destination replaced");
        Log($"Skipped           : {_skipped,-6}  already in place (same path)");
        Log($"Errors            : {_errors}");

        string logPath = Path.Combine(
            AppContext.BaseDirectory,
            $"FileOrganizer_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        File.WriteAllLines(logPath, _log, Encoding.UTF8);
        Console.WriteLine($"\nLog saved to: {logPath}");
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    // ── Image files: try EXIF first, fall back to Last Modified ───────────────
    static void ProcessImageFile(string filePath, string destRoot, bool dryRun)
    {
        DateTime? exifDate = GetExifDateTaken(filePath);
        DateTime date;
        string tag;

        if (exifDate.HasValue)
        {
            _movedImage++;
            date = exifDate.Value;
            tag  = "[IMG-EXIF]    ";
        }
        else if (TryParseDateFromFilename(filePath, out DateTime fnameDate))
        {
            _movedFilename++;
            date = fnameDate;
            tag  = "[IMG-FNAME]  ";
        }
        else
        {
            _movedExifFallback++;
            date = File.GetLastWriteTime(filePath);
            tag  = "[IMG-FALLBACK]";
        }

        MoveFile(filePath, destRoot, date.Year, dryRun, tag);
    }

    // ── Non-image / unknown files: always Last Modified ───────────────────────
    static void ProcessOtherFile(string filePath, string destRoot, bool dryRun, bool unknownExt)
    {
        DateTime date;
        string tag;

        if (TryParseDateFromFilename(filePath, out DateTime fnameDate))
        {
            _movedFilename++;
            date = fnameDate;
            tag  = "[OTHER-FNAME] ";
        }
        else
        {
            date = File.GetLastWriteTime(filePath);
            _movedOther++;
            tag = unknownExt ? "[OTHER-UNK]   " : "[OTHER]       ";
        }

        MoveFile(filePath, destRoot, date.Year, dryRun, tag);
    }

    // Try to parse a date/time from the filename. Returns true if successful.
    static bool TryParseDateFromFilename(string filePath, out DateTime date)
    {
        date = default;
        string name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(name)) return false;

        // Look for an 8-digit date (YYYYMMDD) optionally followed by '_' or '-' and a 6-digit time (HHMMSS)
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(19|20)\d{6}(?:[_-]?(\d{6}))?");
        if (!m.Success) return false;

        string ymd  = m.Value.Substring(0, 8);
        string hms  = null;
        if (m.Groups.Count > 1 && m.Groups[2].Success)
            hms = m.Groups[2].Value;

        string fmt = hms == null ? "yyyyMMdd" : "yyyyMMddHHmmss";
        string combined = hms == null ? ymd : ymd + hms;
        if (DateTime.TryParseExact(combined, fmt, null, System.Globalization.DateTimeStyles.None, out DateTime dt))
        {
            date = dt;
            return true;
        }
        return false;
    }

    // ── Core move logic ───────────────────────────────────────────────────────
    static void MoveFile(string filePath, string destRoot, int year,
                         bool dryRun, string logTag)
    {
        try
        {
            string targetDir  = Path.Combine(destRoot, year.ToString());
            string fileName   = Path.GetFileName(filePath);
            string targetPath = Path.Combine(targetDir, fileName);

            // Same-file guard (file is already exactly where it should go)
            if (string.Equals(filePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                Log($"[SKIP-SAME]    {filePath}");
                _skipped++;
                return;
            }

            bool isOverwrite = File.Exists(targetPath);
            if (isOverwrite) _overwritten++;

            if (!dryRun)
            {
                Directory.CreateDirectory(targetDir);
                if (isOverwrite) File.Delete(targetPath);
                File.Move(filePath, targetPath);
            }

            string owTag = isOverwrite ? " [OVERWRITE]" : "";
            Log($"{logTag}{owTag} {filePath}");
            Log($"               -> {targetPath}");
        }
        catch (Exception ex)
        {
            _errors++;
            Log($"[ERROR]        {filePath}");
            Log($"               {ex.Message}");
        }
    }

    // ── EXIF reader ───────────────────────────────────────────────────────────
    static DateTime? GetExifDateTaken(string filePath)
    {
        try
        {
#if WINDOWS
            using var img = System.Drawing.Image.FromFile(filePath);
            // Try common EXIF date tags in order of preference:
            // DateTimeOriginal (36867), DateTimeDigitized (36868), DateTime (306)
            int[] candidateTags = { ExifTagDateTimeOriginal, 36868, 306 };
            foreach (int tagId in candidateTags)
            {
                var prop = img.PropertyItems.FirstOrDefault(p => p.Id == tagId);
                if (prop?.Value == null) continue;
                string raw = Encoding.ASCII.GetString(prop.Value).TrimEnd('\0');
                if (DateTime.TryParseExact(raw, ExifDateFormat,
                        null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                    return dt;
            }
#else
            DateTime? dt = ReadExifDateRaw(filePath);
            if (dt.HasValue) return dt;
#endif
        }
        catch { }
        return null;
    }

    static DateTime? ReadExifDateRaw(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // ── JPEG ──────────────────────────────────────────────────────────
            byte b0 = br.ReadByte(), b1 = br.ReadByte();
            if (b0 == 0xFF && b1 == 0xD8)
            {
                while (fs.Position < fs.Length - 4)
                {
                    if (br.ReadByte() != 0xFF) break;
                    byte marker = br.ReadByte();
                    int  segLen = ReadBigEndianU16(br) - 2;

                    if (marker == 0xE1 && segLen > 6)
                        return ParseExifSegment(br.ReadBytes(segLen));

                    fs.Seek(segLen, SeekOrigin.Current);
                    if (marker == 0xDA) break; // start of image data
                }
                return null;
            }

            // ── TIFF / RAW formats ────────────────────────────────────────────
            fs.Position = 0;
            byte[] hdr = br.ReadBytes(4);
            bool le    = hdr[0] == 'I' && hdr[1] == 'I';
            ushort magic = le
                ? BitConverter.ToUInt16(new[] { hdr[2], hdr[3] }, 0)
                : (ushort)(hdr[2] << 8 | hdr[3]);
            if (magic != 42) return null;
            return ParseTiffIfd(br, fs, le, baseOff: 0);
        }
        catch { return null; }
    }

    static DateTime? ParseExifSegment(byte[] data)
    {
        if (data.Length < 14) return null;
        if (data[0] != 'E' || data[1] != 'x' || data[2] != 'i' ||
            data[3] != 'f' || data[4] != 0   || data[5] != 0) return null;
        bool le = data[6] == 'I' && data[7] == 'I';
        using var ms = new MemoryStream(data, 6, data.Length - 6);
        using var br = new BinaryReader(ms);
        br.ReadBytes(4); // skip byte-order mark + magic
        return ParseTiffIfd(br, ms, le, baseOff: 0);
    }

    static DateTime? ParseTiffIfd(BinaryReader br, Stream stream, bool le, long baseOff)
    {
        try
        {
            long start      = stream.Position - 4 + baseOff;
            stream.Position = start + 4;
            uint ifdOff     = ReadU32(br, le);
            stream.Position = start + ifdOff;

            ushort count = ReadU16(br, le);
            for (int i = 0; i < count; i++)
            {
                ushort tag  = ReadU16(br, le);
                ushort type = ReadU16(br, le);
                uint   comp = ReadU32(br, le);
                byte[] voff = br.ReadBytes(4);

                if ((tag == ExifTagDateTimeOriginal || tag == 36868 || tag == 306) && type == 2)
                {
                    long saved  = stream.Position;
                    uint valOff = le
                        ? BitConverter.ToUInt32(voff, 0)
                        : (uint)(voff[0] << 24 | voff[1] << 16 | voff[2] << 8 | voff[3]);
                    stream.Position = start + valOff;
                    string dateStr  = Encoding.ASCII.GetString(br.ReadBytes((int)comp)).TrimEnd('\0');
                    stream.Position = saved;
                    if (DateTime.TryParseExact(dateStr, ExifDateFormat,
                            null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                        return dt;
                }

                if (tag == 34665) // SubExif IFD pointer
                {
                    long saved  = stream.Position;
                    uint subOff = le
                        ? BitConverter.ToUInt32(voff, 0)
                        : (uint)(voff[0] << 24 | voff[1] << 16 | voff[2] << 8 | voff[3]);
                    stream.Position = start + subOff;
                    DateTime? sub   = ParseTiffIfd(br, stream, le, start);
                    if (sub.HasValue) return sub;
                    stream.Position = saved;
                }
            }
        }
        catch { }
        return null;
    }

    // ── Binary helpers ────────────────────────────────────────────────────────
    static ushort ReadU16(BinaryReader br, bool le)
    {
        byte[] b = br.ReadBytes(2);
        return le ? BitConverter.ToUInt16(b, 0) : (ushort)(b[0] << 8 | b[1]);
    }
    static uint ReadU32(BinaryReader br, bool le)
    {
        byte[] b = br.ReadBytes(4);
        return le ? BitConverter.ToUInt32(b, 0)
                  : (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }
    static int ReadBigEndianU16(BinaryReader br)
    {
        byte hi = br.ReadByte(), lo = br.ReadByte();
        return (hi << 8) | lo;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
    static bool PromptDryRun()
    {
        Console.Write("Run in DRY-RUN mode first? (recommended) [Y/n]: ");
        string input = Console.ReadLine()?.Trim().ToLower() ?? "y";
        return input != "n";
    }

    static void Log(string message) { _log.Add(message); Console.WriteLine(message); }

    static void ConsoleError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
+====================================================+
|           File Organizer by Year                   |
|  Images -> EXIF Date Taken | Others -> Last Modified |
+====================================================+");
        Console.ResetColor();
        Console.WriteLine();
    }
}
