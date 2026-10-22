# Exact selected-resource grants. Operator metadata and DOCUMENTS runtime proof are independent.
Set-StrictMode -Version Latest

function Get-SitesId([string] $Id) {
  if ($Id -cnotmatch '\A([a-z0-9-]+\.sharepoint\.com),([0-9a-fA-F-]{36}),([0-9a-fA-F-]{36})\z') { throw 'INVALID_OR_UNSAFE' }
  $collection = [guid]::Empty; $web = [guid]::Empty
  if (-not [guid]::TryParseExact($Matches[2], 'D', [ref] $collection) -or -not [guid]::TryParseExact($Matches[3], 'D', [ref] $web) -or
      $collection -eq [guid]::Empty -or $web -eq [guid]::Empty) { throw 'INVALID_OR_UNSAFE' }
  return @{ id = $Id.ToLowerInvariant(); host = $Matches[1]; collection = $collection.ToString('D'); web = $web.ToString('D') }
}

function Assert-SitesFields($Value, [string[]] $Names) {
  if ($Value -isnot [System.Collections.IDictionary] -or @($Names | Where-Object { -not $Value.Contains($_) }).Count) { throw 'INCOMPLETE_EVIDENCE' }
}

function Assert-SelectedSitesPlan($Plan) {
  if (-not $Plan.ContainsKey('sites') -or -not $Plan.sites.Count -or $Plan.sites.Count -gt 50 -or
      -not $Plan.ContainsKey('negativeControls') -or -not $Plan.negativeControls.ContainsKey('siteIds') -or
      -not $Plan.negativeControls.siteIds.Count -or $Plan.negativeControls.siteIds.Count -gt 50) { throw 'INVALID_OR_UNSAFE' }
  $documents = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')
  if ($documents.Count -ne 1 -or -not $documents[0].clientId) { throw 'INVALID_OR_UNSAFE' }
  $ids = @(); $hosts = @()
  foreach ($site in $Plan.sites) {
    $id = Get-SitesId $site.siteId; $uri = [uri] $site.siteUrl
    if ($site.permission -cne 'write' -or $uri.Scheme -cne 'https' -or $uri.Host -cne $id.host -or -not $uri.IsDefaultPort -or
        $uri.UserInfo -or $uri.Query -or $uri.Fragment -or -not $uri.AbsolutePath.StartsWith('/sites/') -or
        $site.ownershipMarker -isnot [string] -or [string]::IsNullOrWhiteSpace($site.ownershipMarker) -or $site.ownershipMarker -match '[\x00-\x1f\x7f]') { throw 'INVALID_OR_UNSAFE' }
    if ($site.ContainsKey('clientId')) {
      $client = [guid] $site.clientId
      if ($client -eq [guid]::Empty -or $site.ownershipMarker -cne "AuditSphere:$($Plan.firmId.ToLowerInvariant()):$($client.ToString('D'))" -or
          -not $uri.AbsolutePath.EndsWith('-' + $client.ToString('N'), [StringComparison]::Ordinal)) { throw 'INVALID_OR_UNSAFE' }
    }
    $ids += $id.id; $hosts += $id.host
  }
  if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'INVALID_OR_UNSAFE' }
  $controls = @(); $controlHosts = @()
  foreach ($control in $Plan.negativeControls.siteIds) {
    $id = Get-SitesId $control
    if ($id.id -cin $ids -or $id.host -cnotin $hosts) { throw 'INVALID_OR_UNSAFE' }
    $controls += $id.id; $controlHosts += $id.host
  }
  if (@($controls | Select-Object -Unique).Count -ne $controls.Count -or @($hosts | Where-Object { $_ -cnotin $controlHosts }).Count) { throw 'INVALID_OR_UNSAFE' }
}

function Assert-SitesObservation($Plan, [string] $Id, $Observed, $ApprovedSite = $null) {
  Assert-SitesFields $Observed @('id','webUrl','sharepointIds')
  $expected = Get-SitesId $Id; $actual = Get-SitesId $Observed.id
  Assert-SitesFields $Observed.sharepointIds @('tenantId','siteId','webId')
  $uri = [uri] $Observed.webUrl
  if ($actual.id -cne $expected.id -or $uri.Scheme -cne 'https' -or $uri.Host -cne $expected.host -or -not $uri.IsDefaultPort -or
      $uri.UserInfo -or $uri.Query -or $uri.Fragment -or -not $uri.AbsolutePath.StartsWith('/sites/') -or
      $Observed.sharepointIds.tenantId -ne $Plan.tenantId -or $Observed.sharepointIds.siteId -ne $expected.collection -or
      $Observed.sharepointIds.webId -ne $expected.web) { throw 'INVALID_OR_UNSAFE' }
  if ($ApprovedSite) {
    Assert-SitesFields $Observed @('description')
    if (-not [string]::Equals($uri.AbsoluteUri.TrimEnd('/'), $ApprovedSite.siteUrl.TrimEnd('/'), [StringComparison]::OrdinalIgnoreCase) -or
        $Observed.description -cne $ApprovedSite.ownershipMarker) { throw 'INVALID_OR_UNSAFE' }
  }
}

