#Requires -Version 7.4
# WU023 focused unit cohort. Every HTTP request is handled by an in-memory handler; no live network is available.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$scriptRoot = Join-Path $repositoryRoot 'scripts/m365'
Import-Module (Join-Path $scriptRoot 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $scriptRoot 'AuditSphere.M365.Enrollment.psm1') -Force
Import-Module (Join-Path $scriptRoot 'AuditSphere.M365.ArchiveCopies.psm1') -Force

Add-Type -TypeDefinition @'
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class ArchiveCopiesQueueHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>();
    public readonly List<string> Methods = new List<string>();
    public readonly List<string> Uris = new List<string>();
    public int SendCount => Methods.Count;
    public void Enqueue(HttpResponseMessage response) => responses.Enqueue(response);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Methods.Add(request.Method.Method);
        Uris.Add(request.RequestUri?.AbsoluteUri);
        if (request.Method != HttpMethod.Get || request.Content != null) throw new HttpRequestException("non-GET request refused");
        if (responses.Count == 0) throw new HttpRequestException("synthetic response queue exhausted");
        var response = responses.Dequeue();
        if (response.RequestMessage == null) response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
'@

$script:assertions = 0
$script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-archive-copies-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($script:work) | Out-Null
$script:plan = Read-M365Plan (Join-Path $scriptRoot 'tests/fixtures/plan.synthetic.json') (Join-Path $scriptRoot 'm365-plan.schema.json')
$script:operationId = '00000000-0000-4000-8000-000000000030'
$script:firmId = [string] $script:plan.firmId
$script:tenantId = [string] $script:plan.tenantId
$script:clientId = '00000000-0000-4000-8000-000000000031'
$script:engagementId = '00000000-0000-4000-8000-000000000032'
$script:archiveId = '00000000-0000-4000-8000-000000000033'
$script:manifestId = '00000000-0000-4000-8000-000000000034'
$script:profileId = '00000000-0000-4000-8000-000000000035'
$script:releaseId = '00000000-0000-4000-8000-000000000036'
$script:releasedArtifactId = '00000000-0000-4000-8000-000000000037'
$script:checkpointId = '00000000-0000-4000-8000-000000000038'
$script:siteBindingId = '00000000-0000-4000-8000-000000000039'
$script:targetRevisionId = '00000000-0000-4000-8000-000000000040'
$script:manifestEntryId = '00000000-0000-4000-8000-000000000041'
$script:manifestSourceId = '00000000-0000-4000-8000-000000000042'
$script:staffGroupId = '42'
$script:staffDigest = 'c' * 64
$script:permissionDigest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
  [System.Text.Encoding]::UTF8.GetBytes('[{"PrincipalKind":"SHAREPOINT_GROUP","PrincipalId":"42","Role":"READ"}]'))).ToLowerInvariant()
$script:targetDigest = 'e' * 64
$script:manifestDigest = 'f' * 64
$script:now = '2026-10-23T07:00:00.0000000Z'

function Assert-That([string] $Name, [bool] $Condition, [string] $Detail = '') {
  if (-not $Condition) { throw "ASSERT_FAILED:$Name $Detail" }
  $script:assertions++
}

function Assert-ThrowsCode([string] $Name, [string] $ExpectedCode, [scriptblock] $Action) {
  $caught = $false
  try { & $Action }
  catch {
    $caught = $true
    if ($_.Exception.Message -cne $ExpectedCode) { throw "ASSERT_FAILED:$Name expected $ExpectedCode got $($_.Exception.Message)" }
  }
  if (-not $caught) { throw "ASSERT_FAILED:$Name did not throw $ExpectedCode" }
  $script:assertions++
}

