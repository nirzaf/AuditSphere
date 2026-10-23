#Requires -Version 7.4
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Deployment binding tests run offline only; pass -Offline.' }

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$fixture = Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-deployment-bindings-' + [guid]::NewGuid().ToString('N'))
$engine = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
$failures = [System.Collections.Generic.List[string]]::new()
$passed = 0
New-Item -ItemType Directory -Path $work | Out-Null

function Assert-That([string] $Name, [bool] $Condition, [string] $Detail = '') {
  if ($Condition) { $script:passed++ } else { $script:failures.Add("$Name $Detail") }
}

function Read-ResultJson([string] $Path) {
  return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable -Depth 64
}

function Invoke-EntryPoint([string] $Name, [string[]] $Arguments) {
  $scriptPath = Join-Path $root "scripts/m365/$Name.ps1"
  & $engine -NoProfile -NonInteractive -File $scriptPath @Arguments | Out-Null
  return $LASTEXITCODE
}

function Get-VerifyArguments([string] $Plan, [string] $Config, [string] $Output, [string[]] $EnabledCapabilities = @('DIRECTORY_READER', 'DOCUMENTS')) {
  return @('-PlanPath', $Plan, '-Mode', 'Verify', '-ConfigJsonPath', $Config, '-TargetEnvironment', 'Acceptance', '-EnabledCapability', ($EnabledCapabilities -join ','), '-OutputPath', $Output)
}

function New-Bind([string] $Source, [string] $Target) {
  return [ordered]@{ type = 'bind'; source = $Source; target = $Target; read_only = $true; bind = [ordered]@{ create_host_path = $false } }
}

function New-Volume([string] $Name, [string] $Target) {
  return [ordered]@{ type = 'volume'; source = $Name; target = $Target; read_only = $true }
}

