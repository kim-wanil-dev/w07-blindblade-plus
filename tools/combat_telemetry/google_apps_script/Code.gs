const EVENT_HEADERS = ['event_key','received_kst','schema_version','build_version','config_version',
  'session_id','stage_id','stage_attempt_id','anonymous_player_id','event_sequence','event_type',
  'occurred_at_kst','realtime_seconds','gameplay_seconds','input_id','action_id','attack_id','dodge_id',
  'kind','pattern_id','outcome','is_attempt','input_status','applied_damage','x','z','origin_x','origin_z',
  'aim_x','aim_z','player_x','player_z','duration_seconds','reaction_seconds','input_to_contact_seconds',
  'reaction_association','payload_json','run_id','step_index','objective_index','retry_count',
  'first_input_seconds','expected','defense_outcome','entry_mode','source_id','enemy_archetype','enemy_id',
  'spawn_ordinal','melee_probability','beast_probability','lifetime_seconds','reason','parent_pattern_id',
  'pattern_instance_id','attacker_actor','boss_actor','player_attack_pattern','health_after',
  'human_health_start','beast_health_start','human_health_end','beast_health_end','pattern_outcome',
  'required_kills','start_boss_immediately','arena_radius','arena_center_x','arena_center_z',
  'thrower_start_time','initial_throwers','throwers_per_minute','maximum_throwers','thrower_spawn_interval',
  'melee_start_time','melee_spawns_per_minute','melee_spawn_growth_per_minute','maximum_melee',
  'timed_wave_spawn_count','timed_wave_duration','timed_wave_interval','melee_spawn_limit',
  'weight_blend_start_spawn_count','weight_blend_end_spawn_count','melee_weight_at_start',
  'beast_weight_at_start','melee_weight_at_end','beast_weight_at_end','sound_awareness_enabled',
  'sound_awareness_distance','show_melee_ground_footprints','human_defeat_time','beast_defeat_time',
  'stage_clear_time','time_since_stage_start'];
const BASE_METRICS = ['deaths','kills','damageTaken','damageHitCount','dodgeAttempts','dodgeRejected','dodgeStarted',
  'guardAttempts','guardRejected','guardAttemptsWithContact','guardAttemptsWithoutContact','guardHitCount','guardGuardCount',
  'guardNormalParryCount','guardPerfectParryCount','attackAttempts','attackRejected','enemyAttackCount',
  'enemyResolvedCount','enemyHitCount','enemyDodgeCount','enemyNormalParryCount','enemyPerfectParryCount',
  'enemyGuardCount','enemyMissCount','enemyInvulnerableCount','enemyIgnoredCount','enemyDestroyedCount',
  'enemyCancelledCount','enemyInterruptedCount','reactionCount','reactionMeanSeconds','enemyHitRate',
  'serviceDroppedEventsTotal'];
const BASE_ATTACK_METRICS = [];
['ranged','melee','bossRanged','bossEscapeBarrage','bossCharge'].forEach(group =>
  ['Attempts','Resolved','Hits','Dodges','Parries','PerfectParries','Damage','HitRate','DamageShare'].forEach(metric => BASE_ATTACK_METRICS.push(group+metric)));
const NEW_METRICS = ['enemySpawnCount','enemyMeleeSpawnCount','enemyBeastSpawnCount','enemyThrowerSpawnCount',
  'enemyDefeatCount','enemyMeleeDefeatCount','enemyBeastDefeatCount','enemyThrowerDefeatCount',
  'enemyDespawnCount','enemyMeleeDespawnCount','enemyBeastDespawnCount','enemyThrowerDespawnCount',
  'beastSpawnShare','beastExpectedSpawnShare','twinBossHumanDamage','twinBossBeastDamage',
  'twinBossHumanDefeated','twinBossBeastDefeated','twinBossHumanDefeatTime','twinBossBeastDefeatTime',
  'twinBossPairDefeated','twinBossStageClearTime'];
['beastLeap','twinBossRanged','twinHumanSlash','twinHumanCounterSlash','twinHumanIai','twinBeastBite'].forEach(group =>
  ['Attempts','Resolved','Hits','Dodges','Parries','PerfectParries','Damage','HitRate','DamageShare'].forEach(metric => NEW_METRICS.push(group+metric)));
['Cooperation','IaiCharge','AlternatingMelee','Countering'].forEach(parent =>
  ['Started','Completed','Interrupted','Cancelled','MeanDurationSeconds'].forEach(metric => NEW_METRICS.push('bossPattern'+parent+metric)));
const METRICS = BASE_METRICS.concat(BASE_ATTACK_METRICS,NEW_METRICS);
const SESSION_HEADERS = ['stage_attempt_id','session_id','stage_id','anonymous_player_id','build_version',
  'config_version','started_kst','last_event_sequence','status','realtime_seconds','gameplay_seconds',
  'active_play_seconds','health'].concat(BASE_METRICS,BASE_ATTACK_METRICS,
    ['patterns_json','run_id','exit_step_index','exit_objective_index','steps_completed','attempts','retry_count'],
    NEW_METRICS,['entry_mode','schema_version']);
