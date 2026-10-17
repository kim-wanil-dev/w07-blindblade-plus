using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using FMODUnity;

/// <summary>보스의 상태별 외형과 공간 소리·이동 발자국 및 파동을 표현한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BossController))]
public sealed class BossPresentation : MonoBehaviour
{
    private const string GUARD_SCABBARD_NAME = "Saya";
    private static readonly HashSet<BossPresentation> _activeChargeWarnings = new HashSet<BossPresentation>();
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");
    private static readonly int _guardHoldStateId = Animator.StringToHash("Base Layer.GuardHold");

    [Header("외형")]
    [SerializeField] private Renderer[] _bodyRenderers;
    private BossController _boss;
    private Vector3 _previousPosition;
    private float _stepDistanceAccumulated;
    private bool _leftStep;
    private FMODRuntimeManagerOnGUIHelper _debugOverlay;
    private bool _debugOverlayWasEnabled;
    private Coroutine _overlayRoutine;

    [Header("보스전 소리 인지")]
    [SerializeField, Min(0f), Tooltip("소리 주변 배경을 공개하는 반경(미터)")]
    private float _soundRevealRadius = 2f;
    private SensoryPerceptionMask _perceptionMask;
    private float _previousSoundRadius;
    private SoundId _playingHeartbeat;

    [Header("원거리 패턴 시야")]
    [SerializeField, Min(0f), Tooltip("원거리 준비 시 보스 주변 시야가 확장되는 최대 반경(미터). 0이면 공개하지 않는다.")]
    private float _rangedRevealRadius = 12f;

    [Header("보행")]
    [SerializeField, Min(0.1f), Tooltip("걸음 사이 이동 거리(미터)")]
    private float _stepDistance = 0.8f;
    [SerializeField] private LayerMask _groundMask = 1;
    [SerializeField] private SoundId _footstepSound = SoundId.EnemyFootstep;
    [SerializeField, Min(0.05f)] private float _minimumStepSoundInterval = 0.18f;
    private float _nextStepSoundTime;

    [Header("이동 파동")]
    [SerializeField] private RevealRippleSettings _movementRipple = new RevealRippleSettings(
        8f, 0.9f, 2, 0.12f, 1.2f, new Color(1f, 0.16f, 0.16f, 0.85f));
    [SerializeField, Min(0.05f), Tooltip("빠른 재배치·돌진 중 파동 사이 최소 시간(초)")]
    private float _movementRippleInterval = 0.45f;
    private PerceptionRevealMap _revealMap;
    private float _nextMovementRippleTime;

    [Header("돌진 기합1")]
    [SerializeField] private SoundId _chargeCry = SoundId.BossChargeCry;
    [SerializeField, Range(0.1f, 3f)] private float _chargeCryPitch = 0.85f;

    [Header("근접 공격 신호")]
    [SerializeField] private BossMeleeAttack _melee;
    [SerializeField] private SoundId _strikeCue = SoundId.EnemySwordSwing;

    [Header("돌진 붉은 테두리")]
    [SerializeField, Tooltip("돌진 패턴 동안 근접 전조와 같은 붉은 테두리를 적용할 연기 몸체 렌더러")]
    private Renderer[] _chargeWarningRenderers;
    [SerializeField, Min(0.01f), Tooltip("돌진 패턴 진입 시 붉은색이 되는 시간(초)")]
    private float _chargeWarningFadeIn = 0.15f;
    [SerializeField, Min(0.01f), Tooltip("돌진 패턴 종료 시 흰색으로 돌아가는 시간(초)")]
    private float _chargeWarningFadeOut = 0.12f;
    private MaterialPropertyBlock _warningProperties;

    internal static IReadOnlyCollection<BossPresentation> ActiveChargeWarnings => _activeChargeWarnings;
    public IReadOnlyList<Renderer> ChargeWarningRenderers => _chargeWarningRenderers;
    public float ChargeWarningIntensity { get; private set; }

