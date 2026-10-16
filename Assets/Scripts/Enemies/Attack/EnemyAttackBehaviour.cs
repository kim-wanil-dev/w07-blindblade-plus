using System;
using System.Collections.Generic;

using UnityEngine;

public enum EnemyAttackPhase
{
    Ready,
    Telegraph,
    Active,
    Recovery
}

[RequireComponent(typeof(EnemySensor))]
public abstract class EnemyAttackBehaviour : MonoBehaviour
{
    private const float MAX_RHYTHM_DELAY = 0.1f;

    [Header("패턴 선택 설정")]
    [SerializeField] private string _patternId;
    [SerializeField] private int _priority;
    [SerializeField, Min(0f)] private float _minimumRange;

    [Header("전조 및 공격 설정")]
    [SerializeField, Min(0.1f)] private float _telegraphDuration = 1f;
    [SerializeField, Min(0f)] private float _recoveryDuration = 0.75f;
    [SerializeField, Min(0.02f)] private float _cooldown = 2f;
    [SerializeField] private bool _cancelWhenHidden = true;
    private EnemySensor _sensor;
    private EnemyAttackPhase _phase;
    private EnemyAttackContext _context;
    private float _phaseEndTime;
    private float _nextAttackTime;

    [Header("박동 공격 전조")]
    [SerializeField, Tooltip("근접·돌진 공격에 박동 전조를 적용합니다. 원거리 공격은 설정과 무관하게 박동을 사용하지 않습니다.")]
    private bool _useHeartbeatTelegraph;
    [SerializeField, Min(1), Tooltip("공격 전에 재생할 박동 횟수입니다. 마지막 박동 이후 공격 지연을 적용합니다.")]
    private int _heartbeatCount = 1;
    [SerializeField, Min(0f), Tooltip("공격 준비 시작부터 첫 박동까지의 고정 대기 시간입니다. 박동 사이 간격과 독립적으로 설정합니다.")]
    private float _firstHeartbeatDelay = 0.2f;
    [SerializeField, Min(0.1f), Tooltip("첫 박동과 두 번째 박동 사이의 최소 간격입니다. 공격마다 한 번 선택합니다.")]
    private float _minimumHeartbeatInterval = 0.4f;
    [SerializeField, Min(0.1f), Tooltip("두 박동 사이의 최대 간격입니다. 최소값보다 작으면 최소값을 사용합니다.")]
    private float _maximumHeartbeatInterval = 0.55f;
    [SerializeField, Min(0.05f), Tooltip("마지막 박동부터 실제 공격까지의 고정 지연입니다. 박동 간격과 독립적으로 설정합니다.")]
    private float _postHeartbeatAttackDelay = 0.2f;
    [SerializeField, Min(0f), Tooltip("공격 시작보다 기합을 먼저 재생할 시간입니다. 마지막 박동 이후로 제한합니다.")]
    private float _attackVoiceLeadTime = 0.05f;
    private bool _useHeartbeatForCurrentAttack;
    private int _heartbeatsPlayed;
    private int _currentHeartbeatCount;
    private bool _attackVoicePlayed;
    private float _nextHeartbeatTime;
    private float _attackVoiceTime;

    [Header("공격 청각 신호")]
    [SerializeField, Min(0f)] private float _telegraphVolume = 1f;
    [SerializeField, Min(0f)] private float _actionVolume = 1f;
    private SoundId _playingActionSound;
    private readonly HashSet<SoundId> _playingHeartbeatSounds = new HashSet<SoundId>();

