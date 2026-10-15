using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;
using CombatDefenseOutcome = BlindBlade.Combat.Contracts.DefenseOutcome;
using CombatDefenseResolution = BlindBlade.Combat.Contracts.DefenseResolution;

namespace BlindBlade.Telemetry
{
    /// <summary>현재 전투의 확정 사건을 읽기 전용으로 구독한다. 전투 판정·HP·시간 배율을 변경하지 않는다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(200)]
    public sealed class CombatTelemetryRecorder : MonoBehaviour
    {
        [Header("사건 공급자")]
        [SerializeField] private PlayerHealth _health;
        [SerializeField] private BambooDefenseDirector _director;
        [SerializeField] private DefenseEnemySpawner _spawner;
        [SerializeField] private CombatThreatPresentationProvider _presentation;

        [Header("원본 저장")]
        [SerializeField] private bool _recordingEnabled = true;
        [SerializeField] private string _configVersion = "bamboo-2026-10-14";
        [SerializeField, Min(64)] private int _bufferLimit = 4096;
        [SerializeField, Min(0.2f)] private float _flushIntervalSeconds = 2f;
        [SerializeField, Min(1)] private int _maximumStorageMegabytes = 100;
        [SerializeField, Min(1f)] private float _sampleIntervalSeconds = 5f;

        [Header("Google Sheets 전송 — 빌드 기본 설정 / 로컬 CONFIG 재정의")]
        [SerializeField] private bool _allowAutomaticUpload = true;
        [SerializeField, Min(5f)] private float _uploadIntervalSeconds = 20f;
        [SerializeField, Range(1, 100)] private int _batchEventLimit = 100;
        [SerializeField, Range(5, 120)] private int _requestTimeoutSeconds = 30;
        [SerializeField, Min(30f)] private float _maximumRetrySeconds = 300f;

        private readonly Dictionary<CombatEntityKey, long> _footsteps = new Dictionary<CombatEntityKey, long>();
        private readonly Dictionary<CombatEntityKey, int> _threatStates = new Dictionary<CombatEntityKey, int>();
        private CombatSessionRuntime _runtime;
        private CombatSessionKey _session;
        private CombatTimestamp _lastTimestamp;
        private DateTimeOffset _sessionStartedAtKst;
        private CombatTelemetryService _service;
        private PlayerController _player;
        private KatanaCombatCoordinator _combat;
        private GameStateManager _state;
        private long _sequence;
        private double _nextSample;
        private double _combatStartedAt = -1d;
        private double _outcomeRealtime = -1d;
        private float _lastHealth;
        private float _lastScale;
        private float _damageTaken;
        private float _pendingDamage;
        private int _defenseCount;
        private int _parryCount;
        private int _kills;
        private string _endReason = "scene_unloaded";
        private bool _bound;
        private bool _closed;
        private bool _combatStarted;
        private bool _outcomeRecorded;

