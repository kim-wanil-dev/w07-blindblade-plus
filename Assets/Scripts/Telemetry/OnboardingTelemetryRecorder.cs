using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.Telemetry
{
    /// <summary>온보딩 7단계의 목표별 입력과 재시도 결과를 기존 로컬 저장·업로드 서비스에 기록한다.</summary>
    [DisallowMultipleComponent]
    public sealed class OnboardingTelemetryRecorder : MonoBehaviour
    {
        private const int STAGE_COUNT = 7;
        private static string _runId;
        private static bool _completed;
        private static readonly int[] _retryCounts = new int[STAGE_COUNT];

        [Header("온보딩 단계 로그")]
        [SerializeField] private string _configVersion = "onboarding-2026-10-16-stage-ids-7";
        [SerializeField, Min(5f)] private float _summaryIntervalSeconds = 15f;

        [Header("로컬 저장과 자동 전송")]
        [SerializeField] private bool _recordingEnabled = true;
        [SerializeField] private bool _allowAutomaticUpload = true;
        [SerializeField, Min(64)] private int _bufferLimit = 4096;
        [SerializeField, Min(0.2f)] private float _flushIntervalSeconds = 2f;
        [SerializeField, Min(1)] private int _maximumStorageMegabytes = 100;
        [SerializeField, Min(5f)] private float _uploadIntervalSeconds = 20f;
        [SerializeField, Range(1, 100)] private int _batchEventLimit = 100;
        [SerializeField, Range(5, 120)] private int _requestTimeoutSeconds = 30;
        [SerializeField, Min(30f)] private float _maximumRetrySeconds = 300f;

        private readonly Dictionary<int, JObject> _attempts = new Dictionary<int, JObject>();
        private OnboardingFlowController _flow;
        private CombatSessionRuntime _runtime;
        private CombatTelemetryService _service;
        private PlayerHealth _health;
        private PlayerMotor _motor;
        private KatanaCombatCoordinator _combat;
        private string _sessionId;
        private string _sectionId;
        private string _sectionStartedKst;
        private int _stage = -1;
        private int _objective = -1;
        private int _sectionCompleted;
        private int _sectionAttempts;
        private int _sectionDeaths;
        private int _stepMovementAttempts;
        private int _stepWrongAttempts;
        private int _stepExpectedAttempts;
        private long _sequence;
        private double _sectionActive;
        private double _stepActive;
        private double _firstInputSeconds = -1d;
        private double _nextSummary;
        private bool _wasMoving;
        private bool _stepOpen;
        private bool _sectionOpen;
        private bool _closing;
        private bool _bound;

        public static string CompletedRunId => _completed ? _runId : null;

        /// <summary>Intro의 새 실행 요청에서 실행 ID와 단계별 사망 재시도 횟수를 초기화한다.</summary>
        public static void BeginNewRun()
        {
            _runId = Guid.NewGuid().ToString("N");
            _completed = false;
            Array.Clear(_retryCounts, 0, _retryCounts.Length);
        }

        void Awake()
        {
            _flow = GetComponent<OnboardingFlowController>();
            _flow.ObjectiveEntered += HandleObjectiveEntered;
            _flow.ObjectiveCompleted += HandleObjectiveCompleted;
            _flow.StageRetrying += HandleStageRetrying;
            _flow.OnboardingCompleted += HandleOnboardingCompleted;
            _flow.ObjectiveDefenseResolved += HandleDefense;
        }

        void Update()
        {
            if (!_stepOpen || !_flow.IsObjectiveInputAvailable) return;
            double delta = Time.unscaledDeltaTime;
            _stepActive += delta;
            _sectionActive += delta;
            if (_stage == 0)
            {
                bool moving = _motor.HorizontalSpeed > 0.05f;
                if (moving && !_wasMoving)
                {
                    _stepMovementAttempts++;
                    _stepExpectedAttempts++;
                    _sectionAttempts++;
                    MarkFirstInput();
                    Record("onboarding_attempt", new JObject { ["runId"] = _runId, ["stepIndex"] = _stage,
                        ["objectiveIndex"] = _objective, ["kind"] = "Move", ["status"] = "Started", ["expected"] = true });
                }
                _wasMoving = moving;
            }
            if (Time.realtimeSinceStartupAsDouble < _nextSummary) return;
            Record("stage_summary", SectionSummary("in_progress"));
            _nextSummary = Time.realtimeSinceStartupAsDouble + _summaryIntervalSeconds;
        }

        /// <summary>stepIndex와 objectiveIndex의 실제 목표 시작 사건에서 구간·목표 집계를 연다.</summary>
        private void HandleObjectiveEntered(int stepIndex, int objectiveIndex)
        {
            if (!_recordingEnabled) return;
            if (stepIndex < 0 || stepIndex >= STAGE_COUNT) return;
            if (!_bound && !BindSources()) return;
            string nextSection = "0-" + (stepIndex + 1);
            if (_sectionOpen && nextSection != _sectionId) CloseSection("cleared");
            _stage = stepIndex;
            _objective = objectiveIndex;
            if (!_sectionOpen) OpenSection(nextSection);
            _stepActive = 0d;
            _firstInputSeconds = -1d;
            _stepMovementAttempts = 0;
            _stepWrongAttempts = 0;
            _stepExpectedAttempts = 0;
            _wasMoving = false;
            _attempts.Clear();
            _stepOpen = true;
        }

        /// <summary>현재 목표의 성공 판정을 받아 시도 결과와 완료 요약을 기록한다.</summary>
        private void HandleObjectiveCompleted(int stepIndex, int objectiveIndex)
        {
            if (_stepOpen && _stage == stepIndex && _objective == objectiveIndex) CloseStep("completed");
        }

        /// <summary>사망 재시도를 구간 종료로 기록하고 해당 단계의 다음 진입 재시도 횟수를 증가시킨다.</summary>
        private void HandleStageRetrying(int stepIndex)
        {
            if (_sectionOpen)
            {
                _sectionDeaths = 1;
                CloseStep("death");
                CloseSection("death");
            }
            if (stepIndex >= 0 && stepIndex < STAGE_COUNT) _retryCounts[stepIndex]++;
        }

        /// <summary>마지막 목표 성공 후 구간을 완료로 닫고 남은 로컬 로그 저장을 요청한다.</summary>
        private void HandleOnboardingCompleted()
        {
            _completed = true;
            CloseSection("cleared");
        }

        /// <summary>공통 세션과 플레이어 입력 공급자를 연결하고 기존 저장 서비스를 반환한다.</summary>
        private bool BindSources()
        {
            _runtime = CombatSessionRuntime.Active;
            if (_runtime == null || !_runtime.IsInitialized || Game.Player == null) return false;
            if (string.IsNullOrEmpty(_runId)) BeginNewRun();
            _sessionId = _runtime.Session.SessionId;
            _service = CombatTelemetryService.GetOrCreate(_bufferLimit, _flushIntervalSeconds, _maximumStorageMegabytes,
                _allowAutomaticUpload, _uploadIntervalSeconds, _batchEventLimit, _requestTimeoutSeconds, _maximumRetrySeconds);
            _health = Game.Player.GetComponent<PlayerHealth>();
            _health.Died += HandleDeath;
            _motor = Game.Player.GetComponent<PlayerMotor>();
            _combat = Game.Player.GetComponent<KatanaCombatCoordinator>();
            _combat.Events.InputResolved += HandleInput;
            _bound = true;
            return true;
        }

        /// <summary>stageId의 새 도전을 열고 해당 씬 방문의 누적 카운터를 초기화한다.</summary>
        private void OpenSection(string stageId)
        {
            _sectionId = stageId;
            _sectionStartedKst = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            _sectionActive = 0d;
            _sectionCompleted = 0;
            _sectionAttempts = 0;
            _sectionDeaths = 0;
            _sectionOpen = true;
            _nextSummary = Time.realtimeSinceStartupAsDouble + _summaryIntervalSeconds;
            Record("stage_started", SectionSummary("started"));
        }

        /// <summary>입력 ID당 최종 상태 한 건만 기록하고 목표 완료·사망·이탈 요약을 만든다.</summary>
        private void CloseStep(string reason)
        {
            if (!_stepOpen) return;
            int started = 0, rejected = 0, buffered = 0, dropped = 0;
            foreach (KeyValuePair<int, JObject> item in _attempts)
            {
                JObject input = item.Value;
                string status = (string)input["status"];
                if (status == "Started") started++;
                else if (status == "Rejected") rejected++;
                else if (status == "BufferDropped") dropped++;
                else buffered++;
                Record("onboarding_attempt", input, item.Key);
            }
            if (reason == "completed") _sectionCompleted++;
            Record("onboarding_step", new JObject { ["runId"] = _runId, ["stepIndex"] = _stage,
                ["objectiveIndex"] = _objective, ["retryCount"] = _retryCounts[_stage],
                ["activeSeconds"] = _stepActive, ["firstInputSeconds"] = _firstInputSeconds >= 0 ? (JToken)_firstInputSeconds : JValue.CreateNull(),
                ["attempts"] = _stepExpectedAttempts, ["wrongAttempts"] = _stepWrongAttempts,
                ["movementAttempts"] = _stepMovementAttempts, ["startedInputs"] = started,
                ["rejectedInputs"] = rejected, ["bufferedInputs"] = buffered, ["droppedInputs"] = dropped,
                ["reason"] = reason });
            _stepOpen = false;
        }

        /// <summary>현재 온보딩 단계의 마지막 목표와 누적값을 저장하고 전송을 요청한다.</summary>
        private void CloseSection(string reason)
        {
            if (!_sectionOpen) return;
            CloseStep(reason);
            Record("stage_ended", SectionSummary(reason));
            _sectionOpen = false;
            _service.RequestFlush();
        }

        /// <summary>현재 구간의 실행 ID, 완료 목표 수, 입력·사망·이탈 위치를 반환한다.</summary>
        private JObject SectionSummary(string reason)
        {
            return new JObject { ["runId"] = _runId, ["startedKst"] = _sectionStartedKst,
                ["reason"] = reason, ["activePlaySeconds"] = _sectionActive,
                ["stepsCompleted"] = _sectionCompleted, ["attempts"] = _sectionAttempts,
                ["deaths"] = _sectionDeaths, ["exitStepIndex"] = _stage,
                ["exitObjectiveIndex"] = _objective, ["retryCount"] = _stage >= 0 ? _retryCounts[_stage] : 0 };
        }

        /// <summary>한 번 누른 전투 입력의 최초와 최종 처리 결과를 목표의 시도 목록에 반영한다.</summary>
        private void HandleInput(PlayerInputRecord input)
        {
            if (!_stepOpen || !_flow.IsObjectiveInputAvailable || input.Kind == PlayerInputKind.GuardRelease
                || input.Kind == PlayerInputKind.GuardHoldResume || input.InputId <= 0) return;
            if (_attempts.TryGetValue(input.InputId, out JObject existing))
            {
                existing["status"] = input.Status.ToString();
                if (input.Status == PlayerInputStatus.Rejected) existing["rejectReason"] = input.RejectReason.ToString();
                return;
            }
            bool expected = IsExpectedInput(input.Kind);
            _attempts.Add(input.InputId, new JObject { ["runId"] = _runId, ["stepIndex"] = _stage,
                ["objectiveIndex"] = _objective, ["kind"] = input.Kind.ToString(),
                ["status"] = input.Status.ToString(), ["rejectReason"] = input.RejectReason.ToString(),
                ["expected"] = expected });
            if (expected) _stepExpectedAttempts++;
            else _stepWrongAttempts++;
            _sectionAttempts++;
            MarkFirstInput();
        }

        /// <summary>현재 목표가 요구하는 전투 입력 종류와 kind가 같은지 반환한다.</summary>
        private bool IsExpectedInput(PlayerInputKind kind)
        {
            if (_stage == 1 || _stage == 3) return kind == PlayerInputKind.Dodge;
            if (_stage == 4) return kind == PlayerInputKind.GuardPress;
            if (_stage == 5) return (kind == PlayerInputKind.Attack && _objective == 2)
                || (kind == PlayerInputKind.GuardPress && _objective < 2);
            if (_stage == 6) return (kind == PlayerInputKind.Attack && _objective == 1)
                || (kind == PlayerInputKind.GuardPress && _objective == 0);
            if (_stage == 2) return kind == PlayerInputKind.Attack;
            return false;
        }

        /// <summary>실제 방어 접촉 결과를 연결된 가드 입력의 결과에 덧붙인다.</summary>
        private void HandleDefense(DefenseResolution resolution)
        {
            if (!_stepOpen || resolution.GuardInputId <= 0) return;
            if (_attempts.TryGetValue(resolution.GuardInputId, out JObject input))
                input["defenseOutcome"] = resolution.Outcome.ToString();
        }

        /// <summary>플레이어 사망 사건에서 현재 온보딩 구간의 사망 횟수를 한 번 표시한다.</summary>
        private void HandleDeath(PlayerHealth health)
        {
            if (_sectionOpen) _sectionDeaths = 1;
        }

        /// <summary>목표가 입력을 받기 시작한 뒤 최초 유효 입력까지의 활성 시간을 한 번 저장한다.</summary>
        private void MarkFirstInput()
        {
            if (_firstInputSeconds < 0d) _firstInputSeconds = _stepActive;
        }

        /// <summary>기존 전투 로그 봉투로 type과 payload 한 건을 저장 서비스에 넣는다.</summary>
        private void Record(string type, JObject payload, int inputId = 0)
        {
            if (_service == null || _closing) return;
            CombatTimestamp stamp = _runtime.CaptureTimestamp();
            string kst = TelemetryJson.FormatKoreanTime(TelemetryJson.CurrentKoreanTime());
            long sequence = ++_sequence;
            _service.Enqueue(new JObject { ["schemaVersion"] = TelemetryJson.SCHEMA_VERSION,
                ["contractVersion"] = "0.2.0", ["configVersion"] = Application.isEditor ? _configVersion + "-editor" : _configVersion,
                ["buildVersion"] = Application.version, ["sessionId"] = _sessionId,
                ["anonymousPlayerId"] = _service.AnonymousPlayerId, ["sessionGeneration"] = _runtime.Session.Generation,
                ["stageId"] = _sectionId, ["stageAttemptId"] = _sessionId + ":" + _sectionId,
                ["eventSequence"] = sequence, ["sourceId"] = "onboarding.telemetry", ["sourceSequence"] = sequence,
                ["eventType"] = type, ["occurredAt"] = TelemetryJson.Timestamp(stamp),
                ["recordedAt"] = TelemetryJson.Timestamp(stamp), ["occurredAtKst"] = kst, ["recordedAtKst"] = kst,
                ["correlation"] = TelemetryJson.Correlation(new CombatCorrelationIds(inputId, 0, 0, 0, 0, 0)),
                ["payload"] = payload });
        }

        void OnApplicationQuit()
        {
            CloseSection("application_quit");
        }

        void OnDisable()
        {
            CloseSection(_health != null && !_health.IsAlive ? "death" : "scene_unloaded");
            _closing = true;
            if (_flow != null)
            {
                _flow.ObjectiveEntered -= HandleObjectiveEntered;
                _flow.ObjectiveCompleted -= HandleObjectiveCompleted;
                _flow.StageRetrying -= HandleStageRetrying;
                _flow.OnboardingCompleted -= HandleOnboardingCompleted;
                _flow.ObjectiveDefenseResolved -= HandleDefense;
            }
            if (!_bound) return;
            _health.Died -= HandleDeath;
            _combat.Events.InputResolved -= HandleInput;
            _bound = false;
        }
    }
}
