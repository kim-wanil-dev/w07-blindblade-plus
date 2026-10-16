using UnityEngine;
using UnityEngine.Rendering;

using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// RainyStage의 빗방울을 GPU에서 갱신하고 검격이 지나간 공간과 파열을 관리한다.
/// 빗방울 땅 파동과 대나무 빗방울 잔상은 화면 공개 텍스처에 구 모양으로 더해 윤곽선 실루엣을 약하게 밝힌다.
/// 씬이 끝나면 GPU 자원을 해제하며 다른 씬에는 영향을 주지 않는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class RainStageWeather : MonoBehaviour
{
    private const int THREAD_GROUP_SIZE = 64;
    private const int DROP_STRIDE = 32;
    private const int SWEEP_STRIDE = 64;
    private const int GROUND_RIPPLE_STRIDE = 32;
    private const int MAX_BAMBOO_RAIN_HITS_PER_FRAME = 4;
    private const int MAX_BAMBOO_RAIN_HITS = 64;

    private static readonly int _rainRevealId = Shader.PropertyToID("_OutlineRainReveal");
    private static readonly int _rainRevealEnabledId = Shader.PropertyToID("_OutlineRainRevealEnabled");
    private static readonly int _sceneDepthId = Shader.PropertyToID("_RainSceneDepth");
    private static RainStageWeather _active;

    [Header("GPU 에셋")]
    [SerializeField] private ComputeShader _simulation;
    [SerializeField] private Material _rainMaterial;

    [Header("비")]
    [SerializeField, Range(256, 8192)] private int _dropletCount = 2048;
    [SerializeField, Range(3f, 30f)] private float _nearRadius = 15f;
    [SerializeField, Range(0, 8192), Tooltip("가까운 원 바깥부터 Far Radius까지 고리에 떨어지는 방울 수. 대숲까지 비를 내린다.")]
    private int _farDropletCount = 4096;
    [SerializeField, Min(3f), Tooltip("카메라에서 바깥 고리 비가 내리는 최대 수평 반경(미터).")]
    private float _farRadius = 40f;
    [SerializeField, Min(1f)] private float _heightAboveCamera = 7f;
    [SerializeField, Min(0f)] private float _depthBelowCamera = 2f;
    [SerializeField, Min(0.1f)] private float _fallSpeed = 11f;
    [SerializeField, Min(0.005f)] private float _dropletSize = 0.055f;
    [SerializeField, Min(0.01f)] private float _streakLength = 0.24f;
    [SerializeField] private Color _dropletOutlineColor = new Color(0.82f, 0.87f, 0.93f, 1f);
    [SerializeField, Range(0.01f, 0.2f)] private float _dropletOutlineWidth = 0.055f;
    [SerializeField, Range(0f, 1f)] private float _dropletOutlineOpacity = 0.58f;
    [SerializeField, Range(0f, 1f), Tooltip("퍼펙트 패링·극한 회피 파동 밖 평상시 떨어지는 빗방울의 선명함 배율. 파동 안에서는 1까지 밝아진다.")]
    private float _idleDropletVisibility = 0.3f;

    [Header("불릿 타임")]
    [SerializeField, Range(0f, 0.1f)] private float _bulletRainSpeed = 0.012f;
    [SerializeField, Min(0.01f)] private float _shapeTransitionTime = 0.12f;
    [SerializeField, Range(0.1f, 1f)] private float _bulletDropletScale = 0.33f;
    [SerializeField, Range(0f, 0.5f)] private float _bulletWiggleAmplitude = 0.25f;
    [SerializeField, Range(0.1f, 2f)] private float _bulletSplashSpeed = 0.65f;

    [Header("검격과 빈 자국")]
    [SerializeField, Min(0.01f)] private float _bladeThickness = 0.12f;
    [SerializeField, Min(0.01f)] private float _waveThickness = 0.35f;
    [SerializeField, Min(0.01f)] private float _trailLifetime = 0.75f;
    [SerializeField, Range(1, 64)] private int _maximumTrails = 32;
    [SerializeField, Range(1, 64)] private int _maximumHitsPerFrame = 48;

    [Header("물 파열")]
    [SerializeField, Min(0.05f)] private float _splashDuration = 0.3f;
    [SerializeField, Min(0.02f)] private float _splashSize = 0.35f;
    [SerializeField] private Color _splashColor = new Color(1f, 1f, 1f, 0.48f);

    [Header("빗방울 땅 파동")]
    [SerializeField, Range(0f, 1f), Tooltip("지면에 닿은 빗방울이 파동을 남길 확률.")]
    private float _groundRippleChance = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("빗방울 파동 하나가 퍼지는 무작위 최대 반경 범위(미터). x는 최소, y는 최대다.")]
    private Vector2 _groundRippleRadius = new Vector2(0.15f, 0.5f);
    [SerializeField, Min(0.01f), Tooltip("링 하나가 최대 반경까지 퍼지며 사라지는 비 시간(초). 불릿 타임에는 비와 함께 느려진다.")]
    private float _groundRippleDuration = 0.35f;
    [SerializeField, Min(0f), Tooltip("세 겹 동심원 중 다음 링이 출발하는 간격(초).")]
    private float _groundRippleRingInterval = 0.1f;
    [SerializeField, Tooltip("빗방울 파동의 바닥 링 색상. 알파는 링의 진하기다.")]
    private Color _groundRippleColor = new Color(1f, 1f, 1f, 0.2f);
    [SerializeField, Range(256, 4096), Tooltip("동시에 남아 있을 수 있는 빗방울 파동 수. 넘으면 가장 오래된 파동부터 덮어쓴다.")]
    private int _maximumGroundRipples = 4096;
    [SerializeField, Min(0.1f), Tooltip("땅 파동이 실루엣을 밝히는 구 반경의 바닥 링 반경 대비 배율.")]
    private float _groundRippleRevealRadiusScale = 1.5f;
    [SerializeField, Min(0.01f), Tooltip("땅 파동 구의 전선이 지나간 실루엣이 흐려지는 잔상 시간(초, 비 시간).")]
    private float _groundRippleRevealAfterimage = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("땅 파동 하나가 실루엣을 밝히는 세기. 겹친 파동은 세기를 더한다.")]
    private float _groundRippleRevealStrength = 0.018f;

    [Header("대숲 빗방울 잔상")]
    [SerializeField, Min(0f), Tooltip("비 영역 안 서 있는 대나무에 빗방울이 맞아 잔상을 남기는 초당 횟수(비 시간 기준).")]
    private float _bambooRainHitsPerSecond = 24f;
    [SerializeField, Min(0.01f), Tooltip("빗방울 잔상 구가 퍼지는 무작위 최대 반경 범위(미터). x는 최소, y는 최대다.")]
    private Vector2 _bambooRainHitRadius = new Vector2(0.6f, 1.2f);
    [SerializeField, Min(0.01f), Tooltip("빗방울 잔상 구가 최대 반경까지 퍼지는 시간(초, 비 시간).")]
    private float _bambooRainHitExpandDuration = 0.25f;
    [SerializeField, Min(0.01f), Tooltip("빗방울 잔상 구의 전선이 지나간 자리가 흐려지는 잔상 시간(초, 비 시간).")]
    private float _bambooRainHitAfterimage = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("빗방울 잔상 하나가 실루엣을 밝히는 세기. 겹친 잔상은 세기를 더한다.")]
    private float _bambooRainHitStrength = 0.35f;

    private struct Sweep
    {
        public Vector4 A;
        public Vector4 B;
        public Vector4 C;
        public Vector4 D;
    }

    private struct Trail
    {
        public Sweep Shape;
        public float ExpiresAt;
    }

    /// <summary>
    /// 빗방울 땅 파동과 대나무 빗방울 잔상을 카메라 깊이로 구 판정해 화면 공개 텍스처에 더한다.
    /// 윤곽선 패스보다 먼저 실행되며, 윤곽선 셰이더는 _OutlineRainReveal 전역 텍스처로 이 결과를 읽는다.
    /// </summary>
    private sealed class RevealPass : ScriptableRenderPass
    {
        private readonly RainStageWeather _owner;
        private readonly int _passIndex;
        private RTHandle _target;

        private sealed class PassData
        {
            public TextureHandle Depth { get; set; }
            public Mesh Quad { get; set; }
            public Material Material { get; set; }
            public int PassIndex { get; set; }
            public MaterialPropertyBlock GroundProperties { get; set; }
            public int GroundCount { get; set; }
            public MaterialPropertyBlock BambooProperties { get; set; }
        }

        /// <summary>
        /// owner의 비 머티리얼에서 RainReveal 패스를 찾고 윤곽선 패스 바로 앞 시점에 실행되도록 만든다.
        /// </summary>
        public RevealPass(RainStageWeather owner)
        {
            _owner = owner;
            _passIndex = owner._rainMaterial.FindPass("RainReveal");
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingTransparents - 1);
        }

        /// <summary>
        /// 카메라 크기의 반정밀도 공개 텍스처를 준비해 전역으로 알리고, 카메라 깊이를 읽어 두 파동 버퍼를 그리는 패스를 기록한다.
        /// 카메라 깊이가 없으면 공개 텍스처 사용을 끄고 기록하지 않는다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle depth = resources.cameraDepthTexture;
            if (_passIndex < 0 || !depth.IsValid())
            {
                Shader.SetGlobalFloat(_rainRevealEnabledId, 0f);
                return;
            }
            RenderTextureDescriptor descriptor = frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
            descriptor.colorFormat = RenderTextureFormat.RHalf;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _target, descriptor, FilterMode.Point,
                TextureWrapMode.Clamp, name: "RainyStage Rain Reveal");
            Shader.SetGlobalTexture(_rainRevealId, _target.rt);
            Shader.SetGlobalFloat(_rainRevealEnabledId, 1f);
            TextureHandle target = graph.ImportTexture(_target);

            using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass("RainyStage Rain Reveal", out PassData data);
            data.Depth = depth;
            data.Quad = _owner._quad;
            data.Material = _owner._rainMaterial;
            data.PassIndex = _passIndex;
            data.GroundProperties = _owner._groundRevealProperties;
            data.GroundCount = _owner._maximumGroundRipples;
            data.BambooProperties = _owner._bambooRevealProperties;
            builder.UseTexture(depth, AccessFlags.Read);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PassData>(Draw);
        }

        /// <summary>
        /// 공개 텍스처를 비우고 data의 땅 파동과 대나무 잔상을 카메라를 향한 사각형으로 인스턴싱해 더한다.
        /// </summary>
        private static void Draw(PassData data, RasterGraphContext context)
        {
            context.cmd.ClearRenderTarget(false, true, Color.clear);
            data.GroundProperties.SetTexture(_sceneDepthId, data.Depth);
            data.BambooProperties.SetTexture(_sceneDepthId, data.Depth);
            context.cmd.DrawMeshInstancedProcedural(data.Quad, 0, data.Material, data.PassIndex,
                data.GroundCount, data.GroundProperties);
            context.cmd.DrawMeshInstancedProcedural(data.Quad, 0, data.Material, data.PassIndex,
                MAX_BAMBOO_RAIN_HITS, data.BambooProperties);
        }

        /// <summary>공개 텍스처를 해제하고 참조를 비운다.</summary>
        public void Dispose()
        {
            _target?.Release();
            _target = null;
        }
    }

    private PlayerController _player;
    private PlayerEmpower _empower;
    private CharacterController _playerBody;
    private Camera _viewCamera;
    private ComputeBuffer _drops;
    private ComputeBuffer _hitBuffer;
    private ComputeBuffer _trailBuffer;
    private ComputeBuffer _groundRipples;
    private ComputeBuffer _groundRippleCounter;
    private ComputeBuffer _bambooHits;
    private MaterialPropertyBlock _properties;
    private MaterialPropertyBlock _rippleProperties;
    private MaterialPropertyBlock _groundRevealProperties;
    private MaterialPropertyBlock _bambooRevealProperties;
    private RevealPass _revealPass;
    private Vector4[] _bambooHitData;
    private int _nextBambooHit;
    private Mesh _quad;
    private Sweep[] _hits;
    private Sweep[] _liveTrails;
    private Trail[] _trails;
    private int _hitCount;
    private int _nextTrail;
    private int _updateKernel;
    private int _frame;
    private float _roundness;
    private float _wiggleTime;
    private float _rainClock;
    private float _groundY;
    private float _bambooRainHitBudget;
    private int _totalDropletCount;
    private bool _initialized;

    void OnEnable()
    {
        _active = this;
        RenderPipelineManager.beginCameraRendering += EnqueueRevealPass;
        RenderPipelineManager.endCameraRendering += DisableRevealAfterCamera;
    }

    void Start()
    {
        _empower = FindFirstObjectByType<PlayerEmpower>();
        if (_empower == null)
        {
            Debug.LogError("RainyStage 비 연출에서 PlayerEmpower를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }
        _player = _empower.GetComponent<PlayerController>();
        _playerBody = _empower.GetComponent<CharacterController>();
        _viewCamera = _empower.GetComponentInChildren<Camera>();
        if (_playerBody == null || _viewCamera == null || _simulation == null || _rainMaterial == null
            || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing)
        {
            Debug.LogError("RainyStage 비 연출의 플레이어 몸체, 카메라, GPU 에셋 또는 GPU 기능이 없습니다.", this);
            enabled = false;
            return;
        }
        InitializeGpu();
    }

    void LateUpdate()
    {
        if (!_initialized) return;

        bool paused = _player.IsPaused || Time.timeScale <= 0f;
        if (!paused)
        {
            _wiggleTime += Time.unscaledDeltaTime;
            bool bulletTime = _empower.IsBulletTimeActive;
            _roundness = Mathf.MoveTowards(_roundness, bulletTime ? 1f : 0f,
                Time.unscaledDeltaTime / _shapeTransitionTime);
            float rainDelta = bulletTime ? Time.unscaledDeltaTime * _bulletRainSpeed : Time.deltaTime;
            float splashDelta = bulletTime ? Time.unscaledDeltaTime * _bulletSplashSpeed : Time.deltaTime;
            _rainClock += rainDelta;
            // 공중에 있는 동안에는 마지막으로 서 있던 지면 높이를 유지한다.
            if (_playerBody.isGrounded) _groundY = _playerBody.bounds.min.y;
            UploadSweeps();
            Vector3 center = _viewCamera.transform.position;
            _simulation.SetVector("_Center", center);
            _simulation.SetFloat("_Radius", _nearRadius);
            _simulation.SetFloat("_FarRadius", _farRadius);
            _simulation.SetFloat("_Top", center.y + _heightAboveCamera);
            _simulation.SetFloat("_Bottom", center.y - _depthBelowCamera);
            _simulation.SetFloat("_FallSpeed", _fallSpeed);
            _simulation.SetFloat("_RainDeltaTime", rainDelta);
            _simulation.SetFloat("_SplashDeltaTime", splashDelta);
            _simulation.SetFloat("_SplashDuration", _splashDuration);
            _simulation.SetFloat("_GroundY", _groundY);
            _simulation.SetFloat("_GroundRippleChance", _groundRippleChance);
            _simulation.SetFloat("_GroundRippleRadiusMin", _groundRippleRadius.x);
            _simulation.SetFloat("_GroundRippleRadiusMax", _groundRippleRadius.y);
            _simulation.SetFloat("_RainClock", _rainClock);
            _simulation.SetInt("_Frame", ++_frame);
            _simulation.Dispatch(_updateKernel, Mathf.CeilToInt(_totalDropletCount / (float)THREAD_GROUP_SIZE), 1, 1);
            _hitCount = 0;
            EmitBambooRainHits(center, rainDelta);
        }
        else
        {
            _hitCount = 0;
        }

        Vector3 cameraRight = _viewCamera.transform.right;
        Vector3 cameraUp = _viewCamera.transform.up;
        _properties.SetVector("_CameraRight", cameraRight);
        _properties.SetVector("_CameraUp", cameraUp);
        _properties.SetFloat("_Roundness", _roundness);
        _properties.SetFloat("_DropSize", _dropletSize);
        _properties.SetFloat("_BulletDropletScale", _bulletDropletScale);
        _properties.SetFloat("_WiggleAmplitude", _bulletWiggleAmplitude);
        _properties.SetFloat("_WiggleTime", _wiggleTime);
        _properties.SetFloat("_StreakLength", _streakLength);
        _properties.SetFloat("_SplashSize", _splashSize);
        _properties.SetFloat("_SplashDuration", _splashDuration);
        _properties.SetColor("_OutlineColor", _dropletOutlineColor);
        _properties.SetFloat("_OutlineWidth", _dropletOutlineWidth);
        _properties.SetFloat("_OutlineOpacity", _dropletOutlineOpacity);
        _properties.SetFloat("_IdleDropVisibility", _idleDropletVisibility);
        _properties.SetColor("_SplashColor", _splashColor);
        RenderParams render = new RenderParams(_rainMaterial)
        {
            camera = _viewCamera,
            matProps = _properties,
            worldBounds = new Bounds(_viewCamera.transform.position,
                new Vector3(_farRadius * 2f + 4f, _heightAboveCamera + _depthBelowCamera + 4f,
                    _farRadius * 2f + 4f))
        };
        Graphics.RenderMeshPrimitives(render, _quad, 0, _totalDropletCount);

        _rippleProperties.SetFloat("_RainClock", _rainClock);
        _rippleProperties.SetFloat("_GroundRippleDuration", _groundRippleDuration);
        _rippleProperties.SetFloat("_GroundRippleRingInterval", _groundRippleRingInterval);
        _rippleProperties.SetColor("_GroundRippleColor", _groundRippleColor);
        render.matProps = _rippleProperties;
        Graphics.RenderMeshPrimitives(render, _quad, 0, _maximumGroundRipples);

        _groundRevealProperties.SetFloat("_RainClock", _rainClock);
        _groundRevealProperties.SetFloat("_RevealRadiusScale", _groundRippleRevealRadiusScale);
        _groundRevealProperties.SetFloat("_RevealExpandDuration", _groundRippleDuration);
        _groundRevealProperties.SetFloat("_RevealAfterimage", _groundRippleRevealAfterimage);
        _groundRevealProperties.SetFloat("_RevealStrength", _groundRippleRevealStrength);
        _bambooRevealProperties.SetFloat("_RainClock", _rainClock);
        _bambooRevealProperties.SetFloat("_RevealRadiusScale", 1f);
        _bambooRevealProperties.SetFloat("_RevealExpandDuration", _bambooRainHitExpandDuration);
        _bambooRevealProperties.SetFloat("_RevealAfterimage", _bambooRainHitAfterimage);
        _bambooRevealProperties.SetFloat("_RevealStrength", _bambooRainHitStrength);
    }

    void OnDisable()
    {
        if (_active == this) _active = null;
        RenderPipelineManager.beginCameraRendering -= EnqueueRevealPass;
        RenderPipelineManager.endCameraRendering -= DisableRevealAfterCamera;
        Shader.SetGlobalFloat(_rainRevealEnabledId, 0f);
        _revealPass?.Dispose();
        _revealPass = null;
        _drops?.Release();
        _hitBuffer?.Release();
        _trailBuffer?.Release();
        _groundRipples?.Release();
        _groundRippleCounter?.Release();
        _bambooHits?.Release();
        _drops = null;
        _hitBuffer = null;
        _trailBuffer = null;
        _groundRipples = null;
        _groundRippleCounter = null;
        _bambooHits = null;
        if (_quad != null) Destroy(_quad);
        _quad = null;
        _initialized = false;
    }

    /// <summary>
    /// 이전 칼날과 현재 칼날의 양 끝으로 베기 면을 기록한다.
    /// RainyStage에 비가 활성화된 경우에만 thickness 범위의 물방울과 빈 자국을 갱신한다.
    /// </summary>
    public static void RecordBladeSweep(Vector3 previousBase, Vector3 previousTip,
        Vector3 currentBase, Vector3 currentTip)
    {
        if (_active == null || !_active._initialized) return;
        _active.AddSweep(previousBase, previousTip, currentBase, currentTip, _active._bladeThickness);
    }

    /// <summary>
    /// 이동하는 검기의 이전·현재 위치와 가로축, 반폭으로 쓸린 면을 기록한다.
    /// RainyStage에서만 thickness 범위의 물방울과 빈 자국을 갱신한다.
    /// </summary>
    public static void RecordWaveSweep(Vector3 previous, Vector3 current, Vector3 right, float halfWidth)
    {
        if (_active == null || !_active._initialized) return;
        Vector3 offset = right.normalized * halfWidth;
        _active.AddSweep(previous - offset, previous + offset, current - offset,
            current + offset, _active._waveThickness);
    }

    /// <summary>
    /// Inspector의 방울 수와 영역·땅 파동 한도로 GPU 버퍼와 사각형 메시를 만들고 초기 방울을 채운다.
    /// 땅 파동 버퍼는 모두 시작 전 시각으로 비우며, 생성된 버퍼와 메시는 비활성화 때 해제한다.
    /// </summary>
    private void InitializeGpu()
    {
        _dropletCount = Mathf.Clamp(_dropletCount, 256, 8192);
        _farDropletCount = Mathf.Clamp(_farDropletCount, 0, 8192);
        _farRadius = Mathf.Max(_farRadius, _nearRadius);
        _totalDropletCount = _dropletCount + _farDropletCount;
        _maximumTrails = Mathf.Clamp(_maximumTrails, 1, 64);
        _maximumHitsPerFrame = Mathf.Clamp(_maximumHitsPerFrame, 1, 64);
        _maximumGroundRipples = Mathf.Clamp(_maximumGroundRipples, 256, 4096);
        _hits = new Sweep[_maximumHitsPerFrame];
        _trails = new Trail[_maximumTrails];
        _liveTrails = new Sweep[_maximumTrails];
        _drops = new ComputeBuffer(_totalDropletCount, DROP_STRIDE);
        _hitBuffer = new ComputeBuffer(_maximumHitsPerFrame, SWEEP_STRIDE);
        _trailBuffer = new ComputeBuffer(_maximumTrails, SWEEP_STRIDE);
        _groundRipples = new ComputeBuffer(_maximumGroundRipples, GROUND_RIPPLE_STRIDE);
        _groundRippleCounter = new ComputeBuffer(1, sizeof(uint));
        Vector4[] emptyRipples = new Vector4[_maximumGroundRipples * 2];
        for (int i = 0; i < emptyRipples.Length; i += 2)
            emptyRipples[i] = new Vector4(0f, 0f, 0f, -100000f);
        _groundRipples.SetData(emptyRipples);
        _groundRippleCounter.SetData(new uint[1]);
        _groundY = _playerBody.bounds.min.y;
        _properties = new MaterialPropertyBlock();
        _properties.SetBuffer("_RainDrops", _drops);
        _properties.SetBuffer("_GroundRipples", _groundRipples);
        _properties.SetFloat("_RippleMode", 0f);
        _rippleProperties = new MaterialPropertyBlock();
        _rippleProperties.SetBuffer("_RainDrops", _drops);
        _rippleProperties.SetBuffer("_GroundRipples", _groundRipples);
        _rippleProperties.SetFloat("_RippleMode", 1f);
        _bambooHits = new ComputeBuffer(MAX_BAMBOO_RAIN_HITS, GROUND_RIPPLE_STRIDE);
        _bambooHitData = new Vector4[MAX_BAMBOO_RAIN_HITS * 2];
        for (int i = 0; i < _bambooHitData.Length; i += 2)
            _bambooHitData[i] = new Vector4(0f, 0f, 0f, -100000f);
        _bambooHits.SetData(_bambooHitData);
        _groundRevealProperties = new MaterialPropertyBlock();
        _groundRevealProperties.SetBuffer("_RevealRipples", _groundRipples);
        _bambooRevealProperties = new MaterialPropertyBlock();
        _bambooRevealProperties.SetBuffer("_RevealRipples", _bambooHits);
        _revealPass = new RevealPass(this);
        _quad = new Mesh { name = "RainyStage GPU Rain Quad" };
        _quad.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f)
        };
        _quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        int initKernel = _simulation.FindKernel("Initialize");
        _updateKernel = _simulation.FindKernel("UpdateRain");
        _simulation.SetBuffer(initKernel, "_Drops", _drops);
        _simulation.SetBuffer(_updateKernel, "_Drops", _drops);
        _simulation.SetBuffer(_updateKernel, "_HitSweeps", _hitBuffer);
        _simulation.SetBuffer(_updateKernel, "_TrailSweeps", _trailBuffer);
        _simulation.SetBuffer(_updateKernel, "_GroundRipples", _groundRipples);
        _simulation.SetBuffer(_updateKernel, "_GroundRippleCounter", _groundRippleCounter);
        Vector3 center = _viewCamera.transform.position;
        _simulation.SetInt("_DropCount", _totalDropletCount);
        _simulation.SetInt("_NearDropCount", _dropletCount);
        _simulation.SetInt("_GroundRippleCapacity", _maximumGroundRipples);
        _simulation.SetVector("_Center", center);
        _simulation.SetFloat("_Radius", _nearRadius);
        _simulation.SetFloat("_FarRadius", _farRadius);
        _simulation.SetFloat("_Top", center.y + _heightAboveCamera);
        _simulation.SetFloat("_Bottom", center.y - _depthBelowCamera);
        _simulation.Dispatch(initKernel, Mathf.CeilToInt(_totalDropletCount / (float)THREAD_GROUP_SIZE), 1, 1);
        _initialized = true;
    }

    /// <summary>
    /// rainDelta와 초당 횟수로 이번 프레임 빗방울 수를 누적하고, 그만큼 center 둘레 바깥 비 반경 안의 서 있는 대나무 위치를 골라 잔상 링 버퍼에 쓴다.
    /// 잔상 높이는 비가 시작되는 높이 아래로 제한하고 시작 시각은 _rainClock이며, 남은 소수 횟수는 _bambooRainHitBudget에 넘긴다.
    /// </summary>
    private void EmitBambooRainHits(Vector3 center, float rainDelta)
    {
        _bambooRainHitBudget += rainDelta * _bambooRainHitsPerSecond;
        // 긴 프레임에서 밀린 횟수는 한 번에 몰아 내지 않도록 프레임당 수를 제한한다.
        int count = Mathf.Min((int)_bambooRainHitBudget, MAX_BAMBOO_RAIN_HITS_PER_FRAME);
        _bambooRainHitBudget = Mathf.Min(_bambooRainHitBudget - count, 1f);
        float top = center.y + _heightAboveCamera;
        for (int i = 0; i < count; i++)
        {
            if (!BambooGrove.TryGetRainHitPoint(center, _farRadius, top, out Vector3 point))
                continue;
            int index = _nextBambooHit * 2;
            _bambooHitData[index] = new Vector4(point.x, point.y, point.z, _rainClock);
            _bambooHitData[index + 1] = new Vector4(
                Random.Range(_bambooRainHitRadius.x, _bambooRainHitRadius.y), 0f, 0f, 0f);
            _bambooHits.SetData(_bambooHitData, index, index, 2);
            _nextBambooHit = (_nextBambooHit + 1) % MAX_BAMBOO_RAIN_HITS;
        }
    }

    /// <summary>
    /// 플레이어 시야 camera가 그려지기 시작할 때 빗방울 파동 화면 공개 패스를 그 카메라 렌더러에 넣는다.
    /// 초기화 전이거나 다른 카메라이면 넣지 않는다.
    /// </summary>
    private void EnqueueRevealPass(ScriptableRenderContext context, Camera camera)
    {
        if (!_initialized || camera != _viewCamera) return;
        camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_revealPass);
    }

    /// <summary>
    /// 플레이어 시야 camera 렌더링이 끝나면 화면 공개 텍스처 사용을 꺼서 크기가 다른 Scene View 등이 읽지 않게 한다.
    /// </summary>
    private void DisableRevealAfterCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == _viewCamera) Shader.SetGlobalFloat(_rainRevealEnabledId, 0f);
    }

    /// <summary>
    /// 네 모서리와 두께로 공격 영역을 만들고 이번 프레임의 파열 판정과 만료되는 빈 자국에 추가한다.
    /// 한도를 넘으면 새 파열 판정은 버리고 오래된 빈 자국부터 덮어쓴다.
    /// </summary>
    private void AddSweep(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float thickness)
    {
        Sweep sweep = new Sweep
        {
            A = new Vector4(a.x, a.y, a.z, thickness),
            B = new Vector4(b.x, b.y, b.z, 0f),
            C = new Vector4(c.x, c.y, c.z, 0f),
            D = new Vector4(d.x, d.y, d.z, 0f)
        };
        if (_hitCount < _hits.Length) _hits[_hitCount++] = sweep;
        _trails[_nextTrail] = new Trail { Shape = sweep, ExpiresAt = Time.time + _trailLifetime };
        _nextTrail = (_nextTrail + 1) % _trails.Length;
    }

    /// <summary>
    /// 이번 프레임 공격 면과 game time상 만료되지 않은 빈 자국을 GPU에 올린다.
    /// GPU에는 각각의 실제 개수만 전달한다.
    /// </summary>
    private void UploadSweeps()
    {
        int trailCount = 0;
        for (int i = 0; i < _trails.Length; i++)
        {
            if (_trails[i].ExpiresAt <= Time.time) continue;
            _liveTrails[trailCount++] = _trails[i].Shape;
        }
        if (_hitCount > 0) _hitBuffer.SetData(_hits, 0, 0, _hitCount);
        if (trailCount > 0) _trailBuffer.SetData(_liveTrails, 0, 0, trailCount);
        _simulation.SetInt("_HitCount", _hitCount);
        _simulation.SetInt("_TrailCount", trailCount);
    }
}
