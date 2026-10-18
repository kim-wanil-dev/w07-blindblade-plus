using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

using ContractSnapshot = BlindBlade.Combat.Contracts.ThreatPresentationSnapshot;

namespace BlindBlade.Perception
{
    /// <summary>실제 발걸음에서 적마다 최신 후보 하나를 저장하고 청각 범위와 현재 적 위치로 HUD 결과를 계산한다.</summary>
    [DefaultExecutionOrder(-8)]
    [DisallowMultipleComponent]
    public sealed class EnemyFootstepCandidates : MonoBehaviour
    {
        [Header("발소리 판정 연결")]
        [SerializeField] private ThreatPresentationResolver _resolver;
        [SerializeField] private CombatSessionRuntime _sessionRuntime;
        private readonly Dictionary<CombatEntityKey, Candidate> _candidates = new Dictionary<CombatEntityKey, Candidate>();
        private readonly List<FootstepPresentationSnapshot> _current = new List<FootstepPresentationSnapshot>();
        private readonly List<CombatEntityKey> _removed = new List<CombatEntityKey>();
        private readonly Plane[] _cameraPlanes = new Plane[6];
        private CombatSessionKey _session;
        private bool _hasSession;
        private long _sequence;
        private long _revision;

        public ThreatPresentationResolver Resolver => _resolver;
        public int CandidateCount => _candidates.Count;
        public event Action<FootstepPresentationSnapshot> FootstepChanged;

        private sealed class Candidate
        {
            public EnemyHealth Health { get; set; }
            public bool UsesHealth { get; set; }
            public ThreatPresentationTarget Target { get; set; }
            public Transform EmitterTransform { get; set; }
            public CombatEntityKey Emitter { get; set; }
            public CombatEventIdentity Identity { get; set; }
            public Vector3 Origin { get; set; }
            public CombatTimestamp OccurredAt { get; set; }
            public float Lifetime { get; set; }
            public float FadeDuration { get; set; }
            public Renderer[] Renderers { get; set; }
            public FootstepPresentationSnapshot Snapshot { get; set; }
        }

        void LateUpdate()
        {
            RefreshNow();
        }

        void OnDisable()
        {
            ClearCandidates();
        }

        /// <summary>발행자 emitter의 원본 ID·세대와 실제 발걸음 origin을 읽어 최신 후보를 교체한다. 체력·소리는 변경하지 않는다.</summary>
        public void RecordFootstep(EnemyHealth emitter, Vector3 origin)
        {
            if (!isActiveAndEnabled || emitter == null || !emitter.IsAlive)
                return;
            AddCandidate(new CombatEntityKey(emitter.TargetId, emitter.SpawnGeneration), emitter.transform,
                emitter, emitter.GetComponent<ThreatPresentationTarget>(), origin);
        }

        /// <summary>C 전용 emitter의 활성 생명과 origin으로 최신 후보를 교체한다. 실제 적은 EnemyHealth 입력 경로를 사용한다.</summary>
        public void RecordFootstep(ThreatPresentationTarget emitter, Vector3 origin)
        {
            if (!isActiveAndEnabled || emitter == null || !emitter.isActiveAndEnabled)
                return;
            ThreatSourceState state = emitter.GetSourceState();
            if (!state.IsActive || state.Kind != ThreatTargetKind.Melee)
                return;
            string entityId = string.IsNullOrWhiteSpace(state.EntityId)
                ? $"Perception/{state.Kind}/{state.TargetId}" : state.EntityId;
            AddCandidate(new CombatEntityKey(entityId, state.SpawnGeneration), emitter.transform, null, emitter, origin);
        }

        /// <summary>현재 수명이 유효한 최신 후보의 공통 발소리 결과를 반환한다. 범위 밖·월드 표시 중인 후보도 숨김 결과로 포함한다.</summary>
        public IReadOnlyList<FootstepPresentationSnapshot> GetCurrentFootsteps()
        {
            return _current;
        }

        /// <summary>emitter의 현재 후보가 있으면 원본 오브젝트 이름을 반환한다. 없거나 제거됐으면 원본 문자열 ID를 반환한다.</summary>
        public string GetEmitterName(CombatEntityKey emitter)
        {
            return _candidates.TryGetValue(emitter, out Candidate candidate) && candidate.EmitterTransform != null
                ? candidate.EmitterTransform.name : emitter.EntityId;
        }

        /// <summary>emitter의 현재 후보가 존재하면 발생 당시 복사한 lifetime과 fadeDuration을 반환한다. 없으면 0을 반환한다.</summary>
        public void GetTiming(CombatEntityKey emitter, out float lifetime, out float fadeDuration)
        {
            bool found = _candidates.TryGetValue(emitter, out Candidate candidate);
            lifetime = found ? candidate.Lifetime : 0f;
            fadeDuration = found ? candidate.FadeDuration : 0f;
        }

