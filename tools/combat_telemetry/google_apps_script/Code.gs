const EVENT_HEADERS = ['event_key','received_kst','schema_version','build_version','config_version',
  'session_id','stage_id','stage_attempt_id','anonymous_player_id','event_sequence','event_type',
  'occurred_at_kst','realtime_seconds','gameplay_seconds','input_id','action_id','attack_id','dodge_id',
  'kind','pattern_id','outcome','is_attempt','input_status','applied_damage','x','z','origin_x','origin_z',
  'aim_x','aim_z','player_x','player_z','duration_seconds','reaction_seconds','input_to_contact_seconds',
  'reaction_association','payload_json','run_id','step_index','objective_index','retry_count',
  'first_input_seconds','expected','defense_outcome'];
const METRICS = ['deaths','kills','damageTaken','damageHitCount','dodgeAttempts','dodgeRejected','dodgeStarted',
  'guardAttempts','guardRejected','guardAttemptsWithContact','guardAttemptsWithoutContact','guardHitCount','guardGuardCount',
  'guardNormalParryCount','guardPerfectParryCount','attackAttempts','attackRejected','enemyAttackCount',
  'enemyResolvedCount','enemyHitCount','enemyDodgeCount','enemyNormalParryCount','enemyPerfectParryCount',
  'enemyGuardCount','enemyMissCount','enemyInvulnerableCount','enemyIgnoredCount','enemyDestroyedCount',
  'enemyCancelledCount','enemyInterruptedCount','reactionCount','reactionMeanSeconds','enemyHitRate',
  'serviceDroppedEventsTotal'];
['ranged','melee','bossRanged','bossCharge'].forEach(group =>
  ['Attempts','Resolved','Hits','Dodges','Parries','PerfectParries','Damage','HitRate','DamageShare'].forEach(metric => METRICS.push(group+metric)));
const SESSION_HEADERS = ['stage_attempt_id','session_id','stage_id','anonymous_player_id','build_version',
  'config_version','started_kst','last_event_sequence','status','realtime_seconds','gameplay_seconds',
  'active_play_seconds','health'].concat(METRICS,
    ['patterns_json','run_id','exit_step_index','exit_objective_index','steps_completed','attempts','retry_count']);
const STEP_HEADERS = ['step_key','received_kst','run_id','session_id','stage_id','stage_attempt_id',
  'step_index','objective_index','retry_count','active_seconds','first_input_seconds','attempts',
  'wrong_attempts','movement_attempts','started_inputs','rejected_inputs','buffered_inputs',
  'dropped_inputs','reason'];
const EVENT_TYPES = new Set(['session_started','session_ended','stage_started','stage_summary','stage_ended',
  'movement_sample','player_input','player_died','dodge_changed','state_changed','reaction_candidate',
  'enemy_attack_started','enemy_attack_committed','enemy_attack_resolved','onboarding_attempt','onboarding_step']);

/** 스프레드시트를 열 때 전투 로그 메뉴를 추가한다. */
function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('전투 로그')
    .addItem('현재 데이터 초기화…','resetTelemetrySheets')
    .addToUi();
}

/** 기존 탭을 보존하고 현재 헤더를 확인한다. 다른 스키마는 명시적 초기화 전까지 거부한다. */
function setupSheets() {
  const p = PropertiesService.getScriptProperties();
  if ((p.getProperty('UPLOAD_TOKEN') || '').length < 32) throw new Error('missing_token');
  const book = SpreadsheetApp.openById(p.getProperty('SPREADSHEET_ID'));
  ensureSheet(book, 'Events', EVENT_HEADERS);
  ensureSheet(book, 'Sessions', SESSION_HEADERS);
  ensureSheet(book, 'OnboardingSteps', STEP_HEADERS);
  repairStageIds(book);
}

