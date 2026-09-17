#Requires -Version 5.1
<#
.SYNOPSIS
  Stops the local development cluster (fast shutdown).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'pg-common.ps1')

if (-not (Test-Path (Join-Path $PG_DATA 'PG_VERSION'))) {
  throw "No cluster at '$PG_DATA'."
}
& (Join-Path $PG_BIN 'pg_ctl.exe') -D $PG_DATA -m fast -w stop
if ($LASTEXITCODE -ne 0) { throw 'pg_ctl stop failed.' }
Write-Host 'Server stopped.'