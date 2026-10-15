param(
    [Parameter(Mandatory)][ValidateSet('ValidatePlan','Prepare','Broker','Dapr','Observe','Stop')][string]$Mode,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$Owner,
    [Parameter(Mandatory)][string]$Settings,
    [int]$Count = 1
)
$ErrorActionPreference = 'Stop'
& 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe' -X utf8 "$PSScriptRoot/runtime.py" $Mode --owner $Owner --settings $Settings --count $Count
if ($LASTEXITCODE -ne 0) { throw "WP02-12 phase failed ($LASTEXITCODE); private logs retained" }