const STEP_HEADERS = ['step_key','received_kst','run_id','session_id','stage_id','stage_attempt_id',
  'step_index','objective_index','retry_count','active_seconds','first_input_seconds','attempts',
  'wrong_attempts','movement_attempts','started_inputs','rejected_inputs','buffered_inputs',
  'dropped_inputs','reason'];
const DASHBOARD_EVENT_HEADERS = ['event_key','received_kst','schema_version','build_version','config_version','session_id','stage_id',
  'stage_attempt_id','event_sequence','event_type','occurred_at_kst','attack_id','kind','pattern_id','outcome','is_attempt',
  'input_id','action_id','input_status','reaction_association','expected','defense_outcome','applied_damage','x','z','origin_x','origin_z',
  'duration_seconds','reaction_seconds','input_to_contact_seconds','run_id','step_index','objective_index','reason','enemy_archetype',
  'beast_probability','parent_pattern_id','attacker_actor','boss_actor',
  'pattern_outcome','arena_radius','arena_center_x','arena_center_z','time_since_stage_start'];
const DASHBOARD_SESSION_HEADERS = ['stage_attempt_id','session_id','stage_id','build_version','config_version','started_kst',
  'last_event_sequence','status','active_play_seconds','kills','deaths','damageTaken','damageHitCount','dodgeAttempts','dodgeRejected',
  'dodgeStarted','guardAttempts','guardRejected','guardAttemptsWithContact','guardAttemptsWithoutContact','guardHitCount',
  'guardGuardCount','guardNormalParryCount','guardPerfectParryCount','enemyResolvedCount','enemyHitCount','steps_completed','run_id',
  'exit_step_index','exit_objective_index','rangedDamage','meleeDamage','bossRangedDamage','bossEscapeBarrageDamage','bossChargeDamage',
  'enemySpawnCount','enemyMeleeSpawnCount','enemyBeastSpawnCount','enemyThrowerSpawnCount','enemyMeleeDefeatCount',
  'enemyBeastDefeatCount','enemyThrowerDefeatCount','enemyMeleeDespawnCount','enemyBeastDespawnCount','enemyThrowerDespawnCount',
  'beastLeapDamage','beastExpectedSpawnShare','twinBossHumanDamage','twinBossBeastDamage','twinBossHumanDefeated',
  'twinBossBeastDefeated','twinBossHumanDefeatTime','twinBossBeastDefeatTime','twinBossPairDefeated','bossPatternCooperationStarted',
  'bossPatternCooperationCompleted','bossPatternCooperationInterrupted','bossPatternCooperationCancelled',
  'bossPatternCooperationMeanDurationSeconds','bossPatternIaiChargeStarted','bossPatternIaiChargeCompleted',
  'bossPatternIaiChargeInterrupted','bossPatternIaiChargeCancelled','bossPatternIaiChargeMeanDurationSeconds',
  'bossPatternAlternatingMeleeStarted','bossPatternAlternatingMeleeCompleted','bossPatternAlternatingMeleeInterrupted',
  'bossPatternAlternatingMeleeCancelled','bossPatternAlternatingMeleeMeanDurationSeconds','bossPatternCounteringStarted',
  'bossPatternCounteringCompleted','bossPatternCounteringInterrupted','bossPatternCounteringCancelled',
  'bossPatternCounteringMeanDurationSeconds','entry_mode','schema_version'];
const DASHBOARD_STEP_HEADERS = ['received_kst','run_id','session_id','stage_id','stage_attempt_id','step_index','objective_index',
  'step_key','retry_count','active_seconds','first_input_seconds','attempts','wrong_attempts','movement_attempts',
  'started_inputs','rejected_inputs','buffered_inputs','dropped_inputs','reason'];
const DASHBOARD_EVENT_PAGE_SIZE = 2500;
const DASHBOARD_DATA_GENERATION_PROPERTY = 'DASHBOARD_DATA_GENERATION';
const UPLOAD_INDEX_SHEET = '_TelemetryUploadIndex';
const UPLOAD_INDEX_VERSION = 'telemetry-upload-index-v1';
const UPLOAD_INDEX_CHUNK_SIZE = 5000;
const UPLOAD_INDEX_REQUEST_BUDGET_MS = 10000;
const UPLOAD_INDEX_PREPARE_BUDGET_MS = 240000;
const EVENT_TYPES = new Set(['session_started','session_ended','stage_started','stage_summary','stage_ended',
  'movement_sample','player_input','player_died','dodge_changed','state_changed','reaction_candidate',
  'enemy_attack_started','enemy_attack_committed','enemy_attack_contact','enemy_attack_resolved',
  'stage_config','enemy_spawned','enemy_defeated','enemy_despawned','boss_pattern_started',
  'boss_pattern_ended','boss_damage_received','boss_actor_defeated','boss_pair_defeated',
  'onboarding_attempt','onboarding_step']);

