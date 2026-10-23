#Requires -Version 7.4
<#
.SYNOPSIS
  Plans an explicit AuditSphere Exchange mail permission model.
.DESCRIPTION
  Plan, -DryRun and -WhatIf validate only local input and write a PLANNED result envelope.
  ReadOnly and Apply report PREREQUISITE_MISSING until the Exchange/Entra read-back adapter is implemented.
  This script never connects to a tenant or changes permissions.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan', 'ReadOnly', 'Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force

# This first WU-022 slice has no Exchange/Entra adapter. Keep the shared provider gate closed in every mode.
Set-M365OfflineMode $true
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
$result = New-M365Result $(if ($offline) { 'Plan' } else { $Mode })

try {
  $plan = Read-M365Plan -Path $PlanPath -SchemaPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
} catch {
  $result.errorCode = if ($_.Exception.PSObject.Properties['Code']) { $_.Exception.Code } else { 'plan-validation-error' }
  exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
}

$result.repositoryCommit = $plan.repositoryCommit
$result.tenantId = $plan.tenantId
$result.planSha256 = Get-M365PlanSha256 $plan
if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) {
  $result.errorCode = 'plan-sha256-mismatch'
  exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
}

$mailCapability = @($plan.capabilities | Where-Object { $_.kind -eq 'MAIL' })
if ($mailCapability.Count -eq 0) {
  Add-M365Check $result 'mail-configuration' 'SKIPPED' 'one explicit MAIL capability and permission model' `
    'No MAIL capability is configured; no mail readiness was evaluated.' 'mail-capability-not-configured'
  $result.blockedPrerequisites += 'mail-capability-not-configured'
  if ($offline) { exit (Complete-M365Result $result 'PLANNED' $OutputPath) }
  $result.errorCode = 'mail-capability-not-configured'
  exit (Complete-M365Result $result 'PREREQUISITE_MISSING' $OutputPath)
}

$model = $plan.mail.permissionModel
Add-M365Check $result 'mail-plan-contract' 'PASS' 'model-specific sender, control and scope fields; exact model role set' `
  "$model plan contract valid; no Exchange or Entra state was read."

if ($offline) { exit (Complete-M365Result $result 'PLANNED' $OutputPath) }

$result.blockedPrerequisites += 'engineering:exchange-entra-readback-adapter-not-implemented'
$result.errorCode = 'adapter-not-implemented'
Add-M365Check $result 'mail-provider-readiness' 'PREREQUISITE_MISSING' `
  'implemented Exchange/Entra read-back adapter for scope authorization and application grants' `
  'Engineering prerequisite: the Exchange/Entra read-back adapter is not implemented; no provider state was read.' $result.errorCode
exit (Complete-M365Result $result 'PREREQUISITE_MISSING' $OutputPath)