function New-SyntheticComposeModel([string] $SourceSha) {
  $tenant = '00000000-0000-4000-8000-000000000001'
  $apiEnv = [ordered]@{
    ASPNETCORE_ENVIRONMENT = 'Acceptance'; ExternalEffects__Enabled = 'false';
    Application__AllowSimulationAdapters = 'false'; DevelopmentIdentity__Enabled = 'false';
    Identity__TenantId = $tenant; Identity__ClientId = '00000000-0000-4000-8000-000000000101';
    Identity__ClientSecret = '[REDACTED:IDENTITY_CLIENT_SECRET]';
    ConnectionStrings__AuditSphere = 'Host=db;Port=5432;Database=auditsphere;Username=auditsphere;Password=[REDACTED:POSTGRES_PASSWORD]';
    Setup__BootstrapProofHash = '[REDACTED:BOOTSTRAP_PROOF_HASH]';
    Application__FirmId = '00000000-0000-4000-8000-000000000002';
    DirectoryReader__ClientId = '00000000-0000-4000-8000-000000000020';
    DirectoryReader__Enabled = 'true';
    DirectoryReader__CertificatePath = '/run/auditsphere/m365/directory-reader/certificate.pem';
    DirectoryReader__PrivateKeyPath = '/run/auditsphere/m365/directory-reader/private-key.pem';
    SelectedSite__TenantId = $tenant;
    SelectedSite__ClientId = '00000000-0000-4000-8000-000000000030';
    SelectedSite__CertificatePath = '/run/auditsphere/m365/selected-site/certificate.pem';
    SelectedSite__PrivateKeyPath = '/run/auditsphere/m365/selected-site/private-key.pem';
    TenantAdministration__Provisioning__ClientId = '00000000-0000-4000-8000-000000000104';
    TenantAdministration__Provisioning__CertificatePath = '/run/auditsphere/m365/provisioning/certificate.pem';
    TenantAdministration__Provisioning__PrivateKeyPath = '/run/auditsphere/m365/provisioning/private-key.pem';
    TenantAdministration__GuestInvitation__ClientId = '00000000-0000-4000-8000-000000000105';
    TenantAdministration__GuestInvitation__CertificatePath = '/run/auditsphere/m365/guest-invitation/certificate.pem';
    TenantAdministration__GuestInvitation__PrivateKeyPath = '/run/auditsphere/m365/guest-invitation/private-key.pem';
    TenantAdministration__GroupMembership__ClientId = '00000000-0000-4000-8000-000000000106';
    TenantAdministration__GroupMembership__CertificatePath = '/run/auditsphere/m365/group-membership/certificate.pem';
    TenantAdministration__GroupMembership__PrivateKeyPath = '/run/auditsphere/m365/group-membership/private-key.pem';
    TenantAdministration__OutboundMail__ClientId = '00000000-0000-4000-8000-000000000107';
    TenantAdministration__OutboundMail__CertificatePath = '/run/auditsphere/m365/outbound-mail/certificate.pem';
    TenantAdministration__OutboundMail__PrivateKeyPath = '/run/auditsphere/m365/outbound-mail/private-key.pem';
    ClientSites__Enabled = 'true'; ClientSites__ClientsCreatedAfter = '2026-10-01T00:00:00Z';
    Storage__PbcStagingRoot = '/var/lib/auditsphere/pbc-staging';
    Storage__ReleaseCheckpointRoot = '/var/lib/auditsphere/release-checkpoints'
  }
  $workerBase = [ordered]@{
    ASPNETCORE_ENVIRONMENT = 'Acceptance'; DOTNET_ENVIRONMENT = 'Acceptance';
    ConnectionStrings__AuditSphere = $apiEnv.ConnectionStrings__AuditSphere;
    Worker__FirmId = $apiEnv.Application__FirmId; Worker__DeploymentEpoch = '1';
    ExternalEffects__Enabled = 'false'; AllowSimulationAdapters = 'false';
    Storage__PbcStagingRoot = '/var/lib/auditsphere/pbc-staging';
    Storage__ReleaseCheckpointRoot = '/var/lib/auditsphere/release-checkpoints'
  }
  $services = [ordered]@{
    db = [ordered]@{ image = 'postgres:18-alpine'; environment = [ordered]@{ POSTGRES_PASSWORD = '[REDACTED:POSTGRES_PASSWORD]' }; volumes = @('postgres_data:/var/lib/postgresql') }
    migrate = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-migrations:$SourceSha"; environment = [ordered]@{ ConnectionStrings__AuditSphere = $apiEnv.ConnectionStrings__AuditSphere } }
    api = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-api:$SourceSha"; environment = $apiEnv; volumes = @(
      (New-Volume 'data_protection' '/app/data-protection-home'), (New-Volume 'pbc_staging' '/var/lib/auditsphere/pbc-staging'), (New-Volume 'release_checkpoints' '/var/lib/auditsphere/release-checkpoints'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/directory-reader' '/run/auditsphere/m365/directory-reader'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/selected-site' '/run/auditsphere/m365/selected-site'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/provisioning' '/run/auditsphere/m365/provisioning'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/guest-invitation' '/run/auditsphere/m365/guest-invitation'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/group-membership' '/run/auditsphere/m365/group-membership'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/outbound-mail' '/run/auditsphere/m365/outbound-mail')
    ) }
    'worker-general' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = $workerBase }
    'worker-processing' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = [ordered]@{} }
    'worker-records' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = [ordered]@{} }
    'worker-mail' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = [ordered]@{}; volumes = @((New-Volume 'm365_mail_credentials' '/run/auditsphere/m365/outbound-mail')) }
    'worker-pbc' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = [ordered]@{}; volumes = @((New-Volume 'm365_selected_site_credentials' '/run/auditsphere/m365/selected-site')) }
    'worker-client-sites' = [ordered]@{ image = "ghcr.io/nirzaf/auditsphere-acceptance-worker:$SourceSha"; environment = [ordered]@{}; volumes = @(
      (New-Bind '/opt/auditsphere/storage/m365-credentials/client-sites' '/run/auditsphere/m365/client-sites'),
      (New-Bind '/opt/auditsphere/storage/m365-credentials/directory-reader' '/run/auditsphere/m365/directory-reader')
    ) }
  }

  foreach ($workerName in @('worker-processing', 'worker-records', 'worker-mail', 'worker-pbc', 'worker-client-sites')) {
    $workerEnv = [ordered]@{}
    foreach ($key in $workerBase.Keys) { $workerEnv[$key] = $workerBase[$key] }
    $services[$workerName].environment = $workerEnv
  }
  $services['worker-processing'].environment.Worker__Group = 'processing'
  $services['worker-records'].environment.Worker__Group = 'records'
  $services['worker-mail'].environment.Worker__Group = 'mail'
  $services['worker-mail'].environment.ExternalEffects__Enabled = 'true'
  $services['worker-mail'].environment.Mail__Provider = 'GRAPH'
  $services['worker-mail'].environment.GraphMail__TenantId = $tenant
  $services['worker-mail'].environment.GraphMail__ClientId = $apiEnv.TenantAdministration__OutboundMail__ClientId
  $services['worker-mail'].environment.TenantAdministration__OutboundMail__ClientId = $apiEnv.TenantAdministration__OutboundMail__ClientId
  $services['worker-mail'].environment.GraphMail__PrivateKeyPath = '/run/auditsphere/m365/outbound-mail/private-key.pem'
  $services['worker-pbc'].environment.Worker__Group = 'pbc'
  $services['worker-pbc'].environment.ExternalEffects__Enabled = 'true'
  $services['worker-pbc'].environment.SelectedSite__TenantId = $tenant
  $services['worker-pbc'].environment.SelectedSite__ClientId = $apiEnv.SelectedSite__ClientId
  $services['worker-pbc'].environment.SelectedSite__CertificatePath = '/run/auditsphere/m365/selected-site/certificate.pem'
  $services['worker-pbc'].environment.SelectedSite__PrivateKeyPath = '/run/auditsphere/m365/selected-site/private-key.pem'
  $services['worker-client-sites'].environment.Worker__Group = 'client-sites'
  $services['worker-client-sites'].environment.ExternalEffects__Enabled = 'true'
  $services['worker-client-sites'].environment.ClientSites__ClientId = '00000000-0000-4000-8000-000000000108'
  $services['worker-client-sites'].environment.ClientSites__TenantId = $tenant
  $services['worker-client-sites'].environment.ClientSites__PrivateKeyPath = '/run/auditsphere/m365/client-sites/private-key.pem'

  $volumes = [ordered]@{}
  foreach ($entry in @(
    @{ name = 'postgres_data'; device = '/opt/auditsphere/storage/postgres' },
    @{ name = 'data_protection'; device = '/opt/auditsphere/storage/data-protection' },
    @{ name = 'pbc_staging'; device = '/opt/auditsphere/storage/pbc-staging' },
    @{ name = 'release_checkpoints'; device = '/opt/auditsphere/storage/release-checkpoints' },
    @{ name = 'm365_mail_credentials'; device = '/opt/auditsphere/storage/m365-credentials/outbound-mail' },
    @{ name = 'm365_selected_site_credentials'; device = '/opt/auditsphere/storage/m365-credentials/selected-site' }
  )) { $volumes[$entry.name] = [ordered]@{ driver = 'local'; driver_opts = [ordered]@{ type = 'none'; o = 'bind'; device = $entry.device } } }
  return [ordered]@{ name = 'auditsphere-acceptance'; services = $services; volumes = $volumes }
}

