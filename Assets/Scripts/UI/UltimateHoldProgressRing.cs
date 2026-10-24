using UnityEngine;

using UnityEngine.UI;

/// <summary>
/// 거합 홀드 차징 진행도를 수리검 방향 인디케이터 원의 안쪽 외곽을 따라 흑백 원형 진행 바로 그린다.
/// 현재 플레이어 PlayerEmpower의 IsAutomaticCharging과 ChargeProgress를 읽으며 차징 중에만 보인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UltimateHoldProgressRing : MaskableGraphic
{
    private const int RING_SEGMENTS = 96;
    private const float PROGRESS_EPSILON = 0.001f;

    [Header("형태")]
    [SerializeField, Min(1f), Tooltip("진행 바 두께 중심의 반지름. 수리검 방향 호(반지름 230, 두께 4)의 안쪽 외곽에 맞춘다.")]
    private float _radius = 223f;
    [SerializeField, Min(1f), Tooltip("진행 바 두께")]
    private float _thickness = 6f;
    [SerializeField, Tooltip("아직 차지 않은 구간의 바탕 색")]
    private Color _trackColor = Color.black;
    [SerializeField, Tooltip("12시부터 시계 방향으로 차오르는 진행 구간의 색")]
    private Color _fillColor = Color.white;

    [Header("상태")]
    private PlayerEmpower _empower;
    private bool _visible;
    private float _progress;

    void LateUpdate()
    {
        PlayerController player = Game.Player;
        if (player != null && (_empower == null || _empower.gameObject != player.gameObject))
            _empower = player.GetComponent<PlayerEmpower>();

        bool visible = _empower != null && _empower.IsAutomaticCharging;
        float progress = visible ? _empower.ChargeProgress : 0f;
        if (visible == _visible && Mathf.Abs(progress - _progress) < PROGRESS_EPSILON)
            return;
        _visible = visible;
        _progress = progress;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!_visible)
            return;
        AddArc(vh, 1f, _trackColor);
        if (_progress > 0f)
            AddArc(vh, _progress, _fillColor);
    }

    /// <summary>
    /// 12시에서 시계 방향으로 원 둘레의 fraction 비율만큼 _radius·_thickness 띠를 arcColor로 vh에 추가한다.
    /// 구간 길이에 비례한 분할 수로 정점과 삼각형을 만든다.
    /// </summary>
    private void AddArc(VertexHelper vh, float fraction, Color arcColor)
    {
        int segments = Mathf.Max(1, Mathf.CeilToInt(RING_SEGMENTS * fraction));
        int start = vh.currentVertCount;
        float inner = _radius - _thickness * 0.5f;
        float outer = _radius + _thickness * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float angle = fraction * (i / (float)segments) * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            vh.AddVert(direction * inner, arcColor, Vector2.zero);
            vh.AddVert(direction * outer, arcColor, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
        {
            int vertex = start + i * 2;
            vh.AddTriangle(vertex, vertex + 1, vertex + 3);
            vh.AddTriangle(vertex, vertex + 3, vertex + 2);
        }
    }
}
