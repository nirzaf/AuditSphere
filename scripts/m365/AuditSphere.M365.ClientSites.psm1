#Requires -Version 7.4
# Thin operator orchestration for server-owned client-site intents. The isolated
# AuditSphere worker remains the only owner of SharePoint and membership effects.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'AuditSphere.M365.ClientSiteIntentHttp.psm1') -Force

function ConvertTo-ClientSiteGuid([object] $Value, [string] $FailureCode = 'INVALID_OR_UNSAFE') {
  $parsed = [guid]::Empty
  if ($Value -isnot [string] -or -not [guid]::TryParseExact($Value, 'D', [ref] $parsed) -or $parsed -eq [guid]::Empty) {
    throw $FailureCode
  }
  return $parsed.ToString('D')
}

function Get-ClientSiteTargets($Plan) {
  if ($Plan -isnot [System.Collections.IDictionary] -or -not $Plan.Contains('clientSites') -or
      $Plan.clientSites -isnot [System.Collections.IDictionary]) { throw 'INVALID_OR_UNSAFE' }
  $clientSites = $Plan.clientSites
  if ($clientSites.Contains('clientIds')) {
    $source = $clientSites.clientIds
    if ($source -isnot [System.Collections.IList] -or $source -is [string]) { throw 'INVALID_OR_UNSAFE' }
  }
  else {
    if (-not $Plan.Contains('sites') -or $Plan.sites -isnot [System.Collections.IList]) { throw 'INVALID_OR_UNSAFE' }
    $source = @($Plan.sites | Where-Object { $_ -is [System.Collections.IDictionary] -and $_.Contains('clientId') } |
      ForEach-Object { $_.clientId })
  }
  if ($source.Count -lt 1 -or $source.Count -gt 50) { throw 'INVALID_OR_UNSAFE' }
  $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  $targets = [System.Collections.Generic.List[string]]::new()
  foreach ($value in $source) {
    $clientId = ConvertTo-ClientSiteGuid $value
    if (-not $seen.Add($clientId)) { throw 'INVALID_OR_UNSAFE' }
    $targets.Add($clientId)
  }
  return ,$targets.ToArray()
}

function ConvertTo-ClientSiteCutoff($Value, [string] $FailureCode) {
  if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
  if ($Value -is [datetime]) {
    if ($Value.Kind -eq [DateTimeKind]::Unspecified) {
      $Value = [DateTime]::SpecifyKind($Value, [DateTimeKind]::Utc)
    }
    return [DateTimeOffset]::new($Value.ToUniversalTime())
  }
  $parsed = [DateTimeOffset]::MinValue
  if ($Value -is [string] -and [DateTimeOffset]::TryParse($Value,
      [System.Globalization.CultureInfo]::InvariantCulture,
      [System.Globalization.DateTimeStyles]::RoundtripKind, [ref] $parsed)) {
    return $parsed.ToUniversalTime()
  }
  throw $FailureCode
}

