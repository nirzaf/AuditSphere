#Requires -Version 7.4
# WU-018: registration/certificate reconciliation. No consent grants or private-key generation.
Set-StrictMode -Version Latest

function Get-EntraStateDirectory([string] $Requested) {
  # One host/operator is the supported execution boundary. Clones/worktrees/output
  # filenames must not introduce fresh journals for the same tenant operation.
  $base = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
  if (-not $base) { throw 'PREREQUISITE_MISSING' }
  $fixed = [System.IO.Path]::GetFullPath((Join-Path $base 'AuditSphere/M365Entra'))
  if ($Requested) {
    if (-not [System.IO.Path]::IsPathFullyQualified($Requested)) { throw 'INVALID_OR_UNSAFE' }
    $normalized = [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Requested))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $normalized.Equals($fixed, $comparison)) { throw 'INVALID_OR_UNSAFE' }
  }
  return $fixed
}

function Get-EntraJournalPath($Plan) {
  return Join-Path (Get-EntraStateDirectory) "$($Plan.tenantId.ToLowerInvariant())-$($Plan.ownershipTag.ToLowerInvariant()).json"
}

function Get-EntraOwnershipMarker($Plan, $Capability) {
  return "AuditSphere/$($Plan.ownershipTag.ToLowerInvariant())/$($Plan.firmId.ToLowerInvariant())/$($Capability.kind)"
}

function Test-EntraOperatorContext($Context, $Plan) {
  return $null -ne $Context -and $Context.TenantId -eq $Plan.tenantId -and
    $Context.ClientId -eq $Plan.operator.clientId -and $Context.Environment -ceq 'Global'
}

function New-EntraGraphTransport($ProviderCommand, $GraphCommand, $GetRequestContext, $SetRequestContext, [scriptblock] $Wait) {
  if (-not $GetRequestContext -or -not $SetRequestContext) { throw 'PREREQUISITE_MISSING' }
  if (-not $Wait) { $Wait = { param($seconds) Start-Sleep -Milliseconds ([int] [Math]::Ceiling($seconds * 1000)) } }
  return {
    param($method, $path, $body)
    if (-not $path.StartsWith('/v1.0/')) { throw 'INVALID_OR_UNSAFE' }
    $prior = & $GetRequestContext
    $restore = @{ MaxRetry = [int] $prior.MaxRetry; RetriesTimeLimit = [int] $prior.RetriesTimeLimit.TotalSeconds
      ClientTimeout = [int] $prior.ClientTimeout.TotalSeconds; Confirm = $false }
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $attempts = if ($method -ceq 'GET') { 3 } else { 1 }
    try {
      for ($attempt = 0; $attempt -lt $attempts; $attempt++) {
        $remaining = 30 - $clock.Elapsed.TotalSeconds
        if ($remaining -lt 1) { throw 'PROVIDER_FAILURE' }
        # Disable the SDK's hidden retries, especially for POST/PATCH. Count each
        # bounded GET attempt through the guarded provider dispatch instead.
        & $SetRequestContext -MaxRetry 0 -RetriesTimeLimit 0 -ClientTimeout ([int] [Math]::Floor($remaining)) -Confirm:$false | Out-Null
        $reply = $null
        $delay = [Math]::Pow(2, $attempt)
        try {
          try {
            $reply = & $ProviderCommand {
              $arguments = @{ Method = $method; Uri = "https://graph.microsoft.com$path"; OutputType = 'HttpResponseMessage'; SkipHttpErrorCheck = $true }
              if ($null -ne $body) { $arguments.Body = ConvertTo-Json $body -Depth 32 -Compress; $arguments.ContentType = 'application/json' }
              & $GraphCommand @arguments
            }
          } catch {
            if ($method -cne 'GET') { throw }
            if ($attempt -eq $attempts - 1) { throw 'PROVIDER_FAILURE' }
          }
          if ($reply) {
            if ($reply -isnot [System.Net.Http.HttpResponseMessage]) { throw 'INCOMPLETE_EVIDENCE' }
            $code = [int] $reply.StatusCode
            if ($reply.IsSuccessStatusCode) {
              $text = $reply.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              if ([string]::IsNullOrWhiteSpace($text)) { return $null }
              $parse = @{ AsHashtable = $true; NoEnumerate = $true; Depth = 64 }
              if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
              return $text | ConvertFrom-Json @parse
            }
            if ($method -cne 'GET' -or $code -notin @(408,429,500,502,503,504)) { throw 'PROVIDER_FAILURE' }
            $retry = $reply.Headers.RetryAfter
            if ($retry) {
              if ($null -ne $retry.Delta) { $delay = $retry.Delta.TotalSeconds }
              elseif ($null -ne $retry.Date) { $delay = [Math]::Max(0, ($retry.Date - [DateTimeOffset]::UtcNow).TotalSeconds) }
              else { throw 'PROVIDER_FAILURE' }
            }
          }
        } finally { if ($reply) { $reply.Dispose() } }
        if ($attempt -eq $attempts - 1 -or $delay -ge 30 - $clock.Elapsed.TotalSeconds) { throw 'PROVIDER_FAILURE' }
        & $Wait $delay
      }
      throw 'PROVIDER_FAILURE'
    } finally { $clock.Stop(); & $SetRequestContext @restore | Out-Null }
  }.GetNewClosure()
}

