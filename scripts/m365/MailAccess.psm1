#Requires -Version 7.4
# WU-022 read-only evidence adapter. It never authenticates or changes provider state.
Set-StrictMode -Version Latest

$script:MailMaximumSenders = 1000
$script:MailMaximumGraphRows = 1000
$script:MailMaximumGraphPages = 10
$script:MailMaximumSnapshotBytes = 1048576
$script:GraphApplicationId = '00000003-0000-0000-c000-000000000000'
$script:MailRoleName = 'Application Mail.Send'

function Get-MailField($Value, [string] $Name) {
  if ($null -eq $Value) { return $null }
  if ($Value -is [System.Collections.IDictionary]) {
    if ($Value.Contains($Name)) { return $Value[$Name] }
    return $null
  }
  $property = $Value.PSObject.Properties[$Name]
  if ($property) { return $property.Value }
  return $null
}

function Assert-MailFields($Value, [string[]] $Names) {
  if ($null -eq $Value) { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($name in $Names) {
    $property = if ($Value -is [System.Collections.IDictionary]) {
      $Value.Contains($name)
    } else {
      $null -ne $Value.PSObject.Properties[$name]
    }
    if (-not $property -or $null -eq (Get-MailField $Value $name)) { throw 'INCOMPLETE_EVIDENCE' }
  }
}

function ConvertTo-MailRows($Value) {
  if ($null -eq $Value) { return }
  if ($Value -is [string] -or $Value -is [System.Collections.IDictionary]) { return ,$Value }
  return @($Value)
}

function Test-MailExactPermission($Value, [string] $Expected) {
  $values = @(ConvertTo-MailRows $Value)
  return $values.Count -eq 1 -and [string] $values[0] -ceq $Expected
}

function ConvertTo-MailGuid([object] $Value) {
  $parsed = [guid]::Empty
  if (-not [guid]::TryParse([string] $Value, [ref] $parsed)) { throw 'INCOMPLETE_EVIDENCE' }
  return $parsed.ToString('D').ToLowerInvariant()
}

function Test-MailExactSet([object[]] $Actual, [object[]] $Expected) {
  $actualValues = @($Actual | ForEach-Object { ConvertTo-MailGuid $_ } | Sort-Object -Unique)
  $expectedValues = @($Expected | ForEach-Object { ConvertTo-MailGuid $_ } | Sort-Object -Unique)
  if ($actualValues.Count -ne @($Actual).Count -or $expectedValues.Count -ne @($Expected).Count) { return $false }
  if ($actualValues.Count -ne $expectedValues.Count) { return $false }
  for ($i = 0; $i -lt $actualValues.Count; $i++) {
    if ($actualValues[$i] -cne $expectedValues[$i]) { return $false }
  }
  return $true
}

function ConvertTo-MailSortedGuidStrings($Values) {
  [string[]] $normalized = @(ConvertTo-MailRows $Values | ForEach-Object { ConvertTo-MailGuid $_ })
  [Array]::Sort($normalized, [StringComparer]::Ordinal)
  return $normalized
}

function ConvertTo-MailSortedStrings($Values) {
  [string[]] $normalized = @(ConvertTo-MailRows $Values | ForEach-Object {
    if ($_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)) { throw 'INCOMPLETE_EVIDENCE' }
    [string] $_
  })
  if (@($normalized | Select-Object -Unique).Count -ne $normalized.Count) { throw 'INCOMPLETE_EVIDENCE' }
  [Array]::Sort($normalized, [StringComparer]::Ordinal)
  return $normalized
}

function Assert-MailProviderSnapshotBound($Snapshot) {
  $json = ConvertTo-Json -InputObject $Snapshot -Depth 16 -Compress
  if ([System.Text.Encoding]::UTF8.GetByteCount($json) -gt $script:MailMaximumSnapshotBytes) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  return $true
}

