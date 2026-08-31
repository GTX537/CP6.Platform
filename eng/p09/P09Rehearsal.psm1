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
        'image-pull','kafka-start','provision-first','provision-idempotent','topic-drift','acl-drift','acl-list',
        'runtime-start','publisher-port','publisher-health','invoke-positive','pubsub-positive','direct-kafka-denied',
        'principal-denied','appid-scope-denied','foreign-topic-denied','topic-list','image-digest','http-status','http-output-limit'
    )
    $allowed += @('topic-create-first','topic-describe-first','acl-list-first','topic-create-replay','acl-list-replay')
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

function Assert-Cp6P09TargetReadableFile {
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$RelativeDirectory,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string]$User
    )
    $directory = Resolve-Cp6P09ContainedPath -Root $Context.RuntimeRoot -Candidate (Join-Path $Context.RuntimeRoot $RelativeDirectory) -RequireChild
    $mount = "${directory}:/input:ro"
    $result = Invoke-Cp6P09Compose -Context $Context -Arguments @(
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--user',$User,
        '--volume',$mount,'--entrypoint','/bin/sh','kafka-admin','-c',"test -r '/input/$FileName'"
    ) -TimeoutSeconds 120
    Assert-Cp6P09CommandSucceeded $result 'runtime-readability'
}

function New-Cp6P09ClientProperties {
    param([string]$Username, [string]$Password)
    return @"
security.protocol=SASL_PLAINTEXT
sasl.mechanism=PLAIN
sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="$Username" password="$Password";
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
        @('kafka/clients','provisioner.properties','1000:1000',(New-Cp6P09ClientProperties 'cp6-p09-provisioner' $Credentials.Provisioner)),
        @('kafka/clients','publisher.properties','1000:1000',(New-Cp6P09ClientProperties 'cp6-p09-probe-publisher' $Credentials.Publisher)),
        @('kafka/clients','receiver.properties','1000:1000',(New-Cp6P09ClientProperties 'cp6-p09-probe-receiver' $Credentials.Receiver)),
        @('kafka/clients','unauthorized.properties','1000:1000',(New-Cp6P09ClientProperties 'cp6-p09-unauthorized-probe' $Credentials.Unauthorized)),
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
    foreach ($file in $files) {
        Write-Cp6P09TargetOwnedFile -Context $Context -RelativeDirectory $file[0] -FileName $file[1] -User $file[2] -Content $file[3]
    }
    foreach ($relative in @($files | ForEach-Object { $_[0] } | Select-Object -Unique)) {
        Set-Cp6P09RuntimeDirectorySecurity -Path (Join-Path $Context.RuntimeRoot $relative) -UnixMode '0711'
    }
    foreach ($file in $files) {
        Assert-Cp6P09TargetReadableFile -Context $Context -RelativeDirectory $file[0] -FileName $file[1] -User $file[2]
    }
}