        /// <summary>현재 게임 시각·청각 범위·적 위치로 후보 결과를 갱신한다. 만료·사망·세대 변경은 종료 발행 후 제거한다.</summary>
        public void RefreshNow()
        {
            if (!TryGetClock(out CombatSessionRuntime runtime))
                return;
            CombatTimestamp now = runtime.CaptureTimestamp();
            GeometryUtility.CalculateFrustumPlanes(_resolver.ViewCamera, _cameraPlanes);
            _current.Clear();
            _removed.Clear();
            foreach (KeyValuePair<CombatEntityKey, Candidate> item in _candidates)
            {
                Candidate candidate = item.Value;
                double age = Math.Max(0d, now.GameplaySeconds - candidate.OccurredAt.GameplaySeconds);
                if (age >= candidate.Lifetime || !IsSameLife(candidate))
                {
                    PublishEnded(candidate);
                    _removed.Add(item.Key);
                    continue;
                }
                candidate.Snapshot = CreateSnapshot(candidate, age);
                _current.Add(candidate.Snapshot);
                FootstepChanged?.Invoke(candidate.Snapshot);
            }
            foreach (CombatEntityKey key in _removed)
                _candidates.Remove(key);
        }

        /// <summary>key의 기존 후보를 종료하고 origin·시각·수명 설정을 새 후보에 복사한다. 후보 수의 별도 상한은 없다.</summary>
        private void AddCandidate(CombatEntityKey key, Transform emitterTransform, EnemyHealth health,
            ThreatPresentationTarget target, Vector3 origin)
        {
            if (!TryGetClock(out CombatSessionRuntime runtime))
                return;
            if (_candidates.TryGetValue(key, out Candidate previous))
                PublishEnded(previous);
            ThreatPresentationSettings settings = _resolver.Settings;
            Candidate candidate = new Candidate
            {
                Health = health,
                UsesHealth = health != null,
                Target = target,
                EmitterTransform = emitterTransform,
                Emitter = key,
                Identity = new CombatEventIdentity(_session, "CFootsteps_" + GetInstanceID(), ++_sequence),
                Origin = origin,
                OccurredAt = runtime.CaptureTimestamp(),
                Lifetime = settings.FootstepLifetime,
                FadeDuration = Mathf.Min(settings.FootstepFadeDuration, settings.FootstepLifetime),
                Renderers = previous != null && previous.EmitterTransform == emitterTransform
                    ? previous.Renderers : emitterTransform.GetComponentsInChildren<Renderer>()
            };
            _candidates[key] = candidate;
            RefreshNow();
        }

        /// <summary>후보의 현재 적 위치·수평 거리·화면 진입과 게임 시간 age로 발소리 표시 결과를 반환한다.</summary>
        private FootstepPresentationSnapshot CreateSnapshot(Candidate candidate, double age)
        {
            Vector3 position = candidate.EmitterTransform.position;
            Vector3 offset = position - _resolver.Observer.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            ThreatPresentationSettings settings = _resolver.Settings;
            float hearingRadius = BambooDefenseDirector.TryGetSoundPerceptionDistance(out float stageDistance)
                ? stageDistance : settings.FootstepHearingRadius;
            bool eligible = hearingRadius > 0f && distance <= hearingRadius;
            bool inCamera = IsInCamera(candidate);
            bool worldVisible = eligible && IsWorldVisible(candidate, inCamera);
            float fade = candidate.FadeDuration > 0f
                ? Mathf.Clamp01((float)((candidate.Lifetime - age) / candidate.FadeDuration)) : 1f;
            Color color = settings.GetDistanceColor(distance);
            float alpha = eligible ? fade * color.a : 0f;
            color.a = 1f;
            Quaternion viewYaw = Quaternion.Euler(0f, _resolver.ViewCamera.transform.eulerAngles.y, 0f);
            Vector3 horizontalOffset = position - _resolver.ViewCamera.transform.position;
            horizontalOffset.y = 0f;
            Vector2 direction = new Vector2(
                Vector3.Dot(horizontalOffset, viewYaw * Vector3.right),
                Vector3.Dot(horizontalOffset, viewYaw * Vector3.forward));
            direction = direction.sqrMagnitude < 0.0001f ? Vector2.up : direction.normalized;
            CombatEntityKey target = new CombatEntityKey(candidate.Emitter.EntityId + "/footstep/" + candidate.Identity.SourceSequence,
                candidate.Emitter.SpawnGeneration);
            ContractSnapshot presentation = new ContractSnapshot(_session, target, ++_revision,
                true, eligible, inCamera, worldVisible, eligible && !inCamera,
                direction, position, settings.GetProximity(distance), color, alpha);
            return new FootstepPresentationSnapshot(candidate.Identity, candidate.Emitter, candidate.Origin,
                candidate.OccurredAt, presentation);
        }

