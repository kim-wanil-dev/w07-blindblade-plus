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

    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _maskUseAlphaId = Shader.PropertyToID("_MaskUseAlpha");

    [Header("윤곽선 분류")]
    [SerializeField, Tooltip("Scenery는 항상 보이는 회색 윤곽선, Threat는 빨간 실루엣으로 표시한다.")]
    private OutlineLayerKind _kind = OutlineLayerKind.Scenery;
    private MaterialPropertyBlock _maskProperties;

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
    /// _kind로 사용할 비트를 선택하며 렌더러의 다른 렌더링 레이어 비트는 유지한다. 파티클 렌더러에는 마스크 모양 입력도 넘긴다.
    /// </summary>
    private void ApplyMask(bool enabled)
    {
        uint mask = _kind == OutlineLayerKind.Scenery ? SCENERY_MASK : THREAT_MASK;
        foreach (Renderer target in GetComponentsInChildren<Renderer>(true))
        {
            target.renderingLayerMask = enabled ? target.renderingLayerMask | mask : target.renderingLayerMask & ~mask;
            if (enabled && target is ParticleSystemRenderer)
                PublishAlphaShape(target);
        }
    }

    /// <summary>
    /// target 머티리얼의 텍스처·기본색과 알파 모양 사용 표시를 속성 블록으로 넘긴다.
    /// 마스크가 파티클을 사각형 대신 선택 VFX와 같은 연기 모양으로 기록하며 기존 속성 블록 값은 보존한다.
    /// </summary>
    private void PublishAlphaShape(Renderer target)
    {
        Material source = target.sharedMaterial;
        if (source == null || !source.HasProperty(_baseMapId) || !source.HasProperty(_baseColorId))
            return;

        if (_maskProperties == null)
            _maskProperties = new MaterialPropertyBlock();
        target.GetPropertyBlock(_maskProperties);
        _maskProperties.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
        _maskProperties.SetColor(_baseColorId, source.GetColor(_baseColorId));
        _maskProperties.SetFloat(_maskUseAlphaId, 1f);
        target.SetPropertyBlock(_maskProperties);
    }
}
