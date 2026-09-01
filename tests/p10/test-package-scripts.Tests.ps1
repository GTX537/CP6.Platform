$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptPaths = @(
    'eng/p10/New-P10TestCertificate.ps1',
    'eng/p10/Pack-P10TestPackages.ps1',
    'eng/p10/New-P10TestPackageSet.ps1',
    'eng/p10/Test-P10TestPackageSet.ps1',
    'eng/p10/New-P10TransportRecord.ps1'
)

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

foreach ($relativePath in $scriptPaths) {
    $path = Join-Path $repositoryRoot $relativePath
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$relativePath is missing."
    $text = Get-Content -LiteralPath $path -Raw
    Assert-True ($text -cmatch '(?m)^\[CmdletBinding\(\)\]\r?$') "$relativePath must declare CmdletBinding."
    Assert-True ($text -cmatch '(?m)^param\(\r?$') "$relativePath must declare explicit parameters."
    Assert-True ($text -cmatch "Set-StrictMode -Version Latest") "$relativePath must enable strict mode."
    Assert-True ($text -cmatch '\$ErrorActionPreference = ''Stop''') "$relativePath must stop on errors."
    Assert-True ($text -match 'artifacts[\\/]p10-test') "$relativePath must confine output to artifacts/p10-test/."
    Assert-True ($text -notmatch '(?i)nuget\s+push') "$relativePath must not publish packages."
    Assert-True ($text -notmatch '(?i)--skip-duplicate') "$relativePath must not hide duplicate packages."
    Assert-True ($text -notmatch '(?i)(api\.nuget\.org|nuget\.pkg\.github\.com|pkgs\.dev\.azure\.com)') "$relativePath must not name a formal feed."
    Assert-True ($text -notmatch '(?i)(wrangler\s+r2|aws\s+s3|rclone\s+.*r2:)') "$relativePath must not call R2 storage."
    Assert-True ($text -notmatch '(?i)(Set-Content|Out-File|WriteAllText).*password') "$relativePath must not persist a signing password."
}

Write-Host 'P10 test-package script contract tests passed.'