function Assert-MailSourceIdentity($Identity, [string] $RepositoryCommit) {
  $fields = @(
    'repositoryCommit',
    'configureMailAccessSha256',
    'mailAccessModuleSha256',
    'modulesLockSha256',
    'commonModuleSha256',
    'entraModuleSha256',
    'planSchemaSha256'
  )
  if ($Identity -isnot [System.Collections.IDictionary] -or $Identity.Count -ne $fields.Count) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  foreach ($field in $fields) {
    if (-not $Identity.Contains($field)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  if ([string] $Identity.repositoryCommit -cne $RepositoryCommit.ToLowerInvariant() -or
      [string] $Identity.repositoryCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($field in $fields | Where-Object { $_ -cne 'repositoryCommit' }) {
    if ([string] $Identity[$field] -cnotmatch '^[0-9a-f]{64}$') { throw 'INCOMPLETE_EVIDENCE' }
  }
  return $fields
}

function Get-M365MailSourceIdentity([string] $RepositoryCommit) {
  if ($RepositoryCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'INCOMPLETE_EVIDENCE' }
  $files = [ordered]@{
    configureMailAccessSha256 = 'Configure-MailAccess.ps1'
    mailAccessModuleSha256 = 'MailAccess.psm1'
    modulesLockSha256 = 'modules.lock.json'
    commonModuleSha256 = 'AuditSphere.M365.Common.psm1'
    entraModuleSha256 = 'AuditSphere.M365.Entra.psm1'
    planSchemaSha256 = 'm365-plan.schema.json'
  }
  $identity = [ordered]@{ repositoryCommit = $RepositoryCommit.ToLowerInvariant() }
  foreach ($field in $files.Keys) {
    $path = Join-Path $PSScriptRoot $files[$field]
    if (-not [System.IO.File]::Exists($path)) { throw 'INCOMPLETE_EVIDENCE' }
    $identity[$field] = (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
  }
  return $identity
}

function Invoke-M365MailReadbackWithSourceFence(
  [string] $RepositoryCommit,
  [scriptblock] $Readback,
  [object] $ReadbackArgument,
  [scriptblock] $SourceObserver
) {
  if ($RepositoryCommit -cnotmatch '^[0-9a-fA-F]{40}$' -or $null -eq $Readback) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $observe = if ($null -eq $SourceObserver) {
    { param($phase, $commit) Get-M365MailSourceIdentity $commit }
  } else { $SourceObserver }

  $before = & $observe 'before' $RepositoryCommit
  $fields = @(Assert-MailSourceIdentity $before $RepositoryCommit)
  $evidence = & $Readback $ReadbackArgument
  if ($evidence -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
  $after = & $observe 'after' $RepositoryCommit
  $null = Assert-MailSourceIdentity $after $RepositoryCommit
  foreach ($field in $fields) {
    if ([string] $before[$field] -cne [string] $after[$field]) { throw 'INCOMPLETE_EVIDENCE' }
  }

  $evidence.source = $after
  Assert-MailProviderSnapshotBound $evidence | Out-Null
  return $evidence
}

function Assert-M365MailSessionBinding($Plan, $GraphContext, $ExchangeConnections) {
  if ($null -eq $GraphContext) { throw 'BLOCKED_BOOTSTRAP' }
  foreach ($name in @('TenantId','ClientId','Environment','AuthType')) {
    if ($null -eq (Get-MailField $GraphContext $name)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  if ((ConvertTo-MailGuid (Get-MailField $GraphContext 'TenantId')) -cne (ConvertTo-MailGuid $Plan.tenantId) -or
      (ConvertTo-MailGuid (Get-MailField $GraphContext 'ClientId')) -cne (ConvertTo-MailGuid $Plan.operator.clientId) -or
      [string] (Get-MailField $GraphContext 'Environment') -cne 'Global') { throw 'BLOCKED_BOOTSTRAP' }
  if ([string] (Get-MailField $GraphContext 'AuthType') -cne 'AppOnly') { throw 'BLOCKED_BOOTSTRAP' }

  $connections = @(ConvertTo-MailRows $ExchangeConnections)
  if ($connections.Count -eq 0) { throw 'BLOCKED_BOOTSTRAP' }
  foreach ($connection in $connections) {
    foreach ($name in @('ConnectionId','State','TenantID','AppId','TokenStatus','IsEopSession','CertificateAuthentication')) {
      if ($null -eq (Get-MailField $connection $name)) { throw 'INCOMPLETE_EVIDENCE' }
    }
    $null = ConvertTo-MailGuid (Get-MailField $connection 'ConnectionId')
  }
  $connected = @($connections | Where-Object {
    [string] (Get-MailField $_ 'State') -ceq 'Connected' -and (Get-MailField $_ 'IsEopSession') -eq $false
  })
  $matching = @($connected | Where-Object {
    (ConvertTo-MailGuid (Get-MailField $_ 'TenantID')) -ceq (ConvertTo-MailGuid $Plan.tenantId) -and
    (ConvertTo-MailGuid (Get-MailField $_ 'AppId')) -ceq (ConvertTo-MailGuid $Plan.operator.clientId) -and
    [string] (Get-MailField $_ 'TokenStatus') -ceq 'Active' -and
    (Get-MailField $_ 'CertificateAuthentication') -eq $true
  })
  if ($connected.Count -ne 1 -or $matching.Count -ne 1) { throw 'BLOCKED_BOOTSTRAP' }
  $connectionId = ConvertTo-MailGuid (Get-MailField $matching[0] 'ConnectionId')
  $graphIdentity = [ordered]@{
    TenantId = ConvertTo-MailGuid (Get-MailField $GraphContext 'TenantId')
    ClientId = ConvertTo-MailGuid (Get-MailField $GraphContext 'ClientId')
    Environment = [string] (Get-MailField $GraphContext 'Environment')
  }
  foreach ($name in @('AuthType','ContextScope','Account','AppName','TokenCredentialType',
      'CertificateThumbprint','CertificateSubjectName','ManagedIdentityId')) {
    $value = Get-MailField $GraphContext $name
    if ($null -ne $value -and ($value -is [string] -or $value -is [ValueType])) { $graphIdentity[$name] = [string] $value }
    elseif ($name -ceq 'Account' -and $null -ne $value) {
      $accountIdentity = [ordered]@{}
      foreach ($accountField in @('Id','Username','Type')) {
        $accountValue = Get-MailField $value $accountField
        if ($null -ne $accountValue -and ($accountValue -is [string] -or $accountValue -is [ValueType])) {
          $accountIdentity[$accountField] = [string] $accountValue
        }
      }
      if ($accountIdentity.Count -gt 0) { $graphIdentity.Account = $accountIdentity }
    }
  }
  $scopes = Get-MailField $GraphContext 'Scopes'
  if ($null -ne $scopes) { $graphIdentity.Scopes = @($scopes | ForEach-Object { [string] $_ } | Sort-Object -Unique) }
  $bytes = [System.Text.Encoding]::UTF8.GetBytes(($graphIdentity | ConvertTo-Json -Depth 8 -Compress))
  $graphHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
  return "$graphHash`:$connectionId"
}

function Assert-M365MailCmdletMetadata($Commands) {
  $requirements = [ordered]@{
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
  foreach ($name in $requirements.Keys) {
    if (-not $Commands.Contains($name) -or $null -eq $Commands[$name] -or $null -eq $Commands[$name].Parameters) {
      throw 'INCOMPLETE_EVIDENCE'
    }
    foreach ($parameter in $requirements[$name]) {
      if (-not $Commands[$name].Parameters.ContainsKey($parameter)) { throw 'INCOMPLETE_EVIDENCE' }
    }
  }
  return $true
}

function Assert-M365MailGraphMetadata($Commands) {
  $requirements = @{
    'Get-MgContext' = @()
    'Invoke-MgGraphRequest' = @('Method','Uri','OutputType','SkipHttpErrorCheck','Body','ContentType')
    'Get-MgRequestContext' = @()
    'Set-MgRequestContext' = @('MaxRetry','RetriesTimeLimit','ClientTimeout','Confirm')
  }
  foreach ($name in $requirements.Keys) {
    if (-not $Commands.Contains($name) -or $null -eq $Commands[$name] -or
        $null -eq $Commands[$name].Parameters) { throw 'INCOMPLETE_EVIDENCE' }
    foreach ($parameter in $requirements[$name]) {
      if (-not $Commands[$name].Parameters.ContainsKey($parameter)) { throw 'INCOMPLETE_EVIDENCE' }
    }
  }
  return $true
}

function Assert-M365MailEntraAssignments($Plan, $Capability, $EnterpriseServicePrincipal,
    $GraphServicePrincipal, [object[]] $AppRoleAssignments) {
  Assert-MailFields $EnterpriseServicePrincipal @('id','appId','appOwnerOrganizationId')
  Assert-MailFields $GraphServicePrincipal @('id','appId','appRoles')
  $clientId = ConvertTo-MailGuid $Capability.clientId
  $tenantId = ConvertTo-MailGuid $Plan.tenantId
  $principalId = ConvertTo-MailGuid (Get-MailField $EnterpriseServicePrincipal 'id')
  if ((ConvertTo-MailGuid (Get-MailField $EnterpriseServicePrincipal 'appId')) -cne $clientId -or
      (ConvertTo-MailGuid (Get-MailField $EnterpriseServicePrincipal 'appOwnerOrganizationId')) -cne $tenantId -or
      ($Capability.ContainsKey('servicePrincipalObjectId') -and
        (ConvertTo-MailGuid $Capability.servicePrincipalObjectId) -cne $principalId)) { throw 'DRIFT' }
  if ((ConvertTo-MailGuid (Get-MailField $GraphServicePrincipal 'appId')) -cne $script:GraphApplicationId) { throw 'INCOMPLETE_EVIDENCE' }

  $mailRoles = @(ConvertTo-MailRows (Get-MailField $GraphServicePrincipal 'appRoles') | Where-Object {
    [string] (Get-MailField $_ 'value') -ceq 'Mail.Send'
  })
  if ($mailRoles.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
  Assert-MailFields $mailRoles[0] @('id','value','isEnabled','allowedMemberTypes')
  if ((Get-MailField $mailRoles[0] 'isEnabled') -ne $true -or
      'Application' -cnotin @(ConvertTo-MailRows (Get-MailField $mailRoles[0] 'allowedMemberTypes'))) { throw 'INCOMPLETE_EVIDENCE' }
  if ((Get-MailField $mailRoles[0] 'isEnabled') -isnot [bool]) { throw 'INCOMPLETE_EVIDENCE' }
  $null = ConvertTo-MailSortedStrings (Get-MailField $mailRoles[0] 'allowedMemberTypes')
  $roleId = ConvertTo-MailGuid (Get-MailField $mailRoles[0] 'id')
  $graphResourceId = ConvertTo-MailGuid (Get-MailField $GraphServicePrincipal 'id')
  $assignments = @(ConvertTo-MailRows $AppRoleAssignments)
  foreach ($assignment in $assignments) {
    Assert-MailFields $assignment @('id','principalId','resourceId','appRoleId')
    if ((ConvertTo-MailGuid (Get-MailField $assignment 'principalId')) -cne $principalId) { throw 'INCOMPLETE_EVIDENCE' }
  }

  $expectedRoles = @(ConvertTo-MailRows (Get-MailField $Capability 'expectedApplicationRoles'))
  if ($Plan.mail.permissionModel -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    if ($expectedRoles.Count -ne 1) { throw 'INVALID_OR_UNSAFE' }
    Assert-MailFields $expectedRoles[0] @('resourceAppId','value')
    if ((ConvertTo-MailGuid (Get-MailField $expectedRoles[0] 'resourceAppId')) -cne $script:GraphApplicationId -or
        [string] (Get-MailField $expectedRoles[0] 'value') -cne 'Mail.Send') { throw 'INVALID_OR_UNSAFE' }
    if ($assignments.Count -ne 1) { throw 'DRIFT' }
    if ((ConvertTo-MailGuid (Get-MailField $assignments[0] 'resourceId')) -cne $graphResourceId -or
        (ConvertTo-MailGuid (Get-MailField $assignments[0] 'appRoleId')) -cne $roleId) { throw 'DRIFT' }
  } elseif ($Plan.mail.permissionModel -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE') {
    if ($expectedRoles.Count -ne 0) { throw 'INVALID_OR_UNSAFE' }
    if ($assignments.Count -ne 0) { throw 'DRIFT' }
  } else { throw 'INVALID_OR_UNSAFE' }
  return $true
}

function Assert-M365MailExchangeEvidence($Plan, $Capability, $Evidence, $EnterpriseServicePrincipal) {
  $senderIds = @($Plan.mail.senderObjectIds | ForEach-Object { ConvertTo-MailGuid $_ })
  $controlId = ConvertTo-MailGuid $Plan.mail.controlMailboxObjectId
  $principal = Get-MailField $Evidence 'exchangeServicePrincipal'
  $roleAssignments = @(ConvertTo-MailRows (Get-MailField $Evidence 'roleAssignments'))
  if ($null -ne $principal) {
    Assert-MailFields $principal @('ObjectId','AppId')
    if ((ConvertTo-MailGuid (Get-MailField $principal 'ObjectId')) -cne
        (ConvertTo-MailGuid (Get-MailField $EnterpriseServicePrincipal 'id')) -or
        (ConvertTo-MailGuid (Get-MailField $principal 'AppId')) -cne (ConvertTo-MailGuid $Capability.clientId)) { throw 'DRIFT' }
  }

  if ($Plan.mail.permissionModel -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    if ($roleAssignments.Count -ne 0) { throw 'DRIFT' }
    $group = Get-MailField $Evidence 'policyGroup'
    Assert-MailFields $group @('ExternalDirectoryObjectId','Name')
    if ((ConvertTo-MailGuid (Get-MailField $group 'ExternalDirectoryObjectId')) -cne
        (ConvertTo-MailGuid $Plan.mail.policyScopeGroupId)) { throw 'DRIFT' }
    $members = @(ConvertTo-MailRows (Get-MailField $Evidence 'policyGroupMemberObjectIds'))
    if (-not (Test-MailExactSet $members $senderIds)) { throw 'DRIFT' }
    $policies = @(ConvertTo-MailRows (Get-MailField $Evidence 'applicationAccessPolicies'))
    if ($policies.Count -ne 1) { throw 'DRIFT' }
    Assert-MailFields $policies[0] @('AppId','ScopeName','AccessRight')
    $policyAppIds = @(ConvertTo-MailRows (Get-MailField $policies[0] 'AppId') | ForEach-Object { ConvertTo-MailGuid $_ })
    if ($policyAppIds.Count -ne 1 -or $policyAppIds[0] -cne (ConvertTo-MailGuid $Capability.clientId) -or
        [string] (Get-MailField $policies[0] 'ScopeName') -cne [string] (Get-MailField $group 'Name') -or
        [string] (Get-MailField $policies[0] 'AccessRight') -cne 'RestrictAccess') { throw 'DRIFT' }
    if ($null -ne (Get-MailField $policies[0] 'PolicyScopeGroupId') -and
        (ConvertTo-MailGuid (Get-MailField $policies[0] 'PolicyScopeGroupId')) -cne
        (ConvertTo-MailGuid $Plan.mail.policyScopeGroupId)) { throw 'DRIFT' }
    $probes = Get-MailField $Evidence 'applicationAccessPolicyProbes'
    if ($null -eq $probes -or $probes -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
    foreach ($mailboxId in (@($senderIds) + @($controlId))) {
      $probe = $probes[$mailboxId]
      Assert-MailFields $probe @('AppId','MailboxId','AccessCheckResult')
      if ((ConvertTo-MailGuid (Get-MailField $probe 'AppId')) -cne (ConvertTo-MailGuid $Capability.clientId) -or
          (ConvertTo-MailGuid (Get-MailField $probe 'MailboxId')) -cne $mailboxId) { throw 'INCOMPLETE_EVIDENCE' }
      $expected = if ($mailboxId -eq $controlId) { 'Denied' } else { 'Granted' }
      if ([string] (Get-MailField $probe 'AccessCheckResult') -cne $expected) { throw 'DRIFT' }
    }
    return $true
  }

  if ($Plan.mail.permissionModel -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE') {
    if ($null -eq $principal -or $roleAssignments.Count -ne 1) { throw 'DRIFT' }
    Assert-MailFields $roleAssignments[0] @('Role','RoleAssigneeName','RoleAssigneeType','AssignmentMethod')
    if ([string] (Get-MailField $roleAssignments[0] 'Role') -cne $script:MailRoleName -or
        (ConvertTo-MailGuid (Get-MailField $roleAssignments[0] 'RoleAssigneeName')) -cne
          (ConvertTo-MailGuid (Get-MailField $principal 'ObjectId')) -or
        [string] (Get-MailField $roleAssignments[0] 'RoleAssigneeType') -cne 'ServicePrincipal' -or
        [string] (Get-MailField $roleAssignments[0] 'AssignmentMethod') -cne 'Direct') { throw 'DRIFT' }
    $scope = Get-MailField $Evidence 'managementScope'
    Assert-MailFields $scope @('Name','ScopeRestrictionType','Exclusive','RecipientFilter')
    if ([string] (Get-MailField $scope 'Name') -cne [string] $Plan.mail.scopeName -or
        [string] (Get-MailField $scope 'ScopeRestrictionType') -cne 'RecipientScope' -or
        (Get-MailField $scope 'Exclusive') -ne $false -or
        [string]::IsNullOrWhiteSpace([string] (Get-MailField $scope 'RecipientFilter'))) { throw 'DRIFT' }
    $scopeMembers = @(ConvertTo-MailRows (Get-MailField $Evidence 'scopeMemberObjectIds'))
    if (-not (Test-MailExactSet $scopeMembers $senderIds)) { throw 'DRIFT' }
    $probes = Get-MailField $Evidence 'servicePrincipalAuthorizationProbes'
    if ($null -eq $probes -or $probes -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
    foreach ($mailboxId in (@($senderIds) + @($controlId))) {
      $rows = @(ConvertTo-MailRows $probes[$mailboxId])
      if ($rows.Count -ne 1) { throw 'DRIFT' }
      Assert-MailFields $rows[0] @('RoleName','GrantedPermissions','AllowedResourceScope','ScopeType','InScope')
      if ([string] (Get-MailField $rows[0] 'RoleName') -cne $script:MailRoleName -or
          -not (Test-MailExactPermission (Get-MailField $rows[0] 'GrantedPermissions') 'Mail.Send') -or
          [string] (Get-MailField $rows[0] 'AllowedResourceScope') -cne [string] $Plan.mail.scopeName -or
          [string] (Get-MailField $rows[0] 'ScopeType') -cne 'CustomRecipientScope' -or
          (Get-MailField $rows[0] 'InScope') -isnot [bool]) { throw 'INCOMPLETE_EVIDENCE' }
      $expectedInScope = $mailboxId -ne $controlId
      if ((Get-MailField $rows[0] 'InScope') -ne $expectedInScope) { throw 'DRIFT' }
    }
    return $true
  }
  throw 'INVALID_OR_UNSAFE'
}

function New-MailAuthorizationProviderSnapshot($Plan, $Evidence, $ExchangeEvidence) {
  $enterprise = Get-MailField $Evidence 'enterpriseServicePrincipal'
  $graph = Get-MailField $Evidence 'graphServicePrincipal'
  $graphRoles = @(ConvertTo-MailRows (Get-MailField $graph 'appRoles') | Where-Object {
    [string] (Get-MailField $_ 'value') -ceq 'Mail.Send'
  })
  if ($graphRoles.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
  $graphRole = $graphRoles[0]

  $providerMailboxIds = @(ConvertTo-MailSortedGuidStrings (Get-MailField $ExchangeEvidence 'mailboxObjectIds'))
  $expectedMailboxIds = @($Plan.mail.senderObjectIds) + @($Plan.mail.controlMailboxObjectId)
  if (-not (Test-MailExactSet $providerMailboxIds $expectedMailboxIds)) { throw 'DRIFT' }

  $senderReadback = if ($Plan.mail.permissionModel -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    Get-MailField $ExchangeEvidence 'policyGroupMemberObjectIds'
  } else {
    Get-MailField $ExchangeEvidence 'scopeMemberObjectIds'
  }
  $senderIds = @(ConvertTo-MailSortedGuidStrings $senderReadback)
  $controlCandidates = @($providerMailboxIds | Where-Object { $_ -notin $senderIds })
  if ($controlCandidates.Count -ne 1) { throw 'DRIFT' }

  $entraGrantSet = @()
  foreach ($assignment in @(ConvertTo-MailRows (Get-MailField $Evidence 'appRoleAssignments'))) {
    $entraGrantSet += [ordered]@{
      assignmentObjectId = ConvertTo-MailGuid (Get-MailField $assignment 'id')
      principalObjectId = ConvertTo-MailGuid (Get-MailField $assignment 'principalId')
      resourceObjectId = ConvertTo-MailGuid (Get-MailField $assignment 'resourceId')
      appRoleId = ConvertTo-MailGuid (Get-MailField $assignment 'appRoleId')
    }
  }
  $entraGrantSet = @($entraGrantSet | Sort-Object -Property assignmentObjectId -CaseSensitive)

  $exchangeRoleAssignmentSet = @()
  foreach ($assignment in @(ConvertTo-MailRows (Get-MailField $ExchangeEvidence 'roleAssignments'))) {
    $exchangeRoleAssignmentSet += [ordered]@{
      role = [string] (Get-MailField $assignment 'Role')
      roleAssigneeObjectId = ConvertTo-MailGuid (Get-MailField $assignment 'RoleAssigneeName')
      roleAssigneeType = [string] (Get-MailField $assignment 'RoleAssigneeType')
      assignmentMethod = [string] (Get-MailField $assignment 'AssignmentMethod')
    }
  }
  $exchangeRoleAssignmentSet = @($exchangeRoleAssignmentSet | Sort-Object -Property roleAssigneeObjectId, role, assignmentMethod -CaseSensitive)

  $legacyAuthorization = $null
  $exchangeRbacAuthorization = $null
  if ($Plan.mail.permissionModel -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    $group = Get-MailField $ExchangeEvidence 'policyGroup'
    $policies = @(ConvertTo-MailRows (Get-MailField $ExchangeEvidence 'applicationAccessPolicies'))
    if ($policies.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
    $policy = $policies[0]
    $legacyProbes = @()
    $policyProbes = Get-MailField $ExchangeEvidence 'applicationAccessPolicyProbes'
    foreach ($mailboxId in $providerMailboxIds) {
      $probe = $policyProbes[$mailboxId]
      if ($null -eq $probe) { throw 'INCOMPLETE_EVIDENCE' }
      $legacyProbes += [ordered]@{
        mailboxObjectId = ConvertTo-MailGuid (Get-MailField $probe 'MailboxId')
        allowed = ([string] (Get-MailField $probe 'AccessCheckResult') -ceq 'Granted')
      }
    }
    $legacyProbes = @($legacyProbes | Sort-Object -Property mailboxObjectId -CaseSensitive)
    $legacyAuthorization = [ordered]@{
      scopeGroupObjectId = ConvertTo-MailGuid (Get-MailField $group 'ExternalDirectoryObjectId')
      scopeGroupName = [string] (Get-MailField $group 'Name')
      policyScopeName = [string] (Get-MailField $policy 'ScopeName')
      accessRight = [string] (Get-MailField $policy 'AccessRight')
      scopeGroupMemberObjectIds = $senderIds
      policyApplicationClientIds = @(ConvertTo-MailSortedGuidStrings (Get-MailField $policy 'AppId'))
      probes = $legacyProbes
    }
  } else {
    $principal = Get-MailField $ExchangeEvidence 'exchangeServicePrincipal'
    $scope = Get-MailField $ExchangeEvidence 'managementScope'
    $authorizationProbes = Get-MailField $ExchangeEvidence 'servicePrincipalAuthorizationProbes'
    $rbacProbes = @()
    foreach ($mailboxId in $providerMailboxIds) {
      $rows = @(ConvertTo-MailRows $authorizationProbes[$mailboxId])
      if ($rows.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
      $probe = $rows[0]
      $rbacProbes += [ordered]@{
        mailboxObjectId = $mailboxId
        roleName = [string] (Get-MailField $probe 'RoleName')
        grantedPermissions = @(ConvertTo-MailSortedStrings (Get-MailField $probe 'GrantedPermissions'))
        allowedResourceScope = [string] (Get-MailField $probe 'AllowedResourceScope')
        scopeType = [string] (Get-MailField $probe 'ScopeType')
        inScope = [bool] (Get-MailField $probe 'InScope')
      }
    }
    $rbacProbes = @($rbacProbes | Sort-Object -Property mailboxObjectId -CaseSensitive)
    if ((Get-MailField $scope 'Exclusive') -isnot [bool]) { throw 'INCOMPLETE_EVIDENCE' }
    $exchangeRbacAuthorization = [ordered]@{
      servicePrincipalObjectId = ConvertTo-MailGuid (Get-MailField $principal 'ObjectId')
      servicePrincipalAppId = ConvertTo-MailGuid (Get-MailField $principal 'AppId')
      managementScopeName = [string] (Get-MailField $scope 'Name')
      scopeRestrictionType = [string] (Get-MailField $scope 'ScopeRestrictionType')
      exclusive = [bool] (Get-MailField $scope 'Exclusive')
      recipientFilter = [string] (Get-MailField $scope 'RecipientFilter')
      scopeMemberObjectIds = $senderIds
      probes = $rbacProbes
    }
  }

  return [ordered]@{
    tenantId = ConvertTo-MailGuid (Get-MailField $enterprise 'appOwnerOrganizationId')
    permissionModel = [string] $Plan.mail.permissionModel
    runtimeClientId = ConvertTo-MailGuid (Get-MailField $enterprise 'appId')
    enterpriseServicePrincipalObjectId = ConvertTo-MailGuid (Get-MailField $enterprise 'id')
    graphServicePrincipalObjectId = ConvertTo-MailGuid (Get-MailField $graph 'id')
    graphServicePrincipalAppId = ConvertTo-MailGuid (Get-MailField $graph 'appId')
    graphMailSendAppRoleId = ConvertTo-MailGuid (Get-MailField $graphRole 'id')
    graphMailSendRoleValue = [string] (Get-MailField $graphRole 'value')
    graphMailSendRoleEnabled = [bool] (Get-MailField $graphRole 'isEnabled')
    graphMailSendAllowedMemberTypes = @(ConvertTo-MailSortedStrings (Get-MailField $graphRole 'allowedMemberTypes'))
    senderMailboxObjectIds = $senderIds
    controlMailboxObjectId = $controlCandidates[0]
    entraGrantSet = $entraGrantSet
    exchangeRoleAssignmentSet = $exchangeRoleAssignmentSet
    legacyAuthorization = $legacyAuthorization
    exchangeRbacAuthorization = $exchangeRbacAuthorization
  }
}

function Assert-M365MailReadinessEvidence($Plan, $Evidence) {
  $capabilities = @($Plan.capabilities | Where-Object { $_.kind -ceq 'MAIL' })
  if ($capabilities.Count -ne 1 -or -not $Plan.ContainsKey('mail')) { throw 'INVALID_OR_UNSAFE' }
  $exchangeEvidence = if ($Evidence.Contains('exchange')) { $Evidence.exchange } else { $Evidence }
  Assert-M365MailEntraAssignments $Plan $capabilities[0] $Evidence.enterpriseServicePrincipal `
    $Evidence.graphServicePrincipal @($Evidence.appRoleAssignments) | Out-Null
  Assert-M365MailExchangeEvidence $Plan $capabilities[0] $exchangeEvidence $Evidence.enterpriseServicePrincipal | Out-Null
  $providerSnapshot = New-MailAuthorizationProviderSnapshot $Plan $Evidence $exchangeEvidence
  Assert-MailProviderSnapshotBound $providerSnapshot | Out-Null
  return [ordered]@{
    model = [string] $Plan.mail.permissionModel
    clientId = [string] $capabilities[0].clientId
    senderObjectIds = @($Plan.mail.senderObjectIds)
    controlMailboxObjectId = [string] $Plan.mail.controlMailboxObjectId
    providerSnapshot = $providerSnapshot
  }
}

function Import-M365MailPinnedModule($ModuleLock) {
  $installed = @(Get-Module -ListAvailable -Name $ModuleLock.name | Where-Object { $_.Version.ToString() -ceq $ModuleLock.version })
  if ($installed.Count -ne 1) { throw 'PREREQUISITE_MISSING' }
  $item = $installed[0]
  $manifestHash = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($manifestHash -cne $ModuleLock.manifestSha256) { throw 'PREREQUISITE_MISSING' }
  $loaded = @(Get-Module -Name $ModuleLock.name)
  if ($loaded.Count -gt 1 -or ($loaded.Count -eq 1 -and $loaded[0].Version.ToString() -cne $ModuleLock.version)) {
    throw 'PREREQUISITE_MISSING'
  }
  if ($loaded.Count -eq 0) { Import-Module -Name $item.Path -ErrorAction Stop }
  return $item
}

function Get-M365MailCommand($Name, [string] $ModuleName = $null) {
  $matches = if ($ModuleName) {
    @(Get-Command -Name $Name -Module $ModuleName -ErrorAction SilentlyContinue)
  } else {
    @(Get-Command -Name $Name -All -ErrorAction SilentlyContinue)
  }
  if ($matches.Count -ne 1) { return $null }
  return $matches[0]
}

function Invoke-M365MailExchangeRead([ValidateSet('Get-EXOMailbox','Get-ServicePrincipal','Get-ManagementScope',
    'Get-ManagementRoleAssignment','Get-DistributionGroup','Get-DistributionGroupMember','Get-EXORecipient',
    'Get-ApplicationAccessPolicy','Test-ApplicationAccessPolicy','Test-ServicePrincipalAuthorization')] [string] $Operation,
    [hashtable] $Arguments, $Commands) {
  if (-not $Commands.Contains($Operation)) { throw 'INCOMPLETE_EVIDENCE' }
  $command = $Commands[$Operation]
  if ($null -eq $command) { throw 'INCOMPLETE_EVIDENCE' }
  try {
    return Invoke-M365ProviderRequest {
      & $command @Arguments -ErrorAction Stop
    }
  } catch {
    $message = [string] $_.Exception.Message
    if ($message -match '(?i)(unauthori[sz]ed|forbidden|access denied|not authorized|insufficient privilege|not connected)') {
      throw 'BLOCKED_BOOTSTRAP'
    }
    throw 'PROVIDER_FAILURE'
  }
}

function Invoke-M365MailGraphGet($Transport, [string] $Path) {
  $response = & $Transport 'GET' $Path $null
  if ($null -eq $response -or $null -eq $response.statusCode) { throw 'INCOMPLETE_EVIDENCE' }
  if ([int] $response.statusCode -in @(401,403)) { throw 'BLOCKED_BOOTSTRAP' }
  if ([int] $response.statusCode -eq 404) { throw 'DRIFT' }
  if ([int] $response.statusCode -ne 200 -or $null -eq $response.body) { throw 'PROVIDER_FAILURE' }
  return $response.body
}

function Get-M365MailGraphCollection($Transport, [string] $Path) {
  $rows = [System.Collections.Generic.List[object]]::new()
  $nextPath = $Path
  for ($page = 0; $page -lt $script:MailMaximumGraphPages; $page++) {
    $body = Invoke-M365MailGraphGet $Transport $nextPath
    if ($body -isnot [System.Collections.IDictionary] -or -not $body.Contains('value')) { throw 'INCOMPLETE_EVIDENCE' }
    $value = $body['value']
    if ($null -eq $value -or $value -isnot [System.Collections.IList]) { throw 'INCOMPLETE_EVIDENCE' }
    foreach ($row in $value) {
      $rows.Add($row)
      if ($rows.Count -gt $script:MailMaximumGraphRows) { throw 'INCOMPLETE_EVIDENCE' }
    }

    $nextLink = if ($body.Contains('@odata.nextLink')) { [string] $body['@odata.nextLink'] } else { '' }
    if ([string]::IsNullOrEmpty($nextLink)) { return $rows.ToArray() }
    if ($page -eq $script:MailMaximumGraphPages - 1 -or $nextLink.Length -gt 8192 -or
        $nextLink -match '[\x00-\x20\x7f]' -or
        $nextLink -match '[\\#]') { throw 'INCOMPLETE_EVIDENCE' }
    $nextUri = $null
    if (-not [uri]::TryCreate($nextLink, [UriKind]::Absolute, [ref] $nextUri) -or
        $nextUri.Scheme -cne 'https' -or $nextUri.Host -cne 'graph.microsoft.com' -or
        $nextUri.Port -ne 443 -or $nextUri.UserInfo -or $nextUri.Fragment -or
        $nextUri.AbsolutePath -cne ([uri]::new('https://graph.microsoft.com' + $Path)).AbsolutePath -or
        -not $nextUri.Query) { throw 'INCOMPLETE_EVIDENCE' }
    $initialParameters = @{}
    foreach ($pair in ([uri]::new('https://graph.microsoft.com' + $Path)).Query.TrimStart('?').Split('&')) {
      $separator = $pair.IndexOf('=')
      if ($separator -lt 1) { throw 'INCOMPLETE_EVIDENCE' }
      $key = [uri]::UnescapeDataString($pair.Substring(0, $separator)).ToLowerInvariant()
      $value = [uri]::UnescapeDataString($pair.Substring($separator + 1))
      if ($initialParameters.ContainsKey($key)) { throw 'INCOMPLETE_EVIDENCE' }
      $initialParameters[$key] = $value
    }
    $continuationFound = $false
    $seenContinuation = @{}
    foreach ($pair in $nextUri.Query.TrimStart('?').Split('&')) {
      $separator = $pair.IndexOf('=')
      if ($separator -lt 1) { throw 'INCOMPLETE_EVIDENCE' }
      $key = [uri]::UnescapeDataString($pair.Substring(0, $separator)).ToLowerInvariant()
      $value = [uri]::UnescapeDataString($pair.Substring($separator + 1))
      if ($initialParameters.ContainsKey($key)) {
        if ($value -cne $initialParameters[$key]) { throw 'INCOMPLETE_EVIDENCE' }
      } elseif ($key -in @('$skiptoken','$skip')) {
        if ([string]::IsNullOrEmpty($value) -or $seenContinuation.ContainsKey($key)) { throw 'INCOMPLETE_EVIDENCE' }
        $seenContinuation[$key] = $true
        $continuationFound = $true
      } else { throw 'INCOMPLETE_EVIDENCE' }
    }
    if (-not $continuationFound) { throw 'INCOMPLETE_EVIDENCE' }
    $nextPath = $nextUri.PathAndQuery
    if (-not $nextPath.StartsWith('/v1.0/', [StringComparison]::Ordinal)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  throw 'INCOMPLETE_EVIDENCE'
}

function Get-M365MailReadOnlyEvidenceCore($Plan) {
  if ($Plan.mail.senderObjectIds.Count -gt $script:MailMaximumSenders) { throw 'INCOMPLETE_EVIDENCE' }
  Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.Entra.psm1') -ErrorAction Stop
  $lock = Get-Content -Raw (Join-Path $PSScriptRoot 'modules.lock.json') | ConvertFrom-Json -AsHashtable
  $graphLock = @($lock.modules | Where-Object { $_.name -ceq 'Microsoft.Graph.Authentication' })
  $exchangeLock = @($lock.modules | Where-Object { $_.name -ceq 'ExchangeOnlineManagement' })
  if ($graphLock.Count -ne 1 -or $exchangeLock.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($name in @('Get-MgContext','Invoke-MgGraphRequest','Get-MgRequestContext','Set-MgRequestContext')) {
    if ($name -cnotin @($graphLock[0].cmdlets)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  foreach ($name in @('Get-ConnectionInformation','Get-EXOMailbox','Get-ServicePrincipal','Get-ManagementScope',
      'Get-ManagementRoleAssignment','Get-DistributionGroup','Get-DistributionGroupMember','Get-EXORecipient',
      'Get-ApplicationAccessPolicy','Test-ApplicationAccessPolicy','Test-ServicePrincipalAuthorization')) {
    if ($name -cnotin @($exchangeLock[0].sessionLoadedCmdlets)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  $graphModule = Import-M365MailPinnedModule $graphLock[0]
  $exchangeModule = Import-M365MailPinnedModule $exchangeLock[0]

  $graphContextCommand = Get-M365MailCommand 'Get-MgContext' $graphModule.Name
  $connectionInfoCommand = Get-M365MailCommand 'Get-ConnectionInformation'
  $graphRequestCommand = Get-M365MailCommand 'Invoke-MgGraphRequest' $graphModule.Name
  $getRequestContext = Get-M365MailCommand 'Get-MgRequestContext' $graphModule.Name
  $setRequestContext = Get-M365MailCommand 'Set-MgRequestContext' $graphModule.Name
  if (-not $graphContextCommand -or -not $connectionInfoCommand) { throw 'INCOMPLETE_EVIDENCE' }
  $graphContext = & $graphContextCommand
  $connections = @(& $connectionInfoCommand)
  if (-not $graphContext -or $connections.Count -eq 0) { throw 'BLOCKED_BOOTSTRAP' }
  $initialSessionBinding = Assert-M365MailSessionBinding $Plan $graphContext $connections

  $commands = @{}
  foreach ($name in @('Get-ConnectionInformation','Get-EXOMailbox','Get-ServicePrincipal','Get-ManagementScope','Get-ManagementRoleAssignment',
      'Get-DistributionGroup','Get-DistributionGroupMember','Get-EXORecipient','Get-ApplicationAccessPolicy',
      'Test-ApplicationAccessPolicy','Test-ServicePrincipalAuthorization')) {
    $commands[$name] = if ($name -ceq 'Get-ConnectionInformation') { $connectionInfoCommand } else { Get-M365MailCommand $name }
  }
  Assert-M365MailCmdletMetadata $commands | Out-Null

  $graphCommands = @{
    'Invoke-MgGraphRequest' = $graphRequestCommand
    'Get-MgRequestContext' = $getRequestContext
    'Set-MgRequestContext' = $setRequestContext
  }
  Assert-M365MailGraphMetadata $graphCommands | Out-Null

  $provider = Get-Command -Name Invoke-M365ProviderRequest -ErrorAction Stop
  $transport = New-EntraGraphTransport $provider $graphRequestCommand $getRequestContext $setRequestContext -ReturnStatus
  $capability = @($Plan.capabilities | Where-Object { $_.kind -ceq 'MAIL' })[0]
  $clientId = ConvertTo-MailGuid $capability.clientId
  $graphId = $script:GraphApplicationId
  $principalPath = "/v1.0/servicePrincipals?`$filter=appId%20eq%20'$clientId'&`$select=id,appId,appOwnerOrganizationId&`$top=2"
  $principalRows = @(Get-M365MailGraphCollection $transport $principalPath)
  if ($principalRows.Count -ne 1) { throw 'DRIFT' }
  $principal = $principalRows[0]
  Assert-MailFields $principal @('id','appId','appOwnerOrganizationId')
  $graphResourcePath = "/v1.0/servicePrincipals?`$filter=appId%20eq%20'$graphId'&`$select=id,appId,appRoles&`$top=2"
  $graphResourceRows = @(Get-M365MailGraphCollection $transport $graphResourcePath)
  if ($graphResourceRows.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
  $graphResource = $graphResourceRows[0]
  $principalId = ConvertTo-MailGuid $principal.id
  $assignmentPath = "/v1.0/servicePrincipals/$principalId/appRoleAssignments?`$top=100&`$select=id,principalId,resourceId,appRoleId"
  $appRoleAssignments = @(Get-M365MailGraphCollection $transport $assignmentPath)

  $roleAssignments = @(Invoke-M365MailExchangeRead 'Get-ManagementRoleAssignment' `
    @{ RoleAssignee = $principalId } $commands)
  $exchangePointer = $null
  if ($Plan.mail.permissionModel -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE') {
    if ($roleAssignments.Count -ne 1) { throw 'DRIFT' }
    $exchangePointerRows = @(Invoke-M365MailExchangeRead 'Get-ServicePrincipal' @{ Identity = $principalId } $commands)
    $matchingPointers = @($exchangePointerRows | Where-Object {
      [string] (Get-MailField $_ 'ObjectId') -ceq $principalId
    })
    if ($matchingPointers.Count -ne 1 -or $exchangePointerRows.Count -ne 1) { throw 'DRIFT' }
    $exchangePointer = $matchingPointers[0]
  }

  $exchangeEvidence = @{
    enterpriseServicePrincipal = $principal
    exchangeServicePrincipal = $exchangePointer
    roleAssignments = $roleAssignments
  }

  $mailboxRowsById = @{}
  $observedMailboxIds = @()
  foreach ($mailboxId in (@($Plan.mail.senderObjectIds) + @($Plan.mail.controlMailboxObjectId))) {
    $mailboxes = @(Invoke-M365MailExchangeRead 'Get-EXOMailbox' `
      @{ ExternalDirectoryObjectId = [guid] $mailboxId; Properties = @('ExternalDirectoryObjectId') } $commands)
    if ($mailboxes.Count -ne 1) { throw 'DRIFT' }
    Assert-MailFields $mailboxes[0] @('ExternalDirectoryObjectId')
    if ((ConvertTo-MailGuid $mailboxes[0].ExternalDirectoryObjectId) -cne (ConvertTo-MailGuid $mailboxId)) { throw 'DRIFT' }
    $actualMailboxId = ConvertTo-MailGuid $mailboxes[0].ExternalDirectoryObjectId
    $mailboxRowsById[(ConvertTo-MailGuid $mailboxId)] = $mailboxes[0]
    $observedMailboxIds += $actualMailboxId
  }
  $exchangeEvidence.mailboxObjectIds = $observedMailboxIds

  if ($Plan.mail.permissionModel -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    $policyGroupId = ConvertTo-MailGuid $Plan.mail.policyScopeGroupId
    $groups = @(Invoke-M365MailExchangeRead 'Get-DistributionGroup' `
      @{ Identity = $policyGroupId; RecipientTypeDetails = @('MailUniversalSecurityGroup') } $commands)
    if ($groups.Count -ne 1) { throw 'DRIFT' }
    $group = $groups[0]
    Assert-MailFields $group @('ExternalDirectoryObjectId','Name')
    $members = @(Invoke-M365MailExchangeRead 'Get-DistributionGroupMember' `
      @{ Identity = $policyGroupId; ResultSize = $Plan.mail.senderObjectIds.Count + 1 } $commands)
    $memberIds = @($members | ForEach-Object {
      Assert-MailFields $_ @('ExternalDirectoryObjectId')
      Get-MailField $_ 'ExternalDirectoryObjectId'
    })
    $allPolicies = @(Invoke-M365MailExchangeRead 'Get-ApplicationAccessPolicy' @{} $commands)
    $appPolicies = @()
    foreach ($policy in $allPolicies) {
      Assert-MailFields $policy @('AppId','ScopeName','AccessRight')
      $ids = @(ConvertTo-MailRows (Get-MailField $policy 'AppId'))
      if (@($ids | Where-Object { (ConvertTo-MailGuid $_) -ceq $clientId }).Count -gt 0) { $appPolicies += $policy }
    }
    $policyProbes = @{}
    foreach ($mailboxId in (@($Plan.mail.senderObjectIds) + @($Plan.mail.controlMailboxObjectId))) {
      $mailbox = $mailboxRowsById[(ConvertTo-MailGuid $mailboxId)]
      $actualMailboxId = ConvertTo-MailGuid $mailbox.ExternalDirectoryObjectId
      $probeRows = @(Invoke-M365MailExchangeRead 'Test-ApplicationAccessPolicy' `
        @{ Identity = $actualMailboxId; AppId = $clientId } $commands)
      if ($probeRows.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
      $policyProbes[$actualMailboxId] = $probeRows[0]
    }
    $exchangeEvidence.policyGroup = $group
    $exchangeEvidence.policyGroupMemberObjectIds = $memberIds
    $exchangeEvidence.applicationAccessPolicies = $appPolicies
    $exchangeEvidence.applicationAccessPolicyProbes = $policyProbes
  } elseif ($Plan.mail.permissionModel -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE') {
    if (-not $exchangePointer) { throw 'DRIFT' }
    $scopes = @(Invoke-M365MailExchangeRead 'Get-ManagementScope' @{ Identity = [string] $Plan.mail.scopeName } $commands)
    if ($scopes.Count -ne 1) { throw 'DRIFT' }
    $scope = $scopes[0]
    Assert-MailFields $scope @('Name','ScopeRestrictionType','Exclusive','RecipientFilter')
    $recipientFilter = [string] (Get-MailField $scope 'RecipientFilter')
    if ([string]::IsNullOrWhiteSpace($recipientFilter) -or $recipientFilter.Length -gt 4096 -or
        $recipientFilter -match '[\x00-\x1f\x7f]') { throw 'INCOMPLETE_EVIDENCE' }
    $scopeRecipients = @(Invoke-M365MailExchangeRead 'Get-EXORecipient' `
      @{ Filter = $recipientFilter; ResultSize = $Plan.mail.senderObjectIds.Count + 1; Properties = @('ExternalDirectoryObjectId') } $commands)
    $scopeMemberIds = @($scopeRecipients | ForEach-Object {
      Assert-MailFields $_ @('ExternalDirectoryObjectId')
      Get-MailField $_ 'ExternalDirectoryObjectId'
    })
    $authorizationProbes = @{}
    foreach ($mailboxId in (@($Plan.mail.senderObjectIds) + @($Plan.mail.controlMailboxObjectId))) {
      $actualMailboxId = ConvertTo-MailGuid $mailboxRowsById[(ConvertTo-MailGuid $mailboxId)].ExternalDirectoryObjectId
      $rows = @(Invoke-M365MailExchangeRead 'Test-ServicePrincipalAuthorization' `
        @{ Identity = $principalId; Resource = $actualMailboxId } $commands)
      $authorizationProbes[$actualMailboxId] = $rows
    }
    $exchangeEvidence.managementScope = $scope
    $exchangeEvidence.scopeMemberObjectIds = $scopeMemberIds
    $exchangeEvidence.servicePrincipalAuthorizationProbes = $authorizationProbes
  } else { throw 'INVALID_OR_UNSAFE' }

  $finalGraphContext = & $graphContextCommand
  $finalConnections = @(& $commands['Get-ConnectionInformation'])
  $finalSessionBinding = Assert-M365MailSessionBinding $Plan $finalGraphContext $finalConnections
  if ($finalSessionBinding -cne $initialSessionBinding) { throw 'BLOCKED_BOOTSTRAP' }

  $evidence = @{
    enterpriseServicePrincipal = $principal
    graphServicePrincipal = $graphResource
    appRoleAssignments = $appRoleAssignments
    exchange = $exchangeEvidence
  }
  $snapshot = Assert-M365MailReadinessEvidence $Plan $evidence
  $snapshot.evidenceReference = "m365-mail-readonly:$($Plan.tenantId):sha256:$(Get-M365PlanSha256 $Plan)"
  return $snapshot
}

function Get-M365MailReadOnlyEvidence($Plan, [string] $RepositoryCommit) {
  if ($RepositoryCommit -cnotmatch '^[0-9a-fA-F]{40}$' -or
      [string] $Plan.repositoryCommit -ine $RepositoryCommit) { throw 'INCOMPLETE_EVIDENCE' }
  $readback = { param($readbackPlan) Get-M365MailReadOnlyEvidenceCore $readbackPlan }
  return Invoke-M365MailReadbackWithSourceFence $RepositoryCommit $readback $Plan $null
}

function Add-M365MailRuntimeGate($Result, $Snapshot) {
  Assert-MailFields $Snapshot @('model','clientId','senderObjectIds','controlMailboxObjectId','evidenceReference',
    'providerSnapshot','source')
  $clientId = ConvertTo-MailGuid (Get-MailField $Snapshot 'clientId')
  $senders = @(ConvertTo-MailRows (Get-MailField $Snapshot 'senderObjectIds'))
  if ($senders.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string] (Get-MailField $Snapshot 'evidenceReference'))) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $source = Get-MailField $Snapshot 'source'
  $sourceFields = @('repositoryCommit','configureMailAccessSha256','mailAccessModuleSha256','modulesLockSha256',
    'commonModuleSha256','entraModuleSha256','planSchemaSha256')
  Assert-MailFields $source $sourceFields
  if ($source -isnot [System.Collections.IDictionary] -or $source.Count -ne $sourceFields.Count -or
      [string] (Get-MailField $source 'repositoryCommit') -cnotmatch '^[0-9a-f]{40}$') { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($field in @('configureMailAccessSha256','mailAccessModuleSha256','modulesLockSha256',
      'commonModuleSha256','entraModuleSha256','planSchemaSha256')) {
    if ([string] (Get-MailField $source $field) -cnotmatch '^[0-9a-f]{64}$') { throw 'INCOMPLETE_EVIDENCE' }
  }
  Assert-MailFields $Result @('repositoryCommit','tenantId')
  $providerSnapshot = Get-MailField $Snapshot 'providerSnapshot'
  $providerFields = @('tenantId','permissionModel','runtimeClientId','enterpriseServicePrincipalObjectId',
    'graphServicePrincipalObjectId','graphServicePrincipalAppId','graphMailSendAppRoleId','graphMailSendRoleValue',
    'graphMailSendRoleEnabled','graphMailSendAllowedMemberTypes','senderMailboxObjectIds','controlMailboxObjectId',
    'entraGrantSet','exchangeRoleAssignmentSet','legacyAuthorization','exchangeRbacAuthorization')
  if ($providerSnapshot -isnot [System.Collections.IDictionary] -or $providerSnapshot.Count -ne $providerFields.Count) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  foreach ($field in $providerFields) {
    if (-not $providerSnapshot.Contains($field)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  Assert-MailFields $providerSnapshot @($providerFields | Where-Object {
    $_ -notin @('entraGrantSet','exchangeRoleAssignmentSet','legacyAuthorization','exchangeRbacAuthorization')
  })
  foreach ($field in @('entraGrantSet','exchangeRoleAssignmentSet')) {
    if ($providerSnapshot[$field] -isnot [System.Collections.IEnumerable] -or $providerSnapshot[$field] -is [string]) {
      throw 'INCOMPLETE_EVIDENCE'
    }
  }
  if ([string] (Get-MailField $Snapshot 'model') -ceq 'LEGACY_APPLICATION_ACCESS_POLICY' -and
      ($null -eq (Get-MailField $providerSnapshot 'legacyAuthorization') -or
       $null -ne (Get-MailField $providerSnapshot 'exchangeRbacAuthorization'))) { throw 'DRIFT' }
  if ([string] (Get-MailField $Snapshot 'model') -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE' -and
      ($null -ne (Get-MailField $providerSnapshot 'legacyAuthorization') -or
       $null -eq (Get-MailField $providerSnapshot 'exchangeRbacAuthorization'))) { throw 'DRIFT' }
  if (
      [string] (Get-MailField $providerSnapshot 'permissionModel') -cne [string] (Get-MailField $Snapshot 'model') -or
      [string] (Get-MailField $source 'repositoryCommit') -cne [string] (Get-MailField $Result 'repositoryCommit') -or
      (ConvertTo-MailGuid (Get-MailField $providerSnapshot 'tenantId')) -cne
        (ConvertTo-MailGuid (Get-MailField $Result 'tenantId')) -or
      (ConvertTo-MailGuid (Get-MailField $providerSnapshot 'runtimeClientId')) -cne $clientId -or
      (ConvertTo-MailGuid (Get-MailField $providerSnapshot 'controlMailboxObjectId')) -cne
        (ConvertTo-MailGuid (Get-MailField $Snapshot 'controlMailboxObjectId')) -or
      -not (Test-MailExactSet @((Get-MailField $providerSnapshot 'senderMailboxObjectIds')) $senders)) { throw 'DRIFT' }
  Add-M365Check $Result 'mail-current-state-authorization' 'PASS' `
    'exact current Entra grants, Exchange scope, sender membership and control denial' `
    'Read-only provider evidence matched the explicit plan; no messages were sent.'
  $Result.checks[-1].capability = 'MAIL'
  $Result.checks[-1].resourceId = $clientId
  $Result.checks[-1].evidenceReference = [string] (Get-MailField $Snapshot 'evidenceReference')
  $Result.checks[-1].source = $source
  $Result.checks[-1].providerSnapshot = $providerSnapshot

  $model = [string] (Get-MailField $Snapshot 'model')
  if ($model -ceq 'LEGACY_APPLICATION_ACCESS_POLICY') {
    $prerequisite = 'verification:legacy-mail-capability-evidence-not-verified'
    $errorCode = 'mail-legacy-capability-evidence-not-verified'
    $checkId = 'mail-capability-evidence'
    $expected = 'actual API capability evidence for the existing legacy mail runtime'
    $observed = 'This read-only provider adapter did not evaluate the existing legacy mail capability verifier; API capability evidence is still required.'
  } elseif ($model -ceq 'EXCHANGE_RBAC_RESOURCE_SCOPE') {
    $prerequisite = 'engineering:exchange-rbac-mail-runtime-not-implemented'
    $errorCode = 'mail-rbac-runtime-not-implemented'
    $checkId = 'mail-runtime-activation'
    $expected = 'implemented Exchange RBAC mail runtime and current authorization enforcement'
    $observed = 'Engineering prerequisite: Exchange RBAC mail runtime support is not implemented; provider authorization evidence alone is not application readiness.'
  } else { throw 'INVALID_OR_UNSAFE' }
  $Result.blockedPrerequisites += $prerequisite
  $Result.errorCode = $errorCode
  Add-M365Check $Result $checkId 'INCOMPLETE_EVIDENCE' $expected $observed $errorCode
  $Result.checks[-1].capability = 'MAIL'
  $Result.checks[-1].resourceId = $clientId
  return 'INCOMPLETE_EVIDENCE'
}

Export-ModuleMember -Function Assert-M365MailSessionBinding, Assert-M365MailCmdletMetadata,
  Assert-M365MailGraphMetadata, Assert-M365MailEntraAssignments, Assert-M365MailExchangeEvidence,
  Assert-M365MailReadinessEvidence, Get-M365MailReadOnlyEvidence, Add-M365MailRuntimeGate
