# Entra registration and public-certificate automation

**Status:** CURRENT — operator procedure; live acceptance remains separate.

**Scope:** WU-018, WU-019, and WU-020 in the [autonomous completion specification](../architecture/auditsphere-autonomous-completion-remediation-spec.md). Registration commands create/reconcile owned registrations, service principals and public certificates. The separate permission command grants only the signed plan's exact application-role set; the enrollment command only reads existing human-consent evidence; the selected-sites command grants only app-only document write access on approved sites. None assigns tenant administrator roles, impersonates a human grantor, creates sites, sends mail, generates private keys or establishes report-signing acceptance. Follow the [permission boundaries](../architecture/auditsphere-m365-tenant-administration-permissions.md) and the canonical operator deployment Wiki for those separate steps.

## Modes and prerequisites

| Mode | Effect | Required inputs |
|---|---|---|
| Default, `-DryRun`, or `-WhatIf` | Offline `PLANNED`; no authentication, provider calls, certificate reads or writes | A schema-valid plan |
| `-Mode ReadOnly` | Exact owned-registration, service-principal and public-certificate readback; missing state is drift | Pinned Graph authentication module already loaded; an authorized matching operator session; existing public certificate files |
| `-Mode Apply` | Reviewed differences only, with `ShouldProcess` for each write | ReadOnly prerequisites, exact plan/source hashes, signed approval and the fixed exclusive per-operator journal directory |

Use PowerShell as pinned by the package. `modules.lock.json` records the installed authentication-module version and manifest hash. The command refuses an unloaded/different module and never installs modules or authenticates automatically. Establish the approved operator connection separately; changing to an unexpected tenant or operator client is refused. Scripts do not broaden operator privileges to repair an authorization error.

Live ReadOnly and Apply require a Git checkout whose HEAD equals `repositoryCommit` and whose `scripts/m365` tree has no pending changes. Put target plans, receipts and approvals outside tracked source. Planning success is not proof that live credentials, permissions or APIs work.

## Plan identities and certificate custody

Start from `scripts/m365/tests/fixtures/plan.synthetic.json` for **offline** planning. Its fake IDs and `synthetic.test` host are not deployment targets. Obtain real identifiers through the separately authorized tenant inventory.

- An existing capability has its exact `clientId`, approved `applicationObjectId` / `servicePrincipalObjectId` when known, and its actual `displayName`.
- To request a genuinely new registration, explicitly set `clientId` to JSON `null` and omit both object IDs. Microsoft assigns its client ID. A fixed client ID never causes a replacement registration to be created.
- The exact ownership tag is `AuditSphere/{lowercase ownershipTag}/{lowercase firmId}/{kind}`. A name collision, multiple matches or an existing app without this tag is unsafe. The script does not adopt an unrelated registration or silently tag it as owned.
- Set `publicCertificatePath` to an absolute path to an existing public-only DER `.cer` or PEM certificate. Set `publicCertificateSha256` to SHA-256 of the certificate's DER `RawData`, not the PEM text file hash. Expired/not-yet-valid certificates, mismatched fingerprints and private-key/PFX input are refused.
- `certificateReference` identifies existing approved private-key custody; these commands do not read that private key. Use a distinct certificate and client identity for every capability.
- Capability `redirectUris`, when present, must be a subset of the plan's approved top-level URI list. LOGIN otherwise uses that list; other capabilities default to no callbacks. Nothing invents a consent callback or copies login callbacks to every app.
- Role declarations are resolved from the tenant resource service principal's enabled Application roles. A declaration is **not** an admin-consent grant. Additional existing role declarations or unapproved outgoing application-role assignments are drift, not permission to remove or expand grants. `signInScopes` permits only `openid`, `profile`, and `email` for LOGIN. DIRECTORY_READER can additionally declare the approved `User.Read` identity leg; those scope IDs are resolved from provider metadata, and this command does not grant their consent.

Plan and inspect the redacted envelope:

```powershell
pwsh -NoProfile -NonInteractive -File scripts/m365/Provision-EntraApps.ps1 `
  -PlanPath /approved/operator/target-plan.json -DryRun `
  -OutputPath /approved/operator/registration-plan.json
python3 scripts/m365/verify-result.py /approved/operator/registration-plan.json --expect PLANNED
```

The envelope contains `planSha256`. Freeze the reviewed plan before obtaining approval; editing any field invalidates that digest.

## Signed approval

Apply accepts an exact UTF-8 JSON approval and detached CMS `.p7s` signature. The operator must receive the approval-signing certificate's SHA-256 fingerprint through an **independently trusted channel**. Never derive the trusted fingerprint from the submitted signature, plan, webpage or untrusted approval attachment. This operational approval is not a legal report signature or professional audit decision.

The signed JSON contains exactly:

```json
{
  "planSha256": "<digest from the reviewed plan>",
  "tenantId": "<approved tenant GUID>",
  "operatorClientId": "<approved operator application GUID>",
  "approvalReference": "<the external change record named in the plan>",
  "expiresAtUtc": "<expiry in UTC, within the next day>",
  "action": "RECONCILE_ENTRA_REGISTRATIONS",
  "exclusiveCertificateWindow": true,
  "stateDirectory": "<fixed operator journal directory>"
}
```

Obtain the exact journal location with `Import-Module ./scripts/m365/AuditSphere.M365.Entra.psm1; Get-EntraStateDirectory`. The signed `stateDirectory` must match that fixed current-operator location; a different process profile cannot silently select a new journal.

The approval owner signs the exact file bytes with their existing approved signing credential. For an already loaded authorized `$approvalCertificate`, standard PowerShell/.NET signing is:

```powershell
$bytes = [System.IO.File]::ReadAllBytes($approvalPath)
$content = [System.Security.Cryptography.Pkcs.ContentInfo]::new($bytes)
$cms = [System.Security.Cryptography.Pkcs.SignedCms]::new($content, $true)
$cms.ComputeSignature([System.Security.Cryptography.Pkcs.CmsSigner]::new($approvalCertificate))
[System.IO.File]::WriteAllBytes($signaturePath, $cms.Encode())
```

Do not generate a self-approved trust anchor to satisfy the gate. Private signing credentials remain with the approval owner. A certificate-only operation requires a separate approval action `SET_PUBLIC_CERTIFICATES`.

`exclusiveCertificateWindow` records the owner's actual maintenance coordination: no other portal/operator may change application certificates during read/append/readback. Graph PATCH replaces a certificate collection; a local process lock cannot fence an independent portal writer. Without that coordinated window, do not approve or execute certificate changes. Live acceptance must confirm this operational prerequisite.

## Apply, readback and recovery

From the authorized operator's existing matching Graph session:

```powershell
& ./scripts/m365/Provision-EntraApps.ps1 `
  -PlanPath $planPath -Mode Apply -ExpectedPlanSha256 $reviewedDigest `
  -ApprovalReference $changeRecord -ApprovalPath $approvalPath `
  -ApprovalSignaturePath $signaturePath -ApprovalSignerSha256 $trustedSignerDigest `
  -OutputPath $receiptPath
```

Interactive confirmation remains enabled. `-Confirm:$false` may suppress it only when all exact signed approvals and prerequisites already hold; it does not create authorization.

Use `Set-CapabilityCertificates.ps1` with the same parameters and the certificate-only signed approval to change only keys. It still refuses wrong ownership, multitenant registrations and incompatible role declarations. Old certificate entries are retained for overlap; unreadable old public keys block a replacement instead of being dropped. There is no automatic expired-key deletion.

The journal lives at `AuditSphere/M365Entra` beneath the operating system’s current-user LocalApplicationData directory, independently of checkout, worktree and output filename. `-StateDirectory` can only assert that exact location; another location is refused. Keep one designated operator host and account, as required by the execution contract. Do not switch hosts/accounts while an outcome is uncertain; recover the original operator journal first. Its per-tenant/ownership intent journal is locked for one writer and flushed before a creation request. Do not delete it, change state directories, rename the app or change ownership tags to escape an unknown result.

