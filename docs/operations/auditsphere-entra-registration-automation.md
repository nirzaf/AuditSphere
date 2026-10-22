# Entra registration and public-certificate automation

**Status:** CURRENT — operator procedure; live acceptance remains separate.

**Scope:** WU-018 in the [autonomous completion specification](../architecture/auditsphere-autonomous-completion-remediation-spec.md). This procedure creates/reconciles owned registrations and service principals and uploads public certificates. It does not grant consent, assign tenant administrator roles, enroll AuditSphere's tenant connection, create SharePoint sites, send mail, generate private keys, or establish report-signing acceptance. Follow the [permission boundaries](../architecture/auditsphere-m365-tenant-administration-permissions.md) and the canonical operator deployment Wiki for those separate steps.

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
  "exclusiveCertificateWindow": true
}
```

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

## Offline engineering checks

```powershell
pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-018 -Offline
```

The transport is synthetic and has no tenant connection. Current observed results and remaining live prerequisites belong in the execution ledger, not this procedure. No simulated result establishes Microsoft provider acceptance.

## Provider contracts

- [Microsoft application resource](https://learn.microsoft.com/en-us/graph/api/resources/application?view=graph-rest-1.0): registration properties and requested resources.
- [Create a service principal](https://learn.microsoft.com/en-us/graph/api/serviceprincipal-post-serviceprincipals?view=graph-rest-1.0): appId identifies the registration.
- [Add a public certificate](https://learn.microsoft.com/en-us/graph/applications-how-to-add-certificate): certificate-collection replacement and overlap preservation.

- [Graph PowerShell request context](https://learn.microsoft.com/en-us/powershell/module/microsoft.graph.authentication/set-mgrequestcontext?view=graph-powershell-1.0): explicit retry and timeout controls.
