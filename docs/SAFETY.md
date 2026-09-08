# Safety and recovery

EC converts text encodings in place only after it can verify the conversion.
It does not normalize text, alter case, change whitespace, or use replacement
characters to make a conversion appear successful.

## Conversion sequence

For each file EC is allowed to convert:

1. Scans the source and decides whether automatic conversion is permitted.
2. For a reviewed plan, verifies that every approved source hash still matches.
3. Rejects malformed input, unsafe source choices, repeated leading BOMs, and
   ambiguous BOM-less UTF-16 and all BOM-less UTF-32 before writing output.
4. If backups are enabled, creates `<file>.bak` before conversion begins.
5. Strictly decodes the source and strictly encodes the target into a sibling
   temporary file.
6. Strictly decodes that output and compares the exact Unicode scalar sequence
   with the source text.
7. If backups are enabled, verifies the backup hash and writes a `Prepared`
   `.ecmeta.json` recovery record.
8. Atomically installs the verified temporary output where the platform supports it.
9. Marks the recovery record `Completed` after installation succeeds.

If a required step fails, EC does not install converted output. Because the
backup is created before decoding and encoding, a later conversion failure can
leave a valid `.bak` beside an unchanged source; metadata is not written until
both the output and backup have verified.

## Source-encoding policy

| File type | Automatic action |
|---|---|
| ASCII, Unicode with a BOM, or text whose encoding EC can prove from its bytes | Convert automatically. |
| Legacy text or BOM-less Unicode whose encoding cannot be proven safely | Do not convert; ask you to choose the original encoding. |

An unchanged file is reported as `Unchanged` and is not decoded or rewritten.

A source encoding chosen by the user controls only how EC reads the original
bytes. It does not bypass any safety check.

### BOM-less UTF-16

Without a byte-order mark, UTF-16 bytes are often valid as both UTF-16LE and
UTF-16BE. Byte-swapped Latin text commonly lands in a valid CJK range. EC
strictly decodes the complete source with the opposite byte order before it
allows automatic conversion. If both work, it reports `Refused` with reason
code `AmbiguousBomlessUtf16` and leaves the source unchanged.

This is intentionally conservative: most ordinary BOM-less UTF-16 files are
expected to be refused. It costs a second full read, which is deliberate because
rewriting a file must not rest on a sample-based byte-order guess.

### BOM-less UTF-32

EC never converts it automatically. It reports `Refused` with reason code
`UnprovableBomlessUtf32` and leaves the source unchanged.

The doubt is larger than a byte order. UTF-16 text with one character per line
puts a C0 control in every second code unit, so each four-byte group is an
in-range unassigned scalar and the whole file decodes as valid UTF-32 — while
the *opposite* UTF-32 order rejects it, so the test that protects UTF-16 would
pass it through. Separately, genuine UTF-32 can be valid under both orders.

The cost is that ordinary BOM-less UTF-32 is refused too. Nothing in the bytes
separates it from the UTF-16 file above. Add a BOM, or name the source.

Choose `-From utf-16le` or `-From utf-16be` if you know the source order. That
chooses the source interpretation only; it does not bypass any other safeguard.

## Plans, backups, and recovery metadata

A saved plan records each selected file's relative path, size, SHA-256, source
interpretation, target/BOM policy, backup choice, and EC safety semantics.
`-Apply` verifies every planned file before writing anything. A changed, missing,
or incompatible planned file invalidates the entire plan.

With `-Backup`, EC creates a portable `<file>.ecmeta.json` sidecar. It records
the codec actually used, whether it was detected or explicitly selected, source
and backup hashes, expected output hash, BOM policy, version, timestamp, and
whether installation was prepared or completed. The `.bak` and sidecar provide
independently verifiable recovery information. EC does not currently provide a
built-in restore command.

`<file>.bak` is a fixed name holding the version the most recent run replaced, so
it is one level of undo rather than a history. Converting the same file again
replaces the backup, and removes its sidecar with it. If you may want the original
of a file you are about to convert a second time, copy the existing `.bak`
elsewhere first.

`-Journal` creates the batch-level record: detection, chosen source, decision,
reason code, final result, and before/after hashes for every file.

## Concurrent changes and links

EC does not follow linked/reparse-point files or directories. Plans verify
source hashes before execution and EC checks again immediately before
installation. This greatly reduces accidental overwrite risk, but cannot remove
the narrow race where another process changes a file between the last check and
replacement.

## Known limits

No detector can infer an author's intended legacy codec when several codecs
accept the same bytes but produce different Unicode text. EC refuses automatic
legacy conversion for that reason. Explicit choices remain the user's
responsibility, but the conversion engine still verifies that it can preserve the
text under that chosen interpretation.
