# EncodingChecker v3.8.0

## Safer legacy conversion

- Unicode and ASCII files continue to convert automatically.
- Detected legacy text is now left unchanged until its original source encoding is
  explicitly chosen in the GUI or supplied with `-From` on the command line.
- An explicit source encoding still uses strict decoding, verified output, backup
  checks, and atomic replacement; it is not a safety bypass.

## Clearer review and export

- The conversion review states which files are ready, which need a source encoding,
  and which EC will leave unchanged.
- Legacy source choices apply only to the ticked files and show their scope clearly.
- **Export results** now offers a CSV report and, after conversion, a JSON conversion
  history.

## Reliability and maintainability

- Saved plans bind approved file hashes and conversion semantics before `-Apply`.
- Backup sidecars preserve the source codec and conversion provenance.
- The shared Unicode detector is checked for parity across EncodingChecker,
  LineEndingNormalizer, and CorpusTesters.
