using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 일반 Beast 늑대 두 눈에서 붉게 빛나는 줄기를 길게 끌리게 그린다.
/// 눈마다 빛나는 점 하나와, 눈에서 일정 간격으로 낸 점들을 월드 공간에 남겨 잇는 LineRenderer 줄기를 쓴다.
/// 남은 점은 머리 뒤 바깥쪽으로 흘러가며 늑대 움직임에 따라 바람에 밀리듯 흔들리고 꼬리로 갈수록 펄럭인다.
/// 줄기와 점은 눈 전용 발광 셰이더(DefenseBeastEyeStreak)로 윤곽선 합성 뒤에 더해 그리며, 몸 연기처럼 파동에 드러날 때만 보인다.
/// 몸 연기가 붉어지는 전조 동안에는 줄기가 더 밝고 굵어진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseBeastEyes : MonoBehaviour
{
    private const float MAX_SWAY_DELTA_TIME = 0.05f;
    private const float FLUTTER_SEED_RANGE = 100f;

    private static readonly int _intensityId = Shader.PropertyToID("_Intensity");
    private static readonly int _dotModeId = Shader.PropertyToID("_DotMode");

    [Header("연결")]
    [SerializeField, Tooltip("몸 연기의 붉은 전조 세기를 읽는 경고 표시")]
    private TwinBossWarningVisual _warningVisual;
    [SerializeField, Tooltip("눈을 붙일 늑대 머리 관절")]
    private Transform _head;
    [SerializeField, Tooltip("눈 줄기와 눈 점을 그리는 발광 재질(DefenseBeastEyeStreak)")]
    private Material _material;
    [SerializeField, Tooltip("머리 로컬 좌표의 눈 위치(미터). 귀와 입 사이, 두개골 앞쪽 위 표면의 좌우다.")]
    private Vector3[] _eyeOffsets = { new Vector3(-0.09f, 0.3f, 0.18f), new Vector3(0.09f, 0.3f, 0.18f) };
    private MaterialPropertyBlock _properties;

    [Header("눈 점")]
    [SerializeField, Min(0f), Tooltip("눈에서 빛나는 점의 크기(미터). 0이면 점을 그리지 않는다.")]
    private float _eyeSize = 0f;
    private Transform[] _dots;
    private Renderer[] _dotRenderers;

    [Header("줄기")]
    [SerializeField, Min(0.001f), Tooltip("눈 쪽 줄기 굵기(미터). 꼬리로 갈수록 가늘어진다.")]
    private float _width = 0.06f;
    [SerializeField, Min(0.05f), Tooltip("줄기의 한 점이 남아 있는 시간(초). 길수록 지나온 길이 길게 남는다.")]
    private float _lifetime = 0.45f;
    [SerializeField, Min(0.05f), Tooltip("줄기 최대 길이(미터). 빨리 달려도 이보다 길어지지 않는다.")]
    private float _maxLength = 1.5f;
    [SerializeField, Min(0.005f), Tooltip("눈에서 줄기 점을 내는 간격(초). 작을수록 부드럽다.")]
    private float _emitInterval = 0.02f;
    [SerializeField, Min(0f), Tooltip("점이 머리 뒤쪽으로 흘러가는 시작 속도(미터/초). 멈춰 있어도 줄기가 뒤로 늘어진다.")]
    private float _driftSpeed = 0.8f;
    [SerializeField, Min(0f), Tooltip("점이 눈마다 바깥 옆으로 벌어지는 시작 속도(미터/초). 클수록 좌우로 찢어지듯 갈라진다.")]
    private float _spreadSpeed = 0.35f;
    [SerializeField, Tooltip("점이 위로 떠오르는 속도(미터/초)")]
    private float _riseSpeed = 0.05f;
    [SerializeField, Min(0f), Tooltip("꼬리 끝에서 줄기가 펄럭이는 최대 폭(미터). 눈 쪽은 흔들리지 않는다.")]
    private float _flutterAmplitude = 0.06f;
    [SerializeField, Min(0f), Tooltip("펄럭임 빠르기(초당 흔들림)")]
    private float _flutterFrequency = 3f;
    private LineRenderer[] _lines;
    private Vector3[][] _points;
    private Vector3[][] _velocities;
    private float[][] _births;
    private int[] _counts;
    private float[] _lastEmitAt;
    private float[] _seeds;
    private Vector3[] _linePositions;

    [Header("움직임 휘날림")]
    [SerializeField, Tooltip("몸 연기 조정. 줄기도 몸 연기와 같은 바람 속도(WorldDrift)로 밀린다.")]
    private TwinBeastSmokeTuning _bodySmoke;
    [SerializeField, Min(0f), Tooltip("몸 연기 바람에 더해, 머리가 1m/s 움직일 때 줄기를 반대쪽으로 더 미는 속도(미터/초)")]
    private float _pushScale = 0.5f;
    [SerializeField, Min(0f), Tooltip("더 미는 속도의 최대값(미터/초)")]
    private float _maxPush = 4f;
    [SerializeField, Min(0.1f), Tooltip("한 프레임에 이보다 멀리 옮겨지면 순간이동으로 보고 움직임에 넣지 않는다(미터)")]
    private float _teleportThreshold = 3f;
    private Vector3 _previousHeadPosition;
    private Vector3 _moveVelocity;

    [Header("전조")]
    [SerializeField, Min(0f), Tooltip("몸 연기가 완전히 붉어졌을 때 줄기·점 밝기 배율")]
    private float _warningIntensity = 2.2f;
    [SerializeField, Min(0f), Tooltip("몸 연기가 완전히 붉어졌을 때 줄기 굵기 배율")]
    private float _warningWidthScale = 1.5f;

    void Awake()
    {
        int eyeCount = _eyeOffsets.Length;
        int capacity = Mathf.CeilToInt(_lifetime / _emitInterval) + 2;
        _properties = new MaterialPropertyBlock();
        _lines = new LineRenderer[eyeCount];
        _points = new Vector3[eyeCount][];
        _velocities = new Vector3[eyeCount][];
        _births = new float[eyeCount][];
        _counts = new int[eyeCount];
        _lastEmitAt = new float[eyeCount];
        _seeds = new float[eyeCount];
        _dots = new Transform[eyeCount];
        _dotRenderers = new Renderer[eyeCount];
        _linePositions = new Vector3[capacity + 1];
        for (int i = 0; i < eyeCount; i++)
        {
            _points[i] = new Vector3[capacity];
            _velocities[i] = new Vector3[capacity];
            _births[i] = new float[capacity];
            _seeds[i] = Random.value * FLUTTER_SEED_RANGE;
            _lines[i] = CreateLine(i);
            CreateDot(i);
        }
    }

    void OnEnable()
    {
        _previousHeadPosition = _head.position;
        _moveVelocity = Vector3.zero;
        // 풀에서 다시 나올 때 이전 자리의 줄기가 이어 그려지지 않게 비운다.
        for (int i = 0; i < _counts.Length; i++)
        {
            _counts[i] = 0;
            _lastEmitAt[i] = float.NegativeInfinity;
            _lines[i].positionCount = 0;
        }
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        Vector3 drift = GetDrift(Mathf.Min(deltaTime, MAX_SWAY_DELTA_TIME));
        float warning = _warningVisual.Intensity;
        float intensity = Mathf.Lerp(1f, _warningIntensity, warning);
        float width = _width * Mathf.Lerp(1f, _warningWidthScale, warning);
        Camera view = Camera.main;
        for (int i = 0; i < _lines.Length; i++)
        {
            Vector3 eye = _head.TransformPoint(_eyeOffsets[i]);
            UpdatePoints(i, eye, drift, deltaTime);
            DrawLine(i, eye, width, intensity);
            UpdateDot(i, view, intensity);
        }
    }

    /// <summary>
    /// eye 번째 눈의 줄기 LineRenderer를 이 오브젝트 아래 만든다. 월드 좌표 점을 화면을 향한 띠로 잇고,
    /// 눈 쪽에서 꼬리로 굵기·밝기가 줄어드는 곡선과 _material을 쓰며 그림자는 끈다. 만든 LineRenderer를 반환한다.
    /// </summary>
    private LineRenderer CreateLine(int eye)
    {
        GameObject streak = new GameObject("WolfEyeStreak_" + eye);
        streak.transform.SetParent(transform, false);
        LineRenderer line = streak.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        // 윤곽선 모드가 띠 양옆을 선으로 그리도록 둥근 끝·모서리 정점을 덧붙이지 않는다.
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = _material;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.2f, 0.8f), new Keyframe(1f, 0f));
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.75f, 0.3f), new GradientAlphaKey(0.3f, 0.7f), new GradientAlphaKey(0f, 1f) });
        line.colorGradient = fade;
        line.positionCount = 0;
        return line;
    }

    /// <summary>
    /// eye 번째 눈 위치에 빛나는 점으로 쓸 사각형을 머리(_head) 아래 만든다. 충돌체는 지우고 _material을 점 모드로 그린다.
    /// _eyeSize가 0이면 만들지 않는다. 만든 점을 _dots·_dotRenderers에 저장한다.
    /// </summary>
    private void CreateDot(int eye)
    {
        if (_eyeSize <= 0f)
            return;

        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Quad);
        dot.name = "WolfEyeDot_" + eye;
        Destroy(dot.GetComponent<Collider>());
        dot.transform.SetParent(_head, false);
        dot.transform.localPosition = _eyeOffsets[eye];
        dot.transform.localScale = Vector3.one * _eyeSize;
        Renderer renderer = dot.GetComponent<Renderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        _dots[eye] = dot.transform;
        _dotRenderers[eye] = renderer;
    }

    /// <summary>
    /// eye 번째 눈 줄기의 점을 deltaTime만큼 진행한다. 남은 점은 자기 시작 속도에 바람 속도 drift를 더해 움직이고,
    /// _lifetime이 지난 점은 지운다. _emitInterval마다 눈 위치 eyePosition에 머리 뒤 바깥쪽·위로 흘러갈 새 점을 맨 앞에 넣는다.
    /// </summary>
    private void UpdatePoints(int eye, Vector3 eyePosition, Vector3 drift, float deltaTime)
    {
        Vector3[] points = _points[eye];
        Vector3[] velocities = _velocities[eye];
        float[] births = _births[eye];
        float now = Time.time;
        int count = _counts[eye];
        for (int p = 0; p < count; p++)
            points[p] += (velocities[p] + drift) * deltaTime;
        while (count > 0 && now - births[count - 1] > _lifetime)
            count--;

        if (now - _lastEmitAt[eye] >= _emitInterval)
        {
            _lastEmitAt[eye] = now;
            if (count == points.Length)
                count--;
            for (int p = count; p > 0; p--)
            {
                points[p] = points[p - 1];
                velocities[p] = velocities[p - 1];
                births[p] = births[p - 1];
            }
            float side = Mathf.Sign(_eyeOffsets[eye].x);
            points[0] = eyePosition;
            velocities[0] = -_head.forward * _driftSpeed + _head.right * (side * _spreadSpeed) + Vector3.up * _riseSpeed;
            births[0] = now;
            count++;
        }
        _counts[eye] = count;
    }

    /// <summary>
    /// eye 번째 눈의 현재 위치 eyePosition에서 시작해 남은 점을 차례로 이어 LineRenderer에 넣는다.
    /// 오래된 점일수록 머리 오른쪽·위 방향으로 펄럭이게 하고, 이은 길이가 _maxLength를 넘으면 그 자리에서 자른다.
    /// 굵기 width와 밝기 intensity를 적용한다.
    /// </summary>
    private void DrawLine(int eye, Vector3 eyePosition, float width, float intensity)
    {
        Vector3[] points = _points[eye];
        float[] births = _births[eye];
        float now = Time.time;
        Vector3 right = _head.right;
        int used = 1;
        float length = 0f;
        _linePositions[0] = eyePosition;
        for (int p = 0; p < _counts[eye]; p++)
        {
            float age = Mathf.Clamp01((now - births[p]) / _lifetime);
            float phase = births[p] * _flutterFrequency + _seeds[eye];
            Vector3 flutter = (right * (Mathf.PerlinNoise(phase, now * _flutterFrequency) - 0.5f)
                + Vector3.up * (Mathf.PerlinNoise(phase + 31.7f, now * _flutterFrequency) - 0.5f))
                * (2f * _flutterAmplitude * age);
            Vector3 position = points[p] + flutter;
            float segment = Vector3.Distance(_linePositions[used - 1], position);
            if (length + segment > _maxLength)
            {
                // 남은 길이만큼만 이어 최대 길이에서 깔끔하게 끝낸다.
                float remain = _maxLength - length;
                if (remain > 0.001f)
                {
                    _linePositions[used] = Vector3.Lerp(_linePositions[used - 1], position, remain / segment);
                    used++;
                }
                break;
            }
            length += segment;
            _linePositions[used++] = position;
        }

        LineRenderer line = _lines[eye];
        line.widthMultiplier = width;
        line.positionCount = used;
        for (int p = 0; p < used; p++)
            line.SetPosition(p, _linePositions[p]);
        line.GetPropertyBlock(_properties);
        _properties.SetFloat(_intensityId, intensity);
        line.SetPropertyBlock(_properties);
    }

    /// <summary>
    /// eye 번째 눈 점이 있으면 view 카메라를 바라보게 돌리고 밝기 intensity를 점 모드로 적용한다.
    /// </summary>
    private void UpdateDot(int eye, Camera view, float intensity)
    {
        Transform dot = _dots[eye];
        if (dot == null)
            return;

        if (view != null)
            dot.rotation = view.transform.rotation;
        Renderer renderer = _dotRenderers[eye];
        renderer.GetPropertyBlock(_properties);
        _properties.SetFloat(_intensityId, intensity);
        _properties.SetFloat(_dotModeId, 1f);
        renderer.SetPropertyBlock(_properties);
    }

    /// <summary>
    /// deltaTime 동안 머리가 수평으로 움직인 속도 _moveVelocity를 구하고, 몸 연기(_bodySmoke)가 이번 프레임 쓰는 바람 속도에
    /// 머리 움직임 반대쪽으로 _pushScale배(_maxPush까지) 더 미는 속도를 더한 월드 속도를 반환한다.
    /// 줄기가 몸 연기와 같이 밀리고 조금 더 뒤로 흘러가게 한다. _previousHeadPosition·_moveVelocity를 갱신한다.
    /// </summary>
    private Vector3 GetDrift(float deltaTime)
    {
        Vector3 headPosition = _head.position;
        Vector3 displacement = Vector3.ProjectOnPlane(headPosition - _previousHeadPosition, Vector3.up);
        _previousHeadPosition = headPosition;
        // 풀에서 다시 배치되는 것처럼 한 번에 멀리 옮겨진 프레임은 직전 움직임을 그대로 쓴다.
        if (displacement.sqrMagnitude <= _teleportThreshold * _teleportThreshold)
            _moveVelocity = displacement / deltaTime;

        return _bodySmoke.WorldDrift + Vector3.ClampMagnitude(-_moveVelocity * _pushScale, _maxPush);
    }
}
