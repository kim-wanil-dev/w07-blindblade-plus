using UnityEngine;
using UnityEngine.UI;

using TMPro;

/// <summary>기력 부족으로 회피 입력이 거부되면 온보딩 안내와 같은 위치에 짧은 경고를 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class PlayerDodgeStaminaHint : MonoBehaviour
{
    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const int CANVAS_SORTING_ORDER = 103;
    private const string WARNING_TEXT = "기력이 부족합니다.";

    [Header("표시")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField, Min(0.01f)] private float _visibleDuration = 1.2f;
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.15f;
    private CanvasGroup _group;
    private RectTransform _panelRect;
    private float _visibleUntil;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    void Awake()
    {
        BuildCanvas();
        ApplyResponsiveLayout();
    }

    void Update()
    {
        if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height)
            ApplyResponsiveLayout();

        float targetAlpha = Time.unscaledTime < _visibleUntil ? 1f : 0f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, Time.unscaledDeltaTime / _fadeDuration);
    }

    void OnDisable()
    {
        Hide();
    }

    /// <summary>부족 경고의 종료 시각을 현재 실시간에서 _visibleDuration 뒤로 갱신하고 패널을 표시한다.</summary>
    public void Show()
    {
        _visibleUntil = Time.unscaledTime + _visibleDuration;
    }

    /// <summary>진행 중인 부족 경고를 끝내고 패널의 투명도를 0으로 되돌린다.</summary>
    public void Hide()
    {
        _visibleUntil = 0f;
        _group.alpha = 0f;
    }

    /// <summary>온보딩 안내의 색·글꼴·배치를 가진 화면 하단 Canvas를 생성해 _group과 _panelRect에 저장한다.</summary>
    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("DodgeStaminaHintCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = CANVAS_SORTING_ORDER;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        GameObject panelObject = new GameObject("DodgeStaminaHintPanel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.015f, 0.02f, 0.025f, 0.78f);
        panel.raycastTarget = false;
        _panelRect = panel.rectTransform;
        _panelRect.anchorMin = new Vector2(0.5f, 0f);
        _panelRect.anchorMax = new Vector2(0.5f, 0f);
        _panelRect.pivot = new Vector2(0.5f, 0f);

        GameObject textObject = new GameObject("DodgeStaminaHint", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.font = _font;
        label.fontSize = 34f;
        label.color = new Color(0.96f, 0.95f, 0.91f, 1f);
        label.alignment = TextAlignmentOptions.Midline;
        label.enableAutoSizing = true;
        label.fontSizeMin = 34f * 0.55f;
        label.fontSizeMax = 34f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = WARNING_TEXT;
        RectTransform textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(26f, 8f);
        textRect.offsetMax = new Vector2(-26f, -8f);
    }

    /// <summary>현재 화면 종횡비로 온보딩 안내와 같은 패널 크기·하단 위치를 _panelRect에 적용한다.</summary>
    private void ApplyResponsiveLayout()
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        bool compact = (float)Screen.width / Mathf.Max(1, Screen.height) < 1.5f;
        _panelRect.sizeDelta = compact ? new Vector2(640f, 104f) : new Vector2(760f, 104f);
        _panelRect.anchoredPosition = new Vector2(0f, compact ? 42f : 62f);
    }
}
