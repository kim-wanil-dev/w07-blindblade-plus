using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using UnityEngine.AI;

/// <summary>인간 보스의 단독·탑승 사격과 탄환 수명 및 반사 적중 알림을 관리한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth), typeof(EnemyMotor), typeof(NavMeshAgent))]
public sealed class HumanBossAttack : MonoBehaviour
{
    [Header("사격 연결")]
    [SerializeField] private TwinBossProjectile _projectilePrefab;
    [SerializeField] private Transform _muzzle;
    [SerializeField] private Collider _hitCollider;
    [SerializeField, Min(0f)] private float _damage = 15f;
    [SerializeField, Min(0.1f)] private float _preparation = 0.8f;
    [SerializeField, Min(0.1f)] private float _shotInterval = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("탑승 연속 사격의 발사 후 간격(초)")]
    private float _continuousShotInterval = 0.5f;
    [SerializeField, Min(1)] private int _soloShots = 3;
    [SerializeField] private SoundId _preparationSound = SoundId.EnemyRangedAttackVoice;
    private readonly List<TwinBossProjectile> _projectiles = new List<TwinBossProjectile>();
    private EnemyHealth _health;
    private EnemyMotor _motor;
    private NavMeshAgent _agent;
    private KatanaDamageReceiver _player;
    private PlayerMotor _playerMotor;
    private Coroutine _shooting;

    [Header("단독 사격 순간이동")]
    [SerializeField, Min(1f), Tooltip("플레이어 주변 순간이동 반경(미터)")]
    private float _teleportRadius = 6f;
    [SerializeField, Range(1f, 180f), Tooltip("플레이어 수평 시점 기준 등장 각도(도)")]
    private float _teleportArc = 180f;
    [SerializeField] private LayerMask _teleportObstacleMask = Physics.DefaultRaycastLayers;
    private readonly Collider[] _teleportContacts = new Collider[16];
    private NavMeshPath _teleportPath;

    [Header("탑승 상태")]
    [SerializeField, Min(0.5f)] private float _dismountDistance = 2f;
    private readonly Collider[] _dismountContacts = new Collider[16];
    private Transform _seat;
    private bool _mounted;
    private Vector3 _navigationPosition;
    private bool _previousUpdatePosition;
    private bool _previousUpdateRotation;

    [Header("탑승 난사")]
    [SerializeField, Min(1), Tooltip("한 번에 부채꼴로 던지는 수리검 수")]
    private int _barrageShots = 3;
    [SerializeField, Range(0f, 60f), Tooltip("부채꼴 양 끝 수리검 사이의 수평 각도(도)")]
    private float _barrageSpreadAngle = 16f;
    [SerializeField, Min(0.05f), Tooltip("묶음마다 던지기 전 준비 시간(초)")]
    private float _barragePreparation = 0.2f;
    [SerializeField, Min(0.05f), Tooltip("묶음을 던진 뒤 다음 준비까지의 간격(초)")]
    private float _barrageInterval = 0.4f;

    [Header("근접 베기")]
    [SerializeField, Min(0.5f), Tooltip("베기 직전 순간이동하는 플레이어와의 거리(미터)")]
    private float _slashDistance = 2.6f;
    [SerializeField, Range(1f, 360f), Tooltip("플레이어 수평 시점 기준 베기 등장 각도 범위(도). 막기 가능한 정면 쪽으로 좁힌다.")]
    private float _slashArc = 100f;
    [SerializeField, Min(0.05f), Tooltip("등장 뒤 붉게 물들며 베기까지 기다리는 시간(초)")]
    private float _slashTelegraph = 0.45f;
    [SerializeField, Min(0f), Tooltip("베기 돌진이 플레이어 위치를 지나 더 나아가는 거리(미터)")]
    private float _slashOvershoot = 1.5f;
    [SerializeField, Min(0.01f), Tooltip("베기 돌진 시간(초)")]
    private float _slashDuration = 0.14f;
    [SerializeField, Min(0.01f), Tooltip("베기 돌진이 플레이어에 닿았다고 보는 몸 반경(미터)")]
    private float _slashContactRadius = 0.8f;
    [SerializeField, Min(0f)] private float _slashDamage = 12f;
    [SerializeField] private SoundId _slashTelegraphSound = SoundId.EnemyMeleeWarning;
    [SerializeField] private SoundId _slashSound = SoundId.EnemySwordSwing;
    private readonly RaycastHit[] _slashHits = new RaycastHit[16];
    private TwinBossWarningVisual _warning;
    private bool _slashing;

    public EnemyHealth Health => _health;
    public bool IsAttacking => _shooting != null;
    public bool HasProjectiles => _projectiles.Count > 0;
    public bool IsReady => _motor.IsReady;
    public bool IsMounted => _mounted;
    public bool IsSlashing => _slashing;
    public bool IsConfigured => _projectilePrefab != null && _muzzle != null && _hitCollider != null;
    public event Action ShotPreparing;
    public event Action ShotFired;
    public event Action ReflectedHit;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _motor = GetComponent<EnemyMotor>();
        _agent = GetComponent<NavMeshAgent>();
        _warning = GetComponent<TwinBossWarningVisual>();
        _teleportPath = new NavMeshPath();
    }

    void LateUpdate()
    {
        if (_seat != null && _health.IsAlive)
            transform.SetPositionAndRotation(_seat.position, _seat.rotation);
    }

    /// <summary>player에게 continuous 연사 또는 설정 횟수 사격을 시작하며 teleportBetweenShots로 발사 사이 순간이동을 제어한다.</summary>
    public void StartShooting(KatanaDamageReceiver player, bool continuous, bool teleportBetweenShots = true)
    {
        StopShooting();
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        _motor.Stop();
        _shooting = StartCoroutine(Shoot(continuous, teleportBetweenShots));
    }

    /// <summary>continuous로 플레이어 시점보다 0.1m 아래를 향한 전조·직선 발사·휴식을 반복하고 teleportBetweenShots이면 사격 사이에 순간이동하며 실행 참조를 정리한다.</summary>
    private IEnumerator Shoot(bool continuous, bool teleportBetweenShots)
    {
        yield return null;
        int shots = 0;
        while (_health.IsAlive && _player != null && _player.IsAlive && (continuous || shots < _soloShots))
        {
            ShotPreparing?.Invoke();
            if (_preparationSound != SoundId.None)
                Managers.Sound.Play(_preparationSound, transform.position, 1f, gameObject);
            yield return new WaitForSeconds(_preparation);
            if (!_health.IsAlive || _player == null || !_player.IsAlive) break;
            Vector3 aimPosition = _playerMotor.ViewOrigin - Vector3.up * 0.1f;
            Vector3 direction = (aimPosition - _muzzle.position).normalized;
            if (direction.sqrMagnitude > 0.000001f)
            {
                Vector3 facing = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (!_mounted && facing.sqrMagnitude > 0.000001f)
                    transform.rotation = Quaternion.LookRotation(facing);
                direction = (aimPosition - _muzzle.position).normalized;
                FireProjectile(direction);
                ShotFired?.Invoke();
            }
            shots++;
            yield return new WaitForSeconds(continuous ? _continuousShotInterval : _shotInterval);
            if (teleportBetweenShots && !continuous && !_mounted && shots < _soloShots
                && _health.IsAlive && _player != null && _player.IsAlive && _motor.IsReady)
                TryTeleportAroundPlayer(_teleportRadius, _teleportArc);
        }
        _shooting = null;
    }

    /// <summary>direction으로 발사구에서 수리검 하나를 만들어 탄환 목록에 넣고 반사·반환 사건을 구독한 뒤 비행을 시작한다.</summary>
    private void FireProjectile(Vector3 direction)
    {
        TwinBossProjectile projectile = Instantiate(_projectilePrefab, _muzzle.position,
            Quaternion.LookRotation(direction));
        projectile.ReflectedHit += HandleReflectedHit;
        projectile.Released += HandleProjectileReleased;
        _projectiles.Add(projectile);
        projectile.Initialize(_health, _hitCollider, _player, _damage, direction);
    }

    /// <summary>player에게 탑승 부채꼴 난사를 시작한다. 진행 중인 사격은 먼저 멈추고 날아가는 탄환은 그대로 둔다.</summary>
    public void StartBarrage(KatanaDamageReceiver player)
    {
        StopShooting();
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        _shooting = StartCoroutine(Barrage());
    }

    /// <summary>
    /// 준비 사건·준비음 뒤 플레이어 시점보다 0.1m 아래를 중심으로 _barrageShots개 수리검을 _barrageSpreadAngle 부채꼴로 던지고 간격을 두기를 반복한다.
    /// 자신이나 플레이어가 쓰러지면 끝내고 실행 참조를 비운다.
    /// </summary>
    private IEnumerator Barrage()
    {
        yield return null;
        while (_health.IsAlive && _player != null && _player.IsAlive)
        {
            ShotPreparing?.Invoke();
            if (_preparationSound != SoundId.None)
                Managers.Sound.Play(_preparationSound, transform.position, 1f, gameObject);
            yield return new WaitForSeconds(_barragePreparation);
            if (!_health.IsAlive || _player == null || !_player.IsAlive) break;
            Vector3 center = (_playerMotor.ViewOrigin - Vector3.up * 0.1f - _muzzle.position).normalized;
            for (int i = 0; i < _barrageShots; i++)
            {
                float angle = _barrageShots > 1
                    ? Mathf.Lerp(-_barrageSpreadAngle * 0.5f, _barrageSpreadAngle * 0.5f, i / (_barrageShots - 1f)) : 0f;
                FireProjectile(Quaternion.AngleAxis(angle, Vector3.up) * center);
            }
            ShotFired?.Invoke();
            yield return new WaitForSeconds(_barrageInterval);
        }
        _shooting = null;
    }

    /// <summary>
    /// strikeAt(게임 시간)에 맞춰 _slashTelegraph 전에 플레이어 정면 쪽 _slashDistance 지점으로 순간이동해 붉게 물들고,
    /// strikeAt에 플레이어를 지나 _slashOvershoot까지 _slashDuration 동안 베며 돌진한다. 첫 접촉에서 한 번만 막기·쳐내기를 확인해 피해를 준다.
    /// 순간이동할 곳이 없으면 지금 자리에서 벤다.
    /// </summary>
    public IEnumerator Slash(KatanaDamageReceiver player, float strikeAt)
    {
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        while (Time.time < strikeAt - _slashTelegraph && CanSlash()) yield return null;
        if (!CanSlash()) yield break;
        _motor.Stop();
        TryTeleportAroundPlayer(_slashDistance, _slashArc);
        _slashing = true;
        _warning.BeginWarning(Mathf.Max(0f, strikeAt - Time.time));
        if (_slashTelegraphSound != SoundId.None)
            Managers.Sound.Play(_slashTelegraphSound, transform.position, 1f, gameObject);
        while (Time.time < strikeAt && CanSlash())
        {
            FacePlayer();
            yield return null;
        }
        if (CanSlash()) yield return SlashDash();
        _slashing = false;
        _warning.EndWarning();
    }

    /// <summary>
    /// 플레이어 쪽으로 _slashDuration 동안 플레이어 너머 _slashOvershoot까지 NavMesh 안에서 돌진하며 베기 소리를 낸다.
    /// 이동 구간마다 몸 캡슐로 플레이어 접촉을 검사해 첫 접촉에만 피해를 판정하고, 지나간 경로는 비 시스템에 전달한다.
    /// </summary>
    private IEnumerator SlashDash()
    {
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
        Vector3 direction = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward;
        float remaining = toPlayer.magnitude + _slashOvershoot;
        float speed = remaining / _slashDuration;
        bool resolved = false;
        transform.rotation = Quaternion.LookRotation(direction);
        if (_slashSound != SoundId.None)
            Managers.Sound.Play(_slashSound, transform.position, 1f, gameObject, true);
        while (remaining > 0.01f && CanSlash())
        {
            Vector3 previous = transform.position;
            Vector3 destination = previous + direction * Mathf.Min(speed * Time.deltaTime, remaining);
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            float travel = Vector3.Distance(previous, destination);
            if (!resolved) resolved = TryHitSlash(previous, direction, travel);
            _agent.Move(destination - previous);
            RainStageWeather.RecordDashSweep(previous + Vector3.up, transform.position + Vector3.up);
            remaining -= travel;
            if (travel < 0.001f && Time.deltaTime > 0f) break;
            yield return null;
        }
        _motor.Stop();
    }

    /// <summary>
    /// start에서 direction으로 travel만큼 몸 캡슐을 밀어 플레이어에 닿으면 전방 막기·쳐내기를 확인하고 실패 시 _slashDamage 피해를 준다.
    /// 판정했으면 true를 반환한다.
    /// </summary>
    private bool TryHitSlash(Vector3 start, Vector3 direction, float travel)
    {
        float radius = _slashContactRadius;
        Vector3 bottom = start + Vector3.up * radius;
        Vector3 top = start + Vector3.up * Mathf.Max(radius, _agent.height - radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _slashHits,
            Mathf.Max(0.01f, travel), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_slashHits[i].collider.GetComponentInParent<PlayerHealth>() != _player) continue;
            Vector3 point = _slashHits[i].distance > 0f ? _slashHits[i].point : _player.transform.position + Vector3.up;
            DamageContext context = new DamageContext(_slashDamage, gameObject, point, direction, "TwinHumanSlash");
            if (!_player.TryDefendProjectile(context, out _)) _player.TakeDamage(context);
            return true;
        }
        return false;
    }

    /// <summary>근접 베기 준비·돌진 상태와 붉은 전조를 정리한다. 패턴이 외부에서 멈출 때 호출한다.</summary>
    public void CancelMelee()
    {
        _slashing = false;
        _warning.EndWarning();
    }

    /// <summary>자신과 플레이어가 살아 있고 탑승하지 않았으며 지면 이동이 가능한지 반환한다.</summary>
    private bool CanSlash()
    {
        return _health.IsAlive && _player != null && _player.IsAlive && !_mounted && _motor.IsReady;
    }

    /// <summary>플레이어 쪽으로 수평 방향만 돌려 바라본다.</summary>
    private void FacePlayer()
    {
        Vector3 facing = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing);
    }

    /// <summary>
    /// 플레이어 수평 시점 기준 arc 범위·distance 거리에서 빈 NavMesh 지면과 완전한 경로를 확인해 순간이동하며 성공 여부를 반환한다.
    /// arc가 180도 이하이면 플레이어 앞쪽 후보만 쓴다.
    /// </summary>
    private bool TryTeleportAroundPlayer(float distance, float arc)
    {
        Vector3 forward = _playerMotor.ViewForward;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = UnityEngine.Random.Range(-arc * 0.5f, arc * 0.5f);
            Vector3 desired = _player.transform.position
                + Quaternion.Euler(0f, angle, 0f) * forward * distance;
            desired.y = _agent.nextPosition.y;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.5f, filter)) continue;
            Vector3 position = hit.position;
            if ((arc <= 180f && Vector3.Dot(position - _player.transform.position, forward) < 0f)
                || (position - transform.position).sqrMagnitude < 0.25f) continue;
            float radius = _agent.radius;
            int count = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * (radius + 0.05f),
                position + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius),
                radius, _teleportContacts, _teleportObstacleMask, QueryTriggerInteraction.Ignore);
            bool blocked = count == _teleportContacts.Length;
            for (int i = 0; i < count; i++)
            {
                if (!_teleportContacts[i].transform.IsChildOf(transform)) blocked = true;
            }
            if (blocked || !_agent.CalculatePath(position, _teleportPath)
                || _teleportPath.status != NavMeshPathStatus.PathComplete) continue;
            _motor.Stop();
            if (!_agent.Warp(position)) continue;
            Vector3 facing = Vector3.ProjectOnPlane(_player.transform.position - position, Vector3.up);
            if (facing.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(facing);
            return true;
        }
        return false;
    }

    /// <summary>seat를 탑승 위치로 저장하고 인간의 NavMesh 위치·회전 갱신을 잠시 중단한다.</summary>
    public void Mount(Transform seat)
    {
        _motor.Stop();
        _navigationPosition = _agent.nextPosition;
        _previousUpdatePosition = _agent.updatePosition;
        _previousUpdateRotation = _agent.updateRotation;
        _agent.updatePosition = false;
        _agent.updateRotation = false;
        _seat = seat;
        _mounted = true;
        transform.SetPositionAndRotation(seat.position, seat.rotation);
    }

    /// <summary>탑승을 끝내고 현재 위치 주변 또는 탑승 전 지면으로 복귀해 NavMesh 갱신을 복원한다.</summary>
    public void Dismount()
    {
        if (!_mounted) return;
        _mounted = false;
        Vector3 right = _seat != null ? _seat.right : transform.right;
        _seat = null;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = _agent.agentTypeID,
            areaMask = _agent.areaMask
        };
        Vector3 destination = _navigationPosition;
        for (int side = 1; side >= -1; side -= 2)
        {
            Vector3 desired = transform.position + right * (_dismountDistance * side);
            desired.y = _navigationPosition.y;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 1f, filter)) continue;
            float radius = _agent.radius;
            int count = Physics.OverlapCapsuleNonAlloc(hit.position + Vector3.up * (radius + 0.05f),
                hit.position + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius),
                radius, _dismountContacts, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool blocked = count == _dismountContacts.Length;
            for (int i = 0; i < count; i++)
            {
                if (!_dismountContacts[i].transform.IsChildOf(transform)) blocked = true;
            }
            if (blocked) continue;
            destination = hit.position;
            break;
        }
        _agent.updatePosition = _previousUpdatePosition;
        _agent.updateRotation = _previousUpdateRotation;
        if (_agent.isActiveAndEnabled) _agent.Warp(destination);
    }

    /// <summary>
    /// 사격 코루틴만 중지해 더 던지지 않게 한다. 이미 날아가는 수리검은 순간이동·패턴 전환과 상관없이 수명이 다하거나 맞을 때까지 그대로 난다.
    /// </summary>
    public void StopShooting()
    {
        if (_shooting != null) StopCoroutine(_shooting);
        _shooting = null;
    }

    /// <summary>날아가는 모든 수리검을 즉시 거두고 이벤트 구독과 관리 목록을 비운다. 인간이 꺼질 때 쓴다.</summary>
    private void ReleaseProjectiles()
    {
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            TwinBossProjectile projectile = _projectiles[i];
            if (projectile == null) continue;
            projectile.ReflectedHit -= HandleReflectedHit;
            projectile.Released -= HandleProjectileReleased;
            projectile.Release();
        }
        _projectiles.Clear();
    }

    /// <summary>projectile의 반사 인간 적중을 협력 패턴 제어자에게 전달한다.</summary>
    private void HandleReflectedHit(TwinBossProjectile projectile)
    {
        ReflectedHit?.Invoke();
    }

    /// <summary>반환된 projectile의 구독을 해제하고 관리 목록에서 제거한다.</summary>
    private void HandleProjectileReleased(TwinBossProjectile projectile)
    {
        projectile.ReflectedHit -= HandleReflectedHit;
        projectile.Released -= HandleProjectileReleased;
        _projectiles.Remove(projectile);
    }

    void OnDisable()
    {
        StopShooting();
        ReleaseProjectiles();
        Dismount();
        CancelMelee();
    }
}
