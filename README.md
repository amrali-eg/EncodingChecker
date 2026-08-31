[![CI](https://github.com/amrali-eg/EncodingChecker/actions/workflows/ci.yml/badge.svg)](https://github.com/amrali-eg/EncodingChecker/actions/workflows/ci.yml)

# EncodingChecker v3.9.2

EncodingChecker is a Windows tool for finding, checking, and safely converting text-file encodings. Use the GUI for everyday work or the command line for repeatable jobs.

Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download). Releases provide both a small framework-dependent build and a self-contained build.

![EncodingChecker window](./form.png)

## Start here

### GUI

1. Open the folder and select file patterns.
2. Choose **View** to inspect the files.
3. Select rows and choose **Convert**.
4. Read the review, then confirm the files that are safe to convert.

Nothing is changed until the final confirmation. The GUI enables backups by default.
Use the single **Export results** menu to export selected rows as text, the displayed
results as CSV, or the most recent completed conversion's immutable journal as JSON.

> [!TIP]
> **Safe workflow:** View → review → choose a legacy source encoding if needed → Convert → keep the resulting `.bak` and `.ecmeta.json` files.

## How conversion works

View → review → confirm → verified conversion

[Read the conversion workflow](docs/CONVERSION-WORKFLOW.md)

### Command line

For a folder you have not converted before, make a plan first:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Target utf-8 -Plan plan.json
```

Review the summary and `plan.json`, then apply exactly that plan:

```powershell
EncodingChecker.exe -Apply plan.json
```

The plan is tied to the selected files and their hashes. If a scheduled source file changes after review, nothing is converted.

## The conversion rule

**Unicode and ASCII files can be converted automatically.**

**Legacy text needs an explicit source encoding.** Tell EC what it is with `-From` on the command line or the source-encoding chooser in the GUI:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Target utf-8 -From windows-1252 -Backup
```

Choosing a source encoding does not bypass safety checks. EC also refuses a choice that conflicts with a fully validated UTF-8 or BOM-confirmed UTF-16/32 reading. The source must still decode strictly, the output must verify as the same text, and a backup failure stops the conversion.

## Common commands

```powershell
# Inspect detected encodings only; do not modify files.
EncodingChecker.exe -BasePath "C:\Files" -DetectOnly

# Preview selected files once; do not modify files or create a plan.
EncodingChecker.exe -BasePath "C:\Files" -Include "*.cs,*.txt" `
  -Exclude "*.g.cs,*.designer.cs" -Target utf-8 -WhatIf

# Validate in CI, save a CSV report, and fail when a file is outside the list.
EncodingChecker.exe -BasePath "C:\Files" -Validate "utf-8,utf-8-bom" `
  -Report validation.csv -FailOnChanges -Quiet

# Convert known legacy text, preserve originals, and record the run.
EncodingChecker.exe -BasePath "C:\Files" -Include "*.txt" -From windows-1252 `
  -Target utf-8 -Backup -Journal conversion.json -Verbose

# Limit work against a network or slow disk.
EncodingChecker.exe -BasePath "D:\Share" -Target utf-8 -MaxParallelism 2
```

## Command-line reference

| Option | Meaning |
| --- | --- |
| `-BasePath <directory>` | Folder to scan. Required except with `-Apply`. |
| `-Target <encoding>` | Target encoding, such as `utf-8` or `utf-8-bom`. Required for conversion. |
| `-From <encoding>` | Explicit original encoding for every selected file. Use for legacy conversion. |
| `-Plan <path>` | Write a reviewable plan; do not modify files. |
| `-Apply <path>` | Execute a saved plan. Its scope and settings are fixed. Only `-Journal`, `-Quiet`, and `-MaxParallelism` may be added; `-WhatIf` and conversion options are rejected. |
| `-WhatIf` | One-time preview; do not write files. |
| `-Backup` | Save every replaced original as `<file>.bak`. |
| `-DetectOnly` | Report detected encodings; do not modify files. |
| `-Validate <encodings>` | Strictly validate complete files against an allowed encoding list; do not modify files. |
| `-Include` / `-Exclude` | Comma-separated wildcard patterns; either option may be repeated. |
| `-Report <path>` | Also write the CSV report as UTF-8 with BOM (Excel-friendly). |
| `-Journal <path>` | Write a JSON record of conversion decisions and results. |
| `-Quiet` / `-Verbose` | Show only a summary, or include details and a result breakdown. |
| `-MaxParallelism <N>` | Maximum simultaneous files; default is `min(CPU count, 4)`. |
| `-FailOnChanges` | Return exit code `2` if files need conversion or fail validation. Useful in CI. |

Patterns without a path separator match filenames at any depth, such as `*.txt`. Patterns containing `/` or `\` match paths relative to `-BasePath`, such as `src/*.cs`. Build and metadata folders including `.git`, `bin`, `obj`, and `node_modules` are skipped automatically. Hidden files, system files, and shortcuts to files elsewhere are left alone as well. If a scan comes across any of these, it tells you how many it passed over, so a clean result never hides files EC did not open.

Run `EncodingChecker.exe -?` for the same reference from the executable. The help aliases are `-?`, `/?`, `-h`, `/h`, and `--help`. Exit codes: `0` completed, `1` invalid command, `2` `-FailOnChanges`, `3` processing/plan/report failure, `4` cancelled, `5` conversion safely refused.

## Safety and transparency

Every conversion uses strict decoding and encoding, verifies the output text before installation, and writes through a temporary file rather than in place. With `-Backup`, EC verifies the backup before it replaces the source. The GUI enables backups by default.

For the complete safety model, conversion-plan guarantees, recovery metadata, known limits, and independent corpus audit, see [Safety and audit](docs/SAFETY-AUDIT.md). The audit harness and reproducible per-file evidence live in [CorpusTesters](https://github.com/amrali-eg/CorpusTesters).

## Known limits

File bytes alone cannot always identify the original legacy encoding uniquely. EC therefore leaves detected legacy text unchanged until you choose or confirm its source encoding. Keep each `.bak` file with its matching `.ecmeta.json` record: together they provide independently verifiable recovery information, although EC does not currently include a restore command.

## Supported charsets

Over forty charsets, matching what [UtfUnknown](https://github.com/CharsetDetector/UTF-unknown) can report and .NET can encode/decode:

* ASCII
* UTF-8 (with or without a BOM)
* UTF-16 BE or LE (with or without a BOM)
* UTF-32 BE or LE (with or without a BOM)
* Arabic: iso-8859-6, windows-1256.
* Baltic: iso-8859-4, windows-1257.
* Central European: ibm852, iso-8859-2, windows-1250, x-mac-ce.
* Chinese (Traditional and Simplified): big5, GB18030, hz-gb-2312, x-cp50227.
* Cyrillic (primarily Russian): IBM855, cp866, iso-8859-5, koi8-r, windows-1251, x-mac-cyrillic.
* Estonian: iso-8859-13.
* Greek: iso-8859-7, windows-1253.
* Hebrew: iso-8859-8, windows-1255.
* Japanese: euc-jp, iso-2022-jp, shift_jis.
* Korean: euc-kr, iso-2022-kr, ks_c_5601-1987 (cp949).
* Thai: windows-874 (aliases TIS-620 and iso-8859-11 in .NET)
* Turkish: iso-8859-3, iso-8859-9.
* Western European: iso-8859-1, iso-8859-15, windows-1252.
* Vietnamese: windows-1258.

**UTF-7 is not supported.** .NET disables its UTF-7 encoder/decoder by default for security reasons (see [SYSLIB0001](https://learn.microsoft.com/dotnet/fundamentals/syslib-diagnostics/syslib0001)) — UTF-7 content can be crafted to evade validation that assumes a different encoding.

## Credits

The original [EncodingChecker](https://archive.codeplex.com/?p=encodingchecker) project was written by [Jeevan James](https://github.com/JeevanJames).

For detection, EncodingChecker uses [UtfUnknown](https://github.com/CharsetDetector/UTF-unknown), a C# port of uchardet. See [THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt) for license details.

## License

[Mozilla Public License 2.0](./LICENSE)
