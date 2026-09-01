[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Available', 'Published')][string]$Mode,
    [string]$CandidateManifestPath = 'artifacts/p09-publication/candidate-manifest.v1.json',
    [string]$OutputPath = 'artifacts/p09-publication/registry-verification.v1.json',
    [string]$DownloadDirectory = 'artifacts/p09-publication/download',
    [string]$RegistryApiUrl = 'https://api.github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment/versions',
    [string]$RegistrySource = 'https://nuget.pkg.github.com/GTX537/index.json',
    [string]$RegistryUser = 'GTX537'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageId = 'CP6.Platform.Deployment'
$packageVersion = '0.9.0-alpha.1'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$githubToken = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($githubToken)) {
    throw 'p09-registry-auth: GITHUB_TOKEN is required.'
}

function Resolve-Cp6P09RegistryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

function New-Cp6P09RegistryClient {
    param([Parameter(Mandatory)][ValidateSet('Api', 'NuGet')][string]$Authentication)

    $client = [Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('CP6.Platform-P09-Publisher/1.0')
    if ($Authentication -ceq 'Api') {
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $githubToken)
        $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
        $client.DefaultRequestHeaders.Add('X-GitHub-Api-Version', '2022-11-28')
    }
    else {
        $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${RegistryUser}:$githubToken"))
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $pair)
    }
    $client
}

function Get-Cp6P09RegistryVersions {
    param([switch]$AllowMissingPackage)

    $versions = [System.Collections.Generic.List[object]]::new()
    $client = New-Cp6P09RegistryClient -Authentication Api
    try {
        foreach ($page in 1..100) {
            $separator = if ($RegistryApiUrl.Contains('?')) { '&' } else { '?' }
            $uri = "${RegistryApiUrl}${separator}per_page=100&page=$page"
            $response = $client.GetAsync($uri).GetAwaiter().GetResult()
            try {
                if ([int]$response.StatusCode -eq 404 -and $AllowMissingPackage) {
                    return @()
                }
                if (-not $response.IsSuccessStatusCode) {
                    throw "p09-registry-api: package version query failed with HTTP $([int]$response.StatusCode)."
                }
                $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $pageItems = @($text | ConvertFrom-Json -Depth 20)
                foreach ($item in $pageItems) {
                    if ($null -eq $item.id -or [string]::IsNullOrWhiteSpace([string]$item.name)) {
                        throw 'p09-registry-api: package version response is malformed.'
                    }
                    $versions.Add($item)
                }
                if ($pageItems.Count -lt 100) {
                    break
                }
            }
            finally {
                $response.Dispose()
            }
        }
    }
    finally {
        $client.Dispose()
    }
    $versions.ToArray()
}

function Get-Cp6P09PublishedVersion {
    param([switch]$WaitForVisibility)

    $attempts = if ($WaitForVisibility) { 12 } else { 1 }
    foreach ($attempt in 1..$attempts) {
        $versions = @(Get-Cp6P09RegistryVersions -AllowMissingPackage)
        $matching = @($versions | Where-Object { [string]$_.name -ceq $packageVersion })
        if ($matching.Count -eq 1) {
            return $matching[0]
        }
        if ($matching.Count -gt 1) {
            throw 'p09-registry-version: multiple exact Registry versions were returned.'
        }
        if ($attempt -lt $attempts) {
            Start-Sleep -Seconds 5
        }
    }
    $null
}

function Get-Cp6P09RequiredJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$CheckId
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "${CheckId}: required JSON file is missing."
    }
    try {
        Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "${CheckId}: required JSON file is invalid."
    }
}

