using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 퍼펙트 패링 뒤의 강화 준비와 강화 공격 수명을 관리한다.
/// 퍼펙트 패링 순간 짧게 세계를 멈춘 뒤 불릿 타임으로 좌클릭을 기다리며, 수락·만료·사망·세션 종료를 EmpowerChanged 사건으로 발행한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KatanaCombatCoordinator))]
[RequireComponent(typeof(PlayerEmpowerStrike))]
[RequireComponent(typeof(PlayerPerfectPulse))]
public sealed class PlayerEmpower : MonoBehaviour
{
    private const string HIT_STOP_TIME_OWNER = "PlayerEmpower.HitStop";
    private const string READY_TIME_OWNER = "PlayerEmpower.Ready";
    private const string STRIKE_TIME_OWNER = "PlayerEmpower.Strike";

    [Header("준비")]
    [SerializeField, Min(0.1f), Tooltip("퍼펙트 패링 뒤 좌클릭을 기다리는 시간. 일시정지를 제외한 실시간으로 잰다.")]
    private float _readyDuration = 5f;

    [Header("히트스톱")]
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간 세계와 플레이어가 함께 거의 멈추는 실시간. 0이면 바로 불릿 타임이다.")]
    private float _hitStopDuration = 0.08f;
    [SerializeField, Range(0.01f, 1f)] private float _hitStopScale = 0.03f;

    [Header("무적")]
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간부터 모든 공격을 무적으로 막는 실시간(초).")]
    private float _perfectParryInvulnerableDuration = 0.3f;
    private float _invulnerableUntil = float.NegativeInfinity;

    [Header("불릿 타임")]
    [SerializeField, Range(0.1f, 1f), Tooltip("강화 준비와 강화 공격 동안 세계 시간 배율.")]
    private float _worldTimeScale = 0.2f;
    [SerializeField, Range(0.1f, 1f), Tooltip("강화 준비와 강화 공격 동안 플레이어 시간 배율. 세계 배율보다 크면 플레이어가 덜 느리다.")]
    private float _playerTimeScale = 0.5f;

    [Header("상태")]
    private KatanaCombatCoordinator _combat;
    private PlayerController _player;
    private PlayerHealth _health;
    private PlayerEmpowerStrike _strike;
    private PlayerPerfectPulse _pulse;
    private CombatSessionRuntime _timeRuntime;
    private CombatTimeToken _timeToken;
    private PlayerEmpowerStage _stage;
    private bool _active;
    private int _empowerId;
    private int _sourceAttackId;
    private float _readyElapsed;
    private float _hitStopRemaining;

    public bool IsReady => _active && _stage == PlayerEmpowerStage.Ready;
    public bool IsStriking => _active && _stage == PlayerEmpowerStage.Started;
    public bool IsBulletTimeActive => _active && _hitStopRemaining <= 0f;
    public bool IsPerfectParryInvulnerable => Time.unscaledTime < _invulnerableUntil;
    public float PlayerTimeScale => _playerTimeScale;

    void Awake()
    {
        _combat = GetComponent<KatanaCombatCoordinator>();
        _player = GetComponent<PlayerController>();
        _health = GetComponent<PlayerHealth>();
        _strike = GetComponent<PlayerEmpowerStrike>();
        _pulse = GetComponent<PlayerPerfectPulse>();
    }

    void OnEnable()
    {
        _health.Died += HandleDied;
    }

    void OnDisable()
    {
        _health.Died -= HandleDied;
        if (_active) End(PlayerEmpowerEndReason.SessionEnded);
    }

    void Update()
    {
        if (!IsReady || Time.timeScale <= 0f || _player.IsPaused) return;

        // 히트스톱과 준비 대기는 불릿 타임에 늘어나지 않도록 일시정지를 제외한 실시간으로 잰다.
        float dt = Time.unscaledDeltaTime;
        if (_hitStopRemaining > 0f)
        {
            _hitStopRemaining -= dt;
            if (_hitStopRemaining <= 0f) ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
        }
        _readyElapsed += dt;
        if (_readyElapsed >= _readyDuration) End(PlayerEmpowerEndReason.Expired);
    }

    /// <summary>
    /// 퍼펙트 패링을 알리고 attackId를 원인으로 강화 준비를 시작한다. 새 준비이면 히트스톱 뒤 불릿 타임으로 넘어가며 퍼펙트 파동을 낸다.
    /// 이미 준비 중이면 대기 시간만 처음부터 다시 세고 false를 반환하며, 새 준비를 시작했을 때만 true를 반환한다.
    /// 준비 중이든 새 준비든 _perfectParryInvulnerableDuration 실시간 동안 무적을 건다. 강화 공격 중이면 아무것도 바꾸지 않고 false를 반환한다.
    /// </summary>
    public bool NotifyPerfectParry(int attackId)
    {
        if (IsStriking) return false;
        _invulnerableUntil = Time.unscaledTime + _perfectParryInvulnerableDuration;
        if (IsReady)
        {
            _readyElapsed = 0f;
            return false;
        }

        _active = true;
        _stage = PlayerEmpowerStage.Ready;
        _empowerId++;
        _sourceAttackId = attackId;
        _readyElapsed = 0f;
        _hitStopRemaining = _hitStopDuration;
        if (_hitStopRemaining > 0f)
            ChangeTimeRequest(HIT_STOP_TIME_OWNER, CombatTimeReason.EmpoweredReady, _hitStopScale);
        else
            ChangeTimeRequest(READY_TIME_OWNER, CombatTimeReason.EmpoweredReady, _worldTimeScale);
        _pulse.Begin();
        Publish(PlayerEmpowerStage.Ready, PlayerEmpowerEndReason.None);
        return true;
    }

    /// <summary>
    /// 준비 중이면 강화 공격을 시작해 Started를 발행하고 true를 반환한다. 준비 중이 아니거나 사망·일시정지이면 false다.
    /// 진행 중인 가드·공격·반격 예약을 끝내고 연출을 재생하며, 시간 요청은 강화 공격용으로 바꿔 연출이 끝날 때까지 불릿 타임을 유지한다.
    /// </summary>
    public bool TryBeginStrike()
    {
        if (!IsReady || _player.IsPaused || _player.CurrentState == PlayerActionState.Dead) return false;

        _stage = PlayerEmpowerStage.Started;
        _hitStopRemaining = 0f;
        ChangeTimeRequest(STRIKE_TIME_OWNER, CombatTimeReason.EmpoweredAttack, _worldTimeScale);
        _combat.CancelForEmpowerStrike();
        _player.CancelActionForEmpowerStrike();
        Publish(PlayerEmpowerStage.Started, PlayerEmpowerEndReason.None);
        _strike.Play(HandleStrikeFinished);
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
        _stage = PlayerEmpowerStage.Ended;
        _hitStopRemaining = 0f;
        ReleaseTimeRequest();
        Publish(PlayerEmpowerStage.Ended, reason);
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
