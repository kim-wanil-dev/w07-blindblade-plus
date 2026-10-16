// Local preprocessing uses the same column mapping as the Google Sheets receiver.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const [source, destination] = process.argv.slice(2);
if (!source || !destination) throw new Error('Usage: node Export-Telemetry.cjs <log directory> <output directory>');
if (path.resolve(source) === path.resolve(destination)) throw new Error('Use a separate output directory.');
const code = fs.readFileSync(path.join(__dirname, 'google_apps_script/Code.gs'), 'utf8');
const mapping = vm.runInNewContext(code + '\n({eventRow,stepRow,EVENT_HEADERS,SESSION_HEADERS,STEP_HEADERS,METRICS,validateRecord})');
const events=[], sessions=new Map(), steps=new Map(), seen=new Set(), heat=new Map(), diagnostics=[];
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
      if (['stage_started','stage_summary','stage_ended'].includes(r.eventType)) {
        const previous=sessions.get(r.stageAttemptId);
        if (!previous || previous.eventSequence<r.eventSequence) sessions.set(r.stageAttemptId,r);
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
        const x=Math.floor(p.position.x),z=Math.floor(p.position.z);
        const key=[r.stageAttemptId,layer,x,z].join('|');
        const row=heat.get(key)||[r.sessionId,r.stageId,r.stageAttemptId,layer,x,z,0];
        row[6]+=weight; heat.set(key,row);
      }
    } catch (error) { diagnostics.push([file,lineNumber,error.message]); }
  }
}
fs.mkdirSync(destination,{recursive:true});
function csv(name,headers,rows) {
  const cell=v=>'"'+String(v??'').replaceAll('"','""')+'"';
  fs.writeFileSync(path.join(destination,name),'\ufeff'+[headers,...rows].map(row=>row.map(cell).join(',')).join('\r\n'),'utf8');
}
const sessionRows=[], patterns=[];
for (const r of sessions.values()) {
  const p=r.payload;
  sessionRows.push([r.stageAttemptId,r.sessionId,r.stageId,r.anonymousPlayerId,r.buildVersion,r.configVersion,
    p.startedKst,r.eventSequence,p.reason,p.realtimeSeconds,p.gameplaySeconds,p.activePlaySeconds,p.currentHealth,
    ...mapping.METRICS.map(k=>p[k]??(r.stageId.startsWith('0-')||k==='reactionMeanSeconds'||k==='enemyHitRate'?'':0)),
    JSON.stringify(p.patterns||{}),p.runId,p.exitStepIndex,p.exitObjectiveIndex,p.stepsCompleted,p.attempts,p.retryCount]);
  for (const v of Object.values(p.patterns||{})) {
    const ratio=(a,b)=>b>0?(a||0)/b:'';
    patterns.push([r.stageAttemptId,r.stageId,v.kind,v.patternId,v.attempts||0,v.resolved||0,v.Hit||0,v.Dodge||0,
      v.NormalParry||0,v.PerfectParry||0,v.Guard||0,v.Miss||0,v.damage||0,v.damageHits||0,
      ratio(v.Hit,v.resolved),ratio(v.Dodge,v.resolved),ratio(v.NormalParry,v.resolved),ratio(v.PerfectParry,v.resolved),
      ratio(v.damage,p.damageTaken),ratio(v.damageHits,p.damageHitCount)]);
  }
}
csv('Events.csv',mapping.EVENT_HEADERS,events);
csv('Sessions.csv',mapping.SESSION_HEADERS,sessionRows);
csv('OnboardingSteps.csv',mapping.STEP_HEADERS,[...steps.values()]);
csv('Heatmaps.csv',['session_id','stage_id','stage_attempt_id','layer','grid_x','grid_z','weight'],[...heat.values()]);
csv('Patterns.csv',['stage_attempt_id','stage_id','kind','pattern_id','attempts','resolved','hit','dodge','parry','perfect_parry','guard','miss','damage','damage_hits','hit_rate','dodge_rate','parry_rate','perfect_parry_rate','damage_share','damage_hit_share'],patterns);
csv('Diagnostics.csv',['file','line','problem'],diagnostics);
console.log(JSON.stringify({events:events.length,stageAttempts:sessions.size,onboardingSteps:steps.size,
  invalidLines:diagnostics.length,output:path.resolve(destination)}));
