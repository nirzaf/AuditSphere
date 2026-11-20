#!/bin/sh
set -eu

data_root=${AUDITSPHERE_POSTGRES_DATA_ROOT:-/var/lib/postgresql}
expected_pgdata="$data_root/19/docker"
pgdata=${PGDATA:-$expected_pgdata}
postgres_entrypoint=${AUDITSPHERE_POSTGRES_ENTRYPOINT:-/usr/local/bin/docker-entrypoint.sh}

if [ "$pgdata" != "$expected_pgdata" ]; then
  printf '%s\n' "Refusing PostgreSQL startup: PGDATA must be $expected_pgdata for PostgreSQL 19." >&2
  exit 78
fi

if [ -s "$pgdata/PG_VERSION" ]; then
  stored_major=$(cat "$pgdata/PG_VERSION")
  if [ "$stored_major" != 19 ]; then
    printf '%s\n' "Refusing PostgreSQL startup: $pgdata contains PostgreSQL $stored_major data, expected 19." >&2
    exit 78
  fi
else
  if [ -e "$pgdata" ] && [ -n "$(find "$pgdata" -mindepth 1 -maxdepth 1 -print -quit 2>/dev/null)" ]; then
    printf '%s\n' "Refusing PostgreSQL startup: target PGDATA $pgdata is non-empty but has no valid PostgreSQL 19 PG_VERSION." >&2
    exit 78
  fi

  for candidate in "$data_root"/*/docker "$data_root"/* "$data_root"; do
    [ -s "$candidate/PG_VERSION" ] || continue
    stored_major=$(cat "$candidate/PG_VERSION")
    printf '%s\n' "Refusing PostgreSQL startup: existing PostgreSQL $stored_major cluster at $candidate; complete and verify the major-version migration before initializing $pgdata." >&2
    exit 78
  done
fi

exec "$postgres_entrypoint" "$@"
