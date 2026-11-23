# PostgreSQL 18 to 19 major-version upgrade

**Status:** CURRENT operator plan for the owner-directed PostgreSQL 19 adoption on the self-managed Hostinger host. The live major-version cutover remains unqualified and has not been run.

**Target:** PostgreSQL 19.0 is generally available; the PostgreSQL Global Development Group released it on 2026-11-19. Confirm the current supported minor and target-provider support before each rollout.

## Scope and version boundaries

The repository targets PostgreSQL 19 for local development, CI and self-managed Hostinger acceptance. Hostinger Compose pins the official `postgres:19.0-alpine3.24` multi-platform OCI index to `sha256:8b0ff4cdf221f0c53d7fcda681d3b461be725c492cd881c5ec9c0ad25023f188`; update that digest only through a reviewed image-provenance change. Azure is reserved for Microsoft Entra and SharePoint integration. Azure database provisioning and the existing Azure database templates are outside this upgrade and must remain unchanged.

**Latest provider and reachability refresh (2026-11-23 04:15:29 UTC):** the Hostinger read-only API reports VM 1304586 (`crm.steaudit.com`) running on the KVM 2 plan. The latest backup listing contains only snapshots from Nov 20 and Nov 13; it is not a fresh quiesced database/catalog plus actual-volume/configuration/credential backup or restore proof. Hostinger Docker Manager still returns `VPS:2044` for this Ubuntu 24.04 VM. The most recent SSH attempt returned `No route to host`; no remote command ran. The API refresh does not expose PostgreSQL catalogs, Docker mounts, volumes, or current compose overlays, so the last complete direct database read below remains the `2026-11-23T03:49:19Z` observation. Evidence: `.local/autonomous/evidence/release-cycle-optimization-20261123/hostinger-readonly-refresh-20261123T041529Z.json` and `.local/autonomous/evidence/release-cycle-optimization-20261123/hostinger-readonly-refresh-20261123T034919Z.json`. No production mutation occurred.

**Last complete database/catalog read (2026-11-23 03:49:19 UTC):** PostgreSQL 18.6 was healthy on mutable `postgres:18-alpine`, using the retained named volume `auditsphere-acceptance_postgres_data`; the API readiness endpoint returned HTTP 200. The repository was detached at `510394848ff5c2b0780091f99f60e37b8adc3c5a` with untracked `compose.acceptance-workflow.yaml` and `compose.release-2b8faeb3.yaml`; preserve both. The application and worker image digests were older than the then-current candidate. This state is now historical because SSH could not refresh the database after 03:49. The provider snapshot did not include a fresh quiesced backup, actual volume/configuration/credential/overlay backup, or independent restored-copy rehearsal. GitHub had no deployment environment, repository deployment secrets/variables, or Hostinger deployment workflow at that observation. Evidence: `.local/autonomous/evidence/release-cycle-optimization-20261123/hostinger-readonly-refresh-20261123T034919Z.json`.

**Earlier source-cluster inventory (2026-11-22):** PostgreSQL 18.6 used `archive_mode=off` and `wal_level=replica`; the `auditsphere` login had superuser, database/role creation, replication, and row-security-bypass attributes; runtime/migrator roles were absent. The database had 392 public tables and 327 EF migration-history rows; three sessions were visible at one instant, which was not quiescence. Only `plpgsql` 1.0 was listed. This older inventory is useful for planning but does not establish the current catalog. Evidence: `.local/autonomous/evidence/pg19-f42-recovery-20261122/hostinger-readonly-observation-20261122.json`.

**Exact-source CI evidence:** application candidate `dbc953e5dd92db9aa7590be70794fdbe66d7ca38` passed fast run `45086381166`, image run `45086381271`, and full run `45088802861` (attempt 1; API 441/441, Domain 2,071/2,071, Chrome/PostgreSQL E2E 351/351; zero failures or skips). The full evidence is `.local/autonomous/evidence/release-cycle-optimization-20261123/ci-dbc953e5/full/full-terminal-review.json`. Current documentation source `7b89f7390eea63b5d9618de2005beab855f4e4a1` passed docs-only fast run `45109170333`; Angular and .NET jobs were correctly skipped because only documentation changed. Neither exact-source CI result qualifies a live database upgrade or production release. The earlier `f42` and `9245` results, including the synthetic PG19 rehearsal, are historical; they do not prove a restored production copy, accepted outage/RPO/RTO/rollback or authenticated live acceptance. No performance improvement is inferred from non-comparable runs.

