# Deployment binding inspection for the approved Hostinger acceptance Compose target.
# This module never invokes Docker, reads .env files, resolves credentials, or emits raw environment values.
Set-StrictMode -Version Latest

$script:AcceptedProjectName = 'auditsphere-acceptance'
$script:ComposeRelativePath = 'infra/hostinger-acceptance/compose.yaml'
$script:OverrideRelativePath = 'infra/hostinger-acceptance/m365-isolation.override.example.yaml'
$script:CredentialHostRoot = '/opt/auditsphere/storage/m365-credentials'
$script:CredentialTargetRoot = '/run/auditsphere/m365'
$script:AllowedCredentialMountsByService = [ordered]@{
  api = @('directory-reader', 'selected-site', 'provisioning', 'guest-invitation', 'group-membership', 'outbound-mail')
  'worker-mail' = @('outbound-mail')
  'worker-pbc' = @('selected-site')
  'worker-client-sites' = @('client-sites', 'directory-reader')
}
$script:PublicIdEnvironmentNames = [ordered]@{
  identity = 'Identity__ClientId'
  directoryReader = 'DirectoryReader__ClientId'
  selectedSite = 'SelectedSite__ClientId'
  provisioning = 'TenantAdministration__Provisioning__ClientId'
  guestInvitation = 'TenantAdministration__GuestInvitation__ClientId'
  groupMembership = 'TenantAdministration__GroupMembership__ClientId'
  outboundMail = 'TenantAdministration__OutboundMail__ClientId'
  clientSites = 'ClientSites__ClientId'
}
$script:PublicIdentityOwners = [ordered]@{
  identity = 'api'; directoryReader = 'api'; selectedSite = 'api'; provisioning = 'api'
  guestInvitation = 'api'; groupMembership = 'api'; outboundMail = 'api'; clientSites = 'worker-client-sites'
}
$script:PlanCapabilityBindings = [ordered]@{
  LOGIN = @{ service = 'api'; environment = 'Identity__ClientId' }
  DIRECTORY_READER = @{ service = 'api'; environment = 'DirectoryReader__ClientId' }
  DOCUMENTS = @{ service = 'api'; environment = 'SelectedSite__ClientId' }
  MAIL = @{ service = 'api'; environment = 'TenantAdministration__OutboundMail__ClientId' }
  USER_CREATE = @{ service = 'api'; environment = 'TenantAdministration__Provisioning__ClientId' }
  INVITATIONS = @{ service = 'api'; environment = 'TenantAdministration__GuestInvitation__ClientId' }
  GROUP_MEMBERSHIP = @{ service = 'api'; environment = 'TenantAdministration__GroupMembership__ClientId' }
  CLIENT_SITES = @{ service = 'worker-client-sites'; environment = 'ClientSites__ClientId' }
}
$script:RequiredCredentialMountsByCapability = [ordered]@{
  LOGIN = @()
  DIRECTORY_READER = @(@{ service = 'api'; name = 'directory-reader' })
  DOCUMENTS = @(@{ service = 'api'; name = 'selected-site' }, @{ service = 'worker-pbc'; name = 'selected-site' })
  MAIL = @(@{ service = 'api'; name = 'outbound-mail' }, @{ service = 'worker-mail'; name = 'outbound-mail' })
  USER_CREATE = @(@{ service = 'api'; name = 'provisioning' })
  INVITATIONS = @(@{ service = 'api'; name = 'guest-invitation' })
  GROUP_MEMBERSHIP = @(@{ service = 'api'; name = 'group-membership' })
  CLIENT_SITES = @(@{ service = 'worker-client-sites'; name = 'client-sites' }, @{ service = 'worker-client-sites'; name = 'directory-reader' })
}
$script:CredentialSettingsByCapability = [ordered]@{
  LOGIN = @()
  DIRECTORY_READER = @(@{ service = 'api'; certificate = 'DirectoryReader__CertificatePath'; privateKey = 'DirectoryReader__PrivateKeyPath'; capability = 'directory-reader' })
  DOCUMENTS = @(
    @{ service = 'api'; certificate = 'SelectedSite__CertificatePath'; privateKey = 'SelectedSite__PrivateKeyPath'; capability = 'selected-site' },
    @{ service = 'worker-pbc'; certificate = 'SelectedSite__CertificatePath'; privateKey = 'SelectedSite__PrivateKeyPath'; capability = 'selected-site' }
  )
  MAIL = @(
    @{ service = 'api'; certificate = 'TenantAdministration__OutboundMail__CertificatePath'; privateKey = 'TenantAdministration__OutboundMail__PrivateKeyPath'; capability = 'outbound-mail' },
    @{ service = 'worker-mail'; certificate = 'GraphMail__CertificatePath'; privateKey = 'GraphMail__PrivateKeyPath'; capability = 'outbound-mail' }
  )
  USER_CREATE = @(@{ service = 'api'; certificate = 'TenantAdministration__Provisioning__CertificatePath'; privateKey = 'TenantAdministration__Provisioning__PrivateKeyPath'; capability = 'provisioning' })
  INVITATIONS = @(@{ service = 'api'; certificate = 'TenantAdministration__GuestInvitation__CertificatePath'; privateKey = 'TenantAdministration__GuestInvitation__PrivateKeyPath'; capability = 'guest-invitation' })
  GROUP_MEMBERSHIP = @(@{ service = 'api'; certificate = 'TenantAdministration__GroupMembership__CertificatePath'; privateKey = 'TenantAdministration__GroupMembership__PrivateKeyPath'; capability = 'group-membership' })
  CLIENT_SITES = @(
    @{ service = 'worker-client-sites'; certificate = 'ClientSites__CertificatePath'; privateKey = 'ClientSites__PrivateKeyPath'; capability = 'client-sites' },
    @{ service = 'worker-client-sites'; certificate = 'DirectoryReader__CertificatePath'; privateKey = 'DirectoryReader__PrivateKeyPath'; capability = 'directory-reader' }
  )
}
$script:EnabledSettingByCapability = [ordered]@{
  DIRECTORY_READER = @{ service = 'api'; environment = 'DirectoryReader__Enabled' }
  MAIL = @{ service = 'api'; environment = 'TenantAdministration__OutboundMail__Enabled' }
  USER_CREATE = @{ service = 'api'; environment = 'TenantAdministration__Provisioning__Enabled' }
  INVITATIONS = @{ service = 'api'; environment = 'TenantAdministration__GuestInvitation__Enabled' }
  GROUP_MEMBERSHIP = @{ service = 'api'; environment = 'TenantAdministration__GroupMembership__Enabled' }
  CLIENT_SITES = @{ service = 'worker-client-sites'; environment = 'ClientSites__Enabled' }
}

function Get-DeploymentMapValue($Map, [string] $Name) {
  if ($Map -is [System.Collections.IDictionary]) {
    foreach ($key in $Map.Keys) { if ([string]$key -ieq $Name) { return $Map[$key] } }
  }
  return $null
}

function Get-DeploymentPlanValue($Plan, [string] $Name) {
  return Get-DeploymentMapValue $Plan $Name
}

function ConvertFrom-AuditSphereEnabledCapabilityParameter([string] $Value) {
  if ([string]::IsNullOrWhiteSpace($Value)) { return ,@() }
  $allowed = @('LOGIN', 'DIRECTORY_READER', 'DOCUMENTS', 'MAIL', 'USER_CREATE', 'INVITATIONS', 'GROUP_MEMBERSHIP', 'CLIENT_SITES')
  $items = @($Value.Split(',') | ForEach-Object { $_.Trim().ToUpperInvariant() })
  if ($items.Count -eq 0 -or @($items | Where-Object { [string]::IsNullOrWhiteSpace($_) -or $_ -cnotin $allowed }).Count -gt 0) { throw 'enabled-capability-selection-invalid' }
  return ,@($items | Select-Object -Unique)
}

