namespace BlindBlade.Combat.Contracts
{
    public enum CombatTimeReason
    {
        Unspecified,
        EmpoweredReady,
        EmpoweredAttack,
        DashTelegraph
    }

    /// <summary>A가 D의 시간 제어기에 전달하는 세션 소속 슬로모션 요청이다.</summary>
    public readonly struct CombatTimeRequest
    {
        public CombatSessionKey Session { get; }
        public string OwnerId { get; }
        public CombatTimeReason Reason { get; }
        public float Scale { get; }

        /// <summary>
        /// session, ownerId, reason, scale로 시간 요청 값을 만든다.
        /// 요청 값만 보존하며 실제 Time.timeScale은 변경하지 않는다.
        /// </summary>
        public CombatTimeRequest(CombatSessionKey session, string ownerId, CombatTimeReason reason, float scale)
        {
            Session = session;
            OwnerId = ownerId;
            Reason = reason;
            Scale = scale;
        }
    }

    /// <summary>시간 제어기가 수락한 요청 하나를 해제하기 위한 식별 값이다.</summary>
    public readonly struct CombatTimeToken
    {
        public CombatSessionKey Session { get; }
        public long RequestId { get; }
        public bool IsValid => RequestId > 0;

        /// <summary>
        /// session과 requestId를 저장해 시간 요청 토큰을 만든다.
        /// 기본값 또는 requestId 0은 수락되지 않은 요청을 나타낸다.
        /// </summary>
        public CombatTimeToken(CombatSessionKey session, long requestId)
        {
            Session = session;
            RequestId = requestId;
        }
    }

    public interface ICombatTimeRequests
    {
        /// <summary>
        /// request의 세션·소유자·배율을 확인해 활성 슬로모션 요청으로 등록한다.
        /// 수락한 요청의 토큰을 반환하고 유효하지 않은 요청에는 기본 토큰을 반환한다.
        /// </summary>
        public CombatTimeToken RequestSlowMotion(CombatTimeRequest request);

        /// <summary>
        /// token에 해당하는 현재 세션의 시간 요청을 해제한다.
        /// 실제 해제 시 true를 반환하며 중복 또는 이전 세션 토큰에는 false를 반환한다.
        /// </summary>
        public bool Release(CombatTimeToken token);

        /// <summary>
        /// paused 값으로 명시적 게임 정지를 설정하고 현재 슬로모션 요청과 합성해 시간 배율을 갱신한다.
        /// 슬로모션 토큰은 유지되어 정지를 해제하면 살아 있는 요청이 다시 적용된다.
        /// </summary>
        public void SetFrozen(bool paused);
    }
}
