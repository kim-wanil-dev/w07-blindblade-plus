using System;
using System.Collections.Generic;

using UnityEngine;


[DisallowMultipleComponent]
public sealed class EnemyCombatService : MonoBehaviour, IPlayerCombatService
{
    [Header("플레이어 공격 판정")]
    [SerializeField] private Transform _playerRoot;
    [SerializeField, Min(0f)] private float _normalDamage = 10f;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.2f;
    [SerializeField] private LayerMask _attackMask = ~0;
    [SerializeField, Tooltip("칼날 길이 대신 시점 방향으로 거리 제한 없이 공격 판정합니다. 벽에 막히는 규칙은 유지합니다.")]
    private bool _unlimitedRange;
    private bool _hasResolution;
    private PlayerAttackResolution _lastResolution;
    private readonly List<PlayerPerceivedTarget> _targetCandidates = new List<PlayerPerceivedTarget>();

    [Header("플레이어 칼날 궤적")]
    [SerializeField] private Transform _bladeBase;
    [SerializeField] private Transform _bladeTip;
    [SerializeField] private bool _useViewModelReach = true;
    [SerializeField, Min(1f), InspectorName("일반 공격 판정 각도 배율")]
    [Tooltip("일반 1·2타의 칼날 판정을 시점 정면 기준 좌우 각도로 이 배율만큼 벌립니다. 보이는 칼, 판정 높이와 공격 시간은 그대로이며 반격에는 적용하지 않습니다.")]
    private float _normalAttackAngleScale = 1.5f;
    [SerializeField, Range(0f, 1f), InspectorName("일반 공격 판정 카메라 쪽 당김")]
    [Tooltip("일반 1·2타 칼날 판정의 손잡이 쪽 끝을 카메라 쪽으로 이 비율만큼 당깁니다. 1이면 카메라 위치에서 시작해 화면 가운데 아주 가까운 적도 벱니다. 칼끝 사거리와 보이는 칼은 그대로입니다.")]
    private float _normalAttackViewPull = 0.7f;
    private KatanaCombatCoordinator _katana;
    private int _sweepActionId = -1;
    private Vector3 _previousBladeBase;
    private Vector3 _previousBladeTip;

    [Header("돌진 수평 부채꼴 베기")]
    [SerializeField, InspectorName("돌진 부채꼴 베기 사용")]
    [Tooltip("좌클릭 대상 돌진 중 돌진 방향의 수평 부채꼴이 돌진 진행도에 맞춰 한쪽 끝에서 반대쪽 끝으로 펴지며 지나간 적을 발도와 같은 판정으로 벱니다.")]
    private bool _useDashFanSweep = true;
    [SerializeField, Min(0f), InspectorName("부채꼴 사거리 (m)")]
    private float _dashFanRange = 4.5f;
    [SerializeField, Range(1f, 360f), InspectorName("부채꼴 각도")]
    private float _dashFanAngle = 120f;
    [SerializeField, Min(0f), InspectorName("근접 판정 반경 (m)")]
    [Tooltip("이 수평 거리 안의 적은 부채꼴이 펴지는 진행과 관계없이 정면 반원 안에 있으면 바로 맞습니다.")]
    private float _dashFanNearbyRadius = 1.5f;
    [SerializeField, InspectorName("오른쪽에서 왼쪽으로 펴기")]
    [Tooltip("켜면 돌진 방향 오른쪽 끝에서 왼쪽 끝으로, 끄면 반대로 부채꼴이 펴집니다.")]
    private bool _dashFanSweepFromRight = true;
    private int _dashSweepActionId = -1;
    private string _dashSweepHitTargetId;
    private bool _dashSweepDeathConfirmed;
    private readonly HashSet<EnemyHealth> _dashSweepStruck = new HashSet<EnemyHealth>();

