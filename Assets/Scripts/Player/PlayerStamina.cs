using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 회피 비용·가드 비용·극한 회피 환불·자연 회복을 관리하는 플레이어 스태미너 원본이다.
/// 회피 시작과 방어 판정은 KatanaCombatCoordinator의 전투 사건으로 받고, HUD에는 IPlayerStaminaReadModel로 값을 공개한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KatanaCombatCoordinator))]
public sealed class PlayerStamina : MonoBehaviour, IPlayerStaminaReadModel
{
    [Header("용량")]
    [SerializeField, Min(1f)] private float _maxStamina = 100f;

    [Header("소모")]
    [SerializeField, Min(0f), Tooltip("회피가 실제로 시작될 때 쓰는 양. 남은 양이 이보다 적으면 회피를 거부한다.")]
    private float _dodgeCost = 34f;
    [SerializeField, Min(0f), Tooltip("공격을 가드로 막았을 때 쓰는 양. 패링은 쓰지 않으며 남은 양보다 크면 0까지만 줄인다.")]
    private float _guardCost = 0f;

    [Header("회복")]
    [SerializeField, Min(0f)] private float _regenPerSecond = 20f;
    [SerializeField, Min(0f), Tooltip("소모하거나 피격된 뒤 회복을 다시 시작할 때까지의 게임 시간.")]
    private float _regenDelay = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("가드 중 회복 속도 배율.")]
    private float _guardRegenMultiplier = 0.5f;

    [Header("상태")]
    private KatanaCombatCoordinator _combat;
    private PlayerController _player;
    private float _current;
    private float _regenResumeTime;
    private float _pendingRegen;
    private int _paidDodgeId;
    private float _paidDodgeCost;

    public StaminaSnapshot Current => new StaminaSnapshot(_current, _maxStamina);

    public event Action<StaminaSnapshot> Changed;

    void Awake()
    {
        _combat = GetComponent<KatanaCombatCoordinator>();
        _player = GetComponent<PlayerController>();
        _current = _maxStamina;
    }

    void OnEnable()
    {
        _combat.Events.DodgeChanged += HandleDodgeChanged;
        _combat.Events.DefenseResolved += HandleDefenseResolved;
    }

    void OnDisable()
    {
        _combat.Events.DodgeChanged -= HandleDodgeChanged;
        _combat.Events.DefenseResolved -= HandleDefenseResolved;
    }

    void Start()
    {
        Publish(0f, PlayerStaminaChangeReason.Reset, 0);
    }

    void Update()
    {
        if (_current >= _maxStamina || Time.time < _regenResumeTime) return;

        float rate = _player.IsGuarding ? _regenPerSecond * _guardRegenMultiplier : _regenPerSecond;
        float amount = Mathf.Min(rate * Time.deltaTime, _maxStamina - _current);
        if (amount <= 0f) return;
        _current += amount;
        _pendingRegen += amount;
        Changed?.Invoke(new StaminaSnapshot(_current, _maxStamina));
        // 회복은 매 프레임 사건을 만들지 않고 가득 찼을 때 한 번에 기록한다.
        if (_current >= _maxStamina) FlushRegen();
    }

    /// <summary>
    /// 현재 스태미너로 회피 비용을 낼 수 있는지 반환한다. 값은 바꾸지 않는다.
    /// PlayerController가 회피 요청을 받기 전에 사용한다.
    /// </summary>
    public bool CanAffordDodge()
    {
        return _current >= _dodgeCost;
    }

    /// <summary>
    /// 회피 사건 dodgeEvent를 받아 Started이면 회피 비용을 쓰고, PerfectDodge이면 그 회피에서 쓴 비용을 돌려준다.
    /// 환불은 비용을 낸 DodgeId와 같을 때 한 번만 하며 _paidDodgeId와 _paidDodgeCost를 갱신한다.
    /// </summary>
    private void HandleDodgeChanged(PlayerDodgeEvent dodgeEvent)
    {
        if (dodgeEvent.Stage == PlayerDodgeStage.Started)
        {
            _paidDodgeId = dodgeEvent.DodgeId;
            _paidDodgeCost = Consume(_dodgeCost, PlayerStaminaChangeReason.DodgeCost, dodgeEvent.DodgeId);
        }
        else if (dodgeEvent.Stage == PlayerDodgeStage.PerfectDodge && dodgeEvent.DodgeId == _paidDodgeId
            && _paidDodgeCost > 0f)
        {
            float refund = Mathf.Min(_paidDodgeCost, _maxStamina - _current);
            _paidDodgeCost = 0f;
            FlushRegen();
            _current += refund;
            Publish(refund, PlayerStaminaChangeReason.PerfectDodgeRefund, dodgeEvent.DodgeId);
        }
    }

    /// <summary>
    /// 방어 판정 resolution을 받아 Guard이면 가드 비용을 쓰고, Hit이면 값은 두고 회복 대기 시간만 처음부터 다시 센다.
    /// 패링·회피·무적 결과는 스태미너를 바꾸지 않는다.
    /// </summary>
    private void HandleDefenseResolved(DefenseResolution resolution)
    {
        if (resolution.Outcome == DefenseOutcome.Guard)
            Consume(_guardCost, PlayerStaminaChangeReason.GuardCost, 0);
        else if (resolution.Outcome == DefenseOutcome.Hit)
        {
            FlushRegen();
            _regenResumeTime = Time.time + _regenDelay;
        }
    }

    /// <summary>
    /// cost만큼 스태미너를 0 아래로 내려가지 않게 줄이고 reason과 dodgeId로 사건을 발행한다.
    /// 실제로 줄어든 양을 반환하며 0보다 크면 회복 대기 시간을 처음부터 다시 센다.
    /// </summary>
    private float Consume(float cost, PlayerStaminaChangeReason reason, int dodgeId)
    {
        float amount = Mathf.Min(cost, _current);
        if (amount <= 0f) return 0f;
        FlushRegen();
        _current -= amount;
        _regenResumeTime = Time.time + _regenDelay;
        Publish(-amount, reason, dodgeId);
        return amount;
    }

    /// <summary>쌓인 회복량 _pendingRegen이 있으면 Regen 사건 하나로 발행하고 0으로 비운다. 현재 값은 이미 반영돼 있다.</summary>
    private void FlushRegen()
    {
        if (_pendingRegen <= 0f) return;
        float amount = _pendingRegen;
        _pendingRegen = 0f;
        _combat.Events.RaiseStaminaChanged(new PlayerStaminaEvent(_current, _maxStamina, amount,
            PlayerStaminaChangeReason.Regen, 0, Time.time, Time.realtimeSinceStartupAsDouble));
    }

    /// <summary>
    /// 현재 값과 변화량 delta, reason, dodgeId로 StaminaChanged 사건을 발행하고 HUD용 Changed도 알린다.
    /// 상태는 호출 전에 바뀌어 있어야 한다.
    /// </summary>
    private void Publish(float delta, PlayerStaminaChangeReason reason, int dodgeId)
    {
        _combat.Events.RaiseStaminaChanged(new PlayerStaminaEvent(_current, _maxStamina, delta, reason, dodgeId,
            Time.time, Time.realtimeSinceStartupAsDouble));
        Changed?.Invoke(new StaminaSnapshot(_current, _maxStamina));
    }
}
