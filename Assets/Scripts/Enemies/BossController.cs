using System;
using System.Collections;

using UnityEngine;

using UnityEngine.AI;

public enum BossState { Idle, Roaming, Charging, ChainCharge, Duel, Stunned, Dead, Disappeared, Ranged }

/// <summary>보스의 원거리·돌진 순환과 피격 전환 및 전투 종료 수명을 관리한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(EnemySensor))]
public sealed class BossController : MonoBehaviour
{
    private const float MAX_RHYTHM_DELAY = 0.1f;

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
    private int _telemetryAttackId;

    public event Action RangedPreparing;
    public event Action RangedFiring;

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

    [Header("무적 중 공격 쳐내기")]
    [SerializeField, Min(0f)] private float _deflectPushDistance = 3f; // 뒤로 밀리는 거리 
    [SerializeField, Min(0.01f)] private float _deflectPushDuration = 0.16f;
    [SerializeField, Min(0f), Tooltip("실제 피격 후 쳐내기를 유지하는 시간. 한 번 쳐내면 즉시 종료합니다.")]
    private float _hitDeflectDuration = 2f;
    private float _hitDeflectUntil;

    [Header("돌진·피격 재배치 위치")]
    [SerializeField, Min(1f)] private float _teleportRadius = 6f;
    [SerializeField, Range(1f, 180f), Tooltip("플레이어 시점 기준 돌진 등장 각도(도). 180도는 전방 반원입니다.")]
    private float _teleportArc = 180f;
    private readonly Collider[] _placementContacts = new Collider[16];

    public BossState State => _state;
    public event Action<BossState> StateChanged;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _motor = GetComponent<EnemyMotor>();
        _sensor = GetComponent<EnemySensor>();
        _agent = GetComponent<NavMeshAgent>();
        _path = new NavMeshPath();
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

    /// <summary>설정 횟수와 준비 시간을 사용해 플레이어 시점보다 0.1m 아래로 투사체를 발사한다. 비행 유도는 투사체 프리팹이 설정한다.</summary>
    private IEnumerator Ranged()
    {
        ChangeState(BossState.Ranged);
        _motor.Stop();
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
            _telemetryAttackId = 0;
            if (i + 1 < _rangedShots) yield return new WaitForSeconds(_rangedInterval);
        }
    }

    /// <summary>준비 시간 뒤 목표 방향을 고정하고 시작 사건을 알린 후 장애물 경계까지 돌진하며 접촉 피해를 한 번 적용한다.</summary>
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

    /// <summary>context의 실제 피격 시 행동을 취소하고 외형을 유지한 채 무적으로 다음 패턴을 준비한다.</summary>
    private void HandleBossDamaged(DamageContext context)
    {
        if (!_health.IsAlive) return;
        switch (_state)
        {
            case BossState.Idle: _resumeState = _nextPattern; break;
            case BossState.Ranged: _resumeState = BossState.Charging; break;
            case BossState.Charging: _resumeState = BossState.Ranged; break;
            default: _resumeState = BossState.Ranged; break;
        }
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        PreparationCancelled?.Invoke();
        _motor.Stop();
        IsRepositioning = false;
        ChangeState(BossState.Idle);
        _health.SetInvulnerable(true);
        _hitDeflectUntil = Time.time + _hitDeflectDuration;
        _routine = StartCoroutine(RecoverFromHit());
    }

    /// <summary>
    /// context가 플레이어의 무적 중 타격이면 검을 쳐내고 플레이어를 밀어낸다.
    /// 현재 재배치를 중단하고 즉시 무적을 해제한 뒤 예약된 다음 패턴을 준비한다.
    /// </summary>
    private void HandleBossDamageBlocked(DamageContext context)
    {
        if (_routine == null || context.Instigator != _player.gameObject) return;
        StopCoroutine(_routine);
        _routine = null;
        PreparationCancelled?.Invoke();
        _motor.Stop();
        IsRepositioning = false;
        RestoreAfterDisappearance(true);
        Vector3 direction = -_playerMotor.ViewForward;
        _playerMotor.ApplyCombatPush(direction, _deflectPushDistance, _deflectPushDuration);
        KatanaWeaponVisual weapon = _player.GetComponent<KatanaWeaponVisual>();
        if (weapon != null) weapon.PlayAttackDeflection();
        Game.Instance.EffectManager.Play(EffectId.ParryClash, context.Point, Quaternion.LookRotation(direction));
        Managers.Sound.Play(SoundId.ParryClash, context.Point, 1f, gameObject);
        _routine = StartCoroutine(RunBattle(_resumeState));
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

    /// <summary>실행 중인 공격·이동·코루틴을 중지하고 피격·사망 사건 구독을 정리한다.</summary>
    private void StopBattle()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        PreparationCancelled?.Invoke();
        IsRepositioning = false;
        RestoreAfterDisappearance(true);
        _motor.Stop();
        _health.Damaged -= HandleBossDamaged;
        _health.DamageBlocked -= HandleBossDamageBlocked;
        _health.Died -= HandleBossDied;
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
