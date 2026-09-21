#!/usr/bin/env bash
set -euo pipefail

# Development-only restore rehearsal. It refuses non-loopback sources and drops
# only the generated temporary database during cleanup.
db_host="127.0.0.1"
db_port="5433"
source_db="auditsphere"
db_user="postgres"
evidence_file="${AUDITSPHERE_EVIDENCE_FILE:-docs/evidence/restore-drill-latest.json}"
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
expected_migrations="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$source_db" \
  -Atc 'SELECT count(*) FROM "__EFMigrationsHistory"')"
expected_latest_migration="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$source_db" \
  -Atc 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 1')"
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
if [[ "$restored_migrations" != "$expected_migrations" || "$latest_migration" != "$expected_latest_migration" ]]; then
  echo "Restore verification failed: source migrations=$expected_migrations latest=$expected_latest_migration; restored migrations=$restored_migrations latest=$latest_migration" >&2
  exit 1
fi

source_checkpoint_summary="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$source_db" \
  -Atc "SELECT count(*)::text || '|' || COALESCE(string_agg(manifest_digest, ',' ORDER BY id), '') FROM release_checkpoints")"
restored_checkpoint_summary="$($psql_bin -h "$db_host" -p "$db_port" -U "$db_user" -d "$restore_db" \
  -Atc "SELECT count(*)::text || '|' || COALESCE(string_agg(manifest_digest, ',' ORDER BY id), '') FROM release_checkpoints")"
if [[ "$source_checkpoint_summary" != "$restored_checkpoint_summary" ]]; then
  echo "Restore checkpoint reconciliation failed: source=$source_checkpoint_summary restored=$restored_checkpoint_summary" >&2
  exit 1
fi

# Keep the accounting/group lineage that a release depends on in the restore
# assertion. These are local hashes of persisted manifests and line identities;
# provider custody and production recovery remain separate gates.
accounting_manifest_summary() {
  local database="$1"
  "$psql_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d "$database" -Atc "
SELECT 'financial-packages|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || revision::text || ':' || COALESCE(calculation_hash, '') || ':' || currency || ':' || COALESCE(period_id::text, ''), ',' ORDER BY id)), md5('')) FROM financial_packages
UNION ALL
SELECT 'financial-package-lines|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || financial_package_id::text || ':' || source_account_code || ':' || destination_code || ':' || amount::text || ':' || currency, ',' ORDER BY id)), md5('')) FROM financial_package_lines
UNION ALL
SELECT 'financial-package-artifacts|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || financial_package_id::text || ':' || package_revision::text || ':' || artifact_sha256_hex, ',' ORDER BY id)), md5('')) FROM financial_package_artifacts
UNION ALL
SELECT 'consolidation-scopes|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || version::text || ':' || reporting_currency || ':' || method || ':' || opening_run_hash || ':' || opening_translation_manifest_hash || ':' || recurring_elimination_manifest, ',' ORDER BY id)), md5('')) FROM consolidation_scope_versions
UNION ALL
SELECT 'consolidation-runs|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || scope_version_id::text || ':' || run_hash || ':' || input_manifest || ':' || reporting_currency || ':' || signed_total::text, ',' ORDER BY id)), md5('')) FROM consolidation_runs
UNION ALL
SELECT 'consolidation-run-lines|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || run_id::text || ':' || COALESCE(component_id::text, '') || ':' || COALESCE(consolidation_journal_id::text, '') || ':' || taxonomy_code || ':' || component_amount::text || ':' || alignment_amount::text || ':' || elimination_amount::text || ':' || consolidated_amount::text || ':' || currency, ',' ORDER BY id)), md5('')) FROM consolidation_run_lines
UNION ALL
SELECT 'external-component-packs|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || version::text || ':' || raw_source_hash || ':' || normalized_source_digest || ':' || pack_digest || ':' || status, ',' ORDER BY id)), md5('')) FROM external_component_packs
UNION ALL
SELECT 'external-component-pack-lines|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || external_component_pack_id::text || ':' || taxonomy_code || ':' || amount::text || ':' || currency || ':' || source_line_reference, ',' ORDER BY id)), md5('')) FROM external_component_pack_lines
UNION ALL
SELECT 'release-deliveries|' || count(*)::text || '|' || COALESCE(md5(string_agg(id::text || ':' || release_candidate_id::text || ':' || manifest_digest || ':' || authorized_release_key, ',' ORDER BY id)), md5('')) FROM releases
ORDER BY 1"
}

source_accounting_manifest_summary="$(accounting_manifest_summary "$source_db")"
restored_accounting_manifest_summary="$(accounting_manifest_summary "$restore_db")"
if [[ "$source_accounting_manifest_summary" != "$restored_accounting_manifest_summary" ]]; then
  echo "Accounting/group manifest reconciliation failed." >&2
  diff -u <(printf '%s\n' "$source_accounting_manifest_summary") <(printf '%s\n' "$restored_accounting_manifest_summary") >&2 || true
  exit 1
fi

release_delivery_duplicate_keys() {
  local database="$1"
  "$psql_bin" -h "$db_host" -p "$db_port" -U "$db_user" -d "$database" -Atc \
    "SELECT count(*) FROM (SELECT authorized_release_key FROM releases GROUP BY authorized_release_key HAVING count(*) > 1) duplicate_keys"
}

source_release_delivery_duplicates="$(release_delivery_duplicate_keys "$source_db")"
restored_release_delivery_duplicates="$(release_delivery_duplicate_keys "$restore_db")"
if [[ "$source_release_delivery_duplicates" != "0" || "$restored_release_delivery_duplicates" != "0" ||
      "$source_release_delivery_duplicates" != "$restored_release_delivery_duplicates" ]]; then
  echo "Duplicate release delivery identities detected: source=$source_release_delivery_duplicates restored=$restored_release_delivery_duplicates" >&2
  exit 1
fi

mkdir -p "$(dirname "$evidence_file")"
printf '%s\n' \
  '{' \
  "  \"status\": \"PASS\"," \
  "  \"recordedAt\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"," \
  "  \"databaseHost\": \"$db_host\"," \
  "  \"sourceDatabase\": \"$source_db\"," \
  "  \"restoredDatabase\": \"$restore_db\"," \
  "  \"expectedMigrationCount\": $expected_migrations," \
  "  \"restoredMigrationCount\": $restored_migrations," \
  "  \"expectedLatestMigration\": \"$expected_latest_migration\"," \
  "  \"restoredLatestMigration\": \"$latest_migration\"," \
  "  \"releaseCheckpointDatabaseSummary\": \"$source_checkpoint_summary\"," \
  "  \"restoredReleaseCheckpointDatabaseSummary\": \"$restored_checkpoint_summary\"," \
  '  "checkpointDatabaseReconciliation": "PASS",' \
  '  "accountingGroupManifestReconciliation": "PASS",' \
  "  \"releaseDeliveryDuplicateKeys\": $source_release_delivery_duplicates," \
  '  "releaseDeliveryDuplicateCheck": "PASS",' \
  '  "externalCheckpointStoreVerification": "NOT_RUN",' \
  '  "custodiallySeparateStorage": false,' \
  '  "productionRpoRto": "NOT_RUN"' \
  '}' > "$evidence_file"
echo "Restore rehearsal passed: PostgreSQL 18 schema restored with $restored_migrations migrations; latest $latest_migration. Evidence: $evidence_file."
