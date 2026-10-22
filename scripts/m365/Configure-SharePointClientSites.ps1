#Requires -Version 7.4
<#
.SYNOPSIS
  WU-021: Automate approved SharePoint client site provisioning and worker write grant reconciliation.
.DESCRIPTION
  Defaults to offline Plan. Live modes require authenticated Graph and SharePoint module bindings.
  Apply additionally requires a detached CMS approval signed by an independently trusted certificate,
  an exact plan hash, ShouldProcess for each mutation, and the local intent journal.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ApprovalReference,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [string] $ApprovalPath,
  [string] $ApprovalSignaturePath,
  [string] $ApprovalSignerSha256,
  [string] $StateDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$result = New-M365Result ($(if ($offline) { 'Plan' } else { $Mode }))
$lockStream = $null

try {
  $plan = Read-M365Plan $PlanPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }

  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Sites.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.ClientSites.psm1') -Force
  Assert-ClientSitesPlan $plan

  if ($offline) {
    foreach ($site in $plan.sites) {
      Add-M365Check $result "plan:client-site:$($site.siteUrl)" 'PLANNED' 'SharePoint client site provisioning' "owner:$($plan.clientSites.custodianObjectId)"
      Add-M365Check $result "plan:client-site:grant:$($site.siteId)" 'PLANNED' 'DOCUMENTS worker application grant' 'planned-write'
    }
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }

  if ($Mode -eq 'Apply') {
    if (-not $ExpectedPlanSha256 -or -not $ApprovalPath -or -not $ApprovalSignaturePath -or
        $ApprovalSignerSha256 -cnotmatch '^[a-f0-9]{64}$' -or -not $ApprovalReference) { throw 'BLOCKED_APPROVAL' }
    if (-not $plan.ContainsKey('approvalReference') -or $plan.approvalReference -cne $ApprovalReference) { throw 'BLOCKED_APPROVAL' }
    if ((Get-Item -LiteralPath $ApprovalPath).Length -gt 8192 -or
        (Get-Item -LiteralPath $ApprovalSignaturePath).Length -gt 65536) { throw 'BLOCKED_APPROVAL' }
    $approvalBytes = [System.IO.File]::ReadAllBytes($ApprovalPath)
    $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new($approvalBytes), $true)
    $cms.Decode([System.IO.File]::ReadAllBytes($ApprovalSignaturePath))
    $cms.CheckSignature($true)
    if ($cms.SignerInfos.Count -ne 1) { throw 'BLOCKED_APPROVAL' }
    $signer = $cms.SignerInfos[0].Certificate
    $fingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($signer.RawData)).ToLowerInvariant()
    if ($fingerprint -cne $ApprovalSignerSha256 -or $signer.NotAfter.ToUniversalTime() -le [datetime]::UtcNow -or
        $signer.NotBefore.ToUniversalTime() -gt [datetime]::UtcNow) { throw 'BLOCKED_APPROVAL' }
    $approval = [System.Text.Encoding]::UTF8.GetString($approvalBytes) | ConvertFrom-Json -AsHashtable
    $required = @('planSha256','tenantId','operatorClientId','approvalReference','expiresAtUtc','action','stateDirectory')
    if (@($approval.Keys | Where-Object { $_ -cnotin $required }).Count -or
        @($required | Where-Object { -not $approval.ContainsKey($_) }).Count -or
        $approval.planSha256 -cne $result.planSha256 -or $approval.tenantId -ne $plan.tenantId -or
        $approval.operatorClientId -ne $plan.operator.clientId -or $approval.approvalReference -cne $ApprovalReference -or
        $approval.stateDirectory -cne (Get-EntraStateDirectory $StateDirectory) -or
        $approval.action -cne 'CONFIGURE_SHAREPOINT_CLIENT_SITES' -or
        ([DateTimeOffset] $approval.expiresAtUtc) -le [DateTimeOffset]::UtcNow -or
        ([DateTimeOffset] $approval.expiresAtUtc) -gt [DateTimeOffset]::UtcNow.AddHours(24)) { throw 'BLOCKED_APPROVAL' }
  }

  if (-not $ExpectedPlanSha256) { throw 'INVALID_OR_UNSAFE' }
  $module = (Get-Content -Raw (Join-Path $PSScriptRoot 'modules.lock.json') | ConvertFrom-Json -AsHashtable).modules |
    Where-Object name -eq 'Microsoft.Graph.Authentication'
  $loaded = Get-Module -Name $module.name
  if (-not $loaded -or $loaded.Version.ToString() -ne $module.version -or
      (Get-FileHash -LiteralPath (Join-Path $loaded.ModuleBase $module.manifestFile) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $module.manifestSha256) { throw 'PREREQUISITE_MISSING' }
  if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'PREREQUISITE_MISSING' }
  $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  $source = & git -C $repository rev-parse HEAD 2>$null
  if ($LASTEXITCODE -ne 0 -or $source -cne $plan.repositoryCommit) { throw 'INVALID_OR_UNSAFE' }
  $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'INVALID_OR_UNSAFE' }

  $contextCommand = Get-Command Get-MgContext -Module $module.name
  $graphCommand = Get-Command Invoke-MgGraphRequest -Module $module.name
  $providerCommand = Get-Command Invoke-M365ProviderRequest
  $getRequestContext = Get-Command Get-MgRequestContext -Module $module.name
  $setRequestContext = Get-Command Set-MgRequestContext -Module $module.name

  if ($Mode -eq 'Apply') { $StateDirectory = Get-EntraStateDirectory $StateDirectory }
  $journalPath = Get-ClientSitesJournalPath $plan $StateDirectory
  $journal = @{}
  if (Test-Path -LiteralPath $journalPath) {
    $journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json -AsHashtable
  }
  if ($Mode -eq 'Apply') {
    $lockPath = "$journalPath.lock"
    $lockStream = [System.IO.FileStream]::new($lockPath, [System.IO.FileMode]::OpenOrCreate,
      [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
  }

  $context = & $contextCommand
  if (-not (Test-EntraOperatorContext $context $plan)) { throw 'BLOCKED_BOOTSTRAP' }
  $graphTransport = New-EntraGraphTransport $providerCommand $graphCommand $getRequestContext $setRequestContext
  $sharePointTransport = $graphTransport # In live deployment, transport handles SharePoint auth endpoint

  $approve = {
    param($method, $path, $body)
    if ($Mode -ne 'Apply') { return $false }
    if (-not $PSCmdlet.ShouldProcess($path, "$method SharePoint client site")) { return $false }
    Save-ClientSitesMutationIntent $journal $journalPath $method $path $body $result.planSha256
    return $true
  }.GetNewClosure()

  $confirm = {
    param($path, $body)
    if ($Mode -eq 'Apply') { Confirm-ClientSitesMutationIntent $journal $journalPath $path $body }
  }.GetNewClosure()

  Invoke-SharePointClientSitesReconciliation $plan $result $Mode $sharePointTransport $approve $confirm $graphTransport
  $status = if ($result.mutationsConfirmed -gt 0) { 'APPLIED_AND_VERIFIED' } else { 'VERIFIED' }
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP','RESULT_UNCERTAIN','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message } else { 'INVALID_OR_UNSAFE' }
  $result.errorCode = "client-sites-$($status.ToLowerInvariant())"
} finally {
  if ($lockStream) { $lockStream.Dispose() }
}

exit (Complete-M365Result $result $status $OutputPath)
