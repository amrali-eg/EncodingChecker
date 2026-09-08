# Defect backlog - historical records

Audit trail for [DEFECT-BACKLOG.md](DEFECT-BACKLOG.md), kept out of it so the
current findings read as current. Nothing here was reworded when it moved: the
recheck that produced the ledger, the measurements behind three rejected
throughput changes, and the mapping from the pre-reformat rows.


## 2026-09-08 recheck record

Every pre-reformat row was checked against source, a current regression test, a
current Release-build probe, or a clearly marked historical measurement. The
source inspection used `c071c10`; the factual recheck is preserved as local
commit `902f567` immediately before this reformat.

For chronology, BL-06 through BL-17 came from the independent review of
`74d5b3d` after v3.11.2. BL-22 through BL-24 came from a later independent review
of released commit `518a844` after v3.12.0. These source identities are retained
because the observations were made against those builds, even though the
current statuses were rechecked against `c071c10`.

Current behavioral probes established:

- the EC-08 hostile regex exceeded a three-second child-process budget;
- automatic and explicit UTF-8 paths both refused a double BOM;
- an old plan in the scan root was scanned as ASCII;
- a filename beginning `=1+1` still produced an absolute-path CSV cell;
- BL-01 and BL-18 both changed Unicode under the wrong automatic interpretation;
- BL-19 and BL-21 were reported inconsistently but refused or failed before a
  write; and
- both names of a hard-linked file preserved exact text and received separate
  recovery artifacts through the normal Windows replacement path.

No old row was skipped. Three portions remain only partly reproducible:

- BL-05 requires precise force-close timing and was inspected rather than
  triggered;
- BL-20's move fallback requires a platform where `File.Replace` is unsupported;
  and
- the historical timing figures above were not rerun; their current code and
  safety properties were checked.

## Three hashing optimisations were measured and rejected

Conversion can read the same file several times, but each read answers a
different question: does the source still match approval, did the backup really
land, and does the installed output contain the verified text? Three throwaway
variants were interleaved against the same 292 MiB, 60-file workload with backup
and journal enabled:

| Variant | Measurement | Safety or usability cost |
|---|---|---|
| Baseline | 1030 ms median | None |
| Digest backup while copying | 872 ms; 15.3% faster | Nothing independently re-reads the restore point on disk |
| Hash source and output while streaming | 3.5% faster in its own batch | Both values derive from intended I/O rather than independent reads |
| XxHash128 instead of SHA-256 | 1078 ms; 4.7% slower | Recorded hashes lose `Get-FileHash` interoperability and collision resistance |

The source can be read up to seven times per converted file, and one hash is
computed twice over bytes already held in memory. Those reads are still not
redundant. The only materially faster variant removed the backup re-read after
`Flush(flushToDisk: true)`, which is the only independent check that the restore
point exists intact. XxHash128 itself measured 16,447 MiB/s against SHA-256 at
2,429 MiB/s—6.8x—but made the parallel I/O-bound workload slower. On that
machine SHA-256 also beat SHA-1 (981 MiB/s), MD5 (754 MiB/s), and SHA-512
(805 MiB/s).

These figures are conditional: a 24-core machine, fast local disk, warm cache,
and eight workers. Cold or network storage may increase the benefit of removing
a read while also increasing the value of verifying it. Even a different speed
result—even 40%—would not change what each check proves. The observed ceiling
was about 15%, and that was the variant which removed the strongest backup
evidence. Only a single-worker run on a slow CPU is likely to make hashing itself
visible. Future throughput work should make the backup re-read cheaper rather
than delete it.

## Pre-reformat row mapping

The task expected 78 rows, 45 with an existing ID and 33 without one. The exact
`c071c10` input had 79 data-shaped rows, 45 of which mentioned an existing ID.
The disjoint breakdown is 35 original canonical rows, 21 unique anonymous
finding or decision rows, 13 duplicate or correction rows, 7 benchmark or
comparison-evidence rows, and 3 malformed blank-cell headers. Four findings
existed only as prose, and one GUI-driver finding existed only in a narrative
section. That yields the 61 unique ledger entries above. The mapping below
accounts for every old location without pretending that a table header or
benchmark variant is a new defect.

