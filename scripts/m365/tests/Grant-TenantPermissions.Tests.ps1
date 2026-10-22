#Requires -Version 7.4
# Synthetic in-memory Graph transport: no authentication, tenant network, grants or private files.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Entra.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Enrollment.psm1') -Force
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-entra-perms-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($work) | Out-Null
$passed = 0
function Assert([bool] $Condition, [string] $Name) { if (-not $Condition) { throw "assertion:$Name" }; $script:passed++ }
function Copy-Synthetic($Value) { return ,(ConvertTo-Json -InputObject $Value -Depth 32 -Compress | ConvertFrom-Json -AsHashtable -NoEnumerate) }
function Must-Refuse([scriptblock] $Action, [string] $Code) {
  $actual = ''
  try { & $Action | Out-Null } catch { $actual = $_.Exception.Message }
  Assert ($actual -ceq $Code) "expected-$Code-got-$actual"
}
function PublicCertificate([string] $Path) {
  $rsa = [System.Security.Cryptography.RSA]::Create(2048)
  try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=AuditSphere Synthetic Test', $rsa,
      [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
    try {
      [System.IO.File]::WriteAllBytes($Path, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
      return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cert.RawData)).ToLowerInvariant()
    } finally { $cert.Dispose() }
  } finally { $rsa.Dispose() }
}

