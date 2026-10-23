# Read-only verifier for the persisted Archive Copy evidence contract.
# It never copies data or performs provider mutations. Historical API readback is not fresh custody proof.
Set-StrictMode -Version Latest
$script:ArchiveCopyMaximumJsonBytes = 4194304

function Assert-ArchiveCopyObject($Value, [string[]] $Required, [switch] $Exact) {
  if ($Value -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($requiredField in $Required) {
    if (@($Value.Keys | Where-Object { [string]::Equals([string] $_, $requiredField, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
      throw 'INCOMPLETE_EVIDENCE'
    }
  }
  if ($Exact -and @($Value.Keys | Where-Object { $_ -cnotin $Required }).Count -gt 0) { throw 'INVALID_OR_UNSAFE' }
}

function Get-ArchiveCopyField($Value, [string] $Name) {
  foreach ($key in $Value.Keys) {
    if ([string]::Equals([string] $key, $Name, [StringComparison]::OrdinalIgnoreCase)) { return $Value[$key] }
  }
  throw 'INCOMPLETE_EVIDENCE'
}

function ConvertTo-ArchiveCopyGuid([object] $Value) {
  $parsed = [guid]::Empty
  if ($Value -isnot [string] -or -not [guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [guid]::Empty) {
    throw 'INVALID_OR_UNSAFE'
  }
  return $parsed.ToString('D')
}

function Assert-ArchiveCopyDigest([object] $Value) {
  if ($Value -isnot [string] -or $Value -cnotmatch '^[0-9a-f]{64}$') { throw 'INVALID_OR_UNSAFE' }
}

function Assert-ArchiveCopyText([object] $Value, [int] $Maximum, [switch] $AllowEmpty) {
  if ($Value -isnot [string] -or $Value.Length -gt $Maximum -or
      (-not $AllowEmpty -and [string]::IsNullOrWhiteSpace($Value)) -or
      $Value.IndexOfAny([char[]] "`r`n`t") -ge 0) { throw 'INVALID_OR_UNSAFE' }
}

function Assert-ArchiveCopyJsonUniqueProperties($Element, [string] $FailureCode) {
  if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($property in $Element.EnumerateObject()) {
      if (-not $names.Add($property.Name)) { throw $FailureCode }
      Assert-ArchiveCopyJsonUniqueProperties $property.Value $FailureCode
    }
  } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
    foreach ($item in $Element.EnumerateArray()) { Assert-ArchiveCopyJsonUniqueProperties $item $FailureCode }
  }
}

function ConvertFrom-ArchiveCopyJson([string] $Json, [int] $MaximumBytes, [string] $FailureCode) {
  if ($null -eq $Json -or [System.Text.Encoding]::UTF8.GetByteCount($Json) -gt $MaximumBytes) { throw $FailureCode }
  $document = $null
  try {
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 32
    $document = [System.Text.Json.JsonDocument]::Parse($Json, $options)
    Assert-ArchiveCopyJsonUniqueProperties $document.RootElement $FailureCode
  } catch { throw $FailureCode } finally { if ($null -ne $document) { $document.Dispose() } }

  $parse = @{ AsHashtable = $true; Depth = 32; NoEnumerate = $true }
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
  try { return ConvertFrom-Json -InputObject $Json @parse } catch { throw $FailureCode }
}

function Read-ArchiveCopyUtf8Stream($Stream, [int] $MaximumBytes, [string] $FailureCode) {
  $memory = [System.IO.MemoryStream]::new()
  $buffer = [byte[]]::new(8192)
  try {
    while (($read = $Stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
      if ($memory.Length + $read -gt $MaximumBytes) { throw $FailureCode }
      $memory.Write($buffer, 0, $read)
    }
    if ($memory.Length -eq 0) { throw $FailureCode }
    try { return [System.Text.UTF8Encoding]::new($false, $true).GetString($memory.ToArray()) }
    catch { throw $FailureCode }
  } finally { $memory.Dispose() }
}

function Read-M365ArchiveCopyJsonResponse([System.Net.Http.HttpContent] $Content, [int] $MaximumBytes) {
  if ($null -eq $Content) { throw 'INCOMPLETE_EVIDENCE' }
  if ($null -ne $Content.Headers.ContentLength -and $Content.Headers.ContentLength -gt $MaximumBytes) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $body = $Content.ReadAsStreamAsync().GetAwaiter().GetResult()
  try {
    $raw = Read-ArchiveCopyUtf8Stream $body $MaximumBytes 'INCOMPLETE_EVIDENCE'
    return ConvertFrom-ArchiveCopyJson $raw $MaximumBytes 'INCOMPLETE_EVIDENCE'
  } finally { $body.Dispose() }
}

function Assert-ArchiveCopyTimestamp([object] $Value) {
  if ($Value -isnot [string] -or $Value -cnotmatch '(?:Z|[+-](?:0\d|1\d|2[0-3]):[0-5]\d)$') { throw 'DRIFT' }
  $timestamp = [DateTimeOffset]::MinValue
  if (-not [DateTimeOffset]::TryParse($Value, [System.Globalization.CultureInfo]::InvariantCulture,
      [System.Globalization.DateTimeStyles]::None, [ref] $timestamp) -or $timestamp -eq [DateTimeOffset]::MinValue) {
    throw 'DRIFT'
  }
  return $timestamp
}

function Assert-ArchiveCopyPositiveInteger([object] $Value, [switch] $AllowZero) {
  if ($Value -isnot [byte] -and $Value -isnot [int16] -and $Value -isnot [int32] -and $Value -isnot [int64] -and
      $Value -isnot [uint16] -and $Value -isnot [uint32] -and $Value -isnot [uint64]) { throw 'INVALID_OR_UNSAFE' }
  $number = [long] $Value
  if (($AllowZero -and $number -lt 0) -or (-not $AllowZero -and $number -lt 1)) { throw 'INVALID_OR_UNSAFE' }
  return $number
}

function Read-M365ArchiveCopyOperationPlan([string] $Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'INVALID_OR_UNSAFE' }
  $stream = $null
  try {
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $raw = Read-ArchiveCopyUtf8Stream $stream $script:ArchiveCopyMaximumJsonBytes 'INVALID_OR_UNSAFE'
    $plan = ConvertFrom-ArchiveCopyJson $raw $script:ArchiveCopyMaximumJsonBytes 'INVALID_OR_UNSAFE'
  } catch { throw 'INVALID_OR_UNSAFE' }
  finally { if ($null -ne $stream) { $stream.Dispose() } }

  $secret = Find-M365SecretProperty $plan
  if ($secret) { throw 'INVALID_OR_UNSAFE' }
  $fields = @(
    'schemaVersion','operationId','firmId','tenantId','clientId','engagementId','archiveId',
    'manifestId','manifestVersion','manifestSha256','recordsProfileId','recordsProfileVersion',
    'releaseId','releasedArtifactId','checkpointId','clientSharePointSiteId','targetRevisionId','targetRevision',
    'targetConfigurationSha256','authorizedStaffSha256','destinationTenantId','destinationSiteId',
    'destinationDriveId','destinationLibraryRootItemId','staffGroupId','expectedPermissionsSha256','files'
  )
  Assert-ArchiveCopyObject $plan $fields -Exact
  if (($plan.schemaVersion -isnot [int] -and $plan.schemaVersion -isnot [long]) -or [long] $plan.schemaVersion -ne 1) {
    throw 'INVALID_OR_UNSAFE'
  }
  foreach ($field in @('operationId','firmId','tenantId','clientId','engagementId','archiveId','manifestId',
      'recordsProfileId','releaseId','releasedArtifactId','checkpointId','targetRevisionId')) {
    $plan[$field] = ConvertTo-ArchiveCopyGuid $plan[$field]
  }
  if ($null -ne $plan.clientSharePointSiteId) { $plan.clientSharePointSiteId = ConvertTo-ArchiveCopyGuid $plan.clientSharePointSiteId }
  foreach ($field in @('manifestSha256','targetConfigurationSha256','authorizedStaffSha256','expectedPermissionsSha256')) {
    Assert-ArchiveCopyDigest $plan[$field]
  }
  $plan.manifestVersion = Assert-ArchiveCopyPositiveInteger $plan.manifestVersion
  $plan.recordsProfileVersion = Assert-ArchiveCopyPositiveInteger $plan.recordsProfileVersion
  $plan.targetRevision = Assert-ArchiveCopyPositiveInteger $plan.targetRevision
  foreach ($field in @('destinationTenantId','destinationSiteId','destinationDriveId','destinationLibraryRootItemId')) {
    Assert-ArchiveCopyText $plan[$field] 2000
  }
  if ($plan.destinationTenantId -cne $plan.tenantId -or $plan.staffGroupId -isnot [string] -or
      $plan.staffGroupId -cnotmatch '^[1-9][0-9]{0,9}$') { throw 'INVALID_OR_UNSAFE' }
  if ($plan.files -isnot [System.Collections.IList] -or $plan.files.Count -lt 2 -or $plan.files.Count -gt 1000) {
    throw 'INVALID_OR_UNSAFE'
  }

  $fileFields = @('sourceKind','sourceKeySha256','sourceId','manifestEntryId','sourceVersion','sourceSha256','sourceByteCount','relativePath')
  $seenKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  $normalizedFiles = [System.Collections.Generic.List[object]]::new()
  foreach ($file in $plan.files) {
    Assert-ArchiveCopyObject $file $fileFields -Exact
    if ($file.sourceKind -cnotin @('CHECKPOINT_MANIFEST','RELEASED_ARTIFACT','REVIEWED_MANIFEST_ENTRY')) {
      throw 'INVALID_OR_UNSAFE'
    }
    Assert-ArchiveCopyDigest $file.sourceKeySha256
    Assert-ArchiveCopyDigest $file.sourceSha256
    Assert-ArchiveCopyText $file.sourceVersion 500
    Assert-ArchiveCopyText $file.relativePath 2000
    $file.sourceByteCount = Assert-ArchiveCopyPositiveInteger $file.sourceByteCount -AllowZero
    if (-not $seenKeys.Add($file.sourceKeySha256)) { throw 'INVALID_OR_UNSAFE' }
    foreach ($field in @('sourceId','manifestEntryId')) {
      if ($null -ne $file[$field]) { $file[$field] = ConvertTo-ArchiveCopyGuid $file[$field] }
    }
    if (($file.sourceKind -eq 'CHECKPOINT_MANIFEST' -and
          ($file.sourceId -cne $plan.checkpointId -or $null -ne $file.manifestEntryId)) -or
        ($file.sourceKind -eq 'RELEASED_ARTIFACT' -and
          ($file.sourceId -cne $plan.releasedArtifactId -or $null -ne $file.manifestEntryId)) -or
        ($file.sourceKind -eq 'REVIEWED_MANIFEST_ENTRY' -and $null -eq $file.manifestEntryId)) {
      throw 'INVALID_OR_UNSAFE'
    }
    $normalizedFiles.Add($file)
  }
  if (@($plan.files | Where-Object sourceKind -CEQ 'CHECKPOINT_MANIFEST').Count -ne 1 -or
      @($plan.files | Where-Object sourceKind -CEQ 'RELEASED_ARTIFACT').Count -ne 1) {
    throw 'INVALID_OR_UNSAFE'
  }
  $plan.files = @($normalizedFiles.ToArray())
  return $plan
}

function New-M365ArchiveCopyTransport([System.Net.Http.HttpClient] $Client, [uri] $BaseUri,
    [string] $Cookie, $ProviderCommand, [string] $OperationId) {
  if ($BaseUri.Scheme -cne 'https' -or $BaseUri.UserInfo -or $BaseUri.Query -or $BaseUri.Fragment -or
      $BaseUri.AbsolutePath -cne '/' -or (ConvertTo-ArchiveCopyGuid $OperationId) -cne $OperationId) {
    throw 'INVALID_OR_UNSAFE'
  }
  $evidencePath = "/api/ui/records/archive-copies/$OperationId/evidence"
  $maximumJsonBytes = $script:ArchiveCopyMaximumJsonBytes
  return {
    param([string] $Path)
    if ($Path -cnotin @('/api/ui/session', $evidencePath)) { throw 'INVALID_OR_UNSAFE' }
    $target = [uri]::new($BaseUri, $Path)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $target)
    try {
      $request.Headers.Add('Cookie', $Cookie)
      $request.Headers.Accept.ParseAdd('application/json')
      $httpClient = $Client; $outboundRequest = $request
      $dispatch = { $httpClient.SendAsync($outboundRequest).GetAwaiter().GetResult() }.GetNewClosure()
      $response = & $ProviderCommand $dispatch
      try {
        if ($null -eq $response.RequestMessage -or $null -eq $response.RequestMessage.RequestUri -or
            $response.RequestMessage.RequestUri.AbsoluteUri -cne $target.AbsoluteUri) { throw 'INVALID_OR_UNSAFE' }
        $code = [int] $response.StatusCode
        if ($code -in @(301,302,303,307,308,401,403)) { throw 'BLOCKED_BOOTSTRAP' }
        if ($code -eq 404) { throw 'INCOMPLETE_EVIDENCE' }
        if ($code -ne 200) { throw 'PROVIDER_FAILURE' }
        if ($null -eq $response.Content.Headers.ContentType -or $response.Content.Headers.ContentType.MediaType -cne 'application/json') {
          throw 'INCOMPLETE_EVIDENCE'
        }
        return Read-M365ArchiveCopyJsonResponse $response.Content $maximumJsonBytes
      } finally { $response.Dispose() }
    } finally { $request.Dispose() }
  }.GetNewClosure()
}

function Assert-M365ArchiveCopySession($Session, $Plan) {
  Assert-ArchiveCopyObject $Session @('userId','firmId','generation','staff')
  if ($Session.staff -isnot [bool] -or -not $Session.staff) { throw 'BLOCKED_BOOTSTRAP' }
  $expectedFirmId = ConvertTo-ArchiveCopyGuid $Plan.firmId
  $Session.firmId = ConvertTo-ArchiveCopyGuid $Session.firmId
  if ($Session.firmId -cne $expectedFirmId) { throw 'DRIFT' }
  ConvertTo-ArchiveCopyGuid $Session.userId | Out-Null
  if ($Session.generation -isnot [string] -or $Session.generation -cnotmatch '^[0-9]{1,20}$') { throw 'INCOMPLETE_EVIDENCE' }
}

function ConvertFrom-ArchiveCopyPermissionSet([object] $Json, [string] $StaffGroupId) {
  if ($Json -isnot [string] -or $Json.Length -gt 8192) { throw 'DRIFT' }
  $rows = ConvertFrom-ArchiveCopyJson $Json 8192 'DRIFT'
  if ($rows -isnot [System.Collections.IList] -or $rows.Count -ne 1) { throw 'DRIFT' }
  $grant = $rows[0]
  Assert-ArchiveCopyObject $grant @('PrincipalKind','PrincipalId','Role') -Exact
  $principalKind = Get-ArchiveCopyField $grant 'principalKind'
  $principalId = Get-ArchiveCopyField $grant 'principalId'
  $permissionRole = Get-ArchiveCopyField $grant 'role'
  if ($principalKind -isnot [string] -or $principalId -isnot [string] -or $permissionRole -isnot [string]) { throw 'DRIFT' }
  $kind = $principalKind.Trim().ToUpperInvariant()
  $principal = $principalId.Trim()
  $role = $permissionRole.Trim().ToUpperInvariant()
  if ($kind -cne 'SHAREPOINT_GROUP' -or $principal -cne $StaffGroupId -or $role -cne 'READ') { throw 'DRIFT' }
  $canonicalJson = '[{"PrincipalKind":"SHAREPOINT_GROUP","PrincipalId":"' + $StaffGroupId + '","Role":"READ"}]'
  if ($Json -cne $canonicalJson) { throw 'DRIFT' }
  $digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($canonicalJson))).ToLowerInvariant()
  return @{ Grant = "$kind|$principal|$role"; Sha256 = $digest }
}

