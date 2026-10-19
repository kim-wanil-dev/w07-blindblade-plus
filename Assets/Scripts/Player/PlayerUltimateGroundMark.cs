using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 궁극기가 세계를 가른 사선을 땅 위의 흰 선 흔적으로 남긴다.
/// 카메라를 지나는 베는 평면과 땅이 만나는 직선 중 그 순간 화면에 보이던 구간을 흔적 선분으로 기억한다.
/// 그 순간에는 화면의 갈라진 선과 겹쳐 보이고, 월드에 남으므로 다가가면 원근에 따라 길어 보인다.
/// 화면 윤곽선 셰이더가 최종 화면에서 땅 표면 위 선분 부분을 직접 칠하므로 파동과 상관없이 보이고 장면 깊이로 가려진다.
/// 잠시 머문 뒤 천천히 흐려지고 사라진다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerUltimateGroundMark : MonoBehaviour
{
    // 흔적 구간을 찾을 때 직선을 따라 화면 안인지 검사하는 간격(미터).
    private const float SAMPLE_STEP = 0.25f;
    // 화면 윤곽선 셰이더의 MAX_GROUND_MARKS와 같아야 한다.
    private const int MAX_MARKS = 8;

    private static readonly int _markCountId = Shader.PropertyToID("_OutlineGroundMarkCount");
    private static readonly int _markSegmentsId = Shader.PropertyToID("_OutlineGroundMarkSegments");
    private static readonly int _markParamsId = Shader.PropertyToID("_OutlineGroundMarkParams");
    private static readonly int _markColorsId = Shader.PropertyToID("_OutlineGroundMarkColors");

    [Header("흔적 선")]
    [SerializeField] private Color _lineColor = Color.white;
    [SerializeField, Min(0.001f), Tooltip("흔적 선 폭(미터).")]
    private float _lineWidth = 0.08f;
    [SerializeField, Min(1f), Tooltip("플레이어 발밑 기준 흔적이 뻗는 최대 거리(미터). 화면에 보이던 구간과 함께 제한한다.")]
    private float _maxLength = 60f;
    [SerializeField, Range(0f, 0.5f), Tooltip("흔적 양 끝이 흐려지는 길이 비율.")]
    private float _endFade = 0.05f;

    [Header("흐려짐")]
    [SerializeField, Min(0f), Tooltip("흔적이 처음 밝기로 머무는 시간(초, 게임 시간).")]
    private float _holdDuration = 2f;
    [SerializeField, Min(0.01f), Tooltip("머문 뒤 사라질 때까지 흐려지는 시간(초, 게임 시간).")]
    private float _fadeDuration = 3f;

    [Header("상태")]
    private readonly List<Mark> _marks = new List<Mark>();
    private readonly Vector4[] _segments = new Vector4[MAX_MARKS];
    private readonly Vector4[] _parameters = new Vector4[MAX_MARKS];
    private readonly Vector4[] _colors = new Vector4[MAX_MARKS];

    private sealed class Mark
    {
        public Vector3 Start { get; set; }
        public Vector3 End { get; set; }
        public Color Color { get; set; }
        public float Age { get; set; }
    }

    void OnDisable()
    {
        _marks.Clear();
        Shader.SetGlobalInt(_markCountId, 0);
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;
        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            _marks[i].Age += deltaTime;
            if (_marks[i].Age >= _holdDuration + _fadeDuration)
                _marks.RemoveAt(i);
        }
    }

    void LateUpdate()
    {
        Publish();
    }

    /// <summary>
    /// planePoint·planeNormal 평면이 viewProjection 화면에 보이던 땅 위 구간을 기본 흔적 색 _lineColor로 남긴다.
    /// 자세한 규칙은 색을 받는 Show와 같다.
    /// </summary>
    public void Show(Vector3 planePoint, Vector3 planeNormal, Matrix4x4 viewProjection)
    {
        Show(planePoint, planeNormal, viewProjection, _lineColor);
    }

    /// <summary>
    /// planePoint·planeNormal 평면이 플레이어 발밑 높이의 땅과 만나는 직선을 구한다.
    /// 그중 viewProjection 화면 안에 보이고 발밑에서 _maxLength 안인 구간을 color 선 흔적으로 남긴다. 나중에 남긴 흔적이 겹치는 자리에서 위에 그려진다.
    /// 흔적이 MAX_MARKS개를 넘으면 가장 오래된 것을 지운다. 평면이 땅과 거의 나란하거나 보이는 구간이 없으면 남기지 않는다.
    /// </summary>
    public void Show(Vector3 planePoint, Vector3 planeNormal, Matrix4x4 viewProjection, Color color)
    {
        Vector3 feet = transform.position;
        Vector3 direction = Vector3.Cross(planeNormal, Vector3.up);
        Vector3 across = Vector3.ProjectOnPlane(planeNormal, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f || across.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();
        across.Normalize();
        // 발밑 높이에서 평면 위에 있으면서 발밑에 가장 가까운 점을 직선의 기준점으로 쓴다.
        float facing = Vector3.Dot(planeNormal, across);
        if (Mathf.Abs(facing) < 0.0001f)
            return;
        Vector3 origin = feet + across * (Vector3.Dot(planeNormal, planePoint - feet) / facing);
        if (!TryGetVisibleRange(origin, direction, viewProjection, out float start, out float end))
            return;
        if (_marks.Count == MAX_MARKS)
            _marks.RemoveAt(0);
        _marks.Add(new Mark { Start = origin + direction * start, End = origin + direction * end, Color = color });
    }

    /// <summary>
    /// origin에서 direction으로 뻗은 땅 위 직선을 -_maxLength~_maxLength 구간에서 SAMPLE_STEP 간격으로 viewProjection에 투영한다.
    /// 화면 안에 들어오는 가장 앞과 뒤의 거리를 start·end로 반환하며, 하나도 없으면 false를 반환한다.
    /// </summary>
    private bool TryGetVisibleRange(Vector3 origin, Vector3 direction, Matrix4x4 viewProjection, out float start, out float end)
    {
        start = float.PositiveInfinity;
        end = float.NegativeInfinity;
        for (float distance = -_maxLength; distance <= _maxLength; distance += SAMPLE_STEP)
        {
            Vector3 point = origin + direction * distance;
            Vector4 clip = viewProjection * new Vector4(point.x, point.y, point.z, 1f);
            if (clip.w <= 0f || Mathf.Abs(clip.x / clip.w) > 1f || Mathf.Abs(clip.y / clip.w) > 1f)
                continue;
            start = Mathf.Min(start, distance);
            end = Mathf.Max(end, distance);
        }
        return end > start;
    }

    /// <summary>
    /// 남아 있는 흔적의 선분·높이·반폭·세기·양 끝 흐림·색을 남긴 순서대로 화면 윤곽선 셰이더 전역 배열과 개수로 넘긴다.
    /// 세기는 머무는 동안 1이고 이후 SmoothStep으로 0까지 줄어든다.
    /// </summary>
    private void Publish()
    {
        for (int i = 0; i < _marks.Count; i++)
        {
            Mark mark = _marks[i];
            float fade = Mathf.Clamp01((mark.Age - _holdDuration) / _fadeDuration);
            _segments[i] = new Vector4(mark.Start.x, mark.Start.z, mark.End.x, mark.End.z);
            _parameters[i] = new Vector4(mark.Start.y, _lineWidth * 0.5f, 1f - Mathf.SmoothStep(0f, 1f, fade), _endFade);
            _colors[i] = mark.Color;
        }
        Shader.SetGlobalInt(_markCountId, _marks.Count);
        Shader.SetGlobalVectorArray(_markSegmentsId, _segments);
        Shader.SetGlobalVectorArray(_markParamsId, _parameters);
        Shader.SetGlobalVectorArray(_markColorsId, _colors);
    }
}
