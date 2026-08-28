[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackageVersion = '0.5.0-alpha.1'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory, $repositoryRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$projects = @(
    'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj',
    'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj',
    'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj',
    'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj'
)

if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Release output must be a child directory of $artifactsRoot."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

Push-Location $repositoryRoot
try {
    foreach ($project in $projects) {
        & dotnet pack $project --configuration Release --no-build --output $resolvedOutput "-p:Version=$PackageVersion"
        if ($LASTEXITCODE -ne 0) {
            throw "Packing $project failed with exit code $LASTEXITCODE."
        }
    }
} finally {
    Pop-Location
}

$packageIds = @('CP6.Platform.Contracts', 'CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Messaging')
$packages = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' } |
    Sort-Object Name)
$expectedNames = @($packageIds | ForEach-Object { "$($_).$PackageVersion.nupkg" } | Sort-Object)

if (($packages.Name | ConvertTo-Json -Compress) -ne ($expectedNames | ConvertTo-Json -Compress)) {
    throw "Release package set is not the approved P05 set: $($packages.Name -join ', ')."
}

foreach ($package in $packages) {
    $packageId = $packageIds | Where-Object { $package.Name.StartsWith("$_.", [StringComparison]::Ordinal) }
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $assemblyPath = "lib/net8.0/$packageId.dll"
        if (-not ($archive.Entries | Where-Object { $_.FullName -eq $assemblyPath -and $_.Length -gt 0 })) {
            throw "$($package.Name) does not contain the expected non-empty $assemblyPath runtime assembly."
        }
    } finally {
        $archive.Dispose()
    }
}

$messagingPackage = $packages | Where-Object { $_.Name -eq "CP6.Platform.Messaging.$PackageVersion.nupkg" }
$requiredContractEntries = @(
    'contracts/contract-bundle.v1.json',
    'contracts/events/platform/contract-example-changed/v1/schema.json',
    'contracts/events/platform/contract-example-changed/v1/examples/valid.json',
    'contracts/events/platform/contract-example-changed/v1/examples/missing-required.json',
    'contracts/events/platform/contract-example-changed/v1/examples/unknown-optional.json',
    'contracts/events/platform/contract-example-changed/v1/examples/wrong-type.json',
    'contracts/events/platform/contract-example-changed/v1/examples/pii-negative.json'
)
$messagingArchive = [IO.Compression.ZipFile]::OpenRead($messagingPackage.FullName)
try {
    $messagingEntries = @($messagingArchive.Entries.FullName)
    foreach ($requiredEntry in $requiredContractEntries) {
        if ($requiredEntry -notin $messagingEntries) {
            throw "$($messagingPackage.Name) does not contain required contract asset $requiredEntry."
        }
    }
} finally {
    $messagingArchive.Dispose()
}

$hashes = @($packages | ForEach-Object {
    [ordered]@{
        file = $_.Name
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput 'sha256.json') -Encoding utf8
Write-Host "Prepared $($packages.Count) immutable P05 packages in $resolvedOutput."