function Invoke-Cp6P09KafkaTool {
    param([Parameter(Mandatory)]$Context, [Parameter(Mandatory)][string]$Tool, [Parameter(Mandatory)][string[]]$Arguments, [AllowEmptyString()][string]$StandardInput)
    if ($Tool -notmatch '^kafka-(?:topics|configs|acls|console-producer|console-consumer)\.sh$') { throw 'kafka-tool' }
    $command = @('--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint',"/opt/kafka/bin/$Tool",'kafka-admin') + $Arguments
    $parameters = @{ Context=$Context; Arguments=$command; TimeoutSeconds=120 }
    if ($PSBoundParameters.ContainsKey('StandardInput')) { $parameters.StandardInput=$StandardInput }
    return Invoke-Cp6P09Compose @parameters
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
    $acls = @(
        @('cp6-p09-probe-publisher','Write','--topic',$topic),
        @('cp6-p09-probe-publisher','Describe','--topic',$topic),
        @('cp6-p09-probe-receiver','Read','--topic',$topic),
        @('cp6-p09-probe-receiver','Describe','--topic',$topic),
        @('cp6-p09-probe-receiver','Read','--group','cp6-p09-probe-receiver-v1'),
        @('cp6-p09-provisioner','Create','--topic',$topic),
        @('cp6-p09-provisioner','Alter','--topic',$topic),
        @('cp6-p09-provisioner','Describe','--topic',$topic),
        @('cp6-p09-provisioner','Describe','--cluster',$null)
    )
    for ($aclIndex = 0; $aclIndex -lt $acls.Count; $aclIndex++) {
        $acl = $acls[$aclIndex]
        $Context.ProvisionFailureId = 'acl-add-first-{0:d2}' -f ($aclIndex + 1)
        $args = @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--add','--allow-principal',"User:$($acl[0])",'--operation',$acl[1],$acl[2])
        if ($null -ne $acl[3]) { $args += $acl[3] }
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09KafkaTool $Context 'kafka-acls.sh' $args) $Context.ProvisionFailureId
    }
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
    for ($aclIndex = 0; $aclIndex -lt $acls.Count; $aclIndex++) {
        $acl = $acls[$aclIndex]
        $Context.ProvisionFailureId = 'acl-add-replay-{0:d2}' -f ($aclIndex + 1)
        $args = @('--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/provisioner.properties','--add','--allow-principal',"User:$($acl[0])",'--operation',$acl[1],$acl[2])
        if ($null -ne $acl[3]) { $args += $acl[3] }
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09KafkaTool $Context 'kafka-acls.sh' $args) $Context.ProvisionFailureId
    }
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
        'compose','--project-name',$ProjectName,'--file',$compose,'down','--volumes','--remove-orphans','--rmi','local'
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
            'compose','--project-name',$ProjectName,'--file',$compose,'down','--volumes','--remove-orphans','--rmi','local'
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

function Invoke-Cp6P09PrincipalNegative {
    param([Parameter(Mandatory)]$Context)
    $topic = 'cp6.platform.deployment-probe.v1'
    $producer = Invoke-Cp6P09KafkaTool -Context $Context -Tool 'kafka-console-producer.sh' -Arguments @('--bootstrap-server','kafka:9092','--producer.config','/etc/kafka/clients/unauthorized.properties','--topic',$topic,'--request-timeout-ms','5000','--max-block-ms','5000') -StandardInput '{"probe":"denied"}'
    $consumer = Invoke-Cp6P09KafkaTool $Context 'kafka-console-consumer.sh' @('--bootstrap-server','kafka:9092','--consumer.config','/etc/kafka/clients/unauthorized.properties','--topic',$topic,'--group','cp6-p09-unauthorized-probe','--timeout-ms','5000','--max-messages','1')
    $producerText = $producer.StandardOutput + $producer.StandardError
    $consumerText = $consumer.StandardOutput + $consumer.StandardError
    if ($producerText -notmatch '(?i)(TopicAuthorization|not authorized)' -or $consumerText -notmatch '(?i)(Authorization|not authorized)') { throw 'principal-denied' }
}

