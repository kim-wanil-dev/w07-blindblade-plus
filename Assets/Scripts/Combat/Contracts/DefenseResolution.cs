using UnityEngine;

namespace BlindBlade.Combat.Contracts
{
    /// <summary>피해 문맥에 담아 수신자에게 전달하는 공격 종류다.</summary>
    public enum IncomingAttackKind
    {
        Unknown,
        Melee,
        Charge,
        Projectile,
        PlayerOwned
    }

    /// <summary>A가 실제 접촉 한 건에 대해 확정하는 방어 결과다.</summary>
    public enum DefenseOutcome
    {
        Hit,
        Guard,
        NormalParry,
        PerfectParry,
        DodgeAvoided,
        Invulnerable,
        Ignored
    }

    /// <summary>A가 한 공격의 개별 타격에 대해 확정한 결과와 실제 피해를 보관한다.</summary>
    public readonly struct DefenseResolution
    {
        public DefenseOutcome Outcome { get; }
        public int AttackId { get; }
        public int HitId { get; }
        public int GuardInputId { get; }
        public float AppliedDamage { get; }
        public Vector3 ContactPoint { get; }
        public float GameTime { get; }
        public double RealTime { get; }

        /// <summary>
        /// outcome, attackId, hitId, guardInputId, appliedDamage, contactPoint와 판정 시각으로 결과를 만든다.
        /// guardInputId는 사용한 가드 입력이며 없으면 0이고 appliedDamage는 실제 체력 감소량이다.
        /// </summary>
        public DefenseResolution(DefenseOutcome outcome, int attackId, int hitId, int guardInputId,
            float appliedDamage, Vector3 contactPoint, float gameTime, double realTime)
        {
            Outcome = outcome;
            AttackId = attackId;
            HitId = hitId;
            GuardInputId = guardInputId;
            AppliedDamage = appliedDamage;
            ContactPoint = contactPoint;
            GameTime = gameTime;
            RealTime = realTime;
        }
    }
}