try {
  $plan = Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json')
  $plan.capabilities = @($plan.capabilities[0]); $cap = $plan.capabilities[0]
  $cap.publicCertificatePath = Join-Path $work 'runtime-public.cer'
  $cap.publicCertificateSha256 = PublicCertificate $cap.publicCertificatePath
  $appId = $cap.clientId
  $appObjId = '00000000-0000-4000-8000-000000000010'
  $spObjId = '00000000-0000-4000-8000-000000000020'
  $cap.applicationObjectId = $appObjId
  $cap.servicePrincipalObjectId = $spObjId

  $marker = "AuditSphere/$($plan.ownershipTag.ToLowerInvariant())/$($plan.firmId.ToLowerInvariant())/$($cap.kind)"
  $certBytes = [System.IO.File]::ReadAllBytes($cap.publicCertificatePath)
  $certB64 = [Convert]::ToBase64String($certBytes)

  $graphResourceAppId = '00000003-0000-0000-c000-000000000000'
  $graphResourceSpId = '00000000-0000-4000-8000-000000000080'
  $userReadAllRoleId = '00000000-0000-4000-8000-000000000081'

  $store = @{
    apps = @{
      $appObjId = @{
        id = $appObjId; appId = $appId; tags = @($marker); displayName = 'AuditSphere Synthetic App'; signInAudience = 'AzureADMyOrg'
        requiredResourceAccess = @(@{ resourceAppId = $graphResourceAppId; resourceAccess = @(@{ id = $userReadAllRoleId; type = 'Role' }) })
        keyCredentials = @(
          @{
            id = [guid]::NewGuid().ToString(); type = 'AsymmetricX509Cert'; usage = 'Verify'
            key = $certB64; startDateTime = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('o')
            endDateTime = [DateTimeOffset]::UtcNow.AddDays(30).ToString('o')
          }
        )
      }
    }
    principals = @{
      $spObjId = @{
        id = $spObjId; appId = $appId; appOwnerOrganizationId = $plan.tenantId; tags = @($marker)
      }
    }
    assignments = [System.Collections.Generic.List[hashtable]]::new()
    writes = 0
  }

  $transport = {
    param($method, $path, $body)
    if ($method -eq 'GET') {
      if ($path.StartsWith('/v1.0/applications?')) {
        $client = [regex]::Match([uri]::UnescapeDataString($path), "appId eq '([^']+)'" ).Groups[1].Value
        return @{ value = @($store.apps.Values | Where-Object appId -eq $client | ForEach-Object { Copy-Synthetic $_ }) }
      }
      if ($path.StartsWith('/v1.0/servicePrincipals?')) {
        $client = [regex]::Match([uri]::UnescapeDataString($path), "appId eq '([^']+)'" ).Groups[1].Value
        if ($client -eq $graphResourceAppId) {
          return @{ value = @(@{
            id = $graphResourceSpId; appId = $graphResourceAppId
            appRoles = @(@{ id = $userReadAllRoleId; value = 'User.Read.All'; isEnabled = $true; allowedMemberTypes = @('Application') })
            oauth2PermissionScopes = @()
          }) }
        }
        return @{ value = @($store.principals.Values | Where-Object appId -eq $client | ForEach-Object { Copy-Synthetic $_ }) }
      }
      if ($path -match '^/v1.0/applications/([^?]+)') { return Copy-Synthetic $store.apps[$Matches[1]] }
      if ($path -match '^/v1.0/servicePrincipals/([^/]+)/appRoleAssignments') {
        $spId = $Matches[1]
        return @{ value = @($store.assignments | Where-Object { $_.principalId -eq $spId } | ForEach-Object { Copy-Synthetic $_ }) }
      }
      if ($path -match '^/v1.0/servicePrincipals/([^?]+)') { return Copy-Synthetic $store.principals[$Matches[1]] }
      throw "unexpected-read:$path"
    }
    $store.writes++
    if ($method -eq 'POST' -and $path -match '^/v1.0/servicePrincipals/([^/]+)/appRoleAssignments$') {
      $asg = Copy-Synthetic $body
      $asg.id = [guid]::NewGuid().ToString()
      if (-not $store.ContainsKey('uncertainBeforeCommit') -or -not $store.uncertainBeforeCommit) { $store.assignments.Add($asg) }
      if ($store.ContainsKey('loseAck') -and $store.loseAck) { throw 'synthetic-lost-ack' }
      return Copy-Synthetic $asg
    }
    throw "unexpected-write:$method $path"
  }

  $approve = { param($method,$path,$body) return $true }

  # ReadOnly throws DRIFT when missing expected role assignment
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'DRIFT'
  Assert ($store.writes -eq 0) 'readonly-throws-drift-no-writes'

  # Apply assigns the missing approved application role
  $applyResult = New-M365Result 'Apply'
  Invoke-EntraPermissionReconciliation $plan $applyResult Apply $transport $approve
  Assert ($store.assignments.Count -eq 1) 'one-assignment-created'
  Assert ($store.assignments[0].principalId -eq $spObjId -and
          $store.assignments[0].resourceId -eq $graphResourceSpId -and
          $store.assignments[0].appRoleId -eq $userReadAllRoleId) 'correct-role-assignment-attributes'
  Assert ($applyResult.mutationsConfirmed -eq 1) 'mutations-confirmed-incremented'
  Assert ($applyResult.checks.Count -ge 1 -and $applyResult.checks[0].state -eq 'PASS') 'permission-check-passed'

  # Idempotent second apply makes zero writes
  $writesBefore = $store.writes
  $secondApplyResult = New-M365Result 'Apply'
  Invoke-EntraPermissionReconciliation $plan $secondApplyResult Apply $transport $approve
  Assert ($store.writes -eq $writesBefore) 'second-apply-makes-zero-new-writes'
  Assert ($store.assignments.Count -eq 1) 'assignments-unchanged'
  Assert ($secondApplyResult.mutationsConfirmed -eq 0) 'zero-mutations-confirmed-on-reapply'

  # ReadOnly now passes without writes
  $roResult = New-M365Result 'ReadOnly'
  Invoke-EntraPermissionReconciliation $plan $roResult ReadOnly $transport { throw 'readonly-write-attempted' }
  Assert ($store.writes -eq $writesBefore) 'readonly-passes-no-writes'
  Assert ($roResult.checks[0].state -eq 'PASS') 'readonly-check-pass'

  # Extra / unapproved role assignment throws DRIFT (no privilege widening)
  $extraAsg = @{ id = [guid]::NewGuid().ToString(); principalId = $spObjId; resourceId = $graphResourceSpId; appRoleId = [guid]::NewGuid().ToString() }
  $store.assignments.Add($extraAsg)
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'DRIFT'
  $store.assignments.Remove($extraAsg) | Out-Null

  # Duplicate assignment of the same role throws INVALID_OR_UNSAFE
  $dupAsg = Copy-Synthetic $store.assignments[0]
  $dupAsg.id = [guid]::NewGuid().ToString()
  $store.assignments.Add($dupAsg)
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'INVALID_OR_UNSAFE'
  $store.assignments.Remove($dupAsg) | Out-Null

  # Mismatched public cert throws DRIFT
  $origCert = $store.apps[$appObjId].keyCredentials[0].key
  $store.apps[$appObjId].keyCredentials[0].key = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('different-cert'))
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'DRIFT'
  $store.apps[$appObjId].keyCredentials[0].key = $origCert

  # Unowned app / wrong tag throws INVALID_OR_UNSAFE
  $origTags = $store.apps[$appObjId].tags
  $store.apps[$appObjId].tags = @('unrelated-tag')
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'INVALID_OR_UNSAFE'
  $store.apps[$appObjId].tags = $origTags

  # A correct granted set cannot hide a stale or excessive manifest declaration.
  $originalDeclarations = $store.apps[$appObjId].requiredResourceAccess
  $store.apps[$appObjId].requiredResourceAccess = @()
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'DRIFT'
  $store.apps[$appObjId].requiredResourceAccess = $originalDeclarations
  $store.apps[$appObjId].signInAudience = 'AzureADMultipleOrgs'
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  $store.apps[$appObjId].signInAudience = 'AzureADMyOrg'

  # CLI entrypoint tests
  $fixedState = Get-EntraStateDirectory
  foreach ($entry in @('Grant-TenantPermissions.ps1')) {
    foreach ($mode in @('Plan', 'WhatIf', 'MissingApproval')) {
      $output = Join-Path $work "$entry-$mode.json"
      $arguments = @(if ($mode -eq 'Plan') { '-DryRun' } elseif ($mode -eq 'WhatIf') { '-Mode'; 'Apply'; '-WhatIf' } else { '-Mode'; 'Apply' })
      & pwsh -NoProfile -NonInteractive -File (Join-Path $root $entry) -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -OutputPath $output @arguments | Out-Null
      $code = $LASTEXITCODE; $envelope = Get-Content -Raw $output | ConvertFrom-Json -AsHashtable
      Assert ($envelope.networkCalls -eq 0 -and $envelope.mutationsAttempted -eq 0) "$entry-no-effects"
      Assert ($(if ($mode -eq 'MissingApproval') { $code -eq 3 -and $envelope.status -eq 'BLOCKED_APPROVAL' }
        else { $code -eq 0 -and $envelope.status -eq 'PLANNED' })) "$entry-$mode"
    }
  }

  # Test-TenantEnrollment tests
  $enrollPlanOut = Join-Path $work 'enroll-plan.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Test-TenantEnrollment.ps1') -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -DryRun -OutputPath $enrollPlanOut | Out-Null
  $code = $LASTEXITCODE; $envelope = Get-Content -Raw $enrollPlanOut | ConvertFrom-Json -AsHashtable
  Assert ($code -eq 0 -and $envelope.status -eq 'PLANNED' -and $envelope.networkCalls -eq 0) 'enrollment-dryrun-planned'

  $enrollApplyOut = Join-Path $work 'enroll-apply.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Test-TenantEnrollment.ps1') -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode Apply -OutputPath $enrollApplyOut | Out-Null
  $code = $LASTEXITCODE; $envelope = Get-Content -Raw $enrollApplyOut | ConvertFrom-Json -AsHashtable
  Assert ($code -eq 4 -and $envelope.status -eq 'INVALID_OR_UNSAFE') 'enrollment-rejects-apply'

  $enrollRoOut = Join-Path $work 'enroll-readonly.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Test-TenantEnrollment.ps1') -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -Mode ReadOnly -OutputPath $enrollRoOut | Out-Null
  $code = $LASTEXITCODE; $envelope = Get-Content -Raw $enrollRoOut | ConvertFrom-Json -AsHashtable
  Assert ($code -eq 3 -and $envelope.status -eq 'BLOCKED_BOOTSTRAP' -and $envelope.errorCode -eq 'enrollment-blocked_bootstrap') 'enrollment-readonly-blocked-bootstrap'

  # Lost acknowledgement is never a reason to repeat a permission POST.
  $retained = Copy-Synthetic $store.assignments[0]
  $store.assignments.Clear(); $store.loseAck = $true
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'RESULT_UNCERTAIN'
  $writesBefore = $store.writes; $store.loseAck = $false
  Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve
  Assert ($store.writes -eq $writesBefore -and $store.assignments.Count -eq 1) 'lost-ack-reconciles-original-without-post'
  $store.assignments.Clear(); $store.loseAck = $true; $store.uncertainBeforeCommit = $true
  $journal = @{}; $journalPath = Join-Path $work 'assignment-journal.json'
  $journalApprove = { param($method,$path,$body) Save-EntraMutationIntent $journal $journalPath $method $path $body ('1' * 64); return $true }
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $journalApprove } 'RESULT_UNCERTAIN'
  $writesBefore = $store.writes
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $journalApprove } 'RESULT_UNCERTAIN'
  Assert ($store.writes -eq $writesBefore) 'unknown-absent-assignment-cannot-be-reposted'
  $secondRole = @{ principalId = $spObjId; resourceId = $graphResourceSpId; appRoleId = '00000000-0000-4000-8000-000000000082' }
  Save-EntraMutationIntent $journal $journalPath POST "/v1.0/servicePrincipals/$spObjId/appRoleAssignments" $secondRole ('2' * 64)
  Assert ($journal.Count -eq 2) 'different-role-has-distinct-intent'
  Must-Refuse { Save-EntraMutationIntent $journal $journalPath POST "/v1.0/servicePrincipals/$spObjId/appRoleAssignments" $secondRole ('3' * 64) } 'RESULT_UNCERTAIN'
  $store.loseAck = $false; $store.uncertainBeforeCommit = $false; $store.assignments.Add($retained)

  # Confirmed intent history allows a later approved plan to repair revocation, never an uncertain repeat.
  $confirmedJournal = @{}; $confirmedPath = Join-Path $work 'confirmed-journal.json'; $reviewedDigest = '4' * 64
  $confirmedApprove = { param($method,$path,$body) Save-EntraMutationIntent $confirmedJournal $confirmedPath $method $path $body $reviewedDigest; return $true }
  $confirm = { param($path,$body) Confirm-EntraMutationIntent $confirmedJournal $confirmedPath $path $body }
  $store.assignments.Clear()
  Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $confirmedApprove $confirm
  $store.assignments.Clear()
  Must-Refuse { Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $confirmedApprove $confirm } 'RESULT_UNCERTAIN'
  $reviewedDigest = '5' * 64
  Invoke-EntraPermissionReconciliation $plan (New-M365Result 'Apply') Apply $transport $confirmedApprove $confirm
  $resolved = @($confirmedJournal.Values)[0]
  Assert ($store.assignments.Count -eq 1 -and $resolved.state -ceq 'CONFIRMED' -and $resolved.priorConfirmedAttempts.Count -eq 1 -and
    $resolved.priorConfirmedAttempts[0].planSha256 -ceq ('4' * 64)) 'new-reviewed-plan-repairs-confirmed-revocation-retaining-original-evidence'

  # The real API contract is the source of human consent evidence, not Graph appRoleAssignments.
  $session = @{ userId = '00000000-0000-4000-8000-000000000099'; firmId = $plan.firmId; generation = '0'; staff = $true }
  function EnrollmentWorkspace {
    return @{
      consentConfigured = $true; directoryConfigured = $true; configuredTenantId = $plan.tenantId; simulation = $false
      workspace = @{
        draftRevision = '1'; setupProgress = @{ consentVerified = $true }
        consentingAdministrator = @{ tenantId = $plan.tenantId; objectId = $session.userId; verifiedAt = [DateTimeOffset]::UtcNow.AddDays(-30).ToString('o') }
        connection = @{
          draftId = '00000000-0000-4000-8000-000000000098'; expectedTenantId = $plan.tenantId
          consentState = 'VERIFIED'; lastAttemptState = 'CONSENT_VERIFIED'; connectionState = 'CONSENT_REQUIRED'
          directoryCapabilityState = 'VERIFIED_RECENT'; directoryLastCheckedAt = [DateTimeOffset]::UtcNow.AddMinutes(-10).ToString('o')
        }
      }
    }
  }
  $workspace = EnrollmentWorkspace; $requests = [System.Collections.Generic.List[string]]::new()
  $api = { param($path)
    $requests.Add($path)
    if ($path -ceq '/api/ui/session') { return $session }
    if ($path -ceq '/api/ui/administration/microsoft365') { return $workspace }
    throw 'unexpected-api-route'
  }
  $enrolled = New-M365Result 'ReadOnly'
  Invoke-M365EnrollmentVerification $plan $enrolled $api
  Assert (($requests -join ',') -ceq '/api/ui/session,/api/ui/administration/microsoft365,/api/ui/session') 'enrollment-brackets-current-admin-api-with-session-checks'
  Assert ($enrolled.checks.Count -eq 1 -and $enrolled.checks[0].state -ceq 'PASS' -and $enrolled.mutationsAttempted -eq 0) 'recorded-human-leg-with-fresh-directory-passes-without-mutation'
  Assert (([DateTimeOffset] $enrolled.checks[0].expiresAtUtc) -le [DateTimeOffset]::UtcNow.AddMinutes(6)) 'observation-does-not-renew-directory-freshness'
  foreach ($case in @('noHuman','grantOnly','simulation','wrongTenant','wrongFirm','revokedSession','staleDirectory','futureDirectory','futureHuman','falseAsText')) {
    $workspace = EnrollmentWorkspace; $before = $session.Clone(); $after = $session.Clone(); $expected = 'INCOMPLETE_EVIDENCE'
    switch ($case) {
      noHuman { $workspace.workspace.consentingAdministrator = $null; $expected = 'BLOCKED_BOOTSTRAP' }
      grantOnly { $workspace.workspace.connection.lastAttemptState = 'RETURNED'; $expected = 'BLOCKED_BOOTSTRAP' }
      simulation { $workspace.simulation = $true; $expected = 'INVALID_OR_UNSAFE' }
      wrongTenant { $workspace.configuredTenantId = $spObjId; $expected = 'INVALID_OR_UNSAFE' }
      wrongFirm { $before.firmId = $spObjId; $expected = 'INVALID_OR_UNSAFE' }
      revokedSession { $after.generation = '1'; $expected = 'DRIFT' }
      staleDirectory { $workspace.workspace.connection.directoryLastCheckedAt = [DateTimeOffset]::UtcNow.AddMinutes(-16).ToString('o') }
      futureDirectory { $workspace.workspace.connection.directoryLastCheckedAt = [DateTimeOffset]::UtcNow.AddMinutes(1).ToString('o') }
      futureHuman { $workspace.workspace.consentingAdministrator.verifiedAt = [DateTimeOffset]::UtcNow.AddMinutes(1).ToString('o') }
      falseAsText { $workspace.simulation = 'false'; $expected = 'INVALID_OR_UNSAFE' }
    }
    Must-Refuse { Assert-M365EnrollmentSnapshot $plan $before $workspace $after (New-M365Result 'ReadOnly') } $expected
  }
  # A process-only cookie reference cannot be substituted with an app-only bearer token or another header.
  $variable = 'AUDITSPHERE_WU019_TEST_' + [guid]::NewGuid().ToString('N').ToUpperInvariant()
  try {
    foreach ($bad in @('Bearer synthetic-app-only-token', ('.AspNetCore.Cookies=one' + "`r`nAuthorization: Bearer two"), '.AspNetCore.Cookies=x; .AspNetCore.Cookies=x', 'other=x')) {
      [Environment]::SetEnvironmentVariable($variable, $bad, 'Process')
      Must-Refuse { Get-M365SessionCookie $variable } 'BLOCKED_BOOTSTRAP'
    }
    [Environment]::SetEnvironmentVariable($variable, '.AspNetCore.Cookies=chunks-2; .AspNetCore.CookiesC1=one; .AspNetCore.CookiesC2=two', 'Process')
    Assert ((Get-M365SessionCookie $variable).StartsWith('.AspNetCore.Cookies=')) 'cookie-chunks-supported-without-token-output'
  } finally { [Environment]::SetEnvironmentVariable($variable, $null, 'Process') }

  # Exercise the actual HTTP adapter with an in-memory handler, not a network or alternate verification mode.
  Add-Type -TypeDefinition @'
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
public sealed class AuditSphereEnrollmentHttpProbe : HttpMessageHandler {
  public int Calls;
  public string Target = "";
  public string Cookie = "";
  public string Method = "";
  public string Payload = "{}";
  public HttpStatusCode Status = HttpStatusCode.OK;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
    Calls++; Target = request.RequestUri.AbsoluteUri; Method = request.Method.Method;
    Cookie = string.Join(";", request.Headers.GetValues("Cookie"));
    var response = new HttpResponseMessage(Status) { Content = new StringContent(Payload, Encoding.UTF8, "application/json") };
    response.Headers.Location = new Uri("https://unrelated.synthetic.test/");
    return Task.FromResult(response);
  }
}
'@
  $handler = [AuditSphereEnrollmentHttpProbe]::new(); $client = [System.Net.Http.HttpClient]::new($handler)
  $client.MaxResponseContentBufferSize = 262144; $client.Timeout = [TimeSpan]::FromSeconds(10)
  try {
    $http = New-M365EnrollmentTransport $client ([uri] $plan.publicBaseUri) '.AspNetCore.Cookies=synthetic' (Get-Command Invoke-M365ProviderRequest)
    Set-M365OfflineMode $false
    $payload = & $http '/api/ui/session'
    Assert ($payload -is [System.Collections.IDictionary] -and $handler.Calls -eq 1 -and $handler.Method -ceq 'GET' -and
      $handler.Target -ceq "$($plan.publicBaseUri)/api/ui/session" -and $handler.Cookie -ceq '.AspNetCore.Cookies=synthetic') 'actual-http-adapter-uses-only-approved-https-origin-cookie-and-get'
    foreach ($status in @(302,401,403)) {
      $handler.Status = $status; $beforeCalls = $handler.Calls
      Must-Refuse { & $http '/api/ui/administration/microsoft365' } 'BLOCKED_BOOTSTRAP'
      Assert ($handler.Calls -eq $beforeCalls + 1) 'auth-failure-or-redirect-is-not-followed-or-retried'
    }
    $beforeCalls = $handler.Calls
    Must-Refuse { & $http '/api/ui/administration/microsoft365/connect' } 'INVALID_OR_UNSAFE'
    Must-Refuse { New-M365EnrollmentTransport $client ([uri] 'http://auditsphere.synthetic.test') '.AspNetCore.Cookies=x' (Get-Command Invoke-M365ProviderRequest) } 'INVALID_OR_UNSAFE'
    Assert ($handler.Calls -eq $beforeCalls) 'unsupported-target-and-insecure-origin-never-dispatched'
    $handler.Status = 200; $handler.Payload = '<html>not consent evidence</html>'
    Must-Refuse { & $http '/api/ui/session' } 'INCOMPLETE_EVIDENCE'
    Set-M365OfflineMode $true; $beforeCalls = $handler.Calls
    Must-Refuse { & $http '/api/ui/session' } 'offline-network-call-refused'
    Assert ($handler.Calls -eq $beforeCalls) 'offline-guard-refuses-even-an-in-memory-http-dispatch'
  } finally { Set-M365OfflineMode $true; $client.Dispose() }

  # Signed approval validation for Grant-TenantPermissions
  $original = Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json')
  $digest = Get-M365PlanSha256 $original
  $approvalKey = [System.Security.Cryptography.RSA]::Create(2048)
  try {
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synthetic Approval', $approvalKey,
      [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $approvalCert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    try {
      $trusted = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($approvalCert.RawData)).ToLowerInvariant()
      $approval = @{
        planSha256 = $digest; tenantId = $original.tenantId; operatorClientId = $original.operator.clientId
        approvalReference = $original.approvalReference; exclusiveCertificateWindow = $false
        stateDirectory = $fixedState; expiresAtUtc = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o')
        action = 'GRANT_TENANT_APPLICATION_ROLES'
      }
      $data = [System.Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json $approval -Compress))
      $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new($data), $true)
      $cms.ComputeSignature([System.Security.Cryptography.Pkcs.CmsSigner]::new($approvalCert))
      $approvalPath = Join-Path $work 'approval-grant.json'; $signaturePath = Join-Path $work 'approval-grant.p7s'
      [System.IO.File]::WriteAllBytes($approvalPath, $data); [System.IO.File]::WriteAllBytes($signaturePath, $cms.Encode())
      $output = Join-Path $work 'approval-valid.json'
      & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-TenantPermissions.ps1') -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') `
        -Mode Apply -ExpectedPlanSha256 $digest -ApprovalReference $original.approvalReference -ApprovalPath $approvalPath `
        -ApprovalSignaturePath $signaturePath -ApprovalSignerSha256 $trusted -OutputPath $output -Confirm:`$false | Out-Null
      $code = $LASTEXITCODE; $envelope = Get-Content -Raw $output | ConvertFrom-Json -AsHashtable
      Assert ($code -eq 7 -and $envelope.status -eq 'PREREQUISITE_MISSING') 'approval-valid-reaches-prereq-missing'
    } finally { $approvalCert.Dispose() }
  } finally { $approvalKey.Dispose() }

  [ordered]@{ unit = 'WU-019'; offline = $true; passed = $passed; failed = 0 } | ConvertTo-Json
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }

exit 0
