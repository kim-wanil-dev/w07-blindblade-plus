using BlindBlade.Combat.Contracts;
using System;
using System.Collections.Generic;
using UnityEngine;
using CFootstepCandidates = BlindBlade.Perception.EnemyFootstepCandidates;
using CThreatAdapter = BlindBlade.Perception.ThreatPresentationContractAdapter;

/// <summary>연결된 C 위협·발소리 결과를 공통 계약으로 HUD에 제공한다. 미연결 슬롯은 각각 기존 경로를 사용한다.</summary>
[DefaultExecutionOrder(-5)]
[DisallowMultipleComponent]
public sealed class CombatThreatPresentationProvider : MonoBehaviour, IThreatPresentationProvider
{
    [Header("C 위협 판정 연결")]
    [SerializeField, Tooltip("연결하면 C의 월드/UI·색상·Alpha를 그대로 사용한다. 비워 두면 기존 수리검 정책을 유지한다.")]
    private CThreatAdapter _threatPresentationSource;

    [Header("C 발소리 판정 연결")]
    [SerializeField, Tooltip("연결하면 실제 발걸음을 C 최신 후보로 전달하고 청각 범위·게임 시간 수명을 사용한다.")]
    private CFootstepCandidates _footstepCandidateSource;

    [Header("방향 인디케이터 표시")]
    [SerializeField, InspectorName("수리검 방향 인디케이터 표시"), Tooltip("끄면 수리검의 투척 전조와 비행 방향 UI만 숨긴다. 투척과 위협 판정은 유지한다.")]
    private bool _showShurikenIndicators = true;
    [SerializeField, InspectorName("적 방향 인디케이터 표시"), Tooltip("끄면 화면 밖 적의 발소리 방향 UI만 숨긴다. 발소리와 인지 판정은 유지한다.")]
    private bool _showEnemyIndicators = false;

    [Header("위협 표시 정책")]
    [SerializeField, Min(0f)] private float _visibilityThreshold;
    [SerializeField, Min(0.1f)] private float _nearDistance = 4f;
    [SerializeField, Min(0.1f)] private float _farDistance = 30f;
    [SerializeField] private Color _perceivedThreatColor = Color.white;
    [SerializeField] private Color _concealedThreatColor = new Color(1f, 0.55f, 0.2f, 1f);
    [SerializeField, Range(0f, 1f)] private float _perceivedThreatAlpha = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _concealedThreatAlpha = 1f;

    [Header("발소리 사건")]
    [SerializeField, Min(0.1f)] private float _footstepLifetime = 2f;
    [SerializeField, Min(0f)] private float _footstepFadeDuration = 0.5f;
    [SerializeField, Min(0.1f)] private float _footstepRefreshInterval = 0.5f;

    private readonly Dictionary<CombatEntityKey, ThreatPresentationSnapshot> _previousThreats =
        new Dictionary<CombatEntityKey, ThreatPresentationSnapshot>();
    private readonly List<ThreatPresentationSnapshot> _currentThreats = new List<ThreatPresentationSnapshot>();
    private readonly HashSet<CombatEntityKey> _seenThreats = new HashSet<CombatEntityKey>();
    private readonly List<CombatEntityKey> _endedThreats = new List<CombatEntityKey>();
    private readonly Dictionary<EnemyFootsteps, Action<Vector3>> _footstepHandlers =
        new Dictionary<EnemyFootsteps, Action<Vector3>>();
    private readonly Dictionary<CombatEntityKey, long> _footstepSequences =
        new Dictionary<CombatEntityKey, long>();
    private readonly List<ActiveFootstep> _activeFootsteps = new List<ActiveFootstep>();
    private readonly List<FootstepPresentationSnapshot> _currentFootsteps =
        new List<FootstepPresentationSnapshot>();
    private readonly Dictionary<CombatEntityKey, FootstepPresentationSnapshot> _forwardedFootsteps =
        new Dictionary<CombatEntityKey, FootstepPresentationSnapshot>();
    private readonly HashSet<CombatEntityKey> _seenFootstepEmitters = new HashSet<CombatEntityKey>();
    private readonly List<CombatEntityKey> _endedFootstepEmitters = new List<CombatEntityKey>();
    private readonly Plane[] _cameraPlanes = new Plane[6];
    private int _cameraPlanesFrame = -1;
    private CombatSessionRuntime _sessionRuntime;
    private SensoryPerceptionMask _perceptionMask;
    private Camera _playerCamera;
    private float _nextFootstepRefreshTime;
    private long _revision;
    private CombatSessionKey _sessionKey;
    private bool _hasSessionKey;

