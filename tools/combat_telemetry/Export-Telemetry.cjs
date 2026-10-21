// Local preprocessing uses the same column mapping as the Google Sheets receiver.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const [source, destination] = process.argv.slice(2);
if (!source || !destination) throw new Error('Usage: node Export-Telemetry.cjs <log directory> <output directory>');
if (path.resolve(source) === path.resolve(destination)) throw new Error('Use a separate output directory.');
const code = fs.readFileSync(path.join(__dirname, 'google_apps_script/Code.gs'), 'utf8');
const mapping = vm.runInNewContext(code + '\n({eventRow,stepRow,EVENT_HEADERS,SESSION_HEADERS,STEP_HEADERS,BASE_METRICS,BASE_ATTACK_METRICS,NEW_METRICS,V05_METRICS,METRICS,validateRecord})');
const events=[], sessions=new Map(), terminalFacts=new Map(), statusFacts=new Map(), deathFacts=new Map(), steps=new Map(), seen=new Set(), heat=new Map(), diagnostics=[];
const lifecycle=new Map(), arenaCenters=new Map();
const statusRank=status=>['death','final_clear','cleared'].includes(String(status||''))?3
  :['scene_unloaded','application_quit','unclean_exit'].includes(String(status||''))?2
  :['in_progress','started'].includes(String(status||''))?1:0;
