#!/usr/bin/env bash
set -euo pipefail

# Integration-check the Hostinger PG19 role bootstrap against a dedicated, disposable
# PostgreSQL container. Never point this test at a production or shared database.
if [[ "${GITHUB_ACTIONS:-false}" != "true" && "${AUDITSPHERE_ALLOW_LOCAL_ROLE_BOOTSTRAP_TEST:-}" != "1" ]]; then
  echo "Refusing to run outside GitHub Actions or an explicitly allowed local disposable test." >&2
  exit 78
fi

container_id="${1:-}"
if [[ ! "$container_id" =~ ^[[:xdigit:]]{12,64}$ ]]; then
  echo "Pass the container ID of the isolated PostgreSQL 19 test service." >&2
  exit 2
fi

image="$(docker inspect --format '{{.Config.Image}}' "$container_id")"
case "$image" in
  postgres:19.*) ;;
  *) echo "Refusing role-bootstrap test against unexpected container image: $image" >&2; exit 78 ;;
esac

container_status="$(docker inspect --format '{{.State.Status}}' "$container_id")"
if [[ "$container_status" != "running" ]]; then
  echo "The isolated PostgreSQL test container is not running." >&2
  exit 1
fi

server_version_num="$(docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --tuples-only --no-align --dbname postgres --command "SHOW server_version_num")"
if [[ ! "$server_version_num" =~ ^19[0-9]{4}$ ]]; then
  echo "Role-bootstrap integration requires a PostgreSQL 19 server." >&2
  exit 78
fi

database_exists="$(docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --tuples-only --no-align --dbname postgres --command "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_database WHERE datname = 'auditsphere')")"
if [[ "$database_exists" == "t" ]]; then
  echo "The isolated container already contains an auditsphere database; refusing to modify it." >&2
  exit 78
fi

bootstrap_script="${BASH_SOURCE[0]%/*}/../../../infra/hostinger-acceptance/postgres-role-bootstrap.sh"
docker cp "$bootstrap_script" "$container_id:/tmp/auditsphere-postgres-role-bootstrap.sh"
runtime_password="$(openssl rand -hex 32)"
migrator_password="$(openssl rand -hex 32)"
probe_output="$(mktemp)"
database_created=false

cleanup() {
  if [[ "$database_created" == true ]]; then
    docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --dbname postgres --command 'DROP DATABASE IF EXISTS auditsphere' >/dev/null
  fi
  docker exec "$container_id" rm -f /tmp/auditsphere-postgres-role-bootstrap.sh >/dev/null 2>&1 || true
  rm -f "$probe_output"
}
trap cleanup EXIT

docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --dbname postgres --command 'CREATE DATABASE auditsphere' >/dev/null
database_created=true

run_bootstrap() {
  docker exec --user postgres \
    --env POSTGRES_USER=postgres \
    --env POSTGRES_DB=auditsphere \
    --env AUDITSPHERE_RUNTIME_DB_PASSWORD="$runtime_password" \
    --env AUDITSPHERE_MIGRATOR_DB_PASSWORD="$migrator_password" \
    "$container_id" sh /tmp/auditsphere-postgres-role-bootstrap.sh
}

# First pass creates the roles and default privileges on an empty database.
run_bootstrap >/dev/null

# A new migration-owned table and sequence verify the default grants before any second pass.
docker exec --user postgres --env PGPASSWORD="$migrator_password" "$container_id" \
  psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --username auditsphere_migrator --dbname auditsphere \
  --command 'CREATE TABLE public.role_bootstrap_probe (id bigserial PRIMARY KEY, value text NOT NULL)' >/dev/null

inserted_id="$(docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --quiet --set=ON_ERROR_STOP=1 --tuples-only --no-align --username auditsphere_runtime --dbname auditsphere \
  --command "INSERT INTO public.role_bootstrap_probe(value) VALUES ('default-grants') RETURNING id")"
if [[ "$inserted_id" != "1" ]]; then
  printf 'Runtime role insert returned an unexpected row identity: %q\n' "$inserted_id" >&2
  exit 1
fi

