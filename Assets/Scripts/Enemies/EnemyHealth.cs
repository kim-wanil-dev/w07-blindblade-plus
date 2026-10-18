using System;
using System.Collections.Generic;

using UnityEngine;


[DisallowMultipleComponent]
public sealed class EnemyHealth : MonoBehaviour, IDamageable
{
    private static readonly Dictionary<string, EnemyHealth> _targets = new Dictionary<string, EnemyHealth>();
    private static readonly Dictionary<string, int> _spawnGenerations = new Dictionary<string, int>();

    [Header("적 체력")]
    [SerializeField, Min(1f)] private float _maxHealth = 10f;
    [SerializeField, Tooltip("켜면 양수 피해를 주는 유효한 공격 한 번에 사망합니다.")]
    private bool _dieOnAnyHit;
    [SerializeField] private Collider _hitCollider;
    [SerializeField] private bool _cancelAttackOnHit;
    [SerializeField, Tooltip("끄면 플레이어의 대상 돌진 공격 후보에서 제외합니다. 다른 피해 판정은 유지합니다.")]
    private bool _dashTargetable = true;
    [SerializeField, Tooltip("체력을 함께 쓰는 상대 적. 한쪽이 피해를 입으면 상대 체력도 같은 값이 되고 0이 되면 함께 쓰러진다. 비우면 혼자 쓴다.")]
    private EnemyHealth _sharedHealth;
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

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;
    public bool IsAlive => isActiveAndEnabled && _currentHealth > 0f;
    public bool IsInvulnerable => _invulnerable;
    public bool IsDashTargetable => _dashTargetable;
    /// <summary>_sharedHealth와 자신 중 인스턴스 ID가 작은 쪽을 반환해 같은 체력 묶음의 타격 기록을 하나로 합친다.</summary>
    public EnemyHealth SharedDamageKey => _sharedHealth != null && _sharedHealth.GetInstanceID() < GetInstanceID()
        ? _sharedHealth : this;
    public string TargetId => "Enemy_" + GetInstanceID();
    public int SpawnGeneration => _spawnGeneration;
    public event Action<DamageContext> Damaged;
    public event Action<DamageContext> DamageBlocked;
    public event Action<float, float> HealthChanged;
    public event Action<EnemyHealth> Died;

    void Awake()
    {
        _attackSelector = GetComponent<EnemyAttackSelector>();
        _motor = GetComponent<EnemyMotor>();
    }

    void OnEnable()
    {
        _currentHealth = _maxHealth;
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
        if (!IsAlive || !(context.Amount > 0f))
            return;

        if (_invulnerable)
        {
            DamageBlocked?.Invoke(context);
            return;
        }
        Debug.Log($"Take Damage {context.Amount} ");
        _currentHealth = _dieOnAnyHit ? 0f : Mathf.Max(0f, _currentHealth - context.Amount);
        if (_currentHealth > 0f && _cancelAttackOnHit) ApplyHitReaction(context);
        PlayDamageEffects(context);
        Damaged?.Invoke(context);
        HealthChanged?.Invoke(_currentHealth, _maxHealth);
        if (_sharedHealth != null) _sharedHealth.SyncSharedHealth(_currentHealth);
        if (_currentHealth <= 0f)
        {
            Died?.Invoke(this);
            gameObject.SetActive(false);
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
            effects.Play(EffectId.EnemyDeath, _hitCollider.bounds.center);
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
        Game.Instance.EffectManager.Play(EffectId.EnemyDeath, _hitCollider.bounds.center);
        Died?.Invoke(this);
        gameObject.SetActive(false);
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
    /// 지정된 Collider와 현재 체력 상태로 플레이어 계약의 전투 대상 정보를 반환한다.
    /// 식별자는 인스턴스마다 고유하며 반경은 Collider의 수평 크기를 사용한다.
    /// </summary>
    public PlayerCombatTarget CreateCombatTarget()
    {
        Vector3 extents = _hitCollider.bounds.extents;
        return new PlayerCombatTarget(TargetId, transform.position, _hitCollider,
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
