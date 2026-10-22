#Requires -Version 7.4
# Synthetic in-memory transport tests for WU-021 (SharePoint client sites and managed staff reconciliation).
# Zero network calls, zero tenant mutations, no credentials or private files required.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Entra.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Sites.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.ClientSites.psm1') -Force

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-client-sites-tests-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($work) | Out-Null
$passed = 0

function Assert([bool] $Condition, [string] $Name) {
  if (-not $Condition) { throw "assertion-failed:$Name" }
  $script:passed++
}

function Copy-Synthetic($Value) {
  return ,(ConvertTo-Json -InputObject $Value -Depth 32 -Compress | ConvertFrom-Json -AsHashtable -NoEnumerate)
}

function Create-SyntheticApproval([hashtable] $Plan, [string] $Action, [string] $StateDir, [datetime] $Expires, [ref] $SignerFingerprint) {
  $rsa = [System.Security.Cryptography.RSA]::Create(2048)
  try {
    $req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
      'CN=AuditSphere Synthetic Approval Signer', $rsa,
      [System.Security.Cryptography.HashAlgorithmName]::SHA256,
      [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
    $cert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
    try {
      $SignerFingerprint.Value = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($cert.RawData)).ToLowerInvariant()
      $payload = [ordered]@{
        planSha256 = Get-M365PlanSha256 $Plan
        tenantId = $Plan.tenantId
        operatorClientId = $Plan.operator.clientId
        approvalReference = $Plan.approvalReference
        expiresAtUtc = $Expires.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        action = $Action
        stateDirectory = (Get-EntraStateDirectory $StateDir)
      }
      $json = ConvertTo-Json -InputObject $payload -Depth 8
      $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
      $approvalPath = Join-Path $work "approval-$Action-$([guid]::NewGuid().ToString('N')).json"
      [System.IO.File]::WriteAllBytes($approvalPath, $bytes)

      $contentInfo = [System.Security.Cryptography.Pkcs.ContentInfo]::new($bytes)
      $signedCms = [System.Security.Cryptography.Pkcs.SignedCms]::new($contentInfo, $true)
      $cmsSigner = [System.Security.Cryptography.Pkcs.CmsSigner]::new($cert)
      $cmsSigner.IncludeOption = [System.Security.Cryptography.X509Certificates.X509IncludeOption]::EndCertOnly
      $signedCms.ComputeSignature($cmsSigner)
      $sigBytes = $signedCms.Encode()
      $sigPath = "$approvalPath.sig"
      [System.IO.File]::WriteAllBytes($sigPath, $sigBytes)

      return @{
        ApprovalPath = $approvalPath
        SignaturePath = $sigPath
      }
    } finally { $cert.Dispose() }
  } finally { $rsa.Dispose() }
}