# Second pass applies grants to restored/pre-existing objects and validates effective privileges.
docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname auditsphere --command 'GRANT CREATE ON SCHEMA public TO PUBLIC' >/dev/null
public_create_before="$(docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --quiet \
  --tuples-only --no-align --dbname auditsphere --command \
  "SELECT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(COALESCE(nspacl, pg_catalog.acldefault('n', nspowner))) AS acl WHERE nspname = 'public' AND grantee = 0 AND privilege_type = 'CREATE') FROM pg_catalog.pg_namespace WHERE nspname = 'public'")"
if [[ "$public_create_before" != "t" ]]; then
  echo "The isolated test did not establish its intended PUBLIC CREATE precondition." >&2
  exit 1
fi
run_bootstrap >/dev/null
public_create_after="$(docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --quiet \
  --tuples-only --no-align --dbname auditsphere --command \
  "SELECT EXISTS (SELECT 1 FROM pg_catalog.aclexplode(COALESCE(nspacl, pg_catalog.acldefault('n', nspowner))) AS acl WHERE nspname = 'public' AND grantee = 0 AND privilege_type = 'CREATE') FROM pg_catalog.pg_namespace WHERE nspname = 'public'")"
if [[ "$public_create_after" != "f" ]]; then
  echo "Bootstrap did not revoke PUBLIC CREATE on the application schema." >&2
  exit 1
fi

updated_value="$(docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --quiet --set=ON_ERROR_STOP=1 --tuples-only --no-align --username auditsphere_runtime --dbname auditsphere \
  --command "UPDATE public.role_bootstrap_probe SET value = 'updated' WHERE id = 1 RETURNING value")"
if [[ "$updated_value" != "updated" ]]; then
  echo "Runtime role could not update an existing migration-owned table." >&2
  exit 1
fi

docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --username auditsphere_runtime --dbname auditsphere \
  --command 'SELECT value FROM public.role_bootstrap_probe WHERE id = 1' >/dev/null
docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --username auditsphere_runtime --dbname auditsphere \
  --command 'DELETE FROM public.role_bootstrap_probe WHERE id = 1' >/dev/null

if docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --username auditsphere_runtime --dbname auditsphere \
  --command 'TRUNCATE TABLE public.role_bootstrap_probe' >/dev/null 2>&1; then
  echo "Runtime role unexpectedly has TRUNCATE privilege." >&2
  exit 1
fi

if docker exec --user postgres --env PGPASSWORD="$runtime_password" "$container_id" \
  psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 --username auditsphere_runtime --dbname auditsphere \
  --command 'CREATE TABLE public.runtime_must_not_create (id integer)' >/dev/null 2>&1; then
  echo "Runtime role unexpectedly has schema CREATE privilege." >&2
  exit 1
fi

# A service login must neither inherit another role nor be assumable by another principal.
docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname postgres --command 'CREATE ROLE auditsphere_role_probe NOLOGIN' >/dev/null
docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname postgres --command 'GRANT auditsphere_role_probe TO auditsphere_runtime' >/dev/null
if run_bootstrap >"$probe_output" 2>&1; then
  echo "Bootstrap accepted an unexpected role inherited by the runtime principal." >&2
  exit 1
elif ! grep -Fq 'unexpected role membership' "$probe_output"; then
  echo "Bootstrap failed for an unexpected reason while checking runtime role membership." >&2
  exit 1
fi
docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname postgres --command 'REVOKE auditsphere_role_probe FROM auditsphere_runtime' >/dev/null

docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname postgres --command 'GRANT auditsphere_runtime TO auditsphere_role_probe' >/dev/null
if run_bootstrap >"$probe_output" 2>&1; then
  echo "Bootstrap accepted another principal being granted the runtime role." >&2
  exit 1
elif ! grep -Fq 'unexpected role membership' "$probe_output"; then
  echo "Bootstrap failed for an unexpected reason while checking role grantees." >&2
  exit 1
fi
docker exec --user postgres "$container_id" psql --no-psqlrc --no-password --set=ON_ERROR_STOP=1 \
  --dbname postgres --command 'REVOKE auditsphere_runtime FROM auditsphere_role_probe; DROP ROLE auditsphere_role_probe' >/dev/null

# Once both intentionally invalid memberships are removed, existing grants still reconcile.
run_bootstrap >/dev/null

echo "PostgreSQL 19 Hostinger role bootstrap passed: default and existing-object DML/sequence grants, no runtime TRUNCATE/CREATE, no elevated service roles or unexpected memberships."
