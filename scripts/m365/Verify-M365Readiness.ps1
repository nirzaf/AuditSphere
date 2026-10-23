#Requires -Version 7.4
<#
.SYNOPSIS
  Reads the current AuditSphere M365 administration workspace and reports readiness evidence gaps.
.DESCRIPTION
  Plan, -DryRun and -WhatIf are offline. ReadOnly performs only the existing authenticated session and
  M365 workspace GETs. Current API setup summaries are never promoted to runtime authorization proof.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly','Verify','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [Alias('SessionCookieEnv')] [string] $SessionCookieEnvironmentVariable
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force

$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$resultMode = if ($offline) { 'Plan' } elseif ($Mode -eq 'Verify') { 'ReadOnly' } else { $Mode }
$result = New-M365Result $resultMode
$status = 'INCOMPLETE_EVIDENCE'
$plan = $null
$client = $null
$cookie = $null
try {
  $plan = Read-M365Plan -Path $PlanPath -SchemaPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }

  if ($offline) {
    Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Readiness.psm1') -Force
    Add-M365ReadinessPlannedChecks $result $plan
    $status = 'PLANNED'
  } elseif ($Mode -eq 'Apply') {
    throw 'READINESS_APPLY_NOT_SUPPORTED'
  } else {
    if (-not $SessionCookieEnvironmentVariable) { throw 'BLOCKED_BOOTSTRAP' }
    if (-not $ExpectedPlanSha256 -or $ExpectedPlanSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'INVALID_OR_UNSAFE' }

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
    Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Readiness.psm1') -Force
    $cookie = Get-M365SessionCookie $SessionCookieEnvironmentVariable
    $http = New-M365EnrollmentHttpClient
    $client = $http.Client
    $transport = New-M365EnrollmentTransport $client ([uri] $plan.publicBaseUri) $cookie (Get-Command Invoke-M365ProviderRequest)
    $status = Invoke-M365ReadinessReadback $plan $result $transport
  }
} catch {
  $message = $_.Exception.Message
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($message -ceq 'READINESS_APPLY_NOT_SUPPORTED') { 'INVALID_OR_UNSAFE' }
    elseif ($message -cin $known) { $message }
    elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INVALID_OR_UNSAFE' } else { 'PROVIDER_FAILURE' }
  if ($message -ceq 'READINESS_APPLY_NOT_SUPPORTED') {
    $result.errorCode = 'readiness-apply-not-supported'
  } elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') {
    $result.errorCode = $_.Exception.Code
  } else {
    $result.errorCode = switch -CaseSensitive ($status) {
      'BLOCKED_BOOTSTRAP' { 'readiness-bootstrap-required' }
      'DRIFT' { 'readiness-source-or-plan-drift' }
      'PROVIDER_FAILURE' { 'readiness-readback-store-inaccessible' }
      'INCOMPLETE_EVIDENCE' { 'readiness-readback-missing-or-malformed' }
      'PREREQUISITE_MISSING' { 'readiness-readback-prerequisite-missing' }
      default { 'readiness-readback-invalid' }
    }
  }
  if ($plan -and @($result.checks).Count -eq 0) {
    Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Readiness.psm1') -Force
    Add-M365ReadinessSessionCheck $result $plan $null $null $status $result.errorCode
    Add-M365ReadinessWorkspaceCheck $result $plan $null $status $result.errorCode
    Add-M365ReadinessCapabilityChecks $result $plan $null $status $result.errorCode @("readiness:$($status.ToLowerInvariant())")
    $result.blockedPrerequisites += "readiness:$($status.ToLowerInvariant())"
  }
} finally {
  $cookie = $null
  if ($client) { $client.Dispose() }
}
exit (Complete-M365Result $result $status $OutputPath)