Changing an image tag, `PGDATA`, Bicep parameter or connection string does not migrate a PostgreSQL cluster. PostgreSQL major versions use different data-file formats. The current Hostinger startup guard refuses to initialize a PostgreSQL 19 cluster beside a detected PostgreSQL 18 or legacy cluster. Do not remove the guard or repoint it at an older cluster to make startup succeed.

This runbook is a safe plan. It does not run a database migration, create a new production database, alter tenant/provider configuration or deploy the application.

## PostgreSQL 19 features and operational defaults

Use the 19.0 engine without speculative configuration changes first. Rebaseline with representative, non-production data and measurements before tuning.

- Preserve the `io_method = worker` default where supported. PostgreSQL 19 automatically adjusts I/O worker counts within its new bounds; do not carry a PostgreSQL 18 `io_workers` setting forward. Compare `pg_stat_io` and workload latency before changing worker bounds.
- Keep autovacuum enabled. PostgreSQL 19 adds weighted autovacuum prioritization and parallel index vacuuming, but `autovacuum_max_parallel_workers` defaults to `0`. Observe `pg_stat_autovacuum_scores` and `pg_stat_progress_vacuum` on the restored workload before setting parallelism; the score view is not a reliable prediction of the exact worker order.
- Refresh planner statistics after the upgrade. The planner, foreign-key checks and some join strategies have changed; compare representative plans and timings on the restored copy, not production first.
- `REPACK (CONCURRENTLY)` is available for evidence-backed bloat maintenance. It is not an automatic migration step; assess free space, write load, privileges and recovery capacity before scheduling it.
- JIT is disabled by default in PostgreSQL 19. Retain that default unless measured analytical workloads justify a scoped change.
- When the target build includes LZ4 support, PostgreSQL 19 changes the default TOAST compression from `pglz` to `lz4`. Keep the supported default for newly stored values; do not claim existing rows were recompressed or estimate storage savings without measuring a restored copy.
- Do not enable `pg_plan_advice` or `pg_stash_advice` merely because they are new. They are optional extensions and need separate support, security and workload review.
- Keep PostgreSQL 19-specific SQL optional until the Npgsql/EF Core stack passes exact-source CI. New features such as `INSERT ... ON CONFLICT DO SELECT` or standby `WAIT FOR LSN` must follow the application's existing idempotency and consistency contracts.

These capabilities are opportunities, not performance guarantees. Do not publish speedup claims without comparable workload, resource and latency measurements.

## Preflight inventory

Bind every inventory and backup to the source, database, provider, engine version and UTC observation. Do not include credentials or client financial data in reports.

Start with the repository's read-only catalog inventory:

```bash
PGSERVICE=auditsphere-preflight PGPASSFILE="$HOME/.pgpass" \
  python3 scripts/db/postgresql_major_upgrade_preflight.py
```

Use a protected libpq service entry and password file or a client certificate; this tool rejects `PGPASSWORD`, uses `psql --no-password`, and runs a single `BEGIN READ ONLY` transaction. It prints no row data, password verifiers, authentication rules or raw connection errors. It reports cluster-wide role/database/tablespace names and settings but inventories extensions only in the selected database. Its result is an inventory, never a cutover approval: it deliberately leaves `cutoverQualified=false` and lists the remaining manual checks. Follow it with the target-version `pg_upgrade --check` on the exact copied clusters and mode; that remains authoritative for binary-upgrade compatibility.