/** stage_attempt_id의 확정 구간을 이용해 날짜로 자동 변환된 stage_id를 텍스트로 복원한다. */
function repairStageIds(book) {
  [['Events',8,7],['Sessions',1,3],['OnboardingSteps',6,5]].forEach(([name,keyColumn,stageColumn]) => {
    const sheet=book.getSheetByName(name);
    if (!sheet) return;
    const count=sheet.getLastRow()-1;
    const cells=sheet.getRange(2,stageColumn,sheet.getMaxRows()-1,1);
    cells.setNumberFormat('@');
    if (count<=0) return;
    const ids=sheet.getRange(2,keyColumn,count,1).getValues();
    const stages=ids.map(([id])=>{
      const match=String(id).match(/:(0-[1-7]|1-1|1-2|unassigned)$/);
      return [match ? match[1] : ''];
    });
    sheet.getRange(2,stageColumn,count,1).setValues(stages);
  });
}

/** 확인을 받은 뒤 Events, Sessions, OnboardingSteps 데이터를 비우고 헤더를 복원한다. */
function resetTelemetrySheets() {
  const ui = SpreadsheetApp.getUi();
  const answer = ui.alert(
    '전투 로그 데이터 초기화',
    'Events, Sessions, OnboardingSteps의 현재 데이터가 삭제되고 헤더가 다시 만들어집니다. 이 작업은 되돌릴 수 없습니다. 아직 전송되지 않은 로컬 로그는 나중에 다시 시트에 추가될 수 있습니다. 계속할까요?',
    ui.ButtonSet.YES_NO
  );
  if (answer !== ui.Button.YES) return;

  const lock = LockService.getScriptLock();
  lock.waitLock(30000);
  try {
    const book = SpreadsheetApp.openById(PropertiesService.getScriptProperties().getProperty('SPREADSHEET_ID'));
    [['Events',EVENT_HEADERS],['Sessions',SESSION_HEADERS],['OnboardingSteps',STEP_HEADERS]].forEach(([name,headers]) => {
      const sheet = book.getSheetByName(name) || book.insertSheet(name);
      sheet.clearContents();
      ensureSheet(book,name,headers);
    });
    SpreadsheetApp.flush();
  } finally { lock.releaseLock(); }
  ui.alert('초기화 완료', 'Events, Sessions, OnboardingSteps 데이터가 비워졌고 헤더는 유지했습니다.', ui.ButtonSet.OK);
}

/** 토큰과 스키마를 검증한 뒤 사건을 중복 없이 저장하고 저장된 키만 ACK한다. */
function doPost(e) {
  let batchId = '', lock;
  try {
    if (!e || !e.postData || e.postData.contents.length > 262144) throw new Error('invalid_request');
    const body = JSON.parse(e.postData.contents);
    const p = PropertiesService.getScriptProperties();
    if (body.action === 'dashboard_snapshot') return dashboardSnapshot(body, p);
    batchId = typeof body.batchId === 'string' ? body.batchId : '';
    const token = p.getProperty('UPLOAD_TOKEN');
    if (!token || token.length < 32 || body.token !== token) throw new Error('unauthorized');
    if (body.protocolVersion !== '1' || !batchId || batchId.length > 160 || !Array.isArray(body.records)
        || body.records.length < 1 || body.records.length > 100) throw new Error('invalid_batch');
    body.records.forEach(validateRecord);
    lock = LockService.getScriptLock();
    if (!lock.tryLock(10000)) throw new Error('busy');
    const book = SpreadsheetApp.openById(p.getProperty('SPREADSHEET_ID'));
    const sheet = ensureSheet(book,'Events',EVENT_HEADERS);
    const existing = new Set(sheet.getLastRow() > 1 ? sheet.getRange(2,1,sheet.getLastRow()-1,1).getValues().flat().map(String) : []);
    const rows = [], ack = [], received = formatKst(new Date());
    body.records.forEach(r => {
      const key = r.sessionId + ':' + r.eventSequence;
      ack.push(key);
      if (existing.has(key)) return;
      existing.add(key);
      rows.push(eventRow(r,key,received));
    });
    writeRows(sheet,sheet.getLastRow()+1,rows,EVENT_HEADERS.length);
    SpreadsheetApp.flush();
    updateSessions(book,body.records);
    updateOnboardingSteps(book,body.records,received);
    SpreadsheetApp.flush();
    return jsonResponse({ok:true,batchId:batchId,acknowledgedEventIds:ack});
  } catch (error) {
    return jsonResponse({ok:false,batchId:batchId,error:'rejected_or_retry',acknowledgedEventIds:[]});
  } finally { if (lock && lock.hasLock()) lock.releaseLock(); }
}