    public event Action<ThreatPresentationSnapshot> ThreatChanged;
    public event Action<FootstepPresentationSnapshot> FootstepChanged;
    public float VisibilityThreshold => _visibilityThreshold;
    /// <summary>Inspector의 수리검 표시 설정을 반환한다.</summary>
    public bool ShowShurikenIndicators => _showShurikenIndicators;
    /// <summary>Inspector의 적 표시 설정을 반환한다.</summary>
    public bool ShowEnemyIndicators => _showEnemyIndicators;
    private readonly struct ActiveFootstep
    {
        public FootstepPresentationSnapshot Snapshot { get; }
        public double ExpiresAt { get; }
        public EnemyHealth Emitter { get; }
        public Renderer[] Renderers { get; }
        public EnemyHitCapsule BodyCapsule { get; }

        /// <summary>snapshot·만료 시각·emitter·외형 renderers·bodyCapsule을 보관해 발소리 수명과 몸체의 화면 진입을 판정한다.</summary>
        public ActiveFootstep(FootstepPresentationSnapshot snapshot, double expiresAt, EnemyHealth emitter,
            Renderer[] renderers, EnemyHitCapsule bodyCapsule)
        {
            Snapshot = snapshot;
            ExpiresAt = expiresAt;
            Emitter = emitter;
            Renderers = renderers;
            BodyCapsule = bodyCapsule;
        }
    }

    void OnEnable()
    {
        _nextFootstepRefreshTime = 0f;
    }

    void LateUpdate()
    {
        if (!TryGetSession())
        {
            if (_hasSessionKey && _previousThreats.Count > 0)
                PublishEndedThreats();
            if (_hasSessionKey)
            {
                PublishEndedFootsteps();
                PublishEndedForwardedFootsteps();
                _footstepSequences.Clear();
                _hasSessionKey = false;
            }
            return;
        }
        if (_threatPresentationSource != null)
            PublishThreatSnapshots();
        if (_footstepCandidateSource != null)
        {
            if (_activeFootsteps.Count > 0)
                PublishEndedFootsteps();
            PublishCandidateFootsteps();
        }
        else if (_forwardedFootsteps.Count > 0)
            PublishEndedForwardedFootsteps();
        if (!TryGetPresentationServices())
        {
            if (_threatPresentationSource == null)
                PublishEndedThreats();
            UnsubscribeFootsteps();
            if (_footstepCandidateSource == null)
                PublishEndedFootsteps();
            return;
        }
        // 기존 발소리 경로의 결과는 적 방향 표시에만 쓰이므로 표시가 꺼져 있으면 수집하지 않는다.
        if (_footstepCandidateSource != null || _showEnemyIndicators)
            RefreshFootstepSubscriptions();
        else
            StopLegacyFootsteps();
        if (_threatPresentationSource == null)
            PublishThreatSnapshots();
        if (_footstepCandidateSource == null && _showEnemyIndicators)
            ExpireFootsteps();
    }

    void OnDisable()
    {
        UnsubscribeFootsteps();
        PublishEndedThreats();
        PublishEndedFootsteps();
        PublishEndedForwardedFootsteps();
    }

    /// <summary>현재 C 연결 또는 기존 수리검 경로가 제공한 위협 스냅샷 목록을 읽기 전용으로 반환한다.</summary>
    public IReadOnlyList<ThreatPresentationSnapshot> GetCurrentThreats()
    {
        return _currentThreats;
    }

    /// <summary>만료되지 않은 실제 발소리 표시 사건을 읽기 전용 목록으로 반환한다.</summary>
    public IReadOnlyList<FootstepPresentationSnapshot> GetCurrentFootsteps()
    {
        return _currentFootsteps;
    }

