# Read-only projection of currently exposed AuditSphere M365 readiness evidence.
# This module never interprets aggregate capability status as provider authorization proof.
Set-StrictMode -Version Latest

$script:MissingRuntimeEvidence = @(
  'api-workspace:runtime-client-identity-not-projected',
  'api-workspace:runtime-application-object-id-not-projected',
  'api-workspace:runtime-service-principal-id-not-projected',
  'api-workspace:observed-roles-not-projected',
  'api-workspace:observed-resources-not-projected',
  'api-workspace:evidence-expiry-not-projected',
  'api-workspace:positive-probe-not-projected',
  'api-workspace:negative-probe-not-projected',
  'api-workspace:configuration-digest-not-projected',
  'api-workspace:exact-resume-identity-not-projected',
  'api-workspace:deployment-environment-not-projected'
)

function Get-M365ReadinessValue($Node, [string] $Name) {
  if ($Node -is [System.Collections.IDictionary] -and $Node.Contains($Name)) { return $Node[$Name] }
  return $null
}

function ConvertTo-M365ReadinessGuid([object] $Value) {
  $parsed = [guid]::Empty
  if ($Value -isnot [string] -or -not [guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [guid]::Empty) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  return $parsed.ToString('D')
}

function Get-M365ReadinessTimestamp([object] $Value) {
  $parsed = [DateTimeOffset]::MinValue
  # These output fields are explicitly labelled UTC. Require a source offset instead of
  # interpreting an unspecified timestamp in the operator machine's local timezone.
  if ($Value -is [string] -and $Value -cmatch '(?:Z|[+-][0-9]{2}:[0-9]{2})$' -and
      [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind, [ref] $parsed) -and $parsed -ne [DateTimeOffset]::MinValue) {
    return $parsed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
  }
  return $null
}

function Get-M365ReadinessApiCapabilityKey([string] $PlanKind) {
  # Values mirror Microsoft365Capabilities in Domain/Microsoft365/TenantAdministration.cs;
  # the API CapabilityStatus.Capability field serializes these exact wire values.
  switch -CaseSensitive ($PlanKind) {
    'DIRECTORY_READER' { return 'DIRECTORY_READ' }
    'MAIL' { return 'OUTBOUND_MAIL' }
    'LOGIN' { return 'SIGN_IN' }
    'USER_CREATE' { return 'TENANT_USER_PROVISIONING' }
    'INVITATIONS' { return 'GUEST_INVITATION' }
    'DOCUMENTS' { return 'SELECTED_SITE' }
    'GROUP_MEMBERSHIP' { return 'GROUP_MEMBERSHIP' }
    'CLIENT_SITES' { return $null }
    default { throw 'INCOMPLETE_EVIDENCE' }
  }
}

function Get-M365ReadinessSafeGuid([object] $Value) {
  try { return ConvertTo-M365ReadinessGuid $Value } catch { return $null }
}

function ConvertTo-M365ReadinessGuidList([object] $Value) {
  if ($Value -isnot [System.Collections.IEnumerable] -or $Value -is [string] -or
      $Value -is [System.Collections.IDictionary]) { return @{ IsValid = $false; Values = @() } }
  $normalized = [System.Collections.Generic.List[string]]::new()
  foreach ($item in $Value) {
    $guid = Get-M365ReadinessSafeGuid $item
    if ($null -eq $guid -or $normalized.Contains($guid)) { return @{ IsValid = $false; Values = @() } }
    $normalized.Add($guid)
  }
  if ($normalized.Count -eq 0) { return @{ IsValid = $false; Values = @() } }
  return @{ IsValid = $true; Values = @($normalized.ToArray()) }
}

function Get-M365ReadinessSafeState([object] $Value) {
  if ($Value -is [string] -and $Value.Length -le 64 -and $Value -cmatch '^[A-Z0-9_]*$') { return $Value }
  return $null
}

function Test-M365ReadinessCapabilityReadbackMissing($Plan, $Workspace) {
  $workspaceState = Get-M365ReadinessValue $Workspace 'workspace'
  $apiRows = $null
  if ($workspaceState -is [System.Collections.IDictionary] -and $workspaceState.Contains('capabilities')) {
    $apiRows = $workspaceState['capabilities']
  }
  if ($workspaceState -isnot [System.Collections.IDictionary] -or
      $apiRows -isnot [System.Collections.IEnumerable] -or $apiRows -is [string] -or $apiRows -is [System.Collections.IDictionary]) { return $true }
  foreach ($capability in $Plan.capabilities) {
    $kind = [string] $capability.kind
    $apiCapabilityKey = Get-M365ReadinessApiCapabilityKey $kind
    if ($null -eq $apiCapabilityKey) { return $true }
    $matching = @($apiRows | Where-Object {
      $_ -is [System.Collections.IDictionary] -and
        (Get-M365ReadinessValue $_ 'capability') -is [string] -and
        (Get-M365ReadinessValue $_ 'capability') -ceq $apiCapabilityKey
    })
    if ($matching.Count -ne 1) { return $true }
  }
  return $false
}

function Assert-M365ReadinessSession($Session, $Plan) {
  if ($Session -isnot [System.Collections.IDictionary] -or
      @('userId','firmId','generation','staff' | Where-Object { -not $Session.Contains($_) }).Count -gt 0) {
    throw 'BLOCKED_BOOTSTRAP'
  }
  if ($Session.staff -isnot [bool] -or -not $Session.staff) { throw 'BLOCKED_BOOTSTRAP' }
  if ((ConvertTo-M365ReadinessGuid $Session.firmId) -cne $Plan.firmId) { throw 'DRIFT' }
  ConvertTo-M365ReadinessGuid $Session.userId | Out-Null
  if ($Session.generation -isnot [string] -or $Session.generation -cnotmatch '^[0-9]{1,20}$') { throw 'INCOMPLETE_EVIDENCE' }
}

function Get-M365ReadinessExpectedResources($Plan, $Capability) {
  $applicationResources = @($Capability.expectedApplicationRoles | ForEach-Object { $_.resourceAppId } | Select-Object -Unique)
  $resources = [ordered]@{
    applicationResourceIds = $applicationResources
    siteIds = $null
    clientIds = $null
    host = $null
    senderObjectIds = $null
    controlMailboxObjectId = $null
    permissionModel = $null
    policyScopeGroupId = $null
    scopeName = $null
  }
  switch -CaseSensitive ($Capability.kind) {
    'DOCUMENTS' {
      if ($Plan.ContainsKey('sites')) { $resources.siteIds = @($Plan.sites | ForEach-Object { $_.siteId }) }
    }
    'CLIENT_SITES' {
      if ($Plan.ContainsKey('clientSites')) {
        $resources.host = $Plan.clientSites.host
        if ($Plan.clientSites.ContainsKey('clientIds')) { $resources.clientIds = @($Plan.clientSites.clientIds) }
        elseif ($Plan.ContainsKey('sites')) { $resources.clientIds = @($Plan.sites | ForEach-Object { $_.clientId } | Select-Object -Unique) }
      }
    }
    'MAIL' {
      if ($Plan.ContainsKey('mail')) {
        $resources.senderObjectIds = @($Plan.mail.senderObjectIds)
        $resources.controlMailboxObjectId = $Plan.mail.controlMailboxObjectId
        $resources.permissionModel = $Plan.mail.permissionModel
        if ($Plan.mail.ContainsKey('policyScopeGroupId')) { $resources.policyScopeGroupId = $Plan.mail.policyScopeGroupId }
        if ($Plan.mail.ContainsKey('scopeName')) { $resources.scopeName = $Plan.mail.scopeName }
      }
    }
  }
  return $resources
}

function Get-M365ReadinessMailAuthorizationEvidence($Workspace, $Plan, $Row, $Capability,
    [System.Collections.Generic.List[string]] $Blockers) {
  $plannedModel = if ($Plan.ContainsKey('mail')) { Get-M365ReadinessValue $Plan.mail 'permissionModel' } else { $null }
  $enrollmentAvailable = Get-M365ReadinessValue $Workspace 'mailAuthorizationEnrollmentAvailable'
  if ($plannedModel -cne 'LEGACY_APPLICATION_ACCESS_POLICY' -or $enrollmentAvailable -isnot [bool] -or -not $enrollmentAvailable) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-deployment-binding-mismatch')
    return $null
  }

  $source = Get-M365ReadinessValue $Row 'authorizationEvidence'
  if ($source -isnot [System.Collections.IDictionary]) {
    $Blockers.Add('api-workspace:MAIL:typed-authorization-evidence-not-projected')
    return $null
  }
  $model = Get-M365ReadinessValue $source 'model'
  $receiptId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'receiptId')
  $attestationSha256 = Get-M365ReadinessValue $source 'attestationSha256'
  $bindingSha256 = Get-M365ReadinessValue $source 'bindingSha256'
  $providerSnapshotSha256 = Get-M365ReadinessValue $source 'providerSnapshotSha256'
  $planSha256 = Get-M365ReadinessValue $source 'planSha256'
  $sourceCommit = Get-M365ReadinessValue $source 'sourceCommit'
  $tenantId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'tenantId')
  $runtimeClientId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'runtimeClientId')
  $connectionRevisionId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'connectionRevisionId')
  $consentAttemptId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'consentAttemptId')
  $sourceSenderMailboxObjectIds = $null
  if ($source.Contains('senderMailboxObjectIds')) { $sourceSenderMailboxObjectIds = $source['senderMailboxObjectIds'] }
  $senderMailboxObjectIds = ConvertTo-M365ReadinessGuidList $sourceSenderMailboxObjectIds
  $controlMailboxObjectId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $source 'controlMailboxObjectId')
  $expiresAt = Get-M365ReadinessValue $source 'expiresAtUtc'
  $normalizedExpiry = Get-M365ReadinessTimestamp $expiresAt
  $evidenceObservedAt = Get-M365ReadinessValue $source 'evidenceObservedAtUtc'
  $normalizedEvidenceObservedAt = Get-M365ReadinessTimestamp $evidenceObservedAt
  if ($model -cne $plannedModel) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-model-mismatch')
    return $null
  }
  if ($null -eq $receiptId -or $attestationSha256 -isnot [string] -or $attestationSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $bindingSha256 -isnot [string] -or $bindingSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $providerSnapshotSha256 -isnot [string] -or $providerSnapshotSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $planSha256 -isnot [string] -or $planSha256 -cnotmatch '^[0-9a-f]{64}$' -or
      $sourceCommit -isnot [string] -or $sourceCommit -cnotmatch '^[0-9a-f]{40}$' -or
      $null -eq $tenantId -or $null -eq $runtimeClientId -or $null -eq $connectionRevisionId -or
      $null -eq $consentAttemptId -or -not $senderMailboxObjectIds.IsValid -or
      $null -eq $controlMailboxObjectId -or $null -eq $normalizedExpiry -or $null -eq $normalizedEvidenceObservedAt) {
    $Blockers.Add('api-workspace:MAIL:typed-authorization-evidence-malformed')
    return $null
  }

  $expectedTenantId = Get-M365ReadinessSafeGuid $Plan.tenantId
  $expectedClientId = Get-M365ReadinessSafeGuid $Capability.clientId
  $expectedSenderMailboxObjectIds = ConvertTo-M365ReadinessGuidList $Plan.mail.senderObjectIds
  $expectedControlMailboxObjectId = Get-M365ReadinessSafeGuid $Plan.mail.controlMailboxObjectId
  $expectedPlanSha256 = Get-M365PlanSha256 $Plan
  if ($null -eq $expectedTenantId -or $null -eq $expectedClientId -or
      -not $expectedSenderMailboxObjectIds.IsValid -or $null -eq $expectedControlMailboxObjectId -or
      $tenantId -cne $expectedTenantId -or $runtimeClientId -cne $expectedClientId -or
      $controlMailboxObjectId -cne $expectedControlMailboxObjectId -or
      $planSha256 -cne $expectedPlanSha256 -or $sourceCommit -cne $Plan.repositoryCommit) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-plan-binding-mismatch')
    return $null
  }
  $observedSenders = @($senderMailboxObjectIds.Values | Sort-Object -CaseSensitive)
  $expectedSenders = @($expectedSenderMailboxObjectIds.Values | Sort-Object -CaseSensitive)
  if ($observedSenders.Count -ne $expectedSenders.Count) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-plan-binding-mismatch')
    return $null
  }
  for ($i = 0; $i -lt $expectedSenders.Count; $i++) {
    if ($observedSenders[$i] -cne $expectedSenders[$i]) {
      $Blockers.Add('api-workspace:MAIL:authorization-evidence-plan-binding-mismatch')
      return $null
    }
  }

  if ((Get-M365ReadinessValue $Row 'state') -cne 'VERIFIED' -or
      (Get-M365ReadinessValue $Row 'enabled') -isnot [bool] -or -not (Get-M365ReadinessValue $Row 'enabled') -or
      (Get-M365ReadinessValue $Row 'stale') -isnot [bool] -or (Get-M365ReadinessValue $Row 'stale')) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-current-status-mismatch')
    return $null
  }

  $expiryValue = [DateTimeOffset]::MinValue
  if (-not [DateTimeOffset]::TryParse($expiresAt, [Globalization.CultureInfo]::InvariantCulture,
      [Globalization.DateTimeStyles]::RoundtripKind, [ref] $expiryValue) -or $expiryValue -le [DateTimeOffset]::UtcNow) {
    $Blockers.Add('api-workspace:MAIL:authorization-evidence-expired')
    return $null
  }
  return [ordered]@{
    receiptId = $receiptId
    model = $model
    attestationSha256 = $attestationSha256
    bindingSha256 = $bindingSha256
    expiresAtUtc = $normalizedExpiry
    tenantId = $tenantId
    runtimeClientId = $runtimeClientId
    connectionRevisionId = $connectionRevisionId
    consentAttemptId = $consentAttemptId
    providerSnapshotSha256 = $providerSnapshotSha256
    planSha256 = $planSha256
    sourceCommit = $sourceCommit
    senderMailboxObjectIds = $observedSenders
    controlMailboxObjectId = $controlMailboxObjectId
    evidenceObservedAtUtc = $normalizedEvidenceObservedAt
  }
}

