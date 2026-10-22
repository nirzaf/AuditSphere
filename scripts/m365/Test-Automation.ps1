#Requires -Version 7.4
<#
.SYNOPSIS
  Offline assertion runner for the AuditSphere M365 package (spec WU-017). Never contacts a network.
.EXAMPLE
  pwsh -NoProfile -NonInteractive -File scripts/m365/Test-Automation.ps1 -Unit WU-017 -Offline
#>
param(
  [ValidateSet('WU-017', 'WU-018', 'WU-019', 'WU-020', 'WU-021')] [string] $Unit = 'WU-017',
  [switch] $Offline
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Test-Automation runs offline only; pass -Offline.' }
if ($Unit -eq 'WU-021') {
  & (Join-Path $PSScriptRoot 'tests/ClientSites.Tests.ps1') -Offline
  exit $LASTEXITCODE
}
if ($Unit -eq 'WU-020') {
  & (Join-Path $PSScriptRoot 'tests/Grant-SelectedDocumentSites.Tests.ps1') -Offline
  exit $LASTEXITCODE
}
if ($Unit -eq 'WU-019') {
  & (Join-Path $PSScriptRoot 'tests/Grant-TenantPermissions.Tests.ps1') -Offline
  exit $LASTEXITCODE
}
if ($Unit -eq 'WU-018') {
  & (Join-Path $PSScriptRoot 'tests/Provision-EntraApps.Tests.ps1') -Offline
  exit $LASTEXITCODE
}

$root = $PSScriptRoot
$fixture = Join-Path $root 'tests/fixtures/plan.synthetic.json'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-m365-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$failures = [System.Collections.Generic.List[string]]::new()
$passed = 0

function Invoke-Prerequisites([string] $Plan, [string[]] $Extra) {
  $output = Join-Path $work ([guid]::NewGuid().ToString('N') + '.json')
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Test-M365Prerequisites.ps1') -PlanPath $Plan -OutputPath $output @Extra | Out-Null
  return @{ Exit = $LASTEXITCODE; Result = (Get-Content -Raw $output | ConvertFrom-Json -AsHashtable) }
}

function Write-Variant([scriptblock] $Change) {
  $plan = Get-Content -Raw $fixture | ConvertFrom-Json -AsHashtable -Depth 32
  & $Change $plan
  $path = Join-Path $work ([guid]::NewGuid().ToString('N') + '.plan.json')
  $plan | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
  return $path
}

function Assert-That([string] $Name, [bool] $Condition, [string] $Detail = '') {
  if ($Condition) { $script:passed++ } else { $failures.Add("$Name $Detail") }
}

try {
  # Valid plan: PLANNED, exit 0, no network, no mutation, deterministic digest independent of property order.
  $planned = Invoke-Prerequisites $fixture @('-DryRun')
  Assert-That 'dryrun-planned' ($planned.Exit -eq 0 -and $planned.Result.status -eq 'PLANNED') "exit=$($planned.Exit) status=$($planned.Result.status)"
  Assert-That 'dryrun-no-network' ($planned.Result.networkCalls -eq 0 -and $planned.Result.mutationsAttempted -eq 0)
  $whatIf = Invoke-Prerequisites $fixture @('-Mode', 'ReadOnly', '-WhatIf')
  Assert-That 'whatif-is-offline-plan' ($whatIf.Exit -eq 0 -and $whatIf.Result.status -eq 'PLANNED' -and $whatIf.Result.networkCalls -eq 0)
  $reordered = Join-Path $work 'reordered.plan.json'
  $source = Get-Content -Raw $fixture | ConvertFrom-Json -AsHashtable -Depth 32
  $flipped = [ordered]@{}; foreach ($key in ($source.Keys | Sort-Object -Descending)) { $flipped[$key] = $source[$key] }
  $flipped | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $reordered -Encoding utf8NoBOM
  Assert-That 'digest-order-independent' ((Invoke-Prerequisites $reordered @('-DryRun')).Result.planSha256 -eq $planned.Result.planSha256)
  $pinned = Invoke-Prerequisites $fixture @('-DryRun', '-ExpectedPlanSha256', ('0' * 64))
  Assert-That 'digest-mismatch-refused' ($pinned.Exit -eq 4 -and $pinned.Result.errorCode -eq 'plan-sha256-mismatch')

  $newIdentity = Invoke-Prerequisites (Write-Variant { param($p) $p.capabilities[0].clientId = $null }) @('-DryRun')
  Assert-That 'explicit-new-identity-plan' ($newIdentity.Exit -eq 0 -and $newIdentity.Result.status -eq 'PLANNED' -and $newIdentity.Result.networkCalls -eq 0)

  # Approved permission profiles remain usable; planning never grants these roles.
  $profiles = @{
    LOGIN = @(); USER_CREATE = @('User.Create'); INVITATIONS = @('User.Invite.All')
    GROUP_MEMBERSHIP = @('GroupMember.ReadWrite.All'); MAIL = @('Mail.Send')
    CLIENT_SITES = @('Sites.FullControl.All', 'Sites.FullControl.All')
  }
  foreach ($kind in $profiles.Keys) {
    $path = Write-Variant {
      param($p)
      $p.capabilities = @($p.capabilities[0])
      $p.capabilities[0].kind = $kind
      $p.capabilities[0].expectedApplicationRoles = @(
        for ($i = 0; $i -lt $profiles[$kind].Count; $i++) {
          @{ resourceAppId = $(if ($i -eq 1) { '00000003-0000-0ff1-ce00-000000000000' } else { '00000003-0000-0000-c000-000000000000' }); value = $profiles[$kind][$i] }
        }
      )
      if ($kind -eq 'MAIL') {
        $p.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050') }
      }
    }
    $outcome = Invoke-Prerequisites $path @('-DryRun')
    Assert-That "allowed-profile-$kind" ($outcome.Exit -eq 0 -and $outcome.Result.status -eq 'PLANNED' -and $outcome.Result.networkCalls -eq 0)
    if ($kind -eq 'MAIL') {
      $rbac = Get-Content -Raw $path | ConvertFrom-Json -AsHashtable
      $rbac.mail.permissionModel = 'EXCHANGE_RBAC_RESOURCE_SCOPE'
      $rbac.capabilities[0].expectedApplicationRoles = @()
      $rbac | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
      $outcome = Invoke-Prerequisites $path @('-DryRun')
      Assert-That 'rbac-no-unscoped-role' ($outcome.Exit -eq 0 -and $outcome.Result.status -eq 'PLANNED')
      $rbac.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
      $rbac | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
      Assert-That 'rbac-additive-unscoped-role-refused' ((Invoke-Prerequisites $path @('-DryRun')).Exit -eq 4)
    }
  }

  # Unsafe or invalid plans: INVALID_OR_UNSAFE (exit 4) before anything else happens.
  $cases = [ordered]@{
    'invalid-guid'          = { param($p) $p.tenantId = 'not-a-guid' }
    'secret-field'          = { param($p) $p.capabilities[0]['clientSecretValue'] = 'x' }
    'unknown-property'      = { param($p) $p['broadScopes'] = @('Directory.Read.All') }
    'duplicate-capability'  = { param($p) $p.capabilities[1].kind = $p.capabilities[0].kind }
    'reused-client-id'      = { param($p) $p.capabilities[1].clientId = $p.capabilities[0].clientId }
    'operator-as-runtime'   = { param($p) $p.capabilities[0].clientId = $p.operator.clientId }
    'invalid-host'          = { param($p) $p.clientSites.host = 'evil.example.com' }
    'non-utc-cutoff'        = { param($p) $p.clientSites.rolloutCutoffUtc = '2026-10-01T00:00:00+03:00' }
    'missing-mail-config'   = { param($p) $p.capabilities[0].kind = 'MAIL'; $p.capabilities[0].expectedApplicationRoles[0].value = 'Mail.Send' }
    'missing-sites-config'  = {
      param($p)
      $p.capabilities[0].kind = 'CLIENT_SITES'
      $p.capabilities[0].expectedApplicationRoles = @(
        @{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Sites.FullControl.All' },
        @{ resourceAppId = '00000003-0000-0ff1-ce00-000000000000'; value = 'Sites.FullControl.All' }
      )
      $p.Remove('clientSites') | Out-Null
    }
    'new-with-existing-id'  = { param($p) $p.capabilities[0].clientId = $null; $p.capabilities[0].applicationObjectId = '00000000-0000-4000-8000-000000000099' }
    'duplicate-object-id'   = { param($p) foreach ($cap in $p.capabilities) { $cap.applicationObjectId = '00000000-0000-4000-8000-000000000099' } }
    'relative-public-cert'  = { param($p) $p.capabilities[0].publicCertificatePath = 'relative.cer' }
    'login-user-read'       = { param($p) $p.capabilities[0].kind = 'LOGIN'; $p.capabilities[0].expectedApplicationRoles = @(); $p.capabilities[0].signInScopes = @('User.Read') }
    'unapproved-scope'      = { param($p) $p.capabilities[0].signInScopes = @('Directory.Read.All') }
    'runtime-signin-scope'  = { param($p) $p.capabilities[1].signInScopes = @('User.Read') }
    'broad-directory-role'  = { param($p) $p.capabilities[0].expectedApplicationRoles[0].value = 'Directory.ReadWrite.All' }
    'wrong-resource-role'   = { param($p) $p.capabilities[0].expectedApplicationRoles[0].resourceAppId = '00000003-0000-0ff1-ce00-000000000000' }
    'missing-role'          = { param($p) $p.capabilities[0].expectedApplicationRoles = @() }
    'reused-certificate'    = { param($p) $p.capabilities[1].certificateReference = $p.capabilities[0].certificateReference }
    'reused-public-key'     = { param($p) $p.capabilities[1].publicCertificateSha256 = $p.capabilities[0].publicCertificateSha256 }
    'foreign-redirect'      = { param($p) $p.redirectUris = @('https://other.synthetic.test/signin-oidc') }
    'http-base-uri'         = { param($p) $p.publicBaseUri = 'http://auditsphere.synthetic.test' }
  }
  foreach ($case in $cases.GetEnumerator()) {
    $outcome = Invoke-Prerequisites (Write-Variant $case.Value) @('-DryRun')
    Assert-That "rejects-$($case.Key)" ($outcome.Exit -eq 4 -and $outcome.Result.status -eq 'INVALID_OR_UNSAFE' -and $outcome.Result.networkCalls -eq 0) "exit=$($outcome.Exit) code=$($outcome.Result.errorCode)"
  }

  # Offline transport refuses any provider call.
  Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
  Set-M365OfflineMode $true
  $refused = $false
  try { Invoke-M365ProviderRequest { 'should not run' } | Out-Null } catch { $refused = $_.Exception.Message -eq 'offline-network-call-refused' }
  Assert-That 'offline-transport-refuses' ($refused -and (Get-M365NetworkCallCount) -eq 0)

  # Static guard: no script reaches the network or installs modules outside the guarded transport.
  $direct = Get-ChildItem $root -Filter '*.ps*1' | Where-Object Name -ne 'Test-Automation.ps1' | Select-String -Pattern '\b(Invoke-WebRequest|Invoke-RestMethod|Install-Module|Install-PSResource|New-SelfSignedCertificate)\b'
  Assert-That 'no-direct-network-or-install' ($null -eq $direct) ($direct | Out-String)

  # The envelope verifier accepts the PLANNED result when Python is available.
  $python = Get-Command python3 -ErrorAction SilentlyContinue
  if ($python) {
    $plannedPath = Join-Path $work 'planned.json'
    $planned.Result | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $plannedPath -Encoding utf8NoBOM
    & $python.Source (Join-Path $root 'verify-result.py') $plannedPath --expect PLANNED | Out-Null
    Assert-That 'verify-result-accepts-planned' ($LASTEXITCODE -eq 0)
  }
}
finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }

$summary = [ordered]@{ unit = $Unit; offline = $true; passed = $passed; failed = $failures.Count; failures = @($failures) }
$summary | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
if ($passed -lt 10) { Write-Error 'Too few assertions ran; the run would be vacuous.'; exit 1 }
exit 0
