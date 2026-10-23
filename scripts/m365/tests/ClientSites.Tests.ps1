#Requires -Version 7.4
# Focused in-memory integration tests for server-owned WU-021 client-site intents.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }

$root = Split-Path $PSScriptRoot -Parent
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $root '../..'))
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Entra.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Enrollment.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.ClientSites.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.ClientSiteIntentHttp.psm1') -Force

Add-Type -TypeDefinition @'
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public sealed class ClientSitesIntentTestHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new Queue<HttpResponseMessage>();
    public readonly List<string> Methods = new List<string>();
    public readonly List<string> Uris = new List<string>();
    public readonly List<string> Cookies = new List<string>();
    public readonly List<string> XsrfHeaders = new List<string>();
    public readonly List<string> Bodies = new List<string>();
    public int ThrowOnDispatch { get; set; }
    public int PostCount { get; private set; }
    public int SendCount => Methods.Count;
    public void Enqueue(HttpResponseMessage response) => responses.Enqueue(response);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Methods.Add(request.Method.Method);
        Uris.Add(request.RequestUri?.AbsoluteUri);
        Cookies.Add(request.Headers.TryGetValues("Cookie", out var cookie) ? string.Join("; ", cookie) : null);
        XsrfHeaders.Add(request.Headers.TryGetValues("X-XSRF-TOKEN", out var xsrf) ? string.Join("; ", xsrf) : null);
        Bodies.Add(request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        if (request.Method == HttpMethod.Post) PostCount++;
        if (ThrowOnDispatch == Methods.Count) throw new HttpRequestException("synthetic lost acknowledgement");
        if (responses.Count == 0) throw new HttpRequestException("synthetic response queue exhausted");
        var response = responses.Dequeue();
        if (response.RequestMessage == null) response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
'@

$script:assertions = 0
$script:firmId = '00000000-0000-4000-8000-000000000002'
$script:tenantId = '00000000-0000-4000-8000-000000000001'
$script:clientId = '00000000-0000-4000-8000-000000000010'
$script:otherClientId = '00000000-0000-4000-8000-000000000011'
$script:userId = '00000000-0000-4000-8000-000000000020'
$script:operationId = '00000000-0000-4000-8000-000000000030'
$script:staffA = '00000000-0000-4000-8000-000000000060'
$script:staffB = '00000000-0000-4000-8000-000000000061'
$script:now = [DateTimeOffset]::UtcNow
$script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-client-sites-intent-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($script:work) | Out-Null

function Assert-That([string] $Name, [bool] $Condition) {
  if (-not $Condition) { throw "ASSERT_FAILED:$Name" }
  $script:assertions++
}

function Assert-Code([string] $Name, [string] $Expected, [scriptblock] $Action) {
  $actual = $null
  try { & $Action | Out-Null } catch { $actual = $_.Exception.Message }
  if ($actual -cne $Expected) { throw "ASSERT_FAILED:$Name expected=$Expected actual=$actual" }
  $script:assertions++
}

function Copy-Synthetic($Value) {
  return ,(ConvertTo-Json -InputObject $Value -Depth 32 -Compress | ConvertFrom-Json -AsHashtable -NoEnumerate)
}

function New-JsonResponse([int] $Status, $Value, [string[]] $SetCookie = @()) {
  $response = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode] $Status)
  if ($null -ne $Value) {
    $body = ConvertTo-Json -InputObject $Value -Depth 24 -Compress
    $response.Content = [System.Net.Http.StringContent]::new($body, [System.Text.Encoding]::UTF8, 'application/json')
  }
  foreach ($cookie in $SetCookie) { $null = $response.Headers.TryAddWithoutValidation('Set-Cookie', $cookie) }
  return $response
}

function New-SessionResponse([string] $Generation = '0', [bool] $WithCsrf = $true,
    [string] $FirmId = $script:firmId, [string] $UserId = $script:userId) {
  $cookies = @()
  if ($WithCsrf) {
    $cookies = @(
      '.AspNetCore.Antiforgery.synthetic=anti-cookie; Path=/; Secure; HttpOnly; SameSite=Strict',
      'XSRF-TOKEN=request%2Dtoken; Path=/; Secure; SameSite=Strict'
    )
  }
  return New-JsonResponse 200 @{ userId = $UserId; firmId = $FirmId; generation = $Generation; staff = $true } $cookies
}

