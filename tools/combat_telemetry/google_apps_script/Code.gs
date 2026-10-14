const EVENT_HEADERS = [
  'event_key', 'received_utc', 'schema_version', 'build_version', 'config_version',
  'session_id', 'event_sequence', 'event_type', 'realtime_seconds', 'gameplay_seconds',
  'active_unscaled_seconds', 'source_id', 'source_sequence', 'input_id', 'action_id',
  'attack_id', 'hit_id', 'projectile_id', 'dodge_id', 'anonymous_player_id', 'payload_json'
];
const SESSION_HEADERS = [
  'session_id', 'build_version', 'config_version', 'started_utc', 'last_event_sequence',
  'status', 'run_realtime_seconds', 'combat_realtime_seconds', 'survival_game_seconds',
  'kills', 'damage_taken', 'defense_count', 'parry_count', 'health', 'dropped_events_total', 'anonymous_player_id'
];
const EVENT_TYPES = new Set([
  'session_started', 'state_changed', 'session_ended', 'combat_started', 'progress_sample',
  'resource_changed', 'damage_applied', 'player_died', 'run_ended', 'action_phase', 'katana_phase',
  'defense_resolved', 'input_resolved', 'dodge_changed', 'empower_changed', 'reveal_pulse',
  'projectile_lifecycle', 'throw_telegraph', 'enemy_lifecycle', 'threat_presentation_changed',
  'footstep', 'time_scale_changed'
]);

/** 스크립트 속성의 저장소를 열고 기존 데이터를 지우지 않은 채 헤더를 준비한다. */
function setupSheets() {
  const properties = PropertiesService.getScriptProperties();
  if (!properties.getProperty('UPLOAD_TOKEN') || properties.getProperty('UPLOAD_TOKEN').length < 32) {
    throw new Error('UPLOAD_TOKEN을 32자 이상으로 설정하세요.');
  }
  const spreadsheet = SpreadsheetApp.openById(properties.getProperty('SPREADSHEET_ID'));
  ensureSheet(spreadsheet, 'Events', EVENT_HEADERS);
  ensureSheet(spreadsheet, 'Sessions', SESSION_HEADERS);
}

/** POST JSON의 접근 토큰·스키마를 검사하고 중복 없는 사건을 저장한 뒤 정확한 확인 키를 반환한다. */
function doPost(e) {
  let batchId = '';
  let lock;
  try {
    if (!e || !e.postData || e.postData.contents.length > 262144) throw new Error('invalid_request');
    const body = JSON.parse(e.postData.contents);
    batchId = typeof body.batchId === 'string' ? body.batchId : '';
    const properties = PropertiesService.getScriptProperties();
    const token = properties.getProperty('UPLOAD_TOKEN');
    if (!token || token.length < 32 || body.token !== token) throw new Error('unauthorized');
    if (body.protocolVersion !== '1' || !batchId || batchId.length > 160
        || !Array.isArray(body.records) || body.records.length < 1 || body.records.length > 100) {
      throw new Error('invalid_batch');
    }
    body.records.forEach(validateRecord);
    lock = LockService.getScriptLock();
    if (!lock.tryLock(10000)) throw new Error('busy_retry');
    const spreadsheet = SpreadsheetApp.openById(properties.getProperty('SPREADSHEET_ID'));
    const sheet = ensureSheet(spreadsheet, 'Events', EVENT_HEADERS);
    const existing = new Set(sheet.getLastRow() > 1
      ? sheet.getRange(2, 1, sheet.getLastRow() - 1, 1).getValues().map(row => String(row[0])) : []);
    const rows = [];
    const acknowledged = [];
    const now = new Date().toISOString();
    body.records.forEach(record => {
      const key = record.sessionId + ':' + record.eventSequence;
      acknowledged.push(key);
      if (existing.has(key)) return;
      existing.add(key);
      const time = record.occurredAt;
      const ids = record.correlation;
      rows.push([
        key, now, record.schemaVersion, record.buildVersion, record.configVersion,
        record.sessionId, record.eventSequence, record.eventType, time.realtimeSeconds,
        time.gameplaySeconds, time.activeUnscaledSeconds, record.sourceId, record.sourceSequence,
        ids.inputId, ids.actionId, ids.attackId, ids.hitId, ids.projectileId, ids.dodgeId, record.anonymousPlayerId,
        JSON.stringify(record.payload)
      ].map(safeCell));
    });
    if (rows.length) {
      const lastRow = sheet.getLastRow();
      if (lastRow + rows.length > sheet.getMaxRows()) sheet.insertRowsAfter(sheet.getMaxRows(), rows.length);
      sheet.getRange(lastRow + 1, 1, rows.length, EVENT_HEADERS.length).setValues(rows);
    }
    SpreadsheetApp.flush();
    // 확인 응답 유실 뒤 재요청에서도 Sessions 집계를 복구한다.
    updateSessions(spreadsheet, body.records);
    SpreadsheetApp.flush();
    return jsonResponse({ ok: true, batchId: batchId, acknowledgedEventIds: acknowledged });
  } catch (error) {
    // 토큰·본문·Google 내부 예외를 클라이언트나 실행 로그에 출력하지 않는다.
    return jsonResponse({ ok: false, batchId: batchId, error: 'rejected_or_retry', acknowledgedEventIds: [] });
  } finally {
    if (lock && lock.hasLock()) lock.releaseLock();
  }
}

