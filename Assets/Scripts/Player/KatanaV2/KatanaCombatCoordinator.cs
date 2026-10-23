using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;


public enum KatanaWeaponState { Sheathed, Drawn, Sheathing }
public enum KatanaStage
{
    NormalStarted, StrikeEnded, ParryAttempted, ParryWindowClosed, ParrySucceeded, ParryFailed, ParryMotionEnded,
    DrawnWaiting, SheatheStarted, SheatheEnded, ChargeStarted, ChargeReady,
    ChargeReleased, DashStarted, SlashStarted, Died, RequestRejected, GuardStarted, GuardEnded, GuardSucceeded,
    ParryMotionStarted
}

public readonly struct KatanaEvent
{
    public KatanaStage Stage { get; }
    public int Strike { get; }
    public int ActionId { get; }
    public bool WasDrawn { get; }
    public string Detail { get; }

    /// <summary>단계, 행동 ID, 이전 발도 여부와 설명을 표현용 이벤트에 저장한다. Strike는 일반 베기의 1·2타를 구분한다.</summary>
    public KatanaEvent(KatanaStage stage, int strike, int actionId, bool wasDrawn, string detail)
    {
        Stage = stage;
        Strike = strike;
        ActionId = actionId;
        WasDrawn = wasDrawn;
        Detail = detail;
    }
}

public readonly struct KatanaParryResult
{
    public DamageContext Incoming { get; }
    public int Strike { get; }

    /// <summary>차단한 원래 피해 문맥과 단일 발도 행동 순번을 저장한다. 반격 피해나 적 상태는 변경하지 않는다.</summary>
    public KatanaParryResult(DamageContext incoming, int strike)
    {
        Incoming = incoming;
        Strike = strike;
    }
}

/// <summary>플레이어의 발도 시작 2타 연계, 유지 가드·정확한 패링, 납도 흐름과 방어 피드백을 연결한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
[DefaultExecutionOrder(-50)]
public sealed class KatanaCombatCoordinator : MonoBehaviour
{
    private const float FALLBACK_IAIDO_HIT_TIME = 0.42f;
    private const float FALLBACK_IAIDO_DURATION = 0.72f;
    private const int MAX_COMBO_STRIKES = 2;

    [Header("References")]
    [SerializeField] private PlayerController _player;

    [Header("Attack / sheathe")]
    [SerializeField, Tooltip("검을 뽑은 채 연계가 끝난 뒤 공격하면 먼저 납도하고 다시 발도로 1타를 시작합니다. 끄면 뽑힌 상태에서 바로 1타를 시작합니다.")]
    private bool _resheatheBeforeOpeningAttack = true;
    [SerializeField, Min(0f)] private float _normalSheatheDelay = 0.8f;

    [Header("발도 시작 2타 연계")]

    [SerializeField, Min(0f), InspectorName("1타 후속 입력 예약 시간 (초)")]
    [Tooltip("연계 프레임 전에 미리 입력을 예약할 수 있는 시간입니다. 연계 프레임이 1타 시작 후 약 0.32초라면 0.2초 설정은 약 0.12초부터 입력을 받습니다.")]
    private float _firstAttackInputBufferWindow = 0.2f;
    [SerializeField, Min(0f)] private float _comboBufferWindow = 0.2f;
    [SerializeField, Min(0f), InspectorName("다음 타 연계 지연 (초)")]
    [Tooltip("각 베기의 타격 구간 종료부터 다음 타 시작까지의 지연입니다. 값이 커지면 연계가 느려집니다. 동작이 끝났다면 회복 종료도 기다립니다.")]
    private float _comboFollowThrough = 0.12f;
    [SerializeField, Min(0f)] private float _comboResetDelay = 0.65f;
    [SerializeField, Min(0.01f), InspectorName("돌진 베기 최소 유지 시간 (초)")]
    [Tooltip("돌진 도착 후 베기 시작부터 다음 클릭 공격까지의 최소 시간입니다. 베기 모션과 회복 종료도 기다립니다.")]
    private float _targetedAttackMinimumDuration = 0.45f;
    // 연계 예약은 QueueAttack이 설정하고 ClearQueuedAttack이 취소한다. 보조 값은 _queuedAttack이 참일 때만 읽는다.
    private bool _queuedAttack;
    private bool _queuedWrapToFirstAttack;
    private bool _queuedFirstAttackVisualLink;
    private bool _queuedTargetedAttack;
    private int _queuedActionId;
    private float _queuedReadyAt;
    private float _nextComboAllowedAt;
    private float _comboExpiresAt;
    private bool _chainTransition;
    private bool _queuedOpeningAttack;
    private bool _sheatheAfterRecovery;

    [Header("방어")]
    [SerializeField, Min(0f), InspectorName("퍼펙트 패링 시작 (초)")]
    [Tooltip("우클릭으로 가드가 열린 시각부터 잽니다. 퍼펙트 패링 구간 밖의 방어는 가드입니다.")]
    private float _perfectParryStart = 0f;
    [SerializeField, Min(0f), InspectorName("퍼펙트 패링 끝 (초)")]
    private float _perfectParryEnd = 0.1f;
    [SerializeField, Min(0f)] private float _blockInputRecovery = 0.18f;
    [SerializeField, Min(0f), InspectorName("일반 적 가드 밀림 거리 (m)")] private float _guardPushDistance = 0.9f;
    [SerializeField, Min(0.01f), InspectorName("일반 적 가드 밀림 시간 (초)")] private float _guardPushDuration = 0.12f;
    private PlayerMotor _motor;
    private PlayerHealth _health;
    private float _defenseReadyUntil;
    private int _guardInputId;

    [Header("패링 반격")]
    [SerializeField, Min(0f), InspectorName("패링 적 경직 시간 (초)")] private float _parryStaggerDuration = 0.9f;
    [SerializeField, Min(0f), InspectorName("반격 후 무적 시간 (초)")]
    [Tooltip("반격 돌진부터 반격 베기 종료까지는 항상 무적이며, 종료 뒤 이 시간만큼 무적을 더 유지합니다. 무적 중 접촉은 피해와 보상이 없습니다.")]
    private float _counterInvulnerabilityAfter = 0.3f;
    private string _counterTargetId;
    private float _counterUntil;
    private bool _counterAttack;
    private float _counterInvulnerableUntil;

    [Header("퍼펙트 패링 모션")]
    [SerializeField, Min(0.01f), InspectorName("퍼펙트 패링 모션 시간 (초)")]
    [Tooltip("퍼펙트 패링 순간 가드를 풀고 흘리기·튕겨내기 모션을 재생하는 시간입니다. 끝나면 바로 납도하며, 도중 공격·회피·새 우클릭으로 끊을 수 있습니다.")]
    private float _perfectParryMotionDuration = 0.35f;
    private int _parryMotionGuardInputId;
    private bool _attackFromParryMotion;

