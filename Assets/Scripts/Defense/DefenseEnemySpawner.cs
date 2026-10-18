using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using Newtonsoft.Json.Linq;

using Random = UnityEngine.Random;

/// <summary>방어 적의 시간별 생성·배치·씬 단위 재사용을 담당하며 확정된 처치를 알린다.</summary>
[DisallowMultipleComponent]
public sealed class DefenseEnemySpawner : MonoBehaviour
{
    private const int FALLBACK_MELEE_ANGLE_COUNT = 24;
    private const int FALLBACK_MELEE_RADIUS_COUNT = 4;
    private const int RANDOM_MELEE_POSITION_ATTEMPTS = 16;
    private const float MELEE_POSITION_SAMPLE_RADIUS = 1.5f;
    private const float TIMED_WAVE_INITIAL_WEIGHT = 3f;

    [Header("스폰 공통")]
    [SerializeField, Min(0f), Tooltip("플레이어와 이 수평 거리 안에서는 적을 스폰하지 않는다.")]
    private float _minimumSpawnDistance = 8f;
    [SerializeField] private Transform _unitRoot;

    [Header("투척수")]
    [SerializeField] private BambooThrower _throwerPrefab;
    [SerializeField, Min(0f)] private float _throwerMinimumRadius = 25.4f;
    [SerializeField, Min(0f)] private float _throwerMaximumRadius = 29f;
    [SerializeField, Min(1f), Tooltip("플레이어와 이 수평 거리보다 먼 숲에는 투척수를 배치하지 않는다.")]
    private float _throwerMaximumDistance = 30f;
    [SerializeField, Min(0f)] private float _throwerMinimumHeight = 0.6f;
    [SerializeField, Min(0f)] private float _throwerMaximumHeight = 5.5f;
    [SerializeField, Min(0f), Tooltip("투척수끼리 유지하는 최소 거리(미터)")]
    private float _throwerSeparation = 3f;
    [SerializeField, Min(0f), Tooltip("준비가 끝난 뒤 첫 투척수가 나타나기까지 기다리는 시간"), InspectorName("준비 종료 후 투척수 등장 지연 (초)")]
    private float _throwerStartTime = 0f;
    [SerializeField, Min(0), InspectorName("투척수 시작 수 (명)")] private int _initialThrowers = 3;
    [SerializeField, Min(0f), Tooltip("등장 시작 후 동시 생존 목표 수를 1분마다 늘리는 양"), InspectorName("동시 투척수 목표 증가 (명/분)")]
    private float _throwersPerMinute = 3f;
    [SerializeField, Min(1), Tooltip("성능 보호용 최대 투척수"), InspectorName("투척수 동시 생존 상한 (명)")]
    private int _maximumThrowers = 12;
    [SerializeField, Min(0f), Tooltip("목표 수를 채우거나 사망한 투척수를 보충할 때 다음 투척수까지 기다리는 최소 시간"), InspectorName("실제 추가 스폰 간격 (초)")]
    private float _throwerSpawnInterval = 1.33f;
    private readonly List<BambooThrower> _activeThrowers = new List<BambooThrower>();
    private readonly Stack<BambooThrower> _idleThrowers = new Stack<BambooThrower>();
    private float _nextThrowerSpawnAt;

