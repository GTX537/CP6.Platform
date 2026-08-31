Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RequiredChecks = @(
    'profile-valid',
    'provision-first',
    'provision-idempotent',
    'invoke-positive',
    'pubsub-positive',
    'direct-kafka-denied',
    'principal-denied',
    'appid-scope-denied',
    'foreign-topic-denied',
    'kubernetes-render',
    'kubernetes-policy',
    'zero-residue'
)

function Get-Cp6P09PathComparison {
    if ($IsWindows) { return [StringComparison]::OrdinalIgnoreCase }
    return [StringComparison]::Ordinal
}

function Resolve-Cp6P09ContainedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Candidate,
        [switch]$RequireChild
    )

    $separator = [IO.Path]::DirectorySeparatorChar
    $rootPath = [IO.Path]::GetFullPath($Root).Replace([IO.Path]::AltDirectorySeparatorChar, $separator).TrimEnd($separator)
    $candidatePath = [IO.Path]::GetFullPath($Candidate).Replace([IO.Path]::AltDirectorySeparatorChar, $separator).TrimEnd($separator)
    $comparison = Get-Cp6P09PathComparison
    $isRoot = [string]::Equals($rootPath, $candidatePath, $comparison)
    $isChild = $candidatePath.StartsWith($rootPath + $separator, $comparison)
    if ((-not $isRoot -and -not $isChild) -or ($RequireChild -and -not $isChild)) {
        throw 'The candidate path is outside its approved contained root.'
    }

    $current = $rootPath
    if (Test-Path -LiteralPath $current) {
        $rootItem = Get-Item -LiteralPath $current -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'A reparse or symbolic-link root is not allowed.'
        }
    }

    if ($isChild) {
        $relative = [IO.Path]::GetRelativePath($rootPath, $candidatePath)
        foreach ($segment in $relative.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)) {
            $current = Join-Path $current $segment
            if (-not (Test-Path -LiteralPath $current)) { continue }
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'A reparse or symbolic-link path escape is not allowed.'
            }
        }
    }

    return $candidatePath
}

function New-Cp6P09RunLayout {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ArtifactsRoot
    )

    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    $approvedArtifacts = Join-Path $repository 'artifacts'
    $artifactBase = if ([IO.Path]::IsPathRooted($ArtifactsRoot)) {
        [IO.Path]::GetFullPath($ArtifactsRoot)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $repository $ArtifactsRoot))
    }
    $artifactBase = Resolve-Cp6P09ContainedPath -Root $approvedArtifacts -Candidate $artifactBase -RequireChild

    $identity = [Guid]::NewGuid().ToString('N').Substring(0, 16)
    $runId = ([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + $identity)
    $projectName = 'cp6-p09-' + $identity
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $runtimeRoot = Resolve-Cp6P09ContainedPath -Root $tempBase -Candidate (Join-Path $tempBase ('cp6-p09-' + $runId)) -RequireChild
    $artifactDirectory = Resolve-Cp6P09ContainedPath -Root $artifactBase -Candidate (Join-Path $artifactBase $runId) -RequireChild
    $artifactReference = [IO.Path]::GetRelativePath($repository, $artifactDirectory).Replace([IO.Path]::DirectorySeparatorChar, '/')

    [pscustomobject]@{
        RunId = $runId
        ProjectName = $projectName
        RuntimeRoot = $runtimeRoot
        ArtifactsDirectory = $artifactDirectory
        ArtifactReference = $artifactReference
    }
}

function Remove-Cp6P09ExactTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$AllowedRoot
    )

    $resolved = Resolve-Cp6P09ContainedPath -Root $AllowedRoot -Candidate $Path -RequireChild
    if ([IO.Path]::GetFileName($resolved) -notmatch '^cp6-p09-[A-Za-z0-9-]+$') {
        throw 'The destructive target does not have a P09-owned basename.'
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Invoke-Cp6P09Process {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ArgumentList,
        [ValidateRange(1, 900)][int]$TimeoutSeconds = 60,
        [ValidateRange(128, 1048576)][int]$MaximumOutputBytes = 65536,
        [string]$WorkingDirectory,
        [AllowEmptyString()][string]$StandardInput,
        [Collections.IDictionary]$EnvironmentVariables
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    if ([IO.Path]::GetExtension($FilePath) -ieq '.ps1') {
        $startInfo.FileName = (Get-Process -Id $PID).Path
        foreach ($prefix in @('-NoProfile', '-File', [IO.Path]::GetFullPath($FilePath))) {
            $startInfo.ArgumentList.Add($prefix)
        }
    }
    else {
        $startInfo.FileName = $FilePath
    }
    foreach ($argument in $ArgumentList) {
        $startInfo.ArgumentList.Add($argument)
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($WorkingDirectory)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.CreateNoWindow = $true
    if ($null -ne $EnvironmentVariables) {
        foreach ($entry in $EnvironmentVariables.GetEnumerator()) {
            if ($entry.Key -notmatch '^CP6_P09_[A-Z0-9_]+$') {
                throw 'Only bounded CP6_P09_* child-process environment keys are allowed.'
            }
            $startInfo.Environment[[string]$entry.Key] = [string]$entry.Value
        }
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'The bounded child process could not be started.' }
        if ($PSBoundParameters.ContainsKey('StandardInput')) {
            $process.StandardInput.Write($StandardInput)
        }
        $process.StandardInput.Close()
        $stdoutBuilder = [Text.StringBuilder]::new()
        $stderrBuilder = [Text.StringBuilder]::new()
        $stdoutBuffer = [char[]]::new(1024)
        $stderrBuffer = [char[]]::new(1024)
        $stdoutTask = $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
        $stderrTask = $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
        $stdoutComplete = $false
        $stderrComplete = $false
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        while (-not ($stdoutComplete -and $stderrComplete -and $process.HasExited)) {
            if ($stopwatch.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                try { $process.Kill($true) } catch [InvalidOperationException] { }
                $process.WaitForExit(5000) | Out-Null
                throw 'The bounded child process exceeded its timeout.'
            }
            if (-not $stdoutComplete -and $stdoutTask.IsCompleted) {
                $count = $stdoutTask.GetAwaiter().GetResult()
                if ($count -eq 0) { $stdoutComplete = $true }
                else {
                    [void]$stdoutBuilder.Append($stdoutBuffer, 0, $count)
                    $stdoutTask = $process.StandardOutput.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
                }
            }
            if (-not $stderrComplete -and $stderrTask.IsCompleted) {
                $count = $stderrTask.GetAwaiter().GetResult()
                if ($count -eq 0) { $stderrComplete = $true }
                else {
                    [void]$stderrBuilder.Append($stderrBuffer, 0, $count)
                    $stderrTask = $process.StandardError.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
                }
            }
            $outputBytes = [Text.Encoding]::UTF8.GetByteCount($stdoutBuilder.ToString()) + [Text.Encoding]::UTF8.GetByteCount($stderrBuilder.ToString())
            if ($outputBytes -gt $MaximumOutputBytes) {
                try { $process.Kill($true) } catch [InvalidOperationException] { }
                $process.WaitForExit(5000) | Out-Null
                throw 'The bounded child process exceeded its output limit.'
            }
            if (-not ($stdoutComplete -and $stderrComplete -and $process.HasExited)) {
                Start-Sleep -Milliseconds 10
            }
        }
        $stdout = $stdoutBuilder.ToString()
        $stderr = $stderrBuilder.ToString()
        [pscustomobject]@{ ExitCode = $process.ExitCode; StandardOutput = $stdout; StandardError = $stderr }
    }
    catch [System.ComponentModel.Win32Exception] {
        throw
    }
    finally {
        $process.Dispose()
    }
}

function Assert-Cp6P09SafeText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    if ($Text.Length -gt 65536 -or
        $Text -match '(?i)(?:password|token|connectionString)\s*=\s*\S+' -or
        $Text -match '(?i)\bBearer\s+\S{8,}' -or
        $Text -match '(?<![A-Za-z0-9])[A-Za-z]:[\\/]' -or
        $Text -match '\\{2}[^\\/\s]+[\\/][^\\/\s]+' -or
        $Text -match '(?<![:A-Za-z0-9._/])/(?:home|Users|tmp|var|opt|run)/') {
        throw 'Unsafe sensitive text or a machine path was rejected.'
    }
}

function New-Cp6P09CredentialSet {
    [CmdletBinding()]
    param()

    function New-Value {
        $bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }

    [pscustomobject][ordered]@{
        Provisioner = New-Value
        Publisher = New-Value
        Receiver = New-Value
        Unauthorized = New-Value
    }
}

function Test-Cp6P09ForeignTopic {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Topic,
        [string]$DockerCommand
    )

    if ($Topic -cne 'cp6.platform.deployment-probe.v1') {
        throw 'foreign-topic-denied'
    }
    return $true
}

function Assert-Cp6P09ForeignTopicRejected {
    $rejected = $false
    try { Test-Cp6P09ForeignTopic 'cp6.platform.other.v1' | Out-Null }
    catch {
        if ($_.Exception.Message -ceq 'foreign-topic-denied') { $rejected = $true }
        else { throw }
    }
    if (-not $rejected) { throw 'foreign-topic-bypass' }
}

function Test-Cp6P09Profile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ProfilePath,
        [string]$DockerCommand
    )

    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    $profile = Resolve-Cp6P09ContainedPath -Root $repository -Candidate $ProfilePath -RequireChild
    if (-not (Test-Path -LiteralPath $profile -PathType Leaf)) { throw 'The Profile file is missing.' }
    $dotnet = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
        $env:DOTNET_HOST_PATH
    }
    else {
        'dotnet'
    }
    $result = Invoke-Cp6P09Process -FilePath $dotnet -ArgumentList @(
        'run', '--project', 'tests/CP6.Platform.DeploymentTests/P09Validator/P09Validator.csproj',
        '--no-launch-profile', '--', '--profile', $profile
    ) -TimeoutSeconds 120 -MaximumOutputBytes 32768 -WorkingDirectory $repository
    if ($result.ExitCode -ne 0) {
        Assert-Cp6P09SafeText -Text $result.StandardError
        throw 'The P09 Profile failed canonical contract validation.'
    }
    return $result.StandardOutput.Trim()
}

