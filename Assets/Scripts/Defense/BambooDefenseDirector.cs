using System;

using UnityEngine;

/// <summary>방어 세션의 준비·생존 시간·처치 기록과 일반 웨이브 청각 거리를 소유하고 스폰과 투척 제한을 연결한다.</summary>
[DisallowMultipleComponent]
public sealed class BambooDefenseDirector : MonoBehaviour
{
    private static BambooDefenseDirector _hearingDirector;

    [Header("세션 연결")]
    [SerializeField] private DefenseEnemySpawner _spawner;
    private readonly DefenseThrowBudget _throwBudget = new DefenseThrowBudget();

    [Header("시작")]
    [SerializeField, Min(0f), Tooltip("Playing 진입 후 준비 시간(초)")]
    private float _startDelay = 2f;
    private float _countdown;
    private bool _started;
    private float _elapsed;
    private int _kills;
    private bool _waveStopped;

    [Header("일반 스테이지 소리 인지")]
    [SerializeField, InspectorName("소리 인지 거리 제한 사용"), Tooltip("일반 웨이브에서 적·수리검 소리와 방향 인디케이터를 같은 거리로 제한한다. 보스전과 온보딩에는 적용하지 않는다.")]
    private bool _limitSoundPerceptionDistance;
    [SerializeField, Min(0.1f), InspectorName("소리 인지 거리 (m)"), Tooltip("플레이어 루트에서 적·수리검까지의 XZ 수평 거리다. 범위 밖의 비행음은 음소거하고 범위에 들어오면 이어 들린다.")]
    private float _soundPerceptionDistance = 20f;
    private bool _isOnboardingScene;

    [Header("동시 투척 제한")]
    [SerializeField, Min(1), Tooltip("예약과 비행 중인 수리검을 합친 상한")]
    private int _maximumConcurrentShurikens = 3;
    [SerializeField, Min(0f), Tooltip("투척 허가 사이의 최소 간격(초)")]
    private float _minimumThrowGap = 0.25f;
    [SerializeField] private bool _deferDuringMeleeTelegraph = true;
    [SerializeField, Min(0f), Tooltip("근접 공격 전조를 우선하는 수평 거리(m)")]
    private float _meleeTelegraphRadius = 4f;

    [Header("근접 공격 차례 제한")]
    [SerializeField, InspectorName("공격 차례 제한 사용"), Tooltip("일반 웨이브의 근접 적과 Beast가 공격 차례를 받아야 공격한다. 차례가 없는 일반 근접 적은 거리를 두고 좌우로 움직인다.")]
    private bool _limitAttackTurns = true;
    [SerializeField, Min(1f), InspectorName("포위 반경 (m)"), Tooltip("플레이어와 이 수평 거리 안의 생존 근접 적을 포위 수로 세고 차례 대기열에 넣는다.")]
    private float _attackTurnRadius = 10f;
    [SerializeField, Min(0.01f), InspectorName("동시 공격 계수"), Tooltip("동시 공격 수 = 내림(계수 × 포위 수^지수)를 최소·최대 사이로 제한한다.")]
    private float _attackTurnScale = 1f;
    [SerializeField, Range(0.1f, 1f), InspectorName("동시 공격 지수"), Tooltip("1이면 포위 수에 비례하고 작을수록 적이 많아질 때 증가 폭이 줄어든다.")]
    private float _attackTurnExponent = 0.6f;
    [SerializeField, Min(1), InspectorName("최소 동시 공격 수")]
    private int _minimumAttackTurns = 1;
    [SerializeField, Min(1), InspectorName("최대 동시 공격 수")]
    private int _maximumAttackTurns = 6;
    [SerializeField, Min(0f), InspectorName("차례 지급 간격 (초)"), Tooltip("연속한 두 공격 차례 지급 사이의 최소 시간이다. 여러 적이 같은 순간에 공격을 시작하지 않게 한다.")]
    private float _attackTurnGrantGap = 0.4f;
    [SerializeField, Min(0.5f), InspectorName("미사용 차례 회수 시간 (초)"), Tooltip("차례를 받은 뒤 이 시간 안에 공격을 시작하지 못하면 차례를 회수해 다음 적에게 넘긴다.")]
    private float _attackTurnTimeout = 3f;
    private readonly DefenseAttackTurnPool _attackTurns = new DefenseAttackTurnPool();


