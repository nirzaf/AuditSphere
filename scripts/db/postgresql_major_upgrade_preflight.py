#!/usr/bin/env python3
"""Read-only PostgreSQL 18 catalog preflight for a separately rehearsed PG19 upgrade."""

from __future__ import annotations

import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from typing import Any


MANUAL_REVIEWS = [
    "Run PostgreSQL 19 pg_upgrade --check against the exact copied source and target clusters, using the same mode planned for rehearsal.",
    "Review pg_hba.conf for RADIUS authentication and authentication policy; this script does not read or print authentication rules.",
    "Review password verifiers for MD5 using an approved protected process; this script never reads or emits password hashes.",
    "Inventory extensions in every database and verify target shared libraries, control files, and upgrade paths.",
    "Verify target collation providers and versions, tablespace paths, replication topology, and configured server files.",
    "Complete a fresh checksum-verified backup in independently controlled storage and restore it into an isolated PostgreSQL 19 target.",
    "Verify target least-privilege service roles, exact application release, accounting invariants, and authenticated Chrome acceptance.",
    "Obtain the approved maintenance window, outage target, RPO/RTO, rollback boundary, and accountable recovery owners.",
]


SQL = r"""
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';
WITH invalid_names AS (
    SELECT kind, object_name
    FROM (
        SELECT 'database'::text AS kind, datname::text AS object_name
        FROM pg_catalog.pg_database
        UNION ALL
        SELECT 'role', rolname::text
        FROM pg_catalog.pg_roles
        UNION ALL
        SELECT 'tablespace', spcname::text
        FROM pg_catalog.pg_tablespace
    ) AS names
    WHERE position(chr(10) IN object_name) > 0
       OR position(chr(13) IN object_name) > 0
), gist_inet_cidr_indexes AS (
    SELECT DISTINCT
        ns.nspname AS schema_name,
        tbl.relname AS table_name,
        idx.relname AS index_name,
        opc.opcname AS operator_class
    FROM pg_catalog.pg_index AS ind
    JOIN pg_catalog.pg_class AS idx ON idx.oid = ind.indexrelid
    JOIN pg_catalog.pg_class AS tbl ON tbl.oid = ind.indrelid
    JOIN pg_catalog.pg_namespace AS ns ON ns.oid = tbl.relnamespace
    JOIN pg_catalog.pg_am AS am ON am.oid = idx.relam
    CROSS JOIN LATERAL unnest(ind.indclass::oid[]) AS classes(opclass_oid)
    JOIN pg_catalog.pg_opclass AS opc ON opc.oid = classes.opclass_oid
    JOIN pg_catalog.pg_depend AS dep
      ON dep.classid = 'pg_catalog.pg_opclass'::regclass
     AND dep.objid = opc.oid
     AND dep.refclassid = 'pg_catalog.pg_extension'::regclass
     AND dep.deptype = 'e'
    JOIN pg_catalog.pg_extension AS ext
      ON ext.oid = dep.refobjid AND ext.extname = 'btree_gist'
    WHERE am.amname = 'gist'
      AND opc.opcintype IN ('inet'::regtype, 'cidr'::regtype)
)
SELECT jsonb_build_object(
    'observedAtUtc', to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'source', jsonb_build_object(
        'serverVersionNum', current_setting('server_version_num')::integer,
        'serverVersion', current_setting('server_version'),
        'currentDatabase', current_database(),
        'currentUser', current_user,
        'transactionReadOnly', current_setting('transaction_read_only') = 'on'
    ),
    'catalog', jsonb_build_object(
        'databaseCount', (SELECT count(*) FROM pg_catalog.pg_database),
        'currentDatabaseEncoding', pg_catalog.pg_encoding_to_char(db.encoding),
        'currentDatabaseCollation', db.datcollate,
        'currentDatabaseCtype', db.datctype,
        'invalidIdentifierNames', COALESCE(
            (SELECT jsonb_agg(jsonb_build_object('kind', kind, 'name', object_name)
                              ORDER BY kind, object_name)
             FROM invalid_names), '[]'::jsonb),
        'tablespaces', COALESCE(
            (SELECT jsonb_agg(spcname ORDER BY spcname) FROM pg_catalog.pg_tablespace), '[]'::jsonb),
        'currentDatabaseExtensions', COALESCE(
            (SELECT jsonb_agg(jsonb_build_object('name', extname, 'version', extversion)
                              ORDER BY extname)
             FROM pg_catalog.pg_extension), '[]'::jsonb),
        'btreeGistInetCidrIndexes', COALESCE(
            (SELECT jsonb_agg(jsonb_build_object(
                 'schema', schema_name,
                 'table', table_name,
                 'index', index_name,
                 'operatorClass', operator_class)
             ORDER BY schema_name, table_name, index_name)
             FROM gist_inet_cidr_indexes), '[]'::jsonb),
        'elevatedRoles', COALESCE(
            (SELECT jsonb_agg(jsonb_build_object(
                 'name', rolname,
                 'superuser', rolsuper,
                 'createRole', rolcreaterole,
                 'createDatabase', rolcreatedb,
                 'replication', rolreplication,
                 'bypassRowSecurity', rolbypassrls)
             ORDER BY rolname)
             FROM pg_catalog.pg_roles
             WHERE rolsuper OR rolcreaterole OR rolcreatedb OR rolreplication OR rolbypassrls),
            '[]'::jsonb)
    ),
    'serverSettings', jsonb_build_object(
        'standardConformingStrings', current_setting('standard_conforming_strings'),
        'ioWorkersSettingPresent', EXISTS (
            SELECT 1 FROM pg_catalog.pg_settings WHERE name = 'io_workers'),
        'maxLocksPerTransaction', (
            SELECT jsonb_build_object('value', setting, 'source', source)
            FROM pg_catalog.pg_settings WHERE name = 'max_locks_per_transaction')
    )
)::text
FROM pg_catalog.pg_database AS db
WHERE db.datname = current_database();
COMMIT;
"""


