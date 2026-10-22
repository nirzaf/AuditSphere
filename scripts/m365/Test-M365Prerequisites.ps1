#Requires -Version 7.4
<#
.SYNOPSIS
  Validates an AuditSphere M365 plan and the local PowerShell prerequisites (spec WU-017).
.DESCRIPTION
  Plan / -DryRun / -WhatIf: offline planning. The plan is validated and hashed and local module metadata is read;
  nothing authenticates, contacts a tenant, installs a module or generates a key. Emits PLANNED (exit 0).
  ReadOnly: verifies locally that every pinned module version, manifest hash and cmdlet in modules.lock.json is
  present. Still no tenant contact; missing prerequisites exit 7. Apply is not supported by this script.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan', 'ReadOnly', 'Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ApprovalReference,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force

$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $true   # this script never needs a provider connection
$result = New-M365Result ($(if ($offline) { 'Plan' } else { $Mode }))

try { $plan = Read-M365Plan -Path $PlanPath -SchemaPath (Join-Path $PSScriptRoot 'm365-plan.schema.json') }
catch {
  # Any failure to validate the plan is unsafe; the envelope is still written with a stable code.
  $result.errorCode = if ($_.Exception.PSObject.Properties['Code']) { $_.Exception.Code } else { 'plan-validation-error' }
  exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
}

$result.repositoryCommit = $plan.repositoryCommit
$result.tenantId = $plan.tenantId
$result.planSha256 = Get-M365PlanSha256 $plan
if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -ne $result.planSha256) {
  $result.errorCode = 'plan-sha256-mismatch'; exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
}
if (-not $offline -and $Mode -eq 'Apply') {
  $result.errorCode = 'apply-not-supported-by-prerequisite-check'; exit (Complete-M365Result $result 'INVALID_OR_UNSAFE' $OutputPath)
}

$lock = Get-Content -Raw (Join-Path $PSScriptRoot 'modules.lock.json') | ConvertFrom-Json -AsHashtable
$powershell = $PSVersionTable.PSVersion
Add-M365Check $result 'powershell-version' ($(if ($powershell -ge [version]'7.4') { 'PASS' } else { 'FAIL' })) '>= 7.4' $powershell.ToString()
foreach ($module in $lock.modules) {
  $installed = Get-Module -ListAvailable -Name $module.name | Where-Object { $_.Version.ToString() -eq $module.version } | Select-Object -First 1
  if (-not $installed) {
    Add-M365Check $result "module:$($module.name)" 'MISSING' $module.version 'not installed' 'module-missing'
    $result.blockedPrerequisites += "module:$($module.name)@$($module.version)"
    continue
  }
  $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installed.Path).Hash.ToLowerInvariant()
  $state = if ($hash -eq $module.manifestSha256) { 'PASS' } else { 'FAIL' }
  Add-M365Check $result "module:$($module.name)" $state "$($module.version) sha256:$($module.manifestSha256)" "$($installed.Version) sha256:$hash" ($(if ($state -eq 'FAIL') { 'module-hash-mismatch' }))
  if ($state -eq 'FAIL') { $result.blockedPrerequisites += "module-hash:$($module.name)" }
  if ($offline) { continue }   # cmdlet resolution imports the module; planning only reads metadata
  foreach ($cmdlet in $module.cmdlets) {
    $command = Get-Command -Name $cmdlet -Module $module.name -ErrorAction SilentlyContinue
    Add-M365Check $result "cmdlet:$cmdlet" ($(if ($command) { 'PASS' } else { 'MISSING' })) $module.name ($(if ($command) { "$($command.Source) $($command.Version)" } else { 'not found' }))
    if (-not $command) { $result.blockedPrerequisites += "cmdlet:$cmdlet" }
  }
}

if ($offline) { exit (Complete-M365Result $result 'PLANNED' $OutputPath) }
$failed = @($result.checks | Where-Object { $_.state -ne 'PASS' })
exit (Complete-M365Result $result ($(if ($failed.Count -gt 0) { 'PREREQUISITE_MISSING' } else { 'VERIFIED' })) $OutputPath)
