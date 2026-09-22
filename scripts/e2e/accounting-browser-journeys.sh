#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
pg_bin="${PGSQL_HOME:-/opt/homebrew/opt/postgresql@18/bin}"
database="auditsphere_browser"
connection="Host=127.0.0.1;Port=5433;Database=${database};Username=postgres"

if ! "${pg_bin}/psql" -h 127.0.0.1 -p 5433 -U postgres -d postgres -Atqc "select 1 from pg_database where datname='${database}'" | grep -qx 1; then
  "${pg_bin}/createdb" -h 127.0.0.1 -p 5433 -U postgres "${database}"
fi

cd "${repo_root}"
AUDITSPHERE_EF_CONNECTION="${connection}" dotnet ef database update \
  --project src/AuditSphereOps.Infrastructure --startup-project src/AuditSphereOps.Web
"${pg_bin}/psql" -h 127.0.0.1 -p 5433 -U postgres -d "${database}" \
  -f scripts/e2e/accounting-browser-seed.sql

exec env ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://127.0.0.1:5029 \
  ConnectionStrings__AuditSphere="${connection}" \
  DevelopmentIdentity__Enabled=true \
  DevelopmentIdentity__Subject=auditsphere-browser-admin \
  DevelopmentIdentity__TenantId=auditsphere-local \
  dotnet run --no-build --configuration Release --project src/AuditSphereOps.Web --no-launch-profile