function Add-M365ReadinessCapabilityChecks($Result, $Plan, $Workspace, [string] $State,
    [string] $ErrorCode = $null, [string[]] $AdditionalBlockers = @()) {
  $workspaceState = Get-M365ReadinessValue $Workspace 'workspace'
  $apiRows = $null
  if ($workspaceState -is [System.Collections.IDictionary] -and $workspaceState.Contains('capabilities')) {
    $apiRows = $workspaceState['capabilities']
  }
  if ($apiRows -isnot [System.Collections.IEnumerable] -or $apiRows -is [string] -or $apiRows -is [System.Collections.IDictionary]) { $apiRows = @() }
  $connection = Get-M365ReadinessValue $workspaceState 'connection'
  $draftId = Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $connection 'draftId')
  $draftRevision = Get-M365ReadinessValue $workspaceState 'draftRevision'
  if ($draftRevision -isnot [string] -or $draftRevision -cnotmatch '^[1-9][0-9]{0,19}$') { $draftRevision = $null }
  $connectionRevisionId = $null
  $consentAttemptId = $null
  $operationId = $null

  foreach ($capability in $Plan.capabilities) {
    $kind = [string] $capability.kind
    $apiCapabilityKey = Get-M365ReadinessApiCapabilityKey $kind
    $matching = @()
    if ($null -ne $apiCapabilityKey) {
      $matching = @($apiRows | Where-Object {
        $_ -is [System.Collections.IDictionary] -and
          (Get-M365ReadinessValue $_ 'capability') -is [string] -and
          (Get-M365ReadinessValue $_ 'capability') -ceq $apiCapabilityKey
      })
    }
    $row = if ($matching.Count -eq 1) { $matching[0] } else { $null }
    $apiObservation = $null
    $sourceObservedAt = $null
    $permissionLabel = $null
    $enabled = $null
    $sourceState = $null
    $stale = $null
    $diagnosticCode = $null
    $blockers = [System.Collections.Generic.List[string]]::new()
    foreach ($blocker in $script:MissingRuntimeEvidence) { $blockers.Add($blocker) }
    foreach ($blocker in $AdditionalBlockers) { if ($blocker) { $blockers.Add($blocker) } }

    if ($null -eq $apiCapabilityKey) {
      $blockers.Add('api-client-sites:separate-owner-readiness-readback-required')
      $blockers.Add('api-client-sites:no-capability-matrix-counterpart')
    } elseif ($matching.Count -gt 1) {
      $blockers.Add("api-workspace:${kind}:${apiCapabilityKey}:duplicate-capability-status-rows")
    } elseif ($null -eq $row) {
      $blockers.Add("api-workspace:${kind}:${apiCapabilityKey}:capability-status-row-missing")
    } else {
      $candidatePermission = Get-M365ReadinessValue $row 'permission'
      if ($candidatePermission -is [string] -and $candidatePermission.Length -le 128 -and $candidatePermission -cmatch '^[A-Za-z0-9 ._-]*$') {
        $permissionLabel = $candidatePermission
      }
      $candidateEnabled = Get-M365ReadinessValue $row 'enabled'
      if ($candidateEnabled -is [bool]) { $enabled = $candidateEnabled }
      $candidateState = Get-M365ReadinessValue $row 'state'
      if ($candidateState -is [string] -and $candidateState.Length -le 64 -and $candidateState -cmatch '^[A-Z0-9_]*$') { $sourceState = $candidateState }
      $candidateStale = Get-M365ReadinessValue $row 'stale'
      if ($candidateStale -is [bool]) { $stale = $candidateStale }
      $candidateDiagnostic = Get-M365ReadinessValue $row 'diagnosticCode'
      if ($candidateDiagnostic -is [string] -and $candidateDiagnostic.Length -le 128 -and $candidateDiagnostic -cmatch '^[A-Za-z0-9._-]*$') {
        $diagnosticCode = $candidateDiagnostic
      }
      $sourceObservedAt = Get-M365ReadinessTimestamp (Get-M365ReadinessValue $row 'lastVerifiedAt')
      if ($null -eq $sourceObservedAt) { $blockers.Add("api-workspace:${kind}:capability-owner-observation-time-missing") }
      $apiObservation = [ordered]@{
        capability = $apiCapabilityKey
        permissionLabel = $permissionLabel
        enabled = $enabled
        state = $sourceState
        lastVerifiedAtUtc = $sourceObservedAt
        stale = $stale
        diagnosticCode = $diagnosticCode
      }
      if ($kind -ceq 'MAIL') {
        $authorizationEvidence = Get-M365ReadinessMailAuthorizationEvidence $Workspace $Plan $row $capability $blockers
        if ($null -ne $authorizationEvidence) { $apiObservation.authorizationEvidence = $authorizationEvidence }
      }
    }

    $expectedRoles = @($capability.expectedApplicationRoles | ForEach-Object {
      [ordered]@{ resourceAppId = $_.resourceAppId; value = $_.value }
    })
    $expectedIdentity = [ordered]@{
      tenantId = $Plan.tenantId
      clientId = $capability.clientId
      applicationObjectId = $(if ($capability.ContainsKey('applicationObjectId')) { $capability.applicationObjectId } else { $null })
      servicePrincipalObjectId = $(if ($capability.ContainsKey('servicePrincipalObjectId')) { $capability.servicePrincipalObjectId } else { $null })
    }
    $resumeIdentity = [ordered]@{
      draftId = $draftId
      draftRevision = $draftRevision
      connectionRevisionId = $connectionRevisionId
      consentAttemptId = $consentAttemptId
      operationId = $operationId
    }
    $check = [ordered]@{
      id = "readiness:capability:$($kind.ToLowerInvariant())"
      capability = $kind
      apiCapabilityKey = $apiCapabilityKey
      resourceId = $null
      expected = 'source-owned runtime identity, grant/resource readback, bounded expiry, probes and resume identity'
      observed = if ($kind -ceq 'MAIL' -and $null -ne $apiObservation -and $apiObservation.Contains('authorizationEvidence')) {
        'API exposes persisted mail authorization receipt metadata only; it does not prove live send or delivery or complete runtime readiness'
      } else { 'API capability status is descriptive setup metadata; required runtime evidence is not projected' }
      state = $State
      observedAtUtc = $sourceObservedAt
      expiresAtUtc = $null
      evidenceReference = $null
      errorCode = $ErrorCode
      expectedEnvironment = $Plan.environment
      observedEnvironment = $null
      expectedIdentity = $expectedIdentity
      observedIdentity = $null
      expectedRoles = $expectedRoles
      observedRoles = $null
      expectedResources = (Get-M365ReadinessExpectedResources $Plan $capability)
      observedResources = $null
      apiCapabilityObservation = $apiObservation
      positiveProbe = $null
      negativeProbe = $null
      configurationDigest = $null
      resumeIdentity = $resumeIdentity
      sourceOwnedBlockers = @($blockers | Select-Object -Unique)
    }
    $Result.checks = @($Result.checks) + @($check)
    if ($State -ne 'PLANNED') {
      foreach ($blocker in $blockers) { $Result.blockedPrerequisites += $blocker }
    }
  }
}

