#Requires -Version 5.1
<#
.SYNOPSIS
  Initialises a local PostgreSQL development cluster at the pinned major version.
.DESCRIPTION
  Development only: binds loopback, trust authentication, no production use.
  Idempotent - an already-initialised data directory is left untouched.
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pg-common.ps1')

$version = Assert-PgBinaries
if ($Force -and (Test-Path (Join-Path $PG_DATA 'PG_VERSION'))) {
  throw "Refusing to reinitialise '$PG_DATA' because -Force was supplied. Remove the directory manually after backing it up."
}
if (Test-Path (Join-Path $PG_DATA 'PG_VERSION')) {
  Write-Host "Cluster already initialised at '$PG_DATA'. Nothing to do."
  exit 0
}

New-Item -ItemType Directory -Force -Path $PG_DATA, $PG_RUN | Out-Null
& (Join-Path $PG_BIN 'initdb.exe') -D $PG_DATA -U $PG_SUPER -A trust -E UTF8 --locale='English_United States.1252'
if ($LASTEXITCODE -ne 0) { throw 'initdb failed.' }

Write-Host "Initialised PostgreSQL $version cluster at '$PG_DATA'."
Write-Host "Start it with: powershell -File '$PSScriptRoot\start.ps1'"