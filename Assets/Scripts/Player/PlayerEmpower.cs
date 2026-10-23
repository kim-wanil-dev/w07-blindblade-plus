using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 성공 시 집중, 일섬의 짧은 집중 연출과 거합 자동 차징·공격 수명을 관리한다.
/// 자원은 PlayerSkillGauge가 보유하며 수동 스킬은 집중 여부와 무관하게 전용 입력으로 시작한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KatanaCombatCoordinator))]
[RequireComponent(typeof(PlayerEmpowerStrike))]
[RequireComponent(typeof(PlayerPerfectPulse))]
[RequireComponent(typeof(PlayerSwordWaveLauncher))]
[RequireComponent(typeof(PlayerSkillGauge))]
public sealed class PlayerEmpower : MonoBehaviour
{
    private const string HIT_STOP_TIME_OWNER = "PlayerEmpower.HitStop";
    private const string READY_TIME_OWNER = "PlayerEmpower.Ready";
    private const string STRIKE_TIME_OWNER = "PlayerEmpower.Strike";
    // 같은 소유자 요청은 겹쳐 등록할 수 없으므로 차징 감속은 두 소유자를 번갈아 써서 배율을 바꾼다.
    private const string CHARGE_TIME_OWNER_A = "PlayerEmpower.ChargeA";
    private const string CHARGE_TIME_OWNER_B = "PlayerEmpower.ChargeB";
    private const int MAX_TOKENS = 3;
    private const float CHARGE_SCALE_STEP = 0.001f;

    [Header("준비")]
    [SerializeField, Min(0.1f), Tooltip("퍼펙트 패링 성공 후 집중을 유지하는 실시간입니다. 스킬 사용 가능 시간 제한과 별개입니다.")]
    private float _readyDuration = 5f;
    [SerializeField, Range(0.5f, 1.5f), Tooltip("극한 회피 성공 후 집중을 유지하는 실시간입니다. 스킬 사용 가능 조건과 별개입니다.")]
    private float _dodgeReadyDuration = 1f;

    [Header("히트스톱")]
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간 세계와 플레이어가 함께 거의 멈추는 실시간. 0이면 바로 불릿 타임이다.")]
    private float _hitStopDuration = 0.08f;
    [SerializeField, Range(0.01f, 1f)] private float _hitStopScale = 0.03f;

    [Header("무적")]
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간부터 모든 공격을 무적으로 막는 실시간(초).")]
    private float _perfectParryInvulnerableDuration = 0.3f;
    private float _invulnerableUntil = float.NegativeInfinity;
    private bool _counterFocusInvulnerable;

    [Header("불릿 타임")]
    [SerializeField, Range(0.1f, 1f), Tooltip("강화 준비와 강화 공격 동안 세계 시간 배율.")]
    private float _worldTimeScale = 0.2f;
    [SerializeField, Range(0.1f, 1f), Tooltip("강화 준비와 강화 공격 동안 플레이어 시간 배율. 세계 배율보다 크면 플레이어가 덜 느리다.")]
    private float _playerTimeScale = 0.5f;

    [Header("거합 자동 차징")]
    [SerializeField, Min(0.01f), Tooltip("Q 한 번으로 거합 시전까지 자동 차징하는 실시간입니다. 세계 감속 시간과 별개입니다.")]
    private float _ultimateChargeDuration = 0.75f;
    [SerializeField, Range(0.01f, 1f), Tooltip("차징 동안 세계 시간 배율이 천천히 내려가 닿는 목표 배율. 플레이어 배율은 바뀌지 않는다.")]
    private float _chargeWorldTimeScale = 0.1f;
    [SerializeField, Min(0.01f), Tooltip("차징 시작부터 세계 배율이 목표 배율에 닿을 때까지의 실시간(초).")]
    private float _chargeSlowDuration = 0.7f;
    private bool _charging;
    private float _chargeElapsed;
    private float _appliedChargeScale;
    private bool _useChargeOwnerB;
    private float _presentationProgress;
    private float _presentationSlowProgress;

