# EncodingChecker v3.10.1

A fix to the BOM-less UTF-16 safety introduced in v3.10.0. That release refuses
files whose byte order cannot be established from their bytes, then lets you
supply one. It reported your choice only when the choice contradicted EC's own
estimate — so it spoke up when you were right and said nothing when you were
wrong.

## The defect

On a UTF-16BE file that detection reads as little-endian:

```text
-From utf-16le   Converted, no reason code, exit 0
                 the text became "A" and "B" repeated

-From utf-16be   Converted, with ExplicitSourceDiffersFromBomlessUnicodeEstimate
```

The destroyed file was the silent one, and its report row was indistinguishable
from an ordinary conversion.

EC held the information that would have warned you: it had already refused the
file as unprovable. One variable carried both that fact and the separate decision
to refuse automatically, so supplying a source erased the fact along with the
decision.

## The fix

An explicit source given for a file whose byte order could not be established is
now always reported, whether or not it matches EC's estimate. Agreeing with an
estimate EC cannot prove is not corroboration.

The two situations keep separate reason codes, because a script told `differs`
when the caller agreed would be told something untrue:

| Reason code | Meaning |
|---|---|
| `ExplicitSourceOnUnprovableBomlessUnicode` | **New.** Your choice matched EC's estimate, but the byte order was never established; it was taken on trust. |
| `ExplicitSourceDiffersFromBomlessUnicodeEstimate` | Your choice contradicted EC's estimate. Unchanged. |

The new code is additive: a run that previously produced an empty reason field
may now produce a value. Scripts matching specific codes are unaffected; a script
asserting the field is empty will see the change.

## Unchanged

Refusal without an explicit source, structurally provable byte orders, files
carrying a byte-order mark, and non-UTF-16 sources all behave exactly as in
v3.10.0. Detection, strict decoding, output verification, backup verification and
atomic installation are untouched.

## Why v3.10.0's checks did not catch this

The full suite was green, the manual GUI smoke test passed, and the four-corpus
audit reported one improved file and no regressions. None of them reached this
case, which is *the caller supplies the same answer the detector guessed*.

The audit could not reach it: it supplies the source codec from each corpus's own
reference metadata, and those files are not ambiguous, so the fixed path never
executes. Its result for this release is therefore evidence about the conversion
engine and says nothing about this fix either way.
