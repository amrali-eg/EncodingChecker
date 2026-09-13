# GUI smoke test: future work

The GUI smoke suite became substantially more reliable in PRs #95–#102. Keep
the narrow review-dialog lookup introduced by #102; it avoids repeatedly
searching unrelated desktop windows and has measured performance benefits.

## EC-28: contained, not fully explained

Moving EC to another virtual desktop reproduces the symptom where EC completes
but UI Automation can no longer inspect its controls. The suite now refuses that
run instead of falsely reporting an EncodingChecker failure.

The original unattended occurrence was not proven to have the same cause. Do not
describe EC-28 as fully root-caused. Prefer: **contained; original trigger
unknown**.

## Implemented in the current branch

These changes are in proposed PR #103, **Harden GUI smoke-test evidence**.

1. Window observations keep found, cleanly absent, lookup-failed, and unknown
   states separate. Only Windows confirming that EC's live window is on another
   virtual desktop makes a run inconclusive.
2. An inconclusive phase preserves completed phases, its own final snapshot,
   timeout, observation, and build identity in both JSON and Markdown. Known
   startup refusals also write an inconclusive preflight report when the output
   folder is available. Any snapshot or cleanup failure is a failed run.
3. Cancellation decisions are separated from UI Automation polling and covered
   with table-driven tests for a successful stop, refusal, uncertain press, no
   button, completed run, failed run, and timeout.
4. Phase I parses the complete stopped-conversion summary. Numeric substrings
   cannot make `16` match `116`.
5. Phase I saves selected, converted, and not-attempted counts as phase metrics
   in both evidence formats.
6. Dialog closure and replacement use the full automation identity, not only a
   native window handle, so a reused same-process handle cannot look like the
   old dialog.

## Phase I limitation

Phase I proves that a real GUI conversion can be interrupted, but its timing
still depends on the machine and UI Automation. It now uses 400 files. In the
latest full GUI run, cancellation stopped after 96 files and left 304 untouched.
That one run is evidence that the workload remains sufficient, not a promised
margin for every machine. The cancellation margin is saved in the evidence. If
practical, use a deterministic progress signal or file-change notification
rather than increasing the file count indefinitely.

## Deferred decisions

- Do not redesign the reachability classifier merely to group its observation
  fields; the current named outcomes are clear and tested.
- Do not widen the review-dialog lookup. It remains scoped to EC's main window
  for both correctness and measured performance.
- Consider a deterministic Phase I progress signal only with a production-side
  design that does not reshape EncodingChecker solely for the smoke test.

No production EncodingChecker conversion code changed in PRs #95–#102; this work
is about ensuring the release gate reports the truth.
