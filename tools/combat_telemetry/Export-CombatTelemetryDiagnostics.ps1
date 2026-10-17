$ErrorActionPreference = 'Stop'

$logRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\w07-blindblade+'
$telemetryRoot = Join-Path $logRoot 'CombatTelemetry'
$playerLog = Join-Path $logRoot 'Player.log'

if (-not (Test-Path -LiteralPath $logRoot)) {
    Write-Host "게임 로그 폴더를 찾지 못했습니다: $logRoot"
    exit 1
}

Write-Host '이 도구는 Player.log와 CombatTelemetry 기록을 ZIP으로 묶습니다.'
Write-Host '플레이 기록과 익명 식별자가 포함될 수 있습니다. upload-config.json은 제외합니다.'
$answer = Read-Host '진단 파일을 내보낼까요? (Y/N)'
if ($answer -notin @('Y', 'y')) { exit 0 }

$desktop = [Environment]::GetFolderPath('Desktop')
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archive = Join-Path $desktop "Blindblade-Telemetry-Diagnostics-$stamp.zip"
$temporary = Join-Path ([IO.Path]::GetTempPath()) "Blindblade-Telemetry-Diagnostics-$([guid]::NewGuid().ToString('N'))"

try {
    New-Item -ItemType Directory -Path $temporary | Out-Null
    if (Test-Path -LiteralPath $playerLog) {
        Copy-Item -LiteralPath $playerLog -Destination $temporary
    }
    if (Test-Path -LiteralPath $telemetryRoot) {
        $destination = Join-Path $temporary 'CombatTelemetry'
        New-Item -ItemType Directory -Path $destination | Out-Null
        Get-ChildItem -LiteralPath $telemetryRoot -File |
            Where-Object { $_.Name -ne 'upload-config.json' -and $_.Extension -in @('.jsonl', '.json', '.cursor', '.txt') } |
            Copy-Item -Destination $destination
    }
    Compress-Archive -Path (Join-Path $temporary '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "진단 파일 생성 완료: $archive"
    Write-Host '이 ZIP을 프로젝트 담당자에게 전달해 주세요.'
} finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
