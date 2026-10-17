using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.AI;

using BlindBlade.Combat.Contracts;

public enum BossState { Idle, Roaming, Charging, ChainCharge, Duel, Stunned, Dead, Disappeared, Ranged }

/// <summary>보스의 원거리·돌진 순환과 피격 전환 및 전투 종료 수명을 관리한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(EnemySensor))]
public sealed class BossController : MonoBehaviour
{
    private const float MAX_RHYTHM_DELAY = 0.1f;
    private const float BACKSTEP_ANGLE_STEP = 30f;
    private const int BACKSTEP_CANDIDATE_COUNT = 9;
    private const float MIN_BACKSTEP_GAIN = 0.5f;
    private const int ESCAPE_DIRECTION_COUNT = 32;
    private const float ESCAPE_RING_STEP = 2f;
    private const string COUNTER_TIME_OWNER_A = "BossController.CounterSlashA";
    private const string COUNTER_TIME_OWNER_B = "BossController.CounterSlashB";
    private const int COUNTER_RETREAT_DIRECTION_COUNT = 24;
    private const float COUNTER_RETREAT_STEP = 0.5f;
    private const float COUNTER_RETREAT_PLAYER_CLEARANCE = 1.5f;
    private const float COUNTER_RETREAT_FLOOR_CLEARANCE = 0.2f;
    private const string REFLECTED_PROJECTILE_PATTERN_ID = "ReflectedProjectile";

    [Header("전투 연결")]
    [SerializeField, Min(0f), Tooltip("피격 후 다음 패턴으로 넘어가기 전 회복 시간(초)")]
    private float _hitRecoveryDuration = 0.25f;
    private EnemyHealth _health;
    private EnemyMotor _motor;
    private EnemySensor _sensor;
    private NavMeshAgent _agent;
    private NavMeshPath _path;
    private PlayerController _player;
    private PlayerMotor _playerMotor;
    private PlayerHealth _playerHealth;
    private KatanaCombatCoordinator _playerCombat;
    private PlayerEmpower _playerEmpower;
    private Coroutine _routine;
    private BossState _state;
    private bool _started;
    private BossState _resumeState;

    [Header("패턴 사이 대기")]
    [SerializeField, Min(0f), Tooltip("패턴 사이 최소 대기 시간(초)")]
    private float _patternWaitMin = 1f;
    [SerializeField, Min(0f), Tooltip("패턴 사이 최대 대기 시간(초)")]
    private float _patternWaitMax = 2f;
    private BossState _nextPattern = BossState.Ranged;

    [Header("박동 공격 전조")]
    [SerializeField] private bool _useHeartbeatTelegraph = true;
    [SerializeField, Min(1), InspectorName("박동 횟수"), Tooltip("발사·돌진 전 박동 횟수입니다. 마지막 박동 이후 공격 지연을 적용합니다.")]
    private int _heartbeatCount = 1;
    [SerializeField, Min(0f), InspectorName("첫 박동 전 대기 (초)"), Tooltip("준비 시작부터 첫 박동까지의 고정 대기 시간입니다. 박동 간격과 독립적으로 설정합니다.")]
    private float _firstHeartbeatDelay = 0.2f;
    [SerializeField, Min(0.1f), InspectorName("박동 간격 최소 (초)"), Tooltip("두 박동 사이의 최소 간격(초). 발사·돌진마다 한 번 선택합니다.")]
    private float _minimumHeartbeatInterval = 0.4f;
    [SerializeField, Min(0.1f), InspectorName("박동 간격 최대 (초)"), Tooltip("두 박동 사이의 최대 간격(초). 최소값보다 작으면 최소값을 사용합니다.")]
    private float _maximumHeartbeatInterval = 0.55f;
    [SerializeField, Min(0.05f), InspectorName("마지막 박동 후 공격 지연 (초)"), Tooltip("마지막 박동부터 발사·돌진까지의 고정 지연(초)")]
    private float _postHeartbeatAttackDelay = 0.2f;
    private bool _preparationSucceeded;
    public event Action Heartbeat;
    public event Action PreparationCancelled;

    [Header("원거리 공격")]
    [SerializeField] private EnemyProjectile _projectilePrefab;
    [SerializeField, Min(0f)] private float _rangedDamage = 10f;
    [SerializeField, Min(0.1f)] private float _rangedPreparation = 0.8f;
    [SerializeField, Min(1)] private int _rangedShots = 3;
    [SerializeField, Min(0.1f), Tooltip("발사 후 다음 전조까지의 휴식 시간(초)")]
    private float _rangedInterval = 0.8f;
    [SerializeField, Min(0.01f), Tooltip("3연발 원거리 수리검의 비행 속도 배율. 도주 난사 수리검은 프리팹 속도를 그대로 씁니다.")]
    private float _rangedProjectileSpeedMultiplier = 1.5f;
    private int _telemetryAttackId;

    public event Action RangedPreparing;
    public event Action RangedFiring;

    [Header("원거리 백스텝")]
    [SerializeField, Min(0f), Tooltip("원거리 패턴 시작 시 플레이어에게서 멀어지는 거리(미터). 0이면 백스텝하지 않는다.")]
    private float _rangedBackstepDistance = 10f;
    [SerializeField, Min(0.1f), Tooltip("백스텝 이동 속도(미터/초)")]
    private float _rangedBackstepSpeed = 12f;

    [Header("연속 피해 도주 난사")]
    [SerializeField, Min(0.1f), Tooltip("이 시간(초) 안의 누적 피해로 도주 난사 발동을 판단한다.")]
    private float _burstDamageWindow = 4f;
    [SerializeField, Min(1f), Tooltip("시간 안에 이 피해 이상을 받으면 먼 곳으로 순간이동해 난사한다.")]
    private float _burstDamageThreshold = 30f;
    [SerializeField, Min(1f), Tooltip("도주 위치가 플레이어와 가져야 하는 최소 수평 거리(미터)")]
    private float _escapeMinDistance = 10f;
    [SerializeField, Min(1f), Tooltip("도주 위치를 찾는 플레이어 기준 최대 수평 거리(미터)")]
    private float _escapeMaxDistance = 45f;
    [SerializeField, Min(0.1f), Tooltip("순간이동 후 첫 난사까지의 준비 시간(초)")]
    private float _barragePreparation = 0.6f;
    [SerializeField, Min(1), Tooltip("왼쪽·가운데·오른쪽 3발 묶음의 발사 횟수")]
    private int _barrageVolleys = 4;
    [SerializeField, Min(0.05f), Tooltip("난사 묶음 사이 간격(초)")]
    private float _barrageInterval = 0.5f;
    [SerializeField, Range(0f, 90f), Tooltip("가운데 수리검 기준 좌우 수리검의 수평 각도(도)")]
    private float _barrageSpreadAngle = 20f;
    [SerializeField, Tooltip("첫 묶음이 조준하는 플레이어 발 기준 높이(미터)")]
    private float _barrageTopAimHeight = 3.2f;
    [SerializeField, Tooltip("마지막 묶음이 조준하는 플레이어 발 기준 높이(미터)")]
    private float _barrageBottomAimHeight = 0.2f;
    private int[] _barrageAttackIds;
    private bool[] _barrageAttackLaunched;
    private readonly Queue<(float At, float Amount)> _recentDamage = new Queue<(float At, float Amount)>();

    [Header("돌진")]
    [SerializeField, Min(0.1f), Tooltip("박동 전조를 끈 경우 사용하는 돌진 준비 시간(초)")]
    private float _chargePreparation = 0.9f;
    [SerializeField, Min(0.1f)] private float _chargeSpeed = 14f;
    [SerializeField, Min(0.1f)] private float _chargeDistance = 12f;
    [SerializeField, Min(0.05f)] private float _chargeDuration = 1f;
    [SerializeField, Min(0f)] private float _chargeDamage = 15f;
    [SerializeField, Min(0.1f)] private float _chargeHitRadius = 0.65f;
    [SerializeField, Min(0f)] private float _chargePushDistance = 3f;
    [SerializeField, Min(0.01f)] private float _chargePushDuration = 0.16f;
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    private readonly RaycastHit[] _obstacleHits = new RaycastHit[16];
    private readonly Collider[] _chargeContacts = new Collider[16];

    public event Action ChargePreparing;
    public event Action ChargeStarted;
    public event Action Teleported;

