$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$moduleDirectory = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $moduleDirectory 'AuditSphere.M365.Enrollment.psm1') -Force
Import-Module (Join-Path $moduleDirectory 'AuditSphere.M365.ClientSiteIntentHttp.psm1') -Force

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class ClientSiteIntentQueueHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>();
    public int SendCount { get; private set; }
    public bool ThrowOnNext { get; set; }
    public string LastMethod { get; private set; }
    public string LastUri { get; private set; }
    public string LastCookie { get; private set; }
    public string LastXsrf { get; private set; }
    public string LastBody { get; private set; }
    public void Enqueue(HttpResponseMessage response) => responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SendCount++;
        LastMethod = request.Method.Method;
        LastUri = request.RequestUri?.AbsoluteUri;
        LastCookie = request.Headers.TryGetValues("Cookie", out var cookies) ? String.Join("; ", cookies) : null;
        LastXsrf = request.Headers.TryGetValues("X-XSRF-TOKEN", out var xsrf) ? String.Join("; ", xsrf) : null;
        LastBody = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (ThrowOnNext)
        {
            ThrowOnNext = false;
            throw new HttpRequestException("synthetic lost acknowledgement");
        }
        if (responses.Count == 0) throw new HttpRequestException("no synthetic response queued");
        var response = responses.Dequeue();
        if (response.RequestMessage == null) response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
'@

$script:assertionCount = 0
function Assert-That([string] $Name, [bool] $Condition) {
  if (-not $Condition) { throw "ASSERT_FAILED:$Name" }
  $script:assertionCount++
}

function Assert-ThrowsCode([string] $Name, [string] $ExpectedCode, [scriptblock] $Action) {
  $caught = $false
  try { & $Action }
  catch {
    $caught = $true
    if ($_.Exception.Message -cne $ExpectedCode) {
      throw "ASSERT_FAILED:$Name expected $ExpectedCode, got $($_.Exception.Message)"
    }
  }
  if (-not $caught) { throw "ASSERT_FAILED:$Name did not throw $ExpectedCode" }
  $script:assertionCount++
}

function New-JsonResponse([int] $StatusCode, $Value, [string[]] $SetCookie = @(), [string] $MediaType = 'application/json') {
  $response = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode] $StatusCode)
  if ($null -ne $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 20 -Compress
    $response.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, $MediaType)
  }
  foreach ($cookie in $SetCookie) { $null = $response.Headers.TryAddWithoutValidation('Set-Cookie', $cookie) }
  return $response
}

function New-SessionResponse([switch] $WithCsrf) {
  $cookies = @()
  if ($WithCsrf) {
    $cookies = @(
      '.AspNetCore.Antiforgery.test=anti-cookie-value; Path=/; Secure; HttpOnly; SameSite=Strict',
      'XSRF-TOKEN=request%2Dtoken; Path=/; Secure; SameSite=Strict'
    )
  }
  return New-JsonResponse 200 @{ userId = $script:userId; firmId = $script:firmId; generation = '7'; staff = $true } $cookies
}

