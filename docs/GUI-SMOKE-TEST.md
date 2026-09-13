# The GUI smoke test

Ten phases that drive a built `EncodingChecker.exe` through Windows UI Automation
and check the bytes it leaves behind. Every phase creates its own disposable folder,
performs a real sequence in the real window, and then verifies files. Where a phase does
read what the window says, it checks that wording against the bytes on disk rather than
taking it on trust.

```powershell
dotnet build sources/EncodingChecker.sln -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
sources/EncodingChecker.GuiSmoke/bin/Release/net10.0-windows/EncodingChecker.GuiSmoke.exe
```

```
--app <path>          the executable to drive; defaults to the Release build
--output <folder>     where evidence is written; must be empty or new
--phase <A-J>         run one phase
--keep-workspace      keep the fixtures even when the run passes
```

Exit `0` means every selected phase passed. Exit `1` means a check, the test tool,
cleanup, or evidence collection failed; it does not by itself identify an EC defect.
Exit `2` means the run was inconclusive or could not start because of its arguments,
environment, or an incompatible build.

Once the suite starts, it records all three outcomes and attempts both
`gui-smoke-report.json` and `gui-smoke-report.md`. Completed results survive a later
failure. A known startup refusal, such as a missing executable or noninteractive desktop,
also writes an inconclusive preflight report when its output folder can be created. A disk
or permission error can still prevent writing one or both formats and exits `1`. Each file
is installed only after it has been fully written; the two files are not one transaction.

The reports include preflight, phase results, file hashes, cleanup errors, structured
window observations, timings, and Phase I's selected, converted, and not-attempted counts.
Build hashes and version are read **before** any GUI
work, so a later problem reading the executable cannot erase completed results. JSON
readers should use `Outcome`; the older `Passed` field is retained for compatibility
and cannot distinguish failure from an inconclusive run.

## Why this is not an ordinary test

Conversion policy, plan binding and orchestration are covered by the unit suite, which
needs no window. What is left is the window itself: whether the controls land where the
designer put them, whether progress from a background thread reaches the screen safely,
and whether the review dialog behaves when a person is actually clicking it.

The obvious way to automate that — hosting the forms inside a test process — would mean
reshaping the application to be drivable, trading a safety property for a test. This
does not. It launches the shipped executable and drives it through the accessibility
layer, the same surface a screen reader uses. The only production concession is a
`Name` on five controls, and a unit test pins those identifiers so a rename cannot
silently break the driver.

**EC has already shipped a defect of exactly this shape.** Every component was correct
and tested while the GUI's *sequence* converted files the CLI refuses. Nothing failed,
because nothing ran the sequence.

## The phases

| Phase | Proves | Would have caught |
|---|---|---|
| **A** | Opening the review and cancelling writes nothing, with backups enabled: no bytes change, no `.bak`, no `.ecmeta.json`. | A review that writes before you confirm. |
| **B** | Unicode and ASCII convert with no source choice offered, both keep their text exactly, and **both** get a verified recovery record naming `Detected` and the right code page — ASCII included, whose bytes do not change. | A safe batch demanding a source choice, a conversion that alters text, or a conversion that skips the restore point when the bytes happen to match. |
| **C** | A chosen legacy source applies **only** to the ticked files; an unticked file keeps its bytes and gets no backup. The record names `Explicit` and the chosen code page. | A source choice leaking to files it was not ticked for. |
| **D** | BOM-less UTF-16 whose byte order cannot be proven is refused, is offered a source choice, and cancelling leaves the folder untouched. | Automatic conversion of a file whose byte order is a coin flip. |
| **E** | Naming `utf-16BE` explicitly converts the same file exactly, with a backup and a recovery record. | A refusal that cannot be answered, or an answered one that loses text. |
| **F** | A source that changes after the review opens stops the **whole** run — the changed file and its unchanged neighbour both keep their bytes. | Applying a review to bytes nobody reviewed, or half-applying it. |
| **G** | When a backup cannot be created the source is untouched and no recovery record is written. | Converting without the restore point the run promised. |
| **H** | A source choice that **agrees** with an unprovable byte order is still flagged, saying the order was taken on trust and *not* that it differs from your choice. | A warning that fires for the safer choice and stays silent for the riskier one. |
| **I** | Cancelling a 400-file run mid-write reports what it actually wrote: the converted count equals the files whose BOM is gone, and unreached files are reported as not attempted. | A cancelled run claiming it changed nothing, or claiming the whole batch converted. |
| **J** | After View, changing the directory without rescanning leaves out-of-directory results visible. Confirming a source encoding keeps the review open, explains why it cannot apply the choice, and changes no bytes. | A ticked source choice being discarded as an unexplained cancellation, or the automation driver failing to reach that path. |

### Notes on two of them

**H** exists because the case it covers shipped broken for a whole release. v3.10.1
recorded a source choice matching an unprovable estimate in the reason codes, and the
review dialog never displayed it — so the warning appeared for the choice that
contradicted EC and stayed silent for the choice that repeated its guess. Every unit
test passed throughout, because the filter that dropped it is not the logic they cover.
The phase asserts on rendered text, not on a control's presence: what matters is the
wording a reader sees.

**I** never takes the status line at its word. It counts the files whose byte-order mark
is actually gone and requires the reported figure to match. Cancellation is timed
against real progress rather than a sleep. The 400-file workload must leave both
converted and untouched files; if it finishes first, the phase fails because it did not
exercise cancellation.

## Checking that a phase can fail

