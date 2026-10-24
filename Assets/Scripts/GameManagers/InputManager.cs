using System;

using UnityEngine;

using UnityEngine.InputSystem;

/// <summary>현재 플레이어의 실제 입력과 게임플레이 입력 허용 정책을 관리한다.</summary>
[DefaultExecutionOrder(-40)]
public sealed class InputManager : MonoBehaviour
{
    private const string DODGE_STAMINA_SOUND_PATH = "UI/StaminaInsufficient.wav";
    private const float DODGE_STAMINA_SOUND_VOLUME = 1.42f;

    [Header("커서")]
    [SerializeField] private bool _lockCursorOnClick = true;

    [Header("패드 시점")]
    [SerializeField, Min(1f)] private float _gamepadLookDegreesPerSecond = 180f;

    [Header("시작 시점")]
    [SerializeField, Min(0f), Tooltip("게임 시작과 커서 잠금 직후 시점 입력을 버리는 실시간(초). 잠금·씬 로드 순간 한꺼번에 들어오는 마우스 이동량으로 시점이 튀는 것을 막는다.")]
    private float _lookIgnoreAfterLock = 0.2f;
    private float _lookIgnoredUntil;
    private bool _viewResetPending;

    [Header("입력 상태")]
    private GameStateManager _state;
    private KatanaCombatCoordinator _combat;
    private PlayerMotor _motor;
    private PlayerEmpower _empower;
    private PlayerClock _playerClock;
    private PlayerDodgeStaminaHint _dodgeStaminaHint;
    private InputSystem_Actions _actions;
    private InputAction _move;
    private InputAction _look;
    private InputAction _attack;
    private InputAction _guard;
    private InputAction _dodge;
    private InputAction _swordWaveInput;
    private InputAction _ultimateInput;
    private InputAction _menu;
    private InputAction _worldInteract;
    private PlayerSkillGauge _skillGauge;
    private int _nextInputId;
    private int _guardInputId;
    private double _attackPressTime;
    private double _guardPressTime;
    private double _guardReleaseTime;
    private double _dodgePressTime;
    private double _swordWavePressTime;
    private double _ultimatePressTime;
    private int _bufferedAttackInputId;
    private int _bufferedAttackActionId;
    private double _bufferedAttackPressTime;
    private bool _ignoreAttackInputUntilRelease;

    public bool IsInitialized => _state != null;
    public bool IsGameplayInputEnabled { get; private set; }
    /// <summary>현재 프레임에 생성된 Attack 액션이 눌렸는지 반환한다.</summary>
    public bool AttackPressedThisFrame => _attack != null && _attack.WasPressedThisFrame();
    /// <summary>현재 프레임에 생성된 Guard 액션이 눌렸는지 반환한다.</summary>
    public bool GuardPressedThisFrame => _guard != null && _guard.WasPressedThisFrame();
    /// <summary>현재 프레임에 생성된 Dodge 액션이 눌렸는지 반환한다.</summary>
    public bool DodgePressedThisFrame => _dodge != null && _dodge.WasPressedThisFrame();
    /// <summary>현재 프레임에 생성된 WorldInteract 액션이 눌렸는지 반환한다.</summary>
    public bool WorldInteractPressedThisFrame => _worldInteract != null && _worldInteract.WasPressedThisFrame();
    public event Action<bool> GameplayInputEnabledChanged;

    void Awake()
    {
        _actions = new InputSystem_Actions();
        InputSystem_Actions.PlayerActions playerActions = _actions.Player;
        _move = playerActions.Move;
        _look = playerActions.Look;
        _attack = playerActions.Attack;
        _guard = playerActions.Guard;
        _dodge = playerActions.Dodge;
        _swordWaveInput = playerActions.SwordWave;
        _ultimateInput = playerActions.Ultimate;
        _menu = playerActions.Menu;
        _worldInteract = playerActions.WorldInteract;

        // 프레임 단위 폴링보다 정확한 장치 입력 시각을 기록용으로만 보관한다.
        _attack.performed += RecordAttackPress;
        _guard.performed += RecordGuardPress;
        _guard.canceled += RecordGuardRelease;
        _dodge.performed += RecordDodgePress;
        _swordWaveInput.performed += RecordSwordWavePress;
        _ultimateInput.performed += RecordUltimatePress;
    }

