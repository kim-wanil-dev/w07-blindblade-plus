using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using BlindBlade.UI.CombatHud;

/// <summary>
/// 인간 보스 허리춤에 끈-구슬-끈-구슬-끈-구슬로 매달린 구슬 세 개를 플레이어 칼 구슬(PlayerKatanaBeads)과 같은 모양으로 그린다.
/// 구슬 속은 HumanBossAttack.Charges만큼 위 구슬부터 붉은 실 구체로 차고, 모두 차면 실이 빨리 돌며 붉은 빛무리가 맥동한다.
/// 끈은 인간 루트 기준 공간에서 중력·거리 제약으로 흔들리며, 보스 이동량이 커서 루트의 이동·회전 관성은 작은 배율만 반영한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack))]
public sealed class TwinHumanBeads : MonoBehaviour
{
    private const int BEAD_COUNT = 3;
    private const int SHELL_RINGS = 10;
    private const int SHELL_SEGMENTS = 16;
    private const int CRACK_STEPS = 6;
    private const float CRACK_STEP_ANGLE = 0.14f;
    private const float CRACK_TURN = 0.7f;
    private const float CRACK_LIFT = 1.004f;
    private const float MAX_DELTA_TIME = 1f / 30f;
    private const int GLOW_SEGMENTS = 24;
    // 빛무리 고리 반지름(구슬 반지름 대비, 마지막은 바깥 끝 배율로 대체)과 고리별 농도다. 칼 구슬과 같다.
    private static readonly float[] GLOW_RING_RADII = { 0f, 0.6f, 1f, 1.3f, 1f };
    private static readonly float[] GLOW_RING_ALPHAS = { 0.55f, 0.45f, 0.3f, 0.12f, 0f };

    [Header("연결")]
    [SerializeField, Tooltip("끈을 매다는 허리춤 지점(인간 루트 로컬 좌표, 미터)")]
    private Vector3 _anchorOffset = new Vector3(0.3f, 0.95f, 0.05f);
    [SerializeField, Tooltip("BlindBlade/KatanaBead 셰이더 재질. 플레이어 칼 구슬과 같은 재질을 쓴다.")]
    private Material _material;
    private HumanBossAttack _attack;

    [Header("구슬")]
    [SerializeField, Min(0.001f), Tooltip("구슬 반지름(m)")]
    private float _beadRadius = 0.07f;
    [SerializeField, Range(0.3f, 1f), Tooltip("구슬 반지름 대비 속을 채우는 실 구체 반지름")]
    private float _sphereRadiusRatio = 0.8f;
    [SerializeField, Tooltip("껍질에 곱하는 색. 알파로 껍질 전체 농도를 조절한다.")]
    private Color _shellTint = Color.white;
    [SerializeField, Range(0, 6), Tooltip("구슬마다 껍질에 난 금 개수")]
    private int _crackCount = 3;
    [SerializeField, Min(0f), Tooltip("금 선 두께(m)")]
    private float _crackWidth = 0.0035f;
    [SerializeField] private Color _crackColor = new Color(0.86f, 0.87f, 0.9f, 0.55f);
    [SerializeField, Tooltip("금 모양을 정하는 난수 시드")]
    private int _crackSeed = 11;

    [Header("끈")]
    [SerializeField, Min(0.001f), Tooltip("허리춤에서 첫 구슬까지 끈 길이(m)")]
    private float _firstCordLength = 0.22f;
    [SerializeField, Min(0.001f), Tooltip("구슬 사이 끈 길이(m)")]
    private float _cordLength = 0.12f;
    [SerializeField, Range(1, 6), Tooltip("끈 하나를 나누는 물리 마디 수")]
    private int _cordSubdivisions = 3;
    [SerializeField, Min(0.0001f), Tooltip("끈 두께(m)")]
    private float _cordWidth = 0.012f;
    [SerializeField] private Color _cordColor = Color.white;

