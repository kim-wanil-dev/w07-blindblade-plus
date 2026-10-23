using UnityEngine;

using TMPro;

using BlindBlade.UI.CombatHud;

/// <summary>
/// PlayerSkillGauge의 거합 게이지를 BambooDefenseHud 프리팹의 원형 실 액체 HUD로 표시한다.
/// 프리팹의 CanvasGroup·TMP·실 액체 구성요소를 사용하며, 거합 차징과 시전 중 HUD를 숨긴다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class PlayerUltimateHint : MonoBehaviour
{
    [Header("프리팹 UI 연결")]
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private RectTransform _gaugeRect;
    [SerializeField] private TextMeshProUGUI _valueText;
    [SerializeField] private TextMeshProUGUI _labelText;
    [SerializeField] private CombatThreadLiquidBody _liquidBody;
    [SerializeField] private CombatThreadLiquidGauge _liquid;

    [Header("표시")]
    [SerializeField, Range(0f, 1f), Tooltip("백분율 글자에 두르는 외곽선 두께. 0이면 외곽선이 없다.")]
    private float _valueOutlineWidth = 0.18f;
    [SerializeField, Min(0.01f), Tooltip("거합 차징·시전이 시작되거나 끝날 때 HUD 전체가 사라지고 나타나는 실시간(초)")]
    private float _hideFadeDuration = 0.15f;

    [Header("실 액체")]
    [SerializeField, Tooltip("실 구체가 겹친 수를 텍스처에 더하는 BlindBlade/ThreadLiquidDensity 재질")]
    private Material _liquidDensityMaterial;
    [SerializeField, Tooltip("겹침 수를 단계별 색으로 바꾸고 수면 위를 잘라 내는 BlindBlade/UIThreadLiquid 재질")]
    private Material _liquidCompositeMaterial;
    [SerializeField, Tooltip("원 안을 아래에서 위로 차오르는 실 액체와 가득 찼을 때 테두리를 감는 실의 모양·흐름·겹침 단계 색")]
    private CombatThreadLiquidStyle _liquidStyle = new CombatThreadLiquidStyle();

    [Header("런타임")]
    private PlayerEmpower _empower;
    private PlayerSkillGauge _skillGauge;

    public RectTransform GaugeRect => _gaugeRect;

    /// <summary>온보딩 하단 조작판 위에 원형 거합 게이지를 배치한다.</summary>
    public void SetOnboardingLayout()
    {
        _gaugeRect.anchoredPosition = new Vector2(0f, 210f);
    }

    void Awake()
    {
        _liquidBody.Initialize(_liquidStyle, _liquidDensityMaterial, _liquidCompositeMaterial);
        _liquid.Initialize(_liquidStyle, _liquidBody);
        _valueText.color = Color.black;
        _valueText.outlineWidth = _valueOutlineWidth;
        _valueText.outlineColor = Color.white;
    }

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
        if (_skillGauge == null)
            return;

        bool hidden = _empower.IsAutomaticCharging || _empower.IsStriking;
        _group.alpha = Mathf.MoveTowards(_group.alpha, hidden ? 0f : 1f, Time.unscaledDeltaTime / _hideFadeDuration);

        bool ready = _skillGauge.CanUseUltimate;
        _liquid.SetState(ready ? 1f : _skillGauge.UltimateNormalized, ready, Time.unscaledDeltaTime);
    }

    void OnDisable()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        UnsubscribeGauge();
        _group.alpha = 0f;
    }

    /// <summary>
    /// 현재 플레이어의 PlayerEmpower와 PlayerSkillGauge를 찾아 거합 게이지 변경을 구독한다.
    /// 게임 매니저나 플레이어가 준비되지 않았으면 다음 프레임에 다시 찾는다.
    /// </summary>
    private void TryBindSkillGauge()
    {
        if (_skillGauge != null || !Game.IsInitialized)
            return;

        PlayerController player = Game.Player;
        if (player == null)
            return;

        _empower = player.GetComponent<PlayerEmpower>();
        _skillGauge = player.GetComponent<PlayerSkillGauge>();
        if (_empower == null || _skillGauge == null)
            return;

        _skillGauge.Changed += HandleGaugeChanged;
        RefreshGauge();
    }

    /// <summary>
    /// 이 HUD가 구독한 게이지 변경 이벤트를 해제하고 플레이어 참조를 비운다.
    /// 비활성화 후 재활성화되면 현재 플레이어에서 다시 찾는다.
    /// </summary>
    private void UnsubscribeGauge()
    {
        if (_skillGauge != null)
            _skillGauge.Changed -= HandleGaugeChanged;
        _empower = null;
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
    /// 거합 게이지가 바뀌면 백분율 텍스트를 최신 값으로 갱신한다.
    /// Changed 입력을 사용하며 TextMeshProUGUI의 표시 문자열을 변경한다.
    /// </summary>
    private void HandleGaugeChanged()
    {
        RefreshGauge();
    }

    /// <summary>
    /// 거합 게이지의 내림 정수 백분율을 표시하고 준비 상태면 100%로 표시한다.
    /// UltimateNormalized와 CanUseUltimate를 사용하며 _valueText.text를 변경한다.
    /// </summary>
    private void RefreshGauge()
    {
        bool ready = _skillGauge.CanUseUltimate;
        float normalizedGauge = _skillGauge.UltimateNormalized;
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
