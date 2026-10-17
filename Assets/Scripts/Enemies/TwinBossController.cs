using System;
using System.Collections;

using UnityEngine;

using UnityEngine.AI;

public enum TwinBossPattern { Idle, Cooperation, HumanRanged, BeastZigzag, BeastJump, Finished, Evading = 7, IaiCharge = 8, AlternatingMelee = 9 }

/// <summary>
/// 생존 조건별 협력·단독 패턴과 피격된 보스의 회피 및 종료 수명을 관리한다.
/// 둘 다 살아 있으면 탑승 가장자리 질주 난사(Cooperation)·거합 차징과 짐승 방해(IaiCharge)·교대 근접(AlternatingMelee) 중 하나를 고른다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBossController : MonoBehaviour
{
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
    [SerializeField, Min(0f), Tooltip("둘 다 생존: 인간 거합 차징과 짐승 잠수 방해")]
    private float _iaiChargeWeight = 1.5f;
    [SerializeField, Min(0f), Tooltip("둘 다 생존: 인간 베기와 짐승 잠수 물기 교대 근접")]
    private float _alternatingMeleeWeight = 2f;
    [SerializeField, Min(0f)] private float _humanRangedWeight = 1f;
    [SerializeField, Min(0f)] private float _beastZigzagWeight = 1f;
    [SerializeField, Min(0f)] private float _beastJumpWeight = 1f;

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
    [SerializeField, Min(1), Tooltip("인간 연속 베기와 짐승 가장자리 돌진을 한 번씩 하는 차례 수")]
    private int _alternatingRounds = 2;
    [SerializeField, Min(1), Tooltip("한 차례에 인간이 연달아 베는 횟수")]
    private int _humanSlashesPerRound = 3;
    [SerializeField, Min(0.1f), Tooltip("인간 베기 사이 간격(초). 베기마다 붉은 전조가 앞에 붙는다.")]
    private float _humanSlashInterval = 1f;
    [SerializeField, Min(0f), Tooltip("패턴 시작부터 첫 베기까지의 준비 시간(초)")]
    private float _alternatingLead = 0.9f;

    [Header("피격 회피 이동")]
    [SerializeField, Min(0f), Tooltip("실제 피격 후 추가 타격을 패링하는 무적 시간(초)")]
    private float _hitInvulnerabilityDuration = 2f;
    [SerializeField, Min(0f)] private float _deflectPushDistance = 3f;
    [SerializeField, Min(0.01f)] private float _deflectPushDuration = 0.16f;
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

    public TwinBossPattern Pattern => _pattern;
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
        if (_humanInvulnerableUntil > 0f && Time.time >= _humanInvulnerableUntil)
        {
            _humanInvulnerableUntil = 0f;
            _human.Health.SetInvulnerable(false);
        }
        if (_beastInvulnerableUntil > 0f && Time.time >= _beastInvulnerableUntil)
        {
            _beastInvulnerableUntil = 0f;
            _beast.Health.SetInvulnerable(false);
        }
        UpdateBeastProwl();
    }

    /// <summary>
    /// 패턴 대기·회피 회복 중에는 짐승이 멈추지 않고 플레이어 주위를 배회하게 하고, 그 밖에는 배회를 끝낸다.
    /// 피격당한 짐승이 회피 이동 중이면 배회하지 않는다.
    /// </summary>
    private void UpdateBeastProwl()
    {
        bool prowl = _beast.Health.IsAlive && _player.IsAlive && !_beastEvadeMoving
            && (_pattern == TwinBossPattern.Idle || _pattern == TwinBossPattern.Evading);
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
        _movePath = new NavMeshPath();
        _hitRequested = false;
        _hitBoss = null;
        _interrupted = false;
        _beastEvadeMoving = false;
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

    /// <summary>생존 조건에 맞는 패턴을 실행하고 실제 피격은 회피·회복으로 전환하며 전투 종료를 정리한다.</summary>
    private IEnumerator RunBattle()
    {
        yield return null;
        while (CanBattle())
        {
            bool recovered = _hitRequested;
            if (recovered) yield return Evade();
            if (!CanBattle()) break;
            ChangePattern(TwinBossPattern.Idle);
            float until = recovered ? Time.time : Time.time + UnityEngine.Random.Range(_patternWaitMin,
                Mathf.Max(_patternWaitMin, _patternWaitMax));
            while (Time.time < until && !_hitRequested && CanBattle()) yield return null;
            if (!CanBattle()) break;
            if (_hitRequested) continue;
            _interrupted = false;
            _patternFinished = false;
            ChangePattern(SelectPattern());
            _patternRoutine = StartCoroutine(RunPattern());
            while (!_patternFinished && !_interrupted && !_hitRequested && CanBattle()) yield return null;
            CancelPattern();
        }
        CancelPattern();
        _battle = null;
        ChangePattern(TwinBossPattern.Finished);
        if (_human != null && _beast != null && !_human.Health.IsAlive && !_beast.Health.IsAlive)
            BattleFinished?.Invoke();
        Unsubscribe();
    }

    /// <summary>현재 선택한 협력 또는 단독 패턴을 실행하고 정상 종료를 기록한다.</summary>
    private IEnumerator RunPattern()
    {
        yield return null;
        switch (_pattern)
        {
            case TwinBossPattern.Cooperation: yield return Cooperate(); break;
            case TwinBossPattern.IaiCharge: yield return ChargeIai(); break;
            case TwinBossPattern.AlternatingMelee: yield return AlternateMelee(); break;
            case TwinBossPattern.HumanRanged:
                _human.StartShooting(_player, false);
                while (_human.IsAttacking || _human.HasProjectiles) yield return null;
                break;
            case TwinBossPattern.BeastZigzag: yield return _beast.Zigzag(_player); break;
            case TwinBossPattern.BeastJump: yield return _beast.Jump(_player); break;
        }
        _patternFinished = true;
        _patternRoutine = null;
    }

    /// <summary>
    /// 인간을 태운 짐승이 공터 가장자리를 플레이어 반대편 호로 빠르게 달리고 인간은 수리검을 하나씩 연달아 던진다.
    /// 가장자리에서 _cooperationArc만큼 달려 반원을 다 돌면 난사와 질주를 끝내고 내리며, 길이 막혀도 _cooperationDuration이 지나면 끝낸다.
    /// </summary>
    private IEnumerator Cooperate()
    {
        _human.Mount(_beast.RiderSeat);
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
        _human.Dismount();
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
            yield return _beast.Ambush(_player, strikeAt, center, radius);
            strikeAt = Time.time + _harassInterval;
            yield return null;
        }
        _beastRoutine = null;
    }

    /// <summary>
    /// 거합 차징 중 맞은 짐승이 하던 기습을 멈추고 바로 땅에 뛰어들어 피한 뒤 방해를 이어가게 한다.
    /// 인간의 차징은 그대로 두며 피격 무적은 HandleBossDamaged가 이미 적용했다.
    /// </summary>
    private IEnumerator EscapeThenHarass()
    {
        yield return _beast.Dive();
        yield return HarassPlayer(Time.time + _harassInterval);
    }

    /// <summary>
    /// 인간 연속 베기와 짐승 가장자리 돌진을 서로 겹치지 않게 _alternatingRounds번 번갈아 한다.
    /// 한 차례마다 짐승이 땅에 뛰어드는 동안 인간이 _humanSlashInterval 간격으로 _humanSlashesPerRound번 베고,
    /// 마지막 베기가 끝난 뒤에야 짐승이 가장자리에서 나와 전조 후 가로질러 돌진한다. 끝나면 잠수 중인 짐승을 땅 위로 돌린다.
    /// </summary>
    private IEnumerator AlternateMelee()
    {
        float firstStrikeAt = Time.time + _alternatingLead;
        for (int round = 0; round < _alternatingRounds && BothAlive(); round++)
        {
            // 땅에 뛰어드는 것은 공격이 아니므로 인간 베기와 함께 진행한다.
            _beastRoutine = StartCoroutine(BeastDive());
            for (int i = 0; i < _humanSlashesPerRound && BothAlive(); i++)
                yield return _human.Slash(_player, firstStrikeAt + _humanSlashInterval * i);
            while (_beastRoutine != null) yield return null;
            if (!BothAlive()) break;
            GetArenaCircle(out Vector3 center, out float radius);
            yield return _beast.Ambush(_player, Time.time, center, radius);
            firstStrikeAt = Time.time + _alternatingLead;
        }
        yield return _beast.Resurface();
    }

    /// <summary>짐승을 땅에 뛰어들게 하고 끝나면 실행 참조를 비운다.</summary>
    private IEnumerator BeastDive()
    {
        yield return _beast.Dive();
        _beastRoutine = null;
    }

    /// <summary>패턴이 따로 돌리던 짐승 코루틴을 멈추고 참조를 비운다.</summary>
    private void StopSideRoutines()
    {
        if (_beastRoutine != null) StopCoroutine(_beastRoutine);
        _beastRoutine = null;
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

    /// <summary>최초 피격 보스만 빈 위치로 빠르게 이동시키고 회복 후 피격 대상과 요청을 해제한다.</summary>
    private IEnumerator Evade()
    {
        ChangePattern(TwinBossPattern.Evading);
        bool humanHit = _hitBoss == _human.Health;
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
            && _hitBoss.IsAlive && !motor.HasArrived) yield return null;
        RestoreMovement();
        _beastEvadeMoving = false;
        until = Time.time + _breakRecovery;
        while (Time.time < until && CanBattle()) yield return null;
        _hitRequested = false;
        _hitBoss = null;
        _interrupted = false;
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
    /// 둘 다 살아 있으면 SelectPairPattern의 협력 패턴을, 한쪽만 살아 있으면 생존자의 개별 패턴을 가중치로 선택해 반환한다.
    /// </summary>
    private TwinBossPattern SelectPattern()
    {
        bool humanAlive = _human.Health.IsAlive;
        bool beastAlive = _beast.Health.IsAlive;
        if (humanAlive && beastAlive) return SelectPairPattern();
        float cooperation = 0f;
        float human = humanAlive ? _humanRangedWeight : 0f;
        float zigzag = beastAlive ? _beastZigzagWeight : 0f;
        float jump = beastAlive ? _beastJumpWeight : 0f;
        float total = cooperation + human + zigzag + jump;
        if (total <= 0f) return humanAlive ? TwinBossPattern.HumanRanged : TwinBossPattern.BeastZigzag;
        float choice = UnityEngine.Random.value * total;
        if (choice < cooperation) return TwinBossPattern.Cooperation;
        choice -= cooperation;
        if (choice < human) return TwinBossPattern.HumanRanged;
        choice -= human;
        if (choice < zigzag) return TwinBossPattern.BeastZigzag;
        return jump > 0f ? TwinBossPattern.BeastJump
            : (zigzag > 0f ? TwinBossPattern.BeastZigzag
                : (human > 0f ? TwinBossPattern.HumanRanged : TwinBossPattern.Cooperation));
    }

    /// <summary>
    /// 질주 난사·거합 차징·교대 근접을 가중치로 고르되 바로 앞 협력 패턴은 다른 후보가 있으면 빼서 연속 반복을 막는다.
    /// 고른 패턴을 _lastPairPattern에 기록해 반환하며 가중치가 모두 0이면 질주 난사를 쓴다.
    /// </summary>
    private TwinBossPattern SelectPairPattern()
    {
        TwinBossPattern[] patterns = { TwinBossPattern.Cooperation, TwinBossPattern.IaiCharge, TwinBossPattern.AlternatingMelee };
        float[] weights = { _cooperationWeight, _iaiChargeWeight, _alternatingMeleeWeight };
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
        HandleBossDamaged(_human.Health);
    }

    /// <summary>짐승의 실제 피해 사건을 전달해 짐승을 회피 대상으로 기록한다.</summary>
    private void HandleBeastDamaged(DamageContext context)
    {
        HandleBossDamaged(_beast.Health);
    }

    /// <summary>health를 최초 피격 회피 대상으로 기록하며 회피·회복 중 추가 피해는 대상을 바꾸지 않는다.</summary>
    private void HandleBossDamaged(EnemyHealth health)
    {
        if (health.IsAlive && _hitInvulnerabilityDuration > 0f)
        {
            health.SetInvulnerable(true);
            if (health == _human.Health)
                _humanInvulnerableUntil = Time.time + _hitInvulnerabilityDuration;
            else
                _beastInvulnerableUntil = Time.time + _hitInvulnerabilityDuration;
        }
        if (_hitRequested || _pattern == TwinBossPattern.Evading || _pattern == TwinBossPattern.Finished) return;
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

    /// <summary>
    /// 인간에게 차단된 context를 전달해 인간의 무적을 해제하고 플레이어 타격을 쳐낸다.
    /// </summary>
    private void HandleHumanDamageBlocked(DamageContext context)
    {
        HandleBossDamageBlocked(_human.Health, context);
    }

    /// <summary>짐승에게 차단된 context를 전달해 짐승의 무적을 해제하고 플레이어 타격을 쳐낸다.</summary>
    private void HandleBeastDamageBlocked(DamageContext context)
    {
        HandleBossDamageBlocked(_beast.Health, context);
    }

    /// <summary>
    /// health가 무적으로 차단한 플레이어 공격 context에 패링 효과·소리·검 튕김을 적용한다.
    /// 플레이어를 시점 뒤로 설정 거리만큼 밀고 해당 보스의 무적과 종료 시각을 즉시 해제한다.
    /// </summary>
    private void HandleBossDamageBlocked(EnemyHealth health, DamageContext context)
    {
        if (context.Instigator != _player.gameObject) return;
        health.SetInvulnerable(false);
        if (health == _human.Health) _humanInvulnerableUntil = 0f;
        else _beastInvulnerableUntil = 0f;
        Vector3 direction = -_playerMotor.ViewForward;
        _playerMotor.ApplyCombatPush(direction, _deflectPushDistance, _deflectPushDuration);
        KatanaWeaponVisual weapon = _player.GetComponent<KatanaWeaponVisual>();
        if (weapon != null) weapon.PlayAttackDeflection();
        Game.Instance.EffectManager.Play(EffectId.ParryClash, context.Point, Quaternion.LookRotation(direction));
        Managers.Sound.Play(SoundId.ParryClash, context.Point, 1f, gameObject);
    }

    /// <summary>health 사망을 기록해 남은 연계를 중단하며 회피 중에는 생존자 이동을 유지한다.</summary>
    private void HandleBossDied(EnemyHealth health)
    {
        _interrupted = true;
        if (_pattern != TwinBossPattern.Evading) CancelPattern();
    }

    /// <summary>패턴 코루틴·인간과 짐승의 보조 코루틴·탄환·탑승·베기·거합·점프·잠수를 취소하고 빠른 이동 설정을 복원한다.</summary>
    private void CancelPattern()
    {
        if (_patternRoutine != null) StopCoroutine(_patternRoutine);
        _patternRoutine = null;
        StopSideRoutines();
        if (_human != null)
        {
            _human.StopShooting();
            _human.CancelMelee();
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
        _pattern = pattern;
        // 패턴이 시작되는 프레임에 바로 배회를 끝내 패턴 이동과 겹치지 않게 한다.
        if (pattern != TwinBossPattern.Idle && pattern != TwinBossPattern.Evading && _beast != null) _beast.EndProwl();
        PatternChanged?.Invoke(pattern);
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
