# Encoding detection

EC identifies a likely source encoding before it converts a file. Detection is
helpful, but it cannot always recover the original historical codec from bytes
alone. EC therefore separates **what it detected** from **what it is safe to
convert automatically**.

## Detection order

1. Read at most 64 KiB from the start of the file.
2. Reject sufficiently large, high-entropy samples as likely binary data.
3. Check Unicode byte patterns for ASCII, UTF-8, UTF-16LE/BE, and UTF-32LE/BE,
   with or without a BOM.
4. If Unicode is not identified, ask UTF.Unknown for a legacy candidate.
5. Strictly validate that candidate and check that the decoded sample looks like text.

The shared detector remains aligned with LineEndingNormalizer and CorpusTesters.
EC's full-file conversion checks are separate: a detector label is never proof
that a destructive conversion should proceed.

## What automatic detection permits

- ASCII and UTF-8 may be converted automatically after strict validation.
- UTF-16/UTF-32 with a BOM may be converted automatically because the marker
  establishes byte order.
- BOM-less UTF-16 is converted automatically only when the complete file is
  invalid under the opposite byte order. Otherwise EC reports
  `AmbiguousBomlessUtf16` and leaves it unchanged.
- Detected legacy text requires an explicit source choice before EC rewrites it.
- Unknown input is skipped and left unchanged.

Use `-From <encoding>` or the GUI source selector when you know a legacy or
BOM-less UTF-16 source. This selects how EC reads the bytes; it does not turn
off strict decoding, output verification, backup verification, or safe install.

## Supported text

EC supports Unicode, ASCII, and the runtime-supported legacy charsets listed in
the [README](../README.md#supported-charsets). The source list is built from
`TextEncoding.SupportedEncodings`; unavailable .NET aliases are filtered before
the GUI offers them.

UTF-7 is intentionally unsupported. Current .NET versions disable its
encoder/decoder by default, and EC does not use a heuristic to guess UTF-7.

## What a detection label means

For a file containing only ASCII bytes, many encodings can produce the same
text. A label such as `utf-8` is a useful operational answer, not proof that the
author originally used UTF-8 rather than ASCII or a legacy codec.

The independent audit measures detector labels and conversion preservation as
separate properties. See [Safety audit](SAFETY-AUDIT.md) for the evidence and
its limits.
