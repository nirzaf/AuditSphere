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

5. write the secret-free result to the ignored `.local/autonomous/evidence/restore-drill-latest.json` (override with `AUDITSPHERE_EVIDENCE_FILE`), then drop only the generated restore database and temporary directory.

The previous tracked checkpoint was dated 2026-09-24 and is retained only in Git history; it reported `externalCheckpointStoreVerification: NOT_RUN`, `custodiallySeparateStorage: false`, and `productionRpoRto: NOT_RUN`. It is not current verification. The operational evidence verifier reports `localRestore: LOCAL_ONLY` only when the currently declared evidence file is present, hash-valid, and passes its required reconciliation checks; a missing or historical-only reference remains `UNKNOWN`. New local results are written under ignored `.local/autonomous/evidence/`; summarize their exact source and outcome in `docs/execution/status.json` only when a rehearsal is actually run. A documentation edit never runs this operational rehearsal.

The dump and restore are on the same loopback development host, so the files are not custodially separate and this proves schema/referential reconciliation plus the local quarantine/epoch boundary only. P7 remains `BLOCKED_EXTERNAL`: operations and records custodians must approve RPO/RTO and recovery topology, rehearse recovery of the authoritative release/archive store, database, selected SharePoint copy and independent checkpoints, and record measured results with independent review. ADR-0009 keeps the release/archive store authoritative; Purview integration is excluded and is not a recovery prerequisite. No production or cross-store recovery was executed by this documentation correction.