function ConvertTo-Cp6P09CanonicalJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Value)

    function Convert-Node($Node) {
        if ($null -eq $Node) { return $null }
        if ($Node -is [Collections.IDictionary]) {
            $ordered = [ordered]@{}
            foreach ($key in @($Node.Keys | ForEach-Object { [string]$_ } | Sort-Object -CaseSensitive)) {
                $ordered[$key] = Convert-Node $Node[$key]
            }
            return $ordered
        }
        if ($Node -is [Management.Automation.PSCustomObject]) {
            $ordered = [ordered]@{}
            foreach ($property in @($Node.PSObject.Properties | Sort-Object Name -CaseSensitive)) {
                $ordered[$property.Name] = Convert-Node $property.Value
            }
            return $ordered
        }
        if ($Node -is [Collections.IEnumerable] -and $Node -isnot [string]) {
            return @($Node | ForEach-Object { Convert-Node $_ })
        }
        return $Node
    }

    return (Convert-Node $Value | ConvertTo-Json -Compress -Depth 64)
}

function Test-Cp6P09Evidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$EvidencePath
    )

    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    $evidence = Resolve-Cp6P09ContainedPath -Root $repository -Candidate $EvidencePath -RequireChild
    if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) { throw 'The evidence file is missing.' }
    $dotnet = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) { $env:DOTNET_HOST_PATH } else { 'dotnet' }
    $result = Invoke-Cp6P09Process -FilePath $dotnet -ArgumentList @(
        'run', '--project', 'tests/CP6.Platform.DeploymentTests/P09Validator/P09Validator.csproj',
        '--no-launch-profile', '--', '--evidence', $evidence
    ) -TimeoutSeconds 120 -MaximumOutputBytes 32768 -WorkingDirectory $repository
    if ($result.ExitCode -ne 0) {
        Assert-Cp6P09SafeText -Text $result.StandardError
        throw 'The P09 evidence failed canonical contract validation.'
    }
    $response = $result.StandardOutput | ConvertFrom-Json
    if ($response.status -cne 'Valid' -or $response.evidenceSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'The P09 evidence validator returned an invalid bounded response.'
    }
    return [string]$response.evidenceSha256
}

function Assert-Cp6P09ExpectedGitState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ExpectedGitSha
    )

    if ($ExpectedGitSha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Expected Git SHA must be exactly 40 lowercase hexadecimal characters.'
    }
    $head = (Invoke-Cp6P09Process -FilePath 'git' -ArgumentList @('-C', $RepositoryRoot, 'rev-parse', 'HEAD') -TimeoutSeconds 20 -MaximumOutputBytes 4096).StandardOutput.Trim()
    if ($head -cne $ExpectedGitSha) { throw 'Expected Git SHA does not equal HEAD.' }
    $paths = @(
        '.gitignore',
        'contracts/p09',
        'deploy/p09/compose',
        'src/CP6.Platform.Deployment',
        'eng/p09',
        'eng/run-p09-compose-rehearsal.ps1',
        'tests/p09',
        'tests/CP6.Platform.DeploymentTests/P09Validator',
        'tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj',
        'tests/CP6.Platform.DeploymentTests/P09FixtureRuntimeTests.cs',
        'tests/CP6.Platform.P09Fixture'
    )
    $status = Invoke-Cp6P09Process -FilePath 'git' -ArgumentList (@('-C', $RepositoryRoot, 'status', '--porcelain=v1', '--') + $paths) -TimeoutSeconds 20 -MaximumOutputBytes 8192
    if ($status.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($status.StandardOutput)) {
        throw 'Canonical P09 assets are dirty relative to the expected Git SHA.'
    }
}

function Invoke-Cp6P09DockerCommand {
    param(
        [Parameter(Mandatory)][string]$DockerCommand,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [int]$TimeoutSeconds = 60,
        [AllowEmptyString()][string]$StandardInput,
        [Collections.IDictionary]$EnvironmentVariables
    )
    $parameters = @{
        FilePath = $DockerCommand
        ArgumentList = $Arguments
        WorkingDirectory = $WorkingDirectory
        TimeoutSeconds = $TimeoutSeconds
        MaximumOutputBytes = 65536
    }
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $parameters.StandardInput = $StandardInput }
    if ($null -ne $EnvironmentVariables) { $parameters.EnvironmentVariables = $EnvironmentVariables }
    Invoke-Cp6P09Process @parameters
}

function Get-Cp6P09Sha256File {
    param([Parameter(Mandatory)][string]$Path)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path))).ToLowerInvariant()
}

function Get-Cp6P09Sha256Text {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

function Invoke-Cp6P09Compose {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = 120,
        [AllowEmptyString()][string]$StandardInput
    )
    $allArguments = @('compose','--project-name',$Context.ProjectName,'--file',$Context.ComposeFile) + $Arguments
    $parameters = @{
        DockerCommand = $Context.DockerCommand
        Arguments = $allArguments
        WorkingDirectory = $Context.RepositoryRoot
        TimeoutSeconds = $TimeoutSeconds
        EnvironmentVariables = $Context.Environment
    }
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $parameters.StandardInput = $StandardInput }
    return Invoke-Cp6P09DockerCommand @parameters
}

function Test-Cp6P09SupportedComposeVersion {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [AllowEmptyString()]
        [string]$VersionOutput
    )

    if ([string]::IsNullOrWhiteSpace($VersionOutput)) {
        return $false
    }

    $candidate = $VersionOutput.Trim()
    $match = [regex]::Match(
        $candidate,
        '^(?:v)?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-+][0-9A-Za-z.-]+)?$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    if (-not $match.Success) {
        return $false
    }

    try {
        $version = [Version]::Parse((
            '{0}.{1}.{2}' -f
            $match.Groups['major'].Value,
            $match.Groups['minor'].Value,
            $match.Groups['patch'].Value
        ))
    }
    catch {
        return $false
    }

    return $version -ge [Version]'2.36.0'
}

function Assert-Cp6P09CommandSucceeded {
    param([Parameter(Mandatory)]$Result, [Parameter(Mandatory)][string]$CheckId)
    if ($Result.ExitCode -ne 0) {
        if (-not [string]::IsNullOrEmpty($Result.StandardError)) {
            Assert-Cp6P09SafeText -Text $Result.StandardError
        }
        throw $CheckId
    }
}

function Get-Cp6P09StableFailureId {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Candidate, [Parameter(Mandatory)][string]$Fallback)
    $allowed = @(
        'compose-contract','contract-tests','runtime-acl','runtime-mode','runtime-population','runtime-readability',
        'image-pull','kafka-start','kafka-health','provision-first','provision-idempotent','topic-drift','acl-drift','acl-list',
        'runtime-start','publisher-port','publisher-health','invoke-positive','pubsub-positive','direct-kafka-denied',
        'principal-denied','appid-scope-denied','foreign-topic-denied','topic-list','image-digest'
    )
    $allowed += @('topic-create-first','topic-describe-first','acl-list-first','topic-create-replay','acl-list-replay')
    $allowed += @('acl-add-first-batch','acl-add-replay-batch')
    $allowed += @(1..9 | ForEach-Object { 'acl-add-first-{0:d2}' -f $_ })
    $allowed += @(1..9 | ForEach-Object { 'acl-add-replay-{0:d2}' -f $_ })
    if ($allowed -ccontains $Candidate) { return $Candidate }
    return $Fallback
}

function Assert-Cp6P09TraceTopology {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Invocation,
        [Parameter(Mandatory)]$Delivery
    )

    function Get-ExactString($Value, [string]$Name) {
        $property = $Value.PSObject.Properties[$Name]
        if ($null -eq $property -or $property.Value -isnot [string]) { return $null }
        return [string]$property.Value
    }

    $invocationTraceId = Get-ExactString $Invocation 'invocationTraceId'
    $invokerSpanId = Get-ExactString $Invocation 'invokerSpanId'
    $invokedSpanId = Get-ExactString $Invocation 'invokedSpanId'
    $invokedParentSpanId = Get-ExactString $Invocation 'invokedParentSpanId'
    if ($invocationTraceId -cnotmatch '^(?!0{32}$)[0-9a-f]{32}$' -or
        $invokerSpanId -cnotmatch '^(?!0{16}$)[0-9a-f]{16}$' -or
        $invokedSpanId -cnotmatch '^(?!0{16}$)[0-9a-f]{16}$' -or
        $invokedParentSpanId -cne $invokerSpanId -or
        $invokedSpanId -ceq $invokerSpanId) {
        throw 'invoke-positive'
    }

    $traceId = Get-ExactString $Delivery 'traceId'
    $publisherSpanId = Get-ExactString $Delivery 'publisherSpanId'
    $receiverSpanId = Get-ExactString $Delivery 'receiverSpanId'
    $receiverParentSpanId = Get-ExactString $Delivery 'receiverParentSpanId'
    if ($traceId -cnotmatch '^(?!0{32}$)[0-9a-f]{32}$' -or
        $publisherSpanId -cnotmatch '^(?!0{16}$)[0-9a-f]{16}$' -or
        $receiverSpanId -cnotmatch '^(?!0{16}$)[0-9a-f]{16}$' -or
        $receiverParentSpanId -cne $publisherSpanId -or
        $receiverSpanId -ceq $publisherSpanId) {
        throw 'pubsub-positive'
    }
}

function Set-Cp6P09RuntimeDirectorySecurity {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$UnixMode)
    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $result = Invoke-Cp6P09Process -FilePath 'icacls.exe' -ArgumentList @(
            $Path, '/inheritance:r', '/grant:r', "${identity}:(OI)(CI)F", '/Q'
        ) -TimeoutSeconds 30 -MaximumOutputBytes 16384
        Assert-Cp6P09CommandSucceeded $result 'runtime-acl'
    }
    else {
        $result = Invoke-Cp6P09Process -FilePath 'chmod' -ArgumentList @($UnixMode, $Path) -TimeoutSeconds 30 -MaximumOutputBytes 4096
        Assert-Cp6P09CommandSucceeded $result 'runtime-mode'
    }
}

