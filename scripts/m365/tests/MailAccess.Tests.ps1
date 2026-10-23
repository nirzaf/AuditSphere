#Requires -Version 7.4
# WU-022 plan/readiness contract only. Synthetic files and result envelopes; no tenant/provider access.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }

$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'MailAccess.psm1') -Force
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

function Assert-ThrowsCode([string] $Name, [string] $ExpectedCode, [scriptblock] $Action) {
  $caught = $false
  try { & $Action | Out-Null }
  catch {
    $caught = $true
    if ($_.Exception.Message -cne $ExpectedCode) {
      throw "ASSERT_FAILED:$Name expected $ExpectedCode, got $($_.Exception.Message)"
    }
  }
  if (-not $caught) { throw "ASSERT_FAILED:$Name did not throw $ExpectedCode" }
  $script:passed++
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

function Invoke-MailAccess([string] $Plan, [string] $Mode = 'Plan', [switch] $DryRun, [switch] $WhatIf,
    [string] $ExpectedPlanSha256, [string] $EntryPoint = $scriptPath) {
  $output = Join-Path $work ([guid]::NewGuid().ToString('N') + '.result.json')
  $childArgs = @('-NoProfile', '-NonInteractive', '-File', $EntryPoint, '-PlanPath', $Plan,
    '-Mode', $Mode, '-OutputPath', $output)
  if ($DryRun) { $childArgs += '-DryRun' }
  if ($WhatIf) { $childArgs += '-WhatIf' }
  if ($ExpectedPlanSha256) { $childArgs += @('-ExpectedPlanSha256', $ExpectedPlanSha256) }
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

  # Source guards are tested in an isolated Git fixture, independent of this checkout's dirtiness.
  $fixtureRoot = Join-Path $work 'checkout'
  $fixtureScripts = Join-Path $fixtureRoot 'scripts/m365'
  [System.IO.Directory]::CreateDirectory($fixtureScripts) | Out-Null
  foreach ($name in @('Configure-MailAccess.ps1','AuditSphere.M365.Common.psm1','MailAccess.psm1','m365-plan.schema.json')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $fixtureScripts $name)
  }
  [System.IO.File]::WriteAllText((Join-Path $fixtureRoot '.gitignore'), ".privatecopy/`n")
  & git -c init.templateDir= -C $fixtureRoot init -q
  if ($LASTEXITCODE -ne 0) { throw 'fixture-git-init-failed' }
  & git -C $fixtureRoot add .
  & git -c user.name=AuditSphereSynthetic -c user.email=synthetic@example.invalid `
    -c commit.gpgsign=false -c core.hooksPath= -C $fixtureRoot commit -qm 'Synthetic source guard fixture'
  if ($LASTEXITCODE -ne 0) { throw 'fixture-git-commit-failed' }
  Assert-That 'source-root-is-the-exact-checkout' (Test-M365ExactCheckoutRoot $fixtureRoot)
  Assert-That 'source-root-refuses-a-git-child-directory' (-not (Test-M365ExactCheckoutRoot $fixtureScripts))

  $readOnlyPlanPath = New-MailPlan 'LEGACY_APPLICATION_ACCESS_POLICY'
  $readOnlyPlanValue = Get-Content -Raw $readOnlyPlanPath | ConvertFrom-Json -AsHashtable -Depth 32
  $readOnlyPlanValue.repositoryCommit = (& git -C $fixtureRoot rev-parse HEAD).Trim()
  $readOnlyPlanValue | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $readOnlyPlanPath -Encoding utf8NoBOM
  $readOnlyPlanSha = Get-M365PlanSha256 $readOnlyPlanValue
  $fixtureEntry = Join-Path $fixtureScripts 'Configure-MailAccess.ps1'
  $originalEntry = [System.IO.File]::ReadAllBytes($fixtureEntry)
  Add-Content -LiteralPath $fixtureEntry -Value '# Deliberately dirty synthetic source.'
  $readOnly = Invoke-MailAccess $readOnlyPlanPath -Mode ReadOnly -ExpectedPlanSha256 $readOnlyPlanSha -EntryPoint $fixtureEntry
  Assert-That 'readonly-requires-clean-exact-owned-source-before-session-read' ($readOnly.ExitCode -eq 4 -and
    $readOnly.Result.status -eq 'INVALID_OR_UNSAFE' -and
    $readOnly.Result.errorCode -eq 'mail-source-tree-dirty' -and
    $readOnly.Result.checks[-1].state -eq 'INVALID_OR_UNSAFE' -and
    $readOnly.Result.mutationsAttempted -eq 0 -and $readOnly.Result.mutationsConfirmed -eq 0) `
    "exit=$($readOnly.ExitCode) status=$($readOnly.Result.status) error=$($readOnly.Result.errorCode)"
  Assert-ZeroEffects 'readonly-source-guard' $readOnly
  [System.IO.File]::WriteAllBytes($fixtureEntry, $originalEntry)

  # Even when the parent Git HEAD matches and ignores the copy, a modified copied entrypoint is not published source.
  $copyScripts = Join-Path $fixtureRoot '.privatecopy/scripts/m365'
  [System.IO.Directory]::CreateDirectory($copyScripts) | Out-Null
  Get-ChildItem -LiteralPath $fixtureScripts -File | Copy-Item -Destination $copyScripts
  $copyEntry = Join-Path $copyScripts 'Configure-MailAccess.ps1'
  Add-Content -LiteralPath $copyEntry -Value '# Ignored source copy must never borrow parent HEAD.'
  $copiedRead = Invoke-MailAccess $readOnlyPlanPath -Mode ReadOnly -ExpectedPlanSha256 $readOnlyPlanSha -EntryPoint $copyEntry
  Assert-That 'ignored-script-copy-refused-before-provider-read' ($copiedRead.ExitCode -eq 4 -and
    $copiedRead.Result.status -eq 'INVALID_OR_UNSAFE' -and
    $copiedRead.Result.errorCode -eq 'mail-readonly-source-or-plan-invalid')
  Assert-ZeroEffects 'ignored-source-copy' $copiedRead

  $apply = Invoke-MailAccess (New-MailPlan 'EXCHANGE_RBAC_RESOURCE_SCOPE') -Mode Apply
  Assert-That 'apply-stays-blocked-until-reviewed-migration' ($apply.ExitCode -eq 7 -and
    $apply.Result.status -eq 'PREREQUISITE_MISSING' -and
    $apply.Result.errorCode -eq 'mail-apply-migration-not-implemented' -and
    $apply.Result.checks[-1].state -eq 'PREREQUISITE_MISSING' -and
    $apply.Result.checks[-1].observed -match 'Engineering prerequisite' -and
    $apply.Result.blockedPrerequisites -contains 'engineering:reviewed-mail-migration-not-implemented') `
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

  $metadata = @{}
  $requiredParameters = @{
    'Get-ConnectionInformation' = @()
    'Get-EXOMailbox' = @('ExternalDirectoryObjectId','Properties')
    'Get-ServicePrincipal' = @('Identity')
    'Get-ManagementScope' = @('Identity')
    'Get-ManagementRoleAssignment' = @('RoleAssignee')
    'Get-DistributionGroup' = @('Identity','RecipientTypeDetails')
    'Get-DistributionGroupMember' = @('Identity','ResultSize')
    'Get-EXORecipient' = @('Filter','ResultSize','Properties')
    'Get-ApplicationAccessPolicy' = @()
    'Test-ApplicationAccessPolicy' = @('Identity','AppId')
    'Test-ServicePrincipalAuthorization' = @('Identity','Resource')
  }
  foreach ($name in $requiredParameters.Keys) {
    $parameters = @{}
    foreach ($parameter in $requiredParameters[$name]) { $parameters[$parameter] = $true }
    $metadata[$name] = @{ Parameters = $parameters }
  }
  Assert-That 'documented-exchange-cmdlet-metadata-is-accepted' ([bool] (Assert-M365MailCmdletMetadata $metadata))
  $metadata['Get-EXORecipient'].Parameters.Remove('Properties')
  Assert-ThrowsCode 'exo-recipient-requires-documented-property-parameter' 'INCOMPLETE_EVIDENCE' {
    Assert-M365MailCmdletMetadata $metadata | Out-Null
  }

  $script:mailTenant = '00000000-0000-4000-8000-000000000101'
  $script:mailClient = '00000000-0000-4000-8000-000000000102'
  $script:mailPrincipal = '00000000-0000-4000-8000-000000000103'
  $script:mailGraphServicePrincipal = '00000000-0000-4000-8000-000000000104'
  $script:mailRole = '00000000-0000-4000-8000-000000000105'
  $script:mailSender = '00000000-0000-4000-8000-000000000106'
  $script:mailControl = '00000000-0000-4000-8000-000000000107'
  $script:mailGroup = '00000000-0000-4000-8000-000000000108'

  function New-SyntheticMailEvidence([ValidateSet('LEGACY_APPLICATION_ACCESS_POLICY', 'EXCHANGE_RBAC_RESOURCE_SCOPE')] [string] $Model) {
    $graphAppId = '00000003-0000-0000-c000-000000000000'
    $expectedRoles = @()
    if ($Model -eq 'LEGACY_APPLICATION_ACCESS_POLICY') {
      $expectedRoles = @(@{ resourceAppId = $graphAppId; value = 'Mail.Send' })
    }
    $capability = @{ kind = 'MAIL'; clientId = $script:mailClient; expectedApplicationRoles = $expectedRoles }
    $mail = @{
      permissionModel = $Model
      senderObjectIds = @($script:mailSender)
      controlMailboxObjectId = $script:mailControl
    }
    if ($Model -eq 'LEGACY_APPLICATION_ACCESS_POLICY') { $mail.policyScopeGroupId = $script:mailGroup }
    else { $mail.scopeName = 'AuditSphere-Sender-Only' }
    $plan = @{ tenantId = $script:mailTenant; capabilities = @($capability); mail = $mail }
    $enterprise = @{ id = $script:mailPrincipal; appId = $script:mailClient; appOwnerOrganizationId = $script:mailTenant }
    $graphRole = @{ id = $script:mailRole; value = 'Mail.Send'; isEnabled = $true; allowedMemberTypes = @('Application') }
    $graph = @{ id = $script:mailGraphServicePrincipal; appId = $graphAppId; appRoles = @($graphRole) }
    $appAssignments = @()
    if ($Model -eq 'LEGACY_APPLICATION_ACCESS_POLICY') {
      $appAssignments = @(@{ principalId = $script:mailPrincipal; resourceId = $script:mailGraphServicePrincipal; appRoleId = $script:mailRole })
    }
    $exchange = @{ exchangeServicePrincipal = $null; roleAssignments = @() }
    if ($Model -eq 'LEGACY_APPLICATION_ACCESS_POLICY') {
      $policyProbes = @{}
      $policyProbes[$script:mailSender] = @{ AppId = $script:mailClient; MailboxId = $script:mailSender; AccessCheckResult = 'Granted' }
      $policyProbes[$script:mailControl] = @{ AppId = $script:mailClient; MailboxId = $script:mailControl; AccessCheckResult = 'Denied' }
      $exchange.policyGroup = @{ ExternalDirectoryObjectId = $script:mailGroup; Name = 'AuditSphere-Mail-Senders' }
      $exchange.policyGroupMemberObjectIds = @($script:mailSender)
      $exchange.applicationAccessPolicies = @(@{ AppId = $script:mailClient; ScopeName = 'AuditSphere-Mail-Senders'; AccessRight = 'RestrictAccess' })
      $exchange.applicationAccessPolicyProbes = $policyProbes
    } else {
      $exchange.exchangeServicePrincipal = @{ ObjectId = $script:mailPrincipal; AppId = $script:mailClient }
      $exchange.roleAssignments = @(@{
        Role = 'Application Mail.Send'; RoleAssigneeName = $script:mailPrincipal
        RoleAssigneeType = 'ServicePrincipal'; AssignmentMethod = 'Direct'
      })
      $exchange.managementScope = @{
        Name = 'AuditSphere-Sender-Only'; ScopeRestrictionType = 'RecipientScope'; Exclusive = $false
        RecipientFilter = '(CustomAttribute15 -eq "AuditSphere")'
      }
      $exchange.scopeMemberObjectIds = @($script:mailSender)
      $authorizationProbes = @{}
      $authorizationProbes[$script:mailSender] = @(@{
        RoleName = 'Application Mail.Send'; GrantedPermissions = 'Mail.Send'; AllowedResourceScope = 'AuditSphere-Sender-Only'
        ScopeType = 'CustomRecipientScope'; InScope = $true
      })
      $authorizationProbes[$script:mailControl] = @(@{
        RoleName = 'Application Mail.Send'; GrantedPermissions = 'Mail.Send'; AllowedResourceScope = 'AuditSphere-Sender-Only'
        ScopeType = 'CustomRecipientScope'; InScope = $false
      })
      $exchange.servicePrincipalAuthorizationProbes = $authorizationProbes
    }
    return @{
      Plan = $plan
      Evidence = @{
        enterpriseServicePrincipal = $enterprise
        graphServicePrincipal = $graph
        appRoleAssignments = $appAssignments
        exchange = $exchange
      }
    }
  }

  $legacyEvidence = New-SyntheticMailEvidence 'LEGACY_APPLICATION_ACCESS_POLICY'
  $legacySnapshot = Assert-M365MailReadinessEvidence $legacyEvidence.Plan $legacyEvidence.Evidence
  Assert-That 'legacy-current-state-is-exactly-authorized-and-control-denied' ($legacySnapshot.model -eq 'LEGACY_APPLICATION_ACCESS_POLICY')
  $wrongLegacyPlan = New-SyntheticMailEvidence 'LEGACY_APPLICATION_ACCESS_POLICY'
  $wrongLegacyPlan.Plan.capabilities[0].expectedApplicationRoles[0].value = 'Mail.Read'
  Assert-ThrowsCode 'pure-verifier-rejects-plan-role-outside-mail-contract' 'INVALID_OR_UNSAFE' {
    Assert-M365MailReadinessEvidence $wrongLegacyPlan.Plan $wrongLegacyPlan.Evidence | Out-Null
  }
  $legacyBypass = New-SyntheticMailEvidence 'LEGACY_APPLICATION_ACCESS_POLICY'
  $legacyBypass.Evidence.exchange.roleAssignments = @(@{ Role = 'Application Mail.Send' })
  Assert-ThrowsCode 'legacy-refuses-overlapping-exchange-rbac-grant' 'DRIFT' {
    Assert-M365MailReadinessEvidence $legacyBypass.Plan $legacyBypass.Evidence | Out-Null
  }
  $legacyControlGrant = New-SyntheticMailEvidence 'LEGACY_APPLICATION_ACCESS_POLICY'
  $legacyControlGrant.Evidence.exchange.applicationAccessPolicyProbes[$script:mailControl].AccessCheckResult = 'Granted'
  Assert-ThrowsCode 'legacy-control-mailbox-must-be-denied' 'DRIFT' {
    Assert-M365MailReadinessEvidence $legacyControlGrant.Plan $legacyControlGrant.Evidence | Out-Null
  }
  $legacyUnknownProbe = New-SyntheticMailEvidence 'LEGACY_APPLICATION_ACCESS_POLICY'
  $legacyUnknownProbe.Evidence.exchange.applicationAccessPolicyProbes[$script:mailSender].Remove('AccessCheckResult')
  Assert-ThrowsCode 'undocumented-policy-probe-shape-is-incomplete' 'INCOMPLETE_EVIDENCE' {
    Assert-M365MailReadinessEvidence $legacyUnknownProbe.Plan $legacyUnknownProbe.Evidence | Out-Null
  }

  $rbacEvidence = New-SyntheticMailEvidence 'EXCHANGE_RBAC_RESOURCE_SCOPE'
  $rbacSnapshot = Assert-M365MailReadinessEvidence $rbacEvidence.Plan $rbacEvidence.Evidence
  Assert-That 'rbac-current-state-proves-scope-and-control-denial' ($rbacSnapshot.model -eq 'EXCHANGE_RBAC_RESOURCE_SCOPE')
  $rbacOverlap = New-SyntheticMailEvidence 'EXCHANGE_RBAC_RESOURCE_SCOPE'
  $rbacOverlap.Evidence.appRoleAssignments = @(@{ principalId = $script:mailPrincipal; resourceId = $script:mailGraphServicePrincipal; appRoleId = $script:mailRole })
  Assert-ThrowsCode 'rbac-refuses-unscoped-entra-mail-send' 'DRIFT' {
    Assert-M365MailReadinessEvidence $rbacOverlap.Plan $rbacOverlap.Evidence | Out-Null
  }
  $rbacMembershipDrift = New-SyntheticMailEvidence 'EXCHANGE_RBAC_RESOURCE_SCOPE'
  $rbacMembershipDrift.Evidence.exchange.scopeMemberObjectIds += '00000000-0000-4000-8000-000000000109'
  Assert-ThrowsCode 'rbac-scope-membership-must-equal-planned-senders' 'DRIFT' {
    Assert-M365MailReadinessEvidence $rbacMembershipDrift.Plan $rbacMembershipDrift.Evidence | Out-Null
  }
  $rbacControlGrant = New-SyntheticMailEvidence 'EXCHANGE_RBAC_RESOURCE_SCOPE'
  $rbacControlGrant.Evidence.exchange.servicePrincipalAuthorizationProbes[$script:mailControl][0].InScope = $true
  Assert-ThrowsCode 'rbac-control-mailbox-must-be-out-of-scope' 'DRIFT' {
    Assert-M365MailReadinessEvidence $rbacControlGrant.Plan $rbacControlGrant.Evidence | Out-Null
  }

  $sessionPlan = @{ tenantId = $script:mailTenant; operator = @{ clientId = $script:mailClient } }
  $graphContext = @{
    TenantId = $script:mailTenant; ClientId = $script:mailClient; Environment = 'Global'; AuthType = 'AppOnly'
    CertificateThumbprint = 'synthetic-cert-thumbprint'; Scopes = @()
  }
  $exchangeConnection = @(@{
    ConnectionId = '00000000-0000-4000-8000-000000000110'; State = 'Connected'; TenantID = $script:mailTenant
    AppId = $script:mailClient; TokenStatus = 'Active'; IsEopSession = $false; CertificateAuthentication = $true
  })
  $initialBinding = Assert-M365MailSessionBinding $sessionPlan $graphContext $exchangeConnection
  $sameBinding = Assert-M365MailSessionBinding $sessionPlan $graphContext $exchangeConnection
  Assert-That 'same-provider-sessions-have-stable-nonsecret-binding' ($initialBinding -ceq $sameBinding -and $initialBinding -notmatch $script:mailClient)
  Assert-ThrowsCode 'missing-exchange-session-is-bootstrap-blocked' 'BLOCKED_BOOTSTRAP' {
    Assert-M365MailSessionBinding $sessionPlan $graphContext @() | Out-Null
  }
  $wrongGraphContext = $graphContext.Clone()
  $wrongGraphContext.TenantId = '00000000-0000-4000-8000-000000000111'
  Assert-ThrowsCode 'foreign-graph-tenant-is-blocked' 'BLOCKED_BOOTSTRAP' {
    Assert-M365MailSessionBinding $sessionPlan $wrongGraphContext $exchangeConnection | Out-Null
  }
  $delegatedGraphContext = $graphContext.Clone()
  $delegatedGraphContext.AuthType = 'Delegated'
  Assert-ThrowsCode 'graph-session-must-be-custom-app-only' 'BLOCKED_BOOTSTRAP' {
    Assert-M365MailSessionBinding $sessionPlan $delegatedGraphContext $exchangeConnection | Out-Null
  }
  $nonCertificateConnection = @($exchangeConnection[0].Clone())
  $nonCertificateConnection[0].CertificateAuthentication = $false
  Assert-ThrowsCode 'exchange-session-must-be-certificate-app-only' 'BLOCKED_BOOTSTRAP' {
    Assert-M365MailSessionBinding $sessionPlan $graphContext $nonCertificateConnection | Out-Null
  }
  $changedConnection = @($exchangeConnection[0].Clone())
  $changedConnection[0].ConnectionId = '00000000-0000-4000-8000-000000000112'
  $changedBinding = Assert-M365MailSessionBinding $sessionPlan $graphContext $changedConnection
  Assert-That 'exchange-session-change-changes-final-binding' ($changedBinding -cne $initialBinding)
  $changedGraphContext = $graphContext.Clone()
  $changedGraphContext.CertificateThumbprint = 'other-synthetic-cert-thumbprint'
  $changedGraphBinding = Assert-M365MailSessionBinding $sessionPlan $changedGraphContext $exchangeConnection
  Assert-That 'graph-credential-change-changes-final-binding' ($changedGraphBinding -cne $initialBinding)

  $snapshotForGate = [ordered]@{
    model = $legacySnapshot.model; clientId = $legacySnapshot.clientId
    senderObjectIds = @($legacySnapshot.senderObjectIds)
    controlMailboxObjectId = $legacySnapshot.controlMailboxObjectId
    evidenceReference = 'm365-mail-readonly:synthetic:sha256:' + ('a' * 64)
  }
  $gateResult = New-M365Result 'ReadOnly'
  $gateStatus = Add-M365MailRuntimeGate $gateResult $snapshotForGate
  Assert-That 'legacy-provider-readiness-defers-to-existing-api-capability-evidence' ($gateStatus -eq 'INCOMPLETE_EVIDENCE' -and
    $gateResult.checks[0].state -eq 'PASS' -and $gateResult.checks[1].state -eq 'INCOMPLETE_EVIDENCE' -and
    $gateResult.checks[1].id -eq 'mail-capability-evidence' -and
    $gateResult.errorCode -eq 'mail-legacy-capability-evidence-not-verified' -and
    $gateResult.blockedPrerequisites[0] -eq 'verification:legacy-mail-capability-evidence-not-verified' -and
    $gateResult.checks[1].observed -notmatch 'runtime is not implemented' -and
    $gateResult.mutationsAttempted -eq 0 -and $gateResult.mutationsConfirmed -eq 0 -and $gateResult.changes.Count -eq 0)
  $rbacSnapshotForGate = [ordered]@{
    model = 'EXCHANGE_RBAC_RESOURCE_SCOPE'; clientId = $snapshotForGate.clientId
    senderObjectIds = @($snapshotForGate.senderObjectIds)
    controlMailboxObjectId = $snapshotForGate.controlMailboxObjectId
    evidenceReference = $snapshotForGate.evidenceReference
  }
  $rbacGateResult = New-M365Result 'ReadOnly'
  $rbacGateStatus = Add-M365MailRuntimeGate $rbacGateResult $rbacSnapshotForGate
  Assert-That 'rbac-provider-readiness-names-missing-engineering-runtime' ($rbacGateStatus -eq 'INCOMPLETE_EVIDENCE' -and
    $rbacGateResult.checks[1].state -eq 'INCOMPLETE_EVIDENCE' -and
    $rbacGateResult.errorCode -eq 'mail-rbac-runtime-not-implemented' -and
    $rbacGateResult.blockedPrerequisites[0] -eq 'engineering:exchange-rbac-mail-runtime-not-implemented' -and
    $rbacGateResult.mutationsAttempted -eq 0 -and $rbacGateResult.mutationsConfirmed -eq 0 -and $rbacGateResult.changes.Count -eq 0)

  $moduleSource = Get-Content -Raw (Join-Path $root 'MailAccess.psm1')
  Assert-That 'every-exchange-remote-cmdlet-uses-counted-provider-wrapper' ($moduleSource -match 'return Invoke-M365ProviderRequest\s*\{' -and
    $moduleSource -match 'New-EntraGraphTransport \$provider' -and
    $moduleSource -match '\$finalSessionBinding -cne \$initialSessionBinding')

  Set-M365OfflineMode $false
  $networkStart = Get-M365NetworkCallCount
  $graphPath = '/v1.0/servicePrincipals?%24filter=appId%20eq%20%27synthetic%27'
  $nextLink = 'https://graph.microsoft.com/v1.0/servicePrincipals?%24filter=appId%20eq%20%27synthetic%27&%24skiptoken=second'
  $pageAudit = @{ Calls = 0; FailAt = 0; Pages = @(
    @{ statusCode = 200; body = @{ value = @(@{ id = 'one' }); '@odata.nextLink' = $nextLink } },
    @{ statusCode = 200; body = @{ value = @(@{ id = 'two' }) } }
  ) }
  $pageDispatch = {
    $pageAudit.Calls++
    if ($pageAudit.FailAt -eq $pageAudit.Calls) { throw 'synthetic-graph-dispatch-failed' }
    return $pageAudit.Pages[$pageAudit.Calls - 1]
  }.GetNewClosure()
  $pageTransport = { param($method, $path, $body) Invoke-M365ProviderRequest $pageDispatch }.GetNewClosure()
  $mailModule = Get-Module MailAccess
  $pageRows = & $mailModule { param($transport, $path) Get-M365MailGraphCollection $transport $path } $pageTransport $graphPath
  Assert-That 'graph-continuation-fetch-counts-each-page' ($pageRows.Count -eq 2 -and
    (Get-M365NetworkCallCount) - $networkStart -eq 2)

  $unsafePageAudit = @{ Calls = 0; Pages = @(
    @{ statusCode = 200; body = @{ value = @(@{ id = 'one' }); '@odata.nextLink' = 'https://attacker.example/v1.0/servicePrincipals?%24skiptoken=two' } }
  ) }
  $unsafePageDispatch = {
    $unsafePageAudit.Calls++
    return $unsafePageAudit.Pages[$unsafePageAudit.Calls - 1]
  }.GetNewClosure()
  $unsafePageTransport = { param($method, $path, $body) Invoke-M365ProviderRequest $unsafePageDispatch }.GetNewClosure()
  Assert-ThrowsCode 'foreign-graph-continuation-is-rejected' 'INCOMPLETE_EVIDENCE' {
    & $mailModule { param($transport, $path) Get-M365MailGraphCollection $transport $path } $unsafePageTransport $graphPath | Out-Null
  }
  Assert-That 'foreign-continuation-is-counted-without-second-dispatch' ((Get-M365NetworkCallCount) - $networkStart -eq 3)

  $changedQueryAudit = @{ Calls = 0; Pages = @(
    @{ statusCode = 200; body = @{ value = @(@{ id = 'one' }); '@odata.nextLink' = 'https://graph.microsoft.com/v1.0/servicePrincipals?%24filter=displayName%20ne%20%27synthetic%27&%24skiptoken=two' } }
  ) }
  $changedQueryDispatch = {
    $changedQueryAudit.Calls++
    return $changedQueryAudit.Pages[$changedQueryAudit.Calls - 1]
  }.GetNewClosure()
  $changedQueryTransport = { param($method, $path, $body) Invoke-M365ProviderRequest $changedQueryDispatch }.GetNewClosure()
  Assert-ThrowsCode 'graph-continuation-cannot-change-filter' 'INCOMPLETE_EVIDENCE' {
    & $mailModule { param($transport, $path) Get-M365MailGraphCollection $transport $path } $changedQueryTransport $graphPath | Out-Null
  }
  Assert-That 'changed-query-is-counted-without-followup-dispatch' ((Get-M365NetworkCallCount) - $networkStart -eq 4)

  $failureAudit = @{ Calls = 0; FailAt = 2; Pages = @(
    @{ statusCode = 200; body = @{ value = @(@{ id = 'one' }); '@odata.nextLink' = $nextLink } },
    @{ statusCode = 200; body = @{ value = @(@{ id = 'two' }) } }
  ) }
  $failureDispatch = {
    $failureAudit.Calls++
    if ($failureAudit.FailAt -eq $failureAudit.Calls) { throw 'synthetic-graph-dispatch-failed' }
    return $failureAudit.Pages[$failureAudit.Calls - 1]
  }.GetNewClosure()
  $failureTransport = { param($method, $path, $body) Invoke-M365ProviderRequest $failureDispatch }.GetNewClosure()
  Assert-ThrowsCode 'partial-graph-read-fails-closed' 'synthetic-graph-dispatch-failed' {
    & $mailModule { param($transport, $path) Get-M365MailGraphCollection $transport $path } $failureTransport $graphPath | Out-Null
  }
  Assert-That 'failed-graph-page-is-counted-before-dispatch' ((Get-M365NetworkCallCount) - $networkStart -eq 6)

  function Invoke-SyntheticExchangeRemote {
    [CmdletBinding()]
    param()
    throw 'synthetic-exchange-dispatch-failed'
  }
  $exchangeCommandMap = @{ 'Get-EXOMailbox' = Get-Command Invoke-SyntheticExchangeRemote }
  Assert-ThrowsCode 'remote-exchange-read-error-is-stable' 'PROVIDER_FAILURE' {
    & $mailModule { param($op, $argsMap, $commandMap) Invoke-M365MailExchangeRead $op $argsMap $commandMap } `
      'Get-EXOMailbox' @{} $exchangeCommandMap | Out-Null
  }
  Assert-That 'failed-exchange-cmdlet-is-counted-before-dispatch' ((Get-M365NetworkCallCount) - $networkStart -eq 7)
  $networkResultPath = Join-Path $work 'partial-provider-failure.result.json'
  $networkResult = New-M365Result 'ReadOnly'
  $networkResult.networkCalls = Get-M365NetworkCallCount
  $null = Complete-M365Result $networkResult 'PROVIDER_FAILURE' $networkResultPath
  $networkResultFromDisk = Get-Content -Raw $networkResultPath | ConvertFrom-Json -AsHashtable
  Assert-That 'result-envelope-keeps-network-count-after-partial-failure' ($networkResultFromDisk.networkCalls -eq 7 -and
    $networkResultFromDisk.mutationsAttempted -eq 0 -and $networkResultFromDisk.mutationsConfirmed -eq 0)
  Set-M365OfflineMode $true

  $source = Get-Content -Raw $scriptPath
  $readOnlySource = Get-Content -Raw (Join-Path $root 'MailAccess.psm1')
  $liveCommands = '(?im)^\s*(Connect-MgGraph|Connect-ExchangeOnline|New-ServicePrincipal|New-ManagementScope|Set-ManagementScope|Remove-ManagementScope|New-ManagementRoleAssignment|Remove-ManagementRoleAssignment|Remove-ApplicationAccessPolicy|New-ApplicationAccessPolicy|Send-MailMessage|Send-MgUserMail|Invoke-WebRequest|Invoke-RestMethod|Install-Module|Install-PSResource|New-SelfSignedCertificate)\b'
  Assert-That 'readonly-adapter-and-cli-have-no-auth-install-write-or-send-command' (($source + $readOnlySource) -notmatch $liveCommands)
} finally {
  Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$summary = [ordered]@{ unit = 'WU-022'; offline = $true; passed = $passed; failed = $failures.Count; failures = @($failures) }
$summary | ConvertTo-Json -Depth 4
if ($failures.Count -gt 0) { exit 1 }
if ($passed -lt 16) { Write-Error 'Too few assertions ran; the run would be vacuous.'; exit 1 }
exit 0
