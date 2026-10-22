using System;

using UnityEngine;

public enum EnemyState
{
    Idle,
    Chasing,
    Searching,
    Attacking,
    Patrolling
}

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySensor), typeof(EnemyMotor), typeof(EnemyAttackSelector))]
public class EnemyController : MonoBehaviour
{
    private const float TURN_WAIT_FACING_MARGIN = 1f;

    [Header("추적 설정")]
    [SerializeField, Min(0.02f)] private float _perceptionInterval = 0.1f;
    [SerializeField, Min(0.02f)] private float _repathInterval = 0.2f;
    [SerializeField, Min(0f)] private float _memoryDuration = 3f;
    [SerializeField, Tooltip("끄면 공격 범위 밖이나 돌진 경로가 막힌 경우 걸어서 접근하지 않고 대기합니다.")]
    private bool _allowWalkingChase = true;
    [SerializeField, Min(0f)] private float _meleeAttackStoppingDistance = 1.2f;

    private EnemySensor _sensor;
    private EnemyMotor _motor;
    private EnemyAttackSelector _attackSelector;
    private EnemyPatrol _patrol;
    private EnemyMeleeWarningVisual _meleeWarningVisual;
    private IEnemyEngagementMovement _engagement;
    private bool _hasVisibleTarget;
    private EnemyState _state;
    private Vector3 _lastKnownPosition;
    private float _lastSeenTime;
    private float _nextPerceptionTime;
    private float _nextRepathTime;
    private Vector3 _heardPosition;
    private float _noiseAlertUntil = float.NegativeInfinity;
    private bool _openingAttackPending;
    private bool _openingAttackPerformed;

