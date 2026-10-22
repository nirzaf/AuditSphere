#Requires -Version 7.4
# Automated SharePoint client site provisioning and managed staff Full Control reconciliation.
# Preserves client isolation, strict ownership markers, rollout cutoffs, and exact staff group membership.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ClientSitesPlan($Plan) {
  if (-not $Plan.ContainsKey('clientSites') -or $null -eq $Plan.clientSites) { throw 'INVALID_OR_UNSAFE' }
  $cs = $Plan.clientSites
  if (-not $cs.ContainsKey('host') -or -not $cs.ContainsKey('custodianObjectId') -or -not $cs.ContainsKey('rolloutCutoffUtc')) { throw 'INVALID_OR_UNSAFE' }
  if ($cs.host -cnotmatch '^[a-z0-9-]+\.sharepoint\.com$') { throw 'INVALID_OR_UNSAFE' }
  $custodian = [guid]::Empty
  if (-not [guid]::TryParseExact($cs.custodianObjectId, 'D', [ref]$custodian) -or $custodian -eq [guid]::Empty) { throw 'INVALID_OR_UNSAFE' }
  $cutoff = [DateTimeOffset]::MinValue
  $parsedCutoff = if ($cs.rolloutCutoffUtc -is [datetime]) { $cutoff = [DateTimeOffset]$cs.rolloutCutoffUtc; $true }
                  else { [DateTimeOffset]::TryParse([string]$cs.rolloutCutoffUtc, [ref]$cutoff) }
  if (-not $parsedCutoff) { throw 'INVALID_OR_UNSAFE' }

  if (-not $Plan.ContainsKey('sites') -or -not $Plan.sites.Count -or $Plan.sites.Count -gt 50) { throw 'INVALID_OR_UNSAFE' }
  $firmId = [guid]::Empty
  if (-not [guid]::TryParseExact($Plan.firmId, 'D', [ref]$firmId) -or $firmId -eq [guid]::Empty) { throw 'INVALID_OR_UNSAFE' }

  $documents = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')
  if ($documents.Count -ne 1 -or -not $documents[0].clientId) { throw 'INVALID_OR_UNSAFE' }

  $seenClients = @{}
  foreach ($site in $Plan.sites) {
    if (-not $site.ContainsKey('siteId') -or -not $site.ContainsKey('siteUrl') -or
        -not $site.ContainsKey('ownershipMarker') -or $site.permission -cne 'write') { throw 'INVALID_OR_UNSAFE' }
    $id = Get-SitesId $site.siteId
    if ($id.host -cne $cs.host) { throw 'INVALID_OR_UNSAFE' }
    $uri = [uri]$site.siteUrl
    if ($uri.Scheme -cne 'https' -or $uri.Host -cne $cs.host -or -not $uri.IsDefaultPort -or
        $uri.UserInfo -or $uri.Query -or $uri.Fragment -or -not $uri.AbsolutePath.StartsWith('/sites/')) { throw 'INVALID_OR_UNSAFE' }

    if ($site.ContainsKey('clientId')) {
      $clientGuid = [guid]::Empty
      if (-not [guid]::TryParseExact($site.clientId, 'D', [ref]$clientGuid) -or $clientGuid -eq [guid]::Empty) { throw 'INVALID_OR_UNSAFE' }
      $clientKey = $clientGuid.ToString('D')
      if ($seenClients.ContainsKey($clientKey)) { throw 'INVALID_OR_UNSAFE' }
      $seenClients[$clientKey] = $true

      $expectedMarker = "AuditSphere:$($Plan.firmId.ToLowerInvariant()):$clientKey"
      if ($site.ownershipMarker -cne $expectedMarker) { throw 'INVALID_OR_UNSAFE' }
      $expectedSuffix = '-' + $clientGuid.ToString('N')
      if (-not $uri.AbsolutePath.EndsWith($expectedSuffix, [StringComparison]::Ordinal)) { throw 'INVALID_OR_UNSAFE' }

      if ($site.ContainsKey('onboardedAtUtc') -and $null -ne $site.onboardedAtUtc) {
        $onboarded = [DateTimeOffset]::MinValue
        $parsedOnboarded = if ($site.onboardedAtUtc -is [datetime]) { $onboarded = [DateTimeOffset]$site.onboardedAtUtc; $true }
                           else { [DateTimeOffset]::TryParse([string]$site.onboardedAtUtc, [ref]$onboarded) }
        if ($parsedOnboarded -and $onboarded -lt $cutoff) {
          if (-not $site.ContainsKey('migrationApproved') -or -not $site.migrationApproved) {
            # Pre-cutoff clients cannot be silently converted to dedicated sites without explicit approval record.
            throw 'INVALID_OR_UNSAFE'
          }
        }
      }
    }
  }

  if ($Plan.ContainsKey('staffAssignments') -and $null -ne $Plan.staffAssignments) {
    $sa = $Plan.staffAssignments
    if (-not $sa.ContainsKey('manifestId') -or -not $sa.ContainsKey('generation') -or
        -not $sa.ContainsKey('observedAtUtc') -or -not $sa.ContainsKey('expiresAtUtc') -or
        -not $sa.ContainsKey('assignments')) { throw 'INVALID_OR_UNSAFE' }
    $manifestGuid = [guid]::Empty
    if (-not [guid]::TryParseExact($sa.manifestId, 'D', [ref]$manifestGuid) -or $manifestGuid -eq [guid]::Empty) { throw 'INVALID_OR_UNSAFE' }
    if ($sa.generation -lt 1) { throw 'INVALID_OR_UNSAFE' }
    $expires = [DateTimeOffset]::MinValue
    $parsedExpires = if ($sa.expiresAtUtc -is [datetime]) { $expires = [DateTimeOffset]$sa.expiresAtUtc; $true }
                     else { [DateTimeOffset]::TryParse([string]$sa.expiresAtUtc, [ref]$expires) }
    if (-not $parsedExpires -or $expires.ToUniversalTime() -lt [DateTimeOffset]::UtcNow) {
      throw 'INCOMPLETE_EVIDENCE'
    }
  }
}