    [Header("공격 판정 기즈모")]
    [SerializeField] private bool _showAttackGizmos = true;
    [SerializeField, Min(0.1f)] private float _gizmoDuration = 2f;
#if UNITY_EDITOR
    private PlayerAttackRequest _debugRequest;
    private bool _debugBladeSweep;
    private Vector3 _debugPreviousBase;
    private Vector3 _debugPreviousTip;
    private Vector3 _debugCurrentBase;
    private Vector3 _debugCurrentTip;
    private bool _hasDebugRequest;
    private bool _debugContact;
    private bool _debugHit;
    private float _debugUntil;
    private Vector3 _debugDashOrigin;
    private Vector3 _debugDashDirection;
    private float _debugDashFromAngle;
    private float _debugDashToAngle;
    private bool _debugDashHit;
    private float _debugDashUntil;
#endif

    /// <summary>
    /// 새 공격 판정이 끝나면 요청, 최초 접촉체, 접촉 위치, 실제 적 피해 여부를 알린다.
    /// 접촉이 없으면 Collider는 null이며 동일 ActionId의 캐시 조회에는 다시 알리지 않는다.
    /// </summary>
    public event Action<PlayerAttackRequest, Collider, Vector3, bool> AttackContactResolved;

    void Awake()
    {
        _katana = GetComponent<KatanaCombatCoordinator>();
    }

    void OnEnable()
    {
        _hasResolution = false;
    }

    /// <summary>
    /// playerRoot를 공격 발생원 및 자기 충돌 제외 대상으로 연결한다.
    /// 이전 공격 판정 캐시를 초기화한다.
    /// </summary>
    public void ConfigurePlayer(Transform playerRoot)
    {
        _playerRoot = playerRoot;
        _hasResolution = false;
    }

    /// <summary>
    /// targetId로 같은 씬의 생존 적을 찾아 플레이어 전투 대상 정보를 target으로 반환한다.
    /// 등록되지 않은 식별자나 다른 씬의 적이면 false를 반환한다.
    /// </summary>
    public bool TryGetTarget(string targetId, out PlayerCombatTarget target)
    {
        if (EnemyHealth.TryFind(targetId, out EnemyHealth health) && health.gameObject.scene == gameObject.scene)
        {
            target = health.CreateCombatTarget();
            return true;
        }
        target = default;
        return false;
    }

    /// <summary>
    /// collider의 부모 적 체력을 조회하여 같은 씬의 생존 적이면 target을 반환한다.
    /// 자식 Collider도 루트의 동일한 전투 대상으로 연결하며 유효하지 않으면 false를 반환한다.
    /// </summary>
    public bool TryGetTarget(Collider collider, out PlayerCombatTarget target)
    {
        EnemyHealth health = collider != null ? collider.GetComponentInParent<EnemyHealth>() : null;
        if (health != null && health.IsAlive && health.gameObject.scene == gameObject.scene)
        {
            target = health.CreateCombatTarget();
            return true;
        }
        target = default;
        return false;
    }

    /// <summary>현재 씬의 활성 생존 적 중 돌진 허용 대상을 즉시 사용할 돌진 대상 목록으로 반환한다. 기존 감지 제공자가 없을 때 화면 중앙 선택에 사용한다.</summary>
    public IReadOnlyList<PlayerPerceivedTarget> GetCurrentTargets()
    {
        _targetCandidates.Clear();
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        foreach (EnemyHealth enemy in enemies)
        {
            if (!enemy.IsAlive || !enemy.IsDashTargetable || enemy.gameObject.scene != gameObject.scene)
                continue;

            _targetCandidates.Add(new PlayerPerceivedTarget(
                enemy.TargetId,
                enemy.transform.position,
                Time.time,
                float.PositiveInfinity));
        }

        return _targetCandidates;
    }

    /// <summary>
    /// request의 원점·방향·사거리로 가장 가까운 적 또는 장애물을 검사하여 피해·사망 결과를 반환한다.
    /// 즉사 설정이 켜진 일반 적의 IaidoFinisher는 남은 체력 전체를, 보스와 그 외 공격은 설정 피해를 적용한다.
    /// 같은 ActionId는 중복 적용하지 않는다.
    /// 새 판정의 접촉체·위치·적 피해 여부는 AttackContactResolved로 구독자에게 알린다.
    /// </summary>
    public PlayerAttackResolution ResolvePlayerAttack(PlayerAttackRequest request)
    {
        return ResolveAttack(request, true);
    }

