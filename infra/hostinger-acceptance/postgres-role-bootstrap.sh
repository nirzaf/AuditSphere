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
SQL
