#!/usr/bin/env bash
# Development only. Existing credentials and database configuration remain authoritative.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
if [[ $# -ne 0 ]]; then
  echo 'Usage: scripts/dev/backend-watch.sh (API on localhost:5100; Angular proxy on :4200)' >&2
  exit 2
fi
if command -v lsof >/dev/null && lsof -nP -iTCP:5100 -sTCP:LISTEN >/dev/null 2>&1; then
  echo 'Port 5100 is occupied. Reuse that backend, or stop its owning process before starting watch.' >&2
  exit 1
fi
export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development
export AngularUi__Enabled=false
export AngularUi__CanonicalRoutes=false
export DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1
exec dotnet watch --non-interactive --project src/AuditSphereOps.Api/AuditSphereOps.Api.csproj \
  run --configuration Debug --no-restore --no-launch-profile \
  --property:BuildInParallel=false -- --urls http://localhost:5100