    /// <summary>request의 대상 방향·일반 사거리로 반격 접촉을 검사한다. finalSample 또는 접촉에서 피해를 한 번 확정하고 결과를 반환한다.</summary>
    public PlayerAttackResolution ResolvePlayerDirectedAttack(PlayerAttackRequest request, bool finalSample)
    {
        return ResolveAttack(request, finalSample);
    }

    /// <summary>request의 시점 방향 접촉을 검사하고 접촉 또는 finalSample에서 피해·결과·이벤트를 한 번 확정한다.</summary>
    private PlayerAttackResolution ResolveAttack(PlayerAttackRequest request, bool finalSample)
    {
        if (_hasResolution && _lastResolution.ActionId == request.ActionId)
            return _lastResolution;

        PlayerAttackResolution result = new PlayerAttackResolution(request.ActionId, false, null, false);
        Collider collider = null;
        Vector3 point = request.Origin;
        if (isActiveAndEnabled && request.Range > 0f && request.Direction.sqrMagnitude > 0f)
        {
            collider = FindFirstContact(request, out point);
            EnemyHealth health = collider != null ? collider.GetComponentInParent<EnemyHealth>() : null;
            if (health != null && health.IsAlive && health.gameObject.scene == gameObject.scene &&
                (string.IsNullOrEmpty(request.PreferredTargetId) || request.PreferredTargetId == health.TargetId))
            {
                string id = health.TargetId;
                health.TakeDamage(new DamageContext(_normalDamage, _playerRoot.gameObject, point,
                    request.Direction, request.Kind.ToString()));
                result = new PlayerAttackResolution(request.ActionId, true, id, health.CurrentHealth <= 0f);
            }
        }
        bool complete = collider != null || finalSample;
        result = new PlayerAttackResolution(request.ActionId, result.HitConfirmed,
            result.HitTargetId, result.DeathConfirmed, complete);
        if (complete)
        {
            _hasResolution = true;
            _lastResolution = result;
        }
#if UNITY_EDITOR
        _debugBladeSweep = false;
        _debugRequest = request;
        _debugContact = collider != null;
        _debugHit = result.HitConfirmed;
        _hasDebugRequest = request.Range > 0f && request.Direction.sqrMagnitude > 0f;
        _debugUntil = Time.time + _gizmoDuration;
#endif
        if (complete) AttackContactResolved?.Invoke(request, collider, point, result.HitConfirmed);
        return result;
    }

