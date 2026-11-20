# PostgreSQL 18 to 19 major-version upgrade

**Status:** CURRENT planning and operator-safety runbook; no live migration is authorized or implied.

**Target:** PostgreSQL 19.0, released 2026-11-19. Recheck the PostgreSQL versioning page and the target provider's supported minor before each rollout.

## Scope and version boundaries

The repository targets PostgreSQL 19 for local development, CI and self-managed acceptance. The Hostinger Acceptance Compose uses the official PostgreSQL 19.0 image and a version-specific data path. Azure Flexible Server templates retain PostgreSQL 18 as their default. As of 2026-11-20, Azure PostgreSQL 19 is preview-only in East Asia and some extensions may be unavailable. Do not select it for a production Azure deployment until the exact region, extension set, preview terms, backup/restore behavior and availability requirements are accepted.

Changing an image tag, `PGDATA`, Bicep parameter or connection string does not migrate a PostgreSQL cluster. PostgreSQL major versions use different data-file formats. The current Hostinger startup guard refuses to initialize a PostgreSQL 19 cluster beside a detected PostgreSQL 18 or legacy cluster. Do not remove the guard or repoint it at an older cluster to make startup succeed.

This runbook is a safe plan. It does not run a database migration, create a new production database, alter tenant/provider configuration or deploy the application.

## PostgreSQL 19 features and operational defaults

Use the 19.0 engine without speculative configuration changes first. Rebaseline with representative, non-production data and measurements before tuning.

- Preserve the `io_method = worker` default where supported. PostgreSQL 19 automatically adjusts I/O worker counts within its new bounds; do not carry a PostgreSQL 18 `io_workers` setting forward. Compare `pg_stat_io` and workload latency before changing worker bounds.
- Keep autovacuum enabled. PostgreSQL 19 can parallelize index vacuuming and reprioritize work; observe vacuum progress, backlog and resource use before changing autovacuum settings.
- Refresh planner statistics after the upgrade. The planner, foreign-key checks and some join strategies have changed; compare representative plans and timings on the restored copy, not production first.
- `REPACK (CONCURRENTLY)` is available for evidence-backed bloat maintenance. It is not an automatic migration step; assess free space, write load, privileges and recovery capacity before scheduling it.
- JIT is disabled by default in PostgreSQL 19. Retain that default unless measured analytical workloads justify a scoped change.
- Do not enable `pg_plan_advice` or `pg_stash_advice` merely because they are new. They are optional extensions and need separate support, security and workload review.
- Keep PostgreSQL 19-specific SQL optional until the Npgsql/EF Core stack passes exact-source CI. New features such as `INSERT ... ON CONFLICT DO SELECT` or standby `WAIT FOR LSN` must follow the application's existing idempotency and consistency contracts.

These capabilities are opportunities, not performance guarantees. Do not publish speedup claims without comparable workload, resource and latency measurements.

## Preflight inventory

Bind every inventory and backup to the source, database, provider, engine version and UTC observation. Do not include credentials or client financial data in reports.

1. Record the source and target engine minor versions, operating system/architecture, locale/collation provider, encoding, tablespaces, WAL placement, authentication methods, replication/slot topology, connection settings and configured server parameters.
2. Inventory installed extensions and versions, their server-side shared libraries, control files, upgrade paths and target-provider availability. An extension absent from the target is a stop condition until a supported replacement or migration is approved.
3. Find database, role and tablespace names containing carriage returns or line feeds; PostgreSQL 19 rejects them, and `pg_upgrade` will refuse such clusters.
4. Inspect every `inet`/`cidr` index using `btree_gist`. PostgreSQL 19 rejects affected indexes because those operator classes could omit matching rows. Resolve each finding and verify query semantics before the upgrade.
5. Check for RADIUS authentication, `io_workers` configuration, scripts reading renamed wait events or `pg_stat_subscription_stats.sync_error_count`, and locale-sensitive full-text-search/`pg_trgm`/`fuzzystrmatch` behavior. PostgreSQL 19 removes RADIUS support and `io_workers`; update monitors for renamed/removed statistics. Follow PostgreSQL 19's reindex/recompute guidance where locale-sensitive objects are used.
6. Check dump compatibility. PostgreSQL 19 forces `standard_conforming_strings` on. For dump/restore, use PostgreSQL 19 `pg_dump`/`pg_dumpall` against the source when supported, or ensure the source setting is on and validate the resulting dump on the isolated target.
7. Run the repository's PostgreSQL 19 focused CI and restore checks against disposable, isolated data. Verify database migrations, extension behavior, query plans, authentication/TLS, application startup and accounting invariants. Do not use production records as a test fixture.

