# Release checklist

Automated coverage is the first gate and is enforced by CI. What follows is what CI
cannot answer.

## Automated

- [ ] `dotnet test sources/EncodingChecker.Tests/EncodingChecker.Tests.csproj -c Release` — all green.
- [ ] The 1,033-file oracle sentinel set still agrees with GNU libiconv and ICU.
- [ ] Detector-drift check passes (the detector sources are duplicated across three
      repositories and nothing enforces the sync; a fix in one is a fix owed to all three).

## Manual: the GUI smoke test

**Why this is manual.** EC's conversion policy, the plan binding, and the whole
orchestration sequence are automated. What is left is Windows Forms itself — designer
layout, background-worker marshalling, and the dialog's behaviour under a real message
pump. Automating those would mean weakening the architecture to make it drivable, which
would trade a real safety property for a test.

**Why it is not optional.** EC has already shipped a defect of exactly this shape: every
component was correct and tested while the GUI's *sequence* converted files the CLI
refuses. Nothing failed, because nothing ran the sequence. The orchestration is now
covered, so what remains is genuinely UI, but "genuinely UI" was also the last
description that turned out to be wrong.

### The evidence that counts

Status messages are not evidence. For every case below that must not modify a file,
record the file's SHA-256 before and after:

```powershell
Get-FileHash -Algorithm SHA256 <path> | Select-Object -ExpandProperty Hash
```

### Test files

| name | contents | encoding | expected classification |
|---|---|---|---|
| `jp.txt` | `こんにちは世界。日本語のテキストです。` | Shift_JIS | unambiguous |
| `french.txt` | `Le café était déjà prêt` | windows-1252 | text-changing |
| `russian.txt` | `Привет мир, это русский текст` | koi8-r | text-changing |
| `plain.txt` | `plain ascii, no high bytes at all` | ASCII | text-equivalent |

### Matrix

| # | step | expected |
|---|---|---|
| 1 | **View** the directory | 4 files listed with their encodings |
| 2 | Tick all, **Convert** to utf-8 | confirmation appears; two files listed as needing an explicit source encoding, with competing encodings named |
| 3 | **Cancel** | nothing converted; **all four hashes unchanged**; no `.bak` files |
| 4 | Convert again; untick `russian.txt`; choose `windows-1252` | button reads "Use this encoding for 1 file(s)" |
| 5 | Confirm the re-planned conversion | `french.txt` converts and reads correctly as French |
| 6 | Check `russian.txt` | **hash unchanged**; still refused |
| 7 | Convert again; while the dialog is open, edit one selected file in another editor and save | — |
| 8 | Confirm | run stops; message names the changed file; **every hash unchanged** |
| 9 | Convert `jp.txt` alone, backups on | converts; `jp.txt.bak` and `jp.txt.ecmeta.json` present; text reads correctly |
| 10 | Create a **directory** named `<file>.bak` beside a file, convert it | conversion refused; **source hash unchanged** |
| 11 | Export report → **Conversion journal (\*.json)** | journal written; refused files present with their competing encodings; `Sha256After` null for everything not converted |

### Record

Fill this in and keep it with the release. It is the auditable answer to the one question
the test suite cannot reach.

```text
EC version:
Commit:
Windows version:
.NET version:
Date:
Tester:

Test files and their SHA-256 before:

Step-by-step observations (expected vs observed):

SHA-256 after, per file:

Cases where observed differed from expected:

Result: PASS / FAIL
```

## Documentation

- [ ] README figures match the current audit run; no stale counts.
- [ ] Version bumped in `Program.cs` usage text and the README heading.
- [ ] `SemanticsVersion` bumped **only** if conversion or classification behaviour
      changed — it invalidates existing plans, and bumping it for a release that changed
      neither teaches people to work around the check.
