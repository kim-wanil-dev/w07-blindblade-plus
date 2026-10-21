using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
    /// <summary>스테이지 도전별 입력, 공격 결과, 공간 표본과 누적 요약을 저장한다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CombatTelemetryRecorder : MonoBehaviour
    {
        [Header("사건 공급자")]
        [SerializeField] private PlayerHealth _health;
        [SerializeField] private DefenseEnemySpawner _spawner;

        [Header("원본 저장")]
        [SerializeField] private bool _recordingEnabled = true;
        [SerializeField] private string _configVersion = "combat-analysis-0.1.3";
        [SerializeField, Min(64)] private int _bufferLimit = 4096;
        [SerializeField, Min(0.2f)] private float _flushIntervalSeconds = 2f;
        [SerializeField, Min(1)] private int _maximumStorageMegabytes = 100;
        [SerializeField, Min(0.1f)] private float _movementSampleIntervalSeconds = 1f;
        [SerializeField, Min(1f)] private float _summaryIntervalSeconds = 15f;

        [Header("Google Sheets 자동 전송")]
        [SerializeField] private bool _allowAutomaticUpload = true;
        [SerializeField, Min(5f)] private float _uploadIntervalSeconds = 20f;
        [SerializeField, Range(1, 100)] private int _batchEventLimit = 100;
        [SerializeField, Range(5, 120)] private int _requestTimeoutSeconds = 30;

        private readonly Dictionary<int, JObject> _pendingEnds = new Dictionary<int, JObject>();
        private bool _completePending;
        private bool _deathPending;
        private readonly Dictionary<int, JObject> _attacks = new Dictionary<int, JObject>();
        private readonly Dictionary<int, double> _reactionStartTimes = new Dictionary<int, double>();
        private readonly Dictionary<int, PlayerInputRecord> _inputs = new Dictionary<int, PlayerInputRecord>();
        private readonly HashSet<int> _attempts = new HashSet<int>();
        private readonly HashSet<int> _guardContacts = new HashSet<int>();
        private readonly Dictionary<int, string> _guardRepresentativeOutcomes = new Dictionary<int, string>();
        private readonly HashSet<int> _startedDodgeIds = new HashSet<int>();
        private readonly HashSet<int> _perfectDodgeIds = new HashSet<int>();
        private JObject _counts;
        private JObject _patterns;
        private JObject _bossPatterns;
        private CombatSessionRuntime _runtime;
        private CombatTelemetryService _service;
        private RainyStageController _rainyStage;
        private BambooStage1Controller _stage1Controller;
        private KatanaCombatCoordinator _combat;
        private GameStateManager _state;
        private string _sessionId;
        private string _runId;
        private string _stageId;
        private string _attemptSessionId;
        private string _previousAttemptSessionId;
        private int _retryIndex;
        private string _entryMode;
        private string _stageStartedKst;
        private long _sequence;
        private double _stageRealtime;
        private double _nextSummary;
        private double _nextMovement;
        private double _lastMovementActive;
        private double _reactionTotal;
        private int _reactionCount;
        private int _guardInputId;
        private int _dodgeInputId;
        private float _stageGameplay;
        private double _stageActive;
        private double _activeSeconds;
        private bool _bound;
        private bool _stageClosed;
        private bool _closed;

        void Start()
        {
            if (!_recordingEnabled) return;
            _runtime = CombatSessionRuntime.Active;
            if (_runtime == null || !_runtime.IsInitialized || _health == null || !Game.IsInitialized) return;
            _sessionId = _runtime.Session.SessionId;
            _runId = CombatTelemetryRunContext.EnsureRunId();
            _combat = _health.GetComponent<KatanaCombatCoordinator>();
            _state = Game.Instance.GameStateManager;
            _service = CombatTelemetryService.GetOrCreate(_bufferLimit, _flushIntervalSeconds, _maximumStorageMegabytes,
                _allowAutomaticUpload, _uploadIntervalSeconds, _batchEventLimit, _requestTimeoutSeconds);
            _service.QuitRequested += HandleQuitRequested;
            _health.Died += HandleDeath;
            _combat.Events.InputResolved += HandleInput;
            _combat.Events.DodgeChanged += HandleDodge;
            _state.StateChanged += HandleState;
            if (_spawner != null)
            {
                _spawner.EnemyKilled += HandleKill;
                _spawner.EnemySpawned += HandleEnemySpawned;
                _spawner.EnemyDefeated += HandleEnemyDefeated;
                _spawner.EnemyDespawned += HandleEnemyDespawned;
            }
            CombatTelemetrySignals.Raised += HandleSignal;
            _bound = true;
            _rainyStage = gameObject.scene.name == "RainyTwinBoss"
                ? FindObjectOfType<RainyStageController>() : null;
            _stage1Controller = gameObject.scene.name == "BambooStage1"
                ? FindObjectOfType<BambooStage1Controller>() : null;
            if (_rainyStage != null)
            {
                _entryMode = _rainyStage.CurrentTelemetryEntryMode;
                BeginStage(_rainyStage.CurrentTelemetryStageId);
            }
            else if (gameObject.scene.name == "BambooStage1")
            {
                _entryMode = _stage1Controller != null ? _stage1Controller.CurrentTelemetryEntryMode
                    : BambooStage1Progress.BossCheckpointReached ? "checkpoint_retry" : CombatTelemetryRunContext.EntryMode;
                BeginStage(_stage1Controller != null ? _stage1Controller.CurrentTelemetryStageId
                    : BambooStage1Progress.BossCheckpointReached ? "1-2" : "1-1");
            }
            else
            {
                _entryMode = CombatTelemetryRunContext.EntryMode;
                BeginStage("unassigned");
            }
            Record("session_started", new JObject {
                ["scene"] = gameObject.scene.path, ["isEditor"] = Application.isEditor,
                ["runId"] = _runId, ["entryMode"] = _entryMode,
                ["startedKst"] = _stageStartedKst, ["movementIntervalSeconds"] = _movementSampleIntervalSeconds,
                ["reactionClock"] = "frameRealtime",
                ["positionSpace"] = "Unity world XZ, metres" });
            _service.RequestFlush();
        }

        void Update()
        {
            if (_bound && !_stageClosed && _state.State == GameplayState.Playing)
                _activeSeconds += Time.unscaledDeltaTime;
        }

        void LateUpdate()
        {
            if (!_bound || _closed || _stageClosed) return;
            DrainAttackEnds();
            if (_deathPending || _state.State == GameplayState.GameOver)
            {
                _counts["deaths"] = 1;
                _counts["gameCompleted"] = false;
                FinishStage("death");
                return;
            }
            if (_state.State == GameplayState.Cleared)
            {
                FinishStage("final_clear");
                return;
            }
            if (_completePending) { FinishStage("cleared"); return; }
            if (_state.State != GameplayState.Playing) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= _nextMovement)
            {
                RecordMovement();
                _nextMovement = now + Math.Max(0.1f, _movementSampleIntervalSeconds);
            }
            if (now >= _nextSummary)
            {
                Record("stage_summary", Summary("in_progress"));
                _nextSummary = now + Math.Max(1f, _summaryIntervalSeconds);
            }
        }

        /// <summary>stage의 새 도전 구간과 고유 attemptSessionId를 열고 카운터와 시작 시각을 초기화한다.</summary>
        private void BeginStage(string stage)
        {
            _stageId = stage;
            _attemptSessionId = CombatTelemetryRunContext.BeginStageAttempt(_runId, _stageId,
                out _retryIndex, out _previousAttemptSessionId);
            _stageClosed = false;
            _stageStartedKst = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            _stageRealtime = Time.realtimeSinceStartupAsDouble;
            _stageGameplay = Time.time;
            _stageActive = _activeSeconds;
            _lastMovementActive = _activeSeconds;
            _counts = new JObject();
            _counts["gameCompleted"] = false;
            _counts["enemyPerfectDodgeCount"] = 0;
            _counts["enemyFinalOutcomeCount"] = 0;
            _counts["enemyValidOutcomeCount"] = 0;
            _counts["dodgePerfectCount"] = 0;
            _counts["skillEmpowerActivationCount"] = 0;
            _counts["skillSwordWaveActivationCount"] = 0;
            _counts["awarenessHitCount"] = 0;
            if (_stageId.StartsWith("1-") || _stageId.StartsWith("2-"))
            {
                foreach (string key in new[]
                {
                    "enemySpawnCount", "enemyMeleeSpawnCount", "enemyBeastSpawnCount", "enemyThrowerSpawnCount",
                    "enemyDefeatCount", "enemyMeleeDefeatCount", "enemyBeastDefeatCount", "enemyThrowerDefeatCount",
                    "enemyDespawnCount", "enemyMeleeDespawnCount", "enemyBeastDespawnCount", "enemyThrowerDespawnCount",
                    "beastExpectedSpawnCount"
                })
                    _counts[key] = 0;
            }
            _patterns = new JObject();
            _bossPatterns = new JObject();
            _attacks.Clear();
            _reactionStartTimes.Clear();
            _pendingEnds.Clear();
            _completePending = false;
            _deathPending = false;
            _inputs.Clear();
            _attempts.Clear();
            _guardContacts.Clear();
            _guardRepresentativeOutcomes.Clear();
            _startedDodgeIds.Clear();
            _perfectDodgeIds.Clear();
            _guardInputId = _dodgeInputId = 0;
            _reactionTotal = 0;
            _reactionCount = 0;
            Record("stage_started", Summary("started"));
            RecordStageConfiguration();
        }

        /// <summary>현재 stage와 연결된 Inspector 값을 stage_config 사건으로 저장한다.</summary>
        private void RecordStageConfiguration()
        {
            JObject payload = new JObject { ["stageId"] = _stageId, ["entryMode"] = _entryMode };
            if (_spawner != null)
                payload.Merge(_spawner.GetTelemetryConfiguration);
            if (_rainyStage != null)
            {
                payload["requiredKills"] = _rainyStage.TelemetryRequiredKills;
                payload["startBossImmediately"] = _rainyStage.TelemetryStartsBossImmediately;
                payload["arenaCenter"] = TelemetryJson.Position(_rainyStage.TelemetryArenaCenter);
                payload["arenaRadius"] = _rainyStage.TelemetryArenaRadius;
            }
            else if (_stage1Controller != null)
            {
                payload["requiredKills"] = _stage1Controller.TelemetryRequiredKills;
                payload["startBossImmediately"] = _stage1Controller.TelemetryStartsBossImmediately;
                payload["arenaCenter"] = TelemetryJson.Position(_stage1Controller.TelemetryArenaCenter);
                payload["arenaRadius"] = _stage1Controller.TelemetryArenaRadius;
            }
            Record("stage_config", payload);
        }

        /// <summary>reason으로 현재 구간을 종료한다. 남은 공격은 Interrupted로 구분하고 최종 요약을 디스크에 요청한다.</summary>
        private void FinishStage(string reason)
        {
            if (_stageClosed) return;
            DrainAttackEnds();
            foreach (int id in new List<int>(_attacks.Keys))
                ResolveAttack(id, new JObject { ["outcome"] = "Interrupted",
                    ["position"] = TelemetryJson.Position(_health.transform.position) }, false);
            RecordMovement();
            Record("stage_ended", Summary(reason));
            _stageClosed = true;
            _service.RequestFlush();
        }

        /// <summary>현재 위치와 직전 표본 이후 실제 플레이 시간을 기록하고 표본 기준 시각을 갱신한다.</summary>
        private void RecordMovement()
        {
            double duration = Math.Max(0, _activeSeconds - _lastMovementActive);
            if (duration <= 0) return;
            Record("movement_sample", new JObject { ["position"] = TelemetryJson.Position(_health.transform.position),
                ["durationSeconds"] = duration });
            _lastMovementActive = _activeSeconds;
        }

        /// <summary>type과 payload를 세션·스테이지·상관 ID와 KST 시각으로 감싸 저장 큐에 넣는다.</summary>
        private void Record(string type, JObject payload, int attackId = 0, int inputId = 0, int actionId = 0, int dodgeId = 0)
        {
            if (_closed) return;
            CombatTimestamp time = _runtime.CaptureTimestamp();
            string kst = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            long sequence = ++_sequence;
            _service.Enqueue(new JObject { ["schemaVersion"] = TelemetryJson.SCHEMA_VERSION,
                ["contractVersion"] = "0.2.0", ["configVersion"] = Application.isEditor ? _configVersion + "-editor" : _configVersion,
                ["buildVersion"] = Application.version, ["sessionId"] = _sessionId,
                ["runId"] = _runId,
                ["anonymousPlayerId"] = _service.AnonymousPlayerId, ["sessionGeneration"] = _runtime.Session.Generation,
                ["stageId"] = _stageId, ["stageAttemptId"] = _attemptSessionId,
                ["attemptSessionId"] = _attemptSessionId, ["retryIndex"] = _retryIndex,
                ["previousAttemptSessionId"] = string.IsNullOrEmpty(_previousAttemptSessionId)
                    ? JValue.CreateNull() : new JValue(_previousAttemptSessionId),
                ["entryMode"] = _entryMode,
                ["eventSequence"] = sequence, ["sourceId"] = "combat.telemetry", ["sourceSequence"] = sequence,
                ["eventType"] = type, ["occurredAt"] = TelemetryJson.Timestamp(time), ["recordedAt"] = TelemetryJson.Timestamp(time),
                ["occurredAtKst"] = kst, ["recordedAtKst"] = kst,
                ["correlation"] = TelemetryJson.Correlation(new CombatCorrelationIds(inputId, actionId, attackId,
                    (int?)payload["hitId"] ?? 0, 0, dodgeId)), ["payload"] = payload });
        }

        /// <summary>type·id·data의 생산자 사건을 공격 상태와 반응 기준 시각에 반영한다. 중복 종료와 종료된 구간의 사건은 무시한다.</summary>
        private void HandleSignal(string type, int id, JObject data)
        {
            if (type == "stage_change")
            {
                string next = (string)data["stageId"];
                if (next == _stageId) return;
                FinishStage("cleared");
                _entryMode = "natural_transition";
                BeginStage(next);
                return;
            }
            if (type == "stage_complete") { _completePending = true; return; }
            if (type == "stage_config" || type == "enemy_spawned" || type == "enemy_defeated"
                || type == "enemy_despawned" || type == "player_skill_activated" || type.StartsWith("boss_"))
            {
                if (_stageClosed) return;
                if (type == "enemy_spawned")
                {
                    string archetype = (string)data["enemyArchetype"] ?? "Unknown";
                    Add(_counts, "enemySpawnCount");
                    Add(_counts, "enemy" + archetype + "SpawnCount");
                    if (archetype == "Melee" || archetype == "Beast")
                        Add(_counts, "beastExpectedSpawnCount", (double?)data["beastProbability"] ?? 0d);
                }
                else if (type == "enemy_defeated")
                {
                    string archetype = (string)data["enemyArchetype"] ?? "Unknown";
                    Add(_counts, "enemyDefeatCount");
                    Add(_counts, "enemy" + archetype + "DefeatCount");
                }
                else if (type == "enemy_despawned")
                {
                    string archetype = (string)data["enemyArchetype"] ?? "Unknown";
                    Add(_counts, "enemyDespawnCount");
                    Add(_counts, "enemy" + archetype + "DespawnCount");
                }
                else if (type == "boss_pattern_started")
                    Add(BossPattern((string)data["parentPatternId"]), "started");
                else if (type == "boss_pattern_ended")
                {
                    JObject pattern = BossPattern((string)data["parentPatternId"]);
                    string outcome = (string)data["patternOutcome"] ?? "Unknown";
                    Add(pattern, "ended");
                    Add(pattern, "durationTotalSeconds", (double?)data["durationSeconds"] ?? 0d);
                    if (outcome == "Completed") Add(pattern, "completed");
                    else if (outcome == "InterruptedByPlayerHit") Add(pattern, "interrupted");
                    else Add(pattern, "cancelled");
                    Add(pattern, outcome);
                }
                else if (type == "boss_damage_received")
                {
                    string actor = (string)data["bossActor"] ?? "Unknown";
                    Add(_counts, "twinBoss" + actor + "Damage", (double?)data["appliedDamage"] ?? 0d);
                }
                else if (type == "boss_actor_defeated")
                {
                    string actor = (string)data["bossActor"] ?? "Unknown";
                    Add(_counts, "twinBoss" + actor + "Defeated");
                    _counts["twinBoss" + actor + "DefeatTime"] = data["timeSinceStageStart"]?.DeepClone();
                }
                else if (type == "boss_pair_defeated")
                {
                    _counts["twinBossPairDefeated"] = true;
                    _counts["twinBossHumanDefeatTime"] = data["humanDefeatTime"]?.DeepClone();
                    _counts["twinBossBeastDefeatTime"] = data["beastDefeatTime"]?.DeepClone();
                    _counts["twinBossStageClearTime"] = data["stageClearTime"]?.DeepClone();
                }
                else if (type == "player_skill_activated")
                {
                    string skillName = (string)data["skillName"] ?? "Unknown";
                    string skillSource = (string)data["skillSource"] ?? "Unknown";
                    if (skillName == "Empower" && skillSource == "manual")
                        Add(_counts, "skillEmpowerActivationCount");
                    else if (skillName == "SwordWave")
                        Add(_counts, "skillSwordWaveActivationCount");
                }
                Record(type, data, id);
                return;
            }
            if (_stageClosed || id == 0) return;
            if (type == "enemy_attack_started")
            {
                double attackStartRealtime = Time.realtimeSinceStartupAsDouble;
                data["telegraphRealtime"] = attackStartRealtime;
                data["committed"] = false;
                _attacks[id] = (JObject)data.DeepClone();
                _reactionStartTimes[id] = attackStartRealtime;
                Record(type, data, id);
            }
            else if (type == "enemy_attack_reaction_started" && _attacks.ContainsKey(id)
                && _reactionStartTimes.ContainsKey(id))
            {
                _reactionStartTimes[id] = Time.realtimeSinceStartupAsDouble;
            }
            else if (type == "enemy_attack_committed" && _attacks.TryGetValue(id, out JObject attack))
            {
                if ((bool)attack["committed"]) return;
                attack["committed"] = true;
                attack["origin"] = data["origin"].DeepClone();
                Add(_counts, "enemyAttackCount");
                Add(Pattern(attack), "attempts");
                Record(type, (JObject)attack.DeepClone(), id);
            }
            else if (type == "enemy_attack_contact") ResolveAttack(id, data, true);
            else if (type == "enemy_attack_resolved" && !_pendingEnds.ContainsKey(id)) _pendingEnds[id] = data;
        }

        /// <summary>프레임 안의 접촉 처리가 끝난 뒤 비접촉 종료를 반영하고 대기 목록을 비운다.</summary>
        private void DrainAttackEnds()
        {
            foreach (KeyValuePair<int, JObject> pair in _pendingEnds) ResolveAttack(pair.Key, pair.Value, false);
            _pendingEnds.Clear();
        }

        /// <summary>id와 data, contact 여부로 최초 공격 결과를 확정하고, 연결 입력과 공격별 반응 기준 시각을 사용해 집계를 갱신한다.</summary>
        private void ResolveAttack(int id, JObject data, bool contact)
        {
            if (!_attacks.TryGetValue(id, out JObject attack) || (bool?)attack["resolved"] == true) return;
            attack["resolved"] = true;
            string outcome = (string)data["outcome"];
            if (outcome == "Dodge" || outcome == "DodgeAvoided")
            {
                bool perfectDodge = (string)data["defenseOutcome"] == "DodgeAvoided" || outcome == "DodgeAvoided";
                outcome = perfectDodge ? "PerfectDodge" : "Dodge";
            }
            JObject payload = (JObject)attack.DeepClone();
            payload.Merge(data, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            payload["playerPosition"] = TelemetryJson.Position(_health.transform.position);
            payload["contact"] = contact;
            payload["outcome"] = outcome;
            Add(_counts, "enemy" + outcome + "Count");
            Add(_counts, "enemyFinalOutcomeCount");
            if ((bool)attack["committed"])
            {
                Add(Pattern(attack), outcome);
                if (outcome == "Hit" || outcome == "Dodge" || outcome == "PerfectDodge" || outcome == "Guard"
                    || outcome == "NormalParry" || outcome == "PerfectParry" || outcome == "Miss")
                    Add(_counts, "enemyValidOutcomeCount");
                if (outcome != "Interrupted" && outcome != "Cancelled")
                {
                    Add(_counts, "enemyResolvedCount");
                    Add(Pattern(attack), "resolved");
                }
            }
            int inputId = (int?)data["inputId"] ?? 0;
            if (contact)
            {
                float damage = (float?)data["appliedDamage"] ?? 0;
                Add(_counts, "damageTaken", damage);
                Add(Pattern(attack), "damage", damage);
                if (damage > 0) { Add(_counts, "damageHitCount"); Add(Pattern(attack), "damageHits"); }
                if (inputId == 0 && outcome == "Dodge") inputId = _dodgeInputId;
                if (inputId == 0 && (outcome == "Hit" || outcome == "Guard"
                    || outcome == "NormalParry" || outcome == "PerfectParry")) inputId = _guardInputId;
                if (inputId != 0 && _inputs.TryGetValue(inputId, out PlayerInputRecord input))
                {
                    bool guard = input.Kind == PlayerInputKind.GuardPress;
                    if (guard)
                    {
                        _guardContacts.Add(inputId);
                        Add(_counts, "guard" + outcome + "Count");
                        UpdateGuardRepresentativeOutcome(inputId, outcome);
                        payload["representativeOutcome"] = _guardRepresentativeOutcomes[inputId];
                    }
                    double reactionStartRealtime = _reactionStartTimes.TryGetValue(id, out double startRealtime)
                        ? startRealtime : (double)attack["telegraphRealtime"];
                    double reaction = input.FrameRealTime - reactionStartRealtime;
                    double lead = Time.realtimeSinceStartupAsDouble - input.FrameRealTime;
                    payload["inputToContactSeconds"] = Math.Max(0, lead);
                    payload["reactionSeconds"] = reaction >= 0 ? (JToken)reaction : JValue.CreateNull();
                    payload["reactionAssociation"] = outcome == "Hit" ? "held_guard_intent" : "resolved_contact";
                    if (reaction >= 0) { _reactionTotal += reaction; _reactionCount++; }
                }
                if ((bool?)payload["awarenessSnapshotCollected"] == true)
                {
                    Add(_counts, "awarenessHitCount");
                    foreach (string key in new[] { "awarenessRadius", "normalEnemyCount", "bossCount",
                        "hostileShurikenCount", "hostileTotalCount" })
                        Add(_counts, key + "Total", (double?)payload[key] ?? 0d);
                }
            }
            Record("enemy_attack_resolved", payload, id, inputId);
            _reactionStartTimes.Remove(id);
        }

        /// <summary>input의 고유 누름만 시도로 합산하고 예약 후 시작은 중복 집계하지 않는다.</summary>
        private void HandleInput(PlayerInputRecord input)
        {
            if (_stageClosed) return;
            if (input.Kind == PlayerInputKind.GuardRelease) _guardInputId = 0;
            bool attempt = input.Kind != PlayerInputKind.GuardRelease && input.Kind != PlayerInputKind.GuardHoldResume
                && input.Status != PlayerInputStatus.BufferDropped && _attempts.Add(input.InputId);
            JObject payload = new JObject { ["kind"] = input.Kind.ToString(), ["status"] = input.Status.ToString(),
                ["rejectReason"] = input.RejectReason.ToString(), ["isAttempt"] = attempt,
                ["position"] = TelemetryJson.Position(_health.transform.position),
                ["deviceTime"] = input.DeviceTime, ["frameRealtime"] = input.FrameRealTime };
            Record("player_input", payload, inputId: input.InputId, actionId: input.ActionId);
            bool knownAttempt = _attempts.Contains(input.InputId);
            if (attempt || knownAttempt) _inputs[input.InputId] = input;
            if (knownAttempt && input.Kind == PlayerInputKind.Dodge && input.Status == PlayerInputStatus.Started)
                _dodgeInputId = input.InputId;
            if (!attempt) return;
            string kind = input.Kind == PlayerInputKind.GuardPress ? "guard" : input.Kind == PlayerInputKind.Dodge ? "dodge" : "attack";
            Add(_counts, kind + "Attempts");
            if (input.Status == PlayerInputStatus.Rejected) Add(_counts, kind + "Rejected");
            if (input.Kind == PlayerInputKind.GuardPress) _guardInputId = input.InputId;
            if (input.Kind == PlayerInputKind.GuardPress && !_guardRepresentativeOutcomes.ContainsKey(input.InputId))
                _guardRepresentativeOutcomes[input.InputId] = "NoResult";
        }

        /// <summary>value의 실제 회피 시작과 퍼펙트 회피만 기록한다. 종료 사건은 전송하지 않는다.</summary>
        private void HandleDodge(PlayerDodgeEvent value)
        {
            if (_stageClosed) return;
            if (value.DodgeId > 0 && value.Stage == PlayerDodgeStage.Started && _startedDodgeIds.Add(value.DodgeId))
                Add(_counts, "dodgeStarted");
            if (value.DodgeId > 0 && value.Stage == PlayerDodgeStage.PerfectDodge && _perfectDodgeIds.Add(value.DodgeId))
                Add(_counts, "dodgePerfectCount");
            if (value.Stage != PlayerDodgeStage.Started && value.Stage != PlayerDodgeStage.PerfectDodge) return;
            Record("dodge_changed", new JObject { ["stage"] = value.Stage.ToString(),
                ["position"] = TelemetryJson.Position(value.Position) }, value.AttackId, dodgeId: value.DodgeId);
        }

        /// <summary>health의 사망을 현재 구간에 한 번 집계한다. 최종 접촉 콜백 이후 LateUpdate에서 요약을 닫는다.</summary>
        private void HandleDeath(PlayerHealth health)
        {
            if (_stageClosed) return;
            _counts["deaths"] = 1;
            _deathPending = true;
            _completePending = false;
            Record("player_died", new JObject { ["position"] = TelemetryJson.Position(health.transform.position) });
        }

        /// <summary>게임 상태가 바뀌면 마지막 위치 표본을 닫아 일시정지 시간을 제외한다.</summary>
        private void HandleState(GameplayState state)
        {
            if (_stageClosed) return;
            RecordMovement();
            if (state == GameplayState.Cleared)
            {
                _counts["gameCompleted"] = true;
                _counts["gameCompletedOccurredAtKst"] = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
                Record("game_completed", new JObject { ["stageId"] = _stageId,
                    ["stageAttemptId"] = _attemptSessionId, ["runId"] = _runId,
                    ["gameCompletedOccurredAtKst"] = _counts["gameCompletedOccurredAtKst"] });
            }
        }

        /// <summary>일반 적 처치 사건을 현재 구간의 kills에 더한다.</summary>
        private void HandleKill()
        {
            if (!_stageClosed) Add(_counts, "kills");
        }

        /// <summary>적 종류와 스폰 확률, 순번, 위치를 구성해 enemy_spawned 사건으로 저장한다.</summary>
        private void HandleEnemySpawned(string enemyId, string enemyArchetype, int spawnOrdinal,
            Vector3 position, float meleeProbability, float beastProbability)
        {
            HandleSignal("enemy_spawned", 0, new JObject {
                ["enemyId"] = enemyId, ["enemyArchetype"] = enemyArchetype, ["spawnOrdinal"] = spawnOrdinal,
                ["position"] = TelemetryJson.Position(position), ["meleeProbability"] = meleeProbability,
                ["beastProbability"] = beastProbability });
        }

        /// <summary>처치한 적 종류와 생존 시간, 위치를 구성해 enemy_defeated 사건으로 저장한다.</summary>
        private void HandleEnemyDefeated(string enemyId, string enemyArchetype, double lifetimeSeconds,
            Vector3 position)
        {
            HandleSignal("enemy_defeated", 0, new JObject {
                ["enemyId"] = enemyId, ["enemyArchetype"] = enemyArchetype,
                ["lifetimeSeconds"] = lifetimeSeconds, ["position"] = TelemetryJson.Position(position) });
        }

        /// <summary>사망 이외 사유로 비활성화된 적 종류와 생존 시간, 위치를 사건으로 저장한다.</summary>
        private void HandleEnemyDespawned(string enemyId, string enemyArchetype, double lifetimeSeconds,
            Vector3 position, string reason)
        {
            HandleSignal("enemy_despawned", 0, new JObject {
                ["enemyId"] = enemyId, ["enemyArchetype"] = enemyArchetype,
                ["lifetimeSeconds"] = lifetimeSeconds, ["position"] = TelemetryJson.Position(position),
                ["reason"] = reason });
        }

        /// <summary>attack의 종류·패턴별 집계 객체를 반환하며 없으면 만든다.</summary>
        private JObject Pattern(JObject attack)
        {
            string key = (string)attack["kind"] + ":" + (string)attack["patternId"]
                + ":" + ((string)attack["parentPatternId"] ?? string.Empty);
            if (!(_patterns[key] is JObject value))
            {
                value = new JObject { ["kind"] = attack["kind"], ["patternId"] = attack["patternId"],
                    ["parentPatternId"] = attack["parentPatternId"]?.DeepClone(),
                    ["attackerActor"] = attack["attackerActor"]?.DeepClone() };
                _patterns[key] = value;
            }
            return value;
        }

        /// <summary>parentPatternId의 부모 패턴 집계를 반환하며 없으면 새 집계 객체를 만든다.</summary>
        private JObject BossPattern(string parentPatternId)
        {
            string key = string.IsNullOrEmpty(parentPatternId) ? "Unknown" : parentPatternId;
            if (!(_bossPatterns[key] is JObject value))
            {
                value = new JObject { ["parentPatternId"] = key };
                _bossPatterns[key] = value;
            }
            return value;
        }

        /// <summary>target의 key 숫자에 amount를 더하여 누적 카운터를 갱신한다.</summary>
        private static void Add(JObject target, string key, double amount = 1)
        {
            target[key] = ((double?)target[key] ?? 0) + amount;
        }

        /// <summary>입력별 방어 결과를 갱신하며 PerfectParry를 Guard보다 우선하여 하나만 집계한다.</summary>
        private void UpdateGuardRepresentativeOutcome(int inputId, string outcome)
        {
            if (outcome != "Guard" && outcome != "PerfectParry") return;
            int candidateRank = outcome == "PerfectParry" ? 2 : 1;
            int currentRank = _guardRepresentativeOutcomes.TryGetValue(inputId, out string current)
                ? current == "PerfectParry" ? 2 : current == "Guard" ? 1 : 0 : 0;
            if (candidateRank > currentRank)
                _guardRepresentativeOutcomes[inputId] = outcome;
        }

        /// <summary>reason과 현재 구간의 누적 값을 복사한다. 비율의 분모가 0이면 null을 반환한다.</summary>
        private JObject Summary(string reason)
        {
            JObject summary = (JObject)_counts.DeepClone();
            summary["reason"] = reason;
            summary["entryMode"] = _entryMode;
            summary["runId"] = _runId;
            summary["startedKst"] = _stageStartedKst;
            summary["realtimeSeconds"] = Time.realtimeSinceStartupAsDouble - _stageRealtime;
            summary["gameplaySeconds"] = Time.time - _stageGameplay;
            summary["activePlaySeconds"] = _activeSeconds - _stageActive;
            summary["currentHealth"] = _health.CurrentHealth;
            double meleeBeastSpawns = ((double?)_counts["enemyMeleeSpawnCount"] ?? 0)
                + ((double?)_counts["enemyBeastSpawnCount"] ?? 0);
            summary["beastSpawnShare"] = meleeBeastSpawns > 0
                ? (JToken)(((double?)_counts["enemyBeastSpawnCount"] ?? 0) / meleeBeastSpawns)
                : JValue.CreateNull();
            summary["beastExpectedSpawnShare"] = meleeBeastSpawns > 0
                ? (JToken)(((double?)_counts["beastExpectedSpawnCount"] ?? 0) / meleeBeastSpawns)
                : JValue.CreateNull();
            summary["guardAttemptsWithContact"] = _guardContacts.Count;
            summary["guardAttemptsWithoutContact"] = Math.Max(0, ((double?)_counts["guardAttempts"] ?? 0) - _guardContacts.Count);
            int representativeGuard = 0;
            int representativePerfectParry = 0;
            foreach (string outcome in _guardRepresentativeOutcomes.Values)
            {
                if (outcome == "Guard") representativeGuard++;
                else if (outcome == "PerfectParry") representativePerfectParry++;
            }
            summary["guardRepresentativeGuardCount"] = representativeGuard;
            summary["guardRepresentativePerfectParryCount"] = representativePerfectParry;
            summary["guardRepresentativeNoResultCount"] = Math.Max(0,
                (int)((double?)_counts["guardAttempts"] ?? 0) - representativeGuard - representativePerfectParry);
            summary["enemyNormalDodgeCount"] = _counts["enemyDodgeCount"]?.DeepClone() ?? new JValue(0);
            summary["reactionCount"] = _reactionCount;
            summary["reactionMeanSeconds"] = _reactionCount > 0 ? (JToken)(_reactionTotal / _reactionCount) : JValue.CreateNull();
            double resolved = (double?)_counts["enemyResolvedCount"] ?? 0;
            summary["enemyHitRate"] = resolved > 0 ? (JToken)(((double?)_counts["enemyHitCount"] ?? 0) / resolved) : JValue.CreateNull();
            string[] groups = { "ranged", "melee", "beastLeap", "bossRanged", "bossEscapeBarrage", "bossCharge",
                "bossCounterSlash", "twinBossRanged", "twinHumanSlash", "twinHumanCounterSlash", "twinHumanIai", "twinBeastBite" };
            foreach (string group in groups)
            {
                double damage = 0, hits = 0, attempts = 0, count = 0, dodges = 0, parries = 0, perfects = 0;
                string bossPatternId = group == "bossRanged" ? "BossRanged"
                    : group == "bossCharge" ? "BossCharge"
                    : group == "bossCounterSlash" ? "BossCounterSlash"
                    : group == "twinBossRanged" ? "TwinBossRanged"
                    : group == "twinHumanSlash" ? "TwinHumanSlash"
                    : group == "twinHumanCounterSlash" ? "TwinHumanCounterSlash"
                    : group == "twinHumanIai" ? "TwinHumanIai"
                    : group == "twinBeastBite" ? "TwinBeastBite"
                    : group == "beastLeap" ? "DefenseBeastLeap" : "BossEscapeBarrage";
                foreach (JProperty property in _patterns.Properties())
                {
                    JObject pattern = (JObject)property.Value;
                    string patternId = (string)pattern["patternId"];
                    bool matches = group == "ranged" ? (string)pattern["kind"] == "Projectile"
                        : group == "melee" ? (string)pattern["kind"] != "Projectile"
                        : patternId == bossPatternId;
                    if (!matches) continue;
                    damage += (double?)pattern["damage"] ?? 0;
                    hits += (double?)pattern["Hit"] ?? 0;
                    attempts += (double?)pattern["attempts"] ?? 0;
                    count += (double?)pattern["resolved"] ?? 0;
                    dodges += ((double?)pattern["Dodge"] ?? 0) + ((double?)pattern["PerfectDodge"] ?? 0);
                    parries += (double?)pattern["NormalParry"] ?? 0;
                    perfects += (double?)pattern["PerfectParry"] ?? 0;
                }
                summary[group + "Attempts"] = attempts;
                summary[group + "Resolved"] = count;
                summary[group + "Hits"] = hits;
                summary[group + "Dodges"] = dodges;
                summary[group + "Parries"] = parries;
                summary[group + "PerfectParries"] = perfects;
                summary[group + "Damage"] = damage;
                summary[group + "HitRate"] = count > 0 ? (JToken)(hits / count) : JValue.CreateNull();
                double totalDamage = (double?)_counts["damageTaken"] ?? 0;
                summary[group + "DamageShare"] = totalDamage > 0 ? (JToken)(damage / totalDamage) : JValue.CreateNull();
            }
            summary["patterns"] = _patterns.DeepClone();
            summary["bossPatterns"] = _bossPatterns.DeepClone();
            summary["twinBossHumanDamage"] = _counts["twinBossHumanDamage"]?.DeepClone() ?? new JValue(0);
            summary["twinBossBeastDamage"] = _counts["twinBossBeastDamage"]?.DeepClone() ?? new JValue(0);
            summary["twinBossHumanDefeated"] = _counts["twinBossHumanDefeated"]?.DeepClone() ?? new JValue(0);
            summary["twinBossBeastDefeated"] = _counts["twinBossBeastDefeated"]?.DeepClone() ?? new JValue(0);
            summary["twinBossHumanDefeatTime"] = _counts["twinBossHumanDefeatTime"]?.DeepClone();
            summary["twinBossBeastDefeatTime"] = _counts["twinBossBeastDefeatTime"]?.DeepClone();
            summary["twinBossPairDefeated"] = _counts["twinBossPairDefeated"]?.DeepClone() ?? new JValue(false);
            summary["twinBossStageClearTime"] = _counts["twinBossStageClearTime"]?.DeepClone();
            foreach (string parent in new[] { "Cooperation", "IaiCharge", "AlternatingMelee", "Countering" })
            {
                JObject pattern = BossPattern(parent);
                double ended = (double?)pattern["ended"] ?? 0d;
                summary["bossPattern" + parent + "Started"] = pattern["started"]?.DeepClone() ?? new JValue(0);
                summary["bossPattern" + parent + "Completed"] = pattern["completed"]?.DeepClone() ?? new JValue(0);
                summary["bossPattern" + parent + "Interrupted"] = pattern["interrupted"]?.DeepClone() ?? new JValue(0);
                summary["bossPattern" + parent + "Cancelled"] = pattern["cancelled"]?.DeepClone() ?? new JValue(0);
                summary["bossPattern" + parent + "MeanDurationSeconds"] = ended > 0
                    ? (JToken)(((double?)pattern["durationTotalSeconds"] ?? 0d) / ended) : JValue.CreateNull();
            }
            summary["serviceDroppedEventsTotal"] = _service.DroppedEvents;
            summary["dodgePerfectCount"] = (int?)_counts["dodgePerfectCount"] ?? 0;
            string[][] awarenessMetrics =
            {
                new[] { "awarenessRadius", "awarenessRadiusMetersMean" },
                new[] { "normalEnemyCount", "normalEnemiesNearHitMean" },
                new[] { "bossCount", "bossesNearHitMean" },
                new[] { "hostileShurikenCount", "hostileShurikensNearHitMean" },
                new[] { "hostileTotalCount", "hostileTotalNearHitMean" }
            };
            foreach (string[] metric in awarenessMetrics)
                summary[metric[1]] = (int?)_counts["awarenessHitCount"] > 0
                    ? (JToken)(((double?)_counts[metric[0] + "Total"] ?? 0d) / (double)_counts["awarenessHitCount"])
                    : JValue.CreateNull();
            summary["gameCompleted"] = (bool?)_counts["gameCompleted"] ?? false;
            summary["attemptSessionId"] = _attemptSessionId;
            summary["retryIndex"] = _retryIndex;
            summary["previousAttemptSessionId"] = string.IsNullOrEmpty(_previousAttemptSessionId)
                ? JValue.CreateNull() : new JValue(_previousAttemptSessionId);
            return summary;
        }

        /// <summary>reason으로 남은 구간과 세션을 닫고 저장을 요청한다. 반복 호출은 무시한다.</summary>
        private void Close(string reason)
        {
            if (!_bound || _closed) return;
            if (_state != null && _state.State == GameplayState.GameOver)
                _counts["deaths"] = 1;
            if (reason == "application_quit")
            {
                _counts["exitStageId"] = _stageId;
                _counts["exitStageAttemptId"] = _attemptSessionId;
                _counts["exitLocation"] = _stageId;
                _counts["exitOccurredAtKst"] = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            }
            FinishStage((_counts.Value<int?>("deaths") ?? 0) > 0 ? "death" : reason);
            Record("session_ended", new JObject { ["reason"] = reason,
                ["exitStageId"] = _stageId, ["exitStageAttemptId"] = _attemptSessionId,
                ["exitLocation"] = _stageId, ["exitOccurredAtKst"] = _counts["exitOccurredAtKst"] });
            _service.RequestFlush();
            _closed = true;
        }

        void OnApplicationQuit() { Close("application_quit"); }

        /// <summary>플레이어 종료가 시작되면 현재 구간과 세션을 닫는다. 최종 사건을 서비스 큐에 넣어 종료 전 전송 대상에 포함한다.</summary>
        private void HandleQuitRequested() { Close("application_quit"); }

        void OnDisable()
        {
            Close("scene_unloaded");
            if (!_bound) return;
            _service.QuitRequested -= HandleQuitRequested;
            _health.Died -= HandleDeath;
            _combat.Events.InputResolved -= HandleInput;
            _combat.Events.DodgeChanged -= HandleDodge;
            _state.StateChanged -= HandleState;
            if (_spawner != null) _spawner.EnemyKilled -= HandleKill;
            if (_spawner != null)
            {
                _spawner.EnemySpawned -= HandleEnemySpawned;
                _spawner.EnemyDefeated -= HandleEnemyDefeated;
                _spawner.EnemyDespawned -= HandleEnemyDespawned;
            }
            CombatTelemetrySignals.Raised -= HandleSignal;
            _bound = false;
        }
    }
}
