using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;
using BlindBlade.Telemetry;

/// <summary>쌍둥이 사격의 전방 방어와 발사자를 추적하는 반사 탄환을 처리한다.</summary>
[DisallowMultipleComponent]
public sealed class TwinBossProjectile : MonoBehaviour
{
    [Header("비행 설정")]
    [SerializeField, Min(0.1f)] private float _speed = 12f;
    [SerializeField, Min(0.1f)] private float _lifetime = 5f;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.12f;
    [SerializeField] private LayerMask _collisionMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0f), Tooltip("비행 중 플레이어 시점으로 꺾는 최대 각도(도/초). 0이면 직진한다.")]
    private float _homingTurnRate = 20f;
    [SerializeField, Min(0f), Tooltip("플레이어와 이 거리 안에서는 유도를 멈추고 직진한다.")]
    private float _homingStopDistance = 3f;
    [SerializeField, Tooltip("적대 비행 동안 투사체를 따라 반복하는 비행음. 반사·소멸하면 멈춘다.")]
    private SoundId _flightSound = SoundId.ShurikenFlight;
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Collider[] _contacts = new Collider[32];
    private EnemyHealth _shooter;
    private Collider _shooterCollider;
    private KatanaDamageReceiver _player;
    private PlayerMotor _playerMotor;
    private DamageContext _damage;
    private Vector3 _direction;
    private float _elapsed;
    private bool _reflected;
    private bool _flying;
    private int _projectileId;
    private bool _hasPassedPlayer;
    private int _telemetryAttackId;
    private string _telemetryParentPatternId;
    private string _telemetryPatternInstanceId;

    public event Action<TwinBossProjectile> ReflectedHit;
    public event Action<TwinBossProjectile> Released;
    public bool IsHostileFlying => _flying && !_reflected;
    public bool HasPassedPlayer => _hasPassedPlayer;
    public int ProjectileId => _projectileId;

    /// <summary>적대 보스 투사체의 현재 피해 구가 cylinder에 닿으면 attackId를 출력한다.</summary>
    public bool TryGetDodgeThreat(DodgeThreatCylinder cylinder, out int attackId)
    {
        attackId = 0;
        if (!isActiveAndEnabled || !IsHostileFlying || _damage.Amount <= 0f
            || !cylinder.OverlapsSphere(transform.position, _hitRadius)) return false;
        attackId = _damage.AttackId;
        return true;
    }

    /// <summary>
    /// start에서 end까지 radius 반경으로 쓸고 지나간 공간에 닿은 적대 비행 수리검을 가드 소리와 함께 없앤다.
    /// 반사된 수리검은 유지한다.
    /// </summary>
    public static void ReleaseInSweep(Vector3 start, Vector3 end, float radius)
    {
        Vector3 travel = end - start;
        float travelLengthSquared = travel.sqrMagnitude;
        foreach (TwinBossProjectile projectile in FindObjectsByType<TwinBossProjectile>(FindObjectsSortMode.None))
        {
            if (!projectile._flying || projectile._reflected) continue;
            Vector3 position = projectile.transform.position;
            float progress = travelLengthSquared > 0f
                ? Mathf.Clamp01(Vector3.Dot(position - start, travel) / travelLengthSquared) : 0f;
            float reach = radius + projectile._hitRadius;
            if ((position - (start + travel * progress)).sqrMagnitude > reach * reach) continue;
            projectile.ReleaseBlocked();
        }
    }

    /// <summary>현재 적대 비행 중인 수리검을 모두 가드 소리와 함께 없애고 반사된 수리검은 유지한다.</summary>
    public static void ReleaseAllHostile()
    {
        foreach (TwinBossProjectile projectile in FindObjectsByType<TwinBossProjectile>(FindObjectsSortMode.None))
            if (projectile._flying && !projectile._reflected) projectile.ReleaseBlocked();
    }

    /// <summary>
    /// isInRange가 true를 반환하는 위치의 적대 비행 보스 투사체를 가드 성공 소리와 함께 없앤다.
    /// 반사되어 아군이 된 투사체는 유지한다.
    /// </summary>
    public static void ReleaseInRange(Predicate<Vector3> isInRange)
    {
        foreach (TwinBossProjectile projectile in FindObjectsByType<TwinBossProjectile>(FindObjectsSortMode.None))
        {
            if (!projectile.IsHostileFlying || !isInRange(projectile.transform.position)) continue;
            projectile.ReleaseBlocked();
        }
    }

    /// <summary>플레이어 공격에 막힌 수리검의 위치에서 가드 소리를 내고 비행을 종료한다.</summary>
    private void ReleaseBlocked()
    {
        Managers.Sound.PlayCue(CombatSoundCue.Guard, transform.position);
        Release();
    }

    /// <summary>
    /// shooter·player·damage·direction으로 발사자와 피해 및 초기 비행 방향을 설정하고 _flightSound가 있으면 비행음을 반복 재생한다.
    /// 유도에 쓸 플레이어 시점은 player의 PlayerMotor에서 읽는다.
    /// </summary>
    public void Initialize(EnemyHealth shooter, Collider shooterCollider,
        KatanaDamageReceiver player, float damage, Vector3 direction, int attackId,
        string parentPatternId, string patternInstanceId)
    {
        _shooter = shooter;
        _shooterCollider = shooterCollider;
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        _direction = direction.normalized;
        _telemetryAttackId = attackId;
        _telemetryParentPatternId = parentPatternId;
        _telemetryPatternInstanceId = patternInstanceId;
        _damage = new DamageContext(damage, shooter.gameObject, transform.position, _direction,
            "TwinBossRanged", attackId, 1, IncomingAttackKind.Projectile);
        _elapsed = 0f;
        _reflected = false;
        _hasPassedPlayer = false;
        CombatSessionRuntime session = CombatSessionRuntime.Active;
        _projectileId = session != null && session.IsInitialized ? session.AllocateProjectileId() : 0;
        _flying = true;
        if (_flightSound != SoundId.None)
            Managers.Sound.PlayAttachedLoop(_flightSound, gameObject);
    }

    void Update()
    {
        if (!_flying) return;
        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime || _player == null || !_player.IsAlive
            || (_reflected && (_shooter == null || !_shooter.IsAlive)))
        {
            Release(_elapsed >= _lifetime ? "Miss" : "Cancelled");
            return;
        }
        if (_reflected)
        {
            Vector3 toShooter = _shooterCollider.bounds.center - transform.position;
            if (toShooter.sqrMagnitude > 0.000001f) _direction = toShooter.normalized;
        }
        else
            SteerTowardsPlayer();
        UpdatePassedPlayer();
        transform.rotation = Quaternion.LookRotation(_direction);
        int overlaps = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius,
            _contacts, _collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!CanHit(_contacts[i])) continue;
            if (!_reflected) DodgeAfterimage.ReportSphereSweep(transform.position, transform.position,
                _hitRadius, _damage);
            ProcessHit(_contacts[i], _contacts[i].ClosestPoint(transform.position));
            return;
        }
        float distance = _speed * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction,
            _hits, distance, _collisionMask, QueryTriggerInteraction.Ignore);
        int closest = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!CanHit(_hits[i].collider) || _hits[i].distance >= closestDistance) continue;
            closest = i;
            closestDistance = _hits[i].distance;
        }
        if (closest >= 0)
        {
            if (!_reflected) DodgeAfterimage.ReportSphereSweep(transform.position,
                transform.position + _direction * closestDistance, _hitRadius, _damage);
            transform.position += _direction * closestDistance;
            ProcessHit(_hits[closest].collider, _hits[closest].point);
            return;
        }
        if (!_reflected) DodgeAfterimage.ReportSphereSweep(transform.position,
            transform.position + _direction * distance, _hitRadius, _damage);
        transform.position += _direction * distance;
        UpdatePassedPlayer();
    }

    /// <summary>
    /// 플레이어 시점보다 0.1m 아래를 향해 _homingTurnRate 각속도만큼 _direction을 꺾는다.
    /// 플레이어와 _homingStopDistance 안이면 방향을 유지해 마지막 구간은 직진한다.
    /// </summary>
    private void SteerTowardsPlayer()
    {
        if (_homingTurnRate <= 0f) return;
        Vector3 toTarget = _playerMotor.ViewOrigin - Vector3.up * 0.1f - transform.position;
        if (toTarget.sqrMagnitude <= _homingStopDistance * _homingStopDistance) return;
        _direction = Vector3.RotateTowards(_direction, toTarget.normalized,
            _homingTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
    }

    /// <summary>
    /// 플레이어 시점과 현재 투사체 방향을 비교해 플레이어를 통과했는지 판정한다.
    /// 통과 상태는 비행 중 다시 해제하지 않고 HUD가 지나간 수리검을 숨기도록 유지한다.
    /// </summary>
    private void UpdatePassedPlayer()
    {
        if (_hasPassedPlayer || _reflected || _player == null)
            return;

        Vector3 playerPoint = _playerMotor != null ? _playerMotor.ViewOrigin : _player.transform.position;
        if (Vector3.Dot(playerPoint - transform.position, _direction) <= 0f)
            _hasPassedPlayer = true;
    }

    /// <summary>collider가 비행 대상 또는 장애물인지 반환하며 다른 적과 발사자 충돌을 제외한다.</summary>
    private bool CanHit(Collider collider)
    {
        if (collider.transform.IsChildOf(transform)) return false;
        EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null) return _reflected && enemy == _shooter;
        PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
        if (player != null) return !_reflected && player == _player;
        return true;
    }

    /// <summary>collider·point에 피해를 적용하고 정확한 패링은 발사자 추적으로 전환하며 적중을 알린다.</summary>
    private void ProcessHit(Collider collider, Vector3 point)
    {
        DamageContext context = new DamageContext(_damage.Amount, _damage.Instigator,
            point, _direction, _damage.PatternId, _damage.AttackId, _damage.HitId, _damage.Kind);
        if (!_reflected && collider.GetComponentInParent<KatanaDamageReceiver>() == _player)
        {
            if (_player.TryDefendProjectile(context, out bool parried))
            {
                if (parried && _shooter != null && _shooter.IsAlive)
                {
                    _reflected = true;
                    _telemetryAttackId = 0;
                    _elapsed = 0f;
                    StopFlightSound();
                    // 스테이지1 보스 수리검처럼 반사되면 붉은 위협 실루엣을 끄고 일반 흰 윤곽선으로 보인다.
                    if (TryGetComponent(out OutlineLayer threatLayer)) threatLayer.enabled = false;
                    _damage = new DamageContext(_damage.Amount, _player.gameObject,
                        point, -_direction, "ReflectedProjectile");
                    return;
                }
                _telemetryAttackId = 0;
                Release();
                return;
            }
        }
        IDamageable receiver = collider.GetComponentInParent<IDamageable>();
        if (receiver != null) receiver.TakeDamage(context);
        if (_reflected && collider.GetComponentInParent<EnemyHealth>() == _shooter)
            ReflectedHit?.Invoke(this);
        if (!_reflected && collider.GetComponentInParent<PlayerHealth>() == _player)
            _telemetryAttackId = 0;
        Release("Miss");
    }

    /// <summary>비행을 종료하고 비행음을 멈춘 뒤 구독자에게 정리를 알리고 투사체를 파괴한다.</summary>
    public void Release(string unresolvedOutcome = "Miss")
    {
        if (!_flying) return;
        _flying = false;
        if (_telemetryAttackId != 0)
        {
            CombatTelemetrySignals.End(_telemetryAttackId, unresolvedOutcome, transform.position);
            _telemetryAttackId = 0;
        }
        StopFlightSound();
        Released?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>_flightSound가 있으면 이 투사체에 붙은 비행음 반복을 즉시 멈춘다.</summary>
    private void StopFlightSound()
    {
        if (_flightSound != SoundId.None && Managers.IsInitialized)
            Managers.Sound.StopFor(gameObject, _flightSound, true, true);
    }

    void OnDisable()
    {
        if (!_flying) return;
        _flying = false;
        if (_telemetryAttackId != 0)
        {
            CombatTelemetrySignals.End(_telemetryAttackId, "Cancelled", transform.position);
            _telemetryAttackId = 0;
        }
        StopFlightSound();
        Released?.Invoke(this);
    }
}
