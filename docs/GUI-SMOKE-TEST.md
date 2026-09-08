# The GUI smoke test

Ten phases that drive a built `EncodingChecker.exe` through Windows UI Automation
and check the bytes it leaves behind. Every phase creates its own disposable folder,
performs a real sequence in the real window, and then verifies files. Where a phase does
read what the window says, it checks that wording against the bytes on disk rather than
taking it on trust.

```powershell
dotnet build sources/EncodingChecker.sln -c Release
sources/EncodingChecker.GuiSmoke/bin/Release/net10.0-windows/EncodingChecker.GuiSmoke.exe
```

```
--app <path>          the executable to drive; defaults to the Release build
--output <folder>     where evidence is written; must be empty or new
--phase <A-J>         run one phase
--keep-workspace      keep the fixtures even when the run passes
```

Exit `0` when every phase passes, `1` when one fails, `2` for a usage, environment, or
build-compatibility problem. Each run writes `gui-smoke-report.json` and
`gui-smoke-report.md`, carrying the EC version, the executable SHA-256, the OS and .NET
versions, and each phase's before and after file hashes. It also hashes the managed
assembly beside the executable when there is one, as in an ordinary Release build. A
single-file publish has none, and the report says so instead of naming a file it could
not read.

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
| **B** | Unicode and ASCII are handled with no source choice offered, and both keep their text exactly. The Unicode file's recovery record names `Detected` and the right code page. | A safe batch demanding a source choice, or a conversion that alters text. |
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
against real progress rather than a sleep, so it does not depend on machine speed, and a
run short enough to finish first is not a failure — the same assertions hold.

## Checking that a phase can fail

A phase that has never failed has not been shown to test anything. Each was verified by
reintroducing the defect it guards, confirming it fails, and restoring the file
byte-identically. For example, restoring the review's advisory filter to the pre-v3.11.0
state fails **H**; letting an interrupted run take the nothing-was-modified path again
fails **I**.

Do this for any phase you add. A green suite is evidence only to the extent its phases
could have gone red.

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

A preflight check enforces this. It opens one review, looks for the five ids, and if
none are present refuses with exit `2` and says so.

This exists because the failure was worse than useless without it. Pointed at v3.11.0
the suite ran every phase and reported, first line, that *the mixed review did not offer
a source-encoding choice* — which reads as a conversion-safety regression in EC. The
control was there; the suite could not see it. A harness that cannot tell "the control
is absent" from "the behaviour is absent" reports the wrong defect, in the alarming
direction, about the wrong component.

## Requirements

An interactive Windows desktop - a real logged-in session with a screen. The automation
layer cannot click a window that nobody is looking at, so with no desktop the runner
refuses to start with exit `2` rather than reporting a pass it did not earn.

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

The report is uploaded as a `gui-smoke-evidence` workflow artifact, which expires on
GitHub's retention schedule. It is not attached to the release, so it is not permanent
evidence unless someone attaches it.

**It also gates every pull request.** `ci.yml` runs the same ten phases against an
ordinary Release build, as its own `gui-smoke` check, so a GUI regression is found on
the pull request that introduced it rather than at tag time with a release waiting.
That costs about two minutes of runner time per pull request, which is the price of
not diagnosing this class of defect mid-release.

The two runs answer different questions and both are kept. The pull-request run asks
whether this change broke the window; the release run asks whether the artifact about
to be published works. Only the release job can ask the second one, because only it
produces a signed, single-file executable.

Evidence is uploaded from the pull-request run whether it passes or fails. A passing
run is the sample that shows an intermittent phase has stopped being intermittent —
which is the open question EC-24 records, since the failure that prompted the fix was
never reproduced.