function New-PinnedOperationPlan {
  $sourceRows = @(
    [ordered]@{ sourceKind = 'CHECKPOINT_MANIFEST'; sourceKeySha256 = 'a' * 64; sourceId = $script:checkpointId; manifestEntryId = $null; sourceVersion = $script:manifestDigest; sourceSha256 = '1' * 64; sourceByteCount = 10; relativePath = 'release-evidence/checkpoint.json' },
    [ordered]@{ sourceKind = 'RELEASED_ARTIFACT'; sourceKeySha256 = 'b' * 64; sourceId = $script:releasedArtifactId; manifestEntryId = $null; sourceVersion = 'release-v7'; sourceSha256 = '2' * 64; sourceByteCount = 20; relativePath = 'release-evidence/artifact.bin' },
    [ordered]@{ sourceKind = 'REVIEWED_MANIFEST_ENTRY'; sourceKeySha256 = '3' * 64; sourceId = $script:manifestSourceId; manifestEntryId = $script:manifestEntryId; sourceVersion = 'snapshot-v3'; sourceSha256 = '4' * 64; sourceByteCount = 30; relativePath = 'manifest-entries/000001/index.csv' }
  )
  return [ordered]@{
    schemaVersion = 1; operationId = $script:operationId; firmId = $script:firmId; tenantId = $script:tenantId
    clientId = $script:clientId; engagementId = $script:engagementId; archiveId = $script:archiveId
    manifestId = $script:manifestId; manifestVersion = 7; manifestSha256 = $script:manifestDigest
    recordsProfileId = $script:profileId; recordsProfileVersion = 4; releaseId = $script:releaseId
    releasedArtifactId = $script:releasedArtifactId; checkpointId = $script:checkpointId
    clientSharePointSiteId = $script:siteBindingId; targetRevisionId = $script:targetRevisionId; targetRevision = 5
    targetConfigurationSha256 = $script:targetDigest; authorizedStaffSha256 = $script:staffDigest
    destinationTenantId = $script:tenantId; destinationSiteId = 'site-exact-01'; destinationDriveId = 'drive-exact-02'
    destinationLibraryRootItemId = 'library-root-03'; staffGroupId = $script:staffGroupId
    expectedPermissionsSha256 = $script:permissionDigest; files = $sourceRows
  }
}