On `RESULT_UNCERTAIN`, preserve the original receipt and journal. The next invocation reads the original owned target first. If it exists, its assigned IDs are reused. If creation remains unobserved, the retained intent prevents a second POST; reconcile that target with the provider before any operator-approved repair. A timeout is not proof that creation failed. The SDK retry setting is temporarily disabled and each dispatch has a bounded timeout; previous local SDK settings are restored in `finally`. GETs alone may retry transient responses or transport failures within a bounded attempt/deadline budget, respecting `Retry-After`. A provider wait longer than the remaining budget stops the read instead of retrying early. Writes receive one dispatch only; preserve receipts and reconcile uncertain effects. Reads and receipt reconciliation must not create another identity or broaden permissions.

Read-only verification:

```powershell
& ./scripts/m365/Provision-EntraApps.ps1 -PlanPath $planPath `
  -Mode ReadOnly -OutputPath $readbackPath
python3 scripts/m365/verify-result.py $readbackPath --expect VERIFIED `
  --plan-sha256 $reviewedDigest --tenant $approvedTenantId
```

Review the assigned app/object IDs and fingerprints. Persist them into the **next separately reviewed plan**, obtain a new digest/approval for that plan, and use those exact IDs for subsequent steps. Entra consent, selected-site access, application enrollment and runtime capability readiness have their own verification gates. Do not enable them from a registration receipt alone.

## Exact application permissions (separate approval)

`Grant-TenantPermissions.ps1` uses the same validated plan, source pin, operator
context, fixed journal and detached-CMS approval as registration. Freeze a new
plan containing the observed existing app/SP IDs and registered certificate
fingerprints. Its approval action must be `GRANT_TENANT_APPLICATION_ROLES`.
`exclusiveCertificateWindow` may be `false` only for this permission action;
no certificate is uploaded or replaced here. The signer, tenant, operator, plan
digest, expiry and signed state-directory binding are still mandatory.

The authorized operator must already have separately approved Graph permissions
for application-role assignment. The script neither obtains administrator roles
nor broadens its own privileges. It resolves enabled role IDs from the exact
resource service principal and compares both `requiredResourceAccess` and the
entire outgoing `appRoleAssignments` set. Extra, missing/disabled or wrong-resource
roles, multitenant registrations and mismatched ownership/key evidence block the
operation. There is no broader-permission fallback and no automatic removal.

```powershell
& ./scripts/m365/Grant-TenantPermissions.ps1 -PlanPath $planPath -DryRun `
  -OutputPath $permissionPlanPath
# Obtain the separate signed permission approval for the resulting exact digest.
& ./scripts/m365/Grant-TenantPermissions.ps1 -PlanPath $planPath -Mode Apply `
  -ExpectedPlanSha256 $reviewedDigest -ApprovalReference $approvedReference `
  -ApprovalPath $approvalPath -ApprovalSignaturePath $signaturePath `
  -ApprovalSignerSha256 $independentlyTrustedSignerSha256 `
  -OutputPath $permissionResultPath -Confirm:$false
python3 scripts/m365/verify-result.py $permissionResultPath --expect APPLIED_AND_VERIFIED `
  --plan-sha256 $reviewedDigest --tenant $approvedTenantId
```

An unknown POST outcome is reconciled by reading the original assignment; an
absent outcome is not reposted. Confirmed assignments are retained in the journal.
If a role is later revoked, repairing it requires a newly reviewed plan and signed
approval with a different digest (for example a renewed approved reference).
Previously confirmed attempts remain in the journal. Do not erase that journal
to bypass uncertainty. Entra reads can lag writes, so a missing immediate readback
is `RESULT_UNCERTAIN`, not permission to grant again. See Microsoft's
[assignment contract](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-approleassignments?view=graph-rest-1.0)
and [outgoing read/replication behavior](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-list-approleassignments?view=graph-rest-1.0).

