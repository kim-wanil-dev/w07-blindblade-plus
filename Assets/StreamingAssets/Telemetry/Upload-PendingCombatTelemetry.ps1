[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$RequestPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$readyPath = $RequestPath + '.ready'
$logPath = $RequestPath + '.log'
$failureReason = 'initialization'
$mutex = $null
$ownsMutex = $false

# Records a diagnostic code without the upload token or response body.
function Write-UploadLog([string]$Message) {
    [IO.File]::AppendAllText($logPath, [DateTimeOffset]::Now.ToString('o') + ' ' + $Message + "`n", $utf8)
}

# Reads a contiguous LF-delimited JSONL batch and its exact byte checkpoint.
function Read-UploadBatch([string]$Path, [long]$Offset) {
    $records = New-Object 'Collections.Generic.List[object]'
    $stream = New-Object IO.FileStream($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $length = $stream.Length
        if ($Offset -lt 0 -or $Offset -gt $length) { throw 'cursor_out_of_range' }
        $stream.Position = $Offset
        $reader = New-Object IO.StreamReader($stream, $utf8, $false, 4096, $true)
        try {
            $end = $Offset
            while (-not $reader.EndOfStream -and $records.Count -lt $batchLimit) {
                $line = $reader.ReadLine()
                $bytes = $utf8.GetByteCount($line) + 1
                if ($bytes -gt 196608 -or $end + $bytes -gt $length) { throw 'invalid_or_incomplete_jsonl' }
                if ($end - $Offset + $bytes -gt 196608 -and $records.Count -gt 0) { break }
                $record = ConvertFrom-Json -InputObject $line
                if ([string]$record.sessionId -ne [IO.Path]::GetFileNameWithoutExtension($Path)) { throw 'session_mismatch' }
                $records.Add($record)
                $end += $bytes
            }
            return [pscustomobject]@{ records = $records.ToArray(); endOffset = $end }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

# Sends a batch and accepts only an exact batch ID and every requested event ACK.
function Send-UploadBatch([object[]]$Records, [string]$BatchId) {
    $script:failureReason = 'network_or_http'
    $body = ConvertTo-Json -InputObject @{
        protocolVersion = '1'; token = [string]$configuration.token; batchId = $BatchId; records = @($Records)
    } -Depth 100 -Compress
    $response = Invoke-WebRequest -Uri ([string]$configuration.endpoint) -Method Post -UseBasicParsing `
        -ContentType 'application/json; charset=utf-8' -Body $utf8.GetBytes($body) -TimeoutSec $timeoutSeconds
    $script:failureReason = 'non_json_response'
    $result = ConvertFrom-Json -InputObject $response.Content
    $script:failureReason = 'response_or_batch_mismatch'
    if ($result.ok -ne $true -or [string]$result.batchId -ne $BatchId) {
        if ($result.PSObject.Properties.Name -contains 'error' -and [string]$result.error -match '^[a-z_]+$') {
            $script:failureReason = 'server_' + [string]$result.error
        }
        throw 'unacknowledged_batch'
    }
    $script:failureReason = 'missing_event_ack'
    $ack = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($key in @($result.acknowledgedEventIds)) { [void]$ack.Add([string]$key) }
    foreach ($record in $Records) {
        if (-not $ack.Contains([string]$record.sessionId + ':' + [string]$record.eventSequence)) {
            throw 'missing_event_ack'
        }
    }
}

# Atomically advances only the contiguous batch that the server fully acknowledged.
function Save-UploadCursor([string]$Path, [long]$Offset) {
    $temporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporary, $Offset.ToString([Globalization.CultureInfo]::InvariantCulture), $utf8)
    if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, $null) }
    else { [IO.File]::Move($temporary, $Path) }
}

# Reads the checkpoint for this exact endpoint, preserving other destinations.
function Get-UploadCursor([string]$Path) {
    if ([IO.File]::Exists($Path)) { return [long]([IO.File]::ReadAllText($Path).Trim()) }
    return [long]0
}

# Collects lifecycle facts and only the latest open summary ahead of normal data.
function Get-PriorityRecords {
    $priority = New-Object 'Collections.Generic.List[object]'
    foreach ($sessionId in $sessionIds) {
        $path = Join-Path $root ($sessionId + '.jsonl')
        if (-not [IO.File]::Exists($path)) { continue }
        $offset = Get-UploadCursor ($path + '.' + $destination + '.cursor')
        $summaries = @{}
        $ended = New-Object 'Collections.Generic.HashSet[string]'
        do {
            $batch = Read-UploadBatch $path $offset
            foreach ($record in $batch.records) {
                $type = [string]$record.eventType
                $attempt = [string]$record.stageAttemptId
                if ($type -eq 'stage_summary') { $summaries[$attempt] = $record }
                elseif ($type -in @('stage_started', 'stage_ended', 'player_died', 'game_completed', 'session_ended')) {
                    $priority.Add($record)
                    if ($type -eq 'stage_ended') { [void]$ended.Add($attempt) }
                }
            }
            $offset = $batch.endOffset
        } while ($batch.records.Count -gt 0)
        foreach ($attempt in $summaries.Keys) {
            if (-not $ended.Contains($attempt)) { $priority.Add($summaries[$attempt]) }
        }
    }
    return ,$priority
}

# Reclaims only fully ACKed closed files from this job when no recovery marker owns them.
function Remove-AcknowledgedFiles {
    $activeSession = ''
    $marker = Join-Path $root 'active-process.json'
    if ([IO.File]::Exists($marker)) {
        $activeSession = [string](ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($marker))).sessionId
    }
    foreach ($sessionId in $sessionIds) {
        $path = Join-Path $root ($sessionId + '.jsonl')
        $cursorPath = $path + '.' + $destination + '.cursor'
        if ($sessionId -eq $activeSession -or -not [IO.File]::Exists($path)) { continue }
        if ((Get-UploadCursor $cursorPath) -ne (Get-Item -LiteralPath $path).Length) { continue }
        $last = $null
        foreach ($line in [IO.File]::ReadLines($path)) { $last = $line }
        if (-not $last -or (ConvertFrom-Json -InputObject $last).eventType -ne 'session_ended') { continue }
        # A second endpoint keeps its independent original and checkpoint.
        $cursors = @(Get-ChildItem -LiteralPath $root -Filter ($sessionId + '.jsonl.*.cursor'))
        if ($cursors.Count -ne 1) { continue }
        [IO.File]::Delete($path)
        [IO.File]::Delete((Join-Path $root ($sessionId + '.json')))
        [IO.File]::Delete($cursorPath)
    }
}

try {
    $RequestPath = [IO.Path]::GetFullPath($RequestPath)
    $job = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($RequestPath))
    $root = [IO.Path]::GetFullPath([string]$job.root)
    if ([IO.Path]::GetDirectoryName($RequestPath) -ne $root.TrimEnd([char]'\')) { throw 'invalid_job_root' }
    $configuration = $job.configuration
    $endpoint = [Uri]([string]$configuration.endpoint)
    if ($configuration.enabled -ne $true -or $endpoint.Scheme -ne 'https' -or ([string]$configuration.token).Length -lt 32) {
        throw 'invalid_upload_configuration'
    }
    $sessionIds = @($job.sessionIds)
    foreach ($sessionId in $sessionIds) {
        if ([string]$sessionId -notmatch '^[a-fA-F0-9]{32}$') { throw 'invalid_session_id' }
    }
    $batchLimit = [Math]::Max(1, [Math]::Min(100, [int]$job.batchLimit))
    $timeoutSeconds = [Math]::Max(60, [Math]::Min(120, [int]$job.timeoutSeconds))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $destination = [BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes([string]$configuration.endpoint))).Replace('-', '').Substring(0, 32) }
    finally { $sha.Dispose() }
    $mutex = New-Object Threading.Mutex($false, ('Local\BlindBladeTelemetry-' + [IO.Path]::GetFileName($RequestPath)))
    try { $ownsMutex = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { exit 0 }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    [IO.File]::WriteAllText($readyPath, 'ready', $utf8)
    Write-UploadLog 'ready; waiting for game process to exit'
    while ($true) {
        $parent = Get-Process -Id ([int]$job.parentProcessId) -ErrorAction SilentlyContinue
        if ($null -eq $parent) { break }
        try { if ($parent.StartTime.ToUniversalTime().Ticks -ne [long]$job.parentStartTicks) { break } }
        catch { Start-Sleep -Milliseconds 200; continue }
        Start-Sleep -Milliseconds 200
    }
    Write-UploadLog ('uploading current job only; sessions=' + $sessionIds.Count)
    $deadline = [DateTime]::UtcNow.AddHours(24)
    $priority = $null
    $lastFailure = ''
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $failureReason = 'read_priority'
            if ($null -eq $priority) { $priority = Get-PriorityRecords }
            while ($priority.Count -gt 0) {
                $records = New-Object 'Collections.Generic.List[object]'
                $bytes = 0
                foreach ($record in $priority) {
                    $recordBytes = $utf8.GetByteCount((ConvertTo-Json -InputObject $record -Depth 100 -Compress)) + 1
                    if ($records.Count -ge $batchLimit -or $bytes + $recordBytes -gt 196608) { break }
                    $records.Add($record)
                    $bytes += $recordBytes
                }
                if ($records.Count -eq 0) { throw 'oversize_priority_record' }
                Send-UploadBatch $records.ToArray() ('priority:' + [Guid]::NewGuid().ToString('N'))
                Write-UploadLog ('priority ACK; records=' + $records.Count)
                $priority.RemoveRange(0, $records.Count)
            }
            $found = $false
            foreach ($sessionId in $sessionIds) {
                $failureReason = 'read_jsonl'
                $path = Join-Path $root ($sessionId + '.jsonl')
                if (-not [IO.File]::Exists($path)) { continue }
                $cursorPath = $path + '.' + $destination + '.cursor'
                $batch = Read-UploadBatch $path (Get-UploadCursor $cursorPath)
                if ($batch.records.Count -eq 0) { continue }
                $records = @($batch.records)
                $batchId = $sessionId + ':' + [string]$records[0].eventSequence + ':' + [string]$records[$records.Count - 1].eventSequence
                Send-UploadBatch $records $batchId
                $failureReason = 'save_ack_cursor'
                Save-UploadCursor $cursorPath $batch.endOffset
                Write-UploadLog ('contiguous ACK; records=' + $records.Count)
                $found = $true
                break
            }
            if (-not $found) {
                $failureReason = 'cleanup'
                Remove-AcknowledgedFiles
                Write-UploadLog 'complete; all current-job records acknowledged'
                [IO.File]::Delete($RequestPath)
                [IO.File]::Delete($readyPath)
                exit 0
            }
            $lastFailure = ''
        } catch {
            if ($failureReason -ne $lastFailure) {
                Write-UploadLog ('retry in 20s; reason=' + $failureReason + '; exception=' + $_.Exception.GetType().Name)
                $lastFailure = $failureReason
            }
            Start-Sleep -Seconds 20
        }
    }
    Write-UploadLog '24h limit reached; job and unacknowledged originals retained for later resumption'
} catch {
    Write-UploadLog ('initialization failed; exception=' + $_.Exception.GetType().Name)
    exit 1
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
}