    [Header("Sheathe")]
    [SerializeField, Min(0.01f)] private float _sheatheDuration = 0.3f;

    private PlayerActionDriver _actionDriver;
    private KatanaWeaponVisual _weaponVisual;
    private KatanaWeaponState _weaponState;
    private int _strike;
    private float _clock;
    private PlayerClock _playerClock;
    private float _parryOpenedAt;
    private float _parryUntil;
    private float _parryMotionUntil;
    private float _sheatheUntil;
    private float _waitStarted;
    private float _attackStartedAt;
    private float _attackParryHitTime;
    private bool _parryOpen;
    private bool _parryMotion;
    private bool _waiting;
    private bool _waitAfterRecovery;
    private bool _normalRequested;
    private bool _suppressSheatheOnGuardEnd;
    // 제자리 공격 요청은 예약된 공격이 시작되거나 버려질 때까지 대상 돌진을 막는다.
    private bool _inPlaceAttackRequested;
    // 불릿 타임 공격 요청은 예약된 공격이 시작되거나 버려질 때까지 대상 돌진을 허용한다.
    private bool _bulletTimeDashRequested;
    private PlayerInputRejectReason _lastRejectReason;

    private bool _attackWasDrawn;
    private readonly PlayerCombatEvents _events = new PlayerCombatEvents();

    public event Action<KatanaEvent> EventRaised;
    public event Action<KatanaParryResult> ParrySucceeded;
    public event Action<KatanaDefenseResult> DefenseSucceeded;
    public PlayerCombatEvents Events => _events;
    public int GuardInputId => _guardInputId;
    public float GuardHeldTime => _player.IsGuarding ? Mathf.Max(0f, _playerClock.Now - _parryOpenedAt) : 0f;
    public float GuardTimingWindow => _perfectParryEnd;
    public PlayerController Player => _player;
    public KatanaWeaponState WeaponState => _weaponState;
    public int Strike => _strike;
    public bool HasBufferedAttack => _queuedAttack || _queuedOpeningAttack;
    public PlayerInputRejectReason LastRejectReason => _lastRejectReason;
    public bool IsFirstAttackFollowupBuffered => _queuedAttack && _queuedFirstAttackVisualLink
        && !_queuedTargetedAttack && IsOpeningAttack && _weaponVisual != null && _weaponVisual.HasFirstAttackFollowup;
    public bool IsFirstAttackFollowupTransition => _chainTransition && _queuedFirstAttackVisualLink && _strike == 2
        && _weaponVisual != null && _weaponVisual.HasFirstAttackFollowup;
    public bool IsOpeningAttack => !_counterAttack && _strike == 1 && _player.CurrentState == PlayerActionState.NormalAttack;
    public bool IsCounterAttack => _counterAttack && (_player.CurrentState == PlayerActionState.NormalAttack
        || _player.CurrentState == PlayerActionState.IaidoDash);
    public bool IsCounterInvulnerable => IsCounterAttack || _playerClock.Now < _counterInvulnerableUntil;
    public float CounterRemaining => Mathf.Max(0f, _counterUntil - _playerClock.Now);
    public bool CanCounterAttack => isActiveAndEnabled && !_player.IsPaused && Time.timeScale > 0f
        && CounterRemaining > 0f && _playerClock.Now >= _defenseReadyUntil
        && _weaponState != KatanaWeaponState.Sheathing
        && (_player.CurrentState == PlayerActionState.Guarding || _player.CurrentState == PlayerActionState.Free);
    public bool IsParryWindowOpen => _parryOpen && _playerClock.Now < _parryUntil
        && _player.IsGuarding;

    /// <summary>
    /// 지금 가드에 공격이 닿으면 받을 결과를 가드가 열린 뒤 지난 시간으로 반환한다.
    /// 퍼펙트 패링 구간이면 PerfectParry, 그 외와 패링 창이 닫힌 경우는 Guard다.
    /// </summary>
    public DefenseOutcome GuardContactOutcome
    {
        get
        {
            if (!IsParryWindowOpen) return DefenseOutcome.Guard;
            float elapsed = _playerClock.Now - _parryOpenedAt;
            if (elapsed >= _perfectParryStart && elapsed < _perfectParryEnd) return DefenseOutcome.PerfectParry;
            return DefenseOutcome.Guard;
        }
    }
    public bool IsParryMotion => _parryMotion;
    public float PerfectParryMotionDuration => _perfectParryMotionDuration;
    public float SheatheDuration => _sheatheDuration;
    public float ParryRemaining => IsParryWindowOpen ? Mathf.Max(0f, _parryUntil - _playerClock.Now) : 0f;
    public bool CanStartAction => isActiveAndEnabled && _player != null
        && Time.timeScale > 0f && _player.CurrentState == PlayerActionState.Free
        && _weaponState != KatanaWeaponState.Sheathing && !_parryOpen && !_parryMotion && _playerClock.Now >= _defenseReadyUntil;

    void Awake()
    {
        if (_player == null)
            _player = GetComponent<PlayerController>();
        _actionDriver = GetComponent<PlayerActionDriver>();
        _weaponVisual = GetComponent<KatanaWeaponVisual>();
        _motor = GetComponent<PlayerMotor>();
        _health = GetComponent<PlayerHealth>();
        _playerClock = GetComponent<PlayerClock>();
    }

    void OnEnable()
    {
        if (_player == null)
            _player = GetComponent<PlayerController>();
        _player.ActionEvent += HandlePlayerAction;
        _health.DamageReceived += HandleDamage;
        _motor.DodgeStarted += HandleDodgeStarted;
    }

    void OnDisable()
    {
        if (_player != null)
            _player.ActionEvent -= HandlePlayerAction;
        _health.DamageReceived -= HandleDamage;
        _motor.DodgeStarted -= HandleDodgeStarted;
        _parryOpen = false;
        _parryMotion = false;
        ResetChain();
    }

    /// <summary>패링·공격·납도 시계를 진행하고 예약된 행동이 가능한 상태가 되면 실행한다.</summary>
    void Update()
    {
        if (_player.IsPaused || _player.CurrentState == PlayerActionState.Dead)
            return;

        if (_inPlaceAttackRequested && !HasBufferedAttack) _inPlaceAttackRequested = false;
        if (_bulletTimeDashRequested && !HasBufferedAttack) _bulletTimeDashRequested = false;
        _clock += _playerClock.DeltaTime;
        if (_queuedAttack && !IsFirstAttackFollowupBuffered && _queuedActionId == _player.CurrentActionId && _clock >= _queuedReadyAt
            && (_player.CurrentState == PlayerActionState.NormalAttack || CanStartAction))
            TryStartQueuedAttack();
        if (_player.CurrentState == PlayerActionState.Free && _comboExpiresAt > 0f
            && _clock >= _comboExpiresAt && !_queuedAttack)
            BeginSheathe();
        if (_parryOpen && !IsParryWindowOpen)
            CloseAttackParryWindow();

        if (_parryMotion && _clock >= _parryMotionUntil)
        {
            _parryMotion = false;
            Publish(KatanaStage.ParryMotionEnded);
            // 퍼펙트 패링 모션이 끊기지 않고 끝나면 발도 대기 없이 바로 납도한다.
            if (_player.CurrentState == PlayerActionState.Free)
                BeginSheathe();
        }

        if (_weaponState == KatanaWeaponState.Sheathing && _clock >= _sheatheUntil)
        {
            _weaponState = KatanaWeaponState.Sheathed;
            Publish(KatanaStage.SheatheEnded);
        }
        if (_queuedOpeningAttack && CanStartAction)
        {
            _queuedOpeningAttack = false;
            RequestAttack();
        }

        if (!_waiting || _player.CurrentState != PlayerActionState.Free || _parryMotion || _queuedAttack || CounterRemaining > 0f)
            return;
        if (!_parryOpen && _clock - _waitStarted >= _normalSheatheDelay)
            BeginSheathe();
    }

