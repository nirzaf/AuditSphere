#Requires -Version 7.4
<#
.SYNOPSIS
  Exports the approved deployment's public IDs and credential references without values.
.DESCRIPTION
  Plan, DryRun and WhatIf use only the validated M365 plan and hashes of the exact accepted
  Compose source files. Verify additionally reads an operator-provided sanitized Compose JSON
  model. This script never opens .env, invokes Docker, looks up credentials, or deploys resources.
  Apply is refused. The result uses the shared M365 envelope and contains only an allowlisted projection.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan', 'Apply', 'Verify')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [string] $IsolationOverridePath,
  [string] $ConfigJsonPath,
  [ValidateSet('Development', 'Acceptance', 'Production')] [string] $TargetEnvironment,
  [string] $EnabledCapability,
  [Parameter(Mandatory)] [string] $OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Deployment.psm1') -Force

$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $true
$result = New-M365Result ($(if ($offline) { 'Plan' } elseif ($Mode -eq 'Verify') { 'ReadOnly' } else { 'Apply' }))
$result['deploymentSettings'] = $null
$enabledCapabilities = @()

try {
  $plan = Read-M365Plan -Path $PlanPath -SchemaPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $enabledCapabilities = ConvertFrom-AuditSphereEnabledCapabilityParameter $EnabledCapability
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) {
    $result.errorCode = 'plan-sha256-mismatch'
    exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
  }

  if (-not $offline -and $Mode -eq 'Apply') {
    $result.errorCode = 'deployment-export-apply-not-supported'
    $result.blockedPrerequisites += 'deployment.exporter-does-not-deploy'
    exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
  }

  $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  $sourceFiles = Get-AuditSphereDeploymentSourceFiles -RepositoryRoot $repositoryRoot -IsolationOverridePath $IsolationOverridePath
  $model = $null
  $sourceEvidence = $null
  if (-not $offline -and $Mode -eq 'Verify') {
    $sourceEvidence = Get-AuditSphereExactCheckoutEvidence -RepositoryRoot $repositoryRoot -SourceFiles $sourceFiles -ExpectedSourceSha ([string]$plan.repositoryCommit)
    $sourceState = [string]$sourceEvidence.state
    Add-M365Check $result 'exact-checkout-source-binding' $sourceState ([string]$plan.repositoryCommit) $(if ($sourceState -eq 'PASS') { 'checkout and selected files match plan source' } else { 'exact source binding unavailable or mismatched' }) $sourceEvidence.errorCode
    if ([string]::IsNullOrWhiteSpace($ConfigJsonPath)) {
      $environmentState = if (-not $TargetEnvironment) { 'MISSING' } elseif ($plan.environment -ceq 'Acceptance' -and $TargetEnvironment -ceq 'Acceptance') { 'PASS' } else { 'FAIL' }
      Add-M365Check $result 'plan-and-operator-environment-assertion' $environmentState 'plan.environment and operator declaration are both Acceptance' $(if ($environmentState -eq 'PASS') { 'explicit Acceptance assertion matches plan' } elseif ($environmentState -eq 'MISSING') { 'operator target environment not declared' } else { 'plan and operator target do not both assert Acceptance' }) 'deployment-environment-assertion-mismatch'
      $plannedKinds = @($plan.capabilities | ForEach-Object { [string]$_.kind })
      $selectionState = if ($enabledCapabilities.Count -eq 0) { 'MISSING' } elseif (@($enabledCapabilities | Where-Object { $_ -cnotin $plannedKinds }).Count -gt 0) { 'FAIL' } else { 'PASS' }
      Add-M365Check $result 'operator-enabled-capability-selection' $selectionState 'explicit enabled capability selection from validated plan' $(if ($selectionState -eq 'PASS') { 'operator selection is a subset of planned capabilities' } elseif ($selectionState -eq 'MISSING') { 'no enabled groups declared by operator' } else { 'selection includes a capability absent from plan' }) 'enabled-capability-selection-incomplete'
      $result.errorCode = 'sanitized-compose-config-required'
      $result.blockedPrerequisites += 'deployment.sanitized-compose-config-not-provided'
      $result['deploymentSettings'] = Get-AuditSphereDeploymentSettings -Plan $plan -Model $null -SourceFiles $sourceFiles -EnabledCapabilities $enabledCapabilities -OperatorDeclaredEnvironment $TargetEnvironment -SourceEvidence $sourceEvidence
      $status = if ($sourceState -eq 'FAIL' -or $environmentState -eq 'FAIL' -or $selectionState -eq 'FAIL') { 'DRIFT' } else { 'INCOMPLETE_EVIDENCE' }
      exit (Complete-M365Result $result $status $OutputPath)
    }
    $model = Read-AuditSphereSanitizedComposeConfig -Path $ConfigJsonPath
  }

  $result['deploymentSettings'] = Get-AuditSphereDeploymentSettings -Plan $plan -Model $model -SourceFiles $sourceFiles -EnabledCapabilities $enabledCapabilities -OperatorDeclaredEnvironment $TargetEnvironment -SourceEvidence $sourceEvidence
  $result['deploymentSettings']['sanitizedConfigSha256'] = if ($model) { Get-AuditSphereDeploymentArtifactSha256 $ConfigJsonPath } else { $null }
  Add-M365Check $result 'allowlisted-deployment-settings-export' 'PASS' 'public identifiers and credential names/references only' $(if ($model) { 'sanitized Compose model projected' } else { 'validated plan projected' }) $null
  if ($offline) {
    $result.blockedPrerequisites += @('deployment.sanitized-compose-config-not-provided', 'runtime-settings-not-observed', 'deployment.role-specific-outbound-hosts-not-declared')
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }
  $result.blockedPrerequisites += @(
    'runtime.environment-not-observed',
    'runtime.process-mounts-not-observed',
    'database.schema-compatibility-not-observed',
    'workers.readiness-not-observed',
    'deployment.role-specific-outbound-hosts-not-declared',
    'deployment.rollback-path-not-observed',
    'deployment.live-acceptance-not-authorized'
  )
  $deploymentChecks = Get-AuditSphereDeploymentChecks -Plan $plan -Model $model -EnabledCapabilities $enabledCapabilities -OperatorDeclaredEnvironment $TargetEnvironment -SourceEvidence $sourceEvidence
  foreach ($check in $deploymentChecks) {
    if ($check.id -eq 'exact-checkout-source-binding') { continue }
    Add-M365Check $result $check.id $check.state $check.expected $check.observed $check.errorCode
  }
  $status = if (@($deploymentChecks | Where-Object state -eq 'FAIL').Count -gt 0) { 'DRIFT' } else { 'INCOMPLETE_EVIDENCE' }
  exit (Complete-M365Result $result $status $OutputPath)
}
catch {
  $errorCode = 'deployment-export-failed'
  if ($_.Exception.PSObject.Properties['Code']) {
    $errorCode = [string]$_.Exception.Code
  } elseif ($_.Exception.Message -match '^[a-z0-9][a-z0-9-]{1,79}$') {
    $errorCode = $_.Exception.Message
  }
  $result.errorCode = $errorCode
  $status = if ($errorCode -match 'compose-config-not-sanitized|credential-reference-unsafe|override-not-approved|enabled-capability-selection-invalid') { 'INVALID_OR_UNSAFE' } else { 'INCOMPLETE_EVIDENCE' }
  exit (Complete-M365Result $result $status $OutputPath)
}
