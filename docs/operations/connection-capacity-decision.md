# PostgreSQL connection-capacity decision

AuditSphere keeps direct, role-specific Npgsql pooling until representative evidence proves a pooler is needed. The attached hardening story does not authorize a production PgBouncer deployment.

Run the read-only baseline from a host that can reach the approved PostgreSQL target:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\diagnostics\connection-baseline.ps1 `
  -DurationSeconds 60 -SampleSeconds 5 -OutputPath .\artifacts\connection-baseline.json
```

The script records session counts, active and idle-in-transaction sessions, and the longest observed transaction. It deliberately excludes the sampling connection and never writes application data. Do not put a password or a full connection string in the output; use the normal PostgreSQL credential mechanism on the host.

Interpretation:

- `NOT JUSTIFIED — RETAIN NPGSQL POOLING` means the observed direct pool stayed within the declared thresholds; no pooler change is warranted.
- `INSUFFICIENT EVIDENCE — NO DEPLOYMENT` means the sample was not representative or did not contain enough observations.
- An over-threshold direct pool is a capacity-review signal, not approval to deploy PgBouncer. Before any isolated pilot, measure Web and Worker pools separately and test transaction-pooling compatibility for `SET LOCAL`, tenant/session state, advisory locks, prepared statements, temporary tables, cursors, cancellation, and worker recovery.

Production decisions must attach the exported evidence, the database connection budget, workload shape, transaction-duration distribution, and an explicit owner approval. No pooler configuration is included in this repository.