    /// <summary>value를 기존 이동 명령으로 전달한다. 충전·회복·사망 이동 규칙은 기존 Controller가 적용한다.</summary>
    public void SetMovementInput(Vector2 value) => _player.SetMovementInput(value);

    /// <summary>delta를 기존 시점 명령으로 전달한다. 사망과 일시정지 제한을 유지한다.</summary>
    public void AddLookInput(Vector2 delta) => _player.AddLookInput(delta);

    /// <summary>
    /// 근처 적이 있어도 대상 돌진 없이 제자리 일반 공격만 시작하거나 예약하고 입력 수락 여부를 반환한다.
    /// 예약된 경우 _inPlaceAttackRequested를 유지해 예약 공격이 시작되거나 버려질 때까지 대상 돌진을 막는다.
    /// </summary>
    public bool RequestInPlaceAttack()
    {
        _inPlaceAttackRequested = true;
        bool accepted = RequestAttack();
        if (!HasBufferedAttack) _inPlaceAttackRequested = false;
        return accepted;
    }

    /// <summary>자유 상태에서 일섬용 1타를 예약 없이 시작한다. 입력 없이 타수·연계 상태를 초기화하고 실제 행동 시작 여부를 반환한다.</summary>
    public bool RequestSwordWaveAttack()
    {
        if (!CanStartAction || _player.IsPaused) return false;
        CancelPendingAttacks();
        _counterAttack = false;
        _counterUntil = 0f;
        _attackWasDrawn = _weaponState != KatanaWeaponState.Sheathed;
        _strike = 1;
        _waiting = false;
        _waitAfterRecovery = false;
        _normalRequested = true;
        bool accepted = _player.RequestNormalAttack();
        _normalRequested = false;
        return accepted;
    }

    /// <summary>
    /// Shift 일섬·Q 거합 같은 수동 스킬을 시작하기 전에 진행 중인 공격·공격 후 회복과 가드·퍼펙트 패링 모션을 끝내고, 이어지는 납도를 기다리지 않고 칼집에 넣은 상태로 확정한다.
    /// 끊는 공격의 미판정 피해는 PlayerController가 먼저 확정하며, 정리한 뒤 행동을 바로 시작할 수 있는지(CanStartAction)를 반환한다.
    /// </summary>
    public bool PrepareManualSkill()
    {
        _player.CancelActionForManualSkill();
        if (_player.IsGuarding) EndDefense();
        CancelParryMotion();
        if (_player.CurrentState == PlayerActionState.Free && _weaponState == KatanaWeaponState.Sheathing)
        {
            _weaponState = KatanaWeaponState.Sheathed;
            Publish(KatanaStage.SheatheEnded);
        }
        return CanStartAction;
    }

    /// <summary>
    /// 불릿 타임 중 입력으로 대상 돌진이 꺼져 있어도 실이 연결된 적에게 대상 돌진을 허용한 채 공격을 시작하거나 예약하고 입력 수락 여부를 반환한다.
    /// 납도 중이면 납도를 끊고 바로 돌진한다. 예약된 경우 _bulletTimeDashRequested를 유지해 예약 공격이 시작될 때 대상 돌진을 시도하며, 이후 입력에는 허용이 이어지지 않는다.
    /// </summary>
    public bool RequestBulletTimeAttack()
    {
        _bulletTimeDashRequested = true;
        bool accepted = RequestAttack();
        if (!HasBufferedAttack) _bulletTimeDashRequested = false;
        return accepted;
    }