function Initialize-Cp6P09RuntimeDirectories {
    param([Parameter(Mandatory)]$Context)
    [IO.Directory]::CreateDirectory($Context.RuntimeRoot) | Out-Null
    Set-Cp6P09RuntimeDirectorySecurity -Path $Context.RuntimeRoot -UnixMode '0700'
    $relativeDirectories = @(
        'kafka/config','kafka/secrets','kafka/clients',
        'dapr/publisher/components','dapr/publisher/secrets',
        'dapr/receiver/components','dapr/receiver/secrets',
        'dapr/unauthorized/components','dapr/unauthorized/secrets'
    )
    foreach ($relative in $relativeDirectories) {
        $directory = Resolve-Cp6P09ContainedPath -Root $Context.RuntimeRoot -Candidate (Join-Path $Context.RuntimeRoot $relative) -RequireChild
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        Set-Cp6P09RuntimeDirectorySecurity -Path $directory -UnixMode '0733'
    }
}

function Write-Cp6P09TargetOwnedFile {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$RelativeDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9.-]{0,63}$')][string]$FileName,
        [Parameter(Mandatory)][ValidatePattern('^[0-9]{1,6}:[0-9]{1,6}$')][string]$User,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content
    )
    $directory = Resolve-Cp6P09ContainedPath -Root $Context.RuntimeRoot -Candidate (Join-Path $Context.RuntimeRoot $RelativeDirectory) -RequireChild
    $allArguments = Get-Cp6P09OwnedFileDockerArguments -ProjectName $Context.ProjectName -ComposeFile $Context.ComposeFile -Directory $directory -FileName $FileName -User $User
    $result = Invoke-Cp6P09DockerCommand -DockerCommand $Context.DockerCommand -Arguments $allArguments -WorkingDirectory $Context.RepositoryRoot -TimeoutSeconds 120 -EnvironmentVariables $Context.Environment -StandardInput $Content
    Assert-Cp6P09CommandSucceeded $result 'runtime-population'
}

function Get-Cp6P09OwnedFileDockerArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^cp6-p09-[a-f0-9]{16}$')][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile,
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9.-]{0,63}$')][string]$FileName,
        [Parameter(Mandatory)][ValidatePattern('^[0-9]{1,6}:[0-9]{1,6}$')][string]$User
    )
    $compose = [IO.Path]::GetFullPath($ComposeFile)
    $targetDirectory = [IO.Path]::GetFullPath($Directory)
    $mount = "${targetDirectory}:/out"
    $shell = "umask 077; cat > '/out/$FileName'; chmod 0600 '/out/$FileName'"
    return @(
        'compose','--project-name',$ProjectName,'--file',$compose,
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--user',$User,
        '--volume',$mount,'--entrypoint','/bin/sh','kafka-admin','-c',$shell
    )
}

function Get-Cp6P09ReadabilityDockerArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^cp6-p09-[a-f0-9]{16}$')][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile,
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string[]]$FileNames,
        [Parameter(Mandatory)][ValidatePattern('^[0-9]{1,6}:[0-9]{1,6}$')][string]$User
    )
    foreach ($fileName in $FileNames) {
        if ($fileName -cnotmatch '^[a-z][a-z0-9.-]{0,63}$') { throw 'runtime-readability' }
    }
    $compose = [IO.Path]::GetFullPath($ComposeFile)
    $directoryPath = [IO.Path]::GetFullPath($Directory)
    $tests = @($FileNames | ForEach-Object { "test -r '/input/$_'" }) -join ' && '
    return @(
        'compose','--project-name',$ProjectName,'--file',$compose,
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--user',$User,
        '--volume',("${directoryPath}:/input:ro"),'--entrypoint','/bin/sh','kafka-admin','-c',$tests
    )
}

function Assert-Cp6P09TargetReadableFiles {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$RelativeDirectory,
        [Parameter(Mandatory)][string[]]$FileNames,
        [Parameter(Mandatory)][string]$User
    )
    $directory = Resolve-Cp6P09ContainedPath -Root $Context.RuntimeRoot -Candidate (Join-Path $Context.RuntimeRoot $RelativeDirectory) -RequireChild
    $arguments = Get-Cp6P09ReadabilityDockerArguments -ProjectName $Context.ProjectName -ComposeFile $Context.ComposeFile -Directory $directory -FileNames $FileNames -User $User
    $result = Invoke-Cp6P09DockerCommand -DockerCommand $Context.DockerCommand -Arguments $arguments -WorkingDirectory $Context.RepositoryRoot -TimeoutSeconds 120 -EnvironmentVariables $Context.Environment
    Assert-Cp6P09CommandSucceeded $result 'runtime-readability'
}

function New-Cp6P09ClientProperties {
    param(
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][ValidateSet(5000, 30000)][int]$TimeoutMilliseconds
    )
    return @"
security.protocol=SASL_PLAINTEXT
sasl.mechanism=PLAIN
sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="$Username" password="$Password";
request.timeout.ms=$TimeoutMilliseconds
default.api.timeout.ms=$TimeoutMilliseconds
"@.Replace("`r`n", "`n")
}

function Initialize-Cp6P09RuntimeFiles {
    param([Parameter(Mandatory)]$Context, [Parameter(Mandatory)]$Credentials)
    Initialize-Cp6P09RuntimeDirectories $Context
    $templateRoot = Join-Path $Context.RepositoryRoot 'deploy/p09/compose/templates'
    $read = { param($name) [IO.File]::ReadAllText((Join-Path $templateRoot $name), [Text.Encoding]::UTF8).Replace("`r`n", "`n") }
    $server = (& $read 'kafka-server.properties').Replace('@@CP6_P09_PROVISIONER_USERNAME@@','cp6-p09-provisioner')
    $jaas = (& $read 'kafka-jaas.conf').Replace('@@CP6_P09_PROVISIONER_USERNAME@@','cp6-p09-provisioner').Replace('@@CP6_P09_PROVISIONER_PASSWORD@@',$Credentials.Provisioner).Replace('@@CP6_P09_PUBLISHER_USERNAME@@','cp6-p09-probe-publisher').Replace('@@CP6_P09_PUBLISHER_PASSWORD@@',$Credentials.Publisher).Replace('@@CP6_P09_RECEIVER_USERNAME@@','cp6-p09-probe-receiver').Replace('@@CP6_P09_RECEIVER_PASSWORD@@',$Credentials.Receiver).Replace('@@CP6_P09_UNAUTHORIZED_USERNAME@@','cp6-p09-unauthorized-probe').Replace('@@CP6_P09_UNAUTHORIZED_PASSWORD@@',$Credentials.Unauthorized)
    $secretStore = (& $read 'secret-store.yaml').Replace('@@CP6_P09_SECRETS_FILE@@','/run/cp6-p09/secrets/secrets.json')
    $publishComponent = & $read 'kafka-publish.yaml'
    $subscribeComponent = & $read 'kafka-subscribe.yaml'
    $subscription = & $read 'subscription.yaml'
    $publisherSecrets = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ 'publisher-username'='cp6-p09-probe-publisher'; 'publisher-password'=$Credentials.Publisher })
    $receiverSecrets = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ 'receiver-username'='cp6-p09-probe-receiver'; 'receiver-password'=$Credentials.Receiver })
    $unauthorizedSecrets = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ 'publisher-username'='cp6-p09-unauthorized-probe'; 'publisher-password'=$Credentials.Unauthorized })
    $files = @(
        @('kafka/config','server.properties','1000:1000',$server),
        @('kafka/secrets','kafka-jaas.conf','1000:1000',$jaas),
        @('kafka/clients','readiness.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password $Credentials.Provisioner -TimeoutMilliseconds 5000)),
        @('kafka/clients','provisioner.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password $Credentials.Provisioner -TimeoutMilliseconds 30000)),
        @('kafka/clients','publisher.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-probe-publisher' -Password $Credentials.Publisher -TimeoutMilliseconds 30000)),
        @('kafka/clients','receiver.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-probe-receiver' -Password $Credentials.Receiver -TimeoutMilliseconds 30000)),
        @('kafka/clients','unauthorized.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-unauthorized-probe' -Password $Credentials.Unauthorized -TimeoutMilliseconds 30000)),
        @('dapr/publisher/components','secret-store.yaml','65532:65532',$secretStore),
        @('dapr/publisher/components','kafka-publish.yaml','65532:65532',$publishComponent),
        @('dapr/publisher/secrets','secrets.json','65532:65532',$publisherSecrets),
        @('dapr/receiver/components','secret-store.yaml','65532:65532',$secretStore),
        @('dapr/receiver/components','kafka-subscribe.yaml','65532:65532',$subscribeComponent),
        @('dapr/receiver/components','subscription.yaml','65532:65532',$subscription),
        @('dapr/receiver/secrets','secrets.json','65532:65532',$receiverSecrets),
        @('dapr/unauthorized/components','secret-store.yaml','65532:65532',$secretStore),
        @('dapr/unauthorized/components','kafka-publish.yaml','65532:65532',$publishComponent),
        @('dapr/unauthorized/secrets','secrets.json','65532:65532',$unauthorizedSecrets)
    )
    $Context.PopulationFailureId = 'runtime-population'
    foreach ($file in $files) {
        Write-Cp6P09TargetOwnedFile -Context $Context -RelativeDirectory $file[0] -FileName $file[1] -User $file[2] -Content $file[3]
    }
    foreach ($relative in @($files | ForEach-Object { $_[0] } | Select-Object -Unique)) {
        Set-Cp6P09RuntimeDirectorySecurity -Path (Join-Path $Context.RuntimeRoot $relative) -UnixMode '0711'
    }
    $readabilityGroups = [ordered]@{}
    foreach ($file in $files) {
        $key = "$($file[2])|$($file[0])"
        if (-not $readabilityGroups.Contains($key)) {
            $readabilityGroups[$key] = [pscustomobject]@{ User=$file[2]; RelativeDirectory=$file[0]; FileNames=[Collections.Generic.List[string]]::new() }
        }
        $readabilityGroups[$key].FileNames.Add([string]$file[1])
    }
    $Context.PopulationFailureId = 'runtime-readability'
    foreach ($group in $readabilityGroups.Values) {
        Assert-Cp6P09TargetReadableFiles -Context $Context -RelativeDirectory $group.RelativeDirectory -FileNames @($group.FileNames) -User $group.User
    }
}

