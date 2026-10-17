using UnityEngine;

/// <summary>
/// Keeps the Beast's skinned smoke emission source updated while preventing the source mesh from rendering.
/// The source renderer drives particle emission and its bones follow the visible Beast body.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseBeastSmokeShapeSource : MonoBehaviour
{
    [Header("Shape Source")]
    [SerializeField] private SkinnedMeshRenderer _sourceRenderer;

    void OnEnable()
    {
        _sourceRenderer.updateWhenOffscreen = true;
        _sourceRenderer.forceRenderingOff = true;
    }
}
