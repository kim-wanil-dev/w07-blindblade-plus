using UnityEngine;

/// <summary>
/// 활성 상태인 동안 매 프레임 자신의 주변을 시야 맵에 공개해 이동 경로를 따라 짧은 실루엣 잔상을 남긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MovingPerceptionReveal : MonoBehaviour
{
    [Header("주변 공개")]
    [SerializeField, Min(0.01f), Tooltip("주변 실루엣을 드러내는 수평 반경(미터).")]
    private float _radius = 2.5f;
    [SerializeField, Min(0.01f), Tooltip("지나간 자리의 실루엣이 남는 잔상 시간(초).")]
    private float _afterimage = 0.3f;
    private PerceptionRevealMap _revealMap;

    void LateUpdate()
    {
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null)
                return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
            if (_revealMap == null)
                return;
        }
        _revealMap.RevealAround(transform.position, _radius, _afterimage);
    }
}
