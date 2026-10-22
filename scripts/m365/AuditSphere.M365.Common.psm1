#Requires -Version 7.4
# Shared contract for AuditSphere M365 operator scripts (spec appendix M.3/M.4, WU-017).
# Offline planning never authenticates, contacts a tenant, installs modules or generates keys.
Set-StrictMode -Version Latest

$script:ExitCodes = [ordered]@{
  PLANNED = 0; VERIFIED = 0; APPLIED_AND_VERIFIED = 0; DRIFT = 2
  BLOCKED_BOOTSTRAP = 3; BLOCKED_APPROVAL = 3; BLOCKED_EXTERNAL = 3; INVALID_OR_UNSAFE = 4
  PROVIDER_FAILURE = 5; RESULT_UNCERTAIN = 6; PREREQUISITE_MISSING = 7; INCOMPLETE_EVIDENCE = 8
}
$script:Offline = $true
$script:NetworkCalls = 0
$script:SecretName = '(?i)(secret|password|passwd|token|privatekey|private_key|pfx|clientassertion)'

class M365PlanException : System.Exception {
  [string] $Code
  M365PlanException([string] $code) : base($code) { $this.Code = $code }
}

function Get-M365ExitCode([string] $Status) { return $script:ExitCodes[$Status] }

function Set-M365OfflineMode([bool] $Offline) { $script:Offline = $Offline }

function Get-M365NetworkCallCount { return $script:NetworkCalls }

# The only path a script may use to reach a provider. Offline mode refuses before any connection.
function Invoke-M365ProviderRequest([scriptblock] $Request) {
  if ($script:Offline) { throw [M365PlanException]::new('offline-network-call-refused') }
  $script:NetworkCalls++
  return & $Request
}

function Find-M365SecretProperty($Node, [string] $Path = '$') {
  if ($Node -is [System.Collections.IDictionary]) {
    foreach ($key in $Node.Keys) {
      if ($key -match $script:SecretName) { return "$Path.$key" }
      $found = Find-M365SecretProperty $Node[$key] "$Path.$key"
      if ($found) { return $found }
    }
  } elseif ($Node -is [System.Collections.IList]) {
    for ($i = 0; $i -lt $Node.Count; $i++) {
      $found = Find-M365SecretProperty $Node[$i] "$Path[$i]"
      if ($found) { return $found }
    }
  }
  return $null
}

function ConvertTo-M365Canonical($Node) {
  if ($Node -is [System.Collections.IDictionary]) {
    $ordered = [ordered]@{}
    foreach ($key in ($Node.Keys | Sort-Object { $_ } -CaseSensitive)) { $ordered[$key] = ConvertTo-M365Canonical $Node[$key] }
    return $ordered
  }
  if ($Node -is [System.Collections.IList]) { return , @($Node | ForEach-Object { ConvertTo-M365Canonical $_ }) }
  return $Node
}

function Get-M365PlanSha256($Plan) {
  $json = ConvertTo-M365Canonical $Plan | ConvertTo-Json -Depth 32 -Compress
  $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
  return ([System.Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
}

# Parses and validates a plan entirely offline. Throws M365PlanException with a stable code when unsafe.
function Read-M365Plan([string] $Path, [string] $SchemaPath) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw [M365PlanException]::new('plan-not-found') }
  $raw = [System.IO.File]::ReadAllText($Path, [System.Text.UTF8Encoding]::new($false, $true))
  # Keep ISO-8601 values as the exact strings the operator wrote (ConvertFrom-Json would otherwise coerce them to DateTime).
  $parse = @{ AsHashtable = $true; Depth = 32 }
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parse.DateKind = 'String' }
  try { $plan = $raw | ConvertFrom-Json @parse } catch { throw [M365PlanException]::new('plan-not-json') }
  $secret = Find-M365SecretProperty $plan
  if ($secret) { throw [M365PlanException]::new("secret-bearing-property:$secret") }
  $schema = [System.IO.File]::ReadAllText($SchemaPath)
  if (-not (Test-Json -Json $raw -Schema $schema -ErrorAction SilentlyContinue)) { throw [M365PlanException]::new('plan-schema-invalid') }
  $kinds = @($plan.capabilities | ForEach-Object { $_.kind })
  $clients = @($plan.capabilities | ForEach-Object { $_.clientId.ToLowerInvariant() }) + @($plan.operator.clientId.ToLowerInvariant())
  if (@($kinds | Select-Object -Unique).Count -ne $kinds.Count) { throw [M365PlanException]::new('duplicate-capability-kind') }
  if (@($clients | Select-Object -Unique).Count -ne $clients.Count) { throw [M365PlanException]::new('reused-client-identity') }
  if ($plan.ContainsKey('clientSites')) {
    $cutoff = [DateTimeOffset]::MinValue
    $value = $plan.clientSites.rolloutCutoffUtc
    if ($value -is [datetime]) { $value = $value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture) }
    if (-not [DateTimeOffset]::TryParseExact($value, "yyyy-MM-dd'T'HH:mm:ss'Z'",
        [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::AssumeUniversal, [ref] $cutoff)) {
      throw [M365PlanException]::new('invalid-client-site-cutoff')
    }
  }
  return $plan
}

function New-M365Result([string] $Mode) {
  return [ordered]@{
    schemaVersion = 1; repositoryCommit = $null; mode = $Mode; status = $null; tenantId = $null; planSha256 = $null
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); completedAtUtc = $null
    mutationsAttempted = 0; mutationsConfirmed = 0; networkCalls = 0
    changes = @(); checks = @(); blockedPrerequisites = @(); errorCode = $null; redactionsApplied = $true
  }
}

function Add-M365Check($Result, [string] $Id, [string] $State, [string] $Expected, [string] $Observed, [string] $ErrorCode = $null) {
  $Result.checks += [ordered]@{
    id = $Id; capability = $null; resourceId = $null; expected = $Expected; observed = $Observed; state = $State
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); expiresAtUtc = $null; evidenceReference = $null; errorCode = $ErrorCode
  }
}

# Writes the envelope atomically and returns the exit code for its status.
function Complete-M365Result($Result, [string] $Status, [string] $OutputPath) {
  $Result.status = $Status
  $Result.networkCalls = $script:NetworkCalls
  $Result.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
  $directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
  # Local output is always written, including under -WhatIf, so .NET file APIs are used instead of ShouldProcess cmdlets.
  [System.IO.Directory]::CreateDirectory($directory) | Out-Null
  $temporary = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($OutputPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  [System.IO.File]::WriteAllText($temporary, ($Result | ConvertTo-Json -Depth 16), [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::Move($temporary, [System.IO.Path]::GetFullPath($OutputPath), $true)
  return Get-M365ExitCode $Status
}

Export-ModuleMember -Function Get-M365ExitCode, Set-M365OfflineMode, Get-M365NetworkCallCount, Invoke-M365ProviderRequest,
  Find-M365SecretProperty, Get-M365PlanSha256, Read-M365Plan, New-M365Result, Add-M365Check, Complete-M365Result