    /// <summary>
    /// 반격, 대상 돌진, 일반 1·2타 연계 중 현재 상태에 맞는 공격을 시작하거나 다음 클릭 하나를 예약한다.
    /// 가드 중 입력은 가드를 끝내고, 퍼펙트 패링 모션 중 입력은 모션을 끊고 발도 상태로 공격에 연결하며 입력 수락 여부를 반환한다. 제자리 공격 요청 중에는 대상 돌진을 건너뛴다.
    /// _resheatheBeforeOpeningAttack가 꺼져 있으면 연계가 끝난 뒤에도 납도 없이 뽑힌 검으로 1타를 시작한다.
    /// </summary>
    public bool RequestAttack()
    {
        if (!isActiveAndEnabled || _player.IsPaused || Time.timeScale <= 0f)
            return Reject(PlayerInputRejectReason.Unavailable, "Attack: input unavailable");
        if (CanCounterAttack) return RequestCounterAttack();
        if (_parryMotion)
        {
            // 퍼펙트 패링 모션 중 공격은 모션을 끊고 납도 없이 발도 상태에서 바로 시작한다. 시작하지 못하면 모션을 이어간다.
            _parryMotion = false;
            _attackFromParryMotion = true;
            bool startedFromParryMotion = RequestAttack();
            _attackFromParryMotion = false;
            if (!startedFromParryMotion) _parryMotion = true;
            return startedFromParryMotion;
        }

        bool attackFromGuard = _player.IsGuarding;
        if (attackFromGuard)
        {
            _suppressSheatheOnGuardEnd = true;
            EndDefense();
            _suppressSheatheOnGuardEnd = false;
        }

        if (_queuedOpeningAttack) return Reject(PlayerInputRejectReason.AlreadyBuffered, "Attack: opening already buffered");
        if (_player.CurrentState == PlayerActionState.Free && _weaponState == KatanaWeaponState.Sheathing)
        {
            // 불릿 타임 발도는 납도 완료를 기다리지 않고 납도를 끊어 바로 돌진한다. 지금 대상이 없으면 기존처럼 예약한다.
            if (_bulletTimeDashRequested && _playerClock.Now >= _defenseReadyUntil
                && _player.RequestTargetedIaidoAttack(1, true))
                return true;
            _queuedOpeningAttack = true;
            return true;
        }

        bool targetedAttackActive = _player.TargetedComboStrike > 0
            && (_player.CurrentState == PlayerActionState.IaidoDash
                || _player.CurrentState == PlayerActionState.IaidoSlash
                || _player.CurrentState == PlayerActionState.Recovery);
        if (targetedAttackActive)
        {
            if (_queuedAttack || _strike >= MAX_COMBO_STRIKES)
                return Reject(_queuedAttack ? PlayerInputRejectReason.AlreadyBuffered : PlayerInputRejectReason.ChainNotOpen,
                    "Attack: targeted chain input unavailable");
            QueueAttack(true, false, false, _nextComboAllowedAt);
            return true;
        }

        bool hasNormalCombo = _weaponVisual != null && _weaponVisual.HasFirstAttackFollowup;
        if (_player.CurrentState == PlayerActionState.NormalAttack && _strike > 0 && !_counterAttack
            && (_strike < MAX_COMBO_STRIKES || _strike == MAX_COMBO_STRIKES && hasNormalCombo))
        {
            if (_queuedAttack)
                return Reject(PlayerInputRejectReason.AlreadyBuffered, "Attack: chain input already buffered");

            bool firstAttackFollowup = _strike == 1 && hasNormalCombo;
            float attackElapsed = GetAttackElapsed();
            float queuedReadyAt = _nextComboAllowedAt;
            bool visualLink = false;
            if (firstAttackFollowup)
            {
                GetFirstAttackBufferWindow(out float bufferStart, out float bufferEnd);
                if (attackElapsed < bufferStart)
                    return Reject(PlayerInputRejectReason.ChainNotOpen, "Attack: first link not open");

                visualLink = attackElapsed <= bufferEnd;
                if (!visualLink)
                {
                    _actionDriver.GetPresentationTiming(PlayerAttackKind.Normal, out _, out float duration);
                    queuedReadyAt = Mathf.Max(queuedReadyAt, _attackStartedAt + duration);
                }
            }
            else if (_strike < MAX_COMBO_STRIKES && attackElapsed < GetBufferStart())
            {
                return Reject(PlayerInputRejectReason.ChainNotOpen, "Attack: chain input unavailable");
            }

            QueueAttack(false, _strike == MAX_COMBO_STRIKES, visualLink, queuedReadyAt);
            if (!visualLink && _clock >= _queuedReadyAt) TryStartQueuedAttack();
            return true;
        }

        if (_player.CurrentState == PlayerActionState.Recovery && _strike > 0 && !_counterAttack
            && hasNormalCombo && _weaponState == KatanaWeaponState.Drawn)
        {
            if (_queuedAttack)
                return Reject(PlayerInputRejectReason.AlreadyBuffered, "Attack: chain input already buffered");
            QueueAttack(false, _strike >= MAX_COMBO_STRIKES, false, Mathf.Max(_clock, _nextComboAllowedAt));
            return true;
        }

        if (_queuedAttack || !CanStartAction)
            return Reject(_queuedAttack ? PlayerInputRejectReason.AlreadyBuffered : PlayerInputRejectReason.ActionBusy,
                "Attack: current action blocks input");
        int strike = _strike > 0 && _strike < MAX_COMBO_STRIKES && _clock < _comboExpiresAt ? _strike + 1 : 1;
        if (strike > 1 && _clock < _nextComboAllowedAt)
        {
            QueueAttack(true, false, false, _nextComboAllowedAt);
            return true;
        }
        if (!_inPlaceAttackRequested && _player.RequestTargetedIaidoAttack(strike, _bulletTimeDashRequested))
            return true;
        if (_resheatheBeforeOpeningAttack && strike == 1 && _weaponState == KatanaWeaponState.Drawn
            && !attackFromGuard && !_attackFromParryMotion && _clock >= _comboExpiresAt)
        {
            BeginSheathe();
            _queuedOpeningAttack = true;
            return true;
        }

        _attackWasDrawn = _weaponState != KatanaWeaponState.Sheathed;
        _strike = strike;
        _waiting = false;
        ClearQueuedAttack();
        _normalRequested = true;
        bool accepted = _player.RequestNormalAttack();
        _normalRequested = false;
        return accepted || Refuse(PlayerInputRejectReason.ActionBusy);
    }

    /// <summary>패링 기회 중 가드를 해제하고 저장한 공격자에게 대시 후 단발 반격을 시작한다. 대상이 없는 패링은 제자리 반격을 사용하며 수락 여부를 반환한다.</summary>
    private bool RequestCounterAttack()
    {
        if (_player.IsGuarding) EndDefense();
        if (!CanStartAction) return Reject(PlayerInputRejectReason.ActionBusy, "반격: 현재 행동으로 입력이 제한됩니다.");
        ClearQueuedAttack();
        _comboExpiresAt = 0f;
        _waiting = false;
        _waitAfterRecovery = false;
        _attackWasDrawn = true;
        _strike = 2;
        _counterAttack = true;
        _normalRequested = true;
        bool accepted = string.IsNullOrEmpty(_counterTargetId)
            ? _player.RequestNormalAttack() : _player.RequestCounterDashAttack(_counterTargetId);
        _normalRequested = false;
        if (accepted)
        {
            _counterUntil = 0f;
            return true;
        }
        _counterAttack = false;
        return Refuse(PlayerInputRejectReason.TargetUnavailable);
    }

    /// <summary>현재 일반 공격의 타격 종료와 최소 후속 동작 시간으로 다음 베기를 시작할 시각을 반환한다.</summary>
    private float GetChainStart()
    {
        _actionDriver.GetStrikeWindow(PlayerAttackKind.Normal, out _, out float end);
        return end + _comboFollowThrough;
    }

    /// <summary>연계 시각과 동작 길이 및 예약 폭으로 입력 수락 시작 시간을 반환한다. 긴 지연에서도 첫 동작 종료 전 예약을 허용한다.</summary>
    private float GetBufferStart()
    {
        _actionDriver.GetPresentationTiming(PlayerAttackKind.Normal, out _, out float duration);
        return Mathf.Max(0f, Mathf.Min(GetChainStart(), duration) - _comboBufferWindow);
    }

    /// <summary>1타의 연결 시각과 입력 예약 폭으로 후속 공격 입력을 처음 받을 시각을 반환한다.</summary>
    /// <summary>1타 연결 시각과 예약 폭으로 후속 입력을 받을 시작·종료 시각을 반환한다.</summary>
    private void GetFirstAttackBufferWindow(out float start, out float end)
    {
        if (_actionDriver != null)
            _actionDriver.GetPresentationTiming(PlayerAttackKind.Normal, out end, out _);
        else
            end = FALLBACK_IAIDO_HIT_TIME;

        start = Mathf.Max(0f, end - _firstAttackInputBufferWindow);
    }


