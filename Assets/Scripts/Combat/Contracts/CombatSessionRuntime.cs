using System;
using System.Collections.Generic;

using UnityEngine;

namespace BlindBlade.Combat.Contracts
{
    /// <summary>한 게임 세션의 식별자, 공통 시계, 공격 ID와 실제 시간 배율을 소유한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatSessionRuntime : MonoBehaviour, ICombatSessionClock, ICombatTimeRequests
    {
        private static CombatSessionRuntime _active;
        private static int _nextGeneration;

        [Header("세션 시간")]
        [SerializeField, Range(0.1f, 2f)] private float _defaultTimeScale = 1f;
        private readonly Dictionary<long, CombatTimeRequest> _requests = new Dictionary<long, CombatTimeRequest>();
        private CombatSessionKey _session;
        private double _startedRealtime;
        private float _startedGameplayTime;
        private double _activeUnscaledSeconds;
        private long _nextRequestId;
        private int _nextAttackId;
        private int _nextProjectileId;
        private bool _isFrozen;
        private bool _isInitialized;

        public static CombatSessionRuntime Active => _active;
        public bool IsInitialized => _isInitialized;
        public CombatSessionKey Session => _session;

        void Awake()
        {
            _active = this;
        }

        void Update()
        {
            if (_isInitialized && !_isFrozen)
                _activeUnscaledSeconds += Time.unscaledDeltaTime;
        }

        void OnDestroy()
        {
            Shutdown();
            if (_active == this)
                _active = null;
        }

        /// <summary>
        /// 새 세션 ID와 세대를 만들고 누적 시계, 공격 ID, 시간 요청을 초기화한다.
        /// 시작 배율은 Inspector의 _defaultTimeScale이며 이후에는 요청과 정지 상태로만 갱신한다.
        /// </summary>
        public void Initialize()
        {
            Shutdown();
            _active = this;
            _nextGeneration++;
            if (_nextGeneration <= 0)
                _nextGeneration = 1;
            _session = new CombatSessionKey(Guid.NewGuid().ToString("N"), _nextGeneration);
            _startedRealtime = Time.realtimeSinceStartupAsDouble;
            _startedGameplayTime = Time.time;
            _activeUnscaledSeconds = 0d;
            _nextRequestId = 0;
            _nextAttackId = 0;
            _nextProjectileId = 0;
            _isFrozen = false;
            _isInitialized = true;
            ApplyTimeScale();
        }

        /// <summary>
        /// 현재 세션의 벽시계, 배율 적용 게임 시간, 명시적 정지를 제외한 시간을 반환한다.
        /// 세션 시작 전 호출이면 기본 타임스탬프를 반환한다.
        /// </summary>
        public CombatTimestamp CaptureTimestamp()
        {
            if (!_isInitialized)
                return default;
            return new CombatTimestamp(Time.realtimeSinceStartupAsDouble - _startedRealtime,
                Time.time - _startedGameplayTime, _activeUnscaledSeconds);
        }

        /// <summary>
        /// 현재 세션에서 전역 고유 공격 ID를 발급하고 반환한다.
        /// 미부여 표시는 0이므로 발급 값은 양수이며 세션을 초기화하면 1부터 시작한다.
        /// </summary>
        public int AllocateAttackId()
        {
            return NextPositiveId(ref _nextAttackId);
        }

        /// <summary>
        /// 현재 세션에서 전역 고유 투사체 ID를 발급하고 반환한다.
        /// 미부여 표시는 0이므로 발급 값은 양수이며 세션을 초기화하면 1부터 시작한다.
        /// </summary>
        public int AllocateProjectileId()
        {
            return NextPositiveId(ref _nextProjectileId);
        }

        /// <summary>
        /// 현재 세션에 속한 유효한 요청을 등록하고 시간 배율을 다시 계산한다.
        /// 수락된 요청에는 해제용 토큰을 반환하고 다른 세션·비정상 요청에는 기본 토큰을 반환한다.
        /// </summary>
        public CombatTimeToken RequestSlowMotion(CombatTimeRequest request)
        {
            if (!_isInitialized || !IsCurrentSession(request.Session)
                || string.IsNullOrWhiteSpace(request.OwnerId) || request.Reason == CombatTimeReason.Unspecified
                || request.Scale <= 0f || request.Scale > 1f
                || float.IsNaN(request.Scale) || float.IsInfinity(request.Scale))
                return default;

            foreach (CombatTimeRequest activeRequest in _requests.Values)
            {
                if (activeRequest.OwnerId == request.OwnerId)
                    return default;
            }
            if (_nextRequestId == long.MaxValue)
            {
                Debug.LogError("시간 요청 ID 범위가 끝나 새 요청을 등록할 수 없습니다.", this);
                return default;
            }
            _nextRequestId++;
            _requests[_nextRequestId] = request;
            ApplyTimeScale();
            return new CombatTimeToken(_session, _nextRequestId);
        }

        /// <summary>
        /// token이 현재 세션의 요청이면 제거하고 시간 배율을 갱신한다.
        /// 요청을 실제로 제거한 경우에만 true를 반환한다.
        /// </summary>
        public bool Release(CombatTimeToken token)
        {
            if (!_isInitialized || !token.IsValid || !IsCurrentSession(token.Session)
                || !_requests.Remove(token.RequestId))
                return false;
            ApplyTimeScale();
            return true;
        }

        /// <summary>
        /// paused 상태를 갱신하고 활성 요청 중 가장 느린 배율을 적용한다.
        /// 정지 해제 후에도 살아 있는 요청은 그대로 다시 반영한다.
        /// </summary>
        public void SetFrozen(bool paused)
        {
            if (!_isInitialized || _isFrozen == paused)
                return;
            _isFrozen = paused;
            ApplyTimeScale();
        }

        /// <summary>
        /// 세션 요청을 지우고 Inspector 기본 배율을 복원한다.
        /// 이전 세션의 토큰이나 정지 상태는 다음 세션에 전달하지 않는다.
        /// </summary>
        public void Shutdown()
        {
            _requests.Clear();
            _isFrozen = false;
            if (_isInitialized)
                Time.timeScale = Mathf.Max(0.1f, _defaultTimeScale);
            _isInitialized = false;
        }

        /// <summary>
        /// id에 다음 양수를 저장하고 공격·투사체 식별값으로 반환한다.
        /// 정수 범위를 넘으면 오류를 기록하고 0을 반환해 기존 ID를 재사용하지 않는다.
        /// </summary>
        private int NextPositiveId(ref int id)
        {
            if (id == int.MaxValue)
            {
                Debug.LogError("전투 ID 범위가 끝나 새 식별자를 발급할 수 없습니다.", this);
                return 0;
            }
            return ++id;
        }

        /// <summary>session의 ID와 세대가 현재 세션과 모두 일치하면 true를 반환한다.</summary>
        private bool IsCurrentSession(CombatSessionKey session)
        {
            return session.Generation == _session.Generation && session.SessionId == _session.SessionId;
        }

        /// <summary>정지 여부와 활성 요청 중 최소 배율을 결합해 Unity의 전역 시간 배율을 갱신한다.</summary>
        private void ApplyTimeScale()
        {
            if (_isFrozen)
            {
                Time.timeScale = 0f;
                return;
            }

            float scale = Mathf.Max(0.1f, _defaultTimeScale);
            foreach (CombatTimeRequest request in _requests.Values)
                scale = Mathf.Min(scale, request.Scale);
            Time.timeScale = scale;
        }

        /// <summary>플레이 도메인이 새로 시작될 때 이전 런타임 객체의 정적 참조를 비운다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
            _nextGeneration = 0;
        }
    }
}