    [Header("빠른 재배치")]
    [SerializeField, Min(0.1f), Tooltip("패턴 위치로 이동하는 속도(미터/초)")]
    private float _repositionSpeed = 25f;
    private bool _repositionReached;
    private DefenseArenaBoundary _arenaBoundary;
    private float _arenaInset;

    public bool IsRepositioning { get; private set; }

    [Header("방어 반격 관통 베기")]
    [SerializeField, Min(0f), Tooltip("실제 피격 후 방어 상태를 유지하는 시간. 근접 베기·발도에 한 번 반격하면 즉시 종료합니다.")]
    private float _hitDeflectDuration = 2f;
    [SerializeField, Min(0f), Tooltip("반격 직전 세계를 불릿 타임 배율로 늦추는 전체 실제 시간(초). 페이드인·아웃 구간을 포함합니다.")]
    private float _counterSlowDuration = 1.5f;
    [SerializeField, Min(0f), Tooltip("감속과 먹먹한 소리가 최대로 걸리기까지의 실제 시간(초)")]
    private float _counterSlowFadeIn = 0.2f;
    [SerializeField, Min(0f), Tooltip("감속 끝에서 시간과 소리가 원래대로 돌아오는 실제 시간(초)")]
    private float _counterSlowFadeOut = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("감속 전체 시간 중 후퇴를 마치고 붉은 돌진 경고를 켜는 진행 비율")]
    private float _counterWarningAt = 1f / 3f;
    [SerializeField, Range(0f, 1f), Tooltip("감속 전체 시간 중 관통 돌진을 시작하는 진행 비율. 경고 비율보다 작으면 경고 비율을 씁니다.")]
    private float _counterDashAt = 2f / 3f;
    [SerializeField, Min(0f), Tooltip("감속 첫 구간에 발자국 없이 물러나는 최대 거리(미터). 뒤가 막히면 옆이나 비스듬히 갈 수 있는 만큼 갑니다. 0이면 물러나지 않습니다.")]
    private float _counterBackstepDistance = 18f;
    [SerializeField, Min(0.1f), Tooltip("관통 베기 돌진 속도(미터/초)")]
    private float _counterSlashSpeed = 90f;
    [SerializeField, Min(0f), Tooltip("플레이어를 지나 뒤로 빠져나가는 거리(미터)")]
    private float _counterSlashExitDistance = 12f;
    [SerializeField, Min(0f)] private float _counterSlashDamage = 25f;
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링에 막혔을 때 보스가 뒤로 밀려나는 거리(미터)")]
    private float _counterParryPushDistance = 6f;
    [SerializeField, Min(0.01f), Tooltip("퍼펙트 패링 밀림 시간(초)")]
    private float _counterParryPushDuration = 0.2f;
    [SerializeField, Tooltip("보스가 공격을 쳐낼 때 퍼지는 파동의 바닥 링과 그 파동이 밝힌 윤곽선 색")]
    private Color _counterPulseColor = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Min(0f), Tooltip("후퇴 중 보스가 시야 중심에서 이 각도(도)보다 벗어나면 플레이어 시점을 보스 쪽으로 보정합니다.")]
    private float _counterViewAssistAngle = 20f;
    [SerializeField, Min(0f), Tooltip("후퇴 중 플레이어 시점 보정 최대 속도(도/실제 초)")]
    private float _counterViewAssistSpeed = 120f;
    [SerializeField, Min(0f), Tooltip("보스가 공격을 쳐낼 때 플레이어 카메라가 위로 젖혀지는 각도(도)")]
    private float _counterCameraKickPitch = 4f;
    [SerializeField, Min(0f), Tooltip("보스가 공격을 쳐낼 때 플레이어 카메라 흔들림 크기(미터)")]
    private float _counterCameraShake = 0.05f;
    [SerializeField, Min(0.01f), Tooltip("플레이어 카메라 흔들림 시간(초, 플레이어 시간)")]
    private float _counterCameraShakeDuration = 0.3f;
    [SerializeField, Min(0f), Tooltip("보스가 공격을 쳐낼 때 플레이어가 뒷걸음질하는 거리(미터)")]
    private float _counterStaggerDistance = 1.5f;
    [SerializeField, Min(0.01f), Tooltip("플레이어 뒷걸음질 시간(초, 플레이어 시간)")]
    private float _counterStaggerDuration = 0.3f;
    private float _hitDeflectUntil;
    private bool _counterSlashing;
    private IPlayerDefenseResolver _playerDefense;
    private PlayerBulletTimeAudio _playerBulletTimeAudio;
    private PlayerPerfectPulse _playerPerfectPulse;
    private CharacterController _playerController;
    private Collider[] _bodyColliders;
    private bool _ignoringPlayerCollision;
    private CombatSessionRuntime _counterTimeRuntime;
    private CombatTimeToken _counterTimeToken;
    private bool _useCounterTimeOwnerB;
    private float _appliedCounterTimeScale = 1f;
    private float _counterSlowElapsed;
    private bool _counterSlowActive;

    [Header("돌진·피격 재배치 위치")]
    [SerializeField, Min(1f)] private float _teleportRadius = 6f;
    [SerializeField, Range(1f, 180f), Tooltip("플레이어 시점 기준 돌진 등장 각도(도). 180도는 전방 반원입니다.")]
    private float _teleportArc = 180f;
    private readonly Collider[] _placementContacts = new Collider[16];

    public BossState State => _state;
    public bool IsDefending => _hitDeflectUntil > 0f;
    public event Action<BossState> StateChanged;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _motor = GetComponent<EnemyMotor>();
        _sensor = GetComponent<EnemySensor>();
        _agent = GetComponent<NavMeshAgent>();
        _path = new NavMeshPath();
        _bodyColliders = GetComponentsInChildren<Collider>(true);
    }

    void Start()
    {
        _started = true;
        InitializeBattle();
    }

    void OnEnable()
    {
        if (_started) InitializeBattle();
    }

    void Update()
    {
        if (_counterSlowActive) UpdateCounterSlow();
        if (_hitDeflectUntil > 0f && Time.time >= _hitDeflectUntil)
            RestoreAfterDisappearance();
        if (_routine != null && (_player == null || !_playerHealth.IsAlive || !_motor.IsReady))
            StopBattle();
    }

    void OnDisable()
    {
        StopBattle();
    }

    /// <summary>
    /// arena와 inset으로 이 보스의 원형 이동 범위를 설정한다.
    /// 스테이지에서 활성화 전에 호출하며 이후 재배치와 돌진 위치를 제한한다.
    /// </summary>
    public void ConfigureArena(DefenseArenaBoundary arena, float inset)
    {
        _arenaBoundary = arena;
        _arenaInset = Mathf.Max(0f, inset);
    }

    /// <summary>현재 세션 플레이어와 필수 참조를 확인하고 전투 결과를 구독하여 행동을 시작한다.</summary>
    private void InitializeBattle()
    {
        _player = Game.Player;
        if (_player != null)
        {
            _playerHealth = _player.GetComponent<PlayerHealth>();
            _playerMotor = _player.GetComponent<PlayerMotor>();
            _playerCombat = _player.GetComponent<KatanaCombatCoordinator>();
            _playerEmpower = _player.GetComponent<PlayerEmpower>();
            _playerDefense = _player.GetComponent<IPlayerDefenseResolver>();
            _playerBulletTimeAudio = _player.GetComponent<PlayerBulletTimeAudio>();
            _playerPerfectPulse = _player.GetComponent<PlayerPerfectPulse>();
            _playerController = _player.GetComponent<CharacterController>();
        }
        if (_projectilePrefab == null || _player == null || _playerMotor == null || _playerHealth == null || !_motor.IsReady)
        {
            Debug.LogError("보스의 투사체·플레이어·NavMesh 연결이 누락되었습니다.", this);
            enabled = false;
            return;
        }
        _sensor.SetTarget(_player.transform);
        _health.Damaged += HandleBossDamaged;
        _health.DamageBlocked += HandleBossDamageBlocked;
        _health.Died += HandleBossDied;
        _playerCombat.Events.EmpowerChanged += HandlePlayerEmpowerChanged;
        _routine = StartCoroutine(RunBattle());
    }

    /// <summary>
    /// startingState부터 단일 행동 흐름을 실행하고 전투 종료 시 코루틴 참조를 정리한다.
    /// resumeAfterHit이면 무적을 해제하고 첫 패턴 대기·돌진 재배치를 생략해 공격을 재개한다.
    /// </summary>
    private IEnumerator RunBattle(BossState startingState = BossState.Ranged, bool resumeAfterHit = false)
    {
        yield return null;
        if (resumeAfterHit)
            RestoreAfterDisappearance();
        while (_health.IsAlive && _playerHealth.IsAlive)
        {
            if (startingState == BossState.Ranged)
            {
                if (!resumeAfterHit) yield return WaitForPattern(BossState.Ranged);
                resumeAfterHit = false;
                yield return Ranged();
            }
            if (resumeAfterHit)
            {
                ChangeState(BossState.Charging);
                yield return Charge();
                resumeAfterHit = false;
            }
            else
            {
                yield return WaitForPattern(BossState.Charging);
                if (TryTeleportAround(out Vector3 position))
                {
                    ChangeState(BossState.Charging);
                    yield return MoveQuickly(position);
                    if (_repositionReached) yield return Charge();
                }
            }
            startingState = BossState.Ranged;
        }
        _routine = null;
    }

    /// <summary>nextPattern을 예약하고 이동·소리를 멈춘 대기 상태에서 설정 범위의 랜덤 시간만큼 기다린다.</summary>
    private IEnumerator WaitForPattern(BossState nextPattern)
    {
        _nextPattern = nextPattern;
        _motor.Stop();
        ChangeState(BossState.Idle);
        float maximum = Mathf.Max(_patternWaitMin, _patternWaitMax);
        yield return new WaitForSeconds(UnityEngine.Random.Range(_patternWaitMin, maximum));
        RestoreAfterDisappearance();
    }

    /// <summary>
    /// 패턴 시작 시 플레이어에게서 멀어지는 위치로 한 번 백스텝한 뒤 설정 횟수와 준비 시간을 사용해 플레이어 시점보다 0.1m 아래로 투사체를 발사한다.
    /// 비행 유도는 투사체 프리팹이 설정하며 비행 속도는 _rangedProjectileSpeedMultiplier배로 바꾼다.
    /// </summary>
    private IEnumerator Ranged()
    {
        ChangeState(BossState.Ranged);
        _motor.Stop();
        if (TryGetBackstepDestination(out Vector3 backstep))
            yield return Backstep(backstep);
        for (int i = 0; i < _rangedShots; i++)
        {
            _telemetryAttackId = BlindBlade.Telemetry.CombatTelemetrySignals.Begin("BossRanged", "Projectile",
                transform.position + Vector3.up, _player.transform.position);
            RangedPreparing?.Invoke();
            yield return PrepareAttack(Mathf.Max(0.1f, _rangedPreparation), false);
            if (!_preparationSucceeded) yield break;
            Vector3 origin = transform.position + Vector3.up;
            Vector3 direction = (_playerMotor.ViewOrigin - Vector3.up * 0.1f - origin).normalized;
            if (direction.sqrMagnitude < 0.0001f) yield break;
            transform.rotation = Quaternion.LookRotation(direction);
            BlindBlade.Telemetry.CombatTelemetrySignals.Commit(_telemetryAttackId, origin);
            RangedFiring?.Invoke();
            EnemyProjectile projectile = Instantiate(_projectilePrefab, origin, Quaternion.LookRotation(direction));
            projectile.Initialize(new DamageContext(_rangedDamage, gameObject, origin, direction, "BossRanged", _telemetryAttackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Projectile),
                direction, _player.transform);
            projectile.SetSpeedMultiplier(_rangedProjectileSpeedMultiplier);
            _telemetryAttackId = 0;
            if (i + 1 < _rangedShots) yield return new WaitForSeconds(_rangedInterval);
        }
    }

    /// <summary>
    /// 플레이어 반대 방향을 기준으로 좌우 30도 간격 후보를 검사해 백스텝 목적지를 destination으로 반환한다.
    /// _rangedBackstepDistance와 직선 NavMesh 경로·빈 공간을 사용하며 플레이어와 가장 멀어지는 후보를 고른다.
    /// 구석에 몰려 전체 거리 후보가 없으면 절반 거리로 다시 찾고, 멀어지는 후보가 없으면 false를 반환한다.
    /// </summary>
    private bool TryGetBackstepDestination(out Vector3 destination)
    {
        destination = transform.position;
        if (_rangedBackstepDistance <= 0f) return false;
        Vector3 playerPosition = _player.transform.position;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - playerPosition, Vector3.up);
        float currentDistance = away.magnitude;
        if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(-transform.forward, Vector3.up);
        if (away.sqrMagnitude < 0.0001f) return false;
        away.Normalize();
        bool found = false;
        float bestDistance = currentDistance + MIN_BACKSTEP_GAIN;
        for (int pass = 0; pass < 2 && !found; pass++)
        {
            float stepDistance = pass == 0 ? _rangedBackstepDistance : _rangedBackstepDistance * 0.5f;
            for (int i = 0; i < BACKSTEP_CANDIDATE_COUNT; i++)
            {
                // 정반대 방향부터 좌우를 번갈아 넓혀 구석에서도 옆으로 빠질 수 있게 한다.
                float angle = BACKSTEP_ANGLE_STEP * ((i + 1) / 2) * (i % 2 == 0 ? 1f : -1f);
                Vector3 desired = transform.position + Quaternion.Euler(0f, angle, 0f) * away * stepDistance;
                if (!TrySamplePosition(desired, out Vector3 position) || _agent.Raycast(position, out _)
                    || !TryPlace(position)) continue;
                float distance = Vector3.ProjectOnPlane(position - playerPosition, Vector3.up).magnitude;
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                destination = position;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// destination까지 직선으로 _rangedBackstepSpeed만큼 이동하며 플레이어를 계속 바라본다.
    /// 이동 중 IsRepositioning을 켜 발소리·발자국을 표현하고 막히면 그 자리에서 멈춘다.
    /// </summary>
    private IEnumerator Backstep(Vector3 destination)
    {
        _motor.Stop();
        IsRepositioning = true;
        try
        {
            while (Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).sqrMagnitude > 0.0025f)
            {
                Vector3 previous = transform.position;
                Vector3 next = LimitToArena(Vector3.MoveTowards(previous, destination, _rangedBackstepSpeed * Time.deltaTime));
                _agent.Move(next - previous);
                Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
                if (toPlayer.sqrMagnitude > 0.000001f)
                    transform.rotation = Quaternion.LookRotation(toPlayer);
                if (Time.deltaTime > 0f && (transform.position - previous).sqrMagnitude < 0.000001f)
                    yield break;
                yield return null;
            }
        }
        finally
        {
            IsRepositioning = false;
            _motor.Stop();
        }
    }

    /// <summary>
    /// 먼 곳으로 순간이동한 뒤 준비 시간을 거쳐 3발 묶음 수리검을 _barrageInterval 간격으로 _barrageVolleys번 발사한다.
    /// 난사 중에는 무적이 아니며 피격되면 기존 피격 흐름으로 취소된다. 끝나면 돌진부터 기존 순환을 재개한다.
    /// </summary>
    private IEnumerator EscapeBarrage()
    {
        if (TryGetEscapePosition(out Vector3 position) && _agent.Warp(position))
            Teleported?.Invoke();
        ChangeState(BossState.Ranged);
        BeginBarrageTelemetry();
        RangedPreparing?.Invoke();
        yield return PrepareAttack(Mathf.Max(0.1f, _barragePreparation), false);
        if (!_preparationSucceeded)
        {
            CancelPendingBarrageAttacks();
            yield break;
        }
        for (int volley = 0; volley < _barrageVolleys; volley++)
        {
            FireBarrageVolley(volley);
            if (volley + 1 < _barrageVolleys) yield return new WaitForSeconds(_barrageInterval);
        }
        CancelPendingBarrageAttacks();
        if (_health.IsAlive && _playerHealth.IsAlive) yield return RunBattle(BossState.Charging);
        _routine = null;
    }

    /// <summary>
    /// 모든 난사 투사체의 공격 ID를 전조 시점에 발급한다.
    /// _barrageVolleys와 플레이어 위치를 사용하며 ID 배열을 초기화한다.
    /// </summary>
    private void BeginBarrageTelemetry()
    {
        CancelPendingBarrageAttacks();
        int attackCount = _barrageVolleys * 3;
        _barrageAttackIds = new int[attackCount];
        _barrageAttackLaunched = new bool[attackCount];
        Vector3 origin = transform.position + Vector3.up;
        Vector3 aim = _player.transform.position;
        for (int index = 0; index < attackCount; index++)
            _barrageAttackIds[index] = BlindBlade.Telemetry.CombatTelemetrySignals.Begin(
                "BossEscapeBarrage", "Projectile", origin, aim);
    }

    /// <summary>
    /// 아직 발사하지 않은 난사 공격을 Cancelled로 끝내고 현재 추적 배열을 비운다.
    /// 발사된 투사체는 자체 적중·빗나감 사건으로 결과를 기록한다.
    /// </summary>
    private void CancelPendingBarrageAttacks()
    {
        if (_barrageAttackIds == null) return;
        for (int index = 0; index < _barrageAttackIds.Length; index++)
        {
            if (_barrageAttackLaunched[index]) continue;
            BlindBlade.Telemetry.CombatTelemetrySignals.End(
                _barrageAttackIds[index], "Cancelled", transform.position);
        }
        _barrageAttackIds = null;
        _barrageAttackLaunched = null;
    }

    /// <summary>
    /// volley 순번으로 위에서 아래로 낮아지는 조준 높이를 정하고 왼쪽·가운데·오른쪽 수리검 3발을 동시에 발사한다.
    /// _barrageSpreadAngle과 기존 원거리 피해 _rangedDamage를 사용하며 보스를 플레이어 쪽으로 회전시킨다.
    /// </summary>
    private void FireBarrageVolley(int volley)
    {
        Vector3 origin = transform.position + Vector3.up;
        float progress = _barrageVolleys > 1 ? volley / (float)(_barrageVolleys - 1) : 0f;
        Vector3 aim = _player.transform.position
            + Vector3.up * Mathf.Lerp(_barrageTopAimHeight, _barrageBottomAimHeight, progress);
        Vector3 center = aim - origin;
        if (center.sqrMagnitude < 0.0001f) return;
        center.Normalize();
        Vector3 facing = Vector3.ProjectOnPlane(center, Vector3.up);
        if (facing.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(facing);
        RangedFiring?.Invoke();
        for (int shot = -1; shot <= 1; shot++)
        {
            Vector3 direction = Quaternion.AngleAxis(shot * _barrageSpreadAngle, Vector3.up) * center;
            EnemyProjectile projectile = Instantiate(_projectilePrefab, origin, Quaternion.LookRotation(direction));
            int attackIndex = volley * 3 + shot + 1;
            int attackId = _barrageAttackIds[attackIndex];
            BlindBlade.Telemetry.CombatTelemetrySignals.Commit(attackId, origin);
            _barrageAttackLaunched[attackIndex] = true;
            projectile.Initialize(new DamageContext(_rangedDamage, gameObject, origin, direction,
                    "BossEscapeBarrage", attackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Projectile),
                direction, _player.transform);
        }
    }

    /// <summary>
    /// 플레이어 주변 32방향을 최대 거리부터 2m 간격으로 검사해 가장 먼 유효 지면을 destination으로 반환한다.
    /// 공터 범위·빈 공간·완전한 NavMesh 경로를 확인하며 _escapeMinDistance 이상인 후보가 없으면 false를 반환한다.
    /// </summary>
    private bool TryGetEscapePosition(out Vector3 destination)
    {
        destination = transform.position;
        Vector3 playerPosition = _player.transform.position;
        float minimum = _escapeMinDistance;
        float bestDistance = minimum;
        bool found = false;
        for (float radius = Mathf.Max(minimum, _escapeMaxDistance); radius >= minimum; radius -= ESCAPE_RING_STEP)
        {
            // 안쪽 고리는 이미 찾은 후보보다 멀어질 수 없으므로 검사를 마친다.
            if (found && radius <= bestDistance) break;
            for (int i = 0; i < ESCAPE_DIRECTION_COUNT; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 360f * i / ESCAPE_DIRECTION_COUNT, 0f) * Vector3.forward * radius;
                if (!TrySamplePosition(playerPosition + offset, out Vector3 position)) continue;
                float distance = Vector3.ProjectOnPlane(position - playerPosition, Vector3.up).magnitude;
                if (distance < bestDistance || !TryPlace(position) || !_agent.CalculatePath(position, _path)
                    || _path.status != NavMeshPathStatus.PathComplete || !IsPathInsideArena(_path)) continue;
                bestDistance = distance;
                destination = position;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// amount를 현재 시각과 함께 기록하고 _burstDamageWindow보다 오래된 기록을 지운다.
    /// 남은 누적 피해가 _burstDamageThreshold 이상이면 기록을 비우고 true를 반환한다.
    /// </summary>
    private bool RegisterBurstDamage(float amount)
    {
        float now = Time.time;
        _recentDamage.Enqueue((now, amount));
        while (_recentDamage.Count > 0 && now - _recentDamage.Peek().At > _burstDamageWindow)
            _recentDamage.Dequeue();
        float total = 0f;
        foreach ((float At, float Amount) record in _recentDamage)
            total += record.Amount;
        if (total < _burstDamageThreshold) return false;
        _recentDamage.Clear();
        return true;
    }

    /// <summary>
    /// 준비 시간 뒤 목표 방향을 고정하고, 실제로 내려치며 돌진하는 순간 피격 후 방어 상태와 무적을 끝내 가드 자세 칼을 숨긴다.
    /// 시작 사건을 알린 뒤 장애물 경계까지 돌진하며 접촉 피해를 한 번 적용한다.
    /// </summary>
    private IEnumerator Charge()
    {
        _motor.Stop();
        _telemetryAttackId = BlindBlade.Telemetry.CombatTelemetrySignals.Begin("BossCharge", "Charge",
            transform.position, _player.transform.position);
        ChargePreparing?.Invoke();
        yield return PrepareAttack(GetPreparationDuration(_chargePreparation));
        if (!_preparationSucceeded) yield break;
        Vector3 direction = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f) yield break;
        transform.rotation = Quaternion.LookRotation(direction);
        float remaining = _chargeDistance;
        float until = Time.time + _chargeDuration;
        // 내려치는 돌진 동작에 들어가면 방어 자세를 풀어 이때부터 플레이어 공격에 맞는다.
        RestoreAfterDisappearance(true);
        BlindBlade.Telemetry.CombatTelemetrySignals.Commit(_telemetryAttackId, transform.position);
        ChargeStarted?.Invoke();
        while (Time.time < until && remaining > 0.01f)
        {
            Vector3 previous = transform.position;
            float distance = Mathf.Min(_chargeSpeed * Time.deltaTime, remaining);
            Vector3 center = previous + Vector3.up;
            int count = Physics.SphereCastNonAlloc(center, _agent.radius, direction, _obstacleHits,
                distance, _obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = _obstacleHits[i].transform;
                if (obstacle.IsChildOf(transform) || obstacle.IsChildOf(_player.transform)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, _obstacleHits[i].distance - 0.05f));
            }
            Vector3 destination = previous + direction * distance;
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            destination = LimitToArena(destination);
            float travelDistance = Mathf.Max(0f, Vector3.Dot(destination - previous, direction));
            if (TryGetChargePlayerContact(direction, travelDistance, out float contactDistance, out Vector3 point))
            {
                _agent.Move(direction * Mathf.Max(0f, contactDistance - 0.05f));
                _motor.Stop();
                _playerHealth.TakeDamage(new DamageContext(_chargeDamage, gameObject, point, direction, "BossCharge", _telemetryAttackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Charge));
                if (_playerHealth.IsAlive)
                    _playerMotor.ApplyCombatPush(direction, _chargePushDistance, _chargePushDuration);
                yield break;
            }
            _agent.Move(destination - previous);
            remaining -= Vector3.Distance(previous, transform.position);
            if (distance < 0.01f || (transform.position - previous).sqrMagnitude < 0.000001f) break;
            yield return null;
        }
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_telemetryAttackId, "Miss", transform.position);
        _telemetryAttackId = 0;
        _motor.Stop();
    }

    /// <summary>
    /// direction과 distance 구간의 보스 캡슐로 플레이어와 겹치기 전 접촉을 검사한다.
    /// 현재 겹침 또는 가장 가까운 이동 접촉의 거리와 위치를 출력하고 발견 여부를 반환한다.
    /// </summary>
    private bool TryGetChargePlayerContact(Vector3 direction, float distance, out float contactDistance, out Vector3 point)
    {
        float radius = Mathf.Max(_agent.radius, _chargeHitRadius);
        Vector3 bottom = transform.position + Vector3.up * radius;
        Vector3 top = transform.position + Vector3.up * Mathf.Max(radius, _agent.height - radius);
        contactDistance = float.PositiveInfinity;
        point = transform.position;
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _chargeContacts, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!_chargeContacts[i].transform.IsChildOf(_player.transform)) continue;
            contactDistance = 0f;
            point = _chargeContacts[i].ClosestPoint(bottom);
            return true;
        }
        foreach (RaycastHit hit in Physics.CapsuleCastAll(bottom, top, radius, direction,
            distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(_player.transform) || hit.distance >= contactDistance) continue;
            contactDistance = hit.distance;
            point = hit.point;
        }
        return !float.IsPositiveInfinity(contactDistance);
    }

    /// <summary>desired 주변의 같은 에이전트 유형·영역에 속한 지면을 찾아 position으로 반환한다.</summary>
    private bool TrySamplePosition(Vector3 desired, out Vector3 position)
    {
        desired.y = _agent.nextPosition.y;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
        bool found = NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.5f, filter);
        position = hit.position;
        return found && IsInsideArena(position);
    }

    /// <summary>플레이어 전방 부채꼴의 랜덤 후보 중 이동 가능한 빈 지면을 destination으로 반환한다.</summary>
    private bool TryTeleportAround(out Vector3 destination)
    {
        destination = transform.position;
        Vector3 forward = _playerMotor.ViewForward;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = UnityEngine.Random.Range(-_teleportArc * 0.5f, _teleportArc * 0.5f);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * forward * _teleportRadius;
            if (!TrySamplePosition(_player.transform.position + offset, out Vector3 position)) continue;
            if (Vector3.Dot(position - _player.transform.position, forward) < 0f) continue;
            if (!TryPlace(position) || !_agent.CalculatePath(position, _path)
                || _path.status != NavMeshPathStatus.PathComplete || !IsPathInsideArena(_path)) continue;
            destination = position;
            return true;
        }
        _motor.Stop();
        return false;
    }

    /// <summary>position의 공간이 비어 있으면 true를 반환하며 현재 위치는 변경하지 않는다.</summary>
    private bool TryPlace(Vector3 position)
    {
        float radius = _agent.radius;
        int count = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * (radius + 0.05f),
            position + Vector3.up * (_agent.height - radius), radius, _placementContacts,
            _obstacleMask, QueryTriggerInteraction.Ignore);
        if (count == _placementContacts.Length) return false;
        for (int i = 0; i < count; i++)
            if (!_placementContacts[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    /// <summary>destination까지 NavMesh 경로를 따라 빠르게 이동하고 도착 여부와 재배치 상태를 갱신한다.</summary>
    private IEnumerator MoveQuickly(Vector3 destination)
    {
        _repositionReached = false;
        _motor.Stop();
        if (!_agent.CalculatePath(destination, _path) || _path.status != NavMeshPathStatus.PathComplete
            || !IsPathInsideArena(_path))
            yield break;
        Vector3[] corners = _path.corners;
        IsRepositioning = true;
        try
        {
            for (int i = 1; i < corners.Length; i++)
            {
                while (Vector3.Distance(transform.position, corners[i]) > 0.05f)
                {
                    Vector3 previous = transform.position;
                    Vector3 next = LimitToArena(Vector3.MoveTowards(previous, corners[i], _repositionSpeed * Time.deltaTime));
                    _agent.Move(next - previous);
                    Vector3 direction = Vector3.ProjectOnPlane(next - previous, Vector3.up);
                    if (direction.sqrMagnitude > 0.000001f)
                        transform.rotation = Quaternion.LookRotation(direction);
                    if (Time.deltaTime > 0f && (transform.position - previous).sqrMagnitude < 0.000001f)
                        yield break;
                    yield return null;
                }
            }
            _repositionReached = Vector3.Distance(transform.position, destination) <= 0.1f;
        }
        finally
        {
            IsRepositioning = false;
            _motor.Stop();
        }
    }

    /// <summary>position이 설정된 arena의 inset만큼 안쪽에 있는지 반환한다. 범위 미설정 시 true다.</summary>
    private bool IsInsideArena(Vector3 position)
    {
        if (_arenaBoundary == null) return true;
        Vector3 offset = Vector3.ProjectOnPlane(position - _arenaBoundary.transform.position, Vector3.up);
        return offset.sqrMagnitude <= Mathf.Pow(Mathf.Max(0f, _arenaBoundary.Radius - _arenaInset), 2f);
    }

    /// <summary>path의 모든 NavMesh 모서리가 설정된 원형 공터 안에 있는지 반환한다.</summary>
    private bool IsPathInsideArena(NavMeshPath path)
    {
        foreach (Vector3 corner in path.corners)
            if (!IsInsideArena(corner)) return false;
        return true;
    }

    /// <summary>position의 수평 좌표를 설정된 arena 반경 안으로 제한해 반환한다. 높이는 유지한다.</summary>
    private Vector3 LimitToArena(Vector3 position)
    {
        if (_arenaBoundary == null) return position;
        Vector3 center = _arenaBoundary.transform.position;
        Vector3 offset = Vector3.ProjectOnPlane(position - center, Vector3.up);
        float radius = Mathf.Max(0f, _arenaBoundary.Radius - _arenaInset);
        return center + Vector3.ClampMagnitude(offset, radius) + Vector3.up * (position.y - center.y);
    }

    /// <summary>
    /// context의 실제 피격 시 진행 중인 행동·반격을 취소하고 외형을 유지한 채 무적 방어 상태로 다음 패턴을 준비한다.
    /// 짧은 시간의 누적 피해가 기준 이상이면 기존 회복 대신 무적 없이 도주 난사를 시작한다.
    /// 플레이어 불릿 타임(강화 준비·강화 공격) 중이거나 반사된 수리검에 맞았으면 무적과 방어 상태 없이 회복한다.
    /// </summary>
    private void HandleBossDamaged(DamageContext context)
    {
        if (!_health.IsAlive) return;
        CancelPendingBarrageAttacks();
        switch (_state)
        {
            case BossState.Idle: _resumeState = _nextPattern; break;
            case BossState.Ranged: _resumeState = BossState.Charging; break;
            case BossState.Charging: _resumeState = BossState.Ranged; break;
            default: _resumeState = BossState.Ranged; break;
        }
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        // 불릿 타임으로 무적이 풀린 반격 중에 맞으면 반격 상태와 감속·소리·충돌 무시를 함께 정리한다.
        EndCounterEffects();
        PreparationCancelled?.Invoke();
        _motor.Stop();
        IsRepositioning = false;
        ChangeState(BossState.Idle);
        _health.SetInvulnerable(true);
        if (RegisterBurstDamage(context.Amount))
        {
            // 도주 난사는 무적 없이 진행하므로 피격 무적과 쳐내기 창을 바로 해제한다.
            RestoreAfterDisappearance(true);
            _routine = StartCoroutine(EscapeBarrage());
            return;
        }
        // 플레이어 불릿 타임 중이거나 반사된 수리검에 맞으면 피격 후 무적과 방어 상태를 만들지 않는다.
        if (_playerEmpower.IsReady || _playerEmpower.IsStriking || context.PatternId == REFLECTED_PROJECTILE_PATTERN_ID)
            RestoreAfterDisappearance(true);
        else
            _hitDeflectUntil = Time.time + _hitDeflectDuration;
        _routine = StartCoroutine(RecoverFromHit());
    }

    /// <summary>
    /// context가 방어 상태 중 플레이어의 근접 베기·발도이면 검을 쳐내고 관통 베기 반격을 시작한다.
    /// 현재 행동을 중단하고 방어 상태를 끝내며 퍼펙트 패링 소리·불꽃·칼 튕김 모션과 보스 발밑 퍼펙트 파동을 재생한다.
    /// 반격 감속을 먼저 시작해 퍼펙트 패링 소리가 처음부터 먹먹하게 들리게 하고,
    /// 플레이어 카메라를 흔들며 위로 젖히고 플레이어를 시선 반대쪽으로 뒷걸음질시킨다.
    /// 일섬 검기와 반격 중 타격은 피해 없이 막기만 하고 상태를 바꾸지 않는다.
    /// </summary>
    private void HandleBossDamageBlocked(DamageContext context)
    {
        if (_routine == null || _counterSlashing || context.Instigator != _player.gameObject
            || !IsPlayerMeleeAttack(context)) return;
        StopCoroutine(_routine);
        _routine = null;
        PreparationCancelled?.Invoke();
        _motor.Stop();
        IsRepositioning = false;
        ChangeState(BossState.Idle);
        _hitDeflectUntil = 0f;
        _counterSlashing = true;
        StartCounterSlow();
        Vector3 direction = -_playerMotor.ViewForward;
        KatanaWeaponVisual weapon = _player.GetComponent<KatanaWeaponVisual>();
        if (weapon != null) weapon.PlayAttackDeflection();
        Game.Instance.EffectManager.Play(EffectId.ParryClash, context.Point, Quaternion.LookRotation(direction));
        Managers.Sound.Play(SoundId.PerfectParryClash, context.Point, 1f, gameObject);
        _playerPerfectPulse.BeginAt(transform.position, Vector3.up, _counterPulseColor, _counterPulseColor);
        _playerMotor.AddCameraKick(_counterCameraKickPitch, _counterCameraShake, _counterCameraShakeDuration);
        _playerMotor.ApplyCombatPush(direction, _counterStaggerDistance, _counterStaggerDuration);
        _routine = StartCoroutine(CounterSlash());
    }

    /// <summary>context의 PatternId가 플레이어 일반 근접 베기 또는 발도 베기인지 반환한다.</summary>
    private bool IsPlayerMeleeAttack(DamageContext context)
    {
        return context.PatternId == PlayerAttackKind.Normal.ToString()
            || context.PatternId == PlayerAttackKind.IaidoFinisher.ToString();
    }

    /// <summary>
    /// 감속 첫 구간에 발자국 없이 뒤로 물러나고, _counterWarningAt 비율부터 붉은 돌진 경고를 켠 뒤
    /// _counterDashAt 비율부터 느려진 시간 속에서 플레이어를 관통해 뒤쪽까지 베고 지나간다.
    /// 반격 동안 무적을 유지하고 감속이 끝나면 연출을 정리해 무적을 해제한 뒤 패턴 사이 대기를 거쳐 _resumeState 패턴을 재개한다.
    /// </summary>
    private IEnumerator CounterSlash()
    {
        _health.SetInvulnerable(true);
        yield return RetreatForCounter(_counterSlowDuration * _counterWarningAt);
        ChangeState(BossState.Charging);
        _telemetryAttackId = BlindBlade.Telemetry.CombatTelemetrySignals.Begin("BossCounterSlash", "Charge",
            transform.position, _player.transform.position);
        float dashAt = _counterSlowDuration * Mathf.Max(_counterWarningAt, _counterDashAt);
        while (_counterSlowActive && _counterSlowElapsed < dashAt)
        {
            AssistPlayerViewToBoss();
            yield return null;
        }
        SetPlayerCollisionIgnored(true);
        yield return CounterSlashDash();
        while (_counterSlowActive) yield return null;
        EndCounterEffects();
        RestoreAfterDisappearance(true);
        if (_health.IsAlive && _playerHealth.IsAlive) yield return RunBattle(_resumeState);
        _routine = null;
    }

    /// <summary>
    /// 감속 시작부터 retreatUntil 실제 시간까지 후퇴 목적지로 직선 이동하며 플레이어를 바라본다.
    /// Idle 상태와 재배치 꺼짐을 유지해 발소리·발자국·이동 파동을 남기지 않는다. 목적지가 없으면 제자리에서 기다린다.
    /// </summary>
    private IEnumerator RetreatForCounter(float retreatUntil)
    {
        bool moving = TryGetCounterRetreatDestination(out Vector3 destination);
        float distance = Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).magnitude;
        float speed = retreatUntil > 0.01f ? distance / retreatUntil : float.PositiveInfinity;
        while (_counterSlowActive && _counterSlowElapsed < retreatUntil)
        {
            if (moving) moving = StepCounterRetreat(destination, speed);
            AssistPlayerViewToBoss();
            yield return null;
        }
        _motor.Stop();
    }

    /// <summary>
    /// 보스가 플레이어 시야 중심에서 _counterViewAssistAngle보다 벗어나면 _counterViewAssistSpeed와 실제 시간 간격만큼 플레이어 좌우 시점을 보스 쪽으로 돌린다.
    /// 옆으로 물러나는 보스가 화면 밖으로 빠지지 않게 하며 일시정지 중에는 돌리지 않는다.
    /// </summary>
    private void AssistPlayerViewToBoss()
    {
        if (Time.timeScale <= 0f) return;
        _playerMotor.NudgeViewToward(transform.position, _counterViewAssistAngle,
            _counterViewAssistSpeed * Time.unscaledDeltaTime);
    }

    /// <summary>
    /// origin에서 direction으로 length까지 보스 캡슐을 밀어 벽·대나무 같은 물리 장애물에 닿기 전까지의 거리를 반환한다.
    /// 보스·플레이어 충돌체, 시작부터 겹친 충돌체, 위를 향한 바닥면은 무시한다.
    /// </summary>
    private float GetObstacleFreeDistance(Vector3 origin, Vector3 direction, float length)
    {
        float radius = _agent.radius;
        Vector3 bottom = origin + Vector3.up * (radius + COUNTER_RETREAT_FLOOR_CLEARANCE);
        Vector3 top = origin + Vector3.up * Mathf.Max(radius + COUNTER_RETREAT_FLOOR_CLEARANCE, _agent.height - radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _obstacleHits, length,
            _obstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _obstacleHits[i];
            if (hit.distance <= 0f || hit.normal.y > 0.7f || hit.transform.IsChildOf(transform)
                || hit.transform.IsChildOf(_player.transform)) continue;
            length = Mathf.Min(length, hit.distance - 0.05f);
        }
        return Mathf.Max(0f, length);
    }

    /// <summary>
    /// 보스 주변 COUNTER_RETREAT_DIRECTION_COUNT 방향마다 _counterBackstepDistance 안에서 직선 NavMesh가 이어지는 가장 먼 지점을 공터 안으로 줄여 후보로 삼는다.
    /// 플레이어 곁을 스쳐 지나가는 방향은 빼고 빈 지면 중 플레이어와 수평 거리가 가장 먼 후보를 destination으로 반환한다.
    /// 뒤가 막혀 있으면 옆이나 비스듬한 방향으로 갈 수 있는 만큼 고르며, 현재보다 멀어지는 후보가 없으면 false를 반환한다.
    /// </summary>
    private bool TryGetCounterRetreatDestination(out Vector3 destination)
    {
        destination = transform.position;
        if (_counterBackstepDistance <= 0f) return false;
        Vector3 origin = transform.position;
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - origin, Vector3.up);
        float bestDistance = toPlayer.magnitude + MIN_BACKSTEP_GAIN;
        bool found = false;
        for (int i = 0; i < COUNTER_RETREAT_DIRECTION_COUNT; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, 360f * i / COUNTER_RETREAT_DIRECTION_COUNT, 0f) * Vector3.forward;
            float length = _counterBackstepDistance;
            if (_agent.Raycast(origin + direction * length, out NavMeshHit hit))
                length = Vector3.ProjectOnPlane(hit.position - origin, Vector3.up).magnitude - _agent.radius;
            // NavMesh에 구워지지 않은 벽·대나무도 막힘으로 보고 그 앞까지만 후보로 삼는다.
            length = GetObstacleFreeDistance(origin, direction, length);
            while (length > COUNTER_RETREAT_STEP && !IsInsideArena(origin + direction * length))
                length -= COUNTER_RETREAT_STEP;
            if (length <= COUNTER_RETREAT_STEP) continue;
            // 플레이어 곁을 지나가는 후퇴는 관통 돌진 전에 겹치므로 후보에서 뺀다.
            float along = Mathf.Clamp(Vector3.Dot(toPlayer, direction), 0f, length);
            if ((toPlayer - direction * along).sqrMagnitude < COUNTER_RETREAT_PLAYER_CLEARANCE * COUNTER_RETREAT_PLAYER_CLEARANCE)
                continue;
            if (!TrySamplePosition(origin + direction * length, out Vector3 position) || !TryPlace(position)) continue;
            float distance = Vector3.ProjectOnPlane(position - _player.transform.position, Vector3.up).magnitude;
            if (distance <= bestDistance) continue;
            bestDistance = distance;
            destination = position;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// destination 쪽으로 speed(미터/실제 초)만큼 한 프레임 이동하고 플레이어를 바라본다.
    /// 일시정지 중에는 움직이지 않으며 도착했거나 막혀 움직이지 못했으면 false를 반환한다.
    /// </summary>
    private bool StepCounterRetreat(Vector3 destination, float speed)
    {
        float deltaTime = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        Vector3 previous = transform.position;
        Vector3 next = LimitToArena(Vector3.MoveTowards(previous, destination, speed * deltaTime));
        _agent.Move(next - previous);
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
        if (toPlayer.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(toPlayer);
        if (deltaTime > 0f && (transform.position - previous).sqrMagnitude < 0.000001f) return false;
        return Vector3.ProjectOnPlane(destination - transform.position, Vector3.up).sqrMagnitude > 0.0025f;
    }

    /// <summary>
    /// 반격 감속을 처음부터 시작하고 연출음 버스를 즉시 먹먹하게 해 보스의 퍼펙트 패링 소리가 시작부터 감속 소리로 들리게 한다.
    /// _counterSlowElapsed와 _counterSlowActive를 초기화하며 이후 Update가 감속을 진행한다.
    /// </summary>
    private void StartCounterSlow()
    {
        _counterSlowElapsed = 0f;
        _counterSlowActive = true;
        ApplyCounterSlow(0f, 1f);
    }

    /// <summary>
    /// 일시정지를 제외한 실제 시간으로 _counterSlowElapsed를 늘리고 페이드 비중으로 세계 시간과 소리를 갱신한다.
    /// _counterSlowDuration이 지나면 감속을 끝낸다.
    /// </summary>
    private void UpdateCounterSlow()
    {
        if (Time.timeScale > 0f) _counterSlowElapsed += Time.unscaledDeltaTime;
        if (_counterSlowElapsed >= _counterSlowDuration)
        {
            StopCounterSlow();
            return;
        }
        ApplyCounterSlow(GetCounterSlowWeight(_counterSlowElapsed), GetCounterSlowFadeOut(_counterSlowElapsed));
    }

    /// <summary>진행 중인 반격 감속을 끝내고 시간 요청과 효과음·연출음 버스의 감속 소리를 해제한다.</summary>
    private void StopCounterSlow()
    {
        _counterSlowActive = false;
        _playerBulletTimeAudio.SetExternalWarp(0f, 0f);
        ReleaseCounterTime();
    }

    /// <summary>
    /// elapsed 실제 시간에서 페이드인·유지·페이드아웃 구간을 따라 0~1 감속 비중을 반환한다.
    /// _counterSlowFadeIn과 _counterSlowFadeOut이 0이면 해당 구간을 즉시 처리한다.
    /// </summary>
    private float GetCounterSlowWeight(float elapsed)
    {
        float fadeIn = _counterSlowFadeIn > 0f ? Mathf.Clamp01(elapsed / _counterSlowFadeIn) : 1f;
        return Mathf.Min(fadeIn, GetCounterSlowFadeOut(elapsed));
    }

    /// <summary>elapsed 실제 시간에서 감속 끝 페이드아웃 구간만 반영한 0~1 비중을 반환한다.</summary>
    private float GetCounterSlowFadeOut(float elapsed)
    {
        return _counterSlowFadeOut > 0f ? Mathf.Clamp01((_counterSlowDuration - elapsed) / _counterSlowFadeOut) : 1f;
    }

    /// <summary>
    /// weight만큼 세계 배율을 1과 플레이어 불릿 타임 세계 배율 사이에서 보간해 요청하고 같은 비중으로 효과음·BGM에 불릿 타임 소리를 건다.
    /// 연출음 버스에는 cinematicWeight를 따로 적용하며, 배율이 이전과 같으면 시간 요청을 다시 등록하지 않는다.
    /// </summary>
    private void ApplyCounterSlow(float weight, float cinematicWeight)
    {
        _playerBulletTimeAudio.SetExternalWarp(weight, cinematicWeight);
        float scale = Mathf.Lerp(1f, _playerEmpower.WorldTimeScale, weight);
        if (Mathf.Abs(scale - _appliedCounterTimeScale) < 0.001f) return;
        ChangeCounterTime(scale);
        _appliedCounterTimeScale = scale;
    }

    /// <summary>
    /// scale로 세계 시간 요청을 다른 소유자 이름으로 새로 등록한 뒤 이전 요청을 해제해 배율이 끊기지 않게 바꾼다.
    /// 세션 시간 런타임이 없으면 요청 없이 진행하며 _counterTimeRuntime과 _counterTimeToken을 갱신한다.
    /// </summary>
    private void ChangeCounterTime(float scale)
    {
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        CombatTimeToken previous = _counterTimeToken;
        CombatSessionRuntime previousRuntime = _counterTimeRuntime;
        _counterTimeRuntime = null;
        _counterTimeToken = default;
        if (runtime != null && runtime.IsInitialized)
        {
            _useCounterTimeOwnerB = !_useCounterTimeOwnerB;
            _counterTimeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session,
                _useCounterTimeOwnerB ? COUNTER_TIME_OWNER_B : COUNTER_TIME_OWNER_A, CombatTimeReason.DashTelegraph, scale));
            if (_counterTimeToken.IsValid) _counterTimeRuntime = runtime;
        }
        if (previousRuntime != null && previous.IsValid) previousRuntime.Release(previous);
    }

    /// <summary>등록한 반격 감속 요청이 있으면 해제하고 _counterTimeRuntime·_counterTimeToken·적용 배율을 초기화한다.</summary>
    private void ReleaseCounterTime()
    {
        if (_counterTimeRuntime != null && _counterTimeToken.IsValid) _counterTimeRuntime.Release(_counterTimeToken);
        _counterTimeRuntime = null;
        _counterTimeToken = default;
        _appliedCounterTimeScale = 1f;
    }

    /// <summary>
    /// ignored가 참이면 보스 몸통 충돌체와 플레이어 CharacterController의 물리 충돌을 끄고 거짓이면 되돌린다.
    /// 관통 중 플레이어가 보스 충돌체 위로 밀려 올라가지 않게 하며 레이·겹침 판정에는 영향을 주지 않는다.
    /// </summary>
    private void SetPlayerCollisionIgnored(bool ignored)
    {
        if (_ignoringPlayerCollision == ignored) return;
        _ignoringPlayerCollision = ignored;
        foreach (Collider body in _bodyColliders)
            if (!body.isTrigger) Physics.IgnoreCollision(_playerController, body, ignored);
    }

    /// <summary>
    /// 진행 중이던 반격의 감속 요청·불릿 타임 소리·플레이어 충돌 무시를 해제하고 _counterSlashing을 끈다.
    /// 반격 중이 아니면 아무것도 바꾸지 않는다.
    /// </summary>
    private void EndCounterEffects()
    {
        if (!_counterSlashing) return;
        _counterSlashing = false;
        StopCounterSlow();
        SetPlayerCollisionIgnored(false);
    }

    /// <summary>
    /// 플레이어 방향으로 _counterSlashSpeed만큼 돌진해 플레이어 뒤 _counterSlashExitDistance 지점까지 지나가며 시작 시 플레이어 대시 소리를 낸다.
    /// 첫 접촉에서 한 번만 방어 판정을 요청하며 맞음·막기·회피는 그대로 관통하고 퍼펙트 패링이면 멈춰 뒤로 밀려난다.
    /// 장애물·NavMesh 경계·공터 범위에 막히면 그 자리에서 멈춘다.
    /// </summary>
    private IEnumerator CounterSlashDash()
    {
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
        Vector3 direction = toPlayer.sqrMagnitude > 0.0001f
            ? toPlayer.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f) yield break;
        transform.rotation = Quaternion.LookRotation(direction);
        float remaining = toPlayer.magnitude + _counterSlashExitDistance;
        bool contacted = false;
        BlindBlade.Telemetry.CombatTelemetrySignals.Commit(_telemetryAttackId, transform.position);
        ChargeStarted?.Invoke();
        // 관통 베기는 보스 기합에 플레이어 대시 소리를 겹쳐 순간 돌진을 알린다.
        Managers.Sound.PlayCue(CombatSoundCue.Dodge, transform.position, gameObject, true);
        while (remaining > 0.01f)
        {
            // 일시정지 프레임은 이동 없이 넘겨 돌진이 중간에 끊기지 않게 한다.
            if (Time.deltaTime <= 0f)
            {
                yield return null;
                continue;
            }
            Vector3 previous = transform.position;
            float distance = Mathf.Min(_counterSlashSpeed * Time.deltaTime, remaining);
            int count = Physics.SphereCastNonAlloc(previous + Vector3.up, _agent.radius, direction, _obstacleHits,
                distance, _obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = _obstacleHits[i].transform;
                if (obstacle.IsChildOf(transform) || obstacle.IsChildOf(_player.transform)) continue;
                distance = Mathf.Min(distance, Mathf.Max(0f, _obstacleHits[i].distance - 0.05f));
            }
            Vector3 destination = previous + direction * distance;
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            destination = LimitToArena(destination);
            float travelDistance = Mathf.Max(0f, Vector3.Dot(destination - previous, direction));
            if (!contacted && TryGetChargePlayerContact(direction, travelDistance, out float contactDistance, out Vector3 point))
            {
                contacted = true;
                DefenseResolution resolution = _playerDefense.ResolveIncomingAttack(new DamageContext(_counterSlashDamage,
                    gameObject, point, direction, "BossCounterSlash", _telemetryAttackId, 1, IncomingAttackKind.Charge));
                _telemetryAttackId = 0;
                if (resolution.Outcome == DefenseOutcome.PerfectParry)
                {
                    _agent.Move(direction * Mathf.Max(0f, contactDistance - 0.05f));
                    yield return PushBackFromParry(-direction);
                    yield break;
                }
            }
            _agent.Move(destination - previous);
            remaining -= Vector3.Distance(previous, transform.position);
            if (distance < 0.01f || (transform.position - previous).sqrMagnitude < 0.000001f) break;
            yield return null;
        }
        if (!contacted)
            BlindBlade.Telemetry.CombatTelemetrySignals.End(_telemetryAttackId, "Miss", transform.position);
        _telemetryAttackId = 0;
        _motor.Stop();
    }

    /// <summary>
    /// 퍼펙트 패링에 막힌 보스를 direction으로 _counterParryPushDuration 동안 _counterParryPushDistance만큼 밀어낸다.
    /// 붉은 돌진 경고를 끄고 플레이어를 바라보며 NavMesh 경계·공터 범위에 막히면 그 자리에서 멈춘다.
    /// </summary>
    private IEnumerator PushBackFromParry(Vector3 direction)
    {
        ChangeState(BossState.Idle);
        float speed = _counterParryPushDistance / _counterParryPushDuration;
        float remaining = _counterParryPushDistance;
        while (remaining > 0.01f)
        {
            if (Time.deltaTime <= 0f)
            {
                yield return null;
                continue;
            }
            Vector3 previous = transform.position;
            Vector3 destination = previous + direction * Mathf.Min(speed * Time.deltaTime, remaining);
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            _agent.Move(LimitToArena(destination) - previous);
            Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
            if (toPlayer.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(toPlayer);
            remaining -= Vector3.Distance(previous, transform.position);
            if ((transform.position - previous).sqrMagnitude < 0.000001f) break;
            yield return null;
        }
        _motor.Stop();
    }

    /// <summary>
    /// empowerEvent가 플레이어의 불릿 타임 준비 시작이면 피격 후 무적과 쳐내기 창을 즉시 끝낸다.
    /// 진행 중인 행동과 예약된 다음 패턴은 그대로 유지한다.
    /// </summary>
    private void HandlePlayerEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        if (empowerEvent.Stage == PlayerEmpowerStage.Ready) RestoreAfterDisappearance(true);
    }

    /// <summary>
    /// 실제 피격 후 빈 공간과 NavMesh 경로가 확인된 랜덤 위치로 순간이동한다.
    /// 기존 후보 반경과 회복 시간을 사용하며 이동 성공을 알리고 예약된 다음 패턴을 재개한다.
    /// </summary>
    private IEnumerator RecoverFromHit()
    {
        if (TryTeleportAround(out Vector3 position) && _agent.Warp(position))
            Teleported?.Invoke();
        yield return new WaitForSeconds(_hitRecoveryDuration);
        if (_health.IsAlive && _playerHealth.IsAlive) yield return RunBattle(_resumeState, true);
        _routine = null;
    }

    /// <summary>
    /// 피격 후 쳐내기 시간이 끝났으면 무적과 종료 시각을 초기화한다.
    /// force가 true인 쳐내기 성공·전투 중단에서는 남은 시간과 무관하게 즉시 해제한다.
    /// </summary>
    private void RestoreAfterDisappearance(bool force = false)
    {
        if (!force && Time.time < _hitDeflectUntil) return;
        _hitDeflectUntil = 0f;

        _health.SetInvulnerable(false);
    }

    /// <summary>사망한 health의 행동을 종료하고 사망 상태를 구독자에게 전달한다.</summary>
    private void HandleBossDied(EnemyHealth health)
    {
        StopBattle();
        ChangeState(BossState.Dead);
    }

    /// <summary>state로 행동 상태를 변경하고 실제 변경이 있을 때 표현 담당자에게 알린다.</summary>
    private void ChangeState(BossState state)
    {
        if (_state == state) return;
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_telemetryAttackId, "Cancelled", transform.position);
        _telemetryAttackId = 0;
        _state = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>실행 중인 공격·이동·코루틴과 반격 감속·소리·충돌 무시를 중지하고 피격·사망·플레이어 불릿 타임 사건 구독을 정리한다.</summary>
    private void StopBattle()
    {
        CancelPendingBarrageAttacks();
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        EndCounterEffects();
        PreparationCancelled?.Invoke();
        IsRepositioning = false;
        _recentDamage.Clear();
        RestoreAfterDisappearance(true);
        _motor.Stop();
        _health.Damaged -= HandleBossDamaged;
        _health.DamageBlocked -= HandleBossDamageBlocked;
        _health.Died -= HandleBossDied;
        // 연결 누락으로 전투를 시작하지 못한 경우에는 구독한 적이 없다.
        if (_playerCombat != null) _playerCombat.Events.EmpowerChanged -= HandlePlayerEmpowerChanged;
        if (_state != BossState.Dead) ChangeState(BossState.Idle);
    }

    /// <summary>
    /// 첫 박동 대기와 박동 사이 간격에 고정 공격 지연을 더해 이번 패턴의 전체 전조 시간을 반환한다.
    /// 박동을 끄면 legacyDuration을 사용하며 호출당 한 번만 랜덤 값을 선택한다.
    /// </summary>
    private float GetPreparationDuration(float legacyDuration)
    {
        if (!_useHeartbeatTelegraph) return Mathf.Max(0.1f, legacyDuration);
        float minimum = Mathf.Max(0.1f, _minimumHeartbeatInterval);
        return Mathf.Max(0f, _firstHeartbeatDelay) + (Mathf.Max(1, _heartbeatCount) - 1)
            * UnityEngine.Random.Range(minimum, Mathf.Max(minimum, _maximumHeartbeatInterval))
            + Mathf.Max(0.05f, _postHeartbeatAttackDelay);
    }

    /// <summary>
    /// duration과 횟수로 첫 박동 대기 뒤부터 박동을 알리고 준비 성공을 저장한다. useHeartbeat 또는 박동 설정이 꺼져 있으면 재생 없이 기다린다.
    /// 일시정지 후 같은 간격의 첫 박동부터 재개하며 큰 프레임 지연에서는 준비를 취소한다.
    /// </summary>
    private IEnumerator PrepareAttack(float duration, bool useHeartbeat = true)
    {
        _preparationSucceeded = false;
        if (!useHeartbeat || !_useHeartbeatTelegraph)
        {
            yield return new WaitForSeconds(duration);
            _preparationSucceeded = true;
            yield break;
        }
        int count = Mathf.Max(1, _heartbeatCount);
        float firstDelay = Mathf.Max(0f, _firstHeartbeatDelay);
        float interval = count > 1
            ? Mathf.Max(0.1f, (duration - firstDelay - Mathf.Max(0.05f, _postHeartbeatAttackDelay)) / (count - 1)) : 0f;
        float started = Time.time;
        int played = 0;
        while (true)
        {
            yield return null;
            if (Time.timeScale <= 0f)
            {
                PreparationCancelled?.Invoke();
                while (Time.timeScale <= 0f) yield return null;
                started = Time.time;
                played = 0;
                continue;
            }
            float now = Time.time;
            float nextAt = started + firstDelay + interval * played;
            float attackAt = started + duration;
            if (played < count && now >= nextAt)
            {
                if (now >= attackAt || now - nextAt > MAX_RHYTHM_DELAY)
                {
                    PreparationCancelled?.Invoke();
                    yield break;
                }
                played++;
                Heartbeat?.Invoke();
            }
            if (now < attackAt) continue;
            if (now - attackAt > MAX_RHYTHM_DELAY)
            {
                PreparationCancelled?.Invoke();
                yield break;
            }
            _preparationSucceeded = played >= count;
            yield break;
        }
    }
}
