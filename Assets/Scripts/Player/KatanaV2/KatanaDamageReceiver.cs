using UnityEngine;

using BlindBlade.Combat.Contracts;


/// <summary>적의 기존 IDamageable 호출을 받아 피해 전달 전에 회피·반격 무적과 전방 근접 베기·돌진·보스 공격 방어를 확인한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KatanaCombatCoordinator))]
public sealed class KatanaDamageReceiver : PlayerHealth, IPlayerDefenseResolver
{
    [Header("Katana connection")]
    [SerializeField] private KatanaCombatCoordinator _combat;
    [SerializeField] private Transform _view;
    [SerializeField, Range(1f, 180f)] private float _frontArc = 120f;
    [System.NonSerialized] private PlayerMotor _motor;
    [System.NonSerialized] private PlayerEmpower _empower;
    [System.NonSerialized] private PlayerClock _playerClock;
    [System.NonSerialized] private PlayerPerfectPulse _perfectPulse;

    [Header("회피 판정")]
    [SerializeField, Min(0f), InspectorName("극한 회피 시작 (초)")]
    [Tooltip("회피 시작부터 잽니다. 이 구간에 닿은 공격은 극한 회피이며 보상은 회피 1회에 한 번입니다.")]
    private float _perfectDodgeStart = 0f;
    [SerializeField, Min(0f), InspectorName("극한 회피 끝 (초)")]
    private float _perfectDodgeEnd = 0.1f;
    [SerializeField, Min(0f), InspectorName("회피 무적 끝 (초)")]
    [Tooltip("회피 시작부터 잽니다. 극한 회피 구간 밖에서 이 시각 전에 닿은 공격은 피해만 없고 보상이 없습니다.")]
    private float _dodgeInvulnerabilityEnd = 0.18f;
    private int _dodgeId;
    private bool _dodgeActive;
    private bool _perfectDodgeRewarded;
    private bool _normalDodgeVignetteShown;

    [Header("파동 요청")]
    private int _pulseId;

    protected override void Awake()
    {
        base.Awake();
        _motor = GetComponent<PlayerMotor>();
        _empower = GetComponent<PlayerEmpower>();
        _playerClock = GetComponent<PlayerClock>();
        _perfectPulse = GetComponent<PlayerPerfectPulse>();
        if (_combat == null) _combat = GetComponent<KatanaCombatCoordinator>();
        if (_view == null)
        {
            Camera camera = GetComponentInChildren<Camera>();
            _view = camera != null ? camera.transform : transform;
        }
    }

    void OnEnable()
    {
        _motor = GetComponent<PlayerMotor>();
        _motor.DodgeStarted += HandleDodgeStarted;
    }

    void OnDisable()
    {
        _motor.DodgeStarted -= HandleDodgeStarted;
        _dodgeActive = false;
    }

    void Update()
    {
        if (_dodgeActive && _playerClock.Now - _motor.DodgeStartedAt >= Mathf.Max(_perfectDodgeEnd, _dodgeInvulnerabilityEnd))
            EndDodge();
    }

    /// <summary>
    /// 기존 IDamageable 호출을 ResolveIncomingAttack에 전달하고 판정 결과는 사용하지 않는다.
    /// 방어에 실패한 피해만 PlayerHealth가 HP 차감과 사망 및 피해 이벤트로 처리한다.
    /// </summary>
    public override void TakeDamage(DamageContext context)
    {
        ResolveIncomingAttack(context);
    }