try {
  $plan = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json -AsHashtable -Depth 32
  $headSha = (& git -C $root rev-parse --verify HEAD).Trim().ToLowerInvariant()
  $verifyPlan = ($plan | ConvertTo-Json -Depth 32 -Compress) | ConvertFrom-Json -AsHashtable -Depth 32
  $verifyPlan.repositoryCommit = $headSha
  $verifyPlan.environment = 'Acceptance'
  $graphAppId = '00000003-0000-0000-c000-000000000000'
  $sharePointAppId = '00000003-0000-0ff1-ce00-000000000000'
  $verifyPlan.capabilities += [ordered]@{
    kind = 'MAIL'; clientId = '00000000-0000-4000-8000-000000000107'; certificateReference = 'file:/run/secrets/synthetic-mail.pem';
    publicCertificateSha256 = ('c' * 64); expectedApplicationRoles = @()
  }
  $verifyPlan.capabilities += [ordered]@{
    kind = 'CLIENT_SITES'; clientId = '00000000-0000-4000-8000-000000000108'; certificateReference = 'file:/run/secrets/synthetic-client-sites.pem';
    publicCertificateSha256 = ('d' * 64); expectedApplicationRoles = @(
      [ordered]@{ resourceAppId = $graphAppId; value = 'Sites.FullControl.All' },
      [ordered]@{ resourceAppId = $sharePointAppId; value = 'Sites.FullControl.All' }
    )
  }
  $verifyPlan.mail = [ordered]@{
    permissionModel = 'EXCHANGE_RBAC_RESOURCE_SCOPE'; senderObjectIds = @('00000000-0000-4000-8000-000000000201');
    controlMailboxObjectId = '00000000-0000-4000-8000-000000000202'; scopeName = 'synthetic-mail-scope'
  }
  $verifyPlan.clientSites = [ordered]@{
    host = 'auditsphere.sharepoint.com'; custodianObjectId = '00000000-0000-4000-8000-000000000203'; rolloutCutoffUtc = '2026-10-01T00:00:00Z'
  }
  $verifyPlanPath = Join-Path $work 'plan-acceptance-exact-checkout.json'
  $verifyPlan | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $verifyPlanPath -Encoding utf8NoBOM
  $configPath = Join-Path $work 'compose-sanitized.json'
  $model = New-SyntheticComposeModel $headSha
  $model | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM

  $planOut = Join-Path $work 'plan.json'
  $planExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $fixture, '-DryRun', '-OutputPath', $planOut)
  $planResult = Read-ResultJson $planOut
  Assert-That 'dryrun-is-planned' ($planExit -eq 0 -and $planResult.status -eq 'PLANNED' -and $planResult.mode -eq 'Plan') "exit=$planExit status=$($planResult.status)"
  Assert-That 'dryrun-zero-effects-and-runtime-null' ($planResult.networkCalls -eq 0 -and $planResult.mutationsAttempted -eq 0 -and $null -eq $planResult.deploymentEvidence.runtimeEnvironment -and $null -eq $planResult.deploymentEvidence.rollbackPath)
  Assert-That 'dryrun-target-source-hashed' (@($planResult.deploymentEvidence.targetFiles | Where-Object path -eq 'infra/hostinger-acceptance/compose.yaml').Count -eq 1)

  $overrideOut = Join-Path $work 'override-plan.json'
  $overrideExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $fixture, '-DryRun', '-IsolationOverridePath', 'infra/hostinger-acceptance/m365-isolation.override.example.yaml', '-OutputPath', $overrideOut)
  $overrideResult = Read-ResultJson $overrideOut
  Assert-That 'optional-override-is-explicit' ($overrideExit -eq 0 -and $overrideResult.status -eq 'PLANNED' -and @($overrideResult.deploymentEvidence.targetFiles | Where-Object path -eq 'infra/hostinger-acceptance/m365-isolation.override.example.yaml').Count -eq 1)

  $whatIfOut = Join-Path $work 'whatif.json'
  $badJson = Join-Path $work 'not-read-under-whatif.json'
  '{not json' | Set-Content -LiteralPath $badJson -Encoding utf8NoBOM
  $whatIfExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $fixture, '-Mode', 'Verify', '-ConfigJsonPath', $badJson, '-WhatIf', '-OutputPath', $whatIfOut)
  $whatIfResult = Read-ResultJson $whatIfOut
  Assert-That 'whatif-does-not-read-config' ($whatIfExit -eq 0 -and $whatIfResult.status -eq 'PLANNED' -and $whatIfResult.networkCalls -eq 0)

  $verifyOut = Join-Path $work 'verify.json'
  $verifyExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $configPath $verifyOut)
  $verifyResult = Read-ResultJson $verifyOut
  Assert-That 'verify-remains-incomplete' ($verifyExit -eq 8 -and $verifyResult.status -eq 'INCOMPLETE_EVIDENCE' -and $verifyResult.mode -eq 'ReadOnly') "exit=$verifyExit status=$($verifyResult.status)"
  $evidence = $verifyResult['deploymentEvidence']
  $evidenceLevel = if ($evidence -is [System.Collections.IDictionary]) { $evidence['evidenceLevel'] } else { 'missing' }
  $runtimeNull = $evidence -is [System.Collections.IDictionary] -and $null -eq $evidence['runtimeEnvironment'] -and $null -eq $evidence['runtimeMounts'] -and $null -eq $evidence['schemaCompatibility'] -and $null -eq $evidence['workerReadiness'] -and $null -eq $evidence['rollbackPath']
  Assert-That 'verify-observes-source-only-runtime-null' ($evidenceLevel -eq 'SANITIZED_COMPOSE_MODEL_ONLY' -and $runtimeNull -and $null -eq $evidence.outboundHosts -and $null -eq $evidence.outboundEgressPolicy) "error=$($verifyResult.errorCode) evidenceLevel=$evidenceLevel"
  Assert-That 'verify-api-key-isolation-check-pass' (@($verifyResult.checks | Where-Object { $_.id -eq 'api-client-sites-key-isolated' -and $_.state -eq 'PASS' }).Count -eq 1)
  $composeGitBinding = @($evidence.sourceGitBindings | Where-Object { $_.path -eq 'infra/hostinger-acceptance/compose.yaml' -and $_.atHead -eq $true })
  Assert-That 'verify-exact-source-checkout-bound' (@($verifyResult.checks | Where-Object { $_.id -eq 'exact-checkout-source-binding' -and $_.state -eq 'PASS' }).Count -eq 1 -and $evidence.observedSourceSha -eq $headSha -and $evidence.sourceFilesAtHead -and $composeGitBinding.Count -eq 1 -and $composeGitBinding[0].gitBlobSha)
  $modelProvenancePresent = $evidence -is [System.Collections.IDictionary] -and $evidence.Contains('modelProvenance') -and $evidence.Contains('modelProvenanceState') -and $evidence.Contains('artifactProvenance') -and $evidence.Contains('artifactProvenanceState')
  $evidenceDescription = if ($evidence -is [System.Collections.IDictionary]) { @($evidence.Keys) -join ',' } elseif ($null -eq $evidence) { 'null' } else { $evidence.GetType().FullName }
  Assert-That 'verify-image-tag-is-not-artifact-provenance' ($modelProvenancePresent -and $null -eq $evidence.modelProvenance -and $evidence.modelProvenanceState -eq 'UNOBSERVED_OPERATOR_PROVIDED_MODEL' -and $null -eq $evidence.artifactProvenance -and $evidence.artifactProvenanceState -eq 'UNOBSERVED' -and $evidence.declaredImageTagSha -eq $headSha) "error=$($verifyResult.errorCode) evidence=$evidenceDescription"
  Assert-That 'verify-does-not-infer-egress-policy' (@($verifyResult.checks | Where-Object { $_.id -eq 'role-specific-outbound-host-policy' -and $_.state -eq 'MISSING' }).Count -eq 1)
  Assert-That 'verify-selected-plan-identities-match' (@($verifyResult.checks | Where-Object { $_.id -eq 'enabled-capability-plan-client-id-bindings' -and $_.state -eq 'PASS' }).Count -eq 1)
  Assert-That 'verify-planned-but-unselected-groups-not-required' (@($verifyResult.checks | Where-Object { $_.id -eq 'enabled-capability-credential-settings' -and $_.state -eq 'PASS' }).Count -eq 1 -and @($evidence.operatorDeclaredEnabledCapabilities).Count -eq 2)
  $apiSitesKeyModel = New-SyntheticComposeModel $headSha
  $apiSitesKeyModel.services.api.environment.ClientSites__SigningKey = '[REDACTED:CLIENT_SITES_SIGNING_KEY]'
  $apiSitesKeyPath = Join-Path $work 'compose-api-client-sites-key.json'
  $apiSitesKeyModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $apiSitesKeyPath -Encoding utf8NoBOM
  $apiSitesKeyOut = Join-Path $work 'api-client-sites-key.json'
  $apiSitesKeyExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $apiSitesKeyPath $apiSitesKeyOut)
  $apiSitesKeyResult = Read-ResultJson $apiSitesKeyOut
  Assert-That 'api-client-sites-signing-key-setting-always-denied' ($apiSitesKeyExit -eq 2 -and @($apiSitesKeyResult.checks | Where-Object { $_.id -eq 'api-client-sites-key-isolated' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $noEnvironmentOut = Join-Path $work 'verify-no-target-environment.json'
  $noEnvironmentExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $verifyPlanPath, '-Mode', 'Verify', '-ConfigJsonPath', $configPath, '-EnabledCapability', 'DIRECTORY_READER,DOCUMENTS', '-OutputPath', $noEnvironmentOut)
  $noEnvironmentResult = Read-ResultJson $noEnvironmentOut
  Assert-That 'verify-requires-explicit-operator-environment-assertion' ($noEnvironmentExit -eq 8 -and @($noEnvironmentResult.checks | Where-Object { $_.id -eq 'plan-and-operator-environment-assertion' -and $_.state -eq 'MISSING' }).Count -eq 1)

  $wrongEnvironmentOut = Join-Path $work 'verify-wrong-target-environment.json'
  $wrongEnvironmentExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $verifyPlanPath, '-Mode', 'Verify', '-ConfigJsonPath', $configPath, '-TargetEnvironment', 'Development', '-EnabledCapability', 'DIRECTORY_READER,DOCUMENTS', '-OutputPath', $wrongEnvironmentOut)
  $wrongEnvironmentResult = Read-ResultJson $wrongEnvironmentOut
  Assert-That 'verify-rejects-plan-environment-substitution' ($wrongEnvironmentExit -eq 2 -and @($wrongEnvironmentResult.checks | Where-Object { $_.id -eq 'plan-and-operator-environment-assertion' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $workerEnvironmentModel = New-SyntheticComposeModel $headSha
  $workerEnvironmentModel.services['worker-client-sites'].environment.DOTNET_ENVIRONMENT = 'Development'
  $workerEnvironmentPath = Join-Path $work 'compose-worker-environment-mismatch.json'
  $workerEnvironmentModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $workerEnvironmentPath -Encoding utf8NoBOM
  $workerEnvironmentOut = Join-Path $work 'worker-environment-mismatch.json'
  $workerEnvironmentExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $workerEnvironmentPath $workerEnvironmentOut)
  $workerEnvironmentResult = Read-ResultJson $workerEnvironmentOut
  Assert-That 'all-declared-worker-environments-are-checked' ($workerEnvironmentExit -eq 2 -and @($workerEnvironmentResult.checks | Where-Object { $_.id -eq 'all-declared-worker-environments-explicit' -and $_.state -eq 'FAIL' }).Count -eq 1)
  $disabledModel = New-SyntheticComposeModel $headSha
  $disabledModel.services.api.environment.Remove('TenantAdministration__OutboundMail__ClientId')
  $disabledModel.services['worker-mail'].environment.Remove('GraphMail__ClientId')
  $disabledModel.services['worker-mail'].environment.Remove('GraphMail__PrivateKeyPath')
  $disabledModel.services['worker-mail'].environment.Remove('GraphMail__CertificatePath')
  $disabledModel.services.api.environment.Remove('SelectedSite__ClientId')
  $disabledModel.services.api.environment.Remove('SelectedSite__PrivateKeyPath')
  $disabledModel.services.api.environment.Remove('SelectedSite__CertificatePath')
  $disabledModel.services['worker-pbc'].environment.Remove('SelectedSite__ClientId')
  $disabledModel.services['worker-pbc'].environment.Remove('SelectedSite__PrivateKeyPath')
  $disabledModel.services['worker-pbc'].environment.Remove('SelectedSite__CertificatePath')
  $disabledModel.services['worker-client-sites'].environment.Remove('ClientSites__ClientId')
  $disabledModel.services['worker-client-sites'].environment.Remove('ClientSites__PrivateKeyPath')
  $disabledModel.services['worker-client-sites'].environment.Remove('ClientSites__CertificatePath')
  $disabledModel.services['worker-client-sites'].environment.Remove('DirectoryReader__PrivateKeyPath')
  $disabledModel.services['worker-client-sites'].environment.Remove('DirectoryReader__CertificatePath')
  $disabledPath = Join-Path $work 'compose-disabled-groups.json'
  $disabledModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $disabledPath -Encoding utf8NoBOM
  $disabledOut = Join-Path $work 'disabled-groups.json'
  $disabledExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $disabledPath $disabledOut @('DIRECTORY_READER'))
  $disabledResult = Read-ResultJson $disabledOut
  Assert-That 'planned-mail-pbc-and-client-sites-may-be-disabled-without-credentials' ($disabledExit -eq 8 -and $disabledResult.status -eq 'INCOMPLETE_EVIDENCE' -and @($disabledResult.checks | Where-Object { $_.id -eq 'enabled-capability-credential-settings' -and $_.state -eq 'PASS' }).Count -eq 1 -and @($disabledResult.checks | Where-Object state -eq 'FAIL').Count -eq 0)
  & python3 (Join-Path $root 'scripts/m365/verify-result.py') $verifyOut --expect INCOMPLETE_EVIDENCE | Out-Null
  $verifyEnvelopeExit = $LASTEXITCODE
  Assert-That 'verify-result-envelope-valid' ($verifyEnvelopeExit -eq 0)

  $broadModel = New-SyntheticComposeModel $plan.repositoryCommit
  $broadModel.services.api.volumes += (New-Bind '/opt/auditsphere/storage/m365-credentials' '/run/auditsphere/m365')
  $broadPath = Join-Path $work 'compose-broad.json'
  $broadModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $broadPath -Encoding utf8NoBOM
  $broadOut = Join-Path $work 'broad.json'
  $broadExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $broadPath $broadOut)
  $broadResult = Read-ResultJson $broadOut
  Assert-That 'broad-parent-mount-detected' ($broadExit -eq 2 -and $broadResult.status -eq 'DRIFT' -and @($broadResult.checks | Where-Object { $_.id -eq 'api-client-sites-key-isolated' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $coreMountModel = New-SyntheticComposeModel $headSha
  $coreMountModel.services['worker-general'].volumes = @((New-Bind '/opt/auditsphere/storage/m365-credentials/client-sites' '/run/auditsphere/m365/client-sites'))
  $coreMountPath = Join-Path $work 'compose-core-worker-credential-mount.json'
  $coreMountModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $coreMountPath -Encoding utf8NoBOM
  $coreMountOut = Join-Path $work 'core-worker-mount.json'
  $coreMountExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $coreMountPath $coreMountOut)
  $coreMountResult = Read-ResultJson $coreMountOut
  Assert-That 'core-worker-credential-mount-denied' ($coreMountExit -eq 2 -and @($coreMountResult.checks | Where-Object { $_.id -eq 'role-specific-readonly-credential-mount-isolation' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $extraMountModel = New-SyntheticComposeModel $headSha
  $extraMountModel.services['unexpected-helper'] = [ordered]@{ volumes = @((New-Bind '/opt/auditsphere/storage/m365-credentials/client-sites' '/run/auditsphere/m365/client-sites')) }
  $extraMountPath = Join-Path $work 'compose-extra-service-credential-mount.json'
  $extraMountModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $extraMountPath -Encoding utf8NoBOM
  $extraMountOut = Join-Path $work 'extra-service-mount.json'
  $extraMountExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $extraMountPath $extraMountOut)
  $extraMountResult = Read-ResultJson $extraMountOut
  Assert-That 'unexpected-service-credential-mount-denied' ($extraMountExit -eq 2 -and @($extraMountResult.checks | Where-Object { $_.id -eq 'role-specific-readonly-credential-mount-isolation' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $unsafeMountModel = New-SyntheticComposeModel $headSha
  $unsafeMountModel.services.api.volumes += (New-Bind '/private/arbitrary-secret-material' '/run/auditsphere/m365/client-sites')
  $unsafeMountPath = Join-Path $work 'compose-unsafe-mount-reference.json'
  $unsafeMountModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $unsafeMountPath -Encoding utf8NoBOM
  $unsafeMountOut = Join-Path $work 'unsafe-mount-reference.json'
  $unsafeMountExit = Invoke-EntryPoint 'Export-DeploymentSettings' (Get-VerifyArguments $verifyPlanPath $unsafeMountPath $unsafeMountOut)
  $unsafeMountResult = Read-ResultJson $unsafeMountOut
  $unsafeMountJson = Get-Content -LiteralPath $unsafeMountOut -Raw
  Assert-That 'untrusted-mount-path-withheld-from-export' ($unsafeMountExit -eq 2 -and $unsafeMountResult.status -eq 'DRIFT' -and -not $unsafeMountJson.Contains('/private/arbitrary-secret-material'))

  $mismatchedModel = New-SyntheticComposeModel $plan.repositoryCommit
  $mismatchedModel.services.api.environment.DirectoryReader__ClientId = '00000000-0000-4000-8000-000000000199'
  $mismatchedPath = Join-Path $work 'compose-plan-id-mismatch.json'
  $mismatchedModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $mismatchedPath -Encoding utf8NoBOM
  $mismatchedOut = Join-Path $work 'plan-id-mismatch.json'
  $mismatchedExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $mismatchedPath $mismatchedOut)
  $mismatchedResult = Read-ResultJson $mismatchedOut
  Assert-That 'planned-capability-id-mismatch-detected' ($mismatchedExit -eq 2 -and $mismatchedResult.status -eq 'DRIFT' -and @($mismatchedResult.checks | Where-Object { $_.id -eq 'enabled-capability-plan-client-id-bindings' -and $_.state -eq 'FAIL' }).Count -eq 1)

  $wrongSourcePlan = ($verifyPlan | ConvertTo-Json -Depth 32 -Compress) | ConvertFrom-Json -AsHashtable -Depth 32
  $wrongSourcePlan.repositoryCommit = '0000000000000000000000000000000000000000'
  $wrongSourcePath = Join-Path $work 'plan-wrong-source-sha.json'
  $wrongSourcePlan | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $wrongSourcePath -Encoding utf8NoBOM
  $wrongSourceOut = Join-Path $work 'verify-wrong-source-sha.json'
  $wrongSourceExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $wrongSourcePath $configPath $wrongSourceOut)
  $wrongSourceResult = Read-ResultJson $wrongSourceOut
  Assert-That 'expected-source-sha-must-match-observed-checkout' ($wrongSourceExit -eq 2 -and @($wrongSourceResult.checks | Where-Object { $_.id -eq 'exact-checkout-source-binding' -and $_.state -eq 'FAIL' }).Count -eq 1 -and $wrongSourceResult.deploymentEvidence.sourceFilesAtHead -eq $false)

  $unsafeModel = New-SyntheticComposeModel $plan.repositoryCommit
  $unsafeModel.services.api.environment.Identity__ClientSecret = 'deployment-test-secret-sentinel'
  $unsafePath = Join-Path $work 'compose-unsanitized.json'
  $unsafeModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $unsafePath -Encoding utf8NoBOM
  $unsafeOut = Join-Path $work 'unsanitized.json'
  $unsafeExit = Invoke-EntryPoint 'Test-DeploymentBindings' (Get-VerifyArguments $verifyPlanPath $unsafePath $unsafeOut)
  $unsafeResult = Read-ResultJson $unsafeOut
  Assert-That 'raw-secret-refused-without-echo' ($unsafeExit -eq 4 -and $unsafeResult.status -eq 'INVALID_OR_UNSAFE' -and $unsafeResult.errorCode -eq 'compose-config-not-sanitized' -and -not (Get-Content -LiteralPath $unsafeOut -Raw).Contains('deployment-test-secret-sentinel'))

  $secretClassModel = New-SyntheticComposeModel $headSha
  $secretClassModel.services.api.environment.Integration__ApiKeyBase64 = 'raw-api-key-sentinel'
  $secretClassModel.services.api.environment.Application__SigningKeyPem = 'raw-signing-key-sentinel'
  $secretClassPath = Join-Path $work 'compose-unredacted-key-classes.json'
  $secretClassModel | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $secretClassPath -Encoding utf8NoBOM
  $secretClassOut = Join-Path $work 'unredacted-key-classes.json'
  $secretClassExit = Invoke-EntryPoint 'Export-DeploymentSettings' (Get-VerifyArguments $verifyPlanPath $secretClassPath $secretClassOut)
  $secretClassResult = Read-ResultJson $secretClassOut
  $secretClassJson = Get-Content -LiteralPath $secretClassOut -Raw
  Assert-That 'api-and-signing-key-classes-are-redaction-guarded' ($secretClassExit -eq 4 -and $secretClassResult.status -eq 'INVALID_OR_UNSAFE' -and -not $secretClassJson.Contains('raw-api-key-sentinel') -and -not $secretClassJson.Contains('raw-signing-key-sentinel'))

  $applyOut = Join-Path $work 'apply.json'
  $applyExit = Invoke-EntryPoint 'Test-DeploymentBindings' @('-PlanPath', $fixture, '-Mode', 'Apply', '-OutputPath', $applyOut)
  $applyResult = Read-ResultJson $applyOut
  Assert-That 'apply-refused-before-provider-or-docker' ($applyExit -eq 4 -and $applyResult.status -eq 'INVALID_OR_UNSAFE' -and $applyResult.errorCode -eq 'deployment-apply-not-supported' -and $applyResult.networkCalls -eq 0 -and $applyResult.mutationsAttempted -eq 0)

  $exportPlanOut = Join-Path $work 'export-plan.json'
  $exportPlanExit = Invoke-EntryPoint 'Export-DeploymentSettings' @('-PlanPath', $fixture, '-DryRun', '-OutputPath', $exportPlanOut)
  $exportPlan = Read-ResultJson $exportPlanOut
  Assert-That 'export-plan-is-planned' ($exportPlanExit -eq 0 -and $exportPlan.status -eq 'PLANNED' -and @($exportPlan.deploymentSettings.plannedCredentialBindings | Where-Object reference -eq 'file:/run/secrets/synthetic-directory.pem').Count -eq 1)

  $exportVerifyOut = Join-Path $work 'export-verify.json'
  $exportVerifyExit = Invoke-EntryPoint 'Export-DeploymentSettings' (Get-VerifyArguments $verifyPlanPath $configPath $exportVerifyOut)
  $exportVerify = Read-ResultJson $exportVerifyOut
  $exportJson = Get-Content -LiteralPath $exportVerifyOut -Raw
  Assert-That 'export-only-public-ids-and-credential-references' ($exportVerifyExit -eq 8 -and $exportVerify.status -eq 'INCOMPLETE_EVIDENCE' -and $exportJson.Contains('IDENTITY_CLIENT_SECRET') -and $exportJson.Contains('/run/auditsphere/m365/client-sites/private-key.pem') -and -not $exportJson.Contains('[REDACTED:POSTGRES_PASSWORD]'))
  & python3 (Join-Path $root 'scripts/m365/verify-result.py') $exportVerifyOut --expect INCOMPLETE_EVIDENCE | Out-Null
  $exportEnvelopeExit = $LASTEXITCODE
  Assert-That 'export-envelope-has-no-unsafe-field-names' ($exportEnvelopeExit -eq 0)

  $exportUnsafeOut = Join-Path $work 'export-unsanitized.json'
  $exportUnsafeExit = Invoke-EntryPoint 'Export-DeploymentSettings' (Get-VerifyArguments $verifyPlanPath $unsafePath $exportUnsafeOut)
  $exportUnsafe = Read-ResultJson $exportUnsafeOut
  Assert-That 'export-refuses-unredacted-config-without-echo' ($exportUnsafeExit -eq 4 -and $exportUnsafe.status -eq 'INVALID_OR_UNSAFE' -and $exportUnsafe.errorCode -eq 'compose-config-not-sanitized' -and -not (Get-Content -LiteralPath $exportUnsafeOut -Raw).Contains('deployment-test-secret-sentinel'))
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }

$summary = [ordered]@{ unit = 'WU-024'; offline = $true; passed = $passed; failed = $failures.Count; failures = @($failures) }
$summary | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
if ($passed -lt 10) { Write-Error 'Too few assertions ran; the run would be vacuous.'; exit 1 }
exit 0