function Assert-M365ArchiveCopyReadback($Readback, $OperationPlan, [string] $OperationId, $Session) {
  Assert-ArchiveCopyObject $Readback @('operationId','firmId','clientId','engagementId','operationStatus','operationResultDigest','result','fileReceipts','permissionObservations')
  if ((ConvertTo-ArchiveCopyGuid $Readback.operationId) -cne $OperationId -or
      $OperationPlan.operationId -cne $OperationId) { throw 'DRIFT' }
  foreach ($field in @('firmId','clientId','engagementId')) { $Readback[$field] = ConvertTo-ArchiveCopyGuid $Readback[$field] }
  if ($Readback.firmId -cne $OperationPlan.firmId -or $Readback.firmId -cne $Session.firmId -or
      $Readback.clientId -cne $OperationPlan.clientId -or $Readback.engagementId -cne $OperationPlan.engagementId) {
    throw 'DRIFT'
  }

  $missing = [System.Collections.Generic.List[string]]::new()
  if ($Readback.operationStatus -cne 'COMPLETED') { $missing.Add('api:operation-not-completed') }
  if ($null -eq $Readback.result) { $missing.Add('api:operation-result-missing') }
  if ($Readback.fileReceipts -isnot [System.Collections.IList] -or $Readback.fileReceipts.Count -eq 0) {
    $missing.Add('api:file-receipts-missing')
  }
  if ($Readback.permissionObservations -isnot [System.Collections.IList] -or $Readback.permissionObservations.Count -eq 0) {
    $missing.Add('api:permission-observation-missing')
  }
  if ($null -ne $Readback.operationResultDigest) { Assert-ArchiveCopyDigest $Readback.operationResultDigest }
  if ($missing.Count -gt 0) { return @{ Complete = $false; Blockers = @($missing.ToArray()); FileCount = 0; AttemptToken = $null } }
  if ($null -eq $Readback.operationResultDigest) { return @{ Complete = $false; Blockers = @('api:operation-result-digest-missing'); FileCount = 0; AttemptToken = $null } }

  $result = $Readback.result
  $resultFields = @(
    'operationId','attemptToken','archiveId','clientId','manifestId','manifestVersion','manifestSha256',
    'recordsProfileId','recordsProfileVersion','releaseId','checkpointId','clientSharePointSiteId',
    'targetRevisionId','targetRevision','targetConfigurationSha256','authorizedStaffSha256','protocolOutcome','files','permissions'
  )
  Assert-ArchiveCopyObject $result $resultFields
  foreach ($field in @('operationId','archiveId','clientId','manifestId','recordsProfileId','releaseId','checkpointId','targetRevisionId')) {
    $result[$field] = ConvertTo-ArchiveCopyGuid $result[$field]
  }
  if ($null -ne $result.clientSharePointSiteId) { $result.clientSharePointSiteId = ConvertTo-ArchiveCopyGuid $result.clientSharePointSiteId }
  if ($result.operationId -cne $OperationId -or $result.archiveId -cne $OperationPlan.archiveId -or
      $result.clientId -cne $OperationPlan.clientId -or $result.manifestId -cne $OperationPlan.manifestId -or
      $result.recordsProfileId -cne $OperationPlan.recordsProfileId -or $result.releaseId -cne $OperationPlan.releaseId -or
      $result.checkpointId -cne $OperationPlan.checkpointId -or
      $result.clientSharePointSiteId -cne $OperationPlan.clientSharePointSiteId -or
      $result.targetRevisionId -cne $OperationPlan.targetRevisionId) {
    throw 'DRIFT'
  }
  if ((Assert-ArchiveCopyPositiveInteger $result.attemptToken) -lt 1 -or
      (Assert-ArchiveCopyPositiveInteger $result.manifestVersion) -ne $OperationPlan.manifestVersion -or
      (Assert-ArchiveCopyPositiveInteger $result.recordsProfileVersion) -ne $OperationPlan.recordsProfileVersion -or
      (Assert-ArchiveCopyPositiveInteger $result.targetRevision) -ne $OperationPlan.targetRevision) { throw 'DRIFT' }
  foreach ($field in @('manifestSha256','targetConfigurationSha256','authorizedStaffSha256')) { Assert-ArchiveCopyDigest $result[$field] }
  if ($result.manifestSha256 -cne $OperationPlan.manifestSha256 -or
      $result.targetConfigurationSha256 -cne $OperationPlan.targetConfigurationSha256 -or
      $result.authorizedStaffSha256 -cne $OperationPlan.authorizedStaffSha256) { throw 'DRIFT' }
  if ($result.protocolOutcome -cne 'MATCH' -or $result.files -isnot [System.Collections.IList] -or
      $result.files.Count -ne $OperationPlan.files.Count) { throw 'DRIFT' }

  $expectedByKey = @{}
  foreach ($expected in $OperationPlan.files) { $expectedByKey[$expected.sourceKeySha256] = $expected }
  $resultByKey = @{}
  foreach ($file in $result.files) {
    Assert-ArchiveCopyObject $file @(
      'sourceKind','sourceKeySha256','sourceId','manifestEntryId','sourceVersion','sourceSha256','sourceByteCount',
      'relativePath','destinationTenantId','destinationSiteId','destinationDriveId','destinationLibraryRootItemId',
      'hasExactTargetBinding','destinationItemId','destinationVersion','readBackSha256','readBackByteCount','verificationOutcome','readBackAt'
    )
    Assert-ArchiveCopyDigest $file.sourceKeySha256
    if (-not $expectedByKey.ContainsKey($file.sourceKeySha256) -or $resultByKey.ContainsKey($file.sourceKeySha256)) { throw 'DRIFT' }
    $expected = $expectedByKey[$file.sourceKeySha256]
    foreach ($field in @('sourceId','manifestEntryId')) {
      if ($null -ne $file[$field]) { $file[$field] = ConvertTo-ArchiveCopyGuid $file[$field] }
    }
    foreach ($field in @('sourceSha256','readBackSha256')) { Assert-ArchiveCopyDigest $file[$field] }
    if ($file.sourceKind -cne $expected.sourceKind -or $file.sourceId -cne $expected.sourceId -or
        $file.manifestEntryId -cne $expected.manifestEntryId -or $file.sourceVersion -cne $expected.sourceVersion -or
        $file.sourceSha256 -cne $expected.sourceSha256 -or
        (Assert-ArchiveCopyPositiveInteger $file.sourceByteCount -AllowZero) -ne $expected.sourceByteCount -or
        $file.relativePath -cne $expected.relativePath -or
        $file.destinationTenantId -cne $OperationPlan.destinationTenantId -or
        $file.destinationSiteId -cne $OperationPlan.destinationSiteId -or
        $file.destinationDriveId -cne $OperationPlan.destinationDriveId -or
        $file.destinationLibraryRootItemId -cne $OperationPlan.destinationLibraryRootItemId -or
        $file.hasExactTargetBinding -isnot [bool] -or -not $file.hasExactTargetBinding -or
        [string]::IsNullOrWhiteSpace([string] $file.destinationItemId) -or
        [string]::IsNullOrWhiteSpace([string] $file.destinationVersion) -or
        $file.readBackSha256 -cne $file.sourceSha256 -or
        (Assert-ArchiveCopyPositiveInteger $file.readBackByteCount -AllowZero) -ne $expected.sourceByteCount -or
        $file.verificationOutcome -cne 'MATCH') { throw 'DRIFT' }
    $fileReadAt = Assert-ArchiveCopyTimestamp $file.readBackAt
    $resultByKey[$file.sourceKeySha256] = $file
  }

  $attempt = [long] $result.attemptToken
  $receipts = @($Readback.fileReceipts | Where-Object { [long] $_.attemptToken -eq $attempt })
  if ($receipts.Count -ne $result.files.Count) { throw 'DRIFT' }
  if (@($Readback.fileReceipts | Where-Object { [long] $_.attemptToken -gt $attempt }).Count -gt 0) { throw 'DRIFT' }
  $receiptKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($receipt in $receipts) {
    Assert-ArchiveCopyObject $receipt @(
      'receiptId','attemptToken','sourceKind','sourceKeySha256','sourceId','manifestEntryId','sourceVersion','sourceSha256',
      'sourceByteCount','relativePath','destinationTenantId','destinationSiteId','destinationDriveId','destinationLibraryRootItemId',
      'hasExactTargetBinding','destinationItemId','destinationVersion','readBackSha256','readBackByteCount','verificationOutcome','readBackAt'
    )
    ConvertTo-ArchiveCopyGuid $receipt.receiptId | Out-Null
    if ([long] $receipt.attemptToken -ne $attempt -or -not $receiptKeys.Add([string] $receipt.sourceKeySha256) -or
        -not $resultByKey.ContainsKey([string] $receipt.sourceKeySha256)) { throw 'DRIFT' }
    $file = $resultByKey[[string] $receipt.sourceKeySha256]
    foreach ($field in @('sourceKind','sourceId','manifestEntryId','sourceVersion','sourceSha256','sourceByteCount','relativePath',
        'destinationTenantId','destinationSiteId','destinationDriveId','destinationLibraryRootItemId','hasExactTargetBinding',
        'destinationItemId','destinationVersion','readBackSha256','readBackByteCount','verificationOutcome')) {
      if ($receipt[$field] -cne $file[$field]) { throw 'DRIFT' }
    }
    if ((Assert-ArchiveCopyTimestamp $receipt.readBackAt) -ne $fileReadAt) { throw 'DRIFT' }
  }

  $permission = $result.permissions
  Assert-ArchiveCopyObject $permission @(
    'expectedPermissionsJson','expectedPermissionsSha256','observedPermissionsJson','observedPermissionsSha256',
    'hasUniquePermissionScope','hasExactTargetBinding','observedStaffSha256','observedTenantId','observedSiteId',
    'observedDriveId','observedLibraryRootItemId','comparisonOutcome','observedAt'
  )
  foreach ($field in @('expectedPermissionsSha256','observedPermissionsSha256','observedStaffSha256')) { Assert-ArchiveCopyDigest $permission[$field] }
  $expectedGrant = ConvertFrom-ArchiveCopyPermissionSet $permission.expectedPermissionsJson $OperationPlan.staffGroupId
  $observedGrant = ConvertFrom-ArchiveCopyPermissionSet $permission.observedPermissionsJson $OperationPlan.staffGroupId
  if ($expectedGrant.Grant -cne $observedGrant.Grant -or
      $expectedGrant.Sha256 -cne $OperationPlan.expectedPermissionsSha256 -or
      $observedGrant.Sha256 -cne $OperationPlan.expectedPermissionsSha256 -or
      $permission.expectedPermissionsSha256 -cne $OperationPlan.expectedPermissionsSha256 -or
      $permission.observedPermissionsSha256 -cne $OperationPlan.expectedPermissionsSha256 -or
      $permission.observedStaffSha256 -cne $OperationPlan.authorizedStaffSha256 -or
      $permission.hasUniquePermissionScope -isnot [bool] -or -not $permission.hasUniquePermissionScope -or
      $permission.hasExactTargetBinding -isnot [bool] -or -not $permission.hasExactTargetBinding -or
      $permission.observedTenantId -cne $OperationPlan.destinationTenantId -or
      $permission.observedSiteId -cne $OperationPlan.destinationSiteId -or
      $permission.observedDriveId -cne $OperationPlan.destinationDriveId -or
      $permission.observedLibraryRootItemId -cne $OperationPlan.destinationLibraryRootItemId -or
      $permission.comparisonOutcome -cne 'MATCH') { throw 'DRIFT' }

  $permissionRows = @($Readback.permissionObservations | Where-Object { [long] $_.attemptToken -eq $attempt })
  if ($permissionRows.Count -ne 1 -or @($Readback.permissionObservations | Where-Object { [long] $_.attemptToken -gt $attempt }).Count -gt 0) {
    throw 'DRIFT'
  }
  $observation = $permissionRows[0]
  Assert-ArchiveCopyObject $observation @(
    'observationId','attemptToken','expectedPermissionsJson','expectedPermissionsSha256','observedPermissionsJson',
    'observedPermissionsSha256','hasUniquePermissionScope','hasExactTargetBinding','observedStaffSha256',
    'observedTenantId','observedSiteId','observedDriveId','observedLibraryRootItemId','comparisonOutcome','observedAt'
  )
  ConvertTo-ArchiveCopyGuid $observation.observationId | Out-Null
  foreach ($field in @('expectedPermissionsJson','expectedPermissionsSha256','observedPermissionsJson','observedPermissionsSha256',
      'hasUniquePermissionScope','hasExactTargetBinding','observedStaffSha256','observedTenantId','observedSiteId',
      'observedDriveId','observedLibraryRootItemId','comparisonOutcome')) {
    if ($observation[$field] -cne $permission[$field]) { throw 'DRIFT' }
  }
  if ((Assert-ArchiveCopyTimestamp $permission.observedAt) -ne (Assert-ArchiveCopyTimestamp $observation.observedAt)) { throw 'DRIFT' }

  return @{ Complete = $true; Blockers = @(); FileCount = $result.files.Count; AttemptToken = $attempt }
}