    /// <summary>
    /// context의 공격 1회를 회피·가드·패링으로 판정하고 막지 못하면 체력을 줄인 뒤 결과를 반환한다.
    /// 회피 판정 구간이면 가드보다 먼저 DodgeAvoided 또는 Invulnerable로, 반격 무적·강화 공격·퍼펙트 패링 직후 무적 중이면 보상 없는 Invulnerable로 확정한다.
    /// 그 외에는 Kind가 Projectile이면 진입 방향과 반격 없음 규칙을, 아니면 기존 전방 근접·보스 공격 규칙을 사용하며
    /// 가드가 열린 뒤 지난 시간으로 PerfectParry·Guard를 나눈다.
    /// 사망·일시정지·시간 정지에서는 Ignored를 반환한다. 모든 결과는 반환 전에 DefenseResolved 사건으로 한 번 발행한다.
    /// </summary>
    public DefenseResolution ResolveIncomingAttack(DamageContext context)
    {
        if (!isActiveAndEnabled || context.Amount <= 0f || _combat.Player.IsPaused
            || Time.timeScale <= 0f || _combat.Player.CurrentState == PlayerActionState.Dead)
            return PublishResolution(context, DefenseOutcome.Ignored, 0f);

        // 회피 무적은 가드·패링보다 먼저 판정한다. 접촉은 계속 이 경로로 들어오고 피해만 막는다.
        if (TryGetDodgeOutcome(out DefenseOutcome dodgeOutcome))
            return ResolveDodge(context, dodgeOutcome);
        if (_combat.IsCounterInvulnerable || _empower.IsStriking || _empower.IsPerfectParryInvulnerable)
            return PublishResolution(context, DefenseOutcome.Invulnerable, 0f);

        // 투사체는 반사·강화 보상을 투사체 쪽에서 처리하므로 근접 자동 반격을 만들지 않는다.
        bool projectile = context.Kind == IncomingAttackKind.Projectile;
        bool eligible = projectile
            ? IsFrontProjectile(context)
            : IsFrontMelee(context) || IsFrontBossCharge(context) || IsFrontBossRanged(context);
        DefenseOutcome guardOutcome = _combat.GuardContactOutcome;
        if (_combat.TryDefendIncoming(context, eligible, !projectile))
            return PublishGuardResolution(context, guardOutcome);

        float healthBefore = CurrentHealth;
        base.TakeDamage(context);
        return PublishResolution(context, DefenseOutcome.Hit, healthBefore - CurrentHealth);
    }

    /// <summary>
    /// context의 투사체 진입 방향으로 전방 가드·패링을 검사하고 방어 성공 여부를 반환한다.
    /// parried에 퍼펙트 패링 여부를 출력하며 기존 방어 효과를 적용한다. allowCounter가 false이면 반격 기회를 만들지 않는다.
    /// 회피 판정 구간이나 반격 무적·강화 공격·퍼펙트 패링 직후 무적 중이면 막지 않고 false를 반환해 호출자의 TakeDamage가 무적 결과로 처리하게 한다.
    /// 방어에 성공한 경우에만 DefenseResolved를 발행하고, 실패는 호출자의 TakeDamage가 발행한다.
    /// </summary>
    public bool TryDefendProjectile(DamageContext context, out bool parried, bool allowCounter = true)
    {
        parried = false;
        if (!isActiveAndEnabled || TryGetDodgeOutcome(out _) || _combat.IsCounterInvulnerable
            || _empower.IsStriking || _empower.IsPerfectParryInvulnerable) return false;
        bool eligible = IsFrontProjectile(context);
        DefenseOutcome guardOutcome = _combat.GuardContactOutcome;
        if (!_combat.TryDefendIncoming(context, eligible, allowCounter)) return false;
        parried = guardOutcome != DefenseOutcome.Guard;
        PublishGuardResolution(context, guardOutcome);
        return true;
    }

    /// <summary>context의 공격자가 실행 중인 유일한 패턴이 활성 근접 베기 또는 돌진이고 공격자가 전방 각도 안에 있는지 반환한다.</summary>
    private bool IsFrontMelee(DamageContext context)
    {
        if (context.Instigator == null) return false;
        EnemyAttackBehaviour matching = null;
        foreach (EnemyAttackBehaviour pattern in context.Instigator.GetComponents<EnemyAttackBehaviour>())
        {
            if (pattern.PatternId != context.PatternId) continue;
            if (matching != null) return false;
            matching = pattern;
        }
        if (!(matching is EnemyMeleeAttack || matching is EnemyChargeAttack)
            || matching.Phase != EnemyAttackPhase.Active) return false;
        Vector3 toEnemy = Vector3.ProjectOnPlane(context.Instigator.transform.position - transform.position, Vector3.up);
        Vector3 forward = _motor.ViewForward;
        return toEnemy.sqrMagnitude > 0.0001f && forward.sqrMagnitude > 0.0001f
            && Vector3.Angle(forward, toEnemy) <= _frontArc * 0.5f;
    }

