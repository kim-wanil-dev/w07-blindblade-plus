param(
    [Parameter(Mandatory = $true)][string]$LogDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
& node (Join-Path $PSScriptRoot 'Export-Telemetry.cjs') $LogDirectory $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Telemetry export failed.' }