    [Header("스킬 자원")]
    [SerializeField, Min(0.01f), Tooltip("일섬 시작부터 집중 연출이 유지되는 실시간입니다. 일시정지 동안 멈춥니다.")]
    private float _swordWaveFocusDuration = 0.2f;
    private PlayerSkillGauge _gauges;
    private float _swordWaveFocusRemaining;
    private bool _automaticCharge;
    private PlayerMotor _motor;
    private bool _rewardingDodge;
    private double _timerUpdatedAt;

    [Header("상태")]
    private KatanaCombatCoordinator _combat;
    private PlayerController _player;
    private PlayerHealth _health;
    private PlayerEmpowerStrike _strike;
    private PlayerPerfectPulse _pulse;
    private PlayerSwordWaveLauncher _swordWave;
    private CombatSessionRuntime _timeRuntime;
    private CombatTimeToken _timeToken;
    private PlayerEmpowerStage _stage;
    private bool _active;
    private bool _dodgeReady;
    private int _empowerId;
    private int _sourceAttackId;
    private float _readyElapsed;
    private float _hitStopRemaining;
    private bool _onboardingReadyHeld;

    public bool IsReady => _active && _stage == PlayerEmpowerStage.Ready;
    public bool IsDodgeReady => IsReady && _dodgeReady;
    public bool IsSwordWaveReady => IsReady && !_dodgeReady;
    public bool IsStriking => _active && _stage == PlayerEmpowerStage.Started;
    public bool IsBulletTimeActive => _active && _hitStopRemaining <= 0f;
    public bool IsPerfectParryInvulnerable => Time.unscaledTime < _invulnerableUntil;
    public bool IsCounterFocusInvulnerable => _counterFocusInvulnerable;
    public float PlayerTimeScale => _playerTimeScale;
    public float WorldTimeScale => _worldTimeScale;
    public int Tokens => Mathf.FloorToInt(_gauges.SwordWaveGauge);
    public int MaxTokens => MAX_TOKENS;
    public bool IsCharging => IsReady && _charging;
    public bool IsUltimateChargeComplete => IsCharging && _chargeElapsed >= _ultimateChargeDuration;
    public float ChargeProgress => IsCharging ? Mathf.Clamp01(_chargeElapsed / _ultimateChargeDuration) : _presentationProgress;
    public float ChargeSlowProgress => IsCharging ? Mathf.Clamp01(_chargeElapsed / _chargeSlowDuration) : _presentationSlowProgress;
    public float UltimateChargeDuration => _ultimateChargeDuration;
    public float ChargeSlowDuration => _chargeSlowDuration;
    public float ChargeWorldTimeScale => _chargeWorldTimeScale;
    public bool IsSwordWaveFocusing => _swordWaveFocusRemaining > 0f;
    public bool IsAutomaticCharging => IsCharging && _automaticCharge;
    public bool IsSwordWaveCasting => _swordWave.IsSkillActive;
    public float CounterChargeDuration => 1.5f;

    /// <summary>거합 게이지가 가득 찬 상태에서 퍼펙트 패링이 성공했을 때 알린다.</summary>
    public event Action UltimateReadyParry;
    public event Action UltimateReadyDodge;

    /// <summary>거합 자원이 새로 완충되면 기존 완충음 구독자에게 한 번 알린다. 입력값 없이 준비 알림을 발행한다.</summary>
    private void HandleUltimateBecameReady()
    {
        if (_rewardingDodge) UltimateReadyDodge?.Invoke();
        else UltimateReadyParry?.Invoke();
    }

