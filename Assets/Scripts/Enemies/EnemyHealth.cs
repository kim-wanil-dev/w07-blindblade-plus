using System;
using System.Collections.Generic;

using UnityEngine;


[DisallowMultipleComponent]
public sealed class EnemyHealth : MonoBehaviour, IDamageable
{
    private const string SWORD_WAVE_DASH_PATTERN_ID = "PlayerSwordWaveDash";
    private const string SWORD_WAVE_PATTERN_ID = "DefenseSwordWave";

    private static readonly Dictionary<string, EnemyHealth> _targets = new Dictionary<string, EnemyHealth>();
    private static readonly Dictionary<string, int> _spawnGenerations = new Dictionary<string, int>();

    [Header("적 체력")]
    [SerializeField, Min(1f)] private float _maxHealth = 10f;
    [SerializeField, Tooltip("켜면 양수 피해를 주는 유효한 공격 한 번에 사망합니다.")]
    private bool _dieOnAnyHit;
    [SerializeField] private EnemyHitCapsule _hitCapsule;
    [SerializeField] private bool _cancelAttackOnHit;
    [SerializeField, Tooltip("끄면 플레이어의 대상 돌진 공격 후보에서 제외합니다. 다른 피해 판정은 유지합니다.")]
    private bool _dashTargetable = true;
    [SerializeField, Tooltip("체력을 함께 쓰는 상대 적. 한쪽이 피해를 입으면 상대 체력도 같은 값이 되고 0이 되면 함께 쓰러진다. 비우면 혼자 쓴다.")]
    private EnemyHealth _sharedHealth;
    [SerializeField, Tooltip("켜면 체력을 함께 쓰는 묶음이 한 번의 범위 공격에 함께 걸렸을 때 이 적이 먼저 피해를 받는다.")]
    private bool _sharedDamagePriority;
    [Header("피격 반응")]
    [SerializeField, Min(0f)] private float _hitRecovery = 0.16f;
    [SerializeField, Min(0f)] private float _hitInterruptCooldown = 0.45f;
    [SerializeField, Min(0f)] private float _hitPushDistance = 0.12f;
    [SerializeField, Min(0.01f)] private float _hitPushDuration = 0.12f;
    private EnemyAttackSelector _attackSelector;
    private EnemyMotor _motor;
    private float _nextHitInterruptTime;
    private float _currentHealth;
    private bool _invulnerable;
    private int _spawnGeneration;
    private int _lastGaugeRewardActionId;
    private int _lastGaugeRewardPlayerId;

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;
    public bool IsAlive => isActiveAndEnabled && _currentHealth > 0f;
    public bool IsInvulnerable => _invulnerable;
    public bool IsDashTargetable => _dashTargetable;
    /// <summary>_sharedHealth와 자신 중 인스턴스 ID가 작은 쪽을 반환해 같은 체력 묶음의 타격 기록을 하나로 합친다.</summary>
    public EnemyHealth SharedDamageKey => _sharedHealth != null && _sharedHealth.GetInstanceID() < GetInstanceID()
        ? _sharedHealth : this;
    public bool HasSharedDamagePriority => _sharedDamagePriority;
    /// <summary>공유 체력 상대가 우선 피격 대상이고 자신은 후순위이면 true를 반환한다.</summary>
    public bool DefersSharedDamage => _sharedHealth != null && _sharedHealth.HasSharedDamagePriority && !_sharedDamagePriority;
    public string TargetId => "Enemy_" + GetInstanceID();
    public int SpawnGeneration => _spawnGeneration;
    public event Action<DamageContext> Damaged;
    public event Action<DamageContext> DamageBlocked;
    public event Action<float, float> HealthChanged;
    public event Action<EnemyHealth> Dying;
    public event Action<EnemyHealth> Died;

    void Awake()
    {
        _attackSelector = GetComponent<EnemyAttackSelector>();
        _motor = GetComponent<EnemyMotor>();
    }

    void OnEnable()
    {
        _currentHealth = _maxHealth;
        _lastGaugeRewardActionId = 0;
        _lastGaugeRewardPlayerId = 0;
        _nextHitInterruptTime = 0f;
        _spawnGenerations.TryGetValue(TargetId, out _spawnGeneration);
        _spawnGeneration++;
        if (_spawnGeneration <= 0)
            _spawnGeneration = 1;
        _spawnGenerations[TargetId] = _spawnGeneration;
        _targets[TargetId] = this;
    }

    void OnDisable()
    {
        _targets.Remove(TargetId);
    }

