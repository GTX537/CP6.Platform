param([Parameter(Mandatory=$true)][string]$Stage)
$ErrorActionPreference = 'Stop'
$root = (git -c core.longpaths=true rev-parse --show-toplevel).Trim()
$tracked = @(git -c core.longpaths=true ls-files -- src tests '*.props' '*.targets' '*.sln' 'global.json' 'NuGet.Config' 'nuget.config')
$newFiles = @(Get-ChildItem -LiteralPath 'src/CP6.Platform.AspNetCore/HttpV2','tests/CP6.Platform.AspNetCoreTests/HttpV2' -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/') })
$source = @($tracked + $newFiles | Sort-Object -Unique | ForEach-Object {
    if (Test-Path -LiteralPath $_ -PathType Leaf) { @{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } }
})
$dependencies = @(Get-ChildItem -LiteralPath 'src','tests' -Recurse -File -Filter project.assets.json | ForEach-Object {
    @{ path = [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$artifactPaths = @('src/CP6.Platform.AspNetCore/bin/Release/net8.0','tests/CP6.Platform.AspNetCoreTests/bin/Release/net8.0','tests/CP6.Platform.ArchitectureTests/bin/Release/net8.0' | Where-Object { Test-Path -LiteralPath $_ })
$artifacts = @(Get-ChildItem -LiteralPath $artifactPaths -File |
    Where-Object { $_.Name -like 'CP6*.dll' -or $_.Name -like 'CP6*.pdb' -or $_.Name -like '*.deps.json' } | ForEach-Object {
        @{ path = [System.IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
@{ stage = $Stage; utc = [DateTimeOffset]::UtcNow.ToString('O'); baseline = (git -c core.longpaths=true rev-parse HEAD).Trim();
    source = $source; restoredDependencies = $dependencies; compiledArtifacts = $artifacts;
    executable = 'C:/Users/tt/.dotnet/dotnet.exe'; sdk = (& C:/Users/tt/.dotnet/dotnet.exe --version).Trim() } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "docs/development/evidence/WP02-11/implementation/$Stage-fingerprints.json" -Encoding utf8