    /// <summary>잔량과 현재 행동을 검사해 일섬을 즉시 시작한다. 수락 시 한 칸을 소비하고 첫 집중 시간을 설정하며 성공 여부를 반환한다.</summary>
    public bool TryBeginSwordWave()
    {
        if (!CanBeginManualSkill() || !_gauges.CanUseSwordWave || !_combat.CanStartAction) return false;
        if (!_combat.RequestSwordWaveAttack()) return false;
        if (!_gauges.TryConsumeSwordWave()) return false;
        if (_active) End(PlayerEmpowerEndReason.SwordWave);
        _active = true;
        _dodgeReady = false;
        _stage = PlayerEmpowerStage.Ready;
        _empowerId++;
        _sourceAttackId = 0;
        _hitStopRemaining = 0f;
        _swordWaveFocusRemaining = _swordWaveFocusDuration;
        _timerUpdatedAt = Time.realtimeSinceStartupAsDouble;
        ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredAttack, _worldTimeScale);
        Publish(PlayerEmpowerStage.Ready, PlayerEmpowerEndReason.None);
        _swordWave.PlaySkill(_player.CurrentActionId);
        return true;
    }

    /// <summary>집중 여부와 무관하게 거합 완충 자원으로 자동 차징을 시작한다. 입력값 없이 시전 상태와 시간을 설정하고 수락 여부를 반환한다.</summary>
    public bool TryBeginAutomaticCharge()
    {
        if (!CanBeginManualSkill() || !_gauges.CanUseUltimate || !_combat.CanStartAction) return false;
        if (_active) End(PlayerEmpowerEndReason.Cancelled);
        _active = true;
        _stage = PlayerEmpowerStage.Ready;
        _dodgeReady = false;
        _empowerId++;
        _sourceAttackId = 0;
        _hitStopRemaining = 0f;
        _charging = true;
        _automaticCharge = true;
        _chargeElapsed = 0f;
        _timerUpdatedAt = Time.realtimeSinceStartupAsDouble;
        _appliedChargeScale = _worldTimeScale;
        _combat.CancelForEmpowerStrike();
        ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
        Publish(PlayerEmpowerStage.Ready, PlayerEmpowerEndReason.None);
        return true;
    }

    /// <summary>사망·일시정지·중복 시전·보스 반격 상태를 검사해 현재 수동 스킬 요청이 가능한지 반환한다.</summary>
    private bool CanBeginManualSkill()
    {
        return isActiveAndEnabled && !_player.IsPaused && Time.timeScale > 0f
            && _health.IsAlive && !IsStriking && !IsCharging && !IsSwordWaveFocusing
            && !_swordWave.IsSkillActive && !_counterFocusInvulnerable
            && !_motor.IsDodging;
    }

    void Awake()
    {
        _combat = GetComponent<KatanaCombatCoordinator>();
        _player = GetComponent<PlayerController>();
        _health = GetComponent<PlayerHealth>();
        _strike = GetComponent<PlayerEmpowerStrike>();
        _pulse = GetComponent<PlayerPerfectPulse>();
        _swordWave = GetComponent<PlayerSwordWaveLauncher>();
        _gauges = GetComponent<PlayerSkillGauge>();
        _motor = GetComponent<PlayerMotor>();
    }

    void OnEnable()
    {
        _health.Died += HandleDied;
        _gauges.UltimateBecameReady += HandleUltimateBecameReady;
    }

    void OnDisable()
    {
        _health.Died -= HandleDied;
        _gauges.UltimateBecameReady -= HandleUltimateBecameReady;
        _swordWave.CancelSkill();
        if (_active) End(PlayerEmpowerEndReason.SessionEnded);
    }

    void Update()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        float dt = (float)Math.Max(0d, now - _timerUpdatedAt);
        _timerUpdatedAt = now;
        if (!IsReady || Time.timeScale <= 0f || _player.IsPaused) return;

        // 시전 전 프레임의 경과 시간이 타이머에 포함되지 않도록 시작 시각부터 잰다.
        if (_hitStopRemaining > 0f)
        {
            _hitStopRemaining -= dt;
            if (_hitStopRemaining <= 0f) ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
        }
        if (_charging)
        {
            _chargeElapsed += dt;
            if (_hitStopRemaining <= 0f) UpdateChargeSlow();
            if (_automaticCharge && IsUltimateChargeComplete && !TryBeginStrike())
                End(PlayerEmpowerEndReason.Cancelled);
            return;
        }
        if (IsSwordWaveFocusing)
        {
            _swordWaveFocusRemaining = Mathf.Max(0f, _swordWaveFocusRemaining - dt);
            if (!IsSwordWaveFocusing) End(PlayerEmpowerEndReason.Completed);
            return;
        }
        _readyElapsed += dt;
        if (!_onboardingReadyHeld && _readyElapsed >= (_dodgeReady ? _dodgeReadyDuration : _readyDuration))
            End(PlayerEmpowerEndReason.Expired);
    }

    /// <summary>
    /// 패링 성공에 따른 일섬·거합 보상을 지급하고 패링 집중과 무적을 시작하거나 갱신한다.
    /// attackId를 원인으로 사용하며 새 준비 또는 회피 준비 교체 여부를 반환한다.
    /// </summary>
    public bool NotifyPerfectParry(int attackId)
    {
        if (IsStriking) return false;
        _gauges.RewardPerfectParry();
        _invulnerableUntil = Time.unscaledTime + _perfectParryInvulnerableDuration;
        if (IsReady)
        {
            if (_dodgeReady || IsSwordWaveFocusing)
            {
                End(PlayerEmpowerEndReason.Cancelled);
                BeginReady(attackId);
                return true;
            }
            _readyElapsed = 0f;
            return false;
        }

        BeginReady(attackId);
        return true;
    }

    /// <summary>
    /// 극한 회피 성공에 따른 게이지 보상을 지급하고 회피 전용 집중을 시작하거나 갱신한다.
    /// attackId를 원인으로 사용하며 거합 게이지 완충 전이 알림을 발생시킬 수 있다.
    /// </summary>
    public void NotifyPerfectDodge(int attackId)
    {
        if (IsStriking) return;
        _rewardingDodge = true;
        _gauges.RewardPerfectDodge();
        _rewardingDodge = false;
        BeginDodgeBulletTime(attackId);
    }

    /// <summary>
    /// attackId를 원인으로 회피 전용 집중을 시작하거나 갱신한다.
    /// 패링 준비와 강화 공격 중에는 해당 상태를 바꾸지 않는다.
    /// </summary>
    public void BeginDodgeBulletTime(int attackId)
    {
        if (IsStriking) return;
        if (IsDodgeReady)
            _readyElapsed = 0f;
        else if (!IsReady)
        {
            _active = true;
            _dodgeReady = true;
            _stage = PlayerEmpowerStage.Ready;
            _empowerId++;
            _sourceAttackId = attackId;
            _readyElapsed = 0f;
            _hitStopRemaining = 0f;
            _timerUpdatedAt = Time.realtimeSinceStartupAsDouble;
            ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
            Publish(PlayerEmpowerStage.Ready, PlayerEmpowerEndReason.None);
        }
    }

    /// <summary>
    /// 현재 준비를 온보딩 연습이 끝날 때까지 유지한다.
    /// 실제 퍼펙트 패링으로 시작한 준비의 토큰과 집중 상태를 그대로 보존한다.
    /// </summary>
    public void HoldOnboardingReady()
    {
        if (IsReady) _onboardingReadyHeld = true;
    }

    /// <summary>
    /// 온보딩 연계 단계에 들어가기 전에 활성 집중을 끝내고 토큰을 startingTokens개로 설정한다.
    /// 새 퍼펙트 패링이 실제로 일어나기 전에는 준비와 불릿 타임을 시작하지 않는다.
    /// </summary>
    public void PrepareOnboardingFollowUp(int startingTokens)
    {
        if (_active) End(PlayerEmpowerEndReason.SessionEnded);
        _gauges.SetGauges(Mathf.Clamp(startingTokens, 0, MAX_TOKENS),
            Mathf.Clamp(startingTokens, 0, MAX_TOKENS) * (100f / MAX_TOKENS));
    }

    /// <summary>
    /// 온보딩 검기 연습에 들어갈 때 준비 상태를 유지하거나 다시 시작한다.
    /// 토큰을 검기 사용 범위인 1~MAX_TOKENS-1개로 맞추고 준비 만료를 막는다.
    /// </summary>
    public void EnsureOnboardingSwordWaveReady()
    {
        if (IsStriking) return;
        _gauges.SetGauges(Mathf.Clamp(Tokens, 1, MAX_TOKENS - 1), _gauges.UltimateGauge);
        if (IsReady)
        {
            _onboardingReadyHeld = true;
            return;
        }

        if (_active) End(PlayerEmpowerEndReason.SessionEnded);
        _onboardingReadyHeld = true;
        BeginReady(0);
    }

    /// <summary>
    /// 온보딩 궁극기 연습을 위해 토큰을 정확히 MAX_TOKENS로 맞추고 준비 상태로 진입한다.
    /// 기존 준비를 끝낸 뒤 집중 파동과 불릿 타임을 시작하고 준비 만료를 잠근다.
    /// </summary>
    public void BeginOnboardingUltimate()
    {
        if (IsStriking) return;
        if (_active) End(PlayerEmpowerEndReason.SessionEnded);
        _gauges.SetGauges(MAX_TOKENS, 100f);
        _onboardingReadyHeld = true;
        BeginReady(0);
    }

    /// <summary>
    /// attackId를 원인으로 새 준비를 시작하고 히트스톱·불릿 타임·퍼펙트 파동을 적용한다.
    /// 준비 시각과 강화 ID를 갱신하고 EmpowerChanged를 발행한다.
    /// </summary>
    private void BeginReady(int attackId)
    {
        _active = true;
        _dodgeReady = false;
        _stage = PlayerEmpowerStage.Ready;
        _empowerId++;
        _sourceAttackId = attackId;
        _readyElapsed = 0f;
        _hitStopRemaining = _hitStopDuration;
        _timerUpdatedAt = Time.realtimeSinceStartupAsDouble;
        if (_hitStopRemaining > 0f)
            ChangeTimeRequest(HIT_STOP_TIME_OWNER, CombatTimeReason.EmpoweredReady, _hitStopScale);
        else
            ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
        _pulse.Begin();
        Publish(PlayerEmpowerStage.Ready, PlayerEmpowerEndReason.None);
    }

    /// <summary>
    /// 레거시 온보딩에서만 거합 홀드 차징을 시작한다.
    /// 준비 상태와 거합 게이지를 확인하고 _charging 및 _chargeElapsed를 설정한 뒤 수락 여부를 반환한다.
    /// </summary>
    public bool BeginCharge()
    {
        if (!IsReady || _charging || !_gauges.CanUseUltimate || _player.IsPaused
            || _player.CurrentState == PlayerActionState.Dead) return false;

        _charging = true;
        _automaticCharge = false;
        _chargeElapsed = 0f;
        _appliedChargeScale = _worldTimeScale;
        return true;
    }

    /// <summary>
    /// 보스 자동 반격 연출에 사용할 표시 진행도와 감속 효과 진행도를 설정한다.
    /// 두 입력값은 0~1로 제한해 ChargeProgress와 ChargeSlowProgress에 저장하며 실제 차징 중에는 실제 진행도가 우선한다.
    /// </summary>
    public void SetChargePresentation(float progress, float slowProgress)
    {
        _presentationProgress = Mathf.Clamp01(progress);
        _presentationSlowProgress = Mathf.Clamp01(slowProgress);
    }

    /// <summary>
    /// 보스 거합을 받아친 뒤 X자 받아치기 강화 공격이 나가기 전까지 모든 공격을 무적으로 막을지 invulnerable로 정한다.
    /// 강화 공격이 시작되면 강화 공격 무적이 이어받으므로 호출자가 끈다. _counterFocusInvulnerable을 바꾼다.
    /// </summary>
    public void SetCounterFocusInvulnerable(bool invulnerable)
    {
        _counterFocusInvulnerable = invulnerable;
    }

    /// <summary>
    /// 진행 중인 거합 차징을 발동 없이 취소하고 소비되지 않은 게이지를 유지한다.
    /// 자동 차징은 준비와 시간 요청을 끝내며, 온보딩 차징은 기존 준비 상태로 복귀한다.
    /// </summary>
    public void CancelCharge()
    {
        if (!IsCharging) return;
        if (_automaticCharge)
        {
            End(PlayerEmpowerEndReason.Cancelled);
            return;
        }
        _charging = false;
        _chargeElapsed = 0f;
        if (_dodgeReady)
        {
            End(PlayerEmpowerEndReason.Cancelled);
            return;
        }
        if (_hitStopRemaining <= 0f)
            ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
    }

    /// <summary>회피 전용 준비 중 일반·돌진 공격이 수락되면 검기 없이 준비와 시간 요청을 즉시 끝낸다.</summary>
    public void CompleteDodgeReadyWithAttack()
    {
        if (IsDodgeReady) End(PlayerEmpowerEndReason.Attack);
    }

    /// <summary>
    /// 레거시 온보딩에서만 일반 공격과 일섬 검기 예약을 연결한다.
    /// previousActionId는 공격 요청 전 행동 ID이며, 준비 상태가 아니면 아무것도 바꾸지 않는다.
    /// </summary>
    public void CompleteWithSwordWave(int previousActionId)
    {
        if (!IsReady) return;
        _onboardingReadyHeld = false;
        _swordWave.Arm(previousActionId);
        End(PlayerEmpowerEndReason.SwordWave);
    }

    /// <summary>
    /// 거합 차징이 완료되면 거합 게이지를 모두 소비하고 강화 공격을 시작해 Started를 발행한 뒤 true를 반환한다.
    /// 차징이 다 차지 않았거나 사망·일시정지이면 false다.
    /// 진행 중인 가드·공격·반격 예약을 끝내고 연출을 재생하며, 시간 요청은 강화 공격용으로 바꿔 연출이 끝날 때까지 불릿 타임을 유지한다.
    /// </summary>
    public bool TryBeginStrike()
    {
        if (!IsUltimateChargeComplete || _player.IsPaused || _player.CurrentState == PlayerActionState.Dead) return false;

        if (!_gauges.TryConsumeUltimate()) return false;
        _onboardingReadyHeld = false;
        _charging = false;
        _automaticCharge = false;
        _stage = PlayerEmpowerStage.Started;
        _hitStopRemaining = 0f;
        ChangeTimeRequest(STRIKE_TIME_OWNER, CombatTimeReason.EmpoweredAttack, _worldTimeScale);
        _combat.CancelForEmpowerStrike();
        _player.CancelActionForEmpowerStrike();
        Publish(PlayerEmpowerStage.Started, PlayerEmpowerEndReason.None);
        _strike.Play(HandleStrikeFinished);
        BlindBlade.Telemetry.CombatTelemetrySignals.SkillActivated("Empower", "manual",
            Guid.NewGuid().ToString("N"), transform.position);
        return true;
    }

    /// <summary>
    /// 보스 거합을 받아쳐 플레이어 강화 공격을 받아치기 연출(적의 enemyCutColor 사선 위에 대칭 사선을 덮고 기본 사선을 더한 X자)로 한 번 발동하고 true를 반환한다.
    /// 게이지를 소비하지 않고 차징도 하지 않는다. 판정·피해·불릿 타임·무적은 평소 강화 공격과 같고 Started를 발행한다.
    /// 이미 강화 공격 중이거나 사망·일시정지이면 false다. 진행 중이던 준비가 있으면 강화 공격으로 이어받는다.
    /// </summary>
    public bool BeginCounterStrike(Color enemyCutColor)
    {
        if (IsStriking || _player.IsPaused || _player.CurrentState == PlayerActionState.Dead) return false;

        if (!_active)
        {
            _active = true;
            _empowerId++;
            _sourceAttackId = 0;
        }
        _onboardingReadyHeld = false;
        _charging = false;
        _stage = PlayerEmpowerStage.Started;
        _hitStopRemaining = 0f;
        ChangeTimeRequest(STRIKE_TIME_OWNER, CombatTimeReason.EmpoweredAttack, _worldTimeScale);
        _combat.CancelForEmpowerStrike();
        _player.CancelActionForEmpowerStrike();
        Publish(PlayerEmpowerStage.Started, PlayerEmpowerEndReason.None);
        _strike.PlayCounter(HandleStrikeFinished, enemyCutColor);
        BlindBlade.Telemetry.CombatTelemetrySignals.SkillActivated("Empower", "counter",
            Guid.NewGuid().ToString("N"), transform.position);
        return true;
    }

    /// <summary>강화 공격 연출이 끝나면 강화 공격을 Completed로 끝낸다.</summary>
    private void HandleStrikeFinished()
    {
        if (IsStriking) End(PlayerEmpowerEndReason.Completed);
    }

    /// <summary>플레이어 사망을 받아 진행 중인 준비나 강화 공격을 Died로 끝낸다.</summary>
    private void HandleDied(PlayerHealth health)
    {
        _swordWave.CancelSkill();
        if (_active) End(PlayerEmpowerEndReason.Died);
    }

    /// <summary>
    /// 진행 중인 준비나 강화 공격을 reason으로 끝내고 시간 요청을 해제한 뒤 Ended를 발행한다.
    /// _active를 거짓으로 바꾸며 이미 끝났으면 호출하지 않는다.
    /// </summary>
    private void End(PlayerEmpowerEndReason reason)
    {
        if (IsStriking && reason != PlayerEmpowerEndReason.Completed) _strike.Stop();
        _active = false;
        _dodgeReady = false;
        _onboardingReadyHeld = false;
        _stage = PlayerEmpowerStage.Ended;
        _hitStopRemaining = 0f;
        _charging = false;
        _chargeElapsed = 0f;
        _automaticCharge = false;
        _swordWaveFocusRemaining = 0f;
        ReleaseTimeRequest();
        Publish(PlayerEmpowerStage.Ended, reason);
    }

    /// <summary>
    /// _chargeElapsed를 _chargeSlowDuration으로 나눈 진행도로 세계 배율을 준비 배율에서 차징 목표 배율까지 부드럽게 내린다.
    /// 배율이 CHARGE_SCALE_STEP 이상 바뀐 경우에만 두 차징 소유자를 번갈아 써서 시간 요청을 바꾸고 _appliedChargeScale을 갱신한다.
    /// </summary>
    private void UpdateChargeSlow()
    {
        float t = Mathf.Clamp01(_chargeElapsed / _chargeSlowDuration);
        float scale = Mathf.Lerp(_worldTimeScale, _chargeWorldTimeScale, Mathf.SmoothStep(0f, 1f, t));
        // 끝에 닿은 순간에는 남은 작은 차이도 한 번 반영해 목표 배율에 정확히 멈춘다.
        if (Mathf.Abs(scale - _appliedChargeScale) < CHARGE_SCALE_STEP && t < 1f) return;
        if (Mathf.Approximately(scale, _appliedChargeScale)) return;

        _useChargeOwnerB = !_useChargeOwnerB;
        ChangeTimeRequest(_useChargeOwnerB ? CHARGE_TIME_OWNER_B : CHARGE_TIME_OWNER_A,
            CombatTimeReason.EmpoweredReady, scale);
        _appliedChargeScale = scale;
    }

    /// <summary>
    /// ownerId·reason·scale로 세계 시간 요청을 새로 등록한 뒤 이전 요청을 해제해 배율이 끊기지 않게 바꾼다.
    /// 세션 시간 런타임이 없으면 요청 없이 진행하며 _timeRuntime과 _timeToken을 갱신한다.
    /// </summary>
    private void ChangeTimeRequest(string ownerId, CombatTimeReason reason, float scale)
    {
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        CombatTimeToken previous = _timeToken;
        CombatSessionRuntime previousRuntime = _timeRuntime;
        _timeRuntime = null;
        _timeToken = default;
        if (runtime != null && runtime.IsInitialized)
        {
            _timeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session, ownerId, reason, scale));
            if (_timeToken.IsValid) _timeRuntime = runtime;
        }
        if (previousRuntime != null && previous.IsValid) previousRuntime.Release(previous);
    }

    /// <summary>등록한 세계 시간 요청이 있으면 해제하고 _timeRuntime과 _timeToken을 비운다.</summary>
    private void ReleaseTimeRequest()
    {
        if (_timeRuntime != null && _timeToken.IsValid) _timeRuntime.Release(_timeToken);
        _timeRuntime = null;
        _timeToken = default;
    }

    /// <summary>현재 _empowerId·원인 공격과 stage, reason, 시각으로 EmpowerChanged 사건을 발행한다.</summary>
    private void Publish(PlayerEmpowerStage stage, PlayerEmpowerEndReason reason)
    {
        _combat.Events.RaiseEmpowerChanged(new PlayerEmpowerEvent(_empowerId, stage, reason, _sourceAttackId, 0,
            Time.time, Time.realtimeSinceStartupAsDouble));
    }
}
