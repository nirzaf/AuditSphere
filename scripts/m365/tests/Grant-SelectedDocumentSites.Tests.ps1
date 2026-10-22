#Requires -Version 7.4
# Synthetic in-memory Graph transport tests for WU-020 (Grant-SelectedDocumentSites).
# Zero network calls, zero tenant mutations, no credentials or private files required.
param([switch] $Offline)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Offline) { throw 'Pass -Offline.' }
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $root 'AuditSphere.M365.Common.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Entra.psm1') -Force
Import-Module (Join-Path $root 'AuditSphere.M365.Sites.psm1') -Force

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('as-sites-tests-' + [guid]::NewGuid().ToString('N'))
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

  # 1. Verify schema and plan loading
  Assert ($null -ne $basePlan.sites) 'plan-has-sites'
  Assert ($basePlan.sites.Count -eq 1) 'plan-has-one-site'
  Assert ($basePlan.sites[0].permission -eq 'write') 'plan-site-permission-write'
  Assert ($null -ne $basePlan.negativeControls.siteIds) 'plan-has-negative-controls'

  # 2. CLI DryRun / offline execution
  $dryRunOutput = Join-Path $work 'dryrun-result.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-SelectedDocumentSites.ps1') `
    -PlanPath $fixturePlanPath -DryRun -OutputPath $dryRunOutput | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-exit-zero'

  $verifyScript = Join-Path $root 'verify-result.py'
  & python3 $verifyScript $dryRunOutput --expect PLANNED | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'dryrun-verified-by-python-script'

  $dryRunJson = Get-Content -Raw -LiteralPath $dryRunOutput | ConvertFrom-Json -AsHashtable
  Assert ($dryRunJson.status -eq 'PLANNED') 'dryrun-status-planned'
  Assert ($dryRunJson.networkCalls -eq 0) 'dryrun-zero-network'
  Assert ($dryRunJson.mutationsAttempted -eq 0) 'dryrun-zero-mutations-attempted'
  Assert ($dryRunJson.mutationsConfirmed -eq 0) 'dryrun-zero-mutations-confirmed'
  Assert ($dryRunJson.checks.Count -ge 2) 'dryrun-checks-present'

  # 3. Mode Plan (default) offline execution
  $planOutput = Join-Path $work 'plan-result.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-SelectedDocumentSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Plan -OutputPath $planOutput | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'plan-mode-exit-zero'
  & python3 $verifyScript $planOutput --expect PLANNED | Out-Null
  Assert ($LASTEXITCODE -eq 0) 'plan-mode-verified-by-python'

  # Exercise operator grants and a distinct runtime transport using the production probe function.
  $plan = Copy-Synthetic $basePlan
  $doc = @($plan.capabilities | Where-Object kind -CEQ 'DOCUMENTS')[0]
  $docSp = '00000000-0000-4000-8000-000000000031'; $appObject = '00000000-0000-4000-8000-000000000032'
  $graphSp = '00000000-0000-4000-8000-000000000080'; $roleId = '00000000-0000-4000-8000-000000000082'
  $graphId = '00000003-0000-0000-c000-000000000000'
  $marker = "AuditSphere/$($plan.ownershipTag.ToLowerInvariant())/$($plan.firmId.ToLowerInvariant())/DOCUMENTS"
  $rsa = [Security.Cryptography.RSA]::Create(2048)
  try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=Synthetic WU020', $rsa,
      [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    try {
      $key = [Convert]::ToBase64String($certificate.RawData); $thumbprint = $certificate.Thumbprint
      $doc.publicCertificateSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData)).ToLowerInvariant()
      $doc.publicCertificatePath = Join-Path $work 'document-public.cer'; [IO.File]::WriteAllBytes($doc.publicCertificatePath, $certificate.RawData)
      $keyPath = Join-Path $work 'synthetic-private.pem'; [IO.File]::WriteAllText($keyPath,$rsa.ExportPkcs8PrivateKeyPem())
      if (-not $IsWindows) { [IO.File]::SetUnixFileMode($keyPath,[IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
      $doc.certificateReference = 'file:' + $keyPath
    } finally { $certificate.Dispose() }
  } finally { $rsa.Dispose() }
  $doc.applicationObjectId = $appObject; $doc.servicePrincipalObjectId = $docSp
  $approved = $plan.sites[0]; $control = $plan.negativeControls.siteIds[0]
  $context=@{TenantId=$plan.tenantId;ClientId=$doc.clientId;Environment='Global';AuthType='AppOnly';CertificateThumbprint=$thumbprint}
  function Refuse([scriptblock] $Action,[string] $Expected) {
    $actual = ''; $trace=''; try { & $Action | Out-Null } catch { $actual=$_.Exception.Message; $trace=$_.ScriptStackTrace }
    if($actual -cne $Expected){$trace}
    Assert ($actual -ceq $Expected) "expected-$Expected-got-$actual"
  }
  function Result([string] $Mode) {
    $r=New-M365Result $Mode; $r.repositoryCommit=$plan.repositoryCommit; $r.tenantId=$plan.tenantId; $r.planSha256=Get-M365PlanSha256 $plan; return $r
  }
  function Metadata([string] $Id,[string] $Url,[string] $Description='') {
    $parts=Get-SitesId $Id
    return @{ id=$Id; webUrl=$Url; description=$Description; sharepointIds=@{tenantId=$plan.tenantId;siteId=$parts.collection;webId=$parts.web}; permissions=@() }
  }
  function Store {
    $sites=@{}
    foreach($site in $plan.sites){ $sites[$site.siteId]=Metadata $site.siteId $site.siteUrl $site.ownershipMarker }
    $sites[$control]=Metadata $control 'https://synthetic.sharepoint.com/sites/unrelated-control'
    return @{
      app=@{id=$appObject;appId=$doc.clientId;signInAudience='AzureADMyOrg';tags=@($marker)
        requiredResourceAccess=@(@{resourceAppId=$graphId;resourceAccess=@(@{id=$roleId;type='Role'})})
        keyCredentials=@(@{key=$key;type='AsymmetricX509Cert';usage='Verify';startDateTime=[DateTimeOffset]::UtcNow.AddDays(-1).ToString('o');endDateTime=[DateTimeOffset]::UtcNow.AddDays(1).ToString('o')})}
      sp=@{id=$docSp;appId=$doc.clientId;appOwnerOrganizationId=$plan.tenantId;tags=@($marker);accountEnabled=$true}
      roles=@(@{id='assignment';principalId=$docSp;resourceId=$graphSp;appRoleId=$roleId})
      sites=$sites; writes=0; runtimeReads=0; operatorReads=0; hideReadback=$false; loseAck=$false; controlCode=403
      graphId=$graphId; graphSp=$graphSp; roleId=$roleId; docClientId=$doc.clientId; controlId=$control
      plan=$plan; context=$context; thumbprint=$thumbprint
    }
  }
  function OperatorTransport($Data) {
    return {
      param($Method,$Path,$Body)
      $decoded=[uri]::UnescapeDataString($Path)
      if($Method -ceq 'GET'){
        $Data.operatorReads++
        if($decoded.StartsWith('/v1.0/applications?')){return @{value=@($Data.app)}}
        if($decoded.StartsWith('/v1.0/applications/')){return $Data.app}
        if($decoded.StartsWith('/v1.0/servicePrincipals?')){
          if($decoded.Contains($Data.graphId)){
            return @{value=@(@{id=$Data.graphSp;appId=$Data.graphId;appRoles=@(@{id=$Data.roleId;value='Sites.Selected';isEnabled=$true;allowedMemberTypes=@('Application')})})}
          }
          return @{value=@($Data.sp)}
        }
        if($decoded -match '/servicePrincipals/[^/]+/appRoleAssignments'){return @{value=@($Data.roles)}}
        if($decoded -match '^/v1.0/sites/([^/?]+)/permissions'){
          $id=$Matches[1]
          if($Data.hideReadback){return @{value=@()}}
          return @{value=@($Data.sites[$id].permissions)}
        }
        if($decoded -match '^/v1.0/sites/([^/?]+)\?'){return $Data.sites[$Matches[1]]}
      }
      if($Method -ceq 'POST' -and $decoded -match '^/v1.0/sites/([^/?]+)/permissions$'){
        $id=$Matches[1]; $Data.writes++
        if (-not ($Body.roles.Count -eq 1 -and $Body.roles[0] -ceq 'write' -and $Body.grantedToIdentities.Count -eq 1 -and
          $Body.grantedToIdentities[0].application.id -ceq $Data.docClientId)) { throw 'assertion-failed:write-contains-only-approved-documents-application' }
        $permission=@{id='grant-'+$Data.writes;roles=@('write');grantedToIdentities=@($Body.grantedToIdentities)}
        $Data.sites[$id].permissions+=@($permission)
        if($Data.loseAck){throw 'synthetic-lost-ack'}
        return $permission
      }
      throw 'unexpected-operator-call'
    }.GetNewClosure()
  }
  function RuntimeTransport($Data) {
    return {
      param($Method,$Path,$Body)
      if($Method -cne 'GET' -or $Path -notmatch '^/v1.0/sites/([^/?]+)(/drive/root)?\?'){throw 'unexpected-runtime-call'}
      $id=$Matches[1]; $rootRead=$Matches[2];$Data.runtimeReads++
      if($id -eq $Data.controlId){return @{statusCode=$Data.controlCode;body=$null}}
      if(-not $Data.sites.ContainsKey($id) -or -not $Data.sites[$id].permissions.Count){return @{statusCode=403;body=$null}}
      if($rootRead){return @{statusCode=200;body=@{id='root-'+$id;folder=@{};webUrl=$Data.sites[$id].webUrl+'/Shared%20Documents';parentReference=@{driveId='drive'}}}}
      return @{statusCode=200;body=$Data.sites[$id]}
    }.GetNewClosure()
  }
  function RuntimeProof($Data) {
    $transport=RuntimeTransport $Data
    return {
      $proof=New-M365Result ReadOnly
      $proof.repositoryCommit=$Data.plan.repositoryCommit; $proof.tenantId=$Data.plan.tenantId
      $proof.planSha256=Get-M365PlanSha256 $Data.plan
      Invoke-DocumentsRuntimeProbe $Data.plan $proof $transport $Data.context $Data.thumbprint
      $proof.status='VERIFIED'; $proof.networkCalls=$Data.runtimeReads+1
      return $proof
    }.GetNewClosure()
  }
  $yes={param($method,$path,$body) $true};$confirm={param($path,$body)}
  $store=Store; $operator=OperatorTransport $store; $runtime=RuntimeProof $store
  Refuse { Invoke-SelectedSitesReconciliation $plan (Result ReadOnly) ReadOnly $operator $yes $confirm $runtime } 'DRIFT'
  Assert ($store.writes -eq 0 -and $store.runtimeReads -eq 0) 'missing-grant-readonly-neither-writes-nor-claims-runtime-proof'
  $result=Result Apply
  Invoke-SelectedSitesReconciliation $plan $result Apply $operator $yes $confirm $runtime
  Assert ($store.writes -eq 1 -and $result.mutationsAttempted -eq 1 -and $result.mutationsConfirmed -eq 1 -and $store.runtimeReads -eq 3) 'one-exact-grant-and-separate-positive-root-and-403-probes'
  Assert (@($result.checks | Where-Object id -Like 'runtime:DOCUMENTS:*').Count -eq 4) 'runtime-identity-and-each-real-read-are-retained'
  $again=Result Apply
  Invoke-SelectedSitesReconciliation $plan $again Apply $operator $yes $confirm $runtime
  Assert ($store.writes -eq 1 -and $again.mutationsAttempted -eq 0) 'second-apply-does-not-duplicate-grant'
  Invoke-SelectedSitesReconciliation $plan (Result ReadOnly) ReadOnly $operator {throw 'write-forbidden'} $confirm $runtime
  Assert ($store.writes -eq 1) 'readonly-with-existing-grant-does-not-write'

  foreach($case in @('wrongTenant','wrongSite','wrongUrl','wrongMarker','missingControl','extraRole','broadGrant','userMix','control200','control401','control404')){
    $data=Store; $transport=OperatorTransport $data; $proof=RuntimeProof $data;$expected='INVALID_OR_UNSAFE'
    switch($case){
      wrongTenant {$data.sites[$approved.siteId].sharepointIds.tenantId=$docSp}
      wrongSite {$data.sites[$approved.siteId].id=$control}
      wrongUrl {$data.sites[$approved.siteId].webUrl='https://synthetic.sharepoint.com/sites/sibling'}
      wrongMarker {$data.sites[$approved.siteId].description='unrelated-owner'}
      missingControl {$data.sites.Remove($control);$expected='INCOMPLETE_EVIDENCE'}
      extraRole {$data.roles+=@(@{id='extra';principalId=$docSp;resourceId=$graphSp;appRoleId=$docSp});$expected='DRIFT'}
      broadGrant {$data.sites[$approved.siteId].permissions=@(@{id='broad';roles=@('fullcontrol');grantedToIdentities=@(@{application=@{id=$doc.clientId}})});$expected='DRIFT'}
      userMix {$data.sites[$approved.siteId].permissions=@(@{id='mixed';roles=@('write');grantedToIdentities=@(@{application=@{id=$doc.clientId};user=@{id=$docSp}})});$expected='DRIFT'}
      control200 {$data.controlCode=200;$expected='DRIFT'}
      control401 {$data.controlCode=401;$expected='DRIFT'}
      control404 {$data.controlCode=404;$expected='DRIFT'}
    }
    Refuse { Invoke-SelectedSitesReconciliation $plan (Result Apply) Apply $transport $yes $confirm $proof } $expected
    if($case -notlike 'control*'){Assert ($data.writes -eq 0) "unsafe-$case-rejected-before-site-mutation"}
  }
  $data=Store;$data.loseAck=$true;$transport=OperatorTransport $data;$proof=RuntimeProof $data
  $journal=@{};$journalPath=Join-Path $work 'site-journal.json'
  $approve={param($m,$p,$b) Save-SitesMutationIntent $journal $journalPath $m $p $b ('1'*64);$true}
  $confirm={param($p,$b) Confirm-SitesMutationIntent $journal $journalPath $p $b}
  Refuse {Invoke-SelectedSitesReconciliation $plan (Result Apply) Apply $transport $approve $confirm $proof} 'RESULT_UNCERTAIN'
  $data.hideReadback=$true;$data.loseAck=$false
  Refuse {Invoke-SelectedSitesReconciliation $plan (Result Apply) Apply $transport $approve $confirm $proof} 'RESULT_UNCERTAIN'
  Assert ($data.writes -eq 1) 'unknown-and-hidden-outcome-never-reposted'
  $data.hideReadback=$false
  Invoke-SelectedSitesReconciliation $plan (Result Apply) Apply $transport $approve $confirm $proof
  Assert ($data.writes -eq 1 -and @($journal.Values)[0].state -ceq 'CONFIRMED') 'late-original-grant-resolves-intent-without-duplicate'
  $sitePath='/v1.0/sites/'+$approved.siteId+'/permissions';$body=@{roles=@('write');grantedToIdentities=@(@{application=@{id=$doc.clientId}})}
  Refuse {Save-SitesMutationIntent $journal $journalPath POST $sitePath $body ('1'*64)} 'RESULT_UNCERTAIN'
  Save-SitesMutationIntent $journal $journalPath POST $sitePath $body ('2'*64)
  Assert (@($journal.Values)[0].priorConfirmedAttempts.Count -eq 1) 'renewed-approved-site-repair-retains-confirmed-history'

  $loadedCertificate=Open-DocumentsRuntimeCertificate $plan
  try {Assert ($loadedCertificate.HasPrivateKey -and $loadedCertificate.Thumbprint -ceq $thumbprint) 'existing-pem-custody-matches-exact-approved-public-certificate'} finally {$loadedCertificate.Dispose()}
  $oldReference=$doc.certificateReference;$doc.certificateReference='keyvault:unsupported-custody'
  Refuse {Open-DocumentsRuntimeCertificate $plan} 'PREREQUISITE_MISSING';$doc.certificateReference=$oldReference
  $oldId=$approved.siteId;$approved.siteId='synthetic.sharepoint.com/path?permissions'
  Refuse {Assert-SelectedSitesPlan $plan} 'INVALID_OR_UNSAFE';$approved.siteId=$oldId
  $oldControls=$plan.negativeControls.siteIds;$plan.negativeControls.siteIds=@()
  Refuse {Assert-SelectedSitesPlan $plan} 'INVALID_OR_UNSAFE';$plan.negativeControls.siteIds=$oldControls
  Refuse {Invoke-SelectedSitesReconciliation $plan (Result ReadOnly) ReadOnly $operator $yes $confirm $null} 'BLOCKED_BOOTSTRAP'

  # 5. Full live-path simulation with CMS approval
  $signerFp = [ref]''
  $approvalInfo = Create-SyntheticApproval $basePlan 'GRANT_SELECTED_DOCUMENT_SITES' (Get-EntraStateDirectory) (Get-Date).AddHours(2) $signerFp

  $applyOutput = Join-Path $work 'apply-live-output.json'
  $wrongHashOutput = Join-Path $work 'apply-wrong-hash.json'

  # Wrong plan hash must fail
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-SelectedDocumentSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 '0000000000000000000000000000000000000000000000000000000000000000' `
    -ApprovalPath $approvalInfo.ApprovalPath `
    -ApprovalSignaturePath $approvalInfo.SignaturePath `
    -ApprovalSignerSha256 $signerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $wrongHashOutput | Out-Null
  Assert ($LASTEXITCODE -eq 4) 'wrong-plan-hash-exits-invalid-or-unsafe'

  # Expired approval must be rejected with BLOCKED_APPROVAL
  $expiredSignerFp = [ref]''
  $expiredApproval = Create-SyntheticApproval $basePlan 'GRANT_SELECTED_DOCUMENT_SITES' (Get-EntraStateDirectory) (Get-Date).AddHours(-1) $expiredSignerFp
  $expiredOutput = Join-Path $work 'apply-expired.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-SelectedDocumentSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
    -ApprovalPath $expiredApproval.ApprovalPath `
    -ApprovalSignaturePath $expiredApproval.SignaturePath `
    -ApprovalSignerSha256 $expiredSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $expiredOutput | Out-Null
  Assert ($LASTEXITCODE -eq 3) 'expired-approval-exits-blocked-approval'

  # Wrong action in approval must be rejected with BLOCKED_APPROVAL
  $wrongActionSignerFp = [ref]''
  $wrongActionApproval = Create-SyntheticApproval $basePlan 'UNAPPROVED_ACTION' (Get-EntraStateDirectory) (Get-Date).AddHours(2) $wrongActionSignerFp
  $wrongActionOutput = Join-Path $work 'apply-wrong-action.json'
  & pwsh -NoProfile -NonInteractive -File (Join-Path $root 'Grant-SelectedDocumentSites.ps1') `
    -PlanPath $fixturePlanPath -Mode Apply `
    -ExpectedPlanSha256 (Get-M365PlanSha256 $basePlan) `
    -ApprovalPath $wrongActionApproval.ApprovalPath `
    -ApprovalSignaturePath $wrongActionApproval.SignaturePath `
    -ApprovalSignerSha256 $wrongActionSignerFp.Value `
    -ApprovalReference $basePlan.approvalReference `
    -StateDirectory (Get-EntraStateDirectory) `
    -OutputPath $wrongActionOutput | Out-Null
  Assert ($LASTEXITCODE -eq 3) 'wrong-action-approval-exits-blocked-approval'

  [ordered]@{ unit = 'WU-020'; offline = $true; passed = $passed; failed = 0 } | ConvertTo-Json
} finally {
  if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
  }
}

exit 0
