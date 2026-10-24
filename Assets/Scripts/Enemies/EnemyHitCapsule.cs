using System.Collections.Generic;

using UnityEngine;


/// <summary>충돌체 없는 적 캡슐과 이동하는 구·캡슐 사이의 접촉 결과다.</summary>
public readonly struct EnemyCapsuleHit
{
    public EnemyHitCapsule Capsule { get; }
    public float Distance { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }

    /// <summary>capsule, 이동 거리 distance, 접촉 위치 point, 적 표면 법선 normal로 접촉 결과를 만든다.</summary>
    public EnemyCapsuleHit(EnemyHitCapsule capsule, float distance, Vector3 point, Vector3 normal)
    {
        Capsule = capsule;
        Distance = distance;
        Point = point;
        Normal = normal;
    }
}

/// <summary>
/// 적의 피격·몸 캡슐을 물리 충돌체 없이 보관하고 플레이어 판정에 쓰는 겹침·이동 접촉 계산을 제공한다.
/// 형상과 스케일 규칙은 CapsuleCollider와 같으며, 몸 충돌체를 유지하는 보스·투척수는 그 CapsuleCollider의 크기와 켜짐 상태를 그대로 읽는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyHitCapsule : MonoBehaviour
{
    private const int MAX_SWEEP_ITERATIONS = 32;
    private const float CONTACT_TOLERANCE = 0.00001f;
    private static readonly List<EnemyHitCapsule> _active = new List<EnemyHitCapsule>();

    [Header("피격 캡슐")]
    [SerializeField] private Vector3 _center;
    [SerializeField, Min(0f)] private float _radius = 0.5f;
    [SerializeField, Min(0f)] private float _height = 2f;
    [SerializeField, Range(0, 2), Tooltip("캡슐 축입니다. 0은 X, 1은 Y, 2는 Z입니다.")]
    private int _direction = 1;
    [SerializeField, Tooltip("켜면 플레이어 CharacterController 이동을 이 캡슐 앞에서 코드로 막습니다. 판정 쿼리에는 영향이 없습니다.")]
    private bool _blocksPlayer = true;
    [SerializeField, Tooltip("몸 충돌체를 유지하는 적의 CapsuleCollider입니다. 지정하면 크기·켜짐 상태·레이어를 이 충돌체에서 읽고, 몸 막힘은 물리에 맡깁니다.")]
    private CapsuleCollider _bodyCollider;
    private EnemyHealth _health;

    public static IReadOnlyList<EnemyHitCapsule> Active => _active;
    public EnemyHealth Health => _health;
    public bool HasBodyCollider => _bodyCollider != null;
    public Collider BodyCollider => _bodyCollider;
    public bool BlocksPlayer => _blocksPlayer && _bodyCollider == null;
    /// <summary>기존 충돌체의 enabled와 같은 뜻이다. 몸 충돌체가 있으면 그 충돌체의 enabled를 반환한다.</summary>
    public bool IsEnabled => _bodyCollider != null ? _bodyCollider.enabled : enabled;
    /// <summary>기존 충돌체가 물리 장면에 있던 조건과 같이 켜져 있고 계층에서 활성일 때 true다.</summary>
    public bool IsPresent => IsEnabled && gameObject.activeInHierarchy;

    /// <summary>현재 월드 캡슐을 감싸는 축 정렬 경계 상자를 반환한다. Collider.bounds를 대신한다.</summary>
    public Bounds Bounds
    {
        get
        {
            GetWorldSegment(out Vector3 start, out Vector3 end, out float radius);
            Bounds bounds = new Bounds(start, Vector3.zero);
            bounds.Encapsulate(end);
            bounds.Expand(radius * 2f);
            return bounds;
        }
    }

    void Awake()
    {
        _health = GetComponentInParent<EnemyHealth>();
    }

    void OnEnable()
    {
        _active.Add(this);
    }

    void OnDisable()
    {
        _active.Remove(this);
    }

    /// <summary>layerMask에 기존 충돌체 레이어가 들어 있으면 true를 반환한다.</summary>
    public bool IsInLayerMask(int layerMask)
    {
        int layer = _bodyCollider != null ? _bodyCollider.gameObject.layer : gameObject.layer;
        return (layerMask & (1 << layer)) != 0;
    }

    /// <summary>
    /// point에서 캡슐 표면의 가장 가까운 위치를 반환한다. point가 캡슐 안이면 point를 그대로 반환한다.
    /// Collider.ClosestPoint와 같은 규칙이며 상태는 바꾸지 않는다.
    /// </summary>
    public Vector3 ClosestPoint(Vector3 point)
    {
        GetWorldSegment(out Vector3 start, out Vector3 end, out float radius);
        Vector3 axis = ClosestPointOnSegment(start, end, point);
        Vector3 offset = point - axis;
        float distance = offset.magnitude;
        return distance <= radius ? point : axis + offset * (radius / distance);
    }

    /// <summary>start부터 end까지 반경 radius인 캡슐(같은 점이면 구)이 이 캡슐과 겹치면 true를 반환한다.</summary>
    public bool Overlaps(Vector3 start, Vector3 end, float radius)
    {
        GetWorldSegment(out Vector3 axisStart, out Vector3 axisEnd, out float capsuleRadius);
        ClosestPointsBetweenSegments(start, end, axisStart, axisEnd, out Vector3 moving, out Vector3 axis);
        float reach = radius + capsuleRadius;
        return (moving - axis).sqrMagnitude <= reach * reach;
    }

    /// <summary>
    /// start부터 end까지 반경 radius인 캡슐(같은 점이면 구)을 direction으로 distance만큼 옮길 때 처음 닿는 위치를 hit에 저장한다.
    /// 시작부터 겹치면 물리 SphereCast·CapsuleCast처럼 거리 0, 위치 Vector3.zero, 진행 반대 법선을 반환한다.
    /// 볼록 형상의 거리는 이동 거리에 대해 볼록하므로 뉴턴 반복으로 첫 접촉까지 넘지 않고 다가간다.
    /// </summary>
    public bool Sweep(Vector3 start, Vector3 end, float radius, Vector3 direction, float distance, out EnemyCapsuleHit hit)
    {
        // 물리 캐스트처럼 방향 길이와 관계없이 단위 방향의 이동 거리로 계산한다.
        direction = direction.normalized;
        GetWorldSegment(out Vector3 axisStart, out Vector3 axisEnd, out float capsuleRadius);
        float reach = radius + capsuleRadius;
        float travelled = 0f;
        for (int i = 0; i < MAX_SWEEP_ITERATIONS; i++)
        {
            Vector3 offset = direction * travelled;
            ClosestPointsBetweenSegments(start + offset, end + offset, axisStart, axisEnd,
                out Vector3 moving, out Vector3 axis);
            Vector3 separation = moving - axis;
            float separationLength = separation.magnitude;
            float gap = separationLength - reach;
            if (gap <= CONTACT_TOLERANCE)
            {
                if (i == 0 && gap < 0f)
                {
                    hit = new EnemyCapsuleHit(this, 0f, Vector3.zero, -direction);
                    return true;
                }
                Vector3 normal = separationLength > 0.000001f ? separation / separationLength : -direction;
                hit = new EnemyCapsuleHit(this, travelled, axis + normal * capsuleRadius, normal);
                return true;
            }
            float closing = -Vector3.Dot(direction, separation) / separationLength;
            if (closing <= 0.000001f)
                break;
            travelled += gap / closing;
            if (travelled > distance)
                break;
        }
        hit = default;
        return false;
    }

    /// <summary>
    /// start부터 end까지 반경 radius인 캡슐이 이 캡슐과 겹치면 수평으로만 밀어낼 이동량을 push에 저장하고 true를 반환한다.
    /// 두 캡슐 축의 가장 가까운 점 사이 수직 차이는 유지하고 수평 거리만 늘려 겹침을 없앤다.
    /// </summary>
    public bool TryGetHorizontalPush(Vector3 start, Vector3 end, float radius, out Vector3 push)
    {
        GetWorldSegment(out Vector3 axisStart, out Vector3 axisEnd, out float capsuleRadius);
        ClosestPointsBetweenSegments(start, end, axisStart, axisEnd, out Vector3 moving, out Vector3 axis);
        Vector3 separation = moving - axis;
        float reach = radius + capsuleRadius;
        if (separation.sqrMagnitude >= reach * reach - CONTACT_TOLERANCE)
        {
            push = Vector3.zero;
            return false;
        }
        Vector3 horizontal = Vector3.ProjectOnPlane(separation, Vector3.up);
        float horizontalLength = horizontal.magnitude;
        // 축이 정확히 겹치면 적 중심에서 플레이어 쪽으로 민다.
        Vector3 away = horizontalLength > 0.000001f
            ? horizontal / horizontalLength
            : Vector3.ProjectOnPlane(start - transform.position, Vector3.up).normalized;
        if (away.sqrMagnitude < 0.000001f)
            away = Vector3.forward;
        float required = Mathf.Sqrt(Mathf.Max(0f, reach * reach - separation.y * separation.y));
        push = away * (required - horizontalLength);
        return true;
    }

    /// <summary>
    /// 반경 radius인 캡슐(같은 점이면 구)이 start·end에서 겹치는 활성 적 캡슐을 results에 모은다.
    /// layerMask에 기존 충돌체 레이어가 없거나 꺼진 캡슐은 제외하며, 피해 적용 전에 목록을 다 모으기 위해 사용한다.
    /// </summary>
    public static void OverlapAll(Vector3 start, Vector3 end, float radius, int layerMask, List<EnemyHitCapsule> results)
    {
        results.Clear();
        foreach (EnemyHitCapsule capsule in _active)
        {
            if (capsule.IsPresent && capsule.IsInLayerMask(layerMask) && capsule.Overlaps(start, end, radius))
                results.Add(capsule);
        }
    }

    /// <summary>
    /// 반경 radius인 캡슐(같은 점이면 구)을 start·end에서 direction으로 distance만큼 옮길 때 닿는 활성 적 캡슐을 results에 모은다.
    /// 물리 SphereCastAll·CapsuleCastAll처럼 캡슐마다 첫 접촉 하나를 저장하며 순서는 정하지 않는다.
    /// </summary>
    public static void SweepAll(Vector3 start, Vector3 end, float radius, Vector3 direction, float distance,
        int layerMask, List<EnemyCapsuleHit> results)
    {
        results.Clear();
        foreach (EnemyHitCapsule capsule in _active)
        {
            if (capsule.IsPresent && capsule.IsInLayerMask(layerMask)
                && capsule.Sweep(start, end, radius, direction, distance, out EnemyCapsuleHit hit))
                results.Add(hit);
        }
    }

    /// <summary>
    /// CapsuleCollider와 같은 규칙으로 중심·반지름·높이·축과 lossyScale을 적용한 월드 캡슐 축 양 끝과 반지름을 반환한다.
    /// 높이가 지름보다 작으면 양 끝이 같은 구가 된다.
    /// </summary>
    private void GetWorldSegment(out Vector3 start, out Vector3 end, out float radius)
    {
        Transform source = _bodyCollider != null ? _bodyCollider.transform : transform;
        Vector3 center = _bodyCollider != null ? _bodyCollider.center : _center;
        float localRadius = _bodyCollider != null ? _bodyCollider.radius : _radius;
        float height = _bodyCollider != null ? _bodyCollider.height : _height;
        int direction = _bodyCollider != null ? _bodyCollider.direction : _direction;
        Vector3 scale = source.lossyScale;
        Vector3 axis;
        float axisScale;
        float radiusScale;
        switch (direction)
        {
            case 0:
                axis = source.right;
                axisScale = Mathf.Abs(scale.x);
                radiusScale = Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                break;
            case 2:
                axis = source.forward;
                axisScale = Mathf.Abs(scale.z);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                break;
            default:
                axis = source.up;
                axisScale = Mathf.Abs(scale.y);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                break;
        }
        radius = localRadius * radiusScale;
        float halfSegment = Mathf.Max(0f, height * axisScale * 0.5f - radius);
        Vector3 worldCenter = source.TransformPoint(center);
        start = worldCenter - axis * halfSegment;
        end = worldCenter + axis * halfSegment;
    }

    /// <summary>start부터 end까지의 선분에서 point에 가장 가까운 점을 반환한다.</summary>
    private static Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.000001f)
            return start;
        return start + segment * Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
    }

    /// <summary>
    /// 두 선분 firstStart·firstEnd와 secondStart·secondEnd 사이의 가장 가까운 두 점을 first와 second에 저장한다.
    /// 길이가 0인 선분은 점으로 처리한다.
    /// </summary>
    private static void ClosestPointsBetweenSegments(Vector3 firstStart, Vector3 firstEnd,
        Vector3 secondStart, Vector3 secondEnd, out Vector3 first, out Vector3 second)
    {
        Vector3 firstSegment = firstEnd - firstStart;
        Vector3 secondSegment = secondEnd - secondStart;
        Vector3 startOffset = firstStart - secondStart;
        float firstLength = Vector3.Dot(firstSegment, firstSegment);
        float secondLength = Vector3.Dot(secondSegment, secondSegment);
        float secondProjection = Vector3.Dot(secondSegment, startOffset);
        float s;
        float t;
        if (firstLength < 0.000001f && secondLength < 0.000001f)
        {
            first = firstStart;
            second = secondStart;
            return;
        }
        if (firstLength < 0.000001f)
        {
            s = 0f;
            t = Mathf.Clamp01(secondProjection / secondLength);
        }
        else
        {
            float firstProjection = Vector3.Dot(firstSegment, startOffset);
            if (secondLength < 0.000001f)
            {
                t = 0f;
                s = Mathf.Clamp01(-firstProjection / firstLength);
            }
            else
            {
                float cross = Vector3.Dot(firstSegment, secondSegment);
                float denominator = firstLength * secondLength - cross * cross;
                s = denominator > 0.000001f
                    ? Mathf.Clamp01((cross * secondProjection - firstProjection * secondLength) / denominator)
                    : 0f;
                t = (cross * s + secondProjection) / secondLength;
                // 두 번째 선분 범위를 벗어나면 끝점에 고정하고 첫 번째 선분 위치를 다시 구한다.
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-firstProjection / firstLength);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((cross - firstProjection) / firstLength);
                }
            }
        }
        first = firstStart + firstSegment * s;
        second = secondStart + secondSegment * t;
    }

    /// <summary>
    /// 플레이 시작 시 입력 없이 이전 실행의 활성 캡슐 목록을 비운다.
    /// Domain Reload를 끈 경우에도 파괴된 캡슐 참조를 유지하지 않는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        _active.Clear();
    }
}
