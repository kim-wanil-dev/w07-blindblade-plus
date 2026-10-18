using System;
using System.Collections;

using UnityEngine;

using UnityEngine.AI;

using Newtonsoft.Json.Linq;

using BlindBlade.Combat.Contracts;
using BlindBlade.Telemetry;

public enum TwinBossPattern { Idle, Cooperation, Finished = 5, Evading = 7, IaiCharge = 8, AlternatingMelee = 9, Countering = 10 }

/// <summary>
/// 체력을 함께 쓰는 두 보스의 협력 패턴과 피격된 보스의 회피 및 종료 수명을 관리한다.
/// 탑승 가장자리 질주 난사(Cooperation)·거합 차징과 짐승 방해(IaiCharge)·교대 근접(AlternatingMelee) 중 하나를 고른다. 두 보스는 함께 쓰러지므로 단독 패턴은 없다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBossController : MonoBehaviour
{
    private const string COUNTER_TIME_OWNER_A = "TwinBossController.CounterA";
    private const string COUNTER_TIME_OWNER_B = "TwinBossController.CounterB";
    private const string REFLECTED_PROJECTILE_PATTERN_ID = "ReflectedProjectile";

    [Header("보스 연결")]
    [SerializeField] private HumanBossAttack _human;
    [SerializeField] private BeastBossAttack _beast;
    [SerializeField, Tooltip("비어 있으면 현재 Game.Player를 사용합니다.")]
    private KatanaDamageReceiver _player;
    private PlayerMotor _playerMotor;
    private EnemyMotor _humanMotor;
    private EnemyMotor _beastMotor;
    private NavMeshAgent _humanAgent;
    private NavMeshAgent _beastAgent;
    private TwinHumanIai _iai;
    private Coroutine _battle;
    private Coroutine _patternRoutine;
    private Coroutine _beastRoutine;
    private Coroutine _humanRoutine;
    private bool _started;
    private bool _subscribed;
    private bool _patternFinished;
    private bool _interrupted;
    private bool _hitRequested;
    private EnemyHealth _hitBoss;
    private TwinBossPattern _pattern;
    private TwinBossPattern _lastPairPattern;

    [Header("패턴 선택 가중치")]
    [SerializeField, Min(0f), Tooltip("둘 다 생존: 탑승 가장자리 질주 난사")]
    private float _cooperationWeight = 2f;
    [SerializeField, Min(0f), Tooltip("둘 다 생존: 인간 거합 차징과 짐승 잠수 방해. 인간 허리춤 구슬 3개가 모두 찼을 때만 쓰며, 다른 패턴보다 훨씬 크게 두어 구슬이 차면 거의 바로 거합을 쓴다.")]
    private float _iaiChargeWeight = 20f;
    [SerializeField, Min(0f), Tooltip("둘 다 생존: 인간 베기와 짐승 잠수 물기 교대 근접")]
    private float _alternatingMeleeWeight = 2f;

    [Header("패턴 시간")]
    [SerializeField, Min(0f)] private float _patternWaitMin = 1f;
    [SerializeField, Min(0f)] private float _patternWaitMax = 2f;
    [SerializeField, Min(0.1f), Tooltip("탑승 가장자리 질주 난사가 길이 막혀도 끝나는 최대 시간(초)")]
    private float _cooperationDuration = 15f;
    [SerializeField, Range(10f, 360f), Tooltip("탑승 질주가 가장자리에서 이만큼(도) 달려 반원을 다 돌면 다음 패턴으로 넘어간다.")]
    private float _cooperationArc = 170f;
    [SerializeField, Min(0f), Tooltip("회피 이동 후 다음 패턴까지의 휴식 시간(초)")]
    private float _breakRecovery = 2f;

    [Header("공터")]
    [SerializeField, Min(1f), Tooltip("스테이지가 공터를 넘기지 않았을 때 플레이어를 중심으로 달리는 원 반경(미터)")]
    private float _fallbackArenaRadius = 12f;
    private DefenseArenaBoundary _arena;
    private float _arenaInset;

    [Header("거합 차징 방해")]
    [SerializeField, Min(0f), Tooltip("차징 시작부터 짐승의 첫 물기까지의 시간(초)")]
    private float _harassFirstDelay = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("짐승이 물고 난 뒤 다음 물기까지의 시간(초)")]
    private float _harassInterval = 1.8f;

    [Header("교대 근접")]
    [SerializeField, Min(1), Tooltip("인간 연속 돌진 베기와 짐승 가장자리 덮치기를 한 번씩 하는 차례 수")]
    private int _alternatingRounds = 2;
    [SerializeField, Min(1), Tooltip("한 차례에 인간이 돌진·베기·후퇴를 반복하는 횟수")]
    private int _humanSlashesPerRound = 4;
    [SerializeField, Min(0.1f), Tooltip("인간 돌진 사이 간격(초). 돌진마다 붉은 전조가 앞에 붙고 베고 나면 물러난다.")]
    private float _humanSlashInterval = 2f;
    [SerializeField, Min(0f), Tooltip("패턴 시작부터 첫 돌진까지의 준비 시간(초). 시작 위치로 달려가고 전조를 보이는 시간을 포함한다.")]
    private float _alternatingLead = 1.2f;

    [Header("피격 무적 반격")]
    [SerializeField, Min(0f), Tooltip("실제 피격 뒤 무적 방어 상태가 시작되기까지의 지연(초). 이 사이에는 무적 없이 맞는다. 스테이지1 보스와 같다.")]
    private float _hitInvulnerableDelay = 0.08f;
    [SerializeField, Min(0f), Tooltip("실제 피격 후 무적 방어 상태를 유지하는 시간(초). 이 동안 근접 베기·발도에 맞으면 쳐내고 반격한다.")]
    private float _hitInvulnerabilityDuration = 2f;
    [SerializeField, Min(0f), Tooltip("반격 직전 세계를 불릿 타임 배율로 늦추는 전체 실제 시간(초). 페이드인·아웃 구간을 포함한다.")]
    private float _counterSlowDuration = 1.5f;
    [SerializeField, Min(0f)] private float _counterSlowFadeIn = 0.2f;
    [SerializeField, Min(0f)] private float _counterSlowFadeOut = 0.3f;
    [SerializeField, Min(0f), Tooltip("인간 반격 관통 베기 피해")]
    private float _counterDamage = 25f;
    [SerializeField, Tooltip("보스가 공격을 쳐낼 때 보스 발밑에서 퍼지는 파동의 바닥 링과 윤곽선 색")]
    private Color _counterPulseColor = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Min(0f), Tooltip("후퇴 중 보스가 시야 중심에서 이 각도(도)보다 벗어나면 플레이어 시점을 보스 쪽으로 보정한다.")]
    private float _counterViewAssistAngle = 20f;
    [SerializeField, Min(0f)] private float _counterViewAssistSpeed = 120f;
    [SerializeField, Min(0f)] private float _counterCameraKickPitch = 4f;
    [SerializeField, Min(0f)] private float _counterCameraShake = 0.05f;
    [SerializeField, Min(0.01f)] private float _counterCameraShakeDuration = 0.3f;
    [SerializeField, Min(0f), Tooltip("인간이 공격을 쳐낼 때 플레이어를 좌후방 또는 우후방으로 밀어내는 거리(미터)")]
    private float _humanCounterPushDistance = 12f;
    [SerializeField, Min(0.01f), Tooltip("인간이 플레이어를 밀어내는 게임 시간(초). 반격 감속 속에서 흐른다.")]
    private float _humanCounterPushDuration = 0.35f;
    [SerializeField, Range(0f, 90f), Tooltip("시선 정반대 기준 밀어내는 방향이 좌우로 벌어지는 각도(도)")]
    private float _humanCounterPushAngle = 45f;
    private PlayerEmpower _playerEmpower;
    private KatanaCombatCoordinator _playerCombat;
    private PlayerBulletTimeAudio _playerBulletTimeAudio;
    private PlayerPerfectPulse _playerPerfectPulse;
    private TwinBossWarningVisual _humanWarning;
    private EnemyHealth _counterBoss;
    private CombatSessionRuntime _counterTimeRuntime;
    private CombatTimeToken _counterTimeToken;
    private bool _useCounterTimeOwnerB;
    private bool _counterSlowActive;
    private float _counterSlowElapsed;
    private float _appliedCounterTimeScale = 1f;
    private Vector3 _counterLanding;
    private Vector3 _counterPushSide;
    private float _counterPushEndAt;
    private float _humanDefendAt;
    private float _beastDefendAt;

    [Header("피격 회피 이동")]
    [SerializeField, Min(1f)] private float _evadeRadius = 6f;
    [SerializeField, Min(0.1f)] private float _evadeSpeed = 10f;
    [SerializeField, Min(0.5f)] private float _evadeMinimumDistance = 3f;
    [SerializeField, Min(0.1f), Tooltip("빠른 이동 가속도(미터/초 제곱)")]
    private float _moveAcceleration = 40f;
    [SerializeField, Min(0.1f), Tooltip("피격 회피 이동 최대 시간(초)")]
    private float _moveTimeout = 2f;
    [SerializeField] private LayerMask _movementObstacleMask = Physics.DefaultRaycastLayers;
    private readonly Collider[] _movementContacts = new Collider[16];
    private NavMeshPath _movePath;
    private float _humanSpeed;
    private float _humanAcceleration;
    private float _beastSpeed;
    private float _beastAcceleration;
    private float _humanInvulnerableUntil;
    private float _beastInvulnerableUntil;
    private bool _beastEvadeMoving;
    private string _telemetryPatternInstanceId;
    private double _telemetryPatternStartedAt;
    private double _telemetryBossFightStartedAt;
    private double _telemetryHumanDefeatTime = -1d;
    private double _telemetryBeastDefeatTime = -1d;
    private float _telemetryHumanHealth;
    private float _telemetryBeastHealth;
    private readonly System.Collections.Generic.HashSet<EnemyHealth> _telemetryDefeatedActors =
        new System.Collections.Generic.HashSet<EnemyHealth>();
    private bool _telemetryPairDefeatedRecorded;

    public TwinBossPattern Pattern => _pattern;
    public bool IsHumanDefending => _human != null && _human.Health.IsAlive && _human.Health.IsInvulnerable;
    public event Action<TwinBossPattern> PatternChanged;
    public event Action CooperationBroken;
    public event Action BattleFinished;

    void Start()
    {
        _started = true;
        BeginBattle();
    }

    void OnEnable()
    {
        if (_started) BeginBattle();
    }

    void Update()
    {
        if (!_subscribed) return;
        if (_counterSlowActive) UpdateCounterSlow();
        // 스테이지1 보스처럼 피격 직후 짧은 지연 뒤 무적 방어 상태를 시작하고 정해진 시간이 지나면 푼다.
        if (_humanDefendAt > 0f && Time.time >= _humanDefendAt) BeginDefense(_human.Health);
        if (_beastDefendAt > 0f && Time.time >= _beastDefendAt) BeginDefense(_beast.Health);
        if (_humanInvulnerableUntil > 0f && Time.time >= _humanInvulnerableUntil) ClearInvulnerability(_human.Health);
        if (_beastInvulnerableUntil > 0f && Time.time >= _beastInvulnerableUntil) ClearInvulnerability(_beast.Health);
        UpdateBeastProwl();
    }

    /// <summary>health의 무적 방어 예약을 비우고 무적을 켠 뒤 _hitInvulnerabilityDuration 뒤에 풀리도록 종료 시각을 기록한다.</summary>
    private void BeginDefense(EnemyHealth health)
    {
        bool human = health == _human.Health;
        if (human) _humanDefendAt = 0f;
        else _beastDefendAt = 0f;
        if (!health.IsAlive) return;
        health.SetInvulnerable(true);
        if (human) _humanInvulnerableUntil = Time.time + _hitInvulnerabilityDuration;
        else _beastInvulnerableUntil = Time.time + _hitInvulnerabilityDuration;
    }

    /// <summary>
    /// health의 무적 방어 예약·종료 시각을 비우고 무적을 푼다. 인간이 반격을 시작할 때도 호출해 반격 동안은 무적이 없다.
    /// 짐승이 맞고 피하는 잠수 중이면 그 무적은 그대로 둔다.
    /// </summary>
    private void ClearInvulnerability(EnemyHealth health)
    {
        if (health == _human.Health)
        {
            _humanDefendAt = 0f;
            _humanInvulnerableUntil = 0f;
        }
        else
        {
            _beastDefendAt = 0f;
            _beastInvulnerableUntil = 0f;
            // 맞고 피하는 잠수의 무적은 그 잠수가 튀어나올 때 직접 푼다.
            if (_beast.IsEscaping) return;
        }
        health.SetInvulnerable(false);
    }

    /// <summary>
    /// 패턴 대기·회피 회복 중에는 짐승이 멈추지 않고 플레이어 주위를 배회하게 하고, 그 밖에는 배회를 끝낸다.
    /// 피격당한 짐승이 회피 이동 중이면 배회하지 않는다.
    /// </summary>
    private void UpdateBeastProwl()
    {
        bool prowl = _beast.Health.IsAlive && _player.IsAlive && !_beastEvadeMoving && !_beast.IsEscaping
            && (_pattern == TwinBossPattern.Idle || _pattern == TwinBossPattern.Evading
                || (_pattern == TwinBossPattern.Countering && _counterBoss == _human.Health));
        if (!prowl)
        {
            _beast.EndProwl();
            return;
        }
        if (_beast.IsProwling) return;
        GetArenaCircle(out Vector3 center, out float radius);
        _beast.BeginProwl(_player.transform, center, radius);
    }

    /// <summary>
    /// arena와 inset으로 탑승 질주와 거합 차징 위치를 찾을 원형 공터를 정한다.
    /// 스테이지가 보스를 켜기 전에 호출하며 넘기지 않으면 플레이어를 중심으로 한 대체 원을 쓴다.
    /// </summary>
    public void ConfigureArena(DefenseArenaBoundary arena, float inset)
    {
        _arena = arena;
        _arenaInset = Mathf.Max(0f, inset);
    }

    /// <summary>필수 연결과 독립 루트를 확인하고 이동 참조를 캐싱하며 실제 피해·사망 사건을 구독한다.</summary>
    private void BeginBattle()
    {
        if (_player == null && Game.Player != null)
            _player = Game.Player.GetComponent<KatanaDamageReceiver>();
        if (_human == null || _beast == null || _player == null || !_human.IsConfigured
            || _beast.RiderSeat == null || !_human.IsReady || !_beast.IsReady
            || _human.transform.IsChildOf(_beast.transform) || _beast.transform.IsChildOf(_human.transform)
            || transform == _human.transform || transform == _beast.transform)
        {
            Debug.LogError("쌍둥이 보스의 독립 루트·플레이어·사격·탑승·NavMesh 연결을 확인하세요.", this);
            enabled = false;
            return;
        }
        _humanMotor = _human.GetComponent<EnemyMotor>();
        _iai = _human.GetComponent<TwinHumanIai>();
        _playerMotor = _player.GetComponent<PlayerMotor>();
        _beastMotor = _beast.GetComponent<EnemyMotor>();
        _humanAgent = _human.GetComponent<NavMeshAgent>();
        _beastAgent = _beast.GetComponent<NavMeshAgent>();
        _humanSpeed = _humanAgent.speed;
        _humanAcceleration = _humanAgent.acceleration;
        _beastSpeed = _beastAgent.speed;
        _beastAcceleration = _beastAgent.acceleration;
        _playerEmpower = _player.GetComponent<PlayerEmpower>();
        _playerCombat = _player.GetComponent<KatanaCombatCoordinator>();
        _playerBulletTimeAudio = _player.GetComponent<PlayerBulletTimeAudio>();
        _playerPerfectPulse = _player.GetComponent<PlayerPerfectPulse>();
        _humanWarning = _human.GetComponent<TwinBossWarningVisual>();
        _movePath = new NavMeshPath();
        _telemetryBossFightStartedAt = Time.realtimeSinceStartupAsDouble;
        _telemetryHumanHealth = _human.Health.CurrentHealth;
        _telemetryBeastHealth = _beast.Health.CurrentHealth;
        _telemetryHumanDefeatTime = -1d;
        _telemetryBeastDefeatTime = -1d;
        _telemetryDefeatedActors.Clear();
        _telemetryPairDefeatedRecorded = false;
        _hitRequested = false;
        _hitBoss = null;
        _counterBoss = null;
        _interrupted = false;
        _beastEvadeMoving = false;
        _humanDefendAt = 0f;
        _beastDefendAt = 0f;
        _playerCombat.Events.EmpowerChanged += HandlePlayerEmpowerChanged;
        _iai.CounterStarted += HandleIaiCounterStarted;
        _human.Health.Damaged += HandleHumanDamaged;
        _beast.Health.Damaged += HandleBeastDamaged;
        _human.Health.DamageBlocked += HandleHumanDamageBlocked;
        _beast.Health.DamageBlocked += HandleBeastDamageBlocked;
        _human.Health.Died += HandleBossDied;
        _beast.Health.Died += HandleBossDied;
        SetPlayerCollisionIgnored(true);
        _subscribed = true;
        _battle = StartCoroutine(RunBattle());
    }

    /// <summary>
    /// 생존 조건에 맞는 패턴을 실행하고 실제 피격은 회피·회복으로, 무적 중 근접 베기는 반격으로 전환하며 전투 종료를 정리한다.
    /// 반격 뒤에는 스테이지1 보스처럼 패턴 사이 대기를 거쳐 다음 패턴을 고른다.
    /// </summary>
    private IEnumerator RunBattle()
    {
        yield return null;
        while (CanBattle())
        {
            bool recovered = false;
            if (_counterBoss != null) yield return Counter();
            else if (_hitRequested)
            {
                recovered = true;
                yield return Evade();
            }
            if (!CanBattle()) break;
            if (_counterBoss != null) continue;
            ChangePattern(TwinBossPattern.Idle);
            float until = recovered ? Time.time : Time.time + UnityEngine.Random.Range(_patternWaitMin,
                Mathf.Max(_patternWaitMin, _patternWaitMax));
            while (Time.time < until && !_hitRequested && _counterBoss == null && CanBattle()) yield return null;
            if (!CanBattle()) break;
            if (_hitRequested || _counterBoss != null) continue;
            _interrupted = false;
            _patternFinished = false;
            ChangePattern(SelectPairPattern());
            _patternRoutine = StartCoroutine(RunPattern());
            while (!_patternFinished && !_interrupted && !_hitRequested && _counterBoss == null && CanBattle()) yield return null;
            CancelPattern();
        }
        CancelPattern();
        _battle = null;
        ChangePattern(TwinBossPattern.Finished);
        if (_human != null && _beast != null && !_human.Health.IsAlive && !_beast.Health.IsAlive)
            BattleFinished?.Invoke();
        Unsubscribe();
    }

    /// <summary>현재 선택한 협력 패턴을 실행하고 정상 종료를 기록한다.</summary>
    private IEnumerator RunPattern()
    {
        yield return null;
        switch (_pattern)
        {
            case TwinBossPattern.Cooperation: yield return Cooperate(); break;
            case TwinBossPattern.IaiCharge: yield return ChargeIai(); break;
            case TwinBossPattern.AlternatingMelee: yield return AlternateMelee(); break;
        }
        _patternFinished = true;
        _patternRoutine = null;
    }

    /// <summary>
    /// 인간이 물속으로 가라앉았다가 짐승 등 위로 떠올라 타면, 짐승이 공터 가장자리를 플레이어 반대편 호로 빠르게 달리고 인간은 수리검을 하나씩 연달아 던진다.
    /// 가장자리에서 _cooperationArc만큼 달려 반원을 다 돌면 난사와 질주를 끝내고, 인간은 짐승 등에서 가라앉았다가 옆 지면으로 떠올라 내린다.
    /// 길이 막혀도 _cooperationDuration이 지나면 끝낸다.
    /// </summary>
    private IEnumerator Cooperate()
    {
        yield return _human.MountThroughWater(_beast.RiderSeat);
        GetArenaCircle(out Vector3 center, out float radius);
        _beast.BeginEdgeRun(center, radius);
        _human.StartBarrage(_player);
        float until = Time.time + _cooperationDuration;
        while (Time.time < until && _beast.EdgeRunTraveledAngle < _cooperationArc && BothAlive() && !_hitRequested)
        {
            _beast.TickEdgeRun(_player.transform);
            yield return null;
        }
        _human.StopShooting();
        _beast.EndEdgeRun();
        yield return _human.DismountThroughWater();
    }

    /// <summary>
    /// 인간이 공터 먼 곳에서 거합을 차징하는 동안 짐승이 잠수 기습으로 플레이어를 방해한다.
    /// 거합이 끝나면 방해를 멈추고 잠수 중인 짐승을 그 자리에서 땅 위로 돌린다.
    /// </summary>
    private IEnumerator ChargeIai()
    {
        GetArenaCircle(out Vector3 center, out float radius);
        _beastRoutine = StartCoroutine(HarassPlayer(Time.time + _harassFirstDelay));
        yield return _iai.Charge(_player, center, radius);
        StopSideRoutines();
        _beast.InterruptAmbush();
        yield return _beast.Resurface();
    }

    /// <summary>firstStrikeAt(게임 시간)부터 짐승이 공터 가장자리에서 반대편으로 가로지르는 잠수 기습을 하고 _harassInterval 뒤 다시 하는 것을 반복한다.</summary>
    private IEnumerator HarassPlayer(float firstStrikeAt)
    {
        float strikeAt = firstStrikeAt;
        while (BothAlive() && _player.IsAlive)
        {
            GetArenaCircle(out Vector3 center, out float radius);
            // 거합 차징의 붉은 파동과 섞이지 않도록 방해 덮치기는 전조 파동을 내지 않는다.
            yield return _beast.Ambush(_player, strikeAt, center, radius, false);
            strikeAt = Time.time + _harassInterval;
            yield return null;
        }
        _beastRoutine = null;
    }

    /// <summary>
    /// 거합 차징 중 맞은 짐승이 무적 잠수로 피해 다른 곳에서 나올 때까지(HandleBossDamaged가 시작한 BeginBeastEscape) 기다린 뒤 방해를 이어가게 한다.
    /// 인간의 차징은 그대로 둔다.
    /// </summary>
    private IEnumerator EscapeThenHarass()
    {
        while (_beast.IsEscaping) yield return null;
        yield return HarassPlayer(Time.time + _harassInterval);
    }

    /// <summary>
    /// 맞은 짐승이 무적이 되어 물 파동과 함께 땅속으로 뛰어들고 잠시 숨었다가 공터 가장자리 중 플레이어와 떨어진 곳에서 파동과 함께 튀어나오게 한다.
    /// 지금 공터 원(GetArenaCircle)과 플레이어를 BeastBossAttack.BeginEscapeDive에 넘기며 이미 피하는 중이면 아무것도 하지 않는다.
    /// </summary>
    private void BeginBeastEscape()
    {
        GetArenaCircle(out Vector3 center, out float radius);
        _beast.BeginEscapeDive(_player, center, radius);
    }

    /// <summary>
    /// 인간 연속 돌진 베기와 짐승 가장자리 덮치기를 서로 겹치지 않게 _alternatingRounds번 번갈아 한다.
    /// 한 차례마다 짐승이 땅에 뛰어드는 동안 인간이 스테이지1 보스 돌진처럼 _humanSlashInterval 간격으로 _humanSlashesPerRound번 돌진해 베고 물러나며,
    /// 마지막 돌진 베기가 끝난 뒤에야 짐승이 가장자리에서 나와 전조 후 가로질러 돌진하고, 그동안 인간은 크게 뒤로 물러난다. 끝나면 잠수 중인 짐승을 땅 위로 돌린다.
    /// </summary>
    private IEnumerator AlternateMelee()
    {
        float firstStrikeAt = Time.time + _alternatingLead;
        for (int round = 0; round < _alternatingRounds && BothAlive(); round++)
        {
            // 땅에 뛰어드는 것은 공격이 아니므로 인간 베기와 함께 진행한다.
            _beastRoutine = StartCoroutine(BeastDive());
            for (int i = 0; i < _humanSlashesPerRound && BothAlive(); i++)
                yield return _human.DashStrike(_player, firstStrikeAt + _humanSlashInterval * i);
            while (_beastRoutine != null) yield return null;
            if (!BothAlive()) break;
            GetArenaCircle(out Vector3 center, out float radius);
            // 돌진 베기를 마친 인간은 짐승이 덮치는 동안 크게 뒤로 물러나 자리를 비킨다.
            _humanRoutine = StartCoroutine(HumanBackstep());
            yield return _beast.Ambush(_player, Time.time, center, radius);
            firstStrikeAt = Time.time + _alternatingLead;
        }
        yield return _beast.Resurface();
    }

    /// <summary>인간을 플레이어 반대쪽으로 크게 뒤로 물러나게 하고 끝나면 실행 참조를 비운다.</summary>
    private IEnumerator HumanBackstep()
    {
        yield return _human.Backstep(_player);
        _humanRoutine = null;
    }

    /// <summary>짐승을 땅에 뛰어들게 하고 끝나면 실행 참조를 비운다.</summary>
    private IEnumerator BeastDive()
    {
        yield return _beast.Dive();
        _beastRoutine = null;
    }

    /// <summary>패턴이 따로 돌리던 짐승·인간 코루틴을 멈추고 참조를 비운다.</summary>
    private void StopSideRoutines()
    {
        if (_beastRoutine != null) StopCoroutine(_beastRoutine);
        _beastRoutine = null;
        if (_humanRoutine != null) StopCoroutine(_humanRoutine);
        _humanRoutine = null;
    }

    /// <summary>
    /// 설정된 공터 중심과 가장자리에서 _arenaInset만큼 안쪽 반경을 center·radius로 반환한다.
    /// 공터가 없으면 플레이어 위치와 _fallbackArenaRadius를 쓴다.
    /// </summary>
    private void GetArenaCircle(out Vector3 center, out float radius)
    {
        if (_arena != null)
        {
            center = _arena.transform.position;
            radius = Mathf.Max(1f, _arena.Radius - _arenaInset);
            return;
        }
        center = _player.transform.position;
        radius = _fallbackArenaRadius;
    }

    /// <summary>
    /// 최초 피격 보스만 빈 위치로 빠르게 이동시키고 회복 후 피격 대상과 요청을 해제한다.
    /// 짐승은 달려 피하는 대신 맞을 때 시작한 무적 잠수(BeginBeastEscape)가 다른 곳에서 튀어나올 때까지 기다린다.
    /// </summary>
    private IEnumerator Evade()
    {
        ChangePattern(TwinBossPattern.Evading);
        bool humanHit = _hitBoss == _human.Health;
        if (!humanHit)
        {
            while (_beast.IsEscaping && CanBattle()) yield return null;
            float recoverUntil = Time.time + _breakRecovery;
            while (Time.time < recoverUntil && CanBattle() && _counterBoss == null) yield return null;
            _hitRequested = false;
            _hitBoss = null;
            _interrupted = false;
            yield break;
        }
        EnemyMotor motor = humanHit ? _humanMotor : _beastMotor;
        NavMeshAgent agent = humanHit ? _humanAgent : _beastAgent;
        Vector3 position = agent.transform.position;
        // 맞은 짐승은 배회 대신 회피 이동을 하고, 인간이 맞았으면 짐승은 계속 배회한다.
        _beastEvadeMoving = !humanHit;
        if (_beastEvadeMoving) _beast.EndProwl();
        bool moving = _hitBoss != null && _hitBoss.IsAlive && motor.IsReady
            && TryEscape(agent, out position);
        if (moving) moving = BeginMove(motor, agent, position, _evadeSpeed);
        float until = Time.time + _moveTimeout;
        while (Time.time < until && CanBattle() && moving && _hitBoss != null
            && _hitBoss.IsAlive && !motor.HasArrived && _counterBoss == null) yield return null;
        RestoreMovement();
        _beastEvadeMoving = false;
        until = Time.time + _breakRecovery;
        // 회복 중 무적 상태에서 베이면 바로 반격으로 넘어간다.
        while (Time.time < until && CanBattle() && _counterBoss == null) yield return null;
        _hitRequested = false;
        _hitBoss = null;
        _interrupted = false;
    }

    /// <summary>
    /// 스테이지1 보스의 관통 베기 반격처럼 인간(_counterBoss)이 반격한다. 쳐낼 때 플레이어를 좌후방 또는 우후방으로 밀어낸 동안(HandleBossDamageBlocked)
    /// 인간은 제자리에서 연기가 붉어지고, 밀려나는 플레이어가 내려앉는 순간(_counterPushEndAt) 그 자리(_counterLanding)에 닿도록 밀리는 도중에 출발해
    /// 밀려난 쪽(_counterPushSide)으로 휜 원호로 플레이어를 뚫고 베어 지나가는 관통 베기(HumanBossAttack.CounterDash)로 _counterDamage 피해를 준다.
    /// 플레이어와의 물리 충돌은 전투 내내 꺼져 있어 몸에 막히지 않는다. 퍼펙트 패링에 막히면 떨며 밀려난다. 반격 동안은 무적이 없으며 감속이 끝나면 반격 상태를 푼다.
    /// </summary>
    private IEnumerator Counter()
    {
        EnemyHealth health = _counterBoss;
        ChangePattern(TwinBossPattern.Countering);
        _hitRequested = false;
        _hitBoss = null;
        TwinBossWarningVisual warning = _humanWarning;
        _humanMotor.Stop();
        // 전조: 출발 순간까지 게임 시간으로 연기가 붉게 물든다. 원호를 달리는 시간만큼 내려앉기 전에 출발한다.
        float dashAt = Mathf.Max(Time.time, _counterPushEndAt - _human.GetCounterArcTime(_counterLanding));
        warning.BeginWarning(dashAt - Time.time);
        while (Time.time < dashAt && health.IsAlive)
        {
            AssistPlayerView(_humanAgent.transform.position);
            yield return null;
        }
        if (health.IsAlive && _player.IsAlive)
            yield return _human.CounterDash(_player, _counterDamage, _counterLanding, _counterPushSide);
        warning.EndWarning();
        while (_counterSlowActive) yield return null;
        EndCounter();
    }

    /// <summary>반격 감속·불릿 타임 소리를 끝내고 반격하던 보스의 무적을 풀며 _counterBoss를 비운다. 반격 중이 아니면 아무것도 바꾸지 않는다.</summary>
    private void EndCounter()
    {
        if (_counterBoss == null) return;
        EnemyHealth health = _counterBoss;
        _counterBoss = null;
        StopCounterSlow();
        _humanWarning.EndWarning();
        ClearInvulnerability(health);
    }

    /// <summary>position이 플레이어 시야 중심에서 _counterViewAssistAngle보다 벗어나면 실제 시간만큼 플레이어 좌우 시점을 그쪽으로 돌린다.</summary>
    private void AssistPlayerView(Vector3 position)
    {
        if (Time.timeScale <= 0f) return;
        _playerMotor.NudgeViewToward(position, _counterViewAssistAngle, _counterViewAssistSpeed * Time.unscaledDeltaTime);
    }

    /// <summary>반격 감속을 처음부터 시작하고 연출음 버스를 바로 먹먹하게 한다. 이후 Update가 감속을 진행한다.</summary>
    private void StartCounterSlow()
    {
        _counterSlowElapsed = 0f;
        _counterSlowActive = true;
        ApplyCounterSlow(0f, 1f);
    }

    /// <summary>실제 시간으로 _counterSlowElapsed를 늘리고 페이드인·유지·페이드아웃 비중으로 세계 시간과 소리를 갱신한다. 끝나면 감속을 멈춘다.</summary>
    private void UpdateCounterSlow()
    {
        if (Time.timeScale > 0f) _counterSlowElapsed += Time.unscaledDeltaTime;
        if (_counterSlowElapsed >= _counterSlowDuration)
        {
            StopCounterSlow();
            return;
        }
        float fadeOut = _counterSlowFadeOut > 0f
            ? Mathf.Clamp01((_counterSlowDuration - _counterSlowElapsed) / _counterSlowFadeOut) : 1f;
        float fadeIn = _counterSlowFadeIn > 0f ? Mathf.Clamp01(_counterSlowElapsed / _counterSlowFadeIn) : 1f;
        ApplyCounterSlow(Mathf.Min(fadeIn, fadeOut), fadeOut);
    }

    /// <summary>진행 중인 반격 감속을 끝내고 시간 요청과 불릿 타임 소리를 해제한다.</summary>
    private void StopCounterSlow()
    {
        if (!_counterSlowActive && !_counterTimeToken.IsValid) return;
        _counterSlowActive = false;
        if (_playerBulletTimeAudio != null) _playerBulletTimeAudio.SetExternalWarp(0f, 0f);
        if (_counterTimeRuntime != null && _counterTimeToken.IsValid) _counterTimeRuntime.Release(_counterTimeToken);
        _counterTimeRuntime = null;
        _counterTimeToken = default;
        _appliedCounterTimeScale = 1f;
    }

    /// <summary>
    /// weight만큼 세계 배율을 1과 플레이어 불릿 타임 세계 배율 사이에서 요청하고 같은 비중으로 불릿 타임 소리를 건다.
    /// 연출음 버스에는 cinematicWeight를 쓰며 배율이 같으면 시간 요청을 다시 등록하지 않는다. 요청은 두 소유자 이름을 번갈아 바꿔 끊기지 않게 한다.
    /// </summary>
    private void ApplyCounterSlow(float weight, float cinematicWeight)
    {
        _playerBulletTimeAudio.SetExternalWarp(weight, cinematicWeight);
        float scale = Mathf.Lerp(1f, _playerEmpower.WorldTimeScale, weight);
        if (Mathf.Abs(scale - _appliedCounterTimeScale) < 0.001f) return;
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
        _appliedCounterTimeScale = scale;
    }

    /// <summary>agent가 플레이어 주변의 현재 위치와 떨어진 빈 지면으로 회피하도록 destination을 반환한다.</summary>
    private bool TryEscape(NavMeshAgent agent, out Vector3 destination)
    {
        destination = agent.transform.position;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 desired = _player.transform.position
                + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _evadeRadius;
            if (!TryDestination(agent, desired, out Vector3 candidate)
                || (candidate - agent.transform.position).sqrMagnitude < _evadeMinimumDistance * _evadeMinimumDistance) continue;
            destination = candidate;
            return true;
        }
        return false;
    }

    /// <summary>agent의 빈 NavMesh 지면과 완전한 경로를 확인해 desired의 destination을 반환한다.</summary>
    private bool TryDestination(NavMeshAgent agent, Vector3 desired, out Vector3 destination)
    {
        desired.y = agent.nextPosition.y;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        bool found = NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.5f, filter);
        destination = hit.position;
        if (!found || !agent.CalculatePath(destination, _movePath)
            || _movePath.status != NavMeshPathStatus.PathComplete) return false;
        float radius = agent.radius;
        int count = Physics.OverlapCapsuleNonAlloc(destination + Vector3.up * (radius + 0.05f),
            destination + Vector3.up * Mathf.Max(radius + 0.05f, agent.height - radius), radius,
            _movementContacts, _movementObstacleMask, QueryTriggerInteraction.Ignore);
        if (count == _movementContacts.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Transform contact = _movementContacts[i].transform;
            if (contact.IsChildOf(agent.transform)) continue;
            return false;
        }
        return true;
    }

    /// <summary>motor·agent의 속도와 가속도를 설정하고 destination으로 지상 이동을 시작해 수락 여부를 반환한다.</summary>
    private bool BeginMove(EnemyMotor motor, NavMeshAgent agent, Vector3 destination, float speed)
    {
        agent.speed = speed;
        agent.acceleration = _moveAcceleration;
        return motor.MoveTo(destination, 0.1f);
    }

    /// <summary>두 보스의 빠른 이동을 정지하고 초기 속도·가속도를 복원한다. 배회 중인 짐승은 멈추지 않는다.</summary>
    private void RestoreMovement()
    {
        if (_humanAgent != null)
        {
            _humanMotor.Stop();
            _humanAgent.speed = _humanSpeed;
            _humanAgent.acceleration = _humanAcceleration;
        }
        if (_beastAgent != null && !_beast.IsProwling)
        {
            _beastMotor.Stop();
            _beastAgent.speed = _beastSpeed;
            _beastAgent.acceleration = _beastAcceleration;
        }
    }

    /// <summary>
    /// 질주 난사·거합 차징·교대 근접을 가중치로 고르되 바로 앞 협력 패턴은 다른 후보가 있으면 빼서 연속 반복을 막는다.
    /// 거합 차징은 인간 허리춤 기운(HumanBossAttack.IsChargeFull)이 모두 찼을 때만 후보가 된다.
    /// 고른 패턴을 _lastPairPattern에 기록해 반환하며 가중치가 모두 0이면 질주 난사를 쓴다.
    /// </summary>
    private TwinBossPattern SelectPairPattern()
    {
        TwinBossPattern[] patterns = { TwinBossPattern.Cooperation, TwinBossPattern.IaiCharge, TwinBossPattern.AlternatingMelee };
        // 거합은 인간 허리춤 구슬 기운이 모두 차 있어야만 고른다.
        float[] weights = { _cooperationWeight, _human.IsChargeFull ? _iaiChargeWeight : 0f, _alternatingMeleeWeight };
        float total = 0f;
        for (int i = 0; i < patterns.Length; i++)
            if (patterns[i] != _lastPairPattern) total += weights[i];
        bool excludeLast = total > 0f;
        if (!excludeLast) total = weights[0] + weights[1] + weights[2];
        TwinBossPattern selected = TwinBossPattern.Cooperation;
        if (total > 0f)
        {
            float choice = UnityEngine.Random.value * total;
            for (int i = 0; i < patterns.Length; i++)
            {
                if (excludeLast && patterns[i] == _lastPairPattern) continue;
                selected = patterns[i];
                if (choice < weights[i]) break;
                choice -= weights[i];
            }
        }
        _lastPairPattern = selected;
        return selected;
    }

    /// <summary>플레이어와 하나 이상의 보스가 생존했는지 반환한다.</summary>
    private bool CanBattle()
    {
        return _player != null && _player.IsAlive && _human != null && _beast != null
            && (_human.Health.IsAlive || _beast.Health.IsAlive);
    }

    /// <summary>협력할 두 보스가 모두 생존했는지 반환한다.</summary>
    private bool BothAlive()
    {
        return _human != null && _beast != null && _human.Health.IsAlive && _beast.Health.IsAlive;
    }

    /// <summary>인간의 실제 피해 사건을 전달해 인간을 회피 대상으로 기록한다.</summary>
    private void HandleHumanDamaged(DamageContext context)
    {
        HandleBossDamaged(_human.Health, context);
    }

    /// <summary>짐승의 실제 피해 사건을 전달해 짐승을 회피 대상으로 기록한다.</summary>
    private void HandleBeastDamaged(DamageContext context)
    {
        HandleBossDamaged(_beast.Health, context);
    }

    /// <summary>
    /// health를 최초 피격 회피 대상으로 기록하며 회피·회복 중 추가 피해는 대상을 바꾸지 않는다.
    /// 스테이지1 보스처럼 _hitInvulnerableDelay 뒤 무적 방어 상태가 시작되도록 예약하되, 플레이어 불릿 타임(강화 준비·강화 공격) 중이거나
    /// 반사된 수리검(context.PatternId)에 맞거나 맞은 쪽이 짐승이면 무적을 만들지 않는다. 거합 받아치기는 플레이어 강화 공격이라 불릿 타임 규칙으로 무적이 생기지 않는다. 반격 중 무적이 풀린 보스가 맞으면 반격을 멈추고 회피로 넘어간다.
    /// </summary>
    private void HandleBossDamaged(EnemyHealth health, DamageContext context)
    {
        RecordBossDamage(health, context);
        // 짐승은 피격 후 무적·방어 상태가 없다.
        bool noDefense = _playerEmpower.IsReady || _playerEmpower.IsStriking
            || context.PatternId == REFLECTED_PROJECTILE_PATTERN_ID || health == _beast.Health;
        if (!health.IsAlive || noDefense) ClearInvulnerability(health);
        else if ((health == _human.Health ? _humanDefendAt : _beastDefendAt) <= 0f)
        {
            // 피격 직후 짧은 지연 동안은 무적 없이 맞을 수 있고, 지연이 끝나면 무적 방어 상태가 시작된다.
            if (health == _human.Health) _humanDefendAt = Time.time + _hitInvulnerableDelay;
            else _beastDefendAt = Time.time + _hitInvulnerableDelay;
            if (_hitInvulnerableDelay <= 0f) BeginDefense(health);
        }
        // 맞은 짐승은 돌진 준비 중이어도 바로 무적이 되어 땅속으로 뛰어들었다가 공터 가장자리에서 나온다.
        if (health == _beast.Health && health.IsAlive && _pattern != TwinBossPattern.Finished) BeginBeastEscape();
        if (_counterBoss == health)
        {
            InterruptCounter(health);
            return;
        }
        if (_hitRequested || _pattern == TwinBossPattern.Evading || _pattern == TwinBossPattern.Finished
            || _pattern == TwinBossPattern.Countering) return;
        // 거합 차징은 인간을 맞혀야만 끊기고, 맞은 짐승은 잠수해 피한 뒤 방해를 이어간다.
        if (_pattern == TwinBossPattern.IaiCharge && health == _beast.Health)
        {
            if (_beastRoutine != null) StopCoroutine(_beastRoutine);
            _beast.InterruptAmbush();
            _beastRoutine = health.IsAlive && _iai.IsCharging ? StartCoroutine(EscapeThenHarass()) : null;
            return;
        }
        _hitRequested = true;
        _hitBoss = health;
        if (_pattern == TwinBossPattern.Cooperation)
            CooperationBroken?.Invoke();
        CancelPattern();
    }

    /// <summary>인간에게 차단된 context를 전달해 무적 방어 중 근접 베기면 인간이 쳐내고 반격하게 한다.</summary>
    private void HandleHumanDamageBlocked(DamageContext context)
    {
        HandleBossDamageBlocked(_human.Health, context);
    }

    /// <summary>짐승에게 차단된 context를 전달해 무적 방어 중 근접 베기면 짐승이 쳐내고 반격하게 한다.</summary>
    private void HandleBeastDamageBlocked(DamageContext context)
    {
        HandleBossDamageBlocked(_beast.Health, context);
    }

    /// <summary>
    /// 스테이지1 보스처럼 인간이 무적 방어 상태에서 플레이어의 근접 베기·발도 context를 막으면 검을 쳐내고 반격을 시작한다. 짐승은 무적이 없어 막히지 않는다.
    /// 진행 중인 패턴·회피를 멈추고 무적을 바로 끝내(방어 자세 칼도 사라짐, 밀려난 플레이어는 반격 중 공격할 수 없다) 감속을 먼저 시작해 퍼펙트 패링 소리가 먹먹하게 들리게 하고,
    /// 칼 튕김·불꽃·퍼펙트 패링 소리·보스 발밑 붉은 파동·카메라 흔들림을 내고 플레이어를 좌후방 또는 우후방으로 크게 밀어낸다(PushPlayerForCounter). 검기 타격은 피해 없이 막기만 한다.
    /// </summary>
    private void HandleBossDamageBlocked(EnemyHealth health, DamageContext context)
    {
        if (_counterBoss != null || !health.IsAlive || health != _human.Health || context.Instigator != _player.gameObject
            || !IsPlayerMeleeAttack(context) || _pattern == TwinBossPattern.Finished) return;
        _counterBoss = health;
        // 반격하는 동안은 무적도 방어 자세 칼도 없다.
        ClearInvulnerability(health);
        CancelPattern();
        StartCounterSlow();
        Vector3 direction = -_playerMotor.ViewForward;
        KatanaWeaponVisual weapon = _player.GetComponent<KatanaWeaponVisual>();
        if (weapon != null) weapon.PlayAttackDeflection();
        Game.Instance.EffectManager.Play(EffectId.ParryClash, context.Point, Quaternion.LookRotation(direction));
        Managers.Sound.Play(SoundId.PerfectParryClash, context.Point, 1f, gameObject);
        _playerPerfectPulse.BeginAt(health.transform.position, Vector3.up, _counterPulseColor, _counterPulseColor);
        _playerMotor.AddCameraKick(_counterCameraKickPitch, _counterCameraShake, _counterCameraShakeDuration);
        PushPlayerForCounter();
    }

    /// <summary>
    /// 인간 반격의 시작으로 플레이어를 시선 정반대에서 좌우 무작위로 _humanCounterPushAngle만큼 벌어진 좌후방 또는 우후방으로
    /// _humanCounterPushDistance만큼 _humanCounterPushDuration 동안 밀어낸다. 내려앉을 자리를 NavMesh 끝까지로 줄여 _counterLanding에,
    /// 밀려난 쪽 옆 방향을 _counterPushSide에, 밀어내기가 끝나는 게임 시각을 _counterPushEndAt에 저장해 관통 베기 원호가 쓴다.
    /// </summary>
    private void PushPlayerForCounter()
    {
        Vector3 forward = Vector3.ProjectOnPlane(_playerMotor.ViewForward, Vector3.up).normalized;
        float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        _counterPushSide = Vector3.Cross(Vector3.up, forward) * side;
        float angle = _humanCounterPushAngle * Mathf.Deg2Rad;
        Vector3 push = (-forward * Mathf.Cos(angle) + _counterPushSide * Mathf.Sin(angle)).normalized;
        Vector3 from = _player.transform.position;
        _counterLanding = from + push * _humanCounterPushDistance;
        // 벽이나 공터 끝에서는 밀림이 멈추므로 걸을 수 있는 지면 끝까지만 내려앉는다고 본다.
        if (NavMesh.SamplePosition(from, out NavMeshHit ground, 2f, NavMesh.AllAreas)
            && NavMesh.Raycast(ground.position, _counterLanding, out NavMeshHit edge, NavMesh.AllAreas))
            _counterLanding = edge.position;
        _counterPushEndAt = Time.time + _humanCounterPushDuration;
        _playerMotor.ApplyCombatPush(push, _humanCounterPushDistance, _humanCounterPushDuration);
    }


    /// <summary>context의 PatternId가 플레이어 일반 근접 베기 또는 발도 베기인지 반환한다.</summary>
    private static bool IsPlayerMeleeAttack(DamageContext context)
    {
        return context.PatternId == PlayerAttackKind.Normal.ToString()
            || context.PatternId == PlayerAttackKind.IaidoFinisher.ToString();
    }

    /// <summary>
    /// 반격 중 무적이 풀려 health가 맞으면 반격 연출을 멈추고 전투 흐름을 다시 시작해 그 보스가 회피하게 한다.
    /// 진행 중인 돌진·덮치기는 패턴 취소로 정리한다.
    /// </summary>
    private void InterruptCounter(EnemyHealth health)
    {
        if (_battle != null) StopCoroutine(_battle);
        EndCounter();
        CancelPattern();
        RestoreMovement();
        _hitRequested = true;
        _hitBoss = health;
        _battle = StartCoroutine(RunBattle());
    }

    /// <summary>플레이어 불릿 타임 준비가 시작되면 스테이지1 보스처럼 두 보스의 피격 후 무적과 방어 예약을 즉시 끝낸다.</summary>
    private void HandlePlayerEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        if (empowerEvent.Stage != PlayerEmpowerStage.Ready) return;
        ClearInvulnerability(_human.Health);
        ClearInvulnerability(_beast.Health);
    }

    /// <summary>인간 거합 받아치기가 발동하면 인간의 피격 무적과 방어 예약을 초기화한다.</summary>
    private void HandleIaiCounterStarted()
    {
        ClearInvulnerability(_human.Health);
    }

    /// <summary>
    /// health 사망을 기록해 남은 연계를 중단하며 회피 중에는 생존자 이동을 유지한다.
    /// </summary>
    private void HandleBossDied(EnemyHealth health)
    {
        RecordBossDefeat(health);
        _interrupted = true;
        if (_pattern != TwinBossPattern.Evading && _pattern != TwinBossPattern.Countering) CancelPattern();
    }

    /// <summary>패턴 코루틴·인간과 짐승의 보조 코루틴·탄환·물속 이동·탑승·베기·거합·점프·잠수를 취소하고 빠른 이동 설정을 복원한다.</summary>
    private void CancelPattern()
    {
        if (_patternRoutine != null) StopCoroutine(_patternRoutine);
        _patternRoutine = null;
        StopSideRoutines();
        if (_human != null)
        {
            _human.StopShooting();
            _human.CancelMelee();
            // 물속으로 가라앉던 중이면 늦게 올라타지 않도록 먼저 멈추고 내린다.
            _human.CancelWaterTransition();
            _human.Dismount();
        }
        if (_iai != null) _iai.Cancel();
        if (_beast != null) _beast.CancelAttack();
        RestoreMovement();
    }

    /// <summary>pattern 변경을 저장하고 표현 담당자에게 전달한다. 대기·회피가 아닌 패턴으로 바뀌면 짐승 배회를 끝낸다.</summary>
    private void ChangePattern(TwinBossPattern pattern)
    {
        if (_pattern == pattern) return;
        EndTelemetryPattern(GetTelemetryPatternOutcome());
        _pattern = pattern;
        BeginTelemetryPattern(pattern);
        // 패턴이 시작되는 프레임에 바로 배회를 끝내 패턴 이동과 겹치지 않게 한다. 인간이 반격할 때는 짐승이 계속 배회한다.
        bool prowling = pattern == TwinBossPattern.Idle || pattern == TwinBossPattern.Evading
            || (pattern == TwinBossPattern.Countering && _counterBoss == _human.Health);
        if (!prowling && _beast != null) _beast.EndProwl();
        PatternChanged?.Invoke(pattern);
    }

    /// <summary>pattern의 분석 ID를 정하고 새 부모 패턴 인스턴스 시작 사건을 보낸다.</summary>
    private void BeginTelemetryPattern(TwinBossPattern pattern)
    {
        string patternId = TelemetryPatternId(pattern);
        if (string.IsNullOrEmpty(patternId)) return;
        _telemetryPatternInstanceId = Guid.NewGuid().ToString("N");
        _telemetryPatternStartedAt = Time.realtimeSinceStartupAsDouble;
        _human.SetTelemetryParent(patternId, _telemetryPatternInstanceId);
        _beast.SetTelemetryParent(patternId, _telemetryPatternInstanceId);
        _iai.SetTelemetryParent(patternId, _telemetryPatternInstanceId);
        CombatTelemetrySignals.Event("boss_pattern_started", new JObject {
            ["patternInstanceId"] = _telemetryPatternInstanceId, ["parentPatternId"] = patternId,
            ["humanHealthStart"] = _human.Health.CurrentHealth, ["beastHealthStart"] = _beast.Health.CurrentHealth });
    }

    /// <summary>열려 있는 부모 패턴의 종료 결과와 지속 시간, 종료 체력을 기록하고 인스턴스 참조를 비운다.</summary>
    private void EndTelemetryPattern(string outcome)
    {
        if (string.IsNullOrEmpty(_telemetryPatternInstanceId)) return;
        CombatTelemetrySignals.Event("boss_pattern_ended", new JObject {
            ["patternInstanceId"] = _telemetryPatternInstanceId, ["parentPatternId"] = TelemetryPatternId(_pattern),
            ["durationSeconds"] = Time.realtimeSinceStartupAsDouble - _telemetryPatternStartedAt,
            ["patternOutcome"] = outcome, ["humanHealthEnd"] = _human.Health.CurrentHealth,
            ["beastHealthEnd"] = _beast.Health.CurrentHealth });
        _telemetryPatternInstanceId = null;
    }

    /// <summary>pattern에 대해 계획에서 정한 부모 패턴 ID를 반환하고 나머지 상태는 빈 문자열로 구분한다.</summary>
    private static string TelemetryPatternId(TwinBossPattern pattern)
    {
        switch (pattern)
        {
            case TwinBossPattern.Cooperation: return "Cooperation";
            case TwinBossPattern.IaiCharge: return "IaiCharge";
            case TwinBossPattern.AlternatingMelee: return "AlternatingMelee";
            case TwinBossPattern.Countering: return "Countering";
            default: return string.Empty;
        }
    }

    /// <summary>현재 패턴의 완료, 플레이어 피격 중단, 보스 사망 취소 중 하나를 반환한다.</summary>
    private string GetTelemetryPatternOutcome()
    {
        if (_patternFinished) return "Completed";
        if (!_human.Health.IsAlive || !_beast.Health.IsAlive) return "CancelledOnBossDeath";
        if (_hitRequested || _counterBoss != null) return "InterruptedByPlayerHit";
        if (!_player.IsAlive) return "CancelledOnPlayerDeath";
        return "CancelledOnStageExit";
    }

    /// <summary>health에 적용된 실제 플레이어 피해와 공격 출처를 보스 개체별 사건으로 저장한다.</summary>
    private void RecordBossDamage(EnemyHealth health, DamageContext context)
    {
        if (context.Instigator != _player.gameObject) return;
        bool human = health == _human.Health;
        float previous = human ? _telemetryHumanHealth : _telemetryBeastHealth;
        float current = health.CurrentHealth;
        float appliedDamage = Mathf.Max(0f, previous - current);
        if (human) _telemetryHumanHealth = current;
        else _telemetryBeastHealth = current;
        if (appliedDamage <= 0f) return;
        CombatTelemetrySignals.Event("boss_damage_received", new JObject {
            ["bossActor"] = human ? "Human" : "Beast", ["playerAttackPattern"] = context.PatternId,
            ["appliedDamage"] = appliedDamage, ["healthAfter"] = current,
            ["parentPatternId"] = TelemetryPatternId(_pattern), ["patternInstanceId"] = _telemetryPatternInstanceId,
            ["position"] = TelemetryJson.Position(health.transform.position) });
    }

    /// <summary>health가 처음 쓰러졌을 때 개체 처치와 쌍둥이 전체 처치 시각을 저장한다.</summary>
    private void RecordBossDefeat(EnemyHealth health)
    {
        if (!_telemetryDefeatedActors.Add(health)) return;
        double defeatTime = Time.realtimeSinceStartupAsDouble - _telemetryBossFightStartedAt;
        bool human = health == _human.Health;
        if (human) _telemetryHumanDefeatTime = defeatTime;
        else _telemetryBeastDefeatTime = defeatTime;
        CombatTelemetrySignals.Event("boss_actor_defeated", new JObject {
            ["bossActor"] = human ? "Human" : "Beast", ["timeSinceStageStart"] = defeatTime,
            ["position"] = TelemetryJson.Position(health.transform.position) });
        if (_telemetryPairDefeatedRecorded || _human.Health.IsAlive || _beast.Health.IsAlive) return;
        _telemetryPairDefeatedRecorded = true;
        CombatTelemetrySignals.Event("boss_pair_defeated", new JObject {
            ["humanDefeatTime"] = _telemetryHumanDefeatTime, ["beastDefeatTime"] = _telemetryBeastDefeatTime,
            ["stageClearTime"] = defeatTime });
    }

    /// <summary>
    /// ignored가 참이면 인간·짐승의 모든 비트리거 충돌체와 플레이어 CharacterController의 물리 충돌을 끄고, 거짓이면 되돌린다.
    /// 돌진·덮치기로 겹칠 때 플레이어가 보스 몸 위로 밀려 올라가지 않게 하며 공격 판정의 레이·겹침 검사에는 영향을 주지 않는다.
    /// </summary>
    private void SetPlayerCollisionIgnored(bool ignored)
    {
        CharacterController controller = _player.GetComponent<CharacterController>();
        foreach (Collider body in _human.GetComponentsInChildren<Collider>(true))
            if (!body.isTrigger) Physics.IgnoreCollision(controller, body, ignored);
        foreach (Collider body in _beast.GetComponentsInChildren<Collider>(true))
            if (!body.isTrigger) Physics.IgnoreCollision(controller, body, ignored);
    }

    /// <summary>실제 피해·사망 사건의 전투 구독을 해제하고 플레이어와의 물리 충돌을 되돌린다.</summary>
    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        EndCounter();
        StopCounterSlow();
        if (_playerCombat != null) _playerCombat.Events.EmpowerChanged -= HandlePlayerEmpowerChanged;
        if (_iai != null) _iai.CounterStarted -= HandleIaiCounterStarted;
        _humanDefendAt = 0f;
        _beastDefendAt = 0f;
        if (_player != null && _human != null && _beast != null) SetPlayerCollisionIgnored(false);
        if (_human != null)
        {
            _human.Health.Damaged -= HandleHumanDamaged;
            _human.Health.DamageBlocked -= HandleHumanDamageBlocked;
            _human.Health.SetInvulnerable(false);
            _human.Health.Died -= HandleBossDied;
        }
        if (_beast != null)
        {
            _beast.Health.Damaged -= HandleBeastDamaged;
            _beast.Health.DamageBlocked -= HandleBeastDamageBlocked;
            _beast.Health.SetInvulnerable(false);
            _beast.Health.Died -= HandleBossDied;
        }
        _humanInvulnerableUntil = 0f;
        _beastInvulnerableUntil = 0f;
    }

    void OnDisable()
    {
        if (_battle != null) StopCoroutine(_battle);
        _battle = null;
        CancelPattern();
        Unsubscribe();
        _hitRequested = false;
        _hitBoss = null;
        ChangePattern(TwinBossPattern.Idle);
    }
}
