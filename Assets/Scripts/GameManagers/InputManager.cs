using System;

using UnityEngine;

using UnityEngine.InputSystem;

/// <summary>현재 플레이어의 실제 입력과 게임플레이 입력 허용 정책을 관리한다.</summary>
[DefaultExecutionOrder(-40)]
public sealed class InputManager : MonoBehaviour
{
    [Header("커서")]
    [SerializeField] private bool _lockCursorOnClick = true;

    [Header("패드 시점")]
    [SerializeField, Min(1f)] private float _gamepadLookDegreesPerSecond = 180f;

    [Header("입력 상태")]
    private GameStateManager _state;
    private KatanaCombatCoordinator _combat;
    private PlayerMotor _motor;
    private PlayerEmpower _empower;
    private PlayerClock _playerClock;
    private InputSystem_Actions _actions;
    private InputAction _guard;
    private InputAction _dodge;
    private int _nextInputId;
    private int _guardInputId;
    private double _attackPressTime;
    private double _guardPressTime;
    private double _guardReleaseTime;
    private double _dodgePressTime;
    private int _bufferedAttackInputId;
    private int _bufferedAttackActionId;
    private double _bufferedAttackPressTime;

    public bool IsInitialized => _state != null;
    public bool IsGameplayInputEnabled { get; private set; }
    public event Action<bool> GameplayInputEnabledChanged;

    void Awake()
    {
        _actions = new InputSystem_Actions();
        _guard = new InputAction("Guard", InputActionType.Button, "<Mouse>/rightButton");
        _dodge = new InputAction("Dodge", InputActionType.Button, "<Keyboard>/space");
        _guard.AddBinding("<Gamepad>/leftShoulder");
        _dodge.AddBinding("<Gamepad>/buttonSouth");
        InputAction attack = _actions.Player.Attack;
        for (int i = 0; i < attack.bindings.Count; i++)
            if (attack.bindings[i].path.StartsWith("<Gamepad>"))
                attack.ApplyBindingOverride(i, "<Gamepad>/rightShoulder");

        // 프레임 단위 폴링보다 정확한 장치 입력 시각을 기록용으로만 보관한다.
        attack.performed += RecordAttackPress;
        _guard.performed += RecordGuardPress;
        _guard.canceled += RecordGuardRelease;
        _dodge.performed += RecordDodgePress;
    }

