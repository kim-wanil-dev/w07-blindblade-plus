namespace BlindBlade.Combat.Contracts
{
    /// <summary>재도전과 재활성화를 구분하는 세션 식별 값이다.</summary>
    public readonly struct CombatSessionKey
    {
        public string SessionId { get; }
        public int Generation { get; }

        /// <summary>
        /// sessionId와 generation으로 세션 식별 값을 만든다.
        /// 두 값을 보존하며 기본값은 활성 세션으로 사용하지 않는다.
        /// </summary>
        public CombatSessionKey(string sessionId, int generation)
        {
            SessionId = sessionId;
            Generation = generation;
        }
    }

    /// <summary>동일 기준점에서 측정한 세 종류의 세션 경과 시간이다. 단위는 초다.</summary>
    public readonly struct CombatTimestamp
    {
        public double RealtimeSeconds { get; }
        public double GameplaySeconds { get; }
        public double ActiveUnscaledSeconds { get; }

        /// <summary>
        /// realtimeSeconds, gameplaySeconds, activeUnscaledSeconds를 시각 스냅샷으로 보관한다.
        /// 실시간, 배율 적용 시간, 명시적 정지를 제외한 실시간을 각각 반환 속성에 저장한다.
        /// </summary>
        public CombatTimestamp(double realtimeSeconds, double gameplaySeconds, double activeUnscaledSeconds)
        {
            RealtimeSeconds = realtimeSeconds;
            GameplaySeconds = gameplaySeconds;
            ActiveUnscaledSeconds = activeUnscaledSeconds;
        }
    }

    public interface ICombatSessionClock
    {
        CombatSessionKey Session { get; }

        /// <summary>
        /// 현재 세션의 공통 시계를 읽어 CombatTimestamp를 반환한다.
        /// 인수를 받지 않으며 전투 상태와 시간 배율은 변경하지 않는다.
        /// </summary>
        public CombatTimestamp CaptureTimestamp();
    }
}
