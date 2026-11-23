#!/bin/sh
# Runs only when the PostgreSQL image initializes an empty data directory. It can also be
# invoked by an operator against a restored PG19 target before migrations or application start.
# Never run it against the live PG18 source: it intentionally changes roles and schema grants.
set -eu

: "${POSTGRES_USER:?POSTGRES_USER must name the bootstrap superuser}"
: "${POSTGRES_DB:?POSTGRES_DB must name the AuditSphere database}"
: "${AUDITSPHERE_RUNTIME_DB_PASSWORD:?Set the separate runtime password in the protected .env}"
: "${AUDITSPHERE_MIGRATOR_DB_PASSWORD:?Set the separate migration password in the protected .env}"

if [ "$POSTGRES_DB" != "auditsphere" ]; then
  printf '%s\n' 'Refusing role bootstrap for an unexpected database name.' >&2
  exit 78
fi

server_version_num=$(psql --no-psqlrc --no-password --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --tuples-only --no-align --command 'SHOW server_version_num')
case "$server_version_num" in
  19[0-9][0-9][0-9][0-9]) ;;
  *)
    printf '%s\n' 'Refusing role bootstrap: this script is restricted to PostgreSQL 19 targets.' >&2
    exit 78
    ;;
esac

# Read passwords from the process environment instead of command-line arguments. The session
# suppresses statement/error logging while password-bearing DDL is evaluated. psql's quoted
# variable substitution keeps arbitrary supported password characters as SQL literals.
psql --no-password --no-psqlrc --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=ON_ERROR_STOP=1 <<'SQL'
\set QUIET 1
\getenv runtime_password AUDITSPHERE_RUNTIME_DB_PASSWORD
\getenv migrator_password AUDITSPHERE_MIGRATOR_DB_PASSWORD

SET log_statement = 'none';
SET log_min_error_statement = 'panic';
SET password_encryption = 'scram-sha-256';

