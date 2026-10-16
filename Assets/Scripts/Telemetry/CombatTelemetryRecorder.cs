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
        [SerializeField] private string _configVersion = "stage1-analysis-v3";
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
        [SerializeField, Min(30f)] private float _maximumRetrySeconds = 300f;

        private readonly Dictionary<int, JObject> _pendingEnds = new Dictionary<int, JObject>();
        private bool _completePending;
        private readonly Dictionary<int, JObject> _attacks = new Dictionary<int, JObject>();
        private readonly Dictionary<int, PlayerInputRecord> _inputs = new Dictionary<int, PlayerInputRecord>();
        private readonly HashSet<int> _attempts = new HashSet<int>();
        private readonly HashSet<int> _guardContacts = new HashSet<int>();
        private JObject _counts;
        private JObject _patterns;
        private CombatSessionRuntime _runtime;
        private CombatTelemetryService _service;
        private KatanaCombatCoordinator _combat;
        private GameStateManager _state;
        private string _sessionId;
        private string _stageId;
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
            _combat = _health.GetComponent<KatanaCombatCoordinator>();
            _state = Game.Instance.GameStateManager;
            _service = CombatTelemetryService.GetOrCreate(_bufferLimit, _flushIntervalSeconds, _maximumStorageMegabytes,
                _allowAutomaticUpload, _uploadIntervalSeconds, _batchEventLimit, _requestTimeoutSeconds, _maximumRetrySeconds);
            _health.Died += HandleDeath;
            _combat.Events.InputResolved += HandleInput;
            _combat.Events.DodgeChanged += HandleDodge;
            _state.StateChanged += HandleState;
            if (_spawner != null) _spawner.EnemyKilled += HandleKill;
            CombatTelemetrySignals.Raised += HandleSignal;
            _bound = true;
            BeginStage(gameObject.scene.name == "BambooStage1"
                ? (BambooStage1Progress.BossCheckpointReached ? "1-2" : "1-1") : "unassigned");
            Record("session_started", new JObject {
                ["scene"] = gameObject.scene.path, ["isEditor"] = Application.isEditor,
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
            if (_completePending) { FinishStage("cleared"); return; }
            if (_state.State == GameplayState.GameOver)
            {
                FinishStage("death");
                return;
            }
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

        /// <summary>stage의 새 도전 구간을 열고 카운터와 시작 시각을 초기화한다. 세션 안에서 stageAttemptId는 stage별로 유일하다.</summary>
        private void BeginStage(string stage)
        {
            _stageId = stage;
            _stageClosed = false;
            _stageStartedKst = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            _stageRealtime = Time.realtimeSinceStartupAsDouble;
            _stageGameplay = Time.time;
            _stageActive = _activeSeconds;
            _lastMovementActive = _activeSeconds;
            _counts = new JObject();
            _patterns = new JObject();
            _attacks.Clear();
            _pendingEnds.Clear();
            _completePending = false;
            _inputs.Clear();
            _attempts.Clear();
            _guardContacts.Clear();
            _guardInputId = _dodgeInputId = 0;
            _reactionTotal = 0;
            _reactionCount = 0;
            Record("stage_started", Summary("started"));
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
                ["anonymousPlayerId"] = _service.AnonymousPlayerId, ["sessionGeneration"] = _runtime.Session.Generation,
                ["stageId"] = _stageId, ["stageAttemptId"] = _sessionId + ":" + _stageId,
                ["eventSequence"] = sequence, ["sourceId"] = "stage1.telemetry", ["sourceSequence"] = sequence,
                ["eventType"] = type, ["occurredAt"] = TelemetryJson.Timestamp(time), ["recordedAt"] = TelemetryJson.Timestamp(time),
                ["occurredAtKst"] = kst, ["recordedAtKst"] = kst,
                ["correlation"] = TelemetryJson.Correlation(new CombatCorrelationIds(inputId, actionId, attackId,
                    (int?)payload["hitId"] ?? 0, 0, dodgeId)), ["payload"] = payload });
        }

        /// <summary>type·id·data의 생산자 사건을 공격 상태에 반영한다. 중복 종료와 종료된 구간의 사건은 무시한다.</summary>
        private void HandleSignal(string type, int id, JObject data)
        {
            if (type == "stage_change")
            {
                string next = (string)data["stageId"];
                if (next == _stageId) return;
                FinishStage("cleared");
                BeginStage(next);
                return;
            }
            if (type == "stage_complete") { _completePending = true; return; }
            if (_stageClosed || id == 0) return;
            if (type == "enemy_attack_started")
            {
                data["telegraphRealtime"] = Time.realtimeSinceStartupAsDouble;
                data["committed"] = false;
                _attacks[id] = (JObject)data.DeepClone();
                Record(type, data, id);
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

        /// <summary>공격 id의 최초 결과만 확정하고 적중 분모·분자와 정확한 접촉의 반응 시간을 갱신한다.</summary>
        private void ResolveAttack(int id, JObject data, bool contact)
        {
            if (!_attacks.TryGetValue(id, out JObject attack) || (bool?)attack["resolved"] == true) return;
            attack["resolved"] = true;
            string outcome = (string)data["outcome"];
            JObject payload = (JObject)attack.DeepClone();
            payload.Merge(data, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            payload["playerPosition"] = TelemetryJson.Position(_health.transform.position);
            payload["contact"] = contact;
            if ((bool)attack["committed"])
            {
                Add(_counts, "enemy" + outcome + "Count");
                Add(Pattern(attack), outcome);
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
                if (inputId == 0 && outcome == "Hit") inputId = _guardInputId;
                if (inputId != 0 && _inputs.TryGetValue(inputId, out PlayerInputRecord input))
                {
                    bool guard = input.Kind == PlayerInputKind.GuardPress;
                    if (guard)
                    {
                        _guardContacts.Add(inputId);
                        Add(_counts, "guard" + outcome + "Count");
                    }
                    double reaction = input.FrameRealTime - (double)attack["telegraphRealtime"];
                    double lead = Time.realtimeSinceStartupAsDouble - input.FrameRealTime;
                    payload["inputToContactSeconds"] = Math.Max(0, lead);
                    payload["reactionSeconds"] = reaction >= 0 ? (JToken)reaction : JValue.CreateNull();
                    payload["reactionAssociation"] = outcome == "Hit" ? "held_guard_intent" : "resolved_contact";
                    if (reaction >= 0) { _reactionTotal += reaction; _reactionCount++; }
                }
            }
            Record("enemy_attack_resolved", payload, id, inputId);
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
            if (!attempt) return;
            _inputs[input.InputId] = input;
            string kind = input.Kind == PlayerInputKind.GuardPress ? "guard" : input.Kind == PlayerInputKind.Dodge ? "dodge" : "attack";
            Add(_counts, kind + "Attempts");
            if (input.Status == PlayerInputStatus.Rejected) Add(_counts, kind + "Rejected");
            if (input.Kind == PlayerInputKind.GuardPress) _guardInputId = input.InputId;
            if (input.Kind == PlayerInputKind.Dodge && input.Status == PlayerInputStatus.Started) _dodgeInputId = input.InputId;
        }

        /// <summary>value의 실제 회피 시작과 퍼펙트 회피만 기록한다. 종료 사건은 전송하지 않는다.</summary>
        private void HandleDodge(PlayerDodgeEvent value)
        {
            if (_stageClosed) return;
            if (value.Stage == PlayerDodgeStage.Started) Add(_counts, "dodgeStarted");
            if (value.Stage != PlayerDodgeStage.Started && value.Stage != PlayerDodgeStage.PerfectDodge) return;
            Record("dodge_changed", new JObject { ["stage"] = value.Stage.ToString(),
                ["position"] = TelemetryJson.Position(value.Position) }, value.AttackId, dodgeId: value.DodgeId);
        }

        /// <summary>health의 사망을 현재 구간에 한 번 집계한다. 최종 접촉 콜백 이후 LateUpdate에서 요약을 닫는다.</summary>
        private void HandleDeath(PlayerHealth health)
        {
            if (_stageClosed) return;
            _counts["deaths"] = 1;
            Record("player_died", new JObject { ["position"] = TelemetryJson.Position(health.transform.position) });
        }

        /// <summary>게임 상태가 바뀌면 마지막 위치 표본을 닫아 일시정지 시간을 제외한다.</summary>
        private void HandleState(GameplayState state)
        {
            if (_stageClosed) return;
            RecordMovement();
        }

        /// <summary>일반 적 처치 사건을 현재 구간의 kills에 더한다.</summary>
        private void HandleKill()
        {
            if (!_stageClosed) Add(_counts, "kills");
        }

        /// <summary>attack의 종류·패턴별 집계 객체를 반환하며 없으면 만든다.</summary>
        private JObject Pattern(JObject attack)
        {
            string key = (string)attack["kind"] + ":" + (string)attack["patternId"];
            if (!(_patterns[key] is JObject value))
            {
                value = new JObject { ["kind"] = attack["kind"], ["patternId"] = attack["patternId"] };
                _patterns[key] = value;
            }
            return value;
        }

        /// <summary>target의 key 숫자에 amount를 더하여 누적 카운터를 갱신한다.</summary>
        private static void Add(JObject target, string key, double amount = 1)
        {
            target[key] = ((double?)target[key] ?? 0) + amount;
        }

        /// <summary>reason과 현재 구간의 누적 값을 복사한다. 비율의 분모가 0이면 null을 반환한다.</summary>
        private JObject Summary(string reason)
        {
            JObject summary = (JObject)_counts.DeepClone();
            summary["reason"] = reason;
            summary["startedKst"] = _stageStartedKst;
            summary["realtimeSeconds"] = Time.realtimeSinceStartupAsDouble - _stageRealtime;
            summary["gameplaySeconds"] = Time.time - _stageGameplay;
            summary["activePlaySeconds"] = _activeSeconds - _stageActive;
            summary["currentHealth"] = _health.CurrentHealth;
            summary["guardAttemptsWithContact"] = _guardContacts.Count;
            summary["guardAttemptsWithoutContact"] = Math.Max(0, ((double?)_counts["guardAttempts"] ?? 0) - _guardContacts.Count);
            summary["reactionCount"] = _reactionCount;
            summary["reactionMeanSeconds"] = _reactionCount > 0 ? (JToken)(_reactionTotal / _reactionCount) : JValue.CreateNull();
            double resolved = (double?)_counts["enemyResolvedCount"] ?? 0;
            summary["enemyHitRate"] = resolved > 0 ? (JToken)(((double?)_counts["enemyHitCount"] ?? 0) / resolved) : JValue.CreateNull();
            string[] groups = { "ranged", "melee", "bossRanged", "bossCharge" };
            foreach (string group in groups)
            {
                double damage = 0, hits = 0, attempts = 0, count = 0, dodges = 0, parries = 0, perfects = 0;
                foreach (JProperty property in _patterns.Properties())
                {
                    JObject pattern = (JObject)property.Value;
                    string patternId = (string)pattern["patternId"];
                    bool matches = group == "ranged" ? (string)pattern["kind"] == "Projectile"
                        : group == "melee" ? (string)pattern["kind"] != "Projectile"
                        : patternId == (group == "bossRanged" ? "BossRanged" : "BossCharge");
                    if (!matches) continue;
                    damage += (double?)pattern["damage"] ?? 0;
                    hits += (double?)pattern["Hit"] ?? 0;
                    attempts += (double?)pattern["attempts"] ?? 0;
                    count += (double?)pattern["resolved"] ?? 0;
                    dodges += (double?)pattern["Dodge"] ?? 0;
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
            summary["serviceDroppedEventsTotal"] = _service.DroppedEvents;
            if (gameObject.scene.name == "BambooStage1" && !string.IsNullOrEmpty(OnboardingTelemetryRecorder.CompletedRunId))
                summary["runId"] = OnboardingTelemetryRecorder.CompletedRunId;
            return summary;
        }

        /// <summary>reason으로 남은 구간과 세션을 닫고 저장을 요청한다. 반복 호출은 무시한다.</summary>
        private void Close(string reason)
        {
            if (!_bound || _closed) return;
            FinishStage((_counts.Value<int?>("deaths") ?? 0) > 0 ? "death" : reason);
            Record("session_ended", new JObject { ["reason"] = reason });
            _service.RequestFlush();
            _closed = true;
        }

        void OnApplicationQuit() { Close("application_quit"); }

        void OnDisable()
        {
            Close("scene_unloaded");
            if (!_bound) return;
            _health.Died -= HandleDeath;
            _combat.Events.InputResolved -= HandleInput;
            _combat.Events.DodgeChanged -= HandleDodge;
            _state.StateChanged -= HandleState;
            if (_spawner != null) _spawner.EnemyKilled -= HandleKill;
            CombatTelemetrySignals.Raised -= HandleSignal;
            _bound = false;
        }
    }
}
