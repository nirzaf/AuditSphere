#Requires -Version 7.4
<#
.SYNOPSIS
  Submit approved managed-staff intents to AuditSphere and verify worker-published receipts.
.DESCRIPTION
  Defaults to offline Plan. Apply preserves detached CMS approval and asks for
  ShouldProcess approval before each reviewed server intent. The isolated worker
  remains the only owner of staff membership and SharePoint permission effects.
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
  [Alias('SessionCookieEnv')] [string] $SessionCookieEnvironmentVariable
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Common.psm1') -Force
$offline = $DryRun.IsPresent -or $WhatIfPreference -or $Mode -eq 'Plan'
Set-M365OfflineMode $offline
$result = New-M365Result ($(if ($offline) { 'Plan' } else { $Mode }))
$client = $null
$cookie = $null
$transport = $null

try {
  $plan = Read-M365Plan $PlanPath (Join-Path $PSScriptRoot 'm365-plan.schema.json')
  $result.repositoryCommit = $plan.repositoryCommit
  $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }

  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.ClientSites.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.ClientSiteIntentHttp.psm1') -Force
  Assert-ClientSitesPlan $plan

  if ($offline) {
    Add-M365Check $result 'plan:client-site-staff' 'PLANNED' 'server-owned staff membership operation' 'offline-plan'
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
        $approval.action -cne 'RECONCILE_CLIENT_SITE_STAFF' -or
        ([DateTimeOffset] $approval.expiresAtUtc) -le [DateTimeOffset]::UtcNow -or
        ([DateTimeOffset] $approval.expiresAtUtc) -gt [DateTimeOffset]::UtcNow.AddHours(24)) { throw 'BLOCKED_APPROVAL' }
  }

  if (-not $ExpectedPlanSha256) { throw 'INVALID_OR_UNSAFE' }
  if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'PREREQUISITE_MISSING' }
  $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
  if (-not (Test-M365ExactCheckoutRoot $repository)) { throw 'INVALID_OR_UNSAFE' }
  $source = & git -C $repository rev-parse HEAD 2>$null
  if ($LASTEXITCODE -ne 0 -or $source -cne $plan.repositoryCommit) { throw 'INVALID_OR_UNSAFE' }
  $dirty = & git -C $repository status --porcelain -- scripts/m365 2>$null
  if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'INVALID_OR_UNSAFE' }
  if (-not $SessionCookieEnvironmentVariable) { throw 'BLOCKED_BOOTSTRAP' }

  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Enrollment.psm1') -Force
  $cookie = Get-M365SessionCookie $SessionCookieEnvironmentVariable
  $http = New-M365EnrollmentHttpClient
  $client = $http.Client
  $providerCommand = Get-Command Invoke-M365ProviderRequest -ErrorAction Stop
  $providerDispatch = { param($request) & $providerCommand $request }.GetNewClosure()
  $clientIds = if ($plan.clientSites.ContainsKey('clientIds')) {
    @($plan.clientSites.clientIds)
  } else {
    @($plan.sites | Where-Object { $_.ContainsKey('clientId') } | ForEach-Object { $_.clientId })
  }
  $transport = New-M365ClientSiteIntentTransport $client ([uri] $plan.publicBaseUri) $cookie $clientIds $providerDispatch
  $requestApproval = {
    param($clientId)
    $PSCmdlet.ShouldProcess("client:$clientId", 'Submit reviewed managed-staff intent to AuditSphere')
  }.GetNewClosure()

  $requested = Invoke-ClientSiteStaffReconciliation $plan $result $Mode $transport $requestApproval
  $status = if ($requested) { 'APPLIED_AND_VERIFIED' } else { 'VERIFIED' }
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP',
    'RESULT_UNCERTAIN','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE','BLOCKED_EXTERNAL')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message }
    elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INVALID_OR_UNSAFE' }
    else { 'PROVIDER_FAILURE' }
  $result.errorCode = "client-site-staff-$($status.ToLowerInvariant())"
} finally {
  $cookie = $null
  $transport = $null
  if ($client) { $client.Dispose() }
}

exit (Complete-M365Result $result $status $OutputPath)