    /// <summary>
    /// request의 시작점 겹침과 이동 구간을 검사하여 자기 Collider를 제외한 최초 접촉체와 point를 반환한다.
    /// 벽도 후보에 포함하여 벽 뒤의 적에게 피해가 전달되지 않게 하며 없으면 null을 반환한다.
    /// </summary>
    private Collider FindFirstContact(PlayerAttackRequest request, out Vector3 point)
    {
        point = request.Origin;
        Collider closestOverlap = null;
        float overlapDistance = float.PositiveInfinity;
        foreach (Collider overlap in Physics.OverlapSphere(request.Origin, _hitRadius, _attackMask, QueryTriggerInteraction.Ignore))
        {
            if (overlap.transform.IsChildOf(_playerRoot))
                continue;
            Vector3 overlapPoint = overlap.ClosestPoint(request.Origin);
            if (overlap.GetComponentInParent<EnemyHealth>() == null)
            {
                point = overlapPoint;
                return overlap;
            }
            float distance = (overlapPoint - request.Origin).sqrMagnitude;
            if (distance < overlapDistance)
            {
                closestOverlap = overlap;
                overlapDistance = distance;
                point = overlapPoint;
            }
        }
        if (closestOverlap != null)
            return closestOverlap;

        RaycastHit[] hits = Physics.SphereCastAll(request.Origin, _hitRadius, request.Direction,
            _unlimitedRange ? Mathf.Infinity : request.Range, _attackMask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(_playerRoot))
                continue;
            point = hit.point;
            return hit.collider;
        }
        return null;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_showAttackGizmos || !isActiveAndEnabled || !Application.isPlaying) return;
        if (Time.time <= _debugDashUntil)
            DrawDashSweepGizmo();
        if (!_hasDebugRequest || Time.time > _debugUntil) return;
        Color color = _debugHit ? Color.green : _debugContact ? Color.red : Color.cyan;
        if (_debugBladeSweep)
            DrawBladeHitboxGizmo(_debugPreviousBase, _debugPreviousTip, _debugCurrentBase, _debugCurrentTip, _hitRadius, color);
        else
            DrawBladeHitboxGizmo(_debugRequest.Origin, _debugRequest.Origin + _debugRequest.Direction * _debugRequest.Range,
                _debugRequest.Origin, _debugRequest.Origin + _debugRequest.Direction * _debugRequest.Range, _hitRadius, color);
        UnityEditor.Handles.Label(_debugBladeSweep ? _debugCurrentBase : _debugRequest.Origin,
            _debugHit ? "플레이어 공격: 적중" : _debugContact ? "플레이어 공격: 접촉" : "플레이어 공격: 검사 범위");
    }

    /// <summary>origin·direction·range와 현재 칼날 참조로 예상 판정 영역의 외접 박스를 그린다. 물리 검사와 공격 상태는 변경하지 않는다.</summary>
    internal void DrawAttackPreview(Vector3 origin, Vector3 direction, float range)
    {
        if (!_showAttackGizmos) return;
        Vector3 start = _bladeBase != null && _bladeTip != null ? _bladeBase.position : origin;
        Vector3 end = _bladeBase != null && _bladeTip != null
            ? (_useViewModelReach ? GetWorldBladeTip(origin, start, _bladeTip.position, range) : _bladeTip.position)
            : origin + direction.normalized * range;
        DrawBladeHitboxGizmo(start, end, start, end, _hitRadius, Color.yellow);
        UnityEditor.Handles.Label(start, "플레이어: 현재 칼날 판정 외접 박스");
    }

    /// <summary>이전·현재 칼날 끝점과 radius로 Capsule·SphereCast 검사 영역을 감싸는 color의 회전 박스를 그린다. Gizmos 상태를 복원한다.</summary>
    private static void DrawBladeHitboxGizmo(Vector3 previousBase, Vector3 previousTip,
        Vector3 currentBase, Vector3 currentTip, float radius, Color color)
    {
        Vector3 blade = currentTip - currentBase;
        Quaternion rotation = blade.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(blade, Mathf.Abs(Vector3.Dot(blade.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up)
            : Quaternion.identity;
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 previousStart = inverse * (previousBase - currentBase);
        Vector3 previousEnd = inverse * (previousTip - currentBase);
        Vector3 currentEnd = inverse * blade;
        Vector3 minimum = Vector3.Min(Vector3.Min(previousStart, previousEnd), Vector3.Min(Vector3.zero, currentEnd));
        Vector3 maximum = Vector3.Max(Vector3.Max(previousStart, previousEnd), Vector3.Max(Vector3.zero, currentEnd));
        minimum -= Vector3.one * radius;
        maximum += Vector3.one * radius;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(currentBase, rotation, Vector3.one);
        Gizmos.color = color;
        Gizmos.DrawWireCube((minimum + maximum) * 0.5f, maximum - minimum);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    /// <summary>마지막 돌진 부채꼴 베기의 원점·방향과 지금까지 펴진 각도 구간을 그린다. 적중이 있었으면 초록, 아니면 하늘색이다.</summary>
    private void DrawDashSweepGizmo()
    {
        float width = Mathf.Abs(_debugDashToAngle - _debugDashFromAngle);
        Color color = _debugDashHit ? Color.green : Color.cyan;
        if (width > 0.01f)
        {
            Vector3 center = Quaternion.AngleAxis((_debugDashFromAngle + _debugDashToAngle) * 0.5f, Vector3.up) * _debugDashDirection;
            DrawAttackFanGizmo(_debugDashOrigin, center, _dashFanRange, width, color);
        }
        DrawAttackFanGizmo(_debugDashOrigin, _debugDashDirection, Mathf.Min(_dashFanRange, _dashFanNearbyRadius), 180f, color);
        UnityEditor.Handles.Label(_debugDashOrigin, _debugDashHit ? "돌진 부채꼴 베기: 적중" : "돌진 부채꼴 베기: 펴지는 중");
    }

    /// <summary>origin·direction·range·angle로 color의 수평 부채꼴 범위 요약을 그린다. 물리 판정과 Handles 상태는 변경하지 않는다.</summary>
    internal static void DrawAttackFanGizmo(Vector3 origin, Vector3 direction, float range, float angle, Color color)
    {
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        Vector3 start = Quaternion.AngleAxis(-angle * 0.5f, Vector3.up) * forward;
        Color previous = UnityEditor.Handles.color;
        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 0.08f);
        UnityEditor.Handles.DrawSolidArc(origin, Vector3.up, start, angle, range);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireArc(origin, Vector3.up, start, angle, range);
        UnityEditor.Handles.DrawLine(origin, origin + start * range);
        UnityEditor.Handles.DrawLine(origin, origin + Quaternion.AngleAxis(angle, Vector3.up) * start * range);
        UnityEditor.Handles.color = previous;
    }

    /// <summary>
    /// start부터 end까지 반경 radius인 구체 검사의 양 끝과 외곽선을 color로 그린다.
    /// Gizmos 색상을 복원하며 게임 오브젝트와 물리 상태는 변경하지 않는다.
    /// </summary>
    internal static void DrawSweepGizmo(Vector3 start, Vector3 end, float radius, Color color)
    {
        Color previous = Gizmos.color;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(start, radius);
        Vector3 delta = end - start;
        if (delta.sqrMagnitude > 0.000001f)
        {
            Vector3 direction = delta.normalized;
            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 0.000001f) side = Vector3.Cross(direction, Vector3.right);
            side = side.normalized * radius;
            Vector3 up = Vector3.Cross(direction, side).normalized * radius;
            Gizmos.DrawWireSphere(end, radius);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(start + side, end + side);
            Gizmos.DrawLine(start - side, end - side);
            Gizmos.DrawLine(start + up, end + up);
            Gizmos.DrawLine(start - up, end - up);
        }
        Gizmos.color = previous;
    }
#endif

    /// <summary>
    /// request의 베기 구간에서 칼날 경로를 검사한다. 접촉 또는 finalSample에서 결과를 확정한다.
    /// 미완료 검사에는 IsComplete=false를 반환하며 완료된 공격은 중복 적용하지 않는다.
    /// </summary>
    public PlayerAttackResolution ResolvePlayerBladeAttack(PlayerAttackRequest request, bool finalSample)
    {
        if (_hasResolution && _lastResolution.ActionId == request.ActionId) return _lastResolution;
        if (_unlimitedRange) return ResolveAttack(request, finalSample);
        if (_bladeBase == null || _bladeTip == null) return ResolvePlayerAttack(request);
        Vector3 currentBase = _bladeBase.position;
        Vector3 currentTip = _bladeTip.position;
        if (_useViewModelReach) currentTip = GetWorldBladeTip(request.Origin, currentBase, currentTip, request.Range);
        ShapeNormalAttackBlade(request.Origin, request.Direction, ref currentBase, ref currentTip);
        if (_sweepActionId != request.ActionId)
        {
            _sweepActionId = request.ActionId;
            _previousBladeBase = currentBase;
            _previousBladeTip = currentTip;
        }
        Collider contact = FindBladeContact(_playerRoot, null, _previousBladeBase, _previousBladeTip,
            currentBase, currentTip, _hitRadius, _attackMask, out Vector3 point);
#if UNITY_EDITOR
        _debugPreviousBase = _previousBladeBase;
        _debugPreviousTip = _previousBladeTip;
        _debugCurrentBase = currentBase;
        _debugCurrentTip = currentTip;
#endif
        _previousBladeBase = currentBase;
        _previousBladeTip = currentTip;
        bool complete = contact != null || finalSample;
        EnemyHealth health = contact != null ? contact.GetComponentInParent<EnemyHealth>() : null;
        bool hit = health != null && health.IsAlive && health.gameObject.scene == gameObject.scene;
        string id = hit ? health.TargetId : null;
        if (hit)
        {
            health.TakeDamage(new DamageContext(_normalDamage, _playerRoot.gameObject, point,
                request.Direction, request.Kind.ToString()));
        }
        PlayerAttackResolution result = new PlayerAttackResolution(request.ActionId, hit, id,
            hit && health.CurrentHealth <= 0f, complete);
#if UNITY_EDITOR
        _debugBladeSweep = true;
        _debugRequest = request;
        _debugContact = contact != null;
        _debugHit = hit;
        _hasDebugRequest = true;
        _debugUntil = Time.time + _gizmoDuration;
#endif
        if (complete)
        {
            _hasResolution = true;
            _lastResolution = result;
            AttackContactResolved?.Invoke(request, contact, point, hit);
        }
        return result;
    }

    /// <summary>
    /// actionId 돌진에서 progress(0~1)까지 펴진 수평 부채꼴로 origin·dashDirection 기준의 적을 검사하고, 새로 들어온 적에게 발도 판정 피해를 한 번씩 준다.
    /// 근접 반경 안의 적은 정면 반원이면 바로 맞고, 공유 체력 묶음은 한 번만 피해를 받는다.
    /// 첫 적중 또는 preferredTargetId 적중을 돌진 결과로 기록하고, 첫 적중에서 AttackContactResolved를 한 번 알린다.
    /// </summary>
    public void SweepDashFan(int actionId, Vector3 origin, Vector3 dashDirection, float progress, string preferredTargetId)
    {
        if (!_useDashFanSweep || !isActiveAndEnabled) return;
        if (_dashSweepActionId != actionId)
        {
            _dashSweepActionId = actionId;
            _dashSweepHitTargetId = null;
            _dashSweepDeathConfirmed = false;
            _dashSweepStruck.Clear();
        }

        Vector3 forward = Vector3.ProjectOnPlane(dashDirection, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) return;
        forward.Normalize();
        // 부채꼴은 한쪽 끝에서 시작해 진행도만큼 반대쪽 끝으로 펴진다. 오른쪽이 양의 각도다.
        float sign = _dashFanSweepFromRight ? 1f : -1f;
        float fromAngle = sign * _dashFanAngle * 0.5f;
        float toAngle = fromAngle - sign * _dashFanAngle * Mathf.Clamp01(progress);
        float minAngle = Mathf.Min(fromAngle, toAngle);
        float maxAngle = Mathf.Max(fromAngle, toAngle);

        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive || enemy.gameObject.scene != gameObject.scene
                || _dashSweepStruck.Contains(enemy) || _dashSweepStruck.Contains(enemy.SharedDamageKey)) continue;
            Collider collider = enemy.CreateCombatTarget().HitCollider;
            if (collider == null || !collider.enabled) continue;

            // 중심 대신 가장 가까운 점을 써서 몸집이 크거나 바로 붙은 적도 거리·각도 조건에 들어오게 한다.
            Vector3 point = collider.ClosestPoint(origin);
            Vector3 offset = Vector3.ProjectOnPlane(point - origin, Vector3.up);
            float distance = offset.magnitude;
            if (distance > _dashFanRange) continue;
            float angle = distance > 0.0001f ? Vector3.SignedAngle(forward, offset, Vector3.up) : 0f;
            bool covered = distance <= _dashFanNearbyRadius
                ? Mathf.Abs(angle) <= 90f
                : angle >= minAngle && angle <= maxAngle;
            if (!covered || IsDashFanTargetBlocked(origin, point)) continue;

            // 무적인 적도 같은 돌진에서 다시 막힘 처리되지 않도록 개별 기록하고, 공유 체력 묶음은 실제 피해일 때만 묶음 단위로 기록한다.
            _dashSweepStruck.Add(enemy);
            if (!enemy.IsInvulnerable) _dashSweepStruck.Add(enemy.SharedDamageKey);
            Vector3 direction = distance > 0.0001f ? offset / distance : forward;
            string targetId = enemy.TargetId;
            enemy.TakeDamage(new DamageContext(_normalDamage, _playerRoot.gameObject, point, direction,
                PlayerAttackKind.IaidoFinisher.ToString()));

            bool firstHit = _dashSweepHitTargetId == null;
            if (firstHit || targetId == preferredTargetId)
            {
                _dashSweepHitTargetId = targetId;
                _dashSweepDeathConfirmed = enemy.CurrentHealth <= 0f;
            }
            if (firstHit)
            {
                PlayerAttackRequest request = new PlayerAttackRequest(actionId, PlayerAttackKind.IaidoFinisher,
                    preferredTargetId, origin, direction, _dashFanRange, Time.time);
                AttackContactResolved?.Invoke(request, collider, point, true);
            }
        }