        /// <summary>candidate가 같은 생성 세대의 활성 적인지 반환한다. 사망·비활성화·외부 활성 상태를 함께 확인한다.</summary>
        private bool IsSameLife(Candidate candidate)
        {
            if (candidate.EmitterTransform == null || !candidate.EmitterTransform.gameObject.activeInHierarchy)
                return false;
            if (candidate.UsesHealth)
                return candidate.Health != null && candidate.Health.IsAlive
                    && candidate.Health.SpawnGeneration == candidate.Emitter.SpawnGeneration;
            if (candidate.Target == null || !candidate.Target.isActiveAndEnabled)
                return false;
            ThreatSourceState state = candidate.Target.GetSourceState();
            string entityId = string.IsNullOrWhiteSpace(state.EntityId)
                ? $"Perception/{state.Kind}/{state.TargetId}" : state.EntityId;
            return state.IsActive && state.SpawnGeneration == candidate.Emitter.SpawnGeneration
                && entityId == candidate.Emitter.EntityId;
        }

        /// <summary>candidate의 C 대상 화면 분류 또는 원본 모델 Bounds와 카메라 교차 여부를 반환한다.</summary>
        private bool IsInCamera(Candidate candidate)
        {
            if (candidate.Target != null && candidate.Target.Resolver == _resolver)
                return candidate.Target.Snapshot.IsInCamera;
            foreach (Renderer renderer in candidate.Renderers)
                if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy
                    && GeometryUtility.TestPlanesAABB(_cameraPlanes, renderer.bounds))
                    return true;
            return false;
        }

        /// <summary>C 대상의 실제 표시 요청을 반환한다. 미연결 기존 적은 현재 감각 마스크와 inCamera로 월드 표시를 확인한다.</summary>
        private bool IsWorldVisible(Candidate candidate, bool inCamera)
        {
            if (candidate.Target != null && candidate.Target.Resolver == _resolver)
            {
                ThreatPresentationSnapshot result = candidate.Target.Snapshot;
                return result.ShowWorld && result.Alpha * result.Color.a > 0f;
            }
            if (!inCamera || !Game.IsInitialized || Game.Instance.SensoryManager == null)
                return false;
            SensoryPerceptionMask mask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
            return mask != null && mask.GetVisibility(candidate.EmitterTransform.position) > 0f;
        }

        /// <summary>같은 씬의 활성 D 시계를 반환하고 세션 변경 시 후보·원본 순번을 초기화한다. 필수 연결이 없으면 false다.</summary>
        private bool TryGetClock(out CombatSessionRuntime runtime)
        {
            runtime = _sessionRuntime != null ? _sessionRuntime : CombatSessionRuntime.Active;
            if (runtime == null || !runtime.IsInitialized || runtime.gameObject.scene != gameObject.scene
                || _resolver == null || !_resolver.isActiveAndEnabled || !_resolver.IsSessionActive
                || _resolver.Settings == null || _resolver.Observer == null || _resolver.ViewCamera == null)
            {
                ClearCandidates();
                return false;
            }
            if (!_hasSession || _session.SessionId != runtime.Session.SessionId || _session.Generation != runtime.Session.Generation)
            {
                bool changedSession = _session.SessionId != runtime.Session.SessionId
                    || _session.Generation != runtime.Session.Generation;
                ClearCandidates();
                _session = runtime.Session;
                _hasSession = true;
                if (changedSession)
                {
                    _sequence = 0;
                    _revision = 0;
                }
            }
            return true;
        }

        /// <summary>candidate의 원점·발행자·사건 식별을 보존한 종료 결과를 발행해 해당 최신 표시를 정리한다.</summary>
        private void PublishEnded(Candidate candidate)
        {
            ContractSnapshot previous = candidate.Snapshot.Presentation;
            ContractSnapshot ended = new ContractSnapshot(candidate.Identity.Session, previous.Target, ++_revision,
                false, false, previous.IsInCamera, false, false, previous.UiDirection,
                previous.WorldPosition, previous.Proximity, previous.Color, 0f);
            FootstepChanged?.Invoke(new FootstepPresentationSnapshot(candidate.Identity, candidate.Emitter,
                candidate.Origin, candidate.OccurredAt, ended));
        }

        /// <summary>입력 없이 모든 후보의 종료를 발행하고 목록·세션 연결을 비운다. 재시작 전 발걸음은 다시 사용하지 않는다.</summary>
        private void ClearCandidates()
        {
            foreach (Candidate candidate in _candidates.Values)
                PublishEnded(candidate);
            _candidates.Clear();
            _current.Clear();
            _hasSession = false;
        }
    }
}
