# Bounded same-origin transport and pure verification for server-owned client-site intents.
Set-StrictMode -Version Latest

function Assert-M365ClientSiteIntentFields($Value, [string[]] $Keys) {
  if ($Value -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($key in $Keys) {
    if (-not $Value.Contains($key)) { throw 'INCOMPLETE_EVIDENCE' }
  }
}

function ConvertTo-M365ClientSiteIntentGuid([object] $Value, [string] $FailureCode = 'INCOMPLETE_EVIDENCE') {
  $parsed = [guid]::Empty
  if ($Value -isnot [string] -or -not [guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [guid]::Empty) {
    throw $FailureCode
  }
  return $parsed.ToString('D')
}

function Assert-M365ClientSiteIntentAuthCookie([string] $Cookie) {
  if (-not $Cookie -or $Cookie.Length -gt 32768 -or $Cookie.IndexOfAny([char[]] "`r`n") -ge 0 -or
      $Cookie -cnotmatch '\A\.AspNetCore\.Cookies(?:C[1-9][0-9]*)?=[A-Za-z0-9._~%+=/-]+(?:; ?\.AspNetCore\.Cookies(?:C[1-9][0-9]*)?=[A-Za-z0-9._~%+=/-]+)*\z') {
    throw 'BLOCKED_BOOTSTRAP'
  }
  $names = @($Cookie.Split(';') | ForEach-Object { $_.Trim().Split('=')[0] })
  if (@($names | Select-Object -Unique).Count -ne $names.Count -or '.AspNetCore.Cookies' -cnotin $names) {
    throw 'BLOCKED_BOOTSTRAP'
  }
}

function ConvertFrom-M365ClientSiteSetCookie([string] $Header, [string] $OriginHost) {
  if (-not $Header -or $Header.Length -gt 8192 -or $Header.IndexOfAny([char[]] "`r`n") -ge 0 -or
      @($Header.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0) { throw 'BLOCKED_BOOTSTRAP' }
  $parts = $Header.Split(';')
  $pair = $parts[0].Trim()
  $equals = $pair.IndexOf('=')
  if ($equals -le 0) { return $null }
  $name = $pair.Substring(0, $equals).Trim()
  $value = $pair.Substring($equals + 1).Trim()
  $isXsrf = $name -ceq 'XSRF-TOKEN'
  $isAntiforgery = $name -cmatch '\A\.AspNetCore\.Antiforgery\.[A-Za-z0-9._-]{1,128}\z'
  if (-not $isXsrf -and -not $isAntiforgery) { return $null }
  if ($value.Length -lt 1 -or $value.Length -gt 4096 -or $value -cnotmatch '\A[A-Za-z0-9._~%+=/-]+\z') {
    throw 'BLOCKED_BOOTSTRAP'
  }

  $seenAttributes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  for ($index = 1; $index -lt $parts.Length; $index++) {
    $attribute = $parts[$index].Trim()
    if (-not $attribute) { continue }
    $separator = $attribute.IndexOf('=')
    $attributeName = if ($separator -lt 0) { $attribute } else { $attribute.Substring(0, $separator).Trim() }
    $attributeValue = if ($separator -lt 0) { $null } else { $attribute.Substring($separator + 1).Trim() }
    if (-not $attributeName -or -not $seenAttributes.Add($attributeName)) { throw 'BLOCKED_BOOTSTRAP' }
    switch -Regex ($attributeName.ToLowerInvariant()) {
      '^path$' {
        if ($attributeValue -cne '/') { throw 'BLOCKED_BOOTSTRAP' }
        break
      }
      '^domain$' {
        if (-not $attributeValue -or $attributeValue.StartsWith('.') -or $attributeValue -ine $OriginHost) { throw 'BLOCKED_BOOTSTRAP' }
        break
      }
      '^secure$' { if ($null -ne $attributeValue) { throw 'BLOCKED_BOOTSTRAP' }; break }
      '^httponly$' { if ($null -ne $attributeValue) { throw 'BLOCKED_BOOTSTRAP' }; break }
      '^samesite$' { if ($attributeValue -cnotin @('Strict','Lax','None')) { throw 'BLOCKED_BOOTSTRAP' }; break }
      '^max-age$' {
        $seconds = 0L
        if (-not [long]::TryParse($attributeValue, [ref] $seconds) -or $seconds -le 0 -or $seconds -gt 31536000) {
          throw 'BLOCKED_BOOTSTRAP'
        }
        break
      }
      '^expires$' {
        $expires = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse($attributeValue, [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref] $expires) -or $expires -le [DateTimeOffset]::UtcNow) {
          throw 'BLOCKED_BOOTSTRAP'
        }
        break
      }
      default { throw 'BLOCKED_BOOTSTRAP' }
    }
  }
  return @{ Name = $name; Value = $value; IsXsrf = $isXsrf; IsAntiforgery = $isAntiforgery }
}

function ConvertTo-M365ClientSiteRequestToken([string] $Value) {
  if ($Value -match '%(?![0-9A-Fa-f]{2})') { throw 'BLOCKED_BOOTSTRAP' }
  try { $decoded = [uri]::UnescapeDataString($Value) } catch { throw 'BLOCKED_BOOTSTRAP' }
  if (-not $decoded -or $decoded.Length -gt 4096 -or $decoded -cnotmatch '\A[A-Za-z0-9._~+=/-]+\z') {
    throw 'BLOCKED_BOOTSTRAP'
  }
  return $decoded
}

function New-M365ClientSiteIntentTransport([System.Net.Http.HttpClient] $Client, [uri] $BaseUri,
    [string] $Cookie, [string[]] $ClientIds, $ProviderCommand) {
  if ($null -eq $Client -or $null -eq $BaseUri -or $null -eq $ClientIds -or $ClientIds.Count -lt 1 -or
      $ProviderCommand -isnot [scriptblock]) { throw 'INVALID_OR_UNSAFE' }
  if ($BaseUri.Scheme -cne 'https' -or $BaseUri.UserInfo -or $BaseUri.Query -or $BaseUri.Fragment -or
      $BaseUri.AbsolutePath -cne '/' -or
      $BaseUri.Host -cnotmatch '\A(?=.{1,253}\z)[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\z' -or
      $BaseUri.Host.Contains('..') -or $BaseUri.OriginalString.IndexOfAny([char[]] "`r`n\") -ge 0) {
    throw 'INVALID_OR_UNSAFE'
  }
  if ($Client.Timeout -eq [System.Threading.Timeout]::InfiniteTimeSpan -or $Client.Timeout -le [TimeSpan]::Zero -or
      $Client.Timeout -gt [TimeSpan]::FromSeconds(10) -or $Client.MaxResponseContentBufferSize -lt 1 -or
      $Client.MaxResponseContentBufferSize -gt 262144) { throw 'INVALID_OR_UNSAFE' }
  Assert-M365ClientSiteIntentAuthCookie $Cookie

  $allowedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($clientId in $ClientIds) {
    $canonical = ConvertTo-M365ClientSiteIntentGuid $clientId 'INVALID_OR_UNSAFE'
    if (-not $allowedIds.Add($canonical)) { throw 'INVALID_OR_UNSAFE' }
  }
  $httpClient = $Client
  $origin = $BaseUri
  $authCookie = $Cookie
  $provider = $ProviderCommand
  $state = @{
    AntiforgeryCookies = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
    RequestToken = $null
    Reviews = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
    UsedReviewTokens = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  }
  $intentHelpers = @{
    AssertFields = ${function:Assert-M365ClientSiteIntentFields}
    ConvertGuid = ${function:ConvertTo-M365ClientSiteIntentGuid}
    ParseSetCookie = ${function:ConvertFrom-M365ClientSiteSetCookie}
    DecodeRequestToken = ${function:ConvertTo-M365ClientSiteRequestToken}
  }

  return {
    param([string] $Method, [string] $Path, $Body)
    if ($Method -cnotin @('GET','POST') -or -not $Path -or $Path.IndexOfAny([char[]] "`r`n") -ge 0) {
      throw 'INVALID_OR_UNSAFE'
    }

    $clientId = $null
    $reviewMode = $null
    if ($Path -ceq '/api/ui/session') {
      if ($Method -cne 'GET' -or $null -ne $Body) { throw 'INVALID_OR_UNSAFE' }
    }
    elseif ($Path -cmatch '\A/api/ui/administration/microsoft365/client-sites/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/(review|staff-review|request)\z') {
      $clientId = $Matches[1]
      $route = $Matches[2]
      if (-not $allowedIds.Contains($clientId)) { throw 'INVALID_OR_UNSAFE' }
      if ($route -ceq 'request') {
        if ($Method -cne 'POST') { throw 'INVALID_OR_UNSAFE' }
      }
      else {
        if ($Method -cne 'GET' -or $null -ne $Body) { throw 'INVALID_OR_UNSAFE' }
        $reviewMode = $route -ceq 'staff-review'
      }
    }
    else { throw 'INVALID_OR_UNSAFE' }

    $jsonBody = $null
    if ($Method -ceq 'POST') {
      if ($Body -isnot [System.Collections.IDictionary] -or $Body.Count -ne 4 -or
          @('reviewToken','reviewed','reason','reconcileOnly' | Where-Object { -not $Body.Contains($_) }).Count -ne 0 -or
          $Body.reviewed -isnot [bool] -or -not $Body.reviewed -or $Body.reconcileOnly -isnot [bool] -or
          $Body.reason -isnot [string] -or [string]::IsNullOrWhiteSpace($Body.reason) -or $Body.reason.Length -gt 1000 -or
          @($Body.reason.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0 -or
          $Body.reviewToken -isnot [string] -or $Body.reviewToken -cnotmatch '\A[0-9a-fA-F]{64}\z') {
        throw 'INVALID_OR_UNSAFE'
      }
      if (-not $state.Reviews.ContainsKey($clientId)) { throw 'BLOCKED_BOOTSTRAP' }
      $review = $state.Reviews[$clientId]
      if ($Body.reviewToken -cne $review.Token -or $Body.reconcileOnly -ne $review.ReconcileOnly -or
          $state.UsedReviewTokens.Contains("$clientId|$($Body.reviewToken)")) { throw 'DRIFT' }
      if ($state.AntiforgeryCookies.Count -lt 1 -or -not $state.RequestToken) { throw 'BLOCKED_BOOTSTRAP' }
      $jsonBody = ConvertTo-Json -InputObject $Body -Depth 4 -Compress
    }

    $target = [uri]::new($origin, $Path)
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $target)
    $httpClientForCall = $Client
    $providerCommandForCall = $ProviderCommand
    $intentStateForCall = $state
    $intentClientIdForCall = $clientId
    $intentMethodForCall = $Method
    $intentReviewTokenForCall = if ($Method -ceq 'POST') { $Body.reviewToken } else { $null }
    $response = $null
    $dispatchState = @{ Count = 0; Attempted = $false }
    try {
      $request.Headers.Accept.ParseAdd('application/json')
      $cookieParts = @($authCookie)
      if ($Method -ceq 'POST') {
        foreach ($name in @($state.AntiforgeryCookies.Keys | Sort-Object -CaseSensitive)) {
          $cookieParts += "$name=$($state.AntiforgeryCookies[$name])"
        }
        $request.Headers.Add('Cookie', ($cookieParts -join '; '))
        $request.Headers.Add('X-XSRF-TOKEN', $state.RequestToken)
        $request.Content = [System.Net.Http.StringContent]::new($jsonBody, [System.Text.Encoding]::UTF8, 'application/json')
      }
      else { $request.Headers.Add('Cookie', $authCookie) }

      $outboundRequest = $request
      $dispatch = {
        if ($dispatchState.Count -ne 0) { throw 'RESULT_UNCERTAIN' }
        $dispatchState.Count++
        $dispatchState.Attempted = $true
        if ($intentMethodForCall -ceq 'POST') {
          $intentStateForCall.UsedReviewTokens.Add("$intentClientIdForCall|$intentReviewTokenForCall") | Out-Null
          $intentStateForCall.Reviews.Remove($intentClientIdForCall) | Out-Null
        }
        $httpClientForCall.SendAsync($outboundRequest).GetAwaiter().GetResult()
      }.GetNewClosure()
      try { $response = & $providerCommandForCall $dispatch }
      catch {
        if ($Method -ceq 'POST' -and $dispatchState.Attempted) { throw 'RESULT_UNCERTAIN' }
        if ($_.Exception.Message -cin @('BLOCKED_BOOTSTRAP','INVALID_OR_UNSAFE','PREREQUISITE_MISSING','DRIFT','INCOMPLETE_EVIDENCE')) {
          throw $_.Exception.Message
        }
        throw 'PROVIDER_FAILURE'
      }

      if ($null -eq $response -or $null -eq $response.RequestMessage -or $null -eq $response.RequestMessage.RequestUri -or
          $response.RequestMessage.RequestUri.AbsoluteUri -cne $target.AbsoluteUri) {
        if ($Method -ceq 'POST' -and $dispatchState.Attempted) { throw 'RESULT_UNCERTAIN' }
        throw 'INVALID_OR_UNSAFE'
      }
      $status = [int] $response.StatusCode
      if (($status -ge 300 -and $status -lt 400) -or $status -in @(401,403)) { throw 'BLOCKED_BOOTSTRAP' }
      if ($status -eq 409) { throw 'DRIFT' }
      if ($status -in @(404,405,410,501)) { throw 'PREREQUISITE_MISSING' }
      if ($status -ne 200) {
        if ($Method -ceq 'POST' -and $dispatchState.Attempted) { throw 'RESULT_UNCERTAIN' }
        throw 'PROVIDER_FAILURE'
      }
      if ($null -eq $response.Content -or $null -eq $response.Content.Headers.ContentType -or
          $response.Content.Headers.ContentType.MediaType -ine 'application/json') {
        if ($Method -ceq 'POST') { throw 'RESULT_UNCERTAIN' }
        throw 'INCOMPLETE_EVIDENCE'
      }
      if ($null -ne $response.Content.Headers.ContentLength -and $response.Content.Headers.ContentLength -gt 262144) {
        if ($Method -ceq 'POST') { throw 'RESULT_UNCERTAIN' }
        throw 'INCOMPLETE_EVIDENCE'
      }

      try {
        $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
        if ($bytes.Length -gt 262144) { throw 'INCOMPLETE_EVIDENCE' }
        $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
        $json = $utf8.GetString($bytes)
        $parse = @{ AsHashtable = $true; Depth = 32; NoEnumerate = $true }
        if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
        $value = ConvertFrom-Json -InputObject $json @parse -ErrorAction Stop
        if ($value -isnot [System.Collections.IDictionary]) { throw 'INCOMPLETE_EVIDENCE' }
      }
      catch {
        if ($Method -ceq 'POST') { throw 'RESULT_UNCERTAIN' }
        if ($_.Exception.Message -ceq 'INCOMPLETE_EVIDENCE') { throw 'INCOMPLETE_EVIDENCE' }
        throw 'INCOMPLETE_EVIDENCE'
      }

      if ($Method -ceq 'GET' -and $Path -ceq '/api/ui/session') {
        $setCookieValues = $null
        $nextCookies = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::Ordinal)
        $nextRequestToken = $null
        if ($response.Headers.TryGetValues('Set-Cookie', [ref] $setCookieValues)) {
          foreach ($header in $setCookieValues) {
            $parsedCookie = & $intentHelpers.ParseSetCookie $header $origin.Host
            if ($null -eq $parsedCookie) { continue }
            if ($parsedCookie.IsXsrf) {
              if ($null -ne $nextRequestToken) { throw 'BLOCKED_BOOTSTRAP' }
              $nextRequestToken = & $intentHelpers.DecodeRequestToken $parsedCookie.Value
            }
            elseif (-not $nextCookies.TryAdd($parsedCookie.Name, $parsedCookie.Value)) { throw 'BLOCKED_BOOTSTRAP' }
          }
        }
        $state.AntiforgeryCookies = $nextCookies
        $state.RequestToken = $nextRequestToken
      }
      elseif ($Method -ceq 'GET' -and $null -ne $clientId) {
        & $intentHelpers.AssertFields $value @('clientId','reviewToken')
        if ((& $intentHelpers.ConvertGuid $value.clientId) -cne $clientId -or
            $value.reviewToken -isnot [string] -or $value.reviewToken -cnotmatch '\A[0-9a-fA-F]{64}\z') {
          throw 'INCOMPLETE_EVIDENCE'
        }
        $state.Reviews[$clientId] = @{ Token = $value.reviewToken; ReconcileOnly = [bool] $reviewMode }
      }
      return $value
    }
    finally {
      if ($null -ne $response) { $response.Dispose() }
      $request.Dispose()
    }
  }.GetNewClosure()
}

function Assert-M365ClientSiteSession($Plan, $Before, $After) {
  Assert-M365ClientSiteIntentFields $Plan @('firmId')
  Assert-M365ClientSiteIntentFields $Before @('userId','firmId','generation','staff')
  Assert-M365ClientSiteIntentFields $After @('userId','firmId','generation','staff')
  $firmId = ConvertTo-M365ClientSiteIntentGuid $Plan.firmId 'INVALID_OR_UNSAFE'
  $beforeFirm = $null; $afterFirm = $null; $beforeUser = $null; $afterUser = $null
  $index = 0
  foreach ($session in @($Before,$After)) {
    if ($session.staff -isnot [bool] -or -not $session.staff) { throw 'BLOCKED_BOOTSTRAP' }
    $sessionFirm = ConvertTo-M365ClientSiteIntentGuid $session.firmId
    $sessionUser = ConvertTo-M365ClientSiteIntentGuid $session.userId
    if ($session.generation -isnot [string] -or $session.generation -cnotmatch '\A[0-9]{1,20}\z') {
      throw 'INCOMPLETE_EVIDENCE'
    }
    if ($sessionFirm -cne $firmId) { throw 'INVALID_OR_UNSAFE' }
    if ($index -eq 0) {
      $beforeFirm = $sessionFirm; $beforeUser = $sessionUser
    }
    else { $afterFirm = $sessionFirm; $afterUser = $sessionUser }
    $index++
  }
  if ($beforeUser -cne $afterUser -or $beforeFirm -cne $afterFirm -or
      $Before.generation -cne $After.generation) { throw 'DRIFT' }
  return $true
}

function ConvertTo-M365ClientSiteGuidSet([object] $Value) {
  if ($Value -isnot [System.Collections.IList] -or $Value -is [string] -or $Value.Count -gt 200) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($item in $Value) {
    $canonical = ConvertTo-M365ClientSiteIntentGuid $item
    if (-not $set.Add($canonical)) { throw 'INCOMPLETE_EVIDENCE' }
  }
  return ,@($set | Sort-Object -CaseSensitive)
}

function Assert-M365ClientSitePublished($Plan, [string] $ClientId, $View,
    [DateTimeOffset] $Now = [DateTimeOffset]::UtcNow) {
  Assert-M365ClientSiteIntentFields $Plan @('firmId','tenantId','clientSites')
  Assert-M365ClientSiteIntentFields $Plan.clientSites @('host')
  Assert-M365ClientSiteIntentFields $View @('clientId','tenantId','url','ownershipMarker','staffAuthorizationDigest',
    'staffObjectIds','state','membershipState','operationId','operationState','receiptState','publishedReceipt')

  $firmId = ConvertTo-M365ClientSiteIntentGuid $Plan.firmId 'INVALID_OR_UNSAFE'
  $tenantId = ConvertTo-M365ClientSiteIntentGuid $Plan.tenantId 'INVALID_OR_UNSAFE'
  $client = ConvertTo-M365ClientSiteIntentGuid $ClientId 'INVALID_OR_UNSAFE'
  $viewClient = ConvertTo-M365ClientSiteIntentGuid $View.clientId
  $viewTenant = ConvertTo-M365ClientSiteIntentGuid $View.tenantId
  if ($viewClient -cne $client -or $viewTenant -cne $tenantId -or
      $View.ownershipMarker -cne "AuditSphere:$firmId`:$client") { throw 'INVALID_OR_UNSAFE' }

  $host = $Plan.clientSites.host
  if ($host -isnot [string] -or $host -cnotmatch '\A[a-z0-9-]+\.sharepoint\.com\z' -or
      $View.url -isnot [string] -or $View.url.Length -gt 2048) { throw 'INVALID_OR_UNSAFE' }
  $siteUri = $null
  if (-not [uri]::TryCreate($View.url, [System.UriKind]::Absolute, [ref] $siteUri) -or
      $siteUri.Scheme -cne 'https' -or $siteUri.UserInfo -or $siteUri.Query -or $siteUri.Fragment -or
      -not $siteUri.IsDefaultPort -or $siteUri.Host -ine $host -or
      $siteUri.AbsolutePath -cnotmatch "\A/sites/[a-z0-9]+(?:-[a-z0-9]+)*-$($client.Replace('-',''))\z") {
    throw 'INVALID_OR_UNSAFE'
  }

  if ($View.state -cne 'READY' -or $View.membershipState -cne 'VERIFIED' -or
      $View.operationState -cne 'COMPLETED' -or $View.receiptState -cne 'PUBLISHED' -or
      $null -eq $View.publishedReceipt) { throw 'INCOMPLETE_EVIDENCE' }
  $receipt = $View.publishedReceipt
  Assert-M365ClientSiteIntentFields $receipt @('operationId','digest','completedAt','siteId','driveId','rootItemId',
    'staffGroupId','staffAuthorizationDigest','managedStaffObjectIds','residualAccessReferences')
  $operationId = ConvertTo-M365ClientSiteIntentGuid $View.operationId
  $receiptOperationId = ConvertTo-M365ClientSiteIntentGuid $receipt.operationId
  if ($operationId -cne $receiptOperationId) { throw 'INCOMPLETE_EVIDENCE' }
  if ($receipt.digest -isnot [string] -or $receipt.digest -cnotmatch '\A[0-9a-fA-F]{64}\z' -or
      $View.staffAuthorizationDigest -isnot [string] -or $View.staffAuthorizationDigest -cnotmatch '\A[0-9a-fA-F]{64}\z' -or
      $receipt.staffAuthorizationDigest -isnot [string] -or $receipt.staffAuthorizationDigest -cnotmatch '\A[0-9a-fA-F]{64}\z') {
    throw 'INCOMPLETE_EVIDENCE'
  }
  if ($receipt.staffAuthorizationDigest -ine $View.staffAuthorizationDigest) { throw 'INCOMPLETE_EVIDENCE' }
  foreach ($key in @('siteId','driveId','rootItemId','staffGroupId')) {
    if ($receipt[$key] -isnot [string] -or [string]::IsNullOrWhiteSpace($receipt[$key]) -or
        $receipt[$key].Length -gt 512 -or @($receipt[$key].ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0) {
      throw 'INCOMPLETE_EVIDENCE'
    }
  }

  $currentStaff = ConvertTo-M365ClientSiteGuidSet $View.staffObjectIds
  $managedStaff = ConvertTo-M365ClientSiteGuidSet $receipt.managedStaffObjectIds
  if (@(Compare-Object -ReferenceObject $currentStaff -DifferenceObject $managedStaff -CaseSensitive).Count -ne 0) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  if ($receipt.residualAccessReferences -isnot [System.Collections.IList] -or
      $receipt.residualAccessReferences -is [string] -or $receipt.residualAccessReferences.Count -ne 0) {
    throw 'INCOMPLETE_EVIDENCE'
  }

  $completedAt = [DateTimeOffset]::MinValue
  if ($receipt.completedAt -isnot [string] -or
      $receipt.completedAt -cnotmatch '\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z' -or
      -not [DateTimeOffset]::TryParse($receipt.completedAt, [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind, [ref] $completedAt)) { throw 'INCOMPLETE_EVIDENCE' }
  if ($completedAt -lt $Now.AddMinutes(-15) -or $completedAt -gt $Now.AddSeconds(5)) { throw 'INCOMPLETE_EVIDENCE' }
  return $true
}

Export-ModuleMember -Function New-M365ClientSiteIntentTransport, Assert-M365ClientSiteSession, Assert-M365ClientSitePublished
