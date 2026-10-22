# Read-only observation of the server-owned human consent protocol. Never writes consent evidence.
Set-StrictMode -Version Latest

function Get-M365EnrollmentGuid($Value) {
  $parsed = [guid]::Empty
  if ($Value -isnot [string] -or -not [guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [guid]::Empty) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  return $parsed.ToString('D')
}

function Assert-M365EnrollmentFields($Value, [string[]] $Keys) {
  if ($Value -isnot [System.Collections.IDictionary] -or @($Keys | Where-Object { -not $Value.Contains($_) }).Count) {
    throw 'INCOMPLETE_EVIDENCE'
  }
}

function Get-M365SessionCookie([string] $EnvironmentVariable) {
  if ($EnvironmentVariable -cnotmatch '^[A-Z][A-Z0-9_]{0,127}$') { throw 'BLOCKED_BOOTSTRAP' }
  $cookie = [Environment]::GetEnvironmentVariable($EnvironmentVariable, 'Process')
  # Only the host's default cookie and its standard chunk suffixes; never accept a bearer token.
  if (-not $cookie -or $cookie.Length -gt 32768 -or $cookie.IndexOfAny([char[]] "`r`n") -ge 0 -or
      $cookie -cnotmatch '\A\.AspNetCore\.Cookies(?:C[1-9][0-9]*)?=[A-Za-z0-9._~%+=/-]+(?:; ?\.AspNetCore\.Cookies(?:C[1-9][0-9]*)?=[A-Za-z0-9._~%+=/-]+)*\z') {
    throw 'BLOCKED_BOOTSTRAP'
  }
  $names = @($cookie.Split(';') | ForEach-Object { $_.Trim().Split('=')[0] })
  if (@($names | Select-Object -Unique).Count -ne $names.Count -or '.AspNetCore.Cookies' -cnotin $names) {
    throw 'BLOCKED_BOOTSTRAP'
  }
  return $cookie
}

function New-M365EnrollmentTransport([System.Net.Http.HttpClient] $Client, [uri] $BaseUri,
    [string] $Cookie, $ProviderCommand) {
  if ($BaseUri.Scheme -cne 'https' -or $BaseUri.UserInfo -or $BaseUri.Query -or $BaseUri.Fragment -or $BaseUri.AbsolutePath -cne '/') {
    throw 'INVALID_OR_UNSAFE'
  }
  return {
    param([string] $Path)
    if ($Path -cnotin @('/api/ui/session', '/api/ui/administration/microsoft365')) { throw 'INVALID_OR_UNSAFE' }
    $target = [uri]::new($BaseUri, $Path)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $target)
    try {
      $request.Headers.Add('Cookie', $Cookie)
      $request.Headers.Accept.ParseAdd('application/json')
      $httpClient = $Client; $outboundRequest = $request
      $dispatch = { $httpClient.SendAsync($outboundRequest).GetAwaiter().GetResult() }.GetNewClosure()
      $response = & $ProviderCommand $dispatch
      try {
        $code = [int] $response.StatusCode
        if ($code -in @(301,302,303,307,308,401,403)) { throw 'BLOCKED_BOOTSTRAP' }
        if ($code -ne 200) { throw 'PROVIDER_FAILURE' }
        if ($response.Content.Headers.ContentType.MediaType -cne 'application/json') { throw 'INCOMPLETE_EVIDENCE' }
        $raw = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $parse = @{ AsHashtable = $true; Depth = 32; NoEnumerate = $true }
        if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
        try { return ConvertFrom-Json -InputObject $raw @parse } catch { throw 'INCOMPLETE_EVIDENCE' }
      } finally { $response.Dispose() }
    } finally { $request.Dispose() }
  }.GetNewClosure()
}

