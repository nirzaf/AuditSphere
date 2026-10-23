#Requires -Version 7.4
# WU-022 read-only evidence adapter. It never authenticates or changes provider state.
Set-StrictMode -Version Latest

$script:MailMaximumSenders = 1000
$script:MailMaximumGraphRows = 1000
$script:MailMaximumGraphPages = 10
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
  $roleId = ConvertTo-MailGuid (Get-MailField $mailRoles[0] 'id')
  $graphResourceId = ConvertTo-MailGuid (Get-MailField $GraphServicePrincipal 'id')
  $assignments = @(ConvertTo-MailRows $AppRoleAssignments)
  foreach ($assignment in $assignments) {
    Assert-MailFields $assignment @('principalId','resourceId','appRoleId')
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

function Assert-M365MailReadinessEvidence($Plan, $Evidence) {
  $capabilities = @($Plan.capabilities | Where-Object { $_.kind -ceq 'MAIL' })
  if ($capabilities.Count -ne 1 -or -not $Plan.ContainsKey('mail')) { throw 'INVALID_OR_UNSAFE' }
  $exchangeEvidence = if ($Evidence.Contains('exchange')) { $Evidence.exchange } else { $Evidence }
  Assert-M365MailEntraAssignments $Plan $capabilities[0] $Evidence.enterpriseServicePrincipal `
    $Evidence.graphServicePrincipal @($Evidence.appRoleAssignments) | Out-Null
  Assert-M365MailExchangeEvidence $Plan $capabilities[0] $exchangeEvidence $Evidence.enterpriseServicePrincipal | Out-Null
  return [ordered]@{
    model = [string] $Plan.mail.permissionModel
    clientId = [string] $capabilities[0].clientId
    senderObjectIds = @($Plan.mail.senderObjectIds)
    controlMailboxObjectId = [string] $Plan.mail.controlMailboxObjectId
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

function Get-M365MailReadOnlyEvidence($Plan) {
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
  foreach ($mailboxId in (@($Plan.mail.senderObjectIds) + @($Plan.mail.controlMailboxObjectId))) {
    $mailboxes = @(Invoke-M365MailExchangeRead 'Get-EXOMailbox' `
      @{ ExternalDirectoryObjectId = [guid] $mailboxId; Properties = @('ExternalDirectoryObjectId') } $commands)
    if ($mailboxes.Count -ne 1) { throw 'DRIFT' }
    Assert-MailFields $mailboxes[0] @('ExternalDirectoryObjectId')
    if ((ConvertTo-MailGuid $mailboxes[0].ExternalDirectoryObjectId) -cne (ConvertTo-MailGuid $mailboxId)) { throw 'DRIFT' }
    $mailboxRowsById[(ConvertTo-MailGuid $mailboxId)] = $mailboxes[0]
  }

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
      $probeRows = @(Invoke-M365MailExchangeRead 'Test-ApplicationAccessPolicy' `
        @{ Identity = [string] (ConvertTo-MailGuid $mailboxId); AppId = $clientId } $commands)
      if ($probeRows.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
      $policyProbes[(ConvertTo-MailGuid $mailboxId)] = $probeRows[0]
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
      $rows = @(Invoke-M365MailExchangeRead 'Test-ServicePrincipalAuthorization' `
        @{ Identity = $principalId; Resource = [string] $mailboxId } $commands)
      $authorizationProbes[(ConvertTo-MailGuid $mailboxId)] = $rows
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

function Add-M365MailRuntimeGate($Result, $Snapshot) {
  Assert-MailFields $Snapshot @('model','clientId','senderObjectIds','controlMailboxObjectId','evidenceReference')
  $clientId = ConvertTo-MailGuid (Get-MailField $Snapshot 'clientId')
  $senders = @(ConvertTo-MailRows (Get-MailField $Snapshot 'senderObjectIds'))
  if ($senders.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string] (Get-MailField $Snapshot 'evidenceReference'))) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  Add-M365Check $Result 'mail-current-state-authorization' 'PASS' `
    'exact current Entra grants, Exchange scope, sender membership and control denial' `
    'Read-only provider evidence matched the explicit plan; no messages were sent.'
  $Result.checks[-1].capability = 'MAIL'
  $Result.checks[-1].resourceId = $clientId
  $Result.checks[-1].evidenceReference = [string] (Get-MailField $Snapshot 'evidenceReference')

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