/** 스프레드시트를 열 때 전투 로그 메뉴를 추가한다. */
function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('전투 로그')
    .addItem('업로드 인덱스 준비/이어 만들기','prepareTelemetryUploadIndex')
    .addItem('업로드 인덱스 다시 만들기','rebuildTelemetryUploadIndex')
    .addSeparator()
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
      const match=String(id).match(/:(0-[1-7]|1-1|1-2|2-1|2-2|unassigned)$/);
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
    const properties = PropertiesService.getScriptProperties();
    const generation = Number(properties.getProperty(DASHBOARD_DATA_GENERATION_PROPERTY) || '0');
    properties.setProperty(DASHBOARD_DATA_GENERATION_PROPERTY,String(Number.isSafeInteger(generation) ? generation+1 : 1));
    const book = SpreadsheetApp.openById(properties.getProperty('SPREADSHEET_ID'));
    [['Events',EVENT_HEADERS],['Sessions',SESSION_HEADERS],['OnboardingSteps',STEP_HEADERS]].forEach(([name,headers]) => {
      const sheet = book.getSheetByName(name) || book.insertSheet(name);
      sheet.clearContents();
      ensureSheet(book,name,headers);
    });
    resetUploadIndex(book,book.getSheetByName('Events'));
    SpreadsheetApp.flush();
  } finally { lock.releaseLock(); }
  ui.alert('초기화 완료', 'Events, Sessions, OnboardingSteps 데이터가 비워졌고 헤더는 유지했습니다.', ui.ButtonSet.OK);
}

/** 토큰과 스키마를 검증한 뒤 사건을 중복 없이 저장하고 저장된 키만 ACK한다. */
function doPost(e) {
  const received = formatKst(new Date());
  let batchId = '', lock, phase = 'validate';
  try {
    if (!e || !e.postData || e.postData.contents.length > 262144) throw new Error('invalid_request');
    let body;
    try { body = JSON.parse(e.postData.contents); }
    catch (error) { throw new Error('invalid_request'); }
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw new Error('invalid_request');
    const p = PropertiesService.getScriptProperties();
    if (body.action === 'dashboard_snapshot') return dashboardSnapshot(body, p);
    batchId = typeof body.batchId === 'string' ? body.batchId : '';
    const token = p.getProperty('UPLOAD_TOKEN');
    if (!token || token.length < 32 || body.token !== token) throw new Error('unauthorized');
    if (body.protocolVersion !== '1' || !batchId || batchId.length > 160 || !Array.isArray(body.records)
        || body.records.length < 1 || body.records.length > 100) throw new Error('invalid_batch');
    body.records.forEach(validateRecord);
    phase = 'lock';
    lock = LockService.getScriptLock();
    if (!lock.tryLock(10000)) throw new Error('busy');
    phase = 'upload_index';
    const book = SpreadsheetApp.openById(p.getProperty('SPREADSHEET_ID'));
    const sheet = ensureSheet(book,'Events',EVENT_HEADERS);
    const lastRow = sheet.getLastRow();
    const index = loadUploadIndex(book,sheet,lastRow);
    if (!advanceUploadIndex(index,sheet,lastRow,UPLOAD_INDEX_REQUEST_BUDGET_MS)) {
      SpreadsheetApp.flush();
      return jsonResponse({ok:false,batchId:batchId,error:'upload_index_rebuilding',acknowledgedEventIds:[]});
    }
    const rows = [], ack = [], batchKeys = new Set();
    body.records.forEach(r => {
      const key = r.sessionId + ':' + r.eventSequence;
      ack.push(key);
      if (batchKeys.has(key) || hasIndexedSequence(index.entries.get(r.sessionId),r.eventSequence)) return;
      batchKeys.add(key);
      rows.push(eventRow(r,key,received));
    });
    phase = 'events';
    writeRows(sheet,lastRow+1,rows,EVENT_HEADERS.length);
    phase = 'sessions';
    updateSessions(book,body.records);
    phase = 'onboarding_steps';
    updateOnboardingSteps(book,body.records,received);
    phase = 'flush';
    SpreadsheetApp.flush();
    return jsonResponse({ok:true,batchId:batchId,acknowledgedEventIds:ack});
  } catch (error) {
    const knownErrors = new Set(['invalid_request','unauthorized','invalid_batch','invalid_record','invalid_time',
      'invalid_id','oversize','busy','header_mismatch','upload_index_invalid','upload_index_invalid_event_key']);
    const code = knownErrors.has(error.message) ? error.message : 'rejected_or_retry';
    console.error('Telemetry upload failed. phase='+phase+'; '+String(error.stack || error));
    return jsonResponse({ok:false,batchId:batchId,error:code,acknowledgedEventIds:[]});
  } finally { if (lock && lock.hasLock()) lock.releaseLock(); }
}

