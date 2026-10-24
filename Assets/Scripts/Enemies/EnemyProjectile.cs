using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

public class EnemyProjectile : MonoBehaviour
{
    [Header("투사체 설정")]
    [SerializeField, Min(0.1f)] private float _speed = 12f;
    [SerializeField, Min(0.01f)] private float _lifetime = 4f;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.12f;
    [SerializeField] private LayerMask _collisionMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0f), Tooltip("비행 중 목표로 꺾는 최대 각도(도/초). 0이면 직진한다.")]
    private float _homingTurnRate;
    [SerializeField, Min(0f), Tooltip("목표에 이 거리 이내로 접근하거나 지나치면 유도를 영구히 멈추고 직진한다.")]
    private float _homingStopDistance = 5f;
    [SerializeField, Tooltip("적대 비행 동안 투사체를 따라 반복하는 비행음. None이면 재생하지 않으며 반사·소멸하면 멈춘다.")]
    private SoundId _flightSound = SoundId.None;
    [SerializeField, Tooltip("켜면 플레이어 검기·검기 돌진·궁극기가 적대 비행 중인 이 투사체를 없앤다.")]
    private bool _clearedByPlayerAttacks;
    private DamageContext _damage;
    private Transform _target;
    private PlayerMotor _targetMotor;
    private Vector3 _direction;
    private float _speedMultiplier = 1f;
    private float _homingMultiplier = 1f;
    private float _elapsedTime;
    private bool _isFlying;
    private bool _reflected;
    private int _projectileId;
    private bool _hasPassedPlayer;
    private bool _terminalFlight;
    private readonly List<EnemyHitCapsule> _capsuleContacts = new List<EnemyHitCapsule>();
    private readonly List<EnemyCapsuleHit> _capsuleHits = new List<EnemyCapsuleHit>();

    [Header("공격 판정 기즈모")]
    [SerializeField] private bool _showAttackGizmos = true;
#if UNITY_EDITOR
    private Vector3 _debugStart;
    private Vector3 _debugEnd;
    private bool _hasDebugSegment;
#endif

    public event Action<Collider, DamageContext> Hit;
    public event Action<Vector3> Impact;

    protected DamageContext Damage => _damage;
    protected Vector3 FlightDirection => _direction;
    public bool IsHostileFlying => _isFlying && !_reflected;
    public bool HasPassedPlayer => _hasPassedPlayer;
    public int ProjectileId => _projectileId;

    /// <summary>적대 투사체의 현재 피해 구가 cylinder에 닿으면 attackId를 출력한다.</summary>
    public bool TryGetDodgeThreat(DodgeThreatCylinder cylinder, out int attackId)
    {
        attackId = 0;
        if (!isActiveAndEnabled || !IsHostileFlying || _damage.Amount <= 0f
            || !cylinder.OverlapsSphere(transform.position, _hitRadius)) return false;
        attackId = _damage.AttackId;
        return true;
    }

    void Update()
    {
        if (!_isFlying)
            return;

        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= _lifetime)
        {
            Release();
            return;
        }

        Vector3 playerPoint = _target == null
            ? transform.position
            : (_targetMotor != null ? _targetMotor.ViewOrigin : _target.position);
        UpdateHomingPhase(transform.position, transform.position, playerPoint);
        SteerTowardsTarget();

#if UNITY_EDITOR
        _debugStart = transform.position;
        _debugEnd = transform.position + _direction * (_speed * _speedMultiplier * Time.deltaTime);
        _hasDebugSegment = true;
