param(
  [string]$ProfilePath = "contracts/p09/examples/non-production-runtime-profile.valid.json",
  [string]$ArtifactsRoot = "artifacts/p09-rehearsal",
  [string]$ExpectedGitSha,
  [switch]$KeepFailedArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Import-Module (Join-Path $PSScriptRoot 'p09\P09Rehearsal.psm1') -Force
$result = Invoke-Cp6P09Rehearsal `
    -RepositoryRoot $repositoryRoot `
    -ProfilePath $ProfilePath `
    -ArtifactsRoot $ArtifactsRoot `
    -ExpectedGitSha $ExpectedGitSha `
    -KeepFailedArtifacts:$KeepFailedArtifacts

$result | ConvertTo-Json -Compress -Depth 8 | Write-Output
if ($result.Status -eq 'Passed') { exit 0 }
if ($result.Status -eq 'NotRun') { exit 2 }
exit 1
