namespace BlindBlade.Combat.Contracts
{
    /// <summary>공급자가 발행 시 부여하는 원본 사건 식별 값이다.</summary>
    public readonly struct CombatEventIdentity
    {
        public CombatSessionKey Session { get; }
        public string SourceId { get; }
        public long SourceSequence { get; }

        /// <summary>
        /// session, sourceId, sourceSequence로 원본 사건의 고유 키를 만든다.
        /// 공급자와 사건 순서를 저장하며 저장기의 수신 순번과 구분한다.
        /// </summary>
        public CombatEventIdentity(CombatSessionKey session, string sourceId, long sourceSequence)
        {
            Session = session;
            SourceId = sourceId;
            SourceSequence = sourceSequence;
        }
    }

    /// <summary>같은 오브젝트의 풀 재사용을 구분하는 대상 식별 값이다.</summary>
    public readonly struct CombatEntityKey
    {
        public string EntityId { get; }
        public int SpawnGeneration { get; }

        /// <summary>
        /// entityId와 spawnGeneration으로 대상의 이번 생성을 식별한다.
        /// Unity 오브젝트 참조 없이 식별 값만 반환 객체에 보관한다.
        /// </summary>
        public CombatEntityKey(string entityId, int spawnGeneration)
        {
            EntityId = entityId;
            SpawnGeneration = spawnGeneration;
        }
    }

    /// <summary>입력, 행동, 적 공격, 개별 타격, 투사체와 회피를 연결하는 식별 값이다.</summary>
    public readonly struct CombatCorrelationIds
    {
        public int InputId { get; }
        public int ActionId { get; }
        public int AttackId { get; }
        public int HitId { get; }
        public int ProjectileId { get; }
        public int DodgeId { get; }

        /// <summary>
        /// inputId, actionId, attackId, hitId, projectileId, dodgeId를 사건 연결 값으로 저장한다.
        /// 모든 ID는 0을 미부여 값으로 사용하고 기존 int ActionId와 같은 정수 형식을 쓴다.
        /// </summary>
        public CombatCorrelationIds(int inputId, int actionId, int attackId, int hitId, int projectileId, int dodgeId)
        {
            InputId = inputId;
            ActionId = actionId;
            AttackId = attackId;
            HitId = hitId;
            ProjectileId = projectileId;
            DodgeId = dodgeId;
        }
    }
}
