using UnityEngine;

/// <summary>Play 중 게임 화면에서 숨긴 도형 몸체를 Scene 뷰에서만 기즈모로 다시 그린다.</summary>
public static class SceneShapeGizmo
{
    private static readonly Color SHAPE_COLOR = new Color(0.75f, 0.75f, 0.75f, 1f);

    /// <summary>
    /// Play 중일 때 renderers 각각의 메시를 현재 위치·회전·크기로 Scene 뷰 기즈모에 그린다.
    /// 편집 중에는 렌더러가 직접 보이므로 그리지 않으며 Gizmos 색상만 잠시 바꿨다가 되돌린다.
    /// </summary>
    public static void Draw(Renderer[] renderers)
    {
        if (!Application.isPlaying)
            return;

        Color previous = Gizmos.color;
        Gizmos.color = SHAPE_COLOR;
        foreach (Renderer body in renderers)
        {
            if (!body.TryGetComponent(out MeshFilter filter))
                continue;
            Transform shape = body.transform;
            Gizmos.DrawMesh(filter.sharedMesh, shape.position, shape.rotation, shape.lossyScale);
        }
        Gizmos.color = previous;
    }
}