/** 기존 Events를 보존하고 중복 확인 인덱스를 준비한다. 미완료 상태이면 다음 실행에서 이어 만든다. */
function prepareTelemetryUploadIndex() {
  return prepareUploadIndex(false);
}

/** 분석 원본은 보존하고 파생 인덱스만 초기화하여 기존 Events에서 다시 만든다. */
function rebuildTelemetryUploadIndex() {
  return prepareUploadIndex(true);
}

/** rebuild 여부에 따라 인덱스를 열거나 비운 뒤 제한 시간 안에서 준비하고 진행률을 반환한다. */
function prepareUploadIndex(rebuild) {
  const lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    const properties = PropertiesService.getScriptProperties();
    const book = SpreadsheetApp.openById(properties.getProperty('SPREADSHEET_ID'));
    const events = ensureSheet(book,'Events',EVENT_HEADERS);
    if (rebuild) resetUploadIndex(book,events);
    const lastRow = events.getLastRow();
    const index = loadUploadIndex(book,events,lastRow);
    const complete = advanceUploadIndex(index,events,lastRow,UPLOAD_INDEX_PREPARE_BUDGET_MS);
    SpreadsheetApp.flush();
    const result = {complete:complete,indexedEvents:index.indexedRow-1,totalEvents:lastRow-1};
    const message = complete ? '업로드 인덱스 준비 완료' : '시간 제한으로 중단했습니다. 준비/이어 만들기를 다시 실행하세요.';
    console.log(message+' '+JSON.stringify(result));
    book.toast(message+' ('+result.indexedEvents+'/'+result.totalEvents+')','전투 로그',10);
    return result;
  } finally { lock.releaseLock(); }
}

/** Events의 식별자를 기준으로 파생 인덱스의 메타데이터와 빈 헤더를 생성하고 숨김 시트를 반환한다. */
function resetUploadIndex(book,events) {
  const sheet = book.getSheetByName(UPLOAD_INDEX_SHEET) || book.insertSheet(UPLOAD_INDEX_SHEET);
  sheet.clearContents();
  writeRows(sheet,1,[[UPLOAD_INDEX_VERSION,events.getSheetId(),1],['session_id','sequence_ranges_json','']],3);
  sheet.hideSheet();
  return sheet;
}

/** 세션별 정확한 사건 순번 구간과 마지막 색인 행을 읽는다. 원본 교체·축소 시에는 파생 상태만 다시 준비한다. */
function loadUploadIndex(book,events,lastRow) {
  let sheet = book.getSheetByName(UPLOAD_INDEX_SHEET);
  if (!sheet || sheet.getLastRow() < 2) sheet = resetUploadIndex(book,events);
  let values = sheet.getRange(1,1,sheet.getLastRow(),3).getValues();
  if (values[0][0] !== UPLOAD_INDEX_VERSION || values[1][0] !== 'session_id'
      || values[1][1] !== 'sequence_ranges_json') throw new Error('upload_index_invalid');
  let indexedRow = Number(values[0][2]);
  if (!Number.isSafeInteger(indexedRow) || indexedRow < 1) throw new Error('upload_index_invalid');
  if (Number(values[0][1]) !== events.getSheetId() || indexedRow > lastRow) {
    sheet = resetUploadIndex(book,events);
    values = [[UPLOAD_INDEX_VERSION,events.getSheetId(),1],['session_id','sequence_ranges_json','']];
    indexedRow = 1;
  }
  const entries = new Map();
  values.slice(2).forEach((row,i) => {
    const sessionId = String(row[0]);
    if (!/^[0-9a-f]{32}$/.test(sessionId) || entries.has(sessionId)) throw new Error('upload_index_invalid');
    let ranges;
    try { ranges = JSON.parse(String(row[1])); }
    catch (error) { throw new Error('upload_index_invalid'); }
    if (!Array.isArray(ranges) || ranges.some((range,j) => !Array.isArray(range) || range.length !== 2
        || !Number.isSafeInteger(range[0]) || !Number.isSafeInteger(range[1]) || range[0] < 1
        || range[1] < range[0] || (j > 0 && range[0]-1 <= ranges[j-1][1])))
      throw new Error('upload_index_invalid');
    entries.set(sessionId,{row:i+3,ranges:ranges});
  });
  return {sheet:sheet,entries:entries,indexedRow:indexedRow,savedRow:indexedRow,
    nextRow:values.length+1,dirty:new Set()};
}

