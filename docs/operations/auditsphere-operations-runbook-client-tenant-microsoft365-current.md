# Client tenant Microsoft 365, SharePoint and email setup

**Status: CURRENT operator procedure; live customer acceptance must be recorded separately.**

This guide configures a separate AuditSphere installation for an audit firm's Microsoft tenant. A **tenant** is the firm's Microsoft directory; an AuditSphere **client** is one of that firm's customers. Dedicated client sites normally live inside the firm's approved SharePoint tenant. Connecting another customer's directory to the same installation is a separate design and authorization decision.

The [operator Wiki](https://github.com/nirzaf/AuditSphere/wiki/Production-Entra-ID-Configuration) is the published deployment entry point. This repository copy supplies the same procedure to coding agents. Use the [configuration reference](auditsphere-operations-reference-microsoft365-configuration-current.md) for exact keys, mounts and source owners; the [approved permission matrix](../architecture/auditsphere-m365-tenant-administration-permissions.md) and [client-site contract](../architecture/auditsphere-client-sharepoint-sites-current.md) govern permissions.

**Command evidence:** CLI syntax and configuration were reviewed against current source and primary documentation on 2026-10-18. These examples have not been executed against a new customer tenant. Documentation is not tenant acceptance. Commands marked **WRITE** require the customer's authorized operator and the reviewed target/change record. A coding agent prepares commands and checks results; the human completes Microsoft sign-in, MFA, policy assent and consent.

## Navigation and order

| Order | Procedure | Exit evidence |
| --- | --- | --- |
| 1 | [Environment support](#1-environment-support) and [customer inputs](#2-customer-inputs-and-tools) | Supported deployment plan and exact tenant/resources |
| 2 | [Registrations and permissions](#3-create-separate-entra-registrations) | Distinct client IDs, actual grants and approved scope |
| 3 | [Credentials](#4-create-and-install-credentials) | Matching public certificates, restricted private mounts |
| 4 | [Host configuration](#5-configure-the-customer-host) and [initial administrator](#6-bind-the-initial-auditsphere-administrator) | HTTPS, readiness, exact local administrator |
| 5 | [Consent](#7-record-auditspheres-consent-session) | Nonce-bound consent and fresh capability results |
| 6 | [Selected site](#8-resolve-and-grant-the-selected-sharepoint-site) and [probe](#9-verify-selected-resource-access) | Exact site/drive/root reads, unrelated-site denial, optional disposable file receipt |
| 7 | [Dedicated client sites](#10-configure-dedicated-client-site-provisioning) | Explicit rollout, independent site and membership readiness |
| 8 | [Mail](#11-configure-exchange-online-mail), [DNS](#12-configure-email-domain-dns), [alternatives](#13-smtp-and-resend-alternatives) | Scoped sender, controlled send and separate delivery evidence |
| 9 | [Optional directory operations](#14-optional-user-invitation-and-group-capabilities) | Individually consented capabilities and reviewed targets |
| 10 | [Activation, recovery and handover](#15-activate-workers-in-stages), [rotation](#16-rotation-revocation-and-troubleshooting), [checklist](#17-customer-handover-checklist) | Retained evidence, current approvals and unresolved gates |

## 1. Environment support

Read this before creating credentials. The current worker composition is an actual deployment constraint:

| Host/runtime | Current behavior |
| --- | --- |
| API in Production or Acceptance | OIDC, configured certificate-backed directory/capability providers and local administration; API `ExternalEffects:Enabled` stays false. Individual reviewed tenant-administration commands can still call their configured providers; the global flag alone is not their disable switch. |
| Core general/processing/records workers | Production and Acceptance with external effects false |
| Live mail, PBC and client-sites workers | Currently composed only in **Acceptance**, with an isolated worker group and external effects true |
| Hostinger Compose | A synthetic Acceptance deployment, fixed `audit.steaudit.com` host, network, paths and images; it is not a generic production template |
| Azure Bicep | Production has core topology only. Acceptance lists external worker groups but lacks their complete provider settings/credentials; this does not constitute a working live M365 deployment |

Source: [Worker composition](../../src/AuditSphereOps.Worker/Program.cs), [API capability composition](../../src/AuditSphereOps.Api/Authentication/TenantAdministrationComposition.cs), [Hostinger Compose](../../infra/hostinger-acceptance/compose.yaml), [infrastructure runbook](../../infra/auditsphere-deployment-iac-runbook.md).

**Production exit:** an approved, verified production worker/credential composition is required before using real customer data or claiming production M365 service. Do not rename a production deployment to Acceptance or relax startup guards to bypass this gate. The commands below can prepare customer registrations and an approved synthetic acceptance installation while that engineering/release gate remains open.

## 2. Customer inputs and tools

Record identifiers in the customer's protected deployment record, outside the repository. Record secret references, never secret values, in shared handover evidence.

| Input | How to obtain or decide it |
| --- | --- |
| Tenant GUID and tenant primary domain | Entra overview; confirm with customer administrator |
| Microsoft cloud | Current providers target commercial `login.microsoftonline.com` / `graph.microsoft.com`; sovereign/national-cloud deployment needs separate supported provider configuration |
| Public application URL | Approved HTTPS hostname, DNS, valid certificate and reverse-proxy plan |
| Initial administrator | Exact same-tenant member object ID, not an email-only identity |
| Installation and firm IDs | New installation identifier and firm GUID from the deployment record; use a stable non-empty installation GUID for Production Data Protection |
| Working site, library and root | Approved resource; exact Graph IDs resolved in step 8 |
| Negative control | Separate same-tenant synthetic site with no grant to the runtime document app |
| Site custodian and rollout | Enabled Microsoft member; local AuditSphere policy administrator; explicit fixed UTC cutoff |
| Sender and test recipient | Approved Exchange Online mailbox and controlled recipient; separate unrelated synthetic sender for denial checks |
| Optional features | Explicit choice of directory creation, invitations, managed groups, reminders and mail provider |
| Existing state | Registration IDs, permissions, site grants, queued operations, current deployment SHA and backup/recovery evidence |

Use Bash on the admin workstation for Azure CLI examples, Linux Bash on the deployment host for certificate/file commands, and PowerShell 7 for Graph/Exchange examples. Blocks state where they run. Run related blocks in the same named session. Install tools once using their official instructions: [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli), [PowerShell](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell), [Graph PowerShell](https://learn.microsoft.com/en-us/powershell/microsoftgraph/installation), [Exchange Online module](https://learn.microsoft.com/en-us/powershell/exchange/exchange-online-powershell-v2).

**Admin workstation / Bash — READ and interactive sign-in.** Replace examples before running. Names use a controlled slug so they are safe in filters. Use an isolated CLI cache outside the checkout.

```bash
set -euo pipefail
set +x
umask 077
TASK_TENANT_ID='replace-with-customer-tenant-guid'
TASK_BASE_URL='https://audit.customer.example'
TASK_PREFIX='auditsphere-customer-acceptance'
TASK_PRIVATE_DIR=$(mktemp -d)
export AZURE_CONFIG_DIR="$TASK_PRIVATE_DIR/azure"
[[ "$TASK_PREFIX" =~ ^[a-z0-9-]+$ ]]
python3 - "$TASK_TENANT_ID" "$TASK_BASE_URL" <<'PY'
import sys, uuid
from urllib.parse import urlsplit
uuid.UUID(sys.argv[1])
u=urlsplit(sys.argv[2])
assert u.scheme=='https' and u.hostname and u.path in ('','/') and not u.query and not u.fragment
assert not u.username and not u.password and not u.hostname.endswith('.example'), 'Replace example hostname'
PY
TASK_BASE_URL=${TASK_BASE_URL%/}
az login --tenant "$TASK_TENANT_ID" --allow-no-subscriptions --use-device-code --output none
az account show --query '{tenant:tenantId,user:user.name}' --output json
test "$(az account show --query tenantId --output tsv)" = "$TASK_TENANT_ID"
```

The human completes authentication. Tenant application consent requires an appropriately authorized administrator, often Privileged Role Administrator or Global Administrator for Graph application roles; creating registrations alone does not prove that authority. SharePoint site grants also require the documented SharePoint authority and site membership. Exchange and DNS operations require their own delegated administrator permissions. Activate existing approved PIM authority when necessary; this procedure does not assign directory administrator roles.

## 3. Create separate Entra registrations

### 3.1 Identity plan

| Identity suffix | Application permission | Purpose and credential |
| --- | --- | --- |
| `login` | None; OIDC `openid profile email` | Confidential web client; current API uses a client secret |
| `directory-reader` | Graph `User.Read.All` only | Directory reads and consent identity leg; dedicated RSA certificate |
| `selected-site` | Graph `Sites.Selected` only | Exact approved document sites; dedicated RSA certificate plus site `write` grant |
| `outbound-mail` | Existing compatibility model: Graph `Mail.Send` only | Separate certificate and Exchange restriction; read step 11 before consenting |
| `provisioning` | Graph `User.Create` only | Optional workforce user creation |
| `guest-invitation` | Graph `User.Invite.All` only | Optional reviewed B2B invitation |
| `group-membership` | Graph `GroupMember.ReadWrite.All` only | Optional local allowlist of non-privileged static groups |
| `client-sites` | Graph **and SharePoint** `Sites.FullControl.All`, one role for each resource | Separately approved isolated site provisioner; never mounted into API, mail or PBC worker |

Directory-reader callbacks also need the reviewed delegated sign-in/profile scope; a delegated `User.Read` entry is not an additional application role. Do not combine runtime identities. Do not add `Directory.ReadWrite.All`, role-management scopes, mailbox reading or broad document roles to make a refusal disappear.

### 3.2 Create or identify apps and enterprise applications

**Admin workstation / Bash — WRITE.** This helper refuses ambiguous names and preserves an existing app. Before reusing an app, inspect its owners, purpose, credentials and actual permissions; a display-name match is not evidence of correct configuration. The helper prints only the non-secret client ID. Service principals may need time to appear after creation; on propagation failure inspect state and resume, rather than create a differently named duplicate.

```bash
ensure_app() {
  local name="$1" count client_id sp_count audience
  count=$(az ad app list --display-name "$name" --all \
    --query "length([?displayName=='$name'])" --output tsv) || return
  if [ "$count" = 0 ]; then
    client_id=$(az ad app create --display-name "$name" \
      --sign-in-audience AzureADMyOrg --query appId --output tsv) || return
  elif [ "$count" = 1 ]; then
    client_id=$(az ad app list --display-name "$name" --all \
      --query "[?displayName=='$name'].appId | [0]" --output tsv) || return
  else
    printf 'Ambiguous application name: %s\n' "$name" >&2
    return 1
  fi
  audience=$(az ad app show --id "$client_id" --query signInAudience --output tsv) || return
  test "$audience" = 'AzureADMyOrg' || return
  sp_count=$(az ad sp list --filter "appId eq '$client_id'" --all --query 'length(@)' --output tsv) || return
  if [ "$sp_count" = 0 ]; then
    az ad sp create --id "$client_id" --output none || return
  elif [ "$sp_count" != 1 ]; then
    return 1
  fi
  printf '%s\n' "$client_id"
}
TASK_LOGIN_ID=$(ensure_app "$TASK_PREFIX-login")
TASK_READER_ID=$(ensure_app "$TASK_PREFIX-directory-reader")
TASK_DOCUMENT_ID=$(ensure_app "$TASK_PREFIX-selected-site")
# Create only the optional identities approved for this installation:
# TASK_MAIL_ID=$(ensure_app "$TASK_PREFIX-outbound-mail")
# TASK_CREATE_ID=$(ensure_app "$TASK_PREFIX-provisioning")
# TASK_INVITE_ID=$(ensure_app "$TASK_PREFIX-guest-invitation")
# TASK_GROUP_ID=$(ensure_app "$TASK_PREFIX-group-membership")
# TASK_CLIENT_SITES_ID=$(ensure_app "$TASK_PREFIX-client-sites")
```

An application object ID, application **client ID**, and enterprise-application/service-principal object ID are different values. Runtime `ClientId` keys require the client ID. Graph role-assignment and Exchange service-principal commands use the enterprise application's object ID where stated. See [application CLI](https://learn.microsoft.com/en-us/cli/azure/ad/app?view=azure-cli-latest) and [service-principal CLI](https://learn.microsoft.com/en-us/cli/azure/ad/sp?view=azure-cli-latest).

### 3.3 Register exact callbacks without deleting other reviewed callbacks

**Admin workstation / Bash — WRITE.** Merge, then inspect the resulting URI list. A production registration should not inherit development URIs; existing unapproved entries require a separate reviewed removal.

```bash
add_callbacks() {
  local client_id="$1" object_id current_file patch_file
  shift
  object_id=$(az ad app show --id "$client_id" --query id --output tsv)
  current_file="$TASK_PRIVATE_DIR/current-callbacks.json"
  patch_file="$TASK_PRIVATE_DIR/callback-patch.json"
  az ad app show --id "$client_id" --query web.redirectUris --output json > "$current_file"
  python3 - "$current_file" "$patch_file" "$@" <<'PY'
import json, sys
existing=json.load(open(sys.argv[1])) or []
uris=list(dict.fromkeys(existing+sys.argv[3:]))
json.dump({'web':{'redirectUris':uris}},open(sys.argv[2],'w'))
PY
  az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$object_id" \
    --body "@$patch_file" --output none
  az ad app show --id "$client_id" --query web.redirectUris --output json
}
add_callbacks "$TASK_LOGIN_ID" "$TASK_BASE_URL/signin-oidc"
add_callbacks "$TASK_READER_ID" \
  "$TASK_BASE_URL/auth/m365-consent/callback" \
  "$TASK_BASE_URL/auth/m365-consent/identity-callback"
```

Use Web callbacks with authorization-code flow. Do not enable implicit flow to repair sign-in. The Directory Reader, rather than the login app, owns both consent callbacks. [Microsoft redirect-URI guidance](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-redirect-uri).

### 3.4 Request and consent exact application roles

**Admin workstation / Bash — WRITE to requested permissions.** Resolve permission GUIDs from the tenant resource service principal instead of copying a GUID for the wrong resource or permission type.

```bash
TASK_GRAPH_RESOURCE='00000003-0000-0000-c000-000000000000'
TASK_SHAREPOINT_RESOURCE='00000003-0000-0ff1-ce00-000000000000'
request_role() {
  local client_id="$1" resource="$2" role="$3" role_id
  role_id=$(az ad sp show --id "$resource" \
    --query "appRoles[?value=='$role' && isEnabled && contains(allowedMemberTypes, 'Application')].id | [0]" \
    --output tsv)
  test -n "$role_id"
  az ad app permission add --id "$client_id" --api "$resource" \
    --api-permissions "$role_id=Role" --output none
}
request_role "$TASK_READER_ID" "$TASK_GRAPH_RESOURCE" 'User.Read.All'
request_role "$TASK_DOCUMENT_ID" "$TASK_GRAPH_RESOURCE" 'Sites.Selected'
# Optional, only after their reviewed feature decisions:
# request_role "$TASK_CREATE_ID" "$TASK_GRAPH_RESOURCE" 'User.Create'
# request_role "$TASK_INVITE_ID" "$TASK_GRAPH_RESOURCE" 'User.Invite.All'
# request_role "$TASK_GROUP_ID" "$TASK_GRAPH_RESOURCE" 'GroupMember.ReadWrite.All'
# request_role "$TASK_CLIENT_SITES_ID" "$TASK_GRAPH_RESOURCE" 'Sites.FullControl.All'
# request_role "$TASK_CLIENT_SITES_ID" "$TASK_SHAREPOINT_RESOURCE" 'Sites.FullControl.All'
# Mail permission follows step 11's compatibility decision, not a default grant.
az ad app permission list --id "$TASK_READER_ID" --output json
az ad app permission list --id "$TASK_DOCUMENT_ID" --output json
```

**WRITE / administrator approval:** after inspecting each app's complete requested-role list, the authorized administrator runs `az ad app permission admin-consent --id "$TASK_READER_ID" --output none` and separately for the document app and each approved optional app. This command consents the app's declared permissions, so do not run it on an app with unexplained extras. `permission grant` is for delegated scopes, not application-role consent. [Permission CLI](https://learn.microsoft.com/en-us/cli/azure/ad/app/permission?view=azure-cli-latest).

**READ / actual grants:** capture each runtime service principal's `appRoleAssignments`, not just its requested manifest:

```bash
TASK_SP_OBJECT_ID=$(az ad sp show --id "$TASK_DOCUMENT_ID" --query id --output tsv)
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/$TASK_SP_OBJECT_ID/appRoleAssignments" \
  --query 'value[].{resource:resourceDisplayName,resourceId:resourceId,roleId:appRoleId}' --output json
```

Resolve each `roleId` against that resource's `appRoles`. Follow `@odata.nextLink` if present when examining a reused app. Requested permissions, consented app roles, site grants and AuditSphere's verified state are separate evidence.

## 4. Create and install credentials

### 4.1 RSA certificate per capability

**Deployment host / Linux Bash — WRITE private files.** Generate on the credential host, once per approved capability. `1654` is the checked-in container image's UID; verify it against the release image before setting ownership. Protect parent directories while allowing that process to traverse/read its own mounts. Never overwrite existing credentials with these creation commands.

For `client-sites`, first prepare and apply step 5's API mount isolation and verify the actual mount boundary. Do not stage its privileged key beneath a parent directory already mounted into a running API. On a new installation, keep application services stopped until the reviewed mounts are ready.

```bash
set -euo pipefail
set +x
umask 077
TASK_CREDENTIAL_ROOT='/opt/auditsphere/storage/m365-credentials'
TASK_CAPABILITY='directory-reader'   # repeat with each approved suffix
TASK_CONTAINER_UID=1654
sudo install -d -o root -g "$TASK_CONTAINER_UID" -m 0750 "$TASK_CREDENTIAL_ROOT"
TASK_CERT_DIR="$TASK_CREDENTIAL_ROOT/$TASK_CAPABILITY"
sudo install -d -o root -g "$TASK_CONTAINER_UID" -m 0750 "$TASK_CERT_DIR"
sudo test ! -e "$TASK_CERT_DIR/private-key.pem"
sudo test ! -e "$TASK_CERT_DIR/certificate.pem"
sudo openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
  -subj "/CN=AuditSphere-$TASK_CAPABILITY" \
  -keyout "$TASK_CERT_DIR/private-key.pem" -out "$TASK_CERT_DIR/certificate.pem"
sudo chown root:"$TASK_CONTAINER_UID" "$TASK_CERT_DIR/"*.pem
sudo chmod 0640 "$TASK_CERT_DIR/"*.pem
sudo openssl x509 -in "$TASK_CERT_DIR/certificate.pem" -noout -dates -fingerprint -sha256
sudo openssl x509 -in "$TASK_CERT_DIR/certificate.pem" -pubkey -noout | openssl pkey -pubin -outform DER | openssl dgst -sha256
sudo openssl pkey -in "$TASK_CERT_DIR/private-key.pem" -pubout -outform DER | openssl dgst -sha256
# The two public-key digests must match; neither command prints private-key contents.
```

The runtime reads a separate unencrypted PEM key file from a restricted read-only mount; unattended password prompts/PFX paths are not supported by these options. `-nodes` creates that unencrypted private key; file permissions, host controls and secret custody are therefore required. Never upload it to Entra. This service certificate is an authentication credential, not a legally accepted report-signing certificate.

Transfer **only** `certificate.pem` to the admin workstation. **Admin workstation / Bash — WRITE public credential:** set `TASK_CAPABILITY_APP_ID` to that capability's client ID and `TASK_PUBLIC_CERT` to its public PEM path, then run:

```bash
az ad app credential reset --id "$TASK_CAPABILITY_APP_ID" \
  --cert "@$TASK_PUBLIC_CERT" --append --display-name 'AuditSphere capability certificate' \
  --only-show-errors --output none
az ad app credential list --id "$TASK_CAPABILITY_APP_ID" --cert \
  --query '[].{keyId:keyId,displayName:displayName,start:startDateTime,end:endDateTime}' --output json
```

Use `--append`: omitting it can replace existing credentials. Check Entra credential validity and public certificate expiry. Rotation is in step 16. [Credential CLI](https://learn.microsoft.com/en-us/cli/azure/ad/app/credential?view=azure-cli-latest).

On an uncertain credential-append result, inspect the certificate credential list and reconcile the exact public certificate fingerprint/key metadata before repeating it. A retry can append another credential rather than recover the first response.

### 4.2 Confidential login secret

**Admin workstation / Bash — WRITE secret.** The login app currently uses `Identity:ClientSecret`. Generate into a protected file rather than terminal output. A certificate for the document app cannot substitute for this secret.

```bash
test ! -e "$TASK_PRIVATE_DIR/login-credential.json"
az ad app credential reset --id "$TASK_LOGIN_ID" --append \
  --display-name 'AuditSphere login credential' --years 1 --only-show-errors \
  --output json > "$TASK_PRIVATE_DIR/login-credential.json"
chmod 0600 "$TASK_PRIVATE_DIR/login-credential.json"
```

Store the password immediately in the approved secret manager; its value is only returned on creation. Transfer it to the deployment host through an approved encrypted channel or enter it at a hidden terminal prompt. Do not supply a real secret in a documented command argument, Git file, chat, transcript, debug output or an expanded `docker compose config` listing. Keep the credential key ID and expiry in the rotation record.

If the password-append request times out or its secret response is lost, inspect the password credential metadata before retrying. The lost password cannot be read back. Have the customer operator identify and retire that exact orphan key ID, then create one reviewed replacement; do not accumulate unknown active secrets or overwrite the existing private response file.

After custody and a successful fresh login are verified, remove the temporary `login-credential.json` file from the admin workstation; keep only the protected runtime/secret-manager copy and non-secret rotation metadata.

## 5. Configure the customer host

Copy the [complete Hostinger variable template](../../infra/hostinger-acceptance/.env.example) to a private deployment `.env`, outside source control. It inventories every interpolated variable in the existing Compose file; it does **not** make that fixed-host Acceptance Compose reusable for production.

Before using the composition for another approved synthetic customer environment, prepare a reviewed copy/override of the fixed public URL, both consent callbacks, guest redirect, proxy/TLS/DNS, timezone, project/network names, host ports, storage and credential paths. `Application:PublicBaseUrl`, callback URIs and proxy trust are not all exposed as `.env` substitutions. Setting an undeclared `PUBLIC_BASE_URL` variable has no effect. Inspect the [configuration reference](auditsphere-operations-reference-microsoft365-configuration-current.md) and actual final Compose model.

For the application-facing URLs, save this privately as `customer-host.override.yaml` after replacing every example hostname and reviewing the business timezone. It does not change DNS, Caddy, network, port or storage isolation; those must match the separately approved infrastructure plan. Entra registrations must contain the same resulting URIs.

```yaml
services:
  api:
    environment:
      Application__PublicBaseUrl: https://audit.customer.example
      Application__TimeZone: Asia/Qatar
      TenantConsent__RedirectUri: https://audit.customer.example/auth/m365-consent/callback
      TenantConsent__IdentityRedirectUri: https://audit.customer.example/auth/m365-consent/identity-callback
      TenantAdministration__GuestRedirectUrl: https://audit.customer.example/auth/landing
```

**Required credential isolation:** the base Compose API mounts the entire parent M365 directory. That includes a `client-sites` key if it is placed beneath the parent, despite the API not having a configured path to use it. Copy/review the [isolation override example](../../infra/hostinger-acceptance/m365-isolation.override.example.yaml) as private `m365-isolation.override.yaml` in the deployment directory. It replaces the API credential mount with six individual capability mounts and excludes the privileged provisioner. Docker Compose 2.24.4+ is required for `!override`. Create protected empty host directories for disabled capability mounts, or omit those individual mounts in the reviewed copy. Inspect the resulting model and running mount destinations before placing the provisioner key. [Compose replacement semantics](https://docs.docker.com/reference/compose-file/merge/#replace-value).

For the remaining host Bash examples, define this array once; include any additional reviewed customer-host override in the same array. Use that identical file list for validation, recreation and later rotations.

```bash
# New deployment: create empty protected mount directories for unused API capabilities.
# Adapt the host root and verify the image UID/GID before running.
TASK_CREDENTIAL_ROOT='/opt/auditsphere/storage/m365-credentials'
TASK_CONTAINER_UID=1654
for TASK_CAPABILITY in directory-reader selected-site provisioning guest-invitation group-membership outbound-mail; do
  if [ ! -d "$TASK_CREDENTIAL_ROOT/$TASK_CAPABILITY" ]; then
    sudo install -d -o root -g "$TASK_CONTAINER_UID" -m 0750 "$TASK_CREDENTIAL_ROOT/$TASK_CAPABILITY"
  fi
done
test -f m365-isolation.override.yaml
test -f customer-host.override.yaml
TASK_COMPOSE=(docker compose -f compose.yaml -f customer-host.override.yaml -f m365-isolation.override.yaml)
"${TASK_COMPOSE[@]}" --profile m365-mail --profile m365-pbc config -q
```

**Deployment host / Bash — WRITE one protected setting.** Run from the private deployment directory. This updates a single exact existing key, refuses duplicate/missing keys and never prints the value. The example supports the login secret or database password only: pass the non-secret key name as the argument. It refuses newlines and apostrophes rather than silently altering a secret. Use an approved secret-store/escaping procedure for unsupported values or provider-specific secrets.

```bash
python3 - IDENTITY_CLIENT_SECRET <<'PY'
from getpass import getpass
from pathlib import Path
import os, sys, tempfile
p=Path('.env')
key=sys.argv[1]
assert key in {'IDENTITY_CLIENT_SECRET','POSTGRES_PASSWORD'}
value=getpass(key+' (hidden): ')
assert value and all(c not in value for c in "\r\n'"), 'Empty or unsupported input'
lines=p.read_text().splitlines()
assert sum(x.startswith(key+'=') for x in lines)==1, 'Key must occur exactly once'
lines=[key+"='"+value+"'" if x.startswith(key+'=') else x for x in lines]
fd,name=tempfile.mkstemp(dir=p.parent,prefix='.env-write-')
try:
    os.fchmod(fd,0o600)
    with os.fdopen(fd,'w') as f: f.write('\n'.join(lines)+'\n')
    os.replace(name,p)
finally:
    if os.path.exists(name): os.unlink(name)
print('SETTING_UPDATED')
PY
"${TASK_COMPOSE[@]}" --profile m365-mail --profile m365-pbc config -q
```

Generate a random one-use installation proof through the approved secret process, configure its SHA-256 hex hash at `BOOTSTRAP_PROOF_HASH`, and supply the proof privately to the nominated administrator. Do not use a short/default proof. The hash is SHA-256 over the exact UTF-8 proof with no trailing newline. `INITIAL_ADMIN_OBJECT_ID` is the Entra object ID; `CLIENT_SITES_ADMINISTRATOR_USER_ID` later means the **different local AuditSphere user ID**.

**Deployment host / Linux Bash — WRITE random proof and hash without displaying the proof.** Run as the approved deployment-file owner from the private Compose directory; this example uses an operator-owned `0700` private directory. Keep the plaintext file only for secure handoff to the nominated administrator.

```bash
install -d -m 0700 /opt/auditsphere/private
python3 - <<'PY'
from pathlib import Path
import hashlib, os, secrets, tempfile
p=Path('.env')
lines=p.read_text().splitlines()
key='BOOTSTRAP_PROOF_HASH'
assert sum(x.startswith(key+'=') for x in lines)==1
proof=secrets.token_urlsafe(32)
proof_path=Path('/opt/auditsphere/private/bootstrap-proof.txt')
fd=os.open(proof_path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
with os.fdopen(fd,'w') as f: f.write(proof)
digest=hashlib.sha256(proof.encode('utf-8')).hexdigest()
lines=[key+'='+digest if x.startswith(key+'=') else x for x in lines]
fd,name=tempfile.mkstemp(dir=p.parent,prefix='.env-write-')
try:
    os.fchmod(fd,0o600)
    with os.fdopen(fd,'w') as f: f.write('\n'.join(lines)+'\n')
    os.replace(name,p)
finally:
    if os.path.exists(name): os.unlink(name)
print('PROOF_STAGED_PRIVATELY_AND_HASH_CONFIGURED')
PY
```

Read/transfer the proof through the approved private operator/secret-manager channel, not captured CLI output. If a proof file already exists, reconcile its current installation use; do not overwrite it or issue a new proof just to retry a consumed setup.

Deploy the approved image and migrations through the normal backup/rehearsal/release procedure. Recreate only services whose reviewed configuration changed. `restart` does not reload changed Compose environment variables; `up -d --no-deps <service>` recreates when needed. Run `curl --fail --silent --show-error https://audit.customer.example/health/ready` after replacing the example host. A 200 response proves readiness, not Microsoft acceptance.

<a id="one-time-owner-onboarding"></a>

## 6. Bind the initial AuditSphere administrator

First verify the exact enabled same-tenant member. **Admin workstation / Bash — READ:** explicitly select `accountEnabled` and `userType`; Graph does not return every property in a default user response.

```bash
TASK_INITIAL_ADMIN_ID=$(az ad user show --id 'approved-admin@customer.example' --query id --output tsv)
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/users/$TASK_INITIAL_ADMIN_ID?\$select=id,userPrincipalName,userType,accountEnabled" \
  --output json > "$TASK_PRIVATE_DIR/initial-administrator.json"
python3 - "$TASK_PRIVATE_DIR/initial-administrator.json" <<'PY'
import json,sys,uuid
u=json.load(open(sys.argv[1]))
uuid.UUID(u['id'])
assert u.get('accountEnabled') is True and u.get('userType')=='Member', 'Expected enabled tenant member'
print('INITIAL_ADMINISTRATOR_MEMBER_CHECK_PASSED')
PY
```

1. Record that exact object ID and enabled-member check. The bootstrap code binds configured `tid`/`oid` and proof; it does not itself perform this directory membership/enabled-state lookup. An email/display-name match is not authority.
2. Configure the exact tenant/object ID, firm/installation IDs and private proof hash before first sign-in.
3. Open the application's `/auth/sign-in` flow and `/setup/microsoft365` setup page under the deployed Angular route configuration. The approved human completes Microsoft authentication, enters the private installation proof and reviews the local Administrator assignment.
4. Complete the fresh sign-in requested after binding so the session has its current epoch. Confirm Administration and Operations use the new local firm-wide grant.
   In that authenticated browser session, read `/api/ui/session` and record its `userId` as the **local** administrator ID for `CLIENT_SITES_ADMINISTRATOR_USER_ID`; confirm it belongs to this approved identity and firm. Do not reuse the Entra object ID or export session cookies to a CLI.
5. Retire the plaintext proof according to custody policy once single-use bootstrap is recorded. Keep its secret reference/consumption receipt rather than the proof value.

The public API implements authenticated `/api/setup/session` and CSRF-protected `/api/setup/bootstrap`. Do not bypass the flow using SQL or invent an unattended admin-binding CLI. A scripted HTTP client would need the actual OIDC session, CSRF token, reviewed input and explicit identity; the normal built-in browser path is the supported operator procedure. [Installation endpoint source](../../src/AuditSphereOps.Api/ApiHost.Installation.cs).

## 7. Record AuditSphere's consent session

Enable Directory Reader and Tenant Consent only with matching ready credentials and correct callbacks. In the API, `TenantConsent:ClientId` and `DirectoryReader:ClientId` identify the **same** separate reader app. Keep login, reader and document IDs distinct.

1. In **Administration → Microsoft 365 → Tenant connection**, prepare/review the matching setup draft.
2. Select **Connect Microsoft 365 tenant**, complete the administrator consent screen, and return through the configured callback.
3. Complete the subsequent nonce-bound administrator identity sign-in. Portal consent by itself does not create AuditSphere's trusted consent attempt.
4. Select **Verify all enabled capabilities** and **Refresh persisted status**. Retain the exact tenant, app IDs, connection/draft revision, diagnostic, observation time and current verified/blocked state.
5. Reverify when stale; the default freshness window is 24 hours. A token or old consent receipt is not a current capability result.

The consent callback, state and identity leg belong to the API. Human setup, selected resource draft, reviewed folder template and final activation remain separate steps. Saved Mail/Records labels are draft configuration only. CLI cannot truthfully set a verification row by writing `VERIFIED` into the database. See [consent service](../../src/AuditSphereOps.Application/Microsoft365/TenantConsentService.cs) and [verification source](../../src/AuditSphereOps.Application/Microsoft365/SelectedSiteBoundaryVerificationService.cs).

## 8. Resolve and grant the selected SharePoint site

Use an existing approved site and library. If absent, the customer SharePoint administrator creates the reviewed synthetic acceptance sites/libraries using the supported admin center or their approved administration tool. AuditSphere's dedicated-client provider owns later automatic client-site creation; do not create parallel client sites manually to bypass its ownership markers/idempotency.

### 8.1 Temporary delegated grantor

An exact-site application grant cannot be created by a runtime `Sites.Selected` token. This step needs a separately approved customer-operated grantor. Reuse an approved administration tool where permitted, or create a temporary single-tenant public client. Delegated `Sites.FullControl.All` is broad during that session; obtain the customer's exact exception first. Never add that permission to the login/reader/document runtime app. The signed-in grantor must have SharePoint Administrator or higher and membership of the target site. [Site permission API](https://learn.microsoft.com/en-us/graph/api/site-post-permissions?view=graph-rest-1.0).

**Admin workstation / Bash — WRITE temporary app and requested scope.** Use the `ensure_app` helper from step 3. Do not add a secret or certificate.

```bash
TASK_GRANTOR_ID=$(ensure_app "$TASK_PREFIX-temporary-site-grantor")
az ad app update --id "$TASK_GRANTOR_ID" --is-fallback-public-client true \
  --public-client-redirect-uris 'http://localhost' --output none
TASK_SITE_SCOPE=$(az ad sp show --id "$TASK_GRAPH_RESOURCE" \
  --query "oauth2PermissionScopes[?value=='Sites.FullControl.All' && isEnabled].id | [0]" --output tsv)
test -n "$TASK_SITE_SCOPE"
az ad app permission add --id "$TASK_GRANTOR_ID" --api "$TASK_GRAPH_RESOURCE" \
  --api-permissions "$TASK_SITE_SCOPE=Scope" --output none
# The customer reviews and consents this exact temporary delegated scope.
az ad app permission list --id "$TASK_GRANTOR_ID" --output json
```

**Admin workstation / PowerShell 7 — interactive delegated session.** Install `Microsoft.Graph.Authentication` once with `Install-Module Microsoft.Graph.Authentication -Scope CurrentUser`. Supply the approved IDs; the human completes sign-in and consent.

```powershell
$ErrorActionPreference = 'Stop'
$TenantId = 'replace-with-customer-tenant-guid'
$GrantorClientId = 'replace-with-temporary-grantor-client-guid'
$DocumentClientId = 'replace-with-selected-site-client-guid'
$SiteHost = 'customer.sharepoint.com'
$SitePath = '/sites/ApprovedAuditWorkspace'
$LibraryName = 'Internal Workpapers'
Connect-MgGraph -TenantId $TenantId -ClientId $GrantorClientId `
  -Scopes 'Sites.FullControl.All' -UseDeviceAuthentication -ContextScope Process -NoWelcome
$Context = Get-MgContext
if ($Context.TenantId -ne $TenantId -or $Context.ClientId -ne $GrantorClientId) { throw 'Wrong grantor context' }
$Site = Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/sites/${SiteHost}:${SitePath}" -OutputType PSObject
$Drives = Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/sites/$($Site.id)/drives" -OutputType PSObject
$DriveRows = @($Drives.value)
while ($Drives.'@odata.nextLink') {
  $Next = [uri]$Drives.'@odata.nextLink'
  if ($Next.Scheme -ne 'https' -or $Next.Host -ne 'graph.microsoft.com') { throw 'Unexpected continuation' }
  $Drives = Invoke-MgGraphRequest -Method GET -Uri $Next.AbsoluteUri -OutputType PSObject
  $DriveRows += @($Drives.value)
}
$Matches = @($DriveRows | Where-Object { $_.name -ceq $LibraryName })
if ($Matches.Count -ne 1) { throw 'Review the exact library; no unique match' }
$Drive = $Matches[0]
$Root = Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/drives/$($Drive.id)/root" -OutputType PSObject
[pscustomobject]@{SiteUrl=$Site.webUrl;SiteId=$Site.id;DriveId=$Drive.id;RootFolderId=$Root.id}
```

For a deliberately scoped subfolder, resolve that exact folder and save its ID instead of the drive root. Inspect and record both approved and unrelated-control site IDs while using the administrator session; record no control-site grant. [Site lookup](https://learn.microsoft.com/en-us/graph/api/site-get?view=graph-rest-1.0), [library enumeration](https://learn.microsoft.com/en-us/graph/api/drive-list?view=graph-rest-1.0), [drive item lookup](https://learn.microsoft.com/en-us/graph/api/driveitem-get?view=graph-rest-1.0).

### 8.2 Apply the exact write grant

**Same PowerShell grantor session — READ then WRITE only if absent.** Inspect all permission pages first. Do not overwrite an existing broader/different grant automatically.

```powershell
$PermissionPage = Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/sites/$($Site.id)/permissions" -OutputType PSObject
$PermissionRows = @($PermissionPage.value)
while ($PermissionPage.'@odata.nextLink') {
  $Next = [uri]$PermissionPage.'@odata.nextLink'
  if ($Next.Scheme -ne 'https' -or $Next.Host -ne 'graph.microsoft.com') { throw 'Unexpected continuation' }
  $PermissionPage = Invoke-MgGraphRequest -Method GET -Uri $Next.AbsoluteUri -OutputType PSObject
  $PermissionRows += @($PermissionPage.value)
}
$Existing = @($PermissionRows | Where-Object {
  $Ids = @($_.grantedToIdentitiesV2) + @($_.grantedToIdentities)
  @($Ids | Where-Object { $_.application.id -eq $DocumentClientId }).Count -gt 0
})
if ($Existing.Count -gt 1) { throw 'Multiple runtime grants require review' }
if ($Existing.Count -eq 1) {
  if (@($Existing[0].roles).Count -ne 1 -or $Existing[0].roles[0] -ne 'write') {
    throw 'Existing role differs from approved write grant'
  }
  $Grant = $Existing[0]
} else {
  $Body = @{roles=@('write');grantedToIdentities=@(@{application=@{
    id=$DocumentClientId;displayName='AuditSphere selected-site documents'
  }})} | ConvertTo-Json -Depth 6
  $Grant = Invoke-MgGraphRequest -Method POST `
    -Uri "https://graph.microsoft.com/v1.0/sites/$($Site.id)/permissions" `
    -Body $Body -ContentType 'application/json' -OutputType PSObject
}
$Grant | Select-Object id,roles
Disconnect-MgGraph | Out-Null
```

Retain the permission ID and exact site/app/role readback. On a timeout after POST, reread/reconcile before retrying. Delete only the temporary grantor after preserving the receipt; the runtime's site permission must remain.

**Admin workstation / Bash — WRITE cleanup of the exact temporary identity:**

```bash
TASK_GRANTOR_SP=$(az ad sp show --id "$TASK_GRANTOR_ID" --query id --output tsv)
az ad sp delete --id "$TASK_GRANTOR_SP"
az ad app delete --id "$TASK_GRANTOR_ID"
az ad sp list --filter "appId eq '$TASK_GRANTOR_ID'" --all --query 'length(@)' --output tsv
az ad app list --app-id "$TASK_GRANTOR_ID" --all --query 'length(@)' --output tsv
# Both counts must be 0; errors are not proof of removal.
```

Confirm its delegated consent is removed with the service principal; retain cleanup evidence and close the Graph session. If cleanup fails, record the outstanding privileged identity and have the customer administrator remove it before handover.

## 9. Verify selected resource access

Set the dedicated document app, certificate paths and negative-control URL privately. The saved connection's `CredentialReference` must match `SelectedSite:CredentialReference` exactly; copy the actual configured value, rather than assume the historical `configuration:SelectedSite` or Hostinger default is universal.

In the current Angular **Selected SharePoint workspace** panel, save the exact site URL, site ID, drive ID, root folder ID and approved access profile with the current draft revision. Complete the trusted resource verification after consent. Require positive reads of all saved resources and **HTTP 403** for the unrelated same-tenant site. A missing site/404, wrong tenant, network failure or admin-session success is not the required negative control. This Application path stores the trusted observation; raw CLI observations alone do not activate the connection. Review/approve the exact folder template and activate only the matching verified revision.

**Optional authorized disposable file check / credential host PowerShell 7 — WRITE, READ, DELETE.** Use only an approved empty synthetic library/root, a unique filename and the runtime certificate. This must not run under the administrator grantor. The sample verifies file bytes; use the built-in resource probe for the required control denial.

```powershell
$ErrorActionPreference = 'Stop'
$TenantId = 'replace-with-customer-tenant-guid'
$DocumentClientId = 'replace-with-selected-site-client-guid'
$DriveId = 'replace-with-approved-drive-id'
$RootId = 'replace-with-approved-synthetic-root-id'
$Certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile(
  '/run/auditsphere/m365/selected-site/certificate.pem',
  '/run/auditsphere/m365/selected-site/private-key.pem')
$ProbeName = 'AuditSphere-verification-' + [guid]::NewGuid().ToString('N') + '.txt'
$Payload = 'Synthetic AuditSphere verification ' + $ProbeName
$Created = $null
try {
  Connect-MgGraph -TenantId $TenantId -ClientId $DocumentClientId -Certificate $Certificate `
    -ContextScope Process -NoWelcome
  $Created = Invoke-MgGraphRequest -Method PUT `
    -Uri "https://graph.microsoft.com/v1.0/drives/${DriveId}/items/${RootId}:/${ProbeName}:/content" `
    -Body ([Text.Encoding]::UTF8.GetBytes($Payload)) -ContentType 'text/plain' -OutputType PSObject
  $Read = Invoke-MgGraphRequest -Method GET `
    -Uri "https://graph.microsoft.com/v1.0/drives/${DriveId}/items/$($Created.id)/content" `
    -OutputType HttpResponseMessage
  try {
    $Bytes = $Read.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    if ([Convert]::ToBase64String($Bytes) -ne [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Payload))) {
      throw 'Readback mismatch'
    }
  } finally { $Read.Dispose() }
} finally {
  if ($Created -and $Created.id) {
    Invoke-MgGraphRequest -Method DELETE `
      -Uri "https://graph.microsoft.com/v1.0/drives/${DriveId}/items/$($Created.id)" | Out-Null
  }
  Disconnect-MgGraph | Out-Null
  $Certificate.Dispose()
}
```

Run on an approved administration host using its actual protected mount paths; the container paths above are examples, not permission to copy the private key to a workstation. Confirm follow-up GET returns 404 for that exact deleted item. If upload outcome is uncertain and no item ID was returned, search/reconcile this exact unique filename before retrying or cleanup; do not create another verification file blindly. Record the upload/readback/delete HTTP outcomes and timestamps without file content or tokens. [Graph upload](https://learn.microsoft.com/en-us/graph/api/driveitem-put-content?view=graph-rest-1.0), [content read](https://learn.microsoft.com/en-us/graph/api/driveitem-get-content?view=graph-rest-1.0), [delete](https://learn.microsoft.com/en-us/graph/api/driveitem-delete?view=graph-rest-1.0).

## 10. Configure dedicated client-site provisioning

The default is **one site per new eligible client**, not per engagement. Annual engagements use approved folder templates within that site. Existing clients before the approved UTC boundary retain their repositories. A missing boundary blocks provisioning; it must never default to historical clients or silently use shared-site fallback for rollout clients.

1. Obtain the customer's approval for the standing site-provisioning policy, staff Full Control model and isolated provisioner's Graph **and SharePoint** `Sites.FullControl.All` exception. These are tenant-wide application grants. `SiteHost`, ownership markers and worker isolation constrain AuditSphere's configured behavior; Microsoft does not narrow those broad roles to a single site just because a hostname is configured.
2. Create the distinct `client-sites` app and certificate; consent one application role on each resource using step 3. This is the only approved broad application exception and never belongs on the document app.
3. Configure `ClientSites:SiteHost` as `customer.sharepoint.com`, without scheme/path. The provider uses supported SharePoint `SPSiteManager` APIs and certificate authentication. Do not replace it with a second Graph beta/manual provisioning process. [SharePoint admin authorization](https://learn.microsoft.com/en-us/sharepoint/dev/sp-add-ins/sharepoint-admin-apis-authentication-and-authorization), [site creation API](https://learn.microsoft.com/en-us/sharepoint/dev/apis/site-creation-rest).
4. Set the enabled same-tenant member custodian object ID, the existing local firm-wide AuditSphere administrator ID, and exact fixed `ClientsCreatedAfter` UTC timestamp. Configure the same rollout values for API, PBC and provisioner; the saved cutoff persists and cannot be changed silently.
5. Mount only the provisioner's certificate plus the separate Directory Reader certificate in the isolated `client-sites` worker. Its selected-site document-worker ID must identify a different app. Configure all other enabled app IDs so identity-separation checks can evaluate them.
6. Check Administration worker heartbeat, readiness, rollout state and errors before admitting new clients. A running process or READY configuration is not a real site-creation receipt.
7. In the approved synthetic acceptance cohort, create one eligible client and confirm one intent/site, a second client with a distinct site, and a second engagement reusing the first site. Check ownership marker, exact document-worker grant, approved folders and staff readback. Exercise removal/reconciliation through the existing reviewed flow.

Assigned audit staff receive Full Control over the whole client site under ADR-0006, including sibling engagements, sharing and deletion. Local engagement scopes remain enforced in AuditSphere but do not undo Microsoft's site-wide rights. Managed-group revocation is asynchronous; direct grants/sharing require separate administrator review. The site is working storage. Authoritative issued artifacts remain in the independent release/archive store; a read-only SharePoint secondary archive requires its own grants, hash and staff-denial verification. [Archive ADR](../architecture/adr/auditsphere-architecture-adr-0009-archive-authority-release-store-approved.md).

## 11. Configure Exchange Online mail

### 11.1 Decide the supported authorization path first

Microsoft's current recommendation for new Exchange application access is **Application RBAC**. Existing Application Access Policies (AAP) are legacy. Crucially, Exchange RBAC grants and Entra application grants are additive: a scoped Exchange role does not restrict a separately consented organization-wide `Mail.Send` grant. [Exchange RBAC](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac), [legacy AAP](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies).

AuditSphere's API verifier currently insists on a Graph token with exactly `Mail.Send`. Its mail worker's sender is a separate adapter and does not perform that same claim check. A role-less RBAC-only credential may send through the adapter, but it cannot currently satisfy the application's strict Outbound Mail capability verification. This integration is **not yet a verified RBAC-only deployment path**. See [token guard](../../src/AuditSphereOps.Infrastructure/Providers/GraphCapabilityCredential.cs), [API composition](../../src/AuditSphereOps.Api/Authentication/TenantAdministrationComposition.cs) and [mail sender](../../src/AuditSphereOps.Infrastructure/Providers/GraphPbcMailSender.cs).

| Customer state | Action |
| --- | --- |
| Existing approved `Mail.Send` + AAP installation | Preserve/reverify the exact single-mailbox restriction; plan a reviewed migration |
| Fresh customer wants Microsoft's recommended RBAC path | Prepare scoped RBAC, keep activation gated until the capability verification composition supports and proves it |
| Unrestricted Entra `Mail.Send` app | Keep mail disabled; do not treat RBAC checks as proof that the Entra grant is restricted |
| Approved SMTP/Resend alternative | Read step 13's provider, environment and attachment limitations |

### 11.2 Inspect mailbox and current restrictions

**Admin workstation / PowerShell 7 — interactive sign-in and READ.** Install ExchangeOnlineManagement once, then use an existing Exchange administrator. Supply a real unrelated synthetic mailbox for denial checks.

On a headless Linux host, use `Connect-ExchangeOnline -Device -ShowBanner:$false` instead of the username/browser form below; the human completes the displayed device-code flow. Keep the same PowerShell process open through inspection, reviewed setup and checks so each short command does not require another login. Use a fresh process when switching customer tenants. [Exchange connection methods](https://learn.microsoft.com/en-us/powershell/exchange/connect-to-exchange-online-powershell).

```powershell
$ErrorActionPreference = 'Stop'
Install-Module ExchangeOnlineManagement -Scope CurrentUser
Import-Module ExchangeOnlineManagement
Connect-ExchangeOnline -UserPrincipalName 'exchange-admin@customer.example' -ShowBanner:$false
$ExpectedTenantId = 'replace-with-customer-tenant-guid'
$Connections = @(Get-ConnectionInformation | Where-Object { $_.State -eq 'Connected' })
if ($Connections.Count -ne 1 -or $Connections[0].TenantID -ne $ExpectedTenantId) {
  throw 'Expected one Exchange connection to the approved customer tenant; use a fresh PowerShell process'
}
$Sender = 'audit-notifications@customer.example'
$DeniedSender = 'unrelated-synthetic@customer.example'
$MailClientId = 'replace-with-mail-application-client-guid'
$MailServicePrincipalId = 'replace-with-enterprise-application-object-guid'
Get-EXOMailbox -Identity $Sender | Select-Object ExternalDirectoryObjectId,PrimarySmtpAddress,RecipientTypeDetails
Get-ApplicationAccessPolicy | Where-Object { $_.AppId -eq $MailClientId } | Format-List
Test-ApplicationAccessPolicy -AppId $MailClientId -Identity $Sender
Test-ApplicationAccessPolicy -AppId $MailClientId -Identity $DeniedSender
```

Confirm the sender is a functioning Exchange Online mailbox with an appropriate license/mailbox entitlement and customer-approved sender address. A verified DNS domain or Entra user by itself is not a mailbox. Do not create or license customer mailboxes implicitly.

The Exchange connection check uses [Get-ConnectionInformation](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-connectioninformation?view=exchange-ps), not an inference from the admin UPN or application name.

If the customer explicitly chooses a new **shared mailbox**, has confirmed the domain and applicable licensing/capacity/retention policy, and has confirmed that this exact address is absent, the Exchange administrator can use the following **WRITE** example. This is an optional mailbox decision, not a default action; keep its direct user sign-in blocked and use the app's reviewed sender authorization.

```powershell
New-Mailbox -Shared -Name 'AuditSphere Notifications' -DisplayName 'AuditSphere Notifications' `
  -Alias 'audit-notifications' -PrimarySmtpAddress $Sender
Get-EXOMailbox -Identity $Sender | Select-Object ExternalDirectoryObjectId,PrimarySmtpAddress,RecipientTypeDetails
```

For a user mailbox, the customer must assign an appropriate Exchange license through its approved account/licensing process; `User.Create` alone does not create a licensed mailbox. [Shared-mailbox requirements](https://learn.microsoft.com/en-us/microsoft-365/admin/email/about-shared-mailboxes?view=o365-worldwide), [New-Mailbox](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/new-mailbox?view=exchange-ps).

For an existing AAP, confirm its scope is a mail-enabled security group whose actual membership is only the approved sender. Use `Get-DistributionGroup -Identity '<existing-scope-group>'` and `Get-DistributionGroupMember -Identity '<existing-scope-group>' -ResultSize Unlimited`. The expected results are **Granted** for the sender and **Denied** for the unrelated mailbox. Policy propagation can outlast the administrative test; require provider observation as well. Do not add a second policy without reviewing the full existing policy set. [AAP testing and propagation](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies).

### 11.3 RBAC preparation and migration commands

**WRITE / reviewed future migration packet, not current activation instructions.** The customer Exchange administrator can prepare an exact sender scope as follows after checking these unique names are unused. The enterprise-application object ID is required, not the app-registration object ID.

```powershell
$ScopeName = 'AuditSphere-Sender-Only'
$AssignmentName = 'AuditSphere-Send-Only'
New-ServicePrincipal -AppId $MailClientId -ObjectId $MailServicePrincipalId `
  -DisplayName 'AuditSphere outbound mail'
New-ManagementScope -Name $ScopeName -RecipientRestrictionFilter "PrimarySmtpAddress -eq '$Sender'"
Get-Recipient -RecipientPreviewFilter (Get-ManagementScope -Identity $ScopeName).RecipientFilter `
  -ResultSize Unlimited | Select-Object PrimarySmtpAddress
New-ManagementRoleAssignment -Name $AssignmentName -App $MailServicePrincipalId `
  -Role 'Application Mail.Send' -CustomResourceScope $ScopeName
Test-ServicePrincipalAuthorization -Identity $MailServicePrincipalId -Resource $Sender
Test-ServicePrincipalAuthorization -Identity $MailServicePrincipalId -Resource $DeniedSender
```

Require the scope preview to contain exactly one intended mailbox, and `InScope=True/False` for the positive/negative pair. These test commands do **not** evaluate separate Entra grants. Before RBAC-only activation, a reviewed engineering/release change must support the application's permission verification, remove the exact Entra `Mail.Send` **app-role assignment** and its stale requested permission where appropriate, prove the actual runtime positive/negative checks, then retire the old AAP under an approved migration plan. Do not delete all app permissions or keep unrestricted Entra consent alongside scoped RBAC. Cache propagation may delay provider behavior; retain timestamps and retry only after inspecting state.

After a partial/uncertain RBAC setup, inspect `Get-ServicePrincipal -Identity $MailServicePrincipalId`, `Get-ManagementScope -Identity $ScopeName` and `Get-ManagementRoleAssignment -Identity $AssignmentName`. Compare the exact client/object IDs, filter, role and resource scope to the approved packet. Reuse a matching existing object; resume only the missing step. An authorization/network error is not evidence that an object is absent, and mismatched objects require review rather than overwrite or a differently named retry.

### 11.4 Current Graph configuration and real send

For a reviewed existing AAP-compatible acceptance deployment, use its distinct mail app/certificate, `OUTBOUND_MAIL_CLIENT_ID`, exact `OUTBOUND_MAIL_SENDER_MAILBOX` and `OUTBOUND_MAIL_ENABLED=true` for API capability verification. Enable the isolated mail worker separately through `m365-mail`. The API's Outbound Mail flag does not start a worker; starting a worker can dispatch queued messages even if that API flag is false.

Before starting the worker, review all current-firm queued commercial/PBC mail and recipients through Administration/Operations. Use one synthetic workflow and controlled recipient; do not bypass reviewed commercial sends with a raw Graph call to real clients. Verify sender acceptance and an unrelated-sender denial under the runtime identity. A denial probe must use only controlled fixtures approved for that purpose.

For a separately authorized **synthetic transport scope probe**, first have Exchange confirm that both the permitted sender and the unrelated synthetic sender really exist as mailboxes. On the credential host, use the mail app's certificate (not the delegated administrator). This PowerShell example sends one test from the approved sender and attempts a second from the approved negative fixture; the recipient is controlled. Unexpected success from the unrelated sender is an access failure that may deliver a second test message. Review that exact effect before running.

```powershell
$ErrorActionPreference = 'Stop'
$TenantId = 'replace-with-customer-tenant-guid'
$MailClientId = 'replace-with-mail-application-client-guid'
$Sender = 'audit-notifications@customer.example'
$DeniedSender = 'unrelated-synthetic@customer.example'
$TestRecipient = 'controlled-recipient@customer.example'
$Correlation = [guid]::NewGuid().ToString('D')
$PositiveClientRequestId = [guid]::NewGuid().ToString('D')
$NegativeClientRequestId = [guid]::NewGuid().ToString('D')
$Certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile(
  '/run/auditsphere/m365/outbound-mail/certificate.pem',
  '/run/auditsphere/m365/outbound-mail/private-key.pem')
$Positive = $null; $Negative = $null
try {
  Connect-MgGraph -TenantId $TenantId -ClientId $MailClientId -Certificate $Certificate `
    -ContextScope Process -NoWelcome
  $Body = @{message=@{subject=('Synthetic AuditSphere mail scope '+$Correlation);
    body=@{contentType='Text';content='Controlled transport verification only'};
    toRecipients=@(@{emailAddress=@{address=$TestRecipient}})};saveToSentItems=$true} | ConvertTo-Json -Depth 8
  $Headers = @{'client-request-id'=$PositiveClientRequestId;'return-client-request-id'='true'}
  $Positive = Invoke-MgGraphRequest -Method POST `
    -Uri "https://graph.microsoft.com/v1.0/users/$Sender/sendMail" -Body $Body `
    -ContentType 'application/json' -Headers $Headers -SkipHttpErrorCheck -OutputType HttpResponseMessage
  if ([int]$Positive.StatusCode -ne 202) { throw 'Approved sender was not accepted; scope probe is not verified' }
  $Headers = @{'client-request-id'=$NegativeClientRequestId;'return-client-request-id'='true'}
  $Negative = Invoke-MgGraphRequest -Method POST `
    -Uri "https://graph.microsoft.com/v1.0/users/$DeniedSender/sendMail" -Body $Body `
    -ContentType 'application/json' -Headers $Headers -SkipHttpErrorCheck -OutputType HttpResponseMessage
  $DeniedBody = $Negative.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
  if ([int]$Negative.StatusCode -ne 403 -or $DeniedBody.error.code -notin @('ErrorAccessDenied','AccessDenied','accessDenied')) {
    throw 'Expected mailbox-policy denial was not confirmed; stop activation'
  }
  $PositiveProviderRequestId = $null; $NegativeProviderRequestId = $null
  if ($Positive.Headers.Contains('request-id')) { $PositiveProviderRequestId = $Positive.Headers.GetValues('request-id') -join ',' }
  if ($Negative.Headers.Contains('request-id')) { $NegativeProviderRequestId = $Negative.Headers.GetValues('request-id') -join ',' }
  [pscustomobject]@{PositiveHttp=202;NegativeHttp=403;ErrorCode=$DeniedBody.error.code;CorrelationId=$Correlation;
    PositiveClientRequestId=$PositiveClientRequestId;NegativeClientRequestId=$NegativeClientRequestId;
    PositiveProviderRequestId=$PositiveProviderRequestId;NegativeProviderRequestId=$NegativeProviderRequestId}
} finally {
  if ($Positive) { $Positive.Dispose() }
  if ($Negative) { $Negative.Dispose() }
  Disconnect-MgGraph | Out-Null
  $Certificate.Dispose()
}
```

Use the real protected host paths when running outside a container; no private key is copied to another workstation. `-SkipHttpErrorCheck` is used only to inspect the expected refusal explicitly; every nonmatching status fails the probe. A missing mailbox/404, token failure, throttling or network error is not sender-scope denial. If the positive send becomes uncertain, reconcile this unique correlation/subject in the controlled mailbox/trace before retrying. Capture provider request IDs as well, and complete the actual reviewed AuditSphere business-mail journey separately. This direct transport probe does not write capability verification or business receipts. [Graph request status handling](https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.authentication/invoke-mggraphrequest?view=graph-powershell-1.0).

Graph HTTP 202 means provider acceptance, not mailbox delivery. Check the controlled mailbox or Exchange trace separately. Do not grant `Mail.Read` to AuditSphere merely to inspect delivery. **READ / Exchange PowerShell** for the bounded approved test:

```powershell
$TestRecipient = 'controlled-recipient@customer.example'
$End = (Get-Date).ToUniversalTime()
$Start = $End.AddHours(-2)
Get-MessageTraceV2 -SenderAddress $Sender -RecipientAddress $TestRecipient `
  -StartDate $Start -EndDate $End | Select-Object Received,MessageTraceId,MessageId,Status
Disconnect-ExchangeOnline -Confirm:$false
```

Record provider acceptance and observed delivery as separate facts. A missing trace is unverified, not delivered. Access to message metadata stays with the authorized mail operator. [Graph sendMail semantics](https://learn.microsoft.com/en-us/graph/api/user-sendmail?view=graph-rest-1.0), [message trace CLI](https://learn.microsoft.com/en-us/powershell/module/exchangepowershell/get-messagetracev2?view=exchange-ps).

## 12. Configure email domain DNS

This step applies only when the customer uses a custom mail domain. Existing approved domain/MX configuration should first be inspected, not replaced. An `onmicrosoft.com` sender uses Microsoft's tenant-managed domain; never edit that domain's public zone.

Use a **separate approved domain-admin interactive session** with delegated `Domain.Read.All`, not a runtime AuditSphere identity. The signed-in account needs the supported domain-read administrator role. The customer supplies `DomainAdminClientId`; do not register or consent a broad helper without approval.

```powershell
$TenantId = 'replace-with-customer-tenant-guid'
$DomainAdminClientId = 'replace-with-approved-domain-admin-tool-client-guid'
$MailDomain = 'customer.example'
Connect-MgGraph -TenantId $TenantId -ClientId $DomainAdminClientId `
  -Scopes 'Domain.Read.All' -UseDeviceAuthentication -ContextScope Process -NoWelcome
Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/domains/$MailDomain/verificationDnsRecords" -OutputType PSObject
Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/domains/$MailDomain/serviceConfigurationRecords" -OutputType PSObject
Disconnect-MgGraph | Out-Null
```

Publish the exact tenant-returned verification and service records using the customer's DNS provider CLI or portal. Adding/verifying a new domain is a separate authorized domain-administration change; do not assume the read session can do it. [Verification DNS API](https://learn.microsoft.com/en-us/graph/api/domain-list-verificationdnsrecords?view=graph-rest-1.0), [service DNS API](https://learn.microsoft.com/en-us/graph/api/domain-list-serviceconfigurationrecords?view=graph-rest-1.0).

If the customer approves adding a previously absent domain, its separate approved domain-admin tool/session can request delegated `Domain.ReadWrite.All`. **WRITE / optional domain onboarding** uses the existing `$TenantId`, `$DomainAdminClientId` and `$MailDomain` variables from this section:

```powershell
Connect-MgGraph -TenantId $TenantId -ClientId $DomainAdminClientId `
  -Scopes 'Domain.ReadWrite.All' -UseDeviceAuthentication -ContextScope Process -NoWelcome
$DomainBody = @{id=$MailDomain} | ConvertTo-Json
# Only after confirming this exact domain is absent from the intended tenant:
Invoke-MgGraphRequest -Method POST -Uri 'https://graph.microsoft.com/v1.0/domains' `
  -Body $DomainBody -ContentType 'application/json' -OutputType PSObject
Invoke-MgGraphRequest -Method GET `
  -Uri "https://graph.microsoft.com/v1.0/domains/$MailDomain/verificationDnsRecords" -OutputType PSObject
```

Stop after reading the verification record. The DNS owner publishes that exact record and verifies public propagation. Only after that checkpoint, continue in the same authorized PowerShell session (or reconnect if it expired):

```powershell
$VerifiedDomain = Invoke-MgGraphRequest -Method POST `
  -Uri "https://graph.microsoft.com/v1.0/domains/$MailDomain/verify" `
  -Body '{"forceTakeover":false}' -ContentType 'application/json' -OutputType PSObject
if (-not $VerifiedDomain.isVerified) { throw 'Domain ownership is not verified' }
Disconnect-MgGraph | Out-Null
```

On an uncertain POST, reread the exact domain before retrying. Do not force takeover/remove another tenant's domain, change the tenant's default domain, or rename existing users as part of this procedure. Domain ownership verification still does not configure Exchange service/mailbox routing; complete the customer-approved service/DNS setup. [Create domain](https://learn.microsoft.com/en-us/graph/api/domain-post-domains?view=graph-rest-1.0), [verify ownership](https://learn.microsoft.com/en-us/graph/api/domain-verify?view=graph-rest-1.0).

| Record | Required review |
| --- | --- |
| MX | Exact Microsoft-returned target/priority; change only with the customer's mail migration plan |
| SPF TXT | Maintain one SPF policy per hostname; merge authorized existing senders. For a Microsoft-only source, the usual policy includes `spf.protection.outlook.com`; do not add a duplicate or blindly discard other providers |
| DKIM CNAMEs | Use the actual Exchange-returned selector targets, not constructed names |
| DMARC TXT | Customer-approved reporting mailbox and enforcement/rollout policy; monitoring alone is not enforced rejection |
| Autodiscover | Use Microsoft's returned/recommended record where required; DNS-only mail records are not an HTTP reverse-proxy route |

**Exchange PowerShell / READ then authorized WRITE for DKIM:**

```powershell
$MailDomain = 'customer.example'
Get-DkimSigningConfig -Identity $MailDomain | Format-List Domain,Enabled,Status,Selector1CNAME,Selector2CNAME
# If no configuration exists, after customer approval:
# New-DkimSigningConfig -DomainName $MailDomain -Enabled $false
# Then reread Selector1CNAME/Selector2CNAME and publish those exact DNS CNAMEs.
# Enable only after their public resolution matches:
# Set-DkimSigningConfig -Identity $MailDomain -Enabled $true
```

**Admin workstation / Bash — READ public DNS.** Replace the domain before running:

```bash
TASK_MAIL_DOMAIN='customer.example'
dig +short MX "$TASK_MAIL_DOMAIN"
dig +short TXT "$TASK_MAIL_DOMAIN"
dig +short CNAME "selector1._domainkey.$TASK_MAIL_DOMAIN"
dig +short CNAME "selector2._domainkey.$TASK_MAIL_DOMAIN"
dig +short TXT "_dmarc.$TASK_MAIL_DOMAIN"
```

Complete a controlled external delivery test and have the mail administrator inspect Authentication-Results for SPF/DKIM/DMARC alignment. DNS resolution alone does not prove message authentication. [SPF](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-spf-configure), [DKIM](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-dkim-configure), [DMARC](https://learn.microsoft.com/en-us/defender-office-365/email-authentication-dmarc-configure).

## 13. SMTP and Resend alternatives

The worker implements `GRAPH`, `SMTP` and `RESEND` inside the same isolated **Acceptance/mail** composition. The checked-in Hostinger Compose selects Graph; alternative providers require a reviewed override. API Outbound Mail verification remains a Graph capability and does not verify SMTP or Resend.

| Provider | Actual implementation and limits |
| --- | --- |
| GRAPH | Certificate-backed Graph sendMail; supports the application's one attachment up to 3 MiB; 202 is accepted, not delivered |
| SMTP | `System.Net.Mail.SmtpClient`, optional paired username/password, `EnableSsl` for STARTTLS; no implemented XOAUTH2 flow; current adapter does not attach `PbcMailPlan.Attachment` |
| RESEND | HTTPS API key and verified sender, correlation ID as idempotency key; current adapter sends text only and does not attach `PbcMailPlan.Attachment` |

Do not use SMTP/Resend for commercial/report workflows requiring attachments until that adapter's attachment behavior is implemented and verified. Microsoft SMTP OAuth requires a SASL XOAUTH2 implementation that this SMTP adapter lacks; do not enable Basic Auth, disable Security Defaults or put a bearer token into its password setting to work around it. For an approved external SMTP relay, use STARTTLS on its documented port and an explicit sender/relay restriction. Port 465 implicit TLS is not the transport implemented by `SmtpClient.EnableSsl`. [SMTP OAuth](https://learn.microsoft.com/en-us/exchange/client-developer/legacy-protocols/how-to-authenticate-an-imap-pop-smtp-application-by-using-oauth), [SMTP STARTTLS behavior](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient.enablessl?view=net-10.0), [adapter source](../../src/AuditSphereOps.Infrastructure/Providers/AlternativePbcMailSenders.cs).

**Deployment host / example override, WRITE only after approved relay setup.** Save privately as `mail-provider.override.yaml`. Populate `SMTP_*` privately; these are additional override inputs, not variables recognized by the unmodified base Compose template.

```yaml
services:
  worker-mail:
    environment:
      Mail__Provider: SMTP
      SmtpMail__Host: ${SMTP_HOST:?Set approved relay host}
      SmtpMail__Port: ${SMTP_PORT:-587}
      SmtpMail__UseSsl: "true"
      SmtpMail__SenderAddress: ${SMTP_SENDER:?Set approved sender}
      SmtpMail__SenderName: AuditSphere
      SmtpMail__Username: ${SMTP_USERNAME:-}
      SmtpMail__Password: ${SMTP_PASSWORD:-}
```

For Resend, use the same service with `Mail__Provider: RESEND`, `ResendMail__ApiKey: ${RESEND_API_KEY:?Set private sending key}` and `ResendMail__SenderAddress: ${RESEND_SENDER:?Set verified sender}`. Provision the provider's verified sending domain/DNS and a sending-only key through the approved provider process. Do not place a key in a CLI argument or documentation. Resend is an external data processor requiring the customer's approval. [Resend API](https://resend.com/docs/api-reference/emails/send-email).

Validate with `"${TASK_COMPOSE[@]}" -f mail-provider.override.yaml --profile m365-mail config -q`. Review pending recipients, then start only the chosen mail worker with that same file list. There is no automatic failover between providers. An uncertain send must be reconciled; restarting with another provider can duplicate it. AuditSphere does not implement inbound mailbox ingestion or delivery webhooks through these adapters; configure no mailbox-read permission or fictitious IMAP/POP setting.

Optional PBC reminders also require explicit `PbcReminders:*` settings and a reviewed timezone, quiet hours, cadence, batch and public URL. They can create new sends, so leave reminders disabled until that operational policy and queued-recipient review are approved. See the configuration reference.

## 14. Optional user, invitation and group capabilities

Create only the identities the customer approved. Each has its own certificate, one application role and enable flag. User creation/invitation reconciliation also needs the separate verified Directory Reader. Complete the consent/verification steps before using **Users & Access** actions.

| Capability | Required operator choices |
| --- | --- |
| Workforce creation | Reviewed UPN/domain, enabled member policy, initial-password custody and local binding; `User.Create` does not assign a license or Entra admin role |
| Guest invitation | Tenant B2B policy, reviewed external address and fixed HTTPS landing URI; an invitation does not grant AuditSphere access |
| Managed groups | Approve the exact existing group object ID in AuditSphere; only static, non-role-assignable managed groups; explicit local allowlist/purpose and independent approval |
| Local roles | Explicit `FIRM_WIDE`, `CLIENT`, `ENGAGEMENT` or `GROUP` grants through application workflows; no automatic mapping from Microsoft group membership |

Use the existing review/preview/dispatch/reconciliation screens and durable operation receipts. Tenant permission enables a capability, not an unrestricted batch of directory changes. Do not create test users, invite real clients or mutate groups as part of documentation/setup unless those targets are separately approved. Timeouts/429/5xx are uncertain outcomes, not permission to repeat mutations. No CLI writes to local authorization tables are part of this guide.

The current managed-group guard rejects dynamic and role-assignable groups but does not require `securityEnabled=true`. A static non-role-assignable group can therefore pass the local approval guard even when it is not a security group. If the customer requires security groups only, inspect and enforce that choice in the reviewed group inventory rather than claim the current application already enforces it. Retiring an AuditSphere allowlist entry does not remove existing Microsoft memberships by itself.

## 15. Activate workers in stages

For the approved synthetic Hostinger-style deployment, validate configuration without printing secrets. Confirm credential readability as the container UID and sibling-key exclusion using boolean/exit-code checks only. Then recreate only the reviewed services:

During initial setup, explicitly start only `api worker-general worker-processing worker-records`; keep `worker-client-sites` stopped until the standing rollout is ready. Do not use an unqualified full-stack `up` while staging. The approved `CLIENT_SITES_ENABLED=true` default is preserved: its worker has no profile and normal full-stack startup includes it. Staging the final boundary/credential can be the effective provisioning activation boundary, not a later repeated `up` command.

```bash
"${TASK_COMPOSE[@]}" --profile m365-mail --profile m365-pbc config -q
# First startup after reviewed migrations; omit privileged/default-started site worker:
# "${TASK_COMPOSE[@]}" up -d api worker-general worker-processing worker-records
# API: apply reviewed capability/configuration changes.
"${TASK_COMPOSE[@]}" up -d --no-deps api
# Only after the exact site's resource/denial evidence and queued-transfer review:
# "${TASK_COMPOSE[@]}" --profile m365-pbc up -d --no-deps worker-pbc
# Only after sender restriction, recipient and queued-message review:
# "${TASK_COMPOSE[@]}" --profile m365-mail up -d --no-deps worker-mail
# Only after dedicated-site standing policy, boundary and credential approval:
# "${TASK_COMPOSE[@]}" up -d --no-deps worker-client-sites
"${TASK_COMPOSE[@]}" ps
# Boolean check after API recreation and before staging privileged provisioner credentials:
"${TASK_COMPOSE[@]}" exec -T api sh -c 'test ! -e /run/auditsphere/m365/client-sites'
```

**READ / secret-safe running mount check.** After API recreation, check actual mount metadata and the three persistent data volumes. This Hostinger-layout example prints only a pass marker; adapt the approved storage root for a customer layout. It does not read the container environment or credential contents.

```bash
TASK_API_CONTAINER_ID=$("${TASK_COMPOSE[@]}" ps -q api)
test -n "$TASK_API_CONTAINER_ID"
python3 - "$TASK_API_CONTAINER_ID" /opt/auditsphere/storage <<'PY'
from pathlib import Path
import json,subprocess,sys
mounts=json.loads(subprocess.check_output(['docker','inspect',sys.argv[1],'--format','{{json .Mounts}}'],text=True))
storage=Path(sys.argv[2])
by_target={m['Destination']:m for m in mounts}
expected={'/app/data-protection-home':'data-protection','/var/lib/auditsphere/pbc-staging':'pbc-staging',
          '/var/lib/auditsphere/release-checkpoints':'release-checkpoints'}
for target,subdir in expected.items():
    m=by_target[target]
    assert m['Type']=='volume' and m['RW'] is True
    options=json.loads(subprocess.check_output(['docker','volume','inspect',m['Name'],'--format','{{json .Options}}'],text=True))
    assert Path(options['device'])==storage/subdir
assert '/run/auditsphere/m365' not in by_target
allowed={'directory-reader','selected-site','provisioning','guest-invitation','group-membership','outbound-mail'}
for m in mounts:
    if m['Destination'].startswith('/run/auditsphere/m365/'):
        suffix=m['Destination'].removeprefix('/run/auditsphere/m365/')
        assert suffix in allowed and m['RW'] is False and m['Type']=='bind'
        assert Path(m['Source'])==storage/'m365-credentials'/suffix
print('RUNNING_API_DATA_AND_CREDENTIAL_MOUNTS_PASSED')
PY
# Example for an enabled reader; run as the container's configured service user:
"${TASK_COMPOSE[@]}" exec -T api sh -c \
  'test -r /run/auditsphere/m365/directory-reader/certificate.pem && test -r /run/auditsphere/m365/directory-reader/private-key.pem'
```

The client-sites service is in default startup and can provision as soon as its standing policy/credentials are complete; keep its cutoff and credentials staged until the reviewed rollout. Profiles isolate mail/PBC startup; API flags and worker processes are separate controls. Never use `docker compose down -v` for configuration updates. The API has capability keys under its parent M365 mount; privileged client-site keys remain on their separate worker mount. The API's actual host-directory visibility must be checked against your secret-store/mount implementation rather than inferred from its configured paths.

Inspect `/health/ready`, current worker heartbeat, Operations outcomes, and each capability/resource verification after recreation. Resume an UNKNOWN operation only through its existing recovery path after remote reconciliation. Accept success only with the provider-specific receipts and current stored revision. No tenant/provider operation is made atomic with the database by these commands.

## 16. Rotation, revocation and troubleshooting

### Credential rotation

1. Record exact app/client/resource/key IDs, current mounts, expiry, current consumers and rollback artifact.
2. Generate a new certificate into a **new protected directory**, append its public key to the same app, and verify public/private matching locally. Do not replace/remove the old key yet.
3. Stage both new PEM paths atomically in the approved secret/mount configuration. Recreate the API or only the relevant isolated worker(s); rotating the reader also affects the consent identity leg and client-sites identity reads.
4. Reverify the exact capability/resources, positive/negative boundaries and a controlled workflow. Retain old credentials during the approved overlap window.
5. Remove only the old key ID using `az ad app credential delete --id "$TASK_CAPABILITY_APP_ID" --key-id "$TASK_OLD_KEY_ID" --cert`. For a login password credential omit `--cert`. Verify the remaining metadata, then retire the old private files through the customer's custody procedure.
6. Document expiry monitoring, owner, next rotation date and revocation/emergency access. Do not treat a self-signed service certificate as a trusted PDF signing certificate.

Revocation sequence: stop the relevant worker profile or disable the corresponding capability, inspect in-flight/UNKNOWN operations, revoke exact upstream grants/credentials through the authorized administrator, and retain the last receipts. Disabling automatic client sites does not erase a saved cutoff or move documents into shared storage. Disabling local staff access does not immediately revoke every direct SharePoint sharing link.

| Symptom | Inspect and recover |
| --- | --- |
| `AADSTS50011` | Exact hostname/scheme/path and callback-owning app; API forwarded HTTPS/public URL |
| `invalid_client` / certificate unavailable | Correct app, matching public upload/private key, validity, readable mounts and clock |
| `app-role-not-granted` | Actual resource app-role assignment; requested manifest alone is insufficient |
| `app-role-set-too-broad` | Unexpected role on that identity; have the admin remove the exact extra assignment after review |
| Consent remains Required | Start AuditSphere's reviewed nonce-bound flow, complete its second identity callback, inspect current draft/session |
| Approved site returns 403 | Correct runtime client ID and exact-site application grant; Entra admin consent is insufficient |
| Negative control returns 200 or 404 | Wrong/broad grant or wrong control resource; block activation and investigate |
| Selected resource draft changes | Re-save/reverify the current revision, IDs and credential reference; never reuse stale pass rows |
| Worker blocked on rollout | Exact UTC `ClientsCreatedAfter`, saved cutoff, custodian/admin IDs and isolated credentials |
| No mail / provider accepted | Worker profile, current queue, sender restriction, transport acceptance, then independent mailbox/trace evidence |
| UNKNOWN send/site creation | Reconcile the original immutable operation/target; do not blindly resend or recreate |
| SMTP authentication failure | Actual approved relay auth method; Microsoft OAuth is not implemented by this adapter |
| RBAC scope test passes but other mailbox accessible | Remove reviewed unscoped Entra grants as part of supported migration; RBAC is additive |

Record sanitized error code, UTC timestamp, provider/application request ID, exact non-secret app/resource IDs, draft revision and operation ID. Do not upload raw token responses, expanded Compose config, `.env`, private keys, message bodies or customer files as diagnostic evidence.

## 17. Customer handover checklist

Treat this as an evidence template, not checked-off acceptance. Store a completed copy privately per installation. Use `NOT_RUN`, `BLOCKED_CONFIGURATION`, `BLOCKED_EXTERNAL` or `VERIFIED` with observation time and reviewer; blank credentials or old acceptance-tenant observations never count as a client pass.

- [ ] Customer tenant, installation, public host, release SHA and deployment environment recorded.
- [ ] Production composition/release gate resolved where real customer data is proposed.
- [ ] Separate app/client and enterprise-application object IDs recorded; approved actual resource grants read back.
- [ ] Login URI and both reader consent URIs match the deployed API exactly.
- [ ] Secret custody, private mounts, public-certificate upload, expiry/rotation owner and least-privilege consumers verified.
- [ ] Initial local administrator bound through the proof-backed flow and fresh sign-in; no Entra role inferred.
- [ ] Trusted consent identity recorded; every enabled capability freshly verified.
- [ ] Exact site/library/root and runtime write grant checked; unrelated control is denied; authorized file probe cleaned up.
- [ ] Approved template and matching verified connection revision activated through the application.
- [ ] Dedicated client-site exception, standing administrator/custodian, explicit rollout and site/staff reconciliation accepted.
- [ ] Mail authorization compatibility resolved; sender restricted; controlled acceptance/delivery and denied-sender evidence retained.
- [ ] Custom domain MX/SPF/DKIM/DMARC and authentication results reviewed where applicable.
- [ ] Optional directory actions, groups, B2B policy, reminder cadence and targets individually approved.
- [ ] Worker profiles, queued operations, retry/reconciliation and mount isolation checked.
- [ ] Temporary grantor, admin token cache and plaintext bootstrap proof retired with cleanup receipts.
- [ ] Backups/restore, authoritative archive, signing/certificate policy, capacity, accessibility and professional sign-off remain separately accepted or explicitly blocked.

For AI-agent handoff, record: inspected source SHA; requested step; exact approved mutation and scope; operator identity/authority; input references; actual commands and exit codes; sanitized HTTP/correlation IDs; before/after app-role/site/Exchange state; UTC observation; reviewer; cleanup; remaining blockers. Preserve task/acceptance statuses unless their own evidence and approval requirements pass.

Use a private packet like this for each bounded setup step; reference customer IDs/secrets from its protected inventory rather than copying secrets into the packet:

```yaml
step: selected-site-grant
source_sha: replace-with-inspected-full-sha
installation_record: protected-record-reference
approved_change: exact-app-site-write-grant-reference
operator_authority: approved-customer-operator-reference
status: NOT_RUN
commands_executed: []
observed_at_utc: null
provider_request_ids: []
resource_revision: null
positive_evidence: null
negative_evidence: null
cleanup_evidence: null
reviewer: null
remaining_blockers: []
```

At the end of the admin CLI session, run `az logout` and remove the isolated Azure CLI cache after approved secret custody is confirmed. Disconnect Graph/Exchange sessions. Never delete the customer's deployment secret store or reusable runtime credentials as session cleanup.