def find_psql() -> str:
    pgsql_home = os.environ.get("PGSQL_HOME")
    if pgsql_home:
        candidate = Path(pgsql_home) / "psql"
        if candidate.is_file() and os.access(candidate, os.X_OK):
            return str(candidate)
        raise RuntimeError("PGSQL_HOME must name a directory containing executable psql.")

    candidate = shutil.which("psql")
    if candidate:
        return candidate

    homebrew_candidate = Path("/opt/homebrew/opt/postgresql@19/bin/psql")
    if homebrew_candidate.is_file() and os.access(homebrew_candidate, os.X_OK):
        return str(homebrew_candidate)
    raise RuntimeError("psql was not found; install PostgreSQL client tools or set PGSQL_HOME.")


def validate_and_redact(payload: Any) -> dict[str, Any]:
    if not isinstance(payload, dict):
        raise ValueError("preflight query returned an unexpected document")

    source = payload.get("source")
    catalog = payload.get("catalog")
    settings = payload.get("serverSettings")
    if not all(isinstance(value, dict) for value in (source, catalog, settings)):
        raise ValueError("preflight query returned an incomplete document")
    if not isinstance(source.get("serverVersionNum"), int):
        raise ValueError("preflight query omitted the server version")
    if source.get("transactionReadOnly") is not True:
        raise ValueError("refusing a result that did not prove a read-only transaction")

    array_fields = (
        "invalidIdentifierNames",
        "tablespaces",
        "currentDatabaseExtensions",
        "btreeGistInetCidrIndexes",
        "elevatedRoles",
    )
    clean_catalog = {key: catalog.get(key) for key in (
        "databaseCount",
        "currentDatabaseEncoding",
        "currentDatabaseCollation",
        "currentDatabaseCtype",
    )}
    allowed_array_items = {
        "invalidIdentifierNames": ("kind", "name"),
        "currentDatabaseExtensions": ("name", "version"),
        "btreeGistInetCidrIndexes": ("schema", "table", "index", "operatorClass"),
        "elevatedRoles": ("name", "superuser", "createRole", "createDatabase", "replication", "bypassRowSecurity"),
    }
    for key in array_fields:
        value = catalog.get(key)
        if not isinstance(value, list):
            raise ValueError(f"preflight query returned an invalid {key} field")
        if key == "tablespaces":
            if not all(isinstance(item, str) for item in value):
                raise ValueError("preflight query returned an invalid tablespaces field")
            clean_catalog[key] = value
            continue
        allowed = allowed_array_items[key]
        if not all(isinstance(item, dict) for item in value):
            raise ValueError(f"preflight query returned an invalid {key} item")
        if any(any(field not in item for field in allowed) for item in value):
            raise ValueError(f"preflight query omitted a required {key} item field")
        clean_catalog[key] = [{field: item[field] for field in allowed if field in item} for item in value]

    max_locks = settings.get("maxLocksPerTransaction")
    if not isinstance(max_locks, dict) or not {"value", "source"}.issubset(max_locks):
        raise ValueError("preflight query returned an invalid maxLocksPerTransaction field")
    if not isinstance(settings.get("standardConformingStrings"), str):
        raise ValueError("preflight query returned an invalid standardConformingStrings field")
    if not isinstance(settings.get("ioWorkersSettingPresent"), bool):
        raise ValueError("preflight query returned an invalid ioWorkersSettingPresent field")
    clean_settings = {
        "standardConformingStrings": settings.get("standardConformingStrings"),
        "ioWorkersSettingPresent": settings.get("ioWorkersSettingPresent"),
        "maxLocksPerTransaction": {
            key: value
            for key, value in max_locks.items()
            if key in {"value", "source"}
        },
    }
    clean_source = {
        "serverVersionNum": source["serverVersionNum"],
        "serverVersion": source.get("serverVersion"),
        "currentDatabase": source.get("currentDatabase"),
        "currentUser": source.get("currentUser"),
        "transactionReadOnly": True,
    }
    return {
        "schemaVersion": 1,
        "observedAtUtc": payload.get("observedAtUtc"),
        "source": clean_source,
        "catalog": clean_catalog,
        "serverSettings": clean_settings,
    }


