using System.Collections.Generic;

using UnityEngine;

/// <summary>카타나 이벤트를 복제한 Animator 클립에 연결한다. 피해 판정이나 전투 행동 종료를 호출하지 않는다.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class KatanaWeaponVisual : MonoBehaviour
{
    private static readonly int ActionProgressId = Animator.StringToHash("ActionProgress");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("References")]
    [SerializeField] private KatanaCombatCoordinator _combat;
    [SerializeField] private PlayerActionDriver _actionDriver;
    [SerializeField] private Animator _animator;
    private PlayerClock _playerClock;

    [Header("Idle 애니메이션 교체")]
    [SerializeField, InspectorName("Idle AnimationClip")]
    [Tooltip("대기 Rest 상태의 클립을 교체합니다. Sword·Saya 경로를 사용하며 빈 슬롯은 기존 대기 클립을 유지합니다.")]
    private AnimationClip _idleClip;
    private AnimationClip _idleSourceClip;
    private AnimationClip _idleDefaultClip;

    [Header("패링 반격 애니메이션")]
    [SerializeField, InspectorName("반격 AnimationClip")] private AnimationClip _counterAttackClip;
    private AnimationClip _counterSourceClip;
    private AnimationClip _counterDefaultClip;

    [Header("퍼펙트 패링 모션")]
    [SerializeField, InspectorName("퍼펙트 패링 AnimationClip")]
    [Tooltip("퍼펙트 패링 직후 가드 자세에서 이어지는 흘리기·튕겨내기 클립입니다. Sword·Saya 경로를 사용하며 Coordinator의 퍼펙트 패링 모션 시간에 맞춰 늘어납니다. 빈 슬롯이면 아래 절차 모션을 사용합니다.")]
    private AnimationClip _perfectParryClip;
    [SerializeField, Min(0f), InspectorName("절차 모션 궤적 오른쪽 거리 (m)")]
    [Tooltip("클립이 없을 때 가드 자세에서 궤적 끝까지 검이 화면 오른쪽으로 이동하는 거리입니다. 카메라 기준이며 크면 화면 밖으로 나갑니다.")]
    private float _perfectParryArcWidth = 0.3f;
    [SerializeField, Min(0.01f), InspectorName("절차 모션 궤적 최고 높이 (m)")]
    [Tooltip("궤적 꼭짓점에서 가드 자세보다 위로 올라가는 높이입니다.")]
    private float _perfectParryArcPeakHeight = 0.22f;
    [SerializeField, InspectorName("절차 모션 궤적 끝 높이 (m)")]
    [Tooltip("궤적 끝에서 가드 자세보다 위에 있는 높이입니다. 최고 높이보다 낮을수록 꼭짓점 뒤에서 더 가파르게 떨어집니다.")]
    private float _perfectParryArcEndHeight = 0.1f;
    [SerializeField, Range(0.05f, 0.95f), InspectorName("절차 모션 꼭짓점 위치")]
    [Tooltip("오른쪽 거리 중 가장 높이 올라가는 지점의 비율입니다. 0.75면 오른쪽 3/4 지점이 가장 높습니다.")]
    private float _perfectParryArcPeakPosition = 0.75f;
    [SerializeField, InspectorName("절차 모션 궤적 앞뒤 거리 (m)")]
    [Tooltip("궤적 끝에서 검이 카메라 앞쪽으로 이동하는 거리입니다. 음수면 카메라 쪽으로 당겨집니다.")]
    private float _perfectParryArcDepth = 0f;
    [SerializeField, InspectorName("절차 모션 칼날 회전 (도)")]
    [Tooltip("궤적 끝에서 칼날이 제자리에서 도는 각도입니다. 카메라 기준 x는 앞뒤 기울기, y는 좌우 방향, z는 화면상 기울기입니다.")]
    private Vector3 _perfectParryBladeAngles = new Vector3(0f, 0f, -20f);
    [SerializeField, Range(0.05f, 0.95f), InspectorName("절차 모션 궤적 끝 도달 진행도")]
    [Tooltip("모션 시간 중 검이 궤적 끝에 도달하는 지점입니다. 그 뒤 남은 시간 동안 가드 자세로 곧게 돌아옵니다.")]
    private float _perfectParryPeakProgress = 0.6f;
    private Transform _viewSpace;
    private AnimationClip _perfectParrySourceClip;
    private AnimationClip _perfectParryDefaultClip;

    [Header("1·2타 애니메이션 교체")]
    [SerializeField, InspectorName("1타 AnimationClip")]
    [Tooltip("일반 공격·대상 돌진의 1타 IaidoDoubleSlash를 교체합니다. 빈 슬롯과 충전 발도는 기존 클립을 사용합니다.")]
    private AnimationClip _firstAttackClip;
    [SerializeField, InspectorName("1타 클립에 Idle 복귀 포함")]
    [Tooltip("교체 1타가 끝에서 검·검집을 Idle 자세로 복귀시키면 체크합니다. 끝까지 완료한 뒤 별도 납도 모션을 재생하지 않습니다.")]
    private bool _firstAttackReturnsToIdle = true;
    [SerializeField, InspectorName("2타 AnimationClip")]
    [Tooltip("Normal1 상태의 클립을 교체합니다. Sword·Saya 경로가 현재 카타나 리그와 같아야 합니다.")]
    private AnimationClip _secondAttackClip;
    [SerializeField, InspectorName("교체 2타에 1타 타이밍 사용")]
    [Tooltip("교체 2타의 적중·종료 시간과 적중 진행도를 1타에 맞춥니다. 1타와 같은 무게감의 연계에 사용하며 해제하면 기존 2타 설정을 사용합니다.")]
    private bool _secondAttackUsesOpeningTiming = true;
    [SerializeField, InspectorName("2타 클립에 Idle 복귀 포함")]
    [Tooltip("교체 2타가 끝에서 검·검집을 Idle 자세로 복귀시키면 체크합니다. 끝까지 완료한 뒤 별도 납도 모션을 재생하지 않습니다.")]
    private bool _secondAttackReturnsToIdle = true;
    private RuntimeAnimatorController _baseAnimatorController;
    private AnimatorOverrideController _attackController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _attackOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private AnimationClip _firstSourceClip;
    private AnimationClip _secondSourceClip;
    private AnimationClip _firstDefaultClip;
    private AnimationClip _secondDefaultClip;

    [Header("Presentation")]
    [SerializeField] private bool _showWeapon = true;
    [SerializeField, Min(0f)] private float _blendTime = 0.025f;
    [SerializeField, Min(0f), InspectorName("가드 전환 보간 시간")] private float _guardTransitionBlendTime = 0.1f;

    [SerializeField, Range(0.01f, 0.99f)] private float _iaidoImpactProgress = 0.88461536f;
    [SerializeField, Range(0.01f, 0.99f), InspectorName("2타 적중 진행도")] private float _normal1ImpactProgress = 0.75f;
    [SerializeField, Range(0.01f, 0.99f), InspectorName("반격 적중 진행도")] private float _counterImpactProgress = 0.4f;
    [SerializeField, Range(0.01f, 0.99f)] private float _drawnSheatheProgress = 0.2f;
    [SerializeField, Min(0.01f)] private float _minimumCancelSheatheDuration = 0.12f;

    [Header("1타 발도와 연계 보간")]
    [SerializeField, Range(0.01f, 0.99f), InspectorName("1타 적중 진행도")] private float _openingImpactProgress = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float _openingEndProgress = 0.65f;
    [SerializeField, Min(0f)] private float _comboBlendTime = 0.08f;

    private readonly Dictionary<string, float> _lengths = new Dictionary<string, float>();
    private readonly Dictionary<int, string> _stateNames = new Dictionary<int, string>();
    private string _motion = "Rest";
    private float _elapsed;
    private float _duration;
    private float _impactTime;
    private float _impactProgress;
    private float _entryProgress;
    private float _endProgress;
    private bool _openingSheathing;
    private bool _usingFirstAttackClip;
    private bool _attackReturnsToIdle;
    private bool _finishedAttackAtIdle;
    private float _flash;
    private float _lastCharge;
    private int _actionId;
    private bool _attack;
    private bool _parry;
    private bool _wasVisible;
    private Renderer[] _renderers;
    private Renderer _blade;
    private Color _bladeColor;
    private MaterialPropertyBlock _properties;
    private Transform[] _poseTargets;
    private Vector3[] _blendPositions;
    private Quaternion[] _blendRotations;
    private float _poseBlendDuration;
    private float _poseBlendElapsed;
    private bool _finishingInsertion;
    private float _insertionStart;
    private float _insertionDuration;

    [Header("보스 쳐내기 검 반동")]
    [SerializeField, Min(0.05f)] private float _attackDeflectionDuration = 0.22f;
    [SerializeField] private Vector3 _attackDeflectionOffset = new Vector3(0.05f, 0.08f, -0.16f);
    [SerializeField] private Vector3 _attackDeflectionAngles = new Vector3(-25f, 0f, 35f);
    private float _attackDeflectionRemaining;
    private Vector3 _attackDeflectionPosition;
    private Quaternion _attackDeflectionRotation;

    public string RequestedMotion => _motion;
    public bool HasFirstAttackFollowup => _firstAttackClip != null && _secondAttackClip != null;
    public bool UsesOpeningTimingForSecondAttack => HasFirstAttackFollowup && _secondAttackUsesOpeningTiming;
    public bool HasFinishedAttackAtIdle => _finishedAttackAtIdle;
    public Animator Animator => _animator;
    public string AnimatorState
    {
        get
        {
            if (_animator == null || !_animator.isInitialized) return "Not initialized";
            int hash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            string name = _stateNames.TryGetValue(hash, out string value) ? value : hash.ToString();
            return _animator.IsInTransition(0) ? name + " -> " + _motion : name;
        }
    }

    void Awake()
    {
        if (_combat == null) _combat = GetComponent<KatanaCombatCoordinator>();
        if (_actionDriver == null) _actionDriver = GetComponent<PlayerActionDriver>();
        _playerClock = GetComponent<PlayerClock>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        if (_animator == null || _animator.runtimeAnimatorController == null)
        {
            Debug.LogError("KatanaWeaponVisual requires the KatanaV2 rig Animator.", this);
            enabled = false;
            return;
        }
        foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
        {
            _lengths[clip.name] = clip.length;
            _stateNames[Animator.StringToHash(clip.name)] = clip.name;
        }
        InitializeAttackClips();
        _animator.applyRootMotion = false;
        _animator.fireEvents = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _poseTargets = new[] { _animator.transform.Find("Sword"), _animator.transform.Find("Saya") };
        _viewSpace = _animator.transform.parent;
        _blendPositions = new Vector3[_poseTargets.Length];
        _blendRotations = new Quaternion[_poseTargets.Length];
        _renderers = _animator.GetComponentsInChildren<Renderer>(true);
        _blade = _animator.transform.Find("Sword/Blade").GetComponent<Renderer>();
        _bladeColor = _blade.sharedMaterial.GetColor(BaseColorId);
        _properties = new MaterialPropertyBlock();
        _wasVisible = !_showWeapon;
    }

    void OnEnable()
    {
        if (_combat != null) _combat.EventRaised += HandleKatana;
    }

    void Start()
    {
        Play("Rest", 0f);
    }

    void OnDisable()
    {
        _attackDeflectionRemaining = 0f;
        if (_combat != null) _combat.EventRaised -= HandleKatana;
    }

    void OnDestroy()
    {
        if (_attackController == null) return;
        if (_animator != null && _animator.runtimeAnimatorController == _attackController)
            _animator.runtimeAnimatorController = _baseAnimatorController;
        Destroy(_attackController);
    }

    /// <summary>
    /// 연결된 Animator의 원본 Controller와 Rest·IaidoDoubleSlash·Normal1 클립을 보관한다.
    /// 플레이어 전용 Override Controller를 생성하여 공유 에셋 수정 없이 Inspector의 교체 클립을 적용한다.
    /// </summary>
    private void InitializeAttackClips()
    {
        _baseAnimatorController = _animator.runtimeAnimatorController;
        _attackController = new AnimatorOverrideController(_baseAnimatorController);
        _attackController.GetOverrides(_attackOverrides);
        foreach (KeyValuePair<AnimationClip, AnimationClip> pair in _attackOverrides)
        {
            if (pair.Key.name == "Rest")
            {
                _idleSourceClip = pair.Key;
                _idleDefaultClip = pair.Value != null ? pair.Value : pair.Key;
            }
            else if (pair.Key.name == "IaidoDoubleSlash")
            {
                _firstSourceClip = pair.Key;
                _firstDefaultClip = pair.Value != null ? pair.Value : pair.Key;
            }
            else if (pair.Key.name == "Normal1")
            {
                _secondSourceClip = pair.Key;
                _secondDefaultClip = pair.Value != null ? pair.Value : pair.Key;
            }
            else if (pair.Key.name == "Normal2")
            {
                _counterSourceClip = pair.Key;
                _counterDefaultClip = pair.Value != null ? pair.Value : pair.Key;
            }
            else if (pair.Key.name == "PerfectParry")
            {
                _perfectParrySourceClip = pair.Key;
                _perfectParryDefaultClip = pair.Value != null ? pair.Value : pair.Key;
            }
        }
        _animator.runtimeAnimatorController = _attackController;
        ApplyAttackClips();
    }

    /// <summary>
    /// Inspector의 Idle·1·2타·퍼펙트 패링 클립을 해당 원본 클립에 대응시킨다.
    /// firstAttack이면 1타를 교체하고 반격 Normal2에 전용 클립을 적용한다. 빈 슬롯은 기본 클립으로 복원한다.
    /// </summary>
    private void ApplyAttackClips(bool firstAttack = false)
    {
        for (int i = 0; i < _attackOverrides.Count; i++)
        {
            KeyValuePair<AnimationClip, AnimationClip> pair = _attackOverrides[i];
            AnimationClip clip = pair.Value;
            if (pair.Key == _idleSourceClip)
                clip = _idleClip != null ? _idleClip : _idleDefaultClip;
            else if (pair.Key == _firstSourceClip)
                clip = firstAttack && _firstAttackClip != null ? _firstAttackClip : _firstDefaultClip;
            else if (pair.Key == _secondSourceClip)
                clip = _secondAttackClip != null ? _secondAttackClip : _secondDefaultClip;
            else if (pair.Key == _counterSourceClip)
                clip = _counterAttackClip != null ? _counterAttackClip : _counterDefaultClip;
            else if (pair.Key == _perfectParrySourceClip)
                clip = _perfectParryClip != null ? _perfectParryClip : _perfectParryDefaultClip;
            _attackOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, clip);
        }
        _attackController.ApplyOverrides(_attackOverrides);
    }

    void Update()
    {
        if (_wasVisible != _showWeapon)
        {
            foreach (Renderer renderer in _renderers) renderer.enabled = _showWeapon;
            _wasVisible = _showWeapon;
        }
        // 불릿 타임 중에는 플레이어 시계 배율로 모션을 재생해 세계보다 덜 느리게 보이게 한다.
        _animator.speed = _combat.Player.IsPaused ? 0f : _playerClock.Scale;
        if (_combat.Player.IsPaused) return;
        float dt = _playerClock.DeltaTime;
        _elapsed += dt;
        _flash = Mathf.Max(0f, _flash - dt * 5f);
        if (_combat.Player.IsGuarding)
        {
            if (_motion == "GuardEnter" || _motion == "GuardDeflect")
            {
                _animator.SetFloat(ActionProgressId, Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration)));
                if (_elapsed >= _duration) Play("GuardHold", _blendTime);
            }
            else _animator.SetFloat(ActionProgressId, 1f);
        }
        else if (_motion == "PerfectParry")
            _animator.SetFloat(ActionProgressId, Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration)));
        else if (_motion == "Rest" && _idleClip != null)
            _animator.SetFloat(ActionProgressId, Mathf.Repeat(_elapsed / Mathf.Max(0.001f, _idleClip.length), 1f));
        else if (_openingSheathing)
            _animator.SetFloat(ActionProgressId, Mathf.Lerp(_entryProgress, 1f,
                Mathf.Clamp01(_elapsed / _combat.SheatheDuration)));
        else if (_finishingInsertion)
        {
            float progress = Mathf.SmoothStep(_insertionStart, _drawnSheatheProgress,
                Mathf.Clamp01(_elapsed / _insertionDuration));
            _animator.SetFloat(ActionProgressId, progress);
            if (_elapsed >= _insertionDuration) Play("Rest", 0.08f);
        }
        else if (_motion == "Charge" || _motion == "ChargeFromDrawn")
        {
            _lastCharge = _combat.Player.ChargeProgress;
            _animator.SetFloat(ActionProgressId, _lastCharge);
        }
        else if (_attack)
        {
            float elapsed = !_parry && _actionDriver != null
                ? _actionDriver.GetPresentationElapsed(_actionId) : _elapsed;
            float progress = elapsed < _impactTime
                ? Mathf.Lerp(_entryProgress, _impactProgress, elapsed / Mathf.Max(0.001f, _impactTime))
                : Mathf.Lerp(_impactProgress, _endProgress, (elapsed - _impactTime) / Mathf.Max(0.001f, _duration - _impactTime));
            if (_usingFirstAttackClip && _combat.IsFirstAttackFollowupBuffered)
                progress = Mathf.Min(progress, FirstAttackFollowupProgress());
            _animator.SetFloat(ActionProgressId, progress);
        }
        else if (_motion.StartsWith("Sheathe") && _motion != "SheathedHold")
            _animator.SetFloat(ActionProgressId, Mathf.Clamp01(_elapsed / _combat.SheatheDuration));
        else if (_motion == "ChargeRelease" && _elapsed >= _duration)
            Play("Rest", _blendTime);

        _blade.GetPropertyBlock(_properties, 0);
        float tint = Mathf.Max(_flash, _combat.Player.IsChargingReady ? 0.1f : 0f);
        _properties.SetColor(BaseColorId, Color.Lerp(_bladeColor, Color.cyan, tint));
        _blade.SetPropertyBlock(_properties, 0);
    }

    void LateUpdate()
    {
        if (_poseBlendDuration > 0f)
        {
            if (!_combat.Player.IsPaused) _poseBlendElapsed += _playerClock.DeltaTime;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_poseBlendElapsed / _poseBlendDuration));
            for (int i = 0; i < _poseTargets.Length; i++)
            {
                Transform target = _poseTargets[i];
                target.localPosition = Vector3.Lerp(_blendPositions[i], target.localPosition, weight);
                target.localRotation = Quaternion.Slerp(_blendRotations[i], target.localRotation, weight);
            }
            if (_poseBlendElapsed >= _poseBlendDuration) _poseBlendDuration = 0f;
        }
        if (_usingFirstAttackClip && _combat.IsFirstAttackFollowupBuffered
            && _animator.GetFloat(ActionProgressId) >= FirstAttackFollowupProgress() - 0.00001f)
        {
            // 연계 판정 전에 정확한 5번 프레임을 평가해 프레임 간격에 따른 자세 초과를 막는다.
            _firstAttackClip.SampleAnimation(_animator.gameObject, Mathf.Min(5f / _firstAttackClip.frameRate, _firstAttackClip.length));
            if (_combat.TryStartFirstAttackFollowup()) _animator.Update(0f);
        }
        ApplyPerfectParryFallback();
        ApplyAttackDeflection();
    }

    /// <summary>
    /// 퍼펙트 패링 클립이 비어 있을 때 가드 자세의 검을 화면 우측으로 완만하게 올려 꼭짓점을 찍고 가파르게 조금 내려온 뒤 곧게 되돌린다.
    /// ActionProgress와 _perfectParryPeakProgress로 궤적 진행과 복귀 가중치를 구하고, 카메라 기준 궤적 오프셋과 _perfectParryBladeAngles를 검 Transform에 더한다.
    /// </summary>
    private void ApplyPerfectParryFallback()
    {
        if (_motion != "PerfectParry" || _perfectParryClip != null) return;
        float progress = _animator.GetFloat(ActionProgressId);
        float t = 1f;
        float returnWeight = 1f;
        if (progress < _perfectParryPeakProgress)
        {
            // 튕겨낸 직후가 가장 빠르고 궤적 끝에 가까울수록 느려진다.
            float u = progress / _perfectParryPeakProgress;
            t = 1f - (1f - u) * (1f - u);
        }
        else
            returnWeight = 1f - Mathf.SmoothStep(0f, 1f,
                (progress - _perfectParryPeakProgress) / (1f - _perfectParryPeakProgress));

        // 꼭짓점까지는 사인 곡선으로 완만하게 오르고, 꼭짓점 뒤에는 제곱 곡선으로 점점 가파르게 내려온다.
        float peak = _perfectParryArcPeakPosition;
        float height = t < peak
            ? _perfectParryArcPeakHeight * Mathf.Sin(t / peak * Mathf.PI * 0.5f)
            : Mathf.Lerp(_perfectParryArcPeakHeight, _perfectParryArcEndHeight, Mathf.Pow((t - peak) / (1f - peak), 2f));
        Vector3 offset = new Vector3(_perfectParryArcWidth * t, height, _perfectParryArcDepth * t) * returnWeight;
        Quaternion viewRotation = _viewSpace.rotation;
        Quaternion bladeTurn = viewRotation * Quaternion.Euler(_perfectParryBladeAngles * (t * returnWeight))
            * Quaternion.Inverse(viewRotation);
        Transform sword = _poseTargets[0];
        sword.SetPositionAndRotation(sword.position + _viewSpace.TransformVector(offset), bladeTurn * sword.rotation);
    }

    /// <summary>
    /// 현재 검 자세를 저장하고 보스에게 쳐내진 순간의 짧은 반동을 시작한다.
    /// 설정된 반동 시간과 오프셋을 사용하며 공격 판정과 연계 상태는 유지한다.
    /// </summary>
    public void PlayAttackDeflection()
    {
        if (!isActiveAndEnabled) return;
        if (_attack)
        {
            _animator.SetFloat(ActionProgressId, _impactProgress);
            _animator.Update(0f);
        }
        Transform sword = _poseTargets[0];
        _attackDeflectionPosition = sword.localPosition;
        _attackDeflectionRotation = sword.localRotation;
        _attackDeflectionRemaining = _attackDeflectionDuration;
        _flash = 1f;
    }

    /// <summary>
    /// 저장된 충돌 자세에서 검을 빠르게 뒤로 튕기고 현재 애니메이션 자세로 복귀시킨다.
    /// 설정된 위치·회전 반동을 검에만 적용하며 일시정지 중 진행 시간을 유지한다.
    /// </summary>
    private void ApplyAttackDeflection()
    {
        if (_attackDeflectionRemaining <= 0f) return;
        if (!_combat.Player.IsPaused)
            _attackDeflectionRemaining = Mathf.Max(0f, _attackDeflectionRemaining - _playerClock.DeltaTime);
        float progress = 1f - _attackDeflectionRemaining / _attackDeflectionDuration;
        float weight = progress < 0.15f ? progress / 0.15f
            : 1f - Mathf.SmoothStep(0f, 1f, (progress - 0.15f) / 0.85f);
        Transform sword = _poseTargets[0];
        sword.localPosition = Vector3.Lerp(sword.localPosition,
            _attackDeflectionPosition + _attackDeflectionOffset, weight);
        sword.localRotation = Quaternion.Slerp(sword.localRotation,
            _attackDeflectionRotation * Quaternion.Euler(_attackDeflectionAngles), weight);
    }

    /// <summary>교체 1타의 프레임률과 길이를 사용해 5번 프레임의 정규화 진행도를 반환한다.</summary>
    private float FirstAttackFollowupProgress()
    {
        return Mathf.Clamp01(5f / Mathf.Max(0.001f, _firstAttackClip.frameRate * _firstAttackClip.length));
    }