#endif
        foreach (Collider overlap in Physics.OverlapSphere(transform.position, _hitRadius,
            _collisionMask, QueryTriggerInteraction.Ignore))
        {
            if (!CanHit(overlap))
                continue;

            if (!_reflected) DodgeAfterimage.ReportSphereSweep(transform.position, transform.position,
                _hitRadius, _damage);

#if UNITY_EDITOR
            _debugEnd = _debugStart;
#endif
            ProcessHit(overlap, overlap.GetComponentInParent<IDamageable>(), overlap.ClosestPoint(transform.position));
            return;
        }
        // 반사된 투사체만 적을 맞히며 적은 피격 캡슐로 판정한다.
        if (_reflected)
        {
            EnemyHitCapsule.OverlapAll(transform.position, transform.position, _hitRadius, _collisionMask, _capsuleContacts);
            foreach (EnemyHitCapsule capsule in _capsuleContacts)
            {
                if (!CanHit(capsule))
                    continue;
#if UNITY_EDITOR
                _debugEnd = _debugStart;
#endif
                ProcessHit(null, capsule.Health, capsule.ClosestPoint(transform.position));
                return;
            }
        }

        float distance = _speed * _speedMultiplier * Time.deltaTime;
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, _hitRadius, _direction,
            distance, _collisionMask, QueryTriggerInteraction.Ignore);
        Collider closestCollider = null;
        EnemyHitCapsule closestCapsule = null;
        Vector3 closestPoint = transform.position;
        float closestDistance = float.PositiveInfinity;
        for (int index = 0; index < hits.Length; index++)
        {
            if (!CanHit(hits[index].collider) || hits[index].distance >= closestDistance)
                continue;

            closestCollider = hits[index].collider;
            closestPoint = hits[index].point;
            closestDistance = hits[index].distance;
        }
        if (_reflected)
        {
            EnemyHitCapsule.SweepAll(transform.position, transform.position, _hitRadius, _direction,
                distance, _collisionMask, _capsuleHits);
            foreach (EnemyCapsuleHit hit in _capsuleHits)
            {
                if (!CanHit(hit.Capsule) || hit.Distance >= closestDistance)
                    continue;

                closestCollider = null;
                closestCapsule = hit.Capsule;
                closestPoint = hit.Point;
                closestDistance = hit.Distance;
            }
        }

        if (closestCollider != null || closestCapsule != null)
        {
            if (!_reflected) DodgeAfterimage.ReportSphereSweep(transform.position,
                transform.position + _direction * closestDistance, _hitRadius, _damage);
            transform.position += _direction * closestDistance;
#if UNITY_EDITOR
            _debugEnd = transform.position;
#endif
            IDamageable receiver = closestCapsule != null
                ? closestCapsule.Health
                : closestCollider.GetComponentInParent<IDamageable>();
            ProcessHit(closestCollider, receiver, closestPoint);
            return;
        }

        Vector3 start = transform.position;
        Vector3 end = start + _direction * distance;
        if (!_reflected) DodgeAfterimage.ReportSphereSweep(start, end, _hitRadius, _damage);
        transform.position = end;
        UpdateHomingPhase(start, end, playerPoint);
    }

    void OnDisable()
    {
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_damage.AttackId, "Cancelled", transform.position);
        _isFlying = false;
    }

    /// <summary>
    /// damage와 direction 및 target을 저장하고 이동 방향, 수명 누적, 속도·유도 배율 및 비행 상태를 초기화한다.
    /// 풀에서 재사용하는 경우에도 호출하며 초기 방향으로 이동을 시작하고 _flightSound가 있으면 비행음을 반복 재생한다.
    /// </summary>
    public virtual void Initialize(DamageContext damage, Vector3 direction, Transform target = null)
    {
        _damage = damage;
        _speedMultiplier = 1f;
        _homingMultiplier = 1f;
        _reflected = false;
        _hasPassedPlayer = false;
        _terminalFlight = false;
        CombatSessionRuntime session = CombatSessionRuntime.Active;
        _projectileId = session != null && session.IsInitialized ? session.AllocateProjectileId() : 0;
        _target = target;
        _targetMotor = target != null ? target.GetComponent<PlayerMotor>() : null;
        _direction = direction.normalized;
        _elapsedTime = 0f;
#if UNITY_EDITOR
        _hasDebugSegment = false;
#endif
        _isFlying = _direction.sqrMagnitude > 0f;
        if (_isFlying)
        {
            transform.rotation = Quaternion.LookRotation(_direction);
            // 소멸하면 소유자 파괴로 SoundManager가 정지하고, 반사는 Reflect에서 직접 멈춘다.
            if (_flightSound != SoundId.None)
                Managers.Sound.PlayAttachedLoop(_flightSound, gameObject);
        }
        else
            Release();
    }

    /// <summary>
    /// 이번 비행의 이동 속도를 프리팹 속도의 multiplier배로 바꾼다. Initialize 뒤에 호출하며 다음 Initialize에서 1로 돌아간다.
    /// 같은 프리팹을 쓰는 다른 패턴의 속도에는 영향을 주지 않는다.
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        _speedMultiplier = Mathf.Max(0.01f, multiplier);
    }

    /// <summary>
    /// 이번 비행의 유도 각속도를 프리팹 Homing Turn Rate의 multiplier배로 바꾼다. Initialize 뒤에 호출하며 다음 Initialize에서 1로 돌아간다.
    /// 유도가 꺼진 프리팹(0)은 배율과 무관하게 직진한다.
    /// </summary>
    public void SetHomingMultiplier(float multiplier)
    {
        _homingMultiplier = Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// 비행 중 target의 시점과 현재 위치를 사용해 설정 각속도만큼 진행 방향을 보정한다.
    /// 반사 후, 종단 비행 시작 후, 근거리에서는 방향을 유지하며 유도할 때 _direction과 회전을 갱신한다.
    /// </summary>
    private void SteerTowardsTarget()
    {
        if (_reflected || _terminalFlight || _homingTurnRate <= 0f || _target == null) return;
        Vector3 aim = _targetMotor != null ? _targetMotor.ViewOrigin - Vector3.up * 0.1f : _target.position;
        Vector3 toTarget = aim - transform.position;
        if (toTarget.sqrMagnitude <= _homingStopDistance * _homingStopDistance) return;
        _direction = Vector3.RotateTowards(_direction, toTarget.normalized,
            _homingTurnRate * _homingMultiplier * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// 이동 구간이 플레이어 시점의 설정 거리 이내를 지나거나 플레이어를 통과하면 종단 비행을 고정한다.
    /// 통과 상태도 기록해 HUD가 지나간 수리검을 숨길 수 있게 한다.
    /// </summary>
    private void UpdateHomingPhase(Vector3 start, Vector3 end, Vector3 playerPoint)
    {
        if (_reflected || _target == null)
            return;

        if (!_terminalFlight)
        {
            Vector3 travel = end - start;
            float travelLengthSquared = travel.sqrMagnitude;
            float progress = travelLengthSquared > 0f
                ? Mathf.Clamp01(Vector3.Dot(playerPoint - start, travel) / travelLengthSquared)
                : 0f;
            Vector3 nearest = start + travel * progress;
            if ((playerPoint - nearest).sqrMagnitude <= _homingStopDistance * _homingStopDistance)
                _terminalFlight = true;
        }

        if (!_hasPassedPlayer && Vector3.Dot(playerPoint - end, _direction) <= 0f)
        {
            _hasPassedPlayer = true;
            _terminalFlight = true;
        }
    }

    /// <summary>
    /// collider가 자신·공격자 또는 지정 대상 밖의 피해 수신자인지 검사한다.
    /// 적 계층은 몸 충돌체가 남아 있어도 제외하며, 반사된 투사체의 적 충돌은 피격 캡슐로 따로 판정한다.
    /// </summary>
    protected virtual bool CanHit(Collider collider)
    {
        return !collider.transform.IsChildOf(transform) &&
            (_damage.Instigator == null || !collider.transform.IsChildOf(_damage.Instigator.transform)) &&
            collider.GetComponentInParent<EnemyHealth>() == null &&
            (_target == null || collider.GetComponentInParent<IDamageable>() == null || collider.transform.IsChildOf(_target));
    }

    /// <summary>
    /// capsule이 반사된 투사체가 맞힐 수 있는 적인지 검사한다. 공격자와 지정 대상 밖의 적은 제외한다.
    /// 반사되지 않은 투사체는 적을 맞히지 않는다.
    /// </summary>
    protected virtual bool CanHit(EnemyHitCapsule capsule)
    {
        return _reflected &&
            (_damage.Instigator == null || !capsule.transform.IsChildOf(_damage.Instigator.transform)) &&
            (_target == null || capsule.transform.IsChildOf(_target));
    }

    /// <summary>
    /// receiver에게 point와 저장한 피해 정보를 전달하고 충돌 이벤트를 발생시킨다. collider는 물리 충돌일 때만 전달하고 적 피격 캡슐이면 null이다.
    /// 정확한 패링이면 피해 없이 반사하고 그 외 최초 충돌에서는 비행을 중지하고 반환한다.
    /// </summary>
    protected virtual void ProcessHit(Collider collider, IDamageable receiver, Vector3 point)
    {
        DamageContext damage = new DamageContext(_damage.Amount, _damage.Instigator,
            point, _direction, _damage.PatternId, _damage.AttackId, _damage.HitId, _damage.Kind);
        bool parried = false;
        bool defended = receiver is KatanaDamageReceiver katana
            && katana.TryDefendProjectile(damage, out parried);
        if (defended)
        {
            if (parried)
            {
                Reflect(((KatanaDamageReceiver)receiver).gameObject);
                return;
            }
        }
        else receiver?.TakeDamage(damage);
        _isFlying = false;
        Hit?.Invoke(collider, damage);
        Impact?.Invoke(point);
        Release();
    }

    /// <summary>
    /// defender를 새 공격자로 지정하고 원래 피해량으로 투사체를 반대 방향에 반사한다.
    /// 적 충돌을 허용하고 지정 대상 제한과 수명을 초기화하며 비행을 유지한다. 적대 비행음은 멈춘다.
    /// </summary>
    private void Reflect(GameObject defender)
    {
        if (_flightSound != SoundId.None)
            Managers.Sound.StopFor(gameObject, _flightSound, true, true);
        _direction = -_direction;
        _damage = new DamageContext(_damage.Amount, defender, transform.position,
            _direction, "ReflectedProjectile");
        _target = null;
        if (TryGetComponent(out OutlineLayer threatLayer)) threatLayer.enabled = false;
        _reflected = true;
        _elapsedTime = 0f;
        transform.rotation = Quaternion.LookRotation(_direction);
        _isFlying = true;
    }

    /// <summary>
    /// start에서 end까지 radius 반경으로 쓸고 지나간 공간에 닿은 제거 가능 적대 투사체를 가드 성공 소리와 함께 없앤다.
    /// 플레이어 검기와 검기 돌진이 경로의 투사체를 벨 때 쓰며 반사된 투사체와 제거 설정이 꺼진 투사체는 유지한다.
    /// </summary>
    public static void ReleaseInSweep(Vector3 start, Vector3 end, float radius)
    {
        Vector3 travel = end - start;
        float travelLengthSquared = travel.sqrMagnitude;
        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
        {
            if (!projectile.IsClearable) continue;
            Vector3 position = projectile.transform.position;
            float progress = travelLengthSquared > 0f
                ? Mathf.Clamp01(Vector3.Dot(position - start, travel) / travelLengthSquared) : 0f;
            float reach = radius + projectile._hitRadius;
            if ((position - (start + travel * progress)).sqrMagnitude > reach * reach) continue;
            projectile.ReleaseBlocked();
        }
    }

    /// <summary>
    /// 현재 적대 비행 중인 제거 가능 투사체를 모두 그 위치에서 가드 성공 소리와 함께 없앤다.
    /// 반사된 투사체와 제거 설정이 꺼진 투사체는 유지한다.
    /// </summary>
    public static void ReleaseAllClearable()
    {
        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
            if (projectile.IsClearable) projectile.ReleaseBlocked();
    }

    /// <summary>
    /// isInRange가 true를 반환하는 위치의 플레이어 공격 제거 허용 투사체를 가드 성공 소리와 함께 없앤다.
    /// 반사된 투사체와 제거 설정이 꺼진 투사체는 유지한다.
    /// </summary>
    public static void ReleaseInRange(Predicate<Vector3> isInRange)
    {
        foreach (EnemyProjectile projectile in FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
        {
            if (!projectile.IsClearable || !isInRange(projectile.transform.position)) continue;
            projectile.ReleaseBlocked();
        }
    }

    /// <summary>_clearedByPlayerAttacks가 켜져 있고 반사되지 않은 채 비행 중인지 반환한다.</summary>
    private bool IsClearable => _clearedByPlayerAttacks && _isFlying && !_reflected;

    /// <summary>플레이어 공격에 막힌 투사체를 현재 위치에서 가드 성공 소리를 내며 비행 종료·해제한다.</summary>
    private void ReleaseBlocked()
    {
        Managers.Sound.PlayCue(CombatSoundCue.Guard, transform.position);
        Release();
    }

    /// <summary>
    /// 비행 상태를 종료하고 기본 구현에서는 투사체 GameObject를 파괴한다.
    /// 입력값 없이 정리하며 파생 클래스에서 풀 반환 방식으로 교체할 수 있다.
    /// </summary>
    public virtual void Release()
    {
        _isFlying = false;
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_damage.AttackId, "Miss", transform.position);
        Destroy(gameObject);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_showAttackGizmos || !isActiveAndEnabled || !Application.isPlaying || !_hasDebugSegment) return;
        EnemyCombatService.DrawSweepGizmo(_debugStart, _debugEnd, _hitRadius, Color.magenta);
        UnityEditor.Handles.Label(_debugEnd, "적 투사체: 현재 프레임 검사 구간");
    }

    void OnDrawGizmosSelected()
    {
        if (!_showAttackGizmos || Application.isPlaying) return;
        EnemyCombatService.DrawSweepGizmo(transform.position, transform.position, _hitRadius, Color.yellow);
        UnityEditor.Handles.Label(transform.position, "적 투사체: 판정 반경 미리보기");
    }
#endif
}
