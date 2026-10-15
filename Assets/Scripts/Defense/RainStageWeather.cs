using UnityEngine;

/// <summary>
/// RainyStage의 빗방울을 GPU에서 갱신하고 검격이 지나간 공간과 파열을 관리한다.
/// 씬이 끝나면 GPU 자원을 해제하며 다른 씬에는 영향을 주지 않는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class RainStageWeather : MonoBehaviour
{
    private const int THREAD_GROUP_SIZE = 64;
    private const int DROP_STRIDE = 32;
    private const int SWEEP_STRIDE = 64;

    private static RainStageWeather _active;

    [Header("GPU 에셋")]
    [SerializeField] private ComputeShader _simulation;
    [SerializeField] private Material _rainMaterial;

    [Header("비")]
    [SerializeField, Range(256, 8192)] private int _dropletCount = 2048;
    [SerializeField, Range(3f, 30f)] private float _nearRadius = 15f;
    [SerializeField, Min(1f)] private float _heightAboveCamera = 7f;
    [SerializeField, Min(0f)] private float _depthBelowCamera = 2f;
    [SerializeField, Min(0.1f)] private float _fallSpeed = 11f;
    [SerializeField, Min(0.005f)] private float _dropletSize = 0.055f;
    [SerializeField, Min(0.01f)] private float _streakLength = 0.24f;
    [SerializeField] private Color _dropletOutlineColor = new Color(0.82f, 0.87f, 0.93f, 1f);
    [SerializeField, Range(0.01f, 0.2f)] private float _dropletOutlineWidth = 0.055f;
    [SerializeField, Range(0f, 1f)] private float _dropletOutlineOpacity = 0.58f;

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

    private PlayerController _player;
    private PlayerEmpower _empower;
    private Camera _viewCamera;
    private ComputeBuffer _drops;
    private ComputeBuffer _hitBuffer;
    private ComputeBuffer _trailBuffer;
    private MaterialPropertyBlock _properties;
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
    private bool _initialized;

    void OnEnable()
    {
        _active = this;
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
        _viewCamera = _empower.GetComponentInChildren<Camera>();
        if (_viewCamera == null || _simulation == null || _rainMaterial == null
            || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing)
        {
            Debug.LogError("RainyStage 비 연출의 카메라, GPU 에셋 또는 GPU 기능이 없습니다.", this);
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
            UploadSweeps();
            Vector3 center = _viewCamera.transform.position;
            _simulation.SetVector("_Center", center);
            _simulation.SetFloat("_Radius", _nearRadius);
            _simulation.SetFloat("_Top", center.y + _heightAboveCamera);
            _simulation.SetFloat("_Bottom", center.y - _depthBelowCamera);
            _simulation.SetFloat("_FallSpeed", _fallSpeed);
            _simulation.SetFloat("_RainDeltaTime", rainDelta);
            _simulation.SetFloat("_SplashDeltaTime", splashDelta);
            _simulation.SetFloat("_SplashDuration", _splashDuration);
            _simulation.SetInt("_Frame", ++_frame);
            _simulation.Dispatch(_updateKernel, Mathf.CeilToInt(_dropletCount / (float)THREAD_GROUP_SIZE), 1, 1);
            _hitCount = 0;
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
        _properties.SetColor("_SplashColor", _splashColor);
        RenderParams render = new RenderParams(_rainMaterial)
        {
            camera = _viewCamera,
            matProps = _properties,
            worldBounds = new Bounds(_viewCamera.transform.position,
                new Vector3(_nearRadius * 2f + 4f, _heightAboveCamera + _depthBelowCamera + 4f,
                    _nearRadius * 2f + 4f))
        };
        Graphics.RenderMeshPrimitives(render, _quad, 0, _dropletCount);
    }

    void OnDisable()
    {
        if (_active == this) _active = null;
        _drops?.Release();
        _hitBuffer?.Release();
        _trailBuffer?.Release();
        _drops = null;
        _hitBuffer = null;
        _trailBuffer = null;
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
    /// Inspector의 방울 수와 영역 한도로 GPU 버퍼와 사각형 메시를 만들고 초기 방울을 채운다.
    /// 생성된 버퍼와 메시는 비활성화 때 해제한다.
    /// </summary>
    private void InitializeGpu()
    {
        _dropletCount = Mathf.Clamp(_dropletCount, 256, 8192);
        _maximumTrails = Mathf.Clamp(_maximumTrails, 1, 64);
        _maximumHitsPerFrame = Mathf.Clamp(_maximumHitsPerFrame, 1, 64);
        _hits = new Sweep[_maximumHitsPerFrame];
        _trails = new Trail[_maximumTrails];
        _liveTrails = new Sweep[_maximumTrails];
        _drops = new ComputeBuffer(_dropletCount, DROP_STRIDE);
        _hitBuffer = new ComputeBuffer(_maximumHitsPerFrame, SWEEP_STRIDE);
        _trailBuffer = new ComputeBuffer(_maximumTrails, SWEEP_STRIDE);
        _properties = new MaterialPropertyBlock();
        _properties.SetBuffer("_RainDrops", _drops);
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
        Vector3 center = _viewCamera.transform.position;
        _simulation.SetInt("_DropCount", _dropletCount);
        _simulation.SetVector("_Center", center);
        _simulation.SetFloat("_Radius", _nearRadius);
        _simulation.SetFloat("_Top", center.y + _heightAboveCamera);
        _simulation.SetFloat("_Bottom", center.y - _depthBelowCamera);
        _simulation.Dispatch(initKernel, Mathf.CeilToInt(_dropletCount / (float)THREAD_GROUP_SIZE), 1, 1);
        _initialized = true;
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