/** record가 지원 스키마·사건 종류·유효 ID와 유한 시계를 가졌는지 확인하며 잘못된 배치를 거부한다. */
function validateRecord(record) {
  if (!record || record.schemaVersion !== '0.2.0' || record.contractVersion !== '0.2.0'
      || !/^[0-9a-f]{32}$/.test(record.sessionId)
      || !/^[0-9a-f]{32}$/.test(record.anonymousPlayerId)
      || !Number.isSafeInteger(record.eventSequence) || record.eventSequence < 1
      || !Number.isSafeInteger(record.sourceSequence) || record.sourceSequence < 1
      || !Number.isSafeInteger(record.sessionGeneration) || record.sessionGeneration < 1
      || !EVENT_TYPES.has(record.eventType)
      || typeof record.buildVersion !== 'string' || typeof record.configVersion !== 'string'
      || !record.configVersion || typeof record.sourceId !== 'string' || !record.sourceId
      || !record.payload || Array.isArray(record.payload) || typeof record.payload !== 'object') {
    throw new Error('invalid_record');
  }
  ['occurredAt', 'recordedAt'].forEach(name => {
    ['realtimeSeconds', 'gameplaySeconds', 'activeUnscaledSeconds'].forEach(field => {
      if (!record[name] || !Number.isFinite(record[name][field]) || record[name][field] < 0) throw new Error('invalid_time');
    });
  });
  ['inputId', 'actionId', 'attackId', 'hitId', 'projectileId', 'dodgeId'].forEach(field => {
    if (!record.correlation || !Number.isSafeInteger(record.correlation[field]) || record.correlation[field] < 0) {
      throw new Error('invalid_id');
    }
  });
  if (JSON.stringify(record.payload).length > 40000) throw new Error('payload_too_large');
}

/** spreadsheet의 name 탭을 준비하고 기존 헤더가 계약과 다르면 덮어쓰지 않고 실패한다. */
function ensureSheet(spreadsheet, name, headers) {
  const sheet = spreadsheet.getSheetByName(name) || spreadsheet.insertSheet(name);
  if (sheet.getMaxColumns() < headers.length) sheet.insertColumnsAfter(sheet.getMaxColumns(), headers.length - sheet.getMaxColumns());
  if (sheet.getLastRow() === 0) {
    sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
    sheet.setFrozenRows(1);
  } else if (JSON.stringify(sheet.getRange(1, 1, 1, headers.length).getValues()[0]) !== JSON.stringify(headers)) {
    throw new Error('header_mismatch');
  }
  return sheet;
}

/** records의 최신 요약을 세션당 한 행으로 갱신한다. 원본 사건 재전송과 오래된 요약은 누적 수를 중복 합산하지 않는다. */
function updateSessions(spreadsheet, records) {
  const sheet = ensureSheet(spreadsheet, 'Sessions', SESSION_HEADERS);
  const rows = sheet.getLastRow() > 1
    ? sheet.getRange(2, 1, sheet.getLastRow() - 1, SESSION_HEADERS.length).getValues() : [];
  const index = new Map(rows.map((row, i) => [String(row[0]), i]));
  records.forEach(record => {
    if (!['session_started', 'progress_sample', 'player_died', 'run_ended', 'session_ended'].includes(record.eventType)) return;
    let i = index.get(record.sessionId);
    if (i === undefined) {
      i = rows.length;
      index.set(record.sessionId, i);
      rows.push(new Array(SESSION_HEADERS.length).fill(''));
      rows[i][0] = record.sessionId;
    }
    const row = rows[i];
    const payload = record.payload;
    if (record.eventType === 'session_started' && !row[3]) row[3] = safeCell(payload.startedUtc || '');
    if (Number(row[4]) >= record.eventSequence) return;
    row[1] = safeCell(record.buildVersion);
    row[2] = safeCell(record.configVersion);
    row[4] = record.eventSequence;
    row[15] = record.anonymousPlayerId;
    if (record.eventType === 'session_started') row[5] = 'started';
    else {
      row[5] = record.eventType === 'progress_sample' ? 'in_progress' : safeCell(payload.reason || record.eventType);
      const fields = ['runRealtimeSeconds', 'combatRealtimeSeconds', 'survivalGameSeconds', 'kills',
        'damageTaken', 'defenseCount', 'parryCount', 'currentHealth', 'serviceDroppedEventsTotal'];
      fields.forEach((field, column) => { row[column + 6] = Number.isFinite(payload[field]) ? payload[field] : ''; });
    }
  });
  if (rows.length) {
    if (rows.length + 1 > sheet.getMaxRows()) sheet.insertRowsAfter(sheet.getMaxRows(), rows.length + 1 - sheet.getMaxRows());
    sheet.getRange(2, 1, rows.length, SESSION_HEADERS.length).setValues(rows.map(row => row.map(safeCell)));
  }
}

/** value가 수식으로 실행되지 않게 텍스트를 이스케이프하고 셀 값을 반환한다. */
function safeCell(value) {
  return typeof value === 'string' && /^[=+\-@\t\r\n]/.test(value) ? "'" + value : value;
}

/** value를 JSON 확인 응답으로 반환한다. */
function jsonResponse(value) {
  return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON);
}