    /// <summary>
    /// context의 양수 피해를 현재 HP에 반영하고 한 대 사망 설정이면 HP를 즉시 0으로 만든다.
    /// 무적이면 피해 대신 DamageBlocked에 context를 전달하며 HP는 유지한다.
    /// 설정에 따라 공격을 취소하며 HP가 0이면 사망 이벤트 후 적 루트를 비활성화한다.
    /// 체력을 함께 쓰는 상대(_sharedHealth)가 있으면 상대 체력도 같은 값으로 맞춘다.
    /// </summary>
    public void TakeDamage(DamageContext context)
    {
        TryTakeDamage(context);
    }

    /// <summary>
    /// context의 양수 피해를 수락하면 체력과 사망 상태를 갱신하고 true를 반환한다.
    /// 생존하지 않거나 무적이면 피해를 거부하고, 무적 거부는 DamageBlocked에 알린다.
    /// </summary>
    public bool TryTakeDamage(DamageContext context)
    {
        if (!IsAlive || !(context.Amount > 0f))
            return false;

        if (_invulnerable)
        {
            DamageBlocked?.Invoke(context);
            return false;
        }
        Debug.Log($"Take Damage {context.Amount} ");
        _currentHealth = _dieOnAnyHit ? 0f : Mathf.Max(0f, _currentHealth - context.Amount);
        RewardPlayerSkillGauge(context);
        if (_currentHealth > 0f && _cancelAttackOnHit) ApplyHitReaction(context);
        PlayDamageEffects(context);
        Damaged?.Invoke(context);
        HealthChanged?.Invoke(_currentHealth, _maxHealth);
        if (_sharedHealth != null) _sharedHealth.SyncSharedHealth(_currentHealth);
        if (_currentHealth <= 0f)
        {
            Dying?.Invoke(this);
            Died?.Invoke(this);
            gameObject.SetActive(false);
        }
        return true;
    }

    /// <summary>수락된 피해 context의 플레이어에게 보스 적중 또는 일반 적 처치 보상을 지급한다. 일섬과 일반·대시 공격을 구분하고 공유 체력 보상은 한 번만 반영한다.</summary>
    private void RewardPlayerSkillGauge(DamageContext context)
    {
        if (context.Instigator == null) return;
        PlayerSkillGauge gauges = context.Instigator.GetComponentInParent<PlayerSkillGauge>();
        if (gauges == null || !gauges.isActiveAndEnabled) return;
        PlayerHealth playerHealth = gauges.GetComponent<PlayerHealth>();
        if (!playerHealth.IsAlive) return;

        bool normal = context.PatternId == nameof(PlayerAttackKind.Normal)
            || context.PatternId == nameof(PlayerAttackKind.IaidoFinisher);
        bool boss = GetComponentInParent<BossController>() != null
            || GetComponentInParent<TwinBossController>() != null;
        PlayerSwordWaveLauncher wave = gauges.GetComponent<PlayerSwordWaveLauncher>();
        KatanaCombatCoordinator combat = gauges.GetComponent<KatanaCombatCoordinator>();
        bool swordWaveAttack = wave.IsSkillAction(context.AttackId)
            || context.PatternId == SWORD_WAVE_DASH_PATTERN_ID
            || context.PatternId == SWORD_WAVE_PATTERN_ID;
        if (normal && boss && context.AttackId > 0 && !wave.IsSkillAction(context.AttackId)
            && !combat.IsCounterAttack)
        {
            EnemyHealth key = SharedDamageKey;
            int playerId = gauges.GetInstanceID();
            if (key._lastGaugeRewardActionId != context.AttackId || key._lastGaugeRewardPlayerId != playerId)
            {
                key._lastGaugeRewardActionId = context.AttackId;
                key._lastGaugeRewardPlayerId = playerId;
                gauges.RewardBossNormalHit();
            }
        }
        // 공유 체력 동기화에서는 이 메서드를 호출하지 않아 처치 보상을 한 번만 지급한다.
        if (_currentHealth <= 0f && !boss)
        {
            if (swordWaveAttack)
                gauges.RewardUltimateKill();
            else if (normal && !combat.IsCounterAttack)
                gauges.RewardKill();
        }
    }

    /// <summary>
    /// 실제 피해 context의 위치에 혈흔을 재생하고 HP가 0이면 적 중심에 사망 효과를 요청한다.
    /// 공용 EffectManager 풀을 사용하여 적 루트 비활성화 후에도 효과의 수명을 유지한다.
    /// </summary>
    private void PlayDamageEffects(DamageContext context)
    {
        EffectManager effects = Game.Instance.EffectManager;
        effects.Play(EffectId.BloodHit, context.Point);
        if (_currentHealth <= 0f)
            effects.Play(EffectId.EnemyDeath, _hitCapsule.Bounds.center);
    }