function Get-ClientSitesJournalPath($Plan, [string] $StateDirectory = $null) {
  return Join-Path (Get-EntraStateDirectory $StateDirectory) "$($Plan.tenantId.ToLowerInvariant())-$($Plan.ownershipTag.ToLowerInvariant())-client-sites.json"
}

function Save-ClientSitesMutationIntent($Journal, [string] $JournalPath, [string] $Method, [string] $Path, $Body, [string] $PlanSha256) {
  Save-EntraMutationIntent $Journal $JournalPath $Method $Path $Body $PlanSha256
}

function Confirm-ClientSitesMutationIntent($Journal, [string] $JournalPath, [string] $Path, $Body) {
  Confirm-EntraMutationIntent $Journal $JournalPath $Path $Body
}

function Invoke-SharePointClientSitesReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $Approve, [scriptblock] $Confirm, [scriptblock] $GraphTransport) {
  Assert-ClientSitesPlan $Plan
  $documents = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $cs = $Plan.clientSites

  foreach ($site in $Plan.sites) {
    $siteUrl = $site.siteUrl.TrimEnd('/')
    $statusUrl = "/_api/SPSiteManager/status?url='$([uri]::EscapeDataString($siteUrl))'"
    $statusResponse = & $Transport 'GET' $statusUrl $null
    if ($null -eq $statusResponse -or -not $statusResponse.ContainsKey('SiteStatus')) { throw 'PROVIDER_FAILURE' }
    $siteStatus = [int]$statusResponse.SiteStatus

    if ($siteStatus -eq 0) {
      # Site does not exist
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      $createPath = '/_api/SPSiteManager/create'
      $createBody = @{
        request = @{
          Title = "AuditSphere Client $($site.clientId)"
          Url = $siteUrl
          Lcid = 1033
          ShareByEmailEnabled = $false
          Description = $site.ownershipMarker
          WebTemplate = 'STS#3'
          Owner = $cs.custodianObjectId
        }
      }
      if (-not (& $Approve 'POST' $createPath $createBody)) { throw 'BLOCKED_APPROVAL' }
      $Result.mutationsAttempted++
      try {
        $created = & $Transport 'POST' $createPath $createBody
        $siteStatus = [int]$created.SiteStatus
      } catch {
        throw 'RESULT_UNCERTAIN'
      }
      & $Confirm $createPath $createBody | Out-Null
      $Result.mutationsConfirmed++
      $Result.changes += @{ target = $siteUrl; action = 'CREATE_CLIENT_SITE'; template = 'STS#3'; owner = $cs.custodianObjectId }
    }

    if ($siteStatus -eq 1) { throw 'RESULT_UNCERTAIN' } # Provisioning pending
    if ($siteStatus -notin @(2, 4)) { throw 'PROVIDER_FAILURE' }

    # Validate web metadata and ownership marker
    $webResponse = & $Transport 'GET' "$siteUrl/_api/web?`$select=Id,Url,Description" $null
    if ($null -eq $webResponse -or -not $webResponse.ContainsKey('Description') -or -not $webResponse.ContainsKey('Url')) { throw 'PROVIDER_FAILURE' }
    if ($webResponse.Description -cne $site.ownershipMarker) { throw 'INVALID_OR_UNSAFE' }
    if (-not [string]::Equals($webResponse.Url.TrimEnd('/'), $siteUrl, [StringComparison]::OrdinalIgnoreCase)) { throw 'INVALID_OR_UNSAFE' }

    Add-M365Check $Result "site:$siteUrl:status" 'PASS' 'SharePoint client site provisioned and active' "status:$siteStatus"
    $Result.checks[-1].capability = 'CLIENT_SITES'; $Result.checks[-1].resourceId = $siteUrl
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "sharepoint-site:$siteUrl"

    Add-M365Check $Result "site:$siteUrl:ownership" 'PASS' 'exact ownership marker verified on site web' $site.ownershipMarker
    $Result.checks[-1].capability = 'CLIENT_SITES'; $Result.checks[-1].resourceId = $siteUrl
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "sharepoint-web-description:$siteUrl"

    # Validate Graph site ID and DOCUMENTS worker grant
    $id = (Get-SitesId $site.siteId).id
    $permissionsUrl = "/v1.0/sites/$id/permissions"
    $permissions = Get-SitesPermissions $GraphTransport $id
    $grant = Get-DocumentsSiteGrant $permissions $documents.clientId
    if (-not $grant.Count) {
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      $grantBody = @{ roles = @('write'); grantedToIdentities = @(@{ application = @{ id = $documents.clientId } }) }
      if (-not (& $Approve 'POST' $permissionsUrl $grantBody)) { throw 'BLOCKED_APPROVAL' }
      $Result.mutationsAttempted++
      try { & $GraphTransport 'POST' $permissionsUrl $grantBody | Out-Null } catch { throw 'RESULT_UNCERTAIN' }
      $grant = Get-DocumentsSiteGrant (Get-SitesPermissions $GraphTransport $id) $documents.clientId
      if ($grant.Count -ne 1) { throw 'RESULT_UNCERTAIN' }
      & $Confirm $permissionsUrl $grantBody | Out-Null
      $Result.mutationsConfirmed++
      $Result.changes += @{ target = $id; action = 'GRANT_DOCUMENT_APPLICATION_WRITE'; clientId = $documents.clientId }
    }

    Add-M365Check $Result "site:$id:worker-grant" 'PASS' 'DOCUMENTS application write grant present on client site' "grant:$($grant[0].id)"
    $Result.checks[-1].capability = 'DOCUMENTS'; $Result.checks[-1].resourceId = $id
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "graph-site-permission:$id/$($grant[0].id)"
  }
}

