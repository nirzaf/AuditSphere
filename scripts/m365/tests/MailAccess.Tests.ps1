#Requires -Version 7.4
# WU-022 plan/readiness contract only. Synthetic files and result envelopes; no tenant/provider access.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }

$root = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json'
$scriptPath = Join-Path $root 'Configure-MailAccess.ps1'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-mail-access-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($work) | Out-Null
$passed = 0
$failures = [System.Collections.Generic.List[string]]::new()
$engine = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })

function Assert-That([string] $Name, [bool] $Condition, [string] $Detail = '') {
  if ($Condition) { $script:passed++ } else { $script:failures.Add("$Name $Detail") }
}

function Write-Plan([string] $Name, [scriptblock] $Change = { param($plan) }, [object[]] $ChangeArguments = @()) {
  $plan = Get-Content -Raw $fixture | ConvertFrom-Json -AsHashtable -Depth 32
  & $Change $plan @ChangeArguments
  $path = Join-Path $work "$Name.plan.json"
  $plan | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $path -Encoding utf8NoBOM
  return $path
}

function New-MailPlan([ValidateSet('LEGACY_APPLICATION_ACCESS_POLICY', 'EXCHANGE_RBAC_RESOURCE_SCOPE')] [string] $Model) {
  return Write-Plan -Name $Model -Change {
    param($plan, $permissionModel)
    $plan.capabilities = @($plan.capabilities[0])
    $plan.capabilities[0].kind = 'MAIL'
    if ($permissionModel -eq 'LEGACY_APPLICATION_ACCESS_POLICY') {
      $plan.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    } else {
      $plan.capabilities[0].expectedApplicationRoles = @()
    }
    $plan.mail = @{
      permissionModel = $permissionModel
      senderObjectIds = @('00000000-0000-4000-8000-000000000050')
      controlMailboxObjectId = '00000000-0000-4000-8000-000000000051'
    }
    if ($permissionModel -eq 'LEGACY_APPLICATION_ACCESS_POLICY') {
      $plan.mail.policyScopeGroupId = '00000000-0000-4000-8000-000000000052'
    } else {
      $plan.mail.scopeName = 'AuditSphere-Sender-Only'
    }
  } -ChangeArguments @($Model)
}

function Invoke-MailAccess([string] $Plan, [string] $Mode = 'Plan', [switch] $DryRun, [switch] $WhatIf) {
  $output = Join-Path $work ([guid]::NewGuid().ToString('N') + '.result.json')
  $childArgs = @('-NoProfile', '-NonInteractive', '-File', $scriptPath, '-PlanPath', $Plan,
    '-Mode', $Mode, '-OutputPath', $output)
  if ($DryRun) { $childArgs += '-DryRun' }
  if ($WhatIf) { $childArgs += '-WhatIf' }
  & $engine @childArgs | Out-Null
  $exitCode = $LASTEXITCODE
  return @{ ExitCode = $exitCode; Result = (Get-Content -Raw $output | ConvertFrom-Json -AsHashtable) }
}

function Assert-ZeroEffects([string] $Name, $Outcome) {
  Assert-That "$Name-zero-effects" ($Outcome.Result.networkCalls -eq 0 -and
    $Outcome.Result.mutationsAttempted -eq 0 -and $Outcome.Result.mutationsConfirmed -eq 0)
}

