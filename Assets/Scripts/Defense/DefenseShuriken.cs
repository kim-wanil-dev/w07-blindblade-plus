using System;
using System.Collections.Generic;

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
    private static readonly HashSet<DefenseShuriken> _activeShurikens = new HashSet<DefenseShuriken>();

    [Header("비행")]
    [SerializeField, Min(0.1f)] private float _speed = 13f;
    [SerializeField, Min(0.1f)] private float _lifetime = 5f;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.3f;
    [SerializeField] private LayerMask _collisionMask = Physics.DefaultRaycastLayers;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _contacts = new Collider[16];
    private readonly List<EnemyHitCapsule> _capsuleContacts = new List<EnemyHitCapsule>();
    private readonly List<EnemyCapsuleHit> _capsuleHits = new List<EnemyCapsuleHit>();
    private GameObject _instigator;
    private PlayerHealth _target;
    private PlayerMotor _targetMotor;
    private BambooDefenseDirector _director;
    private float _damage;
    private bool _onboardingHarmless;
    private Vector3 _direction;
    private float _elapsed;
    private bool _flying;
    private int _attackId;
    private int _projectileId;
    private bool _terminalFlight;
    private bool _hasPassedPlayer;
    [Header("비행 외형")]
    [SerializeField, Range(0f, 45f), Tooltip("외형 진행 방향에 추가할 고정 하향 pitch 각도")]
    private float _visualPitchDownAngle = 12f;

    [SerializeField, Range(0f, 85f), Tooltip("외형 pitch가 월드 수평선에서 벗어날 수 있는 최대 각도")]
    private float _maximumVisualPitch = 50f;    

    [Header("소리")]
    [SerializeField, Tooltip("적대 비행 동안 수리검을 따라 반복하는 비행음. 반사·소멸하면 멈춘다.")]
    private SoundId _flightSound = SoundId.ShurikenFlight;

    [Header("현재 위치 조준·유도")]
    [SerializeField, Min(0f), Tooltip("플레이어 시점보다 낮게 조준하는 높이(미터)")]
    private float _aimDrop = 0.1f;
    [SerializeField, Min(0f), Tooltip("비행 중 현재 플레이어 위치로 꺾는 최대 회전 속도(도/초). 0이면 직선 비행한다.")]
    private float _homingTurnRate = 45f;
    [SerializeField, Min(0f), Tooltip("플레이어와의 거리 또는 이동 경로의 최근접 거리가 이 값 이내가 되면 유도를 영구히 멈춘다.")]
    private float _homingStopDistance = 5f;

    [Header("일반 패링 반사")]
    [SerializeField, Min(0f), Tooltip("반사 수리검이 적에게 주는 고정 피해")]
    private float _reflectDamage = 10f;
    [SerializeField, Min(1f), Tooltip("일반 패링으로 반사한 수리검의 속도 배율. 원래 비행 속도에 곱한다.")]
    private float _reflectSpeedMultiplier = 2f;
    private GameObject _reflectOwner;
    private EnemyHealth _reflectTarget;
    private EnemyHitCapsule _reflectTargetCapsule;
    private bool _reflected;

    public event Action<DefenseShuriken> Released;
    public bool IsFlying => _flying;
    public bool IsHostile => _flying && !_reflected;
    public bool HasPassedPlayer => _hasPassedPlayer;
    public int AttackId => _attackId;
    public int ProjectileId => _projectileId;

    void OnEnable()
    {
        _activeShurikens.Add(this);
    }

    /// <summary>적대 수리검의 현재 피해 구가 cylinder에 닿으면 비행 attackId를 출력한다.</summary>
    public bool TryGetDodgeThreat(DodgeThreatCylinder cylinder, out int attackId)
    {
        attackId = 0;
        if (!isActiveAndEnabled || !_flying || _reflected || _onboardingHarmless || _damage <= 0f
            || !cylinder.OverlapsSphere(transform.position, _hitRadius)) return false;
        attackId = _attackId;
        return true;
    }

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
        ApplyVisualPitch();

        int overlaps = Physics.OverlapSphereNonAlloc(transform.position, _hitRadius, _contacts, _collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps; i++)
        {
            if (!CanHit(_contacts[i]))
                continue;
            ReportAfterimage(transform.position, transform.position);
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
            ReportAfterimage(transform.position, transform.position + _direction * closestDistance);
            transform.position += _direction * closestDistance;
            ProcessHit(_hits[closest].collider, _hits[closest].point);
            return;
        }

        Vector3 start = transform.position;
        Vector3 end = start + _direction * distance;
        ReportAfterimage(start, end);
        UpdateFlightPhase(start, end, playerPoint);
        transform.position = end;
    }

    /// <summary>start부터 end까지 적대 수리검의 피해 궤적을 잔상에 조회하고 접촉 후보를 기록한다.</summary>
    private void ReportAfterimage(Vector3 start, Vector3 end)
    {
        if (_onboardingHarmless) return;
        DodgeAfterimage.ReportSphereSweep(start, end, _hitRadius,
            new DamageContext(_damage, _instigator, end, _direction, PATTERN_ID, _attackId, 1,
                IncomingAttackKind.Projectile));
    }

    void OnDisable()
    {
        _activeShurikens.Remove(this);
        if (!_flying)
            return;
        _flying = false;
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_attackId, "Cancelled", transform.position);
        Released?.Invoke(this);
    }

    /// <summary>현재 활성 상태에서 플레이어에게 적대적인 비반사 수리검을 반환한다.</summary>
    public static List<DefenseShuriken> GetActiveHostileShurikens()
    {
        List<DefenseShuriken> hostileShurikens = new List<DefenseShuriken>(_activeShurikens.Count);
        CopyActiveHostileShurikensTo(hostileShurikens);
        return hostileShurikens;
    }

    /// <summary>
    /// destination 목록을 비우고 활성 상태인 적대 수리검을 복사한다.
    /// 호출자가 destination을 재사용하면 조회마다 목록을 새로 할당하지 않는다.
    /// </summary>
    public static void CopyActiveHostileShurikensTo(List<DefenseShuriken> destination)
    {
        destination.Clear();
        foreach (DefenseShuriken shuriken in _activeShurikens)
            if (shuriken != null && shuriken.IsHostile)
                destination.Add(shuriken);
    }

    /// <summary>
    /// instigator, target, damage, direction, director를 설정하고 수리검 비행을 시작한다.
    /// 활성 전투 세션에서 공격 ID와 투사체 ID를 발급받아 비행 상태에 저장한다.
    /// </summary>
    public void Launch(GameObject instigator, PlayerHealth target, float damage, Vector3 direction, BambooDefenseDirector director, int attackId = 0)
    {
        _instigator = instigator;
        _target = target;
        _targetMotor = target.GetComponent<PlayerMotor>();
        _director = director;
        _damage = damage;
        _onboardingHarmless = false;
        _direction = direction.normalized;
        CombatSessionRuntime session = CombatSessionRuntime.Active;
        _attackId = attackId != 0 ? attackId : BlindBlade.Telemetry.CombatTelemetrySignals.Begin(PATTERN_ID, "Projectile", transform.position, target.transform.position);
        BlindBlade.Telemetry.CombatTelemetrySignals.Commit(_attackId, transform.position);
        _projectileId = session.AllocateProjectileId();
        _elapsed = 0f;
        _terminalFlight = false;
        _hasPassedPlayer = false;
        _flying = true;
        ApplyVisualPitch();
        // 소멸하면 소유자 파괴로 SoundManager가 정지하고, 반사는 TryReflect에서 직접 멈춘다.
        if (_flightSound != SoundId.None)
            Managers.Sound.PlayAttachedLoop(_flightSound, gameObject);
    }

    /// <summary>온보딩에서 완료한 회피 목표의 남은 수리검을 비행시킨 채 플레이어 피해와 잔상 위협을 끈다.</summary>
    public void MakeHarmlessForOnboarding()
    {
        _onboardingHarmless = true;
    }

    /// <summary>motor의 현재 시점보다 _aimDrop만큼 낮은 위치를 발사·유도 공통 조준점으로 반환한다.</summary>
    internal Vector3 GetCurrentAimPoint(PlayerMotor motor)
    {
        return motor.ViewOrigin - Vector3.up * _aimDrop;
    }

    /// <summary>
    /// distance와 현재 직진 상태를 사용해, 아직 유도 중인 수리검만 플레이어의 현재 위치로 회전시킨다.
    /// 회전이 허용되면 _direction과 transform.rotation을 변경한다.
    /// </summary>
    private void SteerTowardsTarget(float distance)
    {
        if (_terminalFlight || _homingTurnRate <= 0f || distance <= _homingStopDistance)
            return;

        Vector3 aim = GetCurrentAimPoint(_targetMotor) - transform.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;

        _direction = Vector3.RotateTowards(_direction, aim.normalized, _homingTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;
    }

    /// <summary>
/// 진행 방향의 pitch에 고정 하향 각도를 적용한다.
/// _direction을 사용하며, 수리검의 외형 회전을 갱신한다.
/// </summary>
private void ApplyVisualPitch()
{
    Vector3 horizontalFlight = Vector3.ProjectOnPlane(_direction, Vector3.up);
    float horizontalFlightMagnitude = horizontalFlight.magnitude;
    Vector3 horizontalHeading = horizontalFlight;

    if (horizontalHeading.sqrMagnitude < 0.0001f)
        horizontalHeading = Vector3.ProjectOnPlane(_targetMotor.ViewForward, Vector3.up);

    if (horizontalHeading.sqrMagnitude < 0.0001f)
        return;

    float flightPitch = Mathf.Atan2(_direction.y, horizontalFlightMagnitude) * Mathf.Rad2Deg;
    float visualPitch = flightPitch - _visualPitchDownAngle;
    visualPitch = Mathf.Clamp(visualPitch, -_maximumVisualPitch, _maximumVisualPitch);

    float pitchRadians = visualPitch * Mathf.Deg2Rad;
    Vector3 visualForward =
        horizontalHeading.normalized * Mathf.Cos(pitchRadians) +
        Vector3.up * Mathf.Sin(pitchRadians);

    transform.rotation = Quaternion.LookRotation(visualForward, Vector3.up);
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
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_attackId, "Miss", transform.position);
        Released?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>
    /// start에서 end까지 radius 반경으로 쓸고 지나간 공간에 판정 구가 닿은 적대 비행 수리검을 모두 그 위치에서 가드 성공 소리를 내며 없앤다.
    /// 플레이어 검기와 돌진이 경로의 수리검을 베어 없앨 때 쓰며, 반사된 수리검은 그대로 둔다.
    /// </summary>
    public static void ReleaseInSweep(Vector3 start, Vector3 end, float radius)
    {
        Vector3 travel = end - start;
        float travelLengthSquared = travel.sqrMagnitude;
        foreach (DefenseShuriken shuriken in FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None))
        {
            if (!shuriken._flying)
                continue;
            Vector3 position = shuriken.transform.position;
            float progress = travelLengthSquared > 0f ? Mathf.Clamp01(Vector3.Dot(position - start, travel) / travelLengthSquared) : 0f;
            float reach = radius + shuriken._hitRadius;
            if ((position - (start + travel * progress)).sqrMagnitude > reach * reach)
                continue;
            shuriken.ReleaseBlocked();
        }
    }

    /// <summary>
    /// isInRange가 true를 반환하는 위치의 적대 비행 수리검을 가드 성공 소리와 함께 없앤다.
    /// 반사되어 아군이 된 수리검은 유지한다.
    /// </summary>
    public static void ReleaseInRange(Predicate<Vector3> isInRange)
    {
        foreach (DefenseShuriken shuriken in FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None))
        {
            if (!shuriken.IsHostile || !isInRange(shuriken.transform.position)) continue;
            shuriken.ReleaseBlocked();
        }
    }

    /// <summary>
    /// 플레이어 공격에 막힌 적대 수리검의 현재 외형을 두 낙하 조각으로 바꾸고 가드 성공 소리를 낸다.
    /// 비행을 즉시 종료하고 Released를 알리며 원본을 제거한다. 이미 종료되거나 반사된 수리검은 유지한다.
    /// </summary>
    public void ReleaseBlocked()
    {
        if (!IsHostile) return;
        ShurikenCutFragment.Spawn(transform, _collisionMask);
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_attackId, "Destroyed", transform.position);
        Managers.Sound.PlayCue(CombatSoundCue.Guard, transform.position);
        Release();
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
    /// 퍼펙트 패링이면 반사 비행으로 바꾸고, 그 외 결과나 반사할 적이 없으면 수리검을 소멸한다.
    /// 처리기가 없을 때만 기존 체력 API로 피해를 적용한다.
    /// </summary>
    private void ProcessHit(Collider collider, Vector3 point)
    {
        PlayerHealth player = collider.GetComponentInParent<PlayerHealth>();
        if (player != null)
        {
            if (_onboardingHarmless)
            {
                Release();
                return;
            }
            DamageContext context = new DamageContext(_damage, _instigator, point, _direction, PATTERN_ID,
                _attackId, 1, IncomingAttackKind.Projectile);
            IPlayerDefenseResolver resolver = player.GetComponent<IPlayerDefenseResolver>();
            if (resolver != null)
            {
                DefenseResolution resolution = resolver.ResolveIncomingAttack(context);
                if (resolution.Outcome == DefenseOutcome.PerfectParry && TryReflect(player.gameObject))
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
        _reflectTargetCapsule = null;
        _direction = motor.ViewForward.normalized;

        if (_director.TryFindDetectedMelee(transform.position, viewCamera, perceptionMask, out EnemyHealth target))
        {
            _reflectTarget = target;
            _reflectTargetCapsule = target.CreateCombatTarget().HitCapsule;
            AimAtReflectTarget();
        }
        else
        {
            transform.rotation = Quaternion.LookRotation(_direction);
        }

        _elapsed = 0f;
        _flying = false;
        _reflected = true;
        // 반사 비행은 더 빨라 같은 비행음을 쓰지 않는다.
        if (_flightSound != SoundId.None)
            Managers.Sound.StopFor(gameObject, _flightSound, true, true);
        ShowAsReflected();
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_attackId, "Miss", transform.position);
        Released?.Invoke(this);
        return true;
    }

    /// <summary>
    /// 반사한 수리검을 흰 윤곽선으로 보이게 바꾼다. 빨간 위협 분류(OutlineLayer)를 끄고,
    /// 파티클을 제외한 자식 렌더러 재질을 흰 Unlit 재질로 바꾼 뒤 인지와 무관하게 보이는 OutlineVFX를 붙인다.
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
        // 연기 파티클은 텍스처 모양을 잃지 않도록 원래 재질을 유지한다.
        foreach (Renderer target in GetComponentsInChildren<Renderer>())
            if (!(target is ParticleSystemRenderer))
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
            StrikeReflected(null, _contacts[i].ClosestPoint(transform.position));
            return;
        }
        EnemyHitCapsule.OverlapAll(transform.position, transform.position, _hitRadius, reflectedMask, _capsuleContacts);
        foreach (EnemyHitCapsule capsule in _capsuleContacts)
        {
            if (!CanHitReflected(capsule))
                continue;
            StrikeReflected(capsule.Health, capsule.ClosestPoint(transform.position));
            return;
        }

        float distance = _speed * _reflectSpeedMultiplier * Time.deltaTime;
        int count = Physics.SphereCastNonAlloc(transform.position, _hitRadius, _direction, _hits, distance, reflectedMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        EnemyHealth struckEnemy = null;
        Vector3 struckPoint = transform.position;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (!CanHitReflected(_hits[i].collider) || _hits[i].distance >= closestDistance)
                continue;
            found = true;
            struckPoint = _hits[i].point;
            closestDistance = _hits[i].distance;
        }
        EnemyHitCapsule.SweepAll(transform.position, transform.position, _hitRadius, _direction, distance,
            reflectedMask, _capsuleHits);
        foreach (EnemyCapsuleHit hit in _capsuleHits)
        {
            if (!CanHitReflected(hit.Capsule) || hit.Distance >= closestDistance)
                continue;
            found = true;
            struckEnemy = hit.Capsule.Health;
            struckPoint = hit.Point;
            closestDistance = hit.Distance;
        }

        if (found)
        {
            transform.position += _direction * closestDistance;
            StrikeReflected(struckEnemy, struckPoint);
            return;
        }
        transform.position += _direction * distance;
    }

    /// <summary>반사 대상 피격 캡슐 중심으로 _direction과 수리검 회전을 바꾼다. 이미 중심에 있으면 방향을 유지한다.</summary>
    private void AimAtReflectTarget()
    {
        Vector3 aim = _reflectTargetCapsule.Bounds.center - transform.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;
        _direction = aim.normalized;
        transform.rotation = Quaternion.LookRotation(_direction);
    }

    /// <summary>
    /// collider가 반사 수리검에 맞을 수 있는 기존 지형인지 판정한다.
    /// 자신, 플레이어, 적 계층은 제외하고 추가 검색한 레이어의 비적 대상도 통과시킨다. 적은 피격 캡슐로 따로 판정한다.
    /// </summary>
    private bool CanHitReflected(Collider collider)
    {
        if (collider.transform.IsChildOf(transform)
            || collider.GetComponentInParent<PlayerHealth>() != null
            || collider.GetComponentInParent<EnemyHealth>() != null
            || collider.GetComponentInParent<BambooThrower>() != null)
            return false;

        int layerBit = 1 << collider.gameObject.layer;
        return (_collisionMask.value & layerBit) != 0 && collider.GetComponentInParent<IDamageable>() == null;
    }

    /// <summary>capsule이 반사 수리검에 맞을 수 있는 생존 적이면 true를 반환한다. 투척수는 제외한다.</summary>
    private bool CanHitReflected(EnemyHitCapsule capsule)
    {
        return capsule.Health != null && capsule.Health.IsAlive
            && capsule.GetComponentInParent<BambooThrower>() == null;
    }

    /// <summary>
    /// enemy가 있으면 point에 reflectDamage만큼의 PlayerOwned 피해를 한 번 적용한 뒤 수리검을 파괴한다.
    /// 지형에 닿아 enemy가 null이면 피해 없이 파괴한다.
    /// </summary>
    private void StrikeReflected(EnemyHealth enemy, Vector3 point)
    {
        if (enemy != null)
            enemy.TakeDamage(new DamageContext(_reflectDamage, _reflectOwner, point, _direction,
                REFLECT_PATTERN_ID, _attackId, 1, IncomingAttackKind.PlayerOwned));
        _reflected = false;
        Destroy(gameObject);
    }
}