    public bool HasStarted => _started;
    public float Countdown => _countdown;
    public float SurvivalTime => _elapsed;
    public int Kills => _kills;
    public int KillGoal { get; private set; }
    public bool IsRecordHidden { get; private set; }
    internal bool TelemetryLimitsSoundPerceptionDistance => _limitSoundPerceptionDistance;
    internal float TelemetrySoundPerceptionDistance => _soundPerceptionDistance;
    internal IEnemyAttackGate AttackTurnGate => _limitAttackTurns ? _attackTurns : null;
    public event Action<Vector3, float> ThrowTelegraphed;
    public event Action<DefenseShuriken> ShurikenLaunched;

    void Awake()
    {
        OnboardingFlowController onboarding = FindFirstObjectByType<OnboardingFlowController>();
        _isOnboardingScene = onboarding != null && onboarding.gameObject.scene == gameObject.scene;
        _countdown = _startDelay;
        if (_spawner == null || !_spawner.Initialize(this))
        {
            Debug.LogError("방어 세션의 스폰 연결 또는 프리팹을 확인하세요.", this);
            enabled = false;
            return;
        }
        _spawner.EnemyKilled += HandleEnemyKilled;
    }

    void OnEnable()
    {
        if (!_isOnboardingScene)
            _hearingDirector = this;
    }

    void OnDisable()
    {
        if (_hearingDirector == this)
            _hearingDirector = null;
    }

    /// <summary>
    /// 활성 일반 웨이브의 거리 제한 설정을 확인하고 distance에 미터 단위 인지 거리를 반환한다.
    /// 옵션 해제·보스전·온보딩 또는 비활성 세션이면 false를 반환한다.
    /// </summary>
    internal static bool TryGetSoundPerceptionDistance(out float distance)
    {
        distance = 0f;
        if (_hearingDirector == null || !_hearingDirector.isActiveAndEnabled
            || !_hearingDirector._limitSoundPerceptionDistance || _hearingDirector._waveStopped)
            return false;
        distance = Mathf.Max(0.1f, _hearingDirector._soundPerceptionDistance);
        return true;
    }

    /// <summary>
    /// position과 플레이어 루트의 XZ 수평 거리를 일반 스테이지 소리 인지 거리와 비교한다.
    /// 경계 안이면 true를 반환하며 거리 제한이 없거나 플레이어가 준비되지 않았어도 기존 재생을 허용한다.
    /// </summary>
    internal static bool IsWithinSoundPerceptionRange(Vector3 position)
    {
        if (!TryGetSoundPerceptionDistance(out float distance) || !Game.IsInitialized || Game.Player == null)
            return true;
        Vector3 offset = position - Game.Player.transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= distance * distance;
    }

    /// <summary>플레이 시작 시 이전 세션의 소리 인지 담당자 참조를 비워 Domain Reload 해제 상태에서도 초기화한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHearingDirector()
    {
        _hearingDirector = null;
    }

    void Update()
    {
        if (_waveStopped)
            return;
        if (!Game.IsInitialized || Game.Player == null || Game.Instance.GameStateManager == null
            || Game.Instance.GameStateManager.State != GameplayState.Playing)
            return;
        if (!_started)
        {
            _countdown = Mathf.Max(0f, _countdown - Time.deltaTime);
            if (_countdown > 0f)
                return;
            _started = true;
            _spawner.Begin(Time.time);
        }
        _elapsed += Time.deltaTime;
        if (_limitAttackTurns)
            _attackTurns.Tick(Time.time, GetAttackTurnCapacity(_spawner.CountMeleeNearPlayer(_attackTurnRadius)),
                _attackTurnRadius, _attackTurnGrantGap, _attackTurnTimeout);
        _spawner.Tick(_elapsed);
    }

    void OnDestroy()
    {
        if (_spawner != null)
            _spawner.EnemyKilled -= HandleEnemyKilled;
        _throwBudget.Clear();
        _attackTurns.Clear();
    }

    /// <summary>
    /// engaged 포위 수로 내림(계수 × engaged^지수)를 계산해 최소·최대 동시 공격 수 사이로 제한한 값을 반환한다.
    /// 포위 수가 0이면 1명으로 계산한다.
    /// </summary>
    private int GetAttackTurnCapacity(int engaged)
    {
        float raw = _attackTurnScale * Mathf.Pow(Mathf.Max(1, engaged), _attackTurnExponent);
        return Mathf.Clamp(Mathf.FloorToInt(raw + 0.0001f), _minimumAttackTurns,
            Mathf.Max(_minimumAttackTurns, _maximumAttackTurns));
    }