    [Header("방어 자세 칼")]
    [SerializeField, Tooltip("방어 상태 동안 보스 앞에 띄울 플레이어 카타나 모형(KatanaRig)")]
    private GameObject _guardWeaponPrefab;
    [SerializeField, Tooltip("플레이어와 같은 가드 자세 상태(GuardHold)를 가진 카타나 Animator Controller")]
    private RuntimeAnimatorController _guardWeaponController;
    [SerializeField, Tooltip("플레이어를 향한 방향 기준 카타나 모형의 시점 위치(미터). x는 오른쪽, y는 높이, z는 플레이어 쪽으로 몸통 앞에 내미는 거리입니다.")]
    private Vector3 _guardWeaponOffset = new Vector3(0f, 1.6f, 0.6f);
    [SerializeField, Min(0.01f), Tooltip("카타나 모형 크기 배율. 1인칭용 모형이라 멀리서 보이도록 키웁니다.")]
    private float _guardWeaponScale = 1.5f;
    private GameObject _guardWeapon;
    private Animator _guardWeaponAnimator;

    void Awake()
    {
        _boss = GetComponent<BossController>();
        _warningProperties = new MaterialPropertyBlock();
        CreateGuardWeapon();
    }

    void Start()
    {
        ApplySoundRadius();
    }

    /// <summary>
    /// _guardWeaponPrefab을 보스 자식으로 한 번 복제해 _guardWeaponScale 크기로 두고 검집 렌더러를 끈 뒤 숨긴다. 위치와 방향은 표시 중에 갱신한다.
    /// 복제본 Animator에 _guardWeaponController를 연결하며 프리팹이 없으면 아무것도 만들지 않는다.
    /// </summary>
    private void CreateGuardWeapon()
    {
        if (_guardWeaponPrefab == null) return;
        _guardWeapon = Instantiate(_guardWeaponPrefab, transform);
        _guardWeapon.name = "BossGuardKatana";
        _guardWeapon.transform.localScale = Vector3.one * _guardWeaponScale;
        _guardWeaponAnimator = _guardWeapon.GetComponentInChildren<Animator>(true);
        if (_guardWeaponController != null) _guardWeaponAnimator.runtimeAnimatorController = _guardWeaponController;
        // 보스 가드 자세는 칼만 보이도록 검집 렌더러를 끈다. 검집 Transform은 Animator가 계속 움직여도 된다.
        Transform scabbard = _guardWeapon.transform.Find(GUARD_SCABBARD_NAME);
        if (scabbard != null)
            foreach (Renderer part in scabbard.GetComponentsInChildren<Renderer>(true))
                part.enabled = false;
        _guardWeapon.SetActive(false);
    }

