using UnityEngine;

/// <summary>
/// 퍼펙트 패링 준비 중(검기를 쓸 수 있는 동안) 발밑에서 시선 수평 방향으로 짧은 바닥 직선을 긋고 그 주변 시야를 밝힌다.
/// 직선은 걸음 파동 링처럼 인지 윤곽선으로만 그려 밝혀진 만큼 실루엣으로 드러나며, 시야는 끝으로 갈수록 흐려진다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEmpower))]
public sealed class PlayerSwordWaveGuide : MonoBehaviour
{
    private const float SURFACE_OFFSET = 0.02f;

    [Header("직선")]
    [SerializeField, Tooltip("바닥 직선 재질. 걸음 파동 링과 같은 스프라이트 재질을 쓴다.")]
    private Material _lineMaterial;
    [SerializeField, Min(0.1f), Tooltip("발밑 앞에서 시선 방향으로 그어지는 직선 길이(미터).")]
    private float _length = 1.3f;
    [SerializeField, Min(0.01f), Tooltip("바닥 직선 폭(미터). 검기가 나가는 순간의 좌우 폭(약 10.5m)의 1/3이 기본값이다.")]
    private float _lineWidth = 3.5f;
    [SerializeField, Tooltip("직선 시작 쪽 색. 끝에서는 같은 색의 알파 0이 된다.")]
    private Color _lineColor = Color.white;
    [SerializeField, Min(0f), Tooltip("발밑에서 직선이 시작되는 앞쪽 거리(미터).")]
    private float _startOffset = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("준비가 시작되고 끝날 때 직선과 시야가 나타나고 사라지는 실시간(초).")]
    private float _fadeDuration = 0.15f;

    [Header("시야")]
    [SerializeField, Range(1, 12), Tooltip("직선 길이 방향으로 시야 구를 놓는 줄 수. 수리검 공개와 같은 구 32개 한도를 나눠 쓴다.")]
    private int _revealCount = 4;
    [SerializeField, Range(1, 5), Tooltip("직선 폭 방향으로 시야 구를 놓는 칸 수. 전체 구 개수는 줄 수 × 칸 수다.")]
    private int _revealColumns = 3;
    [SerializeField, Min(0.1f), Tooltip("직선을 따라 놓는 시야 구 반경(미터).")]
    private float _revealRadius = 1f;
    [SerializeField, Tooltip("바닥 기준 시야 구 중심 높이(미터).")]
    private float _revealHeight = 0.3f;

    [Header("상태")]
    private PlayerEmpower _empower;
    private PlayerMotor _motor;
    private LineRenderer _line;
    private PerceptionRevealMap _revealMap;
    private float _visibility;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
        _motor = GetComponent<PlayerMotor>();
        CreateLine();
    }

    void OnDisable()
    {
        _visibility = 0f;
        _line.enabled = false;
    }

    void OnDestroy()
    {
        if (_line != null) Destroy(_line.gameObject);
    }

    void Update()
    {
        float step = Time.unscaledDeltaTime / _fadeDuration;
        _visibility = Mathf.MoveTowards(_visibility, _empower.IsSwordWaveReady ? 1f : 0f, step);
        if (_visibility <= 0f)
        {
            _line.enabled = false;
            return;
        }

        Vector3 direction = _motor.ViewForward;
        Vector3 start = transform.position + direction * _startOffset + Vector3.up * SURFACE_OFFSET;
        Vector3 end = start + direction * _length;
        UpdateLine(start, end);
        RevealAlong(start, end);
    }

    /// <summary>
    /// 플레이어와 함께 다니는 바닥 직선 LineRenderer를 만든다. 인지 예외를 붙이지 않아 걸음 파동 링처럼 밝혀진 곳에서만 윤곽선으로 보인다.
    /// _lineMaterial·_lineWidth로 설정하며 처음에는 꺼 둔 채 _line에 저장한다.
    /// </summary>
    private void CreateLine()
    {
        GameObject lineObject = new GameObject("SwordWaveGuideLine");
        lineObject.transform.SetParent(transform, false);
        _line = lineObject.AddComponent<LineRenderer>();
        _line.sharedMaterial = _lineMaterial;
        _line.useWorldSpace = true;
        _line.positionCount = 2;
        _line.widthMultiplier = _lineWidth;
        // TransformZ 정렬은 오브젝트의 앞쪽을 면 방향으로 써서 직선을 바닥에 눕힌다.
        _line.alignment = LineAlignment.TransformZ;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;
        _line.enabled = false;
    }

    /// <summary>start에서 end까지 바닥에 눕힌 직선을 그리고 시작 색에서 끝 알파 0으로 흐려지게 _visibility만큼 색을 바꾼다.</summary>
    private void UpdateLine(Vector3 start, Vector3 end)
    {
        _line.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        _line.SetPosition(0, start);
        _line.SetPosition(1, end);
        Color startColor = _lineColor;
        startColor.a *= _visibility;
        Color endColor = _lineColor;
        endColor.a = 0f;
        _line.startColor = startColor;
        _line.endColor = endColor;
        _line.enabled = true;
    }

    /// <summary>
    /// start에서 end까지 _revealCount줄 × _revealColumns칸의 구를 직선 폭에 고르게 펼쳐 이번 프레임 시야를 밝힌다.
    /// 걸음 파동처럼 앞쪽은 온전히 밝히고 끝으로 갈수록 부드럽게 흐려지며 전체 강도에 _visibility를 곱한다.
    /// 시야 맵을 처음 쓸 때 SensoryManager에서 찾아 _revealMap에 저장하고, 없으면 시야는 건너뛴다.
    /// </summary>
    private void RevealAlong(Vector3 start, Vector3 end)
    {
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null) return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
            if (_revealMap == null) return;
        }
        Vector3 side = Vector3.Cross(Vector3.up, end - start).normalized;
        for (int i = 0; i < _revealCount; i++)
        {
            float t = (i + 0.5f) / _revealCount;
            Vector3 row = Vector3.Lerp(start, end, t) + Vector3.up * _revealHeight;
            float strength = (1f - Mathf.SmoothStep(0f, 1f, t)) * _visibility;
            for (int c = 0; c < _revealColumns; c++)
            {
                float offset = ((c + 0.5f) / _revealColumns - 0.5f) * _lineWidth;
                _revealMap.RevealAround(row + side * offset, _revealRadius, 0f, false, strength);
            }
        }
    }
}