    public abstract float Range { get; }
    public string PatternId => string.IsNullOrEmpty(_patternId) ? GetType().Name : _patternId;
    public int Priority => _priority;
    public EnemyAttackPhase Phase => _phase;
    public bool IsBusy => _phase != EnemyAttackPhase.Ready;
    public bool IsAvailable => isActiveAndEnabled && !IsBusy && Time.time >= _nextAttackTime;
    public virtual bool AllowsMovement => false;
    public EnemyAttackContext Context => _context;
    public float FirstHeartbeatAt { get; private set; }
    public float LastHeartbeatInterval { get; private set; }
    public int HeartbeatCount => Mathf.Max(1, _heartbeatCount);
    public float ExecutionDuration => ActiveDuration;
    public bool UsesHeartbeatTelegraph => _useHeartbeatTelegraph && SoundAction != EnemySoundAction.Ranged;
    public Vector2 ExpectedAttackInterval => new Vector2(
        GetExpectedAttackInterval(UsesHeartbeatTelegraph ? FirstHeartbeatDelay + (HeartbeatCount - 1) * MinimumHeartbeatInterval + PostHeartbeatAttackDelay : Mathf.Max(0.1f, _telegraphDuration)),
        GetExpectedAttackInterval(UsesHeartbeatTelegraph ? FirstHeartbeatDelay + (HeartbeatCount - 1) * MaximumHeartbeatInterval + PostHeartbeatAttackDelay : Mathf.Max(0.1f, _telegraphDuration)));
    public float RecoveryRemaining => _phase == EnemyAttackPhase.Recovery ? Mathf.Max(0f, _phaseEndTime - Time.time) : 0f;
    public float TelegraphProgress => _phase == EnemyAttackPhase.Telegraph
        ? Mathf.Clamp01(1f - (_phaseEndTime - Time.time) / Mathf.Max(0.1f, _context.TelegraphDuration)) : 1f;
    public event Action<Transform> AttackPerformed;
    public event Action<EnemyAttackContext> TelegraphStarted;
    public event Action<EnemyAttackContext> FirstHeartbeatPlayed;
    public event Action<EnemyAttackContext> AttackCommitted;
    public event Action<EnemyAttackContext> AttackEnded;
    public event Action<EnemyAttackContext> AttackCompleted;
    public event Action<EnemyAttackContext> AttackCancelled;

    protected virtual float ActiveDuration => 0f;
    protected virtual bool HasFinishedActiveAttack => Time.time >= _phaseEndTime;
    protected virtual EnemySoundAction SoundAction => EnemySoundAction.None;
    private float MinimumHeartbeatInterval => Mathf.Max(0.1f, _minimumHeartbeatInterval);
    private float MaximumHeartbeatInterval => Mathf.Max(MinimumHeartbeatInterval, _maximumHeartbeatInterval);
    private float FirstHeartbeatDelay => Mathf.Max(0f, _firstHeartbeatDelay);
    private float PostHeartbeatAttackDelay => Mathf.Max(0.05f, _postHeartbeatAttackDelay);

    protected virtual void Awake()
    {
        _sensor = GetComponent<EnemySensor>();
    }

    protected virtual void OnEnable()
    {
        _phase = EnemyAttackPhase.Ready;
        _nextAttackTime = 0f;
        _useHeartbeatForCurrentAttack = false;
        _heartbeatsPlayed = 0;
        _attackVoicePlayed = false;
    }

    protected virtual void Update()
    {
        if (_useHeartbeatForCurrentAttack && Time.timeScale <= 0f)
        {
            if (_phase == EnemyAttackPhase.Telegraph) CancelAttack();
            return;
        }
        if (_phase == EnemyAttackPhase.Telegraph)
        {
            if (_context.Target == null || !_context.Target.gameObject.activeInHierarchy ||
                _sensor.Target != _context.Target || (_cancelWhenHidden && !_sensor.CanSeeTarget()))
            {
                CancelAttack();
                return;
            }

            if (_useHeartbeatForCurrentAttack)
                UpdateHeartbeatTelegraph();
            else if (Time.time >= _phaseEndTime)
                CommitAttack();
        }
        else if (_phase == EnemyAttackPhase.Active)
        {
            UpdateActiveAttack(_context);
            if (_phase == EnemyAttackPhase.Active && HasFinishedActiveAttack)
                BeginRecovery();
        }
        else if (_phase == EnemyAttackPhase.Recovery && Time.time >= _phaseEndTime)
        {
            EnemyAttackContext context = _context;
            _phase = EnemyAttackPhase.Ready;
            AttackCompleted?.Invoke(context);
        }
    }

    protected virtual void OnDisable()
    {
        CancelAttack();
    }