try {
  $fixturePlanPath = Join-Path $PSScriptRoot 'fixtures/plan.synthetic.json'
  $schemaPath = Join-Path $root 'm365-plan.schema.json'
  $basePlan = Read-M365Plan $fixturePlanPath $schemaPath

  # 1. Schema & plan validation assertions
  Assert ($null -ne $basePlan.clientSites) 'plan-has-client-sites'
  Assert ($basePlan.clientSites.host -eq 'synthetic.sharepoint.com') 'plan-client-sites-host'
  Assert ($null -ne $basePlan.clientSites.custodianObjectId) 'plan-client-sites-custodian'
  Assert ($null -ne $basePlan.sites) 'plan-has-sites'
  Assert-ClientSitesPlan $basePlan
  Assert ($true) 'base-plan-passes-client-sites-assertion'

  # Test pre-cutoff client rejection without migration approval
  $preCutoffPlan = Copy-Synthetic $basePlan
  $preCutoffPlan.sites[0].onboardedAtUtc = '2026-09-01T00:00:00Z'
  $threwPreCutoff = $false
  try { Assert-ClientSitesPlan $preCutoffPlan } catch { $threwPreCutoff = ($_.Exception.Message -eq 'INVALID_OR_UNSAFE') }
  Assert $threwPreCutoff 'pre-cutoff-client-without-migration-approved-rejected'

  # Pre-cutoff with migrationApproved: true passes
  $preCutoffPlan.sites[0].migrationApproved = $true
  Assert-ClientSitesPlan $preCutoffPlan
  Assert ($true) 'pre-cutoff-with-migration-approved-passes'

  # 2. CLI DryRun / offline execution: Configure-SharePointClientSites.ps1
  $dryRunSites = Join-Path $work 'dryrun-sites.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Configure-SharePointClientSites.ps1') `
    -PlanPath $fixturePlanPath -DryRun -OutputPath $dryRunSites | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-sites-exit-zero'

  $verifyScript = Join-Path $root 'verify-result.py'
  & python3 $verifyScript $dryRunSites --expect PLANNED | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-sites-verified-by-python'

  $dryRunSitesJson = Get-Content -Raw -LiteralPath $dryRunSites | ConvertFrom-Json -AsHashtable
  Assert ($dryRunSitesJson.status -eq 'PLANNED') 'dryrun-sites-status-planned'
  Assert ($dryRunSitesJson.networkCalls -eq 0) 'dryrun-sites-zero-network'
  Assert ($dryRunSitesJson.mutationsAttempted -eq 0) 'dryrun-sites-zero-mutations'

  # 3. CLI DryRun / offline execution: Reconcile-ClientSiteStaff.ps1
  $dryRunStaff = Join-Path $work 'dryrun-staff.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Reconcile-ClientSiteStaff.ps1') `
    -PlanPath $fixturePlanPath -DryRun -OutputPath $dryRunStaff | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-staff-exit-zero'

  & python3 $verifyScript $dryRunStaff --expect PLANNED | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-staff-verified-by-python'

  $dryRunStaffJson = Get-Content -Raw -LiteralPath $dryRunStaff | ConvertFrom-Json -AsHashtable
  Assert ($dryRunStaffJson.status -eq 'PLANNED') 'dryrun-staff-status-planned'
  Assert ($dryRunStaffJson.networkCalls -eq 0) 'dryrun-staff-zero-network'
  Assert ($dryRunStaffJson.mutationsAttempted -eq 0) 'dryrun-staff-zero-mutations'

  # 4. In-Memory Synthetic Store & Transports for Sites & Staff
  $plan = Copy-Synthetic $basePlan
  $doc = @($plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $approvedSite = $plan.sites[0]
  $siteUrl = $approvedSite.siteUrl.TrimEnd('/')
  $siteId = $approvedSite.siteId
  $custodian = $plan.clientSites.custodianObjectId

  function Refuse([scriptblock] $Action, [string] $Expected) {
    $actual = ''
    try { & $Action | Out-Null } catch { $actual = $_.Exception.Message }
    Assert ($actual -ceq $Expected) "expected-$Expected-got-$actual"
  }

  function Result([string] $Mode) {
    $r = New-M365Result $Mode
    $r.repositoryCommit = $plan.repositoryCommit
    $r.tenantId = $plan.tenantId
    $r.planSha256 = Get-M365PlanSha256 $plan
    return $r
  }

  function Create-Store {
    return @{
      siteStatus = 2
      webDescription = $approvedSite.ownershipMarker
      webUrl = $siteUrl
      graphPermissions = @(@{ id = 'perm-1'; roles = @('write'); grantedToIdentities = @(@{ application = @{ id = $doc.clientId } }) })
      staffGroup = @{ Id = 101; Title = 'AuditSphere Assigned Staff'; Description = $approvedSite.ownershipMarker }
      roleBindings = @(@{ Id = 5 })
      groupUsers = @(
        @{ Id = 1; LoginName = 'i:0#.f|membership|staff1@synthetic.test' },
        @{ Id = 2; LoginName = 'i:0#.f|membership|staff2@synthetic.test' }
      )
      users = @{
        '00000000-0000-4000-8000-000000000060' = @{ id = '00000000-0000-4000-8000-000000000060'; userPrincipalName = 'staff1@synthetic.test' }
        '00000000-0000-4000-8000-000000000061' = @{ id = '00000000-0000-4000-8000-000000000061'; userPrincipalName = 'staff2@synthetic.test' }
      }
      siteUsers = @{
        'i:0#.f|membership|staff1@synthetic.test' = @{ Id = 1; LoginName = 'i:0#.f|membership|staff1@synthetic.test'; AadObjectId = '00000000-0000-4000-8000-000000000060' }
        'i:0#.f|membership|staff2@synthetic.test' = @{ Id = 2; LoginName = 'i:0#.f|membership|staff2@synthetic.test'; AadObjectId = '00000000-0000-4000-8000-000000000061' }
      }
      writes = 0
    }
  }

  function Create-Transport($Data) {
    return {
      param($Method, $Path, $Body)
      $decoded = [uri]::UnescapeDataString($Path)
      if ($Method -ceq 'GET') {
        if ($decoded.StartsWith('/_api/SPSiteManager/status?url=')) {
          return @{ SiteStatus = $Data.siteStatus }
        }
        if ($decoded -match '/_api/web\?\$select=Id,Url,Description') {
          return @{ Id = '00000000-0000-4000-8000-000000000051'; Url = $Data.webUrl; Description = $Data.webDescription }
        }
        if ($decoded -match '/_api/web/sitegroups/getbyname\(''AuditSphere%20Assigned%20Staff''\)' -or
            $decoded -match '/_api/web/sitegroups/getbyname\(''AuditSphere Assigned Staff''\)') {
          if ($null -eq $Data.staffGroup) { return $null }
          return $Data.staffGroup
        }
        if ($decoded -match '/_api/web/roledefinitions/getbytype\(5\)') {
          return @{ Id = 5; RoleTypeKind = 5 }
        }
        if ($decoded -match '/_api/web/roleassignments/getbyprincipalid\(\d+\)/roledefinitionbindings') {
          return $Data.roleBindings
        }
        if ($decoded -match '/_api/web/sitegroups\(\d+\)/users$') {
          return $Data.groupUsers
        }
        if ($decoded -match '/_api/web/siteusers/getbyid\((\d+)\)') {
          $id = [int]$Matches[1]
          foreach ($su in $Data.siteUsers.Values) { if ($su.Id -eq $id) { return $su } }
          return $null
        }
      }
      if ($Method -ceq 'POST') {
        $Data.writes++
        if ($decoded -eq '/_api/SPSiteManager/create') {
          $Data.siteStatus = 2
          return @{ SiteStatus = 2 }
        }
        if ($decoded -match '/_api/web/sitegroups$') {
          $Data.staffGroup = @{ Id = 101; Title = $Body.Title; Description = $Body.Description }
          return $Data.staffGroup
        }
        if ($decoded -match '/_api/web/roleassignments/addroleassignment') {
          $Data.roleBindings += @(@{ Id = 5 })
          return @{ ok = $true }
        }
        if ($decoded -match '/_api/web/ensureuser') {
          $logon = $Body.logonName
          if ($Data.siteUsers.ContainsKey($logon)) { return $Data.siteUsers[$logon] }
          $newUser = @{ Id = ($Data.siteUsers.Count + 1); LoginName = $logon; AadObjectId = '00000000-0000-4000-8000-000000000060' }
          $Data.siteUsers[$logon] = $newUser
          return $newUser
        }
        if ($decoded -match '/_api/web/sitegroups\(\d+\)/users$') {
          $login = $Body.LoginName
          $Data.groupUsers += @(@{ Id = ($Data.groupUsers.Count + 1); LoginName = $login })
          return @{ ok = $true }
        }
        if ($decoded -match '/_api/web/sitegroups\(\d+\)/users/removebyid\((\d+)\)') {
          $removeId = [int]$Matches[1]
          $Data.groupUsers = @($Data.groupUsers | Where-Object { $_.Id -ne $removeId })
          return @{ ok = $true }
        }
      }
      throw "unexpected-transport-call:$Method $Path"
    }.GetNewClosure()
  }

  function Create-GraphTransport($Data) {
    return {
      param($Method, $Path, $Body)
      $decoded = [uri]::UnescapeDataString($Path)
      if ($Method -ceq 'GET') {
        if ($decoded -match '^/v1.0/sites/[^/]+/permissions') {
          return @{ value = $Data.graphPermissions }
        }
        if ($decoded -match '^/v1.0/users/([^/?]+)') {
          $uid = $Matches[1]
          if ($Data.users.ContainsKey($uid)) { return $Data.users[$uid] }
          return $null
        }
      }
      if ($Method -ceq 'POST' -and $decoded -match '^/v1.0/sites/[^/]+/permissions') {
        $Data.writes++
        $Data.graphPermissions += @(@{ id = 'perm-new'; roles = $Body.roles; grantedToIdentities = $Body.grantedToIdentities })
        return @{ id = 'perm-new' }
      }
      throw "unexpected-graph-call:$Method $Path"
    }.GetNewClosure()
  }

  $yes = { param($m, $p, $b) $true }
  $confirm = { param($p, $b) }

  # Test Site Reconciliation: existing site in ReadOnly passes
  $store = Create-Store
  $transport = Create-Transport $store
  $graphTransport = Create-GraphTransport $store
  $resSitesReadOnly = Result ReadOnly
  Invoke-SharePointClientSitesReconciliation $plan $resSitesReadOnly ReadOnly $transport $yes $confirm $graphTransport
  Assert ($resSitesReadOnly.checks.Count -eq 3) 'sites-readonly-passes-with-3-checks'
  Assert ($store.writes -eq 0) 'sites-readonly-makes-zero-writes'

  # Test Site Missing in ReadOnly -> DRIFT
  $storeMissing = Create-Store
  $storeMissing.siteStatus = 0
  $tMissing = Create-Transport $storeMissing
  Refuse { Invoke-SharePointClientSitesReconciliation $plan (Result ReadOnly) ReadOnly $tMissing $yes $confirm $graphTransport } 'DRIFT'

  # Test Site Missing in Apply -> creates site and confirms
  $storeApply = Create-Store
  $storeApply.siteStatus = 0
  $tApply = Create-Transport $storeApply
  $resApply = Result Apply
  Invoke-SharePointClientSitesReconciliation $plan $resApply Apply $tApply $yes $confirm $graphTransport
  Assert ($storeApply.writes -ge 1) 'sites-apply-provisions-site'
  Assert ($resApply.mutationsAttempted -ge 1 -and $resApply.mutationsConfirmed -ge 1) 'sites-apply-records-mutations'

  # Second Apply is idempotent
  $writesBefore = $storeApply.writes
  $again = Result Apply
  Invoke-SharePointClientSitesReconciliation $plan $again Apply $tApply $yes $confirm $graphTransport
  Assert ($storeApply.writes -eq $writesBefore) 'second-sites-apply-is-idempotent'

  # Test Web description / ownership mismatch -> INVALID_OR_UNSAFE
  $storeUnsafe = Create-Store
  $storeUnsafe.webDescription = 'unrelated-owner'
  $tUnsafe = Create-Transport $storeUnsafe
  Refuse { Invoke-SharePointClientSitesReconciliation $plan (Result ReadOnly) ReadOnly $tUnsafe $yes $confirm $graphTransport } 'INVALID_OR_UNSAFE'

  # Test Staff Reconciliation: existing staff group and members in ReadOnly passes
  $storeStaff = Create-Store
  $tStaff = Create-Transport $storeStaff
  $gStaff = Create-GraphTransport $storeStaff
  $resStaffReadOnly = Result ReadOnly
  Invoke-ClientSiteStaffReconciliation $plan $resStaffReadOnly ReadOnly $tStaff $yes $confirm $gStaff
  Assert ($resStaffReadOnly.checks.Count -eq 3) 'staff-readonly-passes-with-3-checks'
  Assert ($storeStaff.writes -eq 0) 'staff-readonly-makes-zero-writes'

  # Test Staff Group missing in ReadOnly -> DRIFT
  $storeNoGroup = Create-Store
  $storeNoGroup.staffGroup = $null
  $tNoGroup = Create-Transport $storeNoGroup
  Refuse { Invoke-ClientSiteStaffReconciliation $plan (Result ReadOnly) ReadOnly $tNoGroup $yes $confirm $gStaff } 'DRIFT'

  # Test Staff Group created in Apply
  $storeGroupApply = Create-Store
  $storeGroupApply.staffGroup = $null
  $tGroupApply = Create-Transport $storeGroupApply
  $resGroupApply = Result Apply
  Invoke-ClientSiteStaffReconciliation $plan $resGroupApply Apply $tGroupApply $yes $confirm $gStaff
  Assert ($storeGroupApply.staffGroup -ne $null) 'staff-group-created-in-apply'
  Assert ($resGroupApply.mutationsConfirmed -ge 1) 'staff-group-mutations-confirmed'

  # Test Obsolete member removed in Apply
  $storeObsolete = Create-Store
  $storeObsolete.groupUsers += @(@{ Id = 99; LoginName = 'i:0#.f|membership|obsolete@synthetic.test' })
  $tObsolete = Create-Transport $storeObsolete
  $resObsolete = Result Apply
  Invoke-ClientSiteStaffReconciliation $plan $resObsolete Apply $tObsolete $yes $confirm $gStaff
  $hasObsolete = @($storeObsolete.groupUsers | Where-Object { $_.Id -eq 99 }).Count -gt 0
  Assert (-not $hasObsolete) 'obsolete-staff-member-removed'

  # Test Member added in Apply
  $storeAdd = Create-Store
  $storeAdd.groupUsers = @($storeAdd.groupUsers | Where-Object { $_.Id -ne 2 }) # Remove staff2
  $tAdd = Create-Transport $storeAdd
  $resAdd = Result Apply
  Invoke-ClientSiteStaffReconciliation $plan $resAdd Apply $tAdd $yes $confirm $gStaff
  $hasStaff2 = @($storeAdd.groupUsers | Where-Object { $_.LoginName -eq 'i:0#.f|membership|staff2@synthetic.test' }).Count -gt 0
  Assert $hasStaff2 'missing-staff-member-added'

  # 5. CMS Signature & Authorization tests for both scripts
  $signerFp = [ref]''
  $approvalSites = Create-SyntheticApproval $basePlan 'CONFIGURE_SHAREPOINT_CLIENT_SITES' (Get-EntraStateDirectory) (Get-Date).AddHours(2) $signerFp
  $sitesApplyOut = Join-Path $work 'sites-live-out.json'

  # Wrong plan hash must fail
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Configure-SharePointClientSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 '0000000000000000000000000000000000000000000000000000000000000000' `
    -ApprovalPath $approvalSites.ApprovalPath `
    -ApprovalSignaturePath $approvalSites.SignaturePath `
    -ApprovalSignerSha256 $signerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $sitesApplyOut | Out-Null
  Assert ($LASTEXITCODE -eq 4) 'sites-wrong-plan-hash-exits-invalid-or-unsafe'

  # Expired approval must be rejected with BLOCKED_APPROVAL
  $expiredSignerFp = [ref]''
  $expiredApproval = Create-SyntheticApproval $basePlan 'CONFIGURE_SHAREPOINT_CLIENT_SITES' (Get-EntraStateDirectory) (Get-Date).AddHours(-1) $expiredSignerFp
  $expiredOut = Join-Path $work 'sites-expired-out.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Configure-SharePointClientSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
    -ApprovalPath $expiredApproval.ApprovalPath `
    -ApprovalSignaturePath $expiredApproval.SignaturePath `
    -ApprovalSignerSha256 $expiredSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $expiredOut | Out-Null
  Assert ($LASTEXITCODE -eq 3) 'sites-expired-approval-exits-blocked-approval'

  # Wrong action in approval rejected with BLOCKED_APPROVAL
  $wrongActionSignerFp = [ref]''
  $wrongActionApproval = Create-SyntheticApproval $basePlan 'UNAPPROVED_ACTION' (Get-EntraStateDirectory) (Get-Date).AddHours(2) $wrongActionSignerFp
  $wrongActionOut = Join-Path $work 'sites-wrong-action.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Configure-SharePointClientSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
    -ApprovalPath $wrongActionApproval.ApprovalPath `
    -ApprovalSignaturePath $wrongActionApproval.SignaturePath `
    -ApprovalSignerSha256 $wrongActionSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $wrongActionOut | Out-Null
  Assert ($LASTEXITCODE -eq 3) 'sites-wrong-action-approval-exits-blocked-approval'

  # Repeat CMS approval checks for Reconcile-ClientSiteStaff.ps1
  $staffSignerFp = [ref]''
  $approvalStaff = Create-SyntheticApproval $basePlan 'RECONCILE_CLIENT_SITE_STAFF' (Get-EntraStateDirectory) (Get-Date).AddHours(2) $staffSignerFp
  $staffApplyOut = Join-Path $work 'staff-live-out.json'

  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Reconcile-ClientSiteStaff.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 '0000000000000000000000000000000000000000000000000000000000000000' `
    -ApprovalPath $approvalStaff.ApprovalPath `
    -ApprovalSignaturePath $approvalStaff.SignaturePath `
    -ApprovalSignerSha256 $staffSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $staffApplyOut | Out-Null
  Assert ($LASTEXITCODE -eq 4) 'staff-wrong-plan-hash-exits-invalid-or-unsafe'

  $expiredStaffSignerFp = [ref]''
  $expiredStaffApproval = Create-SyntheticApproval $basePlan 'RECONCILE_CLIENT_SITE_STAFF' (Get-EntraStateDirectory) (Get-Date).AddHours(-1) $expiredStaffSignerFp
  $expiredStaffOut = Join-Path $work 'staff-expired-out.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Reconcile-ClientSiteStaff.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
    -ApprovalPath $expiredStaffApproval.ApprovalPath `
    -ApprovalSignaturePath $expiredStaffApproval.SignaturePath `
    -ApprovalSignerSha256 $expiredStaffSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $expiredStaffOut | Out-Null
  Assert ($LASTEXITCODE -eq 3) 'staff-expired-approval-exits-blocked-approval'

  # A signed plan is not a substitute for the unfinished Application intent adapter.
  foreach ($entry in @('Configure-SharePointClientSites.ps1','Reconcile-ClientSiteStaff.ps1')) {
    $blockedOutput = Join-Path $work ($entry + '-blocked.json')
    & pwsh -NoProfile -NonInteractive -File (Join-Path $root $entry) `
      -PlanPath $fixturePlanPath -Mode ReadOnly -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
      -OutputPath $blockedOutput | Out-Null
    Assert ($LASTEXITCODE -eq (Get-M365ExitCode 'BLOCKED_BOOTSTRAP')) "$entry-requires-application-adapter"
    $blocked = Get-Content -Raw $blockedOutput | ConvertFrom-Json -AsHashtable
    Assert ($blocked.status -ceq 'BLOCKED_BOOTSTRAP') "$entry-blocked-status"
    Assert ($blocked.networkCalls -eq 0 -and $blocked.mutationsAttempted -eq 0) "$entry-zero-provider-effects"
  }

  [ordered]@{ unit = 'WU-021'; offline = $true; passed = $passed; failed = 0 } | ConvertTo-Json
} finally {
  if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
  }
}

exit 0
