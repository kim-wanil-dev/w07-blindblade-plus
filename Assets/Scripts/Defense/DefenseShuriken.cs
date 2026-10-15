using System;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 투척수가 던진 수리검의 예측 조준 비행과 플레이어 적중을 처리한다.
/// 접촉은 회피 중에도 플레이어 방어 판정에 그대로 전달한다. 일반 패링이면 가장 가까운 근접 적에게 반사하고,
/// 퍼펙트 패링·가드·회피면 수리검만 소멸한다. 반사한 수리검은 빨간 위협 실루엣 대신 인지와 무관한 흰 윤곽선으로 보인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseShuriken : MonoBehaviour
{
    private const string PATTERN_ID = "DefenseShuriken";
    private const string REFLECT_PATTERN_ID = "DefenseShurikenReflect";
    private const string REFLECT_SHADER_NAME = "Universal Render Pipeline/Unlit";

    private static Material _reflectMaterial;

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
    private float _elapsed;
    private bool _flying;
    private int _attackId;
    private int _projectileId;
    private bool _terminalFlight;
    private bool _hasPassedPlayer;

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

    [Header("일반 패링 반사")]
    [SerializeField, Min(1f), Tooltip("일반 패링으로 반사한 수리검의 속도 배율. 원래 비행 속도에 곱한다.")]
    private float _reflectSpeedMultiplier = 2f;
    private GameObject _reflectOwner;
    private EnemyHealth _reflectTarget;
    private Collider _reflectTargetCollider;
    private bool _reflected;

    public event Action<DefenseShuriken> Released;
    public bool IsFlying => _flying;
    public bool HasPassedPlayer => _hasPassedPlayer;
    public int AttackId => _attackId;
    public int ProjectileId => _projectileId;

    void Update()
    {
        if (_reflected)
        {
            UpdateReflected();
            return;
        }
        if (!_flying)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime || _target == null || !_target.IsAlive)
        {
            Release();
            return;
        }

        Vector3 playerPoint = _targetMotor.ViewOrigin;
        UpdateFlightPhase(transform.position, transform.position, playerPoint);
        SteerTowardsTarget(Vector3.Distance(playerPoint, transform.position));

        int overlaps = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius, _contacts, _collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!CanHit(_contacts[i]))
                continue;
            ProcessHit(_contacts[i], _contacts[i].ClosestPoint(transform.position));
            return;
        }

        float distance = _speed * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits, distance, _collisionMask, QueryTriggerInteraction.Ignore);
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

        Vector3 start = transform.position;
        Vector3 end = start + _direction * distance;
        UpdateFlightPhase(start, end, playerPoint);
        transform.position = end;
    }

    void OnDisable()
    {
        if (!_flying)
            return;
        _flying = false;
        Released?.Invoke(this);
    }

    /// <summary>
    /// instigator, target, damage, direction, director를 설정하고 수리검 비행을 시작한다.
    /// 활성 전투 세션에서 공격 ID와 투사체 ID를 발급받아 비행 상태에 저장한다.
    /// </summary>
    public void Launch(GameObject instigator, PlayerHealth target, float damage, Vector3 direction,
        BambooDefenseDirector director)
    {
        _instigator = instigator;
        _target = target;
        _targetMotor = target.GetComponent<PlayerMotor>();
        _director = director;
        _damage = damage;
        _direction = direction.normalized;
        CombatSessionRuntime session = CombatSessionRuntime.Active;
        _attackId = session.AllocateAttackId();
        _projectileId = session.AllocateProjectileId();
        _elapsed = 0f;
        _terminalFlight = false;
        _hasPassedPlayer = false;
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
    /// distance와 현재 직진 상태를 사용해, 아직 유도 중인 수리검만 플레이어 예측 위치로 회전시킨다.
    /// 회전이 허용되면 _direction과 transform.rotation을 변경한다.
    /// </summary>
    private void SteerTowardsTarget(float distance)
    {
        if (_terminalFlight || _homingTurnRate <= 0f || distance <= _homingStopDistance)
            return;

        Vector3 aim = PredictAimPoint(transform.position, _targetMotor, _director.PlayerVelocity) - transform.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;

        _direction = Vector3.RotateTowards(_direction, aim.normalized, _homingTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// start부터 end까지의 이동 구간과 playerPoint를 사용해 근접 진입과 통과를 판정한다.
    /// 근접 시 _terminalFlight를 고정하고, 플레이어가 진행 방향 뒤로 넘어가면 _hasPassedPlayer도 고정한다.
    /// </summary>
    private void UpdateFlightPhase(Vector3 start, Vector3 end, Vector3 playerPoint)
    {
        if (!_terminalFlight)
        {
            Vector3 travel = end - start;
            float travelLengthSquared = travel.sqrMagnitude;
            float progress = travelLengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(playerPoint - start, travel) / travelLengthSquared) : 0f;
            Vector3 nearest = start + travel * progress;

            if ((playerPoint - nearest).sqrMagnitude <= _homingStopDistance * _homingStopDistance) _terminalFlight = true;
        }

        if (!_hasPassedPlayer && Vector3.Dot(playerPoint - end, _direction) <= 0f)
        {
            _hasPassedPlayer = true;
            _terminalFlight = true;
        }
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
    /// 일반 패링이면 반사 비행으로 바꾸고, 그 외 결과나 반사할 적이 없으면 수리검을 소멸한다.
    /// 처리기가 없을 때만 기존 체력 API로 피해를 적용한다.
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
                if (resolution.Outcome == DefenseOutcome.NormalParry && TryReflect(player.gameObject))
                    return;
            }
            else
                player.TakeDamage(context);
        }
        Release();
    }

    /// <summary>
    /// owner의 수평 시선 방향으로 수리검을 반사한다.
    /// 화면에서 인지된 적이 있으면 그 적을 목표로 저장하고,
    /// 반사 상태와 적대 비행 종료를 확정한 뒤 true를 반환한다.
    /// </summary>
    private bool TryReflect(GameObject owner)
    {
        PlayerMotor motor = owner.GetComponent<PlayerMotor>();
        Camera viewCamera = owner.GetComponentInChildren<Camera>();
        SensoryPerceptionMask perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();

        _reflectOwner = owner;
        _reflectTarget = null;
        _reflectTargetCollider = null;
        _direction = motor.ViewForward.normalized;

        if (_director.TryFindDetectedMelee(transform.position, viewCamera, perceptionMask, out EnemyHealth target))
        {
            _reflectTarget = target;
            _reflectTargetCollider = target.CreateCombatTarget().HitCollider;
            AimAtReflectTarget();
        }
        else
        {
            transform.rotation = Quaternion.LookRotation(_direction);
        }

        _elapsed = 0f;
        _flying = false;
        _reflected = true;
        ShowAsReflected();
        Released?.Invoke(this);
        return true;
    }

    /// <summary>
    /// 반사한 수리검을 흰 윤곽선으로 보이게 바꾼다. 빨간 위협 분류(OutlineLayer)를 끄고,
    /// 자식 렌더러 재질을 흰 Unlit 재질로 바꾼 뒤 인지와 무관하게 보이는 OutlineVFX를 붙인다.
    /// 재질은 윤곽선 검출에만 쓰이며 처음 반사할 때 한 번 만들어 모든 수리검이 함께 쓴다.
    /// </summary>
    private void ShowAsReflected()
    {
        if (TryGetComponent(out OutlineLayer threatLayer))
            threatLayer.enabled = false;
        if (_reflectMaterial == null)
        {
            _reflectMaterial = new Material(Shader.Find(REFLECT_SHADER_NAME)) { name = "DefenseShurikenReflect" };
            _reflectMaterial.SetColor("_BaseColor", Color.white);
        }
        // 어두운 장면에서 원래 Lit 재질은 검게 그려져 흰 윤곽선이 검출되지 않으므로 Unlit 흰색으로 바꾼다.
        foreach (Renderer target in GetComponentsInChildren<Renderer>())
            target.sharedMaterial = _reflectMaterial;
        gameObject.AddComponent<OutlineVFX>().IgnorePerception = true;
    }

    /// <summary>
    /// 반사 수리검을 반사 속도로 대상 근접 적에게 유도해 이동한다. 대상이 죽으면 마지막 방향으로 직진한다.
    /// 경로에서 처음 닿은 생존 적에게 남은 체력 전체의 플레이어 소유 피해를 주고, 적이나 지형에 닿거나 수명이 끝나면 파괴된다.
    /// </summary>
    private void UpdateReflected()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= _lifetime)
        {
            Destroy(gameObject);
            return;
        }
        if (_reflectTarget != null && _reflectTarget.IsAlive) AimAtReflectTarget();

        int reflectedMask = _collisionMask.value | Physics.IgnoreRaycastLayer;

        int overlaps = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius, _contacts, reflectedMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!CanHitReflected(_contacts[i]))
                continue;
            StrikeReflected(_contacts[i], _contacts[i].ClosestPoint(transform.position));
            return;
        }

        float distance = _speed * _reflectSpeedMultiplier * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits, distance, reflectedMask, QueryTriggerInteraction.Ignore);
        int closest = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!CanHitReflected(_hits[i].collider) || _hits[i].distance >= closestDistance)
                continue;
            closest = i;
            closestDistance = _hits[i].distance;
        }

        if (closest >= 0)
        {
            transform.position += _direction * closestDistance;
            StrikeReflected(_hits[closest].collider, _hits[closest].point);
            return;
        }
        transform.position += _direction * distance;
    }

    /// <summary>반사 대상 충돌체 중심으로 _direction과 수리검 회전을 바꾼다. 이미 중심에 있으면 방향을 유지한다.</summary>
    private void AimAtReflectTarget()
    {
        Vector3 aim = _reflectTargetCollider.bounds.center - transform.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;
        _direction = aim.normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// collider가 반사 수리검에 맞을 수 있는 생존 적 또는 기존 지형인지 판정한다.
    /// 자신, 플레이어, 투척수는 제외하고 추가 검색한 레이어의 비적 대상도 통과시킨다.
    /// </summary>
    private bool CanHitReflected(Collider collider)
    {
        if (collider.transform.IsChildOf(transform)
            || collider.GetComponentInParent<PlayerHealth>() != null
            || collider.GetComponentInParent<BambooThrower>() != null)
            return false;

        EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            return enemy.IsAlive;

        int layerBit = 1 << collider.gameObject.layer;
        return (_collisionMask.value & layerBit) != 0 && collider.GetComponentInParent<IDamageable>() == null;
    }

    /// <summary>
    /// collider가 생존 적이면 point에 남은 체력 전체의 PlayerOwned 피해를 한 번 적용한 뒤 수리검을 파괴한다.
    /// 지형이면 피해 없이 파괴한다.
    /// </summary>
    private void StrikeReflected(Collider collider, Vector3 point)
    {
        EnemyHealth enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            enemy.TakeDamage(new DamageContext(Mathf.Max(enemy.CurrentHealth, 1f), _reflectOwner, point, _direction,
                REFLECT_PATTERN_ID, _attackId, 1, IncomingAttackKind.PlayerOwned));
        _reflected = false;
        Destroy(gameObject);
    }
}
