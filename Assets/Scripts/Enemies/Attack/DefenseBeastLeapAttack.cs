using UnityEngine;
using UnityEngine.AI;

using BlindBlade.Combat.Contracts;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySensor), typeof(EnemyMotor), typeof(EnemyHealth))]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(TwinBossWarningVisual))]
public sealed class DefenseBeastLeapAttack : EnemyAttackBehaviour
{
    private const int CONTACT_BUFFER_SIZE = 32;

    private enum LeapState
    {
        None,
        Jumping,
        Repositioning,
        Finished
    }

    [Header("도약 설정")]
    [SerializeField, Min(0.1f)] private float _attackRange = 6f;
    [SerializeField, Min(0.1f)] private float _jumpDuration = 0.55f;
    [SerializeField, Min(0.1f)] private float _jumpHeight = 2f;
    [SerializeField, Min(0.1f)] private float _landingRadius = 2f;
    [SerializeField, Min(0f)] private float _landingBeyondTargetDistance = 10f;
    [SerializeField, Min(0f)] private float _jumpDamage = 10f;

    [Header("착지 후 이동")]
    [SerializeField, Min(0f)] private float _oppositeDistance = 6f;
    [SerializeField, Min(0.1f)] private float _repositionSpeed = 7.2f;
    [SerializeField, Min(0.1f)] private float _repositionTimeout = 2f;
    [SerializeField, Min(0.05f)] private float _navMeshSampleRadius = 1f;

    [Header("경로와 충돌")]
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    [SerializeField] private LayerMask _landingMask = ~0;

    [Header("공격 소리")]
    [SerializeField] private SoundId _heartbeatSound = SoundId.None;
    [SerializeField] private SoundId _leapSound = SoundId.None;

    [Header("런타임 상태")]
    private EnemyMotor _motor;
    private EnemyHealth _health;
    private NavMeshAgent _agent;
    private TwinBossWarningVisual _warning;
    private NavMeshPath _path;
    private readonly Collider[] _landingContacts = new Collider[CONTACT_BUFFER_SIZE];
    private readonly RaycastHit[] _obstacleHits = new RaycastHit[CONTACT_BUFFER_SIZE];
    private LeapState _state;
    private Vector3 _startPosition;
    private Vector3 _navigationPosition;
    private Vector3 _landingPosition;
    private Vector3 _leapDirection;
    private Vector3 _incomingDirection;
    private Vector3 _repositionDestination;
    private float _jumpElapsed;
    private float _repositionStartedAt;
    private float _previousAgentSpeed;
    private float _previousStoppingDistance;
    private bool _previousUpdatePosition;
    private bool _previousUpdateRotation;
    private bool _previousAgentStopped;
    private bool _navigationCaptured;
    private bool _preparing;
    private bool _jumping;
    private bool _contactResolved;
    private bool _moveToOppositeSide;
    private bool _attackMotionComplete;

    public override float Range => _attackRange;
    public override bool AllowsMovement => _state == LeapState.Jumping || _state == LeapState.Repositioning;
    public bool IsPreparing => _preparing;
    public bool IsJumping => _jumping;
    public Vector3 IncomingDirection => _incomingDirection;
    internal override string TelemetryKind => "Melee";

    protected override float ActiveDuration => _jumpDuration + _repositionTimeout;
    protected override bool HasFinishedActiveAttack => _attackMotionComplete;
    protected override EnemySoundAction SoundAction => EnemySoundAction.Melee;

    protected override void Awake()
    {
        base.Awake();
        _motor = GetComponent<EnemyMotor>();
        _health = GetComponent<EnemyHealth>();
        _agent = GetComponent<NavMeshAgent>();
        _warning = GetComponent<TwinBossWarningVisual>();
        _path = new NavMeshPath();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        _state = LeapState.None;
        _preparing = false;
        _jumping = false;
        _contactResolved = false;
        _moveToOppositeSide = false;
        _attackMotionComplete = false;
        _navigationCaptured = false;
    }

