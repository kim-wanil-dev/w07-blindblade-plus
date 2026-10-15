const EVENT_HEADERS = [
  'event_key', 'received_kst', 'schema_version', 'build_version', 'config_version',
  'session_id', 'event_sequence', 'event_type', 'realtime_seconds', 'gameplay_seconds',
  'active_unscaled_seconds', 'source_id', 'source_sequence', 'input_id', 'action_id',
  'attack_id', 'hit_id', 'projectile_id', 'dodge_id', 'anonymous_player_id', 'payload_json',
  'occurred_at_kst', 'recorded_at_kst'
];
const SESSION_HEADERS = [
  'session_id', 'build_version', 'config_version', 'started_kst', 'last_event_sequence',
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
    const receivedKst = formatKst(new Date());
    body.records.forEach(record => {
      const key = record.sessionId + ':' + record.eventSequence;
      acknowledged.push(key);
      if (existing.has(key)) return;
      existing.add(key);
      const time = record.occurredAt;
      const ids = record.correlation;
      rows.push([
        key, receivedKst, record.schemaVersion, record.buildVersion, record.configVersion,
        record.sessionId, record.eventSequence, record.eventType, time.realtimeSeconds,
        time.gameplaySeconds, time.activeUnscaledSeconds, record.sourceId, record.sourceSequence,
        ids.inputId, ids.actionId, ids.attackId, ids.hitId, ids.projectileId, ids.dodgeId, record.anonymousPlayerId,
        JSON.stringify(record.payload), record.occurredAtKst || '', record.recordedAtKst || ''
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

/** spreadsheet의 name 탭을 준비하고 이전 UTC 열을 KST로 변환해 현재 헤더를 반환한다. */
function ensureSheet(spreadsheet, name, headers) {
  const sheet = spreadsheet.getSheetByName(name) || spreadsheet.insertSheet(name);
  if (sheet.getLastRow() === 0) {
    ensureColumns(sheet, headers.length);
    sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
    sheet.setFrozenRows(1);
    return sheet;
  }
  const count = sheet.getLastColumn();
  const currentHeaders = sheet.getRange(1, 1, 1, count).getValues()[0];
  if (JSON.stringify(currentHeaders) !== JSON.stringify(headers)) {
    if (name === 'Events') migrateEventSheet(sheet, currentHeaders);
    else if (name === 'Sessions') migrateSessionSheet(sheet, currentHeaders);
  }
  if (sheet.getLastColumn() !== headers.length
      || JSON.stringify(sheet.getRange(1, 1, 1, headers.length).getValues()[0]) !== JSON.stringify(headers)) {
    throw new Error('header_mismatch');
  }
  return sheet;
}

/** sheet에 필요한 열 수를 확보하고 기존 열과 데이터는 유지한다. */
function ensureColumns(sheet, count) {
  if (sheet.getMaxColumns() < count) sheet.insertColumnsAfter(sheet.getMaxColumns(), count - sheet.getMaxColumns());
}

/** 이전 Events의 UTC 수신 시각을 KST로 옮기고 중복 수신 열을 제거한다. */
function migrateEventSheet(sheet, currentHeaders) {
  const count = currentHeaders.length;
  if (count < 21 || count > 24) throw new Error('header_mismatch');
  for (let i = 0; i < 21; i++) {
    if (i === 1 && currentHeaders[i] === 'received_utc') continue;
    if (currentHeaders[i] !== EVENT_HEADERS[i]) throw new Error('header_mismatch');
  }
  for (let i = 21; i < Math.min(count, 23); i++) {
    if (currentHeaders[i] !== EVENT_HEADERS[i]) throw new Error('header_mismatch');
  }
  if (count === 24 && currentHeaders[23] !== 'received_kst') throw new Error('header_mismatch');
  migrateTimeColumn(sheet, 2, count === 24 ? 24 : 0);
  sheet.getRange(1, 2).setValue('received_kst');
  if (count < EVENT_HEADERS.length) {
    ensureColumns(sheet, EVENT_HEADERS.length);
    sheet.getRange(1, count + 1, 1, EVENT_HEADERS.length - count).setValues([EVENT_HEADERS.slice(count)]);
  }
  if (count === 24) sheet.deleteColumn(24);
}

/** 이전 Sessions의 UTC 시작 시각을 KST로 옮기고 중복 시작 열을 제거한다. */
function migrateSessionSheet(sheet, currentHeaders) {
  const count = currentHeaders.length;
  if (count !== 16 && count !== 17) throw new Error('header_mismatch');
  for (let i = 0; i < 16; i++) {
    if (i === 3 && currentHeaders[i] === 'started_utc') continue;
    if (currentHeaders[i] !== SESSION_HEADERS[i]) throw new Error('header_mismatch');
  }
  if (count === 17 && currentHeaders[16] !== 'started_kst') throw new Error('header_mismatch');
  migrateTimeColumn(sheet, 4, count === 17 ? 17 : 0);
  sheet.getRange(1, 4).setValue('started_kst');
  if (count === 17) sheet.deleteColumn(17);
}

/** 대상 열의 이전 UTC 또는 추가 KST 값을 +09:00 문자열로 바꾼다. */
function migrateTimeColumn(sheet, destinationColumn, preferredColumn) {
  const lastRow = sheet.getLastRow();
  for (let start = 2; start <= lastRow; start += 500) {
    const count = Math.min(500, lastRow - start + 1);
    const oldValues = sheet.getRange(start, destinationColumn, count, 1).getValues();
    const preferredValues = preferredColumn ? sheet.getRange(start, preferredColumn, count, 1).getValues() : null;
    const converted = oldValues.map((row, index) => [toKst(preferredValues && preferredValues[index][0] ? preferredValues[index][0] : row[0])]);
    sheet.getRange(start, destinationColumn, count, 1).setValues(converted);
  }
}

/** 이전 UTC 날짜 또는 기존 KST 문자열을 한국 표준시 ISO 8601 문자열로 반환한다. */
function toKst(value) {
  if (value === '' || value === null) return '';
  if (typeof value === 'string' && /\+09:00$/.test(value)) return value;
  if (typeof value === 'string' && !/(?:Z|[+\-]\d{2}:\d{2})$/i.test(value)) throw new Error('invalid_legacy_time');
  const date = value instanceof Date ? value : new Date(value);
  if (isNaN(date.getTime())) throw new Error('invalid_legacy_time');
  return formatKst(date);
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
    if (record.eventType === 'session_started' && !row[3]) row[3] = safeCell(payload.startedKst || '');
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

/** date를 Asia/Seoul 시간대의 ISO 8601 문자열로 반환한다. */
function formatKst(date) {
  return Utilities.formatDate(date, 'Asia/Seoul', "yyyy-MM-dd'T'HH:mm:ss.SSSXXX");
}

/** value를 JSON 확인 응답으로 반환한다. */
function jsonResponse(value) {
  return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON);
}