/** checkpoint 뒤에 실제 저장된 Events 키만 구간별로 추가한다. 준비 중 시간 제한이면 진행 위치를 저장하고 false를 반환한다. */
function advanceUploadIndex(index,events,lastRow,budgetMs) {
  const deadline = Date.now()+budgetMs;
  while (index.indexedRow < lastRow && Date.now() < deadline) {
    const count = Math.min(UPLOAD_INDEX_CHUNK_SIZE,lastRow-index.indexedRow);
    const keys = events.getRange(index.indexedRow+1,1,count,1).getValues();
    const sequences = new Map();
    keys.forEach(([value],offset) => {
      if (value === '') return;
      const match = String(value).match(/^([0-9a-f]{32}):([1-9]\d*)$/);
      if (!match || !Number.isSafeInteger(Number(match[2]))) {
        console.error('Invalid event_key at Events!A'+(index.indexedRow+offset+1));
        throw new Error('upload_index_invalid_event_key');
      }
      if (!sequences.has(match[1])) sequences.set(match[1],[]);
      sequences.get(match[1]).push(Number(match[2]));
    });
    sequences.forEach((numbers,sessionId) => {
      let entry = index.entries.get(sessionId);
      if (!entry) {
        entry = {row:index.nextRow++,ranges:[]};
        index.entries.set(sessionId,entry);
      }
      entry.ranges = mergeSequenceRanges(entry.ranges,numbers);
      index.dirty.add(sessionId);
    });
    index.indexedRow += count;
  }
  saveUploadIndex(index);
  return index.indexedRow === lastRow;
}

/** 기존 구간과 실제 확인된 순번을 합쳐 빈 순번을 보존한 정렬 구간 배열을 반환한다. */
function mergeSequenceRanges(ranges,sequences) {
  const ordered = ranges.concat(sequences.map(sequence => [sequence,sequence]))
    .sort((left,right) => left[0]-right[0]);
  const merged = [];
  ordered.forEach(range => {
    const previous = merged[merged.length-1];
    if (previous && range[0]-1 <= previous[1]) previous[1] = Math.max(previous[1],range[1]);
    else merged.push([range[0],range[1]]);
  });
  return merged;
}

/** 세션의 색인 구간에서 sequence가 실제 저장된 순번인지 이진 검색으로 반환한다. */
function hasIndexedSequence(entry,sequence) {
  if (!entry) return false;
  let left = 0, right = entry.ranges.length-1;
  while (left <= right) {
    const middle = Math.floor((left+right)/2), range = entry.ranges[middle];
    if (sequence < range[0]) right = middle-1;
    else if (sequence > range[1]) left = middle+1;
    else return true;
  }
  return false;
}

/** 변경된 색인 구간을 먼저 확정한 다음 checkpoint를 쓴다. 현재 업로드의 새 Events는 다음 요청에서 색인한다. */
function saveUploadIndex(index) {
  if (index.indexedRow === index.savedRow) return;
  const changes = Array.from(index.dirty,sessionId => {
    const entry = index.entries.get(sessionId);
    return {row:entry.row,values:[sessionId,JSON.stringify(entry.ranges)]};
  });
  writeRowGroups(index.sheet,changes,2);
  SpreadsheetApp.flush();
  index.sheet.getRange(1,3).setValue(index.indexedRow);
  index.savedRow = index.indexedRow;
  index.dirty.clear();
}

/** 변경 행을 번호순으로 묶어 연속 행마다 한 번씩 기록한다. rows의 값과 width를 사용해 해당 시트 행을 변경한다. */
function writeRowGroups(sheet,rows,width) {
  const ordered = rows.slice().sort((left,right) => left.row-right.row);
  let start = 0, group = [];
  ordered.forEach(entry => {
    if (group.length && entry.row !== start+group.length) {
      writeRows(sheet,start,group,width);
      group = [];
    }
    if (!group.length) start = entry.row;
    group.push(entry.values);
  });
  writeRows(sheet,start,group,width);
}