    void Update()
    {
        InputDeviceDisplay.PollButtons();
        PlayerController player = Game.Player;
        if (_menu.WasPressedThisFrame() && _state != null)
        {
            if (_state.State == GameplayState.Paused)
            {
                GameSettingsPopupUI.TryCloseForGameplay();
                return;
            }
            if (_state.State == GameplayState.Playing && IsGameplayInputEnabled
                && player != null && !player.IsPaused)
            {
                ClearHeldInput();
                if (!GameSettingsPopupUI.TryOpenForGameplay())
                    SetCursor(false);
                return;
            }
        }
        if (!IsGameplayInputEnabled || player == null || player.IsPaused) return;
        if (!Application.isFocused) return;
        if (_lockCursorOnClick && !InputDeviceDisplay.IsGamepad && Cursor.lockState != CursorLockMode.Locked)
        {
            if (_attack.WasPressedThisFrame() || _guard.WasPressedThisFrame()) SetCursor(true);
            return;
        }
        // 강화 공격 연출 중에는 이동·시점·가드·회피·공격 입력을 받지 않는다.
        if (_empower.IsStriking || _empower.IsAutomaticCharging || _empower.IsSwordWaveCasting)
        {
            player.SetMovementInput(Vector2.zero);
            return;
        }
        player.SetMovementInput(_move.ReadValue<Vector2>());
        Vector2 look = _look.ReadValue<Vector2>();
        // 시작·커서 잠금 직후의 튀는 시점 입력은 버린다.
        bool lookIgnored = Time.unscaledTime < _lookIgnoredUntil;
        if (!lookIgnored && _look.activeControl != null
            && _look.activeControl.device is Gamepad)
            _motor.AddGamepadLookInput(look, _gamepadLookDegreesPerSecond, _playerClock.DeltaTime);
        else if (!lookIgnored)
            player.AddLookInput(look);

        ResolveBufferedInputs(player);
        if (_ultimateInput.WasPressedThisFrame())
        {
            RequestSkill(true);
            return;
        }

        // 새 우클릭은 자유 상태에서 가드와 패링 창을 열고 공격 중에는 거부된다.
        // 계속 누르는 우클릭은 가드가 끝나 자유 상태가 되면 다시 가드와 패링 창을 연다.
        if (_guard.WasPressedThisFrame() && !player.IsGuarding)
        {
            _guardInputId = ++_nextInputId;
            RequestDefense(PlayerInputKind.GuardPress);
        }
        else if (_guard.IsPressed() && !player.IsGuarding
            && player.CurrentState == PlayerActionState.Free)
            RequestDefense(PlayerInputKind.GuardHoldResume);

        if (_guard.WasReleasedThisFrame())
        {
            _combat.EndDefense();
            PublishInput(_guardInputId, PlayerInputKind.GuardRelease, PlayerInputStatus.Started,
                PlayerInputRejectReason.None, 0, _guardReleaseTime);
        }
        if (_dodge.WasPressedThisFrame()) RequestDodge(player);
        bool ignoreAttackPress = _ignoreAttackInputUntilRelease;
        if (_ignoreAttackInputUntilRelease && !_attack.IsPressed())
            _ignoreAttackInputUntilRelease = false;

        if (_attack.WasPressedThisFrame() && !ignoreAttackPress)
            RequestAttack(player);
    }

    /// <summary>
    /// 현재 우클릭 _guardInputId로 방어를 요청하고 가드 시작 또는 거부 결과를 kind 입력 기록으로 발행한다.
    /// 거부 이유는 Coordinator의 LastRejectReason을 사용한다. 누르고 있는 입력의 재진입은 매 프레임 거부될 수 있으므로 가드가 실제로 시작된 경우에만 기록한다.
    /// </summary>
    private void RequestDefense(PlayerInputKind kind)
    {
        bool started = _combat.BeginDefense(_guardInputId);
        if (kind == PlayerInputKind.GuardHoldResume && !started) return;
        PublishInput(_guardInputId, kind, started ? PlayerInputStatus.Started : PlayerInputStatus.Rejected,
            started ? PlayerInputRejectReason.None : _combat.LastRejectReason, 0, _guardPressTime);
    }

