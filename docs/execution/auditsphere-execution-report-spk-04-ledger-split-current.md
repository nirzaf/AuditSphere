# SPK-04 — Splitting the execution ledger without losing evidence

**Status: CURRENT.** Finding for spike SPK-04 (time box 4 hours), feeding STE-NXT-002. The split was applied on 2026-10-08. The moved bytes are kept in the archive files named below; nothing was deleted.

## Result

`docs/execution/status.json` keeps the pointer keys that humans and agents read on resume. Three history keys moved verbatim to `docs/execution/status-archive-2026-10.json`:

| Key | Size before | Where it lives now |
| --- | --- | --- |
| `verification` | about 827 KB | `status-archive-2026-10.json` under `keys` |
| `localEvidence` | about 208 KB | `status-archive-2026-10.json` under `keys` |
| `documentationWork` | about 10 KB | `status-archive-2026-10.json` under `keys` |

The slim file is 29.7 KB (limit 100 KB). It keeps every other key in its original order, plus an `archive` key that names the file and the moved keys. The values of the moved keys are identical to the originals (checked by parsing both files). Only the JSON layout is normalised to two-space indentation.

Pointer keys kept in `status.json`: `schemaVersion`, `specificationVersion`, `specificationSha256`, `repository`, `branch`, `headCommit`, `verifiedCommit`, `remoteHeadVerified`, `verifiedCommitWasRemoteHead`, `productionEnabled`, `verifiedAt`, `toolchain`, `active`, `remainingLocalWork`, `externalGates`, `evidenceBoundary`, `angularChartAliasPublicationFence`, `archive`.

The `jq` query in `AGENTS.md` §6 (`.active.packageId`, `.remainingLocalWork[].item`, `.externalGates`) returns the same answers after the split.

The current-slice file keeps its header and the 24 newest sections (39.8 KB; limit 60 KB). The 285 older sections moved verbatim to `docs/execution/auditsphere-execution-tracker-current-slice-archive-2026-10-historical.md` (423 KB). Each moved section was checked to appear byte-for-byte in the archive.

## Archive naming

- Machine-readable archive of a ledger: `status-archive-<yyyy-mm>.json`, one file per month the archive covers, beside `status.json`.
- Markdown archive of a slice file: `<area>-<type>-<subject>-archive-<yyyy-mm>-historical.md`, so that the basename is unique and the status suffix says it is not authority.

The date suffix is an archive label, not a primary name, so it follows the naming policy's exception for immutable snapshots.

## Consumers checked

- Inside the repository, nothing parses the moved keys. The docs validator only checks that `status.json` exists. The narrative-metrics validator mentions `status.json` only in its messages.
- `.github/workflows/release.yml` does not read `status.json` or the current slice (checked by search).
- Prose pointers in `AGENTS.md`, `README.md`, the docs index and several architecture documents still say "the execution ledger is `status.json`". That remains true: the current evidence is still in that file. The archive holds history.
- Not checked: the Wiki and any script or dashboard outside this repository. This repository cannot see them. Confirm before relying on the moved keys in such a consumer.

## Open items

- Owner: confirm that no external consumer reads `verification`, `localEvidence` or `documentationWork`, and decide how long the monthly archives are kept.
- The next month's history should move to a new `status-archive-<yyyy-mm>.json`, not into the existing file, so each archive stays append-only.