    /// <summary>
    /// target의 활성 상태와 최소·최대 사거리를 검사하여 패턴 사용 범위 안이면 true를 반환한다.
    /// 쿨타임과 시야는 검사하지 않으며 선택기가 공격 가능한 위치를 판단할 때 사용한다.
    /// </summary>
    public virtual bool CanAttack(Transform target)
    {
        if (!isActiveAndEnabled || target == null || !target.gameObject.activeInHierarchy)
            return false;

        float distanceSquared = (target.position - transform.position).sqrMagnitude;
        return distanceSquared >= _minimumRange * _minimumRange && distanceSquared <= Range * Range;
    }

    /// <summary>
    /// target의 사거리, 쿨타임 및 시야가 유효하면 조준 위치를 고정하고 전조를 시작한다.
    /// immediateMeleeAttack이면 근접 전조와 박동을 생략하고 기합과 공격을 즉시 실행하며 성공 시 true를 반환한다.
    /// </summary>
    internal bool TryAttack(Transform target, bool immediateMeleeAttack = false)
    {
        bool attackImmediately = immediateMeleeAttack && this is EnemyMeleeAttack;
        if ((UsesHeartbeatTelegraph || attackImmediately) && Time.timeScale <= 0f) return false;
        if (!IsAvailable || !CanAttack(target) || target != _sensor.Target || !_sensor.CanSeeTarget())
            return false;

        if (_playingHeartbeatSounds.Count > 0) StopActionSound(true);
        _useHeartbeatForCurrentAttack = UsesHeartbeatTelegraph || attackImmediately;
        _currentHeartbeatCount = HeartbeatCount;
        LastHeartbeatInterval = UsesHeartbeatTelegraph && !attackImmediately && _currentHeartbeatCount > 1
            ? UnityEngine.Random.Range(MinimumHeartbeatInterval, MaximumHeartbeatInterval) : 0f;
        float telegraphDuration = attackImmediately ? 0f
            : (_useHeartbeatForCurrentAttack
                ? FirstHeartbeatDelay + (_currentHeartbeatCount - 1) * LastHeartbeatInterval + PostHeartbeatAttackDelay
                : Mathf.Max(0.1f, _telegraphDuration));
        _context = new EnemyAttackContext(this, target, telegraphDuration);
        _phase = EnemyAttackPhase.Telegraph;
        _phaseEndTime = Time.time + telegraphDuration;
        _nextAttackTime = Time.time + _cooldown;
        EnemyAttackContext context = _context;
        PrepareTelegraph(context);
        if (attackImmediately)
        {
            PlayActionSound(EnemySoundStage.AttackVoice);
            TelegraphStarted?.Invoke(context);
            if (_phase != EnemyAttackPhase.Telegraph) return false;
            CommitAttack();
            return true;
        }
        if (_useHeartbeatForCurrentAttack)
        {
            _nextHeartbeatTime = Time.time + FirstHeartbeatDelay;
            FirstHeartbeatAt = _nextHeartbeatTime;
            _attackVoiceTime = _phaseEndTime - Mathf.Clamp(_attackVoiceLeadTime, 0f, PostHeartbeatAttackDelay * 0.5f);
        }
        else
            PlayActionSound(EnemySoundStage.Telegraph);
        TelegraphStarted?.Invoke(context);
        return true;
    }

    /// <summary>
    /// 진행 중인 전조·공격·후딜을 중단하고 현재 공격 문맥을 취소 이벤트에 전달한다.
    /// 파생 공격의 이동 상태를 복원하고 Ready로 전환하며 이미 실행된 피해는 되돌리지 않는다.
    /// </summary>
    public void CancelAttack()
    {
        if (!IsBusy)
        {
            if (_playingHeartbeatSounds.Count > 0) StopActionSound(true);
            return;
        }

        EnemyAttackContext context = _context;
        _phase = EnemyAttackPhase.Ready;
        StopActionSound(_useHeartbeatForCurrentAttack || _playingHeartbeatSounds.Count > 0);
        _useHeartbeatForCurrentAttack = false;
        _heartbeatsPlayed = 0;
        _attackVoicePlayed = false;
        _nextHeartbeatTime = 0f;
        _attackVoiceTime = 0f;
        OnAttackEnded(context, true);
        AttackCancelled?.Invoke(context);
    }