        void Start()
        {
            if (!_recordingEnabled) return;
            _runtime = CombatSessionRuntime.Active;
            if (_runtime == null || !_runtime.IsInitialized || _health == null || !Game.IsInitialized)
            {
                Debug.LogWarning("전투 로그 공급자가 준비되지 않아 이 세션을 기록하지 않습니다.", this);
                return;
            }
            _session = _runtime.Session;
            _sessionStartedAtKst = TelemetryJson.CurrentKoreanTime();
            _lastTimestamp = _runtime.CaptureTimestamp();
            _player = _health.GetComponent<PlayerController>();
            _combat = _health.GetComponent<KatanaCombatCoordinator>();
            _state = Game.Instance.GameStateManager;
            _service = CombatTelemetryService.GetOrCreate(_bufferLimit, _flushIntervalSeconds, _maximumStorageMegabytes,
                _allowAutomaticUpload, _uploadIntervalSeconds, _batchEventLimit, _requestTimeoutSeconds, _maximumRetrySeconds);
            _lastHealth = _health.CurrentHealth;
            _lastScale = Time.timeScale;
            _health.DamageReceived += HandleDamage;
            _health.HealthChanged += HandleHealth;
            _health.Died += HandleDeath;
            _player.ActionEvent += HandleAction;
            _state.StateChanged += HandleState;
            if (_combat != null)
            {
                _combat.EventRaised += HandleKatana;
                _combat.Events.DefenseResolved += HandleDefense;
                _combat.Events.InputResolved += HandleInput;
                _combat.Events.DodgeChanged += HandleDodge;
                _combat.Events.StaminaChanged += HandleStamina;
                _combat.Events.EmpowerChanged += HandleEmpower;
                _combat.Events.RevealPulseRequested += HandleReveal;
            }
            if (_director != null)
            {
                _director.ShurikenLaunched += HandleProjectile;
                _director.ThrowTelegraphed += HandleTelegraph;
            }
            if (_spawner != null) _spawner.EnemyKilled += HandleKill;
            if (_presentation != null)
            {
                _presentation.ThreatChanged += HandleThreat;
                _presentation.FootstepChanged += HandleFootstep;
            }
            _bound = true;
            Record("session_started", new JObject { ["startedKst"] = TelemetryJson.FormatKoreanTime(_sessionStartedAtKst),
                ["scene"] = gameObject.scene.path, ["unityVersion"] = Application.unityVersion,
                ["platform"] = Application.platform.ToString(), ["isEditor"] = Application.isEditor,
                ["clockRealtimeOrigin"] = Time.realtimeSinceStartupAsDouble - _runtime.CaptureTimestamp().RealtimeSeconds,
                ["clockGameplayOrigin"] = Time.time - _runtime.CaptureTimestamp().GameplaySeconds,
                ["inputDeviceClockCalibrated"] = false,
                ["inputProducerPresent"] = false, ["staminaProducerPresent"] = false,
                ["observerIdentityPolicy"] = "D5 assigns source sequence on receipt; missing IDs stay zero",
                ["bufferLimit"] = _bufferLimit, ["sampleIntervalSeconds"] = _sampleIntervalSeconds,
                ["sessionSettings"] = CaptureSettings() });
            HandleState(_state.State);
            Record("resource_changed", new JObject { ["resource"] = "health", ["reason"] = "initial",
                ["current"] = _health.CurrentHealth, ["maximum"] = _health.MaxHealth, ["delta"] = 0f });
            _service.RequestFlush();
        }

        void LateUpdate()
        {
            if (!_bound || _closed) return;
            if (_runtime != null && _runtime.IsInitialized) _lastTimestamp = _runtime.CaptureTimestamp();
            if (_director != null && !_combatStarted && _director.HasStarted)
            {
                _combatStarted = true;
                _combatStartedAt = _lastTimestamp.RealtimeSeconds;
                Record("combat_started", new JObject { ["survivalGameSeconds"] = _director.SurvivalTime });
            }
            if (!Mathf.Approximately(_lastScale, Time.timeScale))
            {
                Record("time_scale_changed", new JObject { ["previous"] = _lastScale, ["current"] = Time.timeScale });
                _lastScale = Time.timeScale;
            }
            if (_state.State == GameplayState.GameOver && !_outcomeRecorded)
            {
                _outcomeRecorded = true;
                Record("run_ended", Summary("game_over"));
                _service.RequestFlush();
            }
            if (_state.State == GameplayState.Playing && Time.realtimeSinceStartupAsDouble >= _nextSample)
            {
                _nextSample = Time.realtimeSinceStartupAsDouble + Mathf.Max(1f, _sampleIntervalSeconds);
                Record("progress_sample", Summary("sample"));
            }
        }

        /// <summary>현재 공급자의 설정을 시작 메타데이터로 복사한다. 상태를 변경하지 않고 재현용 JSON 문자열을 반환한다.</summary>
        private JObject CaptureSettings()
        {
            return new JObject { ["director"] = _director != null ? JsonUtility.ToJson(_director) : "unavailable",
                ["spawner"] = _spawner != null ? JsonUtility.ToJson(_spawner) : "unavailable",
                ["health"] = JsonUtility.ToJson(_health),
                ["combat"] = _combat != null ? JsonUtility.ToJson(_combat) : "unavailable" };
        }

