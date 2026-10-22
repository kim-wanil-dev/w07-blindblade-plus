using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyMotor : MonoBehaviour
{
    [Header("이동 설정")]
    [SerializeField, Min(0f)] private float _moveSpeed = 3.5f;
    [SerializeField, Min(0.01f)] private float _acceleration = 8f;
    [SerializeField, Min(0f)] private float _angularSpeed = 360f;
    [SerializeField, Min(0f)] private float _stoppingDistance = 1.5f;
    [SerializeField, Min(0.01f)] private float _destinationSampleRadius = 2f;
    private NavMeshAgent _agent;
    private Vector3 _destination;
    private bool _hasDestination;
    private int _facingLockFrame = -1;
    private bool _rotationBeforeFacingLock;

    [Header("피격 이동 상태")]
    private Vector3 _pushDirection;
    private float _pushDistance;
    private float _pushDuration;
    private float _pushElapsed;
    private float _pushProgress;
    private bool _isPushed;

    public bool IsReady => _agent.isActiveAndEnabled && _agent.isOnNavMesh;
    public bool HasArrived => IsReady && _hasDestination && !_agent.pathPending &&
        (IsWithinStoppingDistance(_destination) ||
            (_agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete &&
                _agent.remainingDistance <= _agent.stoppingDistance + 0.1f));

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = _moveSpeed;
        _agent.acceleration = _acceleration;
        _agent.angularSpeed = _angularSpeed;
        _agent.stoppingDistance = _stoppingDistance;
        _agent.autoRepath = true;
    }

    void LateUpdate()
    {
        // 대상 주시 이동 요청이 끊긴 다음 프레임부터 이동 방향 회전을 되돌린다.
        if (_facingLockFrame >= 0 && _facingLockFrame != Time.frameCount)
            RestoreMovementRotation();
        if (!_isPushed || Time.deltaTime <= 0f) return;
        if (!IsReady) { _isPushed = false; return; }
        _pushElapsed += Time.deltaTime;
        float normalized = Mathf.Clamp01(_pushElapsed / _pushDuration);
        float progress = 1f - (1f - normalized) * (1f - normalized);
        Vector3 displacement = _pushDirection * (_pushDistance * (progress - _pushProgress));
        if (_agent.Raycast(transform.position + displacement, out NavMeshHit hit))
        {
            displacement = hit.position - transform.position;
            _isPushed = false;
        }
        _agent.Move(displacement);
        _pushProgress = progress;
        if (normalized >= 1f) _isPushed = false;
    }

    /// <summary>
    /// position의 수평 방향으로 설정된 각속도만큼 회전한다.
    /// 이동 경로를 유지하고 대상이 같은 위치이면 현재 회전을 유지한다.
    /// </summary>
    public void FaceTarget(Vector3 position)
    {
        Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(direction), _angularSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 이번 프레임 동안 NavMeshAgent의 이동 방향 회전을 끄고 position의 수평 방향으로 설정된 각속도만큼 회전한다.
    /// 이동 경로는 유지하므로 옆이나 뒤로 이동하면서 대상을 바라본다. 호출이 끊기면 LateUpdate에서 원래 회전 설정을 복원한다.
    /// </summary>
    public void FaceTargetWhileMoving(Vector3 position)
    {
        if (_facingLockFrame < 0)
            _rotationBeforeFacingLock = _agent.updateRotation;
        _facingLockFrame = Time.frameCount;
        _agent.updateRotation = false;
        FaceTarget(position);
    }

    /// <summary>FaceTargetWhileMoving 전에 저장한 _rotationBeforeFacingLock으로 NavMeshAgent 회전 설정을 되돌리고 _facingLockFrame을 초기화한다.</summary>
    private void RestoreMovementRotation()
    {
        _agent.updateRotation = _rotationBeforeFacingLock;
        _facingLockFrame = -1;
    }

    void OnDisable()
    {
        _isPushed = false;
        if (_facingLockFrame >= 0)
            RestoreMovementRotation();
    }

    /// <summary>direction·distance·duration으로 수평 피격 밀림을 예약한다. NavMesh 위에서만 이동하며 중복 밀림은 새 요청으로 교체한다.</summary>
    public void ApplyHitPush(Vector3 direction, float distance, float duration)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (!IsReady || distance <= 0f || duration <= 0f || horizontal.sqrMagnitude < 0.001f) return;
        _pushDirection = horizontal;
        _pushDistance = distance;
        _pushDuration = duration;
        _pushElapsed = 0f;
        _pushProgress = 0f;
        _isPushed = true;
    }

    /// <summary>
    /// destination 주변의 같은 에이전트 유형과 허용 영역에 있는 NavMesh 위치로 이동한다.
    /// stoppingDistance가 있으면 해당 거리로 정지하며 없으면 Inspector 설정을 사용한다.
    /// 경로 요청을 수락하면 true를 반환하고 이동을 재개하며, 실패하면 경로를 제거한다.
    /// </summary>
    public bool MoveTo(Vector3 destination, float? stoppingDistance = null)
    {
        if (!IsReady)
            return false;

        _agent.stoppingDistance = stoppingDistance.HasValue
            ? Mathf.Max(0f, stoppingDistance.Value)
            : _stoppingDistance;

        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };

        if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, _destinationSampleRadius, filter))
        {
            Stop();
            return false;
        }

        _destination = hit.position;
        _hasDestination = true;
        if (IsWithinStoppingDistance(_destination))
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            return true;
        }

        _agent.isStopped = false;
        if (_agent.SetDestination(hit.position))
            return true;

        Stop();
        return false;
    }

    /// <summary>
    /// 현재 NavMeshAgent의 경로와 속도를 초기화하여 이동을 중지한다.
    /// Inspector 입력을 사용하지 않으며 에이전트가 NavMesh 위에 있을 때만 상태를 변경한다.
    /// </summary>
    public void Stop()
    {
        _hasDestination = false;
        if (!IsReady)
            return;

        _agent.ResetPath();
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
    }

    /// <summary>
    /// destination과 현재 위치의 거리를 에이전트 정지 거리 및 도착 허용 오차와 비교한다.
    /// 거리 안에 있으면 true를 반환하여 반복 경로 요청으로 대상에 과도하게 접근하지 않게 한다.
    /// </summary>
    private bool IsWithinStoppingDistance(Vector3 destination)
    {
        float distance = _agent.stoppingDistance + 0.1f;
        return (destination - transform.position).sqrMagnitude <= distance * distance;
    }
}