    /// <summary>
    /// 첫 박동 대기가 지난 뒤 선택한 간격과 횟수로 박동을 실행하고 마지막 박동 이후 기합과 공격을 실행한다.
    /// 게임 시간을 사용하며 박동이나 공격이 0.1초 넘게 지연되면 현재 공격을 취소하여 몰아서 실행하지 않는다.
    /// </summary>
    private void UpdateHeartbeatTelegraph()
    {
        float now = Time.time;
        if (_heartbeatsPlayed < _currentHeartbeatCount && now >= _nextHeartbeatTime)
        {
            if (now >= _phaseEndTime || now - _nextHeartbeatTime > MAX_RHYTHM_DELAY)
            {
                CancelAttack();
                return;
            }
            _heartbeatsPlayed++;
            _nextHeartbeatTime += LastHeartbeatInterval;
            PlayActionSound(EnemySoundStage.Heartbeat);

            if (_heartbeatsPlayed == 1)
                FirstHeartbeatPlayed?.Invoke(_context);
        }
        if (now >= _phaseEndTime && now - _phaseEndTime > MAX_RHYTHM_DELAY)
        {
            CancelAttack();
            return;
        }
        if (!_attackVoicePlayed && now >= _attackVoiceTime)
        {
            _attackVoicePlayed = true;
            if (SoundAction == EnemySoundAction.Melee || SoundAction == EnemySoundAction.Ranged)
                PlayActionSound(EnemySoundStage.AttackVoice);
        }
        if (_heartbeatsPlayed >= _currentHeartbeatCount && now >= _phaseEndTime)
            CommitAttack();
    }

    /// <summary>
    /// telegraphDuration에 최대 실행 시간과 기본 후딜을 더해 쿨타임과 비교한 반복 간격을 반환한다.
    /// 정상 공격을 끝까지 실행하는 경우의 계산값이며 피격·패링·대상 상실에 따른 지연은 포함하지 않는다.
    /// </summary>
    private float GetExpectedAttackInterval(float telegraphDuration)
    {
        return Mathf.Max(_cooldown, telegraphDuration + ActiveDuration + _recoveryDuration);
    }

    /// <summary>
    /// 전조 종료 시 저장된 공격 문맥으로 파생 공격을 실행하고 확정 이벤트를 전달한다.
    /// 조준 위치를 다시 갱신하지 않으며 지속 공격은 Active, 즉시 공격은 후딜로 전환한다.
    /// </summary>
    private void CommitAttack()
    {
        _phase = EnemyAttackPhase.Active;
        _phaseEndTime = Time.time + ActiveDuration;
        EnemyAttackContext context = _context;
        if (SoundAction == EnemySoundAction.Melee && !_useHeartbeatForCurrentAttack) StopActionSound();
        if (!_useHeartbeatForCurrentAttack && (SoundAction == EnemySoundAction.Melee || SoundAction == EnemySoundAction.Ranged))
            PlayActionSound(EnemySoundStage.AttackVoice);
        PlayActionSound(EnemySoundStage.Action);
        PerformAttack(context);
        if (_phase != EnemyAttackPhase.Active)
            return;

        AttackCommitted?.Invoke(context);
        if (_phase != EnemyAttackPhase.Active)
            return;
        AttackPerformed?.Invoke(context.Target);
        if (_phase == EnemyAttackPhase.Active && ActiveDuration <= 0f)
            BeginRecovery();
    }

    /// <summary>
    /// 현재 공격 문맥의 실행 종료를 알리고 설정 후딜과 minimumDuration 중 큰 값으로 종료 시간을 예약한다.
    /// 공격 상태를 Recovery로 변경하여 다른 패턴과 동시에 실행되지 않게 한다.
    /// </summary>
    protected void BeginRecovery(float minimumDuration = 0f)
    {
        _phase = EnemyAttackPhase.Recovery;
        _phaseEndTime = Time.time + Mathf.Max(_recoveryDuration, minimumDuration);
        EnemyAttackContext context = _context;
        if (SoundAction == EnemySoundAction.Melee && !_useHeartbeatForCurrentAttack) StopActionSound();
        OnAttackEnded(context, false);
        AttackEnded?.Invoke(context);
    }