1. Record the source and target engine minor versions, operating system/architecture, locale/collation provider, encoding, tablespaces, WAL placement, authentication methods, replication/slot topology, connection settings and configured server parameters. The last Hostinger database observation is 18.6; upstream PostgreSQL 18.7 is available, but Hostinger's patch path is unconfirmed. Rehearse from the exact observed source minor or first verify an approved supported patch path. The current application DB login was previously observed as superuser: before cutover, separately establish and test a least-privileged runtime principal and a distinct migration principal. Do not revoke or rotate the live credential as part of this unqualified upgrade.
2. Inventory installed extensions and versions, their server-side shared libraries, control files, upgrade paths and target-provider availability. An extension absent from the target is a stop condition until a supported replacement or migration is approved.
3. Find database, role and tablespace names containing carriage returns or line feeds; PostgreSQL 19 rejects them, and `pg_upgrade` will refuse such clusters.
4. Inspect every GiST `inet`/`cidr` index using `btree_gist`. PostgreSQL documents that these opclasses can miss relevant rows and that `pg_upgrade` refuses indexes using `gist_inet_ops` or `gist_cidr_ops`; identify each exact index, create and validate the built-in GiST `inet_ops` replacement, then remove the faulty index only through the separately reviewed migration plan. Neither this preflight nor `pg_upgrade --check` repairs indexes.
5. Check for RADIUS authentication, MD5 password verifiers, `io_workers` configuration, explicit `max_locks_per_transaction` sizing, scripts reading renamed wait events or `pg_stat_subscription_stats.sync_error_count`, and locale-sensitive full-text-search/`pg_trgm`/`fuzzystrmatch` behavior. PostgreSQL 19 removes RADIUS support and `io_workers`, warns on successful MD5 authentication, and raises the default `max_locks_per_transaction` from 64 to 128; update monitors for renamed/removed statistics. Follow PostgreSQL 19's reindex/recompute guidance where locale-sensitive objects are used.
6. Check dump compatibility. PostgreSQL 19 forces `standard_conforming_strings` on. For dump/restore, use PostgreSQL 19 `pg_dump`/`pg_dumpall` against the source when supported, or ensure the source setting is on and validate the resulting dump on the isolated target.
7. Run the repository's PostgreSQL 19 focused CI and restore checks against disposable, isolated data. Verify database migrations, extension behavior, query plans, authentication/TLS, application startup and accounting invariants. Do not use production records as a test fixture.

## Recovery-first upgrade procedure

1. Approve a change record with the target engine/provider, maintenance window, RPO/RTO, owners, user-impact plan, rollback boundary and exact application release SHA. Confirm the production host, tenant and database identity independently.
2. Capture a fresh database backup, global roles/privileges and tablespace/configuration inventory. Verify checksums and restore into an isolated target. Preserve the original cluster and backup in independently controlled storage. A successful dump command is not restore evidence.
3. Rehearse the full migration on a protected copy. Measure duration and storage headroom and verify row counts, constraints, application/domain invariants, migration history, extension status, permissions, indexes, restore behavior and representative read/write journeys. Do not write rehearsal findings as production measurements.
4. For `pg_upgrade`, install PostgreSQL 19 binaries and target-compatible extension files first, initialize a separate PG19 cluster, then use the **PostgreSQL 19** `pg_upgrade` binary. Run `pg_upgrade --check` with the same mode intended for the rehearsal. The default copy mode preserves the old cluster and is the safer starting point. Use `--jobs` only after measuring the host's CPU, memory and I/O headroom; PostgreSQL documents CPU cores as a starting point for multi-database/table-space work. `--link` makes the old cluster unsafe to restart after the new cluster is started; `--swap` modifies the old data during transfer. Use either only with an explicitly accepted recovery design and verified independent backup. `--clone` is supported only on specified filesystems and must itself be rehearsed.
5. Alternatively, use a PG19 `pg_dump`/`pg_dumpall` plus restore into a separate PG19 cluster. Logical replication is a distinct migration design requiring sequence, consistency, replication-lag and cutover analysis; it is not an automatic fallback.
6. Apply generated post-upgrade scripts and extension updates reported by `pg_upgrade`. Keep affected relations out of service until required rebuild scripts finish. Run `vacuumdb --all --analyze-in-stages --missing-stats-only`, followed by `vacuumdb --all --analyze-only`, then confirm fresh statistics.
7. Validate application health and read/write behavior with the exact release candidate. Keep the prior application image, the untouched source cluster and independent backup available. Reopen traffic only after the approved canary and recovery checks pass.
8. Retain both clusters until the owner-approved observation window and recovery decision complete. Do not delete the old data directory or call the change complete from a healthy process alone.

## Hostinger Acceptance image/data-path guard