    /// <summary>
    /// target의 생존·수평 사거리·NavMesh 경로와 직선 도약 장애물을 확인하여 공격 가능 여부를 반환한다.
    /// 유효한 도착 위치가 있을 때만 true를 반환한다.
    /// </summary>
    public override bool CanAttack(Transform target)
    {
        if (!isActiveAndEnabled || target == null || !target.gameObject.activeInHierarchy
            || !_health.IsAlive || !_motor.IsReady)
            return false;

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        if (horizontalOffset.sqrMagnitude > _attackRange * _attackRange)
            return false;

        Vector3 attackDirection = horizontalOffset.sqrMagnitude > 0.0001f
            ? horizontalOffset.normalized
            : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (attackDirection.sqrMagnitude < 0.0001f)
            attackDirection = Vector3.forward;
        Vector3 landingAim = GetPositionOnLeapAxis(transform.position, attackDirection, target.position);
        return TryFindLeapDestination(landingAim, target, out _);
    }

    /// <summary>
    /// context의 도약 방향과 시작 위치를 고정하고 웅크림 전조 상태를 켠다.
    /// 조준 위치 뒤 _landingBeyondTargetDistance만큼의 착지 후보를 저장하고 IncomingDirection으로 전방 방어를 유지한다.
    /// </summary>
    protected override void PrepareTelegraph(EnemyAttackContext context)
    {
        _preparing = true;
        _warning.BeginWarning(context.TelegraphDuration);
        _startPosition = transform.position;
        _leapDirection = Vector3.ProjectOnPlane(context.Direction, Vector3.up).normalized;
        if (_leapDirection.sqrMagnitude < 0.0001f)
            _leapDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (_leapDirection.sqrMagnitude < 0.0001f)
            _leapDirection = Vector3.forward;
        _incomingDirection = -_leapDirection;

        Vector3 targetAim = GetPositionOnLeapAxis(_startPosition, _leapDirection, context.AimPosition);
        _landingPosition = targetAim;
        Vector3 landingAim = GetLeapLandingAim(_startPosition, _leapDirection, context.AimPosition);
        if (TryFindLeapDestination(landingAim, context.Target, out Vector3 sampledLanding)
            || TryFindLeapDestination(targetAim, context.Target, out sampledLanding))
            _landingPosition = sampledLanding;
    }

    /// <summary>
    /// 준비 중인 도약 축으로 현재 target 뒤 10m의 착지점을 찾고 유효한 후보가 없으면 준비 때 저장한 착지점을 사용한다.
    /// NavMeshAgent의 이전 위치·회전·속도·정지 상태를 저장하고 포물선 도약을 시작한다.
    /// </summary>
    protected override void PerformAttack(EnemyAttackContext context)
    {
        _preparing = false;
        if (!_health.IsAlive || !_motor.IsReady)
        {
            _warning.EndWarning();
            CompleteAttackMotion();
            BeginRecovery();
            return;
        }

        _startPosition = transform.position;
        if (TryGetTrackedLandingPosition(context.Target.position, context.Target, out Vector3 trackedLanding))
            _landingPosition = trackedLanding;

        _navigationPosition = _agent.nextPosition;
        _previousUpdatePosition = _agent.updatePosition;
        _previousUpdateRotation = _agent.updateRotation;
        _previousAgentStopped = _agent.isStopped;
        _previousAgentSpeed = _agent.speed;
        _previousStoppingDistance = _agent.stoppingDistance;
        _navigationCaptured = true;
        _motor.Stop();
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _agent.isStopped = true;
        _incomingDirection = -_leapDirection;
        _jumpElapsed = 0f;
        _jumping = true;
        _contactResolved = false;
        _moveToOppositeSide = false;
        _attackMotionComplete = false;
        _state = LeapState.Jumping;
        transform.rotation = Quaternion.LookRotation(_leapDirection);
    }

    /// <summary>
    /// context의 대상 생존과 도약 상태를 확인하고 비행 또는 결과 이동을 한 프레임 진행한다.
    /// 대상이 사라지거나 공격자가 죽으면 현재 공격을 취소하여 NavMesh 상태를 복원한다.
    /// </summary>
    protected override void UpdateActiveAttack(EnemyAttackContext context)
    {
        PlayerHealth targetHealth = context.Target != null ? context.Target.GetComponentInParent<PlayerHealth>() : null;
        if (!_health.IsAlive || context.Target == null || !context.Target.gameObject.activeInHierarchy
            || targetHealth == null || (!targetHealth.IsAlive && !_contactResolved))
        {
            CancelAttack();
            return;
        }

        if (_state == LeapState.Jumping)
            UpdateLeap(context, targetHealth);
        else if (_state == LeapState.Repositioning)
            UpdateReposition();
    }

