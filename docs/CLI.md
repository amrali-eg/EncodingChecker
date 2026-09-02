# Command-line reference

## Basic forms

```text
EncodingChecker.exe -BasePath <directory> -Target <encoding> [options]
EncodingChecker.exe -Apply <plan.json> [options]
```

The GUI is usually simplest for one-time work. The command line is useful for
repeatable work, CI, and reviewed batch conversions.

For a small job you are reviewing and converting in one session, direct
conversion is easiest. For an important batch, automation, or review that
happens later, use `-Plan` followed by `-Apply`: it binds approval to the exact
source files and refuses the entire plan if any scheduled file has changed.

## File selection

| Option | Meaning |
|---|---|
| `-BasePath <directory>` | Folder to scan. Required except with `-Apply`. |
| `-Include <patterns>` | Comma-separated wildcard patterns; may be repeated. |
| `-Exclude <patterns>` | Comma-separated wildcard patterns; may be repeated. |

An explicitly supplied `-Include` or `-Exclude` must contain at least one
non-empty pattern. Values such as `""`, `",,,"`, or whitespace are rejected.

A pattern without `/` or `\` matches file names at any depth. A pattern with a
separator matches the path relative to `-BasePath`. `/` and `\` are equivalent.
`*` and `?` are supported wildcards.

EC always excludes files it can recognise as its own by name: `.bak` backups,
`.ecmeta.json` recovery records, and `.unicodechecker.tmp` temporaries. It also
excludes the plan, journal, and report files written by the current command.

It does **not** exclude plans, journals, or reports left by earlier runs. You
choose those file names, so EC cannot tell them from any other file, and a later
conversion of the same folder will rewrite them like anything else. Keep exported
plans, journals, and reports outside the folder you scan.

Common metadata and build folders such as `.git`, `bin`, `obj`, and
`node_modules` are skipped. Hidden, system, and reparse-point files are left
alone, and hidden, system, and reparse-point folders are not entered.

EC reports how many files each exclusion skipped, counting only files your
patterns actually selected — so `-Include "*.bak"` reports that they were
skipped instead of returning nothing at all. These counts do not change the
exit code.

## Conversion

| Option | Meaning |
|---|---|
| `-Target <encoding>` | Target encoding, for example `utf-8` or `utf-8-bom`. Required for conversion. |
| `-From <encoding>` | Explicit original encoding for every selected file. Use when you know a legacy source encoding. |
| `-Backup` | Save every replaced original as `<file>.bak`. |
| `-WhatIf` | Show a one-time preview without writing files. |
| `-Plan <path>` | Write a reviewable conversion plan; do not modify files. |
| `-Apply <path>` | Execute a saved plan. Its scope and conversion settings are fixed. |

EC converts ASCII, Unicode with a BOM, and text whose encoding it can prove
from its bytes automatically. Legacy text and BOM-less Unicode whose encoding
cannot be proven safely need `-From`. Choosing a source encoding replaces
detection only; strict source decoding, strict target encoding, output
verification, backup checks, and atomic installation still apply.

For BOM-less UTF-16, EC converts automatically only when the bytes prove the
byte order. If both UTF-16LE and UTF-16BE strictly decode the complete file,
EC refuses the automatic conversion. Choose `-From utf-16le` or
`-From utf-16be` if you know the original order.

`-Apply` uses the decisions and hashes stored in the plan; it does not detect
the files again. Only `-Journal`, `-Quiet`, and `-MaxParallelism` may accompany
it. `-WhatIf`, `-Target`, `-From`, `-Backup`, and file-selection options are
rejected with `-Apply`.

## Read-only modes

| Option | Meaning |
|---|---|
| `-DetectOnly` | Report detected encodings; do not modify files. |
| `-Validate <charset1,...>` | Strictly validate complete files against an allowed encoding list; do not modify files. |
| `-FailOnChanges` | Return exit code 2 when files need conversion or fail validation. Useful in CI. |

`-DetectOnly` cannot be combined with `-Validate`, `-Target`, `-From`,
`-WhatIf`, `-Backup`, `-Plan`, `-Apply`, or `-Journal`. `-Validate` cannot be
combined with conversion options.

## Output and performance

| Option | Meaning |
|---|---|
| `-Report <path>` | Also write the CSV report as UTF-8 with a BOM for Excel. |
| `-Journal <path>` | Write a JSON record of the conversion decision and final result for every file. Convert mode only. |
| `-Quiet` | Print only the final summary on standard output. |
| `-Verbose` | Include error details and a result breakdown. |
| `-MaxParallelism <N>` | Maximum simultaneous files. Default: the smaller of CPU count and 4. |

`-Quiet` and `-Verbose` cannot be combined.

## Examples

Preview files without writing anything:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Include "*.cs,*.txt" `
  -Exclude "*.g.cs,*.designer.cs" -Target utf-8 -WhatIf
```

Prepare and later apply an approved batch:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Target utf-8 -Backup -Plan plan.json
EncodingChecker.exe -Apply plan.json -Journal conversion.json
```

Convert known Windows-1252 text while preserving originals:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Include "*.txt" `
  -From windows-1252 -Target utf-8 -Backup -Journal conversion.json
```

Validate a folder in CI:

```powershell
EncodingChecker.exe -BasePath "C:\Files" -Validate "utf-8,utf-8-bom" `
  -Report validation.csv -FailOnChanges -Quiet
```

## Version and exit codes

```powershell
EncodingChecker.exe --version
```

Prints the version and exits. It takes no other arguments and does not need
`-BasePath`.

| Code | Meaning |
|---:|---|
| 0 | Completed. |
| 1 | Invalid command. |
| 2 | `-FailOnChanges` found files requiring conversion or failing validation. |
| 3 | Processing, plan, or report failure. |
| 4 | Cancelled with Ctrl+C. |
| 5 | One or more conversions were safely refused and left unchanged. |

When more than one applies, processing failure (3) wins over safe refusal (5),
which wins over `-FailOnChanges` (2).