    [Header("근접 적")]
    [SerializeField] private GameObject _meleePrefab;
    [SerializeField, InspectorName("근접 적 바닥 발자국 표시"), Tooltip("켜면 일반 근접 적과 Beast가 이동할 때 바닥에 남기는 발자국을 표시한다. 발소리와 걸음 파동은 유지한다.")]
    private bool _showMeleeGroundFootprints = true;
    [SerializeField, Min(0f), Tooltip("근접 적이 나타나는 최소 반경. 숲 안쪽 가장자리 근처로 설정한다.")]
    private float _meleeSpawnMinimumRadius = 24f;
    [SerializeField, Min(0f), Tooltip("근접 적이 나타나는 최대 반경. 플레이어까지 경로가 있는 숲 안 위치만 사용한다.")]
    private float _meleeSpawnMaximumRadius = 28.6f;
    [SerializeField, Min(0f), InspectorName("준비 종료 후 근접 등장 지연 (초)")] private float _meleeStartTime = 5f;
    [SerializeField, Min(0.01f), Tooltip("근접 적 등장 시작 시점의 분당 스폰 수"), InspectorName("근접 시작 스폰율 (회/분)")]
    private float _meleeSpawnsPerMinute = 15f;
    [SerializeField, Min(0f), Tooltip("1분마다 늘어나는 분당 스폰 수"), InspectorName("분당 근접 스폰율 증가")]
    private float _meleeSpawnGrowthPerMinute = 9f;
    [SerializeField, Min(0), Tooltip("0이면 처치 수에 따른 첫 번째 스폰율 변경을 사용하지 않는다.")]
    private int _firstMeleeKillThreshold;
    [SerializeField, Min(0f), Tooltip("첫 번째 처치 기준 이후의 근접 스폰 수(회/분). 0이면 변경하지 않는다.")]
    private float _firstMeleeSpawnsPerMinute;
    [SerializeField, Min(0), Tooltip("0이면 처치 수에 따른 두 번째 스폰율 변경을 사용하지 않는다.")]
    private int _secondMeleeKillThreshold;
    [SerializeField, Min(0f), Tooltip("두 번째 처치 기준 이후의 근접 스폰 수(회/분). 0이면 변경하지 않는다.")]
    private float _secondMeleeSpawnsPerMinute;
    [SerializeField, Min(0f), Tooltip("이 시간 이후 돌진형 근접 적을 섞는다."), InspectorName("돌진형 등장 생존 시간 (초)")]
    private float _chargeTypeStartTime = 90f;
    [SerializeField, Range(0f, 1f), InspectorName("돌진형 선택 확률")] private float _chargeTypeChance = 0.4f;
    [SerializeField, Min(1), Tooltip("성능 보호용 최대 근접 적 수"), InspectorName("근접 동시 생존 상한 (명)")]
    private int _maximumMelee = 30;
    [Header("일반·Beast 선택 가중치")]
    [SerializeField] private GameObject _beastPrefab;
    [SerializeField, Tooltip("이 씬에서 생성한 Beast만 사용할 전용 사망음입니다. None이면 공통 사망음을 사용합니다.")]
    private SoundId _beastDeathSound = SoundId.None;
    [SerializeField, Min(0), InspectorName("보간 시작 스폰 수 (마리)")]
    private int _weightBlendStartSpawnCount;
    [SerializeField, Min(1), InspectorName("보간 종료 스폰 수 (마리)")]
    private int _weightBlendEndSpawnCount = 50;
    [SerializeField, Range(0f, 1f), InspectorName("시작 일반 적 가중치")]
    private float _meleeWeightAtStart = 1f;
    [SerializeField, Range(0f, 1f), InspectorName("시작 Beast 가중치")]
    private float _beastWeightAtStart;
    [SerializeField, Range(0f, 1f), InspectorName("종료 일반 적 가중치")]
    private float _meleeWeightAtEnd = 1f;
    [SerializeField, Range(0f, 1f), InspectorName("종료 Beast 가중치")]
    private float _beastWeightAtEnd;
    private readonly List<MeleeUnit> _activeMelee = new List<MeleeUnit>();
    private readonly Stack<MeleeUnit> _idleMelee = new Stack<MeleeUnit>();
    private readonly Stack<MeleeUnit> _idleBeasts = new Stack<MeleeUnit>();
    private readonly Dictionary<EnemyHealth, MeleeUnit> _meleeByHealth = new Dictionary<EnemyHealth, MeleeUnit>();
    private NavMeshPath _meleePath;
    private bool _reportedMeleePositionFailure;
    private float _nextMeleeSpawnAt;
    private int _meleeSpawnLimit;
    private int _meleeSpawned;
    private int _meleeSpawnTier;
    private int _timedWaveSpawnCount;
    private float _timedWaveDuration;
    private float _timedWaveInterval;
    private float _timedWavePreparationDelay;
    private readonly Plane[] _reflectCameraPlanes = new Plane[6];

    [Header("세션 연결")]
    private BambooDefenseDirector _director;
    private readonly List<BambooThrower> _allThrowers = new List<BambooThrower>();
    private readonly Dictionary<BambooThrower, ThrowerSpawnRecord> _throwerSpawnRecords =
        new Dictionary<BambooThrower, ThrowerSpawnRecord>();
    private int _telemetrySpawnOrdinal;
    private bool _waveStopped;
    public event Action EnemyKilled;
    public event Action<string, string, int, Vector3, float, float> EnemySpawned;
    public event Action<string, string, double, Vector3> EnemyDefeated;
    public event Action<string, string, double, Vector3, string> EnemyDespawned;

