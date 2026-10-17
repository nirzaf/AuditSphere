# AuditSphereOps development restore rehearsal

**Status:** CURRENT

**Purpose:** Local database loopback restore rehearsal runbook and verification boundaries.

**Authority:** Operational runbook for `scripts/db/restore-drill.sh`.

**Audience:** Developers and operators.

`scripts/db/restore-drill.sh` performs a bounded local rehearsal against `127.0.0.1:5433` only:

1. dump the development `auditsphere` database to a generated temporary file;

2. create a generated `auditsphere_restore_*` database;

3. restore the dump with ownership and ACL changes disabled;

4. compare source/restored migration history and the release-checkpoint database count/digest summary; and

5. write the secret-free result to `../evidence/restore-drill-latest.json`, then drop only the generated restore database and temporary directory.

The retained local checkpoint in `docs/evidence/restore-drill-latest.json` is dated 2026-09-24; it is historical evidence, not verification of today's schema or deployed release. It explicitly reports `externalCheckpointStoreVerification: NOT_RUN`, `custodiallySeparateStorage: false`, and `productionRpoRto: NOT_RUN`. Current observations belong in `docs/execution/status.json`; rerunning this script is a separately selected operational action, not a documentation check.

The dump and restore are on the same loopback development host, so the files are not custodially separate and this proves schema/referential reconciliation plus the local quarantine/epoch boundary only. P7 remains `BLOCKED_EXTERNAL`: operations and records custodians must approve RPO/RTO and recovery topology, rehearse recovery of the authoritative release/archive store, database, selected SharePoint copy and independent checkpoints, and record measured results with independent review. ADR-0009 keeps the release/archive store authoritative; Purview integration is excluded and is not a recovery prerequisite. No production or cross-store recovery was executed by this documentation correction.