function Invoke-M365ArchiveCopyEvidenceReadback($Plan, $OperationPlan, [string] $OperationId,
    $Result, [scriptblock] $Transport) {
  $before = $null; $after = $null; $readback = $null
  try {
    $before = & $Transport '/api/ui/session'
    Assert-M365ArchiveCopySession $before $Plan
    $readback = & $Transport "/api/ui/records/archive-copies/$OperationId/evidence"
    $after = & $Transport '/api/ui/session'
    Assert-M365ArchiveCopySession $after $Plan
    foreach ($field in @('userId','firmId','generation')) {
      if ($before[$field] -cne $after[$field]) { throw 'DRIFT' }
    }
    $assessment = Assert-M365ArchiveCopyReadback $readback $OperationPlan $OperationId $after
    if (-not $assessment.Complete) {
      Add-M365Check $Result 'archive-copy-persisted-evidence' 'INCOMPLETE_EVIDENCE' 'exact completed operation, files and permissions' 'one or more persisted evidence records are missing' 'archive-copy-readback-incomplete'
      $Result.checks[-1].resourceId = $OperationId
      $Result.blockedPrerequisites += $assessment.Blockers
      $Result.errorCode = 'archive-copy-readback-incomplete'
      return 'INCOMPLETE_EVIDENCE'
    }

    Add-M365Check $Result 'archive-copy-persisted-evidence' 'PASS' 'pinned source, target, file and exact staff-read permission evidence' "matched $($assessment.FileCount) file receipts at attempt $($assessment.AttemptToken)"
    $Result.checks[-1].resourceId = $OperationId
    Add-M365Check $Result 'archive-copy-live-controls' 'INCOMPLETE_EVIDENCE' 'fresh provider, authoritative custody, denied edit/delete and recovery evidence' 'historical authenticated API readback does not establish these controls' 'archive-copy-live-evidence-not-established'
    $Result.checks[-1].resourceId = $OperationId
    $Result.blockedPrerequisites += @(
      'provider:fresh-destination-readback-not-performed',
      'custody:authoritative-custody-evidence-not-present',
      'control:live-denied-edit-delete-not-tested',
      'recovery:live-recovery-evidence-not-present'
    )
    $Result.errorCode = 'archive-copy-live-evidence-not-established'
    return 'INCOMPLETE_EVIDENCE'
  } catch {
    $message = $_.Exception.Message
    $status = if ($message -cin @('BLOCKED_BOOTSTRAP','DRIFT','INCOMPLETE_EVIDENCE','PROVIDER_FAILURE','PREREQUISITE_MISSING','INVALID_OR_UNSAFE')) {
      $message
    } elseif ($_.Exception.GetType().Name -ceq 'M365PlanException') { 'INVALID_OR_UNSAFE' } else { 'PROVIDER_FAILURE' }
    $errorCode = switch -CaseSensitive ($status) {
      'BLOCKED_BOOTSTRAP' { 'archive-copy-current-staff-session-required' }
      'DRIFT' { 'archive-copy-pinned-evidence-mismatch' }
      'PROVIDER_FAILURE' { 'archive-copy-readback-unavailable' }
      'INCOMPLETE_EVIDENCE' { 'archive-copy-readback-missing-or-malformed' }
      'PREREQUISITE_MISSING' { 'archive-copy-readback-prerequisite-missing' }
      default { 'archive-copy-readback-invalid' }
    }
    Add-M365Check $Result 'archive-copy-persisted-evidence' $status 'exact operation and pinned evidence' 'readback did not satisfy the verifier contract' $errorCode
    $Result.checks[-1].resourceId = $OperationId
    $Result.errorCode = $errorCode
    $Result.blockedPrerequisites += "archive-copy-readback:$($status.ToLowerInvariant())"
    return $status
  }
}

function Add-M365ArchiveCopyPlannedCheck($Result, [string] $OperationId = $null) {
  $expected = if ($OperationId) { 'exact operation and pinned source/target plan' } else { 'offline verifier invocation' }
  $observed = if ($OperationId) { 'offline plan only; no session or API read was performed' } else { 'no operation plan was supplied; no session or API read was performed' }
  Add-M365Check $Result 'archive-copy-readback-plan' 'PLANNED' $expected $observed
  if ($OperationId) { $Result.checks[-1].resourceId = $OperationId }
  $Result.blockedPrerequisites += 'offline-plan:no-authenticated-readback-performed'
}

Export-ModuleMember -Function Read-M365ArchiveCopyOperationPlan, New-M365ArchiveCopyTransport,
  Read-M365ArchiveCopyJsonResponse, Invoke-M365ArchiveCopyEvidenceReadback, Add-M365ArchiveCopyPlannedCheck