    /// <summary>
    /// 패링 집중이면 게이지 소모 없는 일섬을, 그 외에는 player의 일반 공격을 새 InputId로 요청한다.
    /// 수락 결과를 입력 기록으로 발행한다.
    /// </summary>
    private void RequestAttack(PlayerController player)
    {
        int inputId = ++_nextInputId;
        if (_empower.IsSwordWaveReady && !_empower.IsSwordWaveFocusing && !_empower.IsCharging)
        {
            bool accepted = _empower.TryBeginParrySwordWave();
            PublishInput(inputId, PlayerInputKind.SwordWave,
                accepted ? PlayerInputStatus.Started : PlayerInputStatus.Rejected,
                accepted ? PlayerInputRejectReason.None : PlayerInputRejectReason.ActionBusy,
                accepted ? player.CurrentActionId : 0, _attackPressTime);
            return;
        }
        RequestNormalAttack(player, inputId, _attackPressTime);
    }

    /// <summary>
    /// 온보딩 설명에서 이어진 공격 누름을 일반 공격으로 전달한다.
    /// 현재 플레이어 입력을 사용하고 중복 누름은 버튼 해제까지 막는다.
    /// </summary>
    public void ApplyOnboardingAttackInput()
    {
        PlayerController player = Game.Player;
        if (!IsGameplayInputEnabled || player == null || player.IsPaused || !Application.isFocused) return;
        SetCursor(true);
        player.SetMovementInput(_move.ReadValue<Vector2>());
        _attackPressTime = Time.realtimeSinceStartupAsDouble;
        RequestAttack(player);
        _ignoreAttackInputUntilRelease = true;
    }

    /// <summary>온보딩 안내를 닫은 공격 버튼을 놓을 때까지 재활성화된 입력 액션이 다시 공격하지 않도록 잠근다.</summary>
    public void ConsumeOnboardingAttackUntilRelease()
    {
        _ignoreAttackInputUntilRelease = true;
    }

    /// <summary>
    /// inputId로 일반 공격을 요청하고 행동 ID 변화로 즉시 시작·예약·거부를 구분해 pressTime 시각의 입력 기록으로 발행한다.
    /// 즉시 시작된 경우에만 player의 새 행동 ID를, 거부된 경우에만 Coordinator의 거부 이유를 기록한다.
    /// 집중 중에는 대상 돌진 없는 제자리 공격만 요청하며 극한 회피 집중의 남은 시간은 유지한다.
    /// </summary>
    private void RequestNormalAttack(PlayerController player, int inputId, double pressTime)
    {
        int previousActionId = player.CurrentActionId;
        bool accepted = _empower.IsReady ? _combat.RequestInPlaceAttack() : _combat.RequestAttack();
        bool started = accepted && player.CurrentActionId != previousActionId;
        PlayerInputStatus status = started ? PlayerInputStatus.Started
            : accepted ? PlayerInputStatus.Buffered : PlayerInputStatus.Rejected;
        if (status == PlayerInputStatus.Buffered && _combat.HasBufferedAttack)
        {
            _bufferedAttackInputId = inputId;
            _bufferedAttackActionId = player.CurrentActionId;
            _bufferedAttackPressTime = pressTime;
        }
        PublishInput(inputId, PlayerInputKind.Attack, status,
            accepted ? PlayerInputRejectReason.None : _combat.LastRejectReason,
            started ? player.CurrentActionId : 0, pressTime);
    }

    /// <summary>ultimate로 Q 거합과 Shift 일섬을 구분해 한 번 요청한다. 실제 수락/잔량 부족/행동 불가를 고유 입력 ID로 기록한다.</summary>
    private void RequestSkill(bool ultimate)
    {
        int inputId = ++_nextInputId;
        bool enough = ultimate ? _skillGauge.CanUseUltimate : _skillGauge.CanUseSwordWave;
        bool accepted = ultimate ? _empower.TryBeginAutomaticCharge() : _empower.TryBeginSwordWave();
        PublishInput(inputId, ultimate ? PlayerInputKind.Ultimate : PlayerInputKind.SwordWave,
            accepted ? PlayerInputStatus.Started : PlayerInputStatus.Rejected,
            accepted ? PlayerInputRejectReason.None : enough ? PlayerInputRejectReason.ActionBusy
                : PlayerInputRejectReason.NotEnoughSkillGauge,
            accepted && !ultimate ? Game.Player.CurrentActionId : 0,
            ultimate ? _ultimatePressTime : _swordWavePressTime);
    }