A phase that has never failed has not been shown to test anything. Each was verified by
reintroducing the defect it guards, confirming it fails, and restoring the file
byte-identically. For example, restoring the review's advisory filter to the pre-v3.11.0
state fails **H**; letting an interrupted run take the nothing-was-modified path again
fails **I**.

Do this for any phase you add. A green suite is evidence only to the extent its phases
could have gone red.

The gate itself also has paired controls. A reachable window that never produces an
expected result must be `FAIL` with exit `1`. A live EC window that Windows confirms is
on another virtual desktop, with no process window visible through UI Automation, is
`INCONCLUSIVE` with exit `2`. Missing or unreadable automation data alone is not enough:
a hung application can look the same. Unknown observations retain the failure and its
diagnostic rather than inventing an environmental explanation.

Automated tests run the actual phase-recording loop without a GUI. They cover both
outcomes, earlier results surviving a later problem, preflight refusal, cleanup failing
during another exception, and report-writing errors. Real GUI controls remain necessary
for the Windows-specific parts; unit tests do not replace them.

## What it does not cover

- **Display scaling, keyboard-only operation, and high contrast.** Still manual; see the
  accessibility spot check in [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md).
- **Window-position restore across a monitor change.** Unit-tested against synthetic
  layouts only, because it needs a real display change.
- **The journal export dialog.** Phase I checks the status line and the bytes; the
  exported file's contents are reconciled by `InterruptedRunJournalTests` instead.

## Which builds it can drive

The driver finds the review dialog's controls by automation id, and those ids were
added by the same change that added this suite. **No release up to and including
v3.11.0 carries them**, so none of those can be driven by it. **v3.11.1 is the first
release the suite can run against.**

A preflight check enforces this. It opens one review through the same main-window child
lookup used by every phase, then looks for the five ids. This also pins the review's
owned-window relationship that keeps discovery independent of unrelated desktop windows.
If the ids are absent, the run is `INCONCLUSIVE`, exits `2`, and writes the reason.

This exists because the failure was worse than useless without it. Pointed at v3.11.0
the suite ran every phase and reported, first line, that *the mixed review did not offer
a source-encoding choice* — which reads as a conversion-safety regression in EC. The
control was there; the suite could not see it. A harness that cannot tell "the control
is absent" from "the behaviour is absent" reports the wrong defect, in the alarming
direction, about the wrong component.

## Requirements

An active, unlocked Windows desktop - a real logged-in session with a screen, with EC on
that desktop. A confirmed move to another desktop makes the phase `INCONCLUSIVE`.
Other losses of access may remain `FAIL` with an unknown cause; the tool cannot reliably
identify every desktop, provider, or application fault.

For a confirmed desktop move, cleanup first asks EC to close normally. If it does not
close, the driver attempts to end its test process. Cleanup errors are recorded alongside
the original problem and stop later phases, so a leftover process cannot quietly affect
the next test. A final filesystem snapshot is attempted after cleanup, including on
failure; a snapshot error is recorded rather than replaced with invented hashes.

A GitHub-hosted `windows-latest` runner **does** provide one. Measured, not assumed:
`Environment.UserInteractive` is `True` under the `runneradmin` account, and phase A
opened the review, cancelled it, and verified the bytes on disk.

The exit code alone would not have shown that, because a green tick says only that
nothing failed. The evidence the run uploaded is what settles it: one phase recorded,
`A`, with five files hashed before and five after. Read the artifact, not the tick.

## Where it runs

**It gates the release.** `release.yml` runs all ten phases against the published
framework-dependent executable, `publish\win-x64\EncodingChecker.exe`, so what is
verified is a file that ships rather than a rebuild of the same commit. A failure fails
the job and no release is created.

Two limits are worth knowing rather than assuming. The run happens **after** the signing
step, so it drives the signed bytes when signing runs - but signing is conditional on the
certificate secrets being configured, and when they are not it is skipped and the suite
drives an unsigned file. And the **self-contained** executable under
`win-x64-selfcontained` is packaged and shipped without being driven at all; only the
framework-dependent one is.

The report records each phase's duration and the total duration, so a cleanup cannot
quietly make the gate much slower. Phase I also preserves its cancellation margin in the
evidence rather than only in the CI log. The review lookup was measured before it was scoped to
EC's main window: desktop clutter moved a run from about 19 to 22.5 seconds. Afterward,
the same runs were about 17.3–17.8 seconds on either desktop.

The report is uploaded as a `gui-smoke-evidence` workflow artifact, which expires on
GitHub's retention schedule. It is not attached to the release, so it is not permanent
evidence unless someone attaches it.

**It also gates every pull request.** `ci.yml` runs the same ten phases against an
ordinary Release build, as its own `gui-smoke` check, so a GUI regression is found on
the pull request that introduced it rather than at tag time with a release waiting.
That costs about two minutes of runner time per pull request, which is the price of
not diagnosing this class of defect mid-release.

Both workflows use `Write-GateSummary.ps1` to explain the outcome in the job summary.
A failed check and an inconclusive run **both block approval**, but have different
messages. A missing report or a mismatch between the report and exit code also blocks
approval. Making an inconclusive run green would remove the test gate, not fix it.

The two runs answer different questions, and both attempt to upload a report. The pull-request run
asks whether this change broke the window; the release run asks whether the artifact
about to be published works. Only the release job can ask the second one, because only it
produces a published single-file executable.

Evidence is uploaded from the pull-request run whether it passes, fails, or is
inconclusive. A passing report is still useful regression evidence, but it does not by
itself exercise a check that runs only on failure; that requires the paired controls
described above.
