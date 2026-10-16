using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using Random = UnityEngine.Random;

/// <summary>방어 적의 시간별 생성·배치·씬 단위 재사용을 담당하며 확정된 처치를 알린다.</summary>
[DisallowMultipleComponent]
public sealed class DefenseEnemySpawner : MonoBehaviour
{
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
    [SerializeField, Min(0), InspectorName("투척수 시작 수 (명)")] private int _initialThrowers = 3;
    [SerializeField, Min(0f), Tooltip("분당 늘어나는 목표 투척수"), InspectorName("분당 목표 투척수 증가 (명/분)")]
    private float _throwersPerMinute = 3f;
    [SerializeField, Min(1), Tooltip("성능 보호용 최대 투척수"), InspectorName("투척수 동시 생존 상한 (명)")]
    private int _maximumThrowers = 12;
    [SerializeField, Min(0f), InspectorName("투척수 추가 간격 (초)")] private float _throwerSpawnInterval = 1.33f;
    private readonly List<BambooThrower> _activeThrowers = new List<BambooThrower>();
    private readonly Stack<BambooThrower> _idleThrowers = new Stack<BambooThrower>();
    private float _nextThrowerSpawnAt;

    [Header("근접 적")]
    [SerializeField] private GameObject _meleePrefab;
    [SerializeField, Min(0f), Tooltip("근접 적이 나타나는 최소 반경. 숲 안쪽 가장자리 근처로 설정한다.")]
    private float _meleeSpawnMinimumRadius = 24f;
    [SerializeField, Min(0f), Tooltip("근접 적이 나타나는 최대 반경. 플레이어까지 경로가 있는 숲 안 위치만 사용한다.")]
    private float _meleeSpawnMaximumRadius = 28.6f;
    [SerializeField, Min(0f), InspectorName("준비 종료 후 근접 등장 지연 (초)")] private float _meleeStartTime = 5f;
    [SerializeField, Min(0.01f), Tooltip("근접 적 등장 시작 시점의 분당 스폰 수"), InspectorName("근접 시작 스폰율 (회/분)")]
    private float _meleeSpawnsPerMinute = 15f;
    [SerializeField, Min(0f), Tooltip("1분마다 늘어나는 분당 스폰 수"), InspectorName("분당 근접 스폰율 증가")]
    private float _meleeSpawnGrowthPerMinute = 9f;
    [SerializeField, Min(0f), Tooltip("이 시간 이후 돌진형 근접 적을 섞는다."), InspectorName("돌진형 등장 생존 시간 (초)")]
    private float _chargeTypeStartTime = 90f;
    [SerializeField, Range(0f, 1f), InspectorName("돌진형 선택 확률")] private float _chargeTypeChance = 0.4f;
    [SerializeField, Min(1), Tooltip("성능 보호용 최대 근접 적 수"), InspectorName("근접 동시 생존 상한 (명)")]
    private int _maximumMelee = 30;
    private readonly List<MeleeUnit> _activeMelee = new List<MeleeUnit>();
    private readonly Stack<MeleeUnit> _idleMelee = new Stack<MeleeUnit>();
    private readonly Dictionary<EnemyHealth, MeleeUnit> _meleeByHealth = new Dictionary<EnemyHealth, MeleeUnit>();
    private NavMeshPath _meleePath;
    private float _nextMeleeSpawnAt;
    private int _meleeSpawnLimit;
    private int _meleeSpawned;
    private readonly Plane[] _reflectCameraPlanes = new Plane[6];

    [Header("세션 연결")]
    private BambooDefenseDirector _director;
    private readonly List<BambooThrower> _allThrowers = new List<BambooThrower>();
    private bool _waveStopped;
    public event Action EnemyKilled;

    private sealed class MeleeUnit
    {
        public GameObject Root { get; }
        public EnemyHealth Health { get; }
        public EnemyController Controller { get; }
        public EnemyAttackSelector Selector { get; }
        public EnemyChargeAttack Charge { get; }

