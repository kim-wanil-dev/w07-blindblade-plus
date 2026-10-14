namespace BlindBlade.Combat.Contracts
{
    /// <summary>사건별 payload와 별도로 전달할 로그 메타데이터다. 저장용 직렬화 DTO는 아니다.</summary>
    public readonly struct CombatTelemetryEnvelope
    {
        public CombatEventIdentity Identity { get; }
        public CombatTimestamp OccurredAt { get; }
        public CombatCorrelationIds Correlation { get; }
        public string EventType { get; }

        /// <summary>
        /// identity, occurredAt, correlation, eventType을 사건 메타데이터로 저장한다.
        /// 원본 ID와 발생 시각을 보존하고 수신 순번·설정 버전·payload는 D5 저장기가 결합한다.
        /// </summary>
        public CombatTelemetryEnvelope(
            CombatEventIdentity identity, CombatTimestamp occurredAt,
            CombatCorrelationIds correlation, string eventType)
        {
            Identity = identity;
            OccurredAt = occurredAt;
            Correlation = correlation;
            EventType = eventType;
        }
    }
}
