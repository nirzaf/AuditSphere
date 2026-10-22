#Requires -Version 7.4
# Synthetic in-memory Graph transport: no authentication, tenant network, grants or private files.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Entra.psm1') -Force
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-entra-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($work) | Out-Null
$passed = 0
function Assert([bool] $Condition, [string] $Name) { if (-not $Condition) { throw "assertion:$Name" }; $script:passed++ }
function Copy-Synthetic($Value) { return ,(ConvertTo-Json -InputObject $Value -Depth 32 -Compress | ConvertFrom-Json -AsHashtable -NoEnumerate) }
function Must-Refuse([scriptblock] $Action, [string] $Code) {
  $actual = ''
  try { & $Action } catch { $actual = $_.Exception.Message }
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
  $cap.clientId = $null
  $cap.publicCertificatePath = Join-Path $work 'runtime-public.cer'
  $cap.publicCertificateSha256 = PublicCertificate $cap.publicCertificatePath
  $context = @{ TenantId = $plan.tenantId; ClientId = $plan.operator.clientId; Environment = 'Global' }
  Assert (Test-EntraOperatorContext $context $plan) 'exact-operator-context'
  foreach ($field in @('TenantId','ClientId','Environment')) {
    $wrong = Copy-Synthetic $context; $wrong[$field] = 'different'
    Assert (-not (Test-EntraOperatorContext $wrong $plan)) "refuse-wrong-context-$field"
  }
  $adapterState = @{ calls = 0; guarded = 0; maxRetry = 3; timeout = 300; retryTime = 0; fail = $false; statuses = @(); retryAfter = 0; waits = @() }
  $fakeGraph = { param($Method,$Uri,$OutputType,$Body,$ContentType,[switch] $SkipHttpErrorCheck)
    $adapterState.calls++
    if ($adapterState.maxRetry -ne 0 -or $adapterState.timeout -gt 30 -or $adapterState.timeout -lt 1) { throw 'unsafe-sdk-retry-context' }
    if ($adapterState.fail) { throw 'synthetic-dispatch-failed' }
    $code = if ($adapterState.statuses.Count) { $adapterState.statuses[0] } else { 200 }
    if ($adapterState.statuses.Count) { $adapterState.statuses = @($adapterState.statuses | Select-Object -Skip 1) }
    $reply = [System.Net.Http.HttpResponseMessage]::new([System.Net.HttpStatusCode] $code)
    $reply.Content = [System.Net.Http.StringContent]::new((ConvertTo-Json @{ uri = $Uri; method = $Method; body = $Body; output = $OutputType } -Compress))
    if ($adapterState.retryAfter) { $reply.Headers.RetryAfter = [System.Net.Http.Headers.RetryConditionHeaderValue]::new([TimeSpan]::FromSeconds($adapterState.retryAfter)) }
    return $reply
  }.GetNewClosure()
  $guarded = { param($request) $adapterState.guarded++; & $request }.GetNewClosure()
  $getRetry = { return @{ MaxRetry = $adapterState.maxRetry; RetriesTimeLimit = [TimeSpan]::FromSeconds($adapterState.retryTime); ClientTimeout = [TimeSpan]::FromSeconds($adapterState.timeout) } }.GetNewClosure()
  $setRetry = { param($MaxRetry,$RetriesTimeLimit,$ClientTimeout,[switch] $Confirm)
    $adapterState.maxRetry = $MaxRetry; $adapterState.retryTime = $RetriesTimeLimit; $adapterState.timeout = $ClientTimeout
  }.GetNewClosure()
  $wait = { param($seconds) $adapterState.waits += $seconds }.GetNewClosure()
  $adapter = New-EntraGraphTransport $guarded $fakeGraph $getRetry $setRetry $wait
  $adapted = & $adapter PATCH '/v1.0/applications/synthetic' @{ tags = @('synthetic') }
  Assert ($adapterState.calls -eq 1 -and $adapterState.guarded -eq 1 -and
    $adapted.uri -ceq 'https://graph.microsoft.com/v1.0/applications/synthetic' -and
    $adapted.output -ceq 'HttpResponseMessage' -and ($adapted.body | ConvertFrom-Json).tags[0] -ceq 'synthetic') 'guarded-sdk-adapter'
  Must-Refuse { & $adapter GET 'https://different.test/' $null } 'INVALID_OR_UNSAFE'
  Assert ($adapterState.maxRetry -eq 3 -and $adapterState.timeout -eq 300) 'sdk-context-restored'
  $adapterState.fail = $true; $beforeCalls = $adapterState.calls
  Must-Refuse { & $adapter GET '/v1.0/applications' $null } 'PROVIDER_FAILURE'
  Assert ($adapterState.maxRetry -eq 3 -and $adapterState.timeout -eq 300 -and $adapterState.calls -eq $beforeCalls + 3) 'failed-sdk-call-bounded-and-restores-context'
  $adapterState.fail = $false
  $adapterState.statuses = @(429,200); $adapterState.retryAfter = 2; $adapterState.waits = @()
  $beforeCalls = $adapterState.calls
  & $adapter GET '/v1.0/applications' $null | Out-Null
  Assert ($adapterState.calls -eq $beforeCalls + 2 -and $adapterState.waits.Count -eq 1 -and $adapterState.waits[0] -eq 2) 'get-honors-retry-after'
  $adapterState.statuses = @(429,200); $adapterState.retryAfter = 60; $adapterState.waits = @(); $beforeCalls = $adapterState.calls
  Must-Refuse { & $adapter GET '/v1.0/applications' $null } 'PROVIDER_FAILURE'
  Assert ($adapterState.calls -eq $beforeCalls + 1 -and $adapterState.waits.Count -eq 0) 'retry-after-beyond-deadline-not-truncated'
  $adapterState.statuses = @(503,200); $adapterState.retryAfter = 0; $beforeCalls = $adapterState.calls
  Must-Refuse { & $adapter POST '/v1.0/applications' @{} } 'PROVIDER_FAILURE'
  Assert ($adapterState.calls -eq $beforeCalls + 1) 'write-never-retried'
  $fixedState = Get-EntraStateDirectory
  Assert ((Get-EntraStateDirectory $fixedState) -ceq $fixedState) 'journal-path-stable'
  Must-Refuse { Get-EntraStateDirectory $work } 'INVALID_OR_UNSAFE'
  $recased = Copy-Synthetic $plan; $recased.tenantId = $plan.tenantId.ToUpperInvariant(); $recased.ownershipTag = $plan.ownershipTag.ToUpperInvariant()
  Assert ((Get-EntraJournalPath $recased) -ceq (Get-EntraJournalPath $plan)) 'journal-identity-case-stable'
  $intentPath = Join-Path $work 'intents.json'; $intents = @{}
  $intentBody = @{ tags = @('synthetic-owned-marker') }
  Save-EntraMutationIntent $intents $intentPath POST '/v1.0/applications' $intentBody ('a' * 64)
  $retained = Get-Content -Raw $intentPath | ConvertFrom-Json -AsHashtable
  Assert ($retained.ContainsKey('/v1.0/applications/synthetic-owned-marker')) 'intent-persisted-before-write'
  Must-Refuse { Save-EntraMutationIntent $retained $intentPath POST '/v1.0/applications' $intentBody ('b' * 64) } 'RESULT_UNCERTAIN'
  $store = @{ apps = @{}; principals = @{}; sequence = 0; writes = 0; appPosts = 0; principalPosts = 0; keyPatches = 0; loseAppResponse = $false; disableRole = $false; assignments = @() }
  $transport = {
    param($method, $path, $body)
    if ($method -eq 'GET') {
      if ($path.StartsWith('/v1.0/applications?')) {
        $query = [uri]::UnescapeDataString($path)
        $name = [regex]::Match($query, "displayName eq '([^']+)'" ).Groups[1].Value
        $client = [regex]::Match($query, "appId eq '([^']+)'" ).Groups[1].Value
        return @{ value = @($store.apps.Values | Where-Object { $(if ($client) { $_.appId -eq $client } else { $_.displayName -eq $name }) } | ForEach-Object { Copy-Synthetic $_ }) }
      }
      if ($path.StartsWith('/v1.0/servicePrincipals?')) {
        $appId = [regex]::Match([uri]::UnescapeDataString($path), "appId eq '([^']+)'" ).Groups[1].Value
        if ($appId -eq '00000003-0000-0000-c000-000000000000') {
          return @{ value = @(@{ id = '00000000-0000-4000-8000-000000000080'; appId = $appId
            appRoles = @(@{ id = '00000000-0000-4000-8000-000000000081'; value = 'User.Read.All'; isEnabled = (-not $store.disableRole); allowedMemberTypes = @('Application') })
            oauth2PermissionScopes = @(@{ id = '00000000-0000-4000-8000-000000000082'; value = 'User.Read'; isEnabled = $true }) }) }
        }
        return @{ value = @($store.principals.Values | Where-Object appId -eq $appId | ForEach-Object { Copy-Synthetic $_ }) }
      }
      if ($path -match '^/v1.0/applications/([^?]+)') { return Copy-Synthetic $store.apps[$Matches[1]] }
      if ($path -match '^/v1.0/servicePrincipals/[^/]+/appRoleAssignments') { return @{ value = @($store.assignments) } }
      if ($path -match '^/v1.0/servicePrincipals/([^?]+)') { return Copy-Synthetic $store.principals[$Matches[1]] }
      throw 'unexpected-read'
    }
    $store.writes++
    if ($method -eq 'POST' -and $path -eq '/v1.0/applications') {
      $store.sequence++; $store.appPosts++
      $id = [guid]::NewGuid().ToString(); $app = Copy-Synthetic $body
      $app.id = $id; $app.appId = [guid]::NewGuid().ToString(); $app.keyCredentials = @()
      $store.apps[$id] = $app
      if ($store.loseAppResponse) { $store.loseAppResponse = $false; throw 'synthetic-response-lost' }
      return Copy-Synthetic $app
    }
    if ($method -eq 'POST' -and $path -eq '/v1.0/servicePrincipals') {
      $store.principalPosts++; $id = [guid]::NewGuid().ToString(); $sp = Copy-Synthetic $body
      $sp.id = $id; $sp.appOwnerOrganizationId = $plan.tenantId; $store.principals[$id] = $sp
      return Copy-Synthetic $sp
    }
    if ($method -eq 'PATCH' -and $path -match '^/v1.0/applications/([^?]+)$') {
      $app = $store.apps[$Matches[1]]
      foreach ($key in $body.Keys) { $app[$key] = Copy-Synthetic $body[$key] }
      if ($body.ContainsKey('keyCredentials')) { $store.keyPatches++ }
      return $null
    }
    throw 'unexpected-write'
  }
  $approve = { param($method,$path,$body) return $true }
  $result = New-M365Result 'Apply'
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport $approve } 'DRIFT'
  Assert ($store.writes -eq 0) 'readonly-never-creates-missing-app'
  $store.disableRole = $true
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  Assert ($store.writes -eq 0) 'disabled-role-no-write-or-fallback'
  $store.disableRole = $false
  Invoke-EntraReconciliation $plan $result Apply $transport $approve
  Assert ($store.apps.Count -eq 1 -and $store.principals.Count -eq 1 -and $store.keyPatches -eq 1) 'first-apply-one-app-sp-key'
  $writes = $store.writes
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve
  Assert ($store.writes -eq $writes) 'second-apply-idempotent'
  Invoke-EntraReconciliation $plan (New-M365Result 'ReadOnly') ReadOnly $transport { throw 'readonly-write' }
  Assert ($store.writes -eq $writes) 'readonly-no-writes'
  $app = @($store.apps.Values)[0]; $sp = @($store.principals.Values)[0]
  $cap.clientId = $app.appId; $cap.applicationObjectId = $app.id; $cap.servicePrincipalObjectId = $sp.id
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve
  Assert ($store.writes -eq $writes) 'approved-object-id-reuse'
  $cap.applicationObjectId = [guid]::NewGuid().ToString()
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  $cap.applicationObjectId = $app.id
  $tags = $app.tags; $app.tags = @('not-owned')
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  $app.tags = $tags
  $sp.appOwnerOrganizationId = [guid]::NewGuid().ToString()
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  $sp.appOwnerOrganizationId = $plan.tenantId
  $duplicate = Copy-Synthetic $app; $duplicate.id = [guid]::NewGuid().ToString(); $store.apps[$duplicate.id] = $duplicate
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'INVALID_OR_UNSAFE'
  $store.apps.Remove($duplicate.id)
  Assert ($store.writes -eq $writes) 'unsafe-identities-no-writes'
  $cap.signInScopes = @('User.Read')
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve
  Assert (@($app.requiredResourceAccess[0].resourceAccess | Where-Object type -eq 'Scope').Count -eq 1) 'explicit-signin-scope-declaration'
  $writes = $store.writes
  $store.assignments = @(@{ id = [guid]::NewGuid().ToString(); principalId = $sp.id; resourceId = '00000000-0000-4000-8000-000000000080'; appRoleId = [guid]::NewGuid().ToString() })
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve -CertificatesOnly } 'DRIFT'
  Assert ($store.writes -eq $writes) 'overprivileged-principal-no-credential-write'
  $store.assignments = @()
  # Rotate to a new public cert, retaining an existing key with a different/non-owned label.
  $oldKey = Copy-Synthetic $app.keyCredentials[0]; $app.keyCredentials[0].displayName = 'external-owner-key'
  $cap.publicCertificatePath = Join-Path $work 'replacement-public.cer'
  $cap.publicCertificateSha256 = PublicCertificate $cap.publicCertificatePath
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve -CertificatesOnly
  Assert ($app.keyCredentials.Count -eq 2 -and @($app.keyCredentials | Where-Object key -eq $oldKey.key).Count -eq 1) 'rotation-preserves-old-key'
  $writes = $store.writes
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve -CertificatesOnly
  Assert ($store.writes -eq $writes) 'certificate-rerun-idempotent'
  $app.signInAudience = 'AzureADMultipleOrgs'
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve -CertificatesOnly } 'DRIFT'
  $app.signInAudience = 'AzureADMyOrg'
  # Unknown create response is reconciled by owned marker, never by creating another app.
  $store.apps.Clear(); $store.principals.Clear(); $cap.clientId = $null
  $cap.Remove('applicationObjectId'); $cap.Remove('servicePrincipalObjectId'); $store.loseAppResponse = $true
  $beforePosts = $store.appPosts
  Must-Refuse { Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve } 'RESULT_UNCERTAIN'
  Invoke-EntraReconciliation $plan (New-M365Result 'Apply') Apply $transport $approve
  Assert ($store.appPosts -eq $beforePosts + 1 -and $store.apps.Count -eq 1 -and $store.principals.Count -eq 1) 'unknown-create-reconciles-same-app'
  # Offline entrypoints must not inspect certificate files or authenticate.
  foreach ($entry in @('Provision-EntraApps.ps1','Set-CapabilityCertificates.ps1')) {
    foreach ($mode in @('Plan','WhatIf','MissingApproval')) {
      $output = Join-Path $work "$entry-$mode.json"
      $arguments = @(if ($mode -eq 'Plan') { '-DryRun' } elseif ($mode -eq 'WhatIf') { '-Mode'; 'Apply'; '-WhatIf' } else { '-Mode'; 'Apply' })
      & pwsh -NoProfile -NonInteractive -File (Join-Path $root $entry) -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') -OutputPath $output @arguments | Out-Null
      $code = $LASTEXITCODE; $envelope = Get-Content -Raw $output | ConvertFrom-Json -AsHashtable
      Assert ($envelope.networkCalls -eq 0 -and $envelope.mutationsAttempted -eq 0) 'entry-no-effects'
      Assert ($(if ($mode -eq 'MissingApproval') { $code -eq 3 -and $envelope.status -eq 'BLOCKED_APPROVAL' }
        else { $code -eq 0 -and $envelope.status -eq 'PLANNED' })) "entry-$entry-$mode"
    }
  }
  # A signed approval must match a separately trusted signer and the exact target plan.
  $original = Read-M365Plan (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') (Join-Path $root 'm365-plan.schema.json')
  $digest = Get-M365PlanSha256 $original
  $approvalKey = [System.Security.Cryptography.RSA]::Create(2048)
  try {
    $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synthetic Approval', $approvalKey,
      [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $approvalCertificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    try {
      $trusted = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($approvalCertificate.RawData)).ToLowerInvariant()
      foreach ($variant in @('valid','wrong-signer','wrong-tenant','expired','no-window','wrong-state','tampered')) {
        $approval = @{ planSha256 = $digest; tenantId = $original.tenantId; operatorClientId = $original.operator.clientId
          approvalReference = $original.approvalReference; exclusiveCertificateWindow = $true; stateDirectory = $fixedState; expiresAtUtc = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o'); action = 'RECONCILE_ENTRA_REGISTRATIONS' }
        if ($variant -eq 'wrong-tenant') { $approval.tenantId = [guid]::NewGuid().ToString() }
        if ($variant -eq 'wrong-state') { $approval.stateDirectory = $work }
        if ($variant -eq 'no-window') { $approval.exclusiveCertificateWindow = $false }
        if ($variant -eq 'expired') { $approval.expiresAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o') }
        $data = [System.Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json $approval -Compress))
        $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new([System.Security.Cryptography.Pkcs.ContentInfo]::new($data), $true)
        $cms.ComputeSignature([System.Security.Cryptography.Pkcs.CmsSigner]::new($approvalCertificate))
        $approvalPath = Join-Path $work 'approval.json'; $signaturePath = Join-Path $work 'approval.p7s'
        [System.IO.File]::WriteAllBytes($approvalPath, $data); [System.IO.File]::WriteAllBytes($signaturePath, $cms.Encode())
        if ($variant -eq 'tampered') { [System.IO.File]::WriteAllBytes($approvalPath, [System.Text.Encoding]::UTF8.GetBytes(([System.Text.Encoding]::UTF8.GetString($data) + ' '))) }
        $output = Join-Path $work "approval-$variant.json"
        $signer = if ($variant -eq 'wrong-signer') { '0' * 64 } else { $trusted }
        & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Provision-EntraApps.ps1') -PlanPath (Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json') `
          -Mode Apply -ExpectedPlanSha256 $digest -ApprovalReference $original.approvalReference -ApprovalPath $approvalPath `
          -ApprovalSignaturePath $signaturePath -ApprovalSignerSha256 $signer -OutputPath $output -Confirm:`$false | Out-Null
        $code = $LASTEXITCODE; $envelope = Get-Content -Raw $output | ConvertFrom-Json -AsHashtable
        # Valid signature reaches the missing authenticated-module prerequisite, never a live connection.
        Assert ($(if ($variant -eq 'valid') { $code -eq 7 -and $envelope.status -eq 'PREREQUISITE_MISSING' }
          elseif ($variant -eq 'tampered') { $code -eq 4 -and $envelope.status -eq 'INVALID_OR_UNSAFE' }
          else { $code -eq 3 -and $envelope.status -eq 'BLOCKED_APPROVAL' })) "approval-$variant"
        Assert ($envelope.networkCalls -eq 0 -and $envelope.mutationsAttempted -eq 0) 'approval-probe-no-effects'
      }
    } finally { $approvalCertificate.Dispose() }
  } finally { $approvalKey.Dispose() }
  Assert ((ConvertTo-Json $result -Depth 16) -notmatch 'PRIVATE KEY|BEGIN CERTIFICATE|synthetic-response-lost') 'redacted-result'
  [ordered]@{ unit = 'WU-018'; offline = $true; passed = $passed; failed = 0 } | ConvertTo-Json
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }

exit 0