function New-TestTransport([ClientSiteIntentQueueHandler] $Handler, [string[]] $AllowedIds = @($script:clientId),
    [scriptblock] $Provider = $null, [uri] $BaseUri = ([uri] 'https://api.example.test/')) {
  $client = [System.Net.Http.HttpClient]::new($Handler, $false)
  $client.Timeout = [TimeSpan]::FromSeconds(10)
  $client.MaxResponseContentBufferSize = 262144
  if ($null -eq $Provider) { $Provider = { param($dispatch) & $dispatch } }
  $transport = New-M365ClientSiteIntentTransport $client $BaseUri `
    '.AspNetCore.Cookies=auth-cookie-value' $AllowedIds $Provider
  return @{ Client = $client; Transport = $transport; Handler = $Handler }
}

function Add-ReviewResponse([ClientSiteIntentQueueHandler] $Handler, [string] $Id = $script:clientId,
    [string] $Token = $script:reviewToken) {
  $Handler.Enqueue((New-JsonResponse 200 @{ clientId = $Id; reviewToken = $Token; eligible = $true }))
}

$script:firmId = '00000000-0000-4000-8000-000000000002'
$script:tenantId = '00000000-0000-4000-8000-000000000001'
$script:clientId = '00000000-0000-4000-8000-000000000010'
$script:otherClientId = '00000000-0000-4000-8000-000000000011'
$script:userId = '00000000-0000-4000-8000-000000000020'
$script:operationId = '00000000-0000-4000-8000-000000000030'
$script:staffA = '00000000-0000-4000-8000-000000000060'
$script:staffB = '00000000-0000-4000-8000-000000000061'
$script:reviewToken = 'a' * 64
$script:staffDigest = 'd' * 64
$script:now = [DateTimeOffset]::UtcNow
$script:plan = @{
  firmId = $script:firmId
  tenantId = $script:tenantId
  clientSites = @{ host = 'synthetic.sharepoint.com' }
}

# The existing production factory is the caller contract: redirects and cookies stay disabled and limits stay fixed.
$productionClient = New-M365EnrollmentHttpClient
try {
  Assert-That 'production-factory-disables-redirects' (-not $productionClient.Handler.AllowAutoRedirect)
  Assert-That 'production-factory-disables-cookie-jar' (-not $productionClient.Handler.UseCookies)
  Assert-That 'production-factory-ten-second-timeout' ($productionClient.Client.Timeout -eq [TimeSpan]::FromSeconds(10))
  Assert-That 'production-factory-response-cap' ($productionClient.Client.MaxResponseContentBufferSize -eq 262144)
}
finally { $productionClient.Client.Dispose() }

# A successful session -> current review -> one reviewed intent request uses only the manual CSRF cookie pair.
$handler = [ClientSiteIntentQueueHandler]::new()
  $audit = @{ Dispatches = 0 }
  $provider = { param($dispatch) $null = $audit.Dispatches++; & $dispatch }.GetNewClosure()
$fixture = New-TestTransport $handler @($script:clientId) $provider
try {
  $handler.Enqueue((New-SessionResponse -WithCsrf))
  Add-ReviewResponse $handler
  $handler.Enqueue((New-JsonResponse 200 @{ value = @{ clientId = $script:clientId; state = 'PENDING_DISCOVERY'; operationId = $null } }))
  $null = & $fixture.Transport 'GET' '/api/ui/session' $null
  $sessionCookie = $handler.LastCookie
  $null = & $fixture.Transport 'GET' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/review" $null
  $requestResult = & $fixture.Transport 'POST' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request" `
    @{ reviewToken = $script:reviewToken; reviewed = $true; reason = 'Reviewed exact current staff access.'; reconcileOnly = $false }
  Assert-That 'all-http-dispatches-use-provider-wrapper' ($audit.Dispatches -eq 3 -and $handler.SendCount -eq 3)
  Assert-That 'session-sends-only-auth-cookie' ($sessionCookie -ceq '.AspNetCore.Cookies=auth-cookie-value')
  Assert-That 'post-target-is-exact-same-origin-route' ($handler.LastMethod -ceq 'POST' -and
    $handler.LastUri -ceq "https://api.example.test/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request")
  Assert-That 'post-cookie-contains-only-auth-and-antiforgery' ($handler.LastCookie -ceq
    '.AspNetCore.Cookies=auth-cookie-value; .AspNetCore.Antiforgery.test=anti-cookie-value')
  Assert-That 'post-xsrf-header-is-decoded-request-token' ($handler.LastXsrf -ceq 'request-token')
  $sentBody = ConvertFrom-Json -InputObject $handler.LastBody -AsHashtable
  Assert-That 'post-body-is-bounded-server-review-contract' ($sentBody.Count -eq 4 -and $sentBody.reviewToken -ceq $script:reviewToken -and
    $sentBody.reviewed -eq $true -and $sentBody.reconcileOnly -eq $false -and $sentBody.reason -ceq 'Reviewed exact current staff access.')
  Assert-That 'request-reports-pending-discovery-without-verifying' ($requestResult.value.state -ceq 'PENDING_DISCOVERY')
  Assert-ThrowsCode 'used-review-token-cannot-be-replayed' 'BLOCKED_BOOTSTRAP' {
    & $fixture.Transport 'POST' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request" `
      @{ reviewToken = $script:reviewToken; reviewed = $true; reason = 'Replay'; reconcileOnly = $false }
  }
}
finally { $fixture.Client.Dispose() }

# Unknown target and method fail before the provider wrapper or HTTP handler can run.
$handler = [ClientSiteIntentQueueHandler]::new()
$fixture = New-TestTransport $handler @($script:clientId)
try {
  Assert-ThrowsCode 'unknown-target-guid-is-rejected' 'INVALID_OR_UNSAFE' {
    & $fixture.Transport 'GET' "/api/ui/administration/microsoft365/client-sites/$($script:otherClientId)/review" $null
  }
  Assert-ThrowsCode 'invalid-method-is-rejected' 'INVALID_OR_UNSAFE' { & $fixture.Transport 'DELETE' '/api/ui/session' $null }
  Assert-That 'invalid-targets-never-dispatch' ($handler.SendCount -eq 0)
}
finally { $fixture.Client.Dispose() }

