# EncodingChecker v3.11.1

A readability fix for the three files EC writes to be read by a person, a guard on
the GUI smoke test, and corrections to what the About box claims.

## Recovery files keep their names

The conversion journal, the saved plan, and the recovery sidecar all used the
default JSON encoder, which escapes every non-ASCII character. A recovery
record for a Japanese or Arabic filename spelled it as a run of `\uXXXX`, and an
ordinary apostrophe came out as `\u0027`.

The text those files could not spell is exactly the text EC exists to convert,
and they are what you read when a run has gone wrong. They now write the
characters themselves:

```json
"RelativePath": "it's-a-file.txt",
"RelativePath": "ملف-عربي.txt",
"RelativePath": "日本語のファイル.txt",
```

Nothing about the format changes. The JSON was always valid and always
round-tripped, which is why no test caught it; the schema versions are
unchanged and files written by v3.11.0 are still read normally. Backslashes in
Windows paths are still escaped, because JSON requires that.

## The GUI smoke test refuses a build it cannot drive

The suite drives the review dialog by automation id. Pointed at a build without
those ids it ran every phase anyway and reported, first line, that the review
had offered no source-encoding choice — a claim about conversion safety, when
the real cause was a control it could not see.

It now checks once, up front, and exits 2 saying so. **v3.11.1 is the first
release the suite can run against**; no earlier one carries the ids.

## About box and attribution

Four statements in the About box or the assembly were wrong or out of date.

- **The licence.** It said Mozilla Public License **1.1**. The project ships
  **2.0** — that is what `LICENSE` contains, what the README links, and what the
  label's own click target already opened. Only the words a reader saw were wrong.
- **The detector credit.** It named `ude` while linking to UtfUnknown, the
  library actually in use. It now names UtfUnknown.
- **The CodePlex link.** It pointed at `encodingchecker.codeplex.com`, which no
  longer resolves — CodePlex shut down. It now points at the archive, as the
  README does.
- **Attribution.** `AssemblyCompany` names the current maintainer, and the About
  box records both the original author and the maintainer. The copyright notice
  adds the maintainer beside the original author rather than replacing him; MPL
  2.0 section 3.4 forbids removing a copyright notice from covered source, and
  permits altering a notice only to remedy a factual inaccuracy, which is what
  the licence-version fix above is.

`THIRD-PARTY-NOTICES.txt` still says MPL 1.1 and is correct: that describes
UtfUnknown's own licence, not this project's.

## Compatibility

No conversion or classification behaviour changes. Conversion semantics stay at
6, the plan schema at 5, and the journal schema at 4. Exit codes are unchanged.