The self-managed Compose file pins the official PG19 image index by digest, mounts the named volume at `/var/lib/postgresql`, sets `PGDATA=/var/lib/postgresql/19/docker`, and executes `infra/hostinger-acceptance/postgres-entrypoint-guard.sh` before the official image entrypoint. It explicitly sets `command: ["postgres"]` because [Compose ignores the image's default `CMD` when a custom entrypoint is configured](https://docs.docker.com/reference/compose-file/services/#entrypoint); omitting the command would let the guard exit without starting PostgreSQL. A fresh empty volume is initialized at the new path. A PG19 cluster is accepted only at that path. A detected PG18 or legacy cluster causes a refusal before initialization.

Before any future Acceptance rollout, inspect the actual container image, `PGDATA`, Docker volume name/options, mount source, and `PG_VERSION` on the host. Do not recreate, prune, rename or empty an existing volume to bypass refusal. The initial image/path transition requires a separate copied-data major-upgrade rehearsal and owner-authorized cutover plan. This guard is not a migration utility and is not proof that an existing volume has been migrated.

## PG19 runtime and migration principals

The tracked Hostinger Compose target separates the database bootstrap login from service access.
`auditsphere` remains a bootstrap superuser confined to the database container;
`auditsphere_migrator` is used only by the one-shot migration service; the API and workers use
`auditsphere_runtime`. Both service logins use SCRAM-SHA-256 and have no superuser, role/database
creation, replication or row-security-bypass attributes. The bootstrap script revokes schema
creation from `PUBLIC`, assigns the application schema to the migration principal, and grants the
runtime principal DML plus the required sequence, function and type access. Future object grants
are attached explicitly to objects created by `auditsphere_migrator`, as PostgreSQL default
privileges are selected by the role creating each object.

This is a PG19 target change, not a live PG18 credential rotation. The init script runs automatically
only for an empty PGDATA. On a disposable restored PG19 target, apply it as the bootstrap role
before restore. Restore a custom-format archive into a fresh database as
`auditsphere_migrator` with `pg_restore --no-owner --no-acl`; PostgreSQL documents that
`--no-owner` makes the connecting restore user own restored objects, and `--no-acl` leaves the
explicit least-privilege grants to the role bootstrap. Then apply/verify the release migrations and
confirm migration history, extensions, RLS, role grants and application read/write behavior. Do not
restore into a non-empty target with cleanup/drop options as a shortcut.

Keep independent high-entropy values for `POSTGRES_PASSWORD`,
`AUDITSPHERE_MIGRATOR_DB_PASSWORD` and `AUDITSPHERE_RUNTIME_DB_PASSWORD` in a protected mode-`0600`
`.env`; use shell-safe values (for example, 32-byte random hex) because they are embedded in
Npgsql connection strings. The Compose model gives these three variables only to the database
service; migration receives only its login and password, and runtime services receive only the
runtime login and password. Official references: PostgreSQL 19
[`pg_restore`](https://www.postgresql.org/docs/19/app-pgrestore.html),
[password authentication](https://www.postgresql.org/docs/19/auth-password.html),
[`ALTER DEFAULT PRIVILEGES`](https://www.postgresql.org/docs/19/sql-alterdefaultprivileges.html),
[`CREATE EXTENSION`](https://www.postgresql.org/docs/19/sql-createextension.html) and
[privileges](https://www.postgresql.org/docs/19/ddl-priv.html).

## Azure boundary

Azure is used only for Microsoft Entra and SharePoint integration under the current owner scope. Do not provision Azure Database for PostgreSQL, change its Bicep parameters, or migrate an Azure database as part of this task. Keep the existing templates untouched.

## Official references

- [PostgreSQL 19 release notes](https://www.postgresql.org/docs/19/release-19.html)
- [PostgreSQL 19 `pg_upgrade`](https://www.postgresql.org/docs/19/pgupgrade.html)
- [PostgreSQL 19 `btree_gist` compatibility](https://www.postgresql.org/docs/19/btree-gist.html)
- [PostgreSQL 19 vacuuming](https://www.postgresql.org/docs/19/routine-vacuuming.html)
- [PostgreSQL 19 resource configuration](https://www.postgresql.org/docs/19/runtime-config-resource.html)
- [PostgreSQL versioning policy](https://www.postgresql.org/support/versioning/)
- [Official PostgreSQL Docker image documentation](https://github.com/docker-library/docs/tree/master/postgres)