/** 대시보드용 별도 읽기 토큰을 확인하고 분석에 필요한 열만 반환한다. */
function dashboardSnapshot(body, properties) {
  const token = properties.getProperty('DASHBOARD_READ_TOKEN');
  if (!token || token.length < 32 || body.token !== token) return jsonResponse({ok:false,error:'unauthorized'});
  const spreadsheetId = properties.getProperty('SPREADSHEET_ID');
  if (!spreadsheetId) return jsonResponse({ok:false,error:'not_configured'});
  const book = SpreadsheetApp.openById(spreadsheetId);
  const dataset = String(body.dataset || 'all');
  const lock = LockService.getScriptLock();
  if (!lock.tryLock(5000)) return jsonResponse({ok:false,error:'dashboard_busy'});
  try {
    const generatedAtKst = formatKst(new Date());
    const generation = dashboardDataGeneration(properties);
    if (dataset === 'core') {
      const eventsSheet = book.getSheetByName('Events');
      const eventsRowLimit = eventsSheet ? Math.max(0,eventsSheet.getLastRow()-1) : 0;
      return jsonResponse({
        ok:true,
        generatedAtKst:generatedAtKst,
        snapshot:{eventsGeneration:generation,eventsRowLimit:eventsRowLimit},
        sheets:{
          Sessions:dashboardRows(book.getSheetByName('Sessions'),DASHBOARD_SESSION_HEADERS),
          OnboardingSteps:dashboardRows(book.getSheetByName('OnboardingSteps'),DASHBOARD_STEP_HEADERS)
        }
      });
    }
    if (dataset === 'events') {
      const offset = Number(body.offset ?? 0);
      const limit = Number(body.limit ?? DASHBOARD_EVENT_PAGE_SIZE);
      const hasGeneration = body.snapshotGeneration !== undefined && body.snapshotGeneration !== null && body.snapshotGeneration !== '';
      const hasRowLimit = body.snapshotRowLimit !== undefined && body.snapshotRowLimit !== null && body.snapshotRowLimit !== '';
      if (!Number.isSafeInteger(offset) || offset < 0 || !Number.isSafeInteger(limit)
          || limit < 1 || limit > DASHBOARD_EVENT_PAGE_SIZE || hasGeneration !== hasRowLimit)
        return jsonResponse({ok:false,error:'invalid_page'});
      let rowLimit = null;
      if (hasGeneration) {
        const requestedGeneration = Number(body.snapshotGeneration);
        rowLimit = Number(body.snapshotRowLimit);
        if (!Number.isSafeInteger(requestedGeneration) || requestedGeneration < 0
            || !Number.isSafeInteger(rowLimit) || rowLimit < 0)
          return jsonResponse({ok:false,error:'invalid_snapshot'});
        if (requestedGeneration !== generation) return jsonResponse({ok:false,error:'snapshot_changed'});
      }
      const eventsSheet = book.getSheetByName('Events');
      const availableRows = eventsSheet ? Math.max(0,eventsSheet.getLastRow()-1) : 0;
      if (rowLimit !== null && availableRows < rowLimit) return jsonResponse({ok:false,error:'snapshot_changed'});
      if (rowLimit === null) rowLimit = availableRows;
      const page = dashboardRowsPage(eventsSheet,DASHBOARD_EVENT_HEADERS,offset,limit,rowLimit);
      return jsonResponse({
        ok:true,
        generatedAtKst:generatedAtKst,
        snapshot:{eventsGeneration:generation,eventsRowLimit:rowLimit},
        sheets:{Events:page.rows},
        pagination:page.pagination
      });
    }
    if (dataset !== 'all') return jsonResponse({ok:false,error:'invalid_dataset'});
    return jsonResponse({
      ok:true,
      generatedAtKst:generatedAtKst,
      snapshot:{eventsGeneration:generation,eventsRowLimit:Math.max(0,(book.getSheetByName('Events')?.getLastRow()||1)-1)},
      sheets:{
        Events:dashboardRows(book.getSheetByName('Events'),DASHBOARD_EVENT_HEADERS),
        Sessions:dashboardRows(book.getSheetByName('Sessions'),DASHBOARD_SESSION_HEADERS),
        OnboardingSteps:dashboardRows(book.getSheetByName('OnboardingSteps'),DASHBOARD_STEP_HEADERS)
      }
    });
  } finally {
    lock.releaseLock();
  }
}

/** 초기화 세대를 숫자로 반환하며 아직 초기화 이력이 없는 시트는 0세대로 처리한다. */
function dashboardDataGeneration(properties) {
  const generation = Number(properties.getProperty(DASHBOARD_DATA_GENERATION_PROPERTY) || '0');
  return Number.isSafeInteger(generation) && generation >= 0 ? generation : 0;
}

/** 지정한 분석 열만 객체 행으로 만들어 시트의 불필요한 열 전송을 막는다. */
function dashboardRows(sheet, allowedHeaders) {
  return dashboardRowsPage(sheet,allowedHeaders,0,null).rows;
}

