using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemySensor : MonoBehaviour
{
    private const int SIGHT_HIT_BUFFER_SIZE = 16;

    [Header("감지 설정")]
    [SerializeField, Tooltip("게임 시작 시 Game.Player를 사용합니다. 독립 씬에서는 이 참조를 사용합니다.")]
    private Transform _target;
    [SerializeField, Min(0f)] private float _detectionRange = 12f;
    [SerializeField] private Vector3 _eyeOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private Vector3 _targetOffset = new Vector3(0f, 1f, 0f);
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0f), Tooltip("시야 차폐 검사 결과를 재사용하는 시간(초)입니다. 0이면 같은 프레임 안에서만 재사용합니다.")]
    private float _sightCheckInterval = 0.1f;

    [Header("소리 감지")]
    [SerializeField, Min(0f), Tooltip("시야 탐지와 독립적인 검 소음 감지 거리입니다. NavMesh 경로 길이를 사용하며 0이면 소음에 반응하지 않습니다.")]
    private float _hearingDistance = 20f;
    [SerializeField] private bool _showHearingRange = true;

    [Header("런타임 상태")]
    private readonly RaycastHit[] _sightHits = new RaycastHit[SIGHT_HIT_BUFFER_SIZE];
    private Transform _sightCheckTarget;
    private int _sightCheckFrame = -1;
    private float _nextSightCheckTime;
    private bool _isSightClear;

    public Transform Target => _target;
    public float DetectionRange => _detectionRange;
    public float HearingDistance => _hearingDistance;

    void OnEnable()
    {
        // 풀에서 다시 나온 적이 이전 생애의 시야 결과를 쓰지 않게 한다.
        _sightCheckTarget = null;
    }

    void Start()
    {
        if (Game.Player != null)
            _target = Game.Player.transform;
    }

    /// <summary>
    /// target을 감지 대상으로 지정하여 이후 거리와 시야 검사에 사용할 대상을 변경한다.
    /// </summary>
    public void SetTarget(Transform target)
    {
        _target = target;
    }

    /// <summary>
    /// 지정한 대상과 감지 거리, 시야 위치, 장애물 레이어를 사용하여 가시성을 검사한다.
    /// 거리는 매번 확인하고 차폐 결과는 같은 대상에 한해 같은 프레임 또는 _sightCheckInterval 동안 재사용한다.
    /// 활성 대상이 범위 안에 있고 시야가 열려 있으면 true를 반환한다.
    /// </summary>
    public bool CanSeeTarget()
    {
        if (_target == null || !_target.gameObject.activeInHierarchy)
            return false;

        if ((_target.position - transform.position).sqrMagnitude > _detectionRange * _detectionRange)
            return false;

        if (_sightCheckTarget == _target && (_sightCheckFrame == Time.frameCount || Time.time < _nextSightCheckTime))
            return _isSightClear;

        _sightCheckTarget = _target;
        _sightCheckFrame = Time.frameCount;
        _nextSightCheckTime = Time.time + _sightCheckInterval;
        _isSightClear = IsSightClear();
        return _isSightClear;
    }

    /// <summary>
    /// _eyeOffset 위치에서 _target의 _targetOffset 위치까지 _obstacleMask 충돌체를 _sightHits 버퍼로 검사한다.
    /// 자신과 대상 하위 충돌체는 제외하며 가리는 충돌체가 없으면 true를 반환한다.
    /// </summary>
    private bool IsSightClear()
    {
        Vector3 origin = transform.TransformPoint(_eyeOffset);
        Vector3 destination = _target.TransformPoint(_targetOffset);
        int hitCount = Physics.RaycastNonAlloc(origin, destination - origin, _sightHits,
            Vector3.Distance(origin, destination), _obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = _sightHits[i].transform;
            if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(_target))
                continue;

            return false;
        }

        return true;
    }

    void OnDrawGizmosSelected()
    {
        Color previous = Gizmos.color;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _detectionRange);
        if (_showHearingRange)
        {
            float range = _hearingDistance;
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                GameSceneInitializer settings = root.GetComponentInChildren<GameSceneInitializer>();
                if (settings == null) continue;
                range = settings.SwordNoiseEnabled ? Mathf.Min(range, settings.SwordHearingDistance) : 0f;
                break;
            }
            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, range);
#if UNITY_EDITOR
            Color previousHandleColor = UnityEditor.Handles.color;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, range);
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                $"소리 감지: {range:0.##}m (NavMesh 경로 거리)");
            UnityEditor.Handles.color = previousHandleColor;
#endif
        }
        if (_target != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.TransformPoint(_eyeOffset), _target.TransformPoint(_targetOffset));
        }
        Gizmos.color = previous;
    }
}