function Add-M365ReadinessSessionCheck($Result, $Plan, $Before, $After, [string] $State, [string] $ErrorCode = $null) {
  $beforeIdentity = $null
  $afterIdentity = $null
  foreach ($pair in @(@{ Source = $Before; Name = 'before' }, @{ Source = $After; Name = 'after' })) {
    $source = $pair.Source
    if ($source -is [System.Collections.IDictionary] -and
        (Get-M365ReadinessValue $source 'userId') -is [string] -and
        (Get-M365ReadinessValue $source 'firmId') -is [string] -and
        (Get-M365ReadinessValue $source 'generation') -is [string] -and
        (Get-M365ReadinessValue $source 'staff') -is [bool]) {
      $identity = [ordered]@{
        userId = Get-M365ReadinessValue $source 'userId'
        firmId = Get-M365ReadinessValue $source 'firmId'
        generation = Get-M365ReadinessValue $source 'generation'
        staff = Get-M365ReadinessValue $source 'staff'
      }
      if ($pair.Name -eq 'before') { $beforeIdentity = $identity } else { $afterIdentity = $identity }
    }
  }
  $check = [ordered]@{
    id = 'readiness:operator-session-binding'; capability = $null; resourceId = $null
    expected = "stable staff session in plan firm $($Plan.firmId)"
    observed = if ($null -eq $beforeIdentity -or $null -eq $afterIdentity) { 'session binding unavailable' } else { 'before and after session identities captured' }
    state = $State; observedAtUtc = $null; expiresAtUtc = $null; evidenceReference = $null; errorCode = $ErrorCode
    observedSessionIdentity = [ordered]@{ before = $beforeIdentity; after = $afterIdentity }
  }
  $Result.checks = @($Result.checks) + @($check)
  if ($null -ne $beforeIdentity -and $null -ne $afterIdentity -and $State -eq 'PASS') {
    $Result.sessionBinding = $check.observedSessionIdentity
  }
}

