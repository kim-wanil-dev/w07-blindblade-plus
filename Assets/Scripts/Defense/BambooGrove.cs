using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 대숲의 대나무 줄기를 베어 쓰러뜨리고 밑동에서 다시 복구한다.
/// 잘린 윗부분은 물리 조각으로 쓰러진다. 밑동 위의 기울어진 절단면에 얹혀 경사를 따라 미끄러지고, 베인 방향으로 기울며 땅에 떨어진다.
/// 날아가는 조각도 다시 베여 두 조각으로 나뉠 수 있다. 조각은 재사용 풀에서 꺼내고 복구 때 돌려놓는다.
/// 실루엣 세기는 쓰러지는 동안 1에서 0으로 줄고, 그 뒤 원래 대나무가 0에서 1로 강해지며 복구된다.
/// 세기는 파동으로 밝혀진 정도에 곱하므로 밝혀지지 않은 곳에서는 보이지 않는다. OutlineLayer.FORCED_REVEAL_MASK 비트와 렌더러별 _ForcedReveal 속성 블록으로 화면 윤곽선에 넘긴다.
/// 잘린 조각·절단면·바닥 받침은 DEBRIS_LAYER에 두고 서로만 부딪히게 해 플레이어·적·다른 판정에 영향을 주지 않는다.
/// 조각이 바닥이나 다른 조각에 닿으면 닿는 속도에 비례한 크기로 구 모양으로 퍼지는 아주 약한 충격 파동을 남기고, 겹친 파동은 세기를 더해 화면 윤곽선에 넘긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BambooGrove : MonoBehaviour
{
    // 이름 없는 빈 레이어다. 기본 레이캐스트에는 포함되므로 검기·수리검 판정 마스크에서 뺀다.
    public const int DEBRIS_LAYER = 3;
    private const float FLOOR_SIZE = 200f;
    private const float CUT_FACE_THICKNESS = 0.05f;
    private const float MAXIMUM_CUT_SLOPE = 60f;
    // 나뉜 두 조각의 충돌체가 처음부터 겹치지 않도록 띄우는 간격(미터).
    private const float SPLIT_GAP = 0.01f;
    // 화면 윤곽선 셰이더의 MAX_IMPACT_RIPPLES와 같아야 한다.
    private const int MAX_IMPACT_RIPPLES = 32;

    private static readonly int _forcedRevealId = Shader.PropertyToID("_ForcedReveal");
    private static readonly int _impactRippleCountId = Shader.PropertyToID("_OutlineImpactRippleCount");
    private static readonly int _impactRipplesId = Shader.PropertyToID("_OutlineImpactRipples");
    private static readonly int _impactRippleParamsId = Shader.PropertyToID("_OutlineImpactRippleParams");
    private static BambooGrove _active;

    [Header("베기")]
    [SerializeField, Min(0f), Tooltip("베는 높이가 이보다 낮으면 이 높이에서 벤다(미터). 밑동이 남는 최소 높이.")]
    private float _minimumStumpHeight = 0.3f;
    [SerializeField, Min(0.1f), Tooltip("잘린 윗부분이 남는 최소 길이(미터). 베는 높이가 너무 높으면 이 길이가 남도록 낮춘다.")]
    private float _minimumTopLength = 1f;
    [SerializeField, Min(0.05f), Tooltip("날아가는 조각을 다시 벨 때 나뉜 두 조각이 각각 남아야 하는 최소 길이(미터). 이보다 끝에 가까우면 베지 않는다.")]
    private float _minimumPieceLength = 0.3f;

    [Header("쓰러짐 물리")]
    [SerializeField, Min(0f), Tooltip("벨 때 잘린 윗부분을 쓰러질 방향으로 미는 처음 속도(m/s). 날아가는 조각을 다시 벨 때는 위쪽 조각에 더한다.")]
    private float _pushSpeed = 1f;
    [SerializeField, Min(0f), Tooltip("벨 때 잘린 윗부분이 쓰러질 방향으로 기우는 처음 회전 속도(rad/s).")]
    private float _tipSpeed = 0.5f;
    [SerializeField, Range(0f, 1f), Tooltip("밑동 절단면의 마찰. 낮을수록 기울어진 절단면을 따라 잘 미끄러진다.")]
    private float _cutFaceFriction = 0.1f;
    [SerializeField, Min(0.01f), Tooltip("잘린 조각의 길이 1m당 질량(kg).")]
    private float _massPerMeter = 1f;
    [SerializeField, Min(0.01f), Tooltip("잘린 조각의 실루엣 세기가 1에서 0으로 흐려지는 시간(초, 게임 시간). 처음 벤 순간부터 재며, 이 시간이 지나면 그 대나무의 모든 조각이 사라지고 복구가 시작된다.")]
    private float _fadeDuration = 3f;

    [Header("복구")]
    [SerializeField, Min(0.01f), Tooltip("조각이 사라진 뒤 원래 대나무의 실루엣 세기가 0에서 1로 강해지는 시간(초, 게임 시간). 파동으로 밝혀진 정도에 곱한다.")]
    private float _regrowDuration = 3f;

    [Header("땅 충격 파동")]
    [SerializeField, Min(0f), Tooltip("잘린 조각이 바닥이나 다른 조각에 수직으로 닿는 속도가 이보다 느리면 파동을 내지 않는다(m/s).")]
    private float _impactMinimumSpeed = 1f;
    [SerializeField, Min(0f), Tooltip("닿는 속도 1m/s당 파동 반경(미터). 빠르게 부딪힐수록 크게 퍼진다.")]
    private float _impactRadiusPerSpeed = 0.3f;
    [SerializeField, Min(0.01f), Tooltip("충격 파동의 최대 반경(미터).")]
    private float _impactMaximumRadius = 3f;
    [SerializeField, Min(0.01f), Tooltip("충격 파동이 최대 반경까지 퍼지는 시간(초, 게임 시간).")]
    private float _impactExpandDuration = 0.25f;
    [SerializeField, Min(0.01f), Tooltip("전선이 지나간 자리가 흐려지는 잔상 시간(초, 게임 시간).")]
    private float _impactAfterimage = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("충격 파동 하나의 실루엣 세기. 겹친 파동은 세기를 더한다.")]
    private float _impactStrength = 0.15f;

    private Stalk[] _stalks;
    private readonly List<int> _animating = new List<int>();
    private readonly List<Piece> _flyingPieces = new List<Piece>();
    private readonly Stack<Piece> _piecePool = new Stack<Piece>();
    private MaterialPropertyBlock _block;
    private PhysicsMaterial _cutFaceMaterial;
    private Collider _floor;
    private int _debrisOnly;
    // 충격 파동 고리 버퍼다. 가득 차면 가장 오래된 칸을 덮어쓴다.
    private readonly Vector4[] _impactSlots = new Vector4[MAX_IMPACT_RIPPLES];
    private readonly Vector4[] _impactSlotParams = new Vector4[MAX_IMPACT_RIPPLES];
    private readonly Vector4[] _impactRipples = new Vector4[MAX_IMPACT_RIPPLES];
    private readonly Vector4[] _impactRippleParams = new Vector4[MAX_IMPACT_RIPPLES];
    private int _impactNext;

    private enum StalkState
    {
        Standing,
        Falling,
        Regrowing
    }

    private sealed class Stalk
    {
        public Transform Transform { get; set; }
        public MeshRenderer Renderer { get; set; }
        public Mesh Mesh { get; set; }
        public MeshRenderer Stump { get; set; }
        public BoxCollider CutFace { get; set; }
        public List<Piece> Pieces { get; } = new List<Piece>();
        public Quaternion Rotation { get; set; }
        public Vector3 Scale { get; set; }
        public Vector3 Base { get; set; }
        public Vector3 Up { get; set; }
        public float Height { get; set; }
        public float Radius { get; set; }
        public uint Mask { get; set; }
        public StalkState State { get; set; }
        public float Age { get; set; }
    }

    private sealed class Piece
    {
        public GameObject GameObject { get; set; }
        public Transform Transform { get; set; }
        public MeshFilter Filter { get; set; }
        public MeshRenderer Renderer { get; set; }
        public Rigidbody Body { get; set; }
        public Stalk Owner { get; set; }
        public float Length { get; set; }
        public int FlyingIndex { get; set; }
    }

    void Awake()
    {
        _block = new MaterialPropertyBlock();
        _debrisOnly = ~(1 << DEBRIS_LAYER);
        _cutFaceMaterial = new PhysicsMaterial("BambooCutFace")
        {
            staticFriction = _cutFaceFriction,
            dynamicFriction = _cutFaceFriction,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };
        List<Stalk> stalks = new List<Stalk>();
        foreach (Transform child in transform)
        {
            MeshRenderer renderer = child.GetComponent<MeshRenderer>();
            MeshFilter filter = child.GetComponent<MeshFilter>();
            if (renderer == null || filter == null || filter.sharedMesh == null)
                continue;
            Bounds bounds = filter.sharedMesh.bounds;
            Vector3 up = child.up;
            float halfHeight = bounds.extents.y * child.lossyScale.y;
            stalks.Add(new Stalk
            {
                Transform = child,
                Renderer = renderer,
                Mesh = filter.sharedMesh,
                Rotation = child.rotation,
                Scale = child.localScale,
                Base = child.TransformPoint(bounds.center) - up * halfHeight,
                Up = up,
                Height = halfHeight * 2f,
                Radius = bounds.extents.x * child.lossyScale.x,
                Mask = renderer.renderingLayerMask
            });
        }
        _stalks = stalks.ToArray();
        CreateFloor();
    }

    void OnEnable()
    {
        _active = this;
    }

    void OnDisable()
    {
        if (_active == this)
            _active = null;
        Shader.SetGlobalInt(_impactRippleCountId, 0);
    }

    void OnDestroy()
    {
        Destroy(_cutFaceMaterial);
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;
        for (int i = _animating.Count - 1; i >= 0; i--)
        {
            Stalk stalk = _stalks[_animating[i]];
            stalk.Age += deltaTime;
            if (!Animate(stalk))
                _animating.RemoveAt(i);
        }
    }

    void LateUpdate()
    {
        PublishImpactRipples();
    }

    /// <summary>
    /// 검기가 from에서 to로 지난 수평 경로의 좌우 halfWidth 안의 대나무와 날아가는 조각을 벤다.
    /// 베는 평면은 경로를 품고 planeNormal을 법선으로 한다. 서 있거나 복구 중인 대나무는 밑동 위치로, 조각은 평면과 만나는 점으로 범위를 판정한다.
    /// 경로 가운데는 경로 방향으로, 옆은 떨어진 비율 × sideSpread만큼 각자 바깥쪽으로 틀어 쓰러뜨린다. 활성 대숲이 없으면 아무것도 하지 않는다.
    /// </summary>
    public static void CutAlongPath(Vector3 from, Vector3 to, float halfWidth, Vector3 planeNormal, float sideSpread)
    {
        if (_active == null || halfWidth <= 0f)
            return;
        Vector3 path = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float length = path.magnitude;
        if (length < 0.0001f)
            return;
        Vector3 direction = path / length;
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        for (int i = 0; i < _active._stalks.Length; i++)
        {
            Stalk stalk = _active._stalks[i];
            if (stalk.State == StalkState.Falling)
                continue;
            if (!TryGetPathOffset(stalk.Base, from, direction, right, length, halfWidth, out float lateral))
                continue;
            Vector3 fallDirection = (direction + right * (lateral / halfWidth * sideSpread)).normalized;
            _active.Cut(i, Vector3.Lerp(from, to, Vector3.Dot(stalk.Base - from, direction) / length), planeNormal, fallDirection);
        }
        int count = _active._flyingPieces.Count;
        for (int i = 0; i < count; i++)
        {
            Piece piece = _active._flyingPieces[i];
            if (!_active.TryGetPlaneCrossing(piece, from, planeNormal, out float crossing, out Vector3 point)
                || !TryGetPathOffset(point, from, direction, right, length, halfWidth, out float lateral))
                continue;
            Vector3 pushDirection = (direction + right * (lateral / halfWidth * sideSpread)).normalized;
            _active.SplitPiece(piece, crossing, pushDirection);
        }
    }

    /// <summary>
    /// planePoint·planeNormal 평면(화면의 갈라진 선)이 실제로 가로지르는 서 있거나 복구 중인 대나무와 날아가는 조각만 거리와 상관없이 벤다.
    /// 평면이 줄기나 조각의 양 끝 사이를 지나고, 그 교차점이 viewProjection 카메라 앞이며 화면 가장자리에서 margin(화면 크기 비율)만큼 넓힌 범위 안일 때만 벤다.
    /// origin에서 멀어지는 방향으로 쓰러뜨리며, 활성 대숲이 없으면 아무것도 하지 않는다.
    /// </summary>
    public static void CutInView(Matrix4x4 viewProjection, float margin, Vector3 origin, Vector3 planePoint, Vector3 planeNormal)
    {
        if (_active == null)
            return;
        for (int i = 0; i < _active._stalks.Length; i++)
        {
            Stalk stalk = _active._stalks[i];
            if (stalk.State == StalkState.Falling)
                continue;
            float facing = Vector3.Dot(stalk.Up, planeNormal);
            if (Mathf.Abs(facing) < 0.0001f)
                continue;
            float crossing = Vector3.Dot(planePoint - stalk.Base, planeNormal) / facing;
            if (crossing < 0f || crossing > stalk.Height
                || !IsInView(stalk.Base + stalk.Up * crossing, viewProjection, margin)
                || !TryGetAwayDirection(stalk.Base, origin, out Vector3 away))
                continue;
            _active.Cut(i, planePoint, planeNormal, away);
        }
        int count = _active._flyingPieces.Count;
        for (int i = 0; i < count; i++)
        {
            Piece piece = _active._flyingPieces[i];
            if (!_active.TryGetPlaneCrossing(piece, planePoint, planeNormal, out float crossing, out Vector3 point)
                || !IsInView(point, viewProjection, margin)
                || !TryGetAwayDirection(point, origin, out Vector3 away))
                continue;
            _active.SplitPiece(piece, crossing, away);
        }
    }

    /// <summary>
    /// 칼날이 이전 프레임(previousBase→previousTip)에서 지금(currentBase→currentTip)까지 쓸고 지난 면이 가로지르는 대나무와 날아가는 조각을 벤다.
    /// 베는 평면은 칼날 방향과 칼날이 움직인 방향을 품고, 칼날이 움직인 수평 방향으로 쓰러뜨린다. 활성 대숲이 없으면 아무것도 하지 않는다.
    /// </summary>
    public static void CutBySweep(Vector3 previousBase, Vector3 previousTip, Vector3 currentBase, Vector3 currentTip)
    {
        if (_active == null)
            return;
        Vector3 sweep = (currentTip - previousTip + currentBase - previousBase) * 0.5f;
        Vector3 normal = Vector3.Cross(currentTip - currentBase, sweep);
        Vector3 pushDirection = Vector3.ProjectOnPlane(sweep, Vector3.up);
        if (normal.sqrMagnitude < 0.000001f || pushDirection.sqrMagnitude < 0.000001f)
            return;
        normal.Normalize();
        pushDirection.Normalize();
        // 쓸고 지난 면을 감싸는 수평 원으로 먼 대나무를 먼저 걸러 낸다.
        Vector3 center = (previousBase + previousTip + currentBase + currentTip) * 0.25f;
        float reach = Mathf.Max(Mathf.Max(Vector3.Distance(center, previousBase), Vector3.Distance(center, previousTip)),
            Mathf.Max(Vector3.Distance(center, currentBase), Vector3.Distance(center, currentTip)));
        for (int i = 0; i < _active._stalks.Length; i++)
        {
            Stalk stalk = _active._stalks[i];
            if (stalk.State == StalkState.Falling
                || Vector3.ProjectOnPlane(stalk.Base - center, Vector3.up).magnitude > reach + stalk.Radius)
                continue;
            if (!IntersectSweep(stalk.Base, stalk.Base + stalk.Up * stalk.Height,
                    previousBase, previousTip, currentBase, currentTip, out Vector3 point))
                continue;
            _active.Cut(i, point, normal, pushDirection);
        }
        int count = _active._flyingPieces.Count;
        for (int i = 0; i < count; i++)
        {
            Piece piece = _active._flyingPieces[i];
            if (Vector3.Distance(piece.Transform.position, center) > reach + piece.Length * 0.5f)
                continue;
            GetPieceEnds(piece, out Vector3 bottom, out Vector3 top);
            if (!IntersectSweep(bottom, top, previousBase, previousTip, currentBase, currentTip, out Vector3 point))
                continue;
            float crossing = Vector3.Dot(point - bottom, piece.Transform.up);
            if (crossing < _active._minimumPieceLength || crossing > piece.Length - _active._minimumPieceLength)
                continue;
            _active.SplitPiece(piece, crossing, pushDirection);
        }
    }

    /// <summary>
    /// 잘린 조각이 바닥 받침이나 다른 조각의 point에 접촉면 수직 속도 speed로 닿았을 때 작은 충격 파동을 남긴다.
    /// _impactMinimumSpeed보다 느리면 무시하며, isBambooCollision이면 같은 위치에서 대나무 파손음도 재생한다.
    /// </summary>
    public void NotifyImpact(Vector3 point, float speed, bool isBambooCollision)
    {
        if (speed < _impactMinimumSpeed)
            return;
        float radius = Mathf.Min(speed * _impactRadiusPerSpeed, _impactMaximumRadius);
        _impactSlots[_impactNext] = new Vector4(point.x, point.y, point.z, radius);
        _impactSlotParams[_impactNext] = new Vector4(_impactExpandDuration, _impactAfterimage, _impactStrength, Time.time);
        _impactNext = (_impactNext + 1) % MAX_IMPACT_RIPPLES;
        if (isBambooCollision)
            Managers.Sound.Play(SoundId.BambooBreak, point, playDuringMovement: true);
    }

    /// <summary>
    /// point가 from에서 direction으로 length만큼 이어진 수평 경로 구간 안이고 좌우 halfWidth 안이면 true와 오른쪽(right) 기준 부호 있는 옆 거리 lateral을 반환한다.
    /// </summary>
    private static bool TryGetPathOffset(Vector3 point, Vector3 from, Vector3 direction, Vector3 right,
        float length, float halfWidth, out float lateral)
    {
        Vector3 offset = Vector3.ProjectOnPlane(point - from, Vector3.up);
        float along = Vector3.Dot(offset, direction);
        lateral = Vector3.Dot(offset, right);
        return along >= 0f && along <= length && Mathf.Abs(lateral) <= halfWidth;
    }

    /// <summary>point가 viewProjection 카메라 앞이고 화면 가장자리에서 margin(화면 크기 비율)만큼 넓힌 범위 안이면 true를 반환한다.</summary>
    private static bool IsInView(Vector3 point, Matrix4x4 viewProjection, float margin)
    {
        float limit = 1f + margin * 2f;
        Vector4 clip = viewProjection * new Vector4(point.x, point.y, point.z, 1f);
        return clip.w > 0f && Mathf.Abs(clip.x / clip.w) <= limit && Mathf.Abs(clip.y / clip.w) <= limit;
    }

    /// <summary>point에서 origin 반대쪽 수평 방향을 away로 반환한다. 두 점이 수평으로 겹치면 false를 반환한다.</summary>
    private static bool TryGetAwayDirection(Vector3 point, Vector3 origin, out Vector3 away)
    {
        away = Vector3.ProjectOnPlane(point - origin, Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
            return false;
        away.Normalize();
        return true;
    }

    /// <summary>
    /// bottom→top 선분이 칼날이 쓸고 지난 사각형(두 삼각형)을 지나면 true와 지나는 점 point를 반환한다.
    /// </summary>
    private static bool IntersectSweep(Vector3 bottom, Vector3 top, Vector3 previousBase, Vector3 previousTip,
        Vector3 currentBase, Vector3 currentTip, out Vector3 point)
    {
        return IntersectTriangle(bottom, top, previousBase, previousTip, currentTip, out point)
            || IntersectTriangle(bottom, top, previousBase, currentTip, currentBase, out point);
    }

    /// <summary>
    /// start→end 선분이 삼각형 a·b·c와 만나면 true와 만나는 점 point를 반환한다. 삼각형 앞뒤를 가리지 않는다.
    /// </summary>
    private static bool IntersectTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c, out Vector3 point)
    {
        point = Vector3.zero;
        Vector3 segment = end - start;
        Vector3 edgeB = b - a;
        Vector3 edgeC = c - a;
        Vector3 cross = Vector3.Cross(segment, edgeC);
        float determinant = Vector3.Dot(edgeB, cross);
        if (Mathf.Abs(determinant) < 0.0000001f)
            return false;
        float inverse = 1f / determinant;
        Vector3 offset = start - a;
        float u = Vector3.Dot(offset, cross) * inverse;
        if (u < 0f || u > 1f)
            return false;
        Vector3 q = Vector3.Cross(offset, edgeB);
        float v = Vector3.Dot(segment, q) * inverse;
        if (v < 0f || u + v > 1f)
            return false;
        float t = Vector3.Dot(edgeC, q) * inverse;
        if (t < 0f || t > 1f)
            return false;
        point = start + segment * t;
        return true;
    }

    /// <summary>
    /// 잘린 조각이 떨어질 바닥 받침을 대숲 아래에 만든다.
    /// 지면과 같은 높이(y=0)의 윗면을 가진 넓은 상자이며 잘린 조각과만 부딪힌다.
    /// </summary>
    private void CreateFloor()
    {
        GameObject floor = new GameObject("BambooDebrisFloor") { layer = DEBRIS_LAYER };
        floor.transform.SetParent(transform, false);
        floor.transform.position = new Vector3(transform.position.x, -0.5f, transform.position.z);
        BoxCollider collider = floor.AddComponent<BoxCollider>();
        collider.size = new Vector3(FLOOR_SIZE, 1f, FLOOR_SIZE);
        collider.excludeLayers = _debrisOnly;
        _floor = collider;
    }

    /// <summary>
    /// index의 서 있거나 복구 중인 대나무를 planePoint·planeNormal 평면과 만나는 높이에서 벤다.
    /// 원래 렌더러를 숨기고 밑동과 기울어진 절단면을 세운 뒤, 잘린 윗부분 조각을 절단면에 얹어 fallDirection으로 밀며 기울인다.
    /// </summary>
    private void Cut(int index, Vector3 planePoint, Vector3 planeNormal, Vector3 fallDirection)
    {
        Stalk stalk = _stalks[index];
        float maximumCut = stalk.Height - _minimumTopLength;
        if (maximumCut < _minimumStumpHeight)
            return;
        // 절단면 법선은 줄기 위쪽을 향하게 하고, 너무 가파르면 조각이 얹히지 못하므로 기울기를 제한한다.
        Vector3 normal = Vector3.Dot(planeNormal, stalk.Up) < 0f ? -planeNormal : planeNormal;
        if (Vector3.Angle(stalk.Up, normal) > MAXIMUM_CUT_SLOPE)
            normal = Vector3.RotateTowards(stalk.Up, normal, MAXIMUM_CUT_SLOPE * Mathf.Deg2Rad, 0f);
        float facing = Vector3.Dot(stalk.Up, normal);
        float cutHeight = Mathf.Clamp(Vector3.Dot(planePoint - stalk.Base, normal) / facing, _minimumStumpHeight, maximumCut);
        Vector3 cutPoint = stalk.Base + stalk.Up * cutHeight;

        // 복구 중인 대나무는 이미 갱신 목록에 있으므로 다시 넣지 않는다.
        bool animating = stalk.State == StalkState.Regrowing;
        stalk.Renderer.enabled = false;
        ShowStump(stalk, cutHeight);
        ShowCutFace(stalk, cutPoint, normal);
        LaunchTop(stalk, cutPoint, cutHeight, normal, fallDirection);
        Managers.Sound.Play(SoundId.BambooBreak, cutPoint, playDuringMovement: true);
        stalk.State = StalkState.Falling;
        stalk.Age = 0f;
        Animate(stalk);
        if (!animating)
            _animating.Add(index);
    }

    /// <summary>
    /// stalk의 밑동 렌더러를 처음 벨 때 원래 메시·재질로 만들고, 바닥에서 cutHeight까지의 길이로 세워 보이게 한다.
    /// 밑동은 원래 렌더링 레이어를 그대로 써 평소처럼 파동에만 드러난다.
    /// </summary>
    private void ShowStump(Stalk stalk, float cutHeight)
    {
        if (stalk.Stump == null)
        {
            GameObject stump = new GameObject(stalk.Transform.name + "_Stump");
            stump.transform.SetParent(transform, false);
            stump.AddComponent<MeshFilter>().sharedMesh = stalk.Mesh;
            MeshRenderer renderer = stump.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = stalk.Renderer.sharedMaterials;
            renderer.shadowCastingMode = stalk.Renderer.shadowCastingMode;
            renderer.renderingLayerMask = stalk.Mask;
            stalk.Stump = renderer;
        }
        Transform stumpTransform = stalk.Stump.transform;
        stumpTransform.SetPositionAndRotation(stalk.Base + stalk.Up * (cutHeight * 0.5f), stalk.Rotation);
        stumpTransform.localScale = ScaleForLength(stalk, cutHeight);
        stalk.Stump.gameObject.SetActive(true);
    }

    /// <summary>
    /// 밑동 위 cutPoint에 normal을 윗면 법선으로 하는 얇은 절단면 충돌체를 세운다. 처음 벨 때 만들고 이후 재사용한다.
    /// 마찰이 낮아 잘린 조각이 경사를 따라 미끄러지며, 잘린 조각과만 부딪힌다.
    /// </summary>
    private void ShowCutFace(Stalk stalk, Vector3 cutPoint, Vector3 normal)
    {
        if (stalk.CutFace == null)
        {
            GameObject face = new GameObject(stalk.Transform.name + "_CutFace") { layer = DEBRIS_LAYER };
            face.transform.SetParent(transform, false);
            BoxCollider collider = face.AddComponent<BoxCollider>();
            collider.sharedMaterial = _cutFaceMaterial;
            collider.excludeLayers = _debrisOnly;
            collider.center = new Vector3(0f, -CUT_FACE_THICKNESS * 0.5f, 0f);
            stalk.CutFace = collider;
        }
        float width = stalk.Radius * 2f;
        stalk.CutFace.size = new Vector3(width, CUT_FACE_THICKNESS, width);
        Vector3 tangent = Vector3.Cross(normal, stalk.Transform.right);
        stalk.CutFace.transform.SetPositionAndRotation(cutPoint, Quaternion.LookRotation(tangent, normal));
        stalk.CutFace.gameObject.SetActive(true);
    }

    /// <summary>
    /// cutHeight 위의 잘린 윗부분을 조각으로 꺼내 절단면에 얹고 fallDirection으로 밀며 기울인다.
    /// 평평한 아랫면이 기울어진 절단면에 파고들지 않도록 경사만큼 살짝 띄운다.
    /// </summary>
    private void LaunchTop(Stalk stalk, Vector3 cutPoint, float cutHeight, Vector3 normal, Vector3 fallDirection)
    {
        float length = stalk.Height - cutHeight;
        float lift = stalk.Radius * Mathf.Tan(Vector3.Angle(stalk.Up, normal) * Mathf.Deg2Rad);
        Vector3 flatDirection = Vector3.ProjectOnPlane(fallDirection, stalk.Up).normalized;
        Vector3 axis = Vector3.Cross(stalk.Up, flatDirection);
        SpawnPiece(stalk, cutPoint + stalk.Up * (lift + length * 0.5f), stalk.Rotation, length,
            flatDirection * _pushSpeed, axis.sqrMagnitude > 0.0001f ? axis.normalized * _tipSpeed : Vector3.zero);
    }

    /// <summary>
    /// 날아가는 piece를 아래 끝에서 crossing(미터) 떨어진 곳에서 두 조각으로 나눈다.
    /// 아래 조각은 piece를 줄여 쓰고 위 조각은 풀에서 꺼낸다. 두 조각은 나뉘기 전 그 자리의 속도와 같은 회전 속도를 이어받고, 위 조각은 pushDirection으로 더 밀린다.
    /// 양쪽이 _minimumPieceLength보다 짧아지면 나누지 않는다.
    /// </summary>
    private void SplitPiece(Piece piece, float crossing, Vector3 pushDirection)
    {
        float upperLength = piece.Length - crossing;
        if (crossing < _minimumPieceLength || upperLength < _minimumPieceLength)
            return;
        GetPieceEnds(piece, out Vector3 bottom, out _);
        Vector3 up = piece.Transform.up;
        Quaternion rotation = piece.Transform.rotation;
        Vector3 lowerCenter = bottom + up * (crossing * 0.5f);
        Vector3 upperCenter = bottom + up * (crossing + SPLIT_GAP + upperLength * 0.5f);
        Vector3 lowerVelocity = piece.Body.GetPointVelocity(lowerCenter);
        Vector3 upperVelocity = piece.Body.GetPointVelocity(upperCenter) + pushDirection * _pushSpeed;
        Vector3 angularVelocity = piece.Body.angularVelocity;

        PosePiece(piece, lowerCenter, rotation, crossing);
        piece.Body.linearVelocity = lowerVelocity;
        piece.Body.angularVelocity = angularVelocity;
        SpawnPiece(piece.Owner, upperCenter, rotation, upperLength, upperVelocity, angularVelocity);
        Managers.Sound.Play(SoundId.BambooBreak, bottom + up * crossing, playDuringMovement: true);
    }

    /// <summary>
    /// owner 대나무의 조각을 풀에서 꺼내거나 새로 만들어 center·rotation·length로 놓고 velocity·angularVelocity로 날린다.
    /// 조각은 owner의 실루엣 세기를 따르며 날아가는 조각 목록과 owner의 조각 목록에 더한다.
    /// </summary>
    private void SpawnPiece(Stalk owner, Vector3 center, Quaternion rotation, float length, Vector3 velocity, Vector3 angularVelocity)
    {
        Piece piece = _piecePool.Count > 0 ? _piecePool.Pop() : CreatePiece();
        piece.Owner = owner;
        piece.Filter.sharedMesh = owner.Mesh;
        piece.Renderer.sharedMaterials = owner.Renderer.sharedMaterials;
        piece.Renderer.renderingLayerMask = (owner.Mask & ~OutlineLayer.SCENERY_MASK) | OutlineLayer.FORCED_REVEAL_MASK;
        piece.GameObject.SetActive(true);
        piece.Body.isKinematic = false;
        PosePiece(piece, center, rotation, length);
        piece.Body.linearVelocity = velocity;
        piece.Body.angularVelocity = angularVelocity;
        piece.FlyingIndex = _flyingPieces.Count;
        _flyingPieces.Add(piece);
        owner.Pieces.Add(piece);
        ApplyReveal(piece.Renderer, 1f - owner.Age / _fadeDuration);
    }

    /// <summary>
    /// 물리 조각 하나를 만든다. 대숲 아래 잘린 조각 레이어에 두고, 상자 충돌체·보간 Rigidbody·충돌 알림을 붙인다. 잘린 조각끼리만 부딪힌다.
    /// </summary>
    private Piece CreatePiece()
    {
        GameObject pieceObject = new GameObject("BambooPiece") { layer = DEBRIS_LAYER };
        pieceObject.transform.SetParent(transform, false);
        MeshFilter filter = pieceObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = pieceObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        // 대나무 실린더 메시와 같은 크기(지름 1, 높이 2)의 상자로, 크기는 Transform 배율로 맞춘다.
        BoxCollider collider = pieceObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(1f, 2f, 1f);
        Rigidbody body = pieceObject.AddComponent<Rigidbody>();
        body.excludeLayers = _debrisOnly;
        // 세계 감속 중에는 물리 단계가 드물어 끊겨 보이므로 보간한다.
        body.interpolation = RigidbodyInterpolation.Interpolate;
        pieceObject.AddComponent<BambooDebrisImpact>().Bind(this, _floor);
        return new Piece
        {
            GameObject = pieceObject,
            Transform = pieceObject.transform,
            Filter = filter,
            Renderer = renderer,
            Body = body
        };
    }

    /// <summary>piece를 center·rotation에 놓고 owner 대나무 굵기에 length 길이로 크기를 맞춘 뒤 질량을 길이에 맞춘다.</summary>
    private void PosePiece(Piece piece, Vector3 center, Quaternion rotation, float length)
    {
        piece.Length = length;
        piece.Transform.SetPositionAndRotation(center, rotation);
        piece.Transform.localScale = ScaleForLength(piece.Owner, length);
        piece.Body.position = center;
        piece.Body.rotation = rotation;
        piece.Body.mass = Mathf.Max(0.01f, length * _massPerMeter);
    }

    /// <summary>piece가 날아가는 목록과 owner 목록에서 빠지고 꺼진 채 풀로 돌아가게 한다.</summary>
    private void ReleasePiece(Piece piece)
    {
        int last = _flyingPieces.Count - 1;
        Piece moved = _flyingPieces[last];
        _flyingPieces[piece.FlyingIndex] = moved;
        moved.FlyingIndex = piece.FlyingIndex;
        _flyingPieces.RemoveAt(last);
        piece.Body.linearVelocity = Vector3.zero;
        piece.Body.angularVelocity = Vector3.zero;
        piece.Body.isKinematic = true;
        piece.Renderer.SetPropertyBlock(null);
        piece.GameObject.SetActive(false);
        piece.Owner = null;
        _piecePool.Push(piece);
    }

    /// <summary>
    /// piece의 축 방향 선분이 planePoint·planeNormal 평면과 양 끝에서 _minimumPieceLength 이상 떨어진 곳에서 만나면 true와 아래 끝에서의 거리 crossing, 만나는 점 point를 반환한다.
    /// </summary>
    private bool TryGetPlaneCrossing(Piece piece, Vector3 planePoint, Vector3 planeNormal, out float crossing, out Vector3 point)
    {
        GetPieceEnds(piece, out Vector3 bottom, out _);
        Vector3 up = piece.Transform.up;
        float facing = Vector3.Dot(up, planeNormal);
        crossing = 0f;
        point = bottom;
        if (Mathf.Abs(facing) < 0.0001f)
            return false;
        crossing = Vector3.Dot(planePoint - bottom, planeNormal) / facing;
        point = bottom + up * crossing;
        return crossing >= _minimumPieceLength && crossing <= piece.Length - _minimumPieceLength;
    }

    /// <summary>piece 축의 아래 끝 bottom과 위 끝 top을 반환한다.</summary>
    private static void GetPieceEnds(Piece piece, out Vector3 bottom, out Vector3 top)
    {
        Vector3 half = piece.Transform.up * (piece.Length * 0.5f);
        bottom = piece.Transform.position - half;
        top = piece.Transform.position + half;
    }

    /// <summary>
    /// stalk의 현재 상태와 경과 시간(Age)으로 실루엣 세기를 갱신하고, 다음 상태로 넘긴다.
    /// 아직 진행 중이면 true, 평소 상태로 돌아와 더 갱신할 필요가 없으면 false를 반환한다.
    /// </summary>
    private bool Animate(Stalk stalk)
    {
        switch (stalk.State)
        {
            case StalkState.Falling:
                if (stalk.Age >= _fadeDuration)
                {
                    BeginRegrow(stalk);
                    return true;
                }
                float strength = 1f - stalk.Age / _fadeDuration;
                foreach (Piece piece in stalk.Pieces)
                    ApplyReveal(piece.Renderer, strength);
                return true;
            case StalkState.Regrowing:
                if (stalk.Age >= _regrowDuration)
                {
                    stalk.State = StalkState.Standing;
                    stalk.Renderer.renderingLayerMask = stalk.Mask;
                    stalk.Renderer.SetPropertyBlock(null);
                    return false;
                }
                ApplyReveal(stalk.Renderer, stalk.Age / _regrowDuration);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 흐려짐이 끝난 stalk의 조각을 모두 풀로 돌리고 밑동과 절단면을 숨긴 뒤, 원래 렌더러를 다시 보이게 해 복구 상태로 바꾼다.
    /// 원래 배경 비트에 세기 조절 비트를 더해 파동으로 밝혀진 정도에 복구 세기를 곱해 보이게 한다.
    /// </summary>
    private void BeginRegrow(Stalk stalk)
    {
        foreach (Piece piece in stalk.Pieces)
            ReleasePiece(piece);
        stalk.Pieces.Clear();
        stalk.Stump.gameObject.SetActive(false);
        stalk.CutFace.gameObject.SetActive(false);
        stalk.Renderer.renderingLayerMask = stalk.Mask | OutlineLayer.FORCED_REVEAL_MASK;
        stalk.Renderer.enabled = true;
        stalk.State = StalkState.Regrowing;
        stalk.Age = 0f;
        ApplyReveal(stalk.Renderer, 0f);
    }

    /// <summary>
    /// renderer의 _ForcedReveal 속성 블록에 파동 가시성에 곱할 실루엣 세기 strength(0~1)를 넣는다.
    /// </summary>
    private void ApplyReveal(Renderer renderer, float strength)
    {
        _block.SetVector(_forcedRevealId, new Vector4(Mathf.Clamp01(strength), 0f, 0f, 0f));
        renderer.SetPropertyBlock(_block);
    }

    /// <summary>
    /// 아직 전선이 퍼지거나 잔상이 남은 충격 파동만 모아 화면 윤곽선 셰이더 전역 배열과 개수로 넘긴다.
    /// </summary>
    private void PublishImpactRipples()
    {
        float now = Time.time;
        int count = 0;
        for (int i = 0; i < MAX_IMPACT_RIPPLES; i++)
        {
            Vector4 slot = _impactSlots[i];
            Vector4 parameters = _impactSlotParams[i];
            if (slot.w <= 0f || now > parameters.w + parameters.x + parameters.y)
                continue;
            _impactRipples[count] = slot;
            _impactRippleParams[count++] = parameters;
        }
        Shader.SetGlobalInt(_impactRippleCountId, count);
        Shader.SetGlobalVectorArray(_impactRipplesId, _impactRipples);
        Shader.SetGlobalVectorArray(_impactRippleParamsId, _impactRippleParams);
    }

    /// <summary>stalk의 원래 크기에서 높이 방향만 length 길이에 맞게 줄인 로컬 크기를 반환한다.</summary>
    private static Vector3 ScaleForLength(Stalk stalk, float length)
    {
        Vector3 scale = stalk.Scale;
        scale.y *= length / stalk.Height;
        return scale;
    }
}
