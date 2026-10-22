#Requires -Version 7.4
<#
.SYNOPSIS
  WU-018: reconcile isolated owned Entra registrations and public certificates.
.DESCRIPTION
  Defaults to offline Plan. Live modes require an already authenticated pinned Graph module.
  Apply additionally requires a detached CMS approval signed by an independently trusted
  certificate, an exact plan hash, ShouldProcess for each mutation, and the local intent journal.
  No consent grants, administrator roles, private keys, or report-signing certificates are created.
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
  [string] $StateDirectory,
  [switch] $CertificatesOnly
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
  $result.repositoryCommit = $plan.repositoryCommit; $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }
  if ($offline) {
    foreach ($capability in $plan.capabilities) {
      Add-M365Check $result "plan:$($capability.kind)" 'PLANNED' 'exact owned identity and public certificate' "sha256:$($capability.publicCertificateSha256)"
    }
    exit (Complete-M365Result $result 'PLANNED' $OutputPath)
  }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -Force
  if ($Mode -eq 'Apply') {
    if (-not $ExpectedPlanSha256 -or -not $ApprovalPath -or -not $ApprovalSignaturePath -or
        $ApprovalSignerSha256 -cnotmatch '^[a-f0-9]{64}$' -or -not $ApprovalReference) { throw 'BLOCKED_APPROVAL' }
    if (-not $plan.ContainsKey('approvalReference') -or $plan.approvalReference -cne $ApprovalReference) { throw 'BLOCKED_APPROVAL' }
    # Verify exact bytes and a separately trusted signer, not merely a self-asserted approval string.
    if ((Get-Item -LiteralPath $ApprovalPath).Length -gt 8192 -or
        (Get-Item -LiteralPath $ApprovalSignaturePath).Length -gt 65536) { throw 'BLOCKED_APPROVAL' }
    $approvalBytes = [System.IO.File]::ReadAllBytes($ApprovalPath)
    $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new($approvalBytes), $true)
    $cms.Decode([System.IO.File]::ReadAllBytes($ApprovalSignaturePath)); $cms.CheckSignature($true)
    if ($cms.SignerInfos.Count -ne 1) { throw 'BLOCKED_APPROVAL' }
    $signer = $cms.SignerInfos[0].Certificate
    $fingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($signer.RawData)).ToLowerInvariant()
    if ($fingerprint -cne $ApprovalSignerSha256 -or $signer.NotAfter.ToUniversalTime() -le [datetime]::UtcNow -or
        $signer.NotBefore.ToUniversalTime() -gt [datetime]::UtcNow) { throw 'BLOCKED_APPROVAL' }
    $approval = [System.Text.Encoding]::UTF8.GetString($approvalBytes) | ConvertFrom-Json -AsHashtable
    $required = @('planSha256','tenantId','operatorClientId','approvalReference','expiresAtUtc','action','exclusiveCertificateWindow','stateDirectory')
    if (@($approval.Keys | Where-Object { $_ -cnotin $required }).Count -or
        @($required | Where-Object { -not $approval.ContainsKey($_) }).Count -or
        $approval.planSha256 -cne $result.planSha256 -or $approval.tenantId -ne $plan.tenantId -or
        $approval.operatorClientId -ne $plan.operator.clientId -or $approval.approvalReference -cne $ApprovalReference -or
        $approval.exclusiveCertificateWindow -isnot [bool] -or -not $approval.exclusiveCertificateWindow -or
        $approval.stateDirectory -cne (Get-EntraStateDirectory $StateDirectory) -or
        $approval.action -cne $(if ($CertificatesOnly) { 'SET_PUBLIC_CERTIFICATES' } else { 'RECONCILE_ENTRA_REGISTRATIONS' }) -or
        ([DateTimeOffset] $approval.expiresAtUtc) -le [DateTimeOffset]::UtcNow -or
        ([DateTimeOffset] $approval.expiresAtUtc) -gt [DateTimeOffset]::UtcNow.AddHours(24)) { throw 'BLOCKED_APPROVAL' }
  }
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
  $context = & $contextCommand
  if (-not (Test-EntraOperatorContext $context $plan)) { throw 'BLOCKED_BOOTSTRAP' }
  $transport = New-EntraGraphTransport $providerCommand $graphCommand $getRequestContext $setRequestContext
  $operator = & $transport 'GET' "/v1.0/servicePrincipals/$($plan.operator.servicePrincipalObjectId)?`$select=id,appId,appOwnerOrganizationId" $null
  if ($operator.id -ne $plan.operator.servicePrincipalObjectId -or $operator.appId -ne $plan.operator.clientId -or
      $operator.appOwnerOrganizationId -ne $plan.tenantId) { throw 'INVALID_OR_UNSAFE' }
  $journal = @{}
  $journalPath = $null
  if ($Mode -eq 'Apply') {
    if ($IsWindows) { [System.IO.Directory]::CreateDirectory($StateDirectory) | Out-Null }
    else {
      [System.IO.Directory]::CreateDirectory($StateDirectory, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute) | Out-Null
      $unsafe = [System.IO.UnixFileMode]::GroupWrite -bor [System.IO.UnixFileMode]::OtherWrite
      if (([System.IO.File]::GetUnixFileMode($StateDirectory) -band $unsafe) -ne 0) { throw 'INVALID_OR_UNSAFE' }
    }
    if ((Get-Item -LiteralPath $StateDirectory).LinkTarget) { throw 'INVALID_OR_UNSAFE' }
    $journalPath = Get-EntraJournalPath $plan
    foreach ($file in @($journalPath, "$journalPath.lock")) {
      if ((Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).LinkTarget) { throw 'INVALID_OR_UNSAFE' }
    }
    $lockStream = [System.IO.File]::Open("$journalPath.lock", 'OpenOrCreate', 'ReadWrite', 'None')
    if (Test-Path -LiteralPath $journalPath) { $journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json -AsHashtable }
  }
  $scriptCmdlet = $PSCmdlet
  $intentCommand = Get-Command Save-EntraMutationIntent
  $approve = {
    param($method, $path, $body)
    if (-not $scriptCmdlet.ShouldProcess($path, $method)) { return $false }
    & $intentCommand $journal $journalPath $method $path $body $result.planSha256
    return $true
  }.GetNewClosure()
  Invoke-EntraReconciliation $plan $result $Mode $transport $approve -CertificatesOnly:$CertificatesOnly
  if (@($result.checks | Where-Object { $_.state -cne 'PASS' -or -not $_.expiresAtUtc -or ([DateTimeOffset] $_.expiresAtUtc) -le [DateTimeOffset]::UtcNow }).Count) { throw 'INCOMPLETE_EVIDENCE' }
  $status = if ($Mode -eq 'Apply') { 'APPLIED_AND_VERIFIED' } else { 'VERIFIED' }
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','RESULT_UNCERTAIN','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message } else { 'INVALID_OR_UNSAFE' }
  $result.errorCode = "entra-$($status.ToLowerInvariant())"
} finally { if ($lockStream) { $lockStream.Dispose() } }
exit (Complete-M365Result $result $status $OutputPath)
