using System.Collections.Generic;

using UnityEngine;


/// <summary>
/// 플레이어 베기·방어·피격을 FMOD 사운드에 연결한다.
/// 실제 적 피격음은 EnemyDamageAudio가 처리하고 모든 재생은 Managers.Sound를 사용한다.
/// </summary>
[RequireComponent(typeof(PlayerController), typeof(PlayerEmpower))]
[DisallowMultipleComponent]
public sealed class PlayerCombatAudio : MonoBehaviour
{
    private const float ENEMY_AUDIO_SCAN_INTERVAL = 0.25f;
    private const float DODGE_ECHO_WAIT = 0.4f;

    [Header("전투 효과음")]
    [SerializeField] private SoundId _swingSound = SoundId.SwordSwing;
    [SerializeField] private SoundId _hitSound = SoundId.SwordClash;
    [SerializeField, Min(0f)] private float _volume = 0.8f;

    [Header("방어·피격 신호")]
    [SerializeField, Min(0f)] private float _damageVolumeMultiplier = 1f;
    [SerializeField, Range(0.25f, 4f)] private float _damagePitch = 1f;
    [SerializeField, Tooltip("퍼펙트 패링 순간 가드·패링 소리 대신 재생하는 소리. 연출음 버스라 불릿 타임 효과를 받지 않는다.")]
    private SoundId _perfectParrySound = SoundId.PerfectParryClash;
    [SerializeField] private SoundId _ultimateReadySound = SoundId.EmpowerUltimateReady;
    private PlayerHealth _health;
    private PlayerEmpower _empower;
    private PlayerActionDriver _timing;
    private PlayerMotor _motor;
    private int _pendingSwingAction = -1;
    private float _swingBeginTime;

    [Header("Combat Contact")]
    [SerializeField] private EnemyCombatService _contactService;

    private PlayerController _player;
    private KatanaCombatCoordinator _katana;
    private int _lastSwingAction = -1;
    private int _lastHitAction = -1;
    private int _lastContactAction = -1;
    private bool _isPrimary;