function Save-EntraMutationIntent($Journal, [string] $JournalPath, [string] $Method, [string] $Path, $Body, [string] $PlanSha256) {
  if ($Method -ne 'POST') { return }
  $target = if ($Path -eq '/v1.0/applications') { $Body.tags[0] } else { $Body.appId }
  $intent = "$Path/$target"
  if ($Journal.ContainsKey($intent)) { throw 'RESULT_UNCERTAIN' }
  $Journal[$intent] = @{ planSha256 = $PlanSha256; attemptedAtUtc = [DateTimeOffset]::UtcNow.ToString('o') }
  $temporary = "$JournalPath.$([guid]::NewGuid().ToString('N')).tmp"
  $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json $Journal -Depth 8))
  $stream = [System.IO.FileStream]::new($temporary, 'CreateNew', 'Write', 'None', 4096, [System.IO.FileOptions]::WriteThrough)
  try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  if (-not $IsWindows) { [System.IO.File]::SetUnixFileMode($temporary, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite) }
  [System.IO.File]::Move($temporary, $JournalPath, $true)
}

function Invoke-EntraRead([scriptblock] $Transport, [string] $Path) {
  try { return & $Transport 'GET' $Path $null }
  catch { throw 'PROVIDER_FAILURE' } # Never print raw provider responses/tokens.
}

function Get-EntraCollection([scriptblock] $Transport, [string] $Path) {
  $items = @(); $seen = @{}
  for ($page = 0; $Path; $page++) {
    if ($page -ge 100 -or $seen.ContainsKey($Path)) { throw 'INCOMPLETE_EVIDENCE' }
    $seen[$Path] = $true
    $response = Invoke-EntraRead $Transport $Path
    $items += @($response.value)
    if ($items.Count -gt 1000) { throw 'INCOMPLETE_EVIDENCE' }
    $Path = if ($response.ContainsKey('@odata.nextLink')) { $response['@odata.nextLink'] } else { $null }
    if ($Path) {
      $next = [uri] $Path
      if ($next.Scheme -ne 'https' -or $next.Host -ne 'graph.microsoft.com' -or
          $next.Port -ne 443 -or $next.UserInfo -or -not $next.AbsolutePath.StartsWith('/v1.0/')) {
        throw 'INVALID_OR_UNSAFE'
      }
      $Path = $next.PathAndQuery
    }
  }
  return ,$items
}