# Redirects, response-origin mismatches and authentication failures fail closed.
foreach ($case in @(
  @{ Name = 'redirect'; Status = 302; Code = 'BLOCKED_BOOTSTRAP'; Mismatch = $false },
  @{ Name = 'authentication'; Status = 401; Code = 'BLOCKED_BOOTSTRAP'; Mismatch = $false },
  @{ Name = 'csrf-authorization-failure'; Status = 403; Code = 'BLOCKED_BOOTSTRAP'; Mismatch = $false },
  @{ Name = 'missing-route'; Status = 404; Code = 'PREREQUISITE_MISSING'; Mismatch = $false },
  @{ Name = 'final-uri-mismatch'; Status = 200; Code = 'INVALID_OR_UNSAFE'; Mismatch = $true }
)) {
  $handler = [ClientSiteIntentQueueHandler]::new()
  $fixture = New-TestTransport $handler
  try {
    $response = New-JsonResponse $case.Status @{ userId = $script:userId; firmId = $script:firmId; generation = '7'; staff = $true }
    if ($case.Mismatch) { $response.RequestMessage = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, [uri] 'https://other.example.test/api/ui/session') }
    $handler.Enqueue($response)
    Assert-ThrowsCode $case.Name $case.Code { & $fixture.Transport 'GET' '/api/ui/session' $null }
    Assert-That "$($case.Name)-dispatched-once" ($handler.SendCount -eq 1)
  }
  finally { $fixture.Client.Dispose() }
}

# A non-default approved HTTPS API port remains bound to the same exact origin.
$handler = [ClientSiteIntentQueueHandler]::new()
$fixture = New-TestTransport $handler @($script:clientId) $null ([uri] 'https://api.example.test:8443/')
try {
  $handler.Enqueue((New-SessionResponse))
  $null = & $fixture.Transport 'GET' '/api/ui/session' $null
  Assert-That 'approved-api-port-is-retained-in-exact-target' ($handler.LastUri -ceq 'https://api.example.test:8443/api/ui/session')
}
finally { $fixture.Client.Dispose() }

# Only root-path, same-host antiforgery cookies can be retained for a later POST.
foreach ($cookie in @(
  'XSRF-TOKEN=request-token; Path=/private; Secure',
  'XSRF-TOKEN=request-token; Path=/; Domain=other.example.test; Secure'
)) {
  $handler = [ClientSiteIntentQueueHandler]::new()
  $fixture = New-TestTransport $handler
  try {
    $badCookieResponse = New-SessionResponse
    $null = $badCookieResponse.Headers.Remove('Set-Cookie')
    $null = $badCookieResponse.Headers.TryAddWithoutValidation('Set-Cookie', $cookie)
    $handler.Enqueue($badCookieResponse)
    Assert-ThrowsCode 'antiforgery-cookie-scope-is-validated' 'BLOCKED_BOOTSTRAP' {
      & $fixture.Transport 'GET' '/api/ui/session' $null
    }
  }
  finally { $fixture.Client.Dispose() }
}

# No POST is sent until both server-issued antiforgery cookies are captured.
$handler = [ClientSiteIntentQueueHandler]::new()
$fixture = New-TestTransport $handler
try {
  $handler.Enqueue((New-SessionResponse))
  Add-ReviewResponse $handler
  $null = & $fixture.Transport 'GET' '/api/ui/session' $null
  $null = & $fixture.Transport 'GET' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/review" $null
  Assert-ThrowsCode 'missing-csrf-blocks-post-before-dispatch' 'BLOCKED_BOOTSTRAP' {
    & $fixture.Transport 'POST' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request" `
      @{ reviewToken = $script:reviewToken; reviewed = $true; reason = 'Review'; reconcileOnly = $false }
  }
  Assert-That 'missing-csrf-sends-no-write' ($handler.SendCount -eq 2)
}
finally { $fixture.Client.Dispose() }

