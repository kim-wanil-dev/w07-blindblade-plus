using UnityEngine;

/// <summary>
/// 불릿 타임이나 극한 회피 파동으로 위협 실루엣이 강조되는 동안 수리검 연기 렌더러를 숨긴다.
/// 연기 입자는 계속 시뮬레이션해 강조가 끝나면 바로 다시 보인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystemRenderer))]
public sealed class ShurikenSmokeVisibility : MonoBehaviour
{
    [Header("상태")]
    private ParticleSystemRenderer _renderer;

    void Awake()
    {
        _renderer = GetComponent<ParticleSystemRenderer>();
    }

    void LateUpdate()
    {
        // 강조 중에는 연기 없이 수리검만 또렷한 실루엣으로 보이게 한다.
        _renderer.enabled = PlayerPerfectPulse.ThreatSilhouetteWeight <= 0f;
    }
}