    [Header("공격 차례 대기")]
    [SerializeField, Min(0.5f), Tooltip("공격 차례를 기다리는 동안 대상과 유지하는 최소 수평 거리(m)")]
    private float _turnWaitMinimumRadius = 3.5f;
    [SerializeField, Min(0.5f), Tooltip("공격 차례를 기다리는 동안 대상과 유지하는 최대 수평 거리(m). 더 멀면 이 거리까지 다가온다.")]
    private float _turnWaitMaximumRadius = 5f;
    [SerializeField, Range(0f, 45f), Tooltip("좌우 이동 때 대기 원 위에서 현재 방향보다 앞서 목표로 삼는 각도")]
    private float _turnWaitStrafeAngle = 12f;
    [SerializeField, Min(0.1f), Tooltip("좌우 이동 또는 멈춤 한 구간의 최소 시간(초)")]
    private float _turnWaitSegmentMinimum = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("좌우 이동 또는 멈춤 한 구간의 최대 시간(초)")]
    private float _turnWaitSegmentMaximum = 1.6f;
    [SerializeField, Range(0f, 1f), Tooltip("구간마다 좌우 이동 대신 제자리에서 대상을 바라보며 멈출 확률")]
    private float _turnWaitHoldChance = 0.3f;
    private IEnemyAttackGate _attackGate;
    private bool _attackTurnSpent;
    private float _turnWaitSign;
    private float _turnWaitSegmentEndsAt;


    public EnemyState State => _state;
    private bool AllowsWalkingChase => _allowWalkingChase || _attackSelector.MeleeOnly;
    public event Action<EnemyState> StateChanged;

    protected EnemySensor Sensor => _sensor;
    protected EnemyMotor Motor => _motor;
    protected EnemyAttackBehaviour Attack => _attackSelector.CurrentAttack;
    protected EnemyAttackSelector AttackSelector => _attackSelector;

    protected virtual void Awake()
    {
        _sensor = GetComponent<EnemySensor>();
        _motor = GetComponent<EnemyMotor>();
        _attackSelector = GetComponent<EnemyAttackSelector>();
        _patrol = GetComponent<EnemyPatrol>();
        _meleeWarningVisual = GetComponent<EnemyMeleeWarningVisual>();
        _engagement = GetComponent<IEnemyEngagementMovement>();
    }

    protected virtual void OnEnable()
    {
        _nextPerceptionTime = 0f;
        _nextRepathTime = 0f;
        _lastSeenTime = float.NegativeInfinity;
        _lastKnownPosition = transform.position;
        _hasVisibleTarget = false;
        _noiseAlertUntil = float.NegativeInfinity;
        _openingAttackPending = false;
        _openingAttackPerformed = false;
    }

    protected virtual void Update()
    {
        // 차례로 시작한 공격이 끝나면 차례를 반납해 다음 대기 적에게 넘긴다.
        if (_attackTurnSpent && !_attackSelector.IsBusy)
            ReleaseAttackTurn();

        if (!_motor.IsReady)
        {
            _attackSelector.CancelCurrentAttack();
            ChangeState(EnemyState.Idle);
            return;
        }

        if (_attackSelector.IsStaggered)
        {
            _motor.Stop();
            ChangeState(EnemyState.Attacking);
            return;
        }

        if (_meleeWarningVisual != null && _meleeWarningVisual.HoldPosition)
        {
            ChangeState(EnemyState.Attacking);
            _motor.Stop();
            return;
        }

        if (_attackSelector.IsBusy)
        {
            EnemyAttackBehaviour attack = _attackSelector.CurrentAttack;
            if (attack.Phase == EnemyAttackPhase.Recovery)
            {               
                if (attack is EnemyMeleeAttack && _meleeWarningVisual != null)
                    UpdateMeleeAttackPursuit();
                else
                    UpdateRecoveryPursuit();

                return;
            }

            ChangeState(EnemyState.Attacking);
            if (attack is EnemyMeleeAttack)
                UpdateMeleeAttackPursuit();
            else if (!attack.AllowsMovement)
                _motor.Stop();
            return;
        }

        if (Time.time >= _nextPerceptionTime)
        {
            _nextPerceptionTime = Time.time + _perceptionInterval;
            UpdatePerception();
        }

        if (_state == EnemyState.Idle || _state == EnemyState.Patrolling)
        {
            if (!_hasVisibleTarget && _patrol != null && _patrol.IsConfigured)
            {
                ChangeState(EnemyState.Patrolling);
                _patrol.UpdatePatrol();
            }
            else
            {
                ChangeState(EnemyState.Idle);
            }
            return;
        }

        if (_state == EnemyState.Attacking)
        {
            // 교전 이동이 없는 적은 공격 차례가 올 때까지 대기 원으로 물러나 좌우로 움직인다.
            if (_engagement == null && !HasAttackTurn())
            {
                UpdateTurnWaiting(_sensor.Target);
                return;
            }

            if (_attackSelector.MeleeOnly)
                UpdateMeleeAttackPursuit();
            else if (_engagement != null)
                _engagement.UpdateEngagement(_sensor.Target);
            else
                _motor.Stop();

            // 교전 이동이 있는 적은 그 이동을 마치고 공격 차례를 받은 뒤에만 다음 공격을 시작한다.
            if (_engagement != null && (!_engagement.IsReadyToAttack || !HasAttackTurn()))
                return;

            // 첫 추격 후 즉시 베기 제외함
            // if (_attackSelector.TryAttack(_sensor.Target, _openingAttackPending)
            //     && _openingAttackPending && _attackSelector.CurrentAttack is EnemyMeleeAttack)
            // {
            //     _openingAttackPending = false;
            //     _openingAttackPerformed = true;
            // }

            if (_attackSelector.TryAttack(_sensor.Target) && _attackGate != null)
            {
                _attackTurnSpent = true;
                _attackGate.NotifyAttackStarted(this);
            }
            return;
        }

        if (_state == EnemyState.Searching &&
            (Time.time - _lastSeenTime >= _memoryDuration || _motor.HasArrived))
        {
            ChangeState(EnemyState.Idle);
            return;
        }

        if (_state == EnemyState.Chasing && _engagement == null && !HasAttackTurn())
        {
            UpdateTurnWaiting(_sensor.Target);
            return;
        }

        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + _repathInterval;
            float? approachRange = _state == EnemyState.Chasing && _attackSelector.isActiveAndEnabled
                ? _attackSelector.GetApproachRange(_sensor.Target)
                : null;
            float? approachDistance = approachRange.HasValue
                ? Mathf.Max(0f, approachRange.Value - 0.2f)
                : (_state == EnemyState.Searching ? 0.1f : (float?)null);
            _motor.MoveTo(_lastKnownPosition, approachDistance);
        }
    }

    /// <summary>
    /// 센서 대상의 위치를 후딜 중 갱신하여 사거리 밖이면 추적하고 안이면 바라본다.
    /// 기존 감지·경로 갱신 간격을 사용하며 공격 후딜과 쿨타임을 유지한다.
    /// </summary>
    private void UpdateRecoveryPursuit()
    {
        if (Time.time >= _nextPerceptionTime)
        {
            _nextPerceptionTime = Time.time + _perceptionInterval;
            UpdatePerception();
        }
        if (_state == EnemyState.Attacking)
        {
            // 교전 이동이 있는 적은 후딜 중에도 멈추지 않고 대상 주변을 움직인다.
            if (_engagement != null)
            {
                _engagement.UpdateEngagement(_sensor.Target);
                return;
            }
            _motor.Stop();
            _motor.FaceTarget(_lastKnownPosition);
            return;
        }
        if (AllowsWalkingChase && (_state == EnemyState.Chasing || _state == EnemyState.Searching))
        {
            if (Time.time < _nextRepathTime) return;
            _nextRepathTime = Time.time + _repathInterval;
            float? range = _state == EnemyState.Chasing ? _attackSelector.GetApproachRange(_sensor.Target) : null;
            _motor.MoveTo(_lastKnownPosition, range.HasValue ? Mathf.Max(0f, range.Value - 0.2f) : 0.1f);
            return;
        }
        _motor.Stop();
    }

    protected virtual void OnDisable()
    {
        _attackSelector.CancelCurrentAttack();
        ReleaseAttackTurn();
        _motor.Stop();
        ChangeState(EnemyState.Idle);
    }

    /// <summary>
    /// 공격 차례를 나눠 줄 gate를 연결한다. 이전 gate의 차례와 대기 요청은 반납한다.
    /// null이면 차례 제한 없이 기존처럼 공격한다.
    /// </summary>
    public void SetAttackGate(IEnemyAttackGate gate)
    {
        ReleaseAttackTurn();
        _attackGate = gate;
    }

    /// <summary>
    /// target을 센서에 전달하고 기존 기억과 경로를 초기화하여 새 대상의 감지를 시작한다.
    /// 추적 상태는 Idle로 변경되며 null 입력은 감지 대상을 제거한다.
    /// </summary>
    public void SetTarget(Transform target)
    {
        _sensor.SetTarget(target);
        _attackSelector.CancelCurrentAttack();
        ReleaseAttackTurn();
        _lastSeenTime = float.NegativeInfinity;
        _nextPerceptionTime = 0f;
        _hasVisibleTarget = false;
        _openingAttackPending = false;
        _openingAttackPerformed = false;
        _motor.Stop();
        ChangeState(EnemyState.Idle);
    }

    /// <summary>position에서 들린 검 소음을 duration 동안 기억하고 순찰을 중단해 소음 위치로 접근한다.</summary>
    public void HearNoise(Vector3 position, float duration)
    {
        if (!isActiveAndEnabled || _sensor.Target == null)
            return;
        _heardPosition = position;
        _noiseAlertUntil = Time.time + Mathf.Max(0.1f, duration);
        _lastKnownPosition = position;
        _lastSeenTime = Time.time;
        _nextPerceptionTime = 0f;
        if (!_attackSelector.IsBusy)
            ChangeState(EnemyState.Searching);
    }

    /// <summary>
    /// 차례 제한이 없거나 _attackGate에서 공격 차례를 받았으면 true를 반환한다.
    /// 차례가 없으면 gate의 대기 요청 시각을 갱신한다.
    /// </summary>
    private bool HasAttackTurn()
    {
        return _attackGate == null || _attackGate.RequestTurn(this);
    }

    /// <summary>
    /// _attackGate에 현재 차례와 대기 요청을 반납하고 _attackTurnSpent를 초기화한다.
    /// gate가 없으면 상태만 초기화한다.
    /// </summary>
    private void ReleaseAttackTurn()
    {
        _attackTurnSpent = false;
        if (_attackGate != null)
            _attackGate.ReleaseTurn(this);
    }

    /// <summary>
    /// 공격 차례를 기다리는 동안 target 주변 대기 원 위에서 구간마다 왼쪽·오른쪽 이동 또는 멈춤을 고른다.
    /// target과의 수평 거리를 대기 최소·최대 반지름으로 제한한 위치로 EnemyMotor 경로를 갱신하며 막히면 _turnWaitSign을 뒤집는다.
    /// 대기 원 근처에서는 이동 방향 대신 target을 바라본 채 옆걸음하고, 멀리서 다가올 때는 이동 방향을 바라본다.
    /// </summary>
    private void UpdateTurnWaiting(Transform target)
    {
        if (target == null)
        {
            _motor.Stop();
            return;
        }

        if (Time.time >= _turnWaitSegmentEndsAt)
        {
            _turnWaitSign = UnityEngine.Random.value < _turnWaitHoldChance ? 0f : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            _turnWaitSegmentEndsAt = Time.time + UnityEngine.Random.Range(_turnWaitSegmentMinimum,
                Mathf.Max(_turnWaitSegmentMinimum, _turnWaitSegmentMaximum));
        }

        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + _repathInterval;
            Vector3 offset = Vector3.ProjectOnPlane(transform.position - target.position, Vector3.up);
            float distance = offset.magnitude;
            Vector3 direction = distance > 0.01f ? offset / distance : -transform.forward;
            float radius = Mathf.Clamp(distance, _turnWaitMinimumRadius, Mathf.Max(_turnWaitMinimumRadius, _turnWaitMaximumRadius));
            Vector3 destination = target.position + Quaternion.AngleAxis(_turnWaitSign * _turnWaitStrafeAngle, Vector3.up) * direction * radius;
            // 숲이나 다른 적에 막힌 쪽이면 반대 방향으로 돌아 좌우 이동을 이어 간다.
            if (!_motor.MoveTo(destination, 0.1f) && _turnWaitSign != 0f)
                _turnWaitSign = -_turnWaitSign;
        }

        float targetDistance = Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up).magnitude;
        if (targetDistance <= Mathf.Max(_turnWaitMinimumRadius, _turnWaitMaximumRadius) + TURN_WAIT_FACING_MARGIN)
            _motor.FaceTargetWhileMoving(target.position);
        else if (_motor.HasArrived)
            _motor.FaceTarget(target.position);
    }

    /// <summary>
    /// 센서의 현재 대상을 확인해 근접 공격 정지 거리까지 추적한다.
    /// 대상 위치를 사용하여 EnemyMotor의 이동 경로 또는 바라보는 방향을 갱신한다.
    /// </summary>
    private void UpdateMeleeAttackPursuit()
    {
        Transform target = _sensor.Target;
        if (target == null || !_sensor.CanSeeTarget())
        {
            _motor.Stop();
            return;
        }

        if (Time.time >= _nextRepathTime)
        {
            _nextRepathTime = Time.time + _repathInterval;
            _motor.MoveTo(target.position, _meleeAttackStoppingDistance);
        }

        if (_motor.HasArrived)
            _motor.FaceTarget(target.position);
    }

    /// <summary>
    /// 센서의 대상 가시성과 기억 시간을 사용하여 추적 상태를 갱신한다.
    /// 보이는 대상의 마지막 위치와 시간을 기록하고 공격 범위에서는 공격 상태로 전환한다.
    /// 보이지 않는 대상은 수색 또는 대기로 전환한다.
    /// </summary>
    protected virtual void UpdatePerception()
    {
        _hasVisibleTarget = _sensor.CanSeeTarget();
        if (_sensor.Target == null || !_sensor.Target.gameObject.activeInHierarchy)
        {
            if (_state != EnemyState.Patrolling)
                ChangeState(EnemyState.Idle);
            return;
        }

        if (_hasVisibleTarget)
        {
            _lastKnownPosition = _sensor.Target.position;
            _lastSeenTime = Time.time;
            ChangeState(_attackSelector.isActiveAndEnabled &&
                _attackSelector.CanAttack(_sensor.Target) ? EnemyState.Attacking :
                    (AllowsWalkingChase ? EnemyState.Chasing : EnemyState.Idle));
            return;
        }

        if (Time.time < _noiseAlertUntil)
        {
            _lastKnownPosition = _heardPosition;
            _lastSeenTime = Time.time;
            ChangeState(EnemyState.Searching);
            return;
        }

        if (_state == EnemyState.Chasing || _state == EnemyState.Attacking)
            ChangeState(AllowsWalkingChase && _memoryDuration > 0f ? EnemyState.Searching : EnemyState.Idle);
    }

    /// <summary>
    /// nextState가 현재 상태와 다르면 상태를 변경하고 C# 이벤트와 파생 클래스의 상태 반응을 호출한다.
    /// Idle에서는 이동을 중지하고 Searching에서는 이전 경로의 도착 상태를 초기화한다.
    /// 새 추격의 첫 근접 공격을 예약하고 수색·대기·순찰에서는 해당 추격의 공격 기록을 초기화한다.
    /// </summary>
    protected void ChangeState(EnemyState nextState)
    {
        if (_state == nextState)
            return;

        if (nextState == EnemyState.Idle || nextState == EnemyState.Searching || nextState == EnemyState.Patrolling)
        {
            _openingAttackPending = false;
            _openingAttackPerformed = false;
        }
        else if (nextState == EnemyState.Chasing && !_openingAttackPerformed)
            _openingAttackPending = true;

        if (_state == EnemyState.Patrolling && _patrol != null)
            _patrol.Pause();
        _state = nextState;
        if (_state == EnemyState.Idle)
            _motor.Stop();
        else
        {
            if (_state == EnemyState.Searching)
                _motor.Stop();
            _nextRepathTime = 0f;
        }

        StateChanged?.Invoke(_state);
        OnStateChanged(_state);
    }

    /// <summary>
    /// nextState를 파생 유닛의 상태 반응 구현에 전달한다.
    /// 기본 구현은 상태를 추가로 변경하지 않으며 파생 클래스에서 애니메이션 등을 연결할 수 있다.
    /// </summary>
    protected virtual void OnStateChanged(EnemyState nextState)
    {
    }
}