    /// <summary>현재 스포너와 청각 제한 설정을 stage_config 사건에 넣을 JSON 객체로 반환한다.</summary>
    public JObject GetTelemetryConfiguration => new JObject {
        ["throwerStartTime"] = _throwerStartTime, ["initialThrowers"] = _initialThrowers,
        ["throwersPerMinute"] = _throwersPerMinute, ["maximumThrowers"] = _maximumThrowers,
        ["throwerSpawnInterval"] = _throwerSpawnInterval,
        ["meleeStartTime"] = _meleeStartTime, ["meleeSpawnsPerMinute"] = _meleeSpawnsPerMinute,
        ["meleeSpawnGrowthPerMinute"] = _meleeSpawnGrowthPerMinute,
        ["firstMeleeKillThreshold"] = _firstMeleeKillThreshold,
        ["firstMeleeSpawnsPerMinute"] = _firstMeleeSpawnsPerMinute,
        ["secondMeleeKillThreshold"] = _secondMeleeKillThreshold,
        ["secondMeleeSpawnsPerMinute"] = _secondMeleeSpawnsPerMinute,
        ["maximumMelee"] = _maximumMelee, ["chargeTypeStartTime"] = _chargeTypeStartTime,
        ["chargeTypeChance"] = _chargeTypeChance, ["timedWaveSpawnCount"] = _timedWaveSpawnCount,
        ["timedWaveDuration"] = _timedWaveDuration, ["timedWaveInterval"] = _timedWaveInterval,
        ["timedWavePreparationDelay"] = _timedWavePreparationDelay,
        ["meleeSpawnLimit"] = _meleeSpawnLimit,
        ["weightBlendStartSpawnCount"] = _weightBlendStartSpawnCount,
        ["weightBlendEndSpawnCount"] = _weightBlendEndSpawnCount,
        ["meleeWeightAtStart"] = _meleeWeightAtStart, ["beastWeightAtStart"] = _beastWeightAtStart,
        ["meleeWeightAtEnd"] = _meleeWeightAtEnd, ["beastWeightAtEnd"] = _beastWeightAtEnd,
        ["showMeleeGroundFootprints"] = _showMeleeGroundFootprints,
        ["soundAwarenessEnabled"] = _director != null && _director.TelemetryLimitsSoundPerceptionDistance,
        ["soundAwarenessDistance"] = _director != null ? _director.TelemetrySoundPerceptionDistance : 0f
    };

    private sealed class MeleeUnit
    {
        public GameObject Root { get; }
        public EnemyHealth Health { get; }
        public EnemyController Controller { get; }
        public EnemyAttackSelector Selector { get; }
        public EnemyChargeAttack Charge { get; }
        public bool IsBeast { get; }
        public string TelemetryId { get; set; }
        public int TelemetrySpawnOrdinal { get; set; }
        public double TelemetrySpawnedAt { get; set; }
        public float TelemetryMeleeProbability { get; set; }
        public float TelemetryBeastProbability { get; set; }

        /// <summary>root의 전투 컴포넌트와 isBeast 유형을 저장해 스폰 중 재사용한다.</summary>
        public MeleeUnit(GameObject root, bool isBeast)
        {
            Root = root;
            Health = root.GetComponent<EnemyHealth>();
            Controller = root.GetComponent<EnemyController>();
            Selector = root.GetComponent<EnemyAttackSelector>();
            Charge = root.GetComponent<EnemyChargeAttack>();
            IsBeast = isBeast;
        }
    }

    private sealed class ThrowerSpawnRecord
    {
        public string EnemyId { get; }
        public int SpawnOrdinal { get; }
        public double SpawnedAt { get; }

        /// <summary>thrower 활성화 시점의 ID, 순번, 실제 시각을 저장한다.</summary>
        public ThrowerSpawnRecord(string enemyId, int spawnOrdinal, double spawnedAt)
        {
            EnemyId = enemyId;
            SpawnOrdinal = spawnOrdinal;
            SpawnedAt = spawnedAt;
        }
    }

    /// <summary>director를 생성한 적의 세션으로 연결하고 경로 검사를 준비한다. 필수 프리팹 유효 여부를 반환한다.</summary>
    internal bool Initialize(BambooDefenseDirector director)
    {
        _director = director;
        _meleePath = new NavMeshPath();
        if (_throwerPrefab != null && _meleePrefab != null)
            return true;
        Debug.LogError("방어 스폰 담당자의 투척수·근접 적 프리팹 연결이 누락되었습니다.", this);
        return false;
    }

    /// <summary>now와 _meleeStartTime을 사용해 준비 종료 후 첫 근접 스폰 시각을 설정한다.</summary>
    internal void Begin(float now)
    {
        if (_waveStopped)
            return;
        _nextMeleeSpawnAt = now + Mathf.Max(0f, _meleeStartTime);
    }

