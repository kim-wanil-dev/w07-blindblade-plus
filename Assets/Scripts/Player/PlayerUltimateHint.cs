using UnityEngine;
using UnityEngine.UI;

using TMPro;

/// <summary>본게임의 일섬 준비, 거합 게이지 완충 및 거합 사용 가능 상태에 맞는 입력 안내를 표시한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEmpower))]
public sealed class PlayerUltimateHint : MonoBehaviour
{
    private enum HintMode { Hidden, SwordWave, PrepareUltimate, Ultimate }

    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const int CANVAS_SORTING_ORDER = 101;

    [Header("표시")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField, TextArea(1, 2)] private string _keyboardHint = "좌클릭 홀드 - 거합";
    [SerializeField, TextArea(1, 2)] private string _gamepadHint = "RB 홀드 - 거합";
    [SerializeField, TextArea(1, 2)] private string _keyboardSwordWaveHint = "좌클릭 - 일섬";
    [SerializeField, TextArea(1, 2)] private string _gamepadSwordWaveHint = "RB - 일섬";
    [SerializeField, TextArea(1, 2)] private string _keyboardPrepareHint = "거합 게이지 3칸 - 극한 회피 또는 쳐내기로 집중";
    [SerializeField, TextArea(1, 2)] private string _gamepadPrepareHint = "거합 게이지 3칸 - 극한 회피 또는 쳐내기로 집중";
    [SerializeField, Min(0.01f), Tooltip("안내가 나타나고 사라지는 실시간(초)")]
    private float _fadeDuration = 0.15f;
    private PlayerEmpower _empower;
    private CanvasGroup _group;
    private Image _panel;
    private TextMeshProUGUI _text;
    private int _lastScreenWidth;
    private int _lastScreenHeight;
    private HintMode _hintMode;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
        BuildCanvas();
    }

    void OnEnable()
    {
        InputDeviceDisplay.DeviceChanged += HandleDeviceChanged;
        RefreshText();
    }

    void OnDisable()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        _group.alpha = 0f;
    }

    void Update()
    {
        if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height)
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            ApplyResponsiveLayout();
        }

        // 궁극기 차징은 집중 중이고 기운이 가득 찼을 때만 받으므로 같은 조건에서만 보인다.
        HintMode nextMode = HintMode.Hidden;
        if (Time.timeScale > 0f)
        {
            if (_empower.IsReady && _empower.Tokens >= _empower.MaxTokens)
                nextMode = HintMode.Ultimate;
            else if (_empower.Tokens >= _empower.MaxTokens)
                nextMode = HintMode.PrepareUltimate;
            else if (_empower.IsSwordWaveReady)
                nextMode = HintMode.SwordWave;
        }
        if (_hintMode != nextMode)
        {
            _hintMode = nextMode;
            RefreshText();
        }
        _group.alpha = Mathf.MoveTowards(_group.alpha, nextMode != HintMode.Hidden ? 1f : 0f,
            Time.unscaledDeltaTime / _fadeDuration);
    }

    /// <summary>
    /// 온보딩 하단 안내와 같은 크기·색의 화면 하단 안내 Canvas를 만든다.
    /// 처음에는 투명하게 두고 _group·_panel·_text에 저장한다.
    /// </summary>
    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("UltimateHintCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 집중 화면 연출(EmpowerOverlay)보다 위에 그린다.
        canvas.sortingOrder = CANVAS_SORTING_ORDER;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        GameObject panelObject = new GameObject("UltimateHintPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        _panel = panelObject.GetComponent<Image>();
        _panel.color = new Color(0.015f, 0.02f, 0.025f, 0.78f);
        _panel.raycastTarget = false;
        RectTransform panelRect = _panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 62f);
        panelRect.sizeDelta = new Vector2(760f, 104f);

        GameObject textObject = new GameObject("UltimateHint", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        _text = textObject.GetComponent<TextMeshProUGUI>();
        _text.font = _font;
        _text.fontSize = 34f;
        _text.color = new Color(0.96f, 0.95f, 0.91f, 1f);
        _text.alignment = TextAlignmentOptions.Midline;
        _text.enableAutoSizing = true;
        _text.fontSizeMin = 34f * 0.55f;
        _text.fontSizeMax = 34f;
        _text.textWrappingMode = TextWrappingModes.NoWrap;
        _text.raycastTarget = false;
        RectTransform textRect = _text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(26f, 8f);
        textRect.offsetMax = new Vector2(-26f, -8f);
    }

    /// <summary>좁은 화면에서는 온보딩 하단 안내와 같은 규칙으로 패널 폭과 높이를 줄인다.</summary>
    private void ApplyResponsiveLayout()
    {
        bool compact = (float)Screen.width / Mathf.Max(1, Screen.height) < 1.5f;
        _panel.rectTransform.sizeDelta = compact ? new Vector2(640f, 104f) : new Vector2(760f, 104f);
        _panel.rectTransform.anchoredPosition = new Vector2(0f, compact ? 42f : 62f);
    }

    /// <summary>gamepad 사용 여부가 바뀌면 안내 문구를 다시 고른다.</summary>
    private void HandleDeviceChanged(bool gamepad)
    {
        RefreshText();
    }

    /// <summary>현재 준비된 일섬·거합 행동과 입력 장치에 맞는 조작 문구를 _text에 표시한다.</summary>
    private void RefreshText()
    {
        bool gamepad = InputDeviceDisplay.IsGamepad;
        switch (_hintMode)
        {
            case HintMode.SwordWave:
                _text.text = gamepad ? _gamepadSwordWaveHint : _keyboardSwordWaveHint;
                break;
            case HintMode.PrepareUltimate:
                _text.text = gamepad ? _gamepadPrepareHint : _keyboardPrepareHint;
                break;
            default:
                _text.text = gamepad && !string.IsNullOrEmpty(_gamepadHint) ? _gamepadHint : _keyboardHint;
                break;
        }
    }
}