    /// <summary>
    /// context의 대상 뒤 10m를 고정한 수평 축으로 추적하며 비행 경로의 접촉을 한 번 판정한다.
    /// NavMesh 착지점과 장애물 검사를 통과한 위치로 이동하고 끝에 도달하면 복귀 이동을 시작한다.
    /// </summary>
    private void UpdateLeap(EnemyAttackContext context, PlayerHealth targetHealth)
    {
        if (TryGetTrackedLandingPosition(context.Target.position, context.Target, out Vector3 trackedLanding))
            _landingPosition = trackedLanding;

        _jumpElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_jumpElapsed / Mathf.Max(0.1f, _jumpDuration));
        Vector3 nextPosition = Vector3.Lerp(_startPosition, _landingPosition, progress)
            + Vector3.up * (4f * _jumpHeight * progress * (1f - progress));
        Vector3 previousPosition = transform.position;
        Vector3 movement = nextPosition - previousPosition;
        if (movement.sqrMagnitude > 0.000001f
            && !IsClearSegment(previousPosition, nextPosition, context.Target))
        {
            CancelAttack();
            return;
        }

        TryResolveLeapContact(previousPosition, nextPosition, context, targetHealth);
        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(_leapDirection));
        if (progress < 1f)
            return;

        transform.position = _landingPosition;
        if (!RestoreAgentAt(_landingPosition, false))
        {
            CancelAttack();
            return;
        }

        _jumping = false;
        ResolveLanding(context, targetHealth);
    }

    /// <summary>
    /// 비행 경로에서 놓친 접촉을 착지점에서 확인하고 판정 결과에 따라 반대편 또는 시작 위치를 목적지로 설정한다.
    /// 피해·DodgeAvoided·빗나감은 도약축 뒤쪽으로, Guard·쳐내기·Invulnerable은 시작 위치로 이동한다.
    /// </summary>
    private void ResolveLanding(EnemyAttackContext context, PlayerHealth targetHealth)
    {
        TryResolveLeapContact(_landingPosition, _landingPosition, context, targetHealth);
        if (!_contactResolved)
        {
            _contactResolved = true;
            _moveToOppositeSide = true;
        }

        Vector3 desiredDestination = _moveToOppositeSide
            ? GetPostLeapDestination(targetHealth.transform.position)
            : _startPosition;
        _warning.EndWarning();
        BeginReposition(desiredDestination, targetHealth.transform);
    }

    /// <summary>
    /// targetPosition 반대편의 _oppositeDistance 목적지를 만들어 반환한다.
    /// 도약 시작 위치에서 targetPosition으로 향한 수평 방향을 사용한다.
    /// </summary>
    private Vector3 GetOppositeDestination(Vector3 targetPosition)
    {
        Vector3 direction = Vector3.ProjectOnPlane(targetPosition - _startPosition, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.0001f)
            direction = _leapDirection;
        return targetPosition + direction * _oppositeDistance;
    }

    /// <summary>
    /// target 뒤쪽 목적지와 현재 착지점의 도약축 거리를 비교해 이미 충분히 추월했다면 착지점을 반환한다.
    /// 아직 _oppositeDistance만큼 지나지 못했으면 기존 반대편 목적지를 반환한다.
    /// </summary>
    private Vector3 GetPostLeapDestination(Vector3 targetPosition)
    {
        float passedTargetDistance = Vector3.Dot(
            Vector3.ProjectOnPlane(_landingPosition - targetPosition, Vector3.up), _leapDirection);
        return passedTargetDistance >= _oppositeDistance
            ? _landingPosition
            : GetOppositeDestination(targetPosition);
    }

    /// <summary>
    /// from과 to 사이에 targetHealth의 충돌체가 있으면 공격 방어 계약을 한 번 호출한다.
    /// _landingRadius, _landingMask, context의 공격 식별값을 사용하고 판정 결과를 런타임 상태에 저장한다.
    /// </summary>
    private void TryResolveLeapContact(
        Vector3 from,
        Vector3 to,
        EnemyAttackContext context,
        PlayerHealth targetHealth)
    {
        if (_contactResolved || !targetHealth.IsAlive)
            return;

        Physics.SyncTransforms();
        float verticalOffset = Mathf.Max(0.1f, _agent.height * 0.5f);
        Vector3 start = from + Vector3.up * verticalOffset;
        Vector3 end = to + Vector3.up * verticalOffset;
        int contactCount = Physics.OverlapCapsuleNonAlloc(start, end, _landingRadius, _landingContacts,
            _landingMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < contactCount; i++)
        {
            Collider contact = _landingContacts[i];
            if (contact == null || contact.GetComponentInParent<PlayerHealth>() != targetHealth)
                continue;

            ResolveTargetContact(context, targetHealth, contact.ClosestPoint(to));
            return;
        }
    }

    /// <summary>
    /// 접촉 위치에서 DamageContext를 만들고 PlayerDefenseResolver의 결과를 도약 후 이동 상태에 반영한다.
    /// 방어 성공은 시작 위치 복귀로, 피격·회피 성공은 반대편 이동으로 처리한다.
    /// </summary>
    private void ResolveTargetContact(EnemyAttackContext context, PlayerHealth targetHealth, Vector3 contactPoint)
    {
        _contactResolved = true;
        DamageContext damage = new DamageContext(_jumpDamage, gameObject, contactPoint, _leapDirection,
            PatternId, context.AttackId, 1, IncomingAttackKind.Melee);
        if (targetHealth is IPlayerDefenseResolver resolver)
        {
            DefenseResolution resolution = resolver.ResolveIncomingAttack(damage);
            _moveToOppositeSide = resolution.Outcome == DefenseOutcome.DodgeAvoided
                || resolution.Outcome == DefenseOutcome.Hit && resolution.AppliedDamage > 0f;
            return;
        }

        targetHealth.TakeDamage(damage);
        _moveToOppositeSide = true;
    }

    /// <summary>
    /// desiredDestination 주변의 완전한 NavMesh 경로와 장애물 없는 경로 구간을 확인해 결과 이동을 시작한다.
    /// 목적지가 무효하거나 이동 요청이 거부되면 현재 안전 위치에서 공격을 마친다.
    /// </summary>
    private void BeginReposition(Vector3 desiredDestination, Transform ignoredTarget)
    {
        _state = LeapState.Repositioning;
        _agent.speed = _repositionSpeed;
        if (!TryFindRepositionDestination(desiredDestination, ignoredTarget, out _repositionDestination)
            || !_motor.MoveTo(_repositionDestination, 0.1f))
        {
            CompleteAttackMotion();
            return;
        }

        _repositionStartedAt = Time.time;
        if (_motor.HasArrived)
            CompleteAttackMotion();
    }

    /// <summary>
    /// EnemyMotor의 NavMesh 도착 상태와 _repositionStartedAt을 사용하여 이동 완료 또는 제한 시간 초과를 처리한다.
    /// 완료 시 경로를 정지해 EnemyController가 후딜 중 이동을 덮어쓰지 않게 한다.
    /// </summary>
    private void UpdateReposition()
    {
        if (!_motor.IsReady || _motor.HasArrived || Time.time >= _repositionStartedAt + _repositionTimeout)
            CompleteAttackMotion();
    }

    /// <summary>
    /// context의 전조·비행·이동이 끝났음을 표시하고 EnemyMotor 경로를 중지한다.
    /// 기본 EnemyAttackBehaviour가 다음 Update에서 후딜로 전환할 수 있게 한다.
    /// </summary>
    private void CompleteAttackMotion()
    {
        _preparing = false;
        _jumping = false;
        _state = LeapState.Finished;
        _attackMotionComplete = true;
        if (_motor != null && _motor.IsReady)
            _motor.Stop();
    }

    /// <summary>
    /// cancelled 여부와 _navigationCaptured를 사용하여 이동 경로·속도·회전 갱신·NavMesh 위치를 복원한다.
    /// 접촉 전 취소는 도약 시작 위치로 Warp하고 접촉 후 취소는 마지막 비행 위치를 유지한다.
    /// </summary>
    protected override void OnAttackEnded(EnemyAttackContext context, bool cancelled)
    {
        _warning.EndWarning();
        _preparing = false;
        bool wasJumping = _jumping;
        _jumping = false;
        _state = LeapState.None;
        _attackMotionComplete = false;
        if (!_navigationCaptured)
            return;

        Vector3 restorePosition = cancelled && wasJumping && !_contactResolved ? _startPosition : transform.position;
        if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
        {
            _motor.Stop();
            _agent.updatePosition = true;
            _agent.updateRotation = _previousUpdateRotation;
            if (!TrySampleNavMeshPosition(restorePosition, out Vector3 sampledPosition))
                sampledPosition = _navigationPosition;
            _agent.Warp(sampledPosition);
        }
        else
        {
            transform.position = restorePosition;
        }

        _agent.updatePosition = _previousUpdatePosition;
        _agent.updateRotation = _previousUpdateRotation;
        _agent.speed = _previousAgentSpeed;
        _agent.stoppingDistance = _previousStoppingDistance;
        _agent.isStopped = _previousAgentStopped;
        _navigationCaptured = false;
    }

    /// <summary>
    /// desiredPosition을 같은 NavMesh에 투영하고 agent를 착지 위치로 Warp한다.
    /// fallbackToNavigationPosition이 true이면 착지점 샘플 실패 시 저장한 시작 NavMesh 위치를 사용하고, 성공 여부를 반환한다.
    /// </summary>
    private bool RestoreAgentAt(Vector3 desiredPosition, bool fallbackToNavigationPosition)
    {
        if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            return false;

        desiredPosition.y = _navigationPosition.y;
        if (!TrySampleNavMeshPosition(desiredPosition, out Vector3 sampledPosition))
        {
            if (!fallbackToNavigationPosition
                || !TrySampleNavMeshPosition(_navigationPosition, out sampledPosition))
                return false;
        }

        _agent.updatePosition = true;
        _agent.updateRotation = _previousUpdateRotation;
        if (!_agent.Warp(sampledPosition))
            return false;

        transform.position = sampledPosition;
        _motor.Stop();
        return true;
    }

    /// <summary>
    /// stage가 Heartbeat 또는 Action이면 Inspector SoundId를 사용하고 나머지는 공통 근접 음향 설정을 반환한다.
    /// 설정된 ID가 None이면 기존 EnemySoundSettings의 소리를 유지한다.
    /// </summary>
    protected override SoundId ResolveActionSound(EnemySoundStage stage)
    {
        if (stage == EnemySoundStage.Heartbeat && _heartbeatSound != SoundId.None)
            return _heartbeatSound;
        if (stage == EnemySoundStage.Action && _leapSound != SoundId.None)
            return _leapSound;
        return base.ResolveActionSound(stage);
    }

    /// <summary>
    /// desired 위치를 agent 유형과 area mask로 샘플링하고 완전한 경로와 직선 도약 구간이 유효하면 destination을 반환한다.
    /// ignoredTarget의 충돌체는 도약 경로 장애물에서 제외한다.
    /// </summary>
    private bool TryFindLeapDestination(Vector3 desired, Transform ignoredTarget, out Vector3 destination)
    {
        destination = Vector3.zero;
        desired.y = _agent.nextPosition.y;
        if (!TrySampleNavMeshPosition(desired, out destination)
            || !_agent.CalculatePath(destination, _path)
            || _path.status != NavMeshPathStatus.PathComplete)
            return false;

        return IsClearSegment(transform.position, destination, ignoredTarget);
    }

    /// <summary>
    /// origin과 direction을 기준으로 targetPosition을 도약 축에 투영한다.
    /// 뒤로 이동한 거리도 반영하고 옆 방향 위치는 도약 목표에 포함하지 않는다.
    /// </summary>
    private Vector3 GetPositionOnLeapAxis(Vector3 origin, Vector3 direction, Vector3 targetPosition)
    {
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (horizontalDirection.sqrMagnitude < 0.0001f)
            return origin;

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(targetPosition - origin, Vector3.up);
        float distance = Mathf.Max(0f, Vector3.Dot(horizontalOffset, horizontalDirection));
        return origin + horizontalDirection * distance;
    }

    /// <summary>
    /// targetPosition의 도약축 투영점에서 _landingBeyondTargetDistance만큼 더 나아간 착지 조준점을 반환한다.
    /// 옆 방향 추적을 막기 위해 수평 도약 방향만 사용한다.
    /// </summary>
    private Vector3 GetLeapLandingAim(Vector3 origin, Vector3 direction, Vector3 targetPosition)
    {
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        return GetPositionOnLeapAxis(origin, horizontalDirection, targetPosition)
            + horizontalDirection * _landingBeyondTargetDistance;
    }

    /// <summary>
    /// targetPosition 뒤 _landingBeyondTargetDistance를 우선 목표로 삼고 유효하지 않으면 target 위치를 후보로 사용한다.
    /// NavMesh와 장애물 검사를 통과한 축 방향 착지 후보를 destination으로 반환한다.
    /// </summary>
    private bool TryGetTrackedLandingPosition(Vector3 targetPosition, Transform ignoredTarget, out Vector3 destination)
    {
        Vector3 beyondTarget = GetLeapLandingAim(_startPosition, _leapDirection, targetPosition);
        beyondTarget.y = _agent.nextPosition.y;
        if (TrySampleNavMeshPosition(beyondTarget, out destination)
            && IsClearSegment(transform.position, destination, ignoredTarget))
            return true;

        Vector3 targetAim = GetPositionOnLeapAxis(_startPosition, _leapDirection, targetPosition);
        targetAim.y = _agent.nextPosition.y;
        return TrySampleNavMeshPosition(targetAim, out destination)
            && IsClearSegment(transform.position, destination, ignoredTarget);
    }

    /// <summary>
    /// desired 주변에서 도달 가능한 NavMesh 지점을 찾고 경로 전체가 장애물에 막히지 않으면 destination을 반환한다.
    /// NavMesh 유형·area mask와 _navMeshSampleRadius를 사용하며 불완전한 경로는 거부한다.
    /// </summary>
    private bool TryFindRepositionDestination(Vector3 desired, Transform ignoredTarget, out Vector3 destination)
    {
        destination = Vector3.zero;
        desired.y = _agent.nextPosition.y;
        if (!TrySampleNavMeshPosition(desired, out destination)
            || !_agent.CalculatePath(destination, _path)
            || _path.status != NavMeshPathStatus.PathComplete)
            return false;

        Vector3 previous = transform.position;
        foreach (Vector3 corner in _path.corners)
        {
            if (!IsClearSegment(previous, corner, ignoredTarget))
                return false;
            previous = corner;
        }
        return true;
    }

    /// <summary>
    /// desired 위치에서 agent 유형과 area mask가 같은 NavMesh 표면을 찾아 position을 반환한다.
    /// 샘플 범위는 _navMeshSampleRadius이며 실패하면 false를 반환한다.
    /// </summary>
    private bool TrySampleNavMeshPosition(Vector3 desired, out Vector3 position)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        bool found = NavMesh.SamplePosition(desired, out NavMeshHit hit, _navMeshSampleRadius, filter);
        position = hit.position;
        return found;
    }

    /// <summary>
    /// start부터 end까지 agent 캡슐을 이동해 _obstacleMask 장애물이 없으면 true를 반환한다.
    /// 자기 몸체와 ignoredTarget에 속한 충돌체는 검사에서 제외한다.
    /// </summary>
    private bool IsClearSegment(Vector3 start, Vector3 end, Transform ignoredTarget)
    {
        Vector3 delta = end - start;
        float distance = delta.magnitude;
        if (distance <= 0.001f)
            return true;

        float radius = _agent.radius;
        float height = Mathf.Max(_agent.height, radius * 2f);
        Vector3 bottom = start + Vector3.up * radius;
        Vector3 top = start + Vector3.up * Mathf.Max(radius, height - radius);
        int hitCount = Physics.CapsuleCastNonAlloc(bottom, top, radius, delta / distance, _obstacleHits,
            Mathf.Max(0f, distance - radius), _obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = _obstacleHits[i].transform;
            if (hitTransform.IsChildOf(transform)
                || ignoredTarget != null && hitTransform.IsChildOf(ignoredTarget)
                || _obstacleHits[i].collider.GetComponentInParent<PlayerHealth>() != null)
                continue;
            return false;
        }

        return true;
    }
}
