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

### Structure it in phases, not one long sequence

The first version of this test used one folder and one final check for the whole matrix.
That cannot work, and the reason is worth keeping: the stale-plan case **stops the entire
run**, so every "must have converted" expectation after it is unreachable by construction.
Worse, the state it leaves is byte-identical to "the tester cancelled everything", so the
result cannot say *which* protection fired. The first real run produced a FAIL that was
entirely the instrument's fault, and only inspecting the bytes by hand showed the product
had behaved correctly.

Each phase therefore gets its own folder, its own short click sequence, and its own check,
and proves exactly one property. [`tools/gui-smoke-test.py`](tools/gui-smoke-test.py) does the setup and the
verification; it also refuses to pass a phase whose defining action was skipped — a phase
that silently tests nothing is the failure mode a manual matrix is most prone to.

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

Phase A (refuse + cancel change nothing):   PASS / FAIL
Phase B (explicit source, scoped):          PASS / FAIL
Phase C (stale plan stops the whole run):   PASS / FAIL
Phase D (backup + record; backup failure):  PASS / FAIL

Cases where observed differed from expected:

Result: PASS / FAIL
```

### Run of 2026-08-27

```text
EC version:      3.7.0.0
Commit:          a201a08
Windows version: Microsoft Windows NT 10.0.26200.0
.NET version:    10.0.400
Date:            2026-08-27
Tester:          amrali-eg

Phase A (refuse + cancel change nothing):   PASS
Phase B (explicit source, scoped):          PASS
Phase C (stale plan stops the whole run):   PASS
Phase D (backup + record; backup failure):  PASS

Result: PASS
```

Notes from that run, kept because they qualify what the phases actually establish:

- **Phase B proves less on its own than it appears to.** Its French sample decodes
  identically under windows-1252 and iso-8859-1, so "the text is preserved" cannot show
  which codec was used. What settles it is the recovery record: `french.txt.ecmeta.json`
  gives `DetectedCodePage: 1252`, the encoding chosen in the dialog rather than the
  `iso-8859-1` that detection proposed. A future revision should use content where the two
  encodings genuinely disagree, so the assertion stands without the sidecar.
- **Text-equivalent ambiguity is nearly unreachable.** Eight ASCII shapes — short strings,
  digits, JSON, code, newlines — all classify as `StructurallyDetermined`, because ASCII
  constrains every byte below 0x80. Only a **one-byte file** reaches `TextEquivalent`,
  where no codec that decodes it at all can read it differently. The middle class of the
  three-way taxonomy is far rarer in practice than the taxonomy suggests. The classifier
  is right in both cases; the corpus has to be contrived to exercise it, and `tiny.txt`
  exists for that reason alone.

## Documentation

- [ ] README figures match the current audit run; no stale counts.
- [ ] Version bumped in `Program.cs` usage text and the README heading.
- [ ] `SemanticsVersion` bumped **only** if conversion or classification behaviour
      changed — it invalidates existing plans, and bumping it for a release that changed
      neither teaches people to work around the check.