    /// <summary>elapsed 생존 시간으로 투척수·근접 적의 목표 수와 스폰 시각을 갱신한다.</summary>
    internal void Tick(float elapsed)
    {
        if (_waveStopped)
            return;
        UpdateThrowers(elapsed);
        UpdateMelee(elapsed);
    }

    /// <summary>이번 세션에서 생성할 근접 적의 총수를 limit로 제한한다. 0 이하이면 제한하지 않는다.</summary>
    internal void SetMeleeSpawnLimit(int limit)
    {
        _meleeSpawnLimit = Mathf.Max(0, limit);
    }

    /// <summary>count·duration·interval과 preparationDelay를 사용해 Playing 시작 기준 근접 적 생성 일정을 저장한다.</summary>
    internal void ConfigureTimedMeleeWave(int count, float duration, float interval, float preparationDelay)
    {
        _timedWaveSpawnCount = Mathf.Max(1, count);
        _timedWaveDuration = Mathf.Max(1f, duration);
        _timedWaveInterval = Mathf.Clamp(interval, 1f, _timedWaveDuration);
        _timedWavePreparationDelay = Mathf.Max(0f, preparationDelay);
    }

    /// <summary>입력 없이 현재 투척수·근접 적을 대기 풀로 되돌리고 이후 스폰을 막는다. 처치 사건은 만들지 않는다.</summary>
    internal void StopWave()
    {
        if (_waveStopped)
            return;
        _waveStopped = true;
        for (int i = _activeThrowers.Count - 1; i >= 0; i--)
        {
            BambooThrower thrower = _activeThrowers[i];
            if (thrower != null && thrower.gameObject.activeSelf)
                thrower.gameObject.SetActive(false);
            else
                _activeThrowers.RemoveAt(i);
        }
        for (int i = _activeMelee.Count - 1; i >= 0; i--)
        {
            MeleeUnit unit = _activeMelee[i];
            _activeMelee.RemoveAt(i);
            EnemyDespawned?.Invoke(unit.TelemetryId, unit.IsBeast ? "Beast" : "Melee",
                Time.realtimeSinceStartupAsDouble - unit.TelemetrySpawnedAt,
                unit.Root.transform.position, "wave_stopped");
            unit.TelemetryId = null;
            unit.Root.SetActive(false);
            ReturnMeleeToPool(unit);
        }
    }

    void OnDestroy()
    {
        foreach (BambooThrower thrower in _allThrowers)
        {
            if (thrower != null)
                thrower.Despawned -= HandleThrowerDespawned;
        }

        foreach (MeleeUnit unit in _meleeByHealth.Values)
        {
            if (unit.Health != null)
                unit.Health.Died -= HandleMeleeDied;
        }

        EnemyKilled = null;
    }

    /// <summary>
    /// elapsed와 _throwerStartTime으로 목표 투척수와 생성 시작 시점을 계산한다.
    /// 생성 간격에 따라 투척수를 _activeThrowers에 추가하고 다음 생성 시각을 갱신한다.
    /// </summary>
    private void UpdateThrowers(float elapsed)
    {
        float elapsedSinceStart = elapsed - Mathf.Max(0f, _throwerStartTime);
        if (elapsedSinceStart < 0f)
            return;

        int target = Mathf.Min(_maximumThrowers,
            _initialThrowers + Mathf.FloorToInt(elapsedSinceStart / 60f * _throwersPerMinute));
        if (_activeThrowers.Count >= target || Time.time < _nextThrowerSpawnAt)
            return;
        if (!TryFindThrowerPosition(null, out Vector3 position))
            return;

        BambooThrower thrower = _idleThrowers.Count > 0 ? _idleThrowers.Pop() : CreateThrower();
        _activeThrowers.Add(thrower);
        string enemyId = Guid.NewGuid().ToString("N");
        _telemetrySpawnOrdinal++;
        _throwerSpawnRecords[thrower] = new ThrowerSpawnRecord(enemyId, _telemetrySpawnOrdinal,
            Time.realtimeSinceStartupAsDouble);
        thrower.Activate(_director, position);
        EnemySpawned?.Invoke(enemyId, "Thrower", _telemetrySpawnOrdinal, position, 0f, 0f);
        _nextThrowerSpawnAt = Time.time + _throwerSpawnInterval;
    }