const recordStatus=(r,status)=>{
  const previous=statusFacts.get(r.stageAttemptId);
  if (!previous || statusRank(status)>statusRank(previous.status)
      || (statusRank(status)===statusRank(previous.status)&&r.eventSequence>previous.eventSequence))
    statusFacts.set(r.stageAttemptId,{status,eventSequence:r.eventSequence});
};
const ensureTerminalSummary=r=>{
  if (sessions.has(r.stageAttemptId)) return;
  sessions.set(r.stageAttemptId,{...r,eventSequence:0,eventType:'stage_ended',terminalOnly:true,
    payload:{reason:'in_progress'}});
};
for (const file of fs.readdirSync(source).filter(f=>f.endsWith('.jsonl')).sort()) {
  let lineNumber=0;
  for (const line of fs.readFileSync(path.join(source,file),'utf8').split(/\r?\n/)) {
    lineNumber++;
    if (!line.trim()) continue;
    try {
      const r=JSON.parse(line); mapping.validateRecord(r);
      const key=r.sessionId+':'+r.eventSequence;
      if (seen.has(key)) continue;
      seen.add(key);
      events.push(mapping.eventRow(r,key,''));
      const p=r.payload;
      if (r.eventType==='stage_config') arenaCenters.set(r.stageAttemptId,p.arenaCenter||{x:0,z:0});
      if (['enemy_spawned','enemy_defeated','enemy_despawned'].includes(r.eventType) && p.enemyId) {
        const row=lifecycle.get(p.enemyId)||{
          sessionId:r.sessionId,stageId:r.stageId,stageAttemptId:r.stageAttemptId,enemyId:p.enemyId,
          archetype:p.enemyArchetype||'',spawnOrdinal:'',meleeProbability:'',beastProbability:'',
          status:'spawn_record_missing',spawnX:'',spawnZ:'',endX:'',endZ:'',lifetimeSeconds:'',reason:''
        };
        if (r.eventType==='enemy_spawned') Object.assign(row,{archetype:p.enemyArchetype||'',spawnOrdinal:p.spawnOrdinal,
          meleeProbability:p.meleeProbability,beastProbability:p.beastProbability,status:'spawned',
          spawnX:p.position?.x??'',spawnZ:p.position?.z??''});
        else Object.assign(row,{status:r.eventType==='enemy_defeated'?'defeated':'despawned',
          lifetimeSeconds:p.lifetimeSeconds??'',reason:p.reason||'',endX:p.position?.x??'',endZ:p.position?.z??''});
        lifecycle.set(p.enemyId,row);
      }
      if (['stage_started','stage_summary','stage_ended'].includes(r.eventType)) {
        const previous=sessions.get(r.stageAttemptId);
        if (!previous || previous.eventSequence<r.eventSequence) sessions.set(r.stageAttemptId,r);
        deathFacts.set(r.stageAttemptId,Math.max(deathFacts.get(r.stageAttemptId)||0,Number(p.deaths)||0));
        recordStatus(r,p.reason||'');
      }
      if (r.eventType==='player_died') {
        ensureTerminalSummary(r);
        deathFacts.set(r.stageAttemptId,Math.max(deathFacts.get(r.stageAttemptId)||0,1));
        recordStatus(r,'death');
      }
      if (r.eventType==='session_ended') {
        ensureTerminalSummary(r);
        recordStatus(r,String(p.reason||'application_quit').toLowerCase());
      }
      if (r.eventType==='session_ended'
          && ['application_quit','scene_unloaded','unclean_exit'].includes(String(p.reason||'').toLowerCase())) {
        const facts=terminalFacts.get(r.stageAttemptId)||{};
        if(!facts.exit||facts.exit.eventSequence<r.eventSequence) facts.exit=r;
        terminalFacts.set(r.stageAttemptId,facts);
      }
      if (r.eventType==='game_completed') {
        ensureTerminalSummary(r);
        recordStatus(r,'final_clear');
        const facts=terminalFacts.get(r.stageAttemptId)||{};
        if(!facts.completion||facts.completion.eventSequence<r.eventSequence) facts.completion=r;
        terminalFacts.set(r.stageAttemptId,facts);
      }
      if (r.eventType==='onboarding_step') {
        const stepKey=r.sessionId+':'+p.stepIndex+':'+p.objectiveIndex;
        if (!steps.has(stepKey)) steps.set(stepKey,mapping.stepRow(r,stepKey,''));
      }
      let layer='', weight=1;
      if (r.eventType==='movement_sample') { layer='movement'; weight=p.durationSeconds; }
      if (r.eventType==='player_input' && p.kind==='Attack' && p.isAttempt) layer='attack_attempt';
      if (r.eventType==='enemy_attack_resolved' && p.appliedDamage>0) layer='damage_hit';
      if (layer && p.position && Number.isFinite(p.position.x) && Number.isFinite(p.position.z)) {
        const center=arenaCenters.get(r.stageAttemptId)||{x:0,z:0};
        const x=Math.floor((p.position.x-(Number(center.x)||0))/2)*2;
        const z=Math.floor((p.position.z-(Number(center.z)||0))/2)*2;
        const key=[r.stageAttemptId,layer,x,z].join('|');
        const row=heat.get(key)||[r.sessionId,r.stageId,r.stageAttemptId,layer,x,z,0];
        row[6]+=weight; heat.set(key,row);
      }
    } catch (error) { diagnostics.push([file,lineNumber,error.message]); }
  }
}
for (const [attemptId,summary] of sessions) {
  const facts=terminalFacts.get(attemptId)||{};
  const payload={...summary.payload};
  const statusFact=statusFacts.get(attemptId);
  payload.deaths=Math.max(Number(payload.deaths)||0,deathFacts.get(attemptId)||0);
  let lastEventSequence=summary.eventSequence;
  let lastExitSequence=payload.exitOccurredAtKst||payload.exitStageAttemptId||payload.exitLocation?summary.eventSequence:0;
  let lastCompletionSequence=payload.gameCompleted===true?summary.eventSequence:0;
  if (facts.exit) {
    const r=facts.exit, p=r.payload;
    lastEventSequence=Math.max(lastEventSequence,r.eventSequence);
    lastExitSequence=Math.max(lastExitSequence,r.eventSequence);
    if (r.eventSequence>summary.eventSequence || !(payload.exitOccurredAtKst||payload.exitStageAttemptId||payload.exitLocation)) {
      payload.exitStageId=p.exitStageId||r.stageId;
      payload.exitStageAttemptId=p.exitStageAttemptId||r.stageAttemptId;
      payload.exitLocation=p.exitLocation||r.stageId;
      if (p.exitOccurredAtKst) payload.exitOccurredAtKst=p.exitOccurredAtKst;
      else if (['application_quit','scene_unloaded'].includes(String(p.reason||'').toLowerCase()))
        payload.exitOccurredAtKst=r.occurredAtKst;
    }
  }
  if (facts.completion) {
    const r=facts.completion, p=r.payload;
    lastEventSequence=Math.max(lastEventSequence,r.eventSequence);
    lastCompletionSequence=Math.max(lastCompletionSequence,r.eventSequence);
    if (r.eventSequence>summary.eventSequence || payload.gameCompleted!==true) {
      payload.gameCompleted=true;
      payload.gameCompletedOccurredAtKst=p.gameCompletedOccurredAtKst||r.occurredAtKst;
    }
  }
  if (statusFact) {
    payload.reason=statusFact.status;
    lastEventSequence=Math.max(lastEventSequence,statusFact.eventSequence);
  }
  sessions.set(attemptId,{...summary,payload,lastEventSequence,lastExitSequence,lastCompletionSequence,
    lastStatusSequence:statusFact?.eventSequence||summary.eventSequence});
}
fs.mkdirSync(destination,{recursive:true});
function csv(name,headers,rows) {
  const cell=v=>'"'+String(v??'').replaceAll('"','""')+'"';
  fs.writeFileSync(path.join(destination,name),'\ufeff'+[headers,...rows].map(row=>row.map(cell).join(',')).join('\r\n'),'utf8');
}
const sessionRows=[], patterns=[], bossPatterns=[];
for (const r of sessions.values()) {
  const p=r.payload;
  sessionRows.push([r.stageAttemptId,r.sessionId,r.stageId,r.anonymousPlayerId,r.buildVersion,r.configVersion,
    p.startedKst,r.lastEventSequence,p.reason,p.realtimeSeconds,p.gameplaySeconds,p.activePlaySeconds,p.currentHealth,
    ...mapping.BASE_METRICS.map(k=>p[k]??(r.terminalOnly||r.stageId.startsWith('0-')||k==='reactionMeanSeconds'||k==='enemyHitRate'?'':0)),
    ...mapping.BASE_ATTACK_METRICS.map(k=>p[k]??(r.terminalOnly||r.stageId.startsWith('0-')||k.endsWith('HitRate')?'':0)),
    JSON.stringify(p.patterns||{}),r.runId||p.runId,p.exitStepIndex,p.exitObjectiveIndex,p.stepsCompleted,p.attempts,p.retryCount,
    ...mapping.NEW_METRICS.map(k=>mapping.V05_METRICS.includes(k)
      ?(r.schemaVersion!=='0.5.0'?'':(p[k]??'')):(r.schemaVersion==='0.3.0'?'':(p[k]??''))),
    r.entryMode||p.entryMode,r.schemaVersion,r.attemptSessionId||'',r.retryIndex??'',r.previousAttemptSessionId||'',
    p.exitStageId||'',p.exitStageAttemptId||'',p.exitLocation||'',p.gameCompleted===true,
    p.exitOccurredAtKst||'',p.gameCompletedOccurredAtKst||'',r.eventSequence,r.lastExitSequence,
    r.lastCompletionSequence,r.lastStatusSequence]);
  for (const v of Object.values(p.patterns||{})) {
    const ratio=(a,b)=>b>0?(a||0)/b:'';
    patterns.push([r.stageAttemptId,r.stageId,v.kind,v.patternId,v.parentPatternId||'',v.attackerActor||'',v.attempts||0,v.resolved||0,v.Hit||0,v.Dodge||0,
      v.NormalParry||0,v.PerfectParry||0,v.Guard||0,v.Miss||0,v.damage||0,v.damageHits||0,
      ratio(v.Hit,v.resolved),ratio(v.Dodge,v.resolved),ratio(v.NormalParry,v.resolved),ratio(v.PerfectParry,v.resolved),
      ratio(v.damage,p.damageTaken),ratio(v.damageHits,p.damageHitCount)]);
  }
  for (const v of Object.values(p.bossPatterns||{}))
    bossPatterns.push([r.stageAttemptId,r.stageId,v.parentPatternId||'',v.started||0,v.completed||0,v.interrupted||0,
      v.cancelled||0,v.durationTotalSeconds||0,(v.ended||0)>0?(v.durationTotalSeconds||0)/v.ended:'']);
}
csv('Events.csv',mapping.EVENT_HEADERS,events);
csv('Sessions.csv',mapping.SESSION_HEADERS,sessionRows);
csv('OnboardingSteps.csv',mapping.STEP_HEADERS,[...steps.values()]);
csv('Heatmaps.csv',['session_id','stage_id','stage_attempt_id','layer','grid_x','grid_z','weight'],[...heat.values()]);
csv('Patterns.csv',['stage_attempt_id','stage_id','kind','pattern_id','parent_pattern_id','attacker_actor','attempts','resolved','hit','dodge','parry','perfect_parry','guard','miss','damage','damage_hits','hit_rate','dodge_rate','parry_rate','perfect_parry_rate','damage_share','damage_hit_share'],patterns);
csv('EnemyLifecycle.csv',['session_id','stage_id','stage_attempt_id','enemy_id','enemy_archetype','spawn_ordinal','melee_probability','beast_probability','status','spawn_x','spawn_z','end_x','end_z','lifetime_seconds','reason'],[...lifecycle.values()].map(r=>[r.sessionId,r.stageId,r.stageAttemptId,r.enemyId,r.archetype,r.spawnOrdinal,r.meleeProbability,r.beastProbability,r.status,r.spawnX,r.spawnZ,r.endX,r.endZ,r.lifetimeSeconds,r.reason]));
csv('BossPatterns.csv',['stage_attempt_id','stage_id','parent_pattern_id','started','completed','interrupted','cancelled','duration_total_seconds','mean_duration_seconds'],bossPatterns);
csv('Diagnostics.csv',['file','line','problem'],diagnostics);
console.log(JSON.stringify({events:events.length,stageAttempts:sessions.size,onboardingSteps:steps.size,
  enemyLifecycle:lifecycle.size,bossPatternRows:bossPatterns.length,invalidLines:diagnostics.length,output:path.resolve(destination)}));
