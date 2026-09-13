# EncodingChecker project instructions

EC is a Windows Forms and CLI application that converts text files in place.
Preserving file contents and reporting outcomes truthfully come before convenience.
These instructions supplement personal preferences in the user's global AGENTS.md.

## Where to start

- Application: `sources/EncodingChecker/`.
- Unit and integration tests: `sources/EncodingChecker.Tests/`.
- Real-window automation: `sources/EncodingChecker.GuiSmoke/`.
- Read `docs/CONVERSION-WORKFLOW.md` and `docs/SAFETY.md` before changing conversion.
- Read `docs/GUI-SMOKE-TEST.md` before changing the smoke driver.
- Use `docs/CLI.md` for switches and exit codes; check claims against current code.
- Keep the README introductory. Put technical detail in the focused `docs/` files.

## Safety rules

- Keep `ConversionPolicy` as the conversion-decision authority. GUI, CLI and saved
  plans must not introduce their own competing safety rules.
- Explicit source selection (`-From` or GUI choice) chooses how bytes are read;
  it must not bypass strict decoding, encoding, verification or backup checks.
- Preserve exact Unicode text. Do not normalize Unicode, whitespace, case or line
  endings during conversion, or silently substitute replacement characters.
- A confirmed plan must execute its recorded decisions, not silently detect again.
  Preserve file-hash binding and stale-plan refusal.
- Keep detection, user choice, planned action and actual outcome distinct in reports.
  An unreached file is not a completed conversion or a verified unchanged file.
- Backups can remain after a failed conversion. Do not promise otherwise.
  Recovery metadata is independently verifiable; EC has no built-in restore command.
- Do not add full-file buffering, speculative confidence rules or batch-transaction
  machinery as incidental cleanup. Keep fixes proportional to demonstrated problems.

## Shared detector boundary

`UnicodeDetector.cs` and `TextValidation.cs` are compared across EC,
LineEndingNormalizer and CorpusTesters by `.github/workflows/detector-parity.yml`.
Do not modify them without explicit approval to change the shared detector.
`TextEncoding.cs` is an application wrapper and need not be identical across repos.
Do not weaken the parity check to accommodate an accidental difference, update
`UTF.Unknown` without corpus regression checks, or add UTF-7 detection unasked.
Before completing an approved shared-detector change, compare the corresponding
files in LEN and CorpusTesters. Identify any coordinated updates needed; do not
modify sibling repositories without authorization.

## Build and test

Use Windows, the .NET 10 SDK and PowerShell 7 (`pwsh`) for the gate-script tests.
Commands below run from the **repository root**, not `sources`.

```powershell
dotnet build sources/EncodingChecker.sln --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed; do not run old binaries.' }
dotnet test sources/EncodingChecker.sln --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
```

- During development, run affected tests first. Run the full suite before handoff
  for code changes; documentation-only edits do not need an application rebuild.
- `ScanDirectory` and `ConvertFiles` callbacks are concurrent. Tests should collect
  them with `EntrySink` and assert expected count/membership before behavior.
- Test actual orchestration paths where practical, not just helpers populated with
  the expected answer. For refusal/cancellation, check original file bytes too.
- Never modify source corpora. Destructive tests use disposable working copies.
- When changing failure handling, test that failure path directly. Passing
  normal runs does not verify it.
- After changing the backlog, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/Test-DefectBacklog.ps1
```

The checker validates counts and links, not whether a finding deserves to be closed.

## GUI smoke and release evidence

The smoke suite opens real windows. Coordinate its use with the user and keep the
windows on the active interactive desktop. After a successful Release build:

```powershell
sources/EncodingChecker.GuiSmoke/bin/Release/net10.0-windows/EncodingChecker.GuiSmoke.exe
```

- Exit 0 is a pass; 1 is failure; 2 is inconclusive or a startup refusal. Neither
  nonzero exit clears the gate. A driver failure does not automatically blame EC.
- Keep reports and before/after hashes. Record retries and unexplained failures;
  later green runs do not erase them. Check report `Outcome`, not only `Passed`.
- The cancellation count depends on conversion speed, automation latency and
  machine load. Do not attribute a change to one cause without a controlled
  comparison.
- Do not reshape production UI solely to make automation easier.
- Before a release, follow `docs/RELEASE-CHECKLIST.md`. Required PR checks are
  `build`, `gui-smoke` and `parity`; verify them against the exact PR head.
- Detection or conversion-policy changes require the four-corpus audit for release.
  Distinguish assembly, executable and archive hashes; do not carry old evidence
  forward as if it measured a new build.

## File conventions

- Follow `.editorconfig` and `.gitattributes`: C# is UTF-8 without BOM, CRLF;
  PowerShell scripts are UTF-8 with BOM, CRLF; shell scripts use LF.
- Preserve existing conventions in other files. Check touched files for mixed line
  endings; never run repository-wide encoding or formatting cleanup incidentally.
  When checking file formatting, inspect raw bytes before normalization;
  normalized comparisons can hide mixed line endings or BOM differences.
  Normalization remains appropriate for the detector-parity comparison.
- Keep comments concise and explain why. Tests may need fuller explanations of the
  failure they prevent. Write backlog entries for a human reader, with evidence and
  limits, rather than repeating internal type names.
  Explain current intent rather than recounting development history. Test
  comments may briefly describe the regression they prevent.
- Do not hardcode current versions, test totals or backlog counts here. Use their
  existing sources of truth and update versioned artifact contracts deliberately.
