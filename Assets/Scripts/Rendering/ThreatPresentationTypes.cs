using UnityEngine;

namespace BlindBlade.Perception
{
    public enum ThreatTargetKind { Melee, Projectile }
    public enum MeleeThreatType { Normal, Charge }
    public enum ThreatOwnership { Hostile, Reflected }
    public enum ThreatRadiusSource { Default, Type, Individual, Runtime }

    /// <summary>B의 대상 정보를 연결하는 C 입력이다. EntityId로 공통 계약의 문자열 식별자를 보존한다.</summary>
    public readonly struct ThreatSourceState
    {
        public int TargetId { get; }
        public string EntityId { get; }
        public int SpawnGeneration { get; }
        public ThreatTargetKind Kind { get; }
        public MeleeThreatType MeleeType { get; }
        public ThreatOwnership Ownership { get; }
        public bool HasPassedPlayer { get; }
        public bool IsActive { get; }

        /// <summary>
        /// targetId와 spawnGeneration으로 생명을 식별하고 kind·meleeType·ownership·통과·활성 상태를 저장한다.
        /// entityId는 공통 계약으로 전달할 원본 문자열이며 Unity 참조나 전투 상태는 변경하지 않는다.
        /// </summary>
        public ThreatSourceState(int targetId, int spawnGeneration, ThreatTargetKind kind,
            MeleeThreatType meleeType, ThreatOwnership ownership, bool hasPassedPlayer, bool isActive, string entityId = null)
        {
            TargetId = targetId;
            EntityId = entityId;
            SpawnGeneration = spawnGeneration;
            Kind = kind;
            MeleeType = meleeType;
            Ownership = ownership;
            HasPassedPlayer = hasPassedPlayer;
            IsActive = isActive;
        }
    }

    /// <summary>C의 렌더링·Inspector용 내부 표시 결과다. 외부 전달은 공통 계약 어댑터가 담당한다.</summary>
    public readonly struct ThreatPresentationSnapshot
    {
        public int TargetId { get; }
        public string EntityId { get; }
        public bool IsActive { get; }
        public int SpawnGeneration { get; }
        public ThreatTargetKind Kind { get; }
        public Vector3 Position { get; }
        public float HorizontalDistance { get; }
        public float EnemyRadius { get; }
        public ThreatRadiusSource RadiusSource { get; }
        public bool IsEligible { get; }
        public bool IntersectsCamera { get; }
        public bool IsInCamera { get; }
        public bool ShowWorld { get; }
        public bool ShowUi { get; }
        public float DirectionDegrees { get; }
        public Color Color { get; }
        public float Alpha { get; }

        /// <summary>
        /// source의 식별값과 position·distance·radius·radiusSource 및 화면·색상 판정으로 결과를 만든다.
        /// 실제 화면 교차와 전환 여유가 적용된 화면 분류를 분리하고 월드·UI 표시 자격을 저장한다.
        /// </summary>
        public ThreatPresentationSnapshot(ThreatSourceState source, Vector3 position, float distance,
            float radius, ThreatRadiusSource radiusSource, bool eligible, bool intersectsCamera,
            bool isInCamera, float directionDegrees, Color color, float alpha)
        {
            TargetId = source.TargetId;
            EntityId = source.EntityId;
            IsActive = source.IsActive;
            SpawnGeneration = source.SpawnGeneration;
            Kind = source.Kind;
            Position = position;
            HorizontalDistance = distance;
            EnemyRadius = radius;
            RadiusSource = radiusSource;
            IsEligible = eligible;
            IntersectsCamera = intersectsCamera;
            IsInCamera = isInCamera;
            ShowWorld = eligible && isInCamera;
            ShowUi = eligible && !isInCamera && source.Ownership == ThreatOwnership.Hostile
                && !(source.Kind == ThreatTargetKind.Projectile && source.HasPassedPlayer);
            DirectionDegrees = directionDegrees;
            Color = color;
            Alpha = alpha;
        }
    }
}
