#Requires -Version 7.4
# Offline focused tests: real enrollment GET transport with an in-memory HTTP handler only.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }

$root = Split-Path $PSScriptRoot -Parent
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $root '../..'))
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Enrollment.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Readiness.psm1') -Force

Add-Type -TypeDefinition @'
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class ReadinessQueueHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>();
    public readonly List<string> Methods = new List<string>();
    public readonly List<string> Uris = new List<string>();
    public readonly List<string> Bodies = new List<string>();
    public int SendCount => Methods.Count;
    public void Enqueue(HttpResponseMessage response) => responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Methods.Add(request.Method.Method);
        Uris.Add(request.RequestUri?.AbsoluteUri);
        Bodies.Add(request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        if (responses.Count == 0) throw new HttpRequestException("synthetic response queue exhausted");
        var response = responses.Dequeue();
        if (response.RequestMessage == null) response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
'@

$script:assertions = 0
$script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-readiness-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($script:work) | Out-Null
$script:plan = Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json')
# These are the API wire values in Domain/Microsoft365/TenantAdministration.cs,
# serialized by CapabilityStatus.Capability; they intentionally differ from plan kinds.
$script:apiCapabilityKeys = @{
  DIRECTORY_READER = 'DIRECTORY_READ'; MAIL = 'OUTBOUND_MAIL'; LOGIN = 'SIGN_IN'
  USER_CREATE = 'TENANT_USER_PROVISIONING'; INVITATIONS = 'GUEST_INVITATION'
  DOCUMENTS = 'SELECTED_SITE'; GROUP_MEMBERSHIP = 'GROUP_MEMBERSHIP'
}
$script:now = '2026-10-23T09:30:00.0000000+03:00'
$script:normalizedNow = '2026-10-23T06:30:00.0000000Z'

function Assert-That([string] $Name, [bool] $Condition, [string] $Detail = '') {
  if (-not $Condition) { throw "ASSERT_FAILED:$Name $Detail" }
  $script:assertions++
}

function New-JsonResponse([int] $StatusCode, $Value) {
  $response = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode] $StatusCode)
  if ($null -ne $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 32 -Compress
    $response.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, 'application/json')
  }
  return $response
}

function Read-ResultJson([string] $Path) {
  $parse = @{ AsHashtable = $true; Depth = 32 }
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
  return Get-Content -Raw $Path | ConvertFrom-Json @parse
}

function New-Session([string] $UserId = '00000000-0000-4000-8000-000000000020', [string] $Generation = '0', [bool] $Staff = $true) {
  return @{ userId = $UserId; firmId = $script:plan.firmId; generation = $Generation; staff = $Staff }
}

function New-Workspace([switch] $WithoutCapabilityRows, [string] $TenantId = $script:plan.tenantId, [bool] $Simulation = $false) {
  $statuses = @()
  if (-not $WithoutCapabilityRows) {
    foreach ($capability in $script:plan.capabilities) {
      $statuses += ,@{
        capability = $script:apiCapabilityKeys[$capability.kind]; displayName = $capability.kind; permission = 'synthetic permission label'
        enabled = $true; state = 'VERIFIED'; lastVerifiedAt = $script:now; stale = $false; diagnosticCode = ''
      }
    }
  }
  return @{
    workspace = @{
      connection = @{
        draftId = '00000000-0000-4000-8000-000000000080'; expectedTenantId = $TenantId
        connectionState = 'CONNECTED'; consentState = 'VERIFIED'; lastAttemptState = 'CONSENT_VERIFIED'
        lastAttemptAt = $script:now; directoryCapabilityState = 'VERIFIED_RECENT'; directoryLastCheckedAt = $script:now
      }
      capabilities = $statuses
      draftRevision = '4'
    }
    consentConfigured = $true; directoryConfigured = $true; configuredTenantId = $TenantId
    mailAuthorizationEnrollmentAvailable = $false
    preparationConfigured = $true; simulation = $Simulation
  }
}

function New-MailAuthorizationPlan {
  $plan = $script:plan.Clone()
  $capability = $script:plan.capabilities[0].Clone()
  $capability.kind = 'MAIL'
  $capability.expectedApplicationRoles = @()
  $plan.capabilities = @($capability)
  $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050')
    controlMailboxObjectId = '00000000-0000-4000-8000-000000000051' }
  return $plan
}