    /// <summary>
    /// 예약된 공격 입력이 Coordinator에서 처리됐으면 같은 InputId로 Started 또는 BufferDropped 후속 기록을 발행한다.
    /// 예약 당시와 player의 행동 ID가 다르면 시작으로 보며 판정 상태는 바꾸지 않는다.
    /// </summary>
    private void ResolveBufferedInputs(PlayerController player)
    {
        if (_bufferedAttackInputId == 0 || _combat.HasBufferedAttack) return;
        bool started = player.CurrentActionId != _bufferedAttackActionId;
        PublishInput(_bufferedAttackInputId, PlayerInputKind.Attack,
            started ? PlayerInputStatus.Started : PlayerInputStatus.BufferDropped, PlayerInputRejectReason.None,
            started ? player.CurrentActionId : 0, _bufferedAttackPressTime);
        _bufferedAttackInputId = 0;
    }

    /// <summary>새 InputId로 player의 회피를 요청하고 결과를 기록한다. 기력 부족으로 거부된 입력에는 효과음과 하단 경고를 표시한다.</summary>
    private void RequestDodge(PlayerController player)
    {
        int inputId = ++_nextInputId;
        bool accepted = player.RequestDodge(out PlayerInputRejectReason reason);
        PublishInput(inputId, PlayerInputKind.Dodge,
            accepted ? PlayerInputStatus.Started : PlayerInputStatus.Rejected, reason, 0, _dodgePressTime);
        if (!accepted && reason == PlayerInputRejectReason.NotEnoughStamina)
        {
            Managers.Sound.PlayStreamingEffect(DODGE_STAMINA_SOUND_PATH, DODGE_STAMINA_SOUND_VOLUME,
                owner: player.gameObject);
            _dodgeStaminaHint.Show();
        }
    }

    /// <summary>
    /// inputId·kind·status·rejectReason·actionId와 장치 입력 시각 deviceTime, 현재 프레임의 실시간·게임 시간으로 입력 기록을 만든다.
    /// 만든 기록을 Coordinator의 InputResolved 사건으로 발행하며 입력 처리 결과는 바꾸지 않는다.
    /// </summary>
    private void PublishInput(int inputId, PlayerInputKind kind, PlayerInputStatus status,
        PlayerInputRejectReason rejectReason, int actionId, double deviceTime)
    {
        _combat.Events.RaiseInputResolved(new PlayerInputRecord(inputId, kind, status, rejectReason, actionId,
            deviceTime, Time.realtimeSinceStartupAsDouble, Time.time));
    }

    /// <summary>context의 장치 입력 시각을 마지막 공격 누름 시각으로 저장한다.</summary>
    private void RecordAttackPress(InputAction.CallbackContext context) => _attackPressTime = context.time;

    /// <summary>context의 장치 입력 시각을 마지막 우클릭 누름 시각으로 저장한다.</summary>
    private void RecordGuardPress(InputAction.CallbackContext context) => _guardPressTime = context.time;

    /// <summary>context의 장치 입력 시각을 마지막 우클릭 뗌 시각으로 저장한다.</summary>
    private void RecordGuardRelease(InputAction.CallbackContext context) => _guardReleaseTime = context.time;

    /// <summary>context의 장치 입력 시각을 마지막 회피 누름 시각으로 저장한다.</summary>
    private void RecordDodgePress(InputAction.CallbackContext context) => _dodgePressTime = context.time;

    /// <summary>context에 기록된 실제 Shift 또는 RT 누름 시각을 일섬 입력 로그용 상태에 저장한다.</summary>
    private void RecordSwordWavePress(InputAction.CallbackContext context) => _swordWavePressTime = context.time;

    /// <summary>context에 기록된 실제 Q 또는 Y 누름 시각을 거합 입력 로그용 상태에 저장한다.</summary>
    private void RecordUltimatePress(InputAction.CallbackContext context) => _ultimatePressTime = context.time;