function Invoke-EntraWrite($Result, [scriptblock] $Transport, [scriptblock] $Approve,
    [string] $Method, [string] $Path, $Body) {
  if (-not (& $Approve $Method $Path $Body)) { throw 'BLOCKED_APPROVAL' }
  $Result.mutationsAttempted++
  try { $response = & $Transport $Method $Path $Body }
  catch { throw 'RESULT_UNCERTAIN' } # Reconcile original owned IDs on the next invocation; never retry POST here.
  return $response
}

function Get-EntraRequestedResources($Plan, $Capability, [scriptblock] $Transport, [hashtable] $ResourcePrincipals) {
  $resources = @(); $requests = @{}
  foreach ($role in $Capability.expectedApplicationRoles) {
    $resource = $role.resourceAppId.ToLowerInvariant()
    if (-not $requests.ContainsKey($resource)) { $requests[$resource] = @{ roles = @(); scopes = @() } }
    $requests[$resource].roles += $role.value
  }
  if ($Capability.ContainsKey('signInScopes') -and $Capability.signInScopes.Count) {
    $graph = '00000003-0000-0000-c000-000000000000'
    if (-not $requests.ContainsKey($graph)) { $requests[$graph] = @{ roles = @(); scopes = @() } }
    $requests[$graph].scopes = @($Capability.signInScopes)
  }
  foreach ($resourceId in @($requests.Keys | Sort-Object)) {
    $principals = Get-EntraCollection $Transport "/v1.0/servicePrincipals?`$filter=appId%20eq%20'$resourceId'&`$select=id,appId,appRoles,oauth2PermissionScopes"
    if ($principals.Count -ne 1) { throw 'INCOMPLETE_EVIDENCE' }
    if ($principals[0].appId -ne $resourceId) { throw 'INVALID_OR_UNSAFE' }
    $ResourcePrincipals[$resourceId] = $principals[0].id
    $access = @()
    foreach ($value in $requests[$resourceId].roles) {
      $matches = @($principals[0].appRoles | Where-Object {
        $_.value -ceq $value -and $_.isEnabled -is [bool] -and $_.isEnabled -and 'Application' -cin $_.allowedMemberTypes
      })
      if ($matches.Count -ne 1) { throw 'INVALID_OR_UNSAFE' }
      $access += @{ id = $matches[0].id; type = 'Role' }
    }
    foreach ($value in $requests[$resourceId].scopes) {
      $matches = @($principals[0].oauth2PermissionScopes | Where-Object { $_.value -ceq $value -and $_.isEnabled -is [bool] -and $_.isEnabled })
      if ($matches.Count -ne 1) { throw 'INVALID_OR_UNSAFE' }
      $access += @{ id = $matches[0].id; type = 'Scope' }
    }
    $resources += @{ resourceAppId = $resourceId; resourceAccess = $access }
  }
  return ,$resources
}

function Assert-EntraAssignedRoles($Principal, $Desired, $ResourcePrincipals, [scriptblock] $Transport) {
  $allowed = @($Desired | ForEach-Object {
    $resource = $ResourcePrincipals[$_.resourceAppId]
    foreach ($access in $_.resourceAccess) { if ($access.type -ceq 'Role') { "$resource/$($access.id)".ToLowerInvariant() } }
  })
  $assignments = Get-EntraCollection $Transport "/v1.0/servicePrincipals/$($Principal.id)/appRoleAssignments?`$select=id,principalId,resourceId,appRoleId"
  foreach ($assignment in $assignments) {
    if ($assignment.principalId -ne $Principal.id) { throw 'INVALID_OR_UNSAFE' }
    if ("$($assignment.resourceId)/$($assignment.appRoleId)".ToLowerInvariant() -cnotin $allowed) { throw 'DRIFT' }
  }
}

function Get-EntraRoleKeys($Resources) {
  return ,@($Resources | ForEach-Object {
    $resource = $_.resourceAppId.ToLowerInvariant()
    foreach ($access in $_.resourceAccess) { "$resource/$($access.type)/$($access.id.ToLowerInvariant())" }
  } | Sort-Object -CaseSensitive)
}

