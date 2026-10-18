using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 1인칭 칼집과 손잡이 위에 X자(∞)를 그리며 짧은 유선형 실 두 가닥이 같은 방향으로 불규칙하게 순회한다.
/// 첫 실은 카메라 쪽 앞면을, 두 번째 실은 플레이어가 보지 못하는 뒷면을 동시에 돈다.
/// 각 실은 굵은 줄기와 그 1/5 굵기의 잔가지로 이뤄지며, 잔가지는 줄기에서 살짝 벗어났다 다시 돌아간다. 두께는 계속 부드럽게 변한다.
/// 좌클릭 대상 돌진으로 선택될 적이 있으면 실이 매끈한 유선형 줄기와 위아래로만 벗어나는 잔가지로 그 적에게 이어지고,
/// 연결 중에는 적에게서 칼로 느린 맥박이 위아래로 흐른다.
/// 모든 움직임은 플레이어 시계를 따른다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(PlayerController), typeof(PlayerClock), typeof(KatanaCombatCoordinator))]
public sealed class PlayerDashTargetThread : MonoBehaviour
{
    private const int THREAD_COUNT = 2;
    private const int STRAND_COUNT = THREAD_COUNT * 2;
    private const int MAX_PULSES = 12;
    private const float TWO_PI = Mathf.PI * 2f;
    private const float LINKED_THRESHOLD = 0.6f;
    private const float FIRST_BEAT_DELAY = 0.15f;
    private const float TANGENT_STEP = 0.01f;
    private const float STREAMLINE_PEAK = 0.357f;
    private const float DRAWN_DISTANCE = 0.05f;
    private const float DRAWN_BLEND_DURATION = 0.15f;

    [Header("연결")]
    [SerializeField] private Camera _viewCamera;
    [SerializeField, Tooltip("1인칭 칼집 렌더러. 메시 경계의 가장 긴 축을 칼집 방향으로 사용한다.")]
    private Renderer _scabbard;
    [SerializeField, Tooltip("1인칭 칼 손잡이 렌더러. Awake 때(칼집에 꽂힌 자세) 칼집 기준 위치를 기억해 경로에 쓴다.")]
    private Renderer _grip;
    [SerializeField, Tooltip("BlindBlade/DashThread 셰이더 재질. 색·가장자리·가려진 부분 농도는 재질에서 조절한다.")]
    private Material _material;

    [Header("굵기")]
    [SerializeField, Range(24, 192), Tooltip("실 하나를 나누는 점 개수")]
    private int _segments = 96;
    [SerializeField, Min(0.0005f), Tooltip("굵은 줄기의 가장 굵은 곳 두께(화면 높이 비율). 거리와 관계없이 화면에서 같은 두께로 보인다.")]
    private float _mainWidth = 0.0192f;
    [SerializeField, Range(0.05f, 1f), Tooltip("굵은 줄기 대비 잔가지 두께")]
    private float _branchWidthRatio = 0.2f;
    [SerializeField, Range(0f, 0.9f), Tooltip("줄기·잔가지 두께가 부드럽게 굵어졌다 가늘어지는 정도")]
    private float _thicknessVariation = 0.35f;
    [SerializeField, Min(0f), Tooltip("두께 변화가 실을 따라 흐르는 속도")]
    private float _thicknessChangeSpeed = 1.2f;
    [SerializeField, Range(0f, 1f)] private float _opacity = 1f;
    [SerializeField, Min(0.01f), Tooltip("사망·우클릭 가드·칼 숨김 때 실이 사라지고 다시 나타나는 실시간(초)")]
    private float _fadeDuration = 0.2f;