try {
  $withoutMail = Invoke-MailAccess $fixture
  Assert-That 'generic-plan-remains-planned' ($withoutMail.ExitCode -eq 0 -and $withoutMail.Result.status -eq 'PLANNED')
  Assert-That 'generic-plan-describes-disabled-mail' ($withoutMail.Result.checks[0].state -eq 'SKIPPED' -and
    $withoutMail.Result.checks[0].errorCode -eq 'mail-capability-not-configured' -and
    $withoutMail.Result.blockedPrerequisites -contains 'mail-capability-not-configured')
  Assert-ZeroEffects 'generic-plan' $withoutMail

  $legacyPlan = Invoke-MailAccess (New-MailPlan 'LEGACY_APPLICATION_ACCESS_POLICY')
  Assert-That 'legacy-model-plans-with-graph-role-and-policy-scope' ($legacyPlan.ExitCode -eq 0 -and
    $legacyPlan.Result.status -eq 'PLANNED' -and $legacyPlan.Result.checks[0].state -eq 'PASS') `
    "exit=$($legacyPlan.ExitCode) status=$($legacyPlan.Result.status) error=$($legacyPlan.Result.errorCode)"
  Assert-ZeroEffects 'legacy-plan' $legacyPlan

  $rbacPlan = Invoke-MailAccess (New-MailPlan 'EXCHANGE_RBAC_RESOURCE_SCOPE') -DryRun
  Assert-That 'rbac-model-plans-without-unscoped-graph-role' ($rbacPlan.ExitCode -eq 0 -and
    $rbacPlan.Result.status -eq 'PLANNED' -and $rbacPlan.Result.checks[0].state -eq 'PASS') `
    "exit=$($rbacPlan.ExitCode) status=$($rbacPlan.Result.status) error=$($rbacPlan.Result.errorCode)"
  Assert-ZeroEffects 'rbac-dryrun' $rbacPlan

  $whatIf = Invoke-MailAccess (New-MailPlan 'EXCHANGE_RBAC_RESOURCE_SCOPE') -Mode Apply -WhatIf
  Assert-That 'whatif-apply-is-only-plan' ($whatIf.ExitCode -eq 0 -and $whatIf.Result.mode -eq 'Plan' -and
    $whatIf.Result.status -eq 'PLANNED') "exit=$($whatIf.ExitCode) mode=$($whatIf.Result.mode) status=$($whatIf.Result.status) error=$($whatIf.Result.errorCode)"
  Assert-ZeroEffects 'whatif-apply' $whatIf

  $readOnly = Invoke-MailAccess (New-MailPlan 'LEGACY_APPLICATION_ACCESS_POLICY') -Mode ReadOnly
  Assert-That 'readonly-reports-missing-engineering-adapter' ($readOnly.ExitCode -eq 7 -and
    $readOnly.Result.status -eq 'PREREQUISITE_MISSING' -and
    $readOnly.Result.errorCode -eq 'adapter-not-implemented' -and
    $readOnly.Result.checks[-1].state -eq 'PREREQUISITE_MISSING' -and
    $readOnly.Result.checks[-1].expected -match 'implemented Exchange/Entra read-back adapter' -and
    $readOnly.Result.checks[-1].observed -match 'Engineering prerequisite' -and
    $readOnly.Result.blockedPrerequisites -contains 'engineering:exchange-entra-readback-adapter-not-implemented') `
    "exit=$($readOnly.ExitCode) status=$($readOnly.Result.status) error=$($readOnly.Result.errorCode)"
  Assert-ZeroEffects 'readonly-blocked' $readOnly

  $apply = Invoke-MailAccess (New-MailPlan 'EXCHANGE_RBAC_RESOURCE_SCOPE') -Mode Apply
  Assert-That 'apply-reports-missing-engineering-adapter' ($apply.ExitCode -eq 7 -and
    $apply.Result.status -eq 'PREREQUISITE_MISSING' -and
    $apply.Result.errorCode -eq 'adapter-not-implemented' -and
    $apply.Result.checks[-1].state -eq 'PREREQUISITE_MISSING' -and
    $apply.Result.checks[-1].observed -match 'Engineering prerequisite' -and
    $apply.Result.blockedPrerequisites -contains 'engineering:exchange-entra-readback-adapter-not-implemented') `
    "exit=$($apply.ExitCode) status=$($apply.Result.status) error=$($apply.Result.errorCode)"
  Assert-ZeroEffects 'apply-blocked' $apply

  $missingRbacScope = Invoke-MailAccess (Write-Plan 'missing-rbac-scope' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'; $plan.capabilities[0].expectedApplicationRoles = @()
    $plan.mail = @{ permissionModel = 'EXCHANGE_RBAC_RESOURCE_SCOPE'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051' }
  })
  Assert-That 'rbac-requires-scope-name' ($missingRbacScope.ExitCode -eq 4 -and $missingRbacScope.Result.status -eq 'INVALID_OR_UNSAFE')
  Assert-ZeroEffects 'missing-rbac-scope' $missingRbacScope

  $missingLegacyScope = Invoke-MailAccess (Write-Plan 'missing-legacy-scope' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'
    $plan.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051' }
  })
  Assert-That 'legacy-requires-policy-scope-group' ($missingLegacyScope.ExitCode -eq 4 -and $missingLegacyScope.Result.status -eq 'INVALID_OR_UNSAFE')

  $missingControl = Invoke-MailAccess (Write-Plan 'missing-control-mailbox' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'
    $plan.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); policyScopeGroupId = '00000000-0000-4000-8000-000000000052' }
  })
  Assert-That 'configured-mail-requires-control-mailbox' ($missingControl.ExitCode -eq 4 -and $missingControl.Result.status -eq 'INVALID_OR_UNSAFE')

  $senderControl = Invoke-MailAccess (Write-Plan 'control-is-sender' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'
    $plan.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000050'; policyScopeGroupId = '00000000-0000-4000-8000-000000000052' }
  })
  Assert-That 'control-mailbox-cannot-be-sender' ($senderControl.ExitCode -eq 4 -and
    $senderControl.Result.errorCode -eq 'mail-control-mailbox-is-sender')

  $rbacWithEntraRole = Invoke-MailAccess (Write-Plan 'rbac-with-unscoped-role' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'
    $plan.capabilities[0].expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $plan.mail = @{ permissionModel = 'EXCHANGE_RBAC_RESOURCE_SCOPE'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051'; scopeName = 'AuditSphere-Sender-Only' }
  })
  Assert-That 'rbac-rejects-overlapping-entra-mail-send' ($rbacWithEntraRole.ExitCode -eq 4 -and
    $rbacWithEntraRole.Result.errorCode -eq 'capability-permission-mismatch')

  $legacyWithoutRole = Invoke-MailAccess (Write-Plan 'legacy-without-entra-role' {
    param($plan)
    $plan.capabilities = @($plan.capabilities[0]); $plan.capabilities[0].kind = 'MAIL'; $plan.capabilities[0].expectedApplicationRoles = @()
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051'; policyScopeGroupId = '00000000-0000-4000-8000-000000000052' }
  })
  Assert-That 'legacy-requires-entra-mail-send' ($legacyWithoutRole.ExitCode -eq 4 -and
    $legacyWithoutRole.Result.errorCode -eq 'capability-permission-mismatch')

  $mailWithoutCapability = Invoke-MailAccess (Write-Plan 'mail-config-without-capability' {
    param($plan)
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051'; policyScopeGroupId = '00000000-0000-4000-8000-000000000052' }
  })
  Assert-That 'mail-model-requires-one-mail-capability' ($mailWithoutCapability.ExitCode -eq 4 -and
    $mailWithoutCapability.Result.status -eq 'INVALID_OR_UNSAFE')

  $duplicateMail = Invoke-MailAccess (Write-Plan 'duplicate-mail-identities' {
    param($plan)
    $mailCapability = $plan.capabilities[0]
    $mailCapability.kind = 'MAIL'
    $mailCapability.expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $second = $plan.capabilities[1]
    $second.kind = 'MAIL'
    $second.expectedApplicationRoles = @(@{ resourceAppId = '00000003-0000-0000-c000-000000000000'; value = 'Mail.Send' })
    $plan.mail = @{ permissionModel = 'LEGACY_APPLICATION_ACCESS_POLICY'; senderObjectIds = @('00000000-0000-4000-8000-000000000050'); controlMailboxObjectId = '00000000-0000-4000-8000-000000000051'; policyScopeGroupId = '00000000-0000-4000-8000-000000000052' }
  })
  Assert-That 'duplicate-mail-identity-refused' ($duplicateMail.ExitCode -eq 4 -and
    $duplicateMail.Result.errorCode -eq 'duplicate-capability-kind')

  $source = Get-Content -Raw $scriptPath
  $liveCommands = '(?im)^\s*(Connect-ExchangeOnline|New-ServicePrincipal|New-ManagementScope|New-ManagementRoleAssignment|Remove-ApplicationAccessPolicy|New-ApplicationAccessPolicy|Test-ServicePrincipalAuthorization|Invoke-WebRequest|Invoke-RestMethod|Install-Module|Install-PSResource|New-SelfSignedCertificate)\b'
  Assert-That 'no-provider-or-install-command-in-plan-readiness-script' ($source -notmatch $liveCommands)
} finally {
  Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$summary = [ordered]@{ unit = 'WU-022'; offline = $true; passed = $passed; failed = $failures.Count; failures = @($failures) }
$summary | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
if ($passed -lt 16) { Write-Error 'Too few assertions ran; the run would be vacuous.'; exit 1 }
exit 0
