#!/usr/bin/env bash
# CI contract drift check (OpenAPI acceptance): regenerate the canonical OpenAPI 3.1 contract
# from the actual ASP.NET Core endpoints and compare with the committed artifact. A diff means
# the backend changed while the Angular-facing contract was not re-committed; the build fails.
# The contract is written by tools/AuditSphereOps.OpenApiEmitter, which runs the real API host.
# Prerequisites: a Release build of the solution (the script uses --no-build).
set -euo pipefail
cd "$(dirname "$0")/../.."
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
dotnet run --project tools/AuditSphereOps.OpenApiEmitter --no-build --configuration Release \
  -- "$tmp/auditsphere-openapi.json"
if ! diff -u contracts/auditsphere-openapi.json "$tmp/auditsphere-openapi.json"; then
  echo "OpenAPI contract drifted: regenerate contracts/auditsphere-openapi.json and commit it." >&2
  exit 1
fi
echo "OpenAPI contract is current."