def classify(report: dict[str, Any]) -> str:
    source = report["source"]
    catalog = report["catalog"]
    if source["serverVersionNum"] // 10000 != 18:
        return "BLOCKED_UNEXPECTED_SOURCE_MAJOR"
    if catalog["invalidIdentifierNames"]:
        return "BLOCKED_PG19_IDENTIFIER_COMPATIBILITY"
    if catalog["btreeGistInetCidrIndexes"]:
        return "REVIEW_BTREE_GIST_INET_CIDR_INDEXES"
    return "REVIEW_REQUIRED"


def main() -> int:
    service = os.environ.get("PGSERVICE", "")
    if not re.fullmatch(r"[A-Za-z0-9_.-]{1,63}", service):
        print("Set PGSERVICE to a non-secret libpq service name (letters, digits, dot, underscore, hyphen).", file=sys.stderr)
        return 2
    if "PGPASSWORD" in os.environ:
        print("PGPASSWORD is not accepted; use a protected PGPASSFILE or client certificate.", file=sys.stderr)
        return 2

    try:
        psql = find_psql()
        env = os.environ.copy()
        env.setdefault("PGCONNECT_TIMEOUT", "10")
        env["PGAPPNAME"] = "auditsphere-pg19-readonly-preflight"
        result = subprocess.run(
            [
                psql,
                "--no-psqlrc",
                "--no-password",
                "--quiet",
                "--tuples-only",
                "--no-align",
                "--set=ON_ERROR_STOP=1",
                "--dbname",
                f"service={service}",
                "--command",
                SQL,
            ],
            check=False,
            capture_output=True,
            text=True,
            env=env,
            timeout=45,
        )
        if result.returncode != 0:
            raise RuntimeError("read-only preflight query failed")
        parsed = json.loads(result.stdout.strip())
        report = validate_and_redact(parsed)
        report["preflightStatus"] = classify(report)
        report["cutoverQualified"] = False
        report["mutationsPerformed"] = 0
        report["manualReviewRequired"] = MANUAL_REVIEWS
        print(json.dumps(report, indent=2, sort_keys=True))
        return 0
    except (OSError, subprocess.SubprocessError, ValueError, json.JSONDecodeError, RuntimeError, TypeError, KeyError, AttributeError):
        print("PostgreSQL 18-to-19 read-only preflight failed; connection details and provider output were suppressed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