## AuditSphere human enrollment (read-only)

An application-role receipt does not satisfy AuditSphere's nonce-bound human
OIDC grantor leg. Complete the normal approved sign-in/consent workflow first.
`Test-TenantEnrollment.ps1` reads `/api/ui/session`,
`/api/ui/administration/microsoft365`, then `/api/ui/session` again at the plan's
exact HTTPS root origin. It refuses redirects, simulation, another tenant/firm,
changed session generation, missing human grantor evidence and stale/future
directory checks. Observation does not renew the underlying evidence expiry.
It never posts, invokes a callback or writes consent state directly to PostgreSQL.

The plan/source must be pinned and the M365 source tree clean. Supply an existing
approved staff-admin session's `.AspNetCore.Cookies` header (including its chunks
if present) through a short-lived trusted operator process. A Graph bearer token
cannot replace it. Cookie values are credentials: never put them in a plan,
command argument, transcript, screenshot or receipt. The command accepts only the
**name** of a process environment variable; it never authenticates for the user.

```powershell
$session = Read-Host 'Existing authorized AuditSphere cookie header (hidden)' -AsSecureString
try {
  $env:AUDITSPHERE_ENROLLMENT_COOKIE = [pscredential]::new('unused', $session).GetNetworkCredential().Password
  & ./scripts/m365/Test-TenantEnrollment.ps1 -PlanPath $planPath -Mode ReadOnly `
    -ExpectedPlanSha256 $reviewedDigest `
    -SessionCookieEnvironmentVariable AUDITSPHERE_ENROLLMENT_COOKIE `
    -OutputPath $enrollmentResultPath
} finally {
  $env:AUDITSPHERE_ENROLLMENT_COOKIE = $null
  $session.Dispose()
}
python3 scripts/m365/verify-result.py $enrollmentResultPath --expect VERIFIED `
  --plan-sha256 $reviewedDigest --tenant $approvedTenantId
```

Keep this process free of untrusted child programs; the cookie necessarily exists
in its memory/environment while used. The command bounds response size and request
time and accepts only JSON from those two read routes. Missing/expired staff
session or human consent is `BLOCKED_BOOTSTRAP`; malformed, stale or mismatched
provider evidence is not promoted. `Apply` is unsupported; planning/WhatIf read
neither session credentials nor any API. Verification covers enrollment only,
not selected-site access, mail delivery, workspace activation or client-site setup.

## Selected document sites grant automation

WU-020 provides `Grant-SelectedDocumentSites.ps1` to grant and verify application-only write permissions on approved SharePoint document sites under the `Sites.Selected` permission model.

The command separates privileged operator metadata/grant reconciliation from a
fresh child process authenticated with the DOCUMENTS certificate. It checks:

- Exact single-tenant app ownership, certificate and approved application-role set.
- Exact site ID, URL, tenant and approved description ownership marker before writes.
- One application-only `write` grant per site; mixed human/app grants, duplicates
  and broader roles fail closed. Existing unrelated permissions are untouched.
- A durable pending intent before POST and exact permission-ID readback before
  confirmation. Unknown outcomes require reconciliation, never a blind retry.
- Actual DOCUMENTS site and default-library-root reads, followed by HTTP 403 on
  known existing same-tenant unrelated sites. HTTP 401/404 and absence from an
  operator permission listing are not isolation proof.

No site, user permission, client record or tenant consent is created. WU-021 owns
client-site creation and staff membership. Library-root read does not prove file
upload/download, archive protection or application workspace activation.

Live execution requires a clean source checkout matching `repositoryCommit`,
`-ExpectedPlanSha256`, the pinned Graph authentication module and an existing
approved operator session. Apply also requires the existing detached CMS approval
(`GRANT_SELECTED_DOCUMENT_SITES`), trusted signer fingerprint, matching state
location and per-write confirmation. Use the same signed-approval procedure above
with that action; do not reuse an Entra application-role approval.

