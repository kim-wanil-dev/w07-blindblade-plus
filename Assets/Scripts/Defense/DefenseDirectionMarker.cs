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
    private const float ICON_VISIBLE_FRACTION = 0.70f;
    private const float WARNING_BADGE_SIZE_FRACTION = 0.28f;
    private const float MIN_WARNING_BADGE_SIZE = 12f;

    [Header("형태")]
    [SerializeField, Min(10f)] private float _radius = 230f;
    [SerializeField, Min(1f)] private float _iconSize = 16f;
    [SerializeField] private bool _showIcon = true;
    [SerializeField, Min(1f)] private float _arcThickness = 4f;
    [SerializeField, Range(5f, 90f)] private float _arcAngle = 18f;

    [Header("수리검 아이콘")]
    [SerializeField, InspectorName("수리검 Sprite Image")]
    private UnityEngine.UI.Image _shurikenIcon;

    [Header("공격 임박 표시")]
    [SerializeField, InspectorName("위험 배지 Image")]
    private UnityEngine.UI.Image _warningBadge;

    private float _iconScale = 1f;

    /// <summary>scale로 원 반경은 유지한 채 아이콘 크기 배율만 바꾸고 변화가 있을 때 메시를 다시 만든다.</summary>
    public void SetIconScale(float scale)
    {
        if (Mathf.Abs(scale - _iconScale) < 0.01f)
            return;
        _iconScale = scale;
        ApplyIconLayout();
        SetVerticesDirty();
    }

    /// <summary>radius·iconSize·arcThickness·arcAngle로 원호와 자식 Sprite 위치·크기를 설정하고 메시를 다시 만든다.</summary>
    public void Configure(float radius, float iconSize, float arcThickness, float arcAngle)
    {
        _radius = radius;
        _iconSize = iconSize;
        _arcThickness = arcThickness;
        _arcAngle = arcAngle;
        ApplyIconLayout();
        SetVerticesDirty();
    }

    /// <summary>iconSprite를 자식 UI Image에 지정하고 현재 크기·색을 반영한다.</summary>
    public void SetShurikenIconSprite(Sprite iconSprite)
    {
        if (_shurikenIcon == null)
            return;
        _shurikenIcon.sprite = iconSprite;
        _shurikenIcon.color = color;
        ApplyIconLayout();
    }

    /// <summary>visible과 Inspector 표시 설정에 따라 자식 수리검 Image를 켜거나 끈다.</summary>
    public void SetShurikenIconVisible(bool visible)
    {
        if (_shurikenIcon != null)
            _shurikenIcon.enabled = visible && _showIcon;
    }

    /// <summary>tint를 자식 수리검 Image의 표시 색상으로 설정한다.</summary>
    public void SetShurikenIconColor(Color tint)
    {
        if (_shurikenIcon != null)
            _shurikenIcon.color = tint;
    }

    /// <summary>alpha를 자식 수리검 Image의 CanvasRenderer에 적용한다.</summary>
    public void SetShurikenIconAlpha(float alpha)
    {
        if (_shurikenIcon != null)
            _shurikenIcon.canvasRenderer.SetAlpha(alpha);
    }

    /// <summary>위험 배지를 원호의 위쪽 중앙에 놓고 화면에 똑바로 보이도록 회전을 상쇄한다.</summary>
    public void SetWarningBadge(bool visible, Color tint, float alpha)
    {
        if (_warningBadge == null)
            return;
        RectTransform badgeRect = _warningBadge.rectTransform;
        badgeRect.anchoredPosition = Vector2.up * _radius;
        badgeRect.localRotation = Quaternion.Inverse(rectTransform.rotation);
        float iconSize = _iconSize * _iconScale * 2f;
        float badgeSize = Mathf.Max(MIN_WARNING_BADGE_SIZE,
            iconSize * ICON_VISIBLE_FRACTION * WARNING_BADGE_SIZE_FRACTION);
        badgeRect.sizeDelta = Vector2.one * (badgeSize * 1.5f);
        _warningBadge.color = tint;
        _warningBadge.canvasRenderer.SetAlpha(alpha);
        _warningBadge.enabled = visible && alpha > 0f;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        AddArc(vh);
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

    /// <summary>Inspector의 수리검 크기와 방향 원호 설정으로 자식 Sprite의 위치·크기를 갱신한다.</summary>
    private void ApplyIconLayout()
    {
        if (_shurikenIcon == null)
            return;
        float iconSize = _iconSize * _iconScale;
        RectTransform iconRect = _shurikenIcon.rectTransform;
        iconRect.anchoredPosition = new Vector2(0f, _radius + _arcThickness + iconSize);
        iconRect.sizeDelta = Vector2.one * (iconSize * 2f);
    }
}
