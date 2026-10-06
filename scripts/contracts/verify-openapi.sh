#!/usr/bin/env bash
# CI contract drift check (OpenAPI acceptance): regenerate the canonical OpenAPI 3.1 contract
# from the actual ASP.NET Core endpoints and compare with the committed artifact. A diff means
# the backend changed while the Angular-facing contract was not re-committed; the build fails.
# Prerequisites: a Release build of the solution (the script uses --no-build).
set -euo pipefail
cd "$(dirname "$0")/../.."
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
AUDITSPHERE_EMIT_OPENAPI="$tmp/auditsphere-openapi.json" \
  dotnet test tests/AuditSphereOps.Api.Tests --no-build --configuration Release \
  --filter 'FullyQualifiedName~ContractSerializationIsDeterministicForDriftDetection'
if ! diff -u contracts/auditsphere-openapi.json "$tmp/auditsphere-openapi.json"; then
  echo "OpenAPI contract drifted: regenerate contracts/auditsphere-openapi.json and commit it." >&2
  exit 1
fi
echo "OpenAPI contract is current."
