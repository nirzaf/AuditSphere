[CmdletBinding()]
param(
  [string]$HostName = $(if ($env:PGHOST) { $env:PGHOST } else { "127.0.0.1" }),
  [int]$Port = $(if ($env:PGPORT) { [int]$env:PGPORT } else { 5433 }),
  [string]$Database = $(if ($env:PGDATABASE) { $env:PGDATABASE } else { "auditsphere_tests" }),
  [string]$UserName = $(if ($env:PGUSER) { $env:PGUSER } else { "postgres" }),
  [int]$DurationSeconds = 30,
  [int]$SampleSeconds = 5,
  [int]$TargetMaxSessions = 100,
  [double]$TargetMaxTransactionSeconds = 60,
  [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
if ($DurationSeconds -lt 5 -or $SampleSeconds -lt 1 -or $DurationSeconds -lt $SampleSeconds) {
  throw "DurationSeconds must be at least 5 and at least one sample interval."
}
if ($Database -ne "auditsphere_tests" -and $env:ALLOW_AUDITSPHERE_CONNECTION_BASELINE -ne "true") {
  throw "The default baseline is restricted to auditsphere_tests. Set ALLOW_AUDITSPHERE_CONNECTION_BASELINE=true only for an explicitly approved target."
}
$psqlCommand = Get-Command psql -ErrorAction SilentlyContinue
$psqlPath = if ($psqlCommand) {
  $psqlCommand.Source
} elseif ($env:PGSQL_HOME -and (Test-Path (Join-Path $env:PGSQL_HOME "bin\psql.exe"))) {
  Join-Path $env:PGSQL_HOME "bin\psql.exe"
} elseif (Test-Path "C:\Users\DELL\.pgsql18\bin\psql.exe") {
  "C:\Users\DELL\.pgsql18\bin\psql.exe"
} elseif (Test-Path "C:\Users\DELL\.pgsql18\pgsql\bin\psql.exe") {
  "C:\Users\DELL\.pgsql18\pgsql\bin\psql.exe"
} else {
  throw "psql.exe was not found. Put PostgreSQL bin on PATH or set PGSQL_HOME."
}

function Invoke-PgSample {
  $query = @"
SELECT
  count(*) FILTER (WHERE pid <> pg_backend_pid()) AS total_sessions,
  count(*) FILTER (WHERE pid <> pg_backend_pid() AND state = 'active') AS active_sessions,
  count(*) FILTER (WHERE pid <> pg_backend_pid() AND state = 'idle in transaction') AS idle_in_transaction_sessions,
  coalesce(max(extract(epoch FROM (clock_timestamp() - xact_start))) FILTER (WHERE pid <> pg_backend_pid() AND xact_start IS NOT NULL), 0) AS max_transaction_seconds
FROM pg_stat_activity
WHERE datname = current_database();
"@

  $raw = & $psqlPath --no-psqlrc --csv --tuples-only --host=$HostName --port=$Port --username=$UserName --dbname=$Database --command=$query 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "psql connection baseline query failed: $($raw -join ' ')"
  }
  $row = ($raw -join "`n").Trim() | ConvertFrom-Csv -Header total_sessions,active_sessions,idle_in_transaction_sessions,max_transaction_seconds
  [pscustomobject]@{
    ObservedAtUtc = [DateTimeOffset]::UtcNow
    TotalSessions = [int]$row.total_sessions
    ActiveSessions = [int]$row.active_sessions
    IdleInTransactionSessions = [int]$row.idle_in_transaction_sessions
    MaxTransactionSeconds = [double]$row.max_transaction_seconds
  }
}

$samples = [System.Collections.Generic.List[object]]::new()
$started = [DateTimeOffset]::UtcNow
while (([DateTimeOffset]::UtcNow - $started).TotalSeconds -lt $DurationSeconds) {
  $samples.Add((Invoke-PgSample))
  Start-Sleep -Seconds $SampleSeconds
}

$maxSessions = ($samples | Measure-Object -Property TotalSessions -Maximum).Maximum
$maxTransactionSeconds = ($samples | Measure-Object -Property MaxTransactionSeconds -Maximum).Maximum
$decision = if ($samples.Count -lt 3) {
  "INSUFFICIENT EVIDENCE - NO DEPLOYMENT"
} elseif ($maxSessions -le $TargetMaxSessions -and $maxTransactionSeconds -le $TargetMaxTransactionSeconds) {
  "NOT JUSTIFIED - RETAIN NPGSQL POOLING"
} else {
  "DIRECT POOLING NEEDS CAPACITY REVIEW - NO PGBOUNCER DEPLOYMENT"
}

$result = [pscustomobject]@{
  ObservedAtUtc = [DateTimeOffset]::UtcNow
  Target = [pscustomobject]@{ Host = $HostName; Port = $Port; Database = $Database; User = $UserName }
  Sampling = [pscustomobject]@{ DurationSeconds = $DurationSeconds; SampleSeconds = $SampleSeconds; Count = $samples.Count }
  Thresholds = [pscustomobject]@{ MaxSessions = $TargetMaxSessions; MaxTransactionSeconds = $TargetMaxTransactionSeconds }
  Observed = [pscustomobject]@{ MaxSessions = $maxSessions; MaxTransactionSeconds = $maxTransactionSeconds }
  Samples = $samples
  Decision = $decision
  PoolerDeployment = "NOT AUTHORIZED BY THIS BASELINE"
}

$json = $result | ConvertTo-Json -Depth 8
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
  $json
} else {
  $fullPath = [IO.Path]::GetFullPath($OutputPath)
  $parent = Split-Path -Parent $fullPath
  if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
  }
  $json | Set-Content -LiteralPath $fullPath -Encoding utf8
  Write-Output "Wrote connection baseline evidence to $fullPath"
}