/// <summary>e의 전투 이벤트를 발도 공격 클립과 성공 피드백으로 변환한다. 피해 판정은 호출하지 않는다.</summary>
    private void HandleKatana(KatanaEvent e)
    {
        if (e.Stage == KatanaStage.NormalStarted || e.Stage == KatanaStage.DashStarted
            || e.Stage == KatanaStage.SlashStarted || e.Stage == KatanaStage.Died)
            _attackDeflectionRemaining = 0f;
        switch (e.Stage)
        {
            case KatanaStage.NormalStarted:
                string normal = _combat.IsCounterAttack ? "Normal2"
                    : e.Strike == 1 ? "IaidoDoubleSlash" : "Normal1";
                BeginStrike(normal, e, PlayerAttackKind.Normal);
                break;
            case KatanaStage.ParrySucceeded:
                _flash = 1f;
                Play("GuardDeflect", _blendTime);
                _duration = 0.16f;
                break;
            case KatanaStage.ParryMotionStarted:
                _parry = false;
                Play("PerfectParry", _blendTime);
                _duration = _combat.PerfectParryMotionDuration;
                _animator.SetFloat(ActionProgressId, 0f);
                break;
            case KatanaStage.GuardStarted:
                _attack = false;
                _parry = false;
                _finishingInsertion = false;
                Play("GuardEnter", _guardTransitionBlendTime);
                _duration = 0.12f;
                break;
            case KatanaStage.GuardSucceeded:
                _flash = 0.6f;
                Play("GuardDeflect", _blendTime);
                _duration = 0.16f;
                break;
            case KatanaStage.GuardEnded:
                Play("GuardHold", _guardTransitionBlendTime);
                _animator.SetFloat(ActionProgressId, 1f);
                break;
            case KatanaStage.ParryMotionEnded:
            case KatanaStage.DrawnWaiting:
                _attack = false;
                _animator.SetFloat(ActionProgressId, _motion == "IaidoDoubleSlash" ? _endProgress : 1f);
                break;
            case KatanaStage.StrikeEnded:
                if (!_parry)
                {
                    float elapsed = _actionDriver != null ? _actionDriver.GetPresentationElapsed(_actionId) : _elapsed;
                    _finishedAttackAtIdle = _attack && _attackReturnsToIdle && _endProgress >= 1f
                        && elapsed >= _duration - 0.0001f;
                    _attack = false;
                    _animator.SetFloat(ActionProgressId, _endProgress);
                }
                break;
            case KatanaStage.SheatheStarted:
                if (_finishedAttackAtIdle)
                {
                    _animator.SetFloat(ActionProgressId, 1f);
                    break;
                }
                if (_motion == "IaidoDoubleSlash" && _endProgress < 1f)
                {
                    float start = _animator.GetFloat(ActionProgressId);
                    Play("IaidoDoubleSlash", _blendTime);
                    _entryProgress = start;
                    _elapsed = 0f;
                    _openingSheathing = true;
                    _animator.SetFloat(ActionProgressId, start);
                    break;
                }
                string sheath = _motion == "IaidoDoubleSlash" ? "SheatheIaido" : _motion == "Normal2" ? "Sheathe2" : "Sheathe1";
                Play(sheath, _motion == "GuardHold" || _motion == "GuardEnter" || _motion == "GuardDeflect"
                    || _motion == "PerfectParry" ? _guardTransitionBlendTime : _blendTime);
                _animator.SetFloat(ActionProgressId, 0f);
                break;
            case KatanaStage.SheatheEnded:
                if (!_combat.Player.IsCharging) Play("Rest", Mathf.Max(_blendTime, _comboBlendTime));
                break;
            case KatanaStage.ChargeStarted:
                _lastCharge = 0f;
                Play(e.WasDrawn ? "ChargeFromDrawn" : "Charge", 0.08f);
                _animator.SetFloat(ActionProgressId, 0f);
                break;
            case KatanaStage.ChargeReleased:
                ReleaseCharge();
                break;
            case KatanaStage.DashStarted:
                Play(_combat.IsCounterAttack ? "GuardHold" : "SheathedHold", _blendTime);
                break;
            case KatanaStage.SlashStarted:
                string slash = _combat.Strike == 2 ? "Normal1" : "IaidoDoubleSlash";
                BeginStrike(slash, e);
                break;
            case KatanaStage.Died:
                Play("Dead", _blendTime);
                break;
        }
    }

    /// <summary>name과 e의 타수·행동 ID 및 kind로 베기 모션을 선택한다. 공통 타격 시간과 해당 클립의 적중 진행도를 설정한다.</summary>
    private void BeginStrike(string name, KatanaEvent e, PlayerAttackKind kind = PlayerAttackKind.IaidoFinisher)
    {
        bool opening = kind == PlayerAttackKind.Normal && _combat.IsOpeningAttack;
        bool firstAttack = name == "IaidoDoubleSlash" && (opening
            || kind == PlayerAttackKind.IaidoFinisher && _combat.Player.TargetedComboStrike == 1);
        ApplyAttackClips(firstAttack);
        bool customFirstAttack = firstAttack && _firstAttackClip != null;
        bool matchedSecondAttack = name == "Normal1" && UsesOpeningTimingForSecondAttack;
        bool transitioningFromGuard = _motion == "GuardHold" || _motion == "GuardEnter" || _motion == "GuardDeflect"
            || _motion == "PerfectParry";
        float blend = transitioningFromGuard ? _guardTransitionBlendTime
            : kind == PlayerAttackKind.Normal && !opening && !_combat.IsCounterAttack
                ? Mathf.Max(_blendTime, _comboBlendTime) : _blendTime;
        if (kind == PlayerAttackKind.Normal && _combat.IsFirstAttackFollowupTransition)
            blend = 0f;
        Play(name, blend);
        _usingFirstAttackClip = customFirstAttack;
        _attackReturnsToIdle = customFirstAttack && _firstAttackReturnsToIdle
            || name == "Normal1" && _secondAttackClip != null && _secondAttackReturnsToIdle
            || name == "Normal2" && _counterAttackClip != null && _secondAttackReturnsToIdle;
        _attack = true;
        _parry = false;
        _actionId = e.ActionId;
        _impactProgress = _combat.IsCounterAttack ? _counterImpactProgress
            : opening || customFirstAttack || matchedSecondAttack ? _openingImpactProgress : name == "IaidoDoubleSlash" ? _iaidoImpactProgress
            : _normal1ImpactProgress;
        _endProgress = customFirstAttack ? 1f
            : opening ? Mathf.Max(_impactProgress, _openingEndProgress) : 1f;
        _entryProgress = 0f;
        if (_actionDriver != null)
            _actionDriver.GetPresentationTiming(kind, out _impactTime, out _duration);
        else
            _impactTime = _duration * _impactProgress;
        _animator.SetFloat(ActionProgressId, _entryProgress);
    }

    /// <summary>충전 취소 시 현재 진행도를 사용한다. 발도 상태에서 시작한 납도가 덜 끝났다면 삽입 표현만 마저 재생한다.</summary>
    private void ReleaseCharge()
    {
        bool fromDrawn = _motion == "ChargeFromDrawn";
        if (fromDrawn && _lastCharge < _drawnSheatheProgress)
        {
            _finishingInsertion = true;
            _insertionStart = _lastCharge;
            _insertionDuration = Mathf.Max(_minimumCancelSheatheDuration,
                (_drawnSheatheProgress - _lastCharge) * _duration);
            _elapsed = 0f;
            return;
        }
        float readyProgress = fromDrawn
            ? Mathf.InverseLerp(_drawnSheatheProgress, 1f, _lastCharge) : _lastCharge;
        Play("ChargeRelease", 0.08f, 1f - readyProgress);
    }

    /// <summary>name 상태로 전환하고 표현 시계를 초기화한다. blend 동안 직전 표시 자세에서 보간하며 normalizedStart는 클립 진입 위치다.</summary>
    private void Play(string name, float blend, float normalizedStart = 0f)
    {
        if (name == "Rest")
            ApplyAttackClips();
        for (int i = 0; i < _poseTargets.Length; i++)
        {
            _blendPositions[i] = _poseTargets[i].localPosition;
            _blendRotations[i] = _poseTargets[i].localRotation;
        }
        _poseBlendDuration = blend;
        _poseBlendElapsed = 0f;
        _finishingInsertion = false;
        _openingSheathing = false;
        _usingFirstAttackClip = false;
        _attackReturnsToIdle = false;
        _finishedAttackAtIdle = false;
        _motion = name;
        _attack = false;
        _duration = _lengths.TryGetValue(name, out float length) ? Mathf.Max(length, 0.001f) : 0.1f;
        _elapsed = Mathf.Clamp01(normalizedStart) * _duration;
        int hash = Animator.StringToHash("Base Layer." + name);
        // 공용 ActionProgress 초기화가 이전 상태까지 되감지 않도록 현재 표시 자세를 별도로 보간한다.
        _animator.Play(hash, 0, normalizedStart);
    }
}
