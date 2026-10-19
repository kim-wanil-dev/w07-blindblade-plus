using UnityEngine;

/// <summary>
/// 쌍둥이 보스 늑대 두 눈에서 일반 잡몹 연기(EnemySmokeVisual)의 트레일만 붉게 길게 그린다.
/// 눈마다 잡몹 연기를 하나 복제해 연기 입자는 보이지 않을 만큼 작게 두고, 입자가 눈에서 머리 뒤로 흘러가며 남기는 월드 공간 트레일을
/// 연기와 같은 재질로 그린다. 트레일은 연기 렌더러에 속하므로 연기처럼 파동에 드러날 때만 보인다.
/// Awake에서 늑대 몸체 밖(이 오브젝트 아래)에 복제해 털 연기 조정(TwinBeastSmokeTuning)이 건드리지 않게 하고, Start에서 늑대 머리(_head) 자식으로 옮기며
/// 짐승 TwinBossWarningVisual에 렌더러를 더해 윤곽선 색이 몸통과 함께 바뀌게 한다. 잠수 중에는 방출을 멈춘다.
/// 덮치며 플레이어가 가까워질 때(BeastBossAttack.PounceGlint) 두 눈에서 파동과 상관없이 보이는 하늘색 빛과 몸통에서 분리된 윤곽선이 번쩍인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBeastEyes : MonoBehaviour
{
    private const string BODY_SMOKE_NAME = "BodySmoke";
    private const string TRAILING_WISPS_NAME = "TrailingWisps";
    private const float HIDDEN_PARTICLE_SIZE = 0.001f;
    private const int GLINT_TEXTURE_SIZE = 1024;
    private const float GLINT_SORTING_FUDGE = 100f;
    private const float GLINT_MAX_SCREEN_SIZE = 4f;
    private const float GLINT_CURVE_POWER = 32f;
    private const float GLINT_CENTER_WIDTH_SCALE = 0.1f;
    private const float GLINT_ARM_HALF_HEIGHT = 0.035f;

    [Header("연결")]
    [SerializeField] private BeastBossAttack _beast;
    [SerializeField, Tooltip("눈 트레일을 붙일 늑대 머리 관절")]
    private Transform _head;
    [SerializeField, Tooltip("일반 잡몹이 쓰는 공용 EnemySmokeVisual 프리팹. 눈마다 하나씩 복제한다.")]
    private GameObject _smokePrefab;
    [SerializeField, Tooltip("머리 로컬 좌표의 눈 위치(미터). 두개골 앞쪽 위 좌우다.")]
    private Vector3[] _eyeOffsets = { new Vector3(-0.17f, 0.52f, 0.76f), new Vector3(0.17f, 0.52f, 0.76f) };
    private Transform[] _eyes;
    private ParticleSystem[] _smokes;

    [Header("트레일")]
    [SerializeField, Tooltip("잡몹 연기 색에 곱하는 트레일 색")]
    private Color _color = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Min(0f), Tooltip("눈 하나에서 초당 나오는 트레일 가닥 수")]
    private float _rate = 8f;
    [SerializeField, Min(0.05f), Tooltip("가닥이 흘러가는 시간 최소값(초)")]
    private float _minLifetime = 1f;
    [SerializeField, Min(0.05f), Tooltip("가닥이 흘러가는 시간 최대값(초)")]
    private float _maxLifetime = 1.4f;
    [SerializeField, Min(0f), Tooltip("가닥이 머리 뒤쪽으로 수평하게 흘러가는 속도(미터/초)")]
    private float _trailSpeed = 0.8f;
    [SerializeField, Min(0f), Tooltip("눈마다 바깥 옆으로 벌어지는 속도(미터/초)")]
    private float _spreadSpeed = 0.2f;
    [SerializeField, Min(0f), Tooltip("위로 조금만 떠오르는 속도(미터/초)")]
    private float _riseSpeed = 0.05f;
    [SerializeField, Min(0f), Tooltip("트레일 머리 쪽 굵기(미터). 꼬리로 갈수록 가늘어진다. 0이면 평소 눈 트레일을 그리지 않고 덮치기 전 번쩍임만 남는다.")]
    private float _trailWidth = 0f;
    [SerializeField, Range(0.05f, 1f), Tooltip("가닥 수명 대비 트레일이 남는 시간 비율의 최대값. 빨리 움직이면 최대 길이에 맞춰 더 줄어든다.")]
    private float _trailLifetime = 1f;
    [SerializeField, Min(0.05f), Tooltip("트레일 한 가닥의 최대 길이(미터). 머리가 빨리 움직여도 이보다 길어지지 않는다.")]
    private float _maxTrailLength = 1.5f;
    [SerializeField, Min(0.001f), Tooltip("트레일 꼭짓점 사이 최소 거리(미터)")]
    private float _minVertexDistance = 0.03f;
    [SerializeField, Min(0f), Tooltip("잡몹 연기 흔들림 세기에 곱하는 배율")]
    private float _flutterScale = 0.3f;
    private Vector3 _previousHeadPosition;

    [Header("돌진 전 번쩍임")]
    [SerializeField, Tooltip("번쩍임 색")]
    private Color _glintColor = new Color(0.5691823f, 0.8328799f, 1f, 1f);
    [SerializeField, Min(0.01f), Tooltip("번쩍임이 가장 커졌을 때 기본 크기(미터)")]
    private float _glintSize = 1.8f;
    [SerializeField, Min(0.1f), Tooltip("번쩍임 좌우 길이를 기본 크기의 몇 배로 늘일지 정한다")]
    private float _glintWidthScale = 3f;
    [SerializeField, Min(0.1f), Tooltip("번쩍임 세로 길이를 기본 크기의 몇 배로 늘일지 정한다")]
    private float _glintHeightScale = 1.5f;
    [SerializeField, Tooltip("눈 좌표 기준 번쩍임 중심의 머리 로컬 높이 오프셋(미터). 음수는 아래다.")]
    private float _glintVerticalOffset = -1f;
    [SerializeField, Min(0.01f), Tooltip("번쩍임이 커졌다 사라지는 시간(초)")]
    private float _glintDuration = 0.4f;
    private ParticleSystem[] _glints;

    void Awake()
    {
        _eyes = new Transform[_eyeOffsets.Length];
        _smokes = new ParticleSystem[_eyeOffsets.Length];
        for (int i = 0; i < _eyeOffsets.Length; i++)
        {
            GameObject eye = Instantiate(_smokePrefab, transform);
            eye.name = "WolfEye_" + i;
            // 눈에는 한 갈래 트레일만 쓰므로 꼬리 연기는 끈다.
            eye.transform.Find(TRAILING_WISPS_NAME).gameObject.SetActive(false);
            _eyes[i] = eye.transform;
            _smokes[i] = eye.transform.Find(BODY_SMOKE_NAME).GetComponent<ParticleSystem>();
            ConfigureTrail(_smokes[i], Mathf.Sign(_eyeOffsets[i].x));
        }
    }

    void Start()
    {
        // 털 연기 조정·짐승 경고 표시가 Awake에서 털 연기만 모은 뒤 머리로 옮기고 경고 표시에 눈 렌더러를 더한다.
        for (int i = 0; i < _eyes.Length; i++)
        {
            _eyes[i].SetParent(_head, false);
            _eyes[i].localPosition = _eyeOffsets[i];
            _eyes[i].localRotation = Quaternion.identity;
            _beast.GetComponent<TwinBossWarningVisual>().AddRenderers(_eyes[i]);
        }
        _previousHeadPosition = _head.position;
        CreateGlints();
        _beast.PounceGlint += HandlePounceGlint;
    }

    void OnDestroy()
    {
        _beast.PounceGlint -= HandlePounceGlint;
    }

    void LateUpdate()
    {
        bool hidden = _beast.IsBurrowed;
        float lifetime = GetTrailLifetime(Time.deltaTime);
        foreach (ParticleSystem smoke in _smokes)
        {
            if (hidden && smoke.isEmitting) smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            else if (!hidden && !smoke.isEmitting) smoke.Play(false);
            ParticleSystem.TrailModule trails = smoke.trails;
            trails.lifetimeMultiplier = lifetime;
        }
    }

    /// <summary>
    /// 눈마다 머리(_head) 아래 눈 좌표에서 _glintVerticalOffset만큼 옮긴 위치에 한 번만 별빛 파티클을 만든다.
    /// 눈 연기 재질·_glintColor·_glintSize(가로는 _glintWidthScale배, 세로는 _glintHeightScale배)로
    /// _glintDuration 동안 커졌다 사라지며, 짐승 TwinBossWarningVisual의 별도 하늘색 윤곽선 대상으로 더해 몸통의 붉은 테두리와 겹쳐도 분리해 보인다.
    /// 만든 파티클을 _glints에 저장한다.
    /// </summary>
    private void CreateGlints()
    {
        _glints = new ParticleSystem[_eyeOffsets.Length];
        Texture2D glintShape = CreateGlintShape();
        for (int i = 0; i < _eyeOffsets.Length; i++)
        {
            GameObject glint = new GameObject("WolfEyeGlint_" + i);
            glint.SetActive(false);
            glint.transform.SetParent(_head, false);
            glint.transform.localPosition = _eyeOffsets[i] + Vector3.up * _glintVerticalOffset;
            ParticleSystem particles = glint.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = _glintDuration;
            main.startLifetime = _glintDuration;
            main.startSpeed = 0f;
            // 가로와 세로 배율을 각각 적용한다.
            main.startSize3D = true;
            main.startSizeX = _glintSize * _glintWidthScale;
            main.startSizeY = _glintSize * _glintHeightScale;
            main.startSizeZ = _glintSize;
            main.startColor = _glintColor;
            main.startRotation = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 1;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;
            // 짧게 확 커졌다가 바로 사라지는 번쩍임이다.
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
            ParticleSystemRenderer renderer = glint.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = -GLINT_SORTING_FUDGE;
            renderer.maxParticleSize = GLINT_MAX_SCREEN_SIZE;
            renderer.sharedMaterial = _smokes[i].GetComponent<ParticleSystemRenderer>().sharedMaterial;
            glint.SetActive(true);
            _beast.GetComponent<TwinBossWarningVisual>().AddGlintRenderers(glint.transform, glintShape);
            _glints[i] = particles;
        }
    }

    /// <summary>
    /// 가운데의 세로 빛줄기와 얇은 가로 팔이 + 모양에 가깝게 보이는 흰 텍스처를 GLINT_TEXTURE_SIZE 크기로 만들어 반환한다.
    /// 가로 팔의 얇은 높이 위로 GLINT_CENTER_WIDTH_SCALE에 한정된 높은 곡률의 중앙 빛줄기를 세워 + 모양을 만든다.
    /// 팔 끝은 0으로 좁아지며 경계는 한 픽셀만 부드럽게 해 계단지거나 연기처럼 번지지 않는다.
    /// </summary>
    internal static Texture2D CreateGlintShape()
    {
        Texture2D texture = new Texture2D(GLINT_TEXTURE_SIZE, GLINT_TEXTURE_SIZE, TextureFormat.RGBA32, false)
        {
            name = "WolfEyeGlintShape",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[GLINT_TEXTURE_SIZE * GLINT_TEXTURE_SIZE];
        float pixel = 2f / GLINT_TEXTURE_SIZE;
        for (int x = 0; x < GLINT_TEXTURE_SIZE; x++)
        {
            float u = Mathf.Abs((x + 0.5f) * pixel - 1f);
            float horizontalArm = GLINT_ARM_HALF_HEIGHT * Mathf.Sqrt(Mathf.Max(0f, 1f - u));
            float centerSpan = Mathf.Max(0f, 1f - u / GLINT_CENTER_WIDTH_SCALE);
            float halfHeight = horizontalArm + (1f - horizontalArm) * Mathf.Pow(centerSpan, GLINT_CURVE_POWER);
            for (int y = 0; y < GLINT_TEXTURE_SIZE; y++)
            {
                float v = Mathf.Abs((y + 0.5f) * pixel - 1f);
                byte alpha = (byte)(Mathf.Clamp01((halfHeight - v) / pixel + 0.5f) * 255f);
                pixels[y * GLINT_TEXTURE_SIZE + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    /// <summary>덮치며 플레이어에게 접근했다는 알림(BeastBossAttack.PounceGlint)마다 두 눈의 별빛 _glints를 처음부터 다시 터뜨린다.</summary>
    private void HandlePounceGlint()
    {
        foreach (ParticleSystem glint in _glints)
        {
            glint.Clear(false);
            glint.Play(false);
        }
    }

    /// <summary>
    /// 지난 프레임부터 deltaTime 동안 머리가 움직인 속도에 가닥이 흘러가는 속도 _trailSpeed를 더해, 그 속도로 트레일이 남는 시간이
    /// _maxTrailLength를 넘지 않도록 가닥 수명(_maxLifetime) 대비 트레일 시간 비율을 반환한다. 비율은 _trailLifetime을 넘지 않는다.
    /// 머리 위치를 _previousHeadPosition에 기록한다.
    /// </summary>
    private float GetTrailLifetime(float deltaTime)
    {
        Vector3 headPosition = _head.position;
        float headSpeed = deltaTime > 0f ? Vector3.Distance(headPosition, _previousHeadPosition) / deltaTime : 0f;
        _previousHeadPosition = headPosition;
        // 머리 속도와 흘러가는 속도를 더한 값은 실제 꼬리가 늘어나는 속도의 상한이라 길이가 최대값을 넘지 않는다.
        float trailTime = _maxTrailLength / Mathf.Max(0.01f, headSpeed + _trailSpeed);
        return Mathf.Min(_trailLifetime, trailTime / _maxLifetime);
    }

    /// <summary>
    /// 잡몹 연기 smoke를 눈 트레일로 바꾼다. 눈 크기 구에서 _rate개씩 입자를 내고, 입자는 머리 기준으로 _trailSpeed만큼 뒤로 수평하게,
    /// side(-1 왼눈·1 오른눈) 쪽으로 _spreadSpeed만큼 벌어지며 위로는 _riseSpeed만큼만 흘러간다.
    /// 입자 자체는 보이지 않게 줄이고, 연기와 같은 재질의 월드 공간 트레일을 머리 쪽 _trailWidth에서 꼬리로 가늘고 옅어지게 그린다.
    /// </summary>
    private void ConfigureTrail(ParticleSystem smoke, float side)
    {
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        smoke.transform.localPosition = Vector3.zero;
        smoke.transform.localRotation = Quaternion.identity;

        ParticleSystem.MainModule main = smoke.main;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.prewarm = false;
        main.startColor = _color;
        main.startSize = HIDDEN_PARTICLE_SIZE;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(_minLifetime, _maxLifetime);
        main.maxParticles = Mathf.CeilToInt(_rate * _maxLifetime) + 4;

        ParticleSystem.EmissionModule emission = smoke.emission;
        emission.rateOverTimeMultiplier = _rate;

        ParticleSystem.ShapeModule shape = smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = _trailWidth;
        shape.radiusThickness = 1f;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;
        shape.scale = Vector3.one;

        ParticleSystem.VelocityOverLifetimeModule velocity = smoke.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(side * _spreadSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(_riseSpeed);
        velocity.z = new ParticleSystem.MinMaxCurve(-_trailSpeed);

        ParticleSystem.NoiseModule noise = smoke.noise;
        noise.strengthMultiplier *= _flutterScale;

        ParticleSystem.TrailModule trails = smoke.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        // 머리가 움직여도 지나온 자리에 트레일이 남아 옆으로 길게 날린다.
        trails.worldSpace = true;
        trails.lifetime = _trailLifetime;
        trails.minVertexDistance = _minVertexDistance;
        trails.dieWithParticles = false;
        trails.sizeAffectsWidth = false;
        trails.sizeAffectsLifetime = false;
        trails.inheritParticleColor = true;
        trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(_trailWidth, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        trails.colorOverTrail = fade;

        ParticleSystemRenderer renderer = smoke.GetComponent<ParticleSystemRenderer>();
        // 트레일은 연기와 같은 재질로 그려 연기처럼 파동에 드러날 때만 보인다.
        renderer.trailMaterial = renderer.sharedMaterial;
        smoke.Play(false);
    }
}