    /// <summary>context가 진행 중인 보스 돌진 또는 방어 반격 관통 베기이고 진입 방향이 전방 방어 각도 안인지 반환한다.</summary>
    private bool IsFrontBossCharge(DamageContext context)
    {
        if ((context.PatternId != "BossCharge" && context.PatternId != "BossCounterSlash")
            || context.Instigator == null) return false;
        BossController boss = context.Instigator.GetComponent<BossController>();
        if (boss == null || (boss.State != BossState.Charging && boss.State != BossState.ChainCharge)) return false;
        Vector3 incoming = Vector3.ProjectOnPlane(-context.Direction, Vector3.up);
        return incoming.sqrMagnitude > 0.0001f
            && Vector3.Angle(_motor.ViewForward, incoming) <= _frontArc * 0.5f;
    }

    /// <summary>보스 원거리 투사체의 진입 방향이 전방 방어 각도 안인지 반환한다. 발사 후 보스 상태와 무관하게 검사한다.</summary>
    private bool IsFrontBossRanged(DamageContext context)
    {
        if (context.PatternId != "BossRanged" || context.Instigator == null
            || context.Instigator.GetComponent<BossController>() == null) return false;
        Vector3 incoming = Vector3.ProjectOnPlane(-context.Direction, Vector3.up);
        return incoming.sqrMagnitude > 0.0001f
            && Vector3.Angle(_motor.ViewForward, incoming) <= _frontArc * 0.5f;
    }

    /// <summary>context의 투사체 진입 방향이 시점 기준 전방 방어 각도 안인지 반환한다. 공격자 상태는 검사하지 않는다.</summary>
    private bool IsFrontProjectile(DamageContext context)
    {
        Vector3 incoming = Vector3.ProjectOnPlane(-context.Direction, Vector3.up);
        return incoming.sqrMagnitude > 0.0001f
            && Vector3.Angle(_motor.ViewForward, incoming) <= _frontArc * 0.5f;
    }

    /// <summary>
    /// 현재 회피 시작부터 지난 게임 시간을 극한 회피·무적 구간과 비교해 outcome에 DodgeAvoided 또는 Invulnerable을 출력한다.
    /// 회피 판정 구간 안이면 true, 회피가 없거나 구간이 끝났으면 false를 반환한다. 회피 이동이 일찍 끝나도 구간은 시간으로만 판단한다.
    /// </summary>
    private bool TryGetDodgeOutcome(out DefenseOutcome outcome)
    {
        outcome = DefenseOutcome.Hit;
        if (!_dodgeActive) return false;
        float elapsed = _playerClock.Now - _motor.DodgeStartedAt;
        if (elapsed >= _perfectDodgeStart && elapsed < _perfectDodgeEnd) outcome = DefenseOutcome.DodgeAvoided;
        else if (elapsed < _dodgeInvulnerabilityEnd) outcome = DefenseOutcome.Invulnerable;
        return outcome != DefenseOutcome.Hit;
    }

    /// <summary>
    /// context를 dodgeOutcome으로 확정해 피해 없이 DefenseResolved를 발행하고 결과를 반환한다.
    /// DodgeAvoided이면 현재 회피에서 처음일 때만 PerfectDodge 사건을 한 번 발행하고 _perfectDodgeRewarded를 참으로 바꾼다.
    /// 이때 극한 회피 비네트와 파동을 내며, 퍼펙트 파동이 진행 중이라 파동을 내지 않았으면 파동 요청 사건도 발행하지 않는다.
    /// Invulnerable이면 이 회피에서 극한 회피 보상과 일반 회피 비네트가 아직 없을 때만 일반 회피 비네트를 한 번 내고 _normalDodgeVignetteShown을 참으로 바꾼다.
    /// </summary>
    private DefenseResolution ResolveDodge(DamageContext context, DefenseOutcome dodgeOutcome)
    {
        DefenseResolution resolution = PublishResolution(context, dodgeOutcome, 0f);
        if (dodgeOutcome != DefenseOutcome.DodgeAvoided)
        {
            if (!_perfectDodgeRewarded && !_normalDodgeVignetteShown)
            {
                _normalDodgeVignetteShown = true;
                _perfectPulse.PlayDodgeVignette(false);
            }
            return resolution;
        }
        if (_perfectDodgeRewarded) return resolution;
        _perfectDodgeRewarded = true;
        PublishDodge(PlayerDodgeStage.PerfectDodge, context.AttackId);
        _perfectPulse.PlayDodgeVignette(true);
        if (_perfectPulse.BeginPerfectDodge())
            RequestRevealPulse(PlayerRevealPulseCause.PerfectDodge, context.AttackId);
        return resolution;
    }