    /// <summary>
    /// 보스가 방어 상태이면 카타나 모형을 켜고 플레이어와 같은 GuardHold 자세를 재생하며, 방어가 끝나거나 쳐내면 바로 숨긴다.
    /// 켜져 있는 동안 보스 몸 방향과 무관하게 매 프레임 플레이어를 향하도록 _guardWeaponOffset 위치와 회전을 갱신한다.
    /// 칼은 일반 물체처럼 파동·소리로 밝혀진 부분만 윤곽선으로 보인다.
    /// </summary>
    private void UpdateGuardWeapon()
    {
        if (_guardWeapon == null) return;
        bool defending = _boss.IsDefending && _boss.State != BossState.Dead;
        if (_guardWeapon.activeSelf != defending)
        {
            _guardWeapon.SetActive(defending);
            if (defending) _guardWeaponAnimator.Play(_guardHoldStateId, 0, 0f);
        }
        if (!defending) return;
        Vector3 facing = Game.Player != null
            ? Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up) : Vector3.zero;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Quaternion rotation = Quaternion.LookRotation(facing.normalized);
        _guardWeapon.transform.SetPositionAndRotation(transform.position + rotation * _guardWeaponOffset, rotation);
    }

    /// <summary>보스전의 소리 인지 반경을 적용하고 씬 종료 시 복원할 이전 값을 저장한다.</summary>
    private void ApplySoundRadius()
    {
        if (_perceptionMask != null || !Game.IsInitialized) return;
        _perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        _previousSoundRadius = _perceptionMask.SetSoundRadius(_soundRevealRadius);
    }

    void OnEnable()
    {
        _boss.StateChanged += HandleState;
        _boss.ChargePreparing += HandleChargePreparing;
        _boss.ChargeStarted += HandleChargeStarted;
        _boss.RangedPreparing += HandleRangedPreparing;
        _boss.RangedFiring += HandleRangedFiring;
        _boss.Heartbeat += HandleHeartbeat;
        _boss.PreparationCancelled += StopPreparationSounds;
        _boss.Teleported += ResetSteps;
        _melee.TelegraphStarted += HandleStrikePreparing;
        ResetSteps();
        HandleState(_boss.State);
        _overlayRoutine = StartCoroutine(HideDebugOverlay());
        ChargeWarningIntensity = 0f;
        _activeChargeWarnings.Add(this);
    }

    void OnDisable()
    {
        _activeChargeWarnings.Remove(this);
        ChargeWarningIntensity = 0f;
        PublishChargeWarning();
        if (_perceptionMask != null) _perceptionMask.SetSoundRadius(_previousSoundRadius);
        _perceptionMask = null;
        if (_overlayRoutine != null) StopCoroutine(_overlayRoutine);
        _overlayRoutine = null;
        _boss.StateChanged -= HandleState;
        _boss.ChargePreparing -= HandleChargePreparing;
        _boss.ChargeStarted -= HandleChargeStarted;
        _boss.RangedPreparing -= HandleRangedPreparing;
        _boss.RangedFiring -= HandleRangedFiring;
        _boss.Heartbeat -= HandleHeartbeat;
        _boss.PreparationCancelled -= StopPreparationSounds;
        _boss.Teleported -= ResetSteps;
        _melee.TelegraphStarted -= HandleStrikePreparing;
        if (Managers.IsInitialized)
        {
            StopHeartbeat();
            Managers.Sound.StopFor(gameObject, SoundId.EnemyDetected);
            Managers.Sound.StopFor(gameObject, _footstepSound);
            Managers.Sound.StopFor(gameObject, _chargeCry, forceStop: true);
            Managers.Sound.StopFor(gameObject, SoundId.EnemyRangedAttackVoice, forceStop: true);
            Managers.Sound.StopFor(gameObject, SoundId.EnemyAttackVoice, forceStop: true);
            Managers.Sound.StopFor(gameObject, _strikeCue);
        }
    }

    void OnDestroy()
    {
        if (_debugOverlay != null) _debugOverlay.enabled = _debugOverlayWasEnabled;
    }

    void LateUpdate()
    {
        UpdateGuardWeapon();
        UpdateChargeWarning();
        ApplySoundRadius();
        Vector3 movement = Vector3.ProjectOnPlane(transform.position - _previousPosition, Vector3.up);
        _previousPosition = transform.position;
        bool walking = _boss.IsRepositioning || _boss.State == BossState.Roaming || IsChargeState() || _boss.State == BossState.Duel;
        if (!walking || movement.sqrMagnitude < 0.000001f) return;
        _stepDistanceAccumulated += movement.magnitude;
        if (_stepDistanceAccumulated < _stepDistance) return;
        _stepDistanceAccumulated %= _stepDistance;
        if (Time.time >= _nextStepSoundTime)
        {
            _nextStepSoundTime = Time.time + _minimumStepSoundInterval;
            Managers.Sound.StopFor(gameObject, _footstepSound);
            Managers.Sound.Play(_footstepSound, transform.position, owner: gameObject, playDuringMovement: true);
        }
        if (!Game.IsInitialized) return;
        Vector3 direction = movement.normalized;
        EmitFootprint(direction);
        if (Time.time >= _nextMovementRippleTime)
        {
            _nextMovementRippleTime = Time.time + _movementRippleInterval;
            EmitMovementRipple();
        }
    }

    /// <summary>
    /// 돌진 상태이면 _chargeWarningFadeIn 동안 붉은 세기를 1로, 그 외 상태에서는 _chargeWarningFadeOut 동안 0으로 옮긴다.
    /// 변경된 ChargeWarningIntensity를 연기 몸체 렌더러의 경고 마스크 입력으로 전달한다.
    /// </summary>
    private void UpdateChargeWarning()
    {
        bool charging = IsChargeState();
        float duration = charging ? _chargeWarningFadeIn : _chargeWarningFadeOut;
        ChargeWarningIntensity = Mathf.MoveTowards(ChargeWarningIntensity, charging ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.01f, duration));
        PublishChargeWarning();
    }

    /// <summary>
    /// 연결된 연기 몸체 렌더러의 파티클 텍스처·기본색과 현재 ChargeWarningIntensity를 속성 블록으로 전달한다.
    /// 근접 전조와 같은 경고 마스크 입력을 사용하며 렌더러의 기존 속성 블록 값은 보존한다.
    /// </summary>
    private void PublishChargeWarning()
    {
        foreach (Renderer renderer in _chargeWarningRenderers)
        {
            Material source = renderer.sharedMaterial;
            if (source == null || !source.HasProperty(_baseMapId) || !source.HasProperty(_baseColorId)) continue;
            renderer.GetPropertyBlock(_warningProperties);
            _warningProperties.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
            _warningProperties.SetColor(_baseColorId, source.GetColor(_baseColorId));
            _warningProperties.SetFloat(_warningIntensityId, ChargeWarningIntensity);
            renderer.SetPropertyBlock(_warningProperties);
        }
    }

    /// <summary>현재 보스 위치와 Inspector 설정으로 지면 파동을 생성해 이동 방향을 알린다.</summary>
    private void EmitMovementRipple()
    {
        if (_revealMap == null)
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
        if (_revealMap == null) return;
        Vector3 origin = transform.position;
        if (Physics.Raycast(origin + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit,
            1f, _groundMask, QueryTriggerInteraction.Ignore))
            origin = hit.point;
        _revealMap.EmitRipple(origin, _movementRipple);
    }

    /// <summary>현재 보스 발 위치의 지면에 direction 방향으로 좌우 발자국을 번갈아 생성한다.</summary>
    private void EmitFootprint(Vector3 direction)
    {
        Vector3 foot = transform.position + Vector3.Cross(Vector3.up, direction) * (_leftStep ? -0.15f : 0.15f);
        _leftStep = !_leftStep;
        if (Physics.Raycast(foot + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit,
            0.8f, _groundMask, QueryTriggerInteraction.Ignore))
            Game.Instance.SensoryManager.EmitFootstep(new SensoryEvent(hit.point, hit.normal, direction), _leftStep, true);
    }

    /// <summary>퇴장·사망 외의 state에서 보스 외형을 활성화하고 걸음 누적과 상태별 소리를 갱신한다.</summary>
    private void HandleState(BossState state)
    {
        foreach (Renderer body in _bodyRenderers)
            body.enabled = state != BossState.Disappeared && state != BossState.Dead;
        ResetSteps();
        if (Managers.IsInitialized)
        {
            StopHeartbeat();
            Managers.Sound.StopFor(gameObject, SoundId.EnemyDetected);
            Managers.Sound.StopFor(gameObject, _footstepSound);
            Managers.Sound.StopFor(gameObject, _chargeCry, forceStop: true);
            Managers.Sound.StopFor(gameObject, SoundId.EnemyRangedAttackVoice, forceStop: true);
            Managers.Sound.StopFor(gameObject, SoundId.EnemyAttackVoice, forceStop: true);
            Managers.Sound.StopFor(gameObject, _strikeCue);
        }
    }

    /// <summary>원거리 발사 준비마다 이전 신호를 정리하고 _rangedRevealRadius로 보스 위치의 시야를 공개한다.</summary>
    private void HandleRangedPreparing()
    {
        StopPreparationSounds();
        if (_perceptionMask != null) _perceptionMask.RevealFootstep(transform.position, _rangedRevealRadius);
    }

    /// <summary>원거리 투사체 발사 직전 사건에서 Throw2가 연결된 기존 원거리 이벤트를 보스 위치에 한 번 재생한다.</summary>
    private void HandleRangedFiring()
    {
        Managers.Sound.PlayAttached(SoundId.EnemyRangedAttackVoice, gameObject);
    }

    /// <summary>돌진 준비 시 이전 전조·발소리를 정리한다. 실제 돌진 기합은 이동 시작 사건에서 재생한다.</summary>
    private void HandleChargePreparing()
    {
        StopPreparationSounds();
    }

    /// <summary>실제 돌진 이동 시작 사건에서 현재 보스 위치에 돌진 기합을 한 번 재생한다.</summary>
    private void HandleChargeStarted()
    {
        Managers.Sound.Play(_chargeCry, transform.position, owner: gameObject, playDuringMovement: true, pitch: _chargeCryPitch);
    }

    /// <summary>context의 근접 전조 시작에 이전 준비 신호를 정리한다. 기합과 공격음은 실제 공격 코드에서 재생한다.</summary>
    private void HandleStrikePreparing(EnemyAttackContext context)
    {
        StopPreparationSounds();
    }

    /// <summary>
    /// 보스 소유의 이전 전조·기합·발소리를 종료해 새 공격 신호의 중첩을 방지한다.
    /// 입력 없이 보스 소리만 정리하고 짧은 발소리 간격을 적용한다.
    /// </summary>
    private void StopPreparationSounds()
    {
        StopHeartbeat();
        Managers.Sound.StopFor(gameObject, SoundId.EnemyDetected);
        Managers.Sound.StopFor(gameObject, _chargeCry, forceStop: true);
        Managers.Sound.StopFor(gameObject, SoundId.EnemyRangedAttackVoice, forceStop: true);
        Managers.Sound.StopFor(gameObject, SoundId.EnemyAttackVoice, forceStop: true);
        Managers.Sound.StopFor(gameObject, _strikeCue);
        Managers.Sound.StopFor(gameObject, _footstepSound);
        _nextStepSoundTime = Time.time + _minimumStepSoundInterval;
    }

    /// <summary>보스 준비 박동 사건에서 중앙 설정의 심장박동을 보스 위치에 한 번 재생하고 ID를 보관한다.</summary>
    private void HandleHeartbeat()
    {
        _playingHeartbeat = EnemySoundSettings.GetSound(EnemySoundAction.Ranged, EnemySoundStage.Heartbeat);
        if (_playingHeartbeat != SoundId.None) Managers.Sound.PlayAttached(_playingHeartbeat, gameObject);
    }

    /// <summary>현재 보스가 재생한 박동 ID만 강제로 정지하고 보관 ID를 비운다. 피격·패링 및 다른 적 소리는 유지한다.</summary>
    private void StopHeartbeat()
    {
        if (_playingHeartbeat != SoundId.None && Managers.IsInitialized)
            Managers.Sound.StopFor(gameObject, _playingHeartbeat, forceStop: true);
        _playingHeartbeat = SoundId.None;
    }

    /// <summary>현재 상태가 시각적 발자국을 남기는 돌진 패턴인지 반환한다.</summary>
    private bool IsChargeState()
    {
        return _boss.State == BossState.Charging || _boss.State == BossState.ChainCharge;
    }

    /// <summary>현재 위치를 걸음 시작점으로 저장하고 누적 거리를 비운다.</summary>
    private void ResetSteps()
    {
        _previousPosition = transform.position;
        _stepDistanceAccumulated = 0f;
        if (Game.IsInitialized && _boss.State != BossState.Idle && _boss.State != BossState.Dead
            && _boss.State != BossState.Disappeared)
            EmitFootprint(transform.forward);
    }

    /// <summary>사운드 초기화를 기다린 뒤 FMOD 개발용 오버레이를 한 번 찾아 보스전 동안 숨긴다.</summary>
    private IEnumerator HideDebugOverlay()
    {
        while (!RuntimeManager.IsInitialized) yield return null;
        yield return null;
        if (_debugOverlay == null)
        {
            foreach (FMODRuntimeManagerOnGUIHelper overlay in Resources.FindObjectsOfTypeAll<FMODRuntimeManagerOnGUIHelper>())
            {
                if (!overlay.gameObject.scene.IsValid()) continue;
                _debugOverlay = overlay;
                break;
            }
            if (_debugOverlay != null) _debugOverlayWasEnabled = _debugOverlay.enabled;
        }
        if (_debugOverlay != null) _debugOverlay.enabled = false;
        _overlayRoutine = null;
    }
}