| Old location | Canonical finding or destination | Note |
|---|---|---|
| R001 | [EC-01](DEFECT-BACKLOG.md#ec-01) | Original ledger row |
| R002 | [EC-02](DEFECT-BACKLOG.md#ec-02) | Original ledger row |
| R003 | [EC-03](DEFECT-BACKLOG.md#ec-03) | Original ledger row |
| R004 | [EC-04](DEFECT-BACKLOG.md#ec-04) | Original ledger row |
| R005 | [EC-05](DEFECT-BACKLOG.md#ec-05) | Original ledger row |
| R006 | [EC-06](DEFECT-BACKLOG.md#ec-06) | Original ledger row |
| R007 | [EC-07](DEFECT-BACKLOG.md#ec-07) | Original ledger row |
| R008 | [EC-08](DEFECT-BACKLOG.md#ec-08) | Original ledger row |
| R009 | [EC-09](DEFECT-BACKLOG.md#ec-09) | Original ledger row |
| R010 | [EC-10](DEFECT-BACKLOG.md#ec-10) | Original ledger row |
| R011 | [EC-11](DEFECT-BACKLOG.md#ec-11) | Original ledger row |
| R012 | [EC-12](DEFECT-BACKLOG.md#ec-12) | Original ledger row |
| R013 | [EC-13](DEFECT-BACKLOG.md#ec-13) | Original ledger row |
| R014 | [EC-14](DEFECT-BACKLOG.md#ec-14) | Original ledger row |
| R015 | [EC-15](DEFECT-BACKLOG.md#ec-15) | Original ledger row |
| R016 | [EC-16](DEFECT-BACKLOG.md#ec-16) | Original ledger row |
| R017 | [EC-17](DEFECT-BACKLOG.md#ec-17) | Original ledger row |
| R018 | [EC-18](DEFECT-BACKLOG.md#ec-18) | Original ledger row |
| R019 | [EC-19](DEFECT-BACKLOG.md#ec-19) | Original ledger row |
| R020 | [EC-20](DEFECT-BACKLOG.md#ec-20) | Original ledger row |
| R021 | [EC-21](DEFECT-BACKLOG.md#ec-21) | Original ledger row |
| R022 | [EC-22](DEFECT-BACKLOG.md#ec-22) | Original ledger row |
| R023 | [EC-23](DEFECT-BACKLOG.md#ec-23) | Original ledger row |
| R024 | [CX-01](DEFECT-BACKLOG.md#cx-01) | Original ledger row |
| R025 | [CX-02](DEFECT-BACKLOG.md#cx-02) | Original ledger row |
| R026 | [CX-03](DEFECT-BACKLOG.md#cx-03) | Original ledger row |
| R027 | [CX-05](DEFECT-BACKLOG.md#cx-05) | Original ledger row |
| R028 | [CX-06](DEFECT-BACKLOG.md#cx-06) | Original ledger row |
| R029 | [CX-07](DEFECT-BACKLOG.md#cx-07) | Original ledger row |
| R030 | [CX-08](DEFECT-BACKLOG.md#cx-08) | Original ledger row |
| R031 | [CX-09](DEFECT-BACKLOG.md#cx-09) | Original ledger row |
| R032 | [CX-10](DEFECT-BACKLOG.md#cx-10) | Original ledger row |
| R033 | [CX-11](DEFECT-BACKLOG.md#cx-11) | Original ledger row |
| R034 | [CX-12](DEFECT-BACKLOG.md#cx-12) | Original ledger row |
| R035 | [CX-13](DEFECT-BACKLOG.md#cx-13) | Original ledger row |
| R036 | [BL-01](DEFECT-BACKLOG.md#bl-01) | Previously anonymous finding |
| R037 | [BL-02](DEFECT-BACKLOG.md#bl-02) | Previously anonymous finding |
| R038 | [BL-03](DEFECT-BACKLOG.md#bl-03) | Previously anonymous finding |
| R039 | [BL-04](DEFECT-BACKLOG.md#bl-04) | Previously anonymous finding |
| R040 | [BL-05](DEFECT-BACKLOG.md#bl-05) | Previously anonymous finding |
| R041 | [BL-06](DEFECT-BACKLOG.md#bl-06) | Previously anonymous finding |
| R042 | [BL-07](DEFECT-BACKLOG.md#bl-07) | Previously anonymous finding |
| R043 | [BL-08](DEFECT-BACKLOG.md#bl-08) | Previously anonymous finding |
| R044 | [BL-09](DEFECT-BACKLOG.md#bl-09) | Previously anonymous finding |
| R045 | [BL-10](DEFECT-BACKLOG.md#bl-10) | Previously anonymous finding |
| R046 | [BL-11](DEFECT-BACKLOG.md#bl-11) | Previously anonymous finding |
| R047 | [BL-12](DEFECT-BACKLOG.md#bl-12) | Previously anonymous finding |
| R048 | [BL-13](DEFECT-BACKLOG.md#bl-13) | Previously anonymous finding |
| R049 | [BL-14](DEFECT-BACKLOG.md#bl-14) | Previously anonymous finding |
| R050 | [BL-15](DEFECT-BACKLOG.md#bl-15) | Previously anonymous finding |
| R051 | [BL-16](DEFECT-BACKLOG.md#bl-16) | Previously anonymous finding |
| R052 | [BL-17](DEFECT-BACKLOG.md#bl-17) | Previously anonymous finding |
| R053 | [BL-22](DEFECT-BACKLOG.md#bl-22) | Previously anonymous finding |
| R054 | [BL-23](DEFECT-BACKLOG.md#bl-23) | Previously anonymous finding |
| R055 | [BL-24](DEFECT-BACKLOG.md#bl-24) | Previously anonymous finding |
| R056 | [CX-07](DEFECT-BACKLOG.md#cx-07) | Duplicate correction row |
| R057 | [BL-02](DEFECT-BACKLOG.md#bl-02) | Duplicate withdrawal row |
| R058 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Baseline data, not a finding |
| R059 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Backup-read variant, not a separate finding |
| R060 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | Streaming-hash variant, not a separate finding |
| R061 | [Hashing measurements](#three-hashing-optimisations-were-measured-and-rejected) | XxHash variant, not a separate finding |
| R062 | [BL-25](DEFECT-BACKLOG.md#bl-25) | Malformed comparison-table header, not a finding |
| R063 | [BL-25](DEFECT-BACKLOG.md#bl-25) | Raw-hash comparison evidence |
| R064 | [BL-25](DEFECT-BACKLOG.md#bl-25) | Content-digest comparison evidence |
| R065 | [BL-25](DEFECT-BACKLOG.md#bl-25) | Backup-comparison evidence |
| R066 | [Canonical ledger](DEFECT-BACKLOG.md#canonical-ledger) | Malformed score-table header, not a finding |
| R067 | [CX-06](DEFECT-BACKLOG.md#cx-06) | Duplicate score row |
| R068 | [BL-01](DEFECT-BACKLOG.md#bl-01) | Duplicate score row |
| R069 | [EC-08](DEFECT-BACKLOG.md#ec-08) | Duplicate score row |
| R070 | [BL-02](DEFECT-BACKLOG.md#bl-02) | Duplicate score row |
| R071 | [EC-16](DEFECT-BACKLOG.md#ec-16) | Duplicate score row |
| R072 | [EC-20](DEFECT-BACKLOG.md#ec-20) | Duplicate score row |
| R073 | [EC-15](DEFECT-BACKLOG.md#ec-15) | Duplicate score row |
| R074 | [EC-17](DEFECT-BACKLOG.md#ec-17) | Duplicate score row |
| R075 | [EC-23](DEFECT-BACKLOG.md#ec-23) | Duplicate score row |
| R076 | [EC-18](DEFECT-BACKLOG.md#ec-18) | Duplicate score row |
| R077 | [BL-25](DEFECT-BACKLOG.md#bl-25) | Design-decision row |
| R078 | [EC-19](DEFECT-BACKLOG.md#ec-19) | Malformed not-reproduced table header, not a finding |
| R079 | [EC-19](DEFECT-BACKLOG.md#ec-19) | Duplicate evidence row |
| P001 | [BL-18](DEFECT-BACKLOG.md#bl-18) | Former prose-only finding |
| P002 | [BL-19](DEFECT-BACKLOG.md#bl-19) | Former prose-only finding |
| P003 | [BL-20](DEFECT-BACKLOG.md#bl-20) | Former prose-only finding |
| P004 | [BL-21](DEFECT-BACKLOG.md#bl-21) | Former prose-only finding |
| S001 | [BL-26](DEFECT-BACKLOG.md#bl-26) | Former narrative-only GUI-driver finding |
