using UnityEngine;

using BlindBlade.Combat.Contracts;

public readonly struct DamageContext
{
    public float Amount { get; }
    public GameObject Instigator { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }
    public string PatternId { get; }
    public int AttackId { get; }
    public int HitId { get; }
    public IncomingAttackKind Kind { get; }

    /// <summary>
    /// amount, instigator, point, direction, patternId를 저장하여 피해 수신자에게 전달할 문맥을 만든다.
    /// 공격 식별 정보가 없는 기존 호출용이며 AttackId·HitId는 0, Kind는 Unknown으로 저장한다.
    /// </summary>
    public DamageContext(float amount, GameObject instigator, Vector3 point, Vector3 direction, string patternId)
        : this(amount, instigator, point, direction, patternId, 0, 0, IncomingAttackKind.Unknown)
    {
    }

    /// <summary>
    /// 피해량, 공격자, 타격 위치와 방향, 패턴 식별자에 attackId, hitId, kind를 더해 피해 문맥을 만든다.
    /// attackId는 공격 1회, hitId는 그 공격 안의 타격 순번을 뜻하며 0은 미부여 상태다.
    /// </summary>
    public DamageContext(float amount, GameObject instigator, Vector3 point, Vector3 direction, string patternId,
        int attackId, int hitId, IncomingAttackKind kind)
    {
        Amount = amount;
        Instigator = instigator;
        Point = point;
        Direction = direction;
        PatternId = patternId;
        AttackId = attackId;
        HitId = hitId;
        Kind = kind;
    }
}
