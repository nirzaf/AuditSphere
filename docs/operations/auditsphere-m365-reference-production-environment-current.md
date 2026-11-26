# Production Environment Settings

Configuration is deployment-owned. Inject values through the approved secret/configuration store or process environment; do not commit production values. ASP.NET Core maps double underscores to nested keys, so `ExternalEffects__Enabled` means `ExternalEffects:Enabled`.

## Application host (`AuditSphereOps.Api` serving Angular)

The source defaults are in the API [`appsettings.json`](../../src/AuditSphereOps.Api/appsettings.json). The former Blazor host is absent from the current project graph; the API host reads the `Application`, `Setup`, `Identity`, `DataProtection`, `TenantConsent`, `DirectoryReader`, `SelectedSite`, `ExternalEffects`, `FeatureActivation`, `ReleaseSafety` and `Telemetry` key groups. Its checked-in file omits `Identity`, `DataProtection` and `SelectedSite` sections because those are supplied only through the environment/secret store. Production must override local defaults explicitly.

| Key | Development | Production requirement |
| --- | --- | --- |
| `ConnectionStrings__AuditSphere` | Loopback PostgreSQL, usually port `5433` | TLS/credential-managed PostgreSQL connection; never a committed password |
| `Application__FirmId` | Optional when `Setup__FirmId` is used | Stable deployment firm GUID |
| `Application__InstallationId` | Optional local installation ID | Stable installation GUID; required by the production Data Protection key namespace |
| `Application__PublicBaseUrl` | Local base URL or unset | Exact public HTTPS origin used for links and mail |
| `Application__TimeZone` | `UTC` | Approved display/business timezone; keep persisted timestamps UTC |
| `Application__AllowSimulationAdapters` | `true` only for supported local development | `false`; production startup validation rejects simulation adapters |
| `Identity__TenantId` | Usually unset when DevelopmentIdentity is used | Approved workforce tenant GUID |
| `Identity__ClientId` | Usually unset when DevelopmentIdentity is used | Production web OIDC client ID |
| `Identity__ClientSecret` | Never use a production value locally | Runtime-injected client secret from an approved secret store; the current API host consumes a secret value |
| `Identity__CallbackPath` | `/signin-oidc` | Exact path registered in Entra; default `/signin-oidc` |
| `DataProtection__KeyDirectory` | Framework-managed local keys | Existing absolute directory for the persistent web key ring; grant only the authorized web instances access |
| `DataProtection__CertificatePath` | Unset | Absolute path to the approved PKCS#12 certificate mount used to encrypt the key ring |
| `DataProtection__CertificatePassword` | Unset | Inject privately from the approved secret store; never commit or log the value |
| `DataProtection__PreviousCertificates__0__Path` | Unset | During rotation, mount the prior PKCS#12 certificate at an absolute path; add numbered entries for every still-required prior certificate |
| `DataProtection__PreviousCertificates__0__Password` | Unset | Inject that prior certificate password privately; a partial entry refuses startup |
| `ExternalEffects__Enabled` | `false` | Keep `false` until the approved live provider composition exists; current API code refuses startup with it enabled |
| `ExternalEffects__DeploymentEpoch` | Unset or local | Positive deployment/recovery epoch when an approved live composition supports it |
| `Telemetry__Otlp__Endpoint` | Unset is valid | Optional absolute HTTP(S) collector URL without embedded credentials, query or fragment; a malformed configured value refuses API or Worker startup |

Production API startup rejects missing or invalid Data Protection settings or a current certificate without a currently usable private key. Use the same protected key directory and certificate profile on authorized API instances of one installation; a different tenant or installation has a separate application namespace. The current implementation is in [ProductionDataProtection](../../src/AuditSphereOps.Api/Authentication/ProductionDataProtection.cs).

### Certificate rotation (administrator-only)

1. In the approved private certificate mount and secret store, stage the replacement PKCS#12 and retain the existing certificate with its private key. Do not overwrite the old mount.
2. Point `CertificatePath` and `CertificatePassword` to the replacement. Add each still-required old certificate as a numbered `PreviousCertificates` path/password pair. Apply the same profile to authorized API instances and restart them. Expected: startup succeeds; if it refuses, restore the prior configuration while preserving the key ring and investigate privately.
3. In the deployment rehearsal, verify existing protected sessions remain readable and new Data Protection keys are encrypted to the replacement. Keep prior keys and certificates under approved custody until the recovery and expiration policy permits retirement; early removal can make protected data unreadable. Backup, access, rotation, and restore remain unverified for production until recorded in deployment evidence.

## Protected one-time setup

The setup page reads these deployment-owned values:

```text
Setup__FirmId=<stable-firm-guid>
Setup__InstallationId=<stable-installation-id>
Setup__BootstrapProofHash=<hash-only-value>
Setup__InitialAdministratorTenantId=<approved-tenant-guid>
Setup__InitialAdministratorObjectId=<approved-object-guid>
```