    /// <summary>
    /// PlayerMotor의 회피 시작을 받아 새 DodgeId를 발급하고 극한 회피 보상·일반 회피 비네트 여부를 초기화한 뒤 Started 사건을 발행한다.
    /// 이전 회피의 판정 구간이 남아 있으면 그 회피의 Ended를 먼저 발행한다.
    /// </summary>
    private void HandleDodgeStarted()
    {
        if (_dodgeActive) EndDodge();
        _dodgeId++;
        _dodgeActive = true;
        _perfectDodgeRewarded = false;
        _normalDodgeVignetteShown = false;
        PublishDodge(PlayerDodgeStage.Started, 0);
    }

    /// <summary>현재 회피의 판정 구간을 끝내고 _dodgeActive를 거짓으로 바꾼 뒤 Ended 사건을 발행한다.</summary>
    private void EndDodge()
    {
        _dodgeActive = false;
        PublishDodge(PlayerDodgeStage.Ended, 0);
    }

    /// <summary>현재 DodgeId와 stage, attackId, 플레이어 위치, 게임 시간·실시간으로 DodgeChanged 사건을 발행한다.</summary>
    private void PublishDodge(PlayerDodgeStage stage, int attackId)
    {
        _combat.Events.RaiseDodgeChanged(new PlayerDodgeEvent(_dodgeId, stage, attackId, transform.position,
            Time.time, Time.realtimeSinceStartupAsDouble));
    }

    /// <summary>_pulseId를 하나 올려 cause와 attackId의 파동 요청을 현재 플레이어 위치 중심으로 발행한다.</summary>
    private void RequestRevealPulse(PlayerRevealPulseCause cause, int attackId)
    {
        _combat.Events.RaiseRevealPulseRequested(new PlayerRevealPulseEvent(++_pulseId, cause, attackId,
            transform.position, Time.time, Time.realtimeSinceStartupAsDouble));
    }

    /// <summary>
    /// 가드·패링 성공 outcome을 피해 없이 DefenseResolved로 발행하고 결과를 반환한다.
    /// outcome이 PerfectParry이면 강화 준비를 알리고, 새 준비가 시작된 경우에만 파동 요청을 한 번 발행한다.
    /// 이미 준비 중이면 대기 시간만 다시 시작하고 파동은 내지 않는다.
    /// </summary>
    private DefenseResolution PublishGuardResolution(DamageContext context, DefenseOutcome outcome)
    {
        DefenseResolution resolution = PublishResolution(context, outcome, 0f);
        if (outcome == DefenseOutcome.PerfectParry && _empower.NotifyPerfectParry(context.AttackId))
            RequestRevealPulse(PlayerRevealPulseCause.PerfectParry, context.AttackId);
        return resolution;
    }

    /// <summary>
    /// context의 AttackId·HitId·Point와 현재 게임 시간·실시간으로 outcome 판정 결과를 만들고 DefenseResolved로 발행한 뒤 반환한다.
    /// 가드·패링 결과에는 Coordinator의 GuardInputId를 넣으며 appliedDamage는 이번 판정에서 실제로 줄어든 체력이다.
    /// </summary>
    private DefenseResolution PublishResolution(DamageContext context, DefenseOutcome outcome, float appliedDamage)
    {
        bool defended = outcome == DefenseOutcome.Guard || outcome == DefenseOutcome.NormalParry
            || outcome == DefenseOutcome.PerfectParry;
        DefenseResolution resolution = new DefenseResolution(outcome, context.AttackId, context.HitId,
            defended ? _combat.GuardInputId : 0, appliedDamage, context.Point,
            Time.time, Time.realtimeSinceStartupAsDouble);
        _combat.Events.RaiseDefenseResolved(resolution);
        BlindBlade.Telemetry.CombatTelemetrySignals.Contact(context, resolution,
            _dodgeActive && (outcome == DefenseOutcome.DodgeAvoided ||
                (outcome == DefenseOutcome.Invulnerable && TryGetDodgeOutcome(out _))));
        return resolution;
    }

    /// <summary>context의 방향 제한 없는 기본 가드는 사용하지 않는다. 전방 근접·보스 돌진 방어는 Coordinator에서 확정한다.</summary>
    protected override bool CanBlockDamage(DamageContext context)
    {
        return false;
    }
}