function Add-M365ReadinessWorkspaceCheck($Result, $Plan, $Workspace, [string] $State, [string] $ErrorCode = $null) {
  $workspaceState = Get-M365ReadinessValue $Workspace 'workspace'
  $connection = Get-M365ReadinessValue $workspaceState 'connection'
  $configuredTenantId = Get-M365ReadinessValue $Workspace 'configuredTenantId'
  $expectedTenantId = Get-M365ReadinessValue $connection 'expectedTenantId'
  $metadata = [ordered]@{
    configuredTenantId = (Get-M365ReadinessSafeGuid $configuredTenantId)
    expectedTenantId = (Get-M365ReadinessSafeGuid $expectedTenantId)
    draftId = (Get-M365ReadinessSafeGuid (Get-M365ReadinessValue $connection 'draftId'))
    draftRevision = $(if ((Get-M365ReadinessValue $workspaceState 'draftRevision') -is [string] -and
      (Get-M365ReadinessValue $workspaceState 'draftRevision') -cmatch '^[1-9][0-9]{0,19}$') { Get-M365ReadinessValue $workspaceState 'draftRevision' } else { $null })
    connectionState = (Get-M365ReadinessSafeState (Get-M365ReadinessValue $connection 'connectionState'))
    consentState = (Get-M365ReadinessSafeState (Get-M365ReadinessValue $connection 'consentState'))
    lastAttemptState = (Get-M365ReadinessSafeState (Get-M365ReadinessValue $connection 'lastAttemptState'))
    directoryCapabilityState = (Get-M365ReadinessSafeState (Get-M365ReadinessValue $connection 'directoryCapabilityState'))
    lastAttemptAtUtc = (Get-M365ReadinessTimestamp (Get-M365ReadinessValue $connection 'lastAttemptAt'))
    directoryLastCheckedAtUtc = (Get-M365ReadinessTimestamp (Get-M365ReadinessValue $connection 'directoryLastCheckedAt'))
    simulation = $(if ((Get-M365ReadinessValue $Workspace 'simulation') -is [bool]) { Get-M365ReadinessValue $Workspace 'simulation' } else { $null })
  }
  $sourceBlockers = [System.Collections.Generic.List[string]]::new()
  foreach ($blocker in @(
      'api-workspace:connection-revision-id-not-projected',
      'api-workspace:consent-attempt-id-not-projected',
      'api-workspace:capability-verification-receipt-id-not-projected',
      'api-workspace:configuration-digest-not-projected')) { $sourceBlockers.Add($blocker) }
  if ($workspaceState -isnot [System.Collections.IDictionary]) { $sourceBlockers.Add('api-workspace:workspace-record-missing') }
  if ($null -eq (Get-M365ReadinessValue $workspaceState 'capabilities')) { $sourceBlockers.Add('api-workspace:capability-status-list-missing') }
  if ($null -eq $configuredTenantId) { $sourceBlockers.Add('api-workspace:configured-tenant-id-not-projected') }
  if ($null -eq $expectedTenantId) { $sourceBlockers.Add('api-workspace:connection-expected-tenant-not-projected') }
  $sourceBlockers.Add('api-workspace:deployment-environment-not-projected')
  if ($metadata.simulation -eq $true) {
    $sourceBlockers.Add('api-workspace:simulation-mode-is-not-runtime-evidence')
  } elseif ($null -eq $metadata.simulation) {
    $sourceBlockers.Add('api-workspace:simulation-state-not-projected')
  }
  $check = [ordered]@{
    id = 'readiness:api-workspace'; capability = $null; resourceId = $null
    expected = "read-only current M365 workspace for tenant $($Plan.tenantId)"
    observed = 'workspace metadata read; it does not contain complete runtime capability evidence'
    state = $State; observedAtUtc = $null; expiresAtUtc = $null; evidenceReference = '/api/ui/administration/microsoft365'; errorCode = $ErrorCode
    expectedEnvironment = $Plan.environment
    observedEnvironment = $null
    apiWorkspaceMetadata = $metadata
    sourceOwnedBlockers = @($sourceBlockers | Select-Object -Unique)
  }
  $Result.checks = @($Result.checks) + @($check)
  if ($State -ne 'PLANNED') { $Result.blockedPrerequisites += $check.sourceOwnedBlockers }
}