    void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        ClearHeldInput();
        SetCursor(false);
    }

    void OnDisable()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
        if (_actions != null) _actions.Dispose();
    }

    /// <summary>state와 Game.Player의 장치를 연결한다. 초기 입력은 차단하고 Playing 전환을 기다린다.</summary>
    public void Initialize(GameStateManager state)
    {
        Shutdown();
        _state = state;
        _combat = Game.Player.GetComponent<KatanaCombatCoordinator>();
        _motor = Game.Player.GetComponent<PlayerMotor>();
        _empower = Game.Player.GetComponent<PlayerEmpower>();
        _skillGauge = Game.Player.GetComponent<PlayerSkillGauge>();
        _playerClock = Game.Player.GetComponent<PlayerClock>();
        _dodgeStaminaHint = Game.Player.GetComponent<PlayerDodgeStaminaHint>();
        _viewResetPending = true;
        _state.StateChanged += HandleState;
        _actions.Player.Enable();
    }

    /// <summary>state가 Playing이면 입력을 허용하고 그 외에는 명령 전달과 유지 입력을 차단한다.</summary>
    private void HandleState(GameplayState state)
    {
        SetGameplayInputEnabled(state == GameplayState.Playing);
    }

    /// <summary>
    /// allowed와 게임 상태로 입력 허용 여부를 결정한다. 차단 시 유지 입력을 정리하고 활성화 시 커서를 잠근 뒤 정책 변경을 알린다.
    /// 세션을 연결한 뒤 처음 활성화되면 시점을 스폰 정면으로 되돌리고 잠깐 시점 입력을 버린다.
    /// </summary>
    public void SetGameplayInputEnabled(bool allowed)
    {
        bool enabled = allowed && _state != null && _state.State == GameplayState.Playing;
        if (!enabled) ClearHeldInput();
        if (IsGameplayInputEnabled == enabled) return;
        IsGameplayInputEnabled = enabled;
        if (enabled && _viewResetPending)
        {
            _viewResetPending = false;
            _motor.ResetView();
            _lookIgnoredUntil = Time.unscaledTime + _lookIgnoreAfterLock;
        }
        if (enabled) SetCursor(true);
        GameplayInputEnabledChanged?.Invoke(enabled);
    }

    /// <summary>Game.Player의 이동·가드·차지·패링 시도와 궁극기 차징을 정리하고 모터 입력을 초기화한다.</summary>
    private void ClearHeldInput()
    {
        _ignoreAttackInputUntilRelease = false;
        PlayerController player = Game.Player;
        if (player == null) return;
        player.SetMovementInput(Vector2.zero);
        player.EndGuard();
        player.EndCharge();
        if (_combat != null) _combat.CancelParryAttempt();
        if (_motor != null) _motor.ClearInput();
        if (_empower != null && !_empower.IsAutomaticCharging) _empower.CancelCharge();
    }

    /// <summary>
    /// locked와 Inspector 설정으로 커서 잠금·표시 상태를 변경한다. 잠금 관리가 꺼져 있으면 상태를 유지한다.
    /// 풀린 커서를 새로 잠그면 _lookIgnoreAfterLock 동안 시점 입력을 버리도록 _lookIgnoredUntil을 늘린다.
    /// </summary>
    private void SetCursor(bool locked)
    {
        if (!_lockCursorOnClick) return;
        if (locked && Cursor.lockState != CursorLockMode.Locked)
            _lookIgnoredUntil = Mathf.Max(_lookIgnoredUntil, Time.unscaledTime + _lookIgnoreAfterLock);
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>
    /// 튜토리얼 재개에 사용한 command를 복구된 플레이어에 한 번 전달한다. 회피 방향은 현재 이동 입력을 사용한다.
    /// 웅크리기 입력은 게임에서 제외했으므로 Crouch 재개는 전달하지 않는다.
    /// </summary>
    public void ApplyTutorialInput(TutorialResumeInput command)
    {
        PlayerController player = Game.Player;
        if (!IsGameplayInputEnabled || player == null || player.IsPaused || !Application.isFocused) return;
        SetCursor(true);
        player.SetMovementInput(_move.ReadValue<Vector2>());
        switch (command)
        {
            case TutorialResumeInput.Attack:
                _combat.RequestAttack();
                break;
            case TutorialResumeInput.Defense:
                _combat.BeginDefense();
                break;
            case TutorialResumeInput.Dodge:
                player.RequestDodge();
                break;
        }
    }

    /// <summary>입력 액션과 상태 구독·플레이어 장치 참조를 해제한다. 매니저는 다음 씬에서 재사용한다.</summary>
    public void Shutdown()
    {
        SetGameplayInputEnabled(false);
        if (_empower != null) _empower.CancelCharge();
        if (_state != null) _state.StateChanged -= HandleState;
        _state = null;
        _combat = null;
        _motor = null;
        _empower = null;
        _skillGauge = null;
        _playerClock = null;
        if (_dodgeStaminaHint != null) _dodgeStaminaHint.Hide();
        _dodgeStaminaHint = null;
        // 매니저는 다음 씬에서도 재사용되므로 이전 플레이어의 예약 입력 추적을 남기지 않는다.
        _bufferedAttackInputId = 0;
        if (_actions != null) _actions.Player.Disable();
        SetCursor(false);
    }
}
