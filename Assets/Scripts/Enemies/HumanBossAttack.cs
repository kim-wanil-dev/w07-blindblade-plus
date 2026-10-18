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

    [Header("물속 순간이동")]
    [SerializeField, Tooltip("물속으로 가라앉혔다 떠올리는 연기 몸체 도형. 연기가 이 도형 표면에서 나온다.")]
    private Transform _sinkBody;
    [SerializeField, Min(0f), Tooltip("완전히 잠겼을 때 몸체가 내려가는 깊이(미터)")]
    private float _sinkDistance = 2.5f;
    [SerializeField, Min(0.01f), Tooltip("물속으로 가라앉는 시간(초)")]
    private float _sinkDuration = 0.3f;
    [SerializeField, Min(0.01f), Tooltip("새 자리에서 떠오르는 시간(초)")]
    private float _riseDuration = 0.3f;
    [SerializeField, Tooltip("가라앉고 떠오르는 자리에 퍼지는 흰 물 파동. 플레이어 발걸음 파동과 같은 크기다.")]
    private RevealRippleSettings _waterRipple = new RevealRippleSettings(14.4f, 1.2f, 3, 0.15f, 1.2f, Color.white);
    [SerializeField, Tooltip("가라앉고 떠오를 때 입체로 울리는 물방울 소리")]
    private SoundId _waterDropSound = SoundId.TwinBossWaterDrop;
    [SerializeField, Range(0.25f, 4f), Tooltip("물방울 소리 피치 배율. 늑대(0.5)보다 덜 낮춘다.")]
    private float _waterDropPitch = 0.7f;
    [SerializeField, Min(0f)] private float _waterDropVolume = 1f;
    private Vector3 _sinkBodyPosition;
    private float _sinkDepth;
    private Coroutine _teleport;

    [Header("탑승 상태")]
    [SerializeField, Min(0.5f)] private float _dismountDistance = 2f;
    [SerializeField, Range(0f, 90f), Tooltip("탑승 중 연기 몸체가 늑대 목 쪽으로 앞으로 눕는 각도(도). 몸체 아래 끝(안장)을 축으로 기운다.")]
    private float _mountedLean = 60f;
    [SerializeField, Tooltip("탑승 중 누운 몸체를 옮기는 인간 로컬 거리(미터). 늑대 목을 감싸도록 뒤아래로 당긴다.")]
    private Vector3 _mountedBodyOffset = new Vector3(0f, -0.8f, -0.5f);
    [SerializeField, Min(0.01f), Tooltip("타고 내릴 때 몸체가 눕고 일어서는 시간(초)")]
    private float _mountedLeanDuration = 0.25f;
    private readonly Collider[] _dismountContacts = new Collider[16];
    private Transform _seat;
    private bool _mounted;
    private Vector3 _navigationPosition;
    private bool _previousUpdatePosition;
    private bool _previousUpdateRotation;
    private float _mountedPose;

    [Header("탑승 난사")]
    [SerializeField, Min(1), Tooltip("한 번에 부채꼴로 던지는 수리검 수")]
    private int _barrageShots = 3;
    [SerializeField, Range(0f, 60f), Tooltip("부채꼴 양 끝 수리검 사이의 수평 각도(도)")]
    private float _barrageSpreadAngle = 16f;
    [SerializeField, Min(0.05f), Tooltip("묶음마다 던지기 전 준비 시간(초)")]
    private float _barragePreparation = 0.2f;
    [SerializeField, Min(0.05f), Tooltip("묶음을 던진 뒤 다음 준비까지의 간격(초)")]
    private float _barrageInterval = 0.4f;

    [Header("돌진 베기")]
    [SerializeField, Min(1f), Tooltip("돌진을 시작하고 베고 나서 물러나는 플레이어와의 거리(미터). 스테이지1 보스 돌진 등장 거리와 같다.")]
    private float _dashStartDistance = 6f;
    [SerializeField, Range(1f, 360f), Tooltip("플레이어 수평 시점 기준 돌진 시작·후퇴 위치 각도 범위(도). 막기 가능한 정면 쪽으로 좁힌다.")]
    private float _dashArc = 140f;
    [SerializeField, Min(0.05f), Tooltip("돌진 전 플레이어를 노려보며 붉게 물드는 시간(초)")]
    private float _dashTelegraph = 0.6f;
    [SerializeField, Min(0.1f), Tooltip("유선형 곡선 돌진 속도(미터/초)")]
    private float _dashSpeed = 18f;
    [SerializeField, Min(0f), Tooltip("마지막 꼭짓점 뒤 플레이어에 닿지 않으면 플레이어 위치를 지나 더 나아가는 거리(미터)")]
    private float _dashOvershoot = 2f;
    [SerializeField, Min(0), Tooltip("돌진·후퇴 곡선 한 번에 좌우로 휘는 꼭짓점 수")]
    private int _zigzagTurns = 2;
    [SerializeField, Min(0f), Tooltip("곡선 꼭짓점이 곧은 경로에서 옆으로 벗어나는 거리(미터)")]
    private float _zigzagWidth = 2.5f;
    [SerializeField, Tooltip("곡선 꼭짓점을 지날 때마다 그 자리에서 퍼지는 붉은 파동")]
    private RevealRippleSettings _turnRipple = new RevealRippleSettings(7f, 0.5f, 1, 0.1f, 0.8f,
        new Color(1f, 0.1f, 0.06f, 0.95f), true);
    [SerializeField, Min(0.1f), Tooltip("돌진 시작 위치로 다가가거나 베고 유선형 곡선으로 물러나는 빠른 이동 속도(미터/초). 스테이지1 보스 재배치와 같다.")]
    private float _repositionSpeed = 25f;
    [SerializeField, Min(0.05f), Tooltip("빠른 이동이 길이 막혀도 끝나는 최대 시간(초)")]
    private float _repositionTimeout = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("돌진이 플레이어에 닿았다고 보는 몸 반경(미터)")]
    private float _slashContactRadius = 0.8f;
    [SerializeField, Min(0f)] private float _slashDamage = 12f;
    [SerializeField, Min(0f), Tooltip("돌진 베기에 맞은 플레이어를 밀어내는 거리(미터)")]
    private float _dashPushDistance = 3f;
    [SerializeField, Min(0.01f)] private float _dashPushDuration = 0.16f;
    [SerializeField] private SoundId _slashTelegraphSound = SoundId.EnemyMeleeWarning;
    [SerializeField] private SoundId _slashSound = SoundId.EnemySwordSwing;
    private readonly RaycastHit[] _slashHits = new RaycastHit[16];
    [SerializeField, Min(0f), Tooltip("돌진 베기가 퍼펙트 패링에 막혔을 때 떨며 뒤로 밀려나는 거리(미터)")]
    private float _parryRecoilDistance = 10f;
    [SerializeField, Min(0.01f), Tooltip("퍼펙트 패링에 막혀 밀려나는 시간(초)")]
    private float _parryRecoilDuration = 0.45f;
    [SerializeField, Min(0f), Tooltip("밀려나는 동안 좌우로 떠는 폭(미터). 밀려날수록 잦아든다.")]
    private float _recoilShake = 0.25f;
    [SerializeField, Min(0f), Tooltip("밀려나는 동안 좌우로 떠는 빠르기(초당 횟수)")]
    private float _recoilShakeFrequency = 18f;
    [SerializeField, Min(0f), Tooltip("교대 근접에서 돌진 베기를 마치고 짐승 덮치기를 기다리는 동안 크게 뒤로 물러나는 거리(미터)")]
    private float _backstepDistance = 30f;
    [SerializeField, Min(0.01f), Tooltip("크게 뒤로 물러나는 시간(초)")]
    private float _backstepDuration = 0.6f;
    private TwinBossWarningVisual _warning;
    private PerceptionRevealMap _revealMap;
    private bool _slashing;
    private bool _dashParried;
    private float _strikeDamage;

    [Header("반격 관통 베기")]
    [SerializeField, Min(0.1f), Tooltip("받아치기 관통 베기 속도(미터/초). 스테이지1 보스 관통 베기(90)의 4배다.")]
    private float _counterSlashSpeed = 360f;
    [SerializeField, Min(0f), Tooltip("플레이어를 뚫고 지나간 뒤 더 나아가는 거리(미터). 스테이지1 보스 관통 베기와 같다.")]
    private float _counterSlashExitDistance = 12f;
    [SerializeField, Range(0.01f, 0.5f), Tooltip("인간과 플레이어를 잇는 직선 길이 대비 원호가 옆으로 부푼 정도. 클수록 크게 휘어 베고 지나간다.")]
    private float _counterArcBulge = 0.25f;

    [Header("기운 구슬")]
    [SerializeField, Min(1), Tooltip("허리춤 구슬 개수이자 거합에 필요한 기운 수. 인간·짐승 공격이 플레이어에게 피해를 줄 때마다 붉은 실이 구슬에 닿으며 하나씩 찬다.")]
    private int _maxCharges = 3;
    private int _charges;

    public EnemyHealth Health => _health;
    public bool IsAttacking => _shooting != null;
    public bool HasProjectiles => _projectiles.Count > 0;
    public bool IsReady => _motor.IsReady;
    public bool IsMounted => _mounted;
    public bool IsSlashing => _slashing;
    public int Charges => _charges;
    public int MaxCharges => _maxCharges;
    public bool IsChargeFull => _charges >= _maxCharges;
    public float SinkOffset => _sinkDepth * _sinkDistance;
    public Quaternion BodyLean => Quaternion.Euler(_mountedLean * _mountedPose, 0f, 0f);
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
        _sinkBodyPosition = _sinkBody.localPosition;
    }

    void LateUpdate()
    {
        if (_seat != null && _health.IsAlive)
            transform.SetPositionAndRotation(_seat.position, _seat.rotation);
        _mountedPose = Mathf.MoveTowards(_mountedPose, _mounted ? 1f : 0f, Time.deltaTime / _mountedLeanDuration);
        _sinkBody.localRotation = BodyLean;
        _sinkBody.localPosition = GetBodyPoint(_sinkBodyPosition);
    }

    /// <summary>
    /// 서 있는 몸체 기준 인간 로컬 지점 standingPoint를 지금 탑승 자세(_mountedPose만큼 _mountedLean으로 앞으로 눕고 _mountedBodyOffset만큼 옮김)와
    /// 물속에 잠긴 깊이(SinkOffset)를 반영한 인간 로컬 지점으로 바꿔 반환한다.
    /// </summary>
    public Vector3 GetBodyPoint(Vector3 standingPoint)
    {
        return BodyLean * standingPoint
            + _mountedBodyOffset * _mountedPose + Vector3.down * SinkOffset;
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

    /// <summary>
    /// continuous로 플레이어 시점보다 0.1m 아래를 향한 전조·직선 발사·휴식을 반복하고 teleportBetweenShots이면 사격 사이에
    /// 물속으로 가라앉았다 플레이어 주변에서 떠오르는 순간이동을 하며 실행 참조를 정리한다.
    /// </summary>
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
                && _health.IsAlive && _player != null && _player.IsAlive && _motor.IsReady
                && TryFindSpotAroundPlayer(_teleportRadius, _teleportArc, out Vector3 spot))
                yield return TeleportThroughWater(spot);
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
    /// 스테이지1 보스 돌진처럼 플레이어 정면 쪽 _dashStartDistance 지점에서 strikeAt(게임 시간)에 돌진해 베고 다시 물러난다.
    /// 그 거리에서 벗어나 있으면 전조 전에 _repositionSpeed로 시작 지점까지 달려가고, _dashTelegraph 동안 플레이어를 노려보며 붉게 물든 뒤 유선형 곡선으로 돌진한다.
    /// 플레이어에 닿으면 그 자리에서 멈춰 한 번만 막기·쳐내기를 확인해 피해를 주고, 끝나면 플레이어를 바라본 채 정면 쪽 같은 거리로 유선형 곡선을 따라 물러난다.
    /// </summary>
    public IEnumerator DashStrike(KatanaDamageReceiver player, float strikeAt)
    {
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        while (Time.time < strikeAt - _dashTelegraph && CanSlash()) yield return null;
        if (!CanSlash()) yield break;
        float distance = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up).magnitude;
        if (Mathf.Abs(distance - _dashStartDistance) > 1.5f
            && TryFindSpotAroundPlayer(_dashStartDistance, _dashArc, out Vector3 start))
            yield return MoveQuickly(start);
        if (!CanSlash()) yield break;
        // 달려가느라 늦었어도 전조를 절반은 보여준 뒤 돌진한다.
        float dashAt = Mathf.Max(strikeAt, Time.time + _dashTelegraph * 0.5f);
        _slashing = true;
        _warning.BeginWarning(dashAt - Time.time);
        if (_slashTelegraphSound != SoundId.None)
            Managers.Sound.Play(_slashTelegraphSound, transform.position, 1f, gameObject);
        while (Time.time < dashAt && CanSlash())
        {
            FacePlayer();
            yield return null;
        }
        if (CanSlash()) yield return Dash(_slashDamage);
        _slashing = false;
        _warning.EndWarning();
        // 퍼펙트 패링에 막혔으면 곡선 후퇴 대신 떨며 뒤로 밀려난다.
        if (_dashParried && CanSlash())
        {
            yield return RecoilFromPlayer();
            yield break;
        }
        if (CanSlash() && TryFindSpotAroundPlayer(_dashStartDistance, _dashArc, out Vector3 retreat))
            yield return Retreat(retreat);
    }

    /// <summary>
    /// 피격 무적 중 플레이어에게 다시 베이면 쓰는 받아치기 돌진. 스테이지1 보스 관통 베기처럼 플레이어를 뚫고 베어 지나가며,
    /// 인간에서 target(밀려난 플레이어가 내려앉을 곳)까지 bulgeSide 쪽으로 휜 원호를 그리고 그 너머로 빠져나간다(ArcSlash). 맞으면 damage 피해를 주고,
    /// 퍼펙트 패링에 막히면 그 자리에서 멈춰 돌진 베기와 같이 떨며 밀려난다. 전조·감속·밀어내기와 출발 시각은 제어자가 맡는다.
    /// </summary>
    public IEnumerator CounterDash(KatanaDamageReceiver player, float damage, Vector3 target, Vector3 bulgeSide)
    {
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        if (!CanSlash()) yield break;
        _slashing = true;
        yield return ArcSlash(damage, target, bulgeSide);
        _slashing = false;
        if (_dashParried && CanSlash()) yield return RecoilFromPlayer();
    }

    /// <summary>
    /// 지금 위치에서 target까지 관통 베기 원호를 _counterSlashSpeed로 따라가는 데 걸리는 게임 시간(초)을 반환한다.
    /// 제어자가 밀려나는 플레이어가 내려앉는 순간에 맞춰 출발 시각을 정할 때 쓴다.
    /// </summary>
    public float GetCounterArcTime(Vector3 target)
    {
        float span = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).magnitude;
        if (span < 0.01f) return 0f;
        float bulge = span * _counterArcBulge;
        float radius = (span * span * 0.25f + bulge * bulge) / (2f * bulge);
        return radius * 2f * Mathf.Asin(Mathf.Clamp01(span * 0.5f / radius)) / _counterSlashSpeed;
    }

    /// <summary>
    /// 지금 위치에서 target까지 옆으로 직선 길이 × _counterArcBulge만큼 bulgeSide 쪽으로 부푼 원호를 따라 _counterSlashSpeed로 베고,
    /// target의 원호 접선 방향으로 _counterSlashExitDistance만큼 곧게 빠져나간다. bulgeSide가 직선과 나란하면 휘는 쪽을 무작위로 고른다.
    /// 플레이어에 처음 닿을 때 한 번만 전방 막기·쳐내기를 확인해 맞으면 damage 피해만 주고(밀어내지 않음) 막혀도 그대로 뚫고 지나가며,
    /// 퍼펙트 패링이면 닿은 자리에서 멈추고 _dashParried를 켠다. 걸을 수 있는 지면 끝에 막히면 멈춘다.
    /// </summary>
    private IEnumerator ArcSlash(float damage, Vector3 target, Vector3 bulgeSide)
    {
        _dashParried = false;
        Vector3 start = transform.position;
        Vector3 chord = Vector3.ProjectOnPlane(target - start, Vector3.up);
        float span = chord.magnitude;
        if (span < 0.01f) yield break;
        Vector3 forward = chord / span;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float lean = Vector3.Dot(right, bulgeSide);
        float side = Mathf.Abs(lean) > 0.01f ? Mathf.Sign(lean) : (UnityEngine.Random.value < 0.5f ? -1f : 1f);
        Vector3 outward = right * side;
        float bulge = span * _counterArcBulge;
        float radius = (span * span * 0.25f + bulge * bulge) / (2f * bulge);
        // 원의 중심은 직선 가운데에서 부푼 쪽 반대로 놓여 원호가 시작점과 플레이어 위치를 함께 지난다.
        Vector3 center = start + forward * (span * 0.5f) - outward * (radius - bulge);
        Vector3 fromCenter = start - center;
        float sweep = 2f * Mathf.Asin(Mathf.Clamp01(span * 0.5f / radius));
        // 시작점에서 플레이어 쪽이면서 부푼 쪽으로 나가는 접선을 따라 돌도록 회전 방향을 정한다.
        Vector3 startTangent = (forward * (radius - bulge) + outward * (span * 0.5f)).normalized;
        float turn = Vector3.Dot(Vector3.Cross(Vector3.up, fromCenter), startTangent) >= 0f ? 1f : -1f;
        float arcLength = radius * sweep;
        Quaternion arcTurn = Quaternion.AngleAxis(turn * sweep * Mathf.Rad2Deg, Vector3.up);
        Vector3 arcEnd = center + arcTurn * fromCenter;
        Vector3 exitDirection = arcTurn * startTangent;
        float total = arcLength + _counterSlashExitDistance;
        float traveled = 0f;
        bool contacted = false;
        if (_slashSound != SoundId.None)
            Managers.Sound.Play(_slashSound, transform.position, 1f, gameObject, true);
        while (traveled < total && CanSlash())
        {
            // 일시정지 프레임은 이동 없이 넘겨 관통 베기가 중간에 끊기지 않게 한다.
            if (Time.deltaTime <= 0f)
            {
                yield return null;
                continue;
            }

            traveled = Mathf.Min(total, traveled + _counterSlashSpeed * Time.deltaTime);
            Vector3 desired = traveled <= arcLength
                ? center + Quaternion.AngleAxis(turn * (traveled / radius) * Mathf.Rad2Deg, Vector3.up) * fromCenter
                : arcEnd + exitDirection * (traveled - arcLength);
            Vector3 previous = transform.position;
            Vector3 offset = Vector3.ProjectOnPlane(desired - previous, Vector3.up);
            if (offset.sqrMagnitude < 0.000001f)
            {
                yield return null;
                continue;
            }

            Vector3 direction = offset.normalized;
            Vector3 destination = previous + offset;
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            float travel = Vector3.Distance(previous, destination);
            transform.rotation = Quaternion.LookRotation(direction);
            if (!contacted && TryCastPlayer(previous, direction, travel, out float contact, out Vector3 point))
            {
                contacted = true;
                DamageContext context = new DamageContext(damage, gameObject, point, direction, "TwinHumanCounterSlash");
                if (_player.TryDefendProjectile(context, out bool parried))
                {
                    if (parried)
                    {
                        _agent.Move(direction * Mathf.Max(0f, contact - 0.05f));
                        _dashParried = true;
                        yield break;
                    }
                }
                else
                {
                    _player.TakeDamage(context);
                }
            }
            _agent.Move(destination - previous);
            RainStageWeather.RecordDashSweep(previous + Vector3.up, transform.position + Vector3.up);
            if (travel < 0.001f) break;
            yield return null;
        }
        _motor.Stop();
    }

    /// <summary>플레이어 반대쪽으로 _parryRecoilDistance만큼 _parryRecoilDuration 동안 떨며 밀려난다.</summary>
    private IEnumerator RecoilFromPlayer()
    {
        Vector3 back = Vector3.ProjectOnPlane(transform.position - _player.transform.position, Vector3.up);
        yield return Recoil(back.sqrMagnitude > 0.0001f ? back.normalized : -transform.forward,
            _parryRecoilDistance, _parryRecoilDuration, false);
    }

    /// <summary>
    /// direction으로 distance만큼 duration 동안 감속하며 밀려나고, 밀려나는 방향의 옆으로 _recoilShake 폭·_recoilShakeFrequency 빠르기로 잦아들며 떤다.
    /// realTime이면 일시정지를 제외한 실제 시간으로, 아니면 게임 시간으로 진행하며 플레이어를 바라본 채 걸을 수 있는 지면 끝에서 멈춘다.
    /// 퍼펙트 패링에 막힌 돌진 베기와 거합에서 쓴다.
    /// </summary>
    public IEnumerator Recoil(Vector3 direction, float distance, float duration, bool realTime)
    {
        yield return Slide(direction, distance, duration, _recoilShake, realTime);
    }

    /// <summary>
    /// player 반대쪽으로 _backstepDistance만큼 _backstepDuration 동안 감속하며 크게 뒤로 물러난다. 떨지 않고 플레이어를 바라본 채 물러나며 걸을 수 있는 지면 끝에서 멈춘다.
    /// 교대 근접에서 돌진 베기를 마치고 짐승의 덮치기를 기다리는 동안 쓴다.
    /// </summary>
    public IEnumerator Backstep(KatanaDamageReceiver player)
    {
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        if (!CanSlash()) yield break;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - player.transform.position, Vector3.up);
        yield return Slide(away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward,
            _backstepDistance, _backstepDuration, 0f, false);
    }

    /// <summary>
    /// direction으로 distance만큼 duration 동안 감속하며 미끄러지고, 옆으로 shakeWidth 폭·_recoilShakeFrequency 빠르기로 잦아들며 떤다.
    /// realTime이면 일시정지를 제외한 실제 시간으로, 아니면 게임 시간으로 진행하며 플레이어를 바라본 채 걸을 수 있는 지면 끝에서 멈춘다.
    /// </summary>
    private IEnumerator Slide(Vector3 direction, float distance, float duration, float shakeWidth, bool realTime)
    {
        _motor.Stop();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < duration && _health.IsAlive && _motor.IsReady)
        {
            elapsed += realTime ? EmpowerOverlay.RealDeltaTime() : Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float pushed = distance * (1f - (1f - progress) * (1f - progress));
            float shake = Mathf.Sin(elapsed * _recoilShakeFrequency * Mathf.PI * 2f) * shakeWidth * (1f - progress);
            Vector3 destination = start + direction * pushed + side * shake;
            if (_agent.Raycast(destination, out NavMeshHit boundary)) destination = boundary.position;
            _agent.Move(destination - transform.position);
            if (_player != null) FacePlayer();
            yield return null;
        }
        _motor.Stop();
    }

    /// <summary>
    /// 기운을 하나 채운다. 쌍둥이 보스 공격의 피해로 뻗은 붉은 실(TwinHumanChargeStreak)이 구슬에 닿을 때 호출한다.
    /// _charges를 늘리며 _maxCharges를 넘지 않는다.
    /// </summary>
    public void AddCharge()
    {
        _charges = Mathf.Min(_maxCharges, _charges + 1);
    }

    /// <summary>모인 기운을 모두 써서 비운다. 거합을 낼 때 호출한다.</summary>
    public void ConsumeCharges()
    {
        _charges = 0;
    }

    /// <summary>
    /// 지금 위치에서 플레이어 쪽으로 좌우 _zigzagWidth만큼 _zigzagTurns번 휘어지는 유선형 곡선을 따라 _dashSpeed로 돌진하고 베기 소리를 낸다.
    /// 휘어진 꼭짓점을 지날 때마다 붉은 파동을 내고, 마지막 구간에 들어설 때 그 순간 플레이어 위치를 _dashOvershoot만큼 지나는 곳을 끝점으로 고정한다.
    /// 곡선 어디에서든 플레이어에 닿으면 그 앞에서 멈추며 damage 피해를 판정한다.
    /// </summary>
    private IEnumerator Dash(float damage)
    {
        _strikeDamage = damage;
        Vector3 start = transform.position;
        Vector3 toPlayer = Vector3.ProjectOnPlane(_player.transform.position - start, Vector3.up);
        Vector3 forward = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward;
        _dashParried = false;
        if (_slashSound != SoundId.None)
            Managers.Sound.Play(_slashSound, transform.position, 1f, gameObject, true);
        Vector3[] points = BuildCurvePoints(start, forward * toPlayer.magnitude);
        points[points.Length - 1] = GetOvershootPoint(points[points.Length - 2]);
        yield return FollowCurve(points, _dashSpeed, true);
        _motor.Stop();
    }

    /// <summary>
    /// destination까지 좌우 _zigzagWidth만큼 _zigzagTurns번 휘어지는 유선형 곡선을 따라 _repositionSpeed로 플레이어를 바라본 채 물러난다.
    /// 휘어진 꼭짓점을 지날 때마다 붉은 파동을 낸다.
    /// </summary>
    private IEnumerator Retreat(Vector3 destination)
    {
        Vector3 start = transform.position;
        Vector3 path = Vector3.ProjectOnPlane(destination - start, Vector3.up);
        if (path.sqrMagnitude < 0.0001f) yield break;
        yield return FollowCurve(BuildCurvePoints(start, path), _repositionSpeed, false);
        _motor.Stop();
    }

    /// <summary>
    /// start에서 path만큼 가는 곡선의 통과점 배열(시작점, 꼭짓점 _zigzagTurns개, 끝점)을 반환한다.
    /// 꼭짓점은 path를 등분한 지점에서 무작위 쪽부터 좌우 번갈아 _zigzagWidth만큼 벗어난다.
    /// </summary>
    private Vector3[] BuildCurvePoints(Vector3 start, Vector3 path)
    {
        int legs = _zigzagTurns + 1;
        Vector3[] points = new Vector3[legs + 1];
        Vector3 right = Vector3.Cross(Vector3.up, path.normalized);
        float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        points[0] = start;
        for (int leg = 1; leg < legs; leg++)
        {
            points[leg] = start + path * ((float)leg / legs) + right * (side * _zigzagWidth);
            side = -side;
        }
        points[legs] = start + path;
        return points;
    }

    /// <summary>from에서 지금 플레이어 위치를 겨눠 _dashOvershoot만큼 더 지나친 지점을 반환한다.</summary>
    private Vector3 GetOvershootPoint(Vector3 from)
    {
        Vector3 target = _player.transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(target - from, Vector3.up);
        return direction.sqrMagnitude > 0.0001f ? target + direction.normalized * _dashOvershoot : target;
    }

    /// <summary>
    /// points를 모두 지나는 Catmull-Rom 곡선을 따라 speed로 NavMesh 안에서 매끈하게 움직이며, 걸을 수 있는 지면 끝에 막히면 멈춘다.
    /// 중간 통과점(꼭짓점)을 지날 때마다 붉은 파동을 낸다. strike면 마지막 구간에 들어서기 전까지 끝점을 지금 플레이어 너머로 다시 맞추고,
    /// 이동 방향을 보며 몸 캡슐로 플레이어 접촉을 검사해 닿으면 그 앞에서 멈추고 곡선을 끝내며 지나간 경로를 비 시스템에 전달한다.
    /// strike가 아니면 플레이어를 바라본다.
    /// </summary>
    private IEnumerator FollowCurve(Vector3[] points, float speed, bool strike)
    {
        int last = points.Length - 1;
        int nextApex = 1;
        float t = 0f;
        while (t < last && CanSlash())
        {
            float step = speed * Time.deltaTime;
            if (step <= 0f)
            {
                yield return null;
                continue;
            }

            // 마지막 구간에 들어서면 그 순간 겨눈 끝점으로 고정해 플레이어가 피할 틈을 남긴다.
            if (strike && t < last - 1) points[last] = GetOvershootPoint(points[last - 1]);
            GetCurveSegment(points, t, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3, out float u);
            float pace = Vector3.ProjectOnPlane(CurveTangent(p0, p1, p2, p3, u), Vector3.up).magnitude;
            t = Mathf.Min(last, t + step / Mathf.Max(0.01f, pace));
            if (nextApex < last && t >= nextApex)
            {
                EmitTurnRipple();
                nextApex++;
            }

            GetCurveSegment(points, t, out p0, out p1, out p2, out p3, out u);
            Vector3 previous = transform.position;
            Vector3 offset = Vector3.ProjectOnPlane(CurvePoint(p0, p1, p2, p3, u) - previous, Vector3.up);
            if (offset.sqrMagnitude < 0.000001f)
            {
                yield return null;
                continue;
            }

            Vector3 direction = offset.normalized;
            Vector3 destination = previous + offset;
            if (_agent.Raycast(destination, out NavMeshHit boundary))
                destination = Vector3.MoveTowards(boundary.position, previous, 0.05f);
            float travel = Vector3.Distance(previous, destination);
            if (strike)
            {
                transform.rotation = Quaternion.LookRotation(direction);
                if (TryHitSlash(previous, direction, travel, out float contact))
                {
                    _agent.Move(direction * Mathf.Max(0f, contact - 0.05f));
                    RainStageWeather.RecordDashSweep(previous + Vector3.up, transform.position + Vector3.up);
                    yield break;
                }
            }
            _agent.Move(destination - previous);
            if (strike) RainStageWeather.RecordDashSweep(previous + Vector3.up, transform.position + Vector3.up);
            else FacePlayer();
            if (travel < 0.001f) yield break;
            yield return null;
        }
    }

    /// <summary>
    /// points 곡선의 t(0~통과점 수-1) 위치가 속한 구간의 Catmull-Rom 제어점 p0~p3와 구간 안 비율 u를 출력한다.
    /// 양 끝 구간은 끝 통과점을 한 번 더 써서 곡선이 시작점과 끝점을 지나게 한다.
    /// </summary>
    private static void GetCurveSegment(Vector3[] points, float t, out Vector3 p0, out Vector3 p1, out Vector3 p2,
        out Vector3 p3, out float u)
    {
        int last = points.Length - 1;
        int index = Mathf.Clamp(Mathf.FloorToInt(t), 0, last - 1);
        u = Mathf.Clamp01(t - index);
        p0 = points[Mathf.Max(0, index - 1)];
        p1 = points[index];
        p2 = points[index + 1];
        p3 = points[Mathf.Min(last, index + 2)];
    }

    /// <summary>Catmull-Rom 제어점 p0~p3 구간의 u(0~1) 위치를 반환한다. u가 0이면 p1, 1이면 p2다.</summary>
    private static Vector3 CurvePoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        float u2 = u * u;
        return 0.5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
            + (3f * p1 - p0 - 3f * p2 + p3) * (u2 * u));
    }

    /// <summary>Catmull-Rom 제어점 p0~p3 구간의 u(0~1) 위치 접선 벡터(u에 대한 미분)를 반환한다.</summary>
    private static Vector3 CurveTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        return 0.5f * ((p2 - p0) + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (2f * u)
            + (3f * p1 - p0 - 3f * p2 + p3) * (3f * u * u));
    }

    /// <summary>지금 자리에서 곡선이 휘어 방향이 바뀌었음을 알리는 붉은 파동 _turnRipple을 시야 맵에 등록한다.</summary>
    private void EmitTurnRipple()
    {
        if (_revealMap == null && Game.IsInitialized && Game.Instance.SensoryManager != null)
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
        if (_revealMap != null) _revealMap.EmitRipple(transform.position, _turnRipple);
    }

    /// <summary>
    /// start에서 direction으로 travel만큼 몸 캡슐을 밀어 플레이어에 닿으면 전방 막기·쳐내기를 확인하고 실패 시 이번 돌진 피해 _strikeDamage를 주며 밀어낸다.
    /// 닿은 거리를 contact로 출력하고 판정했으면 true를 반환한다.
    /// </summary>
    private bool TryHitSlash(Vector3 start, Vector3 direction, float travel, out float contact)
    {
        if (!TryCastPlayer(start, direction, travel, out contact, out Vector3 point)) return false;
        DamageContext context = new DamageContext(_strikeDamage, gameObject, point, direction, "TwinHumanSlash");
        if (_player.TryDefendProjectile(context, out bool parried))
        {
            _dashParried = parried;
            return true;
        }
        _player.TakeDamage(context);
        if (_player.IsAlive) _playerMotor.ApplyCombatPush(direction, _dashPushDistance, _dashPushDuration);
        return true;
    }

    /// <summary>
    /// start에서 direction으로 travel만큼 _slashContactRadius 몸 캡슐을 밀어 플레이어에 닿는지 검사한다.
    /// 닿으면 닿은 거리를 contact, 닿은 지점(처음부터 겹쳤으면 플레이어 가슴 높이)을 point로 출력하고 true를 반환한다.
    /// </summary>
    private bool TryCastPlayer(Vector3 start, Vector3 direction, float travel, out float contact, out Vector3 point)
    {
        contact = 0f;
        point = default;
        float radius = _slashContactRadius;
        Vector3 bottom = start + Vector3.up * radius;
        Vector3 top = start + Vector3.up * Mathf.Max(radius, _agent.height - radius);
        int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _slashHits,
            Mathf.Max(0.01f, travel), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_slashHits[i].collider.GetComponentInParent<PlayerHealth>() != _player) continue;
            contact = _slashHits[i].distance;
            point = _slashHits[i].distance > 0f ? _slashHits[i].point : _player.transform.position + Vector3.up;
            return true;
        }
        return false;
    }

    /// <summary>
    /// destination까지 NavMesh 경로 모서리를 따라 _repositionSpeed로 이동 방향을 보며 빠르게 다가간다. _repositionTimeout이 지나거나 막히면 멈춘다.
    /// </summary>
    private IEnumerator MoveQuickly(Vector3 destination)
    {
        _motor.Stop();
        if (!_agent.CalculatePath(destination, _teleportPath)
            || _teleportPath.status != NavMeshPathStatus.PathComplete) yield break;
        Vector3[] corners = _teleportPath.corners;
        float until = Time.time + _repositionTimeout;
        for (int i = 1; i < corners.Length; i++)
        {
            while (Vector3.Distance(transform.position, corners[i]) > 0.05f && Time.time < until && CanSlash())
            {
                Vector3 previous = transform.position;
                _agent.Move(Vector3.MoveTowards(previous, corners[i], _repositionSpeed * Time.deltaTime) - previous);
                Vector3 moved = Vector3.ProjectOnPlane(transform.position - previous, Vector3.up);
                if (moved.sqrMagnitude > 0.000001f) transform.rotation = Quaternion.LookRotation(moved);
                if (Time.deltaTime > 0f && moved.sqrMagnitude < 0.000001f) break;
                yield return null;
            }
        }
        _motor.Stop();
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
    /// 플레이어 수평 시점 기준 arc 범위·distance 거리에서 몸이 들어갈 빈 NavMesh 지면과 완전한 경로를 찾아 position으로 반환한다.
    /// arc가 180도 이하이면 플레이어 앞쪽 후보만 쓰며 12번 안에 찾지 못하면 false를 반환한다.
    /// </summary>
    private bool TryFindSpotAroundPlayer(float distance, float arc, out Vector3 position)
    {
        position = transform.position;
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
            Vector3 candidate = hit.position;
            if ((arc <= 180f && Vector3.Dot(candidate - _player.transform.position, forward) < 0f)
                || (candidate - transform.position).sqrMagnitude < 0.25f) continue;
            float radius = _agent.radius;
            int count = Physics.OverlapCapsuleNonAlloc(candidate + Vector3.up * (radius + 0.05f),
                candidate + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius),
                radius, _teleportContacts, _teleportObstacleMask, QueryTriggerInteraction.Ignore);
            bool blocked = count == _teleportContacts.Length;
            for (int i = 0; i < count; i++)
            {
                if (!_teleportContacts[i].transform.IsChildOf(transform)) blocked = true;
            }
            if (blocked || !_agent.CalculatePath(candidate, _teleportPath)
                || _teleportPath.status != NavMeshPathStatus.PathComplete) continue;
            position = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 늑대 잠수처럼 지금 자리에서 흰 물 파동을 내며 물속으로 가라앉았다가 position으로 옮겨 플레이어를 바라보고 다시 파동을 내며 떠오르며, 떠오를 때까지 기다린다.
    /// </summary>
    public IEnumerator TeleportThroughWater(Vector3 position)
    {
        yield return MoveThroughWater(() =>
        {
            if (_agent.Warp(position) && _player != null) FacePlayer();
        });
    }

    /// <summary>지금 자리에서 물속으로 가라앉았다가 seat(짐승 등 위 자리)에 올라탄 채 떠오르며, 떠오를 때까지 기다린다.</summary>
    public IEnumerator MountThroughWater(Transform seat)
    {
        yield return MoveThroughWater(() => Mount(seat));
    }

    /// <summary>탑승한 자리에서 물속으로 가라앉았다가 짐승 옆 지면으로 내려 떠오르며, 떠오를 때까지 기다린다.</summary>
    public IEnumerator DismountThroughWater()
    {
        yield return MoveThroughWater(Dismount);
    }

    /// <summary>
    /// 가라앉은 바닥에서 arrive로 자리를 옮기는 물속 이동을 이 컴포넌트의 코루틴으로 따로 시작하고 끝날 때까지 기다린다.
    /// 호출한 사격 코루틴이 멈춰도 끝까지 떠오르며, 패턴 취소는 CancelWaterTransition으로 멈춘다. 이미 진행 중이면 그 이동이 끝나기만 기다린다.
    /// </summary>
    private IEnumerator MoveThroughWater(Action arrive)
    {
        if (_teleport == null) _teleport = StartCoroutine(SinkAndRise(arrive));
        while (_teleport != null) yield return null;
    }

    /// <summary>
    /// 피격 판정 _hitCollider를 끄고 _sinkDuration 동안 몸체를 _sinkDistance만큼 가라앉힌 뒤 살아 있으면 arrive로 자리를 옮기고,
    /// _riseDuration 동안 떠올라 판정을 다시 켠다. 가라앉기와 떠오르기를 시작할 때마다 _waterRipple을 내며 끝나면 _teleport를 비운다.
    /// </summary>
    private IEnumerator SinkAndRise(Action arrive)
    {
        _motor.Stop();
        _hitCollider.enabled = false;
        EmitWaterRipple();
        yield return ChangeSinkDepth(1f, _sinkDuration);
        if (_health.IsAlive) arrive();
        EmitWaterRipple();
        yield return ChangeSinkDepth(0f, _riseDuration);
        _hitCollider.enabled = true;
        _teleport = null;
    }

    /// <summary>
    /// 진행 중인 물속 이동을 멈추고 몸체를 바로 땅 위로 되돌리며 꺼 둔 피격 판정을 켠다. 가라앉은 바닥에 닿기 전이면 자리를 옮기지 않는다.
    /// 패턴을 취소하거나 이 컴포넌트가 꺼질 때 호출하며 _teleport, _sinkDepth를 비운다.
    /// </summary>
    public void CancelWaterTransition()
    {
        if (_teleport == null) return;
        StopCoroutine(_teleport);
        _teleport = null;
        _sinkDepth = 0f;
        _hitCollider.enabled = true;
    }

    /// <summary>잠긴 정도 _sinkDepth를 지금 값에서 target(0 땅 위~1 완전히 잠김)까지 duration(초, 게임 시간) 동안 부드럽게 바꾼다.</summary>
    private IEnumerator ChangeSinkDepth(float target, float duration)
    {
        float from = _sinkDepth;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _sinkDepth = Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        _sinkDepth = target;
    }

    /// <summary>
    /// 지금 자리 지면에 흰 물 파동 _waterRipple을 시야 맵에 등록하고 물방울 소리 _waterDropSound를 _waterDropPitch·_waterDropVolume으로 입체 재생한다.
    /// 늑대 잠수 파동처럼 자기 몸은 밝히지 않는다. 늑대 등 위(_mounted)에서 떠오르거나 가라앉을 때는 소리만 내고 파동은 내지 않는다.
    /// </summary>
    private void EmitWaterRipple()
    {
        if (_waterDropSound != SoundId.None)
            Managers.Sound.Play(_waterDropSound, transform.position, _waterDropVolume, gameObject, true, _waterDropPitch);
        // 늑대 등 위는 지면이 아니라 공중에서 파동이 퍼지므로 내지 않는다.
        if (_mounted) return;
        if (_revealMap == null && Game.IsInitialized && Game.Instance.SensoryManager != null)
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
        if (_revealMap != null) _revealMap.EmitRipple(transform.position, _waterRipple, transform);
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
        // 꺼지면 물속 이동 코루틴도 멈추므로 잠긴 몸체와 꺼 둔 피격 판정을 되돌린다.
        CancelWaterTransition();
        _mountedPose = 0f;
        _sinkBody.localPosition = _sinkBodyPosition;
        _sinkBody.localRotation = Quaternion.identity;
        StopShooting();
        ReleaseProjectiles();
        Dismount();
        CancelMelee();
    }
}
