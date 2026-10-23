#Requires -Version 7.4
<#
.SYNOPSIS
  Compares exact Archive Copy operation readback with a pinned, non-secret source/target plan.
.DESCRIPTION
  Plan, -DryRun and -WhatIf are offline and report PLANNED. ReadOnly/Verify uses the existing
  authenticated staff session and three GETs only (session, exact operation evidence, session).
  The operation plan is JSON schema version 1 and is pinned by -ExpectedOperationPlanSha256.
  It contains exact operation/scope/revision IDs, manifest/profile/release/checkpoint identities,
  target IDs and digest, staff-group ID, permission digest, and file source/hash/byte/version/path
  rows. It never contains file bytes, cookies, bearer tokens, or provider credentials.
  This command does not copy, apply, probe, or modify destination content. A matching historical
  application readback always remains INCOMPLETE_EVIDENCE for fresh provider/custody/control proof.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [string] $OperationPlanPath,
  [guid] $OperationId = [guid]::Empty,
  [ValidateSet('Plan','ReadOnly','Verify','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [string] $ExpectedOperationPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [Alias('SessionCookieEnv')] [string] $SessionCookieEnvironmentVariable
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$resultMode = if ($offline) { 'Plan' } elseif ($Mode -eq 'Apply') { 'Apply' } else { 'ReadOnly' }
$result = New-M365Result $resultMode
$status = 'INCOMPLETE_EVIDENCE'
$client = $null
$cookie = $null
try {
  $plan = Read-M365Plan -Path $PlanPath -SchemaPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.ArchiveCopies.psm1') -Force
  $planFirmId = ([guid] $plan.firmId).ToString('D')
  $planTenantId = ([guid] $plan.tenantId).ToString('D')
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }
  if (-not $offline -and $Mode -eq 'Apply') { throw 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED' }

  $hasOperationPlan = -not [string]::IsNullOrWhiteSpace($OperationPlanPath)
  $hasOperationId = $OperationId -ne [guid]::Empty
  if ($hasOperationPlan -ne $hasOperationId) { throw 'INVALID_OR_UNSAFE' }
  $operationPlan = $null
  $operationIdText = $null
  if ($hasOperationPlan) {
    $operationPlan = Read-M365ArchiveCopyOperationPlan $OperationPlanPath
    $operationIdText = $OperationId.ToString('D')
    if ($operationPlan.operationId -cne $operationIdText -or $operationPlan.firmId -cne $planFirmId -or
        $operationPlan.tenantId -cne $planTenantId) { throw 'INVALID_OR_UNSAFE' }
  }
  if ($operationPlan) {
    $operationPlanSha256 = Get-M365PlanSha256 $operationPlan
    if ($ExpectedOperationPlanSha256 -and $ExpectedOperationPlanSha256 -cne $operationPlanSha256) { throw 'INVALID_OR_UNSAFE' }
  } elseif ($ExpectedOperationPlanSha256) { throw 'INVALID_OR_UNSAFE' }

  if ($offline) {
    Add-M365ArchiveCopyPlannedCheck $result $operationIdText
    $status = 'PLANNED'
  } else {
    if (-not $operationPlan -or -not $operationIdText) { throw 'INVALID_OR_UNSAFE' }
    if (-not $SessionCookieEnvironmentVariable) { throw 'BLOCKED_BOOTSTRAP' }
    if ($ExpectedPlanSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $ExpectedOperationPlanSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'INVALID_OR_UNSAFE' }

    $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    if (-not (Get-Command git -ErrorAction SilentlyContinue) -or -not (Test-M365ExactCheckoutRoot $repository)) {
      throw 'INVALID_OR_UNSAFE'
    }
    $source = & git -C $repository rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0 -or ([string] $source).Trim() -cne $plan.repositoryCommit) { throw 'DRIFT' }
    $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'PREREQUISITE_MISSING' }
    if ($dirty) { throw 'DRIFT' }

    Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Enrollment.psm1') -Force
    $cookie = Get-M365SessionCookie $SessionCookieEnvironmentVariable
    $http = New-M365EnrollmentHttpClient
    $client = $http.Client
    $client.MaxResponseContentBufferSize = 4194304
    $transport = New-M365ArchiveCopyTransport $client ([uri] $plan.publicBaseUri) $cookie `
      (Get-Command Invoke-M365ProviderRequest) $operationIdText
    $status = Invoke-M365ArchiveCopyEvidenceReadback $plan $operationPlan $operationIdText $result $transport
  }
} catch {
  $message = $_.Exception.Message
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'INVALID_OR_UNSAFE' }
    elseif ($message -cin $known) { $message }
    elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INVALID_OR_UNSAFE' } else { 'PROVIDER_FAILURE' }
  $result.errorCode = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'archive-copy-apply-not-supported' }
    else { switch -CaseSensitive ($status) {
    'BLOCKED_BOOTSTRAP' { 'archive-copy-current-staff-session-required' }
    'DRIFT' { 'archive-copy-source-or-plan-drift' }
    'PROVIDER_FAILURE' { 'archive-copy-readback-unavailable' }
    'INCOMPLETE_EVIDENCE' { 'archive-copy-readback-missing-or-malformed' }
    'PREREQUISITE_MISSING' { 'archive-copy-readback-prerequisite-missing' }
    default { 'archive-copy-readback-invalid' }
  } }
  if (@($result.checks).Count -eq 0) {
    $checkId = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'archive-copy-apply' } else { 'archive-copy-readback' }
    $expected = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'read-only archive-copy evidence retrieval' } else { 'pinned operation and source plan' }
    $observed = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'Apply is unsupported; no session or network request was attempted' } else { 'readback was not completed' }
    Add-M365Check $result $checkId $status $expected $observed $result.errorCode
    if ($OperationId -ne [guid]::Empty) { $result.checks[-1].resourceId = $OperationId.ToString('D') }
    $prerequisite = if ($message -ceq 'ARCHIVE_COPY_APPLY_NOT_SUPPORTED') { 'archive-copy:apply-not-supported' } else { "archive-copy-readback:$($status.ToLowerInvariant())" }
    $result.blockedPrerequisites += $prerequisite
  }
} finally {
  $cookie = $null
  if ($client) { $client.Dispose() }
}
exit (Complete-M365Result $result $status $OutputPath)
