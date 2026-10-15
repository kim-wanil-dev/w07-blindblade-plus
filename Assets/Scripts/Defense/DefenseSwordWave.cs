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
    // 다른 공격과 맞은 적을 함께 셀 때는 넘겨받은 목록을 쓰고, 아니면 자기 목록을 쓴다.
    private HashSet<EnemyHealth> _struckTargets;
    private GameObject _owner;
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
            Destroy(gameObject);
    }

    void OnDestroy()
    {
        Destroy(_mesh);
    }

    /// <summary>
    /// owner 소유의 검기를 origin에서 direction으로 발사하고 발사음을 재생한다.
    /// 이동 거리와 처치 기록을 초기화하며 이후 Update에서 경로를 검사한다.
    /// </summary>
    public void Launch(GameObject owner, Vector3 origin, Vector3 direction)
    {
        _struck.Clear();
        Launch(owner, origin, direction, _struck);
    }

    /// <summary>
    /// owner 소유의 검기를 origin에서 direction으로 발사하고 발사음을 재생한다.
    /// sharedStruck에 이미 있는 적은 맞히지 않고, 새로 맞힌 적은 sharedStruck에 더해 다른 공격과 피해가 겹치지 않게 한다.
    /// </summary>
    public void Launch(GameObject owner, Vector3 origin, Vector3 direction, HashSet<EnemyHealth> sharedStruck)
    {
        _struckTargets = sharedStruck;
        _owner = owner;
        _direction = direction.normalized;
        _travelled = 0f;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(_direction));
        transform.localScale = Vector3.one;
        _flying = true;
        if (_launchSound != SoundId.None)
            Managers.Sound.PlayAttached(_launchSound, owner);
    }

    /// <summary>
    /// 현재 위치에서 step만큼의 구간을 반경 검사하여 처음 닿은 생존 적마다 _damage만큼, 0이면 남은 체력 전체의 피해를 적용한다.
    /// 같은 적은 한 번만 처리하고 적이 아닌 접촉은 무시하여 관통 이동을 유지한다.
    /// </summary>
    private void StrikeAlong(float step)
    {
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits,
            step, _hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            EnemyHealth enemy = _hits[i].collider.GetComponentInParent<EnemyHealth>();
            if (enemy == null || !enemy.IsAlive || !_struckTargets.Add(enemy))
                continue;
            Vector3 point = _hits[i].distance > 0f ? _hits[i].point : _hits[i].collider.ClosestPoint(transform.position);
            float amount = _damage > 0f ? _damage : Mathf.Max(enemy.CurrentHealth, 1f);
            enemy.TakeDamage(new DamageContext(amount, _owner, point, _direction, PATTERN_ID));
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
