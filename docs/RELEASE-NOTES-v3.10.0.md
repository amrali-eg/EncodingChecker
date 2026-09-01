# EncodingChecker v3.10.0

## Safety change

- **Ambiguous BOM-less UTF-16 is now refused automatically.** A BOM-less UTF-16 file can
  be valid as both UTF-16LE and UTF-16BE. Detection may prefer one order, but that is not
  enough evidence to rewrite the file. EC now strictly decodes the complete source with the
  opposite byte order. If that also works, EC reports `Refused` with reason code
  `AmbiguousBomlessUtf16` and does not create a backup, sidecar, temporary output, or
  replacement file.

- **Expect most ordinary BOM-less UTF-16 text to be refused.** Byte-swapped Latin text often
  lands in a valid CJK range, so it commonly succeeds in both byte orders. Add the correct
  BOM, or choose the known source order explicitly with `-From utf-16le` or `-From utf-16be`,
  or with the GUI's source-encoding chooser. An explicit source selection still uses strict
  decoding, strict output encoding, exact text verification, backup verification, and atomic
  installation.

## Plan compatibility

Conversion semantics are now version 5. Plans made by earlier releases are rejected and must
be regenerated, because a formerly automatic BOM-less UTF-16 conversion can now be refused.

## Documentation

The README, conversion workflow, and safety audit now describe this rule using the same
evidence standard as LineEndingNormalizer: automatic rewriting requires bytes that prove the
UTF-16 byte order.
