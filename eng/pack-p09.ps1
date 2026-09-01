[CmdletBinding()]
param(
    [string]$Version = '0.9.0-alpha.1',
    [string]$OutputPath = 'artifacts/p09-package',
    [switch]$VerifyReproducible
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedVersion = '0.9.0-alpha.1'
$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $repositoryRoot 'src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj'
$testProjectPath = Join-Path $repositoryRoot 'tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj'
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
$workRoot = Join-Path $resolvedOutput ".p09-pack-$([Guid]::NewGuid().ToString('N'))"
$packOneRoot = Join-Path $workRoot 'one'
$packTwoRoot = Join-Path $workRoot 'two'

if ($Version -ne $expectedVersion) {
    throw "p09-package-version: only $expectedVersion is allowed."
}

function Assert-Cp6P09ContainedPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Candidate
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate)
    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if (-not $candidatePath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "p09-package-path: '$candidatePath' is outside '$rootPath'."
    }
}

function Invoke-Cp6P09Native {
    param(
        [Parameter(Mandatory)][string]$CheckId,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = 300
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "${CheckId}: failed to start '$FileName'."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "${CheckId}: command timed out after $TimeoutSeconds seconds."
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "${CheckId}: command failed with exit code $($process.ExitCode). $stderr $stdout"
        }
        [pscustomobject]@{ Stdout = $stdout; Stderr = $stderr }
    }
    finally {
        $process.Dispose()
    }
}

function Get-Cp6P09DotnetCommand {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
        return $env:DOTNET_HOST_PATH
    }
    (Get-Command dotnet -ErrorAction Stop).Source
}

function Get-Cp6P09EntryBytes {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $source = $Entry.Open()
    $target = [System.IO.MemoryStream]::new()
    try {
        $source.CopyTo($target)
        $target.ToArray()
    }
    finally {
        $source.Dispose()
        $target.Dispose()
    }
}

function Get-Cp6P09EntryMap {
    param([Parameter(Mandatory)][string]$PackagePath)

    $map = [System.Collections.Generic.SortedDictionary[string, string]]::new(
        [System.StringComparer]::Ordinal)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        foreach ($entry in $archive.Entries) {
            $bytes = Get-Cp6P09EntryBytes -Entry $entry
            $entryName = $entry.FullName
            if ($entryName -match '^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$') {
                $entryName = 'package/services/metadata/core-properties/_normalized.psmdcp'
            }
            elseif ($entryName -eq '_rels/.rels') {
                $relationships = [System.Text.Encoding]::UTF8.GetString($bytes)
                $relationships = [regex]::Replace(
                    $relationships,
                    '/package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp',
                    '/package/services/metadata/core-properties/_normalized.psmdcp')
                $relationships = [regex]::Replace($relationships, ' Id="[^"]+"', ' Id="normalized"')
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($relationships)
            }
            $map.Add(
                $entryName,
                [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant())
        }
    }
    finally {
        $archive.Dispose()
    }
    $map
}

