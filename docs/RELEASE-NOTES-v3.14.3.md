# EncodingChecker v3.14.3

Zero product changes. This release exists because the internal code that implements
v3.14.2's behavior is smaller and clearer than it was; nothing about what EC detects,
converts, refuses, or reports has moved.

## What changed, and why you will not notice it

- Several nested conditionals (the CLI's exit-code and mode selection, the journal's
  status computation, the GUI smoke suite's reachability and cancellation
  classification) were rewritten as plain `if`/`else` chains or `switch` expressions.
  Every branch and its result is unchanged; only how the choice is written changed.
- Duplicate helper methods were collapsed to one implementation each: two byte-identical
  `ResolveCodePage` methods, two `HasPreamble` overloads, and several GUI-smoke
  hash/snapshot helpers. Code-page resolution now has two clearly named entry points
  (`ResolveCodePageOrNull` and `ResolveCodePageOrZero`) instead of four near-duplicate
  spellings, kept separate on purpose: they serve different persisted fields with
  different "unresolved" conventions, and merging them would have changed which value
  a stale plan or journal entry records.
- `-Apply`'s twelve-option conflict check was rewritten as a plain sequence of `if`
  statements instead of a nested ternary chain, with 25 new regression tests pinning
  the exact precedence when several conflicting options are given at once — a gap the
  simplification review found in existing coverage, not a behavior change.
- The GUI's two exception-handling export dialogs (text and CSV) now share one small
  helper; the JSON journal export was deliberately left separate because its failure
  reporting uses a different, incompatible convention (a returned error string rather
  than a thrown exception).
- About eighteen near-identical test helpers that redirect console output around a CLI
  call were consolidated into one shared test utility, and several duplicated test
  fixtures were merged. These changes are internal to the test project and affect
  nothing that ships.
- Two diagnostic strings inside the GUI smoke suite's own Phase J were reworded so a
  failure report can tell which of two checks actually failed. This is test tooling
  read only by whoever investigates a release-gate failure; `EncodingChecker.exe`
  prints neither string.

Every change was verified individually: a Release build with no warnings and the full
test suite after each step, and the code-simplifier tool's review of each diff before
it was kept. Several proposed simplifications were considered and deliberately left
alone because they could not be proven behavior-preserving with confidence — among
them, a 20-field manual equality check used for recovery verification, and a rollback
path whose broader exception handling turned out to be intentional, not an oversight.

## Compatibility

Nothing changes. Conversion semantics stay at **7**, the plan schema at **6**, the
journal schema at **6**. Every exit code, reason code, report column, and journal
field is exactly what it was in v3.14.2. Plans and journals from any 3.14.x release
still apply unchanged.

## Verification

- 851 tests pass, none skipped (was 826); release build with no warnings
- The GUI smoke suite passes all ten phases against an ordinary Release build
- `docs/Test-DefectBacklog.ps1` passes with unchanged counts
- **No four-corpus audit was run.** This release changes no detection or conversion
  policy, so the checklist does not require one
