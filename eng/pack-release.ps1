[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$PackageVersion = '0.8.0-alpha.1'
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
    'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj',
    'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj'
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

$packageIds = @('CP6.Platform.Contracts', 'CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Messaging', 'CP6.Platform.EntityFramework')
$packages = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.nupkg' -File | Sort-Object Name)
$symbolPackages = @(Get-ChildItem -LiteralPath $resolvedOutput -Filter '*.snupkg' -File | Sort-Object Name)
$allPackages = @(($packages + $symbolPackages) | Sort-Object Name)
$expectedNames = @($packageIds | ForEach-Object {
    "$($_).$PackageVersion.nupkg"
    "$($_).$PackageVersion.snupkg"
} | Sort-Object)

if (($allPackages.Name | ConvertTo-Json -Compress) -ne ($expectedNames | ConvertTo-Json -Compress)) {
    throw "Release package set is not the approved P08 set: $($allPackages.Name -join ', ')."
}
if ($allPackages.Name -match 'CP6\.Platform\.Testing') {
    throw 'CP6.Platform.Testing is repository-only and must not be packaged.'
}
if ($symbolPackages.Count -ne $packageIds.Count) {
    throw "Release package set must contain exactly $($packageIds.Count) symbol packages."
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

$contractsPackage = $packages | Where-Object { $_.Name -eq "CP6.Platform.Contracts.$PackageVersion.nupkg" }
$requiredSloEntries = @(
    'contracts/observability/slo-evidence/v1/assets.v1.json',
    'contracts/observability/slo-evidence/v1/schema.json',
    'contracts/observability/slo-evidence/v1/examples/non-candidate-indeterminate.json',
    'contracts/observability/slo-evidence/v1/examples/partial-indeterminate.json',
    'contracts/observability/slo-evidence/v1/examples/pii-negative.json',
    'contracts/observability/slo-evidence/v1/examples/valid-pass.json'
)
$contractsArchive = [IO.Compression.ZipFile]::OpenRead($contractsPackage.FullName)
try {
    $contractsEntries = @($contractsArchive.Entries.FullName)
    foreach ($requiredEntry in $requiredSloEntries) {
        if ($requiredEntry -notin $contractsEntries) {
            throw "$($contractsPackage.Name) does not contain required SLO evidence asset $requiredEntry."
        }
    }
} finally {
    $contractsArchive.Dispose()
}

$textExtensions = @('.cs', '.json', '.md', '.nuspec', '.props', '.targets', '.txt', '.xml')
foreach ($package in $allPackages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        foreach ($entry in $archive.Entries) {
            if ($package.Name -ne $contractsPackage.Name -and
                $entry.FullName.StartsWith('contracts/observability/', [StringComparison]::Ordinal)) {
                throw "$($package.Name) contains SLO evidence assets owned only by CP6.Platform.Contracts."
            }
            if ($package.Name -ne $messagingPackage.Name -and
                ($entry.FullName -eq 'contracts/contract-bundle.v1.json' -or
                 $entry.FullName.StartsWith('contracts/events/', [StringComparison]::Ordinal))) {
                throw "$($package.Name) contains P04 event assets owned only by CP6.Platform.Messaging."
            }
            if ($entry.FullName -match '(^|/)(tests?|CP6\.Platform\.Testing)(/|$)' -or
                $entry.FullName -match '\.Tests(?:\.|/)') {
                throw "$($package.Name) contains a test namespace or asset: $($entry.FullName)"
            }
            if ($entry.FullName -match '^[A-Za-z]:[\\/]' -or $entry.FullName -match '^/(home|Users)/') {
                throw "$($package.Name) contains a machine-specific entry path: $($entry.FullName)"
            }
            if ($textExtensions -contains [IO.Path]::GetExtension($entry.FullName)) {
                $stream = $entry.Open()
                $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
                try {
                    $text = $reader.ReadToEnd()
                } finally {
                    $reader.Dispose()
                    $stream.Dispose()
                }
                if ($text -match '(?i)[A-Z]:[\\/]Users[\\/]' -or
                    $text -match '(?i)/(home|Users)/[^/\s]+/' -or
                    $text.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "$($package.Name) contains machine-specific content in $($entry.FullName)."
                }
            }
        }
    } finally {
        $archive.Dispose()
    }
}

$hashes = @($allPackages | ForEach-Object {
    [ordered]@{
        file = $_.Name
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedOutput 'sha256.json') -Encoding utf8
Write-Host "Prepared $($packages.Count) immutable P08 packages and $($symbolPackages.Count) symbol packages in $resolvedOutput."