function Assert-M365EnrollmentSnapshot($Plan, $Before, $Workspace, $After, $Result,
    [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow) {
  foreach ($session in @($Before, $After)) {
    Assert-M365EnrollmentFields $session @('userId','firmId','generation','staff')
    if ($session.staff -isnot [bool] -or -not $session.staff) { throw 'BLOCKED_BOOTSTRAP' }
    if ((Get-M365EnrollmentGuid $session.firmId) -ne $Plan.firmId) { throw 'INVALID_OR_UNSAFE' }
    Get-M365EnrollmentGuid $session.userId | Out-Null
    if ($session.generation -isnot [string] -or $session.generation -cnotmatch '^[0-9]{1,20}$') { throw 'INCOMPLETE_EVIDENCE' }
  }
  foreach ($key in @('firmId','userId','generation')) { if ($Before[$key] -cne $After[$key]) { throw 'DRIFT' } }
  Assert-M365EnrollmentFields $Workspace @('workspace','consentConfigured','directoryConfigured','configuredTenantId','simulation')
  if ($Workspace.simulation -isnot [bool] -or $Workspace.simulation) { throw 'INVALID_OR_UNSAFE' }
  if ($Workspace.consentConfigured -isnot [bool] -or -not $Workspace.consentConfigured) { throw 'BLOCKED_BOOTSTRAP' }
  if ((Get-M365EnrollmentGuid $Workspace.configuredTenantId) -ne $Plan.tenantId) { throw 'INVALID_OR_UNSAFE' }
  $state = $Workspace.workspace
  Assert-M365EnrollmentFields $state @('connection','setupProgress','consentingAdministrator','draftRevision')
  if ($state.draftRevision -isnot [string] -or $state.draftRevision -cnotmatch '^[1-9][0-9]{0,19}$') { throw 'INCOMPLETE_EVIDENCE' }
  if ($null -eq $state.connection -or $null -eq $state.setupProgress -or $null -eq $state.consentingAdministrator) { throw 'BLOCKED_BOOTSTRAP' }
  $connection = $state.connection
  Assert-M365EnrollmentFields $connection @('draftId','expectedTenantId','consentState','lastAttemptState','directoryCapabilityState','directoryLastCheckedAt')
  Get-M365EnrollmentGuid $connection.draftId | Out-Null
  if ((Get-M365EnrollmentGuid $connection.expectedTenantId) -ne $Plan.tenantId) { throw 'INVALID_OR_UNSAFE' }
  Assert-M365EnrollmentFields $state.setupProgress @('consentVerified')
  if ($connection.consentState -cne 'VERIFIED' -or $connection.lastAttemptState -cne 'CONSENT_VERIFIED' -or
      $state.setupProgress.consentVerified -isnot [bool] -or -not $state.setupProgress.consentVerified) { throw 'BLOCKED_BOOTSTRAP' }
  $administrator = $state.consentingAdministrator
  Assert-M365EnrollmentFields $administrator @('tenantId','objectId','verifiedAt')
  if ((Get-M365EnrollmentGuid $administrator.tenantId) -ne $Plan.tenantId) { throw 'INVALID_OR_UNSAFE' }
  Get-M365EnrollmentGuid $administrator.objectId | Out-Null
  $verifiedAt = [DateTimeOffset]::MinValue
  if ($administrator.verifiedAt -isnot [string] -or -not [DateTimeOffset]::TryParse($administrator.verifiedAt, [ref] $verifiedAt) -or
      $verifiedAt -gt $Now -or $verifiedAt -eq [DateTimeOffset]::MinValue) { throw 'INCOMPLETE_EVIDENCE' }
  if (@($Plan.capabilities | Where-Object kind -CEQ 'DIRECTORY_READER').Count) {
    $checkedAt = [DateTimeOffset]::MinValue
    if ($Workspace.directoryConfigured -isnot [bool] -or -not $Workspace.directoryConfigured -or
        $connection.directoryCapabilityState -cne 'VERIFIED_RECENT' -or $connection.directoryLastCheckedAt -isnot [string] -or
        -not [DateTimeOffset]::TryParse($connection.directoryLastCheckedAt, [ref] $checkedAt) -or
        $checkedAt -gt $Now -or $checkedAt -lt $Now.AddMinutes(-15)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  Add-M365Check $Result 'tenant-enrollment' 'PASS' 'server-owned human consent and current administrator session' 'exact tenant, firm, session and recorded grantor'
  $Result.checks[-1].resourceId = $connection.draftId
  $Result.checks[-1].expiresAtUtc = $Now.AddMinutes(15).ToString('o')
  $Result.checks[-1].evidenceReference = 'auditsphere-api:tenant-enrollment'
  if (@($Plan.capabilities | Where-Object kind -CEQ 'DIRECTORY_READER').Count) {
    # Observation freshness never extends the underlying capability's recorded validity.
    $directoryExpiry = $checkedAt.AddMinutes(15)
    if ($directoryExpiry -le $Now) { throw 'INCOMPLETE_EVIDENCE' }
    $Result.checks[-1].expiresAtUtc = $directoryExpiry.ToString('o')
  }
}

function Invoke-M365EnrollmentVerification($Plan, $Result, [scriptblock] $Transport) {
  $before = & $Transport '/api/ui/session'
  $workspace = & $Transport '/api/ui/administration/microsoft365'
  $after = & $Transport '/api/ui/session'
  Assert-M365EnrollmentSnapshot $Plan $before $workspace $after $Result
}

Export-ModuleMember -Function Get-M365SessionCookie, New-M365EnrollmentTransport,
  Assert-M365EnrollmentSnapshot, Invoke-M365EnrollmentVerification
