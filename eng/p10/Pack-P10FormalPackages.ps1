[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$SourceGitSha,

    [Parameter(Mandatory)]
    [string]$BuildArtifactsPath,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$SymbolOutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$formalArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-formal'))
$formalPrefix = $formalArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedBuildArtifacts = [IO.Path]::GetFullPath($BuildArtifactsPath, $repositoryRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$resolvedSymbolOutput = [IO.Path]::GetFullPath($SymbolOutputPath, $repositoryRoot)
foreach ($path in @($resolvedOutput, $resolvedSymbolOutput)) {
    if (-not $path.StartsWith($formalPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Formal package output must stay below artifacts/p10-formal.'
    }
}
if ($resolvedOutput -ceq $resolvedSymbolOutput) {
    throw 'Runtime and symbol package outputs must be separate directories.'
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
if ($packages.Count -ne 7 -or @($packages.Keys | Sort-Object -Unique).Count -ne 7) {
    throw 'Formal package set must contain exactly seven unique IDs.'
}
foreach ($project in $packages.Values) {
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($project)
    $assemblyPath = Join-Path $resolvedBuildArtifacts "bin\$assemblyName\release\$assemblyName.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "The one-build output is missing for $project."
    }
}

foreach ($path in @($resolvedOutput, $resolvedSymbolOutput)) {
    if ((Test-Path -LiteralPath $path) -and @(Get-ChildItem -LiteralPath $path -Force).Count -ne 0) {
        throw 'Formal package output directories must be empty.'
    }
    [IO.Directory]::CreateDirectory($path) | Out-Null
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
            -p:ContinuousIntegrationBuild=true `
            "-p:RepositoryCommit=$SourceGitSha" `
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

foreach ($symbol in @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.snupkg' -File)) {
    [IO.File]::Move($symbol.FullName, (Join-Path $resolvedSymbolOutput $symbol.Name))
}
$runtimePackages = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.nupkg' -File | Sort-Object Name)
$symbolPackages = @(Get-ChildItem -LiteralPath $resolvedSymbolOutput -Filter '*.snupkg' -File | Sort-Object Name)
$expectedRuntimeNames = @($packages.Keys | ForEach-Object { "$($_).$PackageVersion.nupkg" } | Sort-Object)
$expectedSymbolNames = @($packages.Keys | ForEach-Object { "$($_).$PackageVersion.snupkg" } | Sort-Object)
if (($runtimePackages.Name | ConvertTo-Json -Compress) -cne ($expectedRuntimeNames | ConvertTo-Json -Compress)) {
    throw "Formal runtime package set is not exact: $($runtimePackages.Name -join ', ')."
}
if (($symbolPackages.Name | ConvertTo-Json -Compress) -cne ($expectedSymbolNames | ConvertTo-Json -Compress)) {
    throw "Formal symbol package set is not exact: $($symbolPackages.Name -join ', ')."
}

[pscustomobject]@{
    PackageIds = @($packages.Keys)
    RuntimePackages = $runtimePackages
    SymbolPackages = $symbolPackages
}
