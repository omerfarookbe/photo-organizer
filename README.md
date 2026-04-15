# PhotoOrganizer — README

Organizes files (photos, videos, documents, etc.) into **year-based folders** using the EXIF
"Date Taken" (DateTimeOriginal) when available for images. When EXIF is not present the
file's Last Modified date is used. The program does not use the file Creation date.

---

## Quick Setup

### 1. Edit the two paths at the top of `PhotoOrganizer.cs`

```csharp
private static readonly string SourceRoot      = @"C:\Photos\Source";   // your photos
private static readonly string DestinationRoot = @"C:\Photos\Sorted";   // output root
```

You can also pass them as command-line arguments:

```
PhotoOrganizer.exe "D:\My Pictures" "E:\Sorted Photos"
```

### 2. Build

```bash
dotnet build -c Release
```

### 3. Run (always do a dry-run first!)

```bash
dotnet run --project PhotoOrganizer.csproj
# — OR after build —
.\bin\Release\net8.0\PhotoOrganizer.exe
```

When prompted, press **Y** (or Enter) for dry-run mode. Review the log,
then run again and choose **N** to actually move the files.

---

## What It Does

| Scenario | Behaviour |
|---|---|
| EXIF "Date Taken" found | Moved to `DestinationRoot\<YEAR>\photo.jpg` |
| No EXIF date | Falls back to **Last Modified** year for the folder |
| Duplicate filename in target | **Destination file is overwritten** |
| Unsupported / unknown extension | Treated as "other" and moved using Last Modified (logged as [OTHER-UNK]) |
| Any read/write error | Logged, counted, skipped |

---

## Supported File Types

`.jpg` `.jpeg` `.png` `.tiff` `.tif` `.heic` `.heif` `.bmp` `.gif`  
`.raw` `.cr2` `.cr3` `.nef` `.nrw` `.arw` `.orf` `.rw2` `.dng`  
`.pef` `.srw` `.x3f` `.raf`

---

## Output Structure

Files are placed under the destination root in a folder named for the year
determined from the chosen date (EXIF Date Taken or Last Modified). Example:

```
Sorted\
  2018\
    IMG_0001.jpg
    IMG_0002.jpg
  2019\
    vacation.jpg
  2023\
    birthday.jpg
    birthday.jpg   ← if a file with the same name already exists it will be overwritten
```

Files that lack EXIF metadata (or have an unknown extension) are not skipped —
they are moved into the year folder derived from their Last Modified timestamp and
are logged with the tag `[OTHER-UNK]`.

---

## Log File

A `.log` file is written next to the executable after every run, e.g.:

```
PhotoOrganizer_20260415_093012.log
```

---

## Notes

- **No external libraries required.** The project contains an internal EXIF reader
  implemented with simple JPEG/TIFF parsing. Coverage for some formats (HEIC/HEIF
  and many vendor RAW containers) may be limited — when EXIF cannot be read the
  program falls back to the file's Last Modified timestamp.
- On Windows you can uncomment `<DefineConstants>WINDOWS</DefineConstants>` in
  the `.csproj` to enable the `System.Drawing` path for EXIF reading. (On non-Windows
  platforms the manual parser is used.)
- The program **moves** files (not copies). Duplicate filenames at the destination
  are overwritten by default. Always run a dry-run first to inspect what would
  happen.
- If the destination root is inside the source root the program may re-enumerate
  moved files. It's recommended to make the destination root separate from the
  source directory.
- The log file is written next to the executable (AppContext.BaseDirectory) with
  a timestamped name like `FileOrganizer_YYYYMMDD_HHMMSS.log`.
