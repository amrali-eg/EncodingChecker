# EncodingChecker v3.11.1

A readability fix for the three files EC writes to be read by a person, plus a
guard on the GUI smoke test.

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

## Compatibility

No conversion or classification behaviour changes. Conversion semantics stay at
6, the plan schema at 5, and the journal schema at 4. Exit codes are unchanged.