function Get-AuditSphereDeploymentSourceFiles([string] $RepositoryRoot, [string] $IsolationOverridePath) {
  $root = [System.IO.Path]::GetFullPath($RepositoryRoot)
  $paths = [System.Collections.Generic.List[string]]::new()
  $basePath = [System.IO.Path]::GetFullPath((Join-Path $root $script:ComposeRelativePath))
  if (-not [System.IO.File]::Exists($basePath)) { throw 'deployment-compose-source-not-found' }
  $paths.Add($basePath)
  if (-not [string]::IsNullOrWhiteSpace($IsolationOverridePath)) {
    $requestedPath = if ([System.IO.Path]::IsPathFullyQualified($IsolationOverridePath)) { $IsolationOverridePath } else { Join-Path $root $IsolationOverridePath }
    $requested = [System.IO.Path]::GetFullPath($requestedPath)
    $approved = [System.IO.Path]::GetFullPath((Join-Path $root $script:OverrideRelativePath))
    if (-not [string]::Equals($requested, $approved, [System.StringComparison]::Ordinal)) {
      throw 'deployment-override-not-approved'
    }
    if (-not [System.IO.File]::Exists($requested)) { throw 'deployment-override-not-found' }
    $paths.Add($requested)
  }

  $files = @(
    foreach ($path in $paths) {
      $relative = [System.IO.Path]::GetRelativePath($root, $path).Replace('\', '/')
      $bytes = [System.IO.File]::ReadAllBytes($path)
      $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
      [ordered]@{ path = $relative; sha256 = (($hash | ForEach-Object { $_.ToString('x2') }) -join '') }
    }
  )
  return ,$files
}

function Get-AuditSphereExactCheckoutEvidence([string] $RepositoryRoot, $SourceFiles, [string] $ExpectedSourceSha) {
  $failure = 'source-checkout-not-exact'
  $observedSha = $null
  $filesAtHead = $false
  $fileBindings = [System.Collections.Generic.List[object]]::new()
  try {
    if (-not (Test-M365ExactCheckoutRoot $RepositoryRoot)) {
      return [ordered]@{ state = 'FAIL'; observedSourceSha = $null; sourceFilesAtHead = $false; errorCode = $failure }
    }
    $headOutput = @(& git -C $RepositoryRoot rev-parse --verify HEAD 2>$null)
    $headExitCode = $LASTEXITCODE
    if ($headExitCode -ne 0 -or $headOutput.Count -ne 1) {
      return [ordered]@{ state = 'FAIL'; observedSourceSha = $null; sourceFilesAtHead = $false; errorCode = 'source-sha-unavailable' }
    }
    $candidateSha = ([string]$headOutput[0]).Trim().ToLowerInvariant()
    if ($candidateSha -notmatch '^[0-9a-f]{40}$') {
      return [ordered]@{ state = 'FAIL'; observedSourceSha = $null; sourceFilesAtHead = $false; errorCode = 'source-sha-invalid' }
    }
    $observedSha = $candidateSha
    if ($ExpectedSourceSha -cnotmatch '^[0-9a-fA-F]{40}$' -or $observedSha -cne $ExpectedSourceSha.ToLowerInvariant()) {
      return [ordered]@{ state = 'FAIL'; observedSourceSha = $observedSha; sourceFilesAtHead = $false; fileBindings = @(); errorCode = 'source-sha-mismatch' }
    }
    $relativePaths = @($SourceFiles | ForEach-Object { [string]$_.path })
    foreach ($relative in $relativePaths) {
      if ($relative -notin @($script:ComposeRelativePath, $script:OverrideRelativePath)) {
        return [ordered]@{ state = 'FAIL'; observedSourceSha = $observedSha; sourceFilesAtHead = $false; fileBindings = @(); errorCode = 'source-file-not-approved' }
      }
      $headBlobOutput = @(& git -C $RepositoryRoot rev-parse --verify ("HEAD:$relative") 2>$null)
      $headBlobExit = $LASTEXITCODE
      $workingBlobOutput = @(& git -C $RepositoryRoot hash-object --no-filters -- $relative 2>$null)
      $workingBlobExit = $LASTEXITCODE
      $headBlob = if ($headBlobOutput.Count -eq 1) { ([string]$headBlobOutput[0]).Trim().ToLowerInvariant() } else { '' }
      $workingBlob = if ($workingBlobOutput.Count -eq 1) { ([string]$workingBlobOutput[0]).Trim().ToLowerInvariant() } else { '' }
      $fullPath = Join-Path $RepositoryRoot $relative
      $fileInfo = [System.IO.FileInfo]::new($fullPath)
      $isLink = ([System.IO.File]::GetAttributes($fullPath) -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
      $observedFileHash = Get-AuditSphereDeploymentArtifactSha256 $fullPath
      $sourceFileRecord = @($SourceFiles | Where-Object { [string]$_.path -ceq $relative } | Select-Object -First 1)
      $fileHashMatches = $sourceFileRecord.Count -eq 1 -and [string]$sourceFileRecord[0].sha256 -ceq $observedFileHash
      $fileAtHead = -not $isLink -and $fileInfo.Exists -and $fileHashMatches -and $headBlobExit -eq 0 -and $workingBlobExit -eq 0 -and $headBlob -match '^[0-9a-f]{40}$' -and $headBlob -ceq $workingBlob
      $fileBindings.Add([ordered]@{ path = $relative; gitBlobSha = if ($fileAtHead) { $headBlob } else { $null }; observedSha256 = $observedFileHash; atHead = $fileAtHead })
    }
    $statusOutput = @(& git -C $RepositoryRoot status --porcelain --untracked-files=all -- @relativePaths 2>$null)
    $statusExitCode = $LASTEXITCODE
    $filesAtHead = $statusExitCode -eq 0 -and $statusOutput.Count -eq 0
    $filesAtHead = $filesAtHead -and @($fileBindings | Where-Object { -not $_.atHead }).Count -eq 0
    if ($statusExitCode -ne 0) { $failure = 'source-file-state-unavailable' }
    elseif (-not $filesAtHead) { $failure = 'deployment-source-file-dirty' }
    else { return [ordered]@{ state = 'PASS'; observedSourceSha = $observedSha; sourceFilesAtHead = $true; fileBindings = @($fileBindings); errorCode = $null } }
  } catch { $failure = 'source-checkout-inspection-failed' }
  return [ordered]@{ state = 'FAIL'; observedSourceSha = $observedSha; sourceFilesAtHead = $filesAtHead; fileBindings = @($fileBindings); errorCode = $failure }
}

function ConvertTo-DeploymentEnvironmentMap($Environment) {
  $values = [ordered]@{}
  if ($Environment -is [System.Collections.IDictionary]) {
    foreach ($key in $Environment.Keys) { $values[[string]$key] = $Environment[$key] }
  } elseif ($Environment -is [System.Collections.IEnumerable] -and $Environment -isnot [string]) {
    foreach ($entry in $Environment) {
      if ($entry -isnot [string]) { throw 'compose-environment-shape-invalid' }
      $separator = $entry.IndexOf('=')
      if ($separator -lt 1) { throw 'compose-environment-shape-invalid' }
      $values[$entry.Substring(0, $separator)] = $entry.Substring($separator + 1)
    }
  } elseif ($null -ne $Environment) {
    throw 'compose-environment-shape-invalid'
  }
  return $values
}

function Test-DeploymentRedactionMarker([string] $Value) {
  if ([string]::IsNullOrEmpty($Value)) { return $true }
  return $Value -match '^(?i:(?:<redacted(?:[: ][A-Z0-9_.-]+)?>|\[redacted(?:[: ][A-Z0-9_.-]+)?\]|redacted))$'
}

function Assert-DeploymentConfigSanitized($Model) {
  $services = Get-DeploymentMapValue $Model 'services'
  if ($services -isnot [System.Collections.IDictionary]) { throw 'compose-services-missing' }
  foreach ($serviceName in $services.Keys) {
    $service = Get-DeploymentMapValue $services ([string]$serviceName)
    $environment = ConvertTo-DeploymentEnvironmentMap (Get-DeploymentMapValue $service 'environment')
    foreach ($name in $environment.Keys) {
      $value = if ($null -eq $environment[$name]) { '' } else { [string]$environment[$name] }
      $keyName = [string]$name
      $isReference = $keyName -match '(?i)(?:path|file|reference|ref)$'
      $isSecretValueName = $keyName -match '(?i)(?:password|passwd|secret.?key|secret|access.?token|refresh.?token|client.?assertion|bootstrap.?proof.?hash|api.?key|signing.?key|encryption.?key|private.?key)'
      if (-not $isReference -and $isSecretValueName) {
        if (-not (Test-DeploymentRedactionMarker $value)) { throw 'compose-config-not-sanitized' }
      }
      if ($keyName -match '(?i)^ConnectionStrings__' -and $value -match '(?i)(?:password|pwd)\s*=\s*([^;\s]+)') {
        if (-not (Test-DeploymentRedactionMarker $Matches[1])) { throw 'compose-config-not-sanitized' }
      }
    }
  }
}

function Read-AuditSphereSanitizedComposeConfig([string] $Path) {
  if ([string]::IsNullOrWhiteSpace($Path) -or -not [System.IO.File]::Exists($Path)) { throw 'compose-config-json-not-found' }
  $info = [System.IO.FileInfo]::new([System.IO.Path]::GetFullPath($Path))
  if ($info.Length -gt 10MB) { throw 'compose-config-json-too-large' }
  try {
    $raw = [System.IO.File]::ReadAllText($info.FullName, [System.Text.UTF8Encoding]::new($false, $true))
    $model = $raw | ConvertFrom-Json -AsHashtable -Depth 64
  } catch { throw 'compose-config-json-invalid' }
  if ($model -isnot [System.Collections.IDictionary]) { throw 'compose-config-json-invalid' }
  Assert-DeploymentConfigSanitized $model
  return $model
}

function Get-AuditSphereDeploymentArtifactSha256([string] $Path) {
  $bytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($Path))
  $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
  return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-DeploymentServiceEnvironment($Model, [string] $ServiceName) {
  $services = Get-DeploymentMapValue $Model 'services'
  $service = Get-DeploymentMapValue $services $ServiceName
  if ($null -eq $service) { return $null }
  return ConvertTo-DeploymentEnvironmentMap (Get-DeploymentMapValue $service 'environment')
}

function Get-DeploymentString($Map, [string] $Name) {
  $value = Get-DeploymentMapValue $Map $Name
  if ($null -eq $value) { return $null }
  if ($value -is [bool]) { return $value.ToString().ToLowerInvariant() }
  return [string]$value
}

function Test-DeploymentGuid([string] $Value) {
  $parsed = [guid]::Empty
  return -not [string]::IsNullOrWhiteSpace($Value) -and [guid]::TryParse($Value, [ref]$parsed)
}

function Get-DeploymentService($Model, [string] $ServiceName) {
  return Get-DeploymentMapValue (Get-DeploymentMapValue $Model 'services') $ServiceName
}

function Get-DeploymentVolumeDevice($Model, [string] $VolumeName) {
  $volumes = Get-DeploymentMapValue $Model 'volumes'
  $volume = Get-DeploymentMapValue $volumes $VolumeName
  if ($null -eq $volume -and $volumes -is [System.Collections.IDictionary]) {
    foreach ($key in $volumes.Keys) {
      if ([string]$key -match ('(?:^|_)' + [regex]::Escape($VolumeName) + '$')) { $volume = $volumes[$key]; break }
    }
  }
  $driverOpts = Get-DeploymentMapValue $volume 'driver_opts'
  if ((Get-DeploymentString $volume 'driver') -cne 'local' -or
      (Get-DeploymentString $driverOpts 'type') -cne 'none' -or
      (Get-DeploymentString $driverOpts 'o') -cne 'bind') { return $null }
  $device = Get-DeploymentMapValue $driverOpts 'device'
  if ($null -eq $device) { return $null }
  return [string]$device
}

function ConvertTo-DeploymentMountRecord($Model, $Mount) {
  $source = $null; $target = $null; $readOnly = $false; $createHostPath = $null; $type = $null
  if ($Mount -is [System.Collections.IDictionary]) {
    $source = Get-DeploymentString $Mount 'source'
    $target = Get-DeploymentString $Mount 'target'
    $readOnlyValue = Get-DeploymentMapValue $Mount 'read_only'
    if ($null -eq $readOnlyValue) { $readOnlyValue = Get-DeploymentMapValue $Mount 'readOnly' }
    $readOnly = $readOnlyValue -eq $true -or [string]$readOnlyValue -eq 'true'
    $type = Get-DeploymentString $Mount 'type'
    $bind = Get-DeploymentMapValue $Mount 'bind'
    $createHostPath = Get-DeploymentMapValue $bind 'create_host_path'
  } elseif ($Mount -is [string]) {
    $parts = ([string]$Mount).Split(':')
    if ($parts.Count -lt 2) { throw 'compose-volume-shape-invalid' }
    $source = $parts[0]; $target = $parts[1]; $type = if ($source.StartsWith('/')) { 'bind' } else { 'volume' }
    $readOnly = $parts.Count -gt 2 -and $parts[2].Split(',') -contains 'ro'
  } else { throw 'compose-volume-shape-invalid' }

  $resolvedSource = $source
  if ($type -eq 'volume' -and $source -and -not $source.StartsWith('/')) {
    $device = Get-DeploymentVolumeDevice $Model $source
    if ($device) { $resolvedSource = $device }
  }
  return [ordered]@{ source = $source; resolvedSource = $resolvedSource; target = $target; readOnly = $readOnly; createHostPath = $createHostPath; type = $type }
}

function Get-DeploymentServiceMounts($Model, [string] $ServiceName) {
  $service = Get-DeploymentService $Model $ServiceName
  $rawMounts = Get-DeploymentMapValue $service 'volumes'
  if ($null -eq $rawMounts) { return ,@() }
  return ,@($rawMounts | ForEach-Object { ConvertTo-DeploymentMountRecord $Model $_ })
}

function Test-PathSameOrBelow([string] $Path, [string] $Root) {
  if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
  $pathValue = $Path.Replace('\', '/').TrimEnd('/')
  $rootValue = $Root.Replace('\', '/').TrimEnd('/')
  return $pathValue.Equals($rootValue, [System.StringComparison]::OrdinalIgnoreCase) -or
    $pathValue.StartsWith(($rootValue + '/'), [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-PathSameOrAbove([string] $Path, [string] $Root) {
  if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
  $pathValue = $Path.Replace('\', '/').TrimEnd('/')
  $rootValue = $Root.Replace('\', '/').TrimEnd('/')
  return $rootValue.Equals($pathValue, [System.StringComparison]::OrdinalIgnoreCase) -or
    $rootValue.StartsWith(($pathValue + '/'), [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-DeploymentCanonicalAbsolutePath([string] $Path) {
  if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Contains('\') -or $Path -notmatch '^/(?:[A-Za-z0-9._-]+/)*[A-Za-z0-9._-]+$') { return $false }
  foreach ($segment in $Path.Split('/')) { if ($segment -in @('.', '..')) { return $false } }
  return $true
}

function Test-DeploymentCredentialLeaf([string] $Path, [string] $Root, [string[]] $AllowedLeaves) {
  if (-not (Test-DeploymentCanonicalAbsolutePath $Path)) { return $false }
  $prefix = $Root.TrimEnd('/') + '/'
  if (-not $Path.StartsWith($prefix, [System.StringComparison]::Ordinal)) { return $false }
  $leaf = $Path.Substring($prefix.Length)
  return $leaf -cin $AllowedLeaves
}

function Test-DeploymentCredentialReference([string] $Reference) {
  if ([string]::IsNullOrWhiteSpace($Reference) -or $Reference.Length -gt 256 -or $Reference -notmatch '^(?i:(?:file|certstore|configuration|keyvault)):(?<value>[A-Za-z0-9._/-]+)$') { return $false }
  $value = $Matches.value
  foreach ($segment in $value.Split('/')) { if ($segment -in @('.', '..')) { return $false } }
  if ($Reference.StartsWith('file:', [System.StringComparison]::OrdinalIgnoreCase)) { return $value.StartsWith('/', [System.StringComparison]::Ordinal) -and (Test-DeploymentCanonicalAbsolutePath $value) }
  return $true
}

function Get-AuditSphereDeploymentSettings($Plan, $Model, $SourceFiles, [string[]] $EnabledCapabilities = @(), [string] $OperatorDeclaredEnvironment = $null, $SourceEvidence = $null) {
  $plannedPublicIds = [ordered]@{
    tenantId = Get-DeploymentPlanValue $Plan 'tenantId'
    firmId = Get-DeploymentPlanValue $Plan 'firmId'
    operatorClientId = Get-DeploymentMapValue (Get-DeploymentPlanValue $Plan 'operator') 'clientId'
    capabilities = @()
  }
  $plannedPublicIds.capabilities = @((Get-DeploymentPlanValue $Plan 'capabilities') | ForEach-Object {
    [ordered]@{ capability = [string](Get-DeploymentMapValue $_ 'kind'); clientId = Get-DeploymentMapValue $_ 'clientId' }
  })
  $plannedCredentialBindings = [System.Collections.Generic.List[object]]::new()
  foreach ($capability in @((Get-DeploymentPlanValue $Plan 'capabilities'))) {
    $reference = [string](Get-DeploymentMapValue $capability 'certificateReference')
    if ($reference -and -not (Test-DeploymentCredentialReference $reference)) {
      throw 'plan-credential-reference-unsafe'
    }
    $plannedCredentialBindings.Add([ordered]@{
      capability = [string](Get-DeploymentMapValue $capability 'kind')
      publicClientId = Get-DeploymentMapValue $capability 'clientId'
      referenceName = if ($reference) { ($reference -split ':', 2)[0] } else { $null }
      reference = if ($reference) { $reference } else { $null }
    })
  }

  $settings = [ordered]@{
    target = $script:AcceptedProjectName
    targetFiles = @($SourceFiles)
    expectedSourceSha = Get-DeploymentPlanValue $Plan 'repositoryCommit'
    expectedPlanEnvironment = Get-DeploymentString $Plan 'environment'
    operatorDeclaredEnvironment = $OperatorDeclaredEnvironment
    operatorDeclaredEnabledCapabilities = @($EnabledCapabilities | Sort-Object -Unique)
    observedSourceSha = if ($SourceEvidence) { $SourceEvidence.observedSourceSha } else { $null }
    sourceFilesAtHead = if ($SourceEvidence) { [bool]$SourceEvidence.sourceFilesAtHead } else { $null }
    sourceGitBindings = if ($SourceEvidence) { @($SourceEvidence.fileBindings) } else { @() }
    declaredEnvironment = $null
    declaredImageTagSha = $null
    modelProvenance = $null
    modelProvenanceState = if ($Model) { 'UNOBSERVED_OPERATOR_PROVIDED_MODEL' } else { 'UNOBSERVED' }
    artifactProvenance = $null
    artifactProvenanceState = 'UNOBSERVED'
    runtimeEnabledCapabilities = $null
    plannedPublicIds = $plannedPublicIds
    configuredPublicIds = $null
    plannedCredentialBindings = @($plannedCredentialBindings)
    configuredCredentialBindings = @()
    credentialMounts = @()
    storageRoots = @()
    outboundHosts = $null
    outboundEgressPolicy = $null
    runtimeEnvironment = $null
    runtimeMounts = $null
    schemaCompatibility = $null
    workerReadiness = $null
    rollbackPath = $null
  }
  if ($null -eq $Model) { return $settings }

  $apiEnvironment = Get-DeploymentServiceEnvironment $Model 'api'
  $configuredPublicIds = [ordered]@{
    tenantId = $null; firmId = $null; identity = $null; directoryReader = $null; selectedSite = $null
    provisioning = $null; guestInvitation = $null; groupMembership = $null; outboundMail = $null; clientSites = $null
  }
  foreach ($entry in $script:PublicIdEnvironmentNames.GetEnumerator()) {
    $ownerEnvironment = Get-DeploymentServiceEnvironment $Model $script:PublicIdentityOwners[$entry.Key]
    $value = Get-DeploymentString $ownerEnvironment $entry.Value
    if (Test-DeploymentGuid $value) { $configuredPublicIds[$entry.Key] = $value }
  }
  $tenantId = Get-DeploymentString $apiEnvironment 'Identity__TenantId'
  $firmId = Get-DeploymentString $apiEnvironment 'Application__FirmId'
  if (Test-DeploymentGuid $tenantId) { $configuredPublicIds.tenantId = $tenantId }
  if (Test-DeploymentGuid $firmId) { $configuredPublicIds.firmId = $firmId }
  $settings.configuredPublicIds = $configuredPublicIds
  $declaredEnvironment = Get-DeploymentString $apiEnvironment 'ASPNETCORE_ENVIRONMENT'
  if ($declaredEnvironment -in @('Acceptance', 'Development', 'Test', 'Production')) { $settings.declaredEnvironment = $declaredEnvironment }

  $images = [System.Collections.Generic.List[string]]::new()
  $services = Get-DeploymentMapValue $Model 'services'
  foreach ($serviceName in $services.Keys) {
    $service = Get-DeploymentService $Model ([string]$serviceName)
    $image = Get-DeploymentString $service 'image'
    if ($image -match ':(?<sha>[0-9a-fA-F]{40})$') { $images.Add($Matches.sha.ToLowerInvariant()) }
  }
  if ($images.Count -gt 0 -and @($images | Select-Object -Unique).Count -eq 1) { $settings.declaredImageTagSha = $images[0] }

  $targetCredentials = @(
    @{ service = 'api'; capability = 'directory-reader'; name = 'DirectoryReader__PrivateKeyPath' },
    @{ service = 'api'; capability = 'directory-reader'; name = 'DirectoryReader__CertificatePath' },
    @{ service = 'api'; capability = 'selected-site'; name = 'SelectedSite__PrivateKeyPath' },
    @{ service = 'api'; capability = 'selected-site'; name = 'SelectedSite__CertificatePath' },
    @{ service = 'api'; capability = 'provisioning'; name = 'TenantAdministration__Provisioning__PrivateKeyPath' },
    @{ service = 'api'; capability = 'provisioning'; name = 'TenantAdministration__Provisioning__CertificatePath' },
    @{ service = 'api'; capability = 'guest-invitation'; name = 'TenantAdministration__GuestInvitation__PrivateKeyPath' },
    @{ service = 'api'; capability = 'guest-invitation'; name = 'TenantAdministration__GuestInvitation__CertificatePath' },
    @{ service = 'api'; capability = 'group-membership'; name = 'TenantAdministration__GroupMembership__PrivateKeyPath' },
    @{ service = 'api'; capability = 'group-membership'; name = 'TenantAdministration__GroupMembership__CertificatePath' },
    @{ service = 'api'; capability = 'outbound-mail'; name = 'TenantAdministration__OutboundMail__PrivateKeyPath' },
    @{ service = 'api'; capability = 'outbound-mail'; name = 'TenantAdministration__OutboundMail__CertificatePath' },
    @{ service = 'worker-mail'; capability = 'outbound-mail'; name = 'GraphMail__PrivateKeyPath' },
    @{ service = 'worker-mail'; capability = 'outbound-mail'; name = 'GraphMail__CertificatePath' },
    @{ service = 'worker-pbc'; capability = 'selected-site'; name = 'SelectedSite__PrivateKeyPath' },
    @{ service = 'worker-pbc'; capability = 'selected-site'; name = 'SelectedSite__CertificatePath' },
    @{ service = 'worker-client-sites'; capability = 'client-sites'; name = 'ClientSites__PrivateKeyPath' },
    @{ service = 'worker-client-sites'; capability = 'client-sites'; name = 'ClientSites__CertificatePath' },
    @{ service = 'worker-client-sites'; capability = 'directory-reader'; name = 'DirectoryReader__CertificatePath' },
    @{ service = 'worker-client-sites'; capability = 'directory-reader'; name = 'DirectoryReader__PrivateKeyPath' }
  )
  $configuredCredentialBindings = [System.Collections.Generic.List[object]]::new()
  foreach ($entry in $targetCredentials) {
    $environment = Get-DeploymentServiceEnvironment $Model $entry.service
    $reference = Get-DeploymentString $environment $entry.name
    $expectedReference = "/run/auditsphere/m365/$($entry.capability)/$(if ($entry.name -match 'PrivateKeyPath$') { 'private-key' } else { 'certificate' }).pem"
    if ($reference -ceq $expectedReference -and (Test-DeploymentCanonicalAbsolutePath $reference)) {
      $configuredCredentialBindings.Add([ordered]@{
        capability = $entry.capability
        service = $entry.service
        referenceName = $entry.name
        reference = $reference
      })
    }
  }
  $configuredCredentialBindings.Add([ordered]@{
    capability = 'identity'; service = 'api'; referenceName = 'IDENTITY_CLIENT_SECRET'; reference = 'environment:IDENTITY_CLIENT_SECRET'
  })
  $settings.configuredCredentialBindings = @($configuredCredentialBindings)

  $credentialMounts = [System.Collections.Generic.List[object]]::new()
  foreach ($serviceName in $services.Keys) {
    foreach ($mount in (Get-DeploymentServiceMounts $Model $serviceName)) {
      $credentialRelated = (Test-PathSameOrBelow $mount.target $script:CredentialTargetRoot) -or
          (Test-PathSameOrBelow $mount.resolvedSource $script:CredentialHostRoot) -or
          (Test-PathSameOrAbove $mount.target $script:CredentialTargetRoot) -or
          (Test-PathSameOrAbove $mount.resolvedSource $script:CredentialHostRoot)
      if (-not $credentialRelated) { continue }
      $capability = $null
      if (Test-DeploymentCredentialLeaf $mount.target $script:CredentialTargetRoot @('directory-reader', 'selected-site', 'provisioning', 'guest-invitation', 'group-membership', 'outbound-mail', 'client-sites')) {
        $capability = $mount.target.Substring(($script:CredentialTargetRoot + '/').Length)
      }
      $allowedService = $script:AllowedCredentialMountsByService.Contains([string]$serviceName) -and
        $capability -and $capability -cin @($script:AllowedCredentialMountsByService[[string]$serviceName])
      $approvedReferences = $capability -and
        $mount.resolvedSource -ceq ($script:CredentialHostRoot + '/' + $capability) -and
        $mount.target -ceq ($script:CredentialTargetRoot + '/' + $capability) -and
        (Test-DeploymentCredentialLeaf $mount.resolvedSource $script:CredentialHostRoot @($capability)) -and
        [bool]$mount.readOnly -and $mount.type -in @('bind', 'volume')
      $accepted = $allowedService -and $approvedReferences
      $credentialMounts.Add([ordered]@{
        service = if ($serviceName -in @('api', 'worker-mail', 'worker-pbc', 'worker-client-sites')) { [string]$serviceName } else { 'unrecognized-service' }
        capability = if ($accepted) { $capability } else { $null }
        sourceReference = if ($accepted) { $mount.resolvedSource } else { $null }
        targetReference = if ($accepted) { $mount.target } else { $null }
        readOnly = [bool]$mount.readOnly
        state = if ($accepted) { 'APPROVED_REFERENCE' } else { 'REJECTED_WITHHELD' }
      })
    }
  }
  $settings.credentialMounts = @($credentialMounts)

  $storageRoots = [System.Collections.Generic.List[object]]::new()
  $expectedStorage = [ordered]@{
    postgres_data = '/opt/auditsphere/storage/postgres'
    data_protection = '/opt/auditsphere/storage/data-protection'
    pbc_staging = '/opt/auditsphere/storage/pbc-staging'
    release_checkpoints = '/opt/auditsphere/storage/release-checkpoints'
    m365_mail_credentials = '/opt/auditsphere/storage/m365-credentials/outbound-mail'
    m365_selected_site_credentials = '/opt/auditsphere/storage/m365-credentials/selected-site'
  }
  foreach ($volumeName in $expectedStorage.Keys) {
    $device = Get-DeploymentVolumeDevice $Model $volumeName
    $expectedDevice = [string]$expectedStorage[$volumeName]
    $storageRoots.Add([ordered]@{
      name = $volumeName
      reference = if ($device -ceq $expectedDevice) { $expectedDevice } else { $null }
      state = if ($device -ceq $expectedDevice) { 'MATCH' } else { 'MISMATCH_OR_MISSING' }
    })
  }
  $settings.storageRoots = @($storageRoots)
  return $settings
}

function New-AuditSpherePlannedDeploymentEvidence($SourceFiles, [string] $ExpectedSourceSha, [string] $ExpectedEnvironment, [string[]] $EnabledCapabilities = @(), $SourceEvidence = $null) {
  return [ordered]@{
    target = $script:AcceptedProjectName
    targetFiles = @($SourceFiles)
    expectedSourceSha = $ExpectedSourceSha
    expectedPlanEnvironment = $ExpectedEnvironment
    operatorDeclaredEnvironment = $null
    operatorDeclaredEnabledCapabilities = @($EnabledCapabilities | Sort-Object -Unique)
    outboundHosts = $null
    outboundEgressPolicy = $null
    declaredEnvironment = $null
    declaredImageTagSha = $null
    observedSourceSha = if ($SourceEvidence) { $SourceEvidence.observedSourceSha } else { $null }
    sourceFilesAtHead = if ($SourceEvidence) { [bool]$SourceEvidence.sourceFilesAtHead } else { $null }
    sourceGitBindings = if ($SourceEvidence) { @($SourceEvidence.fileBindings) } else { @() }
    modelProvenance = $null
    modelProvenanceState = 'UNOBSERVED'
    artifactProvenance = $null
    artifactProvenanceState = 'UNOBSERVED'
    runtimeEnabledCapabilities = $null
    runtimeEnvironment = $null
    runtimeMounts = $null
    schemaCompatibility = $null
    workerReadiness = $null
    rollbackPath = $null
    evidenceLevel = 'SOURCE_FILES_ONLY'
  }
}

function Get-AuditSphereDeploymentChecks($Plan, $Model, [string[]] $EnabledCapabilities = @(), [string] $OperatorDeclaredEnvironment = $null, $SourceEvidence = $null) {
  $checks = [System.Collections.Generic.List[object]]::new()
  $add = {
    param([string]$Id, [string]$State, [string]$Expected, [string]$Observed, [string]$ErrorCode)
    $checks.Add([ordered]@{ id = $Id; state = $State; expected = $Expected; observed = $Observed; errorCode = $ErrorCode })
  }
  $planSha = [string](Get-DeploymentPlanValue $Plan 'repositoryCommit')
  $planEnvironment = [string](Get-DeploymentPlanValue $Plan 'environment')
  $sourceState = if ($SourceEvidence) { [string]$SourceEvidence.state } else { 'MISSING' }
  & $add 'exact-checkout-source-binding' $sourceState $planSha $(if ($sourceState -eq 'PASS') { 'checkout and selected files match plan source' } else { 'exact source binding unavailable or mismatched' }) $(if ($SourceEvidence) { $SourceEvidence.errorCode } else { 'source-checkout-evidence-missing' })

  $environmentState = if (-not $OperatorDeclaredEnvironment) { 'MISSING' } elseif ($planEnvironment -cne 'Acceptance' -or $OperatorDeclaredEnvironment -cne 'Acceptance' -or $planEnvironment -cne $OperatorDeclaredEnvironment) { 'FAIL' } else { 'PASS' }
  & $add 'plan-and-operator-environment-assertion' $environmentState 'plan.environment and operator declaration are both Acceptance' $(if ($environmentState -eq 'PASS') { 'explicit Acceptance assertion matches plan' } elseif ($environmentState -eq 'MISSING') { 'operator target environment not declared' } else { 'plan and operator target do not both assert Acceptance' }) 'deployment-environment-assertion-mismatch'

  $plannedCapabilities = @((Get-DeploymentPlanValue $Plan 'capabilities'))
  $plannedKinds = @($plannedCapabilities | ForEach-Object { [string](Get-DeploymentMapValue $_ 'kind') })
  $selected = @($EnabledCapabilities | Sort-Object -Unique)
  $unplannedSelection = @($selected | Where-Object { $_ -cnotin $plannedKinds })
  if ($selected.Count -eq 0) {
    & $add 'operator-enabled-capability-selection' 'MISSING' 'explicit enabled capability selection from validated plan' 'no enabled groups declared by operator' 'enabled-capabilities-not-declared'
  } elseif ($unplannedSelection.Count -gt 0) {
    & $add 'operator-enabled-capability-selection' 'FAIL' 'only capabilities present in validated plan may be selected' 'selection includes a capability absent from plan' 'enabled-capability-not-planned'
  } else {
    & $add 'operator-enabled-capability-selection' 'PASS' 'only capabilities present in validated plan may be selected' 'operator selection is a subset of planned capabilities' $null
  }

  $targetTenantId = Get-DeploymentString $Plan 'tenantId'
  $configuredTenantId = Get-DeploymentString (Get-DeploymentServiceEnvironment $Model 'api') 'Identity__TenantId'
  $targetFirmId = Get-DeploymentString $Plan 'firmId'
  $configuredFirmId = Get-DeploymentString (Get-DeploymentServiceEnvironment $Model 'api') 'Application__FirmId'
  $scopeBindingState = if ($selected.Count -eq 0 -or $unplannedSelection.Count -gt 0 -or
      [string]::IsNullOrWhiteSpace($configuredTenantId) -or [string]::IsNullOrWhiteSpace($configuredFirmId)) { 'MISSING' }
    elseif (-not (Test-DeploymentGuid $configuredTenantId) -or -not (Test-DeploymentGuid $configuredFirmId) -or
      $configuredTenantId -ine $targetTenantId -or $configuredFirmId -ine $targetFirmId) { 'FAIL' }
    else { 'PASS' }
  & $add 'plan-tenant-firm-scope-bindings' $scopeBindingState 'configured API tenant and firm identifiers match validated plan' $(if ($scopeBindingState -eq 'PASS') { 'tenant and firm scope match' } elseif ($scopeBindingState -eq 'MISSING') { 'operator selection or configured scope identifier missing' } else { 'configured scope differs from plan' }) 'deployment-plan-scope-mismatch'

  $services = Get-DeploymentMapValue $Model 'services'
  $projectName = Get-DeploymentString $Model 'name'
  & $add 'compose-project-name' $(if ($projectName -ceq $script:AcceptedProjectName) { 'PASS' } else { 'FAIL' }) $script:AcceptedProjectName $(if ($projectName -ceq $script:AcceptedProjectName) { 'approved target' } else { 'missing or mismatched' }) 'compose-project-name-mismatch'

  $requiredServices = @('api', 'db', 'migrate', 'worker-general', 'worker-processing', 'worker-records')
  foreach ($kind in $selected) {
    switch ($kind) {
      'MAIL' { $requiredServices += 'worker-mail' }
      'DOCUMENTS' { $requiredServices += 'worker-pbc' }
      'CLIENT_SITES' { $requiredServices += 'worker-client-sites' }
    }
  }
  $requiredServices = @($requiredServices | Sort-Object -Unique)
  $missingServices = @($requiredServices | Where-Object { $null -eq (Get-DeploymentService $Model $_) })
  & $add 'required-services-present' $(if ($missingServices.Count -eq 0) { 'PASS' } else { 'FAIL' }) 'core services and selected capability workers are present' $(if ($missingServices.Count -eq 0) { 'all required services present' } else { 'one or more required services missing' }) 'compose-service-set-mismatch'

  $imageFailures = 0
  $imageServices = @('api', 'migrate', 'worker-general', 'worker-processing', 'worker-records')
  foreach ($optionalWorker in @('worker-mail', 'worker-pbc', 'worker-client-sites')) {
    if ($null -ne (Get-DeploymentService $Model $optionalWorker)) { $imageServices += $optionalWorker }
  }
  foreach ($kind in $selected) {
    switch ($kind) {
      'MAIL' { $imageServices += 'worker-mail' }
      'DOCUMENTS' { $imageServices += 'worker-pbc' }
      'CLIENT_SITES' { $imageServices += 'worker-client-sites' }
    }
  }
  foreach ($serviceName in @($imageServices | Sort-Object -Unique)) {
    $service = Get-DeploymentService $Model $serviceName
    $image = Get-DeploymentString $service 'image'
    $prefix = if ($serviceName -eq 'api') { 'ghcr.io/nirzaf/auditsphere-acceptance-api' } elseif ($serviceName -eq 'migrate') { 'ghcr.io/nirzaf/auditsphere-acceptance-migrations' } else { 'ghcr.io/nirzaf/auditsphere-acceptance-worker' }
    if (-not $image -or -not $image.StartsWith(($prefix + ':'), [System.StringComparison]::OrdinalIgnoreCase)) { $imageFailures++; continue }
    $tag = $image.Substring($prefix.Length + 1)
    if ($tag -cnotmatch '^[0-9a-fA-F]{40}$' -or $tag -ine $planSha) { $imageFailures++ }
  }
  & $add 'declared-image-tags-match-plan' $(if ($imageFailures -eq 0) { 'PASS' } else { 'FAIL' }) 'required image names and tags declare the plan SHA' $(if ($imageFailures -eq 0) { 'declared tags match; artifact provenance remains unobserved' } else { 'declared image tag binding mismatch' }) 'deployment-image-tag-mismatch'

  $apiEnv = Get-DeploymentServiceEnvironment $Model 'api'
  $apiEnvironment = Get-DeploymentString $apiEnv 'ASPNETCORE_ENVIRONMENT'
  $apiEnvironmentState = if (-not $OperatorDeclaredEnvironment) { 'MISSING' } elseif ($apiEnvironment -ceq 'Acceptance' -and $apiEnvironment -ceq $planEnvironment -and $apiEnvironment -ceq $OperatorDeclaredEnvironment) { 'PASS' } else { 'FAIL' }
  & $add 'api-environment-explicit-acceptance' $apiEnvironmentState 'API environment explicitly matches plan and operator Acceptance' $(if ($apiEnvironmentState -eq 'PASS') { 'declared Acceptance' } elseif ($apiEnvironmentState -eq 'MISSING') { 'operator environment assertion missing' } else { 'missing or mismatched declaration' }) 'deployment-environment-mismatch'
  $apiEffects = Get-DeploymentString $apiEnv 'ExternalEffects__Enabled'
  $apiSimulation = Get-DeploymentString $apiEnv 'Application__AllowSimulationAdapters'
  $apiDevelopmentIdentity = Get-DeploymentString $apiEnv 'DevelopmentIdentity__Enabled'
  $apiSafeFlags = $apiEffects -ceq 'false' -and $apiSimulation -ceq 'false' -and $apiDevelopmentIdentity -ceq 'false'
  & $add 'api-external-and-simulation-flags' $(if ($apiSafeFlags) { 'PASS' } else { 'FAIL' }) 'external effects, simulation, and development identity disabled' $(if ($apiSafeFlags) { 'all disabled' } else { 'flag mismatch' }) 'api-runtime-flags-mismatch'

  $workerNames = @($services.Keys | Where-Object { [string]$_ -match '^worker(?:-|$)' } | Sort-Object)
  $workerEnvironmentFailures = 0
  $workerEnvironmentMissing = 0
  foreach ($workerName in $workerNames) {
    $workerEnv = Get-DeploymentServiceEnvironment $Model ([string]$workerName)
    $aspnet = Get-DeploymentString $workerEnv 'ASPNETCORE_ENVIRONMENT'
    $dotnet = Get-DeploymentString $workerEnv 'DOTNET_ENVIRONMENT'
    if ([string]::IsNullOrWhiteSpace($aspnet) -or [string]::IsNullOrWhiteSpace($dotnet)) { $workerEnvironmentMissing++; continue }
    if ($aspnet -cne 'Acceptance' -or $dotnet -cne 'Acceptance' -or $aspnet -cne $planEnvironment -or ($OperatorDeclaredEnvironment -and $dotnet -cne $OperatorDeclaredEnvironment)) { $workerEnvironmentFailures++ }
  }
  $workerEnvironmentState = if ($workerEnvironmentFailures -gt 0) { 'FAIL' } elseif ($workerEnvironmentMissing -gt 0 -or $workerNames.Count -eq 0 -or -not $OperatorDeclaredEnvironment) { 'MISSING' } else { 'PASS' }
  & $add 'all-declared-worker-environments-explicit' $workerEnvironmentState 'every declared worker sets ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT to Acceptance' $(if ($workerEnvironmentState -eq 'PASS') { 'all declared workers match' } elseif ($workerEnvironmentState -eq 'MISSING') { 'one or more worker environment declarations are missing' } else { 'one or more worker environment declarations mismatch' }) 'worker-environment-mismatch'

  foreach ($role in @(
    @{ name = 'worker-general'; group = $null; externalEffects = 'false'; simulation = 'AllowSimulationAdapters' },
    @{ name = 'worker-processing'; group = 'processing'; externalEffects = 'false' },
    @{ name = 'worker-records'; group = 'records'; externalEffects = 'false' },
    @{ name = 'worker-mail'; group = 'mail'; externalEffects = 'true'; capability = 'MAIL' },
    @{ name = 'worker-pbc'; group = 'pbc'; externalEffects = 'true'; capability = 'DOCUMENTS' },
    @{ name = 'worker-client-sites'; group = 'client-sites'; externalEffects = 'true'; capability = 'CLIENT_SITES' }
  )) {
    if ($role.ContainsKey('capability') -and $role.capability -notin $selected -and $null -eq (Get-DeploymentService $Model $role.name)) { continue }
    $roleEnv = Get-DeploymentServiceEnvironment $Model $role.name
    $group = Get-DeploymentString $roleEnv 'Worker__Group'
    $effects = Get-DeploymentString $roleEnv 'ExternalEffects__Enabled'
    $expectedGroup = if ($role.group) { $role.group } else { 'core worker group' }
    $rolePass = $null -ne $roleEnv -and $effects -ceq $role.externalEffects -and ((-not $role.group) -or $group -ceq $role.group)
    $roleMissing = $null -eq $roleEnv
    & $add "process-role:$($role.name)" $(if ($rolePass) { 'PASS' } elseif ($roleMissing -and $role.ContainsKey('capability') -and $role.capability -notin $selected) { 'MISSING' } elseif ($roleMissing) { 'FAIL' } else { 'FAIL' }) "service role and effects match $expectedGroup" $(if ($rolePass) { 'declared as expected' } elseif ($roleMissing) { 'service declaration missing' } else { 'role binding mismatch' }) 'worker-capability-role-mismatch'
    if ($role.ContainsKey('simulation')) {
      $simulation = Get-DeploymentString $roleEnv $role.simulation
      & $add 'worker-general-effects-disabled' $(if ($effects -ceq 'false' -and $simulation -ceq 'false') { 'PASS' } else { 'FAIL' }) 'external effects and simulation disabled for core worker' $(if ($effects -ceq 'false' -and $simulation -ceq 'false') { 'both disabled' } else { 'flag mismatch' }) 'worker-general-effects-enabled'
    }
  }

  $apiClientSitesBindingPresent = $false
  foreach ($name in $apiEnv.Keys) {
    if ([string]$name -match '(?i)^ClientSites__(?:ClientId|.*(?:Certificate|Key|Secret|Password|Token|CredentialReference))' -and
        -not [string]::IsNullOrWhiteSpace((Get-DeploymentString $apiEnv ([string]$name)))) { $apiClientSitesBindingPresent = $true }
  }
  $apiIsolationValuesAbsent = -not $apiClientSitesBindingPresent

  $mountViolations = 0
  $mountPairs = [System.Collections.Generic.List[string]]::new()
  $apiClientSitesMount = $false
  $mountLeaves = @('directory-reader', 'selected-site', 'provisioning', 'guest-invitation', 'group-membership', 'outbound-mail', 'client-sites')
  foreach ($serviceName in $services.Keys) {
    foreach ($mount in (Get-DeploymentServiceMounts $Model ([string]$serviceName))) {
      $source = [string]$mount.resolvedSource
      $target = [string]$mount.target
      $credentialRelated = (Test-PathSameOrBelow $target $script:CredentialTargetRoot) -or (Test-PathSameOrBelow $source $script:CredentialHostRoot) -or
        (Test-PathSameOrAbove $target $script:CredentialTargetRoot) -or (Test-PathSameOrAbove $source $script:CredentialHostRoot)
      if (-not $credentialRelated) { continue }
      if ($serviceName -eq 'api' -and (
          (Test-PathSameOrBelow $source ($script:CredentialHostRoot + '/client-sites')) -or
          (Test-PathSameOrBelow $target ($script:CredentialTargetRoot + '/client-sites')) -or
          (Test-PathSameOrAbove $source ($script:CredentialHostRoot + '/client-sites')) -or
          (Test-PathSameOrAbove $target ($script:CredentialTargetRoot + '/client-sites')))) { $apiClientSitesMount = $true }
      $capability = $null
      if (Test-DeploymentCredentialLeaf $target $script:CredentialTargetRoot $mountLeaves) { $capability = $target.Substring(($script:CredentialTargetRoot + '/').Length) }
      $allowedService = $script:AllowedCredentialMountsByService.Contains([string]$serviceName) -and $capability -and $capability -cin @($script:AllowedCredentialMountsByService[[string]$serviceName])
      $safePair = $capability -and $source -ceq ($script:CredentialHostRoot + '/' + $capability) -and $target -ceq ($script:CredentialTargetRoot + '/' + $capability) -and
        (Test-DeploymentCredentialLeaf $source $script:CredentialHostRoot @($capability)) -and [bool]$mount.readOnly -and $mount.type -in @('bind', 'volume')
      if (-not ($allowedService -and $safePair)) { $mountViolations++; continue }
      $pair = ([string]$serviceName) + '|' + $capability
      if ($mountPairs -contains $pair) { $mountViolations++ } else { $mountPairs.Add($pair) }
    }
  }
  if ($apiClientSitesMount) { $apiIsolationValuesAbsent = $false }
  & $add 'role-specific-readonly-credential-mount-isolation' $(if ($mountViolations -eq 0) { 'PASS' } else { 'FAIL' }) 'all declared services, including extras, may mount only approved read-only capability leaves on their owner service' $(if ($mountViolations -eq 0) { 'all present credential mounts are narrowly role-bound' } else { 'one or more credential mounts are broad, writable, or on a non-owner service' }) 'credential-mount-isolation-mismatch'
  & $add 'api-client-sites-key-isolated' $(if ($apiIsolationValuesAbsent) { 'PASS' } else { 'FAIL' }) 'API has no client-sites ID, key path, certificate path, or client-sites credential mount' $(if ($apiIsolationValuesAbsent) { 'no client-sites credential binding in API' } else { 'client-sites credential binding present in API' }) 'api-client-sites-key-mounted'

  $missingMounts = 0
  foreach ($kind in $selected) {
    foreach ($needed in @($script:RequiredCredentialMountsByCapability[$kind])) {
      $pair = ([string]$needed.service) + '|' + ([string]$needed.name)
      if ($mountPairs -notcontains $pair) { $missingMounts++ }
    }
  }
  $mountReadiness = if ($mountViolations -gt 0) { 'FAIL' } elseif ($selected.Count -eq 0 -or $unplannedSelection.Count -gt 0) { 'MISSING' } elseif ($missingMounts -gt 0) { 'MISSING' } else { 'PASS' }
  & $add 'enabled-capability-credential-mounts-present' $mountReadiness 'only selected planned groups require their exact read-only credential mount set' $(if ($mountReadiness -eq 'PASS') { 'selected groups have required mounts' } elseif ($mountReadiness -eq 'FAIL') { 'isolation violation detected' } else { 'one or more selected-group mounts are absent or selection is unavailable' }) 'enabled-capability-mount-missing'

  $credentialConfigFailures = 0
  $credentialConfigMissing = 0
  foreach ($kind in $selected) {
    foreach ($setting in @($script:CredentialSettingsByCapability[$kind])) {
      $env = Get-DeploymentServiceEnvironment $Model $setting.service
      $certificate = Get-DeploymentString $env $setting.certificate
      $privateKey = Get-DeploymentString $env $setting.privateKey
      $expectedCertificate = "$($script:CredentialTargetRoot)/$($setting.capability)/certificate.pem"
      $expectedPrivateKey = "$($script:CredentialTargetRoot)/$($setting.capability)/private-key.pem"
      if ([string]::IsNullOrWhiteSpace($certificate) -or [string]::IsNullOrWhiteSpace($privateKey)) { $credentialConfigMissing++ }
      elseif ($certificate -cne $expectedCertificate -or $privateKey -cne $expectedPrivateKey -or -not (Test-DeploymentCanonicalAbsolutePath $certificate) -or -not (Test-DeploymentCanonicalAbsolutePath $privateKey)) { $credentialConfigFailures++ }
    }
    if ($kind -eq 'LOGIN') {
      $clientSecret = Get-DeploymentString $apiEnv 'Identity__ClientSecret'
      if ([string]::IsNullOrWhiteSpace((Get-DeploymentString $apiEnv 'Identity__ClientId')) -or [string]::IsNullOrWhiteSpace($clientSecret)) { $credentialConfigMissing++ }
    }
  }
  $credentialConfigState = if ($credentialConfigFailures -gt 0) { 'FAIL' } elseif ($selected.Count -eq 0 -or $unplannedSelection.Count -gt 0 -or $credentialConfigMissing -gt 0) { 'MISSING' } else { 'PASS' }
  & $add 'enabled-capability-credential-settings' $credentialConfigState 'certificate/key references are required only for selected planned groups and remain under approved runtime roots' $(if ($credentialConfigState -eq 'PASS') { 'selected group references match approved paths' } elseif ($credentialConfigState -eq 'FAIL') { 'selected group has a mismatched or unbounded reference' } else { 'selected group reference or selection is missing' }) 'enabled-capability-credential-settings-missing'

  $identityValues = [System.Collections.Generic.List[string]]::new()
  $planBindingMissing = $selected.Count -eq 0 -or $unplannedSelection.Count -gt 0
  $planBindingMismatch = $false
  $enabledSettingMissing = 0
  $enabledSettingMismatch = 0
  foreach ($kind in $selected) {
    $capabilityPlan = @($plannedCapabilities | Where-Object { (Get-DeploymentString $_ 'kind') -ceq $kind } | Select-Object -First 1)
    $binding = $script:PlanCapabilityBindings[$kind]
    if ($capabilityPlan.Count -eq 0 -or -not $binding) { $planBindingMismatch = $true; continue }
    $ownerEnvironment = Get-DeploymentServiceEnvironment $Model $binding.service
    $configuredId = Get-DeploymentString $ownerEnvironment $binding.environment
    $plannedId = Get-DeploymentString $capabilityPlan[0] 'clientId'
    if ([string]::IsNullOrWhiteSpace($plannedId) -or [string]::IsNullOrWhiteSpace($configuredId)) { $planBindingMissing = $true; continue }
    if (-not (Test-DeploymentGuid $plannedId) -or -not (Test-DeploymentGuid $configuredId) -or $plannedId -ine $configuredId) { $planBindingMismatch = $true; continue }
    $identityValues.Add($configuredId.ToLowerInvariant())

    $enabledSetting = $script:EnabledSettingByCapability[$kind]
    if ($enabledSetting) {
      $isEnabled = Get-DeploymentString (Get-DeploymentServiceEnvironment $Model $enabledSetting.service) $enabledSetting.environment
      if ([string]::IsNullOrWhiteSpace($isEnabled)) { $enabledSettingMissing++ }
      elseif ($isEnabled -cne 'true') { $enabledSettingMismatch++ }
    }

    if ($kind -eq 'MAIL') {
      $mailEnv = Get-DeploymentServiceEnvironment $Model 'worker-mail'
      $workerId = Get-DeploymentString $mailEnv 'GraphMail__ClientId'
      $provider = Get-DeploymentString $mailEnv 'Mail__Provider'
      if ($workerId -ine $configuredId -or $provider -cne 'GRAPH' -or (Get-DeploymentString $mailEnv 'GraphMail__TenantId') -ine $targetTenantId) { $planBindingMismatch = $true }
    } elseif ($kind -eq 'DOCUMENTS') {
      $pbcEnv = Get-DeploymentServiceEnvironment $Model 'worker-pbc'
      $workerId = Get-DeploymentString $pbcEnv 'SelectedSite__ClientId'
      if ($workerId -ine $configuredId -or (Get-DeploymentString $pbcEnv 'SelectedSite__TenantId') -ine $targetTenantId -or
          (Get-DeploymentString $apiEnv 'SelectedSite__TenantId') -ine $targetTenantId) { $planBindingMismatch = $true }
    } elseif ($kind -eq 'CLIENT_SITES') {
      $sitesEnv = Get-DeploymentServiceEnvironment $Model 'worker-client-sites'
      $tenantId = Get-DeploymentString $apiEnv 'Identity__TenantId'
      if ($tenantId -ine $targetTenantId -or (Get-DeploymentString $sitesEnv 'ClientSites__TenantId') -ine $targetTenantId) { $planBindingMismatch = $true }
    }
  }
  if (@($identityValues | Select-Object -Unique).Count -ne $identityValues.Count) { $planBindingMismatch = $true }
  if ($planBindingMismatch) {
    & $add 'enabled-capability-plan-client-id-bindings' 'FAIL' 'each selected planned client ID matches its configured owner and selected roles stay distinct' 'selected plan and deployment bindings mismatch' 'deployment-capability-plan-mismatch'
  } elseif ($planBindingMissing) {
    & $add 'enabled-capability-plan-client-id-bindings' 'MISSING' 'each selected planned client ID matches its configured owner and selected roles stay distinct' 'operator selection or selected identity is missing' 'deployment-capability-plan-incomplete'
  } else {
    & $add 'enabled-capability-plan-client-id-bindings' 'PASS' 'each selected planned client ID matches its configured owner and selected roles stay distinct' 'selected plan bindings match' $null
  }
  $enabledSettingState = if ($enabledSettingMismatch -gt 0) { 'FAIL' } elseif ($enabledSettingMissing -gt 0 -or $selected.Count -eq 0 -or $unplannedSelection.Count -gt 0) { 'MISSING' } else { 'PASS' }
  & $add 'enabled-capability-flags' $enabledSettingState 'each selected capability enable flag is explicitly true when that setting exists' $(if ($enabledSettingState -eq 'PASS') { 'selected enable flags are true' } elseif ($enabledSettingState -eq 'FAIL') { 'a selected capability is explicitly disabled' } else { 'a selected capability enable flag is missing' }) 'enabled-capability-flag-mismatch'

  $expectedStorage = [ordered]@{
    postgres_data = '/opt/auditsphere/storage/postgres'
    data_protection = '/opt/auditsphere/storage/data-protection'
    pbc_staging = '/opt/auditsphere/storage/pbc-staging'
    release_checkpoints = '/opt/auditsphere/storage/release-checkpoints'
    m365_mail_credentials = '/opt/auditsphere/storage/m365-credentials/outbound-mail'
    m365_selected_site_credentials = '/opt/auditsphere/storage/m365-credentials/selected-site'
  }
  $storageGood = $true
  foreach ($volumeName in $expectedStorage.Keys) {
    $actual = Get-DeploymentVolumeDevice $Model $volumeName
    if ($actual -cne $expectedStorage[$volumeName] -or -not (Test-DeploymentCanonicalAbsolutePath ([string]$actual))) { $storageGood = $false }
  }
  & $add 'approved-storage-roots' $(if ($storageGood) { 'PASS' } else { 'FAIL' }) 'six dedicated Hostinger storage roots match fixed bounded paths' $(if ($storageGood) { 'configured roots match' } else { 'storage root mismatch or missing; unapproved path withheld' }) 'deployment-storage-root-mismatch'
  & $add 'role-specific-outbound-host-policy' 'MISSING' 'explicit role-specific outbound host allowlist' 'not declared by the approved Compose model' 'outbound-host-policy-not-declared'
  return ,@($checks)
}

Export-ModuleMember -Function ConvertFrom-AuditSphereEnabledCapabilityParameter, Get-AuditSphereDeploymentSourceFiles, Get-AuditSphereExactCheckoutEvidence, Read-AuditSphereSanitizedComposeConfig,
  Get-AuditSphereDeploymentArtifactSha256, Get-AuditSphereDeploymentSettings,
  New-AuditSpherePlannedDeploymentEvidence, Get-AuditSphereDeploymentChecks