function New-Readback($OperationPlan = (New-PinnedOperationPlan)) {
  $permissionJson = ConvertTo-Json -InputObject @([ordered]@{
    PrincipalKind = 'SHAREPOINT_GROUP'; PrincipalId = $script:staffGroupId; Role = 'READ'
  }) -Depth 6 -Compress
  $files = @($OperationPlan.files | ForEach-Object {
    [ordered]@{
      sourceKind = $_.sourceKind; sourceKeySha256 = $_.sourceKeySha256; sourceId = $_.sourceId
      manifestEntryId = $_.manifestEntryId; sourceVersion = $_.sourceVersion; sourceSha256 = $_.sourceSha256
      sourceByteCount = $_.sourceByteCount; relativePath = $_.relativePath
      destinationTenantId = $OperationPlan.destinationTenantId; destinationSiteId = $OperationPlan.destinationSiteId
      destinationDriveId = $OperationPlan.destinationDriveId; destinationLibraryRootItemId = $OperationPlan.destinationLibraryRootItemId
      hasExactTargetBinding = $true; destinationItemId = "item-$($_.sourceKeySha256.Substring(0,4))"
      destinationVersion = 'destination-v9'; readBackSha256 = $_.sourceSha256; readBackByteCount = $_.sourceByteCount
      verificationOutcome = 'MATCH'; readBackAt = $script:now
    }
  })
  $receipts = @($files | ForEach-Object -Begin { $i = 50 } -Process {
    $i++
    [ordered]@{ receiptId = ('00000000-0000-4000-8000-{0:D12}' -f $i); attemptToken = 3 } + $_
  })
  $resultPermissions = [ordered]@{
    expectedPermissionsJson = $permissionJson; expectedPermissionsSha256 = $script:permissionDigest
    observedPermissionsJson = $permissionJson; observedPermissionsSha256 = $script:permissionDigest
    hasUniquePermissionScope = $true; hasExactTargetBinding = $true; observedStaffSha256 = $script:staffDigest
    observedTenantId = $OperationPlan.destinationTenantId; observedSiteId = $OperationPlan.destinationSiteId
    observedDriveId = $OperationPlan.destinationDriveId; observedLibraryRootItemId = $OperationPlan.destinationLibraryRootItemId
    comparisonOutcome = 'MATCH'; observedAt = $script:now
  }
  $result = [ordered]@{
    operationId = $OperationPlan.operationId; attemptToken = 3; archiveId = $OperationPlan.archiveId
    clientId = $OperationPlan.clientId; manifestId = $OperationPlan.manifestId; manifestVersion = $OperationPlan.manifestVersion
    manifestSha256 = $OperationPlan.manifestSha256; recordsProfileId = $OperationPlan.recordsProfileId
    recordsProfileVersion = $OperationPlan.recordsProfileVersion; releaseId = $OperationPlan.releaseId
    checkpointId = $OperationPlan.checkpointId; clientSharePointSiteId = $OperationPlan.clientSharePointSiteId
    targetRevisionId = $OperationPlan.targetRevisionId; targetRevision = $OperationPlan.targetRevision
    targetConfigurationSha256 = $OperationPlan.targetConfigurationSha256; authorizedStaffSha256 = $OperationPlan.authorizedStaffSha256
    protocolOutcome = 'MATCH'; files = $files; permissions = $resultPermissions
  }
  $observations = @([ordered]@{
    observationId = '00000000-0000-4000-8000-000000000055'; attemptToken = 3
    expectedPermissionsJson = $permissionJson; expectedPermissionsSha256 = $script:permissionDigest
    observedPermissionsJson = $permissionJson; observedPermissionsSha256 = $script:permissionDigest
    hasUniquePermissionScope = $true; hasExactTargetBinding = $true; observedStaffSha256 = $script:staffDigest
    observedTenantId = $OperationPlan.destinationTenantId; observedSiteId = $OperationPlan.destinationSiteId
    observedDriveId = $OperationPlan.destinationDriveId; observedLibraryRootItemId = $OperationPlan.destinationLibraryRootItemId
    comparisonOutcome = 'MATCH'; observedAt = $script:now
  })
  return [ordered]@{
    operationId = $OperationPlan.operationId; firmId = $OperationPlan.firmId
    clientId = $OperationPlan.clientId; engagementId = $OperationPlan.engagementId
    operationStatus = 'COMPLETED'; operationResultDigest = ('9' * 64)
    result = $result; fileReceipts = $receipts; permissionObservations = $observations
  }
}

function New-Session([bool] $Staff = $true, [string] $Generation = '7', [string] $UserId = '00000000-0000-4000-8000-000000000020') {
  return @{ userId = $UserId; firmId = $script:firmId; generation = $Generation; staff = $Staff }
}

function New-JsonResponse([int] $StatusCode, $Value, [switch] $RawJson) {
  $response = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode] $StatusCode)
  if ($null -ne $Value) {
    $json = if ($RawJson) { [string] $Value } else { ConvertTo-Json -InputObject $Value -Depth 32 -Compress }
    $response.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, 'application/json')
  }
  return $response
}