The runtime supports existing PEM private keys via `certificateReference=file:`
and a matching public certificate. Run only on the approved administration host
with the actual protected mount, never copy a runtime key to a workstation. The
CLI checks the leaf file, Unix access bits and cryptographic match; it cannot
attest the host/mount's custody policy. That remains operator evidence. Other
credential-reference types block rather than silently use the operator identity.
The private key is not placed in arguments, output or the operator token session.

DryRun / Plan invocation:

```powershell
pwsh -NoProfile -NonInteractive -File scripts/m365/Grant-SelectedDocumentSites.ps1 `
  -PlanPath /approved/operator/target-plan.json -DryRun `
  -OutputPath /approved/operator/sites-plan.json
python3 scripts/m365/verify-result.py /approved/operator/sites-plan.json --expect PLANNED
```

## SharePoint client site provisioning and managed staff Full Control (WU-021)

`Configure-SharePointClientSites.ps1` and `Reconcile-ClientSiteStaff.ps1`
are thin authenticated Application clients. Their default
`Plan`, `-DryRun` and `-WhatIf` paths remain entirely offline. Focused offline integration evidence is recorded in the execution ledger;
these instructions do not establish live tenant acceptance.
No command in this procedure grants Microsoft consent or performs a second
SPSiteManager/membership algorithm.

The existing isolated .NET `client-sites` worker remains the effect/receipt owner.
It derives URLs using `ClientSharePointSites.SiteSlug(clientName, clientId)`,
verifies the exact firm/client marker, uses the persisted rollout boundary, and
builds staff authority from current Application assignments. Caller-declared
`staffAssignments` or migration flags in a plan are not authorization. The
CLI requests exact-client intents and observes durable published receipts,
without mounting the privileged worker certificate in the API or CLI.

```powershell
pwsh -NoProfile -NonInteractive -File scripts/m365/Configure-SharePointClientSites.ps1 `
  -PlanPath /approved/operator/target-plan.json -DryRun `
  -OutputPath /approved/operator/client-sites-plan.json
python3 scripts/m365/verify-result.py /approved/operator/client-sites-plan.json --expect PLANNED

pwsh -NoProfile -NonInteractive -File scripts/m365/Reconcile-ClientSiteStaff.ps1 `
  -PlanPath /approved/operator/target-plan.json -DryRun `
  -OutputPath /approved/operator/client-staff-plan.json
python3 scripts/m365/verify-result.py /approved/operator/client-staff-plan.json --expect PLANNED
```

### Application intent boundary

The client-site operator adapter uses the authenticated AuditSphere origin, not
Microsoft Graph. Review one exact client through
`GET /api/ui/administration/microsoft365/client-sites/{id}/review` (or
`staff-review` for an existing site). Submit the returned review identity with
`POST /api/ui/administration/microsoft365/client-sites/{id}/request`; the body
contains `reviewToken`, `reviewed`, `reason` and `reconcileOnly`. Cookie and CSRF
validation and current firm-wide administrator authority are required.

`PENDING_DISCOVERY` and `ALREADY_QUEUED` acknowledge local work only. The existing
isolated worker performs the provider operation. A completed worker receipt must
match the current site, tenant, staff authorization and membership before the
operator can claim verified reconciliation. Blocked or uncertain original
operations require their existing Operations recovery workflow; creating another
request is not recovery. A client predating the persisted rollout boundary must
remain in its existing repository.

The API needs the non-secret `ClientSites:SiteHost`, tenant identity and explicit
rollout boundary to match the persisted tenant configuration. Privileged worker
certificates remain in the isolated worker. Live tenant acceptance and exact-source CI remain separate gates in the execution ledger.

### Reviewed terminal execution

1. Use a clean, reviewed checkout. The non-secret plan's `repositoryCommit` must
   equal `git rev-parse HEAD`; live entry points refuse modified M365 scripts.
   Put exact new-client IDs in `clientSites.clientIds` (unique GUIDs, bounded
   batch). Legacy plans may provide `sites[].clientId`; their URLs, staff lists
   and migration claims do not override Application authority.