function Invoke-Cp6P09KafkaTool {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Tool,
        [Parameter(Mandatory)][string[]]$Arguments,
        [AllowEmptyString()][string]$StandardInput,
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 120
    )
    if ($Tool -notmatch '^kafka-(?:topics|configs|acls|console-producer|console-consumer)\.sh$') { throw 'kafka-tool' }
    $command = @('--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint',"/opt/kafka/bin/$Tool",'kafka-admin') + $Arguments
    $parameters = @{ Context=$Context; Arguments=$command; TimeoutSeconds=$TimeoutSeconds }
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $parameters.StandardInput=$StandardInput }
    return Invoke-Cp6P09Compose @parameters
}

function Wait-Cp6P09KafkaDataPlane {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][DateTimeOffset]$Deadline
    )

    do {
        $remainingSeconds = ($Deadline - [DateTimeOffset]::UtcNow).TotalSeconds
        if ($remainingSeconds -le 0) { break }
        $processTimeout = [Math]::Max(1, [Math]::Min(30, [Math]::Ceiling($remainingSeconds)))
        try {
            $probe = Invoke-Cp6P09KafkaTool -Context $Context -Tool 'kafka-topics.sh' -Arguments @(
                '--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/readiness.properties','--list'
            ) -TimeoutSeconds $processTimeout
            if ($probe.ExitCode -eq 0) { return }
        }
        catch { }
        $remainingMilliseconds = ($Deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds ([int][Math]::Min(1000, $remainingMilliseconds))
        }
    } while ([DateTimeOffset]::UtcNow -lt $Deadline)
    throw 'kafka-health'
}

function Get-Cp6P09AclSpecifications {
    return @(
        [pscustomobject]@{ Principal='cp6-p09-probe-publisher'; Operation='Write'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-probe-publisher'; Operation='Describe'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-probe-receiver'; Operation='Read'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-probe-receiver'; Operation='Describe'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-probe-receiver'; Operation='Read'; ResourceFlag='--group'; ResourceName='cp6-p09-probe-receiver-v1' },
        [pscustomobject]@{ Principal='cp6-p09-provisioner'; Operation='Create'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-provisioner'; Operation='Alter'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-provisioner'; Operation='Describe'; ResourceFlag='--topic'; ResourceName='cp6.platform.deployment-probe.v1' },
        [pscustomobject]@{ Principal='cp6-p09-provisioner'; Operation='Describe'; ResourceFlag='--cluster'; ResourceName=$null }
    )
}

function Get-Cp6P09AclBatchDockerArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^cp6-p09-[a-f0-9]{16}$')][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile
    )

    $commands = [Collections.Generic.List[string]]::new()
    $ordinal = 0
    foreach ($acl in @(Get-Cp6P09AclSpecifications)) {
        $ordinal++
        $tokens = @(
            '/opt/kafka/bin/kafka-acls.sh','--bootstrap-server','kafka:9092',
            '--command-config','/etc/kafka/clients/provisioner.properties','--add',
            '--allow-principal',"User:$($acl.Principal)",'--operation',$acl.Operation,$acl.ResourceFlag
        )
        if ($null -ne $acl.ResourceName) { $tokens += $acl.ResourceName }
        foreach ($token in $tokens) {
            if ($token -cnotmatch '^[A-Za-z0-9_./:@-]+$') { throw 'acl-add-first-batch' }
        }
        $quoted = @($tokens | ForEach-Object { "'$_'" }) -join ' '
        $commands.Add("$quoted || exit $(10 + $ordinal)")
    }
    $shell = 'set -eu; ' + ($commands -join '; ')
    return @(
        'compose','--project-name',$ProjectName,'--file',([IO.Path]::GetFullPath($ComposeFile)),
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint','/bin/sh','kafka-admin','-c',$shell
    )
}

function Get-Cp6P09AclBatchFailureId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('first','replay')][string]$Phase,
        [Parameter(Mandatory)][int]$ExitCode
    )
    if ($ExitCode -ge 11 -and $ExitCode -le 19) {
        return 'acl-add-{0}-{1:d2}' -f $Phase, ($ExitCode - 10)
    }
    return "acl-add-$Phase-batch"
}

function Get-Cp6P09KafkaFailureCategory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$StandardOutput,
        [Parameter(Mandatory)][AllowEmptyString()][string]$StandardError
    )

    $text = $StandardOutput + "`n" + $StandardError
    if ($text -match '(?i)(?:authorization(?:exception| failed)|not authorized|_AUTHORIZATION_FAILED)') { return 'authorization' }
    if ($text -match '(?i)(?:TimeoutException|timed out|timeout expired|deadline exceeded)') { return 'timeout' }
    if ($text -match '(?i)(?:UnknownTopicOrPartitionException|LeaderNotAvailable|NotControllerException|CoordinatorNotAvailable)') { return 'metadata' }
    if ($text -match '(?i)(?:DisconnectException|node\s+\d+\s+disconnected|connection to node.+(?:failed|closed))') { return 'disconnected' }
    if ($text -match '(?i)(?:OutOfMemoryError|cannot allocate memory|unable to create native thread)') { return 'resource' }
    return 'unknown'
}

function Invoke-Cp6P09AclBatch {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][ValidateSet('first','replay')][string]$Phase
    )
    $Context.ProvisionFailureId = "acl-add-$Phase-batch"
    $arguments = Get-Cp6P09AclBatchDockerArguments -ProjectName $Context.ProjectName -ComposeFile $Context.ComposeFile
    $result = Invoke-Cp6P09DockerCommand -DockerCommand $Context.DockerCommand -Arguments $arguments -WorkingDirectory $Context.RepositoryRoot -TimeoutSeconds 120 -EnvironmentVariables $Context.Environment
    if ($result.ExitCode -ne 0) {
        $Context.ProvisionFailureCategory = Get-Cp6P09KafkaFailureCategory -StandardOutput $result.StandardOutput -StandardError $result.StandardError
        $failureId = Get-Cp6P09AclBatchFailureId -Phase $Phase -ExitCode $result.ExitCode
        Assert-Cp6P09CommandSucceeded $result $failureId
    }
}

function Get-Cp6P09NormalizedAcls {
    param([Parameter(Mandatory)]$Context, [string]$CheckId = 'acl-list')
    $result = Invoke-Cp6P09KafkaTool -Context $Context -Tool 'kafka-acls.sh' -Arguments @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--list')
    Assert-Cp6P09CommandSucceeded $result $CheckId
    $current = $null
    $tuples = [Collections.Generic.List[string]]::new()
    foreach ($line in $result.StandardOutput.Replace("`r`n","`n").Split("`n")) {
        $resource = [regex]::Match($line,'resourceType=(?<type>TOPIC|GROUP|CLUSTER), name=(?<name>[^,\)]+)')
        if ($resource.Success) { $current = @($resource.Groups['type'].Value,$resource.Groups['name'].Value); continue }
        $acl = [regex]::Match($line,'principal=User:(?<principal>[^,]+), host=\*, operation=(?<operation>[A-Z]+), permissionType=ALLOW')
        if ($acl.Success -and $null -ne $current) {
            $type = switch ($current[0]) { 'TOPIC' {'Topic'} 'GROUP' {'Group'} 'CLUSTER' {'Cluster'} }
            $operation = (Get-Culture).TextInfo.ToTitleCase($acl.Groups['operation'].Value.ToLowerInvariant())
            $tuples.Add("$($acl.Groups['principal'].Value)|$type|$($current[1])|$operation")
        }
    }
    return @($tuples | Sort-Object -CaseSensitive)
}

function Invoke-Cp6P09Provision {
    param([Parameter(Mandatory)]$Context)
    $topic = 'cp6.platform.deployment-probe.v1'
    $topicArgs = @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--create','--if-not-exists','--topic',$topic,'--partitions','3','--replication-factor','1','--config','retention.ms=3600000','--config','max.message.bytes=1048576')
    $Context.ProvisionFailureId = 'topic-create-first'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09KafkaTool $Context 'kafka-topics.sh' $topicArgs) $Context.ProvisionFailureId
    $Context.ProvisionFailureId = 'topic-describe-first'
    $describe = Invoke-Cp6P09KafkaTool $Context 'kafka-topics.sh' @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--describe','--topic',$topic)
    Assert-Cp6P09CommandSucceeded $describe $Context.ProvisionFailureId
    if ($describe.StandardOutput -notmatch 'PartitionCount:\s*3' -or $describe.StandardOutput -notmatch 'retention\.ms=3600000' -or $describe.StandardOutput -notmatch 'max\.message\.bytes=1048576') { throw 'topic-drift' }
    Invoke-Cp6P09AclBatch -Context $Context -Phase first
    $expected = @(
        'cp6-p09-probe-publisher|Topic|cp6.platform.deployment-probe.v1|Describe',
        'cp6-p09-probe-publisher|Topic|cp6.platform.deployment-probe.v1|Write',
        'cp6-p09-probe-receiver|Group|cp6-p09-probe-receiver-v1|Read',
        'cp6-p09-probe-receiver|Topic|cp6.platform.deployment-probe.v1|Describe',
        'cp6-p09-probe-receiver|Topic|cp6.platform.deployment-probe.v1|Read',
        'cp6-p09-provisioner|Cluster|kafka-cluster|Describe',
        'cp6-p09-provisioner|Topic|cp6.platform.deployment-probe.v1|Alter',
        'cp6-p09-provisioner|Topic|cp6.platform.deployment-probe.v1|Create',
        'cp6-p09-provisioner|Topic|cp6.platform.deployment-probe.v1|Describe'
    ) | Sort-Object -CaseSensitive
    $Context.ProvisionFailureId = 'acl-list-first'
    $first = Get-Cp6P09NormalizedAcls $Context $Context.ProvisionFailureId
    if (($expected -join "`n") -cne ($first -join "`n")) { throw 'acl-drift' }
    $Context.ProvisionFailureId = 'topic-create-replay'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09KafkaTool $Context 'kafka-topics.sh' $topicArgs) $Context.ProvisionFailureId
    Invoke-Cp6P09AclBatch -Context $Context -Phase replay
    $Context.ProvisionFailureId = 'acl-list-replay'
    $second = Get-Cp6P09NormalizedAcls $Context $Context.ProvisionFailureId
    if (($first -join "`n") -cne ($second -join "`n")) { throw 'provision-idempotent' }
}