function Get-Cp6P09ExpectedSourceEntries {
    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($source in @(
        @{ Root = (Join-Path $repositoryRoot 'contracts/p09'); Prefix = 'contracts/p09' },
        @{ Root = (Join-Path $repositoryRoot 'deploy/p09'); Prefix = 'deploy/p09' }
    )) {
        foreach ($file in Get-ChildItem -LiteralPath $source.Root -File -Recurse) {
            $relative = [System.IO.Path]::GetRelativePath($source.Root, $file.FullName).Replace('\', '/')
            $entries.Add("$($source.Prefix)/$relative")
        }
    }
    $entries.ToArray()
}

function Test-Cp6P09Package {
    param([Parameter(Mandatory)][string]$PackagePath)

    $requiredExact = @(
        'lib/net8.0/CP6.Platform.Deployment.dll',
        'lib/net8.0/CP6.Platform.Deployment.xml',
        'README.md',
        '[Content_Types].xml',
        'CP6.Platform.Deployment.nuspec'
    )
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = @($archive.Entries.FullName)
        foreach ($required in $requiredExact) {
            if ($required -notin $names) {
                throw "p09-package-content: required entry '$required' is missing."
            }
        }
        foreach ($expectedSource in Get-Cp6P09ExpectedSourceEntries) {
            if ($expectedSource -notin $names) {
                throw "p09-package-content: source entry '$expectedSource' is missing."
            }
        }
        foreach ($name in $names) {
            $allowed = $name -in $requiredExact -or
                $name.StartsWith('contracts/p09/', [System.StringComparison]::Ordinal) -or
                $name.StartsWith('deploy/p09/', [System.StringComparison]::Ordinal) -or
                $name.StartsWith('_rels/', [System.StringComparison]::Ordinal) -or
                $name.StartsWith('package/', [System.StringComparison]::Ordinal)
            if (-not $allowed) {
                throw "p09-package-content: unexpected entry '$name'."
            }
            if ($name -match '(?i)(?:^|/)(?:\.env(?:\.|$)|kubeconfig(?:\.|/|$)|(?:bin|obj|artifacts|TestResults?)/)') {
                throw "p09-package-residue: forbidden entry '$name'."
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
        $versionElements = @($nuspec.Descendants($namespace + 'version'))
        if ($versionElements.Count -ne 1) {
            throw 'p09-package-version: nuspec version element is not singular.'
        }
        $nuspecVersion = $versionElements[0].Value
        if ($nuspecVersion -ne $expectedVersion) {
            throw "p09-package-version: nuspec version '$nuspecVersion' is not exact."
        }
        if (@($nuspec.Descendants($namespace + 'dependency')).Count -ne 0) {
            throw 'p09-package-dependencies: the Deployment package must have no dependencies.'
        }

        $textExtensions = @('.json', '.yaml', '.yml', '.xml', '.nuspec', '.md', '.ps1', '.py', '.conf', '.properties')
        foreach ($entry in $archive.Entries) {
            if (-not ($textExtensions | Where-Object { $entry.FullName.EndsWith($_, [System.StringComparison]::OrdinalIgnoreCase) })) {
                continue
            }
            $text = [System.Text.Encoding]::UTF8.GetString((Get-Cp6P09EntryBytes -Entry $entry))
            if ($text -match '(?<![A-Za-z0-9])[A-Za-z]:[\\/]' -or
                $text -match '(?:^|[\s"''])/(?:Users|home|var/folders)/') {
                throw "p09-package-machine-path: '$($entry.FullName)' contains a machine path."
            }
            if ($text -match '(?i):latest') {
                throw "p09-package-image: '$($entry.FullName)' contains a mutable image tag."
            }
            foreach ($assignment in [regex]::Matches(
                $text,
                '(?i)"(?:password|token|clientSecret|apiKey)"\s*:\s*"(?<value>[^"]+)"')) {
                if (-not ($entry.FullName.EndsWith('.invalid.json', [System.StringComparison]::Ordinal) -and
                    $assignment.Groups['value'].Value -eq 'obvious-fake-value')) {
                    throw "p09-package-secret: '$($entry.FullName)' contains a secret-like value."
                }
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-Cp6P09Pack {
    param(
        [Parameter(Mandatory)][string]$DotnetCommand,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    [void](Invoke-Cp6P09Native `
        -CheckId 'p09-package-pack' `
        -FileName $DotnetCommand `
        -Arguments @(
            'pack', $projectPath,
            '--configuration', 'Release',
            '--no-build', '--no-restore',
            '--output', $Destination,
            "-p:PackageVersion=$Version",
            '-p:ContinuousIntegrationBuild=true'
        ))
    $packages = @(Get-ChildItem -LiteralPath $Destination -Filter '*.nupkg' -File |
        Where-Object { -not $_.Name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase) })
    if ($packages.Count -ne 1) {
        throw "p09-package-pack: expected one nupkg but found $($packages.Count)."
    }
    $packages[0].FullName
}

$dotnetCommand = Get-Cp6P09DotnetCommand
$previousSourceDateEpoch = $env:SOURCE_DATE_EPOCH
try {
    $gitTimestamp = (Invoke-Cp6P09Native `
        -CheckId 'p09-package-source-date' `
        -FileName 'git' `
        -Arguments @('show', '-s', '--format=%ct', 'HEAD')).Stdout.Trim()
    if ($gitTimestamp -notmatch '^\d+$') {
        throw 'p09-package-source-date: git commit time was not an epoch value.'
    }
    $env:SOURCE_DATE_EPOCH = $gitTimestamp

    [void](Invoke-Cp6P09Native `
        -CheckId 'p09-package-build' `
        -FileName $dotnetCommand `
        -Arguments @('build', 'CP6.Platform.sln', '--configuration', 'Release', '--no-restore') `
        -TimeoutSeconds 600)
    [void](Invoke-Cp6P09Native `
        -CheckId 'p09-package-test' `
        -FileName $dotnetCommand `
        -Arguments @('test', $testProjectPath, '--configuration', 'Release', '--no-build', '--no-restore') `
        -TimeoutSeconds 600)

    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
    Assert-Cp6P09ContainedPath -Root $resolvedOutput -Candidate $workRoot
    $packageOne = Invoke-Cp6P09Pack -DotnetCommand $dotnetCommand -Destination $packOneRoot
    Test-Cp6P09Package -PackagePath $packageOne

    if ($VerifyReproducible) {
        $packageTwo = Invoke-Cp6P09Pack -DotnetCommand $dotnetCommand -Destination $packTwoRoot
        Test-Cp6P09Package -PackagePath $packageTwo
        $mapOne = Get-Cp6P09EntryMap -PackagePath $packageOne
        $mapTwo = Get-Cp6P09EntryMap -PackagePath $packageTwo
        if ($mapOne.Count -ne $mapTwo.Count) {
            throw 'p09-package-reproducible: package entry counts differ.'
        }
        foreach ($entry in $mapOne.GetEnumerator()) {
            if (-not $mapTwo.ContainsKey($entry.Key) -or $mapTwo[$entry.Key] -ne $entry.Value) {
                throw "p09-package-reproducible: entry '$($entry.Key)' differs."
            }
        }
    }

    $finalPackage = Join-Path $resolvedOutput ([System.IO.Path]::GetFileName($packageOne))
    Copy-Item -LiteralPath $packageOne -Destination $finalPackage -Force
    $symbols = Get-ChildItem -LiteralPath $packOneRoot -Filter '*.snupkg' -File
    foreach ($symbolPackage in $symbols) {
        Copy-Item -LiteralPath $symbolPackage.FullName -Destination (Join-Path $resolvedOutput $symbolPackage.Name) -Force
    }
    $packageSha256 = (Get-FileHash -LiteralPath $finalPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]@{
        Status = 'Passed'
        Version = $Version
        Package = [System.IO.Path]::GetRelativePath($repositoryRoot, $finalPackage).Replace('\', '/')
        PackageSha256 = $packageSha256
        Reproducible = [bool]$VerifyReproducible
    }
}
finally {
    $env:SOURCE_DATE_EPOCH = $previousSourceDateEpoch
    if (Test-Path -LiteralPath $workRoot -PathType Container) {
        Assert-Cp6P09ContainedPath -Root $resolvedOutput -Candidate $workRoot
        [System.IO.Directory]::Delete($workRoot, $true)
    }
}