function Invoke-M365ReadinessReadback($Plan, $Result, [scriptblock] $Transport) {
  $before = $null; $workspace = $null; $after = $null
  try {
    $before = & $Transport '/api/ui/session'
    Assert-M365ReadinessSession $before $Plan
    $workspace = & $Transport '/api/ui/administration/microsoft365'
    $after = & $Transport '/api/ui/session'
    Assert-M365ReadinessSession $after $Plan
    foreach ($field in @('userId','firmId','generation')) {
      if ($before[$field] -cne $after[$field]) { throw 'DRIFT' }
    }
    $workspaceState = Get-M365ReadinessValue $workspace 'workspace'
    $connection = Get-M365ReadinessValue $workspaceState 'connection'
    foreach ($binding in @(
        @{ Value = Get-M365ReadinessValue $workspace 'configuredTenantId' },
        @{ Value = Get-M365ReadinessValue $connection 'expectedTenantId' })) {
      if ($null -eq $binding.Value) { continue }
      if ((ConvertTo-M365ReadinessGuid $binding.Value) -cne $Plan.tenantId) { throw 'DRIFT' }
    }
    Add-M365ReadinessSessionCheck $Result $Plan $before $after 'PASS'
    $missingReadback = Test-M365ReadinessCapabilityReadbackMissing $Plan $workspace
    $evidenceError = if ($missingReadback) { 'readiness-readback-missing' } else { 'readiness-runtime-evidence-not-projected' }
    Add-M365ReadinessWorkspaceCheck $Result $Plan $workspace 'INCOMPLETE_EVIDENCE' $evidenceError
    Add-M365ReadinessCapabilityChecks $Result $Plan $workspace 'INCOMPLETE_EVIDENCE' $evidenceError
    $Result.errorCode = $evidenceError
    if ($missingReadback) { $Result.blockedPrerequisites += 'api-workspace:planned-capability-readback-missing' }
    else { $Result.blockedPrerequisites += 'engineering:api-runtime-capability-evidence-projection-required' }
    return 'INCOMPLETE_EVIDENCE'
  } catch {
    $message = $_.Exception.Message
    $status = if ($message -cin @('BLOCKED_BOOTSTRAP','DRIFT','INCOMPLETE_EVIDENCE','PROVIDER_FAILURE','PREREQUISITE_MISSING','INVALID_OR_UNSAFE')) {
      $message
    } elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INCOMPLETE_EVIDENCE' } else { 'PROVIDER_FAILURE' }
    $errorCode = switch -CaseSensitive ($status) {
      'BLOCKED_BOOTSTRAP' { 'readiness-bootstrap-required' }
      'DRIFT' { 'readiness-session-or-tenant-drift' }
      'PROVIDER_FAILURE' { 'readiness-readback-store-inaccessible' }
      'INCOMPLETE_EVIDENCE' { 'readiness-readback-missing-or-malformed' }
      'PREREQUISITE_MISSING' { 'readiness-readback-prerequisite-missing' }
      default { 'readiness-readback-invalid' }
    }
    Add-M365ReadinessSessionCheck $Result $Plan $before $after $status $errorCode
    $trustedWorkspace = if ($status -eq 'DRIFT') { $null } else { $workspace }
    Add-M365ReadinessWorkspaceCheck $Result $Plan $trustedWorkspace $status $errorCode
    Add-M365ReadinessCapabilityChecks $Result $Plan $trustedWorkspace $status $errorCode @("api-readback:$($status.ToLowerInvariant())")
    $Result.errorCode = $errorCode
    $Result.blockedPrerequisites += "api-readback:$($status.ToLowerInvariant())"
    return $status
  }
}

function Add-M365ReadinessPlannedChecks($Result, $Plan) {
  Add-M365ReadinessWorkspaceCheck $Result $Plan $null 'PLANNED'
  Add-M365ReadinessCapabilityChecks $Result $Plan $null 'PLANNED' $null @('offline-plan:no-provider-readback-performed')
}

Export-ModuleMember -Function Invoke-M365ReadinessReadback, Add-M365ReadinessPlannedChecks,
  Add-M365ReadinessCapabilityChecks, Add-M365ReadinessSessionCheck, Add-M365ReadinessWorkspaceCheck
