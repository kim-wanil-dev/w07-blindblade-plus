using UnityEngine;

public readonly struct EnemyAttackContext
{
    public EnemyAttackBehaviour Pattern { get; }
    public string PatternId { get; }
    public int AttackId { get; }
    public Transform Source { get; }
    public Transform Target { get; }
    public Vector3 Origin { get; }
    public Vector3 AimPosition { get; }
    public Vector3 Direction { get; }
    public float TelegraphDuration { get; }
    public float ActiveDuration { get; }

    /// <summary>
    /// pattern과 target, 전조 시간을 사용하여 공격 시작 순간의 위치와 조준 방향을 저장한다.
    /// 대상 이동 이후에도 Origin과 AimPosition이 변하지 않는 공격 문맥을 생성한다.
    /// </summary>
    public EnemyAttackContext(EnemyAttackBehaviour pattern, Transform target, float telegraphDuration)
    {
        Pattern = pattern;
        PatternId = pattern.PatternId;
        Source = pattern.transform;
        Target = target;
        Origin = Source.position;
        AimPosition = target.position;
        Direction = (AimPosition - Origin).normalized;
        TelegraphDuration = telegraphDuration;
        ActiveDuration = pattern.ExecutionDuration;
        AttackId = BlindBlade.Telemetry.CombatTelemetrySignals.Begin(PatternId,
            pattern is EnemyRangedAttack ? "Projectile" : pattern is EnemyMeleeAttack ? "Melee" : "Charge", Origin, AimPosition);
    }
}