function Invoke-ClientSiteStaffReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $Approve, [scriptblock] $Confirm, [scriptblock] $DirectoryTransport) {
  Assert-ClientSitesPlan $Plan
  $groupTitle = 'AuditSphere Assigned Staff'

  foreach ($site in $Plan.sites) {
    $siteUrl = $site.siteUrl.TrimEnd('/')
    $escapedTitle = [uri]::EscapeDataString('AuditSphere Assigned Staff')
    $groupUrl = "$siteUrl/_api/web/sitegroups/getbyname('$escapedTitle')"
    $group = & $Transport 'GET' $groupUrl $null

    if ($null -eq $group -or -not $group.ContainsKey('Id')) {
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      $createGroupPath = "$siteUrl/_api/web/sitegroups"
      $createGroupBody = @{ Title = 'AuditSphere Assigned Staff'; Description = $site.ownershipMarker }
      if (-not (& $Approve 'POST' $createGroupPath $createGroupBody)) { throw 'BLOCKED_APPROVAL' }
      $Result.mutationsAttempted++
      try {
        $created = & $Transport 'POST' $createGroupPath $createGroupBody
        $group = & $Transport 'GET' $groupUrl $null
      } catch {
        throw 'RESULT_UNCERTAIN'
      }
      & $Confirm $createGroupPath $createGroupBody | Out-Null
      $Result.mutationsConfirmed++
      $Result.changes += @{ target = $siteUrl; action = 'CREATE_STAFF_GROUP'; title = 'AuditSphere Assigned Staff' }
    }

    if ($null -eq $group -or $group.Description -cne $site.ownershipMarker) { throw 'INVALID_OR_UNSAFE' }
    $groupId = [int]$group.Id

    Add-M365Check $Result "site:$siteUrl:staff-group" 'PASS' 'dedicated managed staff group verified with ownership marker' "group-id:$groupId"
    $Result.checks[-1].capability = 'CLIENT_SITES'; $Result.checks[-1].resourceId = $siteUrl
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "sharepoint-group:$siteUrl/$groupId"

    # Full Control role assignment (role type 5)
    $roleDef = & $Transport 'GET' "$siteUrl/_api/web/roledefinitions/getbytype(5)?`$select=Id,RoleTypeKind" $null
    if ($null -eq $roleDef -or [int]$roleDef.RoleTypeKind -ne 5) { throw 'PROVIDER_FAILURE' }
    $roleId = [int]$roleDef.Id

    $roleUrl = "$siteUrl/_api/web/roleassignments/getbyprincipalid($groupId)/roledefinitionbindings"
    $roles = @(& $Transport 'GET' $roleUrl $null)
    $hasFullControl = @($roles | Where-Object { [int]$_.Id -eq $roleId }).Count -gt 0
    if (-not $hasFullControl) {
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      $addRolePath = "$siteUrl/_api/web/roleassignments/addroleassignment(principalid=$groupId,roledefid=$roleId)"
      if (-not (& $Approve 'POST' $addRolePath $null)) { throw 'BLOCKED_APPROVAL' }
      $Result.mutationsAttempted++
      try { & $Transport 'POST' $addRolePath $null | Out-Null } catch { throw 'RESULT_UNCERTAIN' }
      & $Confirm $addRolePath $null | Out-Null
      $Result.mutationsConfirmed++
      $Result.changes += @{ target = $siteUrl; action = 'GRANT_STAFF_GROUP_FULL_CONTROL'; groupId = $groupId; roleId = $roleId }
    }

    Add-M365Check $Result "site:$siteUrl:staff-full-control" 'PASS' 'Full Control role definition bound to managed staff group' "role-def:$roleId"
    $Result.checks[-1].capability = 'CLIENT_SITES'; $Result.checks[-1].resourceId = $siteUrl
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "sharepoint-role-assignment:$siteUrl/$groupId/$roleId"

    # Member reconciliation
    $desiredStaffOids = @()
    if ($site.ContainsKey('staffObjectIds') -and $null -ne $site.staffObjectIds) {
      $desiredStaffOids = @($site.staffObjectIds)
    } elseif ($Plan.ContainsKey('staffAssignments') -and $null -ne $Plan.staffAssignments) {
      $assignment = @($Plan.staffAssignments.assignments | Where-Object { $_.clientId -eq $site.clientId })
      if ($assignment.Count -eq 1) { $desiredStaffOids = @($assignment[0].staffObjectIds) }
    }

    $desiredMap = @{}
    foreach ($oid in $desiredStaffOids) {
      $upn = & $DirectoryTransport 'GET' "/v1.0/users/$oid`?`$select=id,userPrincipalName" $null
      if ($null -ne $upn -and $upn.ContainsKey('userPrincipalName')) {
        $login = 'i:0#.f|membership|' + $upn.userPrincipalName.ToLowerInvariant()
        $desiredMap[$login] = $oid
      }
    }

    $usersUrl = "$siteUrl/_api/web/sitegroups($groupId)/users"
    $currentMembers = @(& $Transport 'GET' $usersUrl $null)

    # Remove obsolete members
    foreach ($member in $currentMembers) {
      $login = $member.LoginName
      if (-not $desiredMap.ContainsKey($login)) {
        if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
        $removePath = "$siteUrl/_api/web/sitegroups($groupId)/users/removebyid($($member.Id))"
        if (-not (& $Approve 'POST' $removePath $null)) { throw 'BLOCKED_APPROVAL' }
        $Result.mutationsAttempted++
        try { & $Transport 'POST' $removePath $null | Out-Null } catch { throw 'RESULT_UNCERTAIN' }
        & $Confirm $removePath $null | Out-Null
        $Result.mutationsConfirmed++
        $Result.changes += @{ target = $siteUrl; action = 'REMOVE_STAFF_MEMBER'; userId = $member.Id; login = $login }
      }
    }

    # Add missing members
    foreach ($login in $desiredMap.Keys) {
      $oid = $desiredMap[$login]
      $exists = @($currentMembers | Where-Object { [string]::Equals($_.LoginName, $login, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
      if (-not $exists) {
        if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
        $ensurePath = "$siteUrl/_api/web/ensureuser"
        $ensureBody = @{ logonName = $login }
        if (-not (& $Approve 'POST' $ensurePath $ensureBody)) { throw 'BLOCKED_APPROVAL' }
        $Result.mutationsAttempted++
        $ensured = $null
        try {
          $ensured = & $Transport 'POST' $ensurePath $ensureBody
        } catch {
          throw 'RESULT_UNCERTAIN'
        }
        & $Confirm $ensurePath $ensureBody | Out-Null
        $Result.mutationsConfirmed++

        # Verify AadObjectId matches
        $userCheck = & $Transport 'GET' "$siteUrl/_api/web/siteusers/getbyid($($ensured.Id))?`$select=Id,LoginName,AadObjectId" $null
        $observedOid = $null
        if ($null -ne $userCheck -and $userCheck.ContainsKey('AadObjectId') -and $null -ne $userCheck.AadObjectId) {
          if ($userCheck.AadObjectId -is [System.Collections.IDictionary] -and $userCheck.AadObjectId.ContainsKey('NameId')) {
            $observedOid = [string]$userCheck.AadObjectId.NameId
          } else {
            $observedOid = [string]$userCheck.AadObjectId
          }
        }
        if ($observedOid -cne $oid) {
          throw 'INVALID_OR_UNSAFE'
        }

        # Add to group
        $addPath = "$siteUrl/_api/web/sitegroups($groupId)/users"
        $addBody = @{ LoginName = $login }
        if (-not (& $Approve 'POST' $addPath $addBody)) { throw 'BLOCKED_APPROVAL' }
        $Result.mutationsAttempted++
        try { & $Transport 'POST' $addPath $addBody | Out-Null } catch { throw 'RESULT_UNCERTAIN' }
        & $Confirm $addPath $addBody | Out-Null
        $Result.mutationsConfirmed++
        $Result.changes += @{ target = $siteUrl; action = 'ADD_STAFF_MEMBER'; oid = $oid; login = $login }
      }
    }

    Add-M365Check $Result "site:$siteUrl:staff-membership" 'PASS' 'managed staff members reconciled exactly with approved manifest' "$($desiredMap.Count) members"
    $Result.checks[-1].capability = 'CLIENT_SITES'; $Result.checks[-1].resourceId = $siteUrl
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "sharepoint-staff-membership:$siteUrl/$groupId"
  }
}

Export-ModuleMember -Function Assert-ClientSitesPlan, Get-ClientSitesJournalPath, Save-ClientSitesMutationIntent,
  Confirm-ClientSitesMutationIntent, Invoke-SharePointClientSitesReconciliation, Invoke-ClientSiteStaffReconciliation