# An exception after the single POST dispatch is an unknown outcome and is never retried.
$handler = [ClientSiteIntentQueueHandler]::new()
$fixture = New-TestTransport $handler
try {
  $handler.Enqueue((New-SessionResponse -WithCsrf))
  Add-ReviewResponse $handler
  $null = & $fixture.Transport 'GET' '/api/ui/session' $null
  $null = & $fixture.Transport 'GET' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/review" $null
  $handler.ThrowOnNext = $true
  Assert-ThrowsCode 'post-lost-ack-is-uncertain' 'RESULT_UNCERTAIN' {
    & $fixture.Transport 'POST' "/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request" `
      @{ reviewToken = $script:reviewToken; reviewed = $true; reason = 'Review'; reconcileOnly = $false }
  }
  Assert-That 'post-lost-ack-dispatches-exactly-once' ($handler.SendCount -eq 3)
}
finally { $fixture.Client.Dispose() }

# The pure session verifier binds a staff session to the plan firm and rejects a session change.
$before = @{ userId = $script:userId; firmId = $script:firmId; generation = '7'; staff = $true }
$after = @{ userId = $script:userId; firmId = $script:firmId; generation = '7'; staff = $true }
Assert-That 'unchanged-staff-session-verifies' (Assert-M365ClientSiteSession $script:plan $before $after)
$zeroBefore = $before.Clone(); $zeroBefore.generation = '0'
$zeroAfter = $after.Clone(); $zeroAfter.generation = '0'
Assert-That 'zero-session-generation-is-valid' (Assert-M365ClientSiteSession $script:plan $zeroBefore $zeroAfter)
$changed = $after.Clone(); $changed.generation = '8'
Assert-ThrowsCode 'changed-session-generation-is-drift' 'DRIFT' { Assert-M365ClientSiteSession $script:plan $before $changed }
$nonStaff = $after.Clone(); $nonStaff.staff = $false
Assert-ThrowsCode 'nonstaff-session-is-blocked' 'BLOCKED_BOOTSTRAP' { Assert-M365ClientSiteSession $script:plan $before $nonStaff }

function New-PublishedView([DateTimeOffset] $CompletedAt = $script:now.AddMinutes(-2)) {
  return @{
    clientId = $script:clientId
    tenantId = $script:tenantId
    url = "https://synthetic.sharepoint.com/sites/example-$($script:clientId.Replace('-',''))"
    ownershipMarker = "AuditSphere:$($script:firmId):$($script:clientId)"
    staffAuthorizationDigest = $script:staffDigest
    staffObjectIds = @($script:staffA, $script:staffB)
    state = 'READY'
    membershipState = 'VERIFIED'
    operationId = $script:operationId
    operationState = 'COMPLETED'
    receiptState = 'PUBLISHED'
    publishedReceipt = @{
      operationId = $script:operationId
      digest = 'e' * 64
      completedAt = $CompletedAt.ToString('o')
      siteId = 'tenant,site'
      driveId = 'drive-id'
      rootItemId = 'root-item-id'
      staffGroupId = 'staff-group-id'
      staffAuthorizationDigest = $script:staffDigest
      managedStaffObjectIds = @($script:staffB, $script:staffA)
      residualAccessReferences = @()
    }
  }
}

$validView = New-PublishedView
Assert-That 'exact-current-published-receipt-verifies' (Assert-M365ClientSitePublished $script:plan $script:clientId $validView $script:now)
$pendingView = New-PublishedView
$pendingView.state = 'REQUESTED'; $pendingView.membershipState = 'PENDING'
Assert-ThrowsCode 'pending-discovery-never-verifies' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $pendingView $script:now
}
$oldReceipt = New-PublishedView ($script:now.AddMinutes(-15).AddSeconds(-1))
Assert-ThrowsCode 'old-published-receipt-is-stale' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $oldReceipt $script:now
}
$futureReceipt = New-PublishedView ($script:now.AddSeconds(6))
Assert-ThrowsCode 'future-published-receipt-is-rejected' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $futureReceipt $script:now
}
$staleDigest = New-PublishedView
$staleDigest.publishedReceipt.staffAuthorizationDigest = 'c' * 64
Assert-ThrowsCode 'stale-staff-authorization-digest-is-rejected' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $staleDigest $script:now
}
$wrongStaff = New-PublishedView
$wrongStaff.publishedReceipt.managedStaffObjectIds = @($script:staffA)
Assert-ThrowsCode 'managed-staff-set-must-match-current-exactly' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $wrongStaff $script:now
}
$residual = New-PublishedView
$residual.publishedReceipt.residualAccessReferences = @('unmanaged-membership')
Assert-ThrowsCode 'residual-access-blocks-positive-receipt' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $residual $script:now
}
$wrongOperation = New-PublishedView
$wrongOperation.publishedReceipt.operationId = $script:otherClientId
Assert-ThrowsCode 'receipt-operation-must-match-current-view' 'INCOMPLETE_EVIDENCE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $wrongOperation $script:now
}
$wrongHost = New-PublishedView
$wrongHost.url = $wrongHost.url.Replace('synthetic.sharepoint.com', 'other.sharepoint.com')
Assert-ThrowsCode 'receipt-view-must-use-configured-sharepoint-host' 'INVALID_OR_UNSAFE' {
  Assert-M365ClientSitePublished $script:plan $script:clientId $wrongHost $script:now
}

Write-Output "ClientSiteIntentHttp focused assertions: $script:assertionCount passed."