    /// <summary>1타의 표시 자세가 연계 프레임에 도달하면 예약된 공격 ID의 마지막 피해 검사를 마치고 2타를 시작한다. 시작 성공 여부를 반환한다.</summary>
    public bool TryStartFirstAttackFollowup()
    {
        if (!IsFirstAttackFollowupBuffered || _player.IsPaused || _queuedActionId != _player.CurrentActionId)
            return false;
        _player.NotifyNormalAttackSweep(_queuedActionId, true);
        TryStartQueuedAttack();
        return !_queuedAttack;
    }
    /// <summary>
    /// 예약된 입력이 실행 가능해지면 공개된 중앙 대상 돌진을 우선 시도한다.
    /// 대상이 없으면 기존 일반 연계를 실행하고, 성공 시 예약 상태를 비운다.
    /// </summary>
    private void TryStartQueuedAttack()
    {
        if (_queuedTargetedAttack)
        {
            ClearQueuedAttack();
            RequestAttack();
            return;
        }

        bool wrapToFirstAttack = _queuedWrapToFirstAttack;
        int nextStrike = wrapToFirstAttack ? 1 : Mathf.Min(_strike + 1, MAX_COMBO_STRIKES);

        if (!_inPlaceAttackRequested)
        {
            bool targetedAccepted = false;

            if (_player.CurrentState == PlayerActionState.NormalAttack)
            {
                _chainTransition = true;
                targetedAccepted = _player.TryChainTargetedIaidoAttack(_queuedActionId, nextStrike, _bulletTimeDashRequested);
                _chainTransition = false;
            }
            else if (_player.CurrentState == PlayerActionState.Free)
            {
                targetedAccepted = _player.RequestTargetedIaidoAttack(nextStrike, _bulletTimeDashRequested);
            }

            if (targetedAccepted)
            {
                ClearQueuedAttack();
                _waiting = false;
                _waitAfterRecovery = false;
                return;
            }

            // 돌진 시작 과정에서 행동이 이미 전환됐다면 일반 공격을 중복 시작하지 않는다.
            if (!_queuedAttack)
                return;
        }

        int previousStrike = _strike;
        if (wrapToFirstAttack)
            _strike = 0;

        _attackWasDrawn = true;
        _normalRequested = true;
        _chainTransition = true;

        bool accepted;
        if (_player.CurrentState == PlayerActionState.Free)
            accepted = _player.RequestNormalAttack();
        else
            accepted = _player.TryChainNormalAttack(_queuedActionId);

        _chainTransition = false;
        _normalRequested = false;

        if (accepted)
        {
            ClearQueuedAttack();
            _waiting = false;
            _waitAfterRecovery = false;
        }
        else if (wrapToFirstAttack)
        {
            _strike = previousStrike;
        }
    }

    /// <summary>
    /// 다음 공격 입력 하나를 현재 행동 ID에 예약한다.
    /// reevaluate이면 readyAt에 RequestAttack을 다시 실행하고, 아니면 wrapToFirstAttack·firstAttackVisualLink로 다음 타 연결 방식을 저장한다.
    /// </summary>
    private void QueueAttack(bool reevaluate, bool wrapToFirstAttack, bool firstAttackVisualLink, float readyAt)
    {
        _queuedAttack = true;
        _queuedTargetedAttack = reevaluate;
        _queuedWrapToFirstAttack = wrapToFirstAttack;
        _queuedFirstAttackVisualLink = firstAttackVisualLink;
        _queuedActionId = _player.CurrentActionId;
        _queuedReadyAt = readyAt;
    }

    /// <summary>예약된 다음 공격 입력과 연결 방식을 취소한다. 발도 대기 중 예약(_queuedOpeningAttack)과 타수는 변경하지 않는다.</summary>
    private void ClearQueuedAttack()
    {
        _queuedAttack = false;
        _queuedTargetedAttack = false;
        _queuedWrapToFirstAttack = false;
        _queuedFirstAttackVisualLink = false;
    }

    /// <summary>context의 실제 피격 알림을 받으면 연계 예약과 다음 타수 유지 시간을 취소한다. 진행 중인 공격은 Controller의 피격 처리를 따른다.</summary>
    private void HandleDamage(DamageContext context)
    {
        ClearQueuedAttack();
        _queuedOpeningAttack = false;
        _comboExpiresAt = 0f;
        _counterUntil = 0f;
    }

    /// <summary>기존 일반공격 호출자를 발도 시작 2타 연계 입력 흐름으로 연결한다. 수락 여부를 반환한다.</summary>
    public bool RequestNormalAttack() => RequestAttack();

    /// <summary>기존 입력 호출자를 발도 시작 2타 베기 흐름으로 연결한다. 방어 패링 창은 열지 않으며 수락 여부를 반환한다.</summary>
    public bool RequestNormalAttackWithParry() => RequestAttack();

    /// <summary>행동이 허용될 때 기존 충전을 시작한다. 기존 충전 시간과 이동 제한을 유지하며 수락 여부를 반환한다.</summary>
    public bool BeginCharge()
    {
        return CanStartAction ? _player.BeginCharge() : Reject(PlayerInputRejectReason.ActionBusy, "Charge: current action blocks input");
    }

    /// <summary>기존 충전을 해제한다. 완료·조기 해제의 기존 회복 시간은 Controller가 적용한다.</summary>
    public void EndCharge() => _player.EndCharge();

