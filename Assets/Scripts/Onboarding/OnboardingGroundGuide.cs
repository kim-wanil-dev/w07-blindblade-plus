using UnityEngine;

/// <summary>온보딩에서만 지면의 먼 쪽 명암과 하늘에 닿는 윤곽을 화면 공간에 전달한다.</summary>
[DisallowMultipleComponent]
public sealed class OnboardingGroundGuide : MonoBehaviour
{
    private static readonly int _enabledId = Shader.PropertyToID("_OutlineGroundGuideEnabled");
    private static readonly int _groundHeightId = Shader.PropertyToID("_OutlineGroundGuideHeight");
    private static readonly int _heightToleranceId = Shader.PropertyToID("_OutlineGroundGuideHeightTolerance");
    private static readonly int _nearDistanceId = Shader.PropertyToID("_OutlineGroundGuideNearDistance");
    private static readonly int _farDistanceId = Shader.PropertyToID("_OutlineGroundGuideFarDistance");
    private static readonly int _fillColorId = Shader.PropertyToID("_OutlineGroundGuideFillColor");
    private static readonly int _edgeColorId = Shader.PropertyToID("_OutlineGroundGuideEdgeColor");

    [Header("지면 위치")]
    [SerializeField, Min(0.01f)] private float _heightTolerance = 0.2f;

    [Header("먼 지면 명암")]
    [SerializeField, Min(0f)] private float _nearDistance = 5f;
    [SerializeField, Min(0.01f)] private float _farDistance = 35f;
    [SerializeField] private Color _fillColor = new Color(0.09f, 0.09f, 0.09f, 1f);

    [Header("하늘과 맞닿는 선")]
    [SerializeField] private Color _edgeColor = new Color(0.28f, 0.28f, 0.28f, 1f);

    void OnEnable()
    {
        Publish();
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(_enabledId, 0f);
    }

    void OnValidate()
    {
        _farDistance = Mathf.Max(_nearDistance + 0.01f, _farDistance);
        if (Application.isPlaying && isActiveAndEnabled) Publish();
    }

    /// <summary>현재 지면의 높이와 Inspector의 거리·색상 설정을 셰이더 전역값으로 전달한다.</summary>
    private void Publish()
    {
        Shader.SetGlobalFloat(_groundHeightId, transform.position.y);
        Shader.SetGlobalFloat(_heightToleranceId, _heightTolerance);
        Shader.SetGlobalFloat(_nearDistanceId, _nearDistance);
        Shader.SetGlobalFloat(_farDistanceId, _farDistance);
        Shader.SetGlobalColor(_fillColorId, _fillColor);
        Shader.SetGlobalColor(_edgeColorId, _edgeColor);
        Shader.SetGlobalFloat(_enabledId, 1f);
    }
}