function Get-Cp6P09ExpectedSourceEntries {
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($source in @(
        @{ Root = (Join-Path $repositoryRoot 'contracts/p09'); Prefix = 'contracts/p09' },
        @{ Root = (Join-Path $repositoryRoot 'deploy/p09'); Prefix = 'deploy/p09' }
    )) {
        foreach ($file in Get-ChildItem -LiteralPath $source.Root -File -Recurse) {
            $relative = [IO.Path]::GetRelativePath($source.Root, $file.FullName).Replace('\', '/')
            $entries.Add("$($source.Prefix)/$relative")
        }
    }
    $entries.ToArray()
}

function Test-Cp6P09DownloadedPackage {
    param([Parameter(Mandatory)][string]$PackagePath)

    $requiredExact = @(
        'lib/net8.0/CP6.Platform.Deployment.dll',
        'lib/net8.0/CP6.Platform.Deployment.xml',
        'README.md',
        '[Content_Types].xml',
        'CP6.Platform.Deployment.nuspec'
    )
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = @($archive.Entries.FullName)
        foreach ($required in $requiredExact) {
            $entry = $archive.GetEntry($required)
            if ($null -eq $entry -or $entry.Length -le 0) {
                throw "p09-registry-content: required non-empty entry '$required' is missing."
            }
        }
        foreach ($expectedSource in Get-Cp6P09ExpectedSourceEntries) {
            if ($expectedSource -notin $names) {
                throw "p09-registry-content: source entry '$expectedSource' is missing."
            }
        }
        foreach ($name in $names) {
            $allowed = $name -in $requiredExact -or
                $name.StartsWith('contracts/p09/', [StringComparison]::Ordinal) -or
                $name.StartsWith('deploy/p09/', [StringComparison]::Ordinal) -or
                $name.StartsWith('_rels/', [StringComparison]::Ordinal) -or
                $name.StartsWith('package/', [StringComparison]::Ordinal)
            if (-not $allowed) {
                throw "p09-registry-content: unexpected entry '$name'."
            }
            if ($name -match '(?i)(?:^|/)(?:\.env(?:\.|$)|kubeconfig(?:\.|/|$)|(?:bin|obj|artifacts|TestResults?)/)') {
                throw "p09-registry-content: forbidden entry '$name'."
            }
        }

        $nuspecEntry = $archive.GetEntry('CP6.Platform.Deployment.nuspec')
        $nuspecStream = $nuspecEntry.Open()
        try {
            $nuspec = [System.Xml.Linq.XDocument]::Load($nuspecStream)
        }
        finally {
            $nuspecStream.Dispose()
        }
        $namespace = $nuspec.Root.Name.Namespace
        $idElements = @($nuspec.Descendants($namespace + 'id'))
        $versionElements = @($nuspec.Descendants($namespace + 'version'))
        if ($idElements.Count -ne 1 -or
            $versionElements.Count -ne 1 -or
            [string]$idElements[0].Value -cne $packageId -or
            [string]$versionElements[0].Value -cne $packageVersion -or
            @($nuspec.Descendants($namespace + 'dependency')).Count -ne 0) {
            throw 'p09-registry-content: nuspec identity or dependency boundary is invalid.'
        }

        $textExtensions = @('.json', '.yaml', '.yml', '.xml', '.nuspec', '.md', '.ps1', '.py', '.conf', '.properties')
        foreach ($entry in $archive.Entries) {
            if (-not ($textExtensions | Where-Object { $entry.FullName.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) })) {
                continue
            }
            $stream = $entry.Open()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
            try {
                $text = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
                $stream.Dispose()
            }
            if ($text -match '(?<![A-Za-z0-9])[A-Za-z]:[\\/]' -or
                $text -match '(?:\A|[\s"''])/(?:Users|home|var/folders)/' -or
                $text -match '(?i):latest') {
                throw "p09-registry-content: '$($entry.FullName)' contains local or mutable delivery data."
            }
            foreach ($assignment in [regex]::Matches(
                $text,
                '(?i)"(?:password|token|clientSecret|apiKey)"\s*:\s*"(?<value>[^"]+)"')) {
                if (-not ($entry.FullName.EndsWith('.invalid.json', [StringComparison]::Ordinal) -and
                    $assignment.Groups['value'].Value -ceq 'obvious-fake-value')) {
                    throw "p09-registry-content: '$($entry.FullName)' contains a secret-like value."
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($Mode -ceq 'Available') {
    $collision = Get-Cp6P09PublishedVersion
    if ($null -ne $collision) {
        throw "p09-registry-version: $packageId $packageVersion already exists."
    }
    Write-Output ([pscustomobject]@{
        status = 'Available'
        packageId = $packageId
        packageVersion = $packageVersion
        registry = $RegistrySource
    })
    return
}

$resolvedCandidate = Resolve-Cp6P09RegistryPath $CandidateManifestPath
$resolvedOutput = Resolve-Cp6P09RegistryPath $OutputPath
$resolvedDownloadDirectory = Resolve-Cp6P09RegistryPath $DownloadDirectory
$candidate = Get-Cp6P09RequiredJson -Path $resolvedCandidate -CheckId 'p09-registry-candidate'
if ([string]$candidate.status -cne 'Candidate' -or
    [string]$candidate.package.id -cne $packageId -or
    [string]$candidate.package.version -cne $packageVersion -or
    [string]$candidate.package.file -cne "$packageId.$packageVersion.nupkg" -or
    [string]$candidate.package.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
    [string]$candidate.registry.source -cne $RegistrySource) {
    throw 'p09-registry-candidate: candidate identity is invalid.'
}

$publishedVersion = Get-Cp6P09PublishedVersion -WaitForVisibility
if ($null -eq $publishedVersion) {
    throw 'p09-registry-version: the exact published version was not visible before the verification deadline.'
}

$nugetClient = New-Cp6P09RegistryClient -Authentication NuGet
try {
    $indexResponse = $nugetClient.GetAsync($RegistrySource).GetAwaiter().GetResult()
    try {
        if (-not $indexResponse.IsSuccessStatusCode) {
            throw "p09-registry-index: source query failed with HTTP $([int]$indexResponse.StatusCode)."
        }
        $indexText = $indexResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $index = $indexText | ConvertFrom-Json -Depth 20
    }
    finally {
        $indexResponse.Dispose()
    }

    $packageBaseResources = @($index.resources | Where-Object {
        [string]$_.'@type' -ceq 'PackageBaseAddress/3.0.0'
    })
    if ($packageBaseResources.Count -ne 1) {
        throw 'p09-registry-index: exact GitHub Packages base address is missing.'
    }
    $packageBase = ([string]$packageBaseResources[0].'@id').TrimEnd('/')
    $sourceUri = [Uri]$RegistrySource
    $packageBaseUri = [Uri]$packageBase
    $isAuthoritativeSource = $RegistrySource -ceq 'https://nuget.pkg.github.com/GTX537/index.json'
    $isLoopbackTestSource = $sourceUri.IsLoopback -and $packageBaseUri.IsLoopback -and
        $sourceUri.Scheme -ceq 'http' -and $packageBaseUri.Scheme -ceq 'http' -and
        $sourceUri.Authority -ceq $packageBaseUri.Authority
    if (($isAuthoritativeSource -and $packageBase -cnotmatch '^https://nuget\.pkg\.github\.com/GTX537/') -or
        (-not $isAuthoritativeSource -and -not $isLoopbackTestSource)) {
        throw 'p09-registry-index: package base address is outside the approved authority.'
    }
    $lowerId = $packageId.ToLowerInvariant()
    $lowerVersion = $packageVersion.ToLowerInvariant()
    $downloadUri = "$packageBase/$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
    $downloadResponse = $nugetClient.GetAsync($downloadUri).GetAwaiter().GetResult()
    try {
        if (-not $downloadResponse.IsSuccessStatusCode) {
            throw "p09-registry-download: package query failed with HTTP $([int]$downloadResponse.StatusCode)."
        }
        $downloadBytes = $downloadResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    }
    finally {
        $downloadResponse.Dispose()
    }
}
finally {
    $nugetClient.Dispose()
}

if ($downloadBytes.Length -le 0) {
    throw 'p09-registry-download: downloaded package is empty.'
}
New-Item -ItemType Directory -Path $resolvedDownloadDirectory -Force | Out-Null
$downloadPath = Join-Path $resolvedDownloadDirectory "$packageId.$packageVersion.nupkg"
[IO.File]::WriteAllBytes($downloadPath, $downloadBytes)
$downloadSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($downloadBytes)).ToLowerInvariant()
if ($downloadSha -cne [string]$candidate.package.sha256) {
    throw 'p09-registry-download: downloaded package SHA-256 differs from the candidate.'
}
Test-Cp6P09DownloadedPackage -PackagePath $downloadPath

$result = [ordered]@{
    schemaVersion = 1
    status = 'Verified'
    packageId = $packageId
    packageVersion = $packageVersion
    packageSha256 = $downloadSha
    packageByteLength = $downloadBytes.Length
    registryVersionId = [string]$publishedVersion.id
    registryVersionName = [string]$publishedVersion.name
    registryCreatedAtUtc = [string]$publishedVersion.created_at
    registryUpdatedAtUtc = [string]$publishedVersion.updated_at
    registry = $RegistrySource
    downloadedFile = [IO.Path]::GetRelativePath($repositoryRoot, $downloadPath).Replace('\', '/')
    packageContent = 'Passed'
}
$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$resultJson = $result | ConvertTo-Json -Depth 8 -Compress
if ($resultJson -match '(?i)(authorization|credential|client[_-]?secret|access[_-]?token|private[_-]?key)') {
    throw 'p09-registry-result: result contains forbidden data.'
}
[IO.File]::WriteAllText($resolvedOutput, $resultJson, [Text.UTF8Encoding]::new($false))
$result