/** 대시보드용 별도 읽기 토큰을 확인하고 분석에 필요한 열만 반환한다. */
function dashboardSnapshot(body, properties) {
  const token = properties.getProperty('DASHBOARD_READ_TOKEN');
  if (!token || token.length < 32 || body.token !== token) return jsonResponse({ok:false,error:'unauthorized'});
  const spreadsheetId = properties.getProperty('SPREADSHEET_ID');
  if (!spreadsheetId) return jsonResponse({ok:false,error:'not_configured'});
  const book = SpreadsheetApp.openById(spreadsheetId);
  return jsonResponse({
    ok:true,
    generatedAtKst:formatKst(new Date()),
    sheets:{
      Events:dashboardRows(book.getSheetByName('Events'),[
        'event_key','received_kst','schema_version','build_version','config_version','session_id','stage_id',
        'stage_attempt_id','anonymous_player_id','event_sequence','event_type','occurred_at_kst',
        'realtime_seconds','gameplay_seconds','input_id','action_id','attack_id','dodge_id','kind',
        'pattern_id','outcome','is_attempt','applied_damage','x','z','origin_x','origin_z',
        'duration_seconds','reaction_seconds','input_to_contact_seconds','reaction_association',
        'run_id','step_index','objective_index','retry_count','first_input_seconds'
      ]),
      Sessions:dashboardRows(book.getSheetByName('Sessions'),[
        'stage_attempt_id','session_id','stage_id','build_version','config_version','started_kst','status',
        'active_play_seconds','deaths','damageHitCount','dodgeAttempts','dodgeRejected','dodgeStarted',
        'guardAttempts','guardRejected','guardAttemptsWithContact','guardAttemptsWithoutContact',
        'guardHitCount','guardGuardCount','guardNormalParryCount','guardPerfectParryCount',
        'enemyAttackCount','enemyResolvedCount','enemyHitCount','enemyDodgeCount','enemyNormalParryCount',
        'enemyPerfectParryCount','enemyGuardCount','enemyMissCount','enemyInvulnerableCount',
        'enemyIgnoredCount','enemyDestroyedCount','enemyCancelledCount','enemyInterruptedCount',
        'reactionMeanSeconds','rangedDamage','meleeDamage','bossRangedDamage','bossChargeDamage'
      ]),
      OnboardingSteps:dashboardRows(book.getSheetByName('OnboardingSteps'),[
        'received_kst','run_id','session_id','stage_id','stage_attempt_id','step_index','objective_index',
        'retry_count','active_seconds','first_input_seconds','attempts','wrong_attempts','movement_attempts','reason'
      ])
    }
  });
}

/** 지정한 분석 열만 객체 행으로 만들어 시트의 불필요한 열 전송을 막는다. */
function dashboardRows(sheet, allowedHeaders) {
  if (!sheet || sheet.getLastRow() < 2 || sheet.getLastColumn() < 1) return [];
  const values = sheet.getRange(1,1,sheet.getLastRow(),sheet.getLastColumn()).getDisplayValues();
  const headers = values[0].map(value => String(value).trim());
  const columns = allowedHeaders.map(name => ({name,index:headers.indexOf(name)})).filter(item => item.index >= 0);
  return values.slice(1).filter(row => row.some(value => value !== '')).map(row => {
    const record = {};
    columns.forEach(({name,index}) => record[name] = row[index]);
    return record;
  });
}

