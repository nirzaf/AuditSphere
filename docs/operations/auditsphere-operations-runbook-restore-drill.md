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



The latest observed run passed with 52 migrations through `20260920230951_ClientPeriodAmendmentLineage`. The evidence record explicitly reports `externalCheckpointStoreVerification: NOT_RUN`, `custodiallySeparateStorage: false`, and `productionRpoRto: NOT_RUN`.



The dump and restore are on the same loopback development host, so the files are not custodially separate and this proves schema/referential reconciliation plus the local quarantine/epoch boundary only. Production RPO/RTO and cross-store SharePoint/Purview/checkpoint recovery remain unproven until the approved operations and records custodians run an isolated rehearsal.