`Setup__BootstrapProofHash` is not the proof. Deliver the short-lived proof out of band and never log or store the plaintext. After the one-time claim is consumed, recovery uses the controlled owner process; do not enable first-visitor ownership.

## Feature and release gates

```text
FeatureActivation__LiveAccountingRelease=false
FeatureActivation__LiveAuditRelease=false
FeatureActivation__LiveFirmLedgerPosting=false

ReleaseSafety__RequireSignatureLineage=false
ReleaseSafety__RequireProtectionAttestation=false
ReleaseSafety__RequireExternalCheckpointBeforeDelivery=true
```

These are independent gates. The worker `release-checkpoint` group requires `FeatureActivation:LiveAuditRelease=true`, external effects enabled, simulation adapters disabled, an isolated Acceptance or individually allowlisted Production composition, and the explicitly configured absolute checkpoint root shared by API and worker. This is a code capability; it does not establish owner approval, deployed configuration, or external acceptance. Signed-document lineage and external checkpoints must not be enabled by changing a flag without their approved evidence. Purview and eSignature provider integrations are outside product scope.

## Records settings

There is currently no `Records:*` appsettings section consumed by the API or worker startup code. Records profiles and actions are persisted in PostgreSQL and exposed through the [Angular engagement archive handoff](../../src/AuditSphereOps.Ui/src/app/features/audit/engagement-archive-handoff.ts), [records endpoints](../../src/AuditSphereOps.Api/Ui/UiEndpoints.AuditRecords.cs), [RecordsArchiveService](../../src/AuditSphereOps.Application/Records/RecordsArchiveService.cs), and the existing provider/evidence boundaries.

Do not invent `Records__ProfileId`, retention duration, label ID, or legal-hold settings and assume they work. Preserve exact uploaded evidence, SHA-256 identities, human decisions, and release/archive manifests. A saved local profile is not external provider acceptance.

## Worker (`AuditSphereOps.Worker`)

The source defaults are in the [Worker appsettings](../../src/AuditSphereOps.Worker/appsettings.json).

```text
ConnectionStrings__AuditSphere=<runtime-injected-worker-connection>
ExternalEffects__Enabled=false
AllowSimulationAdapters=false
Worker__FirmId=<stable-firm-guid>
Worker__DeploymentEpoch=<positive-epoch>
Worker__Group=general
Mail__Provider=GRAPH|SMTP|RESEND
```

The worker validates a non-empty firm ID, a positive deployment epoch, bounded lease settings, and an approved environment/composition. Core `general`, `processing`, and `records` groups can run in Production with external effects disabled. The code also permits individually allowlisted Production live groups when external effects are enabled and simulation adapters are disabled. Provider credential and exact-resource checks remain separate; code capability does not establish owner approval, deployed configuration, or external acceptance.

The optional `Telemetry__Otlp__Endpoint` is validated by API and Worker; an unset value leaves export disabled. The API metrics pipeline includes built-in ASP.NET HTTP request measurements. The endpoint URL is not a place for credentials. See [TelemetryEndpoint](../../src/AuditSphereOps.Application/Diagnostics/TelemetryEndpoint.cs). A configured exporter alone is not an accepted dashboard, alert, or privacy review.

### Mail provider settings

Choose exactly one provider for the isolated live-mail worker:

```text
# Graph
GraphMail__TenantId=<tenant-guid>
GraphMail__ClientId=<mail-worker-client-id>
GraphMail__SenderMailbox=<approved-mailbox>
GraphMail__CertificatePath=<approved-runtime-reference>
GraphMail__PrivateKeyPath=<approved-runtime-reference>

# SMTP
SmtpMail__Host=<smtp-host>
SmtpMail__Port=587
SmtpMail__UseSsl=true
SmtpMail__SenderAddress=<approved-sender>
SmtpMail__SenderName=AuditSphere
SmtpMail__Username=<secret-store-reference>
SmtpMail__Password=<secret-store-reference>

# Resend
ResendMail__ApiKey=<secret-store-reference>
ResendMail__SenderAddress=<approved-sender>
```

The values are owner-authorized external credentials. A `202 Accepted` mail response is provider acceptance, not recipient delivery or read evidence.

## Development versus production

Development may use `DevelopmentIdentity__Enabled=true`, a synthetic local subject/tenant, `ExternalEffects__Enabled=false`, and the loopback PostgreSQL database. Development identity is rejected outside Development/Test and cannot coexist with OIDC.

Production must use Microsoft OIDC, exact tenant/object bindings, `Application__AllowSimulationAdapters=false`, no demo actor, approved secret references, and a controlled deployment epoch. Never copy DevelopmentIdentity settings into production.