function Invoke-SyntheticReadback($Evidence, [switch] $Denied, [switch] $StaleSession, [switch] $RawEvidence) {
  Set-M365OfflineMode $false
  $operationPlan = New-PinnedOperationPlan
  $handler = [ArchiveCopiesQueueHandler]::new()
  $before = New-Session -Staff:(-not $Denied)
  $after = if ($StaleSession) { New-Session -Generation '8' } else { New-Session }
  $handler.Enqueue((New-JsonResponse 200 $before))
  $handler.Enqueue((New-JsonResponse 200 $Evidence -RawJson:$RawEvidence))
  $handler.Enqueue((New-JsonResponse 200 $after))
  $client = [System.Net.Http.HttpClient]::new($handler, $false)
  $client.Timeout = [TimeSpan]::FromSeconds(10)
  $client.MaxResponseContentBufferSize = 5242880
  try {
    $transport = New-M365ArchiveCopyTransport $client ([uri] $script:plan.publicBaseUri) `
      '.AspNetCore.Cookies=synthetic-readonly-cookie' (Get-Command Invoke-M365ProviderRequest) $script:operationId
    $result = New-M365Result 'ReadOnly'
    $status = Invoke-M365ArchiveCopyEvidenceReadback $script:plan $operationPlan $script:operationId $result $transport
    return @{ Status = $status; Result = $result; Handler = $handler }
  } finally { $client.Dispose() }
}

$operationPlan = New-PinnedOperationPlan
$operationIdText = $script:operationId
$positiveEvidence = New-Readback $operationPlan
$positive = Invoke-SyntheticReadback $positiveEvidence
Assert-That 'matching-application-evidence-stays-incomplete' ($positive.Status -eq 'INCOMPLETE_EVIDENCE' -and
  $positive.Result.checks[0].state -eq 'PASS' -and $positive.Result.checks[1].state -eq 'INCOMPLETE_EVIDENCE' -and
  @($positive.Result.blockedPrerequisites).Count -eq 4 -and $positive.Result.mutationsAttempted -eq 0 -and
  $positive.Result.mutationsConfirmed -eq 0) "status=$($positive.Status); errorCode=$($positive.Result.errorCode); checkStates=$(@($positive.Result.checks | ForEach-Object state) -join ','); blockers=$(@($positive.Result.blockedPrerequisites) -join ',')"
Assert-That 'transport-is-three-exact-get-requests' ($positive.Handler.SendCount -eq 3 -and
  @($positive.Handler.Methods | Where-Object { $_ -cne 'GET' }).Count -eq 0 -and
  $positive.Handler.Uris[1] -ceq "$($script:plan.publicBaseUri.TrimEnd('/'))/api/ui/records/archive-copies/$operationIdText/evidence") "count=$($positive.Handler.SendCount); methods=$($positive.Handler.Methods -join ',')"

$wrongOperation = New-Readback $operationPlan
$wrongOperation.operationId = '00000000-0000-4000-8000-000000000099'
$case = Invoke-SyntheticReadback $wrongOperation
Assert-That 'wrong-operation-rejected' ($case.Status -eq 'DRIFT')

$wrongEngagement = New-Readback $operationPlan
$wrongEngagement.engagementId = '00000000-0000-4000-8000-000000000099'
$case = Invoke-SyntheticReadback $wrongEngagement
Assert-That 'wrong-root-engagement-rejected' ($case.Status -eq 'DRIFT')

$wrongBytes = New-Readback $operationPlan
$wrongBytes.fileReceipts[0].readBackByteCount++
$case = Invoke-SyntheticReadback $wrongBytes
Assert-That 'wrong-file-byte-count-rejected' ($case.Status -eq 'DRIFT')

$wrongGrant = New-Readback $operationPlan
$extraGrantJson = ConvertTo-Json -InputObject @(
  [ordered]@{ PrincipalKind = 'SHAREPOINT_GROUP'; PrincipalId = $script:staffGroupId; Role = 'READ' },
  [ordered]@{ PrincipalKind = 'USER'; PrincipalId = '00000000-0000-4000-8000-000000000077'; Role = 'READ' }
) -Depth 6 -Compress
$wrongGrant.result.permissions.observedPermissionsJson = $extraGrantJson
$case = Invoke-SyntheticReadback $wrongGrant
Assert-That 'extra-permission-grant-rejected' ($case.Status -eq 'DRIFT')

$wrongPermissionEncoding = New-Readback $operationPlan
$wrongPermissionEncoding.result.permissions.observedPermissionsJson =
  $wrongPermissionEncoding.result.permissions.observedPermissionsJson.Replace(',"PrincipalId"', ', "PrincipalId"')
$case = Invoke-SyntheticReadback $wrongPermissionEncoding
Assert-That 'noncanonical-permission-encoding-rejected' ($case.Status -eq 'DRIFT')

$wrongTimestamp = New-Readback $operationPlan
$wrongTimestamp.result.files[0].readBackAt = '2026-10-23T07:00:00.0000000'
$case = Invoke-SyntheticReadback $wrongTimestamp
Assert-That 'timestamp-without-explicit-offset-rejected' ($case.Status -eq 'DRIFT')

$wrongConfig = New-Readback $operationPlan
$wrongConfig.result.targetRevision = 6
$case = Invoke-SyntheticReadback $wrongConfig
Assert-That 'wrong-target-configuration-revision-rejected' ($case.Status -eq 'DRIFT')

$missing = New-Readback $operationPlan
$missing.result = $null; $missing.operationResultDigest = $null; $missing.fileReceipts = @(); $missing.permissionObservations = @()
$case = Invoke-SyntheticReadback $missing
Assert-That 'missing-result-and-receipts-remain-incomplete' ($case.Status -eq 'INCOMPLETE_EVIDENCE' -and
  $case.Result.blockedPrerequisites -contains 'api:operation-result-missing' -and
  $case.Result.blockedPrerequisites -contains 'api:file-receipts-missing' -and
  $case.Result.blockedPrerequisites -contains 'api:permission-observation-missing')

$denied = Invoke-SyntheticReadback (New-Readback $operationPlan) -Denied
Assert-That 'denied-staff-session-blocks-before-readback' ($denied.Status -eq 'BLOCKED_BOOTSTRAP' -and $denied.Handler.SendCount -eq 1)
$stale = Invoke-SyntheticReadback (New-Readback $operationPlan) -StaleSession
Assert-That 'stale-session-epoch-rejected' ($stale.Status -eq 'DRIFT' -and $stale.Handler.SendCount -eq 3)

$operationPlanPath = Join-Path $script:work 'operation-plan.json'
$operationPlanJson = ConvertTo-Json -InputObject $operationPlan -Depth 24
[System.IO.File]::WriteAllText($operationPlanPath, $operationPlanJson, [System.Text.UTF8Encoding]::new($false))
$planHash = Get-M365PlanSha256 $script:plan
$operationPlanHash = Get-M365PlanSha256 $operationPlan
$verifyScript = Join-Path $scriptRoot 'Verify-ArchiveCopies.ps1'
$engine = (Get-Command pwsh).Source

foreach ($offlineCase in @(
    [pscustomobject]@{ Arguments = @('-DryRun'); IncludeOperationPins = $false; ExpectedStatus = 'PLANNED'; ExpectedMode = 'Plan'; ExpectedExit = 0; ExpectedErrorCode = $null },
    [pscustomobject]@{ Arguments = @('-Mode','Plan'); IncludeOperationPins = $true; ExpectedStatus = 'PLANNED'; ExpectedMode = 'Plan'; ExpectedExit = 0; ExpectedErrorCode = $null },
    [pscustomobject]@{ Arguments = @('-Mode','Verify','-WhatIf'); IncludeOperationPins = $true; ExpectedStatus = 'PLANNED'; ExpectedMode = 'Plan'; ExpectedExit = 0; ExpectedErrorCode = $null },
    [pscustomobject]@{ Arguments = @('-Mode','Apply','-DryRun'); IncludeOperationPins = $false; ExpectedStatus = 'PLANNED'; ExpectedMode = 'Plan'; ExpectedExit = 0; ExpectedErrorCode = $null },
    [pscustomobject]@{ Arguments = @('-Mode','Apply'); IncludeOperationPins = $false; ExpectedStatus = 'INVALID_OR_UNSAFE'; ExpectedMode = 'Apply'; ExpectedExit = 4; ExpectedErrorCode = 'archive-copy-apply-not-supported' }
  )) {
  $output = Join-Path $script:work ([guid]::NewGuid().ToString('N') + '.json')
  $commandArgs = @('-NoProfile','-NonInteractive','-File',$verifyScript,'-PlanPath',
    (Join-Path $scriptRoot 'tests/fixtures/plan.synthetic.json'),'-OutputPath',$output)
  if ($offlineCase.IncludeOperationPins) {
    $commandArgs += @('-OperationPlanPath',$operationPlanPath,'-OperationId',$script:operationId,
      '-ExpectedPlanSha256',$planHash,'-ExpectedOperationPlanSha256',$operationPlanHash)
  }
  $commandArgs += $offlineCase.Arguments
  & $engine @commandArgs
  $exit = $LASTEXITCODE
  $parse = @{ AsHashtable = $true; Depth = 16 }
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
  $offlineResult = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json @parse
  Assert-That 'offline-and-refused-apply-have-no-network-or-mutation-effects' ($exit -eq $offlineCase.ExpectedExit -and
    $offlineResult.status -eq $offlineCase.ExpectedStatus -and $offlineResult.mode -eq $offlineCase.ExpectedMode -and
    $offlineResult.errorCode -ceq $offlineCase.ExpectedErrorCode -and
    $offlineResult.networkCalls -eq 0 -and $offlineResult.mutationsAttempted -eq 0 -and $offlineResult.mutationsConfirmed -eq 0) `
    "exit=$exit status=$($offlineResult.status) mode=$($offlineResult.mode) code=$($offlineResult.errorCode) network=$($offlineResult.networkCalls)"
  if ($offlineCase.Arguments -contains 'Apply' -and $offlineCase.Arguments -notcontains '-DryRun') {
    Assert-That 'apply-refusal-is-explicit-in-standard-envelope' ($offlineResult.checks.Count -eq 1 -and
      $offlineResult.checks[0].id -ceq 'archive-copy-apply' -and $offlineResult.checks[0].state -ceq 'INVALID_OR_UNSAFE' -and
      $offlineResult.blockedPrerequisites -contains 'archive-copy:apply-not-supported')
  }
}

Assert-ThrowsCode 'operation-plan-wrong-checkpoint-link' 'INVALID_OR_UNSAFE' {
  $badPlan = New-PinnedOperationPlan
  $badPlan.files[0].sourceId = '00000000-0000-4000-8000-000000000099'
  $badPath = Join-Path $script:work 'bad-operation-plan.json'
  [System.IO.File]::WriteAllText($badPath, (ConvertTo-Json -InputObject $badPlan -Depth 24), [System.Text.UTF8Encoding]::new($false))
  Read-M365ArchiveCopyOperationPlan $badPath | Out-Null
}

Assert-ThrowsCode 'operation-plan-duplicate-property-rejected' 'INVALID_OR_UNSAFE' {
  $duplicatePath = Join-Path $script:work 'duplicate-operation-plan.json'
  $duplicateJson = $operationPlanJson.TrimEnd().TrimEnd('}') + ',"schemaVersion":1}'
  [System.IO.File]::WriteAllText($duplicatePath, $duplicateJson, [System.Text.UTF8Encoding]::new($false))
  Read-M365ArchiveCopyOperationPlan $duplicatePath | Out-Null
}

Assert-ThrowsCode 'operation-plan-byte-limit-enforced' 'INVALID_OR_UNSAFE' {
  $oversizedPath = Join-Path $script:work 'oversized-operation-plan.json'
  [System.IO.File]::WriteAllText($oversizedPath, (' ' * 4194305), [System.Text.UTF8Encoding]::new($false))
  Read-M365ArchiveCopyOperationPlan $oversizedPath | Out-Null
}

$duplicateReadbackJson = (ConvertTo-Json -InputObject $positiveEvidence -Depth 32 -Compress).TrimEnd('}') +
  ',"operationId":"' + $operationIdText + '"}'
$duplicateReadback = Invoke-SyntheticReadback $duplicateReadbackJson -RawEvidence
Assert-That 'api-json-duplicate-property-rejected' ($duplicateReadback.Status -eq 'INCOMPLETE_EVIDENCE')

$oversizedReadback = Invoke-SyntheticReadback (' ' * 4194305) -RawEvidence
Assert-That 'api-json-byte-limit-enforced' ($oversizedReadback.Status -eq 'INCOMPLETE_EVIDENCE')

Remove-Item -LiteralPath $script:work -Recurse -Force
Write-Output "ArchiveCopies.Tests.ps1 PASS assertions=$script:assertions"