    /// <summary>활성 D 세션을 연결하고 새 세션이면 이전 위협·발소리를 종료한 뒤 표시 순번을 초기화한다.</summary>
    private bool TryGetSession()
    {
        _sessionRuntime = CombatSessionRuntime.Active;
        if (_sessionRuntime == null || !_sessionRuntime.IsInitialized
            || _sessionRuntime.gameObject.scene != gameObject.scene)
            return false;

        if (!_hasSessionKey || !IsSameSession(_sessionKey, _sessionRuntime.Session))
        {
            if (_hasSessionKey)
            {
                PublishEndedThreats();
                PublishEndedFootsteps();
                PublishEndedForwardedFootsteps();
            }
            UnsubscribeFootsteps();
            _activeFootsteps.Clear();
            _currentFootsteps.Clear();
            _footstepSequences.Clear();
            _sessionKey = _sessionRuntime.Session;
            _hasSessionKey = true;
            _revision = 0;
        }
        return true;
    }

    /// <summary>기존 발소리·수리검 경로의 인지 마스크와 플레이어 카메라가 준비되면 true를 반환한다.</summary>
    private bool TryGetPresentationServices()
    {
        if (!Game.IsInitialized || Game.Instance.SensoryManager == null || Game.Player == null)
            return false;
        if (_perceptionMask == null)
            _perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        if (_playerCamera == null)
            _playerCamera = Game.Player.GetComponentInChildren<Camera>();
        return _perceptionMask != null && _playerCamera != null;
    }

    /// <summary>새로 생성되거나 사라진 적 발걸음 발행자의 위치 사건 구독을 갱신한다.</summary>
    private void RefreshFootstepSubscriptions()
    {
        if (Time.unscaledTime < _nextFootstepRefreshTime)
            return;
        _nextFootstepRefreshTime = Time.unscaledTime + _footstepRefreshInterval;

        HashSet<EnemyFootsteps> activeSources = new HashSet<EnemyFootsteps>(
            FindObjectsByType<EnemyFootsteps>(FindObjectsSortMode.None));
        foreach (EnemyFootsteps source in activeSources)
        {
            if (source.gameObject.scene != gameObject.scene || _footstepHandlers.ContainsKey(source))
                continue;
            EnemyFootsteps capturedSource = source;
            Action<Vector3> handler = position => HandleFootstep(capturedSource, position);
            _footstepHandlers.Add(source, handler);
            source.Footstep += handler;
        }

        List<EnemyFootsteps> removedSources = new List<EnemyFootsteps>();
        foreach (EnemyFootsteps source in _footstepHandlers.Keys)
        {
            if (source == null || !activeSources.Contains(source) || source.gameObject.scene != gameObject.scene)
                removedSources.Add(source);
        }
        foreach (EnemyFootsteps source in removedSources)
            RemoveFootstepSubscription(source);
    }

    /// <summary>source의 발소리 구독을 해제하고 사라진 발행자 참조를 제거한다.</summary>
    private void RemoveFootstepSubscription(EnemyFootsteps source)
    {
        if (!_footstepHandlers.TryGetValue(source, out Action<Vector3> handler))
            return;
        if (source != null)
            source.Footstep -= handler;
        _footstepHandlers.Remove(source);
    }

    /// <summary>
    /// 적 방향 표시가 꺼진 기존 발소리 경로에서 발소리 구독을 해제하고 남은 발소리의 종료를 발행한다.
    /// 표시를 다시 켜면 바로 구독하도록 _nextFootstepRefreshTime을 초기화한다.
    /// </summary>
    private void StopLegacyFootsteps()
    {
        if (_footstepHandlers.Count > 0)
            UnsubscribeFootsteps();
        if (_activeFootsteps.Count > 0)
            PublishEndedFootsteps();
        _nextFootstepRefreshTime = 0f;
    }

    /// <summary>모든 적 발걸음 발행자에서 이 공급자가 등록한 이벤트 처리기만 해제한다.</summary>
    private void UnsubscribeFootsteps()
    {
        foreach (KeyValuePair<EnemyFootsteps, Action<Vector3>> subscription in _footstepHandlers)
        {
            if (subscription.Key != null)
                subscription.Key.Footstep -= subscription.Value;
        }
        _footstepHandlers.Clear();
    }

