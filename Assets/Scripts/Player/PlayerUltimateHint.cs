using UnityEngine;

using TMPro;

using BlindBlade.UI.CombatHud;

/// <summary>
/// PlayerSkillGauge의 거합 게이지를 화면 중하단 원형 HUD로 표시한다.
/// 원 안을 아래에서 위로 차오르는 실 액체, 내림 백분율과 현재 입력 장치에 맞는 거합 안내를 갱신하고, 거합 차징·시전 중에는 HUD 전체를 숨긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEmpower))]
public sealed class PlayerUltimateHint : MonoBehaviour
{
    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const float INPUT_LABEL_HEIGHT = 16f;
    private const float INPUT_LABEL_GAP = 4f;
    private static Sprite _runtimeCircleSprite;

    [Header("게이지 원본 및 배치")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private Sprite _gaugeSprite;
    [SerializeField, Min(1f), Tooltip("거합 원형 HUD의 한 변 크기(기준 화면 픽셀)")]
    private float _gaugeSize = 104f;
    [SerializeField, Min(0f), Tooltip("화면 아래쪽에서 게이지 묶음 하단까지의 거리(기준 화면 픽셀)")]
    private float _bottomOffset = 150f;
    [SerializeField] private int _canvasSortingOrder = 101;
    [SerializeField] private Color _backgroundColor = new Color(0.04f, 0.045f, 0.055f, 0.78f);
    [SerializeField, Tooltip("거합을 쓸 수 있을 때(100%) 백분율 글자 색")]
    private Color _readyColor = new Color(1f, 0.78f, 0.28f, 1f);
    [SerializeField, Tooltip("백분율 글자 색")]
    private Color _valueColor = new Color(0.98f, 0.97f, 0.93f, 1f);
    [SerializeField] private Color _labelColor = new Color(0.94f, 0.92f, 0.88f, 1f);
    [SerializeField, Min(12f)] private float _valueFontSize = 24f;
    [SerializeField, Min(8f)] private float _labelFontSize = 12f;
    [SerializeField, Range(0f, 1f), Tooltip("백분율 글자에 두르는 외곽선 두께. 0이면 외곽선이 없다.")]
    private float _valueOutlineWidth;
    [SerializeField] private Color _valueOutlineColor = Color.black;
    [SerializeField, Min(0.01f), Tooltip("거합 차징·시전이 시작되거나 끝날 때 HUD 전체가 사라지고 나타나는 실시간(초)")]
    private float _hideFadeDuration = 0.15f;

    [Header("실 액체")]
    [SerializeField, Tooltip("실 구체가 겹친 수를 텍스처에 더하는 BlindBlade/ThreadLiquidDensity 재질")]
    private Material _liquidDensityMaterial;
    [SerializeField, Tooltip("겹침 수를 단계별 색으로 바꾸고 수면 위를 잘라 내는 BlindBlade/UIThreadLiquid 재질. 실행 중에 복제해 쓴다.")]
    private Material _liquidCompositeMaterial;
    [SerializeField, Tooltip("원 안을 아래에서 위로 차오르는 실 액체와 가득 찼을 때 테두리를 감는 실의 모양·흐름·겹침 단계 색")]
    private CombatThreadLiquidStyle _liquidStyle = new CombatThreadLiquidStyle();

    [Header("런타임")]
    private PlayerEmpower _empower;
    private PlayerSkillGauge _skillGauge;
    private CanvasGroup _group;
    private CombatThreadLiquidGauge _liquid;
    private TextMeshProUGUI _valueText;
    private TextMeshProUGUI _labelText;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
        _skillGauge = GetComponent<PlayerSkillGauge>();
        _gaugeSprite = ResolveGaugeSprite();
        BuildCanvas();
    }

    void OnEnable()
    {
        InputDeviceDisplay.DeviceChanged += HandleDeviceChanged;
        _skillGauge.Changed += HandleGaugeChanged;
        _group.alpha = 1f;
        RefreshLabel(InputDeviceDisplay.IsGamepad);
        RefreshGauge();
    }

