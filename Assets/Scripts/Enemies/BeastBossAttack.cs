using System;
using System.Collections;

using UnityEngine;

using UnityEngine.AI;

/// <summary>짐승이 땅에 뛰어들고 튀어나오는 동작의 진행 구간이다.</summary>
public enum BeastBurrowMotion { None, Diving, Emerging }

/// <summary>짐승의 패턴 사이 배회·원형 이동·가장자리 질주·지그재그 돌진·점프·잠수 기습 공격과 NavMesh 복원을 담당한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(NavMeshAgent))]
public sealed class BeastBossAttack : MonoBehaviour
{
    private const float MAX_RHYTHM_DELAY = 0.1f;
    private const int EMERGE_ATTEMPTS = 12;

    [Header("협력 이동")]
    [SerializeField] private Transform _riderSeat;
    [SerializeField, Min(0.5f)] private float _orbitRadius = 6f;
    [SerializeField, Min(0.1f)] private float _orbitSpeed = 5f;
    [SerializeField, Min(0.05f)] private float _orbitDestinationInterval = 0.2f;
    private EnemyHealth _health;
    private EnemyMotor _motor;
    private NavMeshAgent _agent;
    private NavMeshPath _path;
    private float _previousSpeed;
    private bool _orbiting;
    private float _orbitAngle;
    private float _orbitSign;
    private float _nextOrbitDestination;

    [Header("돌진")]
    [SerializeField, Min(0.1f)] private float _chargePreparation = 0.9f;
    [SerializeField, Min(0.1f)] private float _chargeSpeed = 12f;
    [SerializeField, Min(0.1f)] private float _chargeDistance = 12f;
    [SerializeField, Min(0.1f)] private float _zigzagWidth = 2f;
    [SerializeField, Min(1)] private int _zigzagSegments = 4;
    [SerializeField, Min(0f)] private float _chargeDamage = 25f;
    [SerializeField, Min(0.01f)] private float _contactRadius = 0.7f;
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    [SerializeField] private SoundId _preparationSound = SoundId.EnemyAttackVoice;
    [SerializeField] private SoundId _parrySound = SoundId.None;
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Collider[] _contacts = new Collider[32];
    private bool _attackResolved;

    [Header("근접 공격 박동 전조")]
    [SerializeField] private bool _useHeartbeatTelegraph = true;
    [SerializeField, Min(1), InspectorName("박동 횟수")]
    private int _heartbeatCount = 1;
    [SerializeField, Min(0f), InspectorName("첫 박동 전 대기 (초)")]
    private float _firstHeartbeatDelay = 0.2f;
    [SerializeField, Min(0.1f), InspectorName("박동 간격 최소 (초)")]
    private float _minimumHeartbeatInterval = 1f;
    [SerializeField, Min(0.1f), InspectorName("박동 간격 최대 (초)")]
    private float _maximumHeartbeatInterval = 1.2f;
    [SerializeField, Min(0.05f), InspectorName("마지막 박동 후 공격 지연 (초)"), Tooltip("점프는 이 시간 뒤 도약하며 실제 피해는 기존 착지 시점에 적용합니다.")]
    private float _postHeartbeatAttackDelay = 0.5f;
    private bool _preparationSucceeded;
    private bool _preparing;
    private SoundId _playingHeartbeat;

    [Header("점프")]
    [SerializeField, Min(0.1f)] private float _jumpPreparation = 1f;
    [SerializeField, Min(0.1f)] private float _jumpDuration = 0.8f;
    [SerializeField, Min(0.1f)] private float _jumpHeight = 3f;
    [SerializeField, Min(0.1f)] private float _landingRadius = 2f;
    [SerializeField, Min(0f)] private float _jumpDamage = 25f;
    private bool _jumping;
    private bool _previousUpdatePosition;
    private bool _previousUpdateRotation;
    private Vector3 _navigationPosition;

    [Header("가장자리 질주")]
    [SerializeField, Min(0.1f), Tooltip("탑승 난사 중 공터 가장자리를 달리는 속도(미터/초)")]
    private float _edgeRunSpeed = 14f;
    [SerializeField, Min(0.1f), Tooltip("가장자리 질주 가속도(미터/초 제곱)")]
    private float _edgeRunAcceleration = 40f;
    [SerializeField, Range(0f, 179f), Tooltip("공터 중심에서 본 플레이어 방위와 이 각도(도)보다 가까워지면 방향을 바꿔 반대편 호에 머문다.")]
    private float _edgeRunAvoidAngle = 95f;
    [SerializeField, Min(0.05f), Tooltip("현재 위치에서 몇 초 앞의 원주 지점을 이동 목표로 삼을지")]
    private float _edgeRunLookAhead = 0.6f;
    private Vector3 _edgeCenter;
    private float _edgeRadius;
    private float _edgeSign;
    private float _previousAcceleration;
    private float _lastEdgeAngle;
    private bool _edgeRunning;
    private bool _edgeReached;