        /// <summary>type·payload·ids를 세션 봉투에 담아 저장기에 전달한다. 수신 순번과 발생·수신 시계를 보존한다.</summary>
        private void Record(string type, JObject payload, CombatCorrelationIds ids = default,
            CombatEventIdentity? source = null, CombatTimestamp? occurredAt = null)
        {
            if (_closed || _service == null) return;
            if (_runtime != null && _runtime.IsInitialized && _runtime.Session.SessionId == _session.SessionId)
                _lastTimestamp = _runtime.CaptureTimestamp();
            long sequence = ++_sequence;
            CombatEventIdentity identity = source ?? new CombatEventIdentity(_session, "D5.observer", sequence);
            CombatTimestamp occurredTimestamp = occurredAt ?? _lastTimestamp;
            DateTimeOffset recordedAtKst = TelemetryJson.CurrentKoreanTime();
            string occurredAtKst = TelemetryJson.KoreanWallClock(recordedAtKst, _lastTimestamp.RealtimeSeconds, occurredTimestamp);
            CombatTelemetryEnvelope envelope = new CombatTelemetryEnvelope(identity, occurredTimestamp, ids, type);
            _service.Enqueue(new JObject { ["schemaVersion"] = TelemetryJson.SCHEMA_VERSION,
                ["contractVersion"] = "0.2.0", ["configVersion"] = string.IsNullOrWhiteSpace(_configVersion) ? "unspecified" : _configVersion,
                ["buildVersion"] = Application.version, ["sessionId"] = _session.SessionId,
                ["anonymousPlayerId"] = _service.AnonymousPlayerId,
                ["sessionGeneration"] = _session.Generation, ["eventSequence"] = sequence,
                ["sourceId"] = envelope.Identity.SourceId, ["sourceSequence"] = envelope.Identity.SourceSequence,
                ["eventType"] = envelope.EventType, ["occurredAt"] = TelemetryJson.Timestamp(envelope.OccurredAt),
                ["occurredAtKst"] = occurredAtKst,
                ["recordedAt"] = TelemetryJson.Timestamp(_lastTimestamp),
                ["recordedAtKst"] = TelemetryJson.FormatKoreanTime(recordedAtKst),
                ["correlation"] = TelemetryJson.Correlation(envelope.Correlation), ["payload"] = payload });
        }

        /// <summary>context의 피해 요청과 실제 HP 감소량을 기록한다. 피해 집계는 이 사건만 권위 있는 원본으로 사용한다.</summary>
        private void HandleDamage(DamageContext context)
        {
            // HealthChanged가 먼저 전달되므로 delta는 직전 처리기에서 보관한다.
            _damageTaken += _pendingDamage;
            Record("damage_applied", new JObject { ["requestedDamage"] = context.Amount, ["appliedDamage"] = _pendingDamage,
                ["currentHealth"] = _health.CurrentHealth, ["kind"] = context.Kind.ToString(),
                ["patternId"] = context.PatternId, ["position"] = TelemetryJson.Position(context.Point) },
                new CombatCorrelationIds(0, 0, context.AttackId, context.HitId, 0, 0));
            _pendingDamage = 0f;
        }

        /// <summary>current·maximum을 이전 HP와 비교하여 자원 변경과 다음 피해 사건용 실제 감소량을 저장한다.</summary>
        private void HandleHealth(float current, float maximum)
        {
            float delta = current - _lastHealth;
            _pendingDamage = Mathf.Max(0f, -delta);
            _lastHealth = current;
            Record("resource_changed", new JObject { ["resource"] = "health", ["current"] = current,
                ["maximum"] = maximum, ["delta"] = delta });
        }