    /// <summary>
    /// 행동 종류와 stage로 공통 설정의 소리를 찾아 기존 매니저에 적 위치의 단발 효과음을 요청한다.
    /// 전조·베기 음량과 근접 SwingType A/B를 적용하며 박동·기합·관련 실행음의 ID를 독립적으로 보관한다.
    /// </summary>
    private void PlayActionSound(EnemySoundStage stage)
    {
        SoundId soundId = ResolveActionSound(stage);
        if (soundId == SoundId.None) return;
        if (_useHeartbeatForCurrentAttack || stage == EnemySoundStage.AttackVoice || _playingHeartbeatSounds.Count > 0)
            _playingHeartbeatSounds.Add(soundId);
        else _playingActionSound = soundId;
        float volume = stage == EnemySoundStage.Telegraph || stage == EnemySoundStage.Heartbeat ? _telegraphVolume : _actionVolume;
        if (this is EnemyMeleeAttack melee && stage == EnemySoundStage.Action && soundId == SoundId.EnemySwordSwing)
            Managers.Sound.PlayAttachedWithLabel(soundId, gameObject, "SwingType", melee.IsLeftToRightSlash ? "A" : "B", volume);
        else
            Managers.Sound.PlayAttached(soundId, gameObject, volume);
    }

    /// <summary>
    /// stage에 맞는 공통 공격 소리 ID를 반환한다.
    /// 파생 공격이 특정 단계의 소리를 교체해도 기존 재생·취소 처리를 사용한다.
    /// </summary>
    protected virtual SoundId ResolveActionSound(EnemySoundStage stage)
    {
        return EnemySoundSettings.GetSound(SoundAction, stage);
    }

    /// <summary>
    /// 현재 패턴이 시작한 소리만 정지하며 일반 정지에서는 자연 종료 신호의 ID를 취소용으로 보관한다.
    /// forceStop이면 자연 종료를 무시해 박동·기합·공격음과 예약 및 보관 ID를 정리하며 발걸음·방어음은 유지한다.
    /// </summary>
    private void StopActionSound(bool forceStop = false)
    {
        if (Managers.IsInitialized)
        {
            if (_playingActionSound != SoundId.None)
                Managers.Sound.StopFor(gameObject, _playingActionSound, forceStop: forceStop);
            foreach (SoundId soundId in _playingHeartbeatSounds)
                Managers.Sound.StopFor(gameObject, soundId, forceStop: forceStop);
        }
        _playingActionSound = SoundId.None;
        if (forceStop) _playingHeartbeatSounds.Clear();
    }

    /// <summary>
    /// 전조 시작 순간에 고정된 context를 사용하여 파생 패턴의 공격 동작을 실행한다.
    /// 피해 영역이나 투사체는 context.AimPosition과 Direction을 기준으로 생성한다.
    /// </summary>
    protected abstract void PerformAttack(EnemyAttackContext context);

    /// <summary>context의 전조를 표시하기 전에 파생 패턴의 공격 방향을 준비한다. 기본 구현은 상태를 변경하지 않는다.</summary>
    protected virtual void PrepareTelegraph(EnemyAttackContext context)
    {
    }

    /// <summary>
    /// 지속 공격 중 매 프레임 context를 파생 패턴에 전달한다.
    /// 기본 구현은 상태를 변경하지 않으며 파생 클래스에서 지속 이동이나 타격을 처리한다.
    /// </summary>
    protected virtual void UpdateActiveAttack(EnemyAttackContext context)
    {
    }

    /// <summary>
    /// context와 cancelled를 사용하여 실행 종료 또는 취소에 따른 파생 공격의 자원을 정리한다.
    /// 기본 구현은 상태를 변경하지 않으며 파생 클래스에서 이동 설정 등을 복원한다.
    /// </summary>
    protected virtual void OnAttackEnded(EnemyAttackContext context, bool cancelled)
    {
    }
}