SELECT format(
  'CREATE ROLE auditsphere_runtime WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
  :'runtime_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'auditsphere_runtime')
\gexec
ALTER ROLE auditsphere_runtime WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'runtime_password';

SELECT format(
  'CREATE ROLE auditsphere_migrator WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
  :'migrator_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'auditsphere_migrator')
\gexec
ALTER ROLE auditsphere_migrator WITH LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'migrator_password';

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO auditsphere_migrator;
GRANT CONNECT ON DATABASE auditsphere TO auditsphere_runtime, auditsphere_migrator;
GRANT CREATE ON DATABASE auditsphere TO auditsphere_migrator;
GRANT USAGE ON SCHEMA public TO auditsphere_runtime;
GRANT CREATE ON SCHEMA public TO auditsphere_migrator;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO auditsphere_runtime;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO auditsphere_runtime;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO auditsphere_runtime;
DO $grant_types$
DECLARE
  type_row record;
BEGIN
  FOR type_row IN
    SELECT typname
    FROM pg_catalog.pg_type
    WHERE typnamespace = 'public'::regnamespace AND typtype <> 'p' AND typelem = 0
  LOOP
    EXECUTE format('GRANT USAGE ON TYPE %I.%I TO auditsphere_runtime', 'public', type_row.typname);
  END LOOP;
END
$grant_types$;

ALTER DEFAULT PRIVILEGES FOR ROLE auditsphere_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO auditsphere_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE auditsphere_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO auditsphere_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE auditsphere_migrator IN SCHEMA public
  GRANT EXECUTE ON FUNCTIONS TO auditsphere_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE auditsphere_migrator IN SCHEMA public
  GRANT USAGE ON TYPES TO auditsphere_runtime;

-- Fail closed if the effective service-role boundary differs from the intended PG19 target.
-- This script can be re-run on a restored PG19 copy: grants are idempotent, and the checks
-- cover existing objects as well as the role attributes and ownership boundary.
DO $verify_auditsphere_roles$
DECLARE
  runtime_oid oid := to_regrole('auditsphere_runtime');
  migrator_oid oid := to_regrole('auditsphere_migrator');
  database_oid oid := (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database());
  public_oid oid := to_regnamespace('public');
BEGIN
  IF runtime_oid IS NULL OR migrator_oid IS NULL OR database_oid IS NULL OR public_oid IS NULL THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: required role or namespace is missing';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM pg_catalog.pg_roles
    WHERE oid IN (runtime_oid, migrator_oid)
      AND (NOT rolcanlogin OR rolinherit OR rolsuper OR rolcreaterole OR rolcreatedb
           OR rolreplication OR rolbypassrls)
  ) OR (SELECT count(*) FROM pg_catalog.pg_roles WHERE oid IN (runtime_oid, migrator_oid)) <> 2 THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: unsafe role attributes';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM pg_catalog.pg_auth_members
    WHERE member IN (runtime_oid, migrator_oid)
       OR roleid IN (runtime_oid, migrator_oid)
  ) THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: unexpected role membership';
  END IF;

  IF NOT has_database_privilege(runtime_oid, database_oid, 'CONNECT')
     OR has_database_privilege(runtime_oid, database_oid, 'CREATE')
     OR (SELECT datdba = runtime_oid FROM pg_catalog.pg_database WHERE oid = database_oid)
     OR NOT has_database_privilege(migrator_oid, database_oid, 'CONNECT')
     OR NOT has_database_privilege(migrator_oid, database_oid, 'CREATE') THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: database privileges or ownership';
  END IF;

  IF NOT has_schema_privilege(runtime_oid, public_oid, 'USAGE')
     OR has_schema_privilege(runtime_oid, public_oid, 'CREATE')
     OR NOT has_schema_privilege(migrator_oid, public_oid, 'USAGE')
     OR NOT has_schema_privilege(migrator_oid, public_oid, 'CREATE')
     OR (SELECT nspowner = migrator_oid FROM pg_catalog.pg_namespace WHERE oid = public_oid) IS NOT TRUE THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: schema privileges or ownership';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM pg_catalog.pg_namespace AS ns
    CROSS JOIN LATERAL pg_catalog.aclexplode(
      COALESCE(ns.nspacl, pg_catalog.acldefault('n', ns.nspowner))) AS entry
    WHERE ns.oid = public_oid
      AND entry.grantee = 0
      AND entry.privilege_type = 'CREATE'
  ) THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: PUBLIC can create in the application schema';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM pg_catalog.pg_class AS relation
    WHERE relation.relnamespace = public_oid
      AND relation.relkind IN ('r', 'p')
      AND (
        relation.relowner = runtime_oid
        OR NOT (has_table_privilege(runtime_oid, relation.oid, 'SELECT')
                AND has_table_privilege(runtime_oid, relation.oid, 'INSERT')
                AND has_table_privilege(runtime_oid, relation.oid, 'UPDATE')
                AND has_table_privilege(runtime_oid, relation.oid, 'DELETE'))
        OR has_table_privilege(runtime_oid, relation.oid, 'TRUNCATE')
        OR has_table_privilege(runtime_oid, relation.oid, 'REFERENCES')
        OR has_table_privilege(runtime_oid, relation.oid, 'TRIGGER')
      )
  ) THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: table grants or ownership';
  END IF;

  IF EXISTS (
    SELECT 1
    FROM pg_catalog.pg_class AS sequence
    WHERE sequence.relnamespace = public_oid
      AND sequence.relkind = 'S'
      AND (
        sequence.relowner = runtime_oid
        OR NOT (has_sequence_privilege(runtime_oid, sequence.oid, 'USAGE')
                AND has_sequence_privilege(runtime_oid, sequence.oid, 'SELECT'))
        OR has_sequence_privilege(runtime_oid, sequence.oid, 'UPDATE')
      )
  ) THEN
    RAISE EXCEPTION 'AuditSphere PostgreSQL service-role verification failed: sequence grants or ownership';
  END IF;
END
$verify_auditsphere_roles$;
SQL