    void Update()
    {
        InputDeviceDisplay.PollButtons();
        PlayerController player = Game.Player;
        if (!IsGameplayInputEnabled || player == null || player.IsPaused) return;
        if (InputDeviceDisplay.CancelPressed)
        {
            ClearHeldInput();
            SetCursor(false);
            return;
        }
        if (!Application.isFocused) return;
        if (_lockCursorOnClick && !InputDeviceDisplay.IsGamepad && Cursor.lockState != CursorLockMode.Locked)
        {
            if (_actions.Player.Attack.WasPressedThisFrame() || _guard.WasPressedThisFrame()) SetCursor(true);
            return;
        }
        // 강화 공격 연출 중에는 이동·시점·가드·회피·공격 입력을 받지 않는다.
        if (_empower.IsStriking)
        {
            player.SetMovementInput(Vector2.zero);
            return;
        }
        player.SetMovementInput(_actions.Player.Move.ReadValue<Vector2>());
        Vector2 look = _actions.Player.Look.ReadValue<Vector2>();
        if (_actions.Player.Look.activeControl != null && _actions.Player.Look.activeControl.device is Gamepad)
            _motor.AddGamepadLookInput(look, _gamepadLookDegreesPerSecond, _playerClock.DeltaTime);
        else
            player.AddLookInput(look);

        ResolveBufferedInputs(player);

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
        if (_actions.Player.Attack.WasPressedThisFrame()) RequestAttack(player);
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
    /// 새 InputId로 공격을 요청하고 행동 ID 변화로 즉시 시작·예약·거부를 구분해 입력 기록으로 발행한다.
    /// 즉시 시작된 경우에만 player의 새 행동 ID를, 거부된 경우에만 Coordinator의 거부 이유를 기록한다.
    /// 강화 준비 중이면 일반 공격 대신 강화 공격을 시작하고 그 결과를 기록한다.
    /// </summary>
    private void RequestAttack(PlayerController player)
    {
        int inputId = ++_nextInputId;
        if (_empower.IsReady)
        {
            bool struck = _empower.TryBeginStrike();
            PublishInput(inputId, PlayerInputKind.Attack,
                struck ? PlayerInputStatus.Started : PlayerInputStatus.Rejected,
                struck ? PlayerInputRejectReason.None : PlayerInputRejectReason.Unavailable, 0, _attackPressTime);
            return;
        }
        int previousActionId = player.CurrentActionId;
        bool accepted = _combat.RequestAttack();
        bool started = accepted && player.CurrentActionId != previousActionId;
        PlayerInputStatus status = started ? PlayerInputStatus.Started
            : accepted ? PlayerInputStatus.Buffered : PlayerInputStatus.Rejected;
        if (status == PlayerInputStatus.Buffered && _combat.HasBufferedAttack)
        {
            _bufferedAttackInputId = inputId;
            _bufferedAttackActionId = player.CurrentActionId;
            _bufferedAttackPressTime = _attackPressTime;
        }
        PublishInput(inputId, PlayerInputKind.Attack, status,
            accepted ? PlayerInputRejectReason.None : _combat.LastRejectReason,
            started ? player.CurrentActionId : 0, _attackPressTime);
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

    /// <summary>새 InputId로 player의 회피를 요청하고 시작 또는 거부 결과와 거부 이유를 입력 기록으로 발행한다.</summary>
    private void RequestDodge(PlayerController player)
    {
        int inputId = ++_nextInputId;
        bool accepted = player.RequestDodge(out PlayerInputRejectReason reason);
        PublishInput(inputId, PlayerInputKind.Dodge,
            accepted ? PlayerInputStatus.Started : PlayerInputStatus.Rejected, reason, 0, _dodgePressTime);
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
        _actions.Dispose();
        _guard.Dispose();
        _dodge.Dispose();
    }

    /// <summary>state와 Game.Player의 장치를 연결한다. 초기 입력은 차단하고 Playing 전환을 기다린다.</summary>
    public void Initialize(GameStateManager state)
    {
        Shutdown();
        _state = state;
        _combat = Game.Player.GetComponent<KatanaCombatCoordinator>();
        _motor = Game.Player.GetComponent<PlayerMotor>();
        _empower = Game.Player.GetComponent<PlayerEmpower>();
        _playerClock = Game.Player.GetComponent<PlayerClock>();
        _state.StateChanged += HandleState;
        _actions.Player.Enable();
        _guard.Enable();
        _dodge.Enable();
    }

    /// <summary>state가 Playing이면 입력을 허용하고 그 외에는 명령 전달과 유지 입력을 차단한다.</summary>
    private void HandleState(GameplayState state)
    {
        SetGameplayInputEnabled(state == GameplayState.Playing);
    }

    /// <summary>allowed와 게임 상태로 입력 허용 여부를 결정한다. 차단 시 유지 입력을 정리하고 활성화 시 커서를 잠근 뒤 정책 변경을 알린다.</summary>
    public void SetGameplayInputEnabled(bool allowed)
    {
        bool enabled = allowed && _state != null && _state.State == GameplayState.Playing;
        if (!enabled) ClearHeldInput();
        if (IsGameplayInputEnabled == enabled) return;
        IsGameplayInputEnabled = enabled;
        if (enabled) SetCursor(true);
        GameplayInputEnabledChanged?.Invoke(enabled);
    }

    /// <summary>Game.Player의 이동·가드·차지·패링 시도를 정리하고 모터 입력을 초기화한다.</summary>
    private void ClearHeldInput()
    {
        PlayerController player = Game.Player;
        if (player == null) return;
        player.SetMovementInput(Vector2.zero);
        player.EndGuard();
        player.EndCharge();
        if (_combat != null) _combat.CancelParryAttempt();
        if (_motor != null) _motor.ClearInput();
    }

    /// <summary>locked와 Inspector 설정으로 커서 잠금·표시 상태를 변경한다. 잠금 관리가 꺼져 있으면 상태를 유지한다.</summary>
    private void SetCursor(bool locked)
    {
        if (!_lockCursorOnClick) return;
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
        player.SetMovementInput(_actions.Player.Move.ReadValue<Vector2>());
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
        if (_state != null) _state.StateChanged -= HandleState;
        _state = null;
        _combat = null;
        _motor = null;
        _empower = null;
        _playerClock = null;
        // 매니저는 다음 씬에서도 재사용되므로 이전 플레이어의 예약 입력 추적을 남기지 않는다.
        _bufferedAttackInputId = 0;
        if (_actions != null) _actions.Player.Disable();
        if (_guard != null) _guard.Disable();
        if (_dodge != null) _dodge.Disable();
        SetCursor(false);
    }
}
