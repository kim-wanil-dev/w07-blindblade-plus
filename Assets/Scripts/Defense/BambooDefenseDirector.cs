using System;

using UnityEngine;

/// <summary>방어 세션의 준비·생존 시간·처치 기록을 소유하고 스폰과 투척 제한을 연결한다.</summary>
[DisallowMultipleComponent]
public sealed class BambooDefenseDirector : MonoBehaviour
{
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

    [Header("플레이어 이동 추적")]
    [SerializeField, Min(0.1f), Tooltip("수평 속도 평활 계수. 클수록 방향 전환에 빨리 반응합니다.")]
    private float _playerVelocitySmoothing = 8f;
    private Vector3 _playerVelocity;
    private Vector3 _lastPlayerPosition;
    private bool _hasPlayerSample;

    [Header("동시 투척 제한")]
    [SerializeField, Min(1), Tooltip("예약과 비행 중인 수리검을 합친 상한")]
    private int _maximumConcurrentShurikens = 3;
    [SerializeField, Min(0f), Tooltip("투척 허가 사이의 최소 간격(초)")]
    private float _minimumThrowGap = 0.25f;
    [SerializeField] private bool _deferDuringMeleeTelegraph = true;
    [SerializeField, Min(0f), Tooltip("근접 공격 전조를 우선하는 수평 거리(m)")]
    private float _meleeTelegraphRadius = 4f;


    public bool HasStarted => _started;
    public float Countdown => _countdown;
    public float SurvivalTime => _elapsed;
    public int Kills => _kills;
    public int KillGoal { get; private set; }
    public bool IsRecordHidden { get; private set; }
    public Vector3 PlayerVelocity => _playerVelocity;
    public event Action<Vector3, float> ThrowTelegraphed;
    public event Action<DefenseShuriken> ShurikenLaunched;

    void Awake()
    {
        _countdown = _startDelay;
        if (_spawner == null || !_spawner.Initialize(this))
        {
            Debug.LogError("방어 세션의 스폰 연결 또는 프리팹을 확인하세요.", this);
            enabled = false;
            return;
        }
        _spawner.EnemyKilled += HandleEnemyKilled;
    }

    void Update()
    {
        if (_waveStopped)
            return;
        if (!Game.IsInitialized || Game.Player == null || Game.Instance.GameStateManager == null
            || Game.Instance.GameStateManager.State != GameplayState.Playing)
            return;
        TrackPlayerVelocity();
        if (!_started)
        {
            _countdown = Mathf.Max(0f, _countdown - Time.deltaTime);
            if (_countdown > 0f)
                return;
            _started = true;
            _spawner.Begin(Time.time);
        }
        _elapsed += Time.deltaTime;
        _spawner.Tick(_elapsed);
    }

    void OnDestroy()
    {
        if (_spawner != null)
            _spawner.EnemyKilled -= HandleEnemyKilled;
        _throwBudget.Clear();
    }

    /// <summary>
    /// 현재 플레이어의 프레임 간 수평 이동으로 속도를 구하고 평활 계수로 부드럽게 갱신한다.
    /// 회피 순간의 급격한 속도는 완화되며 결과는 수리검 예측 조준에 사용한다.
    /// </summary>
    private void TrackPlayerVelocity()
    {
        Vector3 position = Game.Player.transform.position;
        if (_hasPlayerSample && Time.deltaTime > 0f)
        {
            Vector3 velocity = Vector3.ProjectOnPlane(position - _lastPlayerPosition, Vector3.up) / Time.deltaTime;
            _playerVelocity = Vector3.Lerp(_playerVelocity, velocity,
                1f - Mathf.Exp(-_playerVelocitySmoothing * Time.deltaTime));
        }
        _lastPlayerPosition = position;
        _hasPlayerSample = true;
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
