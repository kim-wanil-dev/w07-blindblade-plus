using UnityEngine;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;


/// <summary>플레이어 피해의 회피·방어 결과를 확정하고 회피 시작 지점의 잔상 접촉 보상을 처리한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KatanaCombatCoordinator))]
public sealed class KatanaDamageReceiver : PlayerHealth, IPlayerDefenseResolver
{
    private const string TWIN_HUMAN_IAI_PATTERN = "TwinHumanIai";
    private const float DODGE_THREAT_RADIUS = 2f;

    [Header("Katana connection")]
    [SerializeField] private KatanaCombatCoordinator _combat;
    [SerializeField] private Transform _view;
    [SerializeField, Range(1f, 180f)] private float _frontArc = 120f;
    [System.NonSerialized] private PlayerMotor _motor;
    [System.NonSerialized] private PlayerEmpower _empower;
    [System.NonSerialized] private PlayerClock _playerClock;
    [System.NonSerialized] private PlayerPerfectPulse _perfectPulse;
    [System.NonSerialized] private PlayerSwordWaveLauncher _swordWave;
    [System.NonSerialized] private CharacterController _controller;

    [Header("회피 판정")]
    [SerializeField, Min(0f), InspectorName("극한 회피 시작 (초)")]
    [Tooltip("회피 시작부터 잽니다. 회피 무적 전체 구간 안에 닿은 공격은 극한 회피이며 보상은 회피 1회에 한 번입니다.")]
    private float _perfectDodgeStart = 0f;
    [SerializeField, Min(0f), InspectorName("극한 회피 끝 (초)")]
    private float _perfectDodgeEnd = 0.18f;
    [SerializeField, Min(0f), InspectorName("회피 무적 끝 (초)")]
    [Tooltip("회피 시작부터 잽니다. 극한 회피 구간 밖에서 이 시각 전에 닿은 공격은 피해만 없고 보상이 없습니다.")]
    private float _dodgeInvulnerabilityEnd = 0.18f;
    [SerializeField, Range(0f, 2f), InspectorName("잔상 인정 최소 이동 / 몸 반지름")]
    [Tooltip("잔상에 공격이 닿을 때 회피 시작 위치에서 이 거리만큼 벗어나야 회피 후보로 인정합니다.")]
    private float _afterimageMinimumMoveRatio = 0.5f;
    [SerializeField, Min(0f), InspectorName("회피 잔상 수명 (초)")]
    [Tooltip("회피 시작부터 플레이어 시계 기준으로 잔상 감지를 유지하는 시간입니다. 기본값은 회피 무적 시간과 같습니다.")]
    private float _afterimageLifetime = 0.18f;
    private int _dodgeId;
    private bool _dodgeActive;
    private bool _perfectDodgeRewarded;
    private bool _iaiDodgeFeedbackGranted;
    private bool _normalDodgeVignetteShown;
    private DodgeAfterimage _afterimage;
    private DamageContext _afterimageContact;
    private bool _afterimageContacted;
    private bool _dodgeTookDamage;
    private Vector3 _afterimageOrigin;

    public bool IsDodgeInvulnerable => TryGetDodgeOutcome(out _);
    public bool IsDodgeAvoiding => !_dodgeTookDamage && !_empower.IsBulletTimeActive
        && TryGetDodgeOutcome(out _);

    [Header("파동 요청")]
    private int _pulseId;

    protected override void Awake()
    {
        base.Awake();
        _motor = GetComponent<PlayerMotor>();
        _empower = GetComponent<PlayerEmpower>();
        _playerClock = GetComponent<PlayerClock>();
        _perfectPulse = GetComponent<PlayerPerfectPulse>();
        _swordWave = GetComponent<PlayerSwordWaveLauncher>();
        _controller = GetComponent<CharacterController>();
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
        ClearAfterimage();
        _dodgeActive = false;
    }

