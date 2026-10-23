using UnityEngine;

using TMPro;

/// <summary>
/// PlayerSkillGauge의 거합 게이지를 BambooDefenseHud 프리팹의 원형 HUD로 표시한다.
/// 직렬화된 Fill, 백분율, 입력 안내를 갱신하고 HUD 표시 상태를 관리한다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class PlayerUltimateHint : MonoBehaviour
{
    [Header("프리팹 UI 연결")]
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private UnityEngine.UI.Image _fill;
    [SerializeField] private TextMeshProUGUI _valueText;
    [SerializeField] private TextMeshProUGUI _labelText;

    [Header("게이지 색상")]
    [SerializeField] private Color _fillColor = new Color(0.86f, 0.1f, 0.1f, 0.92f);
    [SerializeField] private Color _readyColor = new Color(1f, 0.78f, 0.28f, 1f);
    [SerializeField] private Color _valueColor = new Color(0.98f, 0.97f, 0.93f, 1f);

    [Header("런타임")]
    private PlayerSkillGauge _skillGauge;

    void OnEnable()
    {
        InputDeviceDisplay.DeviceChanged += HandleDeviceChanged;
        RefreshLabel(InputDeviceDisplay.IsGamepad);
        _group.alpha = 0f;
        TryBindSkillGauge();
    }

    void Update()
    {
        TryBindSkillGauge();
    }

    void OnDisable()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        UnsubscribeGauge();
        _group.alpha = 0f;
    }

    /// <summary>
    /// 현재 플레이어의 PlayerSkillGauge를 찾아 변경 이벤트를 구독한다.
    /// 게임 매니저나 플레이어가 준비되지 않았으면 다음 프레임에 다시 찾는다.
    /// </summary>
    private void TryBindSkillGauge()
    {
        if (_skillGauge != null || !Game.IsInitialized)
            return;

        PlayerController player = Game.Player;
        if (player == null)
            return;

        _skillGauge = player.GetComponent<PlayerSkillGauge>();
        if (_skillGauge == null)
            return;

        _skillGauge.Changed += HandleGaugeChanged;
        _group.alpha = 1f;
        RefreshGauge();
    }

    /// <summary>
    /// 이 HUD가 구독한 게이지 변경 이벤트를 해제하고 원본 참조를 비운다.
    /// 비활성화 후 재활성화되면 현재 플레이어에서 다시 찾는다.
    /// </summary>
    private void UnsubscribeGauge()
    {
        if (_skillGauge != null)
            _skillGauge.Changed -= HandleGaugeChanged;
        _skillGauge = null;
    }

    /// <summary>
    /// 입력 장치가 바뀌면 화면 아래 안내를 Q:거합 또는 Y:거합으로 갱신한다.
    /// gamepad 값에 따라 바뀐 안내 문구를 _labelText에 저장한다.
    /// </summary>
    private void HandleDeviceChanged(bool gamepad)
    {
        RefreshLabel(gamepad);
    }

    /// <summary>
    /// 거합 게이지가 바뀌면 Fill과 정수 백분율을 최신 값으로 다시 표시한다.
    /// Changed 입력을 사용하며 HUD Image와 TextMeshProUGUI의 표시 상태를 변경한다.
    /// </summary>
    private void HandleGaugeChanged()
    {
        RefreshGauge();
    }

    /// <summary>
    /// 거합 게이지 양을 아래에서 위로 채우고 정수 백분율을 표시한다.
    /// UltimateNormalized와 CanUseUltimate를 사용하며, 실제 준비 상태에서만 100%를 보인다.
    /// </summary>
    private void RefreshGauge()
    {
        bool ready = _skillGauge.CanUseUltimate;
        float normalizedGauge = _skillGauge.UltimateNormalized;
        _fill.fillAmount = ready ? 1f : normalizedGauge;
        _fill.color = ready ? _readyColor : _fillColor;
        _valueText.color = ready ? _readyColor : _valueColor;
        int percent = ready ? 100 : Mathf.Clamp(Mathf.FloorToInt(normalizedGauge * 100f), 0, 99);
        _valueText.text = percent + "%";
    }

    /// <summary>
    /// 현재 입력 장치에 맞는 키보드 Q 또는 게임패드 Y 안내를 게이지 아래쪽에 표시한다.
    /// gamepad 값에 따라 _labelText의 문구를 변경한다.
    /// </summary>
    private void RefreshLabel(bool gamepad)
    {
        _labelText.text = gamepad ? "Y:거합" : "Q:거합";
    }
}