2. Set `clientSites.host` and `rolloutCutoffUtc` to the approved server values.
   The server checks the persisted boundary and rejects historical conversion.
   The existing `custodianObjectId` describes worker setup; the CLI cannot change
   the worker's custodian or credentials.
3. Compute the plan digest with `Get-M365PlanSha256` from the common module.
   For Apply, obtain the detached CMS approval described above, binding the
   exact digest, tenant, operator client, reference, fixed state directory,
   bounded expiry and action `CONFIGURE_SHAREPOINT_CLIENT_SITES` or
   `RECONCILE_CLIENT_SITE_STAFF`. Trust the signer fingerprint through an
   independently approved channel. A plan flag is not approval.
4. Supply the existing authenticated staff session cookie privately through an
   environment variable. Never put its value in command arguments, plans,
   transcripts, screenshots or Git. The CLI refreshes same-origin CSRF material;
   redirects, changed sessions and cross-origin responses are refused. Normal
   human sign-in/MFA remains required; this does not create an administrator.

```powershell
# Replace placeholders with reviewed non-secret paths, digest and signer hash.
$env:AUDITSPHERE_OPERATOR_COOKIE = Read-Host 'AuditSphere staff session cookie (hidden)' -MaskInput
try {
  pwsh -NoProfile -NonInteractive -File scripts/m365/Configure-SharePointClientSites.ps1 `
    -PlanPath /approved/operator/target-plan.json -Mode Apply `
    -ExpectedPlanSha256 $reviewedPlanSha256 -ApprovalReference $approvalReference `
    -ApprovalPath /approved/operator/client-sites-approval.json `
    -ApprovalSignaturePath /approved/operator/client-sites-approval.p7s `
    -ApprovalSignerSha256 $approvedSignerSha256 `
    -SessionCookieEnvironmentVariable AUDITSPHERE_OPERATOR_COOKIE `
    -OutputPath /approved/operator/client-sites-result.json -Confirm:$false
} finally {
  Remove-Item Env:AUDITSPHERE_OPERATOR_COOKIE -ErrorAction SilentlyContinue
}
```

`-Confirm:$false` is appropriate only after the exact signed approval is reviewed;
it does not bypass CMS, session, CSRF or server authorization. Use
`Reconcile-ClientSiteStaff.ps1` with its separately signed action for staff-only
reconciliation on an existing site. For observation/resume, use `-Mode ReadOnly`
with the exact plan digest and privately supplied cookie; do not resubmit an
uncertain POST automatically. ReadOnly cannot create an intent.

Bounded polling may end `BLOCKED_EXTERNAL` while discovery or the worker is still
pending. Inspect the original operation through Administration/Operations and
rerun ReadOnly after progress. `RESULT_UNCERTAIN` means the POST may have been
accepted: recover its server state first. A queued request, stale receipt,
residual access or changed staff generation cannot produce verified success.
Provider mutations belong to the worker receipt; terminal mutation counts refer
only to local Application intent submissions. No privileged worker certificate
is mounted in the CLI. Live positive provisioning, idempotent rerun and negative
cross-client acceptance still require the separately approved tenant run.

## Offline engineering checks

```powershell
pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-018 -Offline
pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-019 -Offline
pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-020 -Offline
pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-021 -Offline
```

The transport is synthetic and has no tenant connection. Current observed results and remaining live prerequisites belong in the execution ledger, not this procedure. No simulated result establishes Microsoft provider acceptance.

## Provider contracts

- [Microsoft application resource](https://learn.microsoft.com/en-us/graph/api/resources/application?view=graph-rest-1.0): registration properties and requested resources.
- [Create a service principal](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0): appId identifies the registration.
- [Add a public certificate](https://learn.microsoft.com/en-us/graph/applications-how-to-add-certificate): certificate-collection replacement and overlap preservation.

- [Graph PowerShell request context](https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.authentication/set-mgrequestcontext?view=graph-powershell-1.0): explicit retry and timeout controls.