    /// <summary>진행 중 세션에서 근접 전조와 투척 한도를 확인하고 예약 성공 여부를 반환한다.</summary>
    internal bool TryReserveThrow()
    {
        if (_waveStopped || !_started || !isActiveAndEnabled || Game.Instance.GameStateManager.State != GameplayState.Playing
            || (_deferDuringMeleeTelegraph && _spawner.IsMeleeTelegraphNearPlayer(_meleeTelegraphRadius)))
            return false;
        return _throwBudget.TryReserve(Time.time, _maximumConcurrentShurikens, _minimumThrowGap);
    }

    /// <summary>투척수가 보유한 예약 하나를 전체 투척 예산에서 해제한다.</summary>
    internal void CancelThrowReservation()
    {
        _throwBudget.CancelReservation();
    }

    /// <summary>shuriken의 예약을 비행으로 전환하고 방향 표시 구독자에게 발사를 알린다.</summary>
    internal void NotifyShurikenLaunched(DefenseShuriken shuriken)
    {
        _throwBudget.Launch(shuriken);
        ShurikenLaunched?.Invoke(shuriken);
    }

    /// <summary>position에서 시작한 전조와 실제 duration(초)을 표시 구독자에게 알린다.</summary>
    internal void NotifyThrowTelegraph(Vector3 position, float duration)
    {
        ThrowTelegraphed?.Invoke(position, duration);
    }

    /// <summary>self를 제외한 빈 투척 위치를 스폰 담당자에게 요청하고 position과 성공 여부를 반환한다.</summary>
    internal bool TryFindThrowerPosition(BambooThrower self, out Vector3 position)
    {
        return _spawner.TryFindThrowerPosition(self, out position);
    }

    /// <summary>position에서 가장 가까운 생존 근접 적을 스폰 담당자에게 요청하고 target과 성공 여부를 반환한다.</summary>
    internal bool TryFindNearestMelee(Vector3 position, out EnemyHealth target)
    {
        return _spawner.TryFindNearestMelee(position, out target);
    }

    /// <summary>스폰 담당자가 확정한 적 사망을 세션 처치 기록에 한 번 더한다.</summary>
    private void HandleEnemyKilled()
    {
        _kills++;
    }

    /// <summary>기록 표시에 쓸 목표 처치 수를 goal로 저장한다. 0 이하이면 목표 없이 생존 시간과 처치 수를 표시한다.</summary>
    public void SetKillGoal(int goal)
    {
        KillGoal = Mathf.Max(0, goal);
    }

    /// <summary>보스전처럼 처치 기록이 더 필요 없을 때 기록 표시를 숨기도록 IsRecordHidden을 켠다.</summary>
    public void HideRecord()
    {
        IsRecordHidden = true;
    }

    /// <summary>이번 세션의 근접 적 총 생성 수를 limit로 제한하도록 스폰 담당자에게 전달한다. 0 이하이면 제한하지 않는다.</summary>
    public void SetMeleeSpawnLimit(int limit)
    {
        _spawner.SetMeleeSpawnLimit(limit);
    }

    /// <summary>count·duration·interval과 준비 시간 startDelay를 스폰 담당자에게 전달해 시간별 근접 적 일정을 설정한다.</summary>
    public void ConfigureTimedMeleeWave(int count, float duration, float interval)
    {
        _spawner.ConfigureTimedMeleeWave(count, duration, interval, _startDelay);
    }

    /// <summary>스테이지 전환 때 현재 웨이브의 스폰·투척 예약·비행 수리검을 종료하고 처치 수는 보존한다.</summary>
    public void StopWave()
    {
        if (_waveStopped)
            return;
        _waveStopped = true;
        _spawner.StopWave();
        foreach (DefenseShuriken shuriken in FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None))
        {
            if (shuriken.gameObject.scene == gameObject.scene && shuriken.IsFlying)
                shuriken.Release();
        }
        _throwBudget.Clear();
        _attackTurns.Clear();
    }

    /// <summary>
    /// position, viewCamera, perceptionMask로 화면에서 인지된 근접 적을 조회한다.
    /// 스폰 담당자가 찾은 적을 target에 담고 성공 여부를 반환한다.
    /// </summary>
    internal bool TryFindDetectedMelee(Vector3 position, Camera viewCamera, SensoryPerceptionMask perceptionMask, out EnemyHealth target)
    {
        return _spawner.TryFindDetectedMelee(position, viewCamera, perceptionMask, out target);
    }
}
