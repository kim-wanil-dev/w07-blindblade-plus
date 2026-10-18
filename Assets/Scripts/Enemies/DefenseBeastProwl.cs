using UnityEngine;

/// <summary>
/// 일반 Beast가 공격 범위 안에서 대상 주변을 달리며 원을 그리다가 거리를 벌린 뒤 다음 물기 돌진을 허용한다.
/// EnemyController가 공격 중이 아닐 때 UpdateEngagement를 호출하며, 공격이 끝나면 새 원 그리기부터 다시 시작한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMotor), typeof(DefenseBeastLeapAttack))]
public sealed class DefenseBeastProwl : MonoBehaviour, IEnemyEngagementMovement
{
    private enum ProwlPhase
    {
        Circling,
        Retreating,
        Ready
    }

    [Header("원 그리기")]
    [SerializeField, Min(0.5f), Tooltip("대상 주변을 도는 원의 최소 반지름(m)")]
    private float _orbitRadiusMin = 4.5f;
    [SerializeField, Min(0.5f), Tooltip("대상 주변을 도는 원의 최대 반지름(m). 원마다 최소와 최대 사이에서 고른다.")]
    private float _orbitRadiusMax = 6.5f;
    [SerializeField, Range(5f, 90f), Tooltip("원 위에서 현재 위치보다 앞서 목표로 삼는 각도. 클수록 빠르게 돈다.")]
    private float _orbitLeadAngle = 40f;
    [SerializeField, Min(0.1f), Tooltip("한 번 원을 그리는 최소 시간(초)")]
    private float _circleDurationMin = 1.6f;
    [SerializeField, Min(0.1f), Tooltip("한 번 원을 그리는 최대 시간(초)")]
    private float _circleDurationMax = 3f;
    [SerializeField, Min(0.02f)] private float _repathInterval = 0.1f;
    private EnemyMotor _motor;
    private DefenseBeastLeapAttack _attack;
    private Transform _target;
    private ProwlPhase _phase;
    private float _orbitRadius;
    private float _orbitSign = 1f;
    private float _phaseEndsAt;
    private float _nextRepathAt;
    private float _lastUpdateAt;

    [Header("거리 벌리기")]
    [SerializeField, Min(0.5f), Tooltip("물기 돌진 전에 물러나는 대상과의 거리(m)")]
    private float _retreatRadius = 8f;
    [SerializeField, Range(0f, 90f), Tooltip("물러나면서도 원을 따라 앞서 나가는 각도")]
    private float _retreatLeadAngle = 20f;
    [SerializeField, Min(0.1f), Tooltip("물러나기에 쓰는 최대 시간(초). 넘으면 그 자리에서 돌진을 준비한다.")]
    private float _retreatTimeout = 1.2f;
    [SerializeField, Min(0.1f), Tooltip("돌진할 수 있게 된 뒤에도 공격이 시작되지 않으면 원 그리기를 다시 시작하는 시간(초)")]
    private float _readyTimeout = 2f;

    [Header("교전 재시작")]
    [SerializeField, Min(0.05f), Tooltip("이 시간보다 오래 호출이 끊기면 공격이나 추적이 끼어든 것으로 보고 원 그리기부터 다시 시작한다(초)")]
    private float _resetGap = 0.25f;

    // 다른 적에 돌진 경로가 막혀 있으면 원을 계속 돌며 길이 열리기를 기다린다.
    public bool IsReadyToAttack => _phase == ProwlPhase.Ready && _attack.HasClearLunge(_target);

    void Awake()
    {
        _motor = GetComponent<EnemyMotor>();
        _attack = GetComponent<DefenseBeastLeapAttack>();
    }

    void OnEnable()
    {
        _lastUpdateAt = float.NegativeInfinity;
        _phase = ProwlPhase.Circling;
    }

    /// <summary>
    /// target 주변의 원 그리기·물러나기·돌진 대기를 한 프레임 진행한다.
    /// 호출이 _resetGap보다 오래 끊겼으면 새 원을 고르고, 단계 시간이 끝나면 다음 단계로 _phase를 넘긴다.
    /// </summary>
    public void UpdateEngagement(Transform target)
    {
        _target = target;
        if (target == null)
        {
            _motor.Stop();
            return;
        }

        if (Time.time - _lastUpdateAt > _resetGap)
            BeginCycle();
        _lastUpdateAt = Time.time;

        switch (_phase)
        {
            case ProwlPhase.Circling:
                if (Time.time >= _phaseEndsAt)
                {
                    _phase = ProwlPhase.Retreating;
                    _phaseEndsAt = Time.time + _retreatTimeout;
                    _nextRepathAt = 0f;
                }
                MoveAround(target.position, _orbitRadius, _orbitLeadAngle);
                break;
            case ProwlPhase.Retreating:
                float distance = Vector3.ProjectOnPlane(transform.position - target.position, Vector3.up).magnitude;
                if (distance >= _retreatRadius - 0.5f || Time.time >= _phaseEndsAt)
                {
                    _phase = ProwlPhase.Ready;
                    _phaseEndsAt = Time.time + _readyTimeout;
                }
                MoveAround(target.position, _retreatRadius, _retreatLeadAngle);
                break;
            default:
                // 공격이 시작되지 못하는 위치라면 오래 서 있지 않고 다시 원을 그린다.
                if (Time.time >= _phaseEndsAt)
                    BeginCycle();
                MoveAround(target.position, _retreatRadius, _orbitLeadAngle);
                break;
        }
    }

    /// <summary>
    /// 새 원 그리기를 시작한다. 반지름 _orbitRadius, 도는 방향 _orbitSign, 원을 그리는 시간을 무작위로 정하고 _phase를 Circling으로 바꾼다.
    /// </summary>
    private void BeginCycle()
    {
        _phase = ProwlPhase.Circling;
        _orbitRadius = Random.Range(_orbitRadiusMin, Mathf.Max(_orbitRadiusMin, _orbitRadiusMax));
        _orbitSign = Random.value < 0.5f ? -1f : 1f;
        _phaseEndsAt = Time.time + Random.Range(_circleDurationMin, Mathf.Max(_circleDurationMin, _circleDurationMax));
        _nextRepathAt = 0f;
    }

    /// <summary>
    /// center를 중심으로 radius 원 위에서 현재 방향보다 leadAngle만큼 앞선 지점으로 EnemyMotor 경로를 _repathInterval마다 갱신한다.
    /// 그 지점이 NavMesh 밖이면 도는 방향 _orbitSign을 뒤집어 반대쪽으로 돈다.
    /// </summary>
    private void MoveAround(Vector3 center, float radius, float leadAngle)
    {
        if (Time.time < _nextRepathAt)
            return;
        _nextRepathAt = Time.time + _repathInterval;

        Vector3 offset = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        Vector3 direction = offset.sqrMagnitude > 0.0001f ? offset.normalized : -transform.forward;
        if (_motor.MoveTo(GetOrbitPoint(center, direction, radius, leadAngle), 0.1f))
            return;

        _orbitSign = -_orbitSign;
        _motor.MoveTo(GetOrbitPoint(center, direction, radius, leadAngle), 0.1f);
    }

    /// <summary>center에서 direction을 _orbitSign 방향으로 leadAngle만큼 돌린 radius 거리의 원 위 지점을 반환한다.</summary>
    private Vector3 GetOrbitPoint(Vector3 center, Vector3 direction, float radius, float leadAngle)
    {
        return center + Quaternion.AngleAxis(_orbitSign * leadAngle, Vector3.up) * direction * radius;
    }
}
