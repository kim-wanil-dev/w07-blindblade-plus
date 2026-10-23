using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Receives gameplay commands and coordinates player state, combat contracts, and motor behavior.
/// It has no direct dependency on Unity Input System.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMotor))]
[DefaultExecutionOrder(-30)]
public sealed class PlayerController : MonoBehaviour
{
    private const float CROUCH_PULSE_RESTART_DELAY = 0.08f;
    private const float CROUCH_PULSE_RETRY_INTERVAL = 0.2f;
    private const float CROUCH_PULSE_SURFACE_OFFSET = 0.02f;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        EnemyCombatService combat = GetComponent<EnemyCombatService>();
        if (combat == null) return;
        Vector3 origin = _viewCamera != null ? _viewCamera.transform.position
            : transform.position + Vector3.up * _attackOriginHeight;
        combat.DrawAttackPreview(origin, GetHorizontalViewDirection(), _normalAttackRange);
    }
#endif

    [Header("References")]
    [SerializeField] private PlayerMotor _motor;
    [SerializeField] private Camera _viewCamera;
    [SerializeField] private MonoBehaviour _perceptionProviderSource;
    [SerializeField] private MonoBehaviour _combatServiceSource;
    [SerializeField] private MonoBehaviour _mapServiceSource;

    [Header("Charge")]
    [SerializeField] private bool _enableChargeAttack;
    [SerializeField, Min(0f)] private float _minimumChargeTime = 1f;

    [Header("Attack")]
    [SerializeField, Tooltip("좌클릭 시 화면 중앙의 공개된 적에게 대상 돌진(발도)을 사용합니다. 끄면 항상 제자리 일반 공격을 사용하고 대상 실 표시도 숨깁니다.")]
    private bool _enableTargetedDashAttack = true;
    [SerializeField, Tooltip("대상 돌진을 꺼도 불릿 타임(쳐내기·극한 회피 준비) 중 첫 공격은 실이 연결된 적에게 대상 돌진(발도)을 사용합니다. 대상 실 표시도 이때 연결됩니다.")]
    private bool _enableBulletTimeDashAttack = true;
    [SerializeField, Min(0f)] private float _normalAttackRange = 1.5f;
    [SerializeField, Min(0f)] private float _iaidoAttackRange = 2f;
    [SerializeField, Min(0f)] private float _targetStandOff = 0.15f;
    [SerializeField, Min(0f)] private float _attackOriginHeight = 1.1f;
    [SerializeField, Min(0f)] private float _targetedIaidoMaxDistance = 10f;

    [Header("Crouch input")]
    [SerializeField, Min(0f), Tooltip("웅크리기를 다시 시작할 수 있는 최소 간격. 일어서기는 즉시 허용합니다.")]
    private float _crouchReentryInterval = 0.5f;

    [Header("Recovery (scaled game time)")]
    [SerializeField, Min(0f)] private float _normalAttackRecovery = 0.25f;
    [SerializeField, Min(0f)] private float _guardStartRecovery = 0f;
    [SerializeField, Min(0f)] private float _guardEndRecovery = 0f;
    [SerializeField, Min(0f)] private float _guardSuccessRecovery = 0f;
    [SerializeField, Min(0f)] private float _chargeEarlyCancelRecovery = 0f;
    [SerializeField, Min(0f)] private float _chargeReleaseRecovery = 0f;
    [SerializeField, Min(0f)] private float _iaidoHitRecovery = 0.15f;
    [SerializeField, Min(0f)] private float _iaidoMissRecovery = 0.7f;
    [SerializeField, Min(0f)] private float _iaidoWallBlockedRecovery = 0.9f;

    private PlayerActionState _state = PlayerActionState.Free;
    private PlayerActionState _stateBeforePause = PlayerActionState.Free;
    private IPlayerPerceptionProvider _perceptionProvider;
    private IPlayerCombatService _combatService;
    private IPlayerMapService _mapService;
    private PlayerStamina _stamina;
    private PlayerClock _playerClock;
    private PlayerEmpower _empower;
    private Vector2 _moveInput;
    private Vector2 _requestedMoveInput;
    private float _chargeElapsed;
    private float _recoveryRemaining;
    private bool _recoveryAllowsMovement;

    private bool _chargeReady;
    private bool _attackImpactResolved;
    private bool _attackActionFinished;
    private int _nextActionId;
    private int _currentActionId;
    private string _preferredTargetId;
    private Vector3 _lockedAttackDirection;
    private bool _targetedDashAttack;
    private bool _counterDashAttack;
    private bool _attackDeathConfirmed;
    private int _targetedComboStrike;
    private PlayerAttackOutcome _currentAttackOutcome;
    private PlayerSenseContext _lastSenseContext;
    private bool _hasSenseContext;
    private bool _crouchInputActive;
    private float _nextCrouchEntryTime;
    private float _nextCrouchPulseTime;

    public event Action<PlayerActionEvent> ActionEvent;
    public event Action<PlayerFootstepEvent> FootstepEmitted;
    public event Action<PlayerSenseContext> SenseContextChanged;

    public PlayerActionState CurrentState => _state;
    public int TargetedComboStrike => _targetedComboStrike;
    public bool IsGuarding => _state == PlayerActionState.Guarding;
    public bool IsCharging => _state == PlayerActionState.Charging;
    public bool IsChargingReady => _state == PlayerActionState.Charging && _chargeReady;
    /// <summary>실제 차징 시간을 0~1로 읽어 표현에 전달한다. 행동 타이밍은 변경하지 않는다.</summary>
    public float ChargeProgress => _minimumChargeTime <= 0f ? 1f : Mathf.Clamp01(_chargeElapsed / _minimumChargeTime);
    public bool IsPaused => _state == PlayerActionState.Paused;
    public int CurrentActionId => _currentActionId;
    public bool IsInRecovery => _state == PlayerActionState.Recovery;
    /// <summary>일반 공격 후 회복 중이며 이동을 허용하는 상태인지 반환한다.</summary>
    public bool IsAttackRecoveryMovement => _state == PlayerActionState.Recovery && _recoveryAllowsMovement;
    /// <summary>불릿 타임 대상 돌진이 켜져 있고 쳐내기·극한 회피 집중(불릿 타임) 중이면 true를 반환한다. 일섬 집중과 거합 자동 차징은 제외한다.</summary>
    public bool IsBulletTimeDashReady => _enableBulletTimeDashAttack && _empower.IsReady
        && !_empower.IsSwordWaveFocusing && !_empower.IsAutomaticCharging;

    public float RecoveryRemaining => _recoveryRemaining;
    public bool IsCrouching => _motor != null && _motor.IsCrouching;

    private void Awake()
    {
        if (_motor == null)
            _motor = GetComponent<PlayerMotor>();
        if (_viewCamera == null)
            _viewCamera = GetComponentInChildren<Camera>();
        _stamina = GetComponent<PlayerStamina>();
        _playerClock = GetComponent<PlayerClock>();
        _empower = GetComponent<PlayerEmpower>();

        BindConfiguredServices();
        Game.Instance.RegisterPlayer(this);
    }

    void OnDestroy()
    {
        Game.UnregisterPlayer(this);
    }

    private void OnEnable()
    {
        if (_motor == null)
            _motor = GetComponent<PlayerMotor>();

        if (_motor != null)
        {
            _motor.FootstepEmitted += ForwardFootstep;
            _motor.DashEnded += HandleDashEnded;
        }
    }

    private void OnDisable()
    {
        if (_motor != null)
        {
            _motor.FootstepEmitted -= ForwardFootstep;
            _motor.DashEnded -= HandleDashEnded;
        }
    }

    private void Update()
    {
        if (_state == PlayerActionState.Paused || _state == PlayerActionState.Dead)
            return;

        UpdateCrouchPulse();

        // 이동·회복·차징은 불릿 타임 중 세계보다 덜 느린 플레이어 시계로 진행한다.
        float deltaTime = _playerClock.DeltaTime;
        if (_state == PlayerActionState.Recovery)
        {
            _recoveryRemaining = Mathf.Max(0f, _recoveryRemaining - deltaTime);
            if (_recoveryRemaining <= 0f)
                EndRecovery();
        }
        else if (_state == PlayerActionState.Charging && !_chargeReady)
        {
            _chargeElapsed += deltaTime;
            if (_chargeElapsed >= _minimumChargeTime)
            {
                _chargeReady = true;
                Publish(PlayerActionStage.ChargeReady);
            }
        }

        PlayerLocomotionMode mode = GetLocomotionMode();
        bool movementLocked = IsMovementLocked();
        bool lookLocked = _state == PlayerActionState.Paused || _state == PlayerActionState.Dead;
        _motor?.Tick(mode, movementLocked, lookLocked, deltaTime);
        PublishSenseContext(mode);
    }

    /// <summary>감지, 전투, 맵 서비스 구현체를 등록합니다. 전달한 계약을 저장하고 전투·맵 서비스를 PlayerMotor에도 전달합니다.</summary>
    public void ConfigureServices(
        IPlayerPerceptionProvider perception,
        IPlayerCombatService combat,
        IPlayerMapService map)
    {
        _perceptionProvider = perception;
        _combatService = combat;
        _mapService = map;
        if (_motor != null)
        {
            _motor.ConfigureMapService(_mapService);
            _motor.ConfigureCombatService(_combatService);
        }
        _hasSenseContext = false;
    }

    /// <summary>
    /// combatSource를 공격 판정 서비스로 연결하고 이동 장치에도 전달한다.
    /// 기존 감지 및 맵 서비스 연결은 유지한다.
    /// </summary>
    public void ConfigureCombatService(MonoBehaviour combatSource)
    {
        _combatServiceSource = combatSource;
        _combatService = combatSource as IPlayerCombatService;
        _motor.ConfigureCombatService(_combatService);
        _hasSenseContext = false;
    }


    /// <summary>이동 입력을 저장하고 이동 장치에 전달합니다. 회복, 정지, 사망, 발도 동작 중에는 입력을 0으로 제한합니다.</summary>
    /// <summary>이동 입력을 저장하고 이동 장치에 전달한다. 공격 후 회복에서는 입력을 유지하며 다른 회복·정지·사망·발도 동작 중에는 제한한다.</summary>
    public void SetMovementInput(Vector2 value)
    {
        if (_state == PlayerActionState.IaidoDash)
        {
            _requestedMoveInput = Vector2.ClampMagnitude(value, 1f);
            _moveInput = Vector2.zero;
            _motor.SetMoveInput(Vector2.zero);
            return;
        }

        if (_state == PlayerActionState.Paused
            || _state == PlayerActionState.Dead
            || _state == PlayerActionState.IaidoSlash)
        {
            _requestedMoveInput = Vector2.zero;
            _moveInput = Vector2.zero;
            _motor?.SetMoveInput(Vector2.zero);
            return;
        }

        _requestedMoveInput = Vector2.ClampMagnitude(value, 1f);
        if (_state == PlayerActionState.Recovery)
        {
            _moveInput = _recoveryAllowsMovement ? _requestedMoveInput : Vector2.zero;
            _motor?.SetMoveInput(_moveInput);
            return;
        }

        _moveInput = _requestedMoveInput;
        _motor?.SetMoveInput(_moveInput);
    }

    /// <summary>현재 이동 입력으로 짧은 회피를 요청하고 수락 여부를 반환한다. 거부 이유는 사용하지 않는다.</summary>
    public bool RequestDodge() => RequestDodge(out _);

    /// <summary>
    /// 현재 이동 입력으로 짧은 회피를 요청하고 수락 여부를 반환하며 거부되면 reason에 이유를 출력한다.
    /// 일반 공격·발도 돌진·돌진 베기·공격 후딜 중이면 그 공격을 먼저 끝내고 회피한다. 가드는 유지한다.
    /// 충전·다른 회복·사망·일시정지에서는 거부하고, 다른 조건을 통과해도 스태미너가 회피 비용보다 적으면 NotEnoughStamina로 거부한다.
    /// </summary>
    public bool RequestDodge(out PlayerInputRejectReason reason)
    {
        if (Time.timeScale <= 0f || _state == PlayerActionState.Paused)
            reason = PlayerInputRejectReason.Unavailable;
        else if (!CanDodgeFromState())
            reason = PlayerInputRejectReason.ActionBusy;
        else
            reason = _motor.GetDodgeBlockReason();
        if (reason == PlayerInputRejectReason.None && !_stamina.CanAffordDodge())
            reason = PlayerInputRejectReason.NotEnoughStamina;
        if (reason != PlayerInputRejectReason.None) return false;

        // 공격 상태가 남으면 이동 잠금이 회피를 끊으므로 회피 전에 공격을 끝낸다.
        if (_state != PlayerActionState.Free && _state != PlayerActionState.Guarding)
            CancelAttackForDodge();
        return _motor.TryBeginDodge(_requestedMoveInput);
    }

    /// <summary>
    /// 강화 공격이 시작되면 진행 중인 일반 공격·발도 돌진·돌진 베기·회복을 결과 없이 끝내 자유 상태로 만든다.
    /// 가드는 KatanaCombatCoordinator가 끝내며 자유·가드·사망·일시정지 상태는 바꾸지 않는다.
    /// </summary>
    public void CancelActionForEmpowerStrike()
    {
        if (_state == PlayerActionState.NormalAttack || _state == PlayerActionState.IaidoDash
            || _state == PlayerActionState.IaidoSlash || _state == PlayerActionState.Recovery)
            CancelAttackForDodge();
    }

    /// <summary>현재 행동 상태에서 회피를 받을 수 있는지 반환한다. 자유·가드·일반 공격·발도 돌진·돌진 베기와 공격이 끝난 뒤의 회복이 대상이다.</summary>
    private bool CanDodgeFromState()
    {
        return _state == PlayerActionState.Free
            || _state == PlayerActionState.Guarding
            || _state == PlayerActionState.NormalAttack
            || _state == PlayerActionState.IaidoDash
            || _state == PlayerActionState.IaidoSlash
            || (_state == PlayerActionState.Recovery && _attackActionFinished);
    }

    /// <summary>
    /// 회피 요청으로 진행 중인 일반 공격·발도 돌진·돌진 베기를 결과 없이 끝내고 자유 상태로 바꾼다. 공격 후 회복이면 바로 끝낸다.
    /// 남은 피해 판정을 닫고 대상 연계 타수를 초기화하며 발도 돌진은 행동 종료 뒤 멈춘다.
    /// </summary>
    private void CancelAttackForDodge()
    {
        _targetedComboStrike = 0;
        if (_state == PlayerActionState.Recovery)
        {
            EndRecovery();
            return;
        }

        bool dashing = _state == PlayerActionState.IaidoDash;
        _attackImpactResolved = true;
        FinishAttackAction(0f, PlayerAttackOutcome.None, true);
        // 행동을 먼저 끝내야 대시 종료 사건이 베기로 이어지지 않는다.
        if (dashing) _motor.CancelDash();
    }

    /// <summary>웅크리기 진입은 재입력 간격으로 제한하고, 해제는 즉시 이동 장치에 전달합니다.</summary>
    public void SetCrouchInput(bool isCrouching)
    {
        if (_state == PlayerActionState.Paused || _state == PlayerActionState.Dead)
            return;

        bool enteredCrouch = isCrouching && !_crouchInputActive;
        if (enteredCrouch && Time.time < _nextCrouchEntryTime)
            return;

        _crouchInputActive = isCrouching;
        _motor?.SetCrouchInput(isCrouching);
        if (!isCrouching)
        {
            _nextCrouchPulseTime = 0f;
            return;
        }

        if (enteredCrouch)
        {
            _nextCrouchEntryTime = Time.time + Mathf.Max(0f, _crouchReentryInterval);
            _nextCrouchPulseTime = Time.time + EmitCrouchPulse();
        }
    }

    /// <summary>웅크리기 입력과 다음 발행 시각을 사용해 이전 파동 종료 후 감각 파동을 반복 발행한다.</summary>
    private void UpdateCrouchPulse()
    {
        if (_crouchInputActive && Time.time >= _nextCrouchPulseTime)
            _nextCrouchPulseTime = Time.time + EmitCrouchPulse();
    }

    /// <summary>
    /// 충돌 캡슐 아래의 지면 위치와 시선 방향으로 웅크리기 파동을 생성한다.
    /// 플레이어 자체와 트리거를 제외하고 표면 위에 배치하며, 성공 여부에 따른 다음 발행 간격을 반환한다.
    /// </summary>
    private float EmitCrouchPulse()
    {
        if (!Game.IsInitialized)
            return CROUCH_PULSE_RETRY_INTERVAL;

        SensoryManager sensoryManager = Game.Instance.SensoryManager;
        if (sensoryManager == null)
            return CROUCH_PULSE_RETRY_INTERVAL;

        float interval = Mathf.Max(0.01f, sensoryManager.CrouchPulseInterval + CROUCH_PULSE_RESTART_DELAY);
        if (!sensoryManager.IsReady)
            return CROUCH_PULSE_RETRY_INTERVAL;

        Vector3 direction = _viewCamera != null ? _viewCamera.transform.forward : transform.forward;
        CharacterController controller = GetComponent<CharacterController>();
        Bounds bounds = controller.bounds;
        Vector3 position = transform.position;
        Vector3 normal = Vector3.up;
        float closestDistance = float.PositiveInfinity;
        RaycastHit[] hits = Physics.RaycastAll(bounds.center, Vector3.down,
            bounds.extents.y + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y <= 0f
                || hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            normal = hit.normal;
            position = hit.point + normal * CROUCH_PULSE_SURFACE_OFFSET;
        }
        bool spawned = sensoryManager.Emit(new SensoryEvent(position, normal, direction));
        return spawned ? interval : CROUCH_PULSE_RETRY_INTERVAL;
    }

    /// <summary>마우스 또는 패드 시점 변화량을 이동 장치에 누적 전달합니다. 일시정지나 사망 상태에서는 입력을 무시합니다.</summary>
    public void AddLookInput(Vector2 delta)
    {
        if (_state == PlayerActionState.Paused || _state == PlayerActionState.Dead)
            return;
        _motor?.AddLookDelta(delta);
    }

    /// <summary>자유 상태에서 가드를 시작합니다. 상태가 허용되지 않으면 false를 반환합니다.</summary>
    public bool BeginGuard()
    {
        if (_state != PlayerActionState.Free)
            /// <summary>거부된 행동 요청 이벤트를 게시하고 false를 반환합니다.</summary>
            return RejectRequest();

        SetState(PlayerActionState.Guarding, PlayerActionStage.GuardStarted);
        if (_guardStartRecovery > 0f)
            BeginRecovery(_guardStartRecovery, PlayerAttackOutcome.None);
        return true;
    }

    /// <summary>가드 상태를 종료하고 설정된 가드 종료 회복 시간을 적용합니다.</summary>
    public void EndGuard()
    {
        if (_state != PlayerActionState.Guarding)
            return;

        SetState(PlayerActionState.Free, PlayerActionStage.GuardEnded);
        BeginRecovery(_guardEndRecovery, PlayerAttackOutcome.None);
    }

    /// <summary>현재 가드 상태를 검사해 적 공격을 막을 수 있는지 반환합니다.</summary>
    public bool TryBlockIncomingAttack()
    {
        return _state == PlayerActionState.Guarding;
    }

    /// <summary>적 시스템이 확정한 가드 성공을 알리고 설정된 회복을 적용합니다.</summary>
    public void NotifyGuardSuccess()
    {
        if (_state != PlayerActionState.Guarding)
            return;

        Publish(PlayerActionStage.GuardSucceeded);
        BeginRecovery(_guardSuccessRecovery, PlayerAttackOutcome.None);
    }

    /// <summary>자유 상태에서 기 모으기를 시작하고 충전 시간을 초기화합니다. 상태가 허용되지 않으면 false를 반환합니다.</summary>
    public bool BeginCharge()
    {
        if (!_enableChargeAttack || _state != PlayerActionState.Free)
            /// <summary>거부된 행동 요청 이벤트를 게시하고 false를 반환합니다.</summary>
            return RejectRequest();

        _chargeElapsed = 0f;
        _chargeReady = _minimumChargeTime <= 0f;
        SetState(PlayerActionState.Charging, PlayerActionStage.ChargeStarted);
        if (_chargeReady)
            Publish(PlayerActionStage.ChargeReady);
        return true;
    }

    /// <summary>충전을 끝내고 완료 여부에 따라 충전 결과를 게시합니다. 설정된 조기 취소 또는 완료 회복을 적용합니다.</summary>
    public void EndCharge()
    {
        if (_state != PlayerActionState.Charging)
            return;

        bool completed = _chargeReady;
        _chargeElapsed = 0f;
        _chargeReady = false;
        SetState(PlayerActionState.Free, completed ? PlayerActionStage.ChargeEnded : PlayerActionStage.ChargeCancelled);
        BeginRecovery(completed ? _chargeReleaseRecovery : _chargeEarlyCancelRecovery, PlayerAttackOutcome.None);
    }

    /// <summary>자유 상태에서 일반 공격을 시작하고 공격 방향과 행동 식별자를 고정합니다. 요청 수락 여부를 반환합니다.</summary>
    public bool RequestNormalAttack()
    {
        if (_state != PlayerActionState.Free)
            return RejectRequest();

        StartAttack(PlayerActionState.NormalAttack);
        _lockedAttackDirection = GetHorizontalViewDirection();
        _motor?.BeginAttackLunge(_lockedAttackDirection);
        Publish(PlayerActionStage.AttackStarted);
        return true;
    }

    /// <summary>actionId의 일반 공격 판정이 끝났으면 남은 후딜을 마치고 새 일반 공격을 시작한다. 새 행동 ID를 발급하고 성공 여부를 반환한다.</summary>
    /// <summary>actionId의 일반 공격이 명중 판정을 마쳤으면 후속 공격으로 즉시 전환하고 성공 여부를 반환한다.</summary>
    public bool TryChainNormalAttack(int actionId)
    {
        if (_state != PlayerActionState.NormalAttack || actionId != _currentActionId
            || !_attackImpactResolved || _attackActionFinished) return false;
        FinishAttackAction(0f, _currentAttackOutcome, true);
        return RequestNormalAttack();
    }

    /// <summary>
    /// actionId의 일반 공격이 연계 가능한 상태인지 확인하고 공개된 돌진 대상을 찾는다.
    /// bulletTimeDash가 참이면 대상 돌진이 꺼져 있어도 불릿 타임 입력으로 보고 대상을 찾는다.
    /// 유효하면 현재 공격을 끝내고 strike 타수의 대상 돌진을 시작하며 성공 여부를 반환한다.
    /// </summary>
    public bool TryChainTargetedIaidoAttack(int actionId, int strike, bool bulletTimeDash = false)
    {
        if (_state != PlayerActionState.NormalAttack
            || actionId != _currentActionId
            || !_attackImpactResolved
            || _attackActionFinished
            || strike < 1
            || strike > 3
            || !TrySelectIaidoTarget(
                out PlayerCombatTarget combatTarget,
                out Vector3 perceivedPosition,
                targetedAttack: true,
                bulletTimeDash: bulletTimeDash))
            return false;

        FinishAttackAction(0f, _currentAttackOutcome, true);
        _targetedComboStrike = strike;
        return BeginTargetedDashAttack(combatTarget, perceivedPosition);
    }

    /// <summary>actionId의 미적중 일반 공격을 취소합니다. skipRecovery는 가드 전환 시 공격 후딜 없이 즉시 상태를 넘깁니다.</summary>
    public bool CancelNormalAttack(int actionId, bool skipRecovery = false)
    {
        if (_state != PlayerActionState.NormalAttack
            || actionId != _currentActionId
            || _attackActionFinished
            || _attackImpactResolved)
            return false;

        _attackImpactResolved = true;
        _currentAttackOutcome = PlayerAttackOutcome.None;
        float recovery = skipRecovery ? 0f : _normalAttackRecovery;
        FinishAttackAction(recovery, PlayerAttackOutcome.None, true);
        return true;
    }
    /// <summary>현재 일반 공격의 적중 프레임에서 적 시스템에 판정을 요청합니다. actionId가 현재 행동과 다르면 무시합니다.</summary>
    public void NotifyNormalAttackImpact(int actionId)
    {
        if (_state != PlayerActionState.NormalAttack || actionId != _currentActionId || _attackImpactResolved)
            return;

        NotifyNormalAttackSweep(actionId, true);
    }

    /// <summary>actionId의 베기 구간을 finalSample까지 검사하고 완료된 피해·빗나감 결과만 행동에 반영한다.</summary>
    public void NotifyNormalAttackSweep(int actionId, bool finalSample)
    {
        if (_state != PlayerActionState.NormalAttack || actionId != _currentActionId || _attackImpactResolved) return;
        EnemyCombatService combat = _combatService as EnemyCombatService;
        if (combat == null) return;
        if (_counterDashAttack)
        {
            if (!_combatService.TryGetTarget(_preferredTargetId, out PlayerCombatTarget target)
                || !target.IsAlive || target.HitCollider == null)
            {
                ApplyAttackResolution(new PlayerAttackResolution(actionId, false, null, false));
                return;
            }
            Vector3 direction = (target.HitCollider.bounds.center - _motor.ViewOrigin).normalized;
            PlayerAttackRequest counterRequest = new PlayerAttackRequest(actionId, PlayerAttackKind.Normal,
                _preferredTargetId, _motor.ViewOrigin, direction, _normalAttackRange, Time.time);
            PlayerAttackResolution counterResolution = combat.ResolvePlayerDirectedAttack(counterRequest, finalSample);
            if (counterResolution.IsComplete) ApplyAttackResolution(counterResolution);
            return;
        }
        PlayerAttackRequest request = new PlayerAttackRequest(actionId, PlayerAttackKind.Normal, null,
            _motor.ViewOrigin, _motor.ViewForward, _normalAttackRange, Time.time);
        PlayerAttackResolution resolution = combat.ResolvePlayerBladeAttack(request, finalSample);
        if (resolution.IsComplete) ApplyAttackResolution(resolution);
    }

    /// <summary>
    /// duration의 피격 회복을 적용한다. 일반 공격·반격 대시를 중단하고 추가 피해 판정을 닫는다.
    /// 가드 중 막지 못한 피격은 가드 상태와 남은 패링 창을 유지하고 회복을 적용하지 않는다.
    /// </summary>
    public void NotifyHitReaction(float duration)
    {
        if (_counterDashAttack && _state == PlayerActionState.IaidoDash)
        {
            FinishAttackAction(duration, PlayerAttackOutcome.None);
            _motor.CancelDash();
            _counterDashAttack = false;
        }
        else if (_state == PlayerActionState.NormalAttack)
        {
            _attackImpactResolved = true;
            FinishAttackAction(duration, _currentAttackOutcome);
        }
        else if (_state == PlayerActionState.Free)
        {
            BeginRecovery(duration, PlayerAttackOutcome.None);
        }
        else if (_state == PlayerActionState.Recovery)
            _recoveryRemaining = Mathf.Max(_recoveryRemaining, duration);
    }

    /// <summary>일반 공격 애니메이션 종료를 처리하고 미판정 공격을 빗나감으로 확정한 뒤 회복을 적용합니다.</summary>
    /// <summary>일반 공격 애니메이션 종료를 처리하고 미판정 공격을 빗나감으로 확정한 뒤 이동 가능한 회복을 적용한다.</summary>
    public void NotifyNormalAttackAnimationEnded(int actionId)
    {
        if (_state != PlayerActionState.NormalAttack || actionId != _currentActionId || _attackActionFinished)
            return;

        if (!_attackImpactResolved)
            ApplyAttackResolution(new PlayerAttackResolution(_currentActionId, false, null, false));

        FinishAttackAction(_normalAttackRecovery, _currentAttackOutcome, true);
    }

    /// <summary>완료된 기 모으기에서 감지 대상과 중앙 화면 영역으로 목표를 고정하고 발도 대시를 시작합니다. 요청 성공 여부를 반환합니다.</summary>
    public bool RequestIaidoAttack()
    {
        if (!_enableChargeAttack || _state != PlayerActionState.Charging || !_chargeReady)
            return RejectRequest();

        _targetedComboStrike = 0;
        StartAttack(PlayerActionState.IaidoDash);
        _chargeElapsed = 0f;
        _chargeReady = false;

        bool foundTarget = TrySelectIaidoTarget(out PlayerCombatTarget combatTarget, out Vector3 perceivedPosition);
        if (foundTarget)
        {
            _preferredTargetId = combatTarget.TargetId;
            _lockedAttackDirection = Vector3.ProjectOnPlane(perceivedPosition - transform.position, Vector3.up).normalized;
        }
        else
        {
            _preferredTargetId = null;
            _lockedAttackDirection = GetHorizontalViewDirection();
        }

        Publish(PlayerActionStage.DashStarted);
        bool began = _motor != null && _motor.BeginIaidoDash(
            _preferredTargetId,
            foundTarget ? perceivedPosition : transform.position,
            foundTarget ? combatTarget.HitCollider : null,
            foundTarget ? combatTarget.CollisionRadius : 0f,
            _motor != null ? _motor.IaidoMaxDistance : 0f,
            _targetStandOff);

        if (!began)
        {
            SetState(PlayerActionState.Free, PlayerActionStage.RequestRejected);
            return false;
        }

        return true;
    }

    /// <summary>
    /// strike 순번으로 감지된 유효 대상을 선택해 즉사 판정 돌진 공격을 시작한다. 대상이 없거나 대시를 시작할 수 없으면 false를 반환한다.
    /// bulletTimeDash가 참이면 대상 돌진이 꺼져 있어도 불릿 타임 입력으로 보고 대상을 찾는다.
    /// </summary>
    public bool RequestTargetedIaidoAttack(int strike = 1, bool bulletTimeDash = false)
    {
        if (_state != PlayerActionState.Free
            || strike < 1
            || strike > 3
            || !TrySelectIaidoTarget(out PlayerCombatTarget combatTarget, out Vector3 perceivedPosition,
                targetedAttack: true, bulletTimeDash: bulletTimeDash))
            return false;

        _targetedComboStrike = strike;
        return BeginTargetedDashAttack(combatTarget, perceivedPosition);
    }

    /// <summary>
    /// 지금 좌클릭하면 대상 돌진이 고를 적을 TrySelectIaidoTarget과 같은 규칙으로 조회한다.
    /// 대상 돌진이 꺼져 있으면 IsBulletTimeDashReady가 참인 불릿 타임 동안만 후보를 찾는다.
    /// 자유·가드 상태에서만 target에 후보를 출력하고 찾으면 true를 반환하며 행동 상태는 변경하지 않는다.
    /// </summary>
    public bool TryGetTargetedDashCandidate(out PlayerCombatTarget target)
    {
        target = default;
        if (_state != PlayerActionState.Free && _state != PlayerActionState.Guarding)
            return false;

        return TrySelectIaidoTarget(out target, out _, targetedAttack: true, bulletTimeDash: IsBulletTimeDashReady);
    }

    /// <summary>targetId의 생존 적에게 기존 대상 대시를 시작한다. 거리·벽 제한을 검사하고 반격 대시의 수락 여부를 반환한다.</summary>
    public bool RequestCounterDashAttack(string targetId)
    {
        if (_state != PlayerActionState.Free
            || !_combatService.TryGetTarget(targetId, out PlayerCombatTarget target)
            || !target.IsAlive || target.HitCollider == null
            || Vector3.ProjectOnPlane(target.WorldPosition - transform.position, Vector3.up).magnitude > _motor.IaidoMaxDistance
            || !_motor.CanDashToTarget(target))
            return false;

        _targetedComboStrike = 0;
        return BeginTargetedDashAttack(target, target.WorldPosition, true);
    }

    /// <summary>
    /// target과 perceivedPosition으로 대시 공격을 시작한다. counterAttack이면 도착 후 일반 피해의 단발 반격을 사용하며 시작 여부를 반환한다.
    /// 회피 이동 중이면 회피를 끊고 지금 위치에서 바로 돌진한다.
    /// </summary>
    private bool BeginTargetedDashAttack(PlayerCombatTarget target, Vector3 perceivedPosition, bool counterAttack = false)
    {
        StartAttack(PlayerActionState.IaidoDash);
        float maxDistance = counterAttack ? _motor.IaidoMaxDistance : _targetedIaidoMaxDistance;
        _counterDashAttack = counterAttack;
        _targetedDashAttack = true;
        _preferredTargetId = target.TargetId;
        _lockedAttackDirection = Vector3.ProjectOnPlane(perceivedPosition - transform.position, Vector3.up).normalized;
        Publish(PlayerActionStage.DashStarted);

        // 회피 이동이 끝날 때까지 돌진이 미뤄지면 지금 위치로 정한 방향·거리가 어긋나므로 회피 이동을 먼저 끊는다.
        _motor.CancelDodge();
        bool began = _motor != null && _motor.BeginIaidoDash(_preferredTargetId, perceivedPosition, target.HitCollider, target.CollisionRadius, maxDistance, _targetStandOff);

        if (began)
            return true;

        _targetedDashAttack = false;
        _counterDashAttack = false;
        SetState(PlayerActionState.Free, PlayerActionStage.RequestRejected);
        return false;
    }

    /// <summary>
    /// actionId가 현재 발도 행동과 같으면 적 시스템에 한 번의 베기 판정을 요청한다.
    /// 대상 지정 공격은 현재 대상 충돌체 중심 방향을 사용하며 판정 결과를 공격 상태에 저장한다.
    /// </summary>
    public void NotifyIaidoSlashImpact(int actionId)
    {
        if (_state != PlayerActionState.IaidoSlash || actionId != _currentActionId || _attackImpactResolved)
            return;

        Vector3 direction = _lockedAttackDirection;
        if (_targetedDashAttack
            && _combatService.TryGetTarget(_preferredTargetId, out PlayerCombatTarget target)
            && target.IsAlive && target.HitCollider != null)
        {
            Vector3 origin = _viewCamera != null
                ? _viewCamera.transform.position
                : transform.position + Vector3.up * _attackOriginHeight;
            direction = (target.HitCollider.bounds.center - origin).normalized;
        }

        PlayerAttackResolution resolution = ResolveAttack(
            PlayerAttackKind.IaidoFinisher,
            _preferredTargetId,
            _iaidoAttackRange,
            direction);
        ApplyAttackResolution(resolution);
    }

    /// <summary>발도 베기 종료를 처리하고 적중·빗나감 결과에 맞는 회복 시간을 적용합니다.</summary>
    public void NotifyIaidoSlashAnimationEnded(int actionId)
    {
        if (_state != PlayerActionState.IaidoSlash || actionId != _currentActionId || _attackActionFinished)
            return;

        if (!_attackImpactResolved)
            ApplyAttackResolution(new PlayerAttackResolution(_currentActionId, false, null, false));

        if (_currentAttackOutcome != PlayerAttackOutcome.Hit)
            _targetedComboStrike = 0;
        float recovery = _currentAttackOutcome == PlayerAttackOutcome.Hit ? _iaidoHitRecovery : _iaidoMissRecovery;
        FinishAttackAction(recovery, _currentAttackOutcome);
    }

    /// <summary>일시정지 상태를 전환하고 복귀할 행동 상태를 보존합니다. paused가 false이면 보존한 상태를 복원합니다.</summary>
    public void SetPaused(bool paused)
    {
        if (_state == PlayerActionState.Dead || (paused == (_state == PlayerActionState.Paused)))
            return;

        if (paused)
        {
            _stateBeforePause = _state;
            _requestedMoveInput = Vector2.zero;
            _moveInput = Vector2.zero;
            _motor?.ClearInput();
            SetState(PlayerActionState.Paused, PlayerActionStage.StateChanged);
            return;
        }

        SetState(_stateBeforePause, PlayerActionStage.StateChanged);
    }

    /// <summary>사망을 최우선 상태로 설정하고 이동 입력과 진행 중인 대시를 중단합니다.</summary>
    public void SetDead()
    {
        if (_state == PlayerActionState.Dead)
            return;

        _state = PlayerActionState.Dead;
        _recoveryRemaining = 0f;
        _requestedMoveInput = Vector2.zero;
        _moveInput = Vector2.zero;
        _motor?.ClearInput();
        _motor?.CancelDash();
        Publish(PlayerActionStage.StateChanged);
    }

    /// <summary>새 공격 식별자를 만들고 적중 결과를 초기화한 뒤 지정한 공격 상태로 전환합니다.</summary>
    private void StartAttack(PlayerActionState attackState)
    {
        _counterDashAttack = false;
        _currentActionId = ++_nextActionId;
        _attackImpactResolved = false;
        _attackActionFinished = false;
        _attackDeathConfirmed = false;
        _currentAttackOutcome = PlayerAttackOutcome.None;
        _preferredTargetId = null;
        _targetedDashAttack = false;
        SetState(attackState, PlayerActionStage.StateChanged);
    }

    /// <summary>공격 종류, 대상, 거리, 방향으로 적 시스템에 판정을 요청하고 현재 행동 식별자와 일치하는 결과를 반환합니다.</summary>
    private PlayerAttackResolution ResolveAttack(
        PlayerAttackKind kind,
        string targetId,
        float range,
        Vector3 direction)
    {
        if (_combatService == null)
            return new PlayerAttackResolution(_currentActionId, false, null, false);

        Vector3 origin = _viewCamera != null
            ? _viewCamera.transform.position
            : transform.position + Vector3.up * _attackOriginHeight;

        PlayerAttackRequest request = new PlayerAttackRequest(
            _currentActionId,
            kind,
            targetId,
            origin,
            direction,
            range,
            Time.time);

        PlayerAttackResolution result = _combatService.ResolvePlayerAttack(request);
        if (result.ActionId != _currentActionId)
            return new PlayerAttackResolution(_currentActionId, false, null, false);
        return result;
    }

    /// <summary>적중 또는 빗나감 결과를 저장하고 행동 이벤트를 게시합니다. 발도 적중 시 사망 확정 여부도 전달합니다.</summary>
    private void ApplyAttackResolution(PlayerAttackResolution resolution)
    {
        _attackImpactResolved = true;
        _currentAttackOutcome = resolution.HitConfirmed
            ? PlayerAttackOutcome.Hit
            : PlayerAttackOutcome.Miss;
        _attackDeathConfirmed = resolution.DeathConfirmed;

        if (resolution.HitConfirmed)
        {
            _preferredTargetId = null;
            _targetedDashAttack = false;
            Publish(PlayerActionStage.HitConfirmed, _currentAttackOutcome, resolution.HitTargetId);
            if (resolution.DeathConfirmed)
                Publish(PlayerActionStage.DeathConfirmed, _currentAttackOutcome, resolution.HitTargetId);
        }
        else
        {
            Publish(PlayerActionStage.Missed, _currentAttackOutcome, resolution.HitTargetId);
        }
    }

    /// <summary>공격 동작 종료를 게시하고 중복 없이 결과별 회복을 시작합니다.</summary>
    /// <summary>공격 동작 종료를 게시하고 결과별 회복을 시작한다. 이동 허용 여부는 일반 공격 또는 피격 반응 흐름에서 전달한다.</summary>
    private void FinishAttackAction(float recovery, PlayerAttackOutcome outcome, bool allowMovementDuringRecovery = false)
    {
        if (_attackActionFinished)
            return;

        _attackActionFinished = true;
        _recoveryRemaining = 0f;
        _motor?.EndAttackMovement(allowMovementDuringRecovery);
        Publish(PlayerActionStage.ActionEnded, outcome, _preferredTargetId);
        BeginRecovery(recovery, outcome, allowMovementDuringRecovery);
    }

    /// <summary>회복 시간을 0 이상으로 제한해 저장하고 공격 상태를 회복 또는 자유 상태로 전환합니다.</summary>
    /// <summary>회복 시간을 0 이상으로 저장하고 공격 상태를 회복 또는 자유 상태로 전환한다. 일반 공격 회복은 이동 입력을 계속 전달한다.</summary>
    private void BeginRecovery(float duration, PlayerAttackOutcome outcome, bool allowMovement = false)
    {
        duration = Mathf.Max(0f, duration);
        if (duration <= 0f)
        {
            _recoveryRemaining = 0f;
            _recoveryAllowsMovement = false;
            if (_state == PlayerActionState.NormalAttack
                || _state == PlayerActionState.IaidoSlash
                || _state == PlayerActionState.IaidoDash)
            {
                SetState(PlayerActionState.Free, PlayerActionStage.StateChanged, outcome);
                ResetFinishedAttack();
            }
            return;
        }

        _recoveryRemaining = duration;
        _recoveryAllowsMovement = allowMovement;
        if (allowMovement)
        {
            _moveInput = _requestedMoveInput;
            _motor?.SetMoveInput(_moveInput);
        }
        else
        {
            _moveInput = Vector2.zero;
            _motor?.ClearInput();
        }
        SetState(PlayerActionState.Recovery, PlayerActionStage.RecoveryStarted, outcome);
    }

    /// <summary>회복 타이머를 끝내고 자유 상태로 복귀한 뒤 공격 임시 상태를 초기화합니다.</summary>
    /// <summary>회복 타이머를 끝내고 자유 상태로 복귀한 뒤 회복 중 이동 허용 상태를 초기화한다.</summary>
    private void EndRecovery()
    {
        _recoveryRemaining = 0f;
        _recoveryAllowsMovement = false;
        SetState(PlayerActionState.Free, PlayerActionStage.RecoveryEnded, _currentAttackOutcome);
        ResetFinishedAttack();
        _moveInput = _requestedMoveInput;
        _motor?.SetMoveInput(_moveInput);
    }

    /// <summary>종료된 공격의 적중 처리, 결과, 대상 정보를 초기화합니다.</summary>
    private void ResetFinishedAttack()
    {
        _counterDashAttack = false;
        _attackImpactResolved = false;
        _attackActionFinished = false;
        _attackDeathConfirmed = false;
        _currentAttackOutcome = PlayerAttackOutcome.None;
        _preferredTargetId = null;
        _targetedDashAttack = false;
    }

    /// <summary>대시 종료 이유에 따라 벽 충돌 회복을 적용하거나 베기 상태로 전환합니다.</summary>
    private void HandleDashEnded(PlayerDashEnd result)
    {
        if (_state != PlayerActionState.IaidoDash)
            return;

        _lockedAttackDirection = _motor.DashDirection;

        if (_counterDashAttack)
        {
            if (result.Reason == PlayerDashEndReason.WallBlocked
                || !_combatService.TryGetTarget(_preferredTargetId, out PlayerCombatTarget counterTarget)
                || !counterTarget.IsAlive || counterTarget.HitCollider == null
                || Vector3.Distance(transform.position, counterTarget.HitCollider.ClosestPoint(transform.position)) > _normalAttackRange)
            {
                FinishAttackAction(_iaidoWallBlockedRecovery, PlayerAttackOutcome.WallBlocked);
                return;
            }
            _targetedDashAttack = false;
            SetState(PlayerActionState.NormalAttack, PlayerActionStage.StateChanged);
            Publish(PlayerActionStage.AttackStarted);
            return;
        }

        if (result.Reason == PlayerDashEndReason.WallBlocked)
        {
            if (_targetedDashAttack
                && _combatService != null
                && _combatService.TryGetTarget(_preferredTargetId, out PlayerCombatTarget target)
                && target.IsAlive
                && target.HitCollider != null
                && Vector3.Distance(transform.position, target.HitCollider.ClosestPoint(transform.position)) <= _iaidoAttackRange)
            {
                SetState(PlayerActionState.IaidoSlash, PlayerActionStage.DashArrived, PlayerAttackOutcome.None, _preferredTargetId, result.Position);
                Publish(PlayerActionStage.SlashStarted, PlayerAttackOutcome.None, _preferredTargetId, result.Position);
                NotifyIaidoSlashImpact(_currentActionId);
                return;
            }

            _targetedComboStrike = 0;
            Publish(PlayerActionStage.DashBlocked, PlayerAttackOutcome.WallBlocked, _preferredTargetId, result.Position);
            _attackActionFinished = true;
            _recoveryRemaining = 0f;
            Publish(PlayerActionStage.ActionEnded, PlayerAttackOutcome.WallBlocked, _preferredTargetId, result.Position);
            BeginRecovery(_iaidoWallBlockedRecovery, PlayerAttackOutcome.WallBlocked);
            return;
        }

        SetState(PlayerActionState.IaidoSlash, PlayerActionStage.DashArrived, PlayerAttackOutcome.None, _preferredTargetId, result.Position);
        Publish(PlayerActionStage.SlashStarted, PlayerAttackOutcome.None, _preferredTargetId, result.Position);
        if (_targetedDashAttack)
            NotifyIaidoSlashImpact(_currentActionId);
    }

    /// <summary>
    /// 현재 후보 중 화면 조건·거리·돌진 경로를 만족하는 가장 가까운 적을 선택한다.
    /// targetedAttack이 참이면 중앙 화면에 걸친 공개 콜라이더와 대상 돌진 거리로 검사하며,
    /// _enableTargetedDashAttack가 꺼져 있으면 bulletTimeDash가 참인 불릿 타임 요청에서만 대상 돌진 후보를 찾는다.
    /// 선택 결과를 selected와 perceivedPosition에 담고 성공 여부를 반환한다.
    /// </summary>
    private bool TrySelectIaidoTarget(
        out PlayerCombatTarget selected,
        out Vector3 perceivedPosition,
        bool targetedAttack = false,
        bool bulletTimeDash = false)
    {
        selected = default;
        perceivedPosition = Vector3.zero;

        if (_combatService == null || _motor == null)
            return false;
        // 대상 돌진을 끄면 불릿 타임 밖의 좌클릭은 제자리 일반 공격으로 이어지고 대상 실 표시도 후보를 잃는다.
        if (targetedAttack && !_enableTargetedDashAttack && !bulletTimeDash)
            return false;

        IReadOnlyList<PlayerPerceivedTarget> targets = _perceptionProvider != null
            ? _perceptionProvider.GetCurrentTargets()
            : (_combatService as EnemyCombatService)?.GetCurrentTargets();
        if (targets == null || targets.Count == 0)
            return false;

        Camera camera = _viewCamera != null ? _viewCamera : Camera.main;
        if (camera == null)
            return false;

        SensoryPerceptionMask mask = null;
        Plane[] centerPlanes = null;
        if (targetedAttack)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null)
                return false;

            mask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
            if (mask == null)
                return false;

            // 투영 행렬의 X/Y를 3배로 좁혀 화면 중앙 1/3 영역의 프러스텀을 만든다.
            Matrix4x4 centerProjection =
                Matrix4x4.Scale(new Vector3(3f, 3f, 1f)) * camera.projectionMatrix;
            centerPlanes = GeometryUtility.CalculateFrustumPlanes(
                centerProjection * camera.worldToCameraMatrix);
        }

        bool found = false;
        float bestCenterOffset = float.MaxValue;
        float bestDistance = float.MaxValue;
        string bestId = null;
        float now = Time.time;
        float maxDistance = targetedAttack
            ? _targetedIaidoMaxDistance
            : _motor.IaidoMaxDistance;

        foreach (PlayerPerceivedTarget perceived in targets)
        {
            if (string.IsNullOrEmpty(perceived.TargetId) || now > perceived.ValidUntil)
                continue;

            if (!_combatService.TryGetTarget(perceived.TargetId, out PlayerCombatTarget combatTarget) || !combatTarget.IsAlive)
                continue;

            Vector3 toTarget = combatTarget.WorldPosition - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > maxDistance || !_motor.CanDashToTarget(combatTarget))
                continue;

            Collider hitCollider = combatTarget.HitCollider;
            Vector3 targetPosition = hitCollider != null ? hitCollider.bounds.center : combatTarget.WorldPosition;
            Vector3 screen = camera.WorldToViewportPoint(targetPosition);

            if (targetedAttack)
            {
                if (hitCollider == null || !GeometryUtility.TestPlanesAABB(centerPlanes, hitCollider.bounds) || !IsColliderRevealed(hitCollider, mask))
                    continue;
            }
            else if (screen.z < camera.nearClipPlane
                || screen.z > camera.farClipPlane
                || screen.x < 0f || screen.x > 1f
                || screen.y < 0f || screen.y > 1f)
            {
                continue;
            }

            float centerOffset = Mathf.Abs(screen.x - 0.5f);
            bool isBetter = !found
                || distance < bestDistance - 0.0001f
                || (Mathf.Abs(distance - bestDistance) <= 0.0001f
                    && centerOffset < bestCenterOffset - 0.0001f)
                || (Mathf.Abs(distance - bestDistance) <= 0.0001f
                    && Mathf.Abs(centerOffset - bestCenterOffset) <= 0.0001f
                    && string.CompareOrdinal(perceived.TargetId, bestId) < 0);

            if (!isBetter)
                continue;

            found = true;
            bestCenterOffset = centerOffset;
            bestDistance = distance;
            bestId = perceived.TargetId;
            selected = combatTarget;
            perceivedPosition = combatTarget.WorldPosition;
        }

        return found;
    }

    /// <summary>
    /// collider의 중심과 몸체 표면 표본을 mask의 현재 공개도와 비교한다.
    /// 어느 표본이든 밝혀졌으면 true를 반환하며 상태는 변경하지 않는다.
    /// </summary>
    private static bool IsColliderRevealed(
        Collider collider,
        SensoryPerceptionMask mask)
    {
        Bounds bounds = collider.bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        return mask.GetVisibility(center) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.up * extents.y)) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.down * extents.y)) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.right * extents.x)) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.left * extents.x)) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.forward * extents.z)) > 0f
            || mask.GetVisibility(
                collider.ClosestPoint(center + Vector3.back * extents.z)) > 0f;
    }

    /// <summary>행동 상태, 충전 완료, 웅크리기 여부로 이동 유형을 반환합니다.</summary>
    /// <summary>행동 상태, 충전 완료, 웅크리기 여부로 이동 유형을 반환한다. 일반 공격 회복 중에는 보행을 허용한다.</summary>
    private PlayerLocomotionMode GetLocomotionMode()
    {
        if (_state == PlayerActionState.IaidoDash)
            return PlayerLocomotionMode.IaidoDash;

        bool canWalk = _state == PlayerActionState.Free
            || _state == PlayerActionState.Guarding
            || (_state == PlayerActionState.Charging && _chargeReady)
            || (_state == PlayerActionState.Recovery && _recoveryAllowsMovement);
        if (_motor != null && _motor.IsCrouching)
            return canWalk && _moveInput.sqrMagnitude > 0.0001f
                ? PlayerLocomotionMode.CrouchWalking
                : PlayerLocomotionMode.Crouching;

        if (_state == PlayerActionState.Guarding)
            return PlayerLocomotionMode.GuardWalking;
        if (_state == PlayerActionState.Charging)
            return _chargeReady ? PlayerLocomotionMode.ChargingWalking : PlayerLocomotionMode.ChargingStill;
        if (canWalk && _moveInput.sqrMagnitude > 0.0001f)
            return PlayerLocomotionMode.Walking;
        return PlayerLocomotionMode.Stopped;
    }

    /// <summary>일반 공격, 회복, 일시정지, 사망, 발도 동작, 초기 충전 상태의 이동 제한 여부를 반환합니다.</summary>
    /// <summary>일반 공격, 제한된 회복, 일시정지, 사망, 발도 동작, 초기 충전 상태의 이동 잠금 여부를 반환한다.</summary>
    private bool IsMovementLocked()
    {
        return _state == PlayerActionState.NormalAttack
            || (_state == PlayerActionState.Recovery && !_recoveryAllowsMovement)
            || _state == PlayerActionState.Paused
            || _state == PlayerActionState.Dead
            || _state == PlayerActionState.IaidoDash
            || _state == PlayerActionState.IaidoSlash
            || (_state == PlayerActionState.Charging && !_chargeReady);
    }

    /// <summary>카메라 또는 플레이어 정면에서 수평 시선 방향을 계산해 반환합니다.</summary>
    private Vector3 GetHorizontalViewDirection()
    {
        Transform source = _viewCamera != null ? _viewCamera.transform : transform;
        Vector3 forward = Application.isPlaying ? _motor.ViewForward : source.forward;
        Vector3 direction = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        return direction.sqrMagnitude > 0.0001f ? direction : transform.forward;
    }

    /// <summary>이동·집중·충전 상태가 바뀌면 감지 시스템과 구독자에게 새 감각 문맥을 전달합니다.</summary>
    private void PublishSenseContext(PlayerLocomotionMode mode)
    {
        bool moving = _motor != null && _motor.HorizontalSpeed > 0.05f;
        bool canFocus = _state == PlayerActionState.Free || _state == PlayerActionState.Charging;
        PlayerSenseContext context = new PlayerSenseContext(
            moving,
            canFocus && !moving,
            _state == PlayerActionState.Charging,
            mode);

        if (_hasSenseContext
            && _lastSenseContext.IsMoving == context.IsMoving
            && _lastSenseContext.IsFocusing == context.IsFocusing
            && _lastSenseContext.IsCharging == context.IsCharging
            && _lastSenseContext.LocomotionMode == context.LocomotionMode)
            return;

        _hasSenseContext = true;
        _lastSenseContext = context;
        _perceptionProvider?.SetPlayerSenseContext(context);
        SenseContextChanged?.Invoke(context);
    }

    /// <summary>Inspector에 연결된 MonoBehaviour를 플레이어 서비스 계약으로 변환해 등록합니다.</summary>
    private void BindConfiguredServices()
    {
        ConfigureServices(
            _perceptionProviderSource as IPlayerPerceptionProvider,
            _combatServiceSource as IPlayerCombatService,
            _mapServiceSource as IPlayerMapService);
    }

    /// <summary>PlayerMotor에서 발생한 발걸음 이벤트를 플레이어 시스템 구독자에게 전달합니다.</summary>
    private void ForwardFootstep(PlayerFootstepEvent step)
    {
        FootstepEmitted?.Invoke(step);
    }

    /// <summary>행동 상태를 갱신하고 결과·대상·위치를 포함한 상태 이벤트를 게시합니다.</summary>
    private void SetState(
        PlayerActionState next,
        PlayerActionStage stage,
        PlayerAttackOutcome outcome = PlayerAttackOutcome.None,
        string targetId = null,
        Vector3 worldPosition = default)
    {
        _state = next;
        Publish(stage, outcome, targetId, worldPosition);
        _hasSenseContext = false;
    }

    /// <summary>거부된 행동 요청 이벤트를 게시하고 false를 반환합니다.</summary>
    private bool RejectRequest()
    {
        Publish(PlayerActionStage.RequestRejected);
        return false;
    }

    /// <summary>현재 행동 식별자와 지정한 단계·결과 정보를 PlayerActionEvent로 게시합니다.</summary>
    private void Publish(
        PlayerActionStage stage,
        PlayerAttackOutcome outcome = PlayerAttackOutcome.None,
        string targetId = null,
        Vector3 worldPosition = default)
    {
        ActionEvent?.Invoke(new PlayerActionEvent(
            _currentActionId,
            _state,
            stage,
            outcome,
            targetId,
            worldPosition));
    }
}
