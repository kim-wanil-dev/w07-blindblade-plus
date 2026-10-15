using UnityEngine;

public enum OutlineLayerKind
{
    Scenery,
    Threat
}

/// <summary>
/// 이 오브젝트와 자식 렌더러를 인지와 무관하게 보이는 배경 또는 위협 실루엣 대상으로 표시한다.
/// 원본 머티리얼과 조명용 렌더링 비트는 변경하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Effects/Outline Layer")]
public sealed class OutlineLayer : MonoBehaviour
{
    public const uint SCENERY_MASK = 1u << 29;
    public const uint THREAT_MASK = 1u << 28;
    // 파동으로 밝혀진 정도에 따로 세기를 곱하는 대상. 세기는 렌더러의 _ForcedReveal 속성 블록으로 넘긴다.
    public const uint FORCED_REVEAL_MASK = 1u << 27;

    [Header("윤곽선 분류")]
    [SerializeField, Tooltip("Scenery는 항상 보이는 회색 윤곽선, Threat는 빨간 실루엣으로 표시한다.")]
    private OutlineLayerKind _kind = OutlineLayerKind.Scenery;

    void OnEnable()
    {
        ApplyMask(true);
    }

    void OnDisable()
    {
        ApplyMask(false);
    }

    /// <summary>
    /// enabled에 따라 자식을 포함한 모든 렌더러의 분류 비트를 추가하거나 제거한다.
    /// _kind로 사용할 비트를 선택하며 렌더러의 다른 렌더링 레이어 비트는 유지한다.
    /// </summary>
    private void ApplyMask(bool enabled)
    {
        uint mask = _kind == OutlineLayerKind.Scenery ? SCENERY_MASK : THREAT_MASK;
        foreach (Renderer target in GetComponentsInChildren<Renderer>(true))
            target.renderingLayerMask = enabled ? target.renderingLayerMask | mask : target.renderingLayerMask & ~mask;
    }
}