/** 지정한 행 구간만 객체로 만들어 페이지 메타데이터와 함께 반환한다. */
function dashboardRowsPage(sheet, allowedHeaders, offset, limit, rowLimit = null) {
  if (!sheet || sheet.getLastRow() < 2 || sheet.getLastColumn() < 1)
    return {rows:[],pagination:{offset:0,limit:0,totalRows:0,nextOffset:0}};
  const lastRow = sheet.getLastRow();
  const lastColumn = sheet.getLastColumn();
  const headers = sheet.getRange(1,1,1,lastColumn).getDisplayValues()[0].map(value => String(value).trim());
  const columns = allowedHeaders.map(name => ({name,index:headers.indexOf(name)}))
    .filter(item => item.index >= 0).sort((left,right) => left.index-right.index);
  const totalRows = Math.min(lastRow-1,rowLimit ?? lastRow-1);
  const startOffset = Math.min(offset,totalRows);
  const rowCount = Math.min(limit ?? totalRows,totalRows-startOffset);
  if (rowCount < 1) return {rows:[],pagination:{offset:startOffset,limit:0,totalRows:totalRows,nextOffset:startOffset}};
  if (columns.length < 1) return {rows:[],pagination:{offset:startOffset,limit:rowCount,totalRows:totalRows,nextOffset:startOffset+rowCount}};
  const firstColumnIndex = columns[0].index;
  const lastColumnIndex = columns[columns.length-1].index;
  const values = sheet.getRange(2+startOffset,firstColumnIndex+1,rowCount,lastColumnIndex-firstColumnIndex+1).getValues();
  const records = values.map(row => {
    const record = {};
    columns.forEach(column => {
      const rawValue = row[column.index-firstColumnIndex];
      const value = rawValue instanceof Date ? formatKst(rawValue) : rawValue;
      if (value !== '') record[column.name]=value;
    });
    return record;
  });
  return {
    rows:records.filter(record => Object.keys(record).length > 0),
    pagination:{offset:startOffset,limit:rowCount,totalRows:totalRows,nextOffset:startOffset+rowCount}
  };
}