        /// <summary>health의 사망을 기록한다. 최종 피해·방어 판정 구독은 세션 종료까지 유지한다.</summary>
        private void HandleDeath(PlayerHealth health)
        {
            Record("player_died", Summary("death"));
            _service.RequestFlush();
        }

        /// <summary>state 변경을 기록한다. GameOver에서 결과 시각을 고정하고 재도전 요청은 별도 종료 이유로 저장한다.</summary>
        private void HandleState(GameplayState state)
        {
            Record("state_changed", new JObject { ["state"] = state.ToString() });
            if (state == GameplayState.GameOver && _outcomeRealtime < 0d)
            {
                _outcomeRealtime = _lastTimestamp.RealtimeSeconds;
                _endReason = "game_over";
                _service.RequestFlush();
            }
            if (state == GameplayState.Restarting) _endReason = "retry";
        }

        /// <summary>action의 단계·결과·대상과 기존 ActionId를 기록한다. 적중·처치를 입력 성공으로 추정하지 않는다.</summary>
        private void HandleAction(PlayerActionEvent action)
        {
            Record("action_phase", new JObject { ["stage"] = action.Stage.ToString(), ["state"] = action.State.ToString(),
                ["outcome"] = action.Outcome.ToString(), ["targetId"] = action.TargetId,
                ["position"] = TelemetryJson.Position(action.WorldPosition) },
                new CombatCorrelationIds(0, action.ActionId, 0, 0, 0, 0));
        }

        /// <summary>value의 카타나 표현 단계를 기록한다. 확정 피해·방어 통계와 별도 사건으로 구분한다.</summary>
        private void HandleKatana(KatanaEvent value)
        {
            Record("katana_phase", new JObject { ["stage"] = value.Stage.ToString(), ["strike"] = value.Strike,
                ["wasDrawn"] = value.WasDrawn, ["detail"] = value.Detail },
                new CombatCorrelationIds(0, value.ActionId, 0, 0, 0, 0));
        }

        /// <summary>
        /// value의 실제 방어 결과와 공급자 ID·원본 시계를 기록한다. 피해량을 다시 합산하지 않는다.
        /// 공통 방어 결과가 제공하지 않는 DodgeId는 미부여 값 0으로 보존한다.
        /// </summary>
        private void HandleDefense(CombatDefenseResolution value)
        {
            _defenseCount++;
            if (value.Outcome == CombatDefenseOutcome.NormalParry || value.Outcome == CombatDefenseOutcome.PerfectParry)
                _parryCount++;
            Record("defense_resolved", new JObject { ["outcome"] = value.Outcome.ToString(),
                ["appliedDamage"] = value.AppliedDamage, ["position"] = TelemetryJson.Position(value.ContactPoint),
                ["sourceRealtime"] = value.RealTime, ["sourceGameTime"] = value.GameTime },
                new CombatCorrelationIds(value.GuardInputId, 0, value.AttackId, value.HitId, 0, 0));
        }

        /// <summary>value의 공급자 입력 기록을 보존한다. 장치 시계는 공통 시계와 보정 없이 직접 차감하지 않는다.</summary>
        private void HandleInput(PlayerInputRecord value)
        {
            Record("input_resolved", new JObject { ["kind"] = value.Kind.ToString(), ["status"] = value.Status.ToString(),
                ["deviceTime"] = value.DeviceTime, ["sourceRealtime"] = value.FrameRealTime, ["sourceGameTime"] = value.GameTime },
                new CombatCorrelationIds(value.InputId, value.ActionId, 0, 0, 0, 0));
        }

        /// <summary>value의 회피 단계·공격 연결·위치를 기록한다. 실제 회피 판정 공급자가 발행한 사건만 사용한다.</summary>
        private void HandleDodge(PlayerDodgeEvent value)
        {
            Record("dodge_changed", new JObject { ["stage"] = value.Stage.ToString(), ["position"] = TelemetryJson.Position(value.Position),
                ["sourceRealtime"] = value.RealTime, ["sourceGameTime"] = value.GameTime },
                new CombatCorrelationIds(0, 0, value.AttackId, 0, 0, value.DodgeId));
        }

