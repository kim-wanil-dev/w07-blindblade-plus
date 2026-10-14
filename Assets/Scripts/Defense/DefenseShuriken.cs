using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 투척수가 던진 수리검의 예측 조준 비행과 플레이어 적중을 처리한다.
/// 정확한 패링 또는 근접 회피면 투척 위치로 관통 검기를 발사하고 일반 가드면 수리검만 소멸한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseShuriken : MonoBehaviour
{
    private const string PATTERN_ID = "DefenseShuriken";

    [Header("비행")]
    [SerializeField, Min(0.1f)] private float _speed = 13f;
    [SerializeField, Min(0.1f)] private float _lifetime = 5f;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.3f;
    [SerializeField] private LayerMask _collisionMask = Physics.DefaultRaycastLayers;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _contacts = new Collider[16];
    private GameObject _instigator;
    private PlayerHealth _target;
    private PlayerMotor _targetMotor;
    private BambooDefenseDirector _director;
    private float _damage;
    private Vector3 _direction;
    private Vector3 _returnPoint;
    private float _elapsed;
    private bool _flying;
    private int _attackId;
    private int _projectileId;

    [Header("예측 조준·유도")]
    [SerializeField, Min(0f), Tooltip("플레이어 시점보다 낮게 조준하는 높이(미터)")]
    private float _aimDrop = 0.1f;
    [SerializeField, Range(0f, 1f), Tooltip("1이면 현재 속도 기준 정확한 요격 지점, 0이면 현재 위치를 조준한다.")]
    private float _leadAccuracy = 0.8f;
    [SerializeField, Min(0f), Tooltip("예측으로 앞당기는 최대 거리(미터). 회피 순간의 속도로 과하게 앞서지 않게 한다.")]
    private float _maximumLeadDistance = 4f;
    [SerializeField, Min(0f), Tooltip("비행 중 예측 지점으로 꺾는 최대 회전 속도(도/초). 0이면 직선 비행한다.")]
    private float _homingTurnRate = 45f;
    [SerializeField, Min(0f), Tooltip("플레이어와 이 거리 안에서는 유도를 멈추고 직진해 패링·회피 타이밍을 유지한다.")]
    private float _homingStopDistance = 4f;

    [Header("패링·회피 검기")]
    [SerializeField] private DefenseSwordWave _wavePrefab;
    [SerializeField, Min(0f), Tooltip("플레이어가 회피 중일 때 이 거리 안의 수리검을 검기로 되돌린다.")]
    private float _dodgeCounterRadius = 3f;

    public event Action<DefenseShuriken> Released;
    public bool IsFlying => _flying;
    public int AttackId => _attackId;
    public int ProjectileId => _projectileId;

    void Update()
    {
        if (!_flying)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime || _target == null || !_target.IsAlive)
        {
            Release();
            return;
        }

        Vector3 toTarget = _targetMotor.ViewOrigin - transform.position;
        if (_targetMotor.IsDodging && toTarget.sqrMagnitude <= _dodgeCounterRadius * _dodgeCounterRadius)
        {
            LaunchWave(_target);
            Release();
            return;
        }
        SteerTowardsTarget(toTarget.magnitude);

        int overlaps = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius, _contacts,
            _collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!CanHit(_contacts[i]))
                continue;
            ProcessHit(_contacts[i], _contacts[i].ClosestPoint(transform.position));
            return;
        }

        float distance = _speed * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits,
            distance, _collisionMask, QueryTriggerInteraction.Ignore);
        int closest = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!CanHit(_hits[i].collider) || _hits[i].distance >= closestDistance)
                continue;
            closest = i;
            closestDistance = _hits[i].distance;
        }

        if (closest >= 0)
        {
            transform.position += _direction * closestDistance;
            ProcessHit(_hits[closest].collider, _hits[closest].point);
            return;
        }
        transform.position += _direction * distance;
    }

    void OnDisable()
    {
        if (!_flying)
            return;
        _flying = false;
        Released?.Invoke(this);
    }

    /// <summary>
    /// instigator, target, damage, direction, returnPoint, director를 설정하고 수리검 비행을 시작한다.
    /// 활성 전투 세션에서 공격 ID와 투사체 ID를 발급받아 비행 상태에 저장한다.
    /// </summary>
    public void Launch(GameObject instigator, PlayerHealth target, float damage, Vector3 direction, Vector3 returnPoint,
        BambooDefenseDirector director)
    {
        _instigator = instigator;
        _target = target;
        _targetMotor = target.GetComponent<PlayerMotor>();
        _director = director;
        _damage = damage;
        _direction = direction.normalized;
        _returnPoint = returnPoint;
        CombatSessionRuntime session = CombatSessionRuntime.Active;
        _attackId = session.AllocateAttackId();
        _projectileId = session.AllocateProjectileId();
        _elapsed = 0f;
        _flying = true;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// origin에서 이 수리검 속도로 날아갈 때 motor 시점 아래 조준점이 velocity로 이동할 위치를 예측해 반환한다.
    /// 비행 시간을 세 번 보정하며 예측 정확도와 최대 예측 거리로 앞당김을 제한한다.
    /// </summary>
    internal Vector3 PredictAimPoint(Vector3 origin, PlayerMotor motor, Vector3 velocity)
    {
        Vector3 target = motor.ViewOrigin - Vector3.up * _aimDrop;
        Vector3 lead = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            float time = Vector3.Distance(origin, target + lead) / _speed;
            lead = Vector3.ClampMagnitude(velocity * (time * _leadAccuracy), _maximumLeadDistance);
        }
        return target + lead;
    }

    /// <summary>
    /// distance가 유도 종료 거리보다 멀면 예측 조준점 방향으로 최대 회전 속도만큼 비행 방향을 꺾는다.
    /// 가까운 구간은 직진하며 변경된 방향으로 수리검 회전을 맞춘다.
    /// </summary>
    private void SteerTowardsTarget(float distance)
    {
        if (_homingTurnRate <= 0f || distance <= _homingStopDistance)
            return;
        Vector3 aim = PredictAimPoint(transform.position, _targetMotor, _director.PlayerVelocity) - transform.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;
        _direction = Vector3.RotateTowards(_direction, aim.normalized,
            _homingTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// 비행을 종료하고 Released 구독자에게 한 번 알린 뒤 수리검을 파괴한다.
    /// 이미 종료된 수리검의 반복 호출은 무시한다.
    /// </summary>
    public void Release()
    {
        if (!_flying)
            return;
        _flying = false;
        Released?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>
    /// collider가 자신과 적을 제외한 대상 플레이어 또는 지형이면 true를 반환한다.
    /// 다른 피해 수신자는 통과하여 대상 플레이어에게만 피해를 전달한다.
    /// </summary>
    private bool CanHit(Collider collider)
    {
        if (collider.transform.IsChildOf(transform) || collider.GetComponentInParent<EnemyHealth>() != null)
            return false;
        PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
        return player == null ? collider.GetComponentInParent<IDamageable>() == null : player == _target;
    }

    /// <summary>
    /// collider의 point에 공격 ID와 Projectile 종류를 붙여 A의 단일 방어 판정에 전달한다.
    /// 완벽한 패링이면 검기를 발사하고 처리기가 없을 때만 기존 체력 API로 피해를 적용한다.
    /// </summary>
    private void ProcessHit(Collider collider, Vector3 point)
    {
        PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
        if (player != null)
        {
            DamageContext context = new DamageContext(_damage, _instigator, point, _direction, PATTERN_ID,
                _attackId, 1, IncomingAttackKind.Projectile);
            IPlayerDefenseResolver resolver = player.GetComponent<IPlayerDefenseResolver>();
            if (resolver != null)
            {
                DefenseResolution resolution = resolver.ResolveIncomingAttack(context);
                if (resolution.Outcome == DefenseOutcome.PerfectParry)
                    LaunchWave(player);
            }
            else
                player.TakeDamage(context);
        }
        Release();
    }

    /// <summary>
    /// player의 시점 위치에서 투척 위치 방향으로 관통 검기를 생성한다. 정확한 패링과 근접 회피에서 사용한다.
    /// 검기는 플레이어 소유로 적 피해를 적용하며 수리검의 비행 상태는 변경하지 않는다.
    /// </summary>
    private void LaunchWave(PlayerHealth player)
    {
        Vector3 origin = _targetMotor.ViewOrigin;
        Vector3 direction = _returnPoint - origin;
        if (direction.sqrMagnitude < 0.0001f)
            direction = -_direction;
        DefenseSwordWave wave = Instantiate(_wavePrefab, origin, Quaternion.LookRotation(direction.normalized));
        wave.Launch(player.gameObject, origin, direction.normalized);
    }
}
