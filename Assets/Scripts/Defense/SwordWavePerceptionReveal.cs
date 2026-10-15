using UnityEngine;

/// <summary>
/// 날아가는 검기가 소리를 내는 것처럼 수리검과 같은 방식으로 주변 구형 영역의 실루엣을 밝힌다.
/// 검기는 실시간으로 아주 빠르게 날아가므로 잔상은 시간 대신 날아간 거리마다 남긴다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordWavePerceptionReveal : MonoBehaviour
{
    [Header("주변 공개")]
    [SerializeField, Min(0.01f), Tooltip("크기 배율 1에서 검기 중심을 기준으로 실루엣을 밝히는 구 반경(미터). 비행 중 커지는 배율을 곱한다.")]
    private float _radius = 12f;
    [SerializeField, Min(0f), Tooltip("지나간 자리의 구형 공개가 남는 잔상 시간(초, 게임 시간).")]
    private float _afterimage = 1f;
    [SerializeField, Min(0.1f), Tooltip("잔상을 남기는 비행 거리 간격(미터). 구 공개는 수리검과 같은 32개 한도를 나눠 쓴다.")]
    private float _trailSpacing = 10f;
    private PerceptionRevealMap _revealMap;
    private Vector3 _lastTrailPosition;
    private bool _hasTrail;

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
        Vector3 position = transform.position;
        bool recordTrail = !_hasTrail
            || (position - _lastTrailPosition).sqrMagnitude >= _trailSpacing * _trailSpacing;
        if (recordTrail)
        {
            _lastTrailPosition = position;
            _hasTrail = true;
        }
        _revealMap.RevealAround(position, _radius * transform.localScale.x, _afterimage, recordTrail);
    }
}