## Recovery-first upgrade procedure

1. Approve a change record with the target engine/provider, maintenance window, RPO/RTO, owners, user-impact plan, rollback boundary and exact application release SHA. Confirm the production host, tenant and database identity independently.
2. Capture a fresh database backup, global roles/privileges and tablespace/configuration inventory. Verify checksums and restore into an isolated target. Preserve the original cluster and backup in independently controlled storage. A successful dump command is not restore evidence.
3. Rehearse the full migration on a protected copy. Measure duration and storage headroom and verify row counts, constraints, application/domain invariants, migration history, extension status, permissions, indexes, restore behavior and representative read/write journeys. Do not write rehearsal findings as production measurements.
4. For `pg_upgrade`, install PostgreSQL 19 binaries and target-compatible extension files first, initialize a separate PG19 cluster, then use the **PostgreSQL 19** `pg_upgrade` binary. Run `pg_upgrade --check` with the same mode intended for the rehearsal. The default copy mode is the safer starting point. `--link` makes the old cluster unsafe to restart after the new cluster is started; `--swap` also modifies the old data during transfer. Use either only with an explicitly accepted recovery design and verified independent backup. `--clone` is supported only on specified filesystems and must itself be rehearsed.
5. Alternatively, use a PG19 `pg_dump`/`pg_dumpall` plus restore into a separate PG19 cluster. Logical replication is a distinct migration design requiring sequence, consistency, replication-lag and cutover analysis; it is not an automatic fallback.
6. Apply generated post-upgrade scripts and extension updates reported by `pg_upgrade`. Keep affected relations out of service until required rebuild scripts finish. Run `vacuumdb --all --analyze-in-stages --missing-stats-only`, followed by `vacuumdb --all --analyze-only`, then confirm fresh statistics.
7. Validate application health and read/write behavior with the exact release candidate. Keep the prior application image, the untouched source cluster and independent backup available. Reopen traffic only after the approved canary and recovery checks pass.
8. Retain both clusters until the owner-approved observation window and recovery decision complete. Do not delete the old data directory or call the change complete from a healthy process alone.

## Hostinger Acceptance image/data-path guard

The self-managed Compose file mounts the named volume at `/var/lib/postgresql`, sets `PGDATA=/var/lib/postgresql/19/docker`, and executes `infra/hostinger-acceptance/postgres-entrypoint-guard.sh` before the official image entrypoint. A fresh empty volume is initialized at the new path. A PG19 cluster is accepted only at that path. A detected PG18 or legacy cluster causes a refusal before initialization.

Before any future Acceptance rollout, inspect the actual container image, `PGDATA`, Docker volume name/options, mount source, and `PG_VERSION` on the host. Do not recreate, prune, rename or empty an existing volume to bypass refusal. The initial image/path transition requires a separate copied-data major-upgrade rehearsal and owner-authorized cutover plan. This guard is not a migration utility and is not proof that an existing volume has been migrated.

## Azure Flexible Server selection

The Bicep `postgresVersion` parameter accepts 18 or 19 and defaults to 18 in both environments. Azure's PostgreSQL 19.0 preview is currently limited to East Asia, with possible extension gaps. Before selecting 19, verify the actual resource region and supported extension matrix, then obtain owner approval for preview-service use. Keep the default 18 where region/support or recovery requirements do not qualify. Changing the parameter on an existing server is not a substitute for a reviewed major-version migration; use the provider's documented upgrade path and separately rehearse recovery.

## Official references

- [PostgreSQL 19 release notes](https://www.postgresql.org/docs/19/release-19.html)
- [PostgreSQL 19 `pg_upgrade`](https://www.postgresql.org/docs/19/pgupgrade.html)
- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)
- [Azure Database for PostgreSQL supported versions](https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/concepts-supported-versions)
- [Official PostgreSQL Docker image documentation](https://github.com/docker-library/docs/tree/master/postgres)
