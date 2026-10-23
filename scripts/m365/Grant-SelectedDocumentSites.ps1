#Requires -Version 7.4
<#
.SYNOPSIS
  WU-020: grant and verify selected SharePoint document resources independently of site provisioning.
.DESCRIPTION
  Defaults to offline Plan. Live modes require an already authenticated pinned Graph module.
  Apply additionally requires a detached CMS approval signed by an independently trusted
  certificate, an exact plan hash, ShouldProcess for each mutation, and the local intent journal.
  Never grants tenant-wide document roles, user permissions, or creates client sites.
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
  $result.repositoryCommit = $plan.repositoryCommit; $result.tenantId = $plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $plan
  if ($ExpectedPlanSha256 -and $ExpectedPlanSha256 -cne $result.planSha256) { throw 'INVALID_OR_UNSAFE' }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -Force
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Sites.psm1') -Force
  Assert-SelectedSitesPlan $plan
  if ($offline) {
    if ($plan.ContainsKey('sites') -and $plan.sites) {
      foreach ($site in $plan.sites) {
        Add-M365Check $result "plan:site:$($site.siteId)" 'PLANNED' 'document-app write grant' "permission:$($site.permission)"
      }
    } else {
      Add-M365Check $result 'plan:selected-sites' 'PLANNED' 'document-app site grants' 'none-configured'
    }
    if ($plan.ContainsKey('negativeControls') -and $plan.negativeControls -and $plan.negativeControls.ContainsKey('siteIds')) {
      foreach ($control in $plan.negativeControls.siteIds) {
        Add-M365Check $result "plan:negative-control:site:$control" 'PLANNED' 'known control to probe as DOCUMENTS' 'planned-not-verified'
      }
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
    $cms.Decode([System.IO.File]::ReadAllBytes($ApprovalSignaturePath)); $cms.CheckSignature($true)
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
        $approval.action -cne 'GRANT_SELECTED_DOCUMENT_SITES' -or
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
  if (-not (Test-M365ExactCheckoutRoot $repository)) { throw 'INVALID_OR_UNSAFE' }
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

  # Validate existing private custody before any grant, without exporting a key or changing SDK identity.
  $runtimeCertificate = Open-DocumentsRuntimeCertificate $plan
  $runtimeCertificate.Dispose()

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
    $journalPath = Get-SitesJournalPath $plan $StateDirectory
    foreach ($file in @($journalPath, "$journalPath.lock")) {
      if ((Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).LinkTarget) { throw 'INVALID_OR_UNSAFE' }
    }
    $lockStream = [System.IO.File]::Open("$journalPath.lock", 'OpenOrCreate', 'ReadWrite', 'None')
    if (Test-Path -LiteralPath $journalPath) { $journal = Get-Content -Raw -LiteralPath $journalPath | ConvertFrom-Json -AsHashtable }
  }

  $scriptCmdlet = $PSCmdlet
  $intentCommand = Get-Command Save-SitesMutationIntent
  $approve = {
    param($method, $path, $body)
    if (-not $scriptCmdlet.ShouldProcess($path, $method)) { return $false }
    & $intentCommand $journal $journalPath $method $path $body $result.planSha256
    return $true
  }.GetNewClosure()
  $confirmCommand = Get-Command Confirm-SitesMutationIntent
  $confirm = { param($path, $body) & $confirmCommand $journal $journalPath $path $body }.GetNewClosure()

  $runtimeScript = Join-Path $PSScriptRoot 'Test-DocumentSiteAccess.ps1'
  $moduleManifest = Join-Path $loaded.ModuleBase $module.manifestFile
  $absolutePlan = [IO.Path]::GetFullPath($PlanPath)
  $runtimeProbe = {
    $folder = Join-Path ([IO.Path]::GetTempPath()) ('auditsphere-documents-' + [guid]::NewGuid().ToString('N'))
    $process = [Diagnostics.Process]::new()
    try {
      if ($IsWindows) { [IO.Directory]::CreateDirectory($folder) | Out-Null }
      else { [IO.Directory]::CreateDirectory($folder, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) | Out-Null }
      $receipt = Join-Path $folder 'runtime.json'
      $process.StartInfo = [Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath)
      $process.StartInfo.UseShellExecute = $false
      $process.StartInfo.RedirectStandardOutput = $true; $process.StartInfo.RedirectStandardError = $true
      foreach ($argument in @('-NoProfile','-NonInteractive','-File',$runtimeScript,'-PlanPath',$absolutePlan,'-Mode','ReadOnly',
          '-ExpectedPlanSha256',$result.planSha256,'-AuthenticationModulePath',$moduleManifest,'-OutputPath',$receipt)) { $process.StartInfo.ArgumentList.Add($argument) }
      if (-not $process.Start()) { throw 'PROVIDER_FAILURE' }
      $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
      if (-not $process.WaitForExit(300000)) { $process.Kill($true); $process.WaitForExit(); throw 'PROVIDER_FAILURE' }
      $stdout.GetAwaiter().GetResult() | Out-Null; $stderr.GetAwaiter().GetResult() | Out-Null
      if (-not (Test-Path -LiteralPath $receipt -PathType Leaf) -or (Get-Item -LiteralPath $receipt).Length -gt 1048576) { throw 'INCOMPLETE_EVIDENCE' }
      $parse = @{ AsHashtable = $true; Depth = 32 }
      if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
      $proof = [IO.File]::ReadAllText($receipt) | ConvertFrom-Json @parse
      if ($proof.repositoryCommit -cne $plan.repositoryCommit -or $proof.tenantId -ne $plan.tenantId -or $proof.planSha256 -cne $result.planSha256 -or
          $proof.networkCalls -isnot [long] -and $proof.networkCalls -isnot [int]) { throw 'INVALID_OR_UNSAFE' }
      Add-M365ObservedNetworkCalls ([int] $proof.networkCalls)
      if ($process.ExitCode -ne (Get-M365ExitCode $proof.status)) { throw 'INCOMPLETE_EVIDENCE' }
      return $proof
    } finally {
      $process.Dispose()
      if (Test-Path -LiteralPath $folder) { [IO.Directory]::Delete($folder, $true) }
    }
  }.GetNewClosure()
  Invoke-SelectedSitesReconciliation $plan $result $Mode $transport $approve $confirm $runtimeProbe

  if (@($result.checks | Where-Object { $_.state -eq 'FAIL' }).Count -gt 0) { throw 'DRIFT' }
  if (@($result.checks | Where-Object { $_.state -cne 'PASS' -or -not $_.expiresAtUtc -or ([DateTimeOffset] $_.expiresAtUtc) -le [DateTimeOffset]::UtcNow }).Count -gt 0) { throw 'INCOMPLETE_EVIDENCE' }
  $status = if ($Mode -eq 'Apply') { 'APPLIED_AND_VERIFIED' } else { 'VERIFIED' }
} catch {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','RESULT_UNCERTAIN','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE')
  $status = if ($_.Exception.Message -cin $known) { $_.Exception.Message } else { 'INVALID_OR_UNSAFE' }
  $result.errorCode = "sites-$($status.ToLowerInvariant())"
} finally {
  if ($lockStream) { $lockStream.Dispose() }
}
exit (Complete-M365Result $result $status $OutputPath)
