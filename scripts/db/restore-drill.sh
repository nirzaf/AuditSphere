#!/usr/bin/env bash
set -euo pipefail

# Development-only restore rehearsal. It refuses non-loopback sources and drops
# only the generated temporary database during cleanup.
db_host="127.0.0.1"
db_port="5433"
source_db="auditsphere"
db_user="postgres"
psql_bin="$(command -v psql || true)"
if [[ -z "$psql_bin" && -x /opt/homebrew/opt/postgresql@18/bin/psql ]]; then
  psql_bin=/opt/homebrew/opt/postgresql@18/bin/psql
fi
pg_dump_bin="${psql_bin%/psql}/pg_dump"
pg_restore_bin="${psql_bin%/psql}/pg_restore"
if [[ ! -x "$psql_bin" || ! -x "$pg_dump_bin" || ! -x "$pg_restore_bin" ]]; then
  echo "PostgreSQL 18 client tools are required (psql, pg_dump, pg_restore)." >&2
  exit 1
fi

suffix="$(date +%s)_$RANDOM"
restore_db="auditsphere_restore_${suffix}"
work_dir="$(mktemp -d -t auditsphere-restore.XXXXXX)"
dump_file="$work_dir/auditsphere.dump"

cleanup() {
  "$psql_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d postgres \
    -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$restore_db\"" >/dev/null 2>&1 || true
  rm -rf "$work_dir"
}
trap cleanup EXIT

"$psql_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d "$source_db" \
  -v ON_ERROR_STOP=1 -Atc 'SELECT current_setting('"'"'server_version_num'"'"')::int >= 180000'
"$pg_dump_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d "$source_db" \
  --format=custom --no-owner --no-acl --file "$dump_file"
"$psql_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d postgres \
  -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$restore_db\""
"$pg_restore_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d "$restore_db" \
  --no-owner --no-acl "$dump_file"

restored_migrations="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$restore_db" \
  -Atc 'SELECT count(*) FROM "__EFMigrationsHistory"')"
latest_migration="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$restore_db" \
  -Atc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1')"
if [[ "$restored_migrations" != "14" || "$latest_migration" != "20260918133707_ReleaseGateWorkflow" ]]; then
  echo "Restore verification failed: migrations=$restored_migrations latest=$latest_migration" >&2
  exit 1
fi
echo "Restore rehearsal passed: PostgreSQL 18 schema restored with $restored_migrations migrations; latest $latest_migration."
