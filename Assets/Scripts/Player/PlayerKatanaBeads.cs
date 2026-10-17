using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using BlindBlade.UI.CombatHud;

/// <summary>
/// 1인칭 칼 핸드가드(Tsuba) 밑쪽에 끈-구슬-끈-구슬-끈-구슬로 매달린 구슬 세 개를 퍼펙트 패링 토큰의 다이제틱 UI로 그린다.
/// 끈과 구슬은 시점 기준 공간에서 중력·관성·거리 제약으로 흔들려, 가드·납도 등 칼 움직임과 시점 회전을 물리적으로 따라간다.
/// 구슬 껍질은 살짝 금이 간 텅 빈 회색 유리 구슬이고, 토큰이 찰 때마다 위 구슬부터 토큰 HUD와 같은 굵은 실 두 가닥이 공전하며 속을 구체로 채운다.
/// 구슬이 모두 차면 토큰 HUD처럼 실 구체가 더 빠르게 돌고 구슬 뒤에 맥동하는 빛무리가 퍼진다.
/// 모든 움직임은 플레이어 시계를 따른다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(310)]
[RequireComponent(typeof(PlayerController), typeof(PlayerClock), typeof(PlayerEmpower))]
public sealed class PlayerKatanaBeads : MonoBehaviour
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
    // 빛무리 고리 반지름(구슬 반지름 대비, 마지막은 바깥 끝 배율로 대체)과 고리별 농도다. 토큰 HUD 빛무리와 같다.
    private static readonly float[] GLOW_RING_RADII = { 0f, 0.6f, 1f, 1.3f, 1f };
    private static readonly float[] GLOW_RING_ALPHAS = { 0.55f, 0.45f, 0.3f, 0.12f, 0f };

    [Header("연결")]
    [SerializeField, Tooltip("1인칭 칼을 자식으로 둔 시점(Player Camera) Transform. 끈 물리와 메시를 이 공간에서 계산한다.")]
    private Transform _viewSpace;
    [SerializeField, Tooltip("끈을 매다는 칼 핸드가드 렌더러(KatanaRig/Sword/Tsuba, 원기둥). 꺼지면 구슬도 숨긴다.")]
    private Renderer _attach;
    [SerializeField, Tooltip("끈을 매다는 지점(_attach 로컬 좌표). 기본값은 핸드가드의 손잡이 쪽 면 테두리 중 대기·가드 자세에서 가장 아래인 -X 쪽이다.")]
    private Vector3 _attachLocalOffset = new Vector3(-0.5f, -1f, 0f);
    [SerializeField, Tooltip("BlindBlade/KatanaBead 셰이더 재질. 껍질 색·가장자리 밝기는 재질에서 조절한다.")]
    private Material _material;

    [Header("구슬")]
    [SerializeField, Min(0.001f), Tooltip("구슬 반지름(m)")]
    private float _beadRadius = 0.008f;
    [SerializeField, Range(0.3f, 1f), Tooltip("구슬 반지름 대비 속을 채우는 실 구체 반지름")]
    private float _sphereRadiusRatio = 0.8f;
    [SerializeField, Tooltip("껍질에 곱하는 색. 알파로 껍질 전체 농도를 조절한다.")]
    private Color _shellTint = Color.white;
    [SerializeField, Range(0, 6), Tooltip("구슬마다 껍질에 난 금 개수")]
    private int _crackCount = 3;
    [SerializeField, Min(0f), Tooltip("금 선 두께(m)")]
    private float _crackWidth = 0.0004f;
    [SerializeField] private Color _crackColor = new Color(0.86f, 0.87f, 0.9f, 0.55f);
    [SerializeField, Tooltip("금 모양을 정하는 난수 시드. 바꾸면 금 모양이 달라진다.")]
    private int _crackSeed = 7;

    [Header("끈")]
    [SerializeField, Min(0.001f), Tooltip("핸드가드에서 첫 구슬까지 끈 길이(m)")]
    private float _firstCordLength = 0.025f;
    [SerializeField, Min(0.001f), Tooltip("구슬 사이 끈 길이(m)")]
    private float _cordLength = 0.014f;
    [SerializeField, Range(1, 6), Tooltip("끈 하나를 나누는 물리 마디 수")]
    private int _cordSubdivisions = 3;
    [SerializeField, Min(0.0001f), Tooltip("끈 두께(m)")]
    private float _cordWidth = 0.0014f;
    [SerializeField] private Color _cordColor = Color.white;

    [Header("물리")]
    [SerializeField, Min(0f), Tooltip("중력 가속도(m/s²)")]
    private float _gravity = 9.81f;
    [SerializeField, Range(0.8f, 1f), Tooltip("1/60초마다 남는 속도 비율. 작을수록 흔들림이 빨리 잦아든다.")]
    private float _damping = 0.97f;
    [SerializeField, Range(1, 32), Tooltip("끈 길이 제약을 푸는 반복 횟수. 클수록 끈이 덜 늘어난다.")]
    private int _iterations = 16;
    [SerializeField, Range(1f, 1.5f), Tooltip("끈 마디가 원래 길이보다 늘어날 수 있는 최대 배율. 1이면 전혀 늘어나지 않는다.")]
    private float _maxStretch = 1.02f;
    [SerializeField, Range(0f, 1f), Tooltip("시점이 돌 때 구슬이 세계에 남으려는 정도. 클수록 크게 뒤처졌다 흔들린다. 플레이어 이동은 영향을 주지 않는다.")]
    private float _viewInertia = 0.12f;
    [SerializeField, Min(0f), Tooltip("한 프레임에 관성으로 밀리는 최대 거리(m). 아주 빠른 시점 회전에서 끈이 튀지 않게 한다.")]
    private float _maxInertiaStep = 0.006f;
    [SerializeField, Min(0f), Tooltip("구슬·끈이 시점에 다가올 수 있는 최소 깊이(m). 카메라 근평면(0.3m)에 잘리지 않게 한다.")]
    private float _minViewDepth = 0.34f;
    [SerializeField, Range(0.05f, 1f), Tooltip("끈 마디 대비 구슬의 역질량. 작을수록 구슬이 무거워 끈이 더 끌려간다.")]
    private float _beadInverseMass = 0.4f;

    [Header("실 구체")]
    [SerializeField, Tooltip("찬 구슬 속을 채우는 실 구체. 토큰 HUD와 같은 실이지만 더 굵고 길어 구를 완전히 이룬다. Feather는 구 반지름 대비 비율이다.")]
    private CombatTokenOrbitStyle _sphereStyle = new CombatTokenOrbitStyle(2.2f, 0.85f, 0.08f);

    [Header("가득 참")]
    [SerializeField, Min(1f), Tooltip("구슬이 모두 찼을 때 실 구체가 도는 속도 배율")]
    private float _fullSpeedMultiplier = 2.5f;
    [SerializeField, Min(0f), Tooltip("구슬이 모두 찼을 때 구슬 뒤에 퍼지는 빛무리 세기")]
    private float _fullGlowStrength = 1f;
    [SerializeField, Min(1f), Tooltip("빛무리 바깥 끝 반지름(구슬 반지름 대비)")]
    private float _fullGlowRadius = 1.7f;
    [SerializeField, Range(0f, 1f), Tooltip("빛무리가 맥동하며 옅어지는 정도. 0이면 맥동하지 않는다.")]
    private float _fullGlowPulseDepth = 0.35f;
    [SerializeField, Min(0f), Tooltip("빛무리 맥동 속도(초당 횟수)")]
    private float _fullGlowPulseSpeed = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("가득 찬 상태로 바뀌거나 풀릴 때 빛·속도가 바뀌는 시간(초, 플레이어 시간)")]
    private float _fullTransitionDuration = 0.3f;

    [Header("상태")]
    private PlayerController _player;
    private PlayerClock _clock;
    private PlayerEmpower _empower;
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
    private Quaternion _previousViewRotation;
    private bool _hanging;
    private float _time;
    private float _fullBlend;
    private float _glowTime;
    private float _glow;

    /// <summary>검 손잡이에 가장 가까운 첫 구슬의 시점 좌표를 월드 좌표로 변환해 안내 위치를 반환한다.</summary>
    public bool TryGetBeadGuidePosition(out Vector3 position)
    {
        if (_positions == null || _beadNodes == null || _viewSpace == null)
        {
            position = Vector3.zero;
            return false;
        }

        position = _viewSpace.TransformPoint(_positions[_beadNodes[0]]);
        return true;
    }

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _clock = GetComponent<PlayerClock>();
        _empower = GetComponent<PlayerEmpower>();
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
        // 씬 종료 중에는 자식 렌더러가 먼저 파괴될 수 있다.
        if (_meshRenderer != null)
            _meshRenderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_meshObject != null) Destroy(_meshObject);
        if (_mesh != null) Destroy(_mesh);
    }

    void LateUpdate()
    {
        bool visible = _player.CurrentState != PlayerActionState.Dead
            && _attach.enabled && _attach.gameObject.activeInHierarchy;
        _meshRenderer.enabled = visible;
        if (!visible)
        {
            _hanging = false;
            return;
        }

        float deltaTime = Mathf.Min(_clock.DeltaTime, MAX_DELTA_TIME);
        Vector3 anchor = _viewSpace.InverseTransformPoint(_attach.transform.TransformPoint(_attachLocalOffset));
        if (_hanging)
            Simulate(anchor, deltaTime);
        else
            HangChain(anchor);

        _previousViewRotation = _viewSpace.rotation;
        UpdateBeadRotations();
        UpdateFills(deltaTime);
        BuildMesh();
    }

    /// <summary>
    /// 끈을 매단 핸드가드 지점(0번 점)부터 끈 마디와 구슬 점을 차례로 늘어놓는 물리 점 배열과 각 점까지의 길이 제약·역질량을 만든다.
    /// 구슬에 닿는 마디 길이에는 구슬 반지름을 더해 끈이 구슬 표면에서 이어지게 한다. 구슬 점 번호를 _beadNodes에 저장한다.
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
                // 앞 구슬에서 나오는 첫 마디와 다음 구슬로 들어가는 마지막 마디는 구슬 반지름만큼 길다.
                if (step == 0 && bead > 0)
                    _restLengths[node] += _beadRadius;
                if (isBead)
                    _restLengths[node] += _beadRadius;
                _inverseMasses[node] = isBead ? _beadInverseMass : 1f;
                if (isBead)
                    _beadNodes[bead] = node;
                node++;
            }
        }
    }

    /// <summary>
    /// SHELL_RINGS × SHELL_SEGMENTS 위경도 격자로 단위 구 껍질 점(_shellUnit)과 삼각형 번호(_shellTriangles)를 만든다.
    /// </summary>
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

    /// <summary>
    /// 구슬마다 _crackSeed로 정해진 금 _crackCount개를 단위 구 표면의 꺾인 선(구슬 자체 좌표)으로 만들어 _cracks에 저장한다.
    /// 각 금은 무작위 점에서 시작해 CRACK_STEP_ANGLE씩 나아가며 매 걸음 최대 CRACK_TURN 라디안 꺾인다.
    /// </summary>
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
    /// 시점 Transform 아래에 구슬·끈을 그릴 동적 메시와 MeshRenderer를 만든다. 정점은 시점 기준 좌표라 오브젝트는 원점에 둔다.
    /// 그림자·광원 탐침을 끄고 칼과 같은 레이어를 쓰며, 생성한 오브젝트를 _meshObject, _meshRenderer, _mesh에 저장한다.
    /// </summary>
    private void CreateMeshObject()
    {
        _meshObject = new GameObject("KatanaBeads");
        _meshObject.layer = _attach.gameObject.layer;
        _meshObject.transform.SetParent(_viewSpace, false);
        _mesh = new Mesh { name = "KatanaBeads" };
        _mesh.MarkDynamic();
        _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
        _meshRenderer.sharedMaterial = _material;
        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _meshRenderer.allowOcclusionWhenDynamic = false;
        _meshRenderer.enabled = false;
    }

    /// <summary>
    /// 끈과 구슬을 anchor(시점 기준 끈을 매단 핸드가드 지점)에서 중력 방향으로 곧게 늘어뜨려 놓고 속도를 0으로 한다. 구슬 회전은 끈 방향에 맞춘다.
    /// </summary>
    private void HangChain(Vector3 anchor)
    {
        Vector3 down = _viewSpace.InverseTransformDirection(Vector3.down);
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
    /// 시점 기준 공간에서 끈·구슬을 deltaTime만큼 진행한다. 시점 회전에만 _viewInertia만큼 세계에 남으려는 관성을 주고(플레이어 이동은 무시),
    /// 중력과 감쇠를 Verlet 적분한 뒤 0번 점을 anchor에 고정하고 길이 제약과 최소 깊이를 _iterations번 푼다. 플레이어 시계가 멈추면 그대로 둔다.
    /// </summary>
    private void Simulate(Vector3 anchor, float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        // 시점이 돈 만큼 점을 반대로 돌린 자리로 조금 밀어 흔들림을 만든다. 시점의 위치 이동은 반영하지 않는다.
        Quaternion viewDelta = Quaternion.Inverse(_viewSpace.rotation) * _previousViewRotation;
        Vector3 gravity = _viewSpace.InverseTransformDirection(Vector3.down) * (_gravity * deltaTime * deltaTime);
        float keep = Mathf.Pow(_damping, deltaTime * 60f);
        for (int node = 1; node < _positions.Length; node++)
        {
            Vector3 stay = viewDelta * _positions[node];
            _positions[node] += Vector3.ClampMagnitude((stay - _positions[node]) * _viewInertia, _maxInertiaStep);
            Vector3 velocity = (_positions[node] - _previous[node]) * keep;
            _previous[node] = _positions[node];
            _positions[node] += velocity + gravity;
        }

        for (int iteration = 0; iteration < _iterations; iteration++)
        {
            _positions[0] = anchor;
            for (int node = 1; node < _positions.Length; node++)
                SolveDistance(node - 1, node);
            for (int node = 1; node < _positions.Length; node++)
                _positions[node].z = Mathf.Max(_positions[node].z, _minViewDepth);
        }

        LimitStretch();
    }

    /// <summary>
    /// 반복 제약 뒤에도 남은 늘어남을 없애도록, 위 마디부터 차례로 각 점을 윗점에서 원래 길이 × _maxStretch 안으로 끌어온다.
    /// 끌어온 만큼 이전 위치도 함께 옮겨 끈이 튕겨 나가는 속도가 생기지 않게 한다.
    /// </summary>
    private void LimitStretch()
    {
        for (int node = 1; node < _positions.Length; node++)
        {
            Vector3 delta = _positions[node] - _positions[node - 1];
            float limit = _restLengths[node] * _maxStretch;
            if (delta.sqrMagnitude <= limit * limit)
                continue;

            Vector3 correction = _positions[node - 1] + delta.normalized * limit - _positions[node];
            _positions[node] += correction;
            _previous[node] += correction;
        }
    }

    /// <summary>
    /// first와 second 점 사이 거리를 second의 길이 제약(_restLengths[second])에 맞추도록 두 점을 역질량 비율로 옮긴다.
    /// </summary>
    private void SolveDistance(int first, int second)
    {
        Vector3 delta = _positions[second] - _positions[first];
        float distance = delta.magnitude;
        float weight = _inverseMasses[first] + _inverseMasses[second];
        if (distance <= 0.000001f || weight <= 0f)
            return;

        Vector3 correction = delta * ((distance - _restLengths[second]) / (distance * weight));
        _positions[first] += correction * _inverseMasses[first];
        _positions[second] -= correction * _inverseMasses[second];
    }

    /// <summary>
    /// 구슬마다 위쪽 끈 방향을 구슬의 +Y로 삼도록 _beadRotations를 이전 회전에서 최소한으로 돌려, 끈이 흔들리면 구슬도 따라 기운다.
    /// </summary>
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
    /// PlayerEmpower.Tokens만큼 위 구슬부터 실 구체를 FadeDuration(플레이어 시간)에 걸쳐 채우고, 나머지는 같은 시간에 걸쳐 비운다.
    /// 새로 차는 구슬은 흐름을 다시 시작하고, 보이는 구슬은 두 실의 흐름을 적분한다. 결과는 _fills에 저장한다.
    /// 구슬이 모두 차면 _fullTransitionDuration에 걸쳐 실 구체를 _fullSpeedMultiplier배로 빠르게 돌리고, 맥동하는 빛무리 세기를 _glow에 저장한다.
    /// </summary>
    private void UpdateFills(float deltaTime)
    {
        int tokens = _empower.Tokens;
        _fullBlend = Mathf.MoveTowards(_fullBlend, tokens >= BEAD_COUNT ? 1f : 0f, deltaTime / _fullTransitionDuration);
        // 공전·궤도면 회전·두께 변화를 모두 빠르게 하도록 실 구체의 시계를 배율만큼 빨리 돌린다.
        float orbitDelta = deltaTime * Mathf.Lerp(1f, _fullSpeedMultiplier, _fullBlend);
        _time += orbitDelta;
        _glowTime += deltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_glowTime * _fullGlowPulseSpeed * Mathf.PI * 2f);
        _glow = _fullBlend * _fullGlowStrength * (1f - _fullGlowPulseDepth * pulse);
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            float previous = _fills[bead];
            bool filled = bead < tokens;
            if (filled && previous <= 0f)
                _flows[bead].Reset(_sphereStyle, _time);

            _fills[bead] = Mathf.MoveTowards(previous, filled ? 1f : 0f, deltaTime / _sphereStyle.FadeDuration);
            if (_fills[bead] > 0f)
                _flows[bead].Step(_sphereStyle, _time, orbitDelta);
        }
    }

    /// <summary>
    /// 끈을 먼저 그리고, 시점에서 먼 구슬부터 껍질 뒤쪽 → 실 구체 → 껍질 앞쪽 → 금 순서로 한 메시에 쌓아 _mesh에 반영한다.
    /// </summary>
    private void BuildMesh()
    {
        _vertices.Clear();
        _normals.Clear();
        _colors.Clear();
        _uvs.Clear();
        _indices.Clear();
        AppendCords();

        // 구슬끼리 겹칠 때 가까운 구슬이 위에 오도록 먼 구슬부터 그린다.
        for (int bead = 0; bead < BEAD_COUNT; bead++)
        {
            int order = bead;
            while (order > 0 && _positions[_beadNodes[_drawOrder[order - 1]]].z < _positions[_beadNodes[bead]].z)
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

    /// <summary>
    /// 핸드가드에서 첫 구슬까지, 구슬과 구슬 사이의 끈을 시점을 향한 띠로 추가한다. 구슬에 닿는 끝은 구슬 표면까지만 그린다.
    /// </summary>
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
                // 구슬 중심 대신 이웃 마디 쪽 구슬 표면에서 끈이 나오고 들어간다.
                if (node == end)
                    point += (_positions[node - 1] - point).normalized * _beadRadius;
                else if (node == start && start > 0)
                    point += (_positions[node + 1] - point).normalized * _beadRadius;

                Vector3 tangent = _positions[Mathf.Min(node + 1, end)] - _positions[Mathf.Max(node - 1, start)];
                Vector3 side = Vector3.Cross(tangent, point).normalized * (_cordWidth * 0.5f);
                AddVertex(point + side, -point.normalized, _cordColor, 0f);
                AddVertex(point - side, -point.normalized, _cordColor, 0f);
            }

            for (int node = 0; node < end - start; node++)
            {
                int a = first + node * 2;
                AddQuad(a, a + 1, a + 3, a + 2);
            }

            start = end;
        }
    }

    /// <summary>
    /// bead 구슬의 빛무리(구슬이 모두 찼을 때), 껍질 뒤쪽 삼각형, 찬 정도만큼의 실 구체, 껍질 앞쪽 삼각형, 금 선을 차례로 추가한다.
    /// 껍질 삼각형은 시점을 향하는지로 앞·뒤를 나누며, 껍질 색은 셰이더가 가장자리 밝기로 정한다.
    /// </summary>
    private void AppendBead(int bead)
    {
        Vector3 center = _positions[_beadNodes[bead]];
        Quaternion rotation = _beadRotations[bead];
        Vector3 toView = -center.normalized;
        float glow = _glow * _fills[bead];
        if (glow > 0f)
            AppendGlow(center, glow);

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

        if (_fills[bead] > 0f)
            AppendThreadSphere(bead, center, rotation, toView);

        _indices.AddRange(_frontShellIndices);
        AppendCracks(bead, center, rotation, toView);
    }

    /// <summary>
    /// bead 구슬 속 실 구체를 center·rotation 자리에 _sphereRadiusRatio 반지름으로 추가한다. 시점 반대쪽(toView 기준 뒤쪽) 구간을 먼저 옅게 그린다.
    /// 찬 정도 _fills[bead]만큼 굵어지고 진해진다.
    /// </summary>
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
                        if (Vector3.Dot(middle, toView) >= 0f != front)
                            continue;

                        sphere.GetStroke(thread, branch == 1, i, halfMain, out Vector3 start, out float startHalf);
                        sphere.GetStroke(thread, branch == 1, i + 1, halfMain, out Vector3 end, out float endHalf);
                        if (startHalf <= 0f && endHalf <= 0f)
                            continue;

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

    /// <summary>
    /// 구 자체 좌표의 표면 점 point에서 side 방향으로 반폭 half만큼 떨어진 양쪽 가장자리와 Feather만큼 더 나간 투명 가장자리 4점을 구 표면에 추가한다.
    /// center·rotation·radius로 시점 기준 좌표로 옮기고, toView 반대쪽일수록 BackOpacity까지 옅게 한다.
    /// </summary>
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

    /// <summary>
    /// bead 구슬 껍질의 금 선을 껍질 바로 바깥에 _crackWidth 두께의 띠로 추가한다. 시점을 향한 면에서만 보이고 뒤쪽으로 돌아가면 사라진다.
    /// </summary>
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
                // 끝으로 갈수록 가늘게 사라져 금이 자연스럽게 끝난다.
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

    /// <summary>
    /// center 구슬 뒤에 화면과 나란한 둥근 빛무리를 GLOW_RING_RADII·GLOW_RING_ALPHAS 고리로 추가한다.
    /// 바깥 끝 반지름은 구슬 반지름 × _fullGlowRadius이고, 농도에는 실 구체 색과 glow를 곱한다.
    /// </summary>
    private void AppendGlow(Vector3 center, float glow)
    {
        int ringCount = GLOW_RING_RADII.Length;
        int first = _vertices.Count;
        Vector3 normal = -center.normalized;
        for (int ring = 0; ring < ringCount; ring++)
        {
            float radius = _beadRadius * (ring == ringCount - 1 ? _fullGlowRadius : GLOW_RING_RADII[ring]);
            Color ringColor = _sphereStyle.Color;
            ringColor.a *= GLOW_RING_ALPHAS[ring] * glow;
            for (int segment = 0; segment < GLOW_SEGMENTS; segment++)
            {
                float angle = Mathf.PI * 2f * segment / GLOW_SEGMENTS;
                // 시점 공간의 가로·세로 축으로 펼쳐 화면과 나란한 원이 된다.
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                AddVertex(center + offset, normal, ringColor, 0f);
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

    /// <summary>시점 기준 위치 position, 법선 normal, 색 color, 껍질 여부 shell(1이면 껍질)로 정점 하나를 추가한다.</summary>
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