    [Header("물리")]
    [SerializeField, Min(0f), Tooltip("중력 가속도(m/s²)")]
    private float _gravity = 9.81f;
    [SerializeField, Range(0.8f, 1f), Tooltip("1/60초마다 남는 속도 비율. 작을수록 흔들림이 빨리 잦아든다.")]
    private float _damping = 0.95f;
    [SerializeField, Range(1, 32), Tooltip("끈 길이 제약을 푸는 반복 횟수")]
    private int _iterations = 16;
    [SerializeField, Range(1f, 1.5f), Tooltip("끈 마디가 원래 길이보다 늘어날 수 있는 최대 배율")]
    private float _maxStretch = 1.02f;
    [SerializeField, Range(0f, 1f), Tooltip("보스가 움직이거나 돌 때 구슬이 세계에 남으려는 정도. 보스 이동량이 커서 작게 둔다.")]
    private float _movementInertia = 0.12f;
    [SerializeField, Min(0f), Tooltip("한 프레임에 관성으로 밀리는 최대 거리(m). 순간이동·돌진에서 끈이 튀지 않게 한다.")]
    private float _maxInertiaStep = 0.03f;
    [SerializeField, Range(0.05f, 1f), Tooltip("끈 마디 대비 구슬의 역질량. 작을수록 구슬이 무겁다.")]
    private float _beadInverseMass = 0.4f;

    [Header("실 구체")]
    [SerializeField, Tooltip("찬 구슬 속을 채우는 실 구체. 색을 붉게 두어 보스 기운을 나타낸다. Feather는 구 반지름 대비 비율이다.")]
    private CombatTokenOrbitStyle _sphereStyle = new CombatTokenOrbitStyle(2.2f, 0.85f, 0.08f);

    [Header("가득 참")]
    [SerializeField, Min(1f)] private float _fullSpeedMultiplier = 2.5f;
    [SerializeField, Min(0f)] private float _fullGlowStrength = 1f;
    [SerializeField, Min(1f)] private float _fullGlowRadius = 1.7f;
    [SerializeField, Range(0f, 1f)] private float _fullGlowPulseDepth = 0.35f;
    [SerializeField, Min(0f)] private float _fullGlowPulseSpeed = 1.2f;
    [SerializeField, Min(0.01f)] private float _fullTransitionDuration = 0.3f;