/** record의 버전·도전 식별자·시계·종류를 검사하여 잘못된 배치를 거부한다. */
function validateRecord(r) {
  if (!r || !['0.3.0','0.4.0'].includes(r.schemaVersion) || r.contractVersion !== '0.2.0'
      || !/^[0-9a-f]{32}$/.test(r.sessionId) || !/^[0-9a-f]{32}$/.test(r.anonymousPlayerId)
      || !['0-1','0-2','0-3','0-4','0-5','0-6','0-7','1-1','1-2','2-1','2-2','unassigned'].includes(r.stageId) || r.stageAttemptId !== r.sessionId + ':' + r.stageId
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
  const arena=p.arenaCenter||{};
  return [key,received,r.schemaVersion,r.buildVersion,r.configVersion,r.sessionId,r.stageId,r.stageAttemptId,
    r.anonymousPlayerId,r.eventSequence,r.eventType,r.occurredAtKst,t.realtimeSeconds,t.gameplaySeconds,
    c.inputId,c.actionId,c.attackId,c.dodgeId,p.kind,p.patternId,p.outcome,p.isAttempt,p.status,p.appliedDamage,
    pos.x,pos.z,o.x,o.z,a.x,a.z,player.x,player.z,p.durationSeconds,p.reactionSeconds,p.inputToContactSeconds,
    p.reactionAssociation||p.association,JSON.stringify(p),r.runId||p.runId,p.stepIndex,p.objectiveIndex,
    p.retryCount,p.firstInputSeconds,p.expected,p.defenseOutcome,r.entryMode||p.entryMode,r.sourceId,
    p.enemyArchetype,p.enemyId,p.spawnOrdinal,p.meleeProbability,p.beastProbability,p.lifetimeSeconds,p.reason,
    p.parentPatternId,p.patternInstanceId,p.attackerActor,p.bossActor,p.playerAttackPattern,p.healthAfter,
    p.humanHealthStart,p.beastHealthStart,p.humanHealthEnd,p.beastHealthEnd,p.patternOutcome,p.requiredKills,
    p.startBossImmediately,p.arenaRadius,arena.x,arena.z,p.throwerStartTime,p.initialThrowers,
    p.throwersPerMinute,p.maximumThrowers,p.throwerSpawnInterval,p.meleeStartTime,p.meleeSpawnsPerMinute,
    p.meleeSpawnGrowthPerMinute,p.maximumMelee,p.timedWaveSpawnCount,p.timedWaveDuration,
    p.timedWaveInterval,p.meleeSpawnLimit,p.weightBlendStartSpawnCount,p.weightBlendEndSpawnCount,
    p.meleeWeightAtStart,p.beastWeightAtStart,p.meleeWeightAtEnd,p.beastWeightAtEnd,
    p.soundAwarenessEnabled,p.soundAwarenessDistance,p.showMeleeGroundFootprints,p.humanDefeatTime,
    p.beastDefeatTime,p.stageClearTime,p.timeSinceStageStart].map(safeCell);
}

/** 최신 도전별 누적 스냅샷의 변경 행만 덮어쓴다. 시트의 기존 열 순서를 이름으로 매핑해 유지한다. */
function updateSessions(book,records) {
  const snapshots=records.filter(r=>['stage_started','stage_summary','stage_ended'].includes(r.eventType));
  if (!snapshots.length) return;
  const sheet=ensureSheet(book,'Sessions',SESSION_HEADERS);
  const lastRow=sheet.getLastRow();
  const rows=lastRow>1 ? sheet.getRange(2,1,lastRow-1,8).getValues() : [];
  const index=new Map(rows.map((row,i)=>[String(row[0]),{row:i+2,sequence:Number(row[7])||0}]));
  const pending=new Map();
  let nextRow=lastRow+1;
  snapshots.forEach(r=>{
    let entry=pending.get(r.stageAttemptId)||index.get(r.stageAttemptId);
    if (entry && entry.sequence>=r.eventSequence) return;
    if (!entry) entry={row:nextRow++,sequence:0};
    const p=r.payload;
    const row=[r.stageAttemptId,r.sessionId,r.stageId,r.anonymousPlayerId,r.buildVersion,r.configVersion,
      p.startedKst,r.eventSequence,p.reason,p.realtimeSeconds,p.gameplaySeconds,p.activePlaySeconds,p.currentHealth]
      .concat(BASE_METRICS.map(k=>p[k]===undefined ? (r.stageId.startsWith('0-') || k==='reactionMeanSeconds'||k==='enemyHitRate' ? '' : 0) : p[k]),
        BASE_ATTACK_METRICS.map(k=>p[k]===undefined ? (r.stageId.startsWith('0-') || k.endsWith('HitRate') ? '' : 0) : p[k]),
        [JSON.stringify(p.patterns||{}),r.runId||p.runId,p.exitStepIndex,p.exitObjectiveIndex,p.stepsCompleted,p.attempts,p.retryCount],
        NEW_METRICS.map(k=>r.schemaVersion!=='0.4.0' || p[k]===undefined ? '' : p[k]),
        [r.entryMode||p.entryMode,r.schemaVersion]);
    pending.set(r.stageAttemptId,{row:entry.row,sequence:r.eventSequence,values:row});
  });
  if (!pending.size) return;
  const sheetHeaders=sheet.getRange(1,1,1,sheet.getLastColumn()).getValues()[0];
  const changes=Array.from(pending.values(),entry=>{
    const valuesByHeader=new Map(SESSION_HEADERS.map((header,index)=>[header,entry.values[index]]));
    const values=sheetHeaders.map(header=>valuesByHeader.get(header));
    return {row:entry.row,values:values};
  });
  writeRowGroups(sheet,changes,sheetHeaders.length);
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

/** name 탭의 열 용량을 준비한다. Sessions는 기존 열 순서를 보존하며 빠진 열만 뒤에 추가한다. */
function ensureSheet(book,name,headers) {
  const sheet=book.getSheetByName(name)||book.insertSheet(name);
  if (sheet.getMaxColumns()<headers.length) sheet.insertColumnsAfter(sheet.getMaxColumns(),headers.length-sheet.getMaxColumns());
  if (sheet.getLastRow()===0) {
    sheet.getRange(1,1,1,headers.length).setValues([headers]);
    sheet.setFrozenRows(1);
    if (name==='Events' || name==='Sessions' || name==='OnboardingSteps')
      sheet.getRange(2,name==='Events'?7:name==='Sessions'?3:5,sheet.getMaxRows()-1,1).setNumberFormat('@');
  }
  const last=sheet.getLastColumn();
  if (name==='Sessions' && last>0) {
    const current=sheet.getRange(1,1,1,last).getValues()[0];
    const expected=new Set(headers);
    const existing=new Set();
    if (current.some(value=>!expected.has(value)||existing.has(value))) throw new Error('header_mismatch');
    current.forEach(value=>existing.add(value));
    const missing=headers.filter(value=>!existing.has(value));
    if (last+missing.length>sheet.getMaxColumns())
      sheet.insertColumnsAfter(sheet.getMaxColumns(),last+missing.length-sheet.getMaxColumns());
    if (missing.length) sheet.getRange(1,last+1,1,missing.length).setValues([missing]);
    return sheet;
  }
  const current=sheet.getRange(1,1,1,headers.length).getValues()[0];
  if (last>headers.length || current.slice(0,last).some((value,i)=>value!==headers[i])) throw new Error('header_mismatch');
  if (last<headers.length) sheet.getRange(1,last+1,1,headers.length-last).setValues([headers.slice(last)]);
  return sheet;
}

/** rows를 start부터 기록하고 필요한 행을 확보한다. */
function writeRows(sheet,start,rows,width) {
  if (!rows.length) return;
  const last=start+rows.length-1, capacity=sheet.getMaxRows();
  if (last>capacity) sheet.insertRowsAfter(capacity,last-capacity);
  sheet.getRange(start,1,rows.length,width).setValues(rows.map(row=>row.map(safeCell)));
}
/** 값 없음은 빈 셀로, 수식처럼 시작하는 문자열은 안전한 텍스트로 반환한다. */
function safeCell(v) { return v===null||v===undefined ? '' : typeof v==='string'&&/^[=+\-@\t\r\n]/.test(v) ? "'"+v : v; }
/** date를 KST ISO 문자열로 반환한다. */
function formatKst(date) { return Utilities.formatDate(date,'Asia/Seoul',"yyyy-MM-dd'T'HH:mm:ss.SSSXXX"); }
/** 확인 응답을 JSON으로 반환한다. */
function jsonResponse(value) { return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON); }
