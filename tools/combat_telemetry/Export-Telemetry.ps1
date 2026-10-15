param(
    [Parameter(Mandatory = $true)][string]$LogDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$sourceDirectory = (Resolve-Path -LiteralPath $LogDirectory).Path
$destinationDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ($sourceDirectory.TrimEnd('\') -eq $destinationDirectory.TrimEnd('\')) {
    throw '출력은 원본 로그 폴더와 다른 경로를 사용하세요.'
}
[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
$eventRows = [System.Collections.Generic.List[object]]::new()
$diagnostics = [System.Collections.Generic.List[object]]::new()
$seen = @{}
$sessions = @{}
$files = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.jsonl' -File | Sort-Object Name)
foreach ($file in $files) {
    $lineNumber = 0
    $reader = [System.IO.StreamReader]::new($file.FullName, [System.Text.Encoding]::UTF8)
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            $lineNumber++
            try {
                $record = $line | ConvertFrom-Json
                if ($record.schemaVersion -ne '0.2.0') { throw 'unsupported_schema' }
                if ($record.sessionId -notmatch '^[0-9a-f]{32}$' -or [long]$record.eventSequence -lt 1) { throw 'invalid_identity' }
                $key = '{0}:{1}' -f $record.sessionId, $record.eventSequence
                if ($seen.ContainsKey($key)) { throw 'duplicate_event_key' }
                $seen[$key] = $true
                if (!$sessions.ContainsKey($record.sessionId)) {
                    $sessions[$record.sessionId] = [ordered]@{
                        session_id = $record.sessionId; anonymous_player_id = $record.anonymousPlayerId
                        build_version = $record.buildVersion; config_version = $record.configVersion
                        started_kst = ''; status = 'incomplete'; has_session_end = $false
                        run_realtime_seconds = ''; combat_realtime_seconds = ''; survival_game_seconds = ''
                        kills = ''; damage_taken = ''; defense_count = ''; parry_count = ''; health = ''
                        dropped_events_total = ''; last_event_sequence = 0L
                    }
                }
                $summary = $sessions[$record.sessionId]
                if ([long]$record.eventSequence -ne ($summary.last_event_sequence + 1)) {
                    $diagnostics.Add([pscustomobject]@{ file = $file.Name; line = $lineNumber; problem = 'sequence_gap_or_out_of_order'; event_key = $key })
                }
                $summary.last_event_sequence = [Math]::Max($summary.last_event_sequence, [long]$record.eventSequence)
                $payload = $record.payload
                $ids = $record.correlation
                $time = $record.occurredAt
                $eventRows.Add([pscustomobject][ordered]@{
                    event_key = $key; session_id = $record.sessionId; anonymous_player_id = $record.anonymousPlayerId
                    build_version = $record.buildVersion; config_version = $record.configVersion
                    event_sequence = $record.eventSequence; event_type = $record.eventType
                    source_id = $record.sourceId; source_sequence = $record.sourceSequence
                    occurred_at_kst = $record.occurredAtKst; recorded_at_kst = $record.recordedAtKst
                    realtime_seconds = $time.realtimeSeconds; gameplay_seconds = $time.gameplaySeconds
                    active_unscaled_seconds = $time.activeUnscaledSeconds
                    input_id = $ids.inputId; action_id = $ids.actionId; attack_id = $ids.attackId
                    hit_id = $ids.hitId; projectile_id = $ids.projectileId; dodge_id = $ids.dodgeId
                    outcome = $payload.outcome; stage = $payload.stage; applied_damage = $payload.appliedDamage
                    payload_json = ($payload | ConvertTo-Json -Depth 30 -Compress)
                })
                if ($record.eventType -eq 'session_started') {
                    $summary.started_kst = $payload.startedKst
                    if (!$summary.started_kst -and $payload.startedUtc) {
                        $legacyTime = [DateTimeOffset]::MinValue
                        if ([DateTimeOffset]::TryParse([string]$payload.startedUtc, [System.Globalization.CultureInfo]::InvariantCulture,
                            [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$legacyTime)) {
                            $summary.started_kst = $legacyTime.ToOffset([TimeSpan]::FromHours(9)).ToString(
                                "yyyy-MM-dd'T'HH:mm:ss.fffzzz", [System.Globalization.CultureInfo]::InvariantCulture)
                        } else {
                            $diagnostics.Add([pscustomobject]@{ file = $file.Name; line = $lineNumber; problem = 'invalid_legacy_start_time'; event_key = $key })
                        }
                    }
                }
                if ($record.eventType -in @('progress_sample', 'player_died', 'run_ended', 'session_ended')) {
                    $summary.status = $payload.reason
                    $summary.run_realtime_seconds = $payload.runRealtimeSeconds
                    $summary.combat_realtime_seconds = $payload.combatRealtimeSeconds
                    $summary.survival_game_seconds = $payload.survivalGameSeconds
                    $summary.kills = $payload.kills
                    $summary.damage_taken = $payload.damageTaken
                    $summary.defense_count = $payload.defenseCount
                    $summary.parry_count = $payload.parryCount
                    $summary.health = $payload.currentHealth
                    $summary.dropped_events_total = $payload.serviceDroppedEventsTotal
                    if ($record.eventType -eq 'session_ended') { $summary.has_session_end = $true }
                }
            } catch {
                $diagnostics.Add([pscustomobject]@{ file = $file.Name; line = $lineNumber; problem = $_.Exception.Message; event_key = '' })
            }
        }
    } finally { $reader.Dispose() }
}
$summaryRows = @($sessions.Values | ForEach-Object { [pscustomobject]$_ } | Sort-Object session_id)
foreach ($row in $summaryRows) {
    if (!$row.has_session_end) {
        $diagnostics.Add([pscustomobject]@{ file = $row.session_id + '.jsonl'; line = 0; problem = 'missing_session_end'; event_key = '' })
    }
}
$eventRows | Export-Csv -LiteralPath (Join-Path $destinationDirectory 'events.csv') -NoTypeInformation -Encoding UTF8
$summaryRows | Export-Csv -LiteralPath (Join-Path $destinationDirectory 'sessions.csv') -NoTypeInformation -Encoding UTF8
$diagnostics | Export-Csv -LiteralPath (Join-Path $destinationDirectory 'diagnostics.csv') -NoTypeInformation -Encoding UTF8
$eventRows | Where-Object { $_.input_id -gt 0 } |
    Select-Object session_id, input_id, action_id, attack_id, hit_id, projectile_id, dodge_id, event_key, event_type |
    Export-Csv -LiteralPath (Join-Path $destinationDirectory 'explicit-links.csv') -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    analysis_version = '0.2.0'; generated_kst = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(9)).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", [System.Globalization.CultureInfo]::InvariantCulture)
    source_files = @($files | ForEach-Object { [pscustomobject]@{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    events = $eventRows.Count; sessions = $summaryRows.Count; diagnostics = $diagnostics.Count
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destinationDirectory 'analysis-manifest.json') -Encoding UTF8
Write-Output ('events={0}, sessions={1}, diagnostics={2}, output={3}' -f $eventRows.Count, $summaryRows.Count, $diagnostics.Count, $destinationDirectory)
