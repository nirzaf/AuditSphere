#Requires -Version 7.4
<#
.SYNOPSIS
  Plans or inspects role-specific bindings for the approved Hostinger acceptance Compose target.
.DESCRIPTION
  Plan, DryRun and WhatIf hash the exact approved Compose source files only. Verify reads an
  operator-provided sanitized `docker compose config --format json` artifact; this script never
  invokes Docker, opens .env, looks up credentials, or reports runtime readiness. Apply is refused.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan', 'Apply', 'Verify')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [string] $IsolationOverridePath,
  [string] $ConfigJsonPath,
  [string] $ImageManifestPath,
  [string] $ExpectedImageManifestSha256,
  [long] $ExpectedImageWorkflowRunId,
  [int] $ExpectedImageWorkflowRunAttempt,
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
$result['deploymentEvidence'] = $null
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
    $result.errorCode = 'deployment-apply-not-supported'
    $result.blockedPrerequisites += 'deployment.live-apply-not-authorized-or-implemented'
    exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
  }

  $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  $sourceFiles = Get-AuditSphereDeploymentSourceFiles -RepositoryRoot $repositoryRoot -IsolationOverridePath $IsolationOverridePath
  Add-M365Check $result 'approved-deployment-target' 'PASS' 'infra/hostinger-acceptance/compose.yaml' 'exact approved Hostinger acceptance target' $null
  if ($IsolationOverridePath) {
    Add-M365Check $result 'optional-isolation-override-explicit' 'PASS' 'only when explicitly selected' 'operator selected the approved example file; it is not auto-applied' $null
  }

  if ($offline) {
    $result['deploymentEvidence'] = New-AuditSpherePlannedDeploymentEvidence -SourceFiles $sourceFiles -ExpectedSourceSha $plan.repositoryCommit -ExpectedEnvironment $plan.environment -EnabledCapabilities $enabledCapabilities
    $result.blockedPrerequisites += @(
      'deployment.sanitized-compose-config-not-provided',
      'runtime.environment-not-observed',
      'runtime.process-mounts-not-observed',
      'deployment.role-specific-outbound-hosts-not-declared',
      'database.schema-compatibility-not-observed',
      'workers.readiness-not-observed',
      'deployment.rollback-path-not-observed',
      'deployment.live-acceptance-not-authorized'
    )
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }

  if ($Mode -ne 'Verify') {
    $result.errorCode = 'deployment-mode-unsupported'
    exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
  }
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
    $result['deploymentEvidence'] = New-AuditSpherePlannedDeploymentEvidence -SourceFiles $sourceFiles -ExpectedSourceSha $plan.repositoryCommit -ExpectedEnvironment $plan.environment -EnabledCapabilities $enabledCapabilities -SourceEvidence $sourceEvidence
    $status = if ($sourceState -eq 'FAIL' -or $environmentState -eq 'FAIL' -or $selectionState -eq 'FAIL') { 'DRIFT' } else { 'INCOMPLETE_EVIDENCE' }
    exit (Complete-M365Result $result $status $OutputPath)
  }

  $model = Read-AuditSphereSanitizedComposeConfig -Path $ConfigJsonPath
  $verifiedImages = $null
  if ($ImageManifestPath -or $ExpectedImageManifestSha256 -or $ExpectedImageWorkflowRunId -or $ExpectedImageWorkflowRunAttempt) {
    if (-not $ImageManifestPath -or $ExpectedImageManifestSha256 -cnotmatch '^[0-9a-f]{64}$' -or
        $ExpectedImageWorkflowRunId -lt 1 -or $ExpectedImageWorkflowRunAttempt -lt 1) {
      throw 'deployment-image-manifest-invalid'
    }
    $verifier = Join-Path $repositoryRoot 'scripts/release/hostinger_image_manifest.py'
    $references = & python3 $verifier verify --manifest $ImageManifestPath --manifest-sha256 $ExpectedImageManifestSha256 `
      --repository 'nirzaf/AuditSphere' --source-sha $plan.repositoryCommit --run-id $ExpectedImageWorkflowRunId `
      --run-attempt $ExpectedImageWorkflowRunAttempt --emit-references 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $references) { throw 'deployment-image-manifest-invalid' }
    $verifiedImages = $references | ConvertFrom-Json -AsHashtable
  } else {
    $result.blockedPrerequisites += 'deployment.verified-image-manifest-not-provided'
  }
  $deploymentChecks = Get-AuditSphereDeploymentChecks -Plan $plan -Model $model -EnabledCapabilities $enabledCapabilities -OperatorDeclaredEnvironment $TargetEnvironment -SourceEvidence $sourceEvidence -VerifiedImageReferences $verifiedImages
  foreach ($check in $deploymentChecks) {
    if ($check.id -eq 'exact-checkout-source-binding') { continue }
    Add-M365Check $result $check.id $check.state $check.expected $check.observed $check.errorCode
  }
  $settings = Get-AuditSphereDeploymentSettings -Plan $plan -Model $model -SourceFiles $sourceFiles -EnabledCapabilities $enabledCapabilities -OperatorDeclaredEnvironment $TargetEnvironment -SourceEvidence $sourceEvidence
  $settings['sanitizedConfigSha256'] = Get-AuditSphereDeploymentArtifactSha256 $ConfigJsonPath
  $settings['evidenceLevel'] = 'SANITIZED_COMPOSE_MODEL_ONLY'
  if ($null -ne $verifiedImages) {
    $settings['artifactProvenanceState'] = 'VERIFIED_IMAGE_MANIFEST'
    $settings['artifactProvenance'] = [ordered]@{
      manifestSha256 = $ExpectedImageManifestSha256
      sourceSha = $plan.repositoryCommit
      workflowRunId = $ExpectedImageWorkflowRunId
      workflowRunAttempt = $ExpectedImageWorkflowRunAttempt
      immutableImageReferences = $verifiedImages
    }
  }
  $result['deploymentEvidence'] = $settings
  $result.blockedPrerequisites += @(
    'runtime.environment-not-observed',
    'runtime.process-mounts-not-observed',
    'database.schema-compatibility-not-observed',
    'workers.readiness-not-observed',
    'deployment.role-specific-outbound-hosts-not-declared',
    'deployment.rollback-path-not-observed',
    'deployment.live-acceptance-not-authorized'
  )
  $failed = @($deploymentChecks | Where-Object state -eq 'FAIL')
  if ($failed.Count -gt 0) {
    exit (Complete-M365Result $result 'DRIFT' $OutputPath)
  }
  exit (Complete-M365Result $result 'INCOMPLETE_EVIDENCE' $OutputPath)
}
catch {
  $errorCode = 'deployment-validation-failed'
  if ($_.Exception.PSObject.Properties['Code']) {
    $errorCode = [string]$_.Exception.Code
  } elseif ($_.Exception.Message -match '^[a-z0-9][a-z0-9-]{1,79}$') {
    $errorCode = $_.Exception.Message
  }
  $result.errorCode = $errorCode
  $status = if ($errorCode -match 'compose-config-not-sanitized|credential-reference-unsafe|override-not-approved|enabled-capability-selection-invalid|deployment-image-manifest-invalid') { 'INVALID_OR_UNSAFE' } else { 'INCOMPLETE_EVIDENCE' }
  exit (Complete-M365Result $result $status $OutputPath)
}