    void Update()
    {
        if (_afterimage != null && !IsAlive)
            ClearAfterimage();
        else if (_afterimage != null && _playerClock.Now - _motor.DodgeStartedAt >= _afterimageLifetime)
            RemoveAfterimage();
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
    /// 불릿 타임 중에는 보상 없는 Invulnerable로 확정한다. 그 외 회피 판정 구간이면 가드보다 먼저 DodgeAvoided 또는 Invulnerable로 확정한다. 쌍둥이 보스 거합은 피해 면제·불릿 타임·감각 피드백만 적용한다.
    /// 반격 무적·강화 공격·퍼펙트 패링 직후 무적·거합 받아치기 무적·검기 예약부터 발사(돌진 포함) 중이면 보상 없는 Invulnerable로 확정한다.
    /// 그 외에는 Kind가 Projectile이면 진입 방향과 반격 없음 규칙을, 아니면 기존 전방 근접·보스 공격 규칙을 사용하며
    /// 가드가 열린 뒤 지난 시간으로 PerfectParry·Guard를 나눈다.
    /// 사망·일시정지·시간 정지에서는 Ignored를 반환한다. 모든 결과는 반환 전에 DefenseResolved 사건으로 한 번 발행한다.
    /// </summary>
    public DefenseResolution ResolveIncomingAttack(DamageContext context)
    {
        if (!isActiveAndEnabled || context.Amount <= 0f || _combat.Player.IsPaused
            || Time.timeScale <= 0f || _combat.Player.CurrentState == PlayerActionState.Dead)
            return PublishResolution(context, DefenseOutcome.Ignored, 0f);

        // 불릿 타임 중 접촉은 회피·가드 보상 없이 피해만 막는다.
        if (_empower.IsBulletTimeActive)
            return PublishResolution(context, DefenseOutcome.Invulnerable, 0f);

        // 회피 무적은 가드·패링보다 먼저 판정한다. 접촉은 계속 이 경로로 들어오고 피해만 막는다.
        if (TryGetDodgeOutcome(out DefenseOutcome dodgeOutcome))
        {
            if (context.PatternId == TWIN_HUMAN_IAI_PATTERN)
            {
                if (dodgeOutcome == DefenseOutcome.DodgeAvoided)
                {
                    DefenseResolution resolution = PublishResolution(context, DefenseOutcome.Invulnerable, 0f);
                    AwardIaiDodgeFeedback(context.AttackId);
                    return resolution;
                }
                return ResolveDodge(context, DefenseOutcome.Invulnerable);
            }
            return ResolveDodge(context, dodgeOutcome);
        }
        if (_combat.IsCounterInvulnerable || _empower.IsStriking || _empower.IsPerfectParryInvulnerable
            || _empower.IsCounterFocusInvulnerable || _swordWave.IsAttackInvulnerable)
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
        JObject awarenessSnapshot = BlindBlade.Telemetry.CombatTelemetrySignals.CaptureAwarenessSnapshot(transform.position);
        base.TakeDamage(context);
        float appliedDamage = healthBefore - CurrentHealth;
        if (_dodgeActive && appliedDamage > 0f) _dodgeTookDamage = true;
        return PublishResolution(context, DefenseOutcome.Hit, appliedDamage,
            appliedDamage > 0f ? awarenessSnapshot : null);
    }

    /// <summary>
    /// context의 투사체 진입 방향으로 전방 가드·패링을 검사하고 방어 성공 여부를 반환한다.
    /// parried에 퍼펙트 패링 여부를 출력하며 기존 방어 효과를 적용한다. allowCounter가 false이면 반격 기회를 만들지 않는다.
    /// 회피 판정 구간이나 불릿 타임·반격 무적·강화 공격·퍼펙트 패링 직후 무적·거합 받아치기 무적·검기 공격 무적 중이면 막지 않고 false를 반환해 호출자의 TakeDamage가 무적 결과로 처리하게 한다.
    /// 방어에 성공한 경우에만 DefenseResolved를 발행하고, 실패는 호출자의 TakeDamage가 발행한다.
    /// </summary>
    public bool TryDefendProjectile(DamageContext context, out bool parried, bool allowCounter = true)
    {
        parried = false;
        if (!isActiveAndEnabled || TryGetDodgeOutcome(out _) || _empower.IsBulletTimeActive
            || _combat.IsCounterInvulnerable
            || _empower.IsStriking || _empower.IsPerfectParryInvulnerable || _empower.IsCounterFocusInvulnerable
            || _swordWave.IsAttackInvulnerable) return false;
        bool eligible = IsFrontProjectile(context);
        DefenseOutcome guardOutcome = _combat.GuardContactOutcome;
        if (!_combat.TryDefendIncoming(context, eligible, allowCounter)) return false;
        parried = guardOutcome != DefenseOutcome.Guard;
        PublishGuardResolution(context, guardOutcome);
        return true;
    }

    /// <summary>
    /// context의 PatternId에 맞는 활성 근접 베기·돌진·DefenseBeast 도약이 전방 각도 안인지 반환한다.
    /// DefenseBeast는 저장한 도약 진입 방향을 사용하고, 나머지는 공격자 위치를 사용한다.
    /// </summary>
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
        if (!(matching is EnemyMeleeAttack || matching is EnemyChargeAttack || matching is DefenseBeastLeapAttack)
            || matching.Phase != EnemyAttackPhase.Active) return false;
        Vector3 toEnemy = matching is DefenseBeastLeapAttack beastLeap
            ? Vector3.ProjectOnPlane(beastLeap.IncomingDirection, Vector3.up)
            : Vector3.ProjectOnPlane(context.Instigator.transform.position - transform.position, Vector3.up);
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
    /// 이번 회피 중 체력이 줄었다면 이후 접촉은 극한 회피 보상 없이 Invulnerable로 출력한다.
    /// 회피 판정 구간 안이면 true, 회피가 없거나 구간이 끝났으면 false를 반환한다. 회피 이동이 일찍 끝나도 구간은 시간으로만 판단한다.
    /// </summary>
    private bool TryGetDodgeOutcome(out DefenseOutcome outcome)
    {
        outcome = DefenseOutcome.Hit;
        if (!_dodgeActive) return false;
        float elapsed = _playerClock.Now - _motor.DodgeStartedAt;
        if (elapsed >= _perfectDodgeStart && elapsed < _perfectDodgeEnd)
            outcome = _dodgeTookDamage ? DefenseOutcome.Invulnerable : DefenseOutcome.DodgeAvoided;
        else if (elapsed < _dodgeInvulnerabilityEnd) outcome = DefenseOutcome.Invulnerable;
        return outcome != DefenseOutcome.Hit;
    }

    /// <summary>
    /// context를 dodgeOutcome으로 확정해 피해 없이 DefenseResolved를 발행하고 결과를 반환한다.
    /// DodgeAvoided이면 현재 회피에서 처음일 때만 PerfectDodge 사건을 한 번 발행하고 _perfectDodgeRewarded를 참으로 바꾼다.
    /// 이때 극한 회피 비네트와 파동을 내며, 퍼펙트 파동이 진행 중이라 파동을 내지 않았으면 파동 요청 사건도 발행하지 않는다.
    /// Invulnerable이면 피격 이력이 없고 극한 회피 보상과 일반 회피 비네트가 아직 없을 때만 일반 회피 비네트를 한 번 낸다.
    /// </summary>
    private DefenseResolution ResolveDodge(DamageContext context, DefenseOutcome dodgeOutcome)
    {
        DefenseResolution resolution = PublishResolution(context, dodgeOutcome, 0f);
        if (dodgeOutcome != DefenseOutcome.DodgeAvoided)
        {
            if (!_dodgeTookDamage && !_perfectDodgeRewarded && !_normalDodgeVignetteShown)
            {
                _normalDodgeVignetteShown = true;
                _perfectPulse.PlayDodgeVignette(false);
            }
            return resolution;
        }
        if (_perfectDodgeRewarded) return resolution;
        AwardPerfectDodge(context.AttackId, transform.position, false);
        return resolution;
    }

    /// <summary>context가 회피 중 원점 잔상에 닿은 피해 공격이면 이번 DodgeId의 보상 후보로 기록한다. 쌍둥이 보스 거합은 제외한다.</summary>
    public void RecordAfterimageContact(DamageContext context)
    {
        if (_afterimage == null || !_dodgeActive || _perfectDodgeRewarded
            || _afterimageContacted
            || _dodgeTookDamage || _playerClock.Now - _motor.DodgeStartedAt >= _afterimageLifetime
            || context.Amount <= 0f || context.PatternId == TWIN_HUMAN_IAI_PATTERN
            || !IsAlive || Time.timeScale <= 0f) return;
        float displacement = Vector3.ProjectOnPlane(transform.position - _afterimage.Origin, Vector3.up).magnitude;
        if (displacement < _controller.radius * _afterimageMinimumMoveRatio) return;
        _afterimageContact = context;
        _afterimageContacted = true;
    }

    /// <summary>잔상 후보가 있고 회피 중 체력 손실이 없으면 원점에서 극한 회피 보상을 확정한 뒤 잔상을 제거한다.</summary>
    private void SettleAfterimage()
    {
        if (_afterimageContacted && !_dodgeTookDamage && IsAlive && !_perfectDodgeRewarded)
            AwardPerfectDodge(_afterimageContact.AttackId, _afterimageOrigin, true);
        ClearAfterimage();
    }

    /// <summary>잔상 캡슐만 제거하고 이미 기록한 접촉 후보는 회피 종료까지 유지한다.</summary>
    private void RemoveAfterimage()
    {
        if (_afterimage != null)
        {
            _afterimage.gameObject.SetActive(false);
            Destroy(_afterimage.gameObject);
        }
        _afterimage = null;
    }

    /// <summary>잔상 캡슐을 제거하고 이번 회피의 접촉 후보도 지운다.</summary>
    private void ClearAfterimage()
    {
        RemoveAfterimage();
        _afterimageContacted = false;
    }

    /// <summary>attackId의 회피 보상을 DodgeId에 한 번만 발행하고 거합 토큰과 불릿 타임을 부여한다. afterimage면 origin에서 파동을 시작한다.</summary>
    private void AwardPerfectDodge(int attackId, Vector3 origin, bool afterimage)
    {
        if (_perfectDodgeRewarded) return;
        _perfectDodgeRewarded = true;
        _empower.NotifyPerfectDodge(attackId);
        PublishDodge(PlayerDodgeStage.PerfectDodge, attackId);
        _perfectPulse.PlayDodgeVignette(true);
        bool pulsed = afterimage ? _perfectPulse.BeginPerfectDodgeAt(origin) : _perfectPulse.BeginPerfectDodge();
        if (pulsed) RequestRevealPulse(PlayerRevealPulseCause.PerfectDodge, attackId, origin);
    }

    /// <summary>쌍둥이 거합 attackId의 극한 회피 구간 접촉에 토큰·스태미너 환불 없이 불릿 타임과 회피 감각 피드백을 DodgeId당 한 번 부여한다.</summary>
    private void AwardIaiDodgeFeedback(int attackId)
    {
        if (_iaiDodgeFeedbackGranted) return;
        _iaiDodgeFeedbackGranted = true;
        _empower.BeginDodgeBulletTime(attackId);
        PublishDodge(PlayerDodgeStage.IaiDodge, attackId);
        _perfectPulse.PlayDodgeVignette(true);
        if (_perfectPulse.BeginPerfectDodge())
            RequestRevealPulse(PlayerRevealPulseCause.PerfectDodge, attackId);
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
        _iaiDodgeFeedbackGranted = false;
        _normalDodgeVignetteShown = false;
        _dodgeTookDamage = false;
        _afterimageContacted = false;
        _afterimageOrigin = transform.position;
        _afterimage = DodgeAfterimage.Create(this, _controller);
        PublishDodge(PlayerDodgeStage.Started, 0);
        if (!_empower.IsBulletTimeActive && TryGetNearbyThreat(out int attackId))
            AwardPerfectDodge(attackId, transform.position, false);
    }

    /// <summary>회피 시작 위치의 반경 2m·플레이어 높이 원통과 실제 활성 피해 판정이 겹치면 attackId를 출력하고 참을 반환한다.</summary>
    private bool TryGetNearbyThreat(out int attackId)
    {
        attackId = 0;
        Vector3 center = transform.TransformPoint(_controller.center);
        DodgeThreatCylinder cylinder = new DodgeThreatCylinder(center, _controller.height, DODGE_THREAT_RADIUS);
        foreach (EnemyMeleeAttack attack in FindObjectsByType<EnemyMeleeAttack>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (EnemyChargeHitbox attack in FindObjectsByType<EnemyChargeHitbox>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (DefenseBeastLeapAttack attack in FindObjectsByType<DefenseBeastLeapAttack>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (DefenseShuriken projectile in FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None))
            if (projectile.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
            if (projectile.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (TwinBossProjectile projectile in FindObjectsByType<TwinBossProjectile>(FindObjectsSortMode.None))
            if (projectile.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (BeastBossAttack attack in FindObjectsByType<BeastBossAttack>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (HumanBossAttack attack in FindObjectsByType<HumanBossAttack>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        foreach (BossController attack in FindObjectsByType<BossController>(FindObjectsSortMode.None))
            if (attack.TryGetDodgeThreat(cylinder, out attackId)) return true;
        return false;
    }

    /// <summary>현재 회피의 판정 구간을 끝내고 _dodgeActive를 거짓으로 바꾼 뒤 Ended 사건을 발행한다.</summary>
    private void EndDodge()
    {
        if (_afterimage != null || _afterimageContacted) SettleAfterimage();
        _dodgeActive = false;
        PublishDodge(PlayerDodgeStage.Ended, 0);
    }

    /// <summary>현재 DodgeId와 stage, attackId, 플레이어 위치, 게임 시간·실시간으로 DodgeChanged 사건을 발행한다.</summary>
    private void PublishDodge(PlayerDodgeStage stage, int attackId)
    {
        _combat.Events.RaiseDodgeChanged(new PlayerDodgeEvent(_dodgeId, stage, attackId, transform.position,
            Time.time, Time.realtimeSinceStartupAsDouble));
    }

    /// <summary>_pulseId를 하나 올려 cause·attackId의 파동 요청을 center 또는 현재 플레이어 위치 중심으로 발행한다.</summary>
    private void RequestRevealPulse(PlayerRevealPulseCause cause, int attackId, Vector3? center = null)
    {
        _combat.Events.RaiseRevealPulseRequested(new PlayerRevealPulseEvent(++_pulseId, cause, attackId,
            center ?? transform.position, Time.time, Time.realtimeSinceStartupAsDouble));
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
    private DefenseResolution PublishResolution(DamageContext context, DefenseOutcome outcome, float appliedDamage,
        JObject awarenessSnapshot = null)
    {
        bool defended = outcome == DefenseOutcome.Guard || outcome == DefenseOutcome.NormalParry
            || outcome == DefenseOutcome.PerfectParry;
        DefenseResolution resolution = new DefenseResolution(outcome, context.AttackId, context.HitId,
            defended ? _combat.GuardInputId : 0, appliedDamage, context.Point,
            Time.time, Time.realtimeSinceStartupAsDouble);
        _combat.Events.RaiseDefenseResolved(resolution);
        BlindBlade.Telemetry.CombatTelemetrySignals.Contact(context, resolution,
            _dodgeActive && !_dodgeTookDamage && !_empower.IsBulletTimeActive
                && (outcome == DefenseOutcome.DodgeAvoided ||
                (outcome == DefenseOutcome.Invulnerable && TryGetDodgeOutcome(out _))), awarenessSnapshot);
        return resolution;
    }

    /// <summary>context의 방향 제한 없는 기본 가드는 사용하지 않는다. 전방 근접·보스 돌진 방어는 Coordinator에서 확정한다.</summary>
    protected override bool CanBlockDamage(DamageContext context)
    {
        return false;
    }
}

/// <summary>회피 입력 순간 플레이어 몸 높이와 수평 반경 2m로 정의한 원통 판정이다.</summary>
public readonly struct DodgeThreatCylinder
{
    private readonly Vector3 _center;
    private readonly float _bottom;
    private readonly float _top;
    private readonly float _radius;

    /// <summary>center와 height, radius를 받아 원통의 위아래 및 수평 반경을 저장한다.</summary>
    public DodgeThreatCylinder(Vector3 center, float height, float radius)
    {
        _center = center;
        _bottom = center.y - height * 0.5f;
        _top = center.y + height * 0.5f;
        _radius = radius;
    }

    /// <summary>center와 attackRadius의 피해 구가 원통에 닿는지 수평·수직 거리를 함께 계산해 반환한다.</summary>
    public bool OverlapsSphere(Vector3 center, float attackRadius)
    {
        float dx = center.x - _center.x;
        float dz = center.z - _center.z;
        float horizontalGap = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - _radius);
        float verticalGap = Mathf.Max(0f, _bottom - center.y, center.y - _top);
        return horizontalGap * horizontalGap + verticalGap * verticalGap <= attackRadius * attackRadius;
    }

    /// <summary>start부터 end까지의 공격 캡슐이 원통과 겹치면 참을 반환한다. 선분 위 최근접점을 찾아 높이와 수평 거리를 함께 검사한다.</summary>
    public bool OverlapsCapsule(Vector3 start, Vector3 end, float attackRadius)
    {
        float left = 0f;
        float right = 1f;
        for (int index = 0; index < 16; index++)
        {
            float first = (left * 2f + right) / 3f;
            float second = (left + right * 2f) / 3f;
            if (DistanceSquaredToCylinder(Vector3.Lerp(start, end, first))
                < DistanceSquaredToCylinder(Vector3.Lerp(start, end, second))) right = second;
            else left = first;
        }
        return Mathf.Min(DistanceSquaredToCylinder(start), DistanceSquaredToCylinder(end),
            DistanceSquaredToCylinder(Vector3.Lerp(start, end, (left + right) * 0.5f)))
            <= attackRadius * attackRadius;
    }

    /// <summary>point에서 원통 내부까지의 최단 거리 제곱을 반환한다.</summary>
    private float DistanceSquaredToCylinder(Vector3 point)
    {
        float dx = point.x - _center.x;
        float dz = point.z - _center.z;
        float horizontalGap = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - _radius);
        float verticalGap = Mathf.Max(0f, _bottom - point.y, point.y - _top);
        return horizontalGap * horizontalGap + verticalGap * verticalGap;
    }
}