/** record의 버전·도전 식별자·시계·종류를 검사하여 잘못된 배치를 거부한다. */
function validateRecord(r) {
  if (!r || r.schemaVersion !== '0.3.0' || r.contractVersion !== '0.2.0'
      || !/^[0-9a-f]{32}$/.test(r.sessionId) || !/^[0-9a-f]{32}$/.test(r.anonymousPlayerId)
      || !['0-1','0-2','0-3','0-4','0-5','0-6','0-7','1-1','1-2','unassigned'].includes(r.stageId) || r.stageAttemptId !== r.sessionId + ':' + r.stageId
      || !Number.isSafeInteger(r.eventSequence) || r.eventSequence < 1 || !EVENT_TYPES.has(r.eventType)
      || typeof r.buildVersion !== 'string' || typeof r.configVersion !== 'string'
      || !r.payload || Array.isArray(r.payload) || typeof r.payload !== 'object') throw new Error('invalid_record');
  ['occurredAt','recordedAt'].forEach(name => ['realtimeSeconds','gameplaySeconds','activeUnscaledSeconds'].forEach(k => {
    if (!r[name] || !Number.isFinite(r[name][k]) || r[name][k] < 0) throw new Error('invalid_time');
  }));
  ['inputId','actionId','attackId','hitId','projectileId','dodgeId'].forEach(k => {
    if (!r.correlation || !Number.isSafeInteger(r.correlation[k]) || r.correlation[k] < 0) throw new Error('invalid_id');
  });
  if (JSON.stringify(r.payload).length > 40000) throw new Error('oversize');
}

/** 사건의 자주 분석하는 필드를 열로 펼친다. 0과 값 없음은 서로 구분한다. */
function eventRow(r,key,received) {
  const p=r.payload,c=r.correlation,t=r.occurredAt, pos=p.position||{},o=p.origin||{},a=p.aim||{},player=p.playerPosition||{};
  return [key,received,r.schemaVersion,r.buildVersion,r.configVersion,r.sessionId,r.stageId,r.stageAttemptId,
    r.anonymousPlayerId,r.eventSequence,r.eventType,r.occurredAtKst,t.realtimeSeconds,t.gameplaySeconds,
    c.inputId,c.actionId,c.attackId,c.dodgeId,p.kind,p.patternId,p.outcome,p.isAttempt,p.status,p.appliedDamage,
    pos.x,pos.z,o.x,o.z,a.x,a.z,player.x,player.z,p.durationSeconds,p.reactionSeconds,p.inputToContactSeconds,
    p.reactionAssociation||p.association,JSON.stringify(p),p.runId,p.stepIndex,p.objectiveIndex,
    p.retryCount,p.firstInputSeconds,p.expected,p.defenseOutcome].map(safeCell);
}

/** 최신 도전별 누적 스냅샷의 변경 행만 덮어쓴다. 재전송·역순 배치에서도 합산 중복을 만들지 않는다. */
function updateSessions(book,records) {
  const sheet=ensureSheet(book,'Sessions',SESSION_HEADERS);
  const rows=sheet.getLastRow()>1 ? sheet.getRange(2,1,sheet.getLastRow()-1,8).getValues() : [];
  const index=new Map(rows.map((row,i)=>[String(row[0]),{row:i+2,sequence:Number(row[7])||0}]));
  const pending=new Map();
  let nextRow=sheet.getLastRow()+1;
  records.forEach(r=>{
    if (!['stage_started','stage_summary','stage_ended'].includes(r.eventType)) return;
    let entry=pending.get(r.stageAttemptId)||index.get(r.stageAttemptId);
    if (entry && entry.sequence>=r.eventSequence) return;
    if (!entry) entry={row:nextRow++,sequence:0};
    const p=r.payload;
    const row=[r.stageAttemptId,r.sessionId,r.stageId,r.anonymousPlayerId,r.buildVersion,r.configVersion,
      p.startedKst,r.eventSequence,p.reason,p.realtimeSeconds,p.gameplaySeconds,p.activePlaySeconds,p.currentHealth]
      .concat(METRICS.map(k=>p[k]===undefined ? (r.stageId.startsWith('0-') || k==='reactionMeanSeconds'||k==='enemyHitRate' ? '' : 0) : p[k]),
        [JSON.stringify(p.patterns||{}),p.runId,p.exitStepIndex,p.exitObjectiveIndex,p.stepsCompleted,p.attempts,p.retryCount]);
    pending.set(r.stageAttemptId,{row:entry.row,sequence:r.eventSequence,values:row});
  });
  pending.forEach(entry=>writeRows(sheet,entry.row,[entry.values],SESSION_HEADERS.length));
}