        /// <summary>value의 공급자 스태미너 변경 값을 기록한다. 공급자가 없으면 가상 값을 생성하지 않는다.</summary>
        private void HandleStamina(PlayerStaminaEvent value)
        {
            Record("resource_changed", new JObject { ["resource"] = "stamina", ["current"] = value.Current,
                ["maximum"] = value.Max, ["delta"] = value.Delta, ["reason"] = value.Reason.ToString() },
                new CombatCorrelationIds(0, 0, 0, 0, 0, value.DodgeId));
        }

        /// <summary>value의 강화 단계·종료 이유와 기존 행동·공격 연결을 기록한다.</summary>
        private void HandleEmpower(PlayerEmpowerEvent value)
        {
            Record("empower_changed", new JObject { ["empowerId"] = value.EmpowerId, ["stage"] = value.Stage.ToString(),
                ["endReason"] = value.EndReason.ToString() },
                new CombatCorrelationIds(0, value.ActionId, value.SourceAttackId, 0, 0, 0));
        }

        /// <summary>value의 실제 파동 요청 원인·중심과 관련 공격 ID를 기록한다.</summary>
        private void HandleReveal(PlayerRevealPulseEvent value)
        {
            Record("reveal_pulse", new JObject { ["pulseId"] = value.PulseId, ["cause"] = value.Cause.ToString(),
                ["position"] = TelemetryJson.Position(value.Center) }, new CombatCorrelationIds(0, 0, value.AttackId, 0, 0, 0));
        }

        /// <summary>value의 발사 시점과 기존 공격·투사체 ID를 기록한다. 수명 종료나 반사를 추정하지 않는다.</summary>
        private void HandleProjectile(DefenseShuriken value)
        {
            Record("projectile_lifecycle", new JObject { ["stage"] = "launched", ["position"] = TelemetryJson.Position(value.transform.position) },
                new CombatCorrelationIds(0, 0, value.AttackId, 0, value.ProjectileId, 0));
        }

        /// <summary>position과 duration의 실제 투척 전조를 기록한다. 아직 없는 AttackId는 발급하지 않는다.</summary>
        private void HandleTelegraph(Vector3 position, float duration)
        {
            Record("throw_telegraph", new JObject { ["position"] = TelemetryJson.Position(position), ["duration"] = duration });
        }

        /// <summary>스포너의 확정 처치 사건을 한 건 합산한다. 제공되지 않은 적 ID·공격 ID는 만들지 않는다.</summary>
        private void HandleKill()
        {
            _kills++;
            Record("enemy_lifecycle", new JObject { ["stage"] = "killed", ["kills"] = _kills, ["entityIdentityAvailable"] = false });
        }

        /// <summary>value의 표시 플래그가 바뀔 때만 기록한다. 방향·알파의 매 프레임 갱신은 저장하지 않는다.</summary>
        private void HandleThreat(ThreatPresentationSnapshot value)
        {
            if (value.Session.SessionId != _session.SessionId) return;
            int flags = (value.IsActive ? 1 : 0) | (value.IsEligible ? 2 : 0) | (value.IsInCamera ? 4 : 0)
                | (value.WorldVisible ? 8 : 0) | (value.UiVisible ? 16 : 0);
            if (_threatStates.TryGetValue(value.Target, out int previous) && previous == flags) return;
            if (value.IsActive) _threatStates[value.Target] = flags;
            else _threatStates.Remove(value.Target);
            Record("threat_presentation_changed", new JObject { ["entityId"] = value.Target.EntityId,
                ["spawnGeneration"] = value.Target.SpawnGeneration, ["revision"] = value.Revision,
                ["active"] = value.IsActive, ["eligible"] = value.IsEligible, ["inCamera"] = value.IsInCamera,
                ["worldVisible"] = value.WorldVisible, ["uiVisible"] = value.UiVisible,
                ["position"] = TelemetryJson.Position(value.WorldPosition) });
        }

