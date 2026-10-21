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
    private const int LUNGE_PATH_SAMPLES = 16;

    private enum LeapState
    {
        None,
        Lunging,
        Biting,
        BouncingBack,
        Repositioning,
        Finished
    }

    [Header("물기 돌진 설정")]
    [SerializeField, Min(0.1f), Tooltip("이 수평 거리 안에서 교전하고 물기 돌진을 시작한다(m)")]
    private float _attackRange = 9f;
    [SerializeField, Min(0.1f), Tooltip("땅을 박차고 대상에게 뛰어드는 속도(m/s). 높이까지 포함한 비행 경로 길이를 이 속도로 날아간다.")]
    private float _lungeSpeed = 16f;
    [SerializeField, Min(0.01f), Tooltip("뛰어드는 경로의 최고 높이(m). 몸 루트가 땅에서 뜬 높이이며 물기 위치에 도착할 때 이 높이에 닿는다.")]
    private float _lungePeakHeight = 0.6f;
    [SerializeField, Min(0.1f), Tooltip("로그 곡선처럼 처음에 빠르게 솟는 정도. 클수록 초반에 급히 오르고 뒤쪽은 평평해진다.")]
    private float _lungeCurveSteepness = 9f;
    [SerializeField, Min(0f), Tooltip("돌진을 멈추고 무는 대상 중심과의 거리(m)")]
    private float _biteStopDistance = 1.1f;
    [SerializeField, Min(0.1f), Tooltip("돌진 경로에서 대상과 닿았다고 보는 반지름(m). 멈추는 거리보다 커야 물기 위치에서 대상에 닿는다.")]
    private float _biteRadius = 1.6f;
    [SerializeField, Min(0f), Tooltip("돌진 중 대상이 돌진 축을 따라 물러나면 처음 물기 위치보다 더 따라가는 최대 거리(m). 옆으로 피하는 것은 따라가지 않는다.")]
    private float _maxTrackDistance = 3f;
    [SerializeField, Min(0.05f), Tooltip("물어뜯은 뒤 그 자리에 머무는 시간(초)")]
    private float _biteDuration = 0.25f;
    [SerializeField, Min(0f)] private float _biteDamage = 10f;
    [SerializeField, Min(0f), Tooltip("전조 동안 대상 쪽으로 몸을 돌리는 각속도(도/초)")]
    private float _telegraphTurnSpeed = 540f;

    [Header("물고 난 뒤 이탈")]
    [SerializeField, Min(0f), Tooltip("물고 난 뒤 빠져나가는 대상과의 거리(m)")]
    private float _peelDistance = 6f;
    [SerializeField, Range(0f, 90f), Tooltip("돌진 반대 방향에서 옆으로 비껴 빠져나가는 각도")]
    private float _peelAngle = 50f;
    [SerializeField, Min(0.1f)] private float _repositionSpeed = 9f;
    [SerializeField, Min(0.1f)] private float _repositionTimeout = 1.5f;
    [SerializeField, Min(0.05f)] private float _navMeshSampleRadius = 1f;

    [Header("방어 반동")]
    [SerializeField, Min(0.05f)] private float _blockedBounceDuration = 0.25f;
    [SerializeField, Min(0f)] private float _blockedBounceDistance = 4f;

    [Header("경로와 충돌")]
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    [SerializeField, Tooltip("돌진 경로에서 대상 충돌체를 찾는 레이어")]
    private LayerMask _landingMask = ~0;

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
    private Vector3 _lungeDestination;
    private Vector3 _leapDirection;
    private Vector3 _incomingDirection;
    private Vector3 _repositionDestination;
    private Vector3 _bounceStartPosition;
    private Vector3 _bounceDestination;
    private Vector3 _biteFromPosition;
    private Vector3 _biteLandPosition;
    private float _lungeElapsed;
    private float _lungeDuration;
    private float _initialLungeReach;
    private float _lungeAir;
    private float _biteEndsAt;
    private float _repositionStartedAt;
    private float _bounceElapsed;
    private float _previousAgentSpeed;
    private float _previousStoppingDistance;
    private bool _previousUpdatePosition;
    private bool _previousUpdateRotation;
    private bool _previousAgentStopped;
    private bool _navigationCaptured;
    private bool _preparing;
    private bool _contactResolved;
    private bool _bounceBackOnBlock;
    private bool _attackMotionComplete;

    public override float Range => _attackRange;
    public override bool AllowsMovement => _state == LeapState.Lunging || _state == LeapState.Biting
        || _state == LeapState.BouncingBack || _state == LeapState.Repositioning;
    public bool IsPreparing => _preparing;
    public bool IsLunging => _state == LeapState.Lunging;
    public bool IsBiting => _state == LeapState.Biting;
    public bool IsBouncingBack => _state == LeapState.BouncingBack;
    public float LungeAir => _state == LeapState.Lunging ? _lungeAir : 0f;
    public Vector3 IncomingDirection => _incomingDirection;
    internal override string TelemetryKind => "Melee";

    protected override float ActiveDuration => (_attackRange + 2f * _lungePeakHeight) / Mathf.Max(0.1f, _lungeSpeed) + _biteDuration
        + _repositionTimeout + _blockedBounceDuration;
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
        _contactResolved = false;
        _bounceBackOnBlock = false;
        _bounceElapsed = 0f;
        _lungeAir = 0f;
        _attackMotionComplete = false;
        _navigationCaptured = false;
    }

    protected override void Update()
    {
        base.Update();
        // 웅크린 전조 동안 대상을 향해 몸을 돌려 돌진 방향을 맞춘다.
        if (_preparing && Context.Target != null)
            FaceTarget(Context.Target.position, _telegraphTurnSpeed * Time.deltaTime);
    }

    /// <summary>
    /// target의 생존과 수평 사거리를 확인하여 교전 범위 안이면 true를 반환한다.
    /// 다른 적에 경로가 잠깐 막혀도 추적으로 돌아가지 않도록 돌진 경로는 HasClearLunge와 돌진 시작 때 따로 확인한다.
    /// </summary>
    public override bool CanAttack(Transform target)
    {
        if (!isActiveAndEnabled || target == null || !target.gameObject.activeInHierarchy
            || !_health.IsAlive || !_motor.IsReady)
            return false;

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        return horizontalOffset.sqrMagnitude <= _attackRange * _attackRange;
    }

    /// <summary>
    /// target 앞 _biteStopDistance 지점까지 NavMesh와 직선 장애물 검사를 통과해 지금 뛰어들 수 있으면 true를 반환한다.
    /// </summary>
    public bool HasClearLunge(Transform target)
    {
        return target != null && TryFindLungeDestination(target, out _);
    }

    /// <summary>
    /// context의 방향을 돌진 방향으로 저장하고 웅크림 전조와 붉은 경고를 켠다.
    /// IncomingDirection은 전방 방어 판정에 쓰도록 돌진 반대 방향으로 갱신한다.
    /// </summary>
    protected override void PrepareTelegraph(EnemyAttackContext context)
    {
        _preparing = true;
        _warning.BeginWarning(context.TelegraphDuration);
        _startPosition = transform.position;
        _leapDirection = GetHorizontalDirection(context.Direction);
        _incomingDirection = -_leapDirection;
    }

    /// <summary>
    /// 현재 대상 위치로 돌진 방향과 물기 위치를 정하고 NavMeshAgent의 이전 위치·회전·속도·정지 상태를 저장한 뒤 돌진을 시작한다.
    /// 물기 위치가 유효하지 않으면 경고를 끄고 바로 후딜로 넘어간다.
    /// </summary>
    protected override void PerformAttack(EnemyAttackContext context)
    {
        _preparing = false;
        if (!_health.IsAlive || !_motor.IsReady
            || !TryFindLungeDestination(context.Target, out _lungeDestination))
        {
            _warning.EndWarning();
            CompleteAttackMotion();
            BeginRecovery();
            return;
        }

        _startPosition = transform.position;
        _leapDirection = GetHorizontalDirection(context.Target.position - _startPosition);
        _incomingDirection = -_leapDirection;
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
        _initialLungeReach = Vector3.ProjectOnPlane(_lungeDestination - _startPosition, Vector3.up).magnitude;
        // 높이 때문에 길어진 경로도 같은 속도로 날아가도록 실제 비행 경로 길이로 시간을 정한다.
        _lungeDuration = Mathf.Max(0.12f, GetLungePathLength() / Mathf.Max(0.1f, _lungeSpeed));
        _lungeElapsed = 0f;
        _lungeAir = 0f;
        _contactResolved = false;
        _bounceBackOnBlock = false;
        _bounceElapsed = 0f;
        _attackMotionComplete = false;
        _state = LeapState.Lunging;
        transform.rotation = Quaternion.LookRotation(_leapDirection);
    }

    /// <summary>
    /// context의 대상 생존과 돌진 상태를 확인하고 돌진·물기·방어 반동·이탈 이동을 한 프레임 진행한다.
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

        if (_state == LeapState.Lunging)
            UpdateLunge(context, targetHealth);
        else if (_state == LeapState.Biting)
            UpdateBite(targetHealth);
        else if (_state == LeapState.BouncingBack)
            UpdateBlockedBounceBack();
        else if (_state == LeapState.Repositioning)
            UpdateReposition();
    }

    /// <summary>
    /// 시작 위치에서 _lungeDestination까지 감속하며 로그 곡선처럼 _lungePeakHeight 높이로 솟구쳐 뛰어드는 위치를 한 프레임 진행하고 경로의 대상 접촉을 한 번 판정한다.
    /// 막기·쳐내기면 반동을 시작하고, 대상에 닿거나 물기 위치에 도착하면 그 공중 위치에서 물기를 시작한다.
    /// </summary>
    private void UpdateLunge(EnemyAttackContext context, PlayerHealth targetHealth)
    {
        TrackLungeDestination(context.Target);
        _lungeElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_lungeElapsed / _lungeDuration);
        float eased = 1f - (1f - progress) * (1f - progress);
        float height = GetLungeHeight(eased);
        _lungeAir = Mathf.Clamp01(height / _lungePeakHeight);
        Vector3 groundPosition = Vector3.Lerp(_startPosition, _lungeDestination, eased);
        Vector3 nextPosition = groundPosition + Vector3.up * height;
        Vector3 previousPosition = transform.position;
        if ((nextPosition - previousPosition).sqrMagnitude > 0.000001f
            && !IsClearSegment(previousPosition, nextPosition, context.Target))
        {
            CancelAttack();
            return;
        }

        TryResolveBiteContact(previousPosition, nextPosition, context, targetHealth);
        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(_leapDirection));
        if (_bounceBackOnBlock)
        {
            BeginBlockedBounceBack(targetHealth);
            return;
        }

        if (progress < 1f && !_contactResolved)
            return;

        // 곡선의 최고 높이에서 물고, 물어뜯는 동안 땅으로 내려온 뒤 NavMesh에 맞춘다.
        TryResolveBiteContact(transform.position, transform.position, context, targetHealth);
        if (_bounceBackOnBlock)
        {
            BeginBlockedBounceBack(targetHealth);
            return;
        }

        _lungeAir = 0f;
        _biteFromPosition = transform.position;
        _biteLandPosition = groundPosition;
        _biteEndsAt = Time.time + _biteDuration;
        _state = LeapState.Biting;
    }

    /// <summary>
    /// 감속한 수평 진행도 eased로 땅에서 뜬 높이를 반환한다.
    /// 로그 곡선 log(1 + _lungeCurveSteepness * eased)를 0~1로 맞춰 처음에 빠르게 솟고 물기 위치에서 _lungePeakHeight에 닿게 한다.
    /// </summary>
    private float GetLungeHeight(float eased)
    {
        return _lungePeakHeight * Mathf.Log(1f + _lungeCurveSteepness * eased) / Mathf.Log(1f + _lungeCurveSteepness);
    }

    /// <summary>
    /// _startPosition에서 _lungeDestination까지 GetLungeHeight 높이를 포함한 비행 경로를 LUNGE_PATH_SAMPLES 구간으로 나눠 길이를 반환한다.
    /// </summary>
    private float GetLungePathLength()
    {
        float length = 0f;
        Vector3 previous = _startPosition;
        for (int i = 1; i <= LUNGE_PATH_SAMPLES; i++)
        {
            float progress = (float)i / LUNGE_PATH_SAMPLES;
            float eased = 1f - (1f - progress) * (1f - progress);
            Vector3 point = Vector3.Lerp(_startPosition, _lungeDestination, eased) + Vector3.up * GetLungeHeight(eased);
            length += Vector3.Distance(previous, point);
            previous = point;
        }
        return length;
    }

    /// <summary>
    /// target 위치를 고정한 돌진 축에 투영해 그 앞 _biteStopDistance 지점으로 _lungeDestination을 갱신한다.
    /// 이미 지나온 거리보다 짧아지거나 처음 거리보다 _maxTrackDistance 넘게 길어지지 않게 하며, NavMesh와 장애물 검사를 통과할 때만 바꾼다.
    /// </summary>
    private void TrackLungeDestination(Transform target)
    {
        float targetReach = Vector3.Dot(Vector3.ProjectOnPlane(target.position - _startPosition, Vector3.up), _leapDirection)
            - _biteStopDistance;
        float travelled = Vector3.Dot(Vector3.ProjectOnPlane(transform.position - _startPosition, Vector3.up), _leapDirection);
        float reach = Mathf.Clamp(targetReach, Mathf.Max(0f, travelled), _initialLungeReach + _maxTrackDistance);
        Vector3 desired = _startPosition + _leapDirection * reach;
        desired.y = _navigationPosition.y;
        if (TrySampleNavMeshPosition(desired, out Vector3 sampled) && IsClearSegment(transform.position, sampled, target))
            _lungeDestination = sampled;
    }

    /// <summary>
    /// _biteEndsAt까지 물어뜯으며 _biteFromPosition에서 _biteLandPosition으로 점점 빠르게 떨어진다.
    /// 끝나면 NavMesh 위로 돌아와 경고를 끄고 targetHealth에서 옆으로 비껴 물러나는 이탈 이동을 시작하며, NavMesh에 맞추지 못하면 공격을 취소한다.
    /// </summary>
    private void UpdateBite(PlayerHealth targetHealth)
    {
        if (Time.time < _biteEndsAt)
        {
            float fall = 1f - (_biteEndsAt - Time.time) / Mathf.Max(0.05f, _biteDuration);
            transform.position = Vector3.Lerp(_biteFromPosition, _biteLandPosition, fall * fall);
            return;
        }

        if (!RestoreAgentAt(_biteLandPosition, true))
        {
            CancelAttack();
            return;
        }

        _contactResolved = true;
        _warning.EndWarning();
        BeginReposition(GetPeelDestination(targetHealth.transform.position), targetHealth.transform);
    }

    /// <summary>
    /// targetPosition에서 현재 위치 쪽 방향을 무작위 좌우로 _peelAngle만큼 돌린 _peelDistance 지점을 반환한다.
    /// 물고 난 뒤 같은 길로 되돌아가지 않고 비스듬히 빠져나가게 한다.
    /// </summary>
    private Vector3 GetPeelDestination(Vector3 targetPosition)
    {
        Vector3 away = Vector3.ProjectOnPlane(transform.position - targetPosition, Vector3.up);
        away = away.sqrMagnitude > 0.0001f ? away.normalized : -_leapDirection;
        float side = Random.value < 0.5f ? -1f : 1f;
        return targetPosition + Quaternion.AngleAxis(side * _peelAngle, Vector3.up) * away * _peelDistance;
    }

    /// <summary>
    /// from과 to 사이에 targetHealth의 충돌체가 있으면 공격 방어 계약을 한 번 호출한다.
    /// _biteRadius, _landingMask, context의 공격 식별값을 사용하고 판정 결과를 런타임 상태에 저장한다.
    /// </summary>
    private void TryResolveBiteContact(
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
        DodgeAfterimage.ReportCapsuleSweep(start, end, _biteRadius, Vector3.zero,
            new DamageContext(_biteDamage, gameObject, DodgeAfterimage.ActiveOrigin, _leapDirection,
                PatternId, context.AttackId, 1, IncomingAttackKind.Melee));
        int contactCount = Physics.OverlapCapsuleNonAlloc(start, end, _biteRadius, _landingContacts,
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
    /// 접촉 위치에서 DamageContext를 만들고 PlayerDefenseResolver의 결과를 저장한다.
    /// 막기·쳐내기는 반동 복귀 상태를 켜고, 피격·회피·무적은 물기 뒤 이탈로 처리한다.
    /// </summary>
    private void ResolveTargetContact(EnemyAttackContext context, PlayerHealth targetHealth, Vector3 contactPoint)
    {
        _contactResolved = true;
        DamageContext damage = new DamageContext(_biteDamage, gameObject, contactPoint, _leapDirection,
            PatternId, context.AttackId, 1, IncomingAttackKind.Melee);
        if (targetHealth is IPlayerDefenseResolver resolver)
        {
            DefenseResolution resolution = resolver.ResolveIncomingAttack(damage);
            _bounceBackOnBlock = resolution.Outcome == DefenseOutcome.Guard
                || resolution.Outcome == DefenseOutcome.NormalParry
                || resolution.Outcome == DefenseOutcome.PerfectParry;
            return;
        }

        targetHealth.TakeDamage(damage);
    }

    /// <summary>
    /// 현재 돌진 위치와 플레이어 시야 전방 _blockedBounceDistance의 목적지를 저장해 반동 이동을 시작한다.
    /// 유효한 목적지가 없으면 돌진 시작 위치로 복귀하고 경고 표시를 종료한다.
    /// </summary>
    private void BeginBlockedBounceBack(PlayerHealth targetHealth)
    {
        _warning.EndWarning();
        _lungeAir = 0f;
        _bounceStartPosition = transform.position;
        _bounceDestination = GetBlockedBounceDestination(targetHealth);
        _bounceElapsed = 0f;
        _state = LeapState.BouncingBack;
    }

    /// <summary>
    /// targetHealth의 PlayerMotor 시야 방향과 _blockedBounceDistance를 사용해 안전한 반동 목적지를 반환한다.
    /// NavMesh 샘플 또는 장애물 검사가 실패하면 _startPosition을 반환한다.
    /// </summary>
    private Vector3 GetBlockedBounceDestination(PlayerHealth targetHealth)
    {
        PlayerMotor playerMotor = targetHealth.GetComponentInParent<PlayerMotor>();
        Vector3 forward = playerMotor != null ? playerMotor.ViewForward : targetHealth.transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = -_leapDirection;

        Vector3 desiredDestination = targetHealth.transform.position + forward * _blockedBounceDistance;
        desiredDestination.y = _agent.nextPosition.y;
        if (TrySampleNavMeshPosition(desiredDestination, out Vector3 sampledDestination)
            && IsClearSegment(transform.position, sampledDestination, targetHealth.transform))
            return sampledDestination;

        return _startPosition;
    }

    /// <summary>
    /// _bounceStartPosition에서 _bounceDestination까지 빠르게 감속하며 이동하고 NavMeshAgent를 목적지에 맞춘다.
    /// 진행 시간은 _blockedBounceDuration이며 완료되면 공격 동작을 끝낸다.
    /// </summary>
    private void UpdateBlockedBounceBack()
    {
        _bounceElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(_bounceElapsed / Mathf.Max(0.05f, _blockedBounceDuration));
        float easedProgress = 1f - (1f - progress) * (1f - progress);
        Vector3 nextPosition = Vector3.Lerp(_bounceStartPosition, _bounceDestination, easedProgress);
        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(_leapDirection));
        if (progress < 1f)
            return;

        if (!RestoreAgentAt(_bounceDestination, false))
        {
            CancelAttack();
            return;
        }

        CompleteAttackMotion();
    }

    /// <summary>
    /// desiredDestination 주변의 완전한 NavMesh 경로와 장애물 없는 경로 구간을 확인해 이탈 이동을 시작한다.
    /// 목적지가 무효하거나 이동 요청이 거부되면 현재 안전 위치에서 공격을 마친다.
    /// </summary>
    private void BeginReposition(Vector3 desiredDestination, Transform ignoredTarget)
    {
        _state = LeapState.Repositioning;
        _agent.speed = _repositionSpeed;
        _agent.updateRotation = _previousUpdateRotation;
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
    /// </summary>
    private void UpdateReposition()
    {
        if (!_motor.IsReady || _motor.HasArrived || Time.time >= _repositionStartedAt + _repositionTimeout)
            CompleteAttackMotion();
    }

    /// <summary>
    /// 전조·돌진·물기·이동이 끝났음을 표시한다. 다음 교전 이동이 바로 이어지도록 경로는 멈추지 않는다.
    /// 기본 EnemyAttackBehaviour가 다음 Update에서 후딜로 전환할 수 있게 한다.
    /// </summary>
    private void CompleteAttackMotion()
    {
        _preparing = false;
        _lungeAir = 0f;
        _state = LeapState.Finished;
        _attackMotionComplete = true;
    }

    /// <summary>
    /// cancelled 여부와 _navigationCaptured를 사용하여 이동 속도·회전 갱신·NavMesh 위치를 복원한다.
    /// 현재 위치를 NavMesh에 맞추고, 맞출 수 없으면 돌진 시작 때 저장한 NavMesh 위치로 Warp한다.
    /// </summary>
    protected override void OnAttackEnded(EnemyAttackContext context, bool cancelled)
    {
        _warning.EndWarning();
        _preparing = false;
        _lungeAir = 0f;
        bool moving = _state == LeapState.Repositioning || _state == LeapState.Finished;
        _state = LeapState.None;
        _attackMotionComplete = false;
        if (!_navigationCaptured)
            return;

        if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
        {
            // 이탈 이동을 마친 뒤에는 이미 NavMesh 위에 있으므로 경로를 끊거나 Warp하지 않는다.
            if (cancelled || !moving)
            {
                _motor.Stop();
                _agent.updatePosition = true;
                // 높이 뛰어든 공중에서 취소돼도 바로 아래 땅을 찾도록 시작 NavMesh 높이에서 샘플한다.
                Vector3 groundPosition = transform.position;
                groundPosition.y = _navigationPosition.y;
                if (!TrySampleNavMeshPosition(groundPosition, out Vector3 sampledPosition))
                    sampledPosition = _navigationPosition;
                _agent.Warp(sampledPosition);
            }
        }

        _agent.updatePosition = _previousUpdatePosition;
        _agent.updateRotation = _previousUpdateRotation;
        _agent.speed = _previousAgentSpeed;
        _agent.stoppingDistance = _previousStoppingDistance;
        if (cancelled || !moving)
            _agent.isStopped = _previousAgentStopped;
        _navigationCaptured = false;
    }

    /// <summary>
    /// desiredPosition을 같은 NavMesh에 투영하고 agent를 그 위치로 Warp한다.
    /// fallbackToNavigationPosition이 true이면 샘플 실패 시 저장한 시작 NavMesh 위치를 사용하고, 성공 여부를 반환한다.
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
    /// target 앞 _biteStopDistance 지점을 NavMesh에 투영하고 현재 위치에서 그곳까지 직선 장애물이 없으면 destination을 반환한다.
    /// 이미 그 거리 안에 있으면 현재 위치를 물기 위치로 사용한다.
    /// </summary>
    private bool TryFindLungeDestination(Transform target, out Vector3 destination)
    {
        Vector3 offset = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up);
        float reach = Mathf.Max(0f, offset.magnitude - _biteStopDistance);
        Vector3 desired = transform.position + GetHorizontalDirection(offset) * reach;
        desired.y = _agent.nextPosition.y;
        return TrySampleNavMeshPosition(desired, out destination)
            && IsClearSegment(transform.position, destination, target);
    }

    /// <summary>direction의 수평 단위 방향을 반환한다. 길이가 없으면 현재 몸 정면, 그것도 없으면 월드 정면을 반환한다.</summary>
    private Vector3 GetHorizontalDirection(Vector3 direction)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (horizontal.sqrMagnitude < 0.0001f)
            horizontal = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return horizontal.sqrMagnitude < 0.0001f ? Vector3.forward : horizontal.normalized;
    }

    /// <summary>
    /// position의 수평 방향으로 최대 maxDegrees만큼 몸을 돌리고 돌진 방향과 IncomingDirection을 그 방향으로 갱신한다.
    /// </summary>
    private void FaceTarget(Vector3 position, float maxDegrees)
    {
        Vector3 direction = Vector3.ProjectOnPlane(position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), maxDegrees);
        _leapDirection = direction.normalized;
        _incomingDirection = -_leapDirection;
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