    [Header("중앙 사운드 설정")]
    private CombatSoundProfile _profile;
    private readonly List<PendingCue> _pendingCues = new List<PendingCue>();
    private float _cueClock;
    private PlayerClock _playerClock;
    private float _lastDodgeCooldown;
    private float _nextEnemyAudioScan;
    private int _lastDashAction = -1;
    private float _dodgeEchoUntil;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _katana = GetComponent<KatanaCombatCoordinator>();
        _health = GetComponent<PlayerHealth>();
        _empower = GetComponent<PlayerEmpower>();
        _timing = GetComponent<PlayerActionDriver>();
        _motor = GetComponent<PlayerMotor>();
        _playerClock = GetComponent<PlayerClock>();
        _profile = Resources.Load<CombatSoundProfile>(CombatSoundProfile.RESOURCE_PATH);
        // 기존 씬의 적 피격음 override도 금속 접촉음으로 연결한다.
        if (_hitSound == SoundId.SwordHit) _hitSound = SoundId.SwordClash;
        _isPrimary = GetComponent<PlayerCombatAudio>() == this;
        if (!_isPrimary) enabled = false;
    }

    void OnEnable()
    {
        if (!_isPrimary) return;
        _player.ActionEvent += HandleAction;
        _lastSwingAction = -1;
        _lastHitAction = -1;
        _lastContactAction = -1;
        _pendingSwingAction = -1;
        _pendingCues.Clear();
        _cueClock = 0f;
        _lastDodgeCooldown = _motor.DodgeCooldownRemaining;
        _lastDashAction = -1;
        _dodgeEchoUntil = 0f;
        _nextEnemyAudioScan = 0f;
        _health.DamageReceived += HandleDamage;
        _empower.UltimateReadyParry += HandleUltimateReadyParry;
        _empower.UltimateReadyDodge += HandleUltimateReadyParry;
        _katana.Events.DodgeChanged += HandleDodgeChanged;
        if (_contactService != null)
            _contactService.AttackContactResolved += HandleContact;
        if (_katana != null && _katana.isActiveAndEnabled)
        {
            _katana.DefenseSucceeded += HandleDefense;
        }
    }

    void Start()
    {
        RegisterEnemyAudio();
        // 퍼펙트 패링음은 판정 순간 바로 들려야 하므로 샘플 데이터를 미리 올려 둔다.
        if (_isPrimary && _perfectParrySound != SoundId.None) Managers.Sound.PreloadSampleData(_perfectParrySound);
        if (_isPrimary && _ultimateReadySound != SoundId.None) Managers.Sound.PreloadSampleData(_ultimateReadySound);
        if (_profile != null && _profile.PlayAmbience)
            Managers.Sound.PlayBgm(_profile.AmbienceSound, gameObject, _profile.LoopAmbience, _profile.AmbienceVolume);
    }

    void OnDisable()
    {
        if (!_isPrimary) return;
        _player.ActionEvent -= HandleAction;
        _health.DamageReceived -= HandleDamage;
        _empower.UltimateReadyParry -= HandleUltimateReadyParry;
        _empower.UltimateReadyDodge -= HandleUltimateReadyParry;
        _katana.Events.DodgeChanged -= HandleDodgeChanged;
        _pendingSwingAction = -1;
        _pendingCues.Clear();
        if (_contactService != null)
            _contactService.AttackContactResolved -= HandleContact;
        if (_katana != null)
        {
            _katana.DefenseSucceeded -= HandleDefense;
        }
        if (Managers.IsInitialized)
            Managers.Sound.StopFor(gameObject);
    }

    void Update()
    {
        if (Time.unscaledTime >= _nextEnemyAudioScan)
        {
            RegisterEnemyAudio();
            _nextEnemyAudioScan = Time.unscaledTime + ENEMY_AUDIO_SCAN_INTERVAL;
        }
        if (!_player.IsPaused && Time.deltaTime > 0f)
        {
            float cooldown = _motor.DodgeCooldownRemaining;
            if (cooldown > _lastDodgeCooldown + 0.001f)
                QueueCue(CombatSoundCue.Dodge, Vector3.zero, true);
            _lastDodgeCooldown = cooldown;
            _cueClock += _playerClock.DeltaTime;
            UpdatePendingCues();
            if (_dodgeEchoUntil > Time.unscaledTime && Managers.Sound.EmphasizeDodgeSound(gameObject))
                _dodgeEchoUntil = 0f;
        }
        if (_pendingSwingAction < 0 || _player.IsPaused || Time.deltaTime <= 0f) return;
        if (_pendingSwingAction != _player.CurrentActionId
            || (_player.CurrentState != PlayerActionState.NormalAttack && _player.CurrentState != PlayerActionState.IaidoSlash))
        {
            _pendingSwingAction = -1;
            return;
        }
        if (_timing.GetPresentationElapsed(_pendingSwingAction) < _swingBeginTime) return;
        PlaySwing(_pendingSwingAction);
        _pendingSwingAction = -1;
    }

    /// <summary>
    /// action의 일반·반격·충전 베기를 공통 베기 상황에 연결하고 실제 베기 시작에 재생을 예약한다.
    /// 발도음과 타수 Label은 사용하지 않으며 명중음은 적 피해 사건에 맡긴다.
    /// </summary>
    private void HandleAction(PlayerActionEvent action)
    {
        if (action.Stage == PlayerActionStage.DashStarted && _lastDashAction != action.ActionId)
        {
            _lastDashAction = action.ActionId;
            QueueCue(CombatSoundCue.Dodge, Vector3.zero, true);
        }
        if ((action.Stage == PlayerActionStage.AttackStarted && action.State == PlayerActionState.NormalAttack)
            || action.Stage == PlayerActionStage.SlashStarted)
        {
            _pendingSwingAction = action.ActionId;
            _timing.GetStrikeWindow(action.State == PlayerActionState.IaidoSlash ? PlayerAttackKind.IaidoFinisher : PlayerAttackKind.Normal,
                out _swingBeginTime, out _);
            if (_lastSwingAction == action.ActionId
                || QueueCue(CombatSoundCue.Slash, Vector3.zero, true, action.ActionId, _swingBeginTime, true))
            {
                _lastSwingAction = action.ActionId;
                _pendingSwingAction = -1;
            }
        }
        if (action.Stage == PlayerActionStage.ActionEnded && action.ActionId == _pendingSwingAction)
            _pendingSwingAction = -1;
        if (_contactService == null && action.Stage == PlayerActionStage.HitConfirmed && _lastHitAction != action.ActionId)
        {
            _lastHitAction = action.ActionId;
            if (!QueueCue(CombatSoundCue.Contact, Vector3.zero, true))
                Managers.Sound.PlayAttached(_hitSound, gameObject, _volume);
        }
    }

    /// <summary>
    /// request의 실제 접촉 hasContact와 point에 일반 충돌음을 한 번 재생한다.
    /// 적 피해음은 EnemyDamageAudio에 맡기고 헛스윙에는 추가 결과음을 재생하지 않는다.
    /// </summary>
    private void HandleContact(PlayerAttackRequest request, bool hasContact, Vector3 point, bool enemyDamaged)
    {
        if (request.ActionId != _player.CurrentActionId || _lastContactAction == request.ActionId)
            return;
        _lastContactAction = request.ActionId;
        if (hasContact && !enemyDamaged)
        {
            _lastHitAction = request.ActionId;
            if (!QueueCue(CombatSoundCue.Contact, point, false))
                Managers.Sound.Play(_hitSound, point, _volume, gameObject);
        }
    }

    /// <summary>
    /// result의 방어 종류와 접촉 위치로 가드 또는 패링 설정을 재생하고, 퍼펙트 패링이면 대신 _perfectParrySound를 재생한다.
    /// 정확한 패링이면 생존한 공격자 위치에 고통 음성도 재생하며 피해와 반격 기회는 변경하지 않는다.
    /// </summary>
    private void HandleDefense(KatanaDefenseResult result)
    {
        if (result.IsParry && result.Incoming.Instigator != null)
        {
            EnemyHealth enemy = result.Incoming.Instigator.GetComponentInParent<EnemyHealth>();
            if (enemy != null && enemy.IsAlive)
            {
                Managers.Sound.StopFor(enemy.gameObject, SoundId.EnemyGetParryed, forceStop: true);
                Managers.Sound.PlayAttached(SoundId.EnemyGetParryed, enemy.gameObject);
            }
        }
        if (result.IsPerfectParry && _perfectParrySound != SoundId.None
            && Managers.Sound.Play(_perfectParrySound, result.Point, _volume, gameObject)) return;
        if (QueueCue(result.IsParry ? CombatSoundCue.Parry : CombatSoundCue.Guard, result.Point, false)) return;
        Managers.Sound.Play(result.IsParry ? SoundId.ParryClash : _hitSound, result.Point, _volume, gameObject);
    }

    /// <summary>쳐내기·극한 회피로 거합 토큰이 가득 찬 알림을 플레이어에 붙여 재생한다.</summary>
    private void HandleUltimateReadyParry()
    {
        if (_ultimateReadySound == SoundId.None) return;
        Managers.Sound.PlayAttached(_ultimateReadySound, gameObject, _volume);
    }

    /// <summary>일반 극한 회피 또는 쌍둥이 거합 회피 사건을 받으면 방금 시작한 회피음에 에코를 붙이도록 짧게 재생을 기다린다.</summary>
    private void HandleDodgeChanged(PlayerDodgeEvent dodgeEvent)
    {
        if (dodgeEvent.Stage == PlayerDodgeStage.PerfectDodge || dodgeEvent.Stage == PlayerDodgeStage.IaiDodge)
            _dodgeEchoUntil = Time.unscaledTime + DODGE_ECHO_WAIT;
    }

    /// <summary>
    /// context의 실제 플레이어 피해에 접촉 위치의 피격음을 재생한다.
    /// 피해 수신 사건만 구독하므로 가드·패링에 피격음을 중복 재생하지 않는다.
    /// </summary>
    private void HandleDamage(DamageContext context)
    {
        Managers.Sound.Play(SoundId.SwordHit, context.Point,
            _volume * _damageVolumeMultiplier, gameObject, pitch: _damagePitch);
    }

    /// <summary>
    /// actionId의 베기 시작에 기존 Inspector의 공통 스윙음을 재생한다.
    /// 타수 Label을 전달하지 않아 FMOD 이벤트가 음원 변형을 선택한다.
    /// 동일 행동의 반복 알림을 무시하고 재생 ID를 기록한다.
    /// </summary>
    private void PlaySwing(int actionId)
    {
        if (_lastSwingAction == actionId)
            return;
        _lastSwingAction = actionId;
        Managers.Sound.PlayAttached(_swingSound, gameObject, _volume);
    }

    /// <summary>
    /// 로드된 씬의 EnemyHealth에 사운드 연결 컴포넌트가 없으면 런타임으로 추가한다.
    /// 일반 인간형 적과 투척수에는 새 사망음을 설정하고 Beast 전용 컴포넌트가 있는 적은 기존 사망음을 유지한다.
    /// </summary>
    private void RegisterEnemyAudio()
    {
        foreach (EnemyHealth health in Object.FindObjectsByType<EnemyHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (health.GetComponent<EnemyDamageAudio>() != null)
                continue;

            EnemyDamageAudio audio = health.gameObject.AddComponent<EnemyDamageAudio>();
            if (health.GetComponent<BeastBossAudio>() != null || health.GetComponent<DefenseBeastLeapAttack>() != null)
                continue;
            if (health.GetComponent<EnemyController>() != null || health.GetComponent<BambooThrower>() != null)
                audio.ConfigureDeathSound(SoundId.HumanEnemyDie);
        }
    }

    /// <summary>
    /// cue의 모든 활성 음원을 position·attach·actionId와 기준 시간으로 예약한다.
    /// presentationTime이면 베기 시작에서 앞뒤 조정하며 접촉·방어 사건은 양수 지연만 적용한다.
    /// 프로필이 존재하면 누락·비활성 상황도 처리된 것으로 반환하여 기존 재생을 억제한다.
    /// </summary>
    private bool QueueCue(CombatSoundCue cue, Vector3 position, bool attach, int actionId = -1,
        float baseTime = 0f, bool presentationTime = false)
    {
        if (_profile == null) return false;
        foreach (CombatSoundEntry entry in _profile.Entries)
        {
            if (entry.Cue != cue) continue;
            if (!entry.Enabled) continue;
            float dueTime = presentationTime ? Mathf.Max(0f, baseTime + entry.TimeOffset)
                : _cueClock + Mathf.Max(0f, entry.TimeOffset);
            _pendingCues.Add(new PendingCue(entry, position, attach, actionId, dueTime, presentationTime, _player.CurrentState));
        }
        return true;
    }

    /// <summary>
    /// 예약된 사운드의 행동 ID와 게임 시간을 검사하여 만료된 음원을 재생한다.
    /// 취소·사망 예약을 제거하고 일시정지 중에는 호출하지 않아 지연 시간을 유지한다.
    /// </summary>
    private void UpdatePendingCues()
    {
        for (int i = 0; i < _pendingCues.Count;)
        {
            PendingCue pending = _pendingCues[i];
            if ((_player.CurrentState == PlayerActionState.Dead && pending.Entry.Cue != CombatSoundCue.Damage)
                || (pending.ActionId >= 0 && (pending.ActionId != _player.CurrentActionId
                    || pending.ExpectedState != _player.CurrentState)))
            {
                _pendingCues.RemoveAt(i);
                continue;
            }
            float elapsed = pending.PresentationTime ? _timing.GetPresentationElapsed(pending.ActionId) : _cueClock;
            if (elapsed < pending.DueTime) { i++; continue; }
            Managers.Sound.PlayCombatCue(pending.Entry, gameObject, pending.Position, pending.Attach);
            _pendingCues.RemoveAt(i);
        }
    }

    private readonly struct PendingCue
    {
        private readonly CombatSoundEntry _entry;
        private readonly Vector3 _position;
        private readonly bool _attach;
        private readonly int _actionId;
        private readonly float _dueTime;
        private readonly bool _presentationTime;
        private readonly PlayerActionState _expectedState;

        internal CombatSoundEntry Entry => _entry;
        internal Vector3 Position => _position;
        internal bool Attach => _attach;
        internal int ActionId => _actionId;
        internal float DueTime => _dueTime;
        internal bool PresentationTime => _presentationTime;
        internal PlayerActionState ExpectedState => _expectedState;

        /// <summary>
        /// entry·position·attach·actionId·dueTime·presentationTime·expectedState를 하나의 예약으로 저장한다.
        /// 재생 위치와 취소 조건을 보존하며 게임 시스템의 상태는 변경하지 않는다.
        /// </summary>
        internal PendingCue(CombatSoundEntry entry, Vector3 position, bool attach, int actionId, float dueTime, bool presentationTime,
            PlayerActionState expectedState)
        {
            _entry = entry;
            _position = position;
            _attach = attach;
            _actionId = actionId;
            _dueTime = dueTime;
            _presentationTime = presentationTime;
            _expectedState = expectedState;
        }
    }
}
