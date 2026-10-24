using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 발사한 초승달 검기를 직선으로 이동시키며 경로상의 모든 생존 적에게 한 번씩 고정 피해를 주거나 처치한다.
/// 지형과 플레이어는 통과하고 최대 거리에 도달하면 사라진다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class DefenseSwordWave : MonoBehaviour
{
    private const string PATTERN_ID = "DefenseSwordWave";

    [Header("비행")]
    [SerializeField, Min(0.1f)] private float _speed = 40f;
    [SerializeField, Min(1f)] private float _maxDistance = 45f;
    [SerializeField, Min(0.05f)] private float _hitRadius = 0.7f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField, Min(0f), Tooltip("닿은 적에게 주는 고정 피해. 0이면 남은 체력 전체를 줘 처치한다.")]
    private float _damage;
    [SerializeField, Tooltip("켜면 슬로모션과 상관없이 실시간으로 날아간다. 일시정지 중에는 멈춘다. 끄면 기존처럼 게임 시간이다.")]
    private bool _unscaledTime;
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly HashSet<EnemyHealth> _struck = new HashSet<EnemyHealth>();
    private readonly HashSet<EnemyHealth> _blocked = new HashSet<EnemyHealth>();
    private SharedDamageHitTracker _hitTracker;
    private GameObject _owner;
    private int _attackId;
    private Vector3 _direction;
    private float _travelled;
    private bool _flying;

    [Header("형태")]
    [SerializeField, Min(0.1f)] private float _arcRadius = 0.9f;
    [SerializeField, Range(10f, 170f)] private float _arcAngle = 120f;
    [SerializeField, Min(0.01f)] private float _arcThickness = 0.14f;
    [SerializeField, Range(4, 64)] private int _segments = 24;
    [SerializeField, Min(1f)] private float _endScale = 1.8f;
    [SerializeField, Tooltip("켜면 초승달 가운데를 위치에 맞춰 판정 위치와 보이는 검기가 겹친다. 끄면 기존처럼 호의 중심이 위치에 온다.")]
    private bool _centerArc;
    private Mesh _mesh;

    [Header("소리")]
    [SerializeField] private SoundId _launchSound = SoundId.SwordSwing;

    public float Damage => _damage;
    public float HitRadius => _hitRadius;

    void Awake()
    {
        _mesh = CreateCrescentMesh();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    void Update()
    {
        if (!_flying)
            return;

        float deltaTime = _unscaledTime ? (Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f) : Time.deltaTime;
        float step = Mathf.Min(_speed * deltaTime, _maxDistance - _travelled);
        if (step > 0f)
        {
            StrikeAlong(step);
            transform.position += _direction * step;
            _travelled += step;
        }
        transform.localScale = Vector3.one * Mathf.Lerp(1f, _endScale, _travelled / _maxDistance);
        if (_travelled >= _maxDistance - 0.001f)
        {
            _hitTracker.ResolvePendingHits();
            _flying = false;
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        MultiKillAttackRuntime.CompleteAttack(_attackId);
        Destroy(_mesh);
    }

    /// <summary>
    /// owner 소유의 검기를 origin에서 direction으로 발사하고 발사음을 재생한다.
    /// 이동 거리와 처치 기록을 초기화하며 이후 Update에서 경로를 검사한다.
    /// </summary>
    public void Launch(GameObject owner, Vector3 origin, Vector3 direction)
    {
        _struck.Clear();
        _blocked.Clear();
        Launch(owner, origin, direction, new SharedDamageHitTracker(_struck, _blocked));
    }

    /// <summary>
    /// owner 소유의 검기를 origin에서 direction으로 발사하고 발사음을 재생한다.
    /// sharedStruck에 이미 있는 공유 체력 묶음은 맞히지 않고, 막힌 적은 이 검기 안에서 한 번만 처리한다.
    /// </summary>
    public void Launch(GameObject owner, Vector3 origin, Vector3 direction, HashSet<EnemyHealth> sharedStruck)
    {
        _blocked.Clear();
        Launch(owner, origin, direction, new SharedDamageHitTracker(sharedStruck, _blocked));
    }

    /// <summary>
    /// owner의 검기를 origin에서 direction으로 발사하고 sharedStruck와 sharedBlocked에 이미 처리한 적을 공유한다.
    /// 피해를 받은 공유 체력 묶음과 피해를 막은 개별 적을 기록해 돌진과 검기가 같은 대상을 중복 처리하지 않게 한다.
    /// </summary>
    public void Launch(GameObject owner, Vector3 origin, Vector3 direction,
        HashSet<EnemyHealth> sharedStruck, HashSet<EnemyHealth> sharedBlocked)
    {
        Launch(owner, origin, direction, new SharedDamageHitTracker(sharedStruck, sharedBlocked));
    }

    /// <summary>
    /// owner의 검기를 origin에서 direction으로 발사하고 hitTracker의 돌진 피격·보류 기록을 이어받는다.
    /// 비행 중 맞은 공유 체력 묶음을 같은 기록에 추가한다.
    /// </summary>
    internal void Launch(GameObject owner, Vector3 origin, Vector3 direction, SharedDamageHitTracker hitTracker)
    {
        Launch(owner, origin, direction, hitTracker, 0);
    }

    /// <summary>owner의 검기를 origin에서 발사하고 hitTracker와 attackId를 비행 중 지연 처치까지 유지한다.</summary>
    internal void Launch(GameObject owner, Vector3 origin, Vector3 direction,
        SharedDamageHitTracker hitTracker, int attackId)
    {
        _hitTracker = hitTracker;
        _owner = owner;
        _attackId = attackId;
        _direction = direction.normalized;
        _travelled = 0f;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(_direction));
        transform.localScale = Vector3.one;
        _flying = true;
        if (_launchSound != SoundId.None)
            Managers.Sound.PlayAttached(_launchSound, owner);
    }

    /// <summary>
    /// 현재 위치에서 step만큼의 구간을 반경 검사하여 생존 적마다 _damage만큼, 0이면 남은 체력 전체의 피해를 추적기에 요청한다.
    /// 공유 체력의 후순위 피격은 우선 대상 확인까지 보류하며, 적이 아닌 접촉은 무시해 관통 이동을 유지한다. 같은 구간의 적대 수리검은 없앤다.
    /// </summary>
    private void StrikeAlong(float step)
    {
        DefenseShuriken.ReleaseInSweep(transform.position, transform.position + _direction * step, _hitRadius);
        EnemyProjectile.ReleaseInSweep(transform.position, transform.position + _direction * step, _hitRadius);
        TwinBossProjectile.ReleaseInSweep(transform.position, transform.position + _direction * step, _hitRadius);

        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits,
            step, _hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            EnemyHealth enemy = _hits[i].collider.GetComponentInParent<EnemyHealth>();
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 point = _hits[i].distance > 0f ? _hits[i].point : _hits[i].collider.ClosestPoint(transform.position);
            float amount = _damage > 0f ? _damage : Mathf.Max(enemy.CurrentHealth, 1f);
            _hitTracker.ProcessHit(enemy, new DamageContext(amount, _owner, point, _direction,
                PATTERN_ID, _attackId, 0, BlindBlade.Combat.Contracts.IncomingAttackKind.Unknown));
        }
    }

    /// <summary>
    /// 진행 방향에 수직인 평면의 초승달 양면 메시를 생성해 반환한다.
    /// 호 반경·각도·두께·분할 수를 사용하며 양 끝은 두께를 줄여 칼날 형태로 만든다. _centerArc이면 초승달 가운데를 원점으로 옮긴다.
    /// </summary>
    private Mesh CreateCrescentMesh()
    {
        int vertexCount = (_segments + 1) * 2;
        Vector3[] vertices = new Vector3[vertexCount * 2];
        int[] triangles = new int[_segments * 12];
        float halfAngle = _arcAngle * 0.5f * Mathf.Deg2Rad;
        Vector3 center = _centerArc ? Vector3.up * (_arcRadius * (1f + Mathf.Cos(halfAngle)) * 0.5f) : Vector3.zero;
        for (int i = 0; i <= _segments; i++)
        {
            float progress = i / (float)_segments;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, progress);
            // 중앙이 가장 두껍고 양 끝으로 갈수록 얇아진다.
            float thickness = _arcThickness * Mathf.Sin(progress * Mathf.PI);
            Vector3 direction = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
            vertices[i * 2] = direction * _arcRadius - center;
            vertices[i * 2 + 1] = direction * (_arcRadius - thickness) - center;
            vertices[vertexCount + i * 2] = vertices[i * 2];
            vertices[vertexCount + i * 2 + 1] = vertices[i * 2 + 1];
        }
        for (int i = 0; i < _segments; i++)
        {
            int vertex = i * 2;
            int index = i * 12;
            triangles[index] = vertex;
            triangles[index + 1] = vertex + 2;
            triangles[index + 2] = vertex + 1;
            triangles[index + 3] = vertex + 1;
            triangles[index + 4] = vertex + 2;
            triangles[index + 5] = vertex + 3;
            triangles[index + 6] = vertexCount + vertex;
            triangles[index + 7] = vertexCount + vertex + 1;
            triangles[index + 8] = vertexCount + vertex + 2;
            triangles[index + 9] = vertexCount + vertex + 1;
            triangles[index + 10] = vertexCount + vertex + 3;
            triangles[index + 11] = vertexCount + vertex + 2;
        }
        Mesh mesh = new Mesh { name = "Defense Sword Wave" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>
/// 같은 돌진과 검기에서 공유 체력 묶음의 피해를 한 번만 적용한다.
/// 우선 피격 대상이 있는 묶음은 다른 적의 첫 접촉을 보류해 우선 대상도 실제로 맞으면 그 대상의 피격 사건을 발생시킨다.
/// </summary>
internal sealed class SharedDamageHitTracker
{
    private readonly struct PendingHit
    {
        internal readonly EnemyHealth Enemy;
        internal readonly DamageContext Context;

        /// <summary>enemy와 첫 접촉의 context를 보관해 우선 대상이 끝내 맞지 않았을 때 사용한다.</summary>
        internal PendingHit(EnemyHealth enemy, DamageContext context)
        {
            Enemy = enemy;
            Context = context;
        }
    }

    private readonly HashSet<EnemyHealth> _struck;
    private readonly HashSet<EnemyHealth> _blocked;
    private readonly Dictionary<EnemyHealth, PendingHit> _pending = new Dictionary<EnemyHealth, PendingHit>();

    /// <summary>struck와 blocked를 돌진·검기 사이에 공유하고, 우선 대상 대기 기록을 비운 상태로 시작한다.</summary>
    public SharedDamageHitTracker(HashSet<EnemyHealth> struck, HashSet<EnemyHealth> blocked)
    {
        _struck = struck;
        _blocked = blocked;
    }

    /// <summary>
    /// 실제 판정에 들어온 enemy의 context를 처리한다. 공유 체력 우선 대상이 있으면 다른 적의 접촉은 보류한다.
    /// 우선 대상이 맞으면 그 대상에게 한 번 피해를 주고, 무적이면 개별 차단 후 보류한 상대를 처리한다.
    /// </summary>
    public void ProcessHit(EnemyHealth enemy, DamageContext context)
    {
        if (enemy == null || !enemy.IsAlive || _blocked.Contains(enemy)) return;
        EnemyHealth key = enemy.SharedDamageKey;
        if (_struck.Contains(key)) return;

        if (enemy.IsInvulnerable)
        {
            _blocked.Add(enemy);
            enemy.TakeDamage(context);
            if (enemy.HasSharedDamagePriority) ResolvePendingHit(key);
            return;
        }

        if (enemy.DefersSharedDamage)
        {
            if (!_pending.ContainsKey(key)) _pending.Add(key, new PendingHit(enemy, context));
            return;
        }

        _pending.Remove(key);
        _struck.Add(key);
        enemy.TakeDamage(context);
    }

    /// <summary>일섬이 끝날 때까지 우선 대상이 실제로 맞지 않은 묶음에 보류된 첫 피격을 적용하고 대기 기록을 비운다.</summary>
    public void ResolvePendingHits()
    {
        foreach (KeyValuePair<EnemyHealth, PendingHit> pair in _pending)
        {
            if (_struck.Contains(pair.Key) || !pair.Value.Enemy.IsAlive) continue;
            _struck.Add(pair.Key);
            pair.Value.Enemy.TakeDamage(pair.Value.Context);
        }
        _pending.Clear();
    }

    /// <summary>무적인 우선 대상 대신 key 묶음의 보류된 적에게 첫 피격을 적용하고 대기 기록에서 제거한다.</summary>
    private void ResolvePendingHit(EnemyHealth key)
    {
        if (!_pending.TryGetValue(key, out PendingHit pending)) return;
        _pending.Remove(key);
        if (!pending.Enemy.IsAlive) return;
        _struck.Add(key);
        pending.Enemy.TakeDamage(pending.Context);
    }
}
