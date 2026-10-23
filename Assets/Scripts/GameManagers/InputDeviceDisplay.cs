using System;

using UnityEngine;

using UnityEngine.InputSystem;

/// <summary>마지막 실제 입력 장치를 기록하고 키보드 안내를 Xbox 형식 패드 버튼으로 표시한다.</summary>
public static class InputDeviceDisplay
{
    public static bool IsGamepad { get; private set; }
    public static event Action<bool> DeviceChanged;

    public static bool InteractPressed =>
        (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);

    public static bool CancelPressed =>
        (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        IsGamepad = false;
        DeviceChanged = null;
        InputSystem.onActionChange -= HandleActionChange;
        InputSystem.onActionChange += HandleActionChange;
    }

    /// <summary>액션의 실제 입력 장치를 확인하며 작은 스틱 흔들림과 마우스 잡음을 제외하고 표시 장치를 갱신한다.</summary>
    private static void HandleActionChange(object source, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed || !(source is InputAction action)
            || action.activeControl == null) return;
        InputDevice device = action.activeControl.device;
        object value = action.ReadValueAsObject();
        if (value is Vector2 vector && vector.sqrMagnitude < (device is Gamepad ? 0.0625f : 0.25f)) return;
        if (value is float scalar && scalar < 0.5f) return;
        if (device is Gamepad) SetDevice(true);
        else if (device is Keyboard || device is Mouse) SetDevice(false);
    }

    /// <summary>액션 외부의 키·마우스 클릭·패드 상호작용 및 Menu 입력을 기록한다. 중립 상태와 버튼 해제는 장치를 바꾸지 않는다.</summary>
    public static void PollButtons()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if ((keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.middleButton.wasPressedThisFrame)))
            SetDevice(false);
        Gamepad pad = Gamepad.current;
        if (pad != null && (pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame
            || pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
            || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
            || pad.buttonNorth.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame))
            SetDevice(true);
        if (IsGamepad && pad == null) SetDevice(false);
    }

    /// <summary>패드 사용 여부가 바뀔 때만 상태를 저장하고 DeviceChanged를 발행한다.</summary>
    private static void SetDevice(bool gamepad)
    {
        if (IsGamepad == gamepad) return;
        IsGamepad = gamepad;
        DeviceChanged?.Invoke(gamepad);
    }

    /// <summary>키보드 원문을 보존하며 패드 사용 중에는 별도 문구 또는 대응 버튼을 반환한다. 빈 문구는 그대로 유지한다.</summary>
    public static string FormatKeys(string keyboardText, string gamepadText = null)
    {
        if (!IsGamepad || string.IsNullOrEmpty(keyboardText)) return keyboardText;
        if (!string.IsNullOrEmpty(gamepadText)) return gamepadText;
        return ConvertKeys(keyboardText);
    }

    /// <summary>표시 원문의 단축키 부분만 패드 표기로 바꿔 반환한다. F 단독 문구는 X로 표시한다.</summary>
    private static string ConvertKeys(string text)
    {
        if (text == "F") return "X";
        return text.Replace("CTRL / C", "B").Replace("Ctrl / C", "B")
            .Replace("좌클릭", "RB").Replace("우클릭", "LB")
            .Replace("SPACE", "A").Replace("WASD", "왼쪽 스틱")
            .Replace("MOUSE", "오른쪽 스틱").Replace("[F]", "[X]")
            .Replace("CTRL", "B").Replace("Ctrl", "B").Replace("ESC", "Menu");
    }
}