        /// <summary>root에서 근접 적의 체력·추적·공격 선택·돌진 컴포넌트를 찾아 보관한다.</summary>
        public MeleeUnit(GameObject root)
        {
            Root = root;
            Health = root.GetComponent<EnemyHealth>();
            Controller = root.GetComponent<EnemyController>();
            Selector = root.GetComponent<EnemyAttackSelector>();
            Charge = root.GetComponent<EnemyChargeAttack>();
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

    /// <summary>now를 기준으로 준비 종료 후 첫 근접 스폰 시각을 설정한다.</summary>
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
            unit.Root.SetActive(false);
            _idleMelee.Push(unit);
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
    /// 생존 시간에 비례하는 목표 투척수보다 적으면 스폰 간격마다 빈 위치에 투척수를 하나 배치한다.
    /// 목표 수는 시작 수에 분당 증가량을 선형으로 더하고 성능 보호 상한으로 제한한다.
    /// </summary>
    private void UpdateThrowers(float elapsed)
    {
        int target = Mathf.Min(_maximumThrowers,
            _initialThrowers + Mathf.FloorToInt(elapsed / 60f * _throwersPerMinute));
        if (_activeThrowers.Count >= target || Time.time < _nextThrowerSpawnAt)
            return;
        if (!TryFindThrowerPosition(null, out Vector3 position))
            return;

        BambooThrower thrower = _idleThrowers.Count > 0 ? _idleThrowers.Pop() : CreateThrower();
        _activeThrowers.Add(thrower);
        thrower.Activate(_director, position);
        _nextThrowerSpawnAt = Time.time + _throwerSpawnInterval;
    }

    /// <summary>
    /// 근접 적 스폰 시각이 되면 다음 시각을 생존 시간에 비례해 당기고 숲 가장자리의 도달 가능한 위치에 근접 적을 배치한다.
    /// 분당 스폰 수는 시작값에 분당 증가량을 선형으로 더하며 동시 생존 상한을 넘으면 이번 스폰을 건너뛴다.
    /// 총 생성 제한이 있으면 _meleeSpawned가 제한에 닿은 뒤로는 더 생성하지 않는다.
    /// </summary>
    private void UpdateMelee(float elapsed)
    {
        if (_meleePrefab == null || Time.time < _nextMeleeSpawnAt
            || (_meleeSpawnLimit > 0 && _meleeSpawned >= _meleeSpawnLimit))
            return;
        float minutes = Mathf.Max(0f, elapsed - _meleeStartTime) / 60f;
        float spawnsPerMinute = _meleeSpawnsPerMinute + _meleeSpawnGrowthPerMinute * minutes;
        _nextMeleeSpawnAt = Time.time + 60f / Mathf.Max(0.01f, spawnsPerMinute);
        if (_activeMelee.Count >= _maximumMelee || !TryFindMeleePosition(out Vector3 position))
            return;

        MeleeUnit unit = _idleMelee.Count > 0 ? _idleMelee.Pop() : CreateMelee();
        bool chargeType = elapsed >= _chargeTypeStartTime && Random.value < _chargeTypeChance;
        Vector3 toCenter = Vector3.ProjectOnPlane(transform.position - position, Vector3.up);
        unit.Root.transform.SetPositionAndRotation(position,
            toCenter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toCenter) : Quaternion.identity);
        unit.Charge.enabled = chargeType;
        unit.Root.SetActive(true);
        unit.Controller.SetTarget(Game.Player.transform);
        _activeMelee.Add(unit);
        _meleeSpawned++;
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
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(_meleeSpawnMinimumRadius, Mathf.Max(_meleeSpawnMinimumRadius, _meleeSpawnMaximumRadius));
            Vector3 desired = transform.position + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
                || HorizontalDistance(hit.position, player) < _minimumSpawnDistance
                || !NavMesh.CalculatePath(hit.position, player, NavMesh.AllAreas, _meleePath)
                || _meleePath.status != NavMeshPathStatus.PathComplete)
                continue;
            position = hit.position;
            return true;
        }
        position = default;
        return false;
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
    /// 투척수 프리팹을 비활성 상태로 생성하고 디스폰 사건을 구독한다.
    /// 생성한 투척수를 전체 목록에 보관하고 반환한다.
    /// </summary>
    private BambooThrower CreateThrower()
    {
        BambooThrower thrower = Instantiate(_throwerPrefab, transform.position, Quaternion.identity, _unitRoot);

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

        _idleThrowers.Push(thrower);
        _nextThrowerSpawnAt = Mathf.Max(_nextThrowerSpawnAt, Time.time + _throwerSpawnInterval);
    }

    /// <summary>근접 적 프리팹을 비활성 상태로 생성하고 구성 요소와 사망 사건 구독을 등록해 반환한다.</summary>
    private MeleeUnit CreateMelee()
    {
        GameObject root = Instantiate(_meleePrefab, transform.position, Quaternion.identity, _unitRoot);
        root.SetActive(false);
        MeleeUnit unit = new MeleeUnit(root);
        _meleeByHealth.Add(unit.Health, unit);
        unit.Health.Died += HandleMeleeDied;
        return unit;
    }

    /// <summary>health의 근접 적 사망을 처치 수에 더하고 다음 재사용을 위해 대기 목록으로 옮긴다.</summary>
    private void HandleMeleeDied(EnemyHealth health)
    {
        if (!_meleeByHealth.TryGetValue(health, out MeleeUnit unit) || !_activeMelee.Remove(unit))
            return;
        EnemyKilled?.Invoke();
        _idleMelee.Push(unit);
    }

    /// <summary>first와 second의 높이를 제외한 수평 거리를 반환한다.</summary>
    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}