#if UNITY_EDITOR
        _debugDashOrigin = origin;
        _debugDashDirection = forward;
        _debugDashFromAngle = fromAngle;
        _debugDashToAngle = toAngle;
        _debugDashHit = _dashSweepHitTargetId != null;
        _debugDashUntil = Time.time + _gizmoDuration;
#endif
    }

    /// <summary>actionId 돌진의 부채꼴 베기가 적을 맞혔으면 대표 대상 targetId와 그 대상의 사망 여부 deathConfirmed를 반환한다. 다른 행동이거나 적중이 없으면 false다.</summary>
    public bool TryGetDashSweepHit(int actionId, out string targetId, out bool deathConfirmed)
    {
        bool hit = _dashSweepActionId == actionId && _dashSweepHitTargetId != null;
        targetId = hit ? _dashSweepHitTargetId : null;
        deathConfirmed = hit && _dashSweepDeathConfirmed;
        return hit;
    }

    /// <summary>actionId 돌진의 부채꼴 베기가 targetId 적을 이미 처리했는지 반환한다. 대상 고정 베기의 중복 피해를 막는 데 사용한다.</summary>
    public bool WasStruckByDashSweep(int actionId, string targetId)
    {
        if (_dashSweepActionId != actionId || string.IsNullOrEmpty(targetId)) return false;
        foreach (EnemyHealth enemy in _dashSweepStruck)
        {
            if (enemy != null && enemy.TargetId == targetId) return true;
        }
        return false;
    }

    /// <summary>origin에서 point까지 플레이어와 적을 제외한 _attackMask 장애물이 있는지 검사하여 가려졌으면 true를 반환한다.</summary>
    private bool IsDashFanTargetBlocked(Vector3 origin, Vector3 point)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, delta / distance, distance, _attackMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(_playerRoot) || hit.collider.GetComponentInParent<EnemyHealth>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>incomingPoint에서 연결된 플레이어 검의 가장 가까운 지점을 반환하여 방어 섬광 위치로 사용한다.</summary>
    public Vector3 GetGuardContactPoint(Vector3 incomingPoint)
    {
        if (_bladeBase == null || _bladeTip == null) return incomingPoint;
        Vector3 delta = _bladeTip.position - _bladeBase.position;
        float fraction = delta.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(incomingPoint - _bladeBase.position, delta) / delta.sqrMagnitude) : 0f;
        return _bladeBase.position + delta * fraction;
    }

    /// <summary>
    /// 일반 1·2타의 적 판정 칼날 양 끝 bladeBase·bladeTip을 시선 원점 origin과 시점 방향 direction 기준으로 보정한다.
    /// _normalAttackViewPull·_normalAttackAngleScale을 사용하며 반격 중이면 바꾸지 않는다.
    /// </summary>
    public void ShapeNormalAttackBlade(Vector3 origin, Vector3 direction, ref Vector3 bladeBase, ref Vector3 bladeTip)
    {
        ShapeNormalAttackBlade(origin, direction, _normalAttackViewPull, _normalAttackAngleScale, ref bladeBase, ref bladeTip);
    }

    /// <summary>
    /// 일반 1·2타의 칼날 양 끝 bladeBase·bladeTip을 시선 원점 origin과 시점 방향 direction 기준으로 보정한다.
    /// 손잡이 쪽 끝을 viewPull 비율만큼 카메라로 당기고 두 끝의 좌우 각도를 angleScale배로 벌린다. 반격 중이면 바꾸지 않는다.
    /// 대나무 베기처럼 적 판정과 다른 값을 쓰는 호출자가 사용한다.
    /// </summary>
    public void ShapeNormalAttackBlade(Vector3 origin, Vector3 direction, float viewPull, float angleScale,
        ref Vector3 bladeBase, ref Vector3 bladeTip)
    {
        if (_katana.IsCounterAttack) return;
        // 칼끝 사거리를 정한 뒤 손잡이 쪽 끝만 카메라로 당겨 화면 가운데 가까운 대상까지 칼날 구간에 넣는다.
        bladeBase = WidenBladeAngle(origin, direction, Vector3.Lerp(bladeBase, origin, viewPull), angleScale);
        bladeTip = WidenBladeAngle(origin, direction, bladeTip, angleScale);
    }

    /// <summary>
    /// point가 origin에서 수평 시점 방향 direction과 벌어진 좌우 각도를 angleScale배로 벌린 위치를 반환한다.
    /// 높이와 origin까지의 거리는 유지하며 벌린 각도는 뒤쪽 180도를 넘지 않는다.
    /// </summary>
    private static Vector3 WidenBladeAngle(Vector3 origin, Vector3 direction, Vector3 point, float angleScale)
    {
        Vector3 forward = Vector3.ProjectOnPlane(direction, Vector3.up);
        Vector3 offset = point - origin;
        Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
        if (forward.sqrMagnitude < 0.000001f || horizontal.sqrMagnitude < 0.000001f) return point;
        float yaw = Vector3.SignedAngle(forward, horizontal, Vector3.up);
        float widened = Mathf.Clamp(yaw * angleScale, -180f, 180f);
        return origin + Quaternion.AngleAxis(widened - yaw, Vector3.up) * offset;
    }

    /// <summary>origin·bladeBase·viewTip의 칼날 방향을 유지하고 range로 축소된 1인칭 뷰모델의 월드 사거리를 보정한다.</summary>
    private static Vector3 GetWorldBladeTip(Vector3 origin, Vector3 bladeBase, Vector3 viewTip, float range)
    {
        Vector3 direction = (viewTip - bladeBase).normalized;
        float length = Mathf.Max(0f, range - Vector3.Distance(origin, bladeBase));
        return bladeBase + direction * length;
    }

    /// <summary>
    /// owner를 제외하고 칼날의 이전·현재 양 끝점 사이를 반경 radius로 검사한다.
    /// target이 있으면 다른 적을 제외하며 가장 가까운 접촉체와 point를 반환한다.
    /// </summary>
    internal static Collider FindBladeContact(Transform owner, Transform target, Vector3 previousBase,
        Vector3 previousTip, Vector3 currentBase, Vector3 currentTip, float radius, LayerMask mask, out Vector3 point)
    {
        Collider closest = null;
        point = currentTip;
        float nearest = float.PositiveInfinity;
        foreach (Collider collider in Physics.OverlapCapsule(currentBase, currentTip, radius, mask, QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(owner) || (target != null
                && !collider.transform.IsChildOf(target) && collider.GetComponentInParent<EnemyHealth>() != null)) continue;
            Vector3 candidate = collider.ClosestPoint(currentBase);
            float distance = (candidate - currentBase).sqrMagnitude;
            if (distance < nearest) { nearest = distance; closest = collider; point = candidate; }
        }
        for (int i = 0; i < 5; i++)
        {
            float fraction = i / 4f;
            Vector3 start = Vector3.Lerp(previousBase, previousTip, fraction);
            Vector3 end = Vector3.Lerp(currentBase, currentTip, fraction);
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < 0.000001f) continue;
            foreach (RaycastHit hit in Physics.SphereCastAll(start, radius, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(owner) || (target != null
                    && !hit.transform.IsChildOf(target) && hit.collider.GetComponentInParent<EnemyHealth>() != null)) continue;
                float distance = (hit.point - currentBase).sqrMagnitude;
                if (distance < nearest) { nearest = distance; closest = hit.collider; point = hit.point; }
            }
        }
        return closest;
    }

}
