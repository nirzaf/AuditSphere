#Requires -Version 7.4
<#
.SYNOPSIS
  Plans an explicit AuditSphere Exchange mail permission model.
.DESCRIPTION
  Plan, -DryRun and -WhatIf validate only local input and write a PLANNED result envelope.
  ReadOnly checks the approved source and plan, then reads state through pre-authenticated app-only
  Graph and Exchange sessions using the same operator.clientId and tenant. The operator must establish
  those sessions beforehand with a custom application and least-privilege grants. Overall readiness
  remains incomplete pending model-specific application capability evidence. This script never
  authenticates or changes provider state.
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
Import-Module (Join-Path $PSScriptRoot 'MailAccess.psm1') -Force

# Keep the shared provider gate closed until the exact ReadOnly source and plan are approved.
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
Add-M365Check $result 'mail-plan-contract' 'PASS' `
  'model-specific sender, control and scope fields; exact model role set' `
  "Explicit $model plan contract validated offline."

if ($offline) { exit (Complete-M365Result $result 'PLANNED' $OutputPath) }

if ($Mode -eq 'Apply') {
  $result.blockedPrerequisites += 'engineering:reviewed-mail-migration-not-implemented'
  $result.errorCode = 'mail-apply-migration-not-implemented'
  Add-M365Check $result 'mail-apply' 'PREREQUISITE_MISSING' `
    'reviewed migration for the selected Exchange authorization model' `
    'Engineering prerequisite: no MAIL permission migration is implemented; no provider state was read or changed.' `
    $result.errorCode
  exit (Complete-M365Result $result 'PREREQUISITE_MISSING' $OutputPath)
}

try {
  if (-not $ExpectedPlanSha256) { throw 'mail-plan-sha256-required' }
  if ($plan.repositoryCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'mail-plan-repository-commit-invalid' }
  if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'mail-git-unavailable' }
  $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  if (-not (Test-M365ExactCheckoutRoot $repository)) { throw 'INVALID_OR_UNSAFE' }
  $head = & git -C $repository rev-parse HEAD 2>$null
  if ($LASTEXITCODE -ne 0 -or [string] $head -ine [string] $plan.repositoryCommit) { throw 'mail-source-head-mismatch' }
  $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
  if ($LASTEXITCODE -ne 0) { throw 'mail-source-status-unavailable' }
  if ($dirty) { throw 'mail-source-tree-dirty' }

  Set-M365OfflineMode $false
  $snapshot = Get-M365MailReadOnlyEvidence $plan
  $result.networkCalls = Get-M365NetworkCallCount
  $status = Add-M365MailRuntimeGate $result $snapshot
  exit (Complete-M365Result $result $status $OutputPath)
} catch {
  $result.networkCalls = Get-M365NetworkCallCount
  Set-M365OfflineMode $true
  $thrown = [string] $_.Exception.Message
  $known = @('INVALID_OR_UNSAFE','BLOCKED_BOOTSTRAP','DRIFT','INCOMPLETE_EVIDENCE','PREREQUISITE_MISSING','PROVIDER_FAILURE')
  if ($thrown -cin $known) {
    $status = $thrown
    $errorCode = switch ($thrown) {
      'INVALID_OR_UNSAFE' { 'mail-readonly-source-or-plan-invalid' }
      'BLOCKED_BOOTSTRAP' { 'mail-operator-session-not-bound' }
      'DRIFT' { 'mail-provider-state-drift' }
      'INCOMPLETE_EVIDENCE' { 'mail-provider-evidence-incomplete' }
      'PREREQUISITE_MISSING' { 'mail-pinned-runtime-unavailable' }
      default { 'mail-provider-readback-failed' }
    }
  } elseif ($thrown -in @('mail-plan-sha256-required','mail-plan-repository-commit-invalid',
      'mail-source-head-mismatch','mail-source-tree-dirty')) {
    $status = 'INVALID_OR_UNSAFE'
    $errorCode = $thrown
  } elseif ($thrown -in @('mail-git-unavailable','mail-source-status-unavailable')) {
    $status = 'PREREQUISITE_MISSING'
    $errorCode = $thrown
  } else {
    $status = 'PROVIDER_FAILURE'
    $errorCode = 'mail-readonly-operation-failed'
  }
  $result.errorCode = $errorCode
  $observed = if ($status -ceq 'BLOCKED_BOOTSTRAP') {
    "No provider connection was created. ReadOnly requires existing Graph AuthType=AppOnly and Exchange CertificateAuthentication=true sessions for the same tenant and operator.clientId. Operator setup must grant Graph Application.Read.All and Exchange.ManageAsApp with an Exchange custom role group limited to this adapter's read cmdlets; this script creates no connection, consent, or grant."
  } else {
    'Read-only readiness could not be established; consult the stable error code.'
  }
  Add-M365Check $result 'mail-provider-readiness' $status `
    'pre-authenticated Graph and Exchange app-only sessions and current provider state bound to the plan tenant and operator client' `
    $observed $errorCode
  exit (Complete-M365Result $result $status $OutputPath)
}