        /// <summary>value의 실제 발소리를 원본 순번당 한 번 기록한다. 표시 갱신은 새 걸음으로 집계하지 않는다.</summary>
        private void HandleFootstep(FootstepPresentationSnapshot value)
        {
            if (value.EventIdentity.Session.SessionId != _session.SessionId || !value.Presentation.IsActive) return;
            long sequence = value.EventIdentity.SourceSequence;
            if (_footsteps.TryGetValue(value.Emitter, out long previous) && previous >= sequence) return;
            _footsteps[value.Emitter] = sequence;
            Record("footstep", new JObject { ["entityId"] = value.Emitter.EntityId, ["spawnGeneration"] = value.Emitter.SpawnGeneration,
                ["origin"] = TelemetryJson.Position(value.Origin), ["uiVisible"] = value.Presentation.UiVisible,
                ["eligible"] = value.Presentation.IsEligible }, default, value.EventIdentity, value.OccurredAt);
        }

        /// <summary>reason과 현재 누적 값을 요약한다. 결과 시각 이후 게임오버 화면 대기 시간은 runRealtimeSeconds에서 제외한다.</summary>
        private JObject Summary(string reason)
        {
            double end = _outcomeRealtime >= 0d ? _outcomeRealtime : _lastTimestamp.RealtimeSeconds;
            return new JObject { ["reason"] = reason, ["runRealtimeSeconds"] = end,
                ["combatRealtimeSeconds"] = _combatStartedAt < 0d ? 0d : Math.Max(0d, end - _combatStartedAt),
                ["survivalGameSeconds"] = _director != null ? _director.SurvivalTime : 0f,
                ["kills"] = _kills, ["damageTaken"] = _damageTaken, ["defenseCount"] = _defenseCount,
                ["parryCount"] = _parryCount, ["currentHealth"] = _health != null ? _health.CurrentHealth : _lastHealth,
                ["position"] = _player != null ? TelemetryJson.Position(_player.transform.position) : null,
                ["serviceDroppedEventsTotal"] = _service != null ? _service.DroppedEvents : 0 };
        }

        /// <summary>reasonでセッションを一度だけ閉じ、最終要約を保存要求する。ネットワーク完了は待たない。</summary>
        private void Close(string reason)
        {
            if (!_bound || _closed) return;
            Record("session_ended", Summary(reason));
            _closed = true;
            if (_service != null) _service.RequestFlush();
        }

        void OnApplicationQuit()
        {
            Close(_outcomeRealtime >= 0d ? _endReason : "application_quit");
        }

        void OnDisable()
        {
            Close(_endReason);
            if (!_bound) return;
            if (_health != null)
            {
                _health.DamageReceived -= HandleDamage;
                _health.HealthChanged -= HandleHealth;
                _health.Died -= HandleDeath;
            }
            if (_player != null) _player.ActionEvent -= HandleAction;
            if (_state != null) _state.StateChanged -= HandleState;
            if (_combat != null)
            {
                _combat.EventRaised -= HandleKatana;
                _combat.Events.DefenseResolved -= HandleDefense;
                _combat.Events.InputResolved -= HandleInput;
                _combat.Events.DodgeChanged -= HandleDodge;
                _combat.Events.StaminaChanged -= HandleStamina;
                _combat.Events.EmpowerChanged -= HandleEmpower;
                _combat.Events.RevealPulseRequested -= HandleReveal;
            }
            if (_director != null)
            {
                _director.ShurikenLaunched -= HandleProjectile;
                _director.ThrowTelegraphed -= HandleTelegraph;
            }
            if (_spawner != null) _spawner.EnemyKilled -= HandleKill;
            if (_presentation != null)
            {
                _presentation.ThreatChanged -= HandleThreat;
                _presentation.FootstepChanged -= HandleFootstep;
            }
            _bound = false;
        }
    }
}
