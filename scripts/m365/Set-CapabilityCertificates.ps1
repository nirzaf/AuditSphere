#Requires -Version 7.4
# Public-certificate-only entry point; uses the same ownership, approval and receipt fences.
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory)] [string] $PlanPath,
  [ValidateSet('Plan','ReadOnly','Apply')] [string] $Mode = 'Plan',
  [switch] $DryRun,
  [string] $ApprovalReference,
  [string] $ExpectedPlanSha256,
  [Parameter(Mandatory)] [string] $OutputPath,
  [string] $ApprovalPath,
  [string] $ApprovalSignaturePath,
  [string] $ApprovalSignerSha256,
  [string] $StateDirectory
)
& (Join-Path $PSScriptRoot 'Provision-EntraApps.ps1') @PSBoundParameters -CertificatesOnly
exit $LASTEXITCODE