    /// <summary>C 연결 결과 또는 일반·보스 수리검 판정을 발행하고 현재 목록에 없는 대상의 종료를 전달한다.</summary>
    private void PublishThreatSnapshots()
    {
        _seenThreats.Clear();
        _currentThreats.Clear();
        if (_threatPresentationSource != null)
        {
            if (_threatPresentationSource.isActiveAndEnabled)
            {
                foreach (ThreatPresentationSnapshot source in _threatPresentationSource.GetCurrentThreats())
                {
                    if (!IsSameSession(source.Session, _sessionKey) || !source.IsActive)
                        continue;
                    ThreatPresentationSnapshot snapshot = new ThreatPresentationSnapshot(source.Session, source.Target, ++_revision,
                        source.IsActive, source.IsEligible, source.IsInCamera, source.WorldVisible, source.UiVisible,
                        source.UiDirection, source.WorldPosition, source.Proximity, source.Color, source.Alpha);
                    AddCurrentThreat(snapshot);
                }
            }
        }
        else
        {
            DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
            foreach (DefenseShuriken shuriken in shurikens)
                AddProjectileThreat(shuriken.transform, shuriken.ProjectileId, shuriken.IsFlying, shuriken.HasPassedPlayer);

            EnemyProjectile[] enemyProjectiles = FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None);
            foreach (EnemyProjectile projectile in enemyProjectiles)
                AddProjectileThreat(projectile.transform, projectile.ProjectileId,
                    projectile.IsHostileFlying, projectile.HasPassedPlayer);

            TwinBossProjectile[] twinBossProjectiles = FindObjectsByType<TwinBossProjectile>(FindObjectsSortMode.None);
            foreach (TwinBossProjectile projectile in twinBossProjectiles)
                AddProjectileThreat(projectile.transform, projectile.ProjectileId,
                    projectile.IsHostileFlying, projectile.HasPassedPlayer);
        }

