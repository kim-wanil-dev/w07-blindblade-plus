using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 텍스처를 화면 중심을 지나는 사선으로 자른 한쪽 절반만 그린다.
/// 강화 공격에서 실시간 화면을 두 절반으로 갈라 서로 반대 방향으로 밀어내는 데 쓴다.
/// </summary>
public sealed class EmpowerSplitGraphic : MaskableGraphic
{
    [Header("상태")]
    private Texture _texture;
    private bool _upperHalf;
    private float _angle;
    private float _separation;
    private readonly List<Vector2> _polygon = new List<Vector2>();
    private readonly List<Vector2> _clipped = new List<Vector2>();

    public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

    /// <summary>
    /// texture를 그릴 화면으로, angle(도)을 화면 중심을 지나는 자르는 선의 기울기로, upperHalf로 선 위쪽·아래쪽 절반을 정한다.
    /// 바뀐 값만 반영해 메시를 다시 만든다.
    /// </summary>
    public void Configure(Texture texture, float angle, bool upperHalf)
    {
        if (_texture != texture)
        {
            _texture = texture;
            SetMaterialDirty();
        }
        _angle = angle;
        _upperHalf = upperHalf;
        SetVerticesDirty();
    }

    /// <summary>separation만큼 자르는 선의 수직 방향으로 절반을 밀어내고 메시를 다시 만든다. 위쪽 절반은 위로, 아래쪽은 아래로 간다.</summary>
    public void SetSeparation(float separation)
    {
        _separation = separation;
        SetVerticesDirty();
    }

    /// <summary>
    /// 현재 사각형을 자르는 선으로 잘라 남은 다각형을 부채꼴 삼각형으로 vh에 채운다.
    /// 정점 위치는 벌어진 거리만큼 밀고 UV는 원래 화면 위치를 쓴다.
    /// </summary>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float radians = _angle * Mathf.Deg2Rad;
        Vector2 normal = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
        if (!_upperHalf) normal = -normal;

        _polygon.Clear();
        _polygon.Add(new Vector2(rect.xMin, rect.yMin));
        _polygon.Add(new Vector2(rect.xMax, rect.yMin));
        _polygon.Add(new Vector2(rect.xMax, rect.yMax));
        _polygon.Add(new Vector2(rect.xMin, rect.yMax));
        ClipToHalfPlane(rect.center, normal);
        if (_clipped.Count < 3) return;

        Vector2 offset = normal * _separation;
        foreach (Vector2 point in _clipped)
        {
            Vector2 uv = new Vector2((point.x - rect.xMin) / rect.width, (point.y - rect.yMin) / rect.height);
            vh.AddVert(point + offset, color, uv);
        }
        for (int i = 1; i < _clipped.Count - 1; i++) vh.AddTriangle(0, i, i + 1);
    }

    /// <summary>
    /// _polygon 중 center를 지나고 normal 쪽을 향하는 반평면 안에 있는 부분만 _clipped에 남긴다.
    /// 경계를 지나는 변은 교점으로 잘라 넣는다.
    /// </summary>
    private void ClipToHalfPlane(Vector2 center, Vector2 normal)
    {
        _clipped.Clear();
        for (int i = 0; i < _polygon.Count; i++)
        {
            Vector2 current = _polygon[i];
            Vector2 next = _polygon[(i + 1) % _polygon.Count];
            float currentSide = Vector2.Dot(current - center, normal);
            float nextSide = Vector2.Dot(next - center, normal);
            if (currentSide >= 0f) _clipped.Add(current);
            if ((currentSide >= 0f) != (nextSide >= 0f))
                _clipped.Add(Vector2.Lerp(current, next, currentSide / (currentSide - nextSide)));
        }
    }
}
