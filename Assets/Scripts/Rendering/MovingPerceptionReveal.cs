using UnityEngine;

/// <summary>
/// 활성 상태인 동안 매 프레임 자신을 중심으로 한 구형 영역의 실루엣을 가장자리로 갈수록 흐리게 강조한다.
/// 일정 간격으로 지나간 위치를 짧은 잔상으로 남긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class MovingPerceptionReveal : MonoBehaviour
{
    [Header("주변 공개")]
    [SerializeField, Min(0.01f), Tooltip("자신을 중심으로 실루엣을 강조하는 구 반경(미터). 중심에서 가장자리로 갈수록 흐려진다.")]
    private float _radius = 2.5f;
    [SerializeField, Min(0f), Tooltip("지나간 자리의 구형 강조가 남는 잔상 시간(초). 0이면 잔상을 남기지 않는다.")]
    private float _afterimage = 0.3f;
    [SerializeField, Min(0.01f), Tooltip("잔상 표본을 남기는 간격(초).")]
    private float _trailInterval = 0.05f;
    private float _nextTrailTime;
    private PerceptionRevealMap _revealMap;

    void OnEnable()
    {
        _nextTrailTime = 0f;
    }

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
        bool recordTrail = Time.time >= _nextTrailTime;
        if (recordTrail)
            _nextTrailTime = Time.time + _trailInterval;
        _revealMap.RevealAround(transform.position, _radius, _afterimage, recordTrail);
    }
}