    /// <summary>충전 완료 상태에서 발도를 요청한다. 기존 충전 조건과 공격 판정을 유지한다.</summary>
    public bool RequestIaidoAttack()
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f || _parryOpen || _parryMotion || !_player.IsChargingReady || _player.IsPaused)
            return Reject(PlayerInputRejectReason.ActionBusy, "Iaido: charge not ready");
        return _player.RequestIaidoAttack();
    }

    /// <summary>incoming과 eligible을 현재 가드의 패링 창 안에서만 검사한다. 일반·퍼펙트 패링의 방어 성공 여부를 반환한다.</summary>
    public bool TryParryIncoming(DamageContext incoming, bool eligible)
    {
        return IsParryWindowOpen && TryDefendIncoming(incoming, eligible);
    }

    /// <summary>상위 시스템의 paused를 기존 Controller에 전달한다. 카타나 타이머도 Update에서 함께 정지한다.</summary>
    public void SetPaused(bool paused) => _player.SetPaused(paused);

    /// <summary>입력 소유권 상실 시 연계 예약·타수 유지 시간·반격 기회를 지우고 패링 창을 닫는다. 현재 공격은 강제 종료하지 않는다.</summary>
    public void CancelParryAttempt()
    {
        ClearQueuedAttack();
        _queuedOpeningAttack = false;
        _comboExpiresAt = 0f;
        _counterUntil = 0f;
        if (_parryOpen)
            FailParry("Input focus lost");
    }

    /// <summary>발도 동작의 적중 시각과 종료 시각을 읽고 드라이버가 없으면 기본값을 사용한다.</summary>
    private void GetIaidoTiming(out float hitTime, out float duration)
    {
        if (_actionDriver != null)
        {
            _actionDriver.GetPresentationTiming(PlayerAttackKind.IaidoFinisher, out hitTime, out duration);
            return;
        }

        hitTime = FALLBACK_IAIDO_HIT_TIME;
        duration = FALLBACK_IAIDO_DURATION;
    }

    /// <summary>현재 공격의 경과 시간을 읽고 테스트 드라이버가 없으면 Coordinator 시계로 계산한다.</summary>
    private float GetAttackElapsed()
    {
        return _actionDriver != null
            ? _actionDriver.GetPresentationElapsed(_player.CurrentActionId)
            : Mathf.Max(0f, _clock - _attackStartedAt);
    }

    /// <summary>
    /// 현재 게임 시간을 _parryOpenedAt에 저장하고 퍼펙트 패링 구간이 끝나는 시각까지 패링 창을 연다.
    /// 시각화 이벤트를 발행한다.
    /// </summary>
    private void OpenDefenseWindow()
    {
        _parryOpenedAt = _playerClock.Now;
        _parryUntil = _parryOpenedAt + _perfectParryEnd;
        _parryOpen = true;
        Publish(KatanaStage.ParryAttempted, "Guard started: timed parry window");
    }

    /// <summary>패링 시간이 지나면 정확한 패링 창만 닫는다. 우클릭 유지 가드는 계속 허용한다.</summary>
    private void CloseAttackParryWindow()
    {
        if (!_parryOpen)
            return;

        _parryOpen = false;
        Publish(KatanaStage.ParryWindowClosed, "Timed parry window ended; held guard remains");
    }

    /// <summary>공격 회복 후 클립이 Idle 복귀까지 완료했으면 납도 상태를 확정한다. 그 외에는 타수와 기존 납도 대기로 표현 이벤트를 발행한다.</summary>
    private void BeginWaiting()
    {
        if (_weaponVisual != null && _weaponVisual.HasFinishedAttackAtIdle
            && _comboExpiresAt <= _clock && !_queuedAttack)
        {
            bool queuedOpeningAttack = _queuedOpeningAttack;
            ResetChain();
            _queuedOpeningAttack = queuedOpeningAttack;
            _weaponState = KatanaWeaponState.Sheathed;
            Publish(KatanaStage.SheatheEnded);
            return;
        }
        if (_sheatheAfterRecovery)
        {
            BeginSheathe();
            return;
        }
        _waiting = true;
        _waitStarted = _clock;
        Publish(KatanaStage.DrawnWaiting);
    }

    /// <summary>발도된 검의 납도 표현과 공격 대기를 초기화한다. 클립이 Idle 복귀를 완료했다면 납도 재생 없이 상태만 확정한다.</summary>
    private void BeginSheathe()
    {
        if (_weaponVisual != null && _weaponVisual.HasFinishedAttackAtIdle)
        {
            ResetChain();
            _weaponState = KatanaWeaponState.Sheathed;
            Publish(KatanaStage.SheatheEnded);
            return;
        }
        _weaponState = KatanaWeaponState.Sheathing;
        _sheatheUntil = _clock + _sheatheDuration;
        Publish(KatanaStage.SheatheStarted);
        ResetChain();
    }

    /// <summary>마지막 타수·연계·반격·납도 대기 상태를 비운다. 기존 Controller의 행동 상태는 변경하지 않는다.</summary>
    private void ResetChain()
    {
        _counterTargetId = null;
        _strike = 0;
        ClearQueuedAttack();
        _counterUntil = 0f;
        _counterAttack = false;
        _queuedOpeningAttack = false;
        _comboExpiresAt = 0f;
        _waiting = false;
        _waitAfterRecovery = false;
        _sheatheAfterRecovery = false;
    }

    /// <summary>공격 중 쳐내기 창을 한 번 닫고 입력 취소 또는 판정 실패를 이벤트로 전달한다.</summary>
    private void FailParry(string reason)
    {
        if (!_parryOpen)
            return;

        _parryOpen = false;
        Publish(KatanaStage.ParryFailed, reason);
    }

    /// <summary>detail로 입력 거부를 표현 사건으로 알리고 reason을 LastRejectReason에 저장한 뒤 false를 반환한다. 입력 큐는 생성하지 않는다.</summary>
    private bool Reject(PlayerInputRejectReason reason, string detail)
    {
        Publish(KatanaStage.RequestRejected, detail);
        return Refuse(reason);
    }

    /// <summary>표현 사건 없이 reason을 LastRejectReason에 저장하고 false를 반환한다. 입력 큐는 생성하지 않는다.</summary>
    private bool Refuse(PlayerInputRejectReason reason)
    {
        _lastRejectReason = reason;
        return false;
    }

    /// <summary>
    /// 회피가 시작되면 예약된 다음 공격·납도 뒤 공격과 반격 기회를 취소하고 퍼펙트 패링 모션을 끊어 납도한다.
    /// 진행 중인 공격은 회피 요청 시 PlayerController가 먼저 끝낸다.
    /// </summary>
    private void HandleDodgeStarted()
    {
        CancelPendingAttacks();
        CancelParryMotion();
    }

    /// <summary>
    /// 강화 공격이 시작되면 가드와 퍼펙트 패링 모션을 끝내고 예약된 다음 공격·납도 뒤 공격과 반격 기회를 취소한다.
    /// 진행 중인 공격은 PlayerController가 따로 끝낸다.
    /// </summary>
    public void CancelForEmpowerStrike()
    {
        if (_player.IsGuarding) EndDefense();
        CancelParryMotion();
        CancelPendingAttacks();
    }

    /// <summary>
    /// 퍼펙트 패링 모션 중이면 _parryMotion을 끄고 가드 해제와 같은 납도를 시작한다.
    /// 모션 중이 아니면 아무것도 바꾸지 않는다.
    /// </summary>
    private void CancelParryMotion()
    {
        if (!_parryMotion) return;
        _parryMotion = false;
        BeginSheathe();
    }

    /// <summary>
    /// 퍼펙트 패링 직후 납도 없이 가드를 끝내고 _perfectParryMotionDuration 동안 퍼펙트 패링 모션을 시작한다.
    /// 패링한 가드의 _guardInputId를 저장해 같은 우클릭을 계속 누른 가드 재진입을 모션이 끝날 때까지 막는다.
    /// </summary>
    private void BeginPerfectParryMotion()
    {
        _suppressSheatheOnGuardEnd = true;
        EndDefense();
        _suppressSheatheOnGuardEnd = false;
        _parryMotion = true;
        _parryMotionUntil = _clock + _perfectParryMotionDuration;
        _parryMotionGuardInputId = _guardInputId;
        Publish(KatanaStage.ParryMotionStarted);
    }

    /// <summary>_counterUntil과 예약된 공격·납도 뒤 공격을 비운다.</summary>
    private void CancelPendingAttacks()
    {
        _counterUntil = 0f;
        ClearQueuedAttack();
        _queuedOpeningAttack = false;
    }

    /// <summary>e의 일반 공격·방어·회복 이벤트로 2타 연계와 납도 상태를 변경한다. 중단 상태에서는 예약을 취소한다.</summary>
    private void HandlePlayerAction(PlayerActionEvent e)
    {
        if (e.State == PlayerActionState.Paused)
        {
            ClearQueuedAttack();
            _queuedOpeningAttack = false;
            _comboExpiresAt = 0f;
            _counterUntil = 0f;
            return;
        }
        if (e.State == PlayerActionState.Dead)
        {
            _parryOpen = false;
            _parryMotion = false;
            ResetChain();
            Publish(KatanaStage.Died);
            return;
        }

        switch (e.Stage)
        {
            case PlayerActionStage.AttackStarted:
                if (e.State != PlayerActionState.NormalAttack) break;
                if (!_normalRequested && !_counterAttack)
                {
                    _attackWasDrawn = _weaponState != KatanaWeaponState.Sheathed;
                    ResetChain();
                    _strike = 1;
                }
                if (_chainTransition) _strike++;
                if (!_counterAttack) _nextComboAllowedAt = _clock + GetChainStart();
                _attackStartedAt = _clock;
                _weaponState = KatanaWeaponState.Drawn;
                Publish(KatanaStage.NormalStarted, _counterAttack ? "패링 반격" : "Normal chain " + _strike, _attackWasDrawn);
                _parryOpen = false;
                break;
            case PlayerActionStage.GuardStarted:
                ClearQueuedAttack();
                _queuedOpeningAttack = false;
                _comboExpiresAt = 0f;
                _sheatheAfterRecovery = false;
                _counterUntil = 0f;
                _weaponState = KatanaWeaponState.Drawn;
                _waiting = false;
                Publish(KatanaStage.GuardStarted);
                break;
            case PlayerActionStage.GuardEnded:
                _parryOpen = false;
                Publish(KatanaStage.GuardEnded);
                if (_suppressSheatheOnGuardEnd)
                {
                    _weaponState = KatanaWeaponState.Drawn;
                    _waiting = false;
                    return;
                }
                if (CounterRemaining > 0f)
                {
                    BeginWaiting();
                    // 반격 기회가 끝나면 추가 대기 없이 바로 납도한다.
                    _waitStarted = _clock - _normalSheatheDelay;
                }
                else
                {
                    BeginSheathe();
                }
                break;
            case PlayerActionStage.GuardSucceeded:
                Publish(KatanaStage.GuardSucceeded);
                break;
            case PlayerActionStage.ChargeStarted:
                bool drawn = _weaponState != KatanaWeaponState.Sheathed;
                ResetChain();
                _weaponState = drawn ? KatanaWeaponState.Sheathing : KatanaWeaponState.Sheathed;
                _sheatheUntil = _clock + 0.2f;
                Publish(KatanaStage.ChargeStarted, "", drawn);
                break;
            case PlayerActionStage.ChargeReady:
                Publish(KatanaStage.ChargeReady);
                break;
            case PlayerActionStage.ChargeCancelled:
            case PlayerActionStage.ChargeEnded:
                _weaponState = KatanaWeaponState.Sheathed;
                Publish(KatanaStage.ChargeReleased, e.Stage.ToString());
                break;
            case PlayerActionStage.DashStarted:
                ClearQueuedAttack();
                if (_counterAttack)
                    _strike = 2;
                else if (_player.TargetedComboStrike > 0)
                    _strike = _player.TargetedComboStrike;
                else
                    ResetChain();
                _weaponState = _counterAttack ? KatanaWeaponState.Drawn : KatanaWeaponState.Sheathed;
                _waiting = false;
                _waitAfterRecovery = false;
                _comboExpiresAt = 0f;
                Publish(KatanaStage.DashStarted);
                break;
            case PlayerActionStage.SlashStarted:
                if (_player.TargetedComboStrike > 0)
                {
                    _strike = _player.TargetedComboStrike;
                    _nextComboAllowedAt = _clock + _targetedAttackMinimumDuration;
                    if (_queuedTargetedAttack) _queuedReadyAt = _nextComboAllowedAt;
                }
                _weaponState = KatanaWeaponState.Drawn;
                Publish(KatanaStage.SlashStarted);
                break;
            case PlayerActionStage.ActionEnded:
                Publish(KatanaStage.StrikeEnded);
                if (e.State == PlayerActionState.NormalAttack)
                {
                    if (!_chainTransition)
                    {
                        if (e.Outcome == PlayerAttackOutcome.None || _counterAttack)
                            ClearQueuedAttack();
                        _comboExpiresAt = !_counterAttack && _strike >= MAX_COMBO_STRIKES
                            ? _clock + Mathf.Max(_normalSheatheDelay, _comboResetDelay)
                            : !_counterAttack && e.Outcome != PlayerAttackOutcome.None
                                ? _clock + _comboResetDelay
                                : 0f;
                        _sheatheAfterRecovery = false;
                        _waitAfterRecovery = true;
                    }
                }
                else if (e.State == PlayerActionState.IaidoSlash)
                {
                    bool continueTargetedCombo = e.Outcome == PlayerAttackOutcome.Hit
                        && _player.TargetedComboStrike > 0
                        && _strike < MAX_COMBO_STRIKES;
                    _comboExpiresAt = continueTargetedCombo
                        ? Mathf.Max(_clock, _nextComboAllowedAt) + _comboResetDelay : 0f;
                    if (!continueTargetedCombo)
                        ClearQueuedAttack();
                    _sheatheAfterRecovery = !continueTargetedCombo;
                    _waitAfterRecovery = true;
                }
                else if (_weaponState == KatanaWeaponState.Drawn)
                    BeginSheathe();
                else if (e.Outcome == PlayerAttackOutcome.WallBlocked)
                    Publish(KatanaStage.ChargeReleased, "Wall blocked");
                // 반격 돌진·베기 동안의 무적을 반격 종료 뒤에도 설정한 시간만큼 이어간다.
                if (_counterAttack)
                    _counterInvulnerableUntil = _playerClock.Now + _counterInvulnerabilityAfter;
                _counterAttack = false;
                break;
            case PlayerActionStage.RecoveryEnded:
            case PlayerActionStage.StateChanged:
                if (_queuedAttack && e.State == PlayerActionState.Free)
                    _queuedActionId = _player.CurrentActionId;
                if (_waitAfterRecovery && e.State == PlayerActionState.Free && !_parryMotion)
                {
                    _waitAfterRecovery = false;
                    BeginWaiting();
                }
                break;
        }
    }

    /// <summary>stage와 선택 데이터를 현재 행동 ID와 함께 외부 표현·디버그 구독자에게 전달한다.</summary>
    private void Publish(KatanaStage stage, string detail = "", bool wasDrawn = false)
    {
        EventRaised?.Invoke(new KatanaEvent(stage, _strike, _player.CurrentActionId, wasDrawn, detail));
    }

    /// <summary>
    /// 자유 상태에서 가드에 들어가고 패링 창을 연다. 공격 중이면 가드·패링을 받지 않고 Attacking으로 거부하며 예약하지 않는다.
    /// inputId는 가드가 시작되면 GuardInputId에 저장되며, 입력 ID가 없는 호출은 0을 사용한다. 거부 이유는 LastRejectReason에 남긴다.
    /// 퍼펙트 패링 모션 중에는 inputId가 패링한 가드의 입력과 다를 때만 모션을 끊고 가드에 들어간다.
    /// </summary>
    public bool BeginDefense(int inputId = 0)
    {
        if (!isActiveAndEnabled || _player.IsPaused || Time.timeScale <= 0f)
            return Refuse(PlayerInputRejectReason.Unavailable);

        // 공격 중에는 회피만 받는다. 공격을 끊고 가드로 바꾸지 않는다.
        PlayerActionState state = _player.CurrentState;
        if (state == PlayerActionState.NormalAttack || state == PlayerActionState.IaidoDash
            || state == PlayerActionState.IaidoSlash || _player.IsAttackRecoveryMovement)
            return Refuse(PlayerInputRejectReason.Attacking);

        if (state != PlayerActionState.Free || _playerClock.Now < _defenseReadyUntil)
            return Refuse(PlayerInputRejectReason.ActionBusy);

        // 퍼펙트 패링 모션은 새 우클릭만 끊고 가드로 들어간다. 패링한 우클릭을 계속 누르는 재진입은 모션이 끝날 때까지 막는다.
        if (_parryMotion)
        {
            if (inputId == _parryMotionGuardInputId) return Refuse(PlayerInputRejectReason.ActionBusy);
            _parryMotion = false;
        }

        return StartDefense(inputId) || Refuse(PlayerInputRejectReason.ActionBusy);
    }

    /// <summary>유지 가드와 패링 창을 종료하고 기존 납도 대기 흐름으로 복귀한다.</summary>
    public void EndDefense()
    {
        _parryOpen = false;
        _player.EndGuard();
    }

    /// <summary>
    /// incoming이 eligible인 전방 근접 접촉이고 가드 중이면 피해를 차단한다.
    /// GuardContactOutcome의 가드·퍼펙트 패링에 따라 적 회복·반동·방어 피드백을 적용하고 일반 적의 근접 베기·돌진 가드에 수평 밀림을 추가하여 성공 여부를 반환한다.
    /// 퍼펙트 패링은 쳐내기 피드백과 경직을 적용한 뒤 가드를 끝내고 퍼펙트 패링 모션을 시작한다. 패링 뒤 반격은 예약하지 않는다.
    /// allowCounter가 false이면 패링에도 경직을 만들지 않으며 공격자가 사라진 접촉도 처리한다.
    /// </summary>
    public bool TryDefendIncoming(DamageContext incoming, bool eligible, bool allowCounter = true)
    {
        if (!isActiveAndEnabled || !eligible || !_player.IsGuarding || _player.IsPaused
            || incoming.Amount <= 0f || Time.timeScale <= 0f) return false;
        DefenseOutcome outcome = GuardContactOutcome;
        bool parry = outcome != DefenseOutcome.Guard;
        GameObject instigator = incoming.Instigator;
        EnemyHealth parriedEnemy = parry && allowCounter && instigator != null
            ? instigator.GetComponentInParent<EnemyHealth>() : null;
        _defenseReadyUntil = parry ? _playerClock.Now : _playerClock.Now + _blockInputRecovery;
        Vector3 point = _player.GetComponent<EnemyCombatService>().GetGuardContactPoint(incoming.Point);
        _parryOpen = false;
        _waiting = false;
        _weaponState = KatanaWeaponState.Drawn;
        _player.NotifyGuardSuccess();
        _motor.AddCombatRecoil(incoming.Direction, parry ? 0.9f : 0.6f);
        EnemyAttackBehaviour[] patterns = instigator != null
            ? instigator.GetComponents<EnemyAttackBehaviour>() : System.Array.Empty<EnemyAttackBehaviour>();
        bool hasDefenseBeastLeap = false;
        foreach (EnemyAttackBehaviour pattern in patterns)
        {
            if (pattern.PatternId != incoming.PatternId) continue;
            // 돌진의 쳐내기 반응은 적 담당이 제공하므로 플레이어 밀림만 적용한다.
            if (pattern is EnemyMeleeAttack melee) melee.Deflect(parry, point);
            else if (pattern is DefenseBeastLeapAttack) hasDefenseBeastLeap = true;
            else if (!(pattern is EnemyChargeAttack)) continue;
            if (!parry && instigator.GetComponentInParent<BossController>() == null)
            {
                Vector3 pushDirection = Vector3.ProjectOnPlane(
                    _player.transform.position - instigator.transform.position, Vector3.up);
                if (pushDirection.sqrMagnitude < 0.0001f) pushDirection = incoming.Direction;
                _motor.ApplyCombatPush(pushDirection, _guardPushDistance, _guardPushDuration);
            }
        }
        if (!hasDefenseBeastLeap && parriedEnemy != null && parriedEnemy.IsAlive
            && parriedEnemy.TryGetComponent(out EnemyAttackSelector selector))
            selector.Stagger(_parryStaggerDuration);
        DefenseSucceeded?.Invoke(new KatanaDefenseResult(incoming, point, parry, outcome == DefenseOutcome.PerfectParry));
        if (parry)
        {
            Publish(KatanaStage.ParrySucceeded, incoming.PatternId, true);
            ParrySucceeded?.Invoke(new KatanaParryResult(incoming, 1));
            BeginPerfectParryMotion();
        }
        return true;
    }



    /// <summary>자유 상태로 가드에 들어가 inputId를 GuardInputId에 저장하고 패링 창을 연다. 가드 시작 여부를 반환한다.</summary>
    private bool StartDefense(int inputId)
    {
        if (_player.CurrentState != PlayerActionState.Free || !_player.BeginGuard())
            return false;

        _guardInputId = inputId;
        _waiting = false;
        OpenDefenseWindow();
        return true;
    }
}

/// <summary>방어 접촉의 원래 피해, 검 앞 접촉점, 패링(일반 또는 퍼펙트) 여부를 표현 담당자에게 전달한다.</summary>
public readonly struct KatanaDefenseResult
{
    public DamageContext Incoming { get; }
    public Vector3 Point { get; }
    public bool IsParry { get; }
    public bool IsPerfectParry { get; }

    /// <summary>incoming·point·isParry·isPerfectParry를 저장한다. 피해나 공격 상태를 직접 변경하지 않는다.</summary>
    public KatanaDefenseResult(DamageContext incoming, Vector3 point, bool isParry, bool isPerfectParry = false)
    {
        Incoming = incoming;
        Point = point;
        IsParry = isParry;
        IsPerfectParry = isPerfectParry;
    }
}