function New-Plan {
  $plan = Copy-Synthetic (Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json'))
  $plan.publicBaseUri = 'https://api.synthetic.test:8443/'
  $plan.clientSites.clientIds = @($script:clientId)
  return $plan
}

function New-Review([string] $Id = $script:clientId, [switch] $Published) {
  $pathClient = $Id.Replace('-','')
  $view = @{
    clientId = $Id
    tenantId = $script:tenantId
    rolloutBoundary = '2026-10-01T00:00:00Z'
    url = "https://synthetic.sharepoint.com/sites/auditsphere-client-$pathClient"
    ownershipMarker = "AuditSphere:$($script:firmId):$Id"
    reviewToken = 'a' * 64
    staffAuthorizationDigest = 'd' * 64
    staffObjectIds = @($script:staffA, $script:staffB)
    eligible = $true
    requiredAction = 'Ready for review.'
    state = 'NOT_REQUESTED'
    membershipState = 'PENDING'
    operationId = $null
    operationState = $null
    receiptState = 'NOT_PUBLISHED'
    publishedReceipt = $null
  }
  if ($Published) {
    $view.state = 'READY'
    $view.membershipState = 'VERIFIED'
    $view.operationId = $script:operationId
    $view.operationState = 'COMPLETED'
    $view.receiptState = 'PUBLISHED'
    $view.publishedReceipt = @{
      operationId = $script:operationId
      digest = 'e' * 64
      completedAt = $script:now.AddMinutes(-2).ToString('o')
      siteId = 'synthetic,site-id'
      driveId = 'synthetic-drive'
      rootItemId = 'synthetic-root'
      staffGroupId = 'synthetic-staff-group'
      staffAuthorizationDigest = 'd' * 64
      managedStaffObjectIds = @($script:staffB, $script:staffA)
      residualAccessReferences = @()
    }
  }
  return $view
}

function Enqueue-Review([ClientSitesIntentTestHandler] $Handler, $View) {
  $Handler.Enqueue((New-JsonResponse 200 $View))
}

function New-Harness($Plan) {
  $handler = [ClientSitesIntentTestHandler]::new()
  $client = [System.Net.Http.HttpClient]::new($handler, $false)
  $client.Timeout = [TimeSpan]::FromSeconds(10)
  $client.MaxResponseContentBufferSize = 262144
  $audit = @{ Dispatches = 0 }
  $provider = { param($dispatch) $null = $audit.Dispatches++; & $dispatch }.GetNewClosure()
  $targets = if ($Plan.clientSites.ContainsKey('clientIds')) { @($Plan.clientSites.clientIds) } else {
    @($Plan.sites | Where-Object { $_.ContainsKey('clientId') } | ForEach-Object { $_.clientId })
  }
  $transport = New-M365ClientSiteIntentTransport $client ([uri] $Plan.publicBaseUri) `
    '.AspNetCore.Cookies=auth-cookie' $targets $provider
  return @{ Handler = $handler; Client = $client; Transport = $transport; Audit = $audit }
}

function New-Result($Plan, [string] $Mode) {
  $result = New-M365Result $Mode
  $result.repositoryCommit = $Plan.repositoryCommit
  $result.tenantId = $Plan.tenantId
  $result.planSha256 = Get-M365PlanSha256 $Plan
  return $result
}

function Invoke-SiteWorkflow($Plan, $Context, [string] $Mode = 'Apply', [scriptblock] $Approve = $null,
    [int] $PollAttempts = 2) {
  if ($null -eq $Approve) { $Approve = { param($clientId) $true } }
  $result = New-Result $Plan $Mode
  $Context.LastResult = $result
  $noDelay = { param($milliseconds) }
  $requested = Invoke-SharePointClientSitesReconciliation $Plan $result $Mode $Context.Transport $Approve $noDelay $PollAttempts 0
  return @{ Result = $result; Requested = $requested }
}

function Create-SignedApproval($Plan, [string] $Action, [datetime] $ExpiresAt, [string] $StateDirectory) {
  $rsa = [System.Security.Cryptography.RSA]::Create(2048)
  try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
      'CN=AuditSphere synthetic WU-021 signer', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
      [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(7))
    try {
      $fingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($certificate.RawData)).ToLowerInvariant()
      $payload = [ordered]@{
        planSha256 = Get-M365PlanSha256 $Plan
        tenantId = $Plan.tenantId
        operatorClientId = $Plan.operator.clientId
        approvalReference = $Plan.approvalReference
        expiresAtUtc = $ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        action = $Action
        stateDirectory = Get-EntraStateDirectory $StateDirectory
      }
      $bytes = [System.Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $payload -Depth 8))
      $approvalPath = Join-Path $script:work "approval-$([guid]::NewGuid().ToString('N')).json"
      [System.IO.File]::WriteAllBytes($approvalPath, $bytes)
      $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new($bytes), $true)
      $signer = [System.Security.Cryptography.Pkcs.CmsSigner]::new($certificate)
      $signer.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
      $cms.ComputeSignature($signer)
      $signaturePath = "$approvalPath.sig"
      [System.IO.File]::WriteAllBytes($signaturePath, $cms.Encode())
      return @{ ApprovalPath = $approvalPath; SignaturePath = $signaturePath; Fingerprint = $fingerprint }
    }
    finally { $certificate.Dispose() }
  }
  finally { $rsa.Dispose() }
}

function Invoke-ClientSiteCli([string] $Entry, [string[]] $Arguments, [string] $PlanPath = (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json')) {
  $output = Join-Path $script:work ("result-$([guid]::NewGuid().ToString('N')).json")
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root $Entry) -PlanPath $PlanPath `
    -OutputPath $output @Arguments | Out-Null
  $exitCode = $LASTEXITCODE
  $result = Get-Content -Raw -LiteralPath $output | ConvertFrom-Json -AsHashtable
  return @{ ExitCode = $exitCode; Result = $result }
}

try {
  $plan = New-Plan
  Assert-ClientSitesPlan $plan
  Assert-That 'client-site-plan-validates' $true

  # Offline plans and -WhatIf exit before reading the supplied environment variable name.
  foreach ($entry in @('Configure-SharePointClientSites.ps1','Reconcile-ClientSiteStaff.ps1')) {
    $offlineResult = Invoke-ClientSiteCli -Entry $entry -Arguments @('-DryRun','-SessionCookieEnv','AS_CLIENT_SITES_INTENT_TEST_COOKIE')
    Assert-That "$entry-dryrun-is-planned" ($offlineResult.ExitCode -eq 0 -and $offlineResult.Result.status -ceq 'PLANNED' -and
      $offlineResult.Result.networkCalls -eq 0 -and $offlineResult.Result.mutationsAttempted -eq 0)
    $whatIf = Invoke-ClientSiteCli -Entry $entry -Arguments @('-Mode','ReadOnly','-WhatIf','-SessionCookieEnv','NOT_A_VALID_ENV_NAME!')
    Assert-That "$entry-whatif-is-offline" ($whatIf.ExitCode -eq 0 -and $whatIf.Result.status -ceq 'PLANNED' -and
      $whatIf.Result.networkCalls -eq 0 -and $whatIf.Result.mutationsAttempted -eq 0)
  }

  # Existing production HTTP factory stays redirect-free, cookie-jar-free, bounded and fixed.
  $production = New-M365EnrollmentHttpClient
  try {
    Assert-That 'production-factory-disables-redirects' (-not $production.Handler.AllowAutoRedirect)
    Assert-That 'production-factory-disables-cookie-jar' (-not $production.Handler.UseCookies)
    Assert-That 'production-factory-timeout-is-ten-seconds' ($production.Client.Timeout -eq [TimeSpan]::FromSeconds(10))
    Assert-That 'production-factory-response-cap-is-fixed' ($production.Client.MaxResponseContentBufferSize -eq 262144)
  }
  finally { $production.Client.Dispose() }

  # Current published state is VERIFIED in ReadOnly, with unchanged staff session and exact API origin port.
  $context = New-Harness $plan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    $run = Invoke-SiteWorkflow $plan $context 'ReadOnly'
    Assert-That 'readonly-current-receipt-is-verified-without-request' ($run.Result.checks.Count -eq 1 -and
      $run.Requested -eq $false -and $context.Handler.PostCount -eq 0 -and $run.Result.checks[0].state -ceq 'PASS')
    Assert-That 'published-evidence-binds-operation-and-receipt-digest' ($run.Result.checks[0].evidenceReference -ceq
      "client-site-operation:$($script:operationId):sha256:$('e' * 64)" -and
      [DateTimeOffset]::Parse($run.Result.checks[0].expiresAtUtc) -eq $script:now.AddMinutes(13))
    Assert-That 'session-generation-zero-and-nondefault-origin-work' ($context.Handler.Uris[0] -ceq 'https://api.synthetic.test:8443/api/ui/session')
    Assert-That 'session-identity-fences-work-and-final-reread' ($context.Handler.SendCount -eq 5 -and
      $context.Handler.Uris[2].EndsWith('/api/ui/session',[StringComparison]::Ordinal) -and
      $context.Handler.Uris[4].EndsWith('/api/ui/session',[StringComparison]::Ordinal))
    Assert-That 'provider-wrapper-dispatches-every-request' ($context.Audit.Dispatches -eq $context.Handler.SendCount)
  }
  finally { $context.Client.Dispose() }

  # Apply sends only the current reviewed token, then waits for the exact published worker receipt.
  $context = New-Harness $plan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review)
    $context.Handler.Enqueue((New-JsonResponse 200 @{ value = @{ clientId = $script:clientId; state = 'PENDING_DISCOVERY'; operationId = $null } }))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    $run = Invoke-SiteWorkflow $plan $context
    $body = ConvertFrom-Json -InputObject $context.Handler.Bodies[2] -AsHashtable
    Assert-That 'apply-success-requires-published-receipt' ($run.Requested -eq $true -and $run.Result.checks.Count -eq 1 -and
      $run.Result.checks[0].state -ceq 'PASS' -and $run.Result.mutationsAttempted -eq 1 -and
      $run.Result.mutationsConfirmed -eq 1)
    Assert-That 'apply-uses-the-current-reviewed-route-and-token' ($context.Handler.Uris[2] -ceq
      "https://api.synthetic.test:8443/api/ui/administration/microsoft365/client-sites/$($script:clientId)/request" -and
      $body.Count -eq 4 -and $body.reviewToken -ceq ('a' * 64) -and $body.reviewed -eq $true -and
      $body.reconcileOnly -eq $false -and $body.reason -ceq "WU-021 signed plan $($run.Result.planSha256)")
    Assert-That 'csrf-is-manual-and-only-posted-after-session' ($context.Handler.Cookies[2] -ceq
      '.AspNetCore.Cookies=auth-cookie; .AspNetCore.Antiforgery.synthetic=anti-cookie' -and
      $context.Handler.XsrfHeaders[2] -ceq 'request-token')
    Assert-That 'intent-change-is-redacted-and-bound-to-final-worker-receipt' ($run.Result.changes.Count -eq 1 -and
      $run.Result.changes[0].target -ceq "client:$($script:clientId)" -and
      $run.Result.changes[0].action -ceq 'REQUEST_CLIENT_SITE_INTENT' -and
      $run.Result.changes[0].previousState -ceq 'NOT_REQUESTED' -and
      $run.Result.changes[0].requestState -ceq 'PENDING_DISCOVERY' -and
      $null -eq $run.Result.changes[0].correlationOperationId -and
      $run.Result.changes[0].receiptOperationId -ceq $script:operationId -and
      $run.Result.changes[0].receiptDigest -ceq ('e' * 64) -and
      (ConvertTo-Json -InputObject $run.Result -Depth 20 -Compress) -notmatch ('a' * 64))
    Assert-That 'single-intent-post-is-never-retried' ($context.Handler.PostCount -eq 1 -and $context.Audit.Dispatches -eq 7)
  }
  finally { $context.Client.Dispose() }

  # Explicit clientIds are the complete allowlist even if legacy sites entries name another client.
  $explicitPlan = Copy-Synthetic $plan
  $explicitPlan.clientSites.clientIds = @($script:clientId)
  $explicitPlan.sites[0].clientId = $script:otherClientId
  Assert-ClientSitesPlan $explicitPlan
  $context = New-Harness $explicitPlan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review)
    $context.Handler.Enqueue((New-JsonResponse 200 @{ value = @{ clientId = $script:clientId; state = 'PENDING_DISCOVERY'; operationId = $null } }))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    $run = Invoke-SiteWorkflow $explicitPlan $context
    Assert-That 'clientids-array-defines-exact-targets' ($run.Requested -and
      $context.Handler.Uris[1].EndsWith("/$($script:clientId)/review",[StringComparison]::Ordinal) -and
      $context.Handler.Uris[1] -notmatch [regex]::Escape($script:otherClientId))
  }
  finally { $context.Client.Dispose() }

  # Missing explicit clientIds falls back only to Plan.sites.clientId.
  $fallbackPlan = Copy-Synthetic $plan
  $fallbackPlan.clientSites.Remove('clientIds')
  $fallbackPlan.sites[0].clientId = $script:otherClientId
  $context = New-Harness $fallbackPlan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review $script:otherClientId -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review $script:otherClientId -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    $run = Invoke-SiteWorkflow $fallbackPlan $context 'ReadOnly'
    Assert-That 'legacy-site-clientid-is-the-only-fallback-target' ($run.Result.checks.Count -eq 1 -and
      $context.Handler.Uris[1].EndsWith("/$($script:otherClientId)/review",[StringComparison]::Ordinal))
  }
  finally { $context.Client.Dispose() }

  # Staff reconciliation uses only staff-review and sends reconcileOnly=true.
  $staffPlan = Copy-Synthetic $plan
  $context = New-Harness $staffPlan
  try {
    $staffInitial = New-Review
    $staffInitial.state = 'READY'; $staffInitial.membershipState = 'PENDING'
    $staffInitial.operationId = $script:operationId; $staffInitial.operationState = 'COMPLETED'
    $staffInitial.staffAuthorizationDigest = 'f' * 64
    $staffInitial.receiptState = 'HISTORICAL'
    $staffInitial.publishedReceipt = (New-Review -Published).publishedReceipt
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler $staffInitial
    $context.Handler.Enqueue((New-JsonResponse 200 @{ value = @{ clientId = $script:clientId; state = 'PENDING_DISCOVERY'; operationId = $null } }))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    $result = New-Result $staffPlan 'Apply'
    $noDelay = { param($milliseconds) }
    $didRequest = Invoke-ClientSiteStaffReconciliation $staffPlan $result 'Apply' $context.Transport `
      { param($clientId) $true } $noDelay 2 0
    $body = ConvertFrom-Json -InputObject $context.Handler.Bodies[2] -AsHashtable
    Assert-That 'staff-route-preserves-reconcile-only-mode' ($didRequest -and
      $context.Handler.Uris[1].EndsWith("/$($script:clientId)/staff-review",[StringComparison]::Ordinal) -and
      $body.reconcileOnly -eq $true -and $result.checks.Count -eq 1 -and
      $result.mutationsAttempted -eq 1 -and $result.mutationsConfirmed -eq 1 -and
      $result.changes[0].previousState -ceq 'READY' -and $result.changes[0].receiptOperationId -ceq $script:operationId)
  }
  finally { $context.Client.Dispose() }

  # Pending discovery is polled within a finite bound and never treated as provider success.
  $context = New-Harness $plan
  try {
    $pending = New-Review
    $pending.state = 'REQUESTED'
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler $pending
    Enqueue-Review $context.Handler $pending
    Enqueue-Review $context.Handler $pending
    $context.Handler.Enqueue((New-SessionResponse))
    Assert-Code 'pending-apply-returns-blocked-external' 'BLOCKED_EXTERNAL' { Invoke-SiteWorkflow $plan $context | Out-Null }
    Assert-That 'pending-apply-does-not-post-or-report-pass' ($context.Handler.PostCount -eq 0)
  }
  finally { $context.Client.Dispose() }

  # A valid local acknowledgement records the API intent only; pending worker work remains BLOCKED_EXTERNAL.
  $context = New-Harness $plan
  try {
    $pending = New-Review
    $pending.state = 'REQUESTED'
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review)
    $context.Handler.Enqueue((New-JsonResponse 200 @{ value = @{ clientId = $script:clientId; state = 'PENDING_DISCOVERY'; operationId = $null } }))
    Enqueue-Review $context.Handler $pending
    Enqueue-Review $context.Handler $pending
    $context.Handler.Enqueue((New-SessionResponse))
    Assert-Code 'acknowledged-intent-with-pending-worker-is-blocked-external' 'BLOCKED_EXTERNAL' {
      Invoke-SiteWorkflow $plan $context | Out-Null
    }
    Assert-That 'pending-worker-keeps-only-local-intent-counts' ($context.Handler.PostCount -eq 1 -and
      $context.Handler.SendCount -eq 6 -and $context.LastResult.mutationsAttempted -eq 1 -and
      $context.LastResult.mutationsConfirmed -eq 1 -and $context.LastResult.checks.Count -eq 0 -and
      $context.LastResult.changes[0].receiptOperationId -eq $null -and
      $context.LastResult.changes[0].receiptDigest -eq $null)
  }
  finally { $context.Client.Dispose() }

  $context = New-Harness $plan
  try {
    $pending = New-Review
    $pending.state = 'REQUESTED'
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler $pending
    $context.Handler.Enqueue((New-SessionResponse))
    Assert-Code 'pending-readonly-returns-blocked-external' 'BLOCKED_EXTERNAL' { Invoke-SiteWorkflow $plan $context 'ReadOnly' | Out-Null }
  }
  finally { $context.Client.Dispose() }

  # Historical, stale-digest, changed staff, residual access and stale rollout views cannot verify.
  $receiptCases = @(
    @{ Name = 'historical-receipt'; Change = { param($v) $v.receiptState = 'HISTORICAL'; $v.membershipState = 'PENDING' } },
    @{ Name = 'old-receipt'; Change = { param($v) $v.publishedReceipt.completedAt = $script:now.AddMinutes(-16).ToString('o') } },
    @{ Name = 'changed-staff-digest'; Change = { param($v) $v.staffAuthorizationDigest = 'c' * 64 } },
    @{ Name = 'changed-staff-set'; Change = { param($v) $v.publishedReceipt.managedStaffObjectIds = @($script:staffA) } },
    @{ Name = 'residual-access'; Change = { param($v) $v.publishedReceipt.residualAccessReferences = @('unmanaged-reference') } },
    @{ Name = 'cutoff-mismatch'; Change = { param($v) $v.rolloutBoundary = '2026-10-01T00:00:01Z' } }
  )
  foreach ($case in $receiptCases) {
    $context = New-Harness $plan
    try {
      $invalid = New-Review -Published
      & $case.Change $invalid
      $context.Handler.Enqueue((New-SessionResponse))
      Enqueue-Review $context.Handler $invalid
      $context.Handler.Enqueue((New-SessionResponse))
      $expected = if ($case.Name -ceq 'cutoff-mismatch') { 'DRIFT' } else { 'INCOMPLETE_EVIDENCE' }
      Assert-Code "$($case.Name)-cannot-verify" $expected { Invoke-SiteWorkflow $plan $context 'ReadOnly' | Out-Null }
      Assert-That "$($case.Name)-does-not-write" ($context.Handler.PostCount -eq 0)
    }
    finally { $context.Client.Dispose() }
  }

  # Current tenant, marker and SharePoint host are checked before any request write.
  foreach ($case in @(
    @{ Name = 'tenant-mismatch'; Change = { param($v) $v.tenantId = $script:otherClientId } },
    @{ Name = 'marker-mismatch'; Change = { param($v) $v.ownershipMarker = 'AuditSphere:wrong' } },
    @{ Name = 'host-mismatch'; Change = { param($v) $v.url = $v.url.Replace('synthetic.sharepoint.com','other.sharepoint.com') } }
  )) {
    $context = New-Harness $plan
    try {
      $invalid = New-Review
      & $case.Change $invalid
      $context.Handler.Enqueue((New-SessionResponse))
      Enqueue-Review $context.Handler $invalid
      $context.Handler.Enqueue((New-SessionResponse))
      Assert-Code "$($case.Name)-blocks-before-post" 'INVALID_OR_UNSAFE' { Invoke-SiteWorkflow $plan $context | Out-Null }
      Assert-That "$($case.Name)-never-posts" ($context.Handler.PostCount -eq 0)
    }
    finally { $context.Client.Dispose() }
  }

  # The initial session is checked before review/POST, including its firm binding.
  $context = New-Harness $plan
  try {
    $context.Handler.Enqueue((New-SessionResponse '0' $true $script:otherClientId))
    $context.Handler.Enqueue((New-SessionResponse))
    Assert-Code 'wrong-initial-session-firm-blocks-before-review' 'INVALID_OR_UNSAFE' {
      Invoke-SiteWorkflow $plan $context | Out-Null
    }
    Assert-That 'wrong-initial-session-never-reaches-client-review-or-post' ($context.Handler.SendCount -eq 2 -and
      $context.Handler.PostCount -eq 0 -and $context.Handler.Uris[1].EndsWith('/api/ui/session',[StringComparison]::Ordinal))
  }
  finally { $context.Client.Dispose() }

  # Multi-client final reads detect a receipt that changed after the first pass.
  $multiPlan = Copy-Synthetic $plan
  $multiPlan.clientSites.clientIds = @($script:clientId, $script:otherClientId)
  $context = New-Harness $multiPlan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review $script:clientId -Published)
    Enqueue-Review $context.Handler (New-Review $script:otherClientId -Published)
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review $script:clientId -Published)
    $changed = New-Review $script:otherClientId -Published
    $changedOperationId = '00000000-0000-4000-8000-000000000031'
    $changed.operationId = $changedOperationId
    $changed.publishedReceipt.operationId = $changedOperationId
    Enqueue-Review $context.Handler $changed
    $context.Handler.Enqueue((New-SessionResponse))
    Assert-Code 'final-multi-client-read-detects-receipt-change' 'DRIFT' {
      Invoke-SiteWorkflow $multiPlan $context 'ReadOnly' | Out-Null
    }
    Assert-That 'final-reread-covers-every-target-after-session-fence' ($context.Handler.SendCount -eq 7 -and
      $context.Handler.Uris[3].EndsWith('/api/ui/session',[StringComparison]::Ordinal) -and
      $context.Handler.Uris[5].EndsWith("/$($script:otherClientId)/review",[StringComparison]::Ordinal) -and
      $context.Handler.PostCount -eq 0)
  }
  finally { $context.Client.Dispose() }

  # Missing CSRF blocks before dispatch; a lost POST acknowledgement is uncertain and never retried.
  $context = New-Harness $plan
  try {
    $context.Handler.Enqueue((New-SessionResponse '0' $false))
    Enqueue-Review $context.Handler (New-Review)
    $context.Handler.Enqueue((New-SessionResponse '0' $false))
    Assert-Code 'missing-csrf-blocks-before-dispatch' 'BLOCKED_BOOTSTRAP' { Invoke-SiteWorkflow $plan $context | Out-Null }
    Assert-That 'missing-csrf-sends-no-post' ($context.Handler.PostCount -eq 0 -and
      $context.LastResult.mutationsAttempted -eq 1 -and $context.LastResult.mutationsConfirmed -eq 0)
  }
  finally { $context.Client.Dispose() }

  $context = New-Harness $plan
  try {
    $context.Handler.Enqueue((New-SessionResponse))
    Enqueue-Review $context.Handler (New-Review)
    $context.Handler.Enqueue((New-SessionResponse))
    $context.Handler.ThrowOnDispatch = 3
    Assert-Code 'lost-post-ack-is-result-uncertain' 'RESULT_UNCERTAIN' { Invoke-SiteWorkflow $plan $context | Out-Null }
    Assert-That 'lost-post-ack-is-dispatched-once-and-session-closed' ($context.Handler.PostCount -eq 1 -and
      $context.Handler.SendCount -eq 4 -and $context.Handler.Uris[3].EndsWith('/api/ui/session',[StringComparison]::Ordinal))
    Assert-That 'lost-ack-result-accounting-is-attempted-not-confirmed' ($context.LastResult.mutationsAttempted -eq 1 -and
      $context.LastResult.mutationsConfirmed -eq 0 -and $context.LastResult.changes.Count -eq 0)
  }
  finally { $context.Client.Dispose() }

  # Route/method allowlisting rejects an unapproved client before provider dispatch.
  $context = New-Harness $plan
  try {
    Assert-Code 'unknown-client-target-is-rejected' 'INVALID_OR_UNSAFE' {
      & $context.Transport 'GET' "/api/ui/administration/microsoft365/client-sites/$($script:otherClientId)/review" $null
    }
    Assert-Code 'non-http-method-is-rejected' 'INVALID_OR_UNSAFE' { & $context.Transport 'DELETE' '/api/ui/session' $null }
    Assert-That 'invalid-route-and-method-never-dispatch' ($context.Handler.SendCount -eq 0)
  }
  finally { $context.Client.Dispose() }

  # CMS remains detached, plan-bound, time-bounded and action-specific for each wrapper.
  $basePlan = Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json')
  $basePlan.repositoryCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
  $applyPlanPath = Join-Path $script:work 'wrapper-apply-plan.synthetic.json'
  [System.IO.File]::WriteAllText($applyPlanPath, (ConvertTo-Json -InputObject $basePlan -Depth 32),
    [System.Text.UTF8Encoding]::new($false))
  $basePlan = Read-M365Plan $applyPlanPath (Join-Path $root 'm365-plan.schema.json')
  $stateDirectory = Get-EntraStateDirectory
  $actions = @{
    'Configure-SharePointClientSites.ps1' = 'CONFIGURE_SHAREPOINT_CLIENT_SITES'
    'Reconcile-ClientSiteStaff.ps1' = 'RECONCILE_CLIENT_SITE_STAFF'
  }
  foreach ($entry in $actions.Keys) {
    $action = $actions[$entry]
    $valid = Create-SignedApproval $basePlan $action ([datetime]::UtcNow.AddHours(2)) $stateDirectory
    $approvalArgs = @('-Mode','Apply','-ExpectedPlanSha256',(Get-M365PlanSha256 $basePlan),
      '-ApprovalPath',$valid.ApprovalPath,'-ApprovalSignaturePath',$valid.SignaturePath,
      '-ApprovalSignerSha256',$valid.Fingerprint,'-ApprovalReference',$basePlan.approvalReference,
      '-StateDirectory',$stateDirectory)
    $validResult = Invoke-ClientSiteCli -Entry $entry -Arguments $approvalArgs -PlanPath $applyPlanPath
    $sourceDirty = [bool]((& git -C $repositoryRoot status --porcelain -- scripts/m365) -join '')
    $expectedValidStatus = if ($sourceDirty) { 'INVALID_OR_UNSAFE' } else { 'BLOCKED_BOOTSTRAP' }
    Assert-That "$entry-exact-cms-action-reaches-source-and-bootstrap-gates" ($validResult.Result.status -ceq $expectedValidStatus -and
      $validResult.Result.networkCalls -eq 0 -and $validResult.Result.mutationsAttempted -eq 0)

    $wrong = Create-SignedApproval $basePlan 'UNAPPROVED_ACTION' ([datetime]::UtcNow.AddHours(2)) $stateDirectory
    $wrongArgs = @('-Mode','Apply','-ExpectedPlanSha256',(Get-M365PlanSha256 $basePlan),
      '-ApprovalPath',$wrong.ApprovalPath,'-ApprovalSignaturePath',$wrong.SignaturePath,
      '-ApprovalSignerSha256',$wrong.Fingerprint,'-ApprovalReference',$basePlan.approvalReference,
      '-StateDirectory',$stateDirectory)
    $wrongResult = Invoke-ClientSiteCli -Entry $entry -Arguments $wrongArgs -PlanPath $applyPlanPath
    Assert-That "$entry-wrong-cms-action-is-rejected" ($wrongResult.ExitCode -eq 3 -and
      $wrongResult.Result.status -ceq 'BLOCKED_APPROVAL' -and $wrongResult.Result.networkCalls -eq 0)

    $hashArgs = @('-Mode','Apply','-ExpectedPlanSha256',('0' * 64),
      '-ApprovalPath',$valid.ApprovalPath,'-ApprovalSignaturePath',$valid.SignaturePath,
      '-ApprovalSignerSha256',$valid.Fingerprint,'-ApprovalReference',$basePlan.approvalReference,
      '-StateDirectory',$stateDirectory)
    $hashResult = Invoke-ClientSiteCli -Entry $entry -Arguments $hashArgs -PlanPath $applyPlanPath
    Assert-That "$entry-plan-hash-mismatch-is-rejected" ($hashResult.ExitCode -eq 4 -and
      $hashResult.Result.status -ceq 'INVALID_OR_UNSAFE' -and $hashResult.Result.networkCalls -eq 0)
  }

  [ordered]@{ unit = 'WU-021'; offline = $true; assertions = $script:assertions; failed = 0 } | ConvertTo-Json -Compress
}
finally {
  if (Test-Path -LiteralPath $script:work) {
    Remove-Item -LiteralPath $script:work -Recurse -Force -ErrorAction SilentlyContinue
  }
}

exit 0
