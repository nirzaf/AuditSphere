# AuditSphereOps development restore rehearsal

`scripts/db/restore-drill.sh` performs a bounded local rehearsal against `127.0.0.1:5433` only:

1. dump the development `auditsphere` database to a generated temporary file;
2. create a generated `auditsphere_restore_*` database;
3. restore the dump with ownership and ACL changes disabled;
4. verify the migration history count and latest migration; and
5. drop only the generated restore database and temporary directory.

It does not touch a production database, alter application credentials, or claim an external SharePoint/records restore. Production RPO/RTO and cross-store recovery remain unproven until the approved infrastructure and records custodians run an isolated rehearsal.
