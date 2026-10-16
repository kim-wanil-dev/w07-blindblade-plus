using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 퍼펙트 패링과 극한 회피 순간 웅크리기 파동처럼 발밑 지면에서 넓게 퍼지는 파동을 낸다.
/// 바닥 링이 지면을 따라 눈에 보이게 퍼지며 지나간 바닥은 지형 기억에 남고, 그 범위의 적·지형이 드러난다.
/// 퍼짐은 불릿 타임과 무관한 실시간이다. 퍼펙트 파동이 드러낸 범위는 강화 준비·공격 동안 유지하고,
/// 극한 회피 파동은 다 퍼진 뒤 게임 시간으로 감춘다. 퍼펙트 파동이 진행 중이면 극한 회피 파동은 내지 않는다.
/// 시야 공개는 SensoryPerceptionMask의 퍼펙트 파동 영역, 바닥 링은 기존 웅크리기 파동 프리팹, 칼날 빛줄기·비네트는 EmpowerOverlay가 맡는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEmpower))]
[RequireComponent(typeof(EmpowerOverlay))]
public sealed class PlayerPerfectPulse : MonoBehaviour
{
    private const string WAVE_RESOURCE_PATH = "Wave/Wave";
    private const float SURFACE_OFFSET = 0.02f;
    private const float GROUND_PROBE_EXTRA = 0.5f;
    private const float MEMORY_STEP_DURATION = 0.0001f;

    [Header("시야 파동")]
    [SerializeField, Min(1f), Tooltip("파동이 드러내는 최대 반경(미터).")]
    private float _radius = 40f;
    [SerializeField, Min(0.01f), Tooltip("반경이 끝까지 퍼지는 실시간.")]
    private float _expandDuration = 1f;
    [SerializeField, Min(0.01f), Tooltip("강화가 끝난 뒤 드러난 범위가 사라지는 실시간.")]
    private float _fadeDuration = 0.8f;
    [SerializeField, Tooltip("퍼펙트 패링 바닥 링 색상. 새 파동부터 적용한다.")]
    private Color _ringColor = Color.white;

