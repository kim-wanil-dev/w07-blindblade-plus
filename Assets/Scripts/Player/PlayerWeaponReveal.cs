using UnityEngine;

/// <summary>
/// 플레이어 검(칼과 칼집) 렌더러를 파동과 상관없이 항상 최대 세기 윤곽선으로 보이게 한다.
/// 렌더러에 OutlineLayer.FORCED_REVEAL_MASK 비트를 켜고, 머티리얼마다 _ForcedReveal 속성 블록에 세기 1·파동 무시 1을 넣는다.
/// 같은 속성 블록의 다른 값(검 색상 등)은 유지한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWeaponReveal : MonoBehaviour
{
    private static readonly int _forcedRevealId = Shader.PropertyToID("_ForcedReveal");

    [Header("검")]
    [SerializeField, Tooltip("검 모델의 Animator. 이 아래 모든 렌더러를 항상 보이게 한다.")]
    private Animator _weapon;
    [SerializeField, Range(0f, 1f), Tooltip("검 윤곽선 세기. 1이면 파동이 지나간 곳과 같은 밝기다.")]
    private float _strength = 1f;
    private Renderer[] _renderers;
    private MaterialPropertyBlock _block;

    void Awake()
    {
        _renderers = _weapon.GetComponentsInChildren<Renderer>(true);
        _block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        Apply(true);
    }

    void OnDisable()
    {
        Apply(false);
    }

    /// <summary>
    /// enabled이면 검 렌더러마다 강제 공개 비트를 켜고 머티리얼별 속성 블록에 _strength 세기와 파동 무시를 넣는다.
    /// 아니면 비트를 끄고 세기를 0으로 되돌린다. 머티리얼별 속성 블록의 다른 값은 먼저 읽어 그대로 둔다.
    /// </summary>
    private void Apply(bool enabled)
    {
        Vector4 reveal = enabled ? new Vector4(_strength, 1f, 0f, 0f) : Vector4.zero;
        foreach (Renderer renderer in _renderers)
        {
            renderer.renderingLayerMask = enabled
                ? renderer.renderingLayerMask | OutlineLayer.FORCED_REVEAL_MASK
                : renderer.renderingLayerMask & ~OutlineLayer.FORCED_REVEAL_MASK;
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                renderer.GetPropertyBlock(_block, i);
                _block.SetVector(_forcedRevealId, reveal);
                renderer.SetPropertyBlock(_block, i);
            }
        }
    }
}