function New-MailAuthorizationWorkspace([bool] $EnrollmentAvailable = $true, $Evidence = $null, [string] $State = 'VERIFIED') {
  $row = @{ capability = 'OUTBOUND_MAIL'; displayName = 'MAIL'; permission = 'Mail.Send'; enabled = $true
    state = $State; lastVerifiedAt = $script:now; stale = $false; diagnosticCode = '' }
  if ($null -ne $Evidence) { $row.authorizationEvidence = $Evidence }
  return @{ mailAuthorizationEnrollmentAvailable = $EnrollmentAvailable; workspace = @{ capabilities = @($row) } }
}

function New-ResponseSequence([switch] $WithoutCapabilityRows, [string] $AfterUserId = '00000000-0000-4000-8000-000000000020',
    [string] $TenantId = $script:plan.tenantId, [bool] $Simulation = $false) {
  return @(
    (New-JsonResponse 200 (New-Session))
    (New-JsonResponse 200 (New-Workspace -WithoutCapabilityRows:$WithoutCapabilityRows -TenantId $TenantId -Simulation $Simulation))
    (New-JsonResponse 200 (New-Session -UserId $AfterUserId))
  )
}

function Invoke-SyntheticReadback([object[]] $Responses, [string] $CaseName, $Plan = $script:plan) {
  Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
  Set-M365OfflineMode $false
  $handler = [ReadinessQueueHandler]::new()
  foreach ($response in $Responses) { $handler.Enqueue($response) }
  $client = [System.Net.Http.HttpClient]::new($handler, $false)
  $client.Timeout = [TimeSpan]::FromSeconds(10)
  $client.MaxResponseContentBufferSize = 262144
  try {
    $transport = New-M365EnrollmentTransport $client ([uri] $Plan.publicBaseUri) `
      '.AspNetCore.Cookies=synthetic-readonly-cookie' (Get-Command Invoke-M365ProviderRequest)
    $result = New-M365Result 'ReadOnly'
    $result.repositoryCommit = $Plan.repositoryCommit
    $result.tenantId = $Plan.tenantId
    $result.planSha256 = Get-M365PlanSha256 $Plan
    $status = Invoke-M365ReadinessReadback $Plan $result $transport
    $output = Join-Path $script:work "$CaseName.result.json"
    $exitCode = Complete-M365Result $result $status $output
    $saved = Read-ResultJson $output
    return @{ Result = $saved; Handler = $handler; Path = $output; ExitCode = $exitCode }
  } finally {
    $client.Dispose()
    Set-M365OfflineMode $true
  }
}

try {
  $planOutput = Join-Path $script:work 'plan.json'
  $whatIfOutput = Join-Path $script:work 'whatif.json'
  $verifyAliasOutput = Join-Path $script:work 'verify-alias.json'
  $applyOutput = Join-Path $script:work 'apply-refused.json'
  $applyWhatIfOutput = Join-Path $script:work 'apply-whatif.json'
  $engine = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
  & $engine -NoProfile -NonInteractive -File (Join-Path $root 'Verify-M365Readiness.ps1') `
    -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode Plan -OutputPath $planOutput | Out-Null
  $planExit = $LASTEXITCODE
  $planned = Read-ResultJson $planOutput
  Assert-That 'offline-plan-zero-effects' ($planExit -eq 0 -and $planned.status -eq 'PLANNED' -and
    $planned.networkCalls -eq 0 -and $planned.mutationsAttempted -eq 0 -and $planned.mutationsConfirmed -eq 0)
  Assert-That 'offline-plan-has-per-capability-null-runtime-evidence' (@($planned.checks | Where-Object {
    $_.capability -and $null -eq $_.observedIdentity -and $null -eq $_.observedRoles -and $null -eq $_.observedResources -and
      $null -eq $_.expiresAtUtc -and $null -eq $_.positiveProbe -and $null -eq $_.negativeProbe
  }).Count -eq $script:plan.capabilities.Count)
  & $engine -NoProfile -NonInteractive -File (Join-Path $root 'Verify-M365Readiness.ps1') `
    -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode ReadOnly -WhatIf -OutputPath $whatIfOutput | Out-Null
  $whatIfExit = $LASTEXITCODE
  $whatIfResult = Read-ResultJson $whatIfOutput
  Assert-That 'whatif-remains-offline-plan' ($whatIfExit -eq 0 -and $whatIfResult.mode -eq 'Plan' -and
    $whatIfResult.status -eq 'PLANNED' -and $whatIfResult.networkCalls -eq 0 -and $whatIfResult.mutationsAttempted -eq 0)

  & $engine -NoProfile -NonInteractive -File (Join-Path $root 'Verify-M365Readiness.ps1') `
    -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode Verify -OutputPath $verifyAliasOutput | Out-Null
  $verifyAliasExit = $LASTEXITCODE
  $verifyAliasResult = Read-ResultJson $verifyAliasOutput
  Assert-That 'verify-alias-normalizes-to-readonly-before-bootstrap' ($verifyAliasExit -ne 0 -and
    $verifyAliasResult.mode -eq 'ReadOnly' -and $verifyAliasResult.status -eq 'BLOCKED_BOOTSTRAP' -and
    $verifyAliasResult.errorCode -eq 'readiness-bootstrap-required' -and $verifyAliasResult.networkCalls -eq 0 -and
    $verifyAliasResult.mutationsAttempted -eq 0)

  & $engine -NoProfile -NonInteractive -File (Join-Path $root 'Verify-M365Readiness.ps1') `
    -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode Apply `
    -SessionCookieEnvironmentVariable 'AS_WU025_APPLY_REFUSAL_SENTINEL' -OutputPath $applyOutput | Out-Null
  $applyExit = $LASTEXITCODE
  $applyResult = Read-ResultJson $applyOutput
  Assert-That 'apply-refused-before-credentials-or-network' ($applyExit -ne 0 -and $applyResult.mode -eq 'Apply' -and
    $applyResult.status -eq 'INVALID_OR_UNSAFE' -and $applyResult.errorCode -eq 'readiness-apply-not-supported' -and
    $applyResult.networkCalls -eq 0 -and $applyResult.mutationsAttempted -eq 0 -and $applyResult.mutationsConfirmed -eq 0)

  & $engine -NoProfile -NonInteractive -File (Join-Path $root 'Verify-M365Readiness.ps1') `
    -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode Apply -WhatIf -OutputPath $applyWhatIfOutput | Out-Null
  $applyWhatIfExit = $LASTEXITCODE
  $applyWhatIfResult = Read-ResultJson $applyWhatIfOutput
  Assert-That 'apply-whatif-stays-offline-planned' ($applyWhatIfExit -eq 0 -and $applyWhatIfResult.mode -eq 'Plan' -and
    $applyWhatIfResult.status -eq 'PLANNED' -and $applyWhatIfResult.networkCalls -eq 0 -and
    $applyWhatIfResult.mutationsAttempted -eq 0)

  $nominal = Invoke-SyntheticReadback (New-ResponseSequence) 'nominal'
  $apiVerifiedRow = @($nominal.Result.checks | Where-Object { $_.capability -eq 'DIRECTORY_READER' })[0]
  Assert-That 'api-verified-summary-not-promoted' ($nominal.ExitCode -eq 8 -and $nominal.Result.status -eq 'INCOMPLETE_EVIDENCE' -and
    $apiVerifiedRow.state -eq 'INCOMPLETE_EVIDENCE' -and $apiVerifiedRow.apiCapabilityObservation.state -eq 'VERIFIED' -and
    $apiVerifiedRow.capability -eq 'DIRECTORY_READER' -and $apiVerifiedRow.apiCapabilityKey -eq 'DIRECTORY_READ' -and
    $apiVerifiedRow.apiCapabilityObservation.capability -eq 'DIRECTORY_READ')
  Assert-That 'identity-role-resource-expiry-and-probes-remain-null' ($null -eq $apiVerifiedRow.observedIdentity -and
    $null -eq $apiVerifiedRow.observedRoles -and $null -eq $apiVerifiedRow.observedResources -and
    $null -eq $apiVerifiedRow.expiresAtUtc -and $null -eq $apiVerifiedRow.positiveProbe -and $null -eq $apiVerifiedRow.negativeProbe)
  Assert-That 'missing-evidence-has-source-owned-blockers' (@(
    'api-workspace:runtime-client-identity-not-projected', 'api-workspace:observed-roles-not-projected',
    'api-workspace:evidence-expiry-not-projected', 'api-workspace:positive-probe-not-projected',
    'api-workspace:negative-probe-not-projected'
  | Where-Object { $_ -notin $apiVerifiedRow.sourceOwnedBlockers }).Count -eq 0)
  Assert-That 'expected-plan-fields-are-marked-expected-only' ($apiVerifiedRow.expectedIdentity.clientId -eq $script:plan.capabilities[0].clientId -and
    $apiVerifiedRow.expectedRoles[0].value -eq 'User.Read.All' -and $null -eq $apiVerifiedRow.observedRoles)
  Assert-That 'source-timestamps-normalized-utc-and-resume-identities-not-promoted' ($apiVerifiedRow.observedAtUtc -eq $script:normalizedNow -and
    $apiVerifiedRow.apiCapabilityObservation.lastVerifiedAtUtc -eq $script:normalizedNow -and
    $null -eq $apiVerifiedRow.expiresAtUtc -and $null -eq $apiVerifiedRow.configurationDigest -and
    $apiVerifiedRow.resumeIdentity.draftId -eq '00000000-0000-4000-8000-000000000080' -and
    $null -eq $apiVerifiedRow.resumeIdentity.connectionRevisionId -and
    $null -eq $apiVerifiedRow.resumeIdentity.consentAttemptId -and $null -eq $apiVerifiedRow.resumeIdentity.operationId)
  $workspaceCheck = @($nominal.Result.checks | Where-Object { $_.id -eq 'readiness:api-workspace' })[0]
  $workspaceMetadata = $workspaceCheck.apiWorkspaceMetadata
  Assert-That 'environment-is-expected-only-until-api-projects-deployment-environment' ($apiVerifiedRow.expectedEnvironment -eq $script:plan.environment -and
    $null -eq $apiVerifiedRow.observedEnvironment -and $workspaceCheck.expectedEnvironment -eq $script:plan.environment -and
    $null -eq $workspaceCheck.observedEnvironment -and
    $workspaceCheck.sourceOwnedBlockers -contains 'api-workspace:deployment-environment-not-projected')
  Assert-That 'workspace-utc-labelled-metadata-normalized-without-inventing-expiry' ($workspaceMetadata.lastAttemptAtUtc -eq $script:normalizedNow -and
    $workspaceMetadata.directoryLastCheckedAtUtc -eq $script:normalizedNow -and
    $workspaceMetadata.simulation -eq $false -and @($nominal.Result.checks | Where-Object { $null -ne $_.expiresAtUtc }).Count -eq 0)

  $simulation = Invoke-SyntheticReadback (New-ResponseSequence -Simulation $true) 'simulation-mode'
  $simulationWorkspaceCheck = @($simulation.Result.checks | Where-Object { $_.id -eq 'readiness:api-workspace' })[0]
  $simulationCapabilityCheck = @($simulation.Result.checks | Where-Object { $_.capability -eq 'DIRECTORY_READER' })[0]
  Assert-That 'simulation-is-observed-but-never-runtime-environment-proof' ($simulation.Result.status -eq 'INCOMPLETE_EVIDENCE' -and
    $simulationWorkspaceCheck.apiWorkspaceMetadata.simulation -eq $true -and
    $simulationWorkspaceCheck.sourceOwnedBlockers -contains 'api-workspace:simulation-mode-is-not-runtime-evidence' -and
    $simulationWorkspaceCheck.expectedEnvironment -eq $script:plan.environment -and
    $null -eq $simulationWorkspaceCheck.observedEnvironment -and $null -eq $simulationCapabilityCheck.observedEnvironment)
  $unoffsetWorkspace = New-Workspace
  $unoffsetWorkspace.workspace.capabilities[0].lastVerifiedAt = '2026-10-23T09:30:00'
  $unoffsetResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $unoffsetResult $script:plan $unoffsetWorkspace 'INCOMPLETE_EVIDENCE'
  $unoffsetCheck = $unoffsetResult.checks[0]
  Assert-That 'timezone-unspecified-metadata-is-not-labelled-utc' ($null -eq $unoffsetCheck.observedAtUtc -and
    $null -eq $unoffsetCheck.apiCapabilityObservation.lastVerifiedAtUtc -and
    $unoffsetCheck.sourceOwnedBlockers -contains 'api-workspace:DIRECTORY_READER:capability-owner-observation-time-missing')

  $mappingCases = @(
    @{ PlanKind = 'DIRECTORY_READER'; ApiKey = 'DIRECTORY_READ' },
    @{ PlanKind = 'MAIL'; ApiKey = 'OUTBOUND_MAIL' },
    @{ PlanKind = 'LOGIN'; ApiKey = 'SIGN_IN' },
    @{ PlanKind = 'USER_CREATE'; ApiKey = 'TENANT_USER_PROVISIONING' },
    @{ PlanKind = 'INVITATIONS'; ApiKey = 'GUEST_INVITATION' },
    @{ PlanKind = 'DOCUMENTS'; ApiKey = 'SELECTED_SITE' },
    @{ PlanKind = 'GROUP_MEMBERSHIP'; ApiKey = 'GROUP_MEMBERSHIP' }
  )
  foreach ($mappingCase in $mappingCases) {
    $mappingCapability = $script:plan.capabilities[0].Clone()
    $mappingCapability.kind = $mappingCase.PlanKind
    $mappingPlan = $script:plan.Clone()
    $mappingPlan.capabilities = @($mappingCapability)
    $mappingResult = New-M365Result 'ReadOnly'
    Add-M365ReadinessCapabilityChecks $mappingResult $mappingPlan (New-Workspace) 'INCOMPLETE_EVIDENCE'
    $mappingCheck = $mappingResult.checks[0]
    Assert-That "plan-kind-maps-to-api-wire-key-$($mappingCase.PlanKind)" ($mappingCheck.capability -eq $mappingCase.PlanKind -and
      $mappingCheck.apiCapabilityKey -eq $mappingCase.ApiKey)
  }

  $mailPlan = New-MailAuthorizationPlan
  $mailExpiry = [DateTimeOffset]::UtcNow.AddHours(1)
  $mailEvidence = @{ receiptId = '00000000-0000-4000-8000-000000000090'; model = 'LEGACY_APPLICATION_ACCESS_POLICY'
    attestationSha256 = 'a' * 64; bindingSha256 = 'b' * 64; expiresAtUtc = $mailExpiry.ToString('o')
    tenantId = $mailPlan.tenantId; runtimeClientId = $mailPlan.capabilities[0].clientId
    connectionRevisionId = '00000000-0000-4000-8000-000000000060'; consentAttemptId = '00000000-0000-4000-8000-000000000061'
    providerSnapshotSha256 = 'c' * 64; planSha256 = Get-M365PlanSha256 $mailPlan
    sourceCommit = $mailPlan.repositoryCommit; senderMailboxObjectIds = @($mailPlan.mail.senderObjectIds)
    controlMailboxObjectId = $mailPlan.mail.controlMailboxObjectId; evidenceObservedAtUtc = $script:now }
  $mailWorkspace = New-MailAuthorizationWorkspace -Evidence $mailEvidence
  $mailResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $mailResult $mailPlan $mailWorkspace 'INCOMPLETE_EVIDENCE'
  $mailCheck = $mailResult.checks[0]
  $expectedMailExpiry = $mailExpiry.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
  $projectedMailEvidence = if ($null -ne $mailCheck.apiCapabilityObservation) { $mailCheck.apiCapabilityObservation['authorizationEvidence'] } else { $null }
  $mailEvidenceDetail = "planModel=$($mailPlan.mail.permissionModel); enrollment=$($mailWorkspace.mailAuthorizationEnrollmentAvailable); blockers=$($mailCheck.sourceOwnedBlockers -join ',')"
  Assert-That 'mail-receipt-projects-only-exact-bound-canonical-fields' ($mailCheck.state -eq 'INCOMPLETE_EVIDENCE' -and
    $null -ne $projectedMailEvidence -and $projectedMailEvidence.receiptId -eq $mailEvidence.receiptId -and
    $projectedMailEvidence.model -eq 'LEGACY_APPLICATION_ACCESS_POLICY' -and
    $projectedMailEvidence.attestationSha256 -eq $mailEvidence.attestationSha256 -and
    $projectedMailEvidence.bindingSha256 -eq $mailEvidence.bindingSha256 -and
    $projectedMailEvidence.expiresAtUtc -eq $expectedMailExpiry -and
    $projectedMailEvidence.tenantId -eq $mailPlan.tenantId -and
    $projectedMailEvidence.runtimeClientId -eq $mailPlan.capabilities[0].clientId -and
    $projectedMailEvidence.connectionRevisionId -eq $mailEvidence.connectionRevisionId -and
    $projectedMailEvidence.consentAttemptId -eq $mailEvidence.consentAttemptId -and
    $projectedMailEvidence.providerSnapshotSha256 -eq $mailEvidence.providerSnapshotSha256 -and
    $projectedMailEvidence.planSha256 -eq (Get-M365PlanSha256 $mailPlan) -and
    $projectedMailEvidence.sourceCommit -eq $mailPlan.repositoryCommit -and
    $projectedMailEvidence.senderMailboxObjectIds.Count -eq 1 -and
    $projectedMailEvidence.senderMailboxObjectIds[0] -eq $mailPlan.mail.senderObjectIds[0] -and
    $projectedMailEvidence.controlMailboxObjectId -eq $mailPlan.mail.controlMailboxObjectId -and
    $projectedMailEvidence.evidenceObservedAtUtc -eq '2026-10-23T06:30:00.0000000Z' -and
    $mailCheck.observed -like '*does not prove live send or delivery*' -and
    $mailCheck.sourceOwnedBlockers -contains 'api-workspace:runtime-client-identity-not-projected') $mailEvidenceDetail

  $mailBindingMismatchCases = @(
    @{ Name = 'tenant'; Field = 'tenantId'; Value = '00000000-0000-4000-8000-000000000099' },
    @{ Name = 'app'; Field = 'runtimeClientId'; Value = '00000000-0000-4000-8000-000000000099' },
    @{ Name = 'sender'; Field = 'senderMailboxObjectIds'; Value = @('00000000-0000-4000-8000-000000000099') },
    @{ Name = 'control'; Field = 'controlMailboxObjectId'; Value = '00000000-0000-4000-8000-000000000099' },
    @{ Name = 'plan'; Field = 'planSha256'; Value = 'd' * 64 },
    @{ Name = 'source'; Field = 'sourceCommit'; Value = '1111111111111111111111111111111111111111' }
  )
  foreach ($case in $mailBindingMismatchCases) {
    $mismatchedEvidence = $mailEvidence.Clone()
    $mismatchedEvidence[$case.Field] = $case.Value
    $mismatchResult = New-M365Result 'ReadOnly'
    Add-M365ReadinessCapabilityChecks $mismatchResult $mailPlan (New-MailAuthorizationWorkspace -Evidence $mismatchedEvidence) 'INCOMPLETE_EVIDENCE'
    $mismatchCheck = $mismatchResult.checks[0]
    Assert-That "verified-mail-row-rejects-mismatched-$($case.Name)-binding" (-not $mismatchCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
      $mismatchCheck.apiCapabilityObservation.state -eq 'VERIFIED' -and
      $mismatchCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:authorization-evidence-plan-binding-mismatch')
  }

  $staleEvidenceResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $staleEvidenceResult $mailPlan (New-MailAuthorizationWorkspace -Evidence $mailEvidence -State 'STALE') 'INCOMPLETE_EVIDENCE'
  $staleEvidenceCheck = $staleEvidenceResult.checks[0]
  Assert-That 'mail-receipt-not-projected-for-stale-capability-row' (-not $staleEvidenceCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
    $staleEvidenceCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:authorization-evidence-current-status-mismatch')

  $oldMailWorkspace = New-MailAuthorizationWorkspace -Evidence $null -State 'NOT_VERIFIED'
  $oldMailResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $oldMailResult $mailPlan $oldMailWorkspace 'INCOMPLETE_EVIDENCE'
  $oldMailCheck = $oldMailResult.checks[0]
  Assert-That 'historical-mail-row-without-typed-receipt-stays-untrusted' (-not $oldMailCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
    $oldMailCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:typed-authorization-evidence-not-projected' -and
    $oldMailCheck.apiCapabilityObservation.state -eq 'NOT_VERIFIED' -and $oldMailCheck.state -eq 'INCOMPLETE_EVIDENCE')

  $wrongPlan = New-MailAuthorizationPlan
  $wrongPlan.mail.permissionModel = 'EXCHANGE_RBAC_RESOURCE_SCOPE'
  $wrongConfigResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $wrongConfigResult $wrongPlan $mailWorkspace 'INCOMPLETE_EVIDENCE'
  $wrongConfigCheck = $wrongConfigResult.checks[0]
  Assert-That 'mail-receipt-not-projected-when-plan-model-does-not-match-enrollment-config' (-not $wrongConfigCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
    $wrongConfigCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:authorization-evidence-deployment-binding-mismatch')

  $aliasEvidence = $mailEvidence.Clone()
  $aliasEvidence.model = 'LEGACY_AAP_GRAPH'
  $aliasResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $aliasResult $mailPlan (New-MailAuthorizationWorkspace -Evidence $aliasEvidence) 'INCOMPLETE_EVIDENCE'
  $aliasCheck = $aliasResult.checks[0]
  Assert-That 'mail-receipt-rejects-shorthand-model-alias' (-not $aliasCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
    $aliasCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:authorization-evidence-model-mismatch')

  $expiredEvidence = $mailEvidence.Clone()
  $expiredEvidence.expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
  $expiredResult = New-M365Result 'ReadOnly'
  Add-M365ReadinessCapabilityChecks $expiredResult $mailPlan (New-MailAuthorizationWorkspace -Evidence $expiredEvidence) 'INCOMPLETE_EVIDENCE'
  $expiredCheck = $expiredResult.checks[0]
  Assert-That 'expired-mail-receipt-not-projected-as-current-evidence' (-not $expiredCheck.apiCapabilityObservation.Contains('authorizationEvidence') -and
    $expiredCheck.sourceOwnedBlockers -contains 'api-workspace:MAIL:authorization-evidence-expired' -and
    $expiredCheck.state -eq 'INCOMPLETE_EVIDENCE')

  $clientSitesCapability = $script:plan.capabilities[0].Clone()
  $clientSitesCapability.kind = 'CLIENT_SITES'
  $clientSitesCapability.expectedApplicationRoles = @(
    @{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Sites.FullControl.All' },
    @{ resourceAppId = '00000003-0000-0ff1-ce00-000000000000'; value = 'Sites.FullControl.All' }
  )
  $clientSitesPlan = $script:plan.Clone()
  $clientSitesPlan.capabilities = @($clientSitesCapability)
  $clientSites = Invoke-SyntheticReadback (New-ResponseSequence) 'client-sites-separate-owner' $clientSitesPlan
  $clientSitesCheck = @($clientSites.Result.checks | Where-Object { $_.capability -eq 'CLIENT_SITES' })[0]
  Assert-That 'client-sites-has-no-matrix-key-and-separate-owner-blocker' ($clientSites.Result.status -eq 'INCOMPLETE_EVIDENCE' -and
    $clientSites.Result.errorCode -eq 'readiness-readback-missing' -and $clientSitesCheck.capability -eq 'CLIENT_SITES' -and
    $null -eq $clientSitesCheck.apiCapabilityKey -and $null -eq $clientSitesCheck.apiCapabilityObservation -and
    $clientSitesCheck.sourceOwnedBlockers -contains 'api-client-sites:separate-owner-readiness-readback-required' -and
    $clientSitesCheck.sourceOwnedBlockers -contains 'api-client-sites:no-capability-matrix-counterpart')

  $validator = Join-Path $root 'verify-result.py'
  & python3 $validator $nominal.Path --expect INCOMPLETE_EVIDENCE --plan-sha256 $nominal.Result.planSha256 --tenant $script:plan.tenantId | Out-Null
  $validIncompleteExit = $LASTEXITCODE
  & python3 $validator $nominal.Path --expect VERIFIED --plan-sha256 $nominal.Result.planSha256 --tenant $script:plan.tenantId | Out-Null
  $fakeVerifiedExit = $LASTEXITCODE
  Assert-That 'result-validator-refuses-verified-expectation' ($validIncompleteExit -eq 0 -and $fakeVerifiedExit -eq 1)

  $missingRows = Invoke-SyntheticReadback (New-ResponseSequence -WithoutCapabilityRows) 'missing-rows'
  $missingCheck = @($missingRows.Result.checks | Where-Object { $_.capability -eq 'DOCUMENTS' })[0]
  Assert-That 'missing-api-capability-row-is-explicitly-blocked' ($missingRows.Result.status -eq 'INCOMPLETE_EVIDENCE' -and
    $missingRows.Result.errorCode -eq 'readiness-readback-missing' -and
    $null -eq $missingCheck.apiCapabilityObservation -and
    $missingCheck.apiCapabilityKey -eq 'SELECTED_SITE' -and
    $missingCheck.sourceOwnedBlockers -contains 'api-workspace:DOCUMENTS:SELECTED_SITE:capability-status-row-missing')

  $sessionDrift = Invoke-SyntheticReadback (New-ResponseSequence -AfterUserId '00000000-0000-4000-8000-000000000021') 'session-drift'
  $driftCapability = @($sessionDrift.Result.checks | Where-Object { $_.capability -eq 'DIRECTORY_READER' })[0]
  Assert-That 'session-drift-fails-closed' ($sessionDrift.Result.status -eq 'DRIFT' -and
    $sessionDrift.Result.errorCode -eq 'readiness-session-or-tenant-drift' -and
    @($sessionDrift.Result.checks | Where-Object { $_.id -eq 'readiness:operator-session-binding' -and $_.state -eq 'DRIFT' }).Count -eq 1 -and
    $null -eq $driftCapability.apiCapabilityObservation)

  $inaccessibleResponses = @((New-JsonResponse 200 (New-Session)), (New-JsonResponse 503 $null))
  $inaccessible = Invoke-SyntheticReadback $inaccessibleResponses 'inaccessible'
  Assert-That 'inaccessible-readback-is-distinct' ($inaccessible.Result.status -eq 'PROVIDER_FAILURE' -and
    $inaccessible.Result.errorCode -eq 'readiness-readback-store-inaccessible' -and $inaccessible.Result.networkCalls -eq 2)

  $bootstrapResponses = @((New-JsonResponse 401 $null))
  $bootstrap = Invoke-SyntheticReadback $bootstrapResponses 'bootstrap'
  Assert-That 'unauthenticated-session-is-bootstrap-block' ($bootstrap.Result.status -eq 'BLOCKED_BOOTSTRAP' -and
    $bootstrap.Result.errorCode -eq 'readiness-bootstrap-required' -and $bootstrap.Result.networkCalls -eq 1)

  $missingBinding = New-ResponseSequence -TenantId '00000000-0000-4000-8000-000000000099'
  $tenantDrift = Invoke-SyntheticReadback $missingBinding 'tenant-drift'
  Assert-That 'workspace-tenant-mismatch-is-drift' ($tenantDrift.Result.status -eq 'DRIFT' -and
    $tenantDrift.Result.errorCode -eq 'readiness-session-or-tenant-drift')

  $allowlistHandler = [ReadinessQueueHandler]::new()
  $allowlistHandler.Enqueue((New-JsonResponse 200 (New-Session)))
  $allowlistClient = [System.Net.Http.HttpClient]::new($allowlistHandler, $false)
  Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
  Set-M365OfflineMode $false
  try {
    $allowedTransport = New-M365EnrollmentTransport $allowlistClient ([uri] $script:plan.publicBaseUri) `
      '.AspNetCore.Cookies=synthetic-readonly-cookie' (Get-Command Invoke-M365ProviderRequest)
    $null = & $allowedTransport '/api/ui/session'
    $unknownTargetRefused = $false
    try { $null = & $allowedTransport '/api/ui/administration/microsoft365/verify' } catch { $unknownTargetRefused = $_.Exception.Message -eq 'INVALID_OR_UNSAFE' }
    Assert-That 'unknown-target-refused-before-dispatch' ($unknownTargetRefused -and $allowlistHandler.SendCount -eq 1)
    Assert-That 'readonly-http-is-get-only-current-allowlist' ($allowlistHandler.Methods[0] -eq 'GET' -and
      $allowlistHandler.Uris[0] -eq "$($script:plan.publicBaseUri.TrimEnd('/'))/api/ui/session" -and
      $null -eq $allowlistHandler.Bodies[0])
  } finally { $allowlistClient.Dispose(); Set-M365OfflineMode $true }

  foreach ($case in @($nominal, $simulation, $missingRows, $clientSites, $sessionDrift, $inaccessible, $bootstrap, $tenantDrift)) {
    $caseLabel = ([string] $case.Result.status).ToLowerInvariant()
    Assert-That "$caseLabel-no-mutations" ($case.Result.mutationsAttempted -eq 0 -and
      $case.Result.mutationsConfirmed -eq 0 -and @($case.Result.changes).Count -eq 0)
    Assert-That "$caseLabel-synthetic-get-only-no-live-call" ($case.Handler.SendCount -gt 0 -and
      @($case.Handler.Methods | Where-Object { $_ -cne 'GET' }).Count -eq 0 -and
      @($case.Handler.Uris | Where-Object { $_ -notmatch '^https://auditsphere\.synthetic\.test/api/ui/(session|administration/microsoft365)$' }).Count -eq 0)
  }

  Write-Output "Readiness.Tests.ps1: PASS ($script:assertions assertions; all HTTP dispatched through the in-memory handler)"
} finally {
  Remove-Item -LiteralPath $script:work -Recurse -Force -ErrorAction SilentlyContinue
}
