# Shared PostgreSQL settings for local development (Windows, no admin required).
# These defaults match this workstation; override via environment variables elsewhere.
$PG_ROOT   = if ($env:PGSQL_HOME)   { $env:PGSQL_HOME }   else { 'C:\Users\DELL\.pgsql18\pgsql' }
$PG_DATA   = if ($env:PGSQL_DATA)   { $env:PGSQL_DATA }   else { 'C:\Users\DELL\.pgsql18\data\db' }
$PG_RUN    = if ($env:PGSQL_RUN)    { $env:PGSQL_RUN }    else { 'C:\Users\DELL\.pgsql18\run' }
$PG_LOG    = if ($env:PGSQL_LOG)    { $env:PGSQL_LOG }    else { 'C:\Users\DELL\.pgsql18\run\pg.log' }
$PG_PORT   = if ($env:PGSQL_PORT)   { $env:PGSQL_PORT }   else { '5433' }
$PG_HOST   = '127.0.0.1'
$PG_SUPER  = 'postgres'
$PG_DBS    = @('auditsphere', 'auditsphere_tests')
$PG_BIN    = Join-Path $PG_ROOT 'bin'

# Required major version. Development evidence only; production uses the approved baseline.
$PG_REQUIRED_MAJOR = '18'

function Assert-PgBinaries {
  if (-not (Test-Path (Join-Path $PG_BIN 'postgres.exe'))) {
    throw "PostgreSQL binaries not found at '$PG_BIN'. Set PGSQL_HOME or install PostgreSQL $PG_REQUIRED_MAJOR."
  }
  $raw = (& (Join-Path $PG_BIN 'postgres.exe') --version | Select-Object -First 1)
  # Output is e.g. "postgres (PostgreSQL) 18.6"; take the trailing version token.
  $version = (($raw -split '\s+') | Where-Object { $_ })[-1]

  $major = ($version -split '\.')[0]
  Write-Host "PostgreSQL binaries: $version"
  if ($major -ne $PG_REQUIRED_MAJOR) {
    Write-Warning "Expected major version $PG_REQUIRED_MAJOR, found $major. Development-only mismatch; record it rather than hiding it."
  }
  return $version
}

# SQL is passed through a temporary file because PowerShell 5.1 strips embedded double quotes
# from arguments handed to native executables, which silently breaks quoted identifiers.
function Invoke-PsqlQuery {
  param([Parameter(Mandatory)][string]$Database, [Parameter(Mandatory)][string]$Sql)
  $file = [System.IO.Path]::GetTempFileName()
  try {
    Set-Content -LiteralPath $file -Value $Sql -Encoding UTF8
    & (Join-Path $PG_BIN 'psql.exe') -X -w -h $PG_HOST -p $PG_PORT -U $PG_SUPER -d $Database -A -t -f $file
  }
  finally {
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
  }
}

function Get-PgServerVersion {
  (Invoke-PsqlQuery -Database 'postgres' -Sql 'SHOW server_version;' | Select-Object -First 1).Trim()
}

function Get-PgDatabaseNames {
  Invoke-PsqlQuery -Database 'postgres' -Sql "SELECT datname FROM pg_database WHERE NOT datistemplate ORDER BY datname;" |
    Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }
}