function Invoke-Cp6P09Teardown {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DockerCommand,
        [Parameter(Mandatory)][ValidatePattern('^cp6-p09-[a-f0-9]{16}$')][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile,
        [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
        [Collections.IDictionary]$EnvironmentVariables
    )

    $compose = [IO.Path]::GetFullPath($ComposeFile)
    $label = "com.docker.compose.project=$ProjectName"
    $down = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $RepositoryRoot -EnvironmentVariables $EnvironmentVariables -Arguments @(
        'compose','--project-name',$ProjectName,'--file',$compose,'--profile','negative','--profile','provision','down','--volumes','--remove-orphans','--rmi','local'
    ) -TimeoutSeconds 120
    $cleanupFailure = if ($down.ExitCode -eq 0) { $null } else { 'compose-down' }
    $queries = [ordered]@{
        ContainerCount = @('container','ls','--all','--quiet','--filter',"label=$label")
        NetworkCount = @('network','ls','--quiet','--filter',"label=$label")
        VolumeCount = @('volume','ls','--quiet','--filter',"label=$label")
        ImageCount = @('image','ls','--quiet','--filter',"label=$label")
    }
    $invokeQueries = {
        $counts = [ordered]@{}
        $ids = [ordered]@{}
        foreach ($entry in $queries.GetEnumerator()) {
            $query = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $RepositoryRoot -Arguments $entry.Value -TimeoutSeconds 30
            if ($query.ExitCode -ne 0 -and $null -eq $cleanupFailure) { $cleanupFailure = 'residue-query' }
            $values = @(
                if ($query.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($query.StandardOutput)) {
                    $query.StandardOutput.Replace("`r`n", "`n").Split("`n", [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() }
                }
            )
            $counts[$entry.Key] = $values.Count
            $ids[$entry.Key] = $values
        }
        [pscustomobject]@{ Counts=$counts; Ids=$ids }
    }

    $residue = . $invokeQueries
    $initialTotal = [int]$residue.Counts.ContainerCount + [int]$residue.Counts.NetworkCount + [int]$residue.Counts.VolumeCount + [int]$residue.Counts.ImageCount
    if ($initialTotal -gt 0) {
        $containerIds = @($residue.Ids.ContainerCount)
        if ($containerIds.Count -gt 0) {
            $verifiedIds = [Collections.Generic.List[string]]::new()
            $allIdentitiesVerified = $true
            foreach ($containerId in $containerIds) {
                if ($containerId -cnotmatch '^[0-9a-f]{12,64}$') {
                    $allIdentitiesVerified = $false
                    continue
                }
                $inspect = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $RepositoryRoot -Arguments @(
                    'container','inspect','--format','{{json .Config.Labels}}',$containerId
                ) -TimeoutSeconds 30
                if ($inspect.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($inspect.StandardOutput)) {
                    $allIdentitiesVerified = $false
                    continue
                }
                try {
                    $labels = $inspect.StandardOutput | ConvertFrom-Json
                    $actualProject = [string]$labels.PSObject.Properties['com.docker.compose.project'].Value
                    $actualComposeLabel = [string]$labels.PSObject.Properties['com.docker.compose.project.config_files'].Value
                    $actualCompose = if ([string]::IsNullOrWhiteSpace($actualComposeLabel)) { $null } else { [IO.Path]::GetFullPath($actualComposeLabel) }
                    if ($actualProject -cne $ProjectName -or $null -eq $actualCompose -or -not $actualCompose.Equals($compose, (Get-Cp6P09PathComparison))) {
                        $allIdentitiesVerified = $false
                        continue
                    }
                    $verifiedIds.Add($containerId)
                }
                catch {
                    $allIdentitiesVerified = $false
                }
            }
            if (-not $allIdentitiesVerified -or $verifiedIds.Count -ne $containerIds.Count) {
                if ($null -eq $cleanupFailure) { $cleanupFailure = 'residue-identity' }
            }
            else {
                foreach ($containerId in $verifiedIds) {
                    $remove = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $RepositoryRoot -Arguments @(
                        'container','rm','--force','--volumes',$containerId
                    ) -TimeoutSeconds 30
                    if ($remove.ExitCode -ne 0 -and $null -eq $cleanupFailure) { $cleanupFailure = 'residue-remove' }
                }
            }
        }
        $secondDown = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $RepositoryRoot -EnvironmentVariables $EnvironmentVariables -Arguments @(
            'compose','--project-name',$ProjectName,'--file',$compose,'--profile','negative','--profile','provision','down','--volumes','--remove-orphans','--rmi','local'
        ) -TimeoutSeconds 120
        if ($secondDown.ExitCode -ne 0 -and $null -eq $cleanupFailure) { $cleanupFailure = 'compose-down' }
        $residue = . $invokeQueries
    }
    $counts = $residue.Counts
    [pscustomobject]@{
        CommandExitCode = $down.ExitCode
        ContainerCount = $counts.ContainerCount
        NetworkCount = $counts.NetworkCount
        VolumeCount = $counts.VolumeCount
        ImageCount = $counts.ImageCount
        CleanupFailureId = $cleanupFailure
    }
}

function Invoke-Cp6P09GuardedDockerFailure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$DockerCommand,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile
    )
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ComposeFile) '..\..\..'))
    $runtime = $null
    $originalFailure = $null
    try {
        $runtime = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $repositoryRoot -Arguments @(
            'compose','--project-name',$ProjectName,'--file',$ComposeFile,'up','--detach','kafka'
        )
        if ($runtime.ExitCode -ne 0) { $originalFailure = 'runtime-command' }
    }
    finally {
        $teardown = Invoke-Cp6P09Teardown -DockerCommand $DockerCommand -ProjectName $ProjectName -ComposeFile $ComposeFile -RepositoryRoot $repositoryRoot
    }
    [pscustomobject]@{
        Status = if ($null -eq $originalFailure -and $null -eq $teardown.CleanupFailureId) { 'Passed' } else { 'Failed' }
        OriginalFailureId = $originalFailure
        CleanupFailureId = $teardown.CleanupFailureId
        Teardown = $teardown
    }
}

function Invoke-Cp6P09BoundedRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][DateTimeOffset]$Deadline,
        [Parameter(Mandatory)][ValidateSet('invoke-positive','direct-kafka-denied','appid-scope-denied')][string]$FailureId,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    do {
        if ([DateTimeOffset]::UtcNow -ge $Deadline) { throw $FailureId }
        try {
            $result = & $Action
            if ([DateTimeOffset]::UtcNow -gt $Deadline) { throw $FailureId }
            return $result
        }
        catch {
            if ([DateTimeOffset]::UtcNow -ge $Deadline) { throw $FailureId }
        }
        $remainingMilliseconds = ($Deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds
        if ($remainingMilliseconds -gt 0) {
            Start-Sleep -Milliseconds ([int][Math]::Min(250, $remainingMilliseconds))
        }
    } while ([DateTimeOffset]::UtcNow -lt $Deadline)
    throw $FailureId
}

function Invoke-Cp6P09HttpJson {
    param(
        [Parameter(Mandatory)][Net.Http.HttpClient]$Client,
        [Parameter(Mandatory)][Net.Http.HttpMethod]$Method,
        [Parameter(Mandatory)][Uri]$Uri,
        [AllowEmptyString()][string]$Json,
        [switch]$AllowNotFound
    )
    $request = [Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        if ($PSBoundParameters.ContainsKey('Json')) {
            $request.Content = [Net.Http.StringContent]::new($Json, [Text.Encoding]::UTF8, 'application/json')
        }
        $response = $Client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if ($AllowNotFound -and $response.StatusCode -eq [Net.HttpStatusCode]::NotFound) { return $null }
            if (-not $response.IsSuccessStatusCode) { throw 'http-status' }
            if ($response.Content.Headers.ContentLength -gt 16384) { throw 'http-output-limit' }
            $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
            if ($bytes.Length -gt 16384) { throw 'http-output-limit' }
            return ([Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json)
        }
        finally { $response.Dispose() }
    }
    finally { $request.Dispose() }
}

function Get-Cp6P09PublisherEndpoint {
    param([Parameter(Mandatory)]$Context)
    $result = Invoke-Cp6P09Compose -Context $Context -Arguments @('port','publisher','8080') -TimeoutSeconds 30
    Assert-Cp6P09CommandSucceeded $result 'publisher-port'
    $value = $result.StandardOutput.Trim()
    if ($value -notmatch '^127\.0\.0\.1:(?<port>[0-9]{1,5})$') { throw 'publisher-port' }
    $port = [int]$Matches['port']
    if ($port -lt 1 -or $port -gt 65535) { throw 'publisher-port' }
    return [Uri]"http://127.0.0.1:$port/"
}