function Get-SitesJournalPath($Plan, [string] $StateDirectory = $null) {
  return Join-Path (Get-EntraStateDirectory $StateDirectory) "$($Plan.tenantId.ToLowerInvariant())-$($Plan.ownershipTag.ToLowerInvariant())-sites.json"
}

function Save-SitesMutationIntent($Journal, [string] $JournalPath, [string] $Method, [string] $Path, $Body, [string] $PlanSha256) {
  Save-EntraMutationIntent $Journal $JournalPath $Method $Path $Body $PlanSha256
}
function Confirm-SitesMutationIntent($Journal, [string] $JournalPath, [string] $Path, $Body) {
  Confirm-EntraMutationIntent $Journal $JournalPath $Path $Body
}

function Get-SitesPermissions([scriptblock] $Transport, [string] $SiteId) {
  $expected = "/v1.0/sites/$SiteId/permissions"
  $read = { param($method,$path,$body)
    if ($method -cne 'GET' -or $path.Split('?')[0] -cne $expected) { throw 'INVALID_OR_UNSAFE' }
    return & $Transport $method $path $body
  }.GetNewClosure()
  return ,(Get-EntraCollection $read $expected)
}

function Get-DocumentsSiteGrant($Permissions, [string] $ClientId) {
  $owned = @()
  foreach ($permission in $Permissions) {
    Assert-SitesFields $permission @('id','roles')
    $applications = @(); $human = $false
    foreach ($field in @('grantedToIdentities','grantedToIdentitiesV2')) {
      if (-not $permission.ContainsKey($field)) { continue }
      foreach ($identity in $permission[$field]) {
        if ($identity.ContainsKey('application') -and $identity.application) { $applications += $identity.application.id }
        if ($identity.ContainsKey('user') -and $identity.user) { $human = $true }
      }
    }
    if ($ClientId -in $applications) {
      if ($human -or @($applications | Where-Object { $_ -ne $ClientId }).Count -or
          $permission.roles -isnot [System.Collections.IList] -or $permission.roles.Count -ne 1 -or $permission.roles[0] -cne 'write' -or
          $permission.id -isnot [string] -or [string]::IsNullOrWhiteSpace($permission.id)) { throw 'DRIFT' }
      $owned += $permission
    }
  }
  if ($owned.Count -gt 1) { throw 'INVALID_OR_UNSAFE' }
  return ,$owned
}