    [Header("X자 순회 경로")]
    [SerializeField, Min(0.1f), Tooltip("X 교차점에서 손잡이 끝까지 거리 대비 손잡이 쪽 고리 길이")]
    private float _handleLobeScale = 1.08f;
    [SerializeField, Range(0.1f, 1.2f), Tooltip("X 교차점에서 칼집 끝까지 거리 대비 칼집 쪽 고리 길이")]
    private float _scabbardLobeScale = 0.55f;
    [SerializeField, Range(0.05f, 1f), Tooltip("칼을 뽑았을 때(공격) 손잡이 쪽 고리를 줄이는 비율. 손잡이 쪽 고리는 칼집에 꽂힌 자리 기준이다.")]
    private float _drawnHandleLobeScale = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("칼집·손잡이 단면 반지름 대비 고리 반지름 배율")]
    private float _loopRadiusScale = 1.35f;
    [SerializeField, Range(0f, 1f), Tooltip("경로 모양이 일렁이는 정도")]
    private float _loopWobble = 0.25f;
    [SerializeField, Tooltip("X자를 그리는 방향을 반대로 한다. 교차점에서 손잡이 쪽으로 갈 때 벌어지는 쪽이 바뀐다.")]
    private bool _reverseWrap;
    [SerializeField, Range(10f, 90f), Tooltip("한 면 위에서 X자가 칼 둘레 좌우로 퍼지는 최대 각도(도). 90 이하라 실이 자기 면을 벗어나지 않는다.")]
    private float _faceSweep = 70f;
    [SerializeField, Tooltip("실이 화면 높이만큼 흐를 때 경로를 따라 나아가는 각도(라디안). 경로 한 바퀴는 2π다.")]
    private float _loopAnglePerFlow = 21f;

    [Header("굵은 줄기")]
    [SerializeField, Range(0.05f, 0.95f), Tooltip("경로 한 바퀴 대비 줄기 획의 길이. 나머지는 비어 실이 이어지지 않는다.")]
    private float _mainStrokeLength = 0.3f;

    [Header("뒷면 실")]
    [SerializeField, Tooltip("첫 실이 카메라 쪽 앞면을 도는 동안 플레이어가 보지 못하는 뒷면을 같은 방향·같은 속도로 도는 두 번째 실을 그린다.")]
    private bool _counterThread = true;
    [SerializeField, Range(0f, 360f), Tooltip("두 번째 실이 도는 면의 칼 둘레 각도(도, 앞면 기준). 180이면 카메라 반대쪽 뒷면이다.")]
    private float _counterAngle = 180f;
    [SerializeField, Range(-180f, 180f), Tooltip("두 번째 실의 꼬리에서 머리까지 면 각도가 바뀌는 양(도). 두 실이 서로 조금 비틀려 보이게 한다.")]
    private float _counterTwist = 40f;
    [SerializeField, Range(0.1f, 3f), Tooltip("첫 실 대비 두 번째 실의 평균 흐름 속도. 1이 아니면 두 실의 거리가 평균적으로 계속 벌어진다.")]
    private float _counterSpeedRatio = 0.8f;

    [Header("잔가지")]
    [SerializeField, Range(0f, 1f), Tooltip("줄기 꼬리(0)~머리(1) 중 잔가지가 빠져나오는 위치")]
    private float _branchStart = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("줄기 꼬리(0)~머리(1) 중 잔가지가 다시 들어가는 위치")]
    private float _branchEnd = 0.85f;
    [SerializeField, Min(0f), Tooltip("잔가지가 줄기에서 벗어나는 최대 거리(줄기 두께 대비)")]
    private float _branchSpread = 1.2f;
    [SerializeField, Range(1, 4), Tooltip("칼 순회 중 잔가지가 벗어났다 돌아오는 횟수")]
    private int _branchLobes = 1;
    [SerializeField, Min(0f), Tooltip("벗어나는 폭이 커졌다 작아지는 속도")]
    private float _branchBreathSpeed = 2.2f;
    [SerializeField, Tooltip("벗어나는 방향이 줄기 둘레를 도는 속도(라디안/초)")]
    private float _branchSwirlSpeed = 0.8f;

    [Header("불규칙한 흐름")]
    [SerializeField, Min(0f), Tooltip("당김 사이 평균 표류 속도(화면 높이/초). 잡음으로 25~175% 사이를 오가며 멈추지 않는다.")]
    private float _flowDrift = 0.032f;
    [SerializeField, Min(0f), Tooltip("한 번 당길 때 더해지는 기준 속도(화면 높이/초). 당길 때마다 20~120% 사이로 무작위다.")]
    private float _tugStrength = 0.32f;
    [SerializeField, Range(0f, 1f), Tooltip("당김이 크게 치솟을 확률")]
    private float _surgeChance = 0.35f;
    [SerializeField, Min(1f), Tooltip("크게 치솟을 때 당김 세기 배율")]
    private float _surgeMultiplier = 2.2f;
    [SerializeField, Min(0.01f), Tooltip("당긴 속도가 표류 속도로 줄어드는 빠르기 최소값. 당길 때마다 최소~최대 사이로 무작위라 길게 미끄러지거나 짧게 튄다.")]
    private float _tugDampingMin = 1.5f;
    [SerializeField, Min(0.01f), Tooltip("당긴 속도가 표류 속도로 줄어드는 빠르기 최대값")]
    private float _tugDampingMax = 5f;
    [SerializeField, Min(0.05f), Tooltip("다음 당김까지 평균 간격(초). 간격은 지수 분포라 몰려서 오거나 오래 쉬기도 한다.")]
    private float _tugMeanInterval = 1.3f;
    [SerializeField, Min(0f), Tooltip("다음 당김까지 최소 간격(초)")]
    private float _tugIntervalMin = 0.15f;
    [SerializeField, Min(0.05f), Tooltip("다음 당김까지 최대 간격(초)")]
    private float _tugIntervalMax = 4f;
    [SerializeField, Range(0f, 1f), Tooltip("당김이 반대 방향으로 되감길 확률")]
    private float _reverseTugChance = 0.2f;
    [SerializeField, Range(0f, 0.9f), Tooltip("실마다 평균 속도가 따로 천천히 오르내리는 정도. 0.4면 60~140% 사이를 오간다.")]
    private float _speedWander = 0.4f;

    [Header("대상 연결")]
    [SerializeField, Min(0.01f), Tooltip("실이 대상까지 뻗어 연결되는 시간(초)")]
    private float _linkDuration = 0.12f;
    [SerializeField, Min(0.01f), Tooltip("돌진 불가·공격 시작 때 실이 칼로 돌아가는 시간(초)")]
    private float _releaseDuration = 0.12f;
    [SerializeField, Min(0.01f), Tooltip("돌진 후보를 다시 고르는 실시간 간격(초)")]
    private float _candidateInterval = 0.05f;
    [SerializeField, Min(0f), Tooltip("다른 대상으로 옮겨 갈 때 끝점이 따라가는 빠르기")]
    private float _retargetSharpness = 14f;
    [SerializeField, Min(0f), Tooltip("연결된 실이 아래로 처지는 정도(실 길이 비율). 뻗는 동안에는 더 처진다.")]
    private float _slack = 0.035f;
    [SerializeField, Min(0f), Tooltip("연결선에서 잔가지가 줄기에서 위아래로 벗어나는 최대 거리(화면 높이 비율)")]
    private float _linkBranchSpread = 0.02f;
    [SerializeField, Range(1, 6), Tooltip("연결선에서 잔가지가 벗어났다 돌아오는 횟수. 위·아래를 번갈아 벗어난다.")]
    private int _linkBranchLobes = 2;
    [SerializeField, Min(0f), Tooltip("연결될 때 실이 옆으로 굽이치는 최대 폭(화면 높이 비율)")]
    private float _linkUndulation = 0.035f;
    [SerializeField, Min(0f), Tooltip("연결될 때 실을 따라 생기는 굽이 수")]
    private float _linkUndulationWaves = 2.5f;
    [SerializeField, Tooltip("굽이가 실을 따라 대상 쪽으로 흐르는 속도(초당 굽이 수)")]
    private float _linkUndulationSpeed = 4f;
    [SerializeField, Min(0.01f), Tooltip("연결이 시작된 뒤 굽이가 잦아들어 곧게 펴지는 시간(초)")]
    private float _linkUndulationDuration = 0.45f;

    [Header("맥박 파동")]
    [SerializeField, Min(0.1f), Tooltip("한 번의 '쿵-쿵' 박동이 다시 시작되는 간격(초)")]
    private float _beatInterval = 1.4f;
    [SerializeField, Min(0.01f), Tooltip("첫 박과 둘째 박 사이 간격(초)")]
    private float _beatGap = 0.25f;
    [SerializeField, Range(0f, 2f), Tooltip("둘째 박의 세기(첫 박 1 대비)")]
    private float _secondBeatStrength = 0.6f;
    [SerializeField, Min(0.1f), Tooltip("맥박이 적에게서 칼로 흐르는 속도(미터/초)")]
    private float _waveSpeed = 2.5f;
    [SerializeField, Min(0.01f), Tooltip("짧은 실에서도 맥박이 이동하는 최소 시간(초)")]
    private float _minimumWaveDuration = 0.8f;
    [SerializeField, Range(0.01f, 0.5f), Tooltip("맥박 한 박의 폭(실 길이 비율)")]
    private float _wavePacketWidth = 0.12f;
    [SerializeField, Min(0f), Tooltip("맥박이 실을 월드 Y축(위아래)으로 들어 올리는 높이(화면 높이 비율)")]
    private float _waveAmplitude = 0.016f;
    [SerializeField, Min(0f), Tooltip("맥박이 지날 때 밝아지는 정도")]
    private float _waveGlow = 1.6f;
    [SerializeField, Min(0f), Tooltip("맥박이 칼 쪽 끝에 닿은 뒤 머무는 시간(초)")]
    private float _waveArrivalLinger = 0.25f;

    [Header("상태")]
    private PlayerController _player;
    private PlayerClock _clock;
    private KatanaCombatCoordinator _combat;
    private GameObject _meshObject;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<Vector4> _uv0 = new List<Vector4>();
    private readonly List<Vector4> _uv1 = new List<Vector4>();
    private readonly List<int> _indices = new List<int>();
    private Vector3[][] _points;
    private float[][] _profiles;
    private float[][] _widths;
    private float[][] _fronts;
    private float[] _glows;
    private Vector3[] _linkCenters;
    private Vector3[] _linkVerticals;
    private float[] _linkBranchScales;
    private LocalFrame _scabbardFrame;
    private LocalFrame _gripFrame;
    private Matrix4x4 _gripToScabbard;
    private float _drawn;
    private Vector3 _junction;
    private Vector3 _handleDirection;
    private Vector3 _scabbardDirection;
    private Vector3 _ringSide;
    private Vector3 _ringUp;
    private float _handleLength;
    private float _scabbardLength;
    private float _handleRadius;
    private float _scabbardRadius;
    private float _crossRadius;
    private float _crossAngle;
    private readonly Pulse[] _pulses = new Pulse[MAX_PULSES];
    private int _nextPulse;
    private float _nextBeatTime;
    private bool _secondBeatPending;
    private float _visibility;
    private readonly float[] _flowOffsets = new float[THREAD_COUNT];
    private readonly float[] _flowVelocities = new float[THREAD_COUNT];
    private readonly float[] _nextTugTimes = new float[THREAD_COUNT];
    private readonly float[] _tugDampings = { 3f, 3f };
    private bool _hasCandidate;
    private PlayerCombatTarget _candidate;
    private float _nextCandidateTime;
    private float _linkProgress;
    private string _linkTargetId;
    private Vector3 _linkEnd;
    private float _linkSettle;

    public bool HasLinkedTarget => _linkProgress >= LINKED_THRESHOLD && _hasCandidate;
    public Vector3 LinkedTargetPosition => _linkEnd;

    /// <summary>
    /// 현재 적에게 연결된 발도 실의 검 쪽 시작점을 origin에 저장해 반환한다.
    /// 연결된 대상이나 실 경로가 없으면 false를 반환한다.
    /// </summary>
    public bool TryGetLinkedThreadOrigin(out Vector3 origin)
    {
        if (!HasLinkedTarget || _linkCenters == null || _linkCenters.Length == 0)
        {
            origin = default;
            return false;
        }

        origin = _linkCenters[0];
        return true;
    }

    /// <summary>현재 연결된 발도 실의 실제 메시 점 전체를 감싸는 월드 경계를 반환한다.</summary>
    public bool TryGetLinkedThreadBounds(out Bounds bounds)
    {
        if (!HasLinkedTarget || _linkCenters == null || _linkCenters.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds(_linkCenters[0], Vector3.zero);
        for (int i = 1; i < _linkCenters.Length; i++)
            bounds.Encapsulate(_linkCenters[i]);
        return true;
    }

    /// <summary>
    /// 칼집과 손잡이 위 X자 실 교차점의 마지막 월드 위치를 junction에 저장해 반환한다.
    /// 실이 숨겨져 교차점을 갱신하지 않는 동안에는 false를 반환한다.
    /// </summary>
    public bool TryGetSwordJunction(out Vector3 junction)
    {
        junction = _junction;
        return _visibility > 0f;
    }

    private readonly struct LocalFrame
    {
        public readonly Vector3 Center;
        public readonly Vector3 Axis;
        public readonly Vector3 SideExtent;
        public readonly Vector3 UpExtent;
        public readonly float HalfLength;

        /// <summary>
        /// bounds의 가장 긴 축을 길이 방향으로, 나머지 두 축을 단면 반지름 벡터로 보관한다. 모두 렌더러 로컬 좌표다.
        /// </summary>
        public LocalFrame(Bounds bounds)
        {
            Vector3 extents = bounds.extents;
            int axis = extents.x >= extents.y && extents.x >= extents.z ? 0 : extents.y >= extents.z ? 1 : 2;
            Center = bounds.center;
            Axis = AxisVector(axis);
            SideExtent = AxisVector((axis + 1) % 3) * extents[(axis + 1) % 3];
            UpExtent = AxisVector((axis + 2) % 3) * extents[(axis + 2) % 3];
            HalfLength = extents[axis];
        }
    }

    private readonly struct Pulse
    {
        public readonly float StartTime;
        public readonly float Strength;

        /// <summary>
        /// 맥박 한 박의 시작 시각과 세기를 보관한다. 세기가 0이면 빈 슬롯이다.
        /// </summary>
        public Pulse(float startTime, float strength)
        {
            StartTime = startTime;
            Strength = strength;
        }
    }

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _clock = GetComponent<PlayerClock>();
        _combat = GetComponent<KatanaCombatCoordinator>();
        _scabbardFrame = new LocalFrame(_scabbard.localBounds);
        _gripFrame = new LocalFrame(_grip.localBounds);
        // 애니메이션이 돌기 전의 프리팹 자세는 칼이 꽂힌 상태라 이때 손잡이의 칼집 기준 위치를 기억한다.
        _gripToScabbard = _scabbard.transform.worldToLocalMatrix * _grip.transform.localToWorldMatrix;
        AllocateBuffers();
        CreateMeshObject();
    }

    void OnDisable()
    {
        ClearLink();
        _linkProgress = 0f;
        _hasCandidate = false;
        _visibility = 0f;
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
        float deltaTime = _clock.DeltaTime;
        float time = _clock.Now;
        UpdateCandidate();
        UpdateLink(deltaTime);
        UpdateHeartbeat(time);
        UpdateFlow(0, 1f, deltaTime, time);
        UpdateFlow(1, _counterSpeedRatio, deltaTime, time);

        // 우클릭 가드 중에는 칼만 뽑혀 올라가 실이 칼집에 홀로 남으므로 숨긴다.
        bool visible = _player.CurrentState != PlayerActionState.Dead && !_player.IsGuarding
            && _scabbard.enabled && _scabbard.gameObject.activeInHierarchy;
        _visibility = Mathf.MoveTowards(_visibility, visible ? 1f : 0f, Time.unscaledDeltaTime / _fadeDuration);
        if (_visibility <= 0f)
        {
            _meshRenderer.enabled = false;
            return;
        }

        BuildThread(time, deltaTime);
        _meshRenderer.enabled = true;
    }

    /// <summary>index(0=x, 1=y, 2=z)에 해당하는 로컬 단위 축 벡터를 반환한다.</summary>
    private static Vector3 AxisVector(int index)
    {
        return index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
    }

    /// <summary>
    /// _segments 개수로 두 실의 줄기·잔가지와 연결선 계산용 배열을 만든다. 매 프레임 할당 없이 재사용한다.
    /// </summary>
    private void AllocateBuffers()
    {
        int count = _segments + 1;
        _points = new Vector3[STRAND_COUNT][];
        _profiles = new float[STRAND_COUNT][];
        _widths = new float[STRAND_COUNT][];
        _fronts = new float[STRAND_COUNT][];
        for (int strand = 0; strand < STRAND_COUNT; strand++)
        {
            _points[strand] = new Vector3[count];
            _profiles[strand] = new float[count];
            _widths[strand] = new float[count];
            _fronts[strand] = new float[count];
        }

        _glows = new float[count];
        _linkCenters = new Vector3[count];
        _linkVerticals = new Vector3[count];
        _linkBranchScales = new float[count];
    }

    /// <summary>
    /// 플레이어 아래에 실을 그릴 동적 메시와 MeshRenderer를 만든다. 그림자·광원 탐침을 끄고 처음에는 숨긴다.
    /// 생성한 오브젝트를 _meshObject, _meshRenderer, _mesh에 저장한다.
    /// </summary>
    private void CreateMeshObject()
    {
        _meshObject = new GameObject("DashTargetThread");
        _meshObject.transform.SetParent(transform, false);
        _mesh = new Mesh { name = "DashTargetThread" };
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
    /// 자유·가드 상태이고 반격 기회가 아니면 _candidateInterval마다 PlayerController의 돌진 대상 규칙으로 후보를 조회한다.
    /// 행동 중이면 즉시 후보를 비우며 결과를 _hasCandidate와 _candidate에 저장한다.
    /// </summary>
    private void UpdateCandidate()
    {
        PlayerActionState state = _player.CurrentState;
        bool ready = (state == PlayerActionState.Free || state == PlayerActionState.Guarding)
            && !_combat.CanCounterAttack;
        if (!ready)
        {
            _hasCandidate = false;
            _nextCandidateTime = 0f;
            return;
        }

        if (Time.unscaledTime < _nextCandidateTime)
            return;

        _nextCandidateTime = Time.unscaledTime + _candidateInterval;
        _hasCandidate = _player.TryGetTargetedDashCandidate(out _candidate);
    }

    /// <summary>
    /// 후보 유무로 연결 진행도를 deltaTime만큼 늘이거나 줄이고 끝점을 대상 충돌체 중심으로 옮긴다.
    /// 새 연결이면 끝점을 대상에 바로 두고 굽이 잦아듦 _linkSettle을 0부터 다시 올리며, 완전히 풀리면 대상과 맥박을 지운다.
    /// </summary>
    private void UpdateLink(float deltaTime)
    {
        // 다음 후보 조회 전에 대상이 풀로 돌아가 비활성화되면 경계가 원점으로 바뀌므로 바로 푼다.
        bool linkable = _hasCandidate && _candidate.HitCollider != null
            && _candidate.HitCollider.enabled && _candidate.HitCollider.gameObject.activeInHierarchy;
        if (!linkable)
        {
            _linkProgress = Mathf.MoveTowards(_linkProgress, 0f, deltaTime / _releaseDuration);
            if (_linkProgress <= 0f)
                ClearLink();
            return;
        }

        Vector3 targetCenter = _candidate.HitCollider.bounds.center;
        if (_linkTargetId == null)
        {
            _linkEnd = targetCenter;
            _linkSettle = 0f;
        }
        _linkTargetId = _candidate.TargetId;

        // 같은 대상은 거의 즉시 따라가고, 대상이 바뀌면 끝점이 새 대상으로 미끄러져 넘어간다.
        _linkEnd = Vector3.Lerp(_linkEnd, targetCenter, 1f - Mathf.Exp(-_retargetSharpness * deltaTime));
        _linkProgress = Mathf.MoveTowards(_linkProgress, 1f, deltaTime / _linkDuration);
        _linkSettle = Mathf.MoveTowards(_linkSettle, 1f, deltaTime / _linkUndulationDuration);
    }

    /// <summary>연결 대상을 비우고 남은 맥박과 박동 예약을 지운다.</summary>
    private void ClearLink()
    {
        _linkTargetId = null;
        _secondBeatPending = false;
        for (int i = 0; i < MAX_PULSES; i++)
            _pulses[i] = default;
    }

    /// <summary>
    /// 실이 연결돼 있으면 time 기준으로 '쿵-쿵' 두 박을 _beatInterval마다 보낸다. 첫 박은 세기 1, 둘째 박은 _beatGap 뒤 _secondBeatStrength다.
    /// 연결 전에는 다음 박동 시각만 FIRST_BEAT_DELAY 뒤로 맞춰 둔다.
    /// </summary>
    private void UpdateHeartbeat(float time)
    {
        if (_linkProgress < LINKED_THRESHOLD)
        {
            _nextBeatTime = time + FIRST_BEAT_DELAY;
            _secondBeatPending = false;
            return;
        }

        if (time < _nextBeatTime)
            return;

        if (_secondBeatPending)
        {
            AddPulse(_secondBeatStrength);
            _secondBeatPending = false;
            _nextBeatTime += Mathf.Max(0.01f, _beatInterval - _beatGap);
        }
        else
        {
            AddPulse(1f);
            _secondBeatPending = true;
            _nextBeatTime += _beatGap;
        }
    }

    /// <summary>strength 세기의 맥박 한 박을 현재 플레이어 시계 시각으로 순환 슬롯에 기록한다. 세기가 0 이하면 생략한다.</summary>
    private void AddPulse(float strength)
    {
        if (strength <= 0f)
            return;

        _pulses[_nextPulse] = new Pulse(_clock.Now, strength);
        _nextPulse = (_nextPulse + 1) % MAX_PULSES;
    }

    /// <summary>
    /// thread 실이 일정한 속도로 흐르지 않도록 표류와 무작위 당김·되감김을 deltaTime만큼 적분한다.
    /// 실마다 표류 잡음·당김 시각·세기·감속을 따로 뽑아 두 실이 함께 빨라지거나 느려지지 않으며, speedScale로 평균 속도를 바꾸고 _speedWander만큼 실마다 평균 속도가 천천히 오르내린다.
    /// time 기준 다음 당김 시각은 지수 분포 간격으로 정하고, 흐른 거리(화면 높이 단위)를 _flowOffsets[thread]에 저장한다.
    /// </summary>
    private void UpdateFlow(int thread, float speedScale, float deltaTime, float time)
    {
        // 표류 속도는 잡음으로 느려졌다 빨라지지만 멈추거나 되돌아가지 않는다. 실마다 다른 잡음 줄을 쓴다.
        // 실마다 평균 속도가 따로 천천히 오르내려 두 실의 앞서고 뒤처짐이 예측하기 어렵게 바뀐다.
        speedScale *= 1f + _speedWander * (Mathf.PerlinNoise(time * 0.12f, 61.7f + thread * 19.3f) - 0.5f) * 2f;
        float drift = _flowDrift * speedScale * (0.25f + 1.5f * Mathf.PerlinNoise(time * 0.31f, 4.2f + thread * 37.1f));
        _flowVelocities[thread] = Mathf.Lerp(_flowVelocities[thread], drift,
            1f - Mathf.Exp(-_tugDampings[thread] * deltaTime));
        if (time >= _nextTugTimes[thread])
        {
            float direction = Random.value < _reverseTugChance ? -0.6f : 1f;
            float surge = Random.value < _surgeChance ? _surgeMultiplier * Random.Range(0.8f, 1.2f) : 1f;
            _flowVelocities[thread] += _tugStrength * speedScale * Random.Range(0.2f, 1.2f) * surge * direction;
            _tugDampings[thread] = Random.Range(_tugDampingMin, Mathf.Max(_tugDampingMin, _tugDampingMax));
            // 지수 분포 간격이라 당김이 몰려서 오거나 오래 쉬기도 한다.
            float interval = -Mathf.Log(1f - Random.value * 0.999f) * _tugMeanInterval;
            _nextTugTimes[thread] = time + Mathf.Clamp(interval, _tugIntervalMin, Mathf.Max(_tugIntervalMin, _tugIntervalMax));
        }

        _flowOffsets[thread] += _flowVelocities[thread] * deltaTime;
    }

    /// <summary>
    /// 칼집의 현재 월드 자세와 칼집에 꽂힌 자세의 손잡이 위치로 X 교차점, 두 고리의 방향·길이·반지름, 공통 둘레 기준축을 갱신한다.
    /// 실제 손잡이가 꽂힌 자리에서 벗어나면(공격으로 칼을 뽑으면) deltaTime 동안 손잡이 쪽 고리를 줄인다.
    /// cameraPosition 쪽을 교차점의 앞면으로 삼아 X가 화면 쪽에서 엇갈리게 _crossAngle을 정한다.
    /// </summary>
    private void UpdateFrames(Vector3 cameraPosition, float deltaTime)
    {
        Matrix4x4 scabbardMatrix = _scabbard.transform.localToWorldMatrix;
        // 손에 든 실제 손잡이가 아니라 칼집에 꽂힌 자리의 손잡이를 써서 실이 손 쪽으로 끌려가지 않게 한다.
        Matrix4x4 gripMatrix = scabbardMatrix * _gripToScabbard;
        Vector3 sheathedGrip = gripMatrix.MultiplyPoint3x4(_gripFrame.Center);
        Vector3 liveGrip = _grip.transform.localToWorldMatrix.MultiplyPoint3x4(_gripFrame.Center);
        bool drawn = (liveGrip - sheathedGrip).sqrMagnitude > DRAWN_DISTANCE * DRAWN_DISTANCE;
        _drawn = Mathf.MoveTowards(_drawn, drawn ? 1f : 0f, deltaTime / DRAWN_BLEND_DURATION);

        Vector3 scabbardStart = scabbardMatrix.MultiplyPoint3x4(_scabbardFrame.Center - _scabbardFrame.Axis * _scabbardFrame.HalfLength);
        Vector3 scabbardEnd = scabbardMatrix.MultiplyPoint3x4(_scabbardFrame.Center + _scabbardFrame.Axis * _scabbardFrame.HalfLength);
        Vector3 gripStart = gripMatrix.MultiplyPoint3x4(_gripFrame.Center - _gripFrame.Axis * _gripFrame.HalfLength);
        Vector3 gripEnd = gripMatrix.MultiplyPoint3x4(_gripFrame.Center + _gripFrame.Axis * _gripFrame.HalfLength);

        // 손잡이에 가까운 칼집 끝을 칼집 입구로, 칼집 입구에 가까운 손잡이 끝을 코등이 쪽으로 본다.
        bool startIsMouth = (scabbardStart - sheathedGrip).sqrMagnitude <= (scabbardEnd - sheathedGrip).sqrMagnitude;
        Vector3 mouth = startIsMouth ? scabbardStart : scabbardEnd;
        Vector3 scabbardFar = startIsMouth ? scabbardEnd : scabbardStart;
        bool gripStartIsNear = (gripStart - mouth).sqrMagnitude <= (gripEnd - mouth).sqrMagnitude;
        Vector3 gripNear = gripStartIsNear ? gripStart : gripEnd;
        Vector3 gripFar = gripStartIsNear ? gripEnd : gripStart;

        _junction = (mouth + gripNear) * 0.5f;
        Vector3 toHandle = gripFar - _junction;
        Vector3 toScabbard = scabbardFar - _junction;
        _handleLength = toHandle.magnitude * _handleLobeScale * Mathf.Lerp(1f, _drawnHandleLobeScale, _drawn);
        _scabbardLength = toScabbard.magnitude * _scabbardLobeScale;
        _handleDirection = toHandle.normalized;
        _scabbardDirection = toScabbard.normalized;
        _handleRadius = SectionRadius(gripMatrix, _gripFrame) * _loopRadiusScale;
        _scabbardRadius = SectionRadius(scabbardMatrix, _scabbardFrame) * _loopRadiusScale;
        _crossRadius = Mathf.Max(_handleRadius, _scabbardRadius);

        Vector3 axis = (_handleDirection - _scabbardDirection).normalized;
        _ringSide = Vector3.ProjectOnPlane(scabbardMatrix.MultiplyVector(_scabbardFrame.SideExtent), axis).normalized;
        _ringUp = Vector3.Cross(axis, _ringSide);
        Vector3 toCamera = Vector3.ProjectOnPlane(cameraPosition - _junction, axis);
        _crossAngle = Mathf.Atan2(Vector3.Dot(toCamera, _ringUp), Vector3.Dot(toCamera, _ringSide));
    }

    /// <summary>matrix로 frame의 두 단면 반지름 벡터를 월드로 옮겨 더 큰 길이를 반환한다.</summary>
    private static float SectionRadius(Matrix4x4 matrix, LocalFrame frame)
    {
        return Mathf.Max(matrix.MultiplyVector(frame.SideExtent).magnitude, matrix.MultiplyVector(frame.UpExtent).magnitude);
    }

    /// <summary>
    /// theta(라디안) 위치의 X자 경로 점을 월드 좌표로 반환하고 바깥 방향 단위 벡터를 radial로 출력한다.
    /// sin(theta)가 양수인 반 바퀴는 손잡이 쪽, 음수인 반 바퀴는 칼집 쪽 고리이며 한 면 위에서 8자를 그려 교차점에서 X로 엇갈린다.
    /// 면의 중심은 카메라 쪽 앞면에서 ringOffset(라디안)만큼 돌린 자리이며 π면 플레이어가 보지 못하는 뒷면이다.
    /// 둘레 좌우로는 _faceSweep까지만 퍼져 실이 자기 면을 벗어나지 않는다. time으로 경로가 천천히 일렁인다.
    /// </summary>
    private Vector3 GetPathPoint(float theta, float time, float ringOffset, out Vector3 radial)
    {
        float lobe = Mathf.Sin(theta);
        bool handle = lobe >= 0f;
        float reach = Mathf.Abs(lobe) * (handle ? _handleLength : _scabbardLength);
        Vector3 direction = handle ? _handleDirection : _scabbardDirection;
        // 각도의 cos·sin을 잡음 좌표로 써서 한 바퀴 이음매에서도 모양이 끊기지 않는다.
        float wobble = (Mathf.PerlinNoise(Mathf.Cos(theta) * 0.9f + time * 0.45f, Mathf.Sin(theta) * 0.9f + 3.7f) - 0.5f)
            * 2f * _loopWobble;
        float lobeRadius = handle ? _handleRadius : _scabbardRadius;
        float radius = Mathf.Lerp(_crossRadius, lobeRadius, Mathf.SmoothStep(0f, 1f, Mathf.Abs(lobe) * 2f))
            * (1f + wobble * 0.5f);
        // sin(2θ)는 교차점(θ=0, π)과 고리 끝(θ=π/2)에서 0이라 두 고리가 면 가운데에서 X로 엇갈린다.
        float sweep = _faceSweep * Mathf.Deg2Rad * Mathf.Sin(2f * theta);
        float phi = _crossAngle + ringOffset + (_reverseWrap ? -sweep : sweep);
        radial = _ringSide * Mathf.Cos(phi) + _ringUp * Mathf.Sin(phi);
        return _junction + direction * reach + radial * radius;
    }

    /// <summary>
    /// 머리(fromHead=0)에서 꼬리(fromHead=1)까지의 유선형 두께 비율(0~1)을 반환한다. 머리는 둥글고 꼬리는 길게 가늘어진다.
    /// </summary>
    private static float Streamline(float fromHead)
    {
        float x = Mathf.Clamp01(fromHead);
        return Mathf.Clamp01(Mathf.Sqrt(x) * Mathf.Pow(1f - x, 1.2f) / STREAMLINE_PEAK);
    }

    /// <summary>
    /// 연결선에서 s(0 칼~1 대상) 위치의 줄기 두께 비율을 반환한다. 칼 쪽은 가늘고 대상 쪽으로 굵어지며 대상 끝은 둥글다.
    /// </summary>
    private static float LinkProfile(float s)
    {
        float tail = Mathf.SmoothStep(0f, 1f, s / 0.05f);
        float head = Mathf.Sqrt(Mathf.Clamp01((1f - s) / 0.06f));
        return Mathf.Lerp(0.45f, 1f, s * s) * tail * head;
    }

    /// <summary>잔가지의 v(0 나오는 곳~1 들어가는 곳) 위치 두께 비율을 반환한다. 양 끝은 줄기 속으로 가늘게 사라진다.</summary>
    private static float BranchProfile(float v)
    {
        // sin(π)는 부동소수 오차로 아주 작은 음수가 되어 거듭제곱이 NaN이 되므로 0 아래를 자른다.
        return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))), 0.6f);
    }

    /// <summary>
    /// v(0~1) 위치에서 잔가지가 줄기를 벗어난 정도를 lobes와 time으로 반환한다. 양 끝과 lobes 사이 마디에서 0이라 줄기로 돌아가며,
    /// 마디마다 반대쪽으로 벗어나고 폭은 시간에 따라 커졌다 작아진다.
    /// </summary>
    private float BranchOffset(float v, int lobes, float time)
    {
        float lobe = Mathf.Sin(Mathf.PI * lobes * v);
        // 잔물결도 마디에서 0이 되게 곱해 잔가지가 마디마다 줄기로 정확히 돌아간다.
        float wave = lobe + 0.25f * Mathf.Sin(Mathf.PI * 3f * v + time * 2.3f) * Mathf.Abs(lobe);
        float breath = 0.55f + 0.45f * Mathf.Sin(time * _branchBreathSpeed);
        return wave * breath;
    }

    /// <summary>
    /// s(0~1) 위치와 seed로 time에 따라 부드럽게 흐르는 두께 배율을 반환한다. 1을 중심으로 _thicknessVariation만큼 굵어지거나 가늘어진다.
    /// </summary>
    private float Thickness(float s, float time, float seed)
    {
        float noise = Mathf.PerlinNoise(s * 2.2f - time * _thicknessChangeSpeed + seed, seed * 1.7f + 0.3f);
        return 1f + _thicknessVariation * (noise - 0.5f) * 2f;
    }

    /// <summary>
    /// time 시각의 X자 경로·연결선·맥박으로 두 실의 줄기와 잔가지 띠 메시를 다시 만든다.
    /// 두 실은 각자의 _flowOffsets만큼 같은 방향으로 따로 나아가며 첫 실은 카메라 쪽 앞면, 두 번째 실은 보이지 않는 뒷면을 돈다.
    /// 연결 중에는 _linkProgress만큼 뒷면 실(두 번째 실이 없으면 첫 실)의 줄기·잔가지가 연결선으로 모이고 다른 실은 계속 칼을 돈다.
    /// deltaTime은 칼을 뽑았을 때 손잡이 쪽 고리를 줄이는 데 쓰며 결과를 _mesh에 기록한다.
    /// </summary>
    private void BuildThread(float time, float deltaTime)
    {
        int count = _segments + 1;
        Transform cameraTransform = _viewCamera.transform;
        Vector3 cameraPosition = cameraTransform.position;
        Vector3 cameraForward = cameraTransform.forward;
        float screenScale = 2f * Mathf.Tan(_viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

        UpdateFrames(cameraPosition, deltaTime);
        int threadCount = _counterThread ? THREAD_COUNT : 1;
        // 뒷면 실이 적에게 이어지고 앞면 실은 칼을 계속 돈다. 뒷면 실이 꺼져 있으면 앞면 실이 이어진다.
        int linkThread = threadCount - 1;
        float morph = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_linkProgress / 0.4f));
        if (morph > 0f)
            BuildLink(time, count, RingOffset(linkThread), cameraPosition, cameraForward, screenScale);
        else
            System.Array.Clear(_glows, 0, count);

        for (int thread = 0; thread < threadCount; thread++)
        {
            float flowAngle = _flowOffsets[thread] * _loopAnglePerFlow;
            // 두 번째 실은 같은 경로 위치를 뒷면에서 돌며, 꼬리에서 머리로 갈수록 면 각도가 조금씩 바뀌어 비틀린다.
            float ringTwist = thread == 0 ? 0f : _counterTwist * Mathf.Deg2Rad;
            BuildStrandPair(thread * 2, flowAngle, RingOffset(thread), ringTwist, time + thread * 17.3f,
                thread == linkThread ? morph : 0f, count, cameraPosition, cameraForward, screenScale);
        }

        float alpha = _visibility * _opacity;
        _vertices.Clear();
        _colors.Clear();
        _uv0.Clear();
        _uv1.Clear();
        _indices.Clear();
        for (int strand = 0; strand < threadCount * 2; strand++)
        {
            bool branch = strand % 2 == 1;
            // 맥박 밝기는 연결선이 되는 실에만 쓴다.
            float glowScale = strand / 2 == linkThread ? 1f : 0f;
            float width = branch ? _mainWidth * _branchWidthRatio : _mainWidth;
            ComputeWidths(strand, count, width, glowScale, cameraPosition, cameraForward, screenScale);
            AppendRibbon(_points[strand], _widths[strand], _fronts[strand], count, branch, alpha, glowScale, cameraPosition);
        }

        _meshObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetColors(_colors);
        _mesh.SetUVs(0, _uv0);
        _mesh.SetUVs(1, _uv1);
        _mesh.SetTriangles(_indices, 0);
        _mesh.RecalculateBounds();
    }

    /// <summary>
    /// mainIndex 줄기와 그 다음 칸 잔가지의 점·앞뒤·두께 비율을 계산한다. head는 줄기 머리의 경로 각도다.
    /// ringOffset(라디안)은 줄기 가운데의 칼집 둘레 회전, ringTwist(라디안)는 꼬리에서 머리까지 그 회전이 바뀌는 양이다.
    /// 연결 중에는 morph만큼 연결선으로 모이며, 잔가지는 연결선에서 위아래로만 벗어났다 돌아온다. seedTime은 실마다 다른 잡음 시각이다.
    /// </summary>
    private void BuildStrandPair(int mainIndex, float head, float ringOffset, float ringTwist, float seedTime, float morph,
        int count, Vector3 cameraPosition, Vector3 cameraForward, float screenScale)
    {
        int branchIndex = mainIndex + 1;
        float span = TWO_PI * _mainStrokeLength;
        float swirl = seedTime * _branchSwirlSpeed;
        float time = _clock.Now;
        Vector3 fallbackSide = _viewCamera.transform.right;
        for (int i = 0; i < count; i++)
        {
            float s = i / (float)_segments;
            float mainRing = ringOffset + ringTwist * (s - 0.5f);
            Vector3 main = GetPathPoint(head - span * (1f - s), time, mainRing, out Vector3 mainRadial);
            float mainFront = Vector3.Dot(mainRadial, (cameraPosition - main).normalized);
            float mainProfile = Streamline(1f - s);

            // 잔가지는 줄기 획 안의 구간을 따라가며 줄기 옆으로 벗어난다.
            float u = Mathf.Lerp(_branchStart, _branchEnd, s);
            float branchRing = ringOffset + ringTwist * (u - 0.5f);
            float branchTheta = head - span * (1f - u);
            Vector3 branchBase = GetPathPoint(branchTheta, time, branchRing, out Vector3 branchRadial);
            Vector3 branchTangent = GetPathPoint(branchTheta + TANGENT_STEP, time, branchRing, out _)
                - GetPathPoint(branchTheta - TANGENT_STEP, time, branchRing, out _);
            Vector3 branchSide = Vector3.Cross(branchTangent, cameraPosition - branchBase);
            branchSide = branchSide.sqrMagnitude > 0.0000001f ? branchSide.normalized : fallbackSide;
            Vector3 branchDirection = (branchSide * Mathf.Cos(swirl) + branchRadial * (0.6f * Mathf.Sin(swirl))).normalized;
            float branchOffset = BranchOffset(s, _branchLobes, seedTime) * _branchSpread * _mainWidth
                * ScreenToWorld(branchBase, cameraPosition, cameraForward, screenScale);
            Vector3 branch = branchBase + branchDirection * branchOffset;
            float branchFront = Vector3.Dot(branchDirection * Mathf.Sign(branchOffset) + branchRadial,
                (cameraPosition - branch).normalized) * 0.5f;
            float branchProfile = BranchProfile(s);

            if (morph > 0f)
            {
                // 연결선의 잔가지는 맥박과 같은 위아래 방향으로만 벗어나 줄기가 옆으로 구불거리지 않게 한다.
                float linkOffset = BranchOffset(s, _linkBranchLobes, seedTime) * _linkBranchScales[i];
                main = Vector3.Lerp(main, _linkCenters[i], morph);
                branch = Vector3.Lerp(branch, _linkCenters[i] + _linkVerticals[i] * linkOffset, morph);
                mainFront = Mathf.Lerp(mainFront, 0f, morph);
                branchFront = Mathf.Lerp(branchFront, 0f, morph);
                mainProfile = Mathf.Lerp(mainProfile, LinkProfile(s), morph);
            }

            _points[mainIndex][i] = main;
            _points[branchIndex][i] = branch;
            _fronts[mainIndex][i] = mainFront;
            _fronts[branchIndex][i] = branchFront;
            _profiles[mainIndex][i] = mainProfile * Thickness(s, seedTime, 0f);
            _profiles[branchIndex][i] = branchProfile * Thickness(s, seedTime, 5.3f);
        }
    }

    /// <summary>thread 실이 도는 면의 칼 둘레 각도(라디안)를 반환한다. 첫 실은 앞면 0, 두 번째 실은 _counterAngle이다.</summary>
    private float RingOffset(int thread)
    {
        return thread == 0 ? 0f : _counterAngle * Mathf.Deg2Rad;
    }

    /// <summary>
    /// ringOffset 면의 X 교차점에서 _linkEnd까지 처진 매끈한 곡선을 만들고 맥박이 지나는 곳을 월드 Y축(위아래)으로만 들어 올린다.
    /// 연결이 시작되면 _linkSettle이 1이 될 때까지 실이 옆으로 굽이치며 대상 쪽으로 흐르다 곧게 펴진다.
    /// 맥박은 대상 쪽 끝에서 칼 쪽 끝으로 흐른다. 뻗는 동안은 끝점이 교차점에서 대상까지 이동한다.
    /// 결과를 _linkCenters·_glows에, 잔가지용 위쪽 방향과 벗어남 폭을 _linkVerticals·_linkBranchScales에 저장한다.
    /// </summary>
    private void BuildLink(float time, int count, float ringOffset, Vector3 cameraPosition, Vector3 cameraForward,
        float screenScale)
    {
        Vector3 anchor = GetPathPoint(0f, time, ringOffset, out _);
        float settle = 1f - _linkSettle;
        float swing = settle * settle * _linkUndulation;
        float reach = 1f - Mathf.Pow(1f - _linkProgress, 3f);
        Vector3 end = Vector3.Lerp(anchor, _linkEnd, reach);
        float length = Vector3.Distance(anchor, end);
        float sag = _slack * length * Mathf.Lerp(3f, 1f, _linkProgress);
        Vector3 middle = (anchor + end) * 0.5f + Vector3.down * sag;
        Vector3 control = middle * 2f - (anchor + end) * 0.5f;

        float fullLength = Mathf.Max(0.01f, Vector3.Distance(anchor, _linkEnd));
        float travelDuration = Mathf.Max(_minimumWaveDuration, fullLength / _waveSpeed);
        for (int i = 0; i < count; i++)
        {
            float s = i / (float)_segments;
            float inverse = 1f - s;
            Vector3 center = anchor * (inverse * inverse) + control * (2f * inverse * s) + end * (s * s);
            Vector3 tangent = (control - anchor) * (2f * inverse) + (end - control) * (2f * s);
            float scale = ScreenToWorld(center, cameraPosition, cameraForward, screenScale);
            float taper = Mathf.SmoothStep(0f, 1f, s / 0.07f) * Mathf.SmoothStep(0f, 1f, inverse / 0.1f);

            float envelope = 0f;
            float lift = 0f;
            for (int p = 0; p < MAX_PULSES; p++)
            {
                Pulse pulse = _pulses[p];
                if (pulse.Strength <= 0f)
                    continue;

                float elapsed = time - pulse.StartTime;
                if (elapsed < 0f || elapsed > travelDuration + _waveArrivalLinger)
                    continue;

                float travel = Mathf.Clamp01(elapsed / travelDuration);
                float fade = elapsed > travelDuration ? 1f - (elapsed - travelDuration) / _waveArrivalLinger : 1f;
                // 맥박은 대상 쪽(s=1)에서 칼 쪽(s=0)으로 내려온다.
                float distance = (s - (1f - travel)) / _wavePacketWidth;
                float strength = pulse.Strength * fade;
                envelope += Mathf.Exp(-distance * distance) * strength;
                // 혈관의 맥처럼 솟았다가 지나간 뒤쪽이 살짝 꺼진다.
                float trail = distance - 1.2f;
                lift += (Mathf.Exp(-distance * distance) - 0.35f * Mathf.Exp(-trail * trail)) * strength;
            }

            // 실에 수직인 위쪽 성분으로만 움직인다. 실이 거의 수직이면 움직이지 않는다.
            Vector3 vertical = Vector3.ProjectOnPlane(Vector3.up, tangent);
            vertical = vertical.sqrMagnitude > 0.0001f ? vertical.normalized : Vector3.zero;
            center += vertical * (lift * _waveAmplitude * scale * taper);

            // 연결되는 동안 화면 옆 방향으로 굽이가 대상 쪽으로 흐르다 잦아든다.
            if (swing > 0f)
            {
                Vector3 side = Vector3.Cross(tangent, cameraPosition - center);
                if (side.sqrMagnitude > 0.0000001f)
                {
                    float bend = Mathf.Sin(TWO_PI * (s * _linkUndulationWaves - time * _linkUndulationSpeed));
                    center += side.normalized * (bend * swing * scale * taper);
                }
            }

            _linkCenters[i] = center;
            _linkVerticals[i] = vertical;
            _linkBranchScales[i] = _linkBranchSpread * scale * taper * (1f + envelope);
            _glows[i] = _waveGlow * envelope;
        }
    }

    /// <summary>
    /// strand 실의 점마다 width(화면 높이 비율)·두께 비율·앞뒤와 glowScale을 곱한 맥박 밝기를 반영한 띠 반폭을 계산해 _widths에 저장한다.
    /// </summary>
    private void ComputeWidths(int strand, int count, float width, float glowScale, Vector3 cameraPosition,
        Vector3 cameraForward, float screenScale)
    {
        Vector3[] points = _points[strand];
        for (int i = 0; i < count; i++)
        {
            float scale = ScreenToWorld(points[i], cameraPosition, cameraForward, screenScale);
            _widths[strand][i] = width * 0.5f * scale * _profiles[strand][i] * (1f + 0.15f * _fronts[strand][i])
                * (1f + 0.5f * _glows[i] * glowScale);
        }
    }

    /// <summary>
    /// point의 카메라 앞쪽 깊이와 screenScale(2·tan(시야각/2))로 화면 높이 1에 해당하는 월드 길이를 반환한다.
    /// </summary>
    private float ScreenToWorld(Vector3 point, Vector3 cameraPosition, Vector3 cameraForward, float screenScale)
    {
        float depth = Mathf.Max(_viewCamera.nearClipPlane, Vector3.Dot(point - cameraPosition, cameraForward));
        return depth * screenScale;
    }

    /// <summary>
    /// points를 카메라를 향한 띠로 펼쳐 메시 버퍼에 추가한다. halfWidths는 점마다의 반폭, fronts는 앞뒤 정도, alpha는 정점 투명도다.
    /// glowScale을 곱한 _glows를 밝기로, branch 여부를 UV에 담아 셰이더가 줄기와 잔가지를 구분하게 한다.
    /// </summary>
    private void AppendRibbon(Vector3[] points, float[] halfWidths, float[] fronts, int count, bool branch,
        float alpha, float glowScale, Vector3 cameraPosition)
    {
        int baseIndex = _vertices.Count;
        Color color = new Color(1f, 1f, 1f, alpha);
        Vector4 kind = new Vector4(branch ? 1f : 0f, 0f, 0f, 0f);
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
            Vector3 side = Vector3.Cross(tangent, cameraPosition - points[i]);
            side = side.sqrMagnitude > 0.0000001f ? side.normalized * halfWidths[i] : Vector3.zero;
            float s = i / (float)(count - 1);
            _vertices.Add(points[i] - side);
            _vertices.Add(points[i] + side);
            _colors.Add(color);
            _colors.Add(color);
            float glow = _glows[i] * glowScale;
            _uv0.Add(new Vector4(s, 0f, glow, fronts[i]));
            _uv0.Add(new Vector4(s, 1f, glow, fronts[i]));
            _uv1.Add(kind);
            _uv1.Add(kind);
            if (i == 0)
                continue;

            int index = baseIndex + (i - 1) * 2;
            _indices.Add(index);
            _indices.Add(index + 1);
            _indices.Add(index + 2);
            _indices.Add(index + 1);
            _indices.Add(index + 3);
            _indices.Add(index + 2);
        }
    }
}