    void OnDisable()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        _skillGauge.Changed -= HandleGaugeChanged;
        _group.alpha = 0f;
    }

    void Update()
    {
        // 게이지 캔버스가 암전 연출보다 위에 있으므로 거합 차징·시전 동안에는 직접 숨긴다.
        bool hidden = _empower.IsAutomaticCharging || _empower.IsStriking;
        _group.alpha = Mathf.MoveTowards(_group.alpha, hidden ? 0f : 1f, Time.unscaledDeltaTime / _hideFadeDuration);

        // 실제 사용 가능할 때만 가득 채우고 테두리 실을 돌린다. HUD라 불릿 타임에도 느려지지 않는다.
        bool ready = _skillGauge.CanUseUltimate;
        _liquid.SetState(ready ? 1f : _skillGauge.UltimateNormalized, ready, Time.unscaledDeltaTime);
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
    /// 거합 게이지가 바뀌면 정수 백분율을 최신 값으로 다시 표시한다.
    /// Changed 입력을 사용하며 TextMeshProUGUI의 표시 상태를 변경한다.
    /// </summary>
    private void HandleGaugeChanged()
    {
        RefreshGauge();
    }

    /// <summary>
    /// 작은 원형 게이지용 Overlay Canvas와 배경, 실 액체, 외곽선을 두른 중앙 수치, 입력 안내를 만든다.
    /// Inspector의 크기·하단 오프셋·Sprite·색·실 액체 설정을 적용하고 모든 그래픽의 Raycast Target을 끈다.
    /// </summary>
    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("UltimateGaugeCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _canvasSortingOrder;

        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasObject.GetComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        GameObject gaugeObject = new GameObject("UltimateGauge", typeof(RectTransform));
        gaugeObject.transform.SetParent(canvasObject.transform, false);
        RectTransform gaugeRect = gaugeObject.GetComponent<RectTransform>();
        gaugeRect.anchorMin = new Vector2(0.5f, 0f);
        gaugeRect.anchorMax = new Vector2(0.5f, 0f);
        gaugeRect.pivot = new Vector2(0.5f, 0f);
        gaugeRect.anchoredPosition = new Vector2(0f, _bottomOffset);
        gaugeRect.sizeDelta = new Vector2(_gaugeSize, _gaugeSize + INPUT_LABEL_HEIGHT + INPUT_LABEL_GAP);

        GameObject circleObject = new GameObject("GaugeCircle", typeof(RectTransform));
        circleObject.transform.SetParent(gaugeObject.transform, false);
        RectTransform circleRect = circleObject.GetComponent<RectTransform>();
        circleRect.anchorMin = new Vector2(0.5f, 1f);
        circleRect.anchorMax = new Vector2(0.5f, 1f);
        circleRect.pivot = new Vector2(0.5f, 1f);
        circleRect.anchoredPosition = Vector2.zero;
        circleRect.sizeDelta = new Vector2(_gaugeSize, _gaugeSize);

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        backgroundObject.transform.SetParent(circleObject.transform, false);
        UnityEngine.UI.Image background = backgroundObject.GetComponent<UnityEngine.UI.Image>();
        background.sprite = _gaugeSprite;
        background.color = _backgroundColor;
        background.preserveAspect = true;
        background.raycastTarget = false;
        SetStretchToParent(background.rectTransform);

        // 액체 본체를 먼저 두어 수면 실과 테두리 실이 그 위에 그려지게 한다.
        GameObject bodyObject = new GameObject("ThreadLiquidBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.RawImage));
        bodyObject.transform.SetParent(circleObject.transform, false);
        CombatThreadLiquidBody body = bodyObject.AddComponent<CombatThreadLiquidBody>();
        body.Initialize(_liquidStyle, _liquidDensityMaterial, _liquidCompositeMaterial);

        GameObject liquidObject = new GameObject("ThreadLiquid", typeof(RectTransform), typeof(CanvasRenderer));
        liquidObject.transform.SetParent(circleObject.transform, false);
        _liquid = liquidObject.AddComponent<CombatThreadLiquidGauge>();
        _liquid.Initialize(_liquidStyle, body);
        SetStretchToParent(_liquid.rectTransform);

        GameObject valueObject = new GameObject("Percentage", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        valueObject.transform.SetParent(circleObject.transform, false);
        _valueText = valueObject.GetComponent<TextMeshProUGUI>();
        _valueText.font = _font;
        _valueText.fontSize = _valueFontSize;
        _valueText.fontSizeMin = Mathf.Max(12f, _valueFontSize * 0.65f);
        _valueText.fontSizeMax = _valueFontSize;
        _valueText.enableAutoSizing = true;
        _valueText.textWrappingMode = TextWrappingModes.NoWrap;
        _valueText.alignment = TextAlignmentOptions.Center;
        _valueText.color = _valueColor;
        _valueText.outlineWidth = _valueOutlineWidth;
        _valueText.outlineColor = _valueOutlineColor;
        _valueText.raycastTarget = false;
        RectTransform valueRect = _valueText.rectTransform;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = new Vector2(3f, _gaugeSize * 0.25f);
        valueRect.offsetMax = new Vector2(-3f, -_gaugeSize * 0.2f);

        GameObject labelObject = new GameObject("InputLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(gaugeObject.transform, false);
        _labelText = labelObject.GetComponent<TextMeshProUGUI>();
        _labelText.font = _font;
        _labelText.fontSize = _labelFontSize;
        _labelText.fontSizeMin = _labelFontSize;
        _labelText.fontSizeMax = _labelFontSize;
        _labelText.alignment = TextAlignmentOptions.Center;
        _labelText.color = _labelColor;
        _labelText.raycastTarget = false;
        RectTransform labelRect = _labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0.5f, 0f);
        labelRect.anchorMax = new Vector2(0.5f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(_gaugeSize * 0.86f, INPUT_LABEL_HEIGHT);
    }

    /// <summary>
    /// 원본 Image들을 동일한 부모 RectTransform 크기로 늘려 배치한다.
    /// rectTransform을 사용하며 anchor와 offset을 전체 영역에 맞춘다.
    /// </summary>
    private void SetStretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Inspector Sprite를 우선 사용하고, 없으면 로드된 전투 HUD의 찬 슬롯 Sprite를 찾는다.
    /// 프로젝트에 재사용할 원형 Sprite가 없을 때만 런타임 원형 Sprite를 생성해 반환한다.
    /// </summary>
    private Sprite ResolveGaugeSprite()
    {
        if (_gaugeSprite != null)
            return _gaugeSprite;

        CombatPerfectParryTokenHud[] tokenHuds = Resources.FindObjectsOfTypeAll<CombatPerfectParryTokenHud>();
        for (int i = 0; i < tokenHuds.Length; i++)
        {
            if (tokenHuds[i].FilledSprite != null)
                return tokenHuds[i].FilledSprite;
        }

        return CreateRuntimeCircleSprite();
    }

    /// <summary>
    /// 원형 Sprite가 없을 때 부드러운 가장자리의 흰 원형 Texture2D와 Sprite를 한 번 생성해 반환한다.
    /// 입력은 없으며 공유 정적 Sprite를 _runtimeCircleSprite에 저장한다.
    /// </summary>
    private static Sprite CreateRuntimeCircleSprite()
    {
        if (_runtimeCircleSprite != null)
            return _runtimeCircleSprite;

        const int textureSize = 64;
        const float radius = 30f;
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "RuntimeUltimateGaugeCircleTexture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color[] pixels = new Color[textureSize * textureSize];
        Vector2 center = new Vector2(textureSize * 0.5f, textureSize * 0.5f);
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = 1f - Mathf.SmoothStep(radius - 1f, radius + 1f, distance);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        _runtimeCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, textureSize, textureSize), new Vector2(0.5f, 0.5f), 100f);
        _runtimeCircleSprite.name = "RuntimeUltimateGaugeCircle";
        return _runtimeCircleSprite;
    }

    /// <summary>
    /// 거합 게이지의 내림 정수 백분율을 표시한다. 실 액체는 Update에서 같은 게이지를 따라 차오른다.
    /// UltimateNormalized와 CanUseUltimate를 사용하며, 실제 준비 상태에서만 100%와 준비 색을 보인다.
    /// </summary>
    private void RefreshGauge()
    {
        bool ready = _skillGauge.CanUseUltimate;
        float normalizedGauge = _skillGauge.UltimateNormalized;
        _valueText.color = ready ? _readyColor : _valueColor;
        int percent = ready ? 100 : Mathf.Clamp(Mathf.FloorToInt(normalizedGauge * 100f), 0, 99);
        _valueText.text = percent + "%";
    }

    /// <summary>
    /// 현재 입력 장치에 맞는 키보드 Q 또는 게임패드 Y 안내를 중앙 원형 게이지 아래쪽에 표시한다.
    /// gamepad 값에 따라 _labelText의 문구를 변경한다.
    /// </summary>
    private void RefreshLabel(bool gamepad)
    {
        _labelText.text = gamepad ? "Y:거합" : "Q:거합";
    }
}
