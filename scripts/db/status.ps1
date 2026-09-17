#Requires -Version 5.1
<#
.SYNOPSIS
  Reports the running server version, databases and applied migrations.
.DESCRIPTION
  Read-only. Fails loudly when the server version does not match the required major version.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pg-common.ps1')

$version = Get-PgServerVersion
$major = ($version -split '\.')[0]
Write-Host "server_version : $version"
Write-Host "endpoint       : $PG_HOST`:$PG_PORT"
Write-Host "data directory : $PG_DATA"
if ($major -ne $PG_REQUIRED_MAJOR) {
  Write-Warning "Required major version is $PG_REQUIRED_MAJOR but the server reports $major."
  exit 1
}

Invoke-PsqlQuery -Database 'postgres' -Sql @'
SELECT datname || ' | ' || pg_size_pretty(pg_database_size(datname))
FROM pg_database WHERE NOT datistemplate ORDER BY datname;
'@ | Where-Object { $_ -and $_.Trim() } | ForEach-Object { Write-Host "database       : $_" }

$anyMigrations = $false
foreach ($db in $PG_DBS) {
  Write-Host "[$db] migrations:"
  $hasHistory = (Invoke-PsqlQuery -Database $db -Sql @'
SELECT count(*) FROM information_schema.tables
WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory';
'@ | Where-Object { $_ -and $_.Trim() } | Select-Object -First 1)
  if ($hasHistory -ne '1') {
    Write-Host '  (none applied)'
    continue
  }
  $migrations = Invoke-PsqlQuery -Database $db -Sql 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
  $rows = @($migrations | Where-Object { $_ -and $_.Trim() })
  if ($rows.Count -eq 0) { Write-Host '  (none applied)' } else {
    foreach ($m in $rows) { Write-Host "  $($m.Trim())" }
    $anyMigrations = $true
  }
}

if ($anyMigrations) { Write-Host 'status         : migrations applied' } else { Write-Host 'status         : no migrations applied' }