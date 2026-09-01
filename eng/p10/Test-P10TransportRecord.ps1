[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TransportPath,

    [Parameter(Mandatory)]
    [string]$EvaluationUtc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$repositoryArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedTransport = [IO.Path]::GetFullPath($TransportPath, $repositoryRoot)
$repositoryArtifactsPrefix = $repositoryArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedTransport.StartsWith($repositoryArtifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Transport input must be below $repositoryArtifactsRoot."
}
if (-not (Test-Path -LiteralPath $resolvedTransport -PathType Leaf)) {
    throw 'Transport input is missing.'
}

$evaluation = [DateTimeOffset]::Parse(
    $EvaluationUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
$canonicalEvaluationUtc = $evaluation.ToUniversalTime().UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture)

$privateRoot = Join-Path $artifactsRoot ("private\transport-verify-" + [Guid]::NewGuid().ToString('N'))
$transportVerifyBuild = Join-Path $privateRoot 'build'
[IO.Directory]::CreateDirectory($privateRoot) | Out-Null
$toolProject = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
$toolPath = Join-Path $transportVerifyBuild 'bin\CP6.Platform.ReleaseTool\release\CP6.Platform.ReleaseTool.dll'
$hadMsbuildDisableNodeReuse = Test-Path Env:MSBUILDDISABLENODEREUSE
$previousMsbuildDisableNodeReuse = $env:MSBUILDDISABLENODEREUSE
$env:MSBUILDDISABLENODEREUSE = '1'
try {
    & dotnet restore $toolProject "-p:ArtifactsPath=$transportVerifyBuild" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Release tool restore failed.' }
    & dotnet build $toolProject --configuration Release --no-restore "-p:ArtifactsPath=$transportVerifyBuild" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Release tool build failed.' }
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw 'Release tool build output is missing.'
    }
    & dotnet $toolPath validate-transport $resolvedTransport $canonicalEvaluationUtc | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Transport contract validation failed.' }
}
finally {
    if ($hadMsbuildDisableNodeReuse) {
        $env:MSBUILDDISABLENODEREUSE = $previousMsbuildDisableNodeReuse
    }
    else {
        Remove-Item Env:MSBUILDDISABLENODEREUSE -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $privateRoot) {
        Remove-Item -LiteralPath $privateRoot -Recurse -Force
    }
}

Write-Host "Verified P10 transport record $resolvedTransport."
