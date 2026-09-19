#!/usr/bin/env bash
set -euo pipefail

environment="${2:-}"
if [[ "${1:-}" != "--environment" || -z "$environment" ]]; then
  echo 'usage: scripts/verify-tenant.sh --environment <name>' >&2
  exit 1
fi

missing=()
for name in AUDITSPHERE_TENANT_ID AUDITSPHERE_CLIENT_ID AUDITSPHERE_SELECTED_SITE_ID; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done

if ((${#missing[@]} > 0)); then
  printf '{"status":"BLOCKED","environment":"%s","reason":"missing approved tenant prerequisites","missing":[' "$environment"
  printf '"%s",' "${missing[@]}" | sed 's/,$//'
  printf ']}\n'
  exit 2
fi

# A configured identity is not tenant acceptance. The real protected runner is
# intentionally absent until its grants, profiles and evidence are approved.
printf '{"status":"BLOCKED","environment":"%s","reason":"live tenant acceptance runner is not approved in this repository"}\n' "$environment"
exit 2