/** 목표 완료·이탈 사건만 목표별 한 행으로 저장하며 배치 재시도는 step_key로 중복 제거한다. */
function updateOnboardingSteps(book,records,received) {
  const steps=records.filter(r=>r.eventType==='onboarding_step');
  if (!steps.length) return;
  const sheet=ensureSheet(book,'OnboardingSteps',STEP_HEADERS);
  const existing=new Set(sheet.getLastRow()>1
    ? sheet.getRange(2,1,sheet.getLastRow()-1,1).getValues().flat().map(String) : []);
  const rows=[];
  steps.forEach(r=>{
    const p=r.payload,key=r.sessionId+':'+p.stepIndex+':'+p.objectiveIndex;
    if (existing.has(key)) return;
    existing.add(key);
    rows.push(stepRow(r,key,received));
  });
  writeRows(sheet,sheet.getLastRow()+1,rows,STEP_HEADERS.length);
}

/** 온보딩 목표 사건을 반복 설명 없는 분석용 열로 펼친다. */
function stepRow(r,key,received) {
  const p=r.payload;
  return [key,received,p.runId,r.sessionId,r.stageId,r.stageAttemptId,p.stepIndex,p.objectiveIndex,
    p.retryCount,p.activeSeconds,p.firstInputSeconds,p.attempts,p.wrongAttempts,p.movementAttempts,
    p.startedInputs,p.rejectedInputs,p.bufferedInputs,p.droppedInputs,p.reason].map(safeCell);
}

/** name 탭의 열 용량을 준비하고 이전 헤더가 접두부이면 추가 열만 확장한다. */
function ensureSheet(book,name,headers) {
  const sheet=book.getSheetByName(name)||book.insertSheet(name);
  if (sheet.getMaxColumns()<headers.length) sheet.insertColumnsAfter(sheet.getMaxColumns(),headers.length-sheet.getMaxColumns());
  if (sheet.getLastRow()===0) {
    sheet.getRange(1,1,1,headers.length).setValues([headers]);
    sheet.setFrozenRows(1);
    if (name==='Events' || name==='Sessions' || name==='OnboardingSteps')
      sheet.getRange(2,name==='Events'?7:name==='Sessions'?3:5,sheet.getMaxRows()-1,1).setNumberFormat('@');
  }
  const current=sheet.getRange(1,1,1,headers.length).getValues()[0];
  const last=sheet.getLastColumn();
  if (last>headers.length || current.slice(0,last).some((value,i)=>value!==headers[i])) throw new Error('header_mismatch');
  if (last<headers.length) sheet.getRange(1,last+1,1,headers.length-last).setValues([headers.slice(last)]);
  return sheet;
}

/** rows를 start부터 기록하고 필요한 행을 확보한다. */
function writeRows(sheet,start,rows,width) {
  if (!rows.length) return;
  const last=start+rows.length-1;
  if (last>sheet.getMaxRows()) sheet.insertRowsAfter(sheet.getMaxRows(),last-sheet.getMaxRows());
  sheet.getRange(start,1,rows.length,width).setValues(rows.map(row=>row.map(safeCell)));
}
/** 값 없음은 빈 셀로, 수식처럼 시작하는 문자열은 안전한 텍스트로 반환한다. */
function safeCell(v) { return v===null||v===undefined ? '' : typeof v==='string'&&/^[=+\-@\t\r\n]/.test(v) ? "'"+v : v; }
/** date를 KST ISO 문자열로 반환한다. */
function formatKst(date) { return Utilities.formatDate(date,'Asia/Seoul',"yyyy-MM-dd'T'HH:mm:ss.SSSXXX"); }
/** 확인 응답을 JSON으로 반환한다. */
function jsonResponse(value) { return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON); }