function Wait-Cp6P09Publisher {
    param([Parameter(Mandatory)][Net.Http.HttpClient]$Client, [Parameter(Mandatory)][Uri]$BaseUri)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        try {
            $health = Invoke-Cp6P09HttpJson $Client ([Net.Http.HttpMethod]::Get) ([Uri]::new($BaseUri,'healthz'))
            if ($health.profileId -ceq 'cp6-platform-p09-ci-v1' -and $health.profileSha256 -ceq '94addf0349ff895f21eca3e0d660c8d5159198267080df9109ff6493c1063681') { return }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw 'publisher-health'
}

function Get-Cp6P09TopicList {
    param([Parameter(Mandatory)]$Context)
    $result = Invoke-Cp6P09KafkaTool $Context 'kafka-topics.sh' @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--list')
    Assert-Cp6P09CommandSucceeded $result 'topic-list'
    return @($result.StandardOutput.Replace("`r`n","`n").Split("`n",[StringSplitOptions]::RemoveEmptyEntries) | Sort-Object -CaseSensitive)
}

function Assert-Cp6P09ForeignTopicBoundary {
    param([Parameter(Mandatory)]$Context)
    $Context.MatrixFailureId = 'topic-list'
    $beforeTopics = Get-Cp6P09TopicList $Context
    $Context.MatrixFailureId = 'foreign-topic-denied'
    Assert-Cp6P09ForeignTopicRejected
    $Context.MatrixFailureId = 'topic-list'
    $afterTopics = Get-Cp6P09TopicList $Context
    if (($beforeTopics -join "`n") -cne ($afterTopics -join "`n")) { throw 'foreign-topic-denied' }
}

function Invoke-Cp6P09PrincipalNegative {
    param([Parameter(Mandatory)]$Context)
    $topic = 'cp6.platform.deployment-probe.v1'
    $producer = Invoke-Cp6P09KafkaTool -Context $Context -Tool 'kafka-console-producer.sh' -Arguments @('--bootstrap-server','kafka:9092','--producer.config','/etc/kafka/clients/unauthorized.properties','--topic',$topic,'--request-timeout-ms','5000','--max-block-ms','5000') -StandardInput '{"probe":"denied"}'
    $consumer = Invoke-Cp6P09KafkaTool $Context 'kafka-console-consumer.sh' @('--bootstrap-server','kafka:9092','--consumer.config','/etc/kafka/clients/unauthorized.properties','--topic',$topic,'--group','cp6-p09-unauthorized-probe','--timeout-ms','5000','--max-messages','1')
    $producerText = $producer.StandardOutput + $producer.StandardError
    $consumerText = $consumer.StandardOutput + $consumer.StandardError
    if ($producerText -notmatch '(?i)(TopicAuthorization|not authorized)' -or $consumerText -notmatch '(?i)(Authorization|not authorized)') { throw 'principal-denied' }
}

function Get-Cp6P09DaprDiagnosticProcessSpec {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^cp6-p09-[a-f0-9]{16}$')][string]$ProjectName,
        [Parameter(Mandatory)][string]$ComposeFile
    )

    $shell = @'
set -eu; exec 3<>/dev/tcp/publisher-dapr/3500; printf 'POST /v1.0/invoke/cp6-p09-probe-receiver/method/invoked HTTP/1.1\r\nHost: publisher-dapr\r\nContent-Type: application/json\r\nContent-Length: 34\r\nConnection: close\r\n\r\n{"correlationId":"p09-diagnostic"}' >&3; timeout 10 head -c 3072 <&3
'@.Trim()
    [pscustomobject]@{
        Arguments = @(
            'compose','--project-name',$ProjectName,'--file',([IO.Path]::GetFullPath($ComposeFile)),
            '--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint','/bin/bash','kafka-admin','-c',$shell
        )
        TimeoutSeconds = 15
        MaximumOutputBytes = 4096
    }
}

function Get-Cp6P09ReceiverEndpointNetworkClasses {
    param([Parameter(Mandatory)]$Context, [Parameter(Mandatory)][string]$Text)
    $classes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $addresses = @([regex]::Matches($Text,'(?<![0-9])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9])') | ForEach-Object Value | Select-Object -Unique)
    if ($addresses.Count -eq 0) { return @() }
    $list = Invoke-Cp6P09Process -FilePath $Context.DockerCommand -ArgumentList @(
        'container','ls','--quiet',
        '--filter',"label=com.docker.compose.project=$($Context.ProjectName)",
        '--filter','label=com.docker.compose.service=receiver-dapr'
    ) -TimeoutSeconds 10 -MaximumOutputBytes 4096 -WorkingDirectory $Context.RepositoryRoot
    $ids = @($list.StandardOutput.Trim().Split("`n",[StringSplitOptions]::RemoveEmptyEntries))
    if ($list.ExitCode -ne 0 -or $ids.Count -ne 1 -or $ids[0] -cnotmatch '^[a-f0-9]{12,64}$') { return @() }
    $inspect = Invoke-Cp6P09Process -FilePath $Context.DockerCommand -ArgumentList @(
        'container','inspect','--format','{{json .NetworkSettings.Networks}}',$ids[0]
    ) -TimeoutSeconds 10 -MaximumOutputBytes 4096 -WorkingDirectory $Context.RepositoryRoot
    if ($inspect.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($inspect.StandardOutput)) { return @() }
    $networks = $inspect.StandardOutput | ConvertFrom-Json
    foreach ($property in $networks.PSObject.Properties) {
        $address = [string]$property.Value.IPAddress
        if ([string]::IsNullOrWhiteSpace($address) -or $addresses -cnotcontains $address) { continue }
        if ($property.Name -cmatch '_receiver-app$') { [void]$classes.Add('receiver-app') }
        elseif ($property.Name -cmatch '_runtime$') { [void]$classes.Add('runtime') }
    }
    return @($classes | Sort-Object -CaseSensitive)
}

function Invoke-Cp6P09DaprDiagnostic {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context)
    try {
        $spec = Get-Cp6P09DaprDiagnosticProcessSpec -ProjectName $Context.ProjectName -ComposeFile $Context.ComposeFile
        $result = Invoke-Cp6P09Process -FilePath $Context.DockerCommand -ArgumentList $spec.Arguments `
            -TimeoutSeconds $spec.TimeoutSeconds -MaximumOutputBytes $spec.MaximumOutputBytes `
            -WorkingDirectory $Context.RepositoryRoot -EnvironmentVariables $Context.Environment
        $text = $result.StandardOutput + $result.StandardError
        Assert-Cp6P09SafeText -Text $text
        if ([Text.Encoding]::UTF8.GetByteCount($result.StandardOutput) -gt 3072) { return 'diagnostic-unavailable' }
        if ($result.ExitCode -ne 0 -and $result.ExitCode -ne 124) { return 'diagnostic-unavailable' }
        $status = [regex]::Matches($result.StandardOutput,'(?m)^HTTP/1\.1 (?<status>[1-5][0-9]{2})(?: [\x20-\x7e]{0,64})?\r?$')
        if ($status.Count -ne 1) { return 'diagnostic-unavailable' }
        $errorCode = [regex]::Match($result.StandardOutput,'"errorCode"\s*:\s*"(?<code>[A-Z][A-Z0-9_]{2,63})"')
        if (-not $errorCode.Success) { return 'diagnostic-unavailable' }
        if ($errorCode.Groups['code'].Value -ceq 'DAPR_APP_ID_NOT_FOUND') { return 'target-app-id-not-found' }
        if ($errorCode.Groups['code'].Value -cne 'ERR_DIRECT_INVOKE') { return 'diagnostic-unavailable' }
        if ($result.StandardOutput -cmatch '"message"\s*:\s*"failed to invoke, id: cp6-p09-probe-receiver, err: (?:couldn''t find service: cp6-p09-probe-receiver|timeout waiting for address for app id cp6-p09-probe-receiver)"') {
            return 'service-discovery-unavailable'
        }
        $networkClasses = @(Get-Cp6P09ReceiverEndpointNetworkClasses -Context $Context -Text $text)
        if ($networkClasses -ccontains 'receiver-app') { return 'target-receiver-app-network' }
        if ($networkClasses -ccontains 'runtime') { return 'target-runtime-network' }
        if ($text -match '(?i)no address(?:es)? (?:found|available)|address is empty') { return 'target-no-address' }
        if ($text -match '(?i)connection refused|actively refused') { return 'target-refused' }
        if ($text -match '(?i)error while dialing|failed to connect') { return 'target-dial' }
        if ($text -match '(?i)(?:code\s*=\s*Unavailable|\bUnavailable\b)') { return 'target-unavailable' }
        if ($text -match '(?i)timeout|timed out|deadline exceeded') { return 'target-timeout' }
        return 'diagnostic-unavailable'
    }
    catch {
        return 'diagnostic-unavailable'
    }
}