function Assert-ClientSitesPlan($Plan) {
  if ($Plan -isnot [System.Collections.IDictionary] -or
      @('firmId','tenantId','publicBaseUri','clientSites' | Where-Object { -not $Plan.Contains($_) }).Count -gt 0 -or
      $Plan.clientSites -isnot [System.Collections.IDictionary] -or
      @('host','custodianObjectId','rolloutCutoffUtc' | Where-Object { -not $Plan.clientSites.Contains($_) }).Count -gt 0) {
    throw 'INVALID_OR_UNSAFE'
  }
  $null = ConvertTo-ClientSiteGuid $Plan.firmId
  $null = ConvertTo-ClientSiteGuid $Plan.tenantId
  $null = ConvertTo-ClientSiteGuid $Plan.clientSites.custodianObjectId
  if ($Plan.clientSites.host -isnot [string] -or $Plan.clientSites.host -cnotmatch '\A[a-z0-9-]+\.sharepoint\.com\z') {
    throw 'INVALID_OR_UNSAFE'
  }
  $null = ConvertTo-ClientSiteCutoff $Plan.clientSites.rolloutCutoffUtc 'INVALID_OR_UNSAFE'
  $base = $null
  if ($Plan.publicBaseUri -isnot [string] -or -not [uri]::TryCreate($Plan.publicBaseUri, [System.UriKind]::Absolute, [ref] $base) -or
      $base.Scheme -cne 'https' -or $base.UserInfo -or $base.Query -or $base.Fragment -or $base.AbsolutePath -cne '/' -or
      $base.OriginalString.IndexOfAny([char[]] "`r`n\") -ge 0) { throw 'INVALID_OR_UNSAFE' }
  $null = Get-ClientSiteTargets $Plan
}

function Assert-ClientSiteReviewBinding($Plan, [string] $ClientId, $View) {
  $required = @('clientId','tenantId','rolloutBoundary','url','ownershipMarker','reviewToken',
    'staffAuthorizationDigest','staffObjectIds','eligible','requiredAction','state','membershipState',
    'operationId','operationState','receiptState','publishedReceipt')
  if ($View -isnot [System.Collections.IDictionary] -or @($required | Where-Object { -not $View.Contains($_) }).Count -gt 0) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $client = ConvertTo-ClientSiteGuid $ClientId 'INCOMPLETE_EVIDENCE'
  $tenant = ConvertTo-ClientSiteGuid $Plan.tenantId
  if ((ConvertTo-ClientSiteGuid $View.clientId 'INCOMPLETE_EVIDENCE') -cne $client -or
      (ConvertTo-ClientSiteGuid $View.tenantId 'INCOMPLETE_EVIDENCE') -cne $tenant -or
      $View.ownershipMarker -isnot [string] -or
      $View.ownershipMarker -cne "AuditSphere:$(ConvertTo-ClientSiteGuid $Plan.firmId):$client") { throw 'INVALID_OR_UNSAFE' }

  $viewCutoff = ConvertTo-ClientSiteCutoff $View.rolloutBoundary 'DRIFT'
  $plannedCutoff = ConvertTo-ClientSiteCutoff $Plan.clientSites.rolloutCutoffUtc 'INVALID_OR_UNSAFE'
  if ($viewCutoff.UtcTicks -ne $plannedCutoff.UtcTicks) { throw 'DRIFT' }

  $host = $Plan.clientSites.host
  $siteUri = $null
  if ($View.url -isnot [string] -or $View.url.Length -gt 2048 -or
      -not [uri]::TryCreate($View.url, [System.UriKind]::Absolute, [ref] $siteUri) -or
      $siteUri.Scheme -cne 'https' -or -not $siteUri.IsDefaultPort -or $siteUri.UserInfo -or
      $siteUri.Query -or $siteUri.Fragment -or $siteUri.Host -cne $host -or
      $siteUri.AbsolutePath -cnotmatch "\A/sites/[a-z0-9]+(?:-[a-z0-9]+)*-$($client.Replace('-',''))\z") {
    throw 'INVALID_OR_UNSAFE'
  }
  if ($View.reviewToken -isnot [string] -or $View.reviewToken -cnotmatch '\A[0-9a-fA-F]{64}\z' -or
      $View.staffAuthorizationDigest -isnot [string] -or $View.staffAuthorizationDigest -cnotmatch '\A[0-9a-fA-F]{64}\z' -or
      $View.eligible -isnot [bool] -or $View.state -isnot [string] -or
      $View.membershipState -isnot [string] -or $View.receiptState -isnot [string] -or
      ($null -ne $View.operationState -and $View.operationState -isnot [string])) { throw 'INCOMPLETE_EVIDENCE' }
  if ($View.staffObjectIds -isnot [System.Collections.IList] -or $View.staffObjectIds -is [string] -or $View.staffObjectIds.Count -gt 200) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $staff = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($staffId in $View.staffObjectIds) {
    if (-not $staff.Add((ConvertTo-ClientSiteGuid $staffId 'INCOMPLETE_EVIDENCE'))) { throw 'INCOMPLETE_EVIDENCE' }
  }
  if ($null -ne $View.operationId) { $null = ConvertTo-ClientSiteGuid $View.operationId 'INCOMPLETE_EVIDENCE' }
  return $true
}

function Test-ClientSiteWorkerPending($View) {
  if ($View.state -cin @('REQUESTED','PENDING_DISCOVERY')) { return $true }
  if ($null -ne $View.operationId -and $View.operationState -notin @('COMPLETED','CANCELLED_WITH_DISPOSITION')) { return $true }
  return $false
}

function Test-ClientSitePublished($Plan, [string] $ClientId, $View) {
  try {
    Assert-M365ClientSitePublished $Plan $ClientId $View | Out-Null
    return $true
  }
  catch {
    if ($_.Exception.Message -ceq 'INCOMPLETE_EVIDENCE') { return $false }
    throw
  }
}

function Add-ClientSitePublishedCheck($Result, [string] $ClientId, $View) {
  $operationId = ConvertTo-ClientSiteGuid $View.operationId 'INCOMPLETE_EVIDENCE'
  $receiptOperationId = ConvertTo-ClientSiteGuid $View.publishedReceipt.operationId 'INCOMPLETE_EVIDENCE'
  $digest = $View.publishedReceipt.digest
  if ($digest -isnot [string] -or $digest -cnotmatch '\A[0-9a-fA-F]{64}\z' -or $receiptOperationId -cne $operationId) {
    throw 'INCOMPLETE_EVIDENCE'
  }
  $completedAt = [DateTimeOffset]::Parse($View.publishedReceipt.completedAt,
    [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
  Add-M365Check $Result "client-site:${ClientId}:published" 'PASS' `
    'current dedicated site and exact staff membership in a worker-published receipt' 'verified server state'
  $Result.checks[-1].capability = 'CLIENT_SITES'
  $Result.checks[-1].resourceId = "client:$ClientId"
  $Result.checks[-1].expiresAtUtc = $completedAt.AddMinutes(15).ToString('o')
  $Result.checks[-1].evidenceReference = "client-site-operation:$operationId`:sha256:$($digest.ToLowerInvariant())"
}

function Get-ClientSiteFailureCode($ErrorRecord) {
  $known = @('DRIFT','INVALID_OR_UNSAFE','PROVIDER_FAILURE','BLOCKED_APPROVAL','BLOCKED_BOOTSTRAP',
    'RESULT_UNCERTAIN','PREREQUISITE_MISSING','INCOMPLETE_EVIDENCE','BLOCKED_EXTERNAL')
  if ($ErrorRecord.Exception.Message -cin $known) { return $ErrorRecord.Exception.Message }
  if ($ErrorRecord.Exception.GetType().Name -ceq 'M365PlanException') { return 'INVALID_OR_UNSAFE' }
  return 'PROVIDER_FAILURE'
}

function Invoke-ClientSiteIntentWorkflow($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [ValidateSet('review','staff-review')] [string] $ReviewRoute, [bool] $ReconcileOnly,
    [scriptblock] $Transport, [scriptblock] $RequestApproval,
    [scriptblock] $Delay, [int] $PollAttempts = 6, [int] $PollIntervalMilliseconds = 1000) {
  if ($Transport -isnot [scriptblock] -or $PollAttempts -lt 1 -or $PollAttempts -gt 10 -or
      $PollIntervalMilliseconds -lt 0 -or $PollIntervalMilliseconds -gt 5000) { throw 'INVALID_OR_UNSAFE' }
  Assert-ClientSitesPlan $Plan
  $targets = Get-ClientSiteTargets $Plan
  if ($null -eq $Delay) { $Delay = { param([int] $Milliseconds) Start-Sleep -Milliseconds $Milliseconds } }
  if ($Delay -isnot [scriptblock]) { throw 'INVALID_OR_UNSAFE' }

  $failureCode = $null
  $before = $null
  $requested = $false
  $verified = [System.Collections.Generic.Dictionary[string,object]]::new([System.StringComparer]::Ordinal)
  $changeIndexByClient = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
  $sessionClosed = $false
  try {
    $before = & $Transport 'GET' '/api/ui/session' $null
    # Bind the caller to the signed plan before any review reads or intent POST.
    Assert-M365ClientSiteSession $Plan $before $before | Out-Null
    foreach ($clientId in $targets) {
      $reviewPath = "/api/ui/administration/microsoft365/client-sites/$clientId/$ReviewRoute"
      $view = & $Transport 'GET' $reviewPath $null
      Assert-ClientSiteReviewBinding $Plan $clientId $view | Out-Null
      if (Test-ClientSitePublished $Plan $clientId $view) {
        $verified[$clientId] = @{
          OperationId = ConvertTo-ClientSiteGuid $view.operationId 'INCOMPLETE_EVIDENCE'
          Digest = $view.publishedReceipt.digest.ToLowerInvariant()
        }
        continue
      }

      if ($Mode -eq 'ReadOnly') {
        if (Test-ClientSiteWorkerPending $view) { throw 'BLOCKED_EXTERNAL' }
        throw 'INCOMPLETE_EVIDENCE'
      }

      if (-not (Test-ClientSiteWorkerPending $view)) {
        if (-not $view.eligible) { throw 'BLOCKED_EXTERNAL' }
        if ($RequestApproval -isnot [scriptblock] -or -not (& $RequestApproval $clientId)) { throw 'BLOCKED_APPROVAL' }
        $requestPath = "/api/ui/administration/microsoft365/client-sites/$clientId/request"
        $body = @{
          reviewToken = $view.reviewToken
          reviewed = $true
          reason = "WU-021 signed plan $($Result.planSha256)"
          reconcileOnly = $ReconcileOnly
        }
        # This counts the local AuditSphere intent POST attempt. The worker owns
        # all downstream SharePoint and membership effects.
        $Result.mutationsAttempted++
        try { $ack = & $Transport 'POST' $requestPath $body }
        catch { throw (Get-ClientSiteFailureCode $_) }
        if ($ack -isnot [System.Collections.IDictionary] -or $ack.Count -ne 1 -or -not $ack.Contains('value') -or
            $ack.value -isnot [System.Collections.IDictionary] -or
            @('clientId','state','operationId' | Where-Object { -not $ack.value.Contains($_) }).Count -gt 0 -or
            (ConvertTo-ClientSiteGuid $ack.value.clientId 'RESULT_UNCERTAIN') -cne $clientId -or
            $ack.value.state -cnotin @('PENDING_DISCOVERY','ALREADY_QUEUED')) { throw 'RESULT_UNCERTAIN' }
        if ($ack.value.state -ceq 'ALREADY_QUEUED') {
          if ($null -eq $ack.value.operationId) { throw 'RESULT_UNCERTAIN' }
          $expectedOperationId = ConvertTo-ClientSiteGuid $ack.value.operationId 'RESULT_UNCERTAIN'
        }
        else {
          if ($null -ne $ack.value.operationId) { throw 'RESULT_UNCERTAIN' }
          $expectedOperationId = $null
        }
        $Result.mutationsConfirmed++
        $Result.changes += [ordered]@{
          target = "client:$clientId"
          action = 'REQUEST_CLIENT_SITE_INTENT'
          previousState = $view.state
          requestState = $ack.value.state
          correlationOperationId = $expectedOperationId
          receiptOperationId = $null
          receiptDigest = $null
        }
        $changeIndexByClient[$clientId] = $Result.changes.Count - 1
        $requested = $true
      }
      else {
        $expectedOperationId = if ($null -ne $view.operationId) { ConvertTo-ClientSiteGuid $view.operationId 'INCOMPLETE_EVIDENCE' } else { $null }
      }

      $published = $false
      for ($poll = 1; $poll -le $PollAttempts; $poll++) {
        if ($PollIntervalMilliseconds -gt 0) { & $Delay $PollIntervalMilliseconds }
        $candidate = & $Transport 'GET' $reviewPath $null
        Assert-ClientSiteReviewBinding $Plan $clientId $candidate | Out-Null
        if ($null -ne $expectedOperationId -and $candidate.operationId -ne $expectedOperationId) { continue }
        if (Test-ClientSitePublished $Plan $clientId $candidate) {
          $verified[$clientId] = @{
            OperationId = ConvertTo-ClientSiteGuid $candidate.operationId 'INCOMPLETE_EVIDENCE'
            Digest = $candidate.publishedReceipt.digest.ToLowerInvariant()
          }
          $published = $true
          break
        }
      }
      if (-not $published) { throw 'BLOCKED_EXTERNAL' }
    }

    # Fence the complete review/write phase, then reread every client so an
    # earlier receipt cannot mask drift while a later target is processed.
    $afterWorkflow = & $Transport 'GET' '/api/ui/session' $null
    Assert-M365ClientSiteSession $Plan $before $afterWorkflow | Out-Null
    foreach ($clientId in $targets) {
      $reviewPath = "/api/ui/administration/microsoft365/client-sites/$clientId/$ReviewRoute"
      $finalView = & $Transport 'GET' $reviewPath $null
      Assert-ClientSiteReviewBinding $Plan $clientId $finalView | Out-Null
      if (-not (Test-ClientSitePublished $Plan $clientId $finalView)) {
        if (Test-ClientSiteWorkerPending $finalView) { throw 'BLOCKED_EXTERNAL' }
        throw 'INCOMPLETE_EVIDENCE'
      }
      $finalOperationId = ConvertTo-ClientSiteGuid $finalView.operationId 'INCOMPLETE_EVIDENCE'
      $finalDigest = $finalView.publishedReceipt.digest.ToLowerInvariant()
      if (-not $verified.ContainsKey($clientId) -or
          $verified[$clientId].OperationId -cne $finalOperationId -or
          $verified[$clientId].Digest -cne $finalDigest) { throw 'DRIFT' }

      Add-ClientSitePublishedCheck $Result $clientId $finalView
      if ($changeIndexByClient.ContainsKey($clientId)) {
        $change = $Result.changes[$changeIndexByClient[$clientId]]
        $change.receiptOperationId = $finalOperationId
        $change.receiptDigest = $finalDigest
      }
    }
    $afterFinalRead = & $Transport 'GET' '/api/ui/session' $null
    Assert-M365ClientSiteSession $Plan $before $afterFinalRead | Out-Null
    $sessionClosed = $true
  }
  catch { $failureCode = Get-ClientSiteFailureCode $_ }

  if ($null -ne $before -and -not $sessionClosed) {
    try {
      $after = & $Transport 'GET' '/api/ui/session' $null
      Assert-M365ClientSiteSession $Plan $before $after | Out-Null
    }
    catch {
      if (-not $failureCode) { $failureCode = Get-ClientSiteFailureCode $_ }
    }
  }
  if ($failureCode) { throw $failureCode }
  return $requested
}

function Invoke-SharePointClientSitesReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $RequestApproval, [scriptblock] $Delay,
    [int] $PollAttempts = 6, [int] $PollIntervalMilliseconds = 1000) {
  return Invoke-ClientSiteIntentWorkflow $Plan $Result $Mode 'review' $false $Transport $RequestApproval $Delay `
    $PollAttempts $PollIntervalMilliseconds
}

function Invoke-ClientSiteStaffReconciliation($Plan, $Result, [ValidateSet('ReadOnly','Apply')] [string] $Mode,
    [scriptblock] $Transport, [scriptblock] $RequestApproval, [scriptblock] $Delay,
    [int] $PollAttempts = 6, [int] $PollIntervalMilliseconds = 1000) {
  return Invoke-ClientSiteIntentWorkflow $Plan $Result $Mode 'staff-review' $true $Transport $RequestApproval $Delay `
    $PollAttempts $PollIntervalMilliseconds
}

Export-ModuleMember -Function Assert-ClientSitesPlan, Invoke-SharePointClientSitesReconciliation,
  Invoke-ClientSiteStaffReconciliation