    /// <summary>
    /// elapsed와 현재 처치 수로 분당 근접 스폰율을 정하고 숲 가장자리의 도달 가능한 위치에 근접 적을 배치한다.
    /// 처치 구간이 바뀌면 다음 스폰 시각을 새 스폰율에 맞춰 다시 잡으며 동시 생존 상한을 넘으면 이번 스폰을 건너뛴다.
    /// 총 생성 제한이 있으면 _meleeSpawned가 제한에 닿은 뒤로는 더 생성하지 않는다.
    /// </summary>
    private void UpdateMelee(float elapsed)
    {
        if (_meleePrefab == null || (_meleeSpawnLimit > 0 && _meleeSpawned >= _meleeSpawnLimit))
            return;
        if (_timedWaveSpawnCount > 0)
        {
            UpdateTimedMelee(elapsed);
            return;
        }
        if (elapsed < Mathf.Max(0f, _meleeStartTime))
            return;

        float minutes = Mathf.Max(0f, elapsed - _meleeStartTime) / 60f;
        float spawnsPerMinute = _meleeSpawnsPerMinute + _meleeSpawnGrowthPerMinute * minutes;
        int tier = 0;
        if (_firstMeleeKillThreshold > 0 && _firstMeleeSpawnsPerMinute > 0f
            && _director.Kills >= _firstMeleeKillThreshold)
        {
            tier = 1;
            spawnsPerMinute = _firstMeleeSpawnsPerMinute;
        }
        if (_secondMeleeKillThreshold > 0 && _secondMeleeSpawnsPerMinute > 0f
            && _director.Kills >= _secondMeleeKillThreshold)
        {
            tier = 2;
            spawnsPerMinute = _secondMeleeSpawnsPerMinute;
        }
        if (tier != _meleeSpawnTier)
        {
            _meleeSpawnTier = tier;
            _nextMeleeSpawnAt = Time.time + 60f / Mathf.Max(0.01f, spawnsPerMinute);
        }
        if (Time.time < _nextMeleeSpawnAt)
            return;
        _nextMeleeSpawnAt = Time.time + 60f / Mathf.Max(0.01f, spawnsPerMinute);
        TrySpawnMelee(elapsed);
    }

    /// <summary>elapsed와 준비 시간으로 Playing 경과를 구해 interval마다 생성 빈도를 높이고 누적 예정 수까지 근접 적을 배치한다.</summary>
    private void UpdateTimedMelee(float elapsed)
    {
        float stageElapsed = Mathf.Clamp(elapsed + _timedWavePreparationDelay, 0f, _timedWaveDuration);
        int intervals = Mathf.CeilToInt(_timedWaveDuration / _timedWaveInterval);
        float totalWeight = 0f;
        float elapsedWeight = 0f;
        for (int index = 0; index < intervals; index++)
        {
            float start = index * _timedWaveInterval;
            float length = Mathf.Min(_timedWaveInterval, _timedWaveDuration - start);
            float weight = TIMED_WAVE_INITIAL_WEIGHT + index;
            totalWeight += length * weight;
            elapsedWeight += Mathf.Clamp(stageElapsed - start, 0f, length) * weight;
        }

        int due = stageElapsed >= _timedWaveDuration
            ? _timedWaveSpawnCount
            : Mathf.FloorToInt(_timedWaveSpawnCount * elapsedWeight / totalWeight + 0.00001f);
        if (_meleeSpawnLimit > 0)
            due = Mathf.Min(due, _meleeSpawnLimit);
        while (_meleeSpawned < due)
        {
            if (!TrySpawnMelee(elapsed))
                break;
        }
    }

