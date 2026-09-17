#Requires -Version 5.1
<#
.SYNOPSIS
  Starts the local development cluster and ensures the application databases exist.
.DESCRIPTION
  Development only: loopback binding, trust authentication, no production use.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pg-common.ps1')

Assert-PgBinaries | Out-Null
if (-not (Test-Path (Join-Path $PG_DATA 'PG_VERSION'))) {
  throw "No cluster at '$PG_DATA'. Run init-cluster.ps1 first."
}
New-Item -ItemType Directory -Force -Path $PG_RUN | Out-Null

$options = "-p $PG_PORT -c listen_addresses=$PG_HOST -c unix_socket_directories=$($PG_RUN -replace '\\', '/')"
& (Join-Path $PG_BIN 'pg_ctl.exe') -D $PG_DATA status *> $null
if ($LASTEXITCODE -eq 0) {
  Write-Host 'Server already running; skipping start.'
} else {
  & (Join-Path $PG_BIN 'pg_ctl.exe') -D $PG_DATA -l $PG_LOG -o $options -w start
  if ($LASTEXITCODE -ne 0) { Get-Content $PG_LOG -Tail 20; throw 'pg_ctl start failed.' }
}

$existing = Get-PgDatabaseNames
foreach ($db in $PG_DBS) {
  if ($existing -notcontains $db) {
    & (Join-Path $PG_BIN 'createdb.exe') -h $PG_HOST -p $PG_PORT -U $PG_SUPER -E UTF8 --locale='English_United States.1252' -T template0 $db
    if ($LASTEXITCODE -ne 0) { throw "createdb failed for '$db'." }
    Write-Host "Created database '$db'."
  }
}

Write-Host "Server version: $(Get-PgServerVersion) on $PG_HOST`:$PG_PORT"
Write-Host "Databases: $($PG_DBS -join ', ')"