function Invoke-Cp6P09RuntimeMatrix {
    param([Parameter(Mandatory)]$Context)
    $Context.MatrixFailureId = 'runtime-start'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('up','--detach','--build','--wait','--wait-timeout','120','receiver','receiver-dapr') 600) 'runtime-start'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('up','--detach','--build','--wait','--wait-timeout','120','publisher','publisher-dapr') 600) 'runtime-start'
    $Context.MatrixFailureId = 'publisher-port'
    $baseUri = Get-Cp6P09PublisherEndpoint $Context
    $handler = [Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(5)
    try {
        $Context.MatrixFailureId = 'publisher-health'
        Wait-Cp6P09Publisher $client $baseUri
        $Context.MatrixFailureId = 'invoke-positive'
        $matrixDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        try {
            $invocation = Invoke-Cp6P09BoundedRetry -Deadline $matrixDeadline -FailureId 'invoke-positive' -Action {
                Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'invoke-positive')) '{}'
            }
        }
        catch {
            $Context.MatrixDiagnosticCategory = Invoke-Cp6P09DaprDiagnostic -Context $Context
            throw
        }
        if ($invocation.appId -cne 'cp6-p09-probe-receiver' -or $invocation.invocationTraceId -cnotmatch '^[0-9a-f]{32}$') { throw 'invoke-positive' }
        $Context.MatrixFailureId = 'pubsub-positive'
        if ([DateTimeOffset]::UtcNow -ge $matrixDeadline) { throw 'pubsub-positive' }
        $eventId = 'p09-event-' + [Guid]::NewGuid().ToString('N')
        $partitionKey = 'cp6-p09-entity-' + [Guid]::NewGuid().ToString('N')
        $publishJson = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ eventId=$eventId; partitionKey=$partitionKey })
        $receipt = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'publish-positive')) $publishJson
        if ($receipt.eventId -cne $eventId -or $receipt.partitionKey -cne $partitionKey -or $receipt.topic -cne 'cp6.platform.deployment-probe.v1' -or $receipt.region -cne 'TEST') { throw 'pubsub-positive' }
        $received = $null
        do {
            $received = Invoke-Cp6P09HttpJson -Client $client -Method ([Net.Http.HttpMethod]::Get) -Uri ([Uri]::new($baseUri,"received/$eventId")) -AllowNotFound
            if ($null -ne $received) { break }
            Start-Sleep -Milliseconds 500
        } while ([DateTimeOffset]::UtcNow -lt $matrixDeadline)
        if ($null -eq $received -or -not $received.contractValid -or $received.eventId -cne $eventId -or $received.partitionKey -cne $partitionKey -or $received.topicName -cne 'cp6.platform.deployment-probe.v1') { throw 'pubsub-positive' }
        Assert-Cp6P09TraceTopology -Invocation $invocation -Delivery $received

        $Context.MatrixFailureId = 'direct-kafka-denied'
        $Context.Environment['CP6_P09_NEGATIVE_ROLE'] = 'probe'
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('--profile','negative','up','--detach','--build','--force-recreate','direct-probe','unauthorized-dapr') 600) 'direct-kafka-denied'
        $directDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        $direct = Invoke-Cp6P09BoundedRetry -Deadline $directDeadline -FailureId 'direct-kafka-denied' -Action {
            $candidate = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'negative/direct-kafka')) '{}'
            if (-not $candidate.denied -or $candidate.code -cne 'direct-kafka-denied') { throw 'direct-kafka-denied' }
            return $candidate
        }

        $Context.MatrixFailureId = 'appid-scope-denied'
        $Context.Environment['CP6_P09_NEGATIVE_ROLE'] = 'unauthorized'
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('--profile','negative','up','--detach','--build','--force-recreate','direct-probe','unauthorized-dapr') 600) 'appid-scope-denied'
        $appidDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        $appid = Invoke-Cp6P09BoundedRetry -Deadline $appidDeadline -FailureId 'appid-scope-denied' -Action {
            $candidate = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'negative/appid-scope')) '{}'
            if (-not $candidate.denied -or $candidate.code -cne 'appid-scope-denied') { throw 'appid-scope-denied' }
            return $candidate
        }
        $Context.MatrixFailureId = 'principal-denied'
        Invoke-Cp6P09PrincipalNegative $Context
        $Context.MatrixFailureId = 'foreign-topic-denied'
        Assert-Cp6P09ForeignTopicBoundary $Context
        return [pscustomobject]@{ Invocation=$invocation; Received=$received; EventId=$eventId; PartitionKey=$partitionKey }
    }
    finally { $client.Dispose(); $handler.Dispose() }
}

function Get-Cp6P09ImageDigest {
    param([Parameter(Mandatory)]$Context, [Parameter(Mandatory)][string]$Image)
    $result = Invoke-Cp6P09DockerCommand -DockerCommand $Context.DockerCommand -WorkingDirectory $Context.RepositoryRoot -Arguments @('image','inspect',$Image,'--format','{{json .RepoDigests}}') -TimeoutSeconds 30
    Assert-Cp6P09CommandSucceeded $result 'image-digest'
    $digests = @($result.StandardOutput | ConvertFrom-Json)
    $match = @($digests | Where-Object { $_ -match '@(?<digest>sha256:[0-9a-f]{64})$' } | Select-Object -First 1)
    if ($match.Count -ne 1 -or $match[0] -notmatch '@(?<digest>sha256:[0-9a-f]{64})$') { throw 'image-digest' }
    return $Matches['digest']
}

function Add-Cp6P09RunLog {
    param([Parameter(Mandatory)][string]$LogPath, [Parameter(Mandatory)][string]$Step, [Parameter(Mandatory)][string]$Result)
    $line = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ result=$Result; step=$Step })
    Assert-Cp6P09SafeText $line
    [IO.File]::AppendAllText($LogPath,$line+"`n",[Text.UTF8Encoding]::new($false))
}

function Get-Cp6P09EvidenceObject {
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Profile,[Parameter(Mandatory)]$Matrix,[Parameter(Mandatory)]$Digests,[Parameter(Mandatory)]$Teardown,[Parameter(Mandatory)][string]$GitSha,[Parameter(Mandatory)][DateTimeOffset]$Started,[Parameter(Mandatory)][string]$Overall)
    $checks = @($script:RequiredChecks | ForEach-Object { [ordered]@{ id=$_; result=if ($Overall -ceq 'Passed') {'Passed'} else {'Failed'}; summary=$_ } })
    $aclObjects = @($Profile.acls | ForEach-Object { [ordered]@{ operation=$_.operation; principal=$_.principal; resourceName=$_.resourceName; resourceType=$_.resourceType } })
    [ordered]@{
        schemaVersion='1'; profileId='cp6-platform-p09-ci-v1'; profileSha256='94addf0349ff895f21eca3e0d660c8d5159198267080df9109ff6493c1063681'; platformGitSha=$GitSha
        repositoryVersion='0.9.0.0'; packageVersion='0.9.0-alpha.1'; composeManifestSha256=(Get-Cp6P09Sha256File $Context.ComposeFile)
        kubernetesManifestSha256=$Context.KubernetesManifestSha
        runtime=[ordered]@{ daprImage='daprio/daprd:1.18.2'; daprImageDigest=$Digests.Dapr; kafkaImage='apache/kafka:4.3.1'; kafkaImageDigest=$Digests.Kafka; kubectlImage='registry.k8s.io/kubectl:v1.34.1'; kubectlImageDigest=$Digests.Kubectl; kubectlVersion='v1.34.1' }
        topic=[ordered]@{ name='cp6.platform.deployment-probe.v1'; eventType='com.gtx537.platform.contract-example.changed.v1'; partitions=3; retentionMs=3600000; maxMessageBytes=1048576 }
        acls=$aclObjects; checks=$checks
        trace=[ordered]@{ eventId=$Matrix.EventId; eventType='com.gtx537.platform.contract-example.changed.v1'; topic='cp6.platform.deployment-probe.v1'; partitionKey=$Matrix.PartitionKey; traceId=$Matrix.Received.traceId; publisherSpanId=$Matrix.Received.publisherSpanId; receiverSpanId=$Matrix.Received.receiverSpanId; invocationTraceId=$Matrix.Invocation.invocationTraceId; invokerSpanId=$Matrix.Invocation.invokerSpanId; invokedSpanId=$Matrix.Invocation.invokedSpanId }
        startedUtc=$Started.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ'); completedUtc=[DateTimeOffset]::UtcNow.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
        teardown=[ordered]@{ commandExitCode=$Teardown.CommandExitCode; containerCount=$Teardown.ContainerCount; networkCount=$Teardown.NetworkCount; volumeCount=$Teardown.VolumeCount; imageCount=$Teardown.ImageCount; temporaryDirectoryRemoved=$Teardown.TemporaryDirectoryRemoved }
        overall=$Overall
    }
}

