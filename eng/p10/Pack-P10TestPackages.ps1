[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0-test\.[0-9a-f]{12}\.[1-9][0-9]*$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$SourceGitSha,

    [Parameter(Mandatory)]
    [string]$BuildArtifactsPath,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$resolvedBuildArtifacts = [IO.Path]::GetFullPath($BuildArtifactsPath, $repositoryRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must be below $artifactsRoot."
}
if (-not $resolvedBuildArtifacts.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Build artifacts must be below $artifactsRoot."
}

$packages = [ordered]@{
    'CP6.Platform.Abstractions' = 'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj'
    'CP6.Platform.AspNetCore' = 'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj'
    'CP6.Platform.Contracts' = 'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj'
    'CP6.Platform.Deployment' = 'src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj'
    'CP6.Platform.EntityFramework' = 'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj'
    'CP6.Platform.Messaging' = 'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj'
    'CP6.Platform.Release' = 'src/CP6.Platform.Release/CP6.Platform.Release.csproj'
}
if (@($packages.Keys | Sort-Object -Unique).Count -ne 7) {
    throw 'P10 package list contains duplicate package IDs.'
}
foreach ($project in $packages.Values) {
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
    $assemblyPath = Join-Path $resolvedBuildArtifacts "bin\$assemblyName\release\$assemblyName.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "The required Release solution build output is missing for $project."
    }
}

if (Test-Path -LiteralPath $resolvedOutput) {
    $existing = @(Get-ChildItem -LiteralPath $resolvedOutput -Force)
    if ($existing.Count -ne 0) {
        throw 'P10 package output directory must be empty.'
    }
}
else {
    [IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
}

Push-Location $repositoryRoot
try {
    foreach ($project in $packages.Values) {
        & dotnet pack $project `
            --configuration Release `
            --no-build `
            --no-restore `
            "-p:ArtifactsPath=$resolvedBuildArtifacts" `
            "-p:PackageVersion=$PackageVersion" `
            "-p:RepositoryCommit=$SourceGitSha" `
            -p:ContinuousIntegrationBuild=true `
            -p:IncludeSymbols=true `
            -p:SymbolPackageFormat=snupkg `
            --output $resolvedOutput | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Packing $project failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Pop-Location
}

$ordinary = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.nupkg' -File |
    Where-Object { -not $_.Name.EndsWith('.snupkg', [StringComparison]::Ordinal) } |
    Sort-Object Name)
$symbols = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.snupkg' -File | Sort-Object Name)
$expectedOrdinary = @($packages.Keys | ForEach-Object { "$($_).$PackageVersion.nupkg" } | Sort-Object)
$expectedSymbols = @($packages.Keys | ForEach-Object { "$($_).$PackageVersion.snupkg" } | Sort-Object)
if (($ordinary.Name | ConvertTo-Json -Compress) -cne ($expectedOrdinary | ConvertTo-Json -Compress)) {
    throw "P10 ordinary package set is not exact: $($ordinary.Name -join ', ')."
}
if (($symbols.Name | ConvertTo-Json -Compress) -cne ($expectedSymbols | ConvertTo-Json -Compress)) {
    throw "P10 symbol package set is not exact: $($symbols.Name -join ', ')."
}

[pscustomobject]@{
    PackageIds = @($packages.Keys)
    OrdinaryPackages = $ordinary
    SymbolPackages = $symbols
}
