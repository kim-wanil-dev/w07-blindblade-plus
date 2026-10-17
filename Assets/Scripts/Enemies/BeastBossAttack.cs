using System;
using System.Collections;

using UnityEngine;

using UnityEngine.AI;

/// <summary>짐승의 원형 이동·지그재그 돌진·점프 공격과 NavMesh 복원을 담당한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(NavMeshAgent))]
public sealed class BeastBossAttack : MonoBehaviour
{
    private const float MAX_RHYTHM_DELAY = 0.1f;

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

    public EnemyHealth Health => _health;
    public Transform RiderSeat => _riderSeat;
    public bool IsReady => _motor.IsReady;
    public bool IsPreparing => _preparing;
    public bool IsJumping => _jumping;
    public event Action AttackPreparing;
    public event Action AttackStarted;
    public event Action Landed;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _motor = GetComponent<EnemyMotor>();
        _agent = GetComponent<NavMeshAgent>();
        _path = new NavMeshPath();
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
                    CheckChargeContact(player, previous, direction, travel);
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

    /// <summary>start부터 direction·distance 구간과 현재 겹침을 검사해 player 접촉 피해를 한 번 적용한다.</summary>
    private void CheckChargeContact(KatanaDamageReceiver player, Vector3 start, Vector3 direction, float distance)
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
            ApplyAttack(player, _chargeDamage, direction, _hits[i].point, "TwinBeastCharge");
            return;
        }
        count = Physics.OverlapCapsuleNonAlloc(transform.position + Vector3.up * radius,
            transform.position + Vector3.up * Mathf.Max(radius, _agent.height - radius), radius,
            _contacts, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_contacts[i].GetComponentInParent<PlayerHealth>() != player) continue;
            ApplyAttack(player, _chargeDamage, direction, _contacts[i].ClosestPoint(bottom), "TwinBeastCharge");
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
        Vector3 destination = NavMesh.SamplePosition(desired, out NavMeshHit hit, 2f, filter)
            ? hit.position : _navigationPosition;
        _agent.updatePosition = _previousUpdatePosition;
        _agent.updateRotation = _previousUpdateRotation;
        if (_agent.isActiveAndEnabled) _agent.Warp(destination);
    }

    /// <summary>원형 이동·점프를 중단하고 기존 NavMesh 설정과 이동 정지 상태를 복원한다.</summary>
    public void CancelAttack()
    {
        _preparationSucceeded = false;
        // 패턴 코루틴이 외부에서 멈추면 준비 종료 처리가 실행되지 않으므로 여기서 함께 끈다.
        _preparing = false;
        StopHeartbeat();
        EndOrbit();
        RestoreNavigation();
        _motor.Stop();
    }

    void OnDisable()
    {
        CancelAttack();
    }
}
