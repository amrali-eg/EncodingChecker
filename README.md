[![CI](https://github.com/amrali-eg/EncodingChecker/actions/workflows/ci.yml/badge.svg)](https://github.com/amrali-eg/EncodingChecker/actions/workflows/ci.yml)

# EncodingChecker v3.2

File Encoding Checker detects, validates, and converts the text encoding of one or more files. It runs either as a Windows GUI app or as a command-line tool for scripting and CI, and shares one detection/conversion engine between both.

Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) (Windows only).

Each [release](https://github.com/amrali-eg/EncodingChecker/releases) publishes two single-file builds: `EncodingChecker.zip` (framework-dependent, requires the .NET 10 Desktop Runtime above) and `EncodingChecker-selfcontained.zip` (larger, but runs on a machine with no .NET runtime installed).

![form image](./form.png "File Encoding Checker Form Preview")

## Highlights

- Layered detection: byte-order-mark and heuristic checks for Unicode encodings, [UtfUnknown](https://github.com/CharsetDetector/UTF-unknown) for legacy code pages, each candidate independently verified by strict decoding before being trusted.
- Lossless, safe conversion: every write is verified afterward by comparing a SHA-256 hash of the decoded content, so a silent encoder substitution (e.g. an unrepresentable character) is caught and reported as an error instead of corrupting the file.
- Optional `.bak` backup before overwriting, and a `-WhatIf` dry-run mode that reports what would happen without touching any file.
- Covered by an xUnit test suite exercising the detection/conversion engine, CLI argument parsing, and CSV report formatting across multilingual content and edge cases.

## GUI usage

Launch `EncodingChecker.exe` with no arguments. Pick a directory and file filters, choose **View** to detect encodings, **Validate** against a set of accepted charsets, or **Convert** to a target encoding. Results can be exported to CSV.

## Command-line usage

Launch `EncodingChecker.exe` with arguments to run in console mode instead. Run `EncodingChecker.exe -?` (or `-h`, `/?`, `--help`) at any time to print this from the tool itself.

```
EncodingChecker.exe
    -BasePath <directory>
    [-Include "<pattern1,pattern2,...>"]
    [-Exclude "<pattern1,pattern2,...>"]

    -Target "<encoding>"          # Convert mode (default); e.g. "utf-8" or "utf-8-bom"
    -Validate "<charset1,...>"    # Validate mode: flag files not in this list
    -DetectOnly                   # Read-only detection mode

    [-Report <path>]              # Also write a CSV report to this path
    [-MaxParallelism <N>]         # Default: min(logical processor count, 4)
    [-WhatIf]                     # Convert mode: report without writing
    [-Backup]                     # Convert mode: write "<file>.bak" before overwriting
    [-Quiet]                      # Suppress per-file rows; print only a summary
    [-Verbose]                    # Print error detail and a result breakdown
    [-FailOnChanges]              # Non-zero exit code if any file needs (or, under
                                   # -Validate, fails) conversion — useful as a CI gate
```

`-Include`/`-Exclude` are comma-separated wildcard patterns. A pattern with no `/` or `\` matches just the filename (e.g. `*.cs` matches at any depth); a pattern containing a separator matches the path relative to `-BasePath` instead (e.g. `src/*.cs` matches only under `src`, `\` and `/` behave the same way). `.git`, `.svn`, `.hg`, `.vs`, `.idea`, `bin`, `obj`, `node_modules`, `packages`, `dist`, `build`, and `target` directories are always skipped. Convert, Validate, and Detect-only are mutually exclusive modes.

Exit codes: `0` clean, `1` usage/argument error, `2` `-FailOnChanges` triggered, `3` one or more files failed to process, `4` cancelled (Ctrl+C).

Examples:

```bash
EncodingChecker.exe -BasePath C:\Source -Include "*.cs,*.txt" -Target "utf-8"

EncodingChecker.exe -BasePath . -Include "*.cpp,*.hpp" -Target "utf-8" -WhatIf

EncodingChecker.exe -BasePath . -Include "*" -Validate "utf-8,utf-8-bom" -Report report.csv -FailOnChanges
```

## Safety model

These are the guarantees the implementation actually provides.

- Unicode content is decoded and re-encoded through a strict `Decoder`/`Encoder`
  pair (`DecoderFallback.ExceptionFallback` / `EncoderFallback.ExceptionFallback`):
  malformed input is rejected rather than silently replaced. There is no
  raw-byte conversion path — every encoding, Unicode or legacy, goes through
  decode/re-encode.
- Every write is verified before installation by re-decoding the temporary
  file and comparing a SHA-256 hash of its *decoded* content and BOM state
  against the source, so an encoder that silently substitutes an
  unrepresentable character (e.g. Windows-1252 writing `?` instead of
  throwing) is caught and reported as an error instead of corrupting the
  file.
- The source file is never rewritten in place: conversion writes to a new
  temporary file beside the destination, which is verified before it is
  installed.
- Immediately before installation, the destination is revalidated (length
  and last-write time) so a file changed elsewhere during conversion is not
  silently overwritten. **This is a point-in-time race check, not a
  complete elimination of every possible TOCTOU window.**
- Original file attributes and timestamps are preserved: applied to the
  temporary file before installation, so the final file's metadata is
  correct atomically along with its content.
- With `-Backup`, the original is copied to `<file>.bak` *before* the main
  file is replaced; if the backup fails, the main conversion is aborted and
  the original is left untouched. A previously read-only `.bak` is still
  replaced correctly.
- `-BasePath` itself is rejected if it is a symbolic link, junction, or
  other reparse point. Reparse-point subdirectories are skipped during
  traversal, and a file that is (or becomes) a reparse point is rejected at
  the point of installation.
- `.bak` files and the tool's own abandoned temporary files — both the
  conversion temp file and the `-Backup` install's own temp file — are
  automatically excluded from scanning, including under a broad
  `-Include "*"`, so a later run never treats its own output as input.
- Installation uses .NET's `File.Replace` where supported; a plain,
  non-atomic move is used only when that platform support is genuinely
  unavailable, never as a silent fallback after a real replacement
  failure.
- Cleanup of the temporary file after a failure clears any inherited
  ReadOnly attribute before deleting it, and a cleanup failure can never
  replace or mask the actual error being reported — conversion results are
  returned as structured data, not thrown, so the result is already
  finalized before cleanup ever runs.
- Cancellation (Ctrl+C in the CLI) is observed between files and at
  multiple points within a single file's conversion; a cancelled run never
  leaves a half-written destination, because the destination is only
  touched by the final install step.

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

The original project [EncodingChecker](https://archive.codeplex.com/?p=encodingchecker) on CodePlex was written by [Jeevan James](https://github.com/JeevanJames).

For encoding detection, File Encoding Checker uses the [UtfUnknown](https://github.com/CharsetDetector/UTF-unknown) library, a C# port of [uchardet](https://gitlab.freedesktop.org/uchardet/uchardet), itself a C++ port of the original [Mozilla Universal Charset Detector](https://dxr.mozilla.org/mozilla/source/extensions/universalchardet/). See [THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt) for its license.

## License

[Mozilla Public License 2.0](./LICENSE)