    [Header("극한 회피 파동")]
    [SerializeField, Min(1f), Tooltip("극한 회피 파동이 드러내는 최대 반경(미터).")]
    private float _dodgeRadius = 12f;
    [SerializeField, Min(0.01f), Tooltip("극한 회피 파동 반경이 끝까지 퍼지는 실시간.")]
    private float _dodgeExpandDuration = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("극한 회피 파동이 다 퍼진 뒤 공개 영역과 링이 사라지는 게임 시간. 정지·불릿 타임에 함께 느려진다.")]
    private float _dodgeFadeDuration = 0.6f;
    [SerializeField, Tooltip("극한 회피 바닥 링 색상. 새 파동부터 적용한다.")]
    private Color _dodgeRingColor = Color.white;

    [Header("시작 파동")]
    [SerializeField, Min(0f), Tooltip("게임 플레이가 처음 시작될 때 한 번 내는 회피 파동 반경(미터). 맵 전체를 덮도록 크게 두며 0이면 내지 않는다. 확산 시간·링 색은 극한 회피 파동 값을 쓴다.")]
    private float _startPulseRadius = 50f;
    [SerializeField, Min(0.01f), Tooltip("시작 파동이 다 퍼진 뒤 공개 영역과 링이 천천히 흐려져 사라지는 게임 시간(초).")]
    private float _startPulseFadeDuration = 3f;
    private bool _startPulsePlayed;

    [Header("바닥 링")]
    [SerializeField, Min(0.01f), Tooltip("퍼펙트 패링 바닥 링이 다 퍼진 뒤 사라지는 실시간. 극한 회피 링은 공개 영역과 함께 게임 시간으로 사라진다.")]
    private float _ringFadeDuration = 0.3f;

    [Header("화면 연출")]
    [SerializeField, Min(0.01f), Tooltip("퍼펙트 패링 순간 화면을 가로지르는 칼날 반사 빛줄기의 실시간 길이.")]
    private float _glintDuration = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("강화 준비 중 화면 가장자리 먹 번짐 진하기.")]
    private float _readyVignette = 0.75f;
    [SerializeField, Range(0f, 1f), Tooltip("궁극기 차징이 다 찼을 때 준비 비네트 위에 겹치는 차징 비네트 진하기. 차징 진행도만큼 경계 없이 안쪽으로 번지며 진해진다.")]
    private float _chargeVignette = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("극한 회피 순간 화면 가장자리에 짧게 번지는 먹 비네트 진하기. 0이면 내지 않는다.")]
    private float _perfectDodgeVignette = 0.6f;
    [SerializeField, Range(0f, 1f), Tooltip("일반 회피(회피 무적 구간에서 공격을 피함) 순간 짧게 번지는 먹 비네트 진하기. 0이면 내지 않는다.")]
    private float _normalDodgeVignette = 0f;
    [SerializeField, Min(0.01f), Tooltip("회피 비네트가 번졌다 사라지는 실시간.")]
    private float _dodgeVignetteDuration = 0.4f;

    [Header("상태")]
    private PlayerEmpower _empower;
    private EmpowerOverlay _overlay;
    private CharacterController _controller;
    private PlayerHealth _health;
    private SensoryPerceptionMask _mask;
    private TerrainMemory _terrainMemory;
    private GameObject _wave;
    private SpriteRenderer _ring;
    private Color _ringBaseColor;
    private Vector3 _center;
    private float _elapsed;
    private float _fadeElapsed;
    private float _rememberedRadius;
    private float _activeRadius;
    private float _activeExpandDuration;
    private float _activeFadeDuration;
    private float _activeRingFadeDuration;
    private CombatSessionRuntime _sessionRuntime;
    private CombatSessionKey _session;
    private bool _hasSession;
    private bool _running;
    private bool _perfectRunning;

    public bool IsRunning => _running;
    public bool IsPerfectRunning => _running && _perfectRunning;
    public Vector3 Center => _center;
    public float CurrentRadius { get; private set; }
    public float CurrentVisibility { get; private set; }
    public float ExpansionElapsed => _elapsed;
    public float FadeElapsed => _fadeElapsed;
    public float ActiveRadius => _activeRadius;
    public float ActiveExpandDuration => _activeExpandDuration;
    public float ActiveFadeDuration => _activeFadeDuration;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
        _overlay = GetComponent<EmpowerOverlay>();
        _controller = GetComponent<CharacterController>();
        _health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        _health.Died += HandleDied;
    }

    void OnDisable()
    {
        _health.Died -= HandleDied;
        Stop();
    }

    void OnDestroy()
    {
        if (_wave != null) Destroy(_wave);
    }

    void Update()
    {
        // 강화 공격 중의 비네트는 연출이 직접 정하고, 궁극기 차징 비네트는 따로 겹쳐 진행도만큼 안쪽으로 번진다.
        if (!_empower.IsStriking) _overlay.SetVignette(_empower.IsReady ? _readyVignette : 0f);
        _overlay.SetChargeVignette(_empower.ChargeProgress, _chargeVignette);
        if (!_startPulsePlayed) TryPlayStartPulse();
        if (!_running) return;

        if (_hasSession && (_sessionRuntime == null || !_sessionRuntime.IsInitialized
            || _sessionRuntime.Session.SessionId != _session.SessionId
            || _sessionRuntime.Session.Generation != _session.Generation))
        {
            Stop();
            return;
        }

        float dt = EmpowerOverlay.RealDeltaTime();
        float previousElapsed = _elapsed;
        _elapsed += dt;
        float t = Mathf.Clamp01(_elapsed / _activeExpandDuration);
        float radius = _activeRadius * (1f - Mathf.Pow(1f - t, 2f));
        RememberGround(radius);
        float visibility = 1f;
        // 퍼펙트 파동만 불릿 타임 동안 공개를 유지하고, 극한 회피 파동은 다 퍼지면 바로 감춘다.
        bool hold = _perfectRunning && _empower.IsBulletTimeActive;
        if (!hold && t >= 1f)
        {
            float fadeDelta = _perfectRunning ? dt : Time.deltaTime;
            if (!_perfectRunning && dt > 0f && previousElapsed < _activeExpandDuration)
                fadeDelta *= Mathf.Clamp01((_elapsed - _activeExpandDuration) / dt);
            _fadeElapsed += fadeDelta;
            visibility = 1f - Mathf.Clamp01(_fadeElapsed / _activeFadeDuration);
            if (visibility <= 0f)
            {
                Stop();
                return;
            }
        }
        CurrentRadius = radius;
        CurrentVisibility = visibility;
        UpdateRing(radius);
        if (_mask != null) _mask.SetPulseRegion(_center, radius, visibility);
    }

    /// <summary>
    /// 발밑 지면에서 퍼펙트 파동을 처음부터 시작하고 바닥 링과 화면을 가로지르는 칼날 반사 빛줄기를 띄운다.
    /// 지면을 찾지 못하면 플레이어 발 위치를 쓰며, 감각 매니저가 준비되지 않았으면 시야 공개 없이 링과 빛줄기만 보인다.
    /// </summary>
    public void Begin()
    {
        _overlay.Glint(_glintDuration);
        StartPulse(_radius, _expandDuration, _fadeDuration, _ringColor, true);
    }

    /// <summary>
    /// 발밑 지면에서 극한 회피 파동을 처음부터 시작한다. 칼날 빛줄기 없이 바닥 링·지형 기억·시야 공개만 낸다.
    /// 퍼펙트 파동이 진행 중이면 아무것도 바꾸지 않고 false를, 파동을 시작했으면 true를 반환한다.
    /// </summary>
    public bool BeginPerfectDodge()
    {
        if (_running && _perfectRunning) return false;
        StartPulse(_dodgeRadius, _dodgeExpandDuration, _dodgeFadeDuration, _dodgeRingColor, false);
        return true;
    }

    /// <summary>
    /// 보스 재도전 시작 시 자동 흰 파동을 건너뛰도록 표시한다.
    /// 이후 퍼펙트 패링과 극한 회피로 발생하는 파동은 유지한다.
    /// </summary>
    public void SuppressStartPulse()
    {
        _startPulsePlayed = true;
    }

    /// <summary>
    /// 온보딩 단계가 바뀌면 시작 파동의 반경·확산·페이드·링 색으로 발밑 시야를 다시 밝힌다.
    /// 현재 시작 파동 설정을 사용하며 자동 시작 파동은 중복 재생하지 않도록 표시한다.
    /// </summary>
    public void ReplayStartPulseForOnboarding()
    {
        _startPulsePlayed = true;
        if (_startPulseRadius <= 0f) return;
        StartPulse(_startPulseRadius, _dodgeExpandDuration, _startPulseFadeDuration, _dodgeRingColor, false);
    }

    /// <summary>
    /// 게임 상태가 처음 Playing이 되면 발밑에서 _startPulseRadius 반경의 회피 파동을 한 번 내 맵 전체를 밝힌다.
    /// 다 퍼진 뒤에는 _startPulseFadeDuration 동안 천천히 흐려진다.
    /// 한 번 시도하면 _startPulsePlayed를 참으로 바꾸며, 반경이 0이면 파동 없이 끝낸다.
    /// </summary>
    private void TryPlayStartPulse()
    {
        if (!Game.IsInitialized || Game.Instance.GameStateManager == null
            || Game.Instance.GameStateManager.State != GameplayState.Playing) return;
        _startPulsePlayed = true;
        if (_startPulseRadius <= 0f) return;
        StartPulse(_startPulseRadius, _dodgeExpandDuration, _startPulseFadeDuration, _dodgeRingColor, false);
    }

    /// <summary>
    /// perfect가 참이면 극한 회피, 거짓이면 일반 회피 진하기로 화면 가장자리에 짧은 먹 비네트를 _dodgeVignetteDuration 동안 띄운다.
    /// 진하기가 0이면 아무것도 하지 않으며, 강화 준비 비네트와 겹치면 오버레이가 더 진한 쪽을 보인다.
    /// </summary>
    public void PlayDodgeVignette(bool perfect)
    {
        float strength = perfect ? _perfectDodgeVignette : _normalDodgeVignette;
        if (strength <= 0f) return;
        _overlay.PulseVignette(strength, _dodgeVignetteDuration);
    }

    /// <summary>
    /// radius·expandDuration·fadeDuration·ringColor를 복사해 발밑 지면에서 파동을 시작하고 바닥 링을 켠다.
    /// perfect가 참이면 강화 동안 공개를 유지하는 퍼펙트 파동으로 표시하며, 진행 상태와 지면 중심을 갱신한다.
    /// 지면을 찾지 못하면 플레이어 발 위치를 쓰며, 감각 매니저가 준비되지 않았으면 시야 공개 없이 링만 보인다.
    /// </summary>
    private void StartPulse(float radius, float expandDuration, float fadeDuration, Color ringColor, bool perfect)
    {
        _mask = FindMask();
        if (_terrainMemory == null) _terrainMemory = FindAnyObjectByType<TerrainMemory>();
        FindGround(out Vector3 point, out Vector3 normal);
        _center = point;
        _activeRadius = Mathf.Max(0f, radius);
        _activeExpandDuration = Mathf.Max(0.01f, expandDuration);
        _activeFadeDuration = Mathf.Max(0.01f, fadeDuration);
        _activeRingFadeDuration = Mathf.Max(0.01f, _ringFadeDuration);
        _ringBaseColor = ringColor;
        _sessionRuntime = CombatSessionRuntime.Active;
        _hasSession = _sessionRuntime != null && _sessionRuntime.IsInitialized
            && _sessionRuntime.gameObject.scene == gameObject.scene;
        _session = _hasSession ? _sessionRuntime.Session : default;
        _perfectRunning = perfect;
        _elapsed = 0f;
        _fadeElapsed = 0f;
        _rememberedRadius = 0f;
        _running = true;
        CurrentRadius = 0f;
        CurrentVisibility = 1f;
        if (_mask != null) _mask.ClearPulseRegion();
        PrepareRing(point, normal);
    }

    /// <summary>진행 중인 파동을 멈추고 퍼펙트 파동 영역과 바닥 링을 감춘다. 지형 기억에 남은 바닥은 그대로 둔다.</summary>
    private void Stop()
    {
        if (_running && _mask != null) _mask.ClearPulseRegion();
        if (_ring != null) _ring.enabled = false;
        _running = false;
        _perfectRunning = false;
        _hasSession = false;
        CurrentRadius = 0f;
        CurrentVisibility = 0f;
    }

    /// <summary>health의 사망 알림을 받아 진행 중인 파동 영역과 링을 즉시 지운다. 지형 기억은 유지한다.</summary>
    private void HandleDied(PlayerHealth health)
    {
        Stop();
    }

    /// <summary>
    /// 웅크리기 파동과 같은 방식으로 캡슐 중심에서 아래로 지면을 찾아 point와 표면 법선 normal을 출력한다.
    /// 플레이어 자신과 트리거, 위를 향하지 않는 면은 건너뛰고 가장 가까운 지면을 쓰며, 없으면 발 위치와 위쪽 법선을 출력한다.
    /// </summary>
    private void FindGround(out Vector3 point, out Vector3 normal)
    {
        point = transform.position;
        normal = Vector3.up;
        Bounds bounds = _controller.bounds;
        float closest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(bounds.center, Vector3.down, bounds.extents.y + GROUND_PROBE_EXTRA,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y <= 0f || hit.distance >= closest) continue;
            closest = hit.distance;
            point = hit.point;
            normal = hit.normal;
        }
    }

    /// <summary>
    /// 기존 웅크리기 파동 프리팹을 한 번만 복제해 두고 point 지면에 normal을 위로 하여 시선 방향으로 놓아 링을 켠다.
    /// 프리팹 자체 재생은 쓰지 않으며 발동 시 저장한 색상·반경·페이드 상태를 UpdateRing에 적용한다.
    /// </summary>
    private void PrepareRing(Vector3 point, Vector3 normal)
    {
        if (_wave == null)
        {
            GameObject prefab = Resources.Load<GameObject>(WAVE_RESOURCE_PATH);
            if (prefab == null) return;
            _wave = Instantiate(prefab);
            _wave.name = "PerfectPulseWave";
            _ring = _wave.GetComponentInChildren<SpriteRenderer>(true);
        }
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal);
        if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        _wave.transform.SetPositionAndRotation(point + normal * SURFACE_OFFSET, Quaternion.LookRotation(forward, normal));
        _ring.enabled = true;
        UpdateRing(0f);
    }

    /// <summary>바닥 링 지름을 radius에 맞춘다. 패링은 저장한 링 실시간 수명, 회피는 공개 가시성으로 알파를 변경한다.</summary>
    private void UpdateRing(float radius)
    {
        if (_ring == null || !_ring.enabled) return;
        _ring.transform.localScale = Vector3.one * (radius * 2f);
        float fade = _perfectRunning
            ? 1f - Mathf.Clamp01((_elapsed - _activeExpandDuration) / _activeRingFadeDuration)
            : CurrentVisibility;
        Color color = _ringBaseColor;
        color.a *= fade;
        _ring.color = color;
        if (fade <= 0f) _ring.enabled = false;
    }

    /// <summary>
    /// 파동이 radius까지 넓어졌으면 그 반경을 지형 기억에 바로 남겨 지나간 바닥이 웅크리기 파동처럼 기억되게 한다.
    /// 지형 기억의 자체 확산 시간은 게임 시간이라 쓰지 않고, 실시간 반경을 매 프레임 즉시 기록한다.
    /// </summary>
    private void RememberGround(float radius)
    {
        if (_terrainMemory == null || radius <= _rememberedRadius) return;
        _terrainMemory.RememberExpandingWave(_center, radius, radius, MEMORY_STEP_DURATION);
        _rememberedRadius = radius;
    }

    /// <summary>현재 게임의 감각 매니저에 붙은 시야 마스크를 찾아 반환한다. 준비되지 않았으면 null이다.</summary>
    private static SensoryPerceptionMask FindMask()
    {
        if (!Game.IsInitialized) return null;
        SensoryManager sensoryManager = Game.Instance.SensoryManager;
        if (sensoryManager == null || !sensoryManager.IsReady) return null;
        return sensoryManager.GetComponent<SensoryPerceptionMask>();
    }
}