function Invoke-Cp6P09Rehearsal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$ProfilePath,
        [Parameter(Mandatory)][string]$ArtifactsRoot,
        [string]$ExpectedGitSha,
        [switch]$KeepFailedArtifacts,
        [string]$DockerCommand = 'docker',
        [switch]$SkipDotnetPreflight
    )

    $repository = [IO.Path]::GetFullPath($RepositoryRoot)
    $layout = New-Cp6P09RunLayout -RepositoryRoot $repository -ArtifactsRoot $ArtifactsRoot
    $resolvedProfile = Resolve-Cp6P09ContainedPath -Root $repository -Candidate $(if ([IO.Path]::IsPathRooted($ProfilePath)) { $ProfilePath } else { Join-Path $repository $ProfilePath }) -RequireChild
    $composeFile = Resolve-Cp6P09ContainedPath -Root $repository -Candidate (Join-Path $repository 'deploy/p09/compose/compose.yaml') -RequireChild
    if (-not (Test-Path -LiteralPath $composeFile -PathType Leaf)) { throw 'compose-contract' }
    if (-not $SkipDotnetPreflight) {
        $dotnet = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) { $env:DOTNET_HOST_PATH } else { 'dotnet' }
        $contracts = Invoke-Cp6P09Process -FilePath $dotnet -ArgumentList @(
            'test','tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj',
            '--filter','FullyQualifiedName!~DockerComposeConfig_WhenAvailable','--no-restore'
        ) -TimeoutSeconds 180 -MaximumOutputBytes 262144 -WorkingDirectory $repository
        Assert-Cp6P09CommandSucceeded $contracts 'contract-tests'
        Test-Cp6P09Profile -RepositoryRoot $repository -ProfilePath $resolvedProfile | Out-Null
    }
    $composeText = [IO.File]::ReadAllText($composeFile,[Text.Encoding]::UTF8)
    foreach ($forbidden in @(':latest','network_mode:','privileged:','/var/run/docker.sock','authType: none')) {
        if ($composeText.Contains($forbidden,[StringComparison]::OrdinalIgnoreCase)) { throw 'compose-contract' }
    }
    Assert-Cp6P09ForeignTopicRejected
    if (-not [string]::IsNullOrWhiteSpace($ExpectedGitSha)) {
        Assert-Cp6P09ExpectedGitState -RepositoryRoot $repository -ExpectedGitSha $ExpectedGitSha
    }
    try {
        $version = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $repository -Arguments @('version','--format','{{.Server.Version}}') -TimeoutSeconds 30
    }
    catch [System.ComponentModel.Win32Exception] {
        return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='docker-unavailable' }
    }
    if ($version.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($version.StandardOutput)) {
        return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='docker-unavailable' }
    }
    try {
        $composeVersion = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $repository -Arguments @('compose','version','--short') -TimeoutSeconds 30
    }
    catch {
        return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='unsupported-compose-version' }
    }
    if ($composeVersion.ExitCode -ne 0 -or -not (Test-Cp6P09SupportedComposeVersion -VersionOutput $composeVersion.StandardOutput)) {
        return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='unsupported-compose-version' }
    }

    $clusterBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $clusterId = [Convert]::ToBase64String($clusterBytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    $context = [pscustomobject]@{
        RepositoryRoot=$repository; ProjectName=$layout.ProjectName; ComposeFile=$composeFile
        RuntimeRoot=$layout.RuntimeRoot; DockerCommand=$DockerCommand
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=$layout.RuntimeRoot; CP6_P09_CLUSTER_ID=$clusterId; CP6_P09_NEGATIVE_ROLE='probe' }
        KubernetesManifestSha=$null
        MatrixFailureId='runtime-start'
        MatrixDiagnosticCategory=$null
        ProvisionFailureId='topic-create-first'
        ProvisionFailureCategory=$null
        PopulationFailureId='runtime-population'
    }
    $config = Invoke-Cp6P09Compose $context @('--profile','negative','--profile','provision','config','--quiet') 30
    Assert-Cp6P09CommandSucceeded $config 'compose-contract'
    $profile = [IO.File]::ReadAllText($resolvedProfile,[Text.Encoding]::UTF8) | ConvertFrom-Json
    $kubernetesGate = Invoke-Cp6P09Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', 'eng/test-p09-kubernetes.ps1'
    ) -TimeoutSeconds 300 -MaximumOutputBytes 32768 -WorkingDirectory $repository
    Assert-Cp6P09CommandSucceeded $kubernetesGate 'kubernetes-policy'
    try {
        $kubernetesResult = $kubernetesGate.StandardOutput | ConvertFrom-Json
    }
    catch {
        throw 'kubernetes-policy'
    }
    if ($kubernetesResult.Status -cne 'Passed' -or
        $kubernetesResult.ManifestSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'kubernetes-policy'
    }
    $context.KubernetesManifestSha = [string]$kubernetesResult.ManifestSha256

    [IO.Directory]::CreateDirectory($layout.ArtifactsDirectory) | Out-Null
    $logPath = Join-Path $layout.ArtifactsDirectory 'run-log.v1.jsonl'
    $started = [DateTimeOffset]::UtcNow
    $credentials = $null
    $matrix = $null
    $digests = $null
    $originalFailure = $null
    $cleanupFailure = $null
    $teardown = $null
    $runtimeRemoved = $false
    $stage = 'runtime-population'
    try {
        Add-Cp6P09RunLog $logPath 'preflight' 'Passed'
        Add-Cp6P09RunLog $logPath 'kubernetes-policy' 'Passed'
        $pull = Invoke-Cp6P09Compose $context @('pull','kafka','kafka-admin','publisher-dapr','receiver-dapr','unauthorized-dapr') 600
        Assert-Cp6P09CommandSucceeded $pull 'image-pull'
        $credentials = New-Cp6P09CredentialSet
        Initialize-Cp6P09RuntimeFiles $context $credentials
        Add-Cp6P09RunLog $logPath 'runtime-population' 'Passed'
        $stage = 'kafka-start'
        $kafkaDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
        $startKafka = Invoke-Cp6P09Compose $context @('up','--detach','--no-build','--wait','--wait-timeout','120','kafka') 180
        Assert-Cp6P09CommandSucceeded $startKafka 'kafka-start'
        $stage = 'kafka-health'
        Wait-Cp6P09KafkaDataPlane -Context $context -Deadline $kafkaDeadline
        Add-Cp6P09RunLog $logPath 'kafka-start' 'Passed'
        $stage = 'provision-first'
        Invoke-Cp6P09Provision $context
        Add-Cp6P09RunLog $logPath 'provision' 'Passed'
        $stage = 'runtime-matrix'
        $matrix = Invoke-Cp6P09RuntimeMatrix $context
        Add-Cp6P09RunLog $logPath 'runtime-matrix' 'Passed'
        $stage = 'image-digest'
        $digests = [pscustomobject]@{
            Dapr=(Get-Cp6P09ImageDigest $context 'daprio/daprd:1.18.2')
            Kafka=(Get-Cp6P09ImageDigest $context 'apache/kafka:4.3.1')
            Kubectl=(Get-Cp6P09ImageDigest $context 'registry.k8s.io/kubectl:v1.34.1')
        }
        Add-Cp6P09RunLog $logPath 'image-digest' 'Passed'
    }
    catch {
        $fallback = if ($stage -ceq 'runtime-matrix') {
            [string]$context.MatrixFailureId
        }
        elseif ($stage -ceq 'provision-first') {
            [string]$context.ProvisionFailureId
        }
        elseif ($stage -ceq 'runtime-population') {
            [string]$context.PopulationFailureId
        }
        else {
            $stage
        }
        $originalFailure = Get-Cp6P09StableFailureId -Candidate $_.Exception.Message -Fallback $fallback
        Add-Cp6P09RunLog $logPath $originalFailure 'Failed'
    }
    finally {
        try {
            $teardown = Invoke-Cp6P09Teardown -DockerCommand $DockerCommand -ProjectName $layout.ProjectName -ComposeFile $composeFile -RepositoryRoot $repository -EnvironmentVariables $context.Environment
            $cleanupFailure = $teardown.CleanupFailureId
        }
        catch { $cleanupFailure = 'cleanup-exception' }
        try {
            Remove-Cp6P09ExactTree -Path $layout.RuntimeRoot -AllowedRoot ([IO.Path]::GetTempPath())
            $runtimeRemoved = -not (Test-Path -LiteralPath $layout.RuntimeRoot)
        }
        catch { $runtimeRemoved=$false; if ($null -eq $cleanupFailure) { $cleanupFailure='runtime-root' } }
    }
    if ($null -eq $teardown) {
        $teardown = [pscustomobject]@{ CommandExitCode=-1; ContainerCount=0; NetworkCount=0; VolumeCount=0; ImageCount=0 }
    }
    $teardown | Add-Member -NotePropertyName TemporaryDirectoryRemoved -NotePropertyValue $runtimeRemoved -Force
    $zeroResidue = $teardown.CommandExitCode -eq 0 -and $teardown.ContainerCount -eq 0 -and $teardown.NetworkCount -eq 0 -and $teardown.VolumeCount -eq 0 -and $teardown.ImageCount -eq 0 -and $runtimeRemoved
    if (-not $zeroResidue -and $null -eq $cleanupFailure) { $cleanupFailure='zero-residue' }
    Add-Cp6P09RunLog $logPath 'zero-residue' $(if ($zeroResidue) {'Passed'} else {'Failed'})

    if ($null -ne $credentials) {
        foreach ($artifact in Get-ChildItem -LiteralPath $layout.ArtifactsDirectory -File -Recurse) {
            $text = [IO.File]::ReadAllText($artifact.FullName,[Text.Encoding]::UTF8)
            Assert-Cp6P09SafeText $text
            foreach ($value in @($credentials.PSObject.Properties.Value)) {
                if ($text.Contains([string]$value,[StringComparison]::Ordinal)) { $cleanupFailure='artifact-secret' }
            }
        }
    }
    $gitSha = if ([string]::IsNullOrWhiteSpace($ExpectedGitSha)) { (Invoke-Cp6P09Process -FilePath 'git' -ArgumentList @('-C',$repository,'rev-parse','HEAD') -TimeoutSeconds 20 -MaximumOutputBytes 4096).StandardOutput.Trim() } else { $ExpectedGitSha }
    if ($null -eq $originalFailure -and $null -eq $cleanupFailure -and $null -ne $matrix) {
        $evidence = Get-Cp6P09EvidenceObject -Context $context -Profile $profile -Matrix $matrix -Digests $digests -Teardown $teardown -GitSha $gitSha -Started $started -Overall 'Passed'
        $evidencePath = Join-Path $layout.ArtifactsDirectory 'rehearsal-evidence.v1.json'
        [IO.File]::WriteAllText($evidencePath,(ConvertTo-Cp6P09CanonicalJson $evidence),[Text.UTF8Encoding]::new($false))
        $evidenceSha = Test-Cp6P09Evidence -RepositoryRoot $repository -EvidencePath $evidencePath
        return [pscustomobject]@{
            Status='Passed'
            RunId=$layout.RunId
            ArtifactsDirectory=$layout.ArtifactReference
            EvidenceSha256=$evidenceSha
            KubernetesManifestSha256=$context.KubernetesManifestSha
            ZeroResidue=$true
        }
    }
    return [pscustomobject]@{ Status='Failed'; RunId=$layout.RunId; Reason=$(if ($null -ne $cleanupFailure) {$cleanupFailure} else {$originalFailure}); ArtifactsDirectory=$layout.ArtifactReference; ZeroResidue=$zeroResidue; OriginalFailureId=$originalFailure; CleanupFailureId=$cleanupFailure; FailureCategory=$context.ProvisionFailureCategory; DiagnosticCategory=$context.MatrixDiagnosticCategory }
}

Export-ModuleMember -Function @(
    'Resolve-Cp6P09ContainedPath',
    'New-Cp6P09RunLayout',
    'Remove-Cp6P09ExactTree',
    'Invoke-Cp6P09Process',
    'Assert-Cp6P09SafeText',
    'New-Cp6P09CredentialSet',
    'Test-Cp6P09ForeignTopic',
    'Assert-Cp6P09ForeignTopicRejected',
    'Test-Cp6P09Profile',
    'ConvertTo-Cp6P09CanonicalJson',
    'Test-Cp6P09Evidence',
    'Get-Cp6P09OwnedFileDockerArguments',
    'Get-Cp6P09ReadabilityDockerArguments',
    'Wait-Cp6P09KafkaDataPlane',
    'Get-Cp6P09AclBatchDockerArguments',
    'Get-Cp6P09AclBatchFailureId',
    'Get-Cp6P09KafkaFailureCategory',
    'Get-Cp6P09DaprDiagnosticProcessSpec',
    'Invoke-Cp6P09DaprDiagnostic',
    'Get-Cp6P09StableFailureId',
    'Assert-Cp6P09TraceTopology',
    'Assert-Cp6P09ExpectedGitState',
    'Test-Cp6P09SupportedComposeVersion',
    'Invoke-Cp6P09Teardown',
    'Invoke-Cp6P09GuardedDockerFailure',
    'Invoke-Cp6P09BoundedRetry',
    'Invoke-Cp6P09Rehearsal'
)