    [Header("상태")]
    private GameObject _meshObject;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Vector3> _normals = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<int> _indices = new List<int>();
    private readonly List<int> _frontShellIndices = new List<int>();
    private Vector3[] _positions;
    private Vector3[] _previous;
    private float[] _restLengths;
    private float[] _inverseMasses;
    private readonly int[] _beadNodes = new int[BEAD_COUNT];
    private readonly Quaternion[] _beadRotations = new Quaternion[BEAD_COUNT];
    private readonly ThreadSphere[] _spheres = new ThreadSphere[BEAD_COUNT];
    private readonly ThreadSphereFlow[] _flows = new ThreadSphereFlow[BEAD_COUNT];
    private readonly float[] _fills = new float[BEAD_COUNT];
    private readonly int[] _drawOrder = new int[BEAD_COUNT];
    private readonly List<Vector3[]>[] _cracks = new List<Vector3[]>[BEAD_COUNT];
    private Vector3[] _shellUnit;
    private int[] _shellTriangles;
    private Matrix4x4 _previousLocalToWorld;
    private Vector3 _viewPoint;
    private Vector3 _viewRight;
    private Vector3 _viewUp;
    private bool _hanging;
    private float _time;
    private float _fullBlend;
    private float _glowTime;
    private float _glow;

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        BuildChain();
        BuildShell();
        BuildCracks();
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            _spheres[bead] = new ThreadSphere(_sphereStyle);
            _flows[bead] = new ThreadSphereFlow(10f + bead * 2f);
        }
        CreateMeshObject();
    }

    void OnDisable()
    {
        _hanging = false;
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_meshObject != null) Destroy(_meshObject);
        if (_mesh != null) Destroy(_mesh);
    }

    void LateUpdate()
    {
        Camera view = Camera.main;
        bool visible = _attack.Health.IsAlive && view != null;
        _meshRenderer.enabled = visible;
        if (!visible)
        {
            _hanging = false;
            return;
        }

        float deltaTime = Mathf.Min(Time.deltaTime, MAX_DELTA_TIME);
        _viewPoint = transform.InverseTransformPoint(view.transform.position);
        _viewRight = transform.InverseTransformDirection(view.transform.right);
        _viewUp = transform.InverseTransformDirection(view.transform.up);
        // 인간이 물속 순간이동으로 가라앉거나 늑대 위에 누우면 허리춤 지점도 몸체와 함께 움직인다.
        Vector3 anchor = _attack.GetBodyPoint(_anchorOffset);
        if (_hanging)
            Simulate(anchor, deltaTime);
        else
            HangChain(anchor);
        _previousLocalToWorld = transform.localToWorldMatrix;
        UpdateBeadRotations();
        UpdateFills(deltaTime);
        BuildMesh();
    }

    /// <summary>
    /// bead번째(0이 허리춤에 가장 가까운 위 구슬) 구슬 중심의 월드 위치를 반환한다. 범위를 벗어난 번호는 양 끝 구슬로 맞춘다.
    /// 끈을 아직 늘어뜨리지 않았으면 허리춤 지점 _anchorOffset의 월드 위치를 반환한다.
    /// </summary>
    public Vector3 GetBeadPosition(int bead)
    {
        if (!_hanging) return transform.TransformPoint(_anchorOffset);
        return transform.TransformPoint(_positions[_beadNodes[Mathf.Clamp(bead, 0, BEAD_COUNT - 1)]]);
    }

    /// <summary>
    /// 허리춤 지점(0번 점)부터 끈 마디와 구슬 점을 차례로 늘어놓는 물리 점 배열과 각 점까지의 길이 제약·역질량을 만든다.
    /// 구슬에 닿는 마디 길이에는 구슬 반지름을 더하며 구슬 점 번호를 _beadNodes에 저장한다.
    /// </summary>
    private void BuildChain()
    {
        int count = 1 + BEAD_COUNT * (_cordSubdivisions + 1);
        _positions = new Vector3[count];
        _previous = new Vector3[count];
        _restLengths = new float[count];
        _inverseMasses = new float[count];
        int node = 1;
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            float piece = (bead == 0 ? _firstCordLength : _cordLength) / (_cordSubdivisions + 1);
            for (int step = 0; step <= _cordSubdivisions; step++)
            {
                bool isBead = step == _cordSubdivisions;
                _restLengths[node] = piece;
                if (step == 0 && bead > 0) _restLengths[node] += _beadRadius;
                if (isBead) _restLengths[node] += _beadRadius;
                _inverseMasses[node] = isBead ? _beadInverseMass : 1f;
                if (isBead) _beadNodes[bead] = node;
                node++;
            }
        }
    }

    /// <summary>SHELL_RINGS × SHELL_SEGMENTS 위경도 격자로 단위 구 껍질 점(_shellUnit)과 삼각형 번호(_shellTriangles)를 만든다.</summary>
    private void BuildShell()
    {
        _shellUnit = new Vector3[(SHELL_RINGS + 1) * (SHELL_SEGMENTS + 1)];
        for (int ring = 0; ring <= SHELL_RINGS; ring++)
        {
            float polar = Mathf.PI * ring / SHELL_RINGS;
            for (int segment = 0; segment <= SHELL_SEGMENTS; segment++)
            {
                float azimuth = 2f * Mathf.PI * segment / SHELL_SEGMENTS;
                _shellUnit[ring * (SHELL_SEGMENTS + 1) + segment] = new Vector3(
                    Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
            }
        }

        _shellTriangles = new int[SHELL_RINGS * SHELL_SEGMENTS * 6];
        int index = 0;
        for (int ring = 0; ring < SHELL_RINGS; ring++)
        {
            for (int segment = 0; segment < SHELL_SEGMENTS; segment++)
            {
                int a = ring * (SHELL_SEGMENTS + 1) + segment;
                int b = a + SHELL_SEGMENTS + 1;
                _shellTriangles[index++] = a;
                _shellTriangles[index++] = b;
                _shellTriangles[index++] = a + 1;
                _shellTriangles[index++] = a + 1;
                _shellTriangles[index++] = b;
                _shellTriangles[index++] = b + 1;
            }
        }
    }

    /// <summary>구슬마다 _crackSeed로 정해진 금 _crackCount개를 단위 구 표면의 꺾인 선으로 만들어 _cracks에 저장한다.</summary>
    private void BuildCracks()
    {
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            System.Random random = new System.Random(_crackSeed + bead * 101);
            _cracks[bead] = new List<Vector3[]>();
            for (int crack = 0; crack < _crackCount; crack++)
            {
                Vector3 point = RandomUnit(random);
                Vector3 direction = Vector3.ProjectOnPlane(RandomUnit(random), point).normalized;
                Vector3[] line = new Vector3[CRACK_STEPS + 1];
                line[0] = point;
                for (int step = 1; step <= CRACK_STEPS; step++)
                {
                    float turn = ((float)random.NextDouble() * 2f - 1f) * CRACK_TURN;
                    direction = Quaternion.AngleAxis(turn * Mathf.Rad2Deg, point) * direction;
                    point = (point + direction * CRACK_STEP_ANGLE).normalized;
                    direction = Vector3.ProjectOnPlane(direction, point).normalized;
                    line[step] = point;
                }
                _cracks[bead].Add(line);
            }
        }
    }

    /// <summary>random으로 단위 구 위의 고른 무작위 방향을 반환한다.</summary>
    private static Vector3 RandomUnit(System.Random random)
    {
        float z = (float)random.NextDouble() * 2f - 1f;
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(ring * Mathf.Cos(angle), ring * Mathf.Sin(angle), z);
    }

    /// <summary>
    /// 인간 루트 아래 원점에 구슬·끈을 그릴 동적 메시와 MeshRenderer를 만든다. 정점은 루트 기준 좌표다.
    /// 그림자·광원 탐침을 끄고 만든 오브젝트를 _meshObject, _meshRenderer, _mesh에 저장한다.
    /// </summary>
    private void CreateMeshObject()
    {
        _meshObject = new GameObject("WaistBeads");
        _meshObject.layer = gameObject.layer;
        _meshObject.transform.SetParent(transform, false);
        _mesh = new Mesh { name = "WaistBeads" };
        _mesh.MarkDynamic();
        _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
        _meshRenderer.sharedMaterial = _material;
        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _meshRenderer.enabled = false;
    }

    /// <summary>끈과 구슬을 anchor(루트 기준 허리춤 지점)에서 중력 방향으로 곧게 늘어뜨리고 속도를 0으로 한다.</summary>
    private void HangChain(Vector3 anchor)
    {
        Vector3 down = transform.InverseTransformDirection(Vector3.down);
        _positions[0] = anchor;
        for (int node = 1; node < _positions.Length; node++)
            _positions[node] = _positions[node - 1] + down * _restLengths[node];
        for (int node = 0; node < _positions.Length; node++)
            _previous[node] = _positions[node];
        for (int bead = 0; bead < BEAD_COUNT; bead++)
            _beadRotations[bead] = Quaternion.FromToRotation(Vector3.up, -down);
        _hanging = true;
    }

    /// <summary>
    /// 루트 기준 공간에서 끈·구슬을 deltaTime만큼 진행한다. 루트가 지난 프레임에서 움직이고 돈 만큼 세계에 남으려는 관성을
    /// _movementInertia 배율로만 주고(_maxInertiaStep으로 제한), 중력과 감쇠를 Verlet 적분한 뒤 0번 점을 anchor에 고정하고 길이 제약을 _iterations번 푼다.
    /// </summary>
    private void Simulate(Vector3 anchor, float deltaTime)
    {
        if (deltaTime <= 0f) return;
        Matrix4x4 stay = transform.worldToLocalMatrix * _previousLocalToWorld;
        Vector3 gravity = transform.InverseTransformDirection(Vector3.down) * (_gravity * deltaTime * deltaTime);
        float keep = Mathf.Pow(_damping, deltaTime * 60f);
        for (int node = 1; node < _positions.Length; node++)
        {
            // 보스가 움직인 반대쪽으로 아주 조금만 밀어 크게 끌려가지 않고 살짝 흔들리게 한다.
            Vector3 inertia = (stay.MultiplyPoint3x4(_positions[node]) - _positions[node]) * _movementInertia;
            _positions[node] += Vector3.ClampMagnitude(inertia, _maxInertiaStep);
            Vector3 velocity = (_positions[node] - _previous[node]) * keep;
            _previous[node] = _positions[node];
            _positions[node] += velocity + gravity;
        }

        for (int iteration = 0; iteration < _iterations; iteration++)
        {
            _positions[0] = anchor;
            for (int node = 1; node < _positions.Length; node++)
                SolveDistance(node - 1, node);
        }
        LimitStretch();
    }

    /// <summary>위 마디부터 차례로 각 점을 윗점에서 원래 길이 × _maxStretch 안으로 끌어오고 이전 위치도 같이 옮겨 튕기는 속도를 막는다.</summary>
    private void LimitStretch()
    {
        for (int node = 1; node < _positions.Length; node++)
        {
            Vector3 delta = _positions[node] - _positions[node - 1];
            float limit = _restLengths[node] * _maxStretch;
            if (delta.sqrMagnitude <= limit * limit) continue;
            Vector3 correction = _positions[node - 1] + delta.normalized * limit - _positions[node];
            _positions[node] += correction;
            _previous[node] += correction;
        }
    }

    /// <summary>first와 second 점 사이 거리를 second의 길이 제약에 맞추도록 두 점을 역질량 비율로 옮긴다.</summary>
    private void SolveDistance(int first, int second)
    {
        Vector3 delta = _positions[second] - _positions[first];
        float distance = delta.magnitude;
        float weight = _inverseMasses[first] + _inverseMasses[second];
        if (distance <= 0.000001f || weight <= 0f) return;
        Vector3 correction = delta * ((distance - _restLengths[second]) / (distance * weight));
        _positions[first] += correction * _inverseMasses[first];
        _positions[second] -= correction * _inverseMasses[second];
    }

    /// <summary>구슬마다 위쪽 끈 방향을 구슬의 +Y로 삼도록 _beadRotations를 이전 회전에서 최소한으로 돌린다.</summary>
    private void UpdateBeadRotations()
    {
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            int node = _beadNodes[bead];
            Vector3 up = (_positions[node - 1] - _positions[node]).normalized;
            Quaternion rotation = _beadRotations[bead];
            _beadRotations[bead] = Quaternion.FromToRotation(rotation * Vector3.up, up) * rotation;
        }
    }

    /// <summary>
    /// HumanBossAttack.Charges만큼 위 구슬부터 실 구체를 FadeDuration에 걸쳐 채우고 나머지는 비워 _fills에 저장한다.
    /// 모두 차면 _fullTransitionDuration에 걸쳐 실을 _fullSpeedMultiplier배로 빠르게 돌리고 맥동하는 빛무리 세기를 _glow에 저장한다.
    /// </summary>
    private void UpdateFills(float deltaTime)
    {
        int charges = _attack.Charges;
        _fullBlend = Mathf.MoveTowards(_fullBlend, charges >= BEAD_COUNT ? 1f : 0f, deltaTime / _fullTransitionDuration);
        float orbitDelta = deltaTime * Mathf.Lerp(1f, _fullSpeedMultiplier, _fullBlend);
        _time += orbitDelta;
        _glowTime += deltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_glowTime * _fullGlowPulseSpeed * Mathf.PI * 2f);
        _glow = _fullBlend * _fullGlowStrength * (1f - _fullGlowPulseDepth * pulse);
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            float previous = _fills[bead];
            bool filled = bead < charges;
            if (filled && previous <= 0f) _flows[bead].Reset(_sphereStyle, _time);
            _fills[bead] = Mathf.MoveTowards(previous, filled ? 1f : 0f, deltaTime / _sphereStyle.FadeDuration);
            if (_fills[bead] > 0f) _flows[bead].Step(_sphereStyle, _time, orbitDelta);
        }
    }

    /// <summary>끈을 먼저 그리고 카메라에서 먼 구슬부터 껍질 뒤쪽 → 실 구체 → 껍질 앞쪽 → 금 순서로 한 메시에 쌓아 _mesh에 반영한다.</summary>
    private void BuildMesh()
    {
        _vertices.Clear();
        _normals.Clear();
        _colors.Clear();
        _uvs.Clear();
        _indices.Clear();
        AppendCords();

        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            int order = bead;
            float depth = (_positions[_beadNodes[bead]] - _viewPoint).sqrMagnitude;
            while (order > 0 && (_positions[_beadNodes[_drawOrder[order - 1]]] - _viewPoint).sqrMagnitude < depth)
            {
                _drawOrder[order] = _drawOrder[order - 1];
                order--;
            }
            _drawOrder[order] = bead;
        }
        for (int order = 0; order < BEAD_COUNT; order++)
            AppendBead(_drawOrder[order]);

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetNormals(_normals);
        _mesh.SetColors(_colors);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_indices, 0);
        _mesh.RecalculateBounds();
    }

    /// <summary>허리춤에서 첫 구슬까지, 구슬과 구슬 사이의 끈을 카메라를 향한 띠로 추가한다. 구슬에 닿는 끝은 구슬 표면까지만 그린다.</summary>
    private void AppendCords()
    {
        int start = 0;
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            int end = _beadNodes[bead];
            int first = _vertices.Count;
            for (int node = start; node <= end; node++)
            {
                Vector3 point = _positions[node];
                if (node == end)
                    point += (_positions[node - 1] - point).normalized * _beadRadius;
                else if (node == start && start > 0)
                    point += (_positions[node + 1] - point).normalized * _beadRadius;
                Vector3 toView = (_viewPoint - point).normalized;
                Vector3 tangent = _positions[Mathf.Min(node + 1, end)] - _positions[Mathf.Max(node - 1, start)];
                Vector3 side = Vector3.Cross(tangent, toView).normalized * (_cordWidth * 0.5f);
                AddVertex(point + side, toView, _cordColor, 0f);
                AddVertex(point - side, toView, _cordColor, 0f);
            }
            for (int node = 0; node < end - start; node++)
            {
                int a = first + node * 2;
                AddQuad(a, a + 1, a + 3, a + 2);
            }
            start = end;
        }
    }

    /// <summary>bead 구슬의 빛무리(모두 찼을 때), 껍질 뒤쪽, 찬 정도만큼의 실 구체, 껍질 앞쪽, 금 선을 차례로 추가한다.</summary>
    private void AppendBead(int bead)
    {
        Vector3 center = _positions[_beadNodes[bead]];
        Quaternion rotation = _beadRotations[bead];
        Vector3 toView = (_viewPoint - center).normalized;
        float glow = _glow * _fills[bead];
        if (glow > 0f) AppendGlow(center, toView, glow);

        int shellStart = _vertices.Count;
        for (int i = 0; i < _shellUnit.Length; i++)
        {
            Vector3 normal = rotation * _shellUnit[i];
            AddVertex(center + normal * _beadRadius, normal, _shellTint, 1f);
        }

        _frontShellIndices.Clear();
        for (int i = 0; i < _shellTriangles.Length; i += 3)
        {
            Vector3 normal = rotation * (_shellUnit[_shellTriangles[i]] + _shellUnit[_shellTriangles[i + 1]] + _shellUnit[_shellTriangles[i + 2]]);
            List<int> target = Vector3.Dot(normal, toView) > 0f ? _frontShellIndices : _indices;
            target.Add(shellStart + _shellTriangles[i]);
            target.Add(shellStart + _shellTriangles[i + 1]);
            target.Add(shellStart + _shellTriangles[i + 2]);
        }

        if (_fills[bead] > 0f) AppendThreadSphere(bead, center, rotation, toView);
        _indices.AddRange(_frontShellIndices);
        AppendCracks(bead, center, rotation, toView);
    }

    /// <summary>bead 구슬 속 실 구체를 center·rotation 자리에 추가한다. 카메라 반대쪽 구간을 먼저 옅게 그리고 찬 정도만큼 굵고 진해진다.</summary>
    private void AppendThreadSphere(int bead, Vector3 center, Quaternion rotation, Vector3 toView)
    {
        ThreadSphere sphere = _spheres[bead];
        sphere.Sample(_flows[bead], _time);
        float fill = _fills[bead];
        float radius = _beadRadius * _sphereRadiusRatio;
        float halfMain = _sphereStyle.MainWidth * 0.5f * Mathf.Lerp(0.5f, 1f, fill);
        Color tint = _sphereStyle.Color;
        tint.a *= fill;
        int count = sphere.SampleCount;
        for (int pass = 0; pass < 2; pass++)
        {
            bool front = pass == 1;
            for (int thread = 0; thread < ThreadSphere.THREAD_COUNT; thread++)
            {
                for (int branch = 0; branch < 2; branch++)
                {
                    for (int i = 0; i < count - 1; i++)
                    {
                        Vector3 middle = rotation * (sphere.GetPoint(thread, i) + sphere.GetPoint(thread, i + 1));
                        if (Vector3.Dot(middle, toView) >= 0f != front) continue;
                        sphere.GetStroke(thread, branch == 1, i, halfMain, out Vector3 start, out float startHalf);
                        sphere.GetStroke(thread, branch == 1, i + 1, halfMain, out Vector3 end, out float endHalf);
                        if (startHalf <= 0f && endHalf <= 0f) continue;
                        int first = _vertices.Count;
                        AddSphereEdge(center, rotation, radius, start, sphere.GetSide(thread, i), startHalf, toView, tint);
                        AddSphereEdge(center, rotation, radius, end, sphere.GetSide(thread, i + 1), endHalf, toView, tint);
                        for (int k = 0; k < 3; k++)
                            AddQuad(first + k, first + k + 1, first + 5 + k, first + 4 + k);
                    }
                }
            }
        }
    }

    /// <summary>구 표면 점 point에서 side 방향으로 반폭 half와 Feather만큼 더 나간 투명 가장자리까지 4점을 추가한다. 카메라 반대쪽일수록 옅다.</summary>
    private void AddSphereEdge(Vector3 center, Quaternion rotation, float radius, Vector3 point, Vector3 side, float half, Vector3 toView, Color tint)
    {
        Vector3 normal = rotation * point;
        tint.a *= Mathf.Lerp(_sphereStyle.BackOpacity, 1f, Vector3.Dot(normal, toView) * 0.5f + 0.5f);
        Color clear = tint;
        clear.a = 0f;
        float outer = half + _sphereStyle.Feather;
        AddVertex(center + rotation * (point + side * outer).normalized * radius, normal, clear, 0f);
        AddVertex(center + rotation * (point + side * half).normalized * radius, normal, tint, 0f);
        AddVertex(center + rotation * (point - side * half).normalized * radius, normal, tint, 0f);
        AddVertex(center + rotation * (point - side * outer).normalized * radius, normal, clear, 0f);
    }

    /// <summary>bead 구슬 껍질의 금 선을 껍질 바로 바깥에 _crackWidth 두께의 띠로 추가한다. 카메라를 향한 면에서만 보인다.</summary>
    private void AppendCracks(int bead, Vector3 center, Quaternion rotation, Vector3 toView)
    {
        float radius = _beadRadius * CRACK_LIFT;
        float half = _crackWidth * 0.5f / radius;
        foreach (Vector3[] line in _cracks[bead])
        {
            int first = _vertices.Count;
            for (int i = 0; i < line.Length; i++)
            {
                Vector3 tangent = line[Mathf.Min(i + 1, line.Length - 1)] - line[Mathf.Max(i - 1, 0)];
                Vector3 side = Vector3.Cross(line[i], tangent).normalized;
                Vector3 normal = rotation * line[i];
                Color color = _crackColor;
                color.a *= Mathf.Clamp01(Vector3.Dot(normal, toView) * 3f) * Mathf.Sin(Mathf.PI * (i + 0.5f) / line.Length);
                AddVertex(center + rotation * (line[i] + side * half).normalized * radius, normal, color, 0f);
                AddVertex(center + rotation * (line[i] - side * half).normalized * radius, normal, color, 0f);
            }
            for (int i = 0; i < line.Length - 1; i++)
            {
                int a = first + i * 2;
                AddQuad(a, a + 1, a + 3, a + 2);
            }
        }
    }

    /// <summary>center 구슬 뒤에 카메라 화면과 나란한 둥근 붉은 빛무리를 고리별 농도로 추가한다. 농도에는 실 구체 색과 glow를 곱한다.</summary>
    private void AppendGlow(Vector3 center, Vector3 toView, float glow)
    {
        int ringCount = GLOW_RING_RADII.Length;
        int first = _vertices.Count;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float radius = _beadRadius * (ring == ringCount - 1 ? _fullGlowRadius : GLOW_RING_RADII[ring]);
            Color ringColor = _sphereStyle.Color;
            ringColor.a *= GLOW_RING_ALPHAS[ring] * glow;
            for (int segment = 0; segment < GLOW_SEGMENTS; segment++)
            {
                float angle = Mathf.PI * 2f * segment / GLOW_SEGMENTS;
                Vector3 offset = (_viewRight * Mathf.Cos(angle) + _viewUp * Mathf.Sin(angle)) * radius;
                AddVertex(center + offset, toView, ringColor, 0f);
            }
        }
        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            int inner = first + ring * GLOW_SEGMENTS;
            int outer = inner + GLOW_SEGMENTS;
            for (int segment = 0; segment < GLOW_SEGMENTS; segment++)
            {
                int next = (segment + 1) % GLOW_SEGMENTS;
                AddQuad(inner + segment, outer + segment, outer + next, inner + next);
            }
        }
    }

    /// <summary>루트 기준 위치 position, 법선 normal, 색 color, 껍질 여부 shell(1이면 껍질)로 정점 하나를 추가한다.</summary>
    private void AddVertex(Vector3 position, Vector3 normal, Color color, float shell)
    {
        _vertices.Add(position);
        _normals.Add(normal);
        _colors.Add(color);
        _uvs.Add(new Vector2(shell, 0f));
    }

    /// <summary>a-b-c-d 순서의 사각형을 삼각형 두 개로 _indices에 추가한다.</summary>
    private void AddQuad(int a, int b, int c, int d)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
        _indices.Add(a);
        _indices.Add(c);
        _indices.Add(d);
    }
}
