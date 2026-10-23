#Requires -Version 7.4
# Observes server-owned human enrollment through the authenticated API. Never grants or writes.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ApprovalReference,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [Alias('SessionCookieEnv')] [string] $SessionCookieEnvironmentVariable
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$result = New-M365Result ($(if ($offline) { 'Plan' } else { $Mode }))
$client = $null
try {
  $plan = Read-M365Plan $PlanPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit; $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }
  if ($offline) {
    Add-M365Check $result 'plan:tenant-enrollment' 'PLANNED' 'server-owned human grantor consent' 'offline-planning'
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }
  if ($Mode -eq 'Apply') { throw 'INVALID_OR_UNSAFE' }
  if (-not $SessionCookieEnvironmentVariable) { throw 'BLOCKED_BOOTSTRAP' }
  if (-not $ExpectedPlanSha256) { throw 'INVALID_OR_UNSAFE' }
  $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'PREREQUISITE_MISSING' }
  if (-not (Test-M365ExactCheckoutRoot $repository)) { throw 'INVALID_OR_UNSAFE' }
  $source = & git -C $repository rev-parse HEAD 2>$null
  if ($LASTEXITCODE -ne 0 -or $source -cne $plan.repositoryCommit) { throw 'INVALID_OR_UNSAFE' }
  $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'INVALID_OR_UNSAFE' }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Enrollment.psm1') -Force
  $cookie = Get-M365SessionCookie $SessionCookieEnvironmentVariable
  $http = New-M365EnrollmentHttpClient
  $client = $http.Client
  $transport = New-M365EnrollmentTransport $client ([uri] $plan.publicBaseUri) $cookie (Get-Command Invoke-M365ProviderRequest)
  Invoke-M365EnrollmentVerification $plan $result $transport
  $status = 'VERIFIED'
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message }
    elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INVALID_OR_UNSAFE' } else { 'PROVIDER_FAILURE' }
  $result.errorCode = "enrollment-$($status.ToLowerInvariant())"
} finally {
  $cookie = $null
  if ($client) { $client.Dispose() }
}
exit (Complete-M365Result $result $status $OutputPath)
