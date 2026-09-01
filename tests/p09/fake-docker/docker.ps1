$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$logPath = $env:CP6_P09_FAKE_DOCKER_LOG
if ([string]::IsNullOrWhiteSpace($logPath)) {
    throw 'CP6_P09_FAKE_DOCKER_LOG is required.'
}

$stdin = [Console]::In.ReadToEnd()
$record = [ordered]@{
    argv = @($args)
    stdinBytes = [Text.Encoding]::UTF8.GetByteCount($stdin)
}
$json = $record | ConvertTo-Json -Compress -Depth 8
[IO.File]::AppendAllText($logPath, $json + "`n", [Text.UTF8Encoding]::new($false))

$callIndexPath = "$logPath.index"
$callIndex = if (Test-Path -LiteralPath $callIndexPath -PathType Leaf) {
    [int]([IO.File]::ReadAllText($callIndexPath, [Text.Encoding]::UTF8))
}
else {
    0
}
[IO.File]::WriteAllText($callIndexPath, ([string]($callIndex + 1)), [Text.UTF8Encoding]::new($false))

$responsesPath = $env:CP6_P09_FAKE_DOCKER_RESPONSES
$response = $null
if (-not [string]::IsNullOrWhiteSpace($responsesPath) -and
    (Test-Path -LiteralPath $responsesPath -PathType Leaf)) {
    $responses = @(
        Get-Content -LiteralPath $responsesPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_ | ConvertFrom-Json }
    )
    if ($callIndex -lt $responses.Count) {
        $response = $responses[$callIndex]
    }
}

if ($null -ne $response) {
    if ($null -ne $response.stdout) {
        [Console]::Out.Write([string]$response.stdout)
    }
    if ($null -ne $response.stderr) {
        [Console]::Error.Write([string]$response.stderr)
    }
    exit [int]$response.exitCode
}

exit 0