    [Header("잠수 기습")]
    [SerializeField, Min(0.05f), Tooltip("땅에 뛰어들어 완전히 잠기기까지의 시간(초)")]
    private float _diveDuration = 0.35f;
    [SerializeField, Min(0.05f), Tooltip("땅에서 튀어나와 지면에 서기까지의 시간(초)")]
    private float _emergeDuration = 0.25f;
    [SerializeField, Min(0.5f), Tooltip("출현할 가장자리 지점이 플레이어와 떨어져야 하는 최소 거리(미터)")]
    private float _emergeDistance = 8f;
    [SerializeField, Range(1f, 360f), Tooltip("플레이어 수평 시점 기준 출현 방향 범위(도). 막기 가능한 정면 쪽 가장자리에서 나온다.")]
    private float _emergeArc = 140f;
    [SerializeField, Min(0f), Tooltip("가장자리 출현·도착 지점을 원 안쪽으로 당기는 거리(미터)")]
    private float _edgeMargin = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("가장자리에 나온 뒤 붉게 물들며 돌진 직전까지 기다리는 시간(초)")]
    private float _biteTelegraph = 2f;
    [SerializeField, Min(0.05f), Tooltip("플레이어를 지나 반대편 가장자리까지 뛰어 덮치는 비행 시간(초)")]
    private float _biteDuration = 1f;
    [SerializeField, Min(0f), Tooltip("덮치며 날아갈 때 포물선의 가장 높은 높이(미터). 낮을수록 플레이어 몸을 그대로 꿰뚫고 지나간다.")]
    private float _pounceHeight = 0.8f;
    [SerializeField, Min(0f), Tooltip("덮치기가 플레이어 위치를 지나 최소한 더 나아가는 거리(미터)")]
    private float _pounceOvershoot = 3f;
    [SerializeField, Tooltip("가장자리에서 붉게 물들기 시작할 때 짐승에게서 울리는 소리. 퍼펙트 패링음을 낮춰 쓴다.")]
    private SoundId _chargeSound = SoundId.TwinBossChargePulse;
    [SerializeField, Range(0.1f, 3f)] private float _chargeSoundPitch = 0.6f;
    [SerializeField, Min(0f)] private float _chargeSoundVolume = 1.4f;
    [SerializeField, Min(0f)] private float _biteDamage = 15f;
    [SerializeField, Tooltip("뛰어들고 튀어나오는 자리에 크게 퍼지는 물 파동")]
    private RevealRippleSettings _burrowRipple = new RevealRippleSettings(16f, 1f, 3, 0.15f, 1.5f,
        new Color(1f, 1f, 1f, 0.9f), true);
    [SerializeField, Tooltip("뛰어들고 튀어나올 때 크게 울리는 울음소리")]
    private SoundId _burrowCry = SoundId.BeastBossIdle;
    [SerializeField, Min(0f)] private float _burrowCryVolume = 2f;
    [SerializeField, Min(0f), Tooltip("멈춰 있다가 뛰어들 때도 앞으로 뛰어드는 최소 속도(미터/초). 달리던 중이면 그 기세를 이어받는다.")]
    private float _diveMinSpeed = 7f;
    [SerializeField, Min(0f), Tooltip("뛰어들며 이어받는 기세의 최대 속도(미터/초)")]
    private float _diveMaxSpeed = 14f;
    [SerializeField, Min(0f), Tooltip("땅에서 튀어나오며 앞으로 뛰쳐나가는 속도(미터/초)")]
    private float _emergeLeapSpeed = 7f;
    private Vector3 _momentum;
    private Collider _hitCollider;
    private TwinBossWarningVisual _warning;
    private PerceptionRevealMap _revealMap;
    private bool _burrowed;
    private bool _biting;
    private bool _hasLandingSpot;

    [Header("배회")]
    [SerializeField, Min(1f), Tooltip("패턴 사이 플레이어 주위를 맴도는 거리(미터)")]
    private float _prowlRadius = 9f;
    [SerializeField, Min(0.5f), Tooltip("플레이어가 이 거리(미터)보다 가까우면 고개를 플레이어 쪽에 둔 채 바깥으로 호를 그리며 달려 거리를 벌린다.")]
    private float _prowlKeepAway = 6f;
    [SerializeField, Min(0f), Tooltip("거리 벌리기를 멈추고 다시 맴돌기까지 더 벌릴 거리(미터)")]
    private float _prowlBackHysteresis = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("배회하며 달리는 속도(미터/초). 걷지 않도록 질주 속도로 둔다.")]
    private float _prowlSpeed = 9f;
    [SerializeField, Min(0.1f), Tooltip("배회 가속도(미터/초 제곱). 작을수록 출발·방향 전환이 부드럽다.")]
    private float _prowlAcceleration = 14f;
    [SerializeField, Min(0.05f), Tooltip("현재 위치에서 몇 초 앞의 지점을 이동 목표로 삼을지")]
    private float _prowlLookAhead = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("맴도는 방향을 바꾸는 최소 간격(초)")]
    private float _prowlSwitchMin = 2.5f;
    [SerializeField, Min(0.1f)] private float _prowlSwitchMax = 5f;
    private Transform _prowlTarget;
    private Vector3 _prowlCenter;
    private float _prowlArenaRadius;
    private float _prowlSign;
    private float _nextProwlSwitch;
    private float _nextProwlDestination;
    private float _prowlBaseSpeed;
    private float _prowlBaseAcceleration;
    private bool _prowlEscaping;
    private bool _prowling;

    public EnemyHealth Health => _health;
    public Transform RiderSeat => _riderSeat;
    public bool IsReady => _motor.IsReady;
    public bool IsPreparing => _preparing;
    public bool IsJumping => _jumping;
    public bool IsBurrowed => _burrowed;
    public float EdgeRunTraveledAngle { get; private set; }
    public bool IsBiting => _biting;
    public BeastBurrowMotion BurrowMotion { get; private set; }
    public float BurrowDepth { get; private set; }
    public bool IsProwling => _prowling;
    public bool HasLookTarget => _prowling && _prowlTarget != null;
    public Vector3 LookTarget => _prowlTarget.position;
    public event Action AttackPreparing;
    public event Action AttackStarted;
    public event Action Landed;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _motor = GetComponent<EnemyMotor>();
        _agent = GetComponent<NavMeshAgent>();
        _hitCollider = GetComponent<Collider>();
        _warning = GetComponent<TwinBossWarningVisual>();
        _path = new NavMeshPath();
    }

    void Update()
    {
        if (_prowling) TickProwl();
    }

    /// <summary>
    /// 패턴 사이에 멈추지 않고 player 주위를 달리며 배회하기 시작한다. center·radius 원형 공터 안에서만 움직인다.
    /// 기존 이동 속도·가속도를 저장하고 맴도는 방향을 무작위로 정한다. 이미 배회 중이면 아무것도 하지 않는다.
    /// </summary>
    public void BeginProwl(Transform player, Vector3 center, float radius)
    {
        if (_prowling) return;
        _prowlBaseSpeed = _agent.speed;
        _prowlBaseAcceleration = _agent.acceleration;
        _prowlTarget = player;
        _prowlCenter = center;
        _prowlArenaRadius = Mathf.Max(1f, radius);
        _prowlSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        _nextProwlSwitch = Time.time + UnityEngine.Random.Range(_prowlSwitchMin, Mathf.Max(_prowlSwitchMin, _prowlSwitchMax));
        _nextProwlDestination = 0f;
        _prowlEscaping = false;
        _prowling = true;
    }

    /// <summary>
    /// 항상 _prowlSpeed로 달린다. 플레이어가 _prowlKeepAway보다 가까우면 바깥으로 호를 그리며 달려 거리를 벌리고,
    /// _prowlBackHysteresis만큼 더 벌어지면 _prowlRadius 거리에서 플레이어 주위를 맴돈다. 고개는 시각 담당자가 플레이어 쪽으로 돌린다.
    /// 일정 간격으로 앞쪽 목표 지점을 공터 안으로 당겨 이동 목표로 정하고, 막히거나 정해진 시간이 지나면 맴도는 방향을 바꾼다.
    /// 잠수·도약 중이거나 NavMesh 위에 없으면 건너뛴다.
    /// </summary>
    private void TickProwl()
    {
        if (_burrowed || _jumping || BurrowMotion != BeastBurrowMotion.None || !_motor.IsReady || _prowlTarget == null) return;
        Vector3 offset = Vector3.ProjectOnPlane(transform.position - _prowlTarget.position, Vector3.up);
        float distance = offset.magnitude;
        if (distance < _prowlKeepAway) _prowlEscaping = true;
        else if (distance > _prowlKeepAway + _prowlBackHysteresis) _prowlEscaping = false;
        if (Time.time >= _nextProwlSwitch)
        {
            _prowlSign = -_prowlSign;
            _nextProwlSwitch = Time.time + UnityEngine.Random.Range(_prowlSwitchMin, Mathf.Max(_prowlSwitchMin, _prowlSwitchMax));
        }
        if (Time.time < _nextProwlDestination) return;
        _nextProwlDestination = Time.time + _orbitDestinationInterval;
        Vector3 away = distance > 0.01f ? offset / distance : transform.forward;
        _agent.speed = _prowlSpeed;
        _agent.acceleration = _prowlAcceleration;
        float reach = _prowlSpeed * _prowlLookAhead + 1f;
        Vector3 desired;
        if (_prowlEscaping)
        {
            // 돌아서 등지지 않도록 옆으로 크게 비껴 바깥쪽 호로 달린다.
            Vector3 side = Vector3.Cross(Vector3.up, away) * _prowlSign;
            desired = transform.position + (away * 0.6f + side).normalized * reach;
        }
        else
        {
            float ring = Mathf.Max(_prowlKeepAway + _prowlBackHysteresis, _prowlRadius);
            float step = _prowlSign * reach / ring * Mathf.Rad2Deg;
            desired = _prowlTarget.position + Quaternion.AngleAxis(step, Vector3.up) * away * ring;
        }
        desired = ClampToProwlArena(desired);
        // 공터 가장자리나 장애물에 막혀 더 갈 곳이 없으면 반대로 돈다.
        if (Vector3.ProjectOnPlane(desired - transform.position, Vector3.up).sqrMagnitude < 0.25f
            || !TryDestination(desired, out Vector3 destination))
        {
            _prowlSign = -_prowlSign;
            return;
        }
        _motor.MoveTo(destination, 0.1f);
    }

    /// <summary>position을 배회 공터 가장자리에서 _edgeMargin과 짐승 반경만큼 안쪽 원 안으로 당겨 반환한다.</summary>
    private Vector3 ClampToProwlArena(Vector3 position)
    {
        Vector3 fromCenter = Vector3.ProjectOnPlane(position - _prowlCenter, Vector3.up);
        float limit = Mathf.Max(0.5f, _prowlArenaRadius - _edgeMargin - _agent.radius);
        if (fromCenter.sqrMagnitude <= limit * limit) return position;
        Vector3 clamped = _prowlCenter + fromCenter.normalized * limit;
        clamped.y = position.y;
        return clamped;
    }

    /// <summary>
    /// 배회를 끝내고 저장한 이동 속도·가속도를 되돌린다. 달리던 속도는 멈추지 않고 남겨 바로 이어지는 잠수·질주가 그 기세를 이어받게 한다.
    /// </summary>
    public void EndProwl()
    {
        if (!_prowling) return;
        _prowling = false;
        _prowlTarget = null;
        _prowlEscaping = false;
        _agent.speed = _prowlBaseSpeed;
        _agent.acceleration = _prowlBaseAcceleration;
    }

    /// <summary>player 주변의 현재 각도에서 원형 이동을 시작하고 기존 이동 속도를 저장한다.</summary>
    public void BeginOrbit(Transform player)
    {
        _motor.Stop();
        _previousSpeed = _agent.speed;
        _agent.speed = _orbitSpeed;
        Vector3 offset = transform.position - player.position;
        _orbitAngle = Mathf.Atan2(offset.z, offset.x);
        _orbitSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        _nextOrbitDestination = 0f;
        _orbiting = true;
    }

    /// <summary>player 주변의 다음 원주 위치를 일정 간격으로 탐색하고 도달 가능하면 이동 목표를 갱신한다.</summary>
    public void TickOrbit(Transform player)
    {
        if (!_orbiting || Time.time < _nextOrbitDestination) return;
        _nextOrbitDestination = Time.time + _orbitDestinationInterval;
        _orbitAngle += _orbitSign * _orbitSpeed / _orbitRadius * _orbitDestinationInterval;
        Vector3 desired = player.position + new Vector3(Mathf.Cos(_orbitAngle), 0f,
            Mathf.Sin(_orbitAngle)) * _orbitRadius;
        if (TryDestination(desired, out Vector3 destination))
            _motor.MoveTo(destination, 0.1f);
    }

    /// <summary>원형 이동을 끝내고 NavMeshAgent의 기존 이동 속도를 복원한다.</summary>
    public void EndOrbit()
    {
        if (!_orbiting) return;
        _orbiting = false;
        _motor.Stop();
        _agent.speed = _previousSpeed;
    }

    /// <summary>
    /// center를 중심으로 한 반경 radius 원주를 따라 빠르게 달리기 시작한다.
    /// 기존 이동 속도·가속도를 저장하고 _edgeRunSpeed·_edgeRunAcceleration으로 바꾸며, 도는 방향은 첫 TickEdgeRun에서 플레이어 반대쪽으로 정한다.
    /// 가장자리에 닿은 뒤 달린 각도 EdgeRunTraveledAngle을 0부터 센다.
    /// </summary>
    public void BeginEdgeRun(Vector3 center, float radius)
    {
        _motor.Stop();
        _previousSpeed = _agent.speed;
        _previousAcceleration = _agent.acceleration;
        _agent.speed = _edgeRunSpeed;
        _agent.acceleration = _edgeRunAcceleration;
        _edgeCenter = center;
        _edgeRadius = Mathf.Max(1f, radius);
        _edgeSign = 0f;
        _edgeReached = false;
        EdgeRunTraveledAngle = 0f;
        _nextOrbitDestination = 0f;
        _edgeRunning = true;
    }

    /// <summary>
    /// player 방위를 피해 원주의 반대편 호를 달리도록 일정 간격으로 앞쪽 원주 지점을 이동 목표로 정한다.
    /// 처음에는 플레이어에게서 멀어지는 쪽으로 돌고, 반대편 호 끝(플레이어 방위 _edgeRunAvoidAngle)에 닿으면 도는 방향 _edgeSign을 뒤집는다.
    /// 가장자리 근처를 달린 각도를 EdgeRunTraveledAngle에 더한다.
    /// </summary>
    public void TickEdgeRun(Transform player)
    {
        if (!_edgeRunning || Time.time < _nextOrbitDestination) return;
        _nextOrbitDestination = Time.time + _orbitDestinationInterval;
        float angle = AngleAroundEdge(transform.position);
        float playerAngle = AngleAroundEdge(player.position);
        if (_edgeSign == 0f)
            _edgeSign = Mathf.DeltaAngle(playerAngle, angle) >= 0f ? 1f : -1f;
        float fromCenter = Vector3.ProjectOnPlane(transform.position - _edgeCenter, Vector3.up).magnitude;
        if (Mathf.Abs(fromCenter - _edgeRadius) < 2f)
        {
            if (_edgeReached) EdgeRunTraveledAngle += Mathf.Abs(Mathf.DeltaAngle(_lastEdgeAngle, angle));
            _edgeReached = true;
            _lastEdgeAngle = angle;
        }
        float step = _edgeSign * _edgeRunSpeed * _edgeRunLookAhead / _edgeRadius * Mathf.Rad2Deg;
        float current = Mathf.Abs(Mathf.DeltaAngle(playerAngle, angle));
        float next = Mathf.Abs(Mathf.DeltaAngle(playerAngle, angle + step));
        // 지금 위치가 반대편 호 끝에 닿았고 플레이어 쪽으로 다가가는 중일 때만 돌아서서 호 전체를 달린다.
        if (current < _edgeRunAvoidAngle && next < current)
        {
            _edgeSign = -_edgeSign;
            step = -step;
        }
        float target = (angle + step) * Mathf.Deg2Rad;
        Vector3 desired = _edgeCenter + new Vector3(Mathf.Cos(target), 0f, Mathf.Sin(target)) * _edgeRadius;
        if (TryDestination(desired, out Vector3 destination))
            _motor.MoveTo(destination, 0.1f);
    }

    /// <summary>가장자리 질주를 끝내고 NavMeshAgent의 기존 이동 속도·가속도를 복원한다.</summary>
    public void EndEdgeRun()
    {
        if (!_edgeRunning) return;
        _edgeRunning = false;
        _motor.Stop();
        _agent.speed = _previousSpeed;
        _agent.acceleration = _previousAcceleration;
    }

    /// <summary>position이 가장자리 질주 중심 _edgeCenter에서 놓인 수평 방위각(도)을 반환한다.</summary>
    private float AngleAroundEdge(Vector3 position)
    {
        return Mathf.Atan2(position.z - _edgeCenter.z, position.x - _edgeCenter.x) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 땅 위에 있으면 지금 자리에서 울음소리와 큰 물 파동을 내며 _diveDuration 동안 땅속으로 뛰어든다.
    /// 달리던 속도나 덮치기 착지 기세 _momentum 중 큰 쪽을 _diveMinSpeed~_diveMaxSpeed로 잘라 그 방향으로 점점 느려지며 앞으로 뛰어든다.
    /// BurrowMotion과 BurrowDepth로 진행을 알리고 끝나면 피격 판정을 끈 잠수 상태가 된다. 이미 잠수 중이면 바로 끝낸다.
    /// </summary>
    public IEnumerator Dive()
    {
        if (_burrowed) yield break;
        Vector3 velocity = Vector3.ProjectOnPlane(_agent.velocity, Vector3.up);
        if (_momentum.sqrMagnitude > velocity.sqrMagnitude) velocity = _momentum;
        _momentum = Vector3.zero;
        Vector3 direction = velocity.sqrMagnitude > 0.01f
            ? velocity.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float speed = Mathf.Clamp(velocity.magnitude, _diveMinSpeed, Mathf.Max(_diveMinSpeed, _diveMaxSpeed));
        _motor.Stop();
        if (direction.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(direction);
        EmitBurrowSignal(transform.position);
        BurrowMotion = BeastBurrowMotion.Diving;
        float elapsed = 0f;
        while (elapsed < _diveDuration && _health.IsAlive)
        {
            elapsed += Time.deltaTime;
            BurrowDepth = Mathf.Clamp01(elapsed / _diveDuration);
            // 달리던 기세 그대로 앞으로 뛰어들며 물속으로 잠길수록 느려진다.
            MoveAlongGround(direction, speed * (1f - 0.6f * BurrowDepth) * Time.deltaTime);
            yield return null;
        }
        BurrowMotion = BeastBurrowMotion.None;
        SetBurrowed(true);
    }

    /// <summary>
    /// 잠수한 채 strikeAt(게임 시간) 덮치기에 맞춰 튀어나오고, 붉게 물들며 웅크린 뒤 플레이어를 지나 반대편 가장자리까지 덮친다.
    /// 이전 덮치기로 내려앉은 자리가 있으면 그 자리에서 다시 나오고, 없으면 center·radius 원형 공터의 플레이어 정면 쪽 가장자리로 옮겨 나온다.
    /// 땅 위에 있으면 먼저 뛰어든다. player나 자신이 쓰러지면 남은 단계를 건너뛰고 땅 위에서 끝낸다.
    /// </summary>
    public IEnumerator Ambush(KatanaDamageReceiver player, float strikeAt, Vector3 center, float radius)
    {
        if (!_burrowed) yield return Dive();
        float emergeAt = strikeAt - _biteTelegraph - _emergeDuration;
        while (Time.time < emergeAt && CanAttack(player)) yield return null;
        if (!CanAttack(player)) yield break;
        Vector3 start = _hasLandingSpot || !TryGetEmergePosition(player, center, radius, out Vector3 position)
            ? transform.position : position;
        yield return Emerge(start, player);
        if (CanAttack(player)) yield return Bite(player, center, radius);
    }

    /// <summary>잠수 중이면 지금 자리에서 울음소리·물 파동과 함께 튀어나와 땅 위로 돌아온다. 땅 위면 아무것도 하지 않는다.</summary>
    public IEnumerator Resurface()
    {
        if (!_burrowed) yield break;
        yield return Emerge(transform.position, null);
    }

    /// <summary>
    /// 잠수한 채 position으로 옮긴 뒤 울음소리와 큰 물 파동을 내고 _emergeDuration 동안 땅에서 튀어나온다.
    /// player가 있으면 그쪽을 바라보며 나오고, 시작과 함께 피격 판정을 다시 켠다.
    /// 튀어나오는 동안 바라보는 쪽으로 _emergeLeapSpeed로 뛰쳐나가 다음 동작으로 기세가 이어지게 한다.
    /// </summary>
    private IEnumerator Emerge(Vector3 position, KatanaDamageReceiver player)
    {
        if ((position - transform.position).sqrMagnitude > 0.0001f) _agent.Warp(position);
        if (player != null) FaceTowards(player.transform.position);
        SetBurrowed(false);
        BurrowDepth = 1f;
        BurrowMotion = BeastBurrowMotion.Emerging;
        EmitBurrowSignal(transform.position);
        float elapsed = 0f;
        while (elapsed < _emergeDuration && _health.IsAlive)
        {
            elapsed += Time.deltaTime;
            BurrowDepth = 1f - Mathf.Clamp01(elapsed / _emergeDuration);
            if (player != null) FaceTowards(player.transform.position);
            MoveAlongGround(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized,
                _emergeLeapSpeed * Time.deltaTime);
            yield return null;
        }
        BurrowDepth = 0f;
        BurrowMotion = BeastBurrowMotion.None;
    }

    /// <summary>
    /// 공격 전조를 알리고 낮춘 퍼펙트 패링음을 울리며 _biteTelegraph 동안 웅크려 연기를 붉게 물들인 뒤, 그 순간 플레이어 쪽으로 방향을 고정해
    /// 플레이어 위치를 반드시 지나 반대편 가장자리(적어도 플레이어 너머 _pounceOvershoot)까지 _biteDuration 동안 낮은 포물선으로 덮친다.
    /// 정해진 직선 경로를 장애물과 상관없이 끝까지 날아가며, 바닥에 투영한 경로로 첫 접촉에서 한 번만 전방 막기·쳐내기를 확인해 피해를 준다.
    /// 내려앉은 자리를 다음 덮치기 출발점으로 기록하고 NavMesh 위치를 복원한 뒤 붉은색을 되돌린다.
    /// </summary>
    private IEnumerator Bite(KatanaDamageReceiver player, Vector3 center, float radius)
    {
        _motor.Stop();
        AttackPreparing?.Invoke();
        if (_chargeSound != SoundId.None)
            Managers.Sound.Play(_chargeSound, transform.position, _chargeSoundVolume, gameObject, true, _chargeSoundPitch);
        _warning.BeginWarning(_biteTelegraph);
        _preparing = true;
        float until = Time.time + _biteTelegraph;
        while (Time.time < until && CanAttack(player))
        {
            FaceTowards(player.transform.position);
            yield return null;
        }
        _preparing = false;
        if (!CanAttack(player))
        {
            _warning.EndWarning();
            yield break;
        }
        Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
        Vector3 direction = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward;
        // 플레이어를 반드시 지나 원을 빠져나가는 반대편 가장자리에 내려앉도록 정해진 직선으로 뛴다.
        float length = Mathf.Max(toPlayer.magnitude + _pounceOvershoot,
            GetCircleExitDistance(transform.position, direction, center, radius) - _edgeMargin);
        Vector3 start = transform.position;
        Vector3 landing = start + direction * length;
        _navigationPosition = _agent.nextPosition;
        _previousUpdatePosition = _agent.updatePosition;
        _previousUpdateRotation = _agent.updateRotation;
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _jumping = true;
        _attackResolved = false;
        _biting = true;
        AttackStarted?.Invoke();
        Quaternion facing = Quaternion.LookRotation(direction);
        Vector3 groundPrevious = start;
        float elapsed = 0f;
        while (elapsed < _biteDuration && CanAttack(player))
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / _biteDuration);
            Vector3 ground = Vector3.Lerp(start, landing, progress);
            Vector3 next = ground + Vector3.up * (4f * _pounceHeight * progress * (1f - progress));
            transform.SetPositionAndRotation(next, facing);
            // 덮치는 몸이 플레이어 위를 지나도 맞도록 비행 경로를 바닥 높이로 내려 접촉을 검사한다.
            Vector3 groundStep = ground - groundPrevious;
            if (!_attackResolved && groundStep.sqrMagnitude > 0.000001f)
                CheckChargeContact(player, groundPrevious, groundStep.normalized, groundStep.magnitude,
                    _biteDamage, "TwinBeastBite");
            groundPrevious = ground;
            yield return null;
        }
        if (_health.IsAlive) Landed?.Invoke();
        RestoreNavigation();
        _hasLandingSpot = true;
        // 착지한 기세를 남겨 바로 이어지는 뛰어들기가 덮치던 방향으로 미끄러지듯 이어지게 한다.
        _momentum = direction * Mathf.Min(length / _biteDuration, _diveMaxSpeed);
        _biting = false;
        _motor.Stop();
        _warning.EndWarning();
    }

    /// <summary>
    /// 잠수 기습의 웅크림·물기·붉은 전조만 정리하고 잠수 여부와 위치는 그대로 둔다.
    /// 거합 차징 중 맞은 짐승이 하던 기습을 멈추고 바로 다시 뛰어들 때 쓴다.
    /// </summary>
    public void InterruptAmbush()
    {
        _preparing = false;
        _biting = false;
        // 덮치며 날던 중이면 지금 자리 아래 지면으로 내려 NavMesh 위치를 되돌린다.
        RestoreNavigation();
        _warning.EndWarning();
        if (!_burrowed && BurrowMotion == BeastBurrowMotion.Emerging)
        {
            BurrowMotion = BeastBurrowMotion.None;
            BurrowDepth = 0f;
        }
        _motor.Stop();
    }

    /// <summary>
    /// player의 수평 시점 기준 _emergeArc 범위 방향으로 뻗은 선이 center·radius 원 가장자리와 만나는 지점 중
    /// 플레이어와 _emergeDistance 이상 떨어진 빈 NavMesh 지면을 찾아 position으로 반환한다. 지점은 _edgeMargin만큼 원 안쪽으로 당긴다.
    /// 경로가 끊기거나 몸이 들어갈 공간이 없는 후보는 버리며 EMERGE_ATTEMPTS번 안에 찾지 못하면 false를 반환한다.
    /// </summary>
    private bool TryGetEmergePosition(KatanaDamageReceiver player, Vector3 center, float radius, out Vector3 position)
    {
        position = transform.position;
        Vector3 origin = player.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(player.GetComponent<PlayerMotor>().ViewForward, Vector3.up).normalized;
        for (int attempt = 0; attempt < EMERGE_ATTEMPTS; attempt++)
        {
            float angle = UnityEngine.Random.Range(-_emergeArc * 0.5f, _emergeArc * 0.5f);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
            float distance = GetCircleExitDistance(origin, direction, center, radius) - _edgeMargin;
            if (distance < _emergeDistance) continue;
            if (!TryDestination(origin + direction * distance, out Vector3 candidate) || IsBlocked(candidate)) continue;
            position = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// origin에서 수평 direction으로 나아갈 때 center 중심 radius 원을 빠져나가는 거리(미터)를 반환한다.
    /// origin이 원 밖이거나 만나지 않으면 0을 반환한다.
    /// </summary>
    private static float GetCircleExitDistance(Vector3 origin, Vector3 direction, Vector3 center, float radius)
    {
        Vector2 offset = new Vector2(origin.x - center.x, origin.z - center.z);
        Vector2 heading = new Vector2(direction.x, direction.z).normalized;
        float along = Vector2.Dot(offset, heading);
        float discriminant = along * along - (offset.sqrMagnitude - radius * radius);
        if (discriminant < 0f) return 0f;
        return Mathf.Max(0f, -along + Mathf.Sqrt(discriminant));
    }

    /// <summary>position에 짐승 이동 캡슐을 세웠을 때 자신·플레이어를 제외한 장애물과 겹치는지 반환한다.</summary>
    private bool IsBlocked(Vector3 position)
    {
        float radius = _agent.radius;
        int count = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * (radius + 0.05f),
            position + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius), radius,
            _contacts, _obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform contact = _contacts[i].transform;
            if (contact.IsChildOf(transform) || contact.GetComponentInParent<PlayerHealth>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>direction으로 distance만큼 NavMesh 지면을 따라 옮기고, 걸을 수 있는 지면 끝에 닿으면 그 앞에서 멈춘다.</summary>
    private void MoveAlongGround(Vector3 direction, float distance)
    {
        if (distance <= 0f || direction.sqrMagnitude < 0.0001f || !_motor.IsReady) return;
        Vector3 destination = transform.position + direction * distance;
        if (_agent.Raycast(destination, out NavMeshHit boundary)) destination = boundary.position;
        _agent.Move(destination - transform.position);
    }

    /// <summary>target 쪽으로 수평 방향만 돌려 바라본다.</summary>
    private void FaceTowards(Vector3 target)
    {
        Vector3 facing = Vector3.ProjectOnPlane(target - transform.position, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing);
    }

    /// <summary>
    /// burrowed로 잠수 상태를 바꾸고 피격 판정 _hitCollider를 끄거나 켠다.
    /// 잠수하면 BurrowDepth를 1, 땅 위로 돌아오면 지금 깊이를 그대로 두어 출현 동작이 이어지게 한다.
    /// </summary>
    private void SetBurrowed(bool burrowed)
    {
        _burrowed = burrowed;
        _hitCollider.enabled = !burrowed;
        if (burrowed) BurrowDepth = 1f;
    }

    /// <summary>position 지면에 큰 물 파동을 시야 맵에 등록하고 울음소리를 _burrowCryVolume으로 크게 재생한다.</summary>
    private void EmitBurrowSignal(Vector3 position)
    {
        if (_burrowCry != SoundId.None)
            Managers.Sound.Play(_burrowCry, position, _burrowCryVolume, gameObject, true);
        if (_revealMap == null && Game.IsInitialized && Game.Instance.SensoryManager != null)
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
        if (_revealMap != null) _revealMap.EmitRipple(position, _burrowRipple, transform);
    }

    /// <summary>전조 후 player 방향을 고정하고 좌우 교대 목표로 돌진하며 접촉 피해를 한 번 적용한다.</summary>
    public IEnumerator Zigzag(KatanaDamageReceiver player)
    {
        _motor.Stop();
        AttackPreparing?.Invoke();
        if (_preparationSound != SoundId.None)
            Managers.Sound.Play(_preparationSound, transform.position, 1f, gameObject);
        yield return PrepareMeleeAttack(player, _chargePreparation);
        if (!_preparationSucceeded) yield break;
        if (!CanAttack(player)) yield break;
        Vector3 start = transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(player.transform.position - start, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.000001f) forward = transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        _attackResolved = false;
        AttackStarted?.Invoke();
        for (int i = 1; i <= _zigzagSegments; i++)
        {
            float side = i == _zigzagSegments ? 0f : (i % 2 == 1 ? 1f : -1f) * _zigzagWidth;
            Vector3 desired = start + forward * (_chargeDistance * i / _zigzagSegments) + right * side;
            if (!TryDestination(desired, out Vector3 destination)) break;
            float until = Time.time + Vector3.Distance(transform.position, destination) / _chargeSpeed + 0.2f;
            while (CanAttack(player) && Time.time < until
                && Vector3.Distance(transform.position, destination) > 0.05f)
            {
                Vector3 direction = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).normalized;
                float distance = Mathf.Min(_chargeSpeed * Time.deltaTime,
                    Vector3.Distance(transform.position, destination));
                float travel = GetClearDistance(direction, distance);
                if (travel > 0f)
                {
                    Vector3 previous = transform.position;
                    _agent.Move(direction * travel);
                    transform.rotation = Quaternion.LookRotation(direction);
                    CheckChargeContact(player, previous, direction, travel, _chargeDamage, "TwinBeastCharge");
                    if ((transform.position - previous).sqrMagnitude < 0.000001f && Time.deltaTime > 0f)
                        yield break;
                }
                if (_attackResolved || travel + 0.001f < distance) yield break;
                yield return null;
            }
        }
        _motor.Stop();
    }

    /// <summary>전조 종료 시 player 위치로 포물선 점프를 수행하고 착지 피해와 NavMesh 복원을 처리한다.</summary>
    public IEnumerator Jump(KatanaDamageReceiver player)
    {
        _motor.Stop();
        AttackPreparing?.Invoke();
        if (_preparationSound != SoundId.None)
            Managers.Sound.Play(_preparationSound, transform.position, 1f, gameObject);
        yield return PrepareMeleeAttack(player, _jumpPreparation);
        if (!_preparationSucceeded) yield break;
        if (!CanAttack(player) || !TryDestination(player.transform.position, out Vector3 destination)) yield break;
        Vector3 start = transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(destination - start, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.000001f) direction = transform.forward;
        if (GetClearDistance(direction, Vector3.Distance(start, destination)) + 0.05f
            < Vector3.Distance(start, destination)) yield break;
        _navigationPosition = _agent.nextPosition;
        _previousUpdatePosition = _agent.updatePosition;
        _previousUpdateRotation = _agent.updateRotation;
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _jumping = true;
        _attackResolved = false;
        AttackStarted?.Invoke();
        try
        {
            float elapsed = 0f;
            while (elapsed < _jumpDuration && _health.IsAlive && player != null && player.IsAlive)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / _jumpDuration);
                Vector3 next = Vector3.Lerp(start, destination, progress)
                    + Vector3.up * (4f * _jumpHeight * progress * (1f - progress));
                Vector3 movement = next - transform.position;
                if (movement.sqrMagnitude > 0.000001f
                    && GetClearDistance(movement.normalized, movement.magnitude) + 0.001f < movement.magnitude)
                    yield break;
                transform.SetPositionAndRotation(next, Quaternion.LookRotation(direction));
                yield return null;
            }
            if (_health.IsAlive && player != null && player.IsAlive)
            {
                transform.position = destination;
                Landed?.Invoke();
                Vector3 offset = Vector3.ProjectOnPlane(player.transform.position - destination, Vector3.up);
                if (offset.sqrMagnitude <= _landingRadius * _landingRadius)
                    ApplyAttack(player, _jumpDamage, direction, destination, "TwinBeastJump");
            }
        }
        finally
        {
            RestoreNavigation();
        }
    }

    /// <summary>
    /// 준비 중 상태 _preparing을 켠 채 PrepareHeartbeat로 점프·돌진 준비를 실행하고 끝나면 끈다.
    /// player와 legacyDuration을 그대로 전달하며 성공 여부는 _preparationSucceeded에 남는다.
    /// </summary>
    private IEnumerator PrepareMeleeAttack(KatanaDamageReceiver player, float legacyDuration)
    {
        _preparing = true;
        yield return PrepareHeartbeat(player, legacyDuration);
        _preparing = false;
    }

    /// <summary>
    /// player 생존과 이동 상태를 확인하며 설정한 첫 대기·횟수·간격·마지막 지연으로 점프·돌진 준비를 실행한다.
    /// 박동을 끄면 legacyDuration을 사용하고 성공 여부를 저장하며 중단·큰 지연은 잔여 박동을 정리한다.
    /// </summary>
    private IEnumerator PrepareHeartbeat(KatanaDamageReceiver player, float legacyDuration)
    {
        StopHeartbeat();
        _preparationSucceeded = false;
        if (!_useHeartbeatTelegraph)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, legacyDuration));
            _preparationSucceeded = CanAttack(player);
            yield break;
        }
        int count = Mathf.Max(1, _heartbeatCount);
        float firstDelay = Mathf.Max(0f, _firstHeartbeatDelay);
        float minimum = Mathf.Max(0.1f, _minimumHeartbeatInterval);
        float interval = count > 1
            ? UnityEngine.Random.Range(minimum, Mathf.Max(minimum, _maximumHeartbeatInterval)) : 0f;
        float duration = firstDelay + (count - 1) * interval + Mathf.Max(0.05f, _postHeartbeatAttackDelay);
        float started = Time.time;
        int played = 0;
        while (CanAttack(player))
        {
            yield return null;
            if (!CanAttack(player)) break;
            if (Time.timeScale <= 0f)
            {
                StopHeartbeat();
                while (Time.timeScale <= 0f && CanAttack(player)) yield return null;
                if (!CanAttack(player)) break;
                started = Time.time;
                played = 0;
                continue;
            }
            float now = Time.time;
            float nextAt = started + firstDelay + interval * played;
            float attackAt = started + duration;
            if (played < count && now >= nextAt)
            {
                if (now >= attackAt || now - nextAt > MAX_RHYTHM_DELAY) break;
                played++;
                _playingHeartbeat = EnemySoundSettings.GetSound(EnemySoundAction.Charge, EnemySoundStage.Heartbeat);
                if (_playingHeartbeat != SoundId.None) Managers.Sound.PlayAttached(_playingHeartbeat, gameObject);
            }
            if (now < attackAt) continue;
            if (now - attackAt > MAX_RHYTHM_DELAY || played < count) break;
            _preparationSucceeded = true;
            yield break;
        }
        StopHeartbeat();
    }

    /// <summary>보관한 박동 ID와 현재 짐승 소유자만 강제 정지하고 ID를 비운다. 피격·패링·다른 보스의 소리는 유지한다.</summary>
    private void StopHeartbeat()
    {
        if (_playingHeartbeat != SoundId.None && Managers.IsInitialized)
            Managers.Sound.StopFor(gameObject, _playingHeartbeat, forceStop: true);
        _playingHeartbeat = SoundId.None;
    }

    /// <summary>player와 자신이 생존하고 지면 이동이 가능한지 반환한다.</summary>
    private bool CanAttack(KatanaDamageReceiver player)
    {
        return _health.IsAlive && player != null && player.IsAlive && _motor.IsReady;
    }

    /// <summary>desired 주변의 도달 가능한 지면을 destination으로 반환하고 불완전한 경로를 거부한다.</summary>
    private bool TryDestination(Vector3 desired, out Vector3 destination)
    {
        desired.y = _agent.nextPosition.y;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        bool found = NavMesh.SamplePosition(desired, out NavMeshHit hit, 1f, filter);
        destination = hit.position;
        return found && _agent.CalculatePath(destination, _path) && _path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>direction·distance 구간의 장애물을 검사하고 자신과 플레이어를 제외한 이동 가능 거리를 반환한다.</summary>
    private float GetClearDistance(Vector3 direction, float distance)
    {
        float radius = _agent.radius;
        Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
        Vector3 top = transform.position + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _hits,
            distance, _obstacleMask, QueryTriggerInteraction.Ignore);
        float clear = distance;
        for (int i = 0; i < count; i++)
        {
            Transform obstacle = _hits[i].transform;
            if (obstacle.IsChildOf(transform) || obstacle.GetComponentInParent<PlayerHealth>() != null
                || obstacle.GetComponentInParent<HumanBossAttack>() != null) continue;
            clear = Mathf.Min(clear, Mathf.Max(0f, _hits[i].distance - 0.05f));
        }
        if (!_jumping && _agent.Raycast(transform.position + direction * clear, out NavMeshHit boundary))
            clear = Mathf.Min(clear, Mathf.Max(0f, Vector3.Distance(transform.position, boundary.position) - 0.05f));
        return clear;
    }

    /// <summary>start부터 direction·distance 구간과 현재 겹침을 검사해 player에게 damage·pattern 접촉 피해를 한 번 적용한다.</summary>
    private void CheckChargeContact(KatanaDamageReceiver player, Vector3 start, Vector3 direction, float distance,
        float damage, string pattern)
    {
        if (_attackResolved) return;
        float radius = Mathf.Max(_contactRadius, _agent.radius);
        Vector3 bottom = start + Vector3.up * radius;
        Vector3 top = start + Vector3.up * Mathf.Max(radius, _agent.height - radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _hits,
            distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_hits[i].collider.GetComponentInParent<PlayerHealth>() != player) continue;
            ApplyAttack(player, damage, direction, _hits[i].point, pattern);
            return;
        }
        count = Physics.OverlapCapsuleNonAlloc(transform.position + Vector3.up * radius,
            transform.position + Vector3.up * Mathf.Max(radius, _agent.height - radius), radius,
            _contacts, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_contacts[i].GetComponentInParent<PlayerHealth>() != player) continue;
            ApplyAttack(player, damage, direction, _contacts[i].ClosestPoint(bottom), pattern);
            return;
        }
    }

    /// <summary>player의 damage·direction·point·pattern 피해를 전방 가드·패링으로 확인하고 실패 시 체력을 차감한다.</summary>
    private void ApplyAttack(KatanaDamageReceiver player, float damage, Vector3 direction, Vector3 point, string pattern)
    {
        _attackResolved = true;
        DamageContext context = new DamageContext(damage, gameObject, point, direction, pattern);
        if (!player.TryDefendProjectile(context, out bool parried)) player.TakeDamage(context);
        else if (parried && _parrySound != SoundId.None)
        {
            Managers.Sound.StopFor(gameObject, SoundId.EnemyGetParryed, forceStop: true);
            Managers.Sound.Play(_parrySound, transform.position, owner: gameObject, playDuringMovement: true);
        }
    }

    /// <summary>점프 종료·중단 시 유효한 지면으로 복귀하고 위치·회전 자동 갱신을 복원한다.</summary>
    private void RestoreNavigation()
    {
        if (!_jumping) return;
        _jumping = false;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        Vector3 desired = transform.position;
        desired.y = _navigationPosition.y;
        // 덮치기는 정해진 직선으로 내려앉아 걸을 수 있는 지면 밖일 수 있으므로 가까운 지면을 넓게 찾는다.
        Vector3 destination = NavMesh.SamplePosition(desired, out NavMeshHit hit, 5f, filter)
            ? hit.position : _navigationPosition;
        _agent.updatePosition = _previousUpdatePosition;
        _agent.updateRotation = _previousUpdateRotation;
        if (_agent.isActiveAndEnabled) _agent.Warp(destination);
    }

    /// <summary>
    /// 배회·원형 이동·가장자리 질주·점프·잠수 기습을 중단하고 기존 NavMesh 설정과 이동 정지 상태를 복원한다.
    /// 잠수 중이었으면 지금 자리에서 바로 땅 위로 돌려 피격 판정을 켠다.
    /// </summary>
    public void CancelAttack()
    {
        _preparationSucceeded = false;
        // 패턴 코루틴이 외부에서 멈추면 준비 종료 처리가 실행되지 않으므로 여기서 함께 끈다.
        _preparing = false;
        _biting = false;
        StopHeartbeat();
        EndProwl();
        EndOrbit();
        EndEdgeRun();
        RestoreNavigation();
        _warning.EndWarning();
        // 잠수·출현 도중 멈춰도 다음 패턴에서 보이고 맞을 수 있도록 땅 위 상태로 되돌린다.
        if (_burrowed) SetBurrowed(false);
        // 패턴이 끝나면 다음 패턴의 첫 덮치기는 다시 가장자리를 골라 나온다.
        _hasLandingSpot = false;
        _momentum = Vector3.zero;
        BurrowDepth = 0f;
        BurrowMotion = BeastBurrowMotion.None;
        _motor.Stop();
    }

    void OnDisable()
    {
        CancelAttack();
    }
}