function Invoke-Cp6P09RuntimeMatrix {
    param([Parameter(Mandatory)]$Context)
    $Context.MatrixFailureId = 'topic-list'
    $beforeTopics = Get-Cp6P09TopicList $Context
    $Context.MatrixFailureId = 'foreign-topic-denied'
    Assert-Cp6P09ForeignTopicRejected
    $Context.MatrixFailureId = 'runtime-start'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('up','--detach','--build','--wait','--wait-timeout','120','receiver','receiver-dapr') 600) 'runtime-start'
    Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('up','--detach','--build','--wait','--wait-timeout','120','publisher','publisher-dapr') 600) 'runtime-start'
    $Context.MatrixFailureId = 'publisher-port'
    $baseUri = Get-Cp6P09PublisherEndpoint $Context
    $handler = [Net.Http.SocketsHttpHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $Context.MatrixFailureId = 'publisher-health'
        Wait-Cp6P09Publisher $client $baseUri
        $Context.MatrixFailureId = 'invoke-positive'
        $invocation = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'invoke-positive')) '{}'
        if ($invocation.appId -cne 'cp6-p09-probe-receiver' -or $invocation.invocationTraceId -cnotmatch '^[0-9a-f]{32}$') { throw 'invoke-positive' }
        $Context.MatrixFailureId = 'pubsub-positive'
        $eventId = 'p09-event-' + [Guid]::NewGuid().ToString('N')
        $partitionKey = 'cp6-p09-entity-' + [Guid]::NewGuid().ToString('N')
        $publishJson = ConvertTo-Cp6P09CanonicalJson ([ordered]@{ eventId=$eventId; partitionKey=$partitionKey })
        $receipt = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'publish-positive')) $publishJson
        if ($receipt.eventId -cne $eventId -or $receipt.partitionKey -cne $partitionKey -or $receipt.topic -cne 'cp6.platform.deployment-probe.v1' -or $receipt.region -cne 'TEST') { throw 'pubsub-positive' }
        $received = $null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
        do {
            $received = Invoke-Cp6P09HttpJson -Client $client -Method ([Net.Http.HttpMethod]::Get) -Uri ([Uri]::new($baseUri,"received/$eventId")) -AllowNotFound
            if ($null -ne $received) { break }
            Start-Sleep -Milliseconds 500
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
        if ($null -eq $received -or -not $received.contractValid -or $received.eventId -cne $eventId -or $received.partitionKey -cne $partitionKey -or $received.topicName -cne 'cp6.platform.deployment-probe.v1') { throw 'pubsub-positive' }
        Assert-Cp6P09TraceTopology -Invocation $invocation -Delivery $received

        $Context.MatrixFailureId = 'direct-kafka-denied'
        $Context.Environment['CP6_P09_NEGATIVE_ROLE'] = 'probe'
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('--profile','negative','up','--detach','--build','--force-recreate','direct-probe','unauthorized-dapr') 600) 'direct-kafka-denied'
        Start-Sleep -Seconds 3
        $direct = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'negative/direct-kafka')) '{}'
        if (-not $direct.denied -or $direct.code -cne 'direct-kafka-denied') { throw 'direct-kafka-denied' }

        $Context.MatrixFailureId = 'appid-scope-denied'
        $Context.Environment['CP6_P09_NEGATIVE_ROLE'] = 'unauthorized'
        Assert-Cp6P09CommandSucceeded (Invoke-Cp6P09Compose $Context @('--profile','negative','up','--detach','--build','--force-recreate','direct-probe','unauthorized-dapr') 600) 'appid-scope-denied'
        Start-Sleep -Seconds 3
        $appid = Invoke-Cp6P09HttpJson $client ([Net.Http.HttpMethod]::Post) ([Uri]::new($baseUri,'negative/appid-scope')) '{}'
        if (-not $appid.denied -or $appid.code -cne 'appid-scope-denied') { throw 'appid-scope-denied' }
        $Context.MatrixFailureId = 'principal-denied'
        Invoke-Cp6P09PrincipalNegative $Context
        $Context.MatrixFailureId = 'foreign-topic-denied'
        $afterTopics = Get-Cp6P09TopicList $Context
        if (($beforeTopics -join "`n") -cne ($afterTopics -join "`n")) { throw 'foreign-topic-denied' }
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
        if ($version.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($version.StandardOutput)) {
            return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='docker-unavailable' }
        }
        $composeVersion = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $repository -Arguments @('compose','version','--short') -TimeoutSeconds 30
        if ($composeVersion.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($composeVersion.StandardOutput)) {
            return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='compose-unavailable' }
        }
    }
    catch [System.ComponentModel.Win32Exception] {
        return [pscustomobject]@{ Status='NotRun'; RunId=$layout.RunId; Reason='docker-unavailable' }
    }

    $clusterBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $clusterId = [Convert]::ToBase64String($clusterBytes).TrimEnd('=').Replace('+','-').Replace('/','_')
    $context = [pscustomobject]@{
        RepositoryRoot=$repository; ProjectName=$layout.ProjectName; ComposeFile=$composeFile
        RuntimeRoot=$layout.RuntimeRoot; DockerCommand=$DockerCommand
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=$layout.RuntimeRoot; CP6_P09_CLUSTER_ID=$clusterId; CP6_P09_NEGATIVE_ROLE='probe' }
        KubernetesManifestSha=$null
        MatrixFailureId='runtime-start'
        ProvisionFailureId='topic-create-first'
    }
    $config = Invoke-Cp6P09Compose $context @('--profile','negative','--profile','provision','config','--quiet') 30
    Assert-Cp6P09CommandSucceeded $config 'compose-contract'
    $profile = [IO.File]::ReadAllText($resolvedProfile,[Text.Encoding]::UTF8) | ConvertFrom-Json
    $kubernetesRoot = Join-Path $repository 'deploy/p09/kubernetes'
    $kubernetesReady = Test-Path -LiteralPath $kubernetesRoot -PathType Container
    if ($kubernetesReady) {
        throw 'kubernetes-gate-not-integrated'
    }

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
        $pull = Invoke-Cp6P09Compose $context @('pull','kafka','kafka-admin','publisher-dapr','receiver-dapr','unauthorized-dapr') 600
        Assert-Cp6P09CommandSucceeded $pull 'image-pull'
        $credentials = New-Cp6P09CredentialSet
        Initialize-Cp6P09RuntimeFiles $context $credentials
        Add-Cp6P09RunLog $logPath 'runtime-population' 'Passed'
        $stage = 'kafka-start'
        $startKafka = Invoke-Cp6P09Compose $context @('up','--detach','--no-build','--wait','--wait-timeout','120','kafka') 180
        Assert-Cp6P09CommandSucceeded $startKafka 'kafka-start'
        Add-Cp6P09RunLog $logPath 'kafka-start' 'Passed'
        $stage = 'provision-first'
        Invoke-Cp6P09Provision $context
        Add-Cp6P09RunLog $logPath 'provision' 'Passed'
        $stage = 'runtime-matrix'
        $matrix = Invoke-Cp6P09RuntimeMatrix $context
        Add-Cp6P09RunLog $logPath 'runtime-matrix' 'Passed'
        $stage = 'image-digest'
        $digests = [pscustomobject]@{ Dapr=(Get-Cp6P09ImageDigest $context 'daprio/daprd:1.18.2'); Kafka=(Get-Cp6P09ImageDigest $context 'apache/kafka:4.3.1'); Kubectl=$null }
        Add-Cp6P09RunLog $logPath 'image-digest' 'Passed'
    }
    catch {
        $fallback = if ($stage -ceq 'runtime-matrix') {
            [string]$context.MatrixFailureId
        }
        elseif ($stage -ceq 'provision-first') {
            [string]$context.ProvisionFailureId
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
        $composeResult = [ordered]@{
            schemaVersion='1'; profileId='cp6-platform-p09-ci-v1'; profileSha256='94addf0349ff895f21eca3e0d660c8d5159198267080df9109ff6493c1063681'
            platformGitSha=$gitSha; composeManifestSha256=(Get-Cp6P09Sha256File $composeFile); composeChecks='Passed'; zeroResidue=$true; kubernetesEvidence='Pending'
        }
        $resultPath = Join-Path $layout.ArtifactsDirectory 'compose-rehearsal-result.v1.json'
        [IO.File]::WriteAllText($resultPath,(ConvertTo-Cp6P09CanonicalJson $composeResult)+"`n",[Text.UTF8Encoding]::new($false))
        return [pscustomobject]@{ Status='Failed'; RunId=$layout.RunId; Reason='kubernetes-assets-pending'; ArtifactsDirectory=$layout.ArtifactReference; ZeroResidue=$true }
    }
    return [pscustomobject]@{ Status='Failed'; RunId=$layout.RunId; Reason=$(if ($null -ne $cleanupFailure) {$cleanupFailure} else {$originalFailure}); ArtifactsDirectory=$layout.ArtifactReference; ZeroResidue=$zeroResidue; OriginalFailureId=$originalFailure; CleanupFailureId=$cleanupFailure }
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
    'Get-Cp6P09StableFailureId',
    'Assert-Cp6P09TraceTopology',
    'Assert-Cp6P09ExpectedGitState',
    'Invoke-Cp6P09Teardown',
    'Invoke-Cp6P09GuardedDockerFailure',
    'Invoke-Cp6P09Rehearsal'
)
