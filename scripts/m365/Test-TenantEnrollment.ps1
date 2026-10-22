#Requires -Version 7.4
<#
.SYNOPSIS
  WU-019: verifies existing tenant consent/configuration state and freshness.
.DESCRIPTION
  An administrator grant of application roles is not a substitute for the human grantor leg.
  Test-TenantEnrollment checks consent status through the documented API / workspace query.
  If the human OIDC grantor leg has not completed, returns BLOCKED_BOOTSTRAP.
  Never writes CONSENT_VERIFIED directly into PostgreSQL or bypasses enrollment.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ApprovalReference,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [string] $SessionCookieEnv = 'AUDITSPHERE_SESSION_COOKIE'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$result = New-M365Result ($(if ($offline) { 'Plan' } else { $Mode }))

try {
  $plan = Read-M365Plan $PlanPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) {
    throw 'INVALID_OR_UNSAFE'
  }
  if ($Mode -eq 'Apply') {
    $result.errorCode = 'apply-not-supported-by-enrollment-test'
    exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
  }
  if ($offline) {
    Add-M365Check $result 'plan:tenant-enrollment' 'PLANNED' 'human-grantor-consent-verified' 'offline-planning'
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Enrollment.psm1') -Force
  $cookie = Get-M365SessionCookie $SessionCookieEnv
  $providerCommand = Get-Command Invoke-M365ProviderRequest
  $client = [System.Net.Http.HttpClient]::new()
  try {
    $transport = New-M365EnrollmentTransport $client ([uri] $plan.publicBaseUri) $cookie $providerCommand
    Invoke-M365EnrollmentVerification $plan $result $transport
    exit (Complete-M365Result $result 'VERIFIED' $OutputPath)
  } finally { $client.Dispose() }
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','RESULT_UNCERTAIN','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message } else { 'INVALID_OR_UNSAFE' }
  $result.errorCode = "enrollment-$($status.ToLowerInvariant())"
  exit (Complete-M365Result $result $status $OutputPath)
}