    /// <summary>elapsed로 돌진형 여부를 정해 유효한 NavMesh 위치에 근접 적 한 명을 배치하고 생성 수를 갱신한다. 성공 여부를 반환한다.</summary>
    private bool TrySpawnMelee(float elapsed)
    {
        if (_activeMelee.Count >= _maximumMelee || !TryFindMeleePosition(out Vector3 position))
            return false;

        bool spawnBeast = SelectSpawnType(out float meleeProbability, out float beastProbability);
        MeleeUnit unit = GetOrCreateMelee(spawnBeast);
        bool chargeType = !unit.IsBeast && elapsed >= _chargeTypeStartTime && Random.value < _chargeTypeChance;
        Vector3 toCenter = Vector3.ProjectOnPlane(transform.position - position, Vector3.up);
        unit.Root.transform.SetPositionAndRotation(position,
            toCenter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCenter) : Quaternion.identity);
        if (unit.Charge != null)
            unit.Charge.enabled = chargeType;
        unit.Root.SetActive(true);
        unit.Controller.SetTarget(Game.Player.transform);
        _activeMelee.Add(unit);
        _meleeSpawned++;
        unit.TelemetryId = Guid.NewGuid().ToString("N");
        unit.TelemetrySpawnOrdinal = ++_telemetrySpawnOrdinal;
        unit.TelemetrySpawnedAt = Time.realtimeSinceStartupAsDouble;
        unit.TelemetryMeleeProbability = meleeProbability;
        unit.TelemetryBeastProbability = beastProbability;
        EnemySpawned?.Invoke(unit.TelemetryId, unit.IsBeast ? "Beast" : "Melee", unit.TelemetrySpawnOrdinal,
            position, unit.TelemetryMeleeProbability, unit.TelemetryBeastProbability);
        return true;
    }

    /// <summary>
    /// 숲 고리 안의 무작위 각도·반경·높이 중 플레이어 최소·최대 거리와 다른 투척수 간격을 만족하는 위치를 찾는다.
    /// self는 간격 검사에서 제외할 투척수이며 찾으면 position을 채우고 true를 반환한다.
    /// </summary>
    internal bool TryFindThrowerPosition(BambooThrower self, out Vector3 position)
    {
        Vector3 player = Game.Player.transform.position;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(_throwerMinimumRadius, Mathf.Max(_throwerMinimumRadius, _throwerMaximumRadius));
            float height = Random.Range(_throwerMinimumHeight, Mathf.Max(_throwerMinimumHeight, _throwerMaximumHeight));
            position = transform.position + new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            float distance = HorizontalDistance(position, player);
            if (distance < _minimumSpawnDistance || distance > _throwerMaximumDistance || IsCrowded(self, position))
                continue;
            return true;
        }
        position = default;
        return false;
    }

    /// <summary>position과 다른 활성 투척수의 수평 거리가 최소 간격보다 가까우면 true를 반환한다. self는 제외한다.</summary>
    private bool IsCrowded(BambooThrower self, Vector3 position)
    {
        foreach (BambooThrower thrower in _activeThrowers)
        {
            if (thrower != self && HorizontalDistance(thrower.transform.position, position) < _throwerSeparation)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 숲 가장자리 반경의 무작위 위치 중 NavMesh 위이고 플레이어까지 완전한 경로가 있으며 최소 거리 이상인 곳을 찾는다.
    /// 찾으면 position을 채우고 true를 반환하며 모든 시도가 실패하면 false를 반환한다.
    /// </summary>
    private bool TryFindMeleePosition(out Vector3 position)
    {
        Vector3 player = Game.Player.transform.position;
        for (int attempt = 0; attempt < RANDOM_MELEE_POSITION_ATTEMPTS; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(_meleeSpawnMinimumRadius, Mathf.Max(_meleeSpawnMinimumRadius, _meleeSpawnMaximumRadius));
            Vector3 desired = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (!TryValidateMeleePosition(desired, player, out position))
                continue;
            _reportedMeleePositionFailure = false;
            return true;
        }

        if (TryFindFallbackMeleePosition(player, out position))
        {
            _reportedMeleePositionFailure = false;
            return true;
        }

        position = default;
        if (!_reportedMeleePositionFailure)
        {
            Debug.LogWarning("근접 적 스폰 위치를 찾지 못했습니다. NavMesh 베이크와 플레이어까지의 경로, 스폰 반경 설정을 확인하세요.", this);
            _reportedMeleePositionFailure = true;
        }
        return false;
    }

    /// <summary>
    /// 무작위 위치를 모두 놓쳤을 때 설정 반경의 고정 각도·고리 후보를 검색한다.
    /// player까지 완전한 NavMesh 경로가 있는 후보를 position에 담고 성공 여부를 반환한다.
    /// </summary>
    private bool TryFindFallbackMeleePosition(Vector3 player, out Vector3 position)
    {
        float minimumRadius = Mathf.Max(0f, _meleeSpawnMinimumRadius);
        float maximumRadius = Mathf.Max(minimumRadius, _meleeSpawnMaximumRadius);
        for (int radiusIndex = 0; radiusIndex < FALLBACK_MELEE_RADIUS_COUNT; radiusIndex++)
        {
            float radiusBlend = radiusIndex / (float)(FALLBACK_MELEE_RADIUS_COUNT - 1);
            float radius = Mathf.Lerp(minimumRadius, maximumRadius, radiusBlend);
            for (int angleIndex = 0; angleIndex < FALLBACK_MELEE_ANGLE_COUNT; angleIndex++)
            {
                float angle = angleIndex * (Mathf.PI * 2f / FALLBACK_MELEE_ANGLE_COUNT);
                Vector3 desired = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (TryValidateMeleePosition(desired, player, out position))
                    return true;
            }
        }

        position = default;
        return false;
    }

    /// <summary>
    /// desired를 NavMesh에 투영한 뒤 최소 플레이어 거리와 완전한 경로를 확인한다.
    /// 유효한 표본을 position에 담고 성공 여부를 반환한다.
    /// </summary>
    private bool TryValidateMeleePosition(Vector3 desired, Vector3 player, out Vector3 position)
    {
        if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, MELEE_POSITION_SAMPLE_RADIUS, NavMesh.AllAreas)
            || HorizontalDistance(hit.position, player) < _minimumSpawnDistance
            || !NavMesh.CalculatePath(hit.position, player, NavMesh.AllAreas, _meleePath)
            || _meleePath.status != NavMeshPathStatus.PathComplete)
        {
            position = default;
            return false;
        }

        position = hit.position;
        return true;
    }

    /// <summary>플레이어와 radius 수평 거리 안의 활성 근접 적이 공격 전조 중이면 true를 반환한다.</summary>
    internal bool IsMeleeTelegraphNearPlayer(float radius)
    {
        Vector3 player = Game.Player.transform.position;
        foreach (MeleeUnit unit in _activeMelee)
        {
            EnemyAttackBehaviour attack = unit.Selector.CurrentAttack;
            if (attack != null && attack.Phase == EnemyAttackPhase.Telegraph
                && HorizontalDistance(unit.Root.transform.position, player) <= radius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// position에서 수평 거리가 가장 가까운 생존 근접 적을 nearest로 출력하고 찾았는지 반환한다.
    /// 활성 근접 적 목록만 읽으며 적 상태는 변경하지 않는다.
    /// </summary>
    internal bool TryFindNearestMelee(Vector3 position, out EnemyHealth nearest)
    {
        nearest = null;
        float closest = float.PositiveInfinity;
        foreach (MeleeUnit unit in _activeMelee)
        {
            if (!unit.Health.IsAlive)
                continue;
            float distance = HorizontalDistance(unit.Root.transform.position, position);
            if (distance >= closest)
                continue;
            closest = distance;
            nearest = unit.Health;
        }
        return nearest != null;
    }

    /// <summary>
    /// position에서 반사할 활성 근접 적을 찾는다.
    /// viewCamera 화면에 몸체가 걸치고 perceptionMask로 인지된 적 중
    /// 가장 가까운 적을 nearest에 담아 성공 여부를 반환한다.
    /// </summary>
    internal bool TryFindDetectedMelee(Vector3 position, Camera viewCamera, SensoryPerceptionMask perceptionMask, out EnemyHealth nearest)
    {
        nearest = null;
        float closest = float.PositiveInfinity;

        Matrix4x4 viewProjection = viewCamera.projectionMatrix * viewCamera.worldToCameraMatrix;
        GeometryUtility.CalculateFrustumPlanes(viewProjection, _reflectCameraPlanes);

        foreach (MeleeUnit unit in _activeMelee)
        {
            if (!unit.Health.IsAlive)
                continue;

            Collider hitCollider = unit.Health.CreateCombatTarget().HitCollider;
            Bounds bounds = hitCollider.bounds;

            if (!GeometryUtility.TestPlanesAABB(_reflectCameraPlanes, bounds))
                continue;
            if (perceptionMask.GetVisibility(bounds.center) <= 0f)
                continue;

            float distance = HorizontalDistance(unit.Root.transform.position, position);
            if (distance >= closest)
                continue;

            closest = distance;
            nearest = unit.Health;
        }

        return nearest != null;
    }



    /// <summary>
    /// 투척수 프리팹에 인간형 사망음을 연결한 뒤 비활성 상태로 생성하고 디스폰 사건을 구독한다.
    /// 생성한 투척수를 전체 목록에 보관하고 반환한다.
    /// </summary>
    private BambooThrower CreateThrower()
    {
        BambooThrower thrower = Instantiate(_throwerPrefab, transform.position, Quaternion.identity, _unitRoot);

        EnemyDamageAudio audio = thrower.GetComponent<EnemyDamageAudio>();
        if (audio == null) audio = thrower.gameObject.AddComponent<EnemyDamageAudio>();
        audio.ConfigureDeathSound(SoundId.HumanEnemyDie);
        thrower.gameObject.SetActive(false);
        thrower.Despawned += HandleThrowerDespawned;
        _allThrowers.Add(thrower);

        return thrower;
    }

    /// <summary>
    /// 디스폰한 thrower를 활성 목록에서 제거하고 재사용 풀에 반환한다.
    /// 다음 보충 스폰 시각을 갱신하며 처치 수는 증가시키지 않는다.
    /// </summary>
    private void HandleThrowerDespawned(BambooThrower thrower)
    {
        if (!_activeThrowers.Remove(thrower))
            return;

        if (_throwerSpawnRecords.TryGetValue(thrower, out ThrowerSpawnRecord spawnRecord))
        {
            _throwerSpawnRecords.Remove(thrower);
            string reason = _waveStopped ? "wave_stopped" : "throw_cycle_completed";
            EnemyDespawned?.Invoke(spawnRecord.EnemyId, "Thrower",
                Time.realtimeSinceStartupAsDouble - spawnRecord.SpawnedAt, thrower.transform.position, reason);
        }

        _idleThrowers.Push(thrower);
        _nextThrowerSpawnAt = Mathf.Max(_nextThrowerSpawnAt, Time.time + _throwerSpawnInterval);
    }

    /// <summary>현재 스폰 순번의 근접/Beast 확률을 계산해 선택 종류와 두 확률을 반환한다.</summary>
    private bool SelectSpawnType(out float meleeProbability, out float beastProbability)
    {
        if (_beastPrefab == null)
        {
            meleeProbability = 1f;
            beastProbability = 0f;
            return false;
        }

        int startCount = Mathf.Max(0, _weightBlendStartSpawnCount);
        int endCount = Mathf.Max(startCount + 1, _weightBlendEndSpawnCount);
        float blend = Mathf.InverseLerp(startCount, endCount, _meleeSpawned);
        float meleeWeight = Mathf.Lerp(_meleeWeightAtStart, _meleeWeightAtEnd, blend);
        float beastWeight = Mathf.Lerp(_beastWeightAtStart, _beastWeightAtEnd, blend);
        float totalWeight = meleeWeight + beastWeight;
        meleeProbability = totalWeight > 0f ? meleeWeight / totalWeight : 1f;
        beastProbability = totalWeight > 0f ? beastWeight / totalWeight : 0f;
        if (beastWeight <= 0f || totalWeight <= 0f)
            return false;
        if (meleeWeight <= 0f)
            return true;

        return Random.value < beastWeight / totalWeight;
    }

    /// <summary>isBeast에 맞는 대기 풀에서 적을 꺼내고 풀이 비었으면 해당 프리팹을 생성해 반환한다.</summary>
    private MeleeUnit GetOrCreateMelee(bool isBeast)
    {
        Stack<MeleeUnit> idlePool = isBeast ? _idleBeasts : _idleMelee;
        return idlePool.Count > 0 ? idlePool.Pop() : CreateMelee(isBeast);
    }

    /// <summary>isBeast에 해당하는 프리팹을 비활성 상태로 생성하고 Beast 전용 사망음을 설정한다. 체력 사망 사건을 구독한 유닛을 반환한다.</summary>
    private MeleeUnit CreateMelee(bool isBeast)
    {
        GameObject prefab = isBeast ? _beastPrefab : _meleePrefab;
        GameObject root = Instantiate(prefab, transform.position, Quaternion.identity, _unitRoot);
        root.SetActive(false);
        if (isBeast)
            root.GetComponent<EnemyDamageAudio>().ConfigureDeathSound(_beastDeathSound);
        root.GetComponent<FootstepEmitter>().SetGroundFootprintsVisible(_showMeleeGroundFootprints);
        MeleeUnit unit = new MeleeUnit(root, isBeast);
        _meleeByHealth.Add(unit.Health, unit);
        unit.Health.Died += HandleMeleeDied;
        return unit;
    }

    /// <summary>unit을 Beast 여부에 맞는 비활성 재사용 풀에 보관한다.</summary>
    private void ReturnMeleeToPool(MeleeUnit unit)
    {
        Stack<MeleeUnit> idlePool = unit.IsBeast ? _idleBeasts : _idleMelee;
        idlePool.Push(unit);
    }

    /// <summary>health 사망의 종류와 생존 시간을 기록하고 처치 수를 올린 뒤 비활성 재사용 풀로 옮긴다.</summary>
    private void HandleMeleeDied(EnemyHealth health)
    {
        if (!_meleeByHealth.TryGetValue(health, out MeleeUnit unit) || !_activeMelee.Remove(unit))
            return;
        EnemyDefeated?.Invoke(unit.TelemetryId, unit.IsBeast ? "Beast" : "Melee",
            Time.realtimeSinceStartupAsDouble - unit.TelemetrySpawnedAt, health.transform.position);
        EnemyKilled?.Invoke();
        ReturnMeleeToPool(unit);
    }

    /// <summary>first와 second의 높이를 제외한 수평 거리를 반환한다.</summary>
    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}
