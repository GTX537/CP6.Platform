param([Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$Owner,[Parameter(Mandatory)][string]$Settings)
$ErrorActionPreference='Stop'
$expectedRoot="D:/CP6-local-test/WP02-12/$Owner"
if([IO.Path]::GetFullPath($Settings) -ne [IO.Path]::GetFullPath("$expectedRoot/settings.json")){throw 'Settings path mismatch'}
& 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe' -X utf8 "$PSScriptRoot/pg_resource.py" verify $Settings
if($LASTEXITCODE -ne 0){throw 'Native PG ownership failed'}
foreach($port in @(18012,18013)){
    if(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue){throw 'Fixture port is occupied'}
}
$fixtureRoot=[IO.Path]::GetFullPath("$PSScriptRoot/../..")
$fixtureDll="$fixtureRoot/tests/CP6.Platform.WP0212.Fixture/bin/Release/net8.0/CP6.Platform.WP0212.Fixture.dll"
$env:CP6_WP0212_SETTINGS=[IO.Path]::GetFullPath($Settings)
$runStamp=[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$fixtureProcess=Start-Process -FilePath 'C:/Users/tt/.dotnet/dotnet.exe' -ArgumentList @($fixtureDll) -WorkingDirectory $fixtureRoot -WindowStyle Hidden -RedirectStandardOutput "$expectedRoot/transport/fixture-$runStamp.stdout.log" -RedirectStandardError "$expectedRoot/transport/fixture-$runStamp.stderr.log" -PassThru
@{Owner=$Owner;ProcessId=$fixtureProcess.Id;StartTime=$fixtureProcess.StartTime.ToUniversalTime().ToString('O');Dll=$fixtureDll;DllHash=(Get-FileHash $fixtureDll -Algorithm SHA256).Hash} | ConvertTo-Json | Set-Content -LiteralPath "$expectedRoot/transport/fixture-process-$runStamp.json"
Write-Output "Fixture started: $($fixtureProcess.Id); evidence retained under owned root"
