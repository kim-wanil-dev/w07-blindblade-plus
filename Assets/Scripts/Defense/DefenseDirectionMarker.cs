using UnityEngine;

using UnityEngine.UI;

/// <summary>
/// 화면 중심 원 위의 한 방향에 방향 호와 선택적인 수리검 아이콘을 그린다.
/// 회전과 투명도는 표시 담당자가 RectTransform과 CanvasRenderer로 조정한다.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DefenseDirectionMarker : MaskableGraphic
{
    private const int ARC_SEGMENTS = 8;

    [Header("형태")]
    [SerializeField, Min(10f)] private float _radius = 230f;
    [SerializeField, Min(1f)] private float _iconSize = 16f;
    [SerializeField] private bool _showIcon = true;
    [SerializeField, Min(1f)] private float _arcThickness = 4f;
    [SerializeField, Range(5f, 90f)] private float _arcAngle = 18f;
    private float _iconScale = 1f;

    /// <summary>scale로 원 반경은 유지한 채 아이콘 크기 배율만 바꾸고 변화가 있을 때 메시를 다시 만든다.</summary>
    public void SetIconScale(float scale)
    {
        if (Mathf.Abs(scale - _iconScale) < 0.01f)
            return;
        _iconScale = scale;
        SetVerticesDirty();
    }

    /// <summary>radius·iconSize·arcThickness·arcAngle로 원 반경과 아이콘·호 크기를 설정하고 메시를 다시 만든다.</summary>
    public void Configure(float radius, float iconSize, float arcThickness, float arcAngle)
    {
        _radius = radius;
        _iconSize = iconSize;
        _arcThickness = arcThickness;
        _arcAngle = arcAngle;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        AddArc(vh);
        if (_showIcon)
        {
            float iconSize = _iconSize * _iconScale;
            AddShurikenIcon(vh, new Vector2(0f, _radius + _arcThickness + iconSize), iconSize);
        }
    }

    /// <summary>위쪽을 중심으로 _arcAngle 폭의 얇은 호를 원 반경에 추가한다.</summary>
    private void AddArc(VertexHelper vh)
    {
        int start = vh.currentVertCount;
        float half = _arcAngle * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i <= ARC_SEGMENTS; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (float)ARC_SEGMENTS);
            Vector2 direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            vh.AddVert(direction * (_radius - _arcThickness * 0.5f), color, Vector2.zero);
            vh.AddVert(direction * (_radius + _arcThickness * 0.5f), color, Vector2.zero);
        }
        for (int i = 0; i < ARC_SEGMENTS; i++)
        {
            int vertex = start + i * 2;
            vh.AddTriangle(vertex, vertex + 1, vertex + 3);
            vh.AddTriangle(vertex, vertex + 3, vertex + 2);
        }
    }

    /// <summary>center에 size 크기의 긴 날 네 개와 짧은 홈 네 개를 번갈아 둔 수리검 모양 별을 추가한다.</summary>
    private void AddShurikenIcon(VertexHelper vh, Vector2 center, float size)
    {
        int start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i < 8; i++)
        {
            float angle = (i * 45f + 45f) * Mathf.Deg2Rad;
            float length = i % 2 == 0 ? size : size * 0.35f;
            vh.AddVert(center + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * length, color, Vector2.zero);
        }
        for (int i = 0; i < 8; i++)
            vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 8);
    }
}