        _endedThreats.Clear();
        foreach (CombatEntityKey key in _previousThreats.Keys)
        {
            if (!_seenThreats.Contains(key))
                _endedThreats.Add(key);
        }
        foreach (CombatEntityKey key in _endedThreats)
        {
            ThreatPresentationSnapshot previous = _previousThreats[key];
            ThreatChanged?.Invoke(CreateEndedSnapshot(previous));
            _previousThreats.Remove(key);
        }
    }

    /// <summary>snapshot을 현재 조회 목록·생명 목록에 저장하고 같은 공통 결과를 HUD 구독자에게 발행한다.</summary>
    private void AddCurrentThreat(ThreatPresentationSnapshot snapshot)
    {
        _seenThreats.Add(snapshot.Target);
        _currentThreats.Add(snapshot);
        _previousThreats[snapshot.Target] = snapshot;
        ThreatChanged?.Invoke(snapshot);
    }

    /// <summary>
    /// 같은 씬의 아직 플레이어를 지나지 않은 적대 투사체를 현재 수리검 위협 목록에 등록한다.
    /// transform과 양수 projectileId를 사용해 기존 CreateSnapshot의 화면·인지·방향 판정을 적용한다.
    /// </summary>
    private void AddProjectileThreat(Transform projectile, int projectileId, bool isFlying, bool hasPassedPlayer)
    {
        if (projectile == null || projectile.gameObject.scene != gameObject.scene
            || projectileId <= 0 || !isFlying || hasPassedPlayer)
            return;

        CombatEntityKey key = new CombatEntityKey("Projectile_" + projectileId, 1);
        AddCurrentThreat(CreateSnapshot(key, projectile.position, true, true));
    }

    /// <summary>등록된 발소리의 수명·발행자를 확인하고 적의 현재 위치로 표시 결과와 초기 조회 목록을 갱신한다.</summary>
    private void ExpireFootsteps()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        for (int i = _activeFootsteps.Count - 1; i >= 0; i--)
        {
            ActiveFootstep activeFootstep = _activeFootsteps[i];
            if (activeFootstep.ExpiresAt <= now || activeFootstep.Emitter == null
                || !activeFootstep.Emitter.IsAlive
                || activeFootstep.Emitter.SpawnGeneration != activeFootstep.Snapshot.Emitter.SpawnGeneration)
            {
                PublishEndedFootstep(activeFootstep.Snapshot);
                _activeFootsteps.RemoveAt(i);
                continue;
            }

            FootstepPresentationSnapshot previous = activeFootstep.Snapshot;
            float fade = _footstepFadeDuration > 0f
                ? Mathf.Clamp01((float)(activeFootstep.ExpiresAt - now) / _footstepFadeDuration) : 1f;
            ThreatPresentationSnapshot presentation = CreateSnapshot(previous.Presentation.Target,
                activeFootstep.Emitter.transform.position, true, true, fade, activeFootstep.Renderers,
                activeFootstep.BodyCapsule);
            FootstepPresentationSnapshot updated = new FootstepPresentationSnapshot(
                previous.EventIdentity, previous.Emitter, previous.Origin, previous.OccurredAt, presentation);
            _activeFootsteps[i] = new ActiveFootstep(updated, activeFootstep.ExpiresAt,
                activeFootstep.Emitter, activeFootstep.Renderers, activeFootstep.BodyCapsule);
        }
        _currentFootsteps.Clear();
        foreach (ActiveFootstep footstep in _activeFootsteps)
        {
            _currentFootsteps.Add(footstep.Snapshot);
            FootstepChanged?.Invoke(footstep.Snapshot);
        }
    }

    /// <summary>
    /// 적 발소리 위치에 발행자 세대·사건 순번·세션 시각을 붙여 C의 표시 결과를 발행한다.
    /// 같은 적의 이전 활성 발소리는 새 발소리로 교체하고 종료를 발행해 _activeFootsteps에 적별 최신 걸음 하나만 남긴다.
    /// </summary>
    private void HandleFootstep(EnemyFootsteps source, Vector3 position)
    {
        if (source == null || _sessionRuntime == null || !_sessionRuntime.IsInitialized)
            return;
        EnemyHealth health = source.GetComponent<EnemyHealth>();
        if (health == null || !health.IsAlive)
            return;

        if (_footstepCandidateSource != null)
        {
            _footstepCandidateSource.RecordFootstep(health, position);
            return;
        }
        if (!_showEnemyIndicators)
            return;

        CombatEntityKey emitter = new CombatEntityKey(health.TargetId, health.SpawnGeneration);
        _footstepSequences.TryGetValue(emitter, out long sequence);
        sequence++;
        _footstepSequences[emitter] = sequence;
        CombatEventIdentity identity = new CombatEventIdentity(_sessionRuntime.Session, health.TargetId, sequence);
        CombatTimestamp timestamp = _sessionRuntime.CaptureTimestamp();
        CombatEntityKey pulseTarget = new CombatEntityKey(
            emitter.EntityId + "/footstep/" + sequence, emitter.SpawnGeneration);
        int activeIndex = FindActiveFootstep(health);
        Renderer[] renderers = activeIndex >= 0
            ? _activeFootsteps[activeIndex].Renderers : health.GetComponentsInChildren<Renderer>();
        EnemyHitCapsule bodyCapsule = activeIndex >= 0
            ? _activeFootsteps[activeIndex].BodyCapsule : health.GetComponent<EnemyHitCapsule>();
        ThreatPresentationSnapshot presentation = CreateSnapshot(pulseTarget, health.transform.position,
            true, true, 1f, renderers, bodyCapsule);
        FootstepPresentationSnapshot snapshot = new FootstepPresentationSnapshot(
            identity, emitter, position, timestamp, presentation);
        ActiveFootstep activeFootstep = new ActiveFootstep(snapshot,
            Time.realtimeSinceStartupAsDouble + _footstepLifetime, health, renderers, bodyCapsule);
        if (activeIndex < 0)
        {
            _activeFootsteps.Add(activeFootstep);
            FootstepChanged?.Invoke(snapshot);
            return;
        }

        // 새 걸음을 먼저 발행해야 D가 순번이 낮은 이전 걸음의 종료를 무시하고 마커를 유지한다.
        FootstepPresentationSnapshot replaced = _activeFootsteps[activeIndex].Snapshot;
        _activeFootsteps[activeIndex] = activeFootstep;
        FootstepChanged?.Invoke(snapshot);
        PublishEndedFootstep(replaced);
    }

    /// <summary>health의 현재 생성 세대로 등록된 활성 발소리의 _activeFootsteps 인덱스를 반환한다. 없으면 -1을 반환한다.</summary>
    private int FindActiveFootstep(EnemyHealth health)
    {
        for (int i = 0; i < _activeFootsteps.Count; i++)
        {
            ActiveFootstep activeFootstep = _activeFootsteps[i];
            if (activeFootstep.Emitter == health
                && activeFootstep.Snapshot.Emitter.SpawnGeneration == health.SpawnGeneration)
                return i;
        }
        return -1;
    }

    /// <summary>C의 최신 발소리 후보를 재판정 없이 복사하고 교체·만료·비활성화된 이전 걸음의 종료를 발행한다.</summary>
    private void PublishCandidateFootsteps()
    {
        _currentFootsteps.Clear();
        _seenFootstepEmitters.Clear();
        if (_footstepCandidateSource.isActiveAndEnabled)
        {
            foreach (FootstepPresentationSnapshot source in _footstepCandidateSource.GetCurrentFootsteps())
            {
                if (!IsSameSession(source.Presentation.Session, _sessionKey) || !source.Presentation.IsActive)
                    continue;
                if (_forwardedFootsteps.TryGetValue(source.Emitter, out FootstepPresentationSnapshot previous)
                    && (previous.EventIdentity.SourceId != source.EventIdentity.SourceId
                        || previous.EventIdentity.SourceSequence != source.EventIdentity.SourceSequence))
                    PublishEndedFootstep(previous);
                ThreatPresentationSnapshot value = source.Presentation;
                ThreatPresentationSnapshot presentation = new ThreatPresentationSnapshot(value.Session, value.Target, ++_revision,
                    value.IsActive, value.IsEligible, value.IsInCamera, value.WorldVisible, value.UiVisible,
                    value.UiDirection, value.WorldPosition, value.Proximity, value.Color, value.Alpha);
                FootstepPresentationSnapshot snapshot = new FootstepPresentationSnapshot(source.EventIdentity,
                    source.Emitter, source.Origin, source.OccurredAt, presentation);
                _seenFootstepEmitters.Add(source.Emitter);
                _forwardedFootsteps[source.Emitter] = snapshot;
                _currentFootsteps.Add(snapshot);
                FootstepChanged?.Invoke(snapshot);
            }
        }
        _endedFootstepEmitters.Clear();
        foreach (CombatEntityKey key in _forwardedFootsteps.Keys)
            if (!_seenFootstepEmitters.Contains(key))
                _endedFootstepEmitters.Add(key);
        foreach (CombatEntityKey key in _endedFootstepEmitters)
        {
            PublishEndedFootstep(_forwardedFootsteps[key]);
            _forwardedFootsteps.Remove(key);
        }
    }

    /// <summary>입력 없이 C에서 전달받은 모든 발소리의 종료를 발행하고 D의 조회·추적 목록을 비운다.</summary>
    private void PublishEndedForwardedFootsteps()
    {
        foreach (FootstepPresentationSnapshot snapshot in _forwardedFootsteps.Values)
            PublishEndedFootstep(snapshot);
        _forwardedFootsteps.Clear();
        _currentFootsteps.Clear();
    }

    /// <summary>position·활성/자격·alphaMultiplier와 발소리 외형을 읽어 화면 진입·플레이어 기준 방향의 표시 결과를 반환한다.</summary>
    private ThreatPresentationSnapshot CreateSnapshot(
        CombatEntityKey target, Vector3 position, bool isActive, bool isEligible,
        float alphaMultiplier = 1f, Renderer[] footstepRenderers = null, EnemyHitCapsule bodyCapsule = null)
    {
        float visibility = _perceptionMask.GetVisibility(position);
        Vector3 viewport = _playerCamera.WorldToViewportPoint(position);
        bool inCamera = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
            && viewport.y >= 0f && viewport.y <= 1f;
        if (footstepRenderers != null)
        {
            if (_cameraPlanesFrame != Time.frameCount)
            {
                GeometryUtility.CalculateFrustumPlanes(_playerCamera, _cameraPlanes);
                _cameraPlanesFrame = Time.frameCount;
            }
            inCamera = BlindBlade.Perception.EnemyFootstepVisibility.IsInCamera(
                bodyCapsule, footstepRenderers, _cameraPlanes, inCamera);
        }
        bool worldVisible = visibility > _visibilityThreshold && inCamera;
        Quaternion viewYaw = Quaternion.Euler(0f, _playerCamera.transform.eulerAngles.y, 0f);
        Vector3 horizontalOffset = position - _playerCamera.transform.position;
        horizontalOffset.y = 0f;
        Vector2 direction = new Vector2(
            Vector3.Dot(horizontalOffset, viewYaw * Vector3.right),
            Vector3.Dot(horizontalOffset, viewYaw * Vector3.forward));
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;
        else
            direction.Normalize();

        bool uiVisible = isActive && isEligible && !worldVisible
            && (footstepRenderers == null || !inCamera);
        Color color = worldVisible ? _perceivedThreatColor : _concealedThreatColor;
        color.a = 1f;
        float alpha = worldVisible ? _perceivedThreatAlpha : _concealedThreatAlpha;
        alpha *= Mathf.Clamp01(alphaMultiplier);
        if (!isActive)
            alpha = 0f;
        float proximity = Mathf.InverseLerp(_farDistance, _nearDistance,
            Vector3.Distance(position, _playerCamera.transform.position));
        return new ThreatPresentationSnapshot(_sessionRuntime.Session, target, ++_revision,
            isActive, isEligible, inCamera, worldVisible, uiVisible, direction, position,
            proximity, color, alpha);
    }

    /// <summary>snapshot의 사건·원점을 유지한 종료 스냅샷을 발행해 D의 해당 발소리 표시를 반환한다.</summary>
    private void PublishEndedFootstep(FootstepPresentationSnapshot snapshot)
    {
        FootstepChanged?.Invoke(new FootstepPresentationSnapshot(snapshot.EventIdentity,
            snapshot.Emitter, snapshot.Origin, snapshot.OccurredAt,
            CreateEndedSnapshot(snapshot.Presentation)));
    }

    /// <summary>입력 없이 모든 발소리에 종료 사건을 발행하고 활성·초기 조회 목록을 비운다.</summary>
    private void PublishEndedFootsteps()
    {
        foreach (ActiveFootstep footstep in _activeFootsteps)
            PublishEndedFootstep(footstep.Snapshot);
        _activeFootsteps.Clear();
        _currentFootsteps.Clear();
    }

    /// <summary>비활성 스냅샷을 발행해 구독자가 가진 이전 위협 표시를 정리한다.</summary>
    private void PublishEndedThreats()
    {
        _endedThreats.Clear();
        foreach (CombatEntityKey key in _previousThreats.Keys)
            _endedThreats.Add(key);
        foreach (CombatEntityKey key in _endedThreats)
        {
            ThreatPresentationSnapshot previous = _previousThreats[key];
            ThreatChanged?.Invoke(CreateEndedSnapshot(previous));
        }
        _previousThreats.Clear();
        _currentThreats.Clear();
    }

    /// <summary>previous의 마지막 위치와 방향을 보존한 비활성 스냅샷을 반환한다.</summary>
    private ThreatPresentationSnapshot CreateEndedSnapshot(ThreatPresentationSnapshot previous)
    {
        return new ThreatPresentationSnapshot(previous.Session, previous.Target, ++_revision,
            false, false, previous.IsInCamera, false, false, previous.UiDirection,
            previous.WorldPosition, previous.Proximity, previous.Color, 0f);
    }

    /// <summary>left와 right의 ID·세대가 같으면 같은 세션으로 반환한다.</summary>
    private static bool IsSameSession(CombatSessionKey left, CombatSessionKey right)
    {
        return left.Generation == right.Generation && left.SessionId == right.SessionId;
    }
}