    /// <summary>
    /// 체력을 함께 쓰는 상대가 맞아 바뀐 체력 current로 이 적의 체력을 맞추고 HealthChanged를 알린다.
    /// 피격 반응·혈흔·Damaged는 맞은 쪽만 내며, current가 0이면 이 적도 사망 효과와 사망 이벤트 뒤 비활성화된다.
    /// </summary>
    private void SyncSharedHealth(float current)
    {
        if (!IsAlive || Mathf.Approximately(_currentHealth, current)) return;
        _currentHealth = Mathf.Clamp(current, 0f, _maxHealth);
        HealthChanged?.Invoke(_currentHealth, _maxHealth);
        if (_currentHealth > 0f) return;
        Game.Instance.EffectManager.Play(EffectId.EnemyDeath, _hitCapsule.Bounds.center);
        Dying?.Invoke(this);
        Died?.Invoke(this);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 체력을 함께 쓰는 상대가 대신 맞은 범위 공격에 이 적도 걸렸을 때 point에 혈흔만 재생한다.
    /// 살아 있고 무적이 아니며 피격 캡슐 _hitCapsule이 켜져 있을 때만 재생하고 HP·피격 반응·Damaged는 바꾸지 않는다.
    /// </summary>
    public void PlaySharedHitEffect(Vector3 point)
    {
        if (!IsAlive || _invulnerable || !_hitCapsule.IsEnabled) return;
        Game.Instance.EffectManager.Play(EffectId.BloodHit, point);
    }

    /// <summary>invulnerable 값으로 일시적인 피해 수신 차단을 설정한다. 체력과 생존 상태는 유지한다.</summary>
    public void SetInvulnerable(bool invulnerable)
    {
        _invulnerable = invulnerable;
    }

    /// <summary>context의 공격 방향으로 동일한 경직·밀림을 적용한다. 연속 중단을 제한하고 패링 회복 시간은 보존한다.</summary>
    private void ApplyHitReaction(DamageContext context)
    {
        if (_attackSelector != null && !_attackSelector.IsStaggered && Time.time >= _nextHitInterruptTime)
        {
            _attackSelector.Stagger(_hitRecovery);
            _nextHitInterruptTime = Time.time + _attackSelector.StaggerRemaining + _hitInterruptCooldown;
        }
        if (_motor == null) return;
        Vector3 direction = context.Instigator != null
            ? transform.position - context.Instigator.transform.position : context.Direction;
        _motor.ApplyHitPush(direction, _hitPushDistance, _hitPushDuration);
    }

    /// <summary>
    /// 지정된 피격 캡슐과 현재 체력 상태로 플레이어 계약의 전투 대상 정보를 반환한다.
    /// 식별자는 인스턴스마다 고유하며 반경은 피격 캡슐 경계의 수평 크기를 사용한다.
    /// </summary>
    public PlayerCombatTarget CreateCombatTarget()
    {
        Vector3 extents = _hitCapsule.Bounds.extents;
        return new PlayerCombatTarget(TargetId, transform.position, _hitCapsule,
            Mathf.Max(extents.x, extents.z), IsAlive);
    }

    /// <summary>
    /// targetId로 활성 적 체력 레지스트리를 조회하여 생존 적을 health로 반환한다.
    /// 식별자가 없거나 죽은 적이면 false를 반환한다.
    /// </summary>
    public static bool TryFind(string targetId, out EnemyHealth health)
    {
        health = null;
        return !string.IsNullOrEmpty(targetId) && _targets.TryGetValue(targetId, out health) && health.IsAlive;
    }

    /// <summary>등록 목록에서 현재 활성 상태이고 생존한 적을 중복 없이 반환한다.</summary>
    public static List<EnemyHealth> GetActiveTargets()
    {
        List<EnemyHealth> activeTargets = new List<EnemyHealth>(_targets.Count);
        foreach (EnemyHealth target in _targets.Values)
            if (target != null && target.IsAlive)
                activeTargets.Add(target);
        return activeTargets;
    }

    /// <summary>
    /// 플레이 시작 시 입력 없이 이전 활성 적 레지스트리를 비운다.
    /// Domain Reload를 끈 경우에도 이전 실행의 적 참조를 유지하지 않는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        _targets.Clear();
        _spawnGenerations.Clear();
    }
}
