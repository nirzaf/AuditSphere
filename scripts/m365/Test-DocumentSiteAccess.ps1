#Requires -Version 7.4
# Source-pinned child process: certificate authentication as DOCUMENTS, never the operator session.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ExpectedPlanSha256,
  [string] $AuthenticationModulePath,
  [Parameter(Mandatory)] [string] $OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$result = New-M365Result ($(if ($offline) { 'Plan' } else { 'ReadOnly' }))
$certificate = $null; $disconnect = $null
try {
  $plan = Read-M365Plan $PlanPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit; $result.tenantId = $plan.tenantId; $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Sites.psm1') -Force
  Assert-SelectedSitesPlan $plan
  if ($offline) {
    Add-M365Check $result 'plan:DOCUMENTS:runtime' 'PLANNED' 'separate certificate identity, document root read and known-site 403' 'no credential read or authentication'
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }
  if (-not $ExpectedPlanSha256) { throw 'INVALID_OR_UNSAFE' }
  $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  if (-not (Test-M365ExactCheckoutRoot $repository)) { throw 'INVALID_OR_UNSAFE' }
  $source = & git -C $repository rev-parse HEAD 2>$null
  if ($LASTEXITCODE -ne 0 -or $source -cne $plan.repositoryCommit) { throw 'INVALID_OR_UNSAFE' }
  $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'INVALID_OR_UNSAFE' }
  $module = (Get-Content -Raw (Join-Path $PSScriptRoot 'modules.lock.json') | ConvertFrom-Json -AsHashtable).modules | Where-Object name -EQ 'Microsoft.Graph.Authentication'
  if (-not $AuthenticationModulePath -or -not [IO.Path]::IsPathFullyQualified($AuthenticationModulePath) -or
      (Get-FileHash -LiteralPath $AuthenticationModulePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $module.manifestSha256) { throw 'PREREQUISITE_MISSING' }
  Import-Module -Name $AuthenticationModulePath -Force
  $loaded = Get-Module -Name $module.name
  if (-not $loaded -or $loaded.Version.ToString() -cne $module.version) { throw 'PREREQUISITE_MISSING' }
  $documents = @($plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $certificate = Open-DocumentsRuntimeCertificate $plan
  $connect = Get-Command Connect-MgGraph -Module $module.name
  $disconnect = Get-Command Disconnect-MgGraph -Module $module.name
  $provider = Get-Command Invoke-M365ProviderRequest
  $authenticate = { & $connect -TenantId $plan.tenantId -ClientId $documents.clientId -Certificate $certificate -ContextScope Process -Environment Global -NoWelcome | Out-Null }.GetNewClosure()
  & $provider $authenticate | Out-Null
  $context = & (Get-Command Get-MgContext -Module $module.name)
  $transport = New-EntraGraphTransport $provider (Get-Command Invoke-MgGraphRequest -Module $module.name) `
    (Get-Command Get-MgRequestContext -Module $module.name) (Get-Command Set-MgRequestContext -Module $module.name) -ReturnStatus
  Invoke-DocumentsRuntimeProbe $plan $result $transport $context $certificate.Thumbprint
  $status = 'VERIFIED'
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message } else { 'INVALID_OR_UNSAFE' }
  $result.errorCode = "documents-runtime-$($status.ToLowerInvariant())"
} finally {
  if ($disconnect) { try { & $disconnect | Out-Null } catch { } }
  if ($certificate) { $certificate.Dispose() }
}
exit (Complete-M365Result $result $status $OutputPath)