function Open-DocumentsRuntimeCertificate($Plan) {
  $capability = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $public = Get-EntraPublicKey $capability
  if (-not $capability.certificateReference.StartsWith('file:', [StringComparison]::Ordinal)) { throw 'PREREQUISITE_MISSING' }
  $path = $capability.certificateReference.Substring(5)
  if (-not [IO.Path]::IsPathFullyQualified($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'PREREQUISITE_MISSING' }
  $item = Get-Item -LiteralPath $path
  if ($item.LinkTarget -or $item.Length -lt 1 -or $item.Length -gt 65536) { throw 'INVALID_OR_UNSAFE' }
  if (-not $IsWindows -and (([int] [IO.File]::GetUnixFileMode($path)) -band 63) -ne 0) { throw 'INVALID_OR_UNSAFE' }
  $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($public.key))
  try {
    try { return [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem($certificate.ExportCertificatePem(), [IO.File]::ReadAllText($path)) }
    catch { throw 'PREREQUISITE_MISSING' }
  } finally { $certificate.Dispose() }
}

function Invoke-DocumentsRuntimeProbe($Plan, $Result, [scriptblock] $Transport, $Context, [string] $CertificateThumbprint) {
  Assert-SelectedSitesPlan $Plan
  $documents = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  if (-not $context -or $context.ClientId -ne $documents.clientId -or $context.TenantId -ne $plan.tenantId -or
      $context.Environment -cne 'Global' -or $context.AuthType -cne 'AppOnly' -or $context.CertificateThumbprint -ne $CertificateThumbprint) { throw 'BLOCKED_BOOTSTRAP' }
  Add-M365Check $result 'runtime:DOCUMENTS:identity' 'PASS' 'exact separate app-only certificate context' 'DOCUMENTS certificate session'
  $result.checks[-1].capability = 'DOCUMENTS'; $result.checks[-1].resourceId = $documents.clientId
  $result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
  $result.checks[-1].evidenceReference = "certificate-context:$($plan.tenantId)/$($documents.clientId)/$($documents.publicCertificateSha256)"
  foreach ($site in $plan.sites) {
    $id = (Get-SitesId $site.siteId).id
    $response = & $transport GET "/v1.0/sites/$id`?`$select=id,webUrl,description,sharepointIds" $null
    if ($response.statusCode -ne 200) { throw 'BLOCKED_BOOTSTRAP' }
    Assert-SitesObservation $plan $id $response.body $site
    Add-M365Check $result "runtime:DOCUMENTS:site:$id" 'PASS' 'approved site read as DOCUMENTS' 'exact site metadata read'
    $result.checks[-1].capability = 'DOCUMENTS'; $result.checks[-1].resourceId = $id
    $result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o'); $result.checks[-1].evidenceReference = "graph-runtime-site:$id"
    $response = & $transport GET "/v1.0/sites/$id/drive/root?`$select=id,folder,webUrl,parentReference" $null
    if ($response.statusCode -ne 200 -or -not $response.body -or -not $response.body.ContainsKey('folder') -or
        $null -eq $response.body.folder -or [string]::IsNullOrWhiteSpace($response.body.id)) { throw 'INCOMPLETE_EVIDENCE' }
    $root = [uri] $response.body.webUrl; $expected = [uri] $site.siteUrl
    if ($root.Scheme -cne 'https' -or $root.Host -cne $expected.Host -or -not $root.IsDefaultPort -or $root.Query -or $root.Fragment -or $root.UserInfo -or
        -not $root.AbsolutePath.StartsWith($expected.AbsolutePath.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) { throw 'INVALID_OR_UNSAFE' }
    Add-M365Check $result "runtime:DOCUMENTS:drive:$id" 'PASS' 'document-library root read as DOCUMENTS' 'approved site default-library namespace read'
    $result.checks[-1].capability = 'DOCUMENTS'; $result.checks[-1].resourceId = $id
    $result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o'); $result.checks[-1].evidenceReference = "graph-runtime-root:$id/$($response.body.id)"
  }
  foreach ($control in $plan.negativeControls.siteIds) {
    $id = (Get-SitesId $control).id
    $response = & $transport GET "/v1.0/sites/$id`?`$select=id" $null
    if ($response.statusCode -ne 403) { throw 'DRIFT' } # 401/404 and permission-list absence are never isolation proof.
    Add-M365Check $result "runtime:DOCUMENTS:denied:$id" 'PASS' 'known same-tenant control returns HTTP 403 as DOCUMENTS' 'HTTP 403'
    $result.checks[-1].capability = 'DOCUMENTS'; $result.checks[-1].resourceId = $id
    $result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o'); $result.checks[-1].evidenceReference = "graph-runtime-denied:$id"
  }
}

function Assert-DocumentsRuntimeResult($Plan, $Proof, $Result) {
  Assert-SitesFields $Proof @('schemaVersion','repositoryCommit','mode','status','tenantId','planSha256','mutationsAttempted','mutationsConfirmed','checks','networkCalls')
  if ($Proof.schemaVersion -ne 1 -or $Proof.repositoryCommit -cne $Plan.repositoryCommit -or $Proof.mode -cne 'ReadOnly' -or
      $Proof.tenantId -ne $Plan.tenantId -or $Proof.planSha256 -cne (Get-M365PlanSha256 $Plan) -or
      $Proof.mutationsAttempted -ne 0 -or $Proof.mutationsConfirmed -ne 0) { throw 'INVALID_OR_UNSAFE' }
  if ($Proof.status -cne 'VERIFIED') {
    if ($Proof.status -cin @('BLOCKED_BOOTSTRAP','PREREQUISITE_MISSING','PROVIDER_FAILURE','DRIFT','INCOMPLETE_EVIDENCE','INVALID_OR_UNSAFE')) { throw $Proof.status }
    throw 'INCOMPLETE_EVIDENCE'
  }
  $documentId = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0].clientId
  $required = @{ 'runtime:DOCUMENTS:identity' = $documentId }
  foreach ($site in $Plan.sites) {
    $id = (Get-SitesId $site.siteId).id
    $required["runtime:DOCUMENTS:site:$id"] = $id; $required["runtime:DOCUMENTS:drive:$id"] = $id
  }
  foreach ($control in $Plan.negativeControls.siteIds) { $id = (Get-SitesId $control).id; $required["runtime:DOCUMENTS:denied:$id"] = $id }
  if ($Proof.checks.Count -ne $required.Count) { throw 'INCOMPLETE_EVIDENCE' }
  $seen = @{}; $now = [DateTimeOffset]::UtcNow
  foreach ($check in $Proof.checks) {
    Assert-SitesFields $check @('id','state','capability','resourceId','observedAtUtc','expiresAtUtc','evidenceReference')
    if (-not $required.ContainsKey($check.id) -or $seen.ContainsKey($check.id) -or $check.state -cne 'PASS' -or
        $check.capability -cne 'DOCUMENTS' -or $check.resourceId -ne $required[$check.id] -or
        ([DateTimeOffset] $check.observedAtUtc) -gt $now -or ([DateTimeOffset] $check.observedAtUtc) -lt $now.AddMinutes(-15) -or
        ([DateTimeOffset] $check.expiresAtUtc) -le $now -or ([DateTimeOffset] $check.expiresAtUtc) -gt $now.AddMinutes(15) -or
        [string]::IsNullOrWhiteSpace($check.evidenceReference)) { throw 'INCOMPLETE_EVIDENCE' }
    $seen[$check.id] = $true
  }
  $Result.checks += @($Proof.checks)
}

function Invoke-SelectedSitesReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $Approve, [scriptblock] $Confirm, [scriptblock] $RuntimeProbe) {
  Assert-SelectedSitesPlan $Plan
  if (-not $RuntimeProbe) { throw 'BLOCKED_BOOTSTRAP' }
  $documents = @($Plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $documentPlan = @{} + $Plan; $documentPlan.capabilities = @($documents)
  # Reuse exact app/SP ownership, manifest, granted role and registered-certificate checks. Never mutate Entra here.
  Invoke-EntraPermissionReconciliation $documentPlan $Result ReadOnly $Transport { throw 'INVALID_OR_UNSAFE' }
  # All exact targets and known controls must exist in the approved tenant before any site grant.
  foreach ($site in $Plan.sites) {
    $id = (Get-SitesId $site.siteId).id
    $observed = & $Transport GET "/v1.0/sites/$id`?`$select=id,webUrl,description,sharepointIds" $null
    Assert-SitesObservation $Plan $id $observed $site
  }
  foreach ($control in $Plan.negativeControls.siteIds) {
    $id = (Get-SitesId $control).id
    $observed = & $Transport GET "/v1.0/sites/$id`?`$select=id,webUrl,sharepointIds" $null
    Assert-SitesObservation $Plan $id $observed
  }
  foreach ($site in $Plan.sites) {
    $id = (Get-SitesId $site.siteId).id; $path = "/v1.0/sites/$id/permissions"
    $grant = Get-DocumentsSiteGrant (Get-SitesPermissions $Transport $id) $documents.clientId
    if (-not $grant.Count) {
      if ($Mode -eq 'ReadOnly') { throw 'DRIFT' }
      $body = @{ roles = @('write'); grantedToIdentities = @(@{ application = @{ id = $documents.clientId } }) }
      if (-not (& $Approve POST $path $body)) { throw 'BLOCKED_APPROVAL' }
      $Result.mutationsAttempted++
      try { & $Transport POST $path $body | Out-Null } catch { throw 'RESULT_UNCERTAIN' }
      $grant = Get-DocumentsSiteGrant (Get-SitesPermissions $Transport $id) $documents.clientId
      if ($grant.Count -ne 1) { throw 'RESULT_UNCERTAIN' }
      $Result.mutationsConfirmed++
      $Result.changes += @{ target = "site:$id"; action = 'GRANT_DOCUMENT_APPLICATION_WRITE'; permissionId = $grant[0].id; clientId = $documents.clientId }
    }
    if ($Mode -eq 'Apply') {
      $body = @{ roles = @('write'); grantedToIdentities = @(@{ application = @{ id = $documents.clientId } }) }
      & $Confirm $path $body | Out-Null
    }
    Add-M365Check $Result "site:$id`:permission" 'PASS' 'one DOCUMENTS application write grant' 'exact grant read back'
    $Result.checks[-1].capability = 'DOCUMENTS'; $Result.checks[-1].resourceId = $id
    $Result.checks[-1].expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(15).ToString('o')
    $Result.checks[-1].evidenceReference = "graph-site-permission:$id/$($grant[0].id)"
  }
  $proof = & $RuntimeProbe
  Assert-DocumentsRuntimeResult $Plan $proof $Result
  foreach ($control in $Plan.negativeControls.siteIds) {
    $id = (Get-SitesId $control).id
    Assert-SitesObservation $Plan $id (& $Transport GET "/v1.0/sites/$id`?`$select=id,webUrl,sharepointIds" $null)
  }
  Invoke-EntraPermissionReconciliation $documentPlan $Result ReadOnly $Transport { throw 'INVALID_OR_UNSAFE' }
}


Export-ModuleMember -Function Invoke-DocumentsRuntimeProbe, Get-SitesId, Assert-SelectedSitesPlan, Assert-SitesObservation, Get-SitesJournalPath,
  Save-SitesMutationIntent, Confirm-SitesMutationIntent, Get-DocumentsSiteGrant, Open-DocumentsRuntimeCertificate,
  Assert-DocumentsRuntimeResult, Invoke-SelectedSitesReconciliation