function Test-EntraExactKey($Actual, $Expected) {
  return $Actual.key -ceq $Expected.key -and $Actual.usage -ceq 'Verify' -and $Actual.type -ceq 'AsymmetricX509Cert' -and
    ([DateTimeOffset] $Actual.startDateTime) -eq ([DateTimeOffset] $Expected.startDateTime) -and
    ([DateTimeOffset] $Actual.endDateTime) -eq ([DateTimeOffset] $Expected.endDateTime)
}

function Get-EntraPublicKey($Capability) {
  if (-not $Capability.ContainsKey('publicCertificatePath')) { throw 'PREREQUISITE_MISSING' }
  $path = $Capability.publicCertificatePath
  if (-not [System.IO.Path]::IsPathFullyQualified($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw 'PREREQUISITE_MISSING'
  }
  if ((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'INVALID_OR_UNSAFE' }
  $bytes = [System.IO.File]::ReadAllBytes($path)
  if ($bytes.Length -gt 65536) { throw 'INVALID_OR_UNSAFE' }
  $text = [System.Text.Encoding]::UTF8.GetString($bytes)
  if ($text -match 'PRIVATE KEY') { throw 'INVALID_OR_UNSAFE' }
  $cert = $null
  try {
    $cert = if ($text.Contains('-----BEGIN CERTIFICATE-----')) {
      [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($text)
    } else {
      if ([System.Security.Cryptography.X509Certificates.X509Certificate2]::GetCertContentType($bytes) -ne [System.Security.Cryptography.X509Certificates.X509ContentType]::Cert) { throw 'INVALID_OR_UNSAFE' }
      [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes)
    }
    $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cert.RawData)).ToLowerInvariant()
    if ($cert.HasPrivateKey -or $hash -cne $Capability.publicCertificateSha256 -or
        $cert.NotBefore.ToUniversalTime() -gt [datetime]::UtcNow -or $cert.NotAfter.ToUniversalTime() -le [datetime]::UtcNow) {
      throw 'INVALID_OR_UNSAFE'
    }
    return @{
      keyId = ([guid]::new([byte[]]([System.Security.Cryptography.SHA256]::HashData($cert.RawData)[0..15]))).ToString()
      displayName = "AuditSphere/$($Capability.kind)/$($hash.Substring(0,32))"
      type = 'AsymmetricX509Cert'; usage = 'Verify'; key = [Convert]::ToBase64String($cert.RawData)
      customKeyIdentifier = [Convert]::ToBase64String($cert.GetCertHash())
      startDateTime = $cert.NotBefore.ToUniversalTime().ToString('o')
      endDateTime = $cert.NotAfter.ToUniversalTime().ToString('o')
    }
  } catch { throw 'INVALID_OR_UNSAFE' }
  finally { if ($cert) { $cert.Dispose() } }
}

function Invoke-EntraReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $Approve, [switch] $CertificatesOnly) {
  $publicKeys = @{}
  foreach ($capability in $Plan.capabilities) {
    $publicKeys[$capability.kind] = Get-EntraPublicKey $capability
  }
  foreach ($capability in $Plan.capabilities) {
    $marker = Get-EntraOwnershipMarker $Plan $capability
    $name = if ($capability.ContainsKey('displayName')) { $capability.displayName } else { "$($Plan.ownershipTag) $($capability.kind)" }
    $identityFilter = if ($capability.clientId) { "appId eq '$($capability.clientId)'" } else { "displayName eq '$($name.Replace("'", "''"))'" }
    $filter = [uri]::EscapeDataString($identityFilter)
    $apps = Get-EntraCollection $Transport "/v1.0/applications?`$filter=$filter&`$select=id,appId,tags,displayName"
    if ($apps.Count -gt 1) { throw 'INVALID_OR_UNSAFE' }
    $app = if ($apps.Count) { $apps[0] } else { $null }
    if ($app -and ($marker -cnotin $app.tags -or
        ($capability.clientId -and $app.appId -ne $capability.clientId) -or
        ($capability.ContainsKey('applicationObjectId') -and $app.id -ne $capability.applicationObjectId))) {
      throw 'INVALID_OR_UNSAFE'
    }
    # Fixed IDs mean "manage this existing registration", never create a replacement identity.
    if (-not $app -and ($capability.clientId -or $CertificatesOnly -or $Mode -eq 'ReadOnly')) { throw 'DRIFT' }
    $resourcePrincipals = @{}
    $desired = Get-EntraRequestedResources $Plan $capability $Transport $resourcePrincipals
    $redirects = if ($capability.ContainsKey('redirectUris')) { @($capability.redirectUris) }
      elseif ($capability.kind -eq 'LOGIN') { @($Plan.redirectUris) } else { @() }
    $created = $false
    if (-not $app) {
      $body = @{ displayName = $name; signInAudience = 'AzureADMyOrg'; tags = @($marker)
        web = @{ redirectUris = $redirects }; requiredResourceAccess = $desired }
      $app = Invoke-EntraWrite $Result $Transport $Approve 'POST' '/v1.0/applications' $body
      $created = $true
    }
    $id = $app.id; $clientId = $app.appId
    $app = Invoke-EntraRead $Transport "/v1.0/applications/$id`?`$select=id,appId,tags,displayName,signInAudience,web,requiredResourceAccess,keyCredentials"
    if ($app.id -ne $id -or $app.appId -ne $clientId -or $marker -cnotin $app.tags) { throw 'INVALID_OR_UNSAFE' }
    if ($created) { $Result.mutationsConfirmed++ }
    # A declaration is not authority. Refuse an already overprivileged runtime
    # principal before introducing a new credential, without changing its grants.
    $principals = Get-EntraCollection $Transport "/v1.0/servicePrincipals?`$filter=appId%20eq%20'$clientId'&`$select=id,appId,appOwnerOrganizationId,tags"
    if ($principals.Count -gt 1) { throw 'INVALID_OR_UNSAFE' }
    if (-not $principals.Count -and $capability.ContainsKey('servicePrincipalObjectId')) { throw 'DRIFT' }
    if ($principals.Count) {
      $principal = $principals[0]
      if ($principal.appId -ne $clientId -or $principal.appOwnerOrganizationId -ne $Plan.tenantId -or $marker -cnotin $principal.tags -or
          ($capability.ContainsKey('servicePrincipalObjectId') -and $principal.id -ne $capability.servicePrincipalObjectId)) { throw 'INVALID_OR_UNSAFE' }
      Assert-EntraAssignedRoles $principal $desired $resourcePrincipals $Transport
    }
    $currentRoles = Get-EntraRoleKeys $app.requiredResourceAccess
    $desiredRoles = Get-EntraRoleKeys $desired
    if (@($currentRoles | Where-Object { $_ -cnotin $desiredRoles }).Count) { throw 'DRIFT' }
    if ($CertificatesOnly -and ($app.signInAudience -ne 'AzureADMyOrg' -or
        (ConvertTo-Json $currentRoles -Compress) -cne (ConvertTo-Json $desiredRoles -Compress))) { throw 'DRIFT' }
    if (-not $CertificatesOnly) {
      $changed = $app.signInAudience -ne 'AzureADMyOrg' -or
        (ConvertTo-Json @($app.web.redirectUris | Sort-Object) -Compress) -cne (ConvertTo-Json @($redirects | Sort-Object) -Compress) -or
        (ConvertTo-Json $currentRoles -Compress) -cne (ConvertTo-Json $desiredRoles -Compress)
      if ($changed) {
        if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
        Invoke-EntraWrite $Result $Transport $Approve 'PATCH' "/v1.0/applications/$id" @{
          signInAudience = 'AzureADMyOrg'; web = @{ redirectUris = $redirects }; requiredResourceAccess = $desired
        } | Out-Null
        $readback = Invoke-EntraRead $Transport "/v1.0/applications/$id`?`$select=id,appId,tags,signInAudience,web,requiredResourceAccess,keyCredentials"
        if ($readback.signInAudience -ne 'AzureADMyOrg' -or $marker -cnotin $readback.tags -or $readback.appId -ne $clientId -or
            (ConvertTo-Json @($readback.web.redirectUris | Sort-Object) -Compress) -cne (ConvertTo-Json @($redirects | Sort-Object) -Compress) -or
            (ConvertTo-Json (Get-EntraRoleKeys $readback.requiredResourceAccess) -Compress) -cne (ConvertTo-Json $desiredRoles -Compress)) { throw 'DRIFT' }
        $app = $readback; $Result.mutationsConfirmed++
      }
    }
    $key = $publicKeys[$capability.kind]
    $same = @($app.keyCredentials | Where-Object { $_.keyId -eq $key.keyId -or $_.key -eq $key.key })
    if ($same.Count -gt 1) { throw 'INVALID_OR_UNSAFE' }
    if ($same.Count -eq 1 -and -not (Test-EntraExactKey $same[0] $key)) { throw 'INVALID_OR_UNSAFE' }
    if (-not $same.Count) {
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      # PATCH replaces the collection: keep every old entry (including non-owned keys) for overlap.
      $oldKeys = @($app.keyCredentials)
      if (@($oldKeys | Where-Object { -not $_.key }).Count) { throw 'INCOMPLETE_EVIDENCE' }
      Invoke-EntraWrite $Result $Transport $Approve 'PATCH' "/v1.0/applications/$id" @{ keyCredentials = @($oldKeys) + @($key) } | Out-Null
      $app = Invoke-EntraRead $Transport "/v1.0/applications/$id`?`$select=id,appId,tags,keyCredentials"
      if ($app.appId -ne $clientId -or $marker -cnotin $app.tags -or
          @($app.keyCredentials | Where-Object { Test-EntraExactKey $_ $key }).Count -ne 1) { throw 'DRIFT' }
      foreach ($old in $oldKeys) {
        if (@($app.keyCredentials | Where-Object { $_.keyId -eq $old.keyId -and $_.key -ceq $old.key }).Count -ne 1) { throw 'DRIFT' }
      }
      $Result.mutationsConfirmed++
    }
    if (-not $CertificatesOnly) {
      $createdSp = $false
      if (-not $principals.Count) {
        if ($Mode -eq 'ReadOnly' -or $capability.ContainsKey('servicePrincipalObjectId')) { throw 'DRIFT' }
        $sp = Invoke-EntraWrite $Result $Transport $Approve 'POST' '/v1.0/servicePrincipals' @{ appId = $clientId; tags = @($marker) }
        $principals = @(Invoke-EntraRead $Transport "/v1.0/servicePrincipals/$($sp.id)`?`$select=id,appId,appOwnerOrganizationId,tags")
        $createdSp = $true
      }
      $sp = $principals[0]
      if ($sp.appId -ne $clientId -or $sp.appOwnerOrganizationId -ne $Plan.tenantId -or $marker -cnotin $sp.tags -or
          ($capability.ContainsKey('servicePrincipalObjectId') -and $sp.id -ne $capability.servicePrincipalObjectId)) { throw 'INVALID_OR_UNSAFE' }
    }
    if (-not $CertificatesOnly -and $createdSp) { $Result.mutationsConfirmed++ }
    Add-M365Check $Result "registration:$($capability.kind)" 'PASS' 'owned registration and public certificate' "$id/$clientId sha256:$($capability.publicCertificateSha256)"
    $Result.checks[-1].capability = $capability.kind
    $Result.checks[-1].resourceId = $id
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "graph-readback:$($Plan.tenantId)/$id"
  }
}

Export-ModuleMember -Function Invoke-EntraReconciliation, Test-EntraOperatorContext, Save-EntraMutationIntent, Get-EntraStateDirectory, Get-EntraJournalPath, New-EntraGraphTransport
