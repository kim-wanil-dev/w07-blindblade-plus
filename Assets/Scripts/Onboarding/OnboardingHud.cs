using System;
using System.Collections;

using UnityEngine;
using UnityEngine.UI;

using TMPro;

using BlindBlade.Combat.Contracts;

/// <summary>온보딩 안내, 현재 목표와 가드 유지 시간·판정을 화면 비율에 맞춰 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class OnboardingHud : MonoBehaviour
{
    [Serializable]
    public sealed class Prompt
    {
        [SerializeField, TextArea(1, 2)] private string _keyboardHint;
        [SerializeField, TextArea(1, 2)] private string _gamepadHint;
        [SerializeField] private Sprite _keyboardIcon;
        [SerializeField, TextArea(1, 2)] private string _followUpKeyboardHint;
        [SerializeField, TextArea(1, 2)] private string _followUpGamepadHint;
        [SerializeField] private Sprite _followUpKeyboardIcon;
        [SerializeField, Min(1), Tooltip("후속 안내를 표시하기 시작하는 목표 순번(0부터)")]
        private int _followUpObjective = 1;
        [SerializeField, TextArea(1, 2), Tooltip("후속 안내 위 줄에 표시하는 설명. 비우면 표시하지 않는다.")]
        private string _followUpNote;
        [SerializeField, TextArea(1, 2)] private string[] _objectives;

        /// <summary>keyboardHint, gamepadHint, keyboardIcon, objectives로 교체 가능한 단계 안내를 만든다.</summary>
        public Prompt(string keyboardHint, string gamepadHint, Sprite keyboardIcon, params string[] objectives)
            : this(keyboardHint, gamepadHint, keyboardIcon, objectives, null, null, null)
        {
        }

        /// <summary>
        /// 여러 목표 단계에 입력 장치별 후속 안내와 선택 아이콘을 추가해 Inspector 편집 가능한 단계 안내를 만든다.
        /// followUpObjective 순번부터 후속 안내를 쓰며 followUpNote가 있으면 후속 안내 위 줄에 함께 표시한다.
        /// </summary>
        public Prompt(string keyboardHint, string gamepadHint, Sprite keyboardIcon, string[] objectives,
            string followUpKeyboardHint, string followUpGamepadHint, Sprite followUpKeyboardIcon,
            int followUpObjective = 1, string followUpNote = null)
        {
            _keyboardHint = keyboardHint;
            _gamepadHint = gamepadHint;
            _keyboardIcon = keyboardIcon;
            _followUpKeyboardHint = followUpKeyboardHint;
            _followUpGamepadHint = followUpGamepadHint;
            _followUpKeyboardIcon = followUpKeyboardIcon;
            _followUpObjective = followUpObjective;
            _followUpNote = followUpNote;
            _objectives = objectives;
        }

        /// <summary>gamepad와 objectiveIndex에 맞는 현재 목표의 입력 안내 문구를 반환한다.</summary>
        public string GetHint(bool gamepad, int objectiveIndex)
        {
            if (UsesFollowUp(objectiveIndex))
                return gamepad && !string.IsNullOrEmpty(_followUpGamepadHint)
                    ? _followUpGamepadHint : _followUpKeyboardHint;
            return gamepad && !string.IsNullOrEmpty(_gamepadHint) ? _gamepadHint : _keyboardHint;
        }

        /// <summary>objectiveIndex가 후속 안내 구간이면 안내 위 줄의 설명을, 아니면 빈 문자열을 반환한다.</summary>
        public string GetNote(int objectiveIndex)
        {
            return UsesFollowUp(objectiveIndex) && !string.IsNullOrEmpty(_followUpNote) ? _followUpNote : string.Empty;
        }

        /// <summary>후속 안내가 등록되어 있고 objectiveIndex가 _followUpObjective 이상인지 반환한다.</summary>
        private bool UsesFollowUp(int objectiveIndex)
        {
            return objectiveIndex >= Mathf.Max(1, _followUpObjective) && !string.IsNullOrEmpty(_followUpKeyboardHint);
        }

        /// <summary>현재 목표 순번의 문구를 반환하고 범위를 벗어나면 빈 문자열을 돌려준다.</summary>
        public string GetObjective(int index)
        {
            return _objectives != null && index >= 0 && index < _objectives.Length ? _objectives[index] : string.Empty;
        }

        /// <summary>objectiveIndex에 해당하는 키보드·마우스 안내 스프라이트를 반환한다.</summary>
        public Sprite GetIcon(int objectiveIndex)
        {
            return UsesFollowUp(objectiveIndex) ? _followUpKeyboardIcon : _keyboardIcon;
        }

        /// <summary>현재 단계에 등록된 순차 목표 문구 수를 반환한다.</summary>
        public int ObjectiveCount => _objectives != null ? _objectives.Length : 0;
    }

    private const float REFERENCE_WIDTH = 1920f;
    private const float REFERENCE_HEIGHT = 1080f;
    private const float OBJECTIVE_SHINE_DURATION = 0.62f;
    private const float OBJECTIVE_SHINE_PEAK_ALPHA = 0.42f;
    private const float COMPLETION_TITLE_SIZE = 38f;
    private const float TITLE_ONLY_SCALE = 1.4f;

    [Header("표시 자산")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private Prompt[] _prompts =
    {
        new Prompt("이동 - WASD", "이동 - 왼쪽 스틱", null, "파동 중심 1m 이내 도착"),
        new Prompt("돌진/회피 - SPACE", "돌진/회피 - A 버튼", null, "돌진 1회"),
        new Prompt("좌클릭 - 공격", "RB - 공격", null, "근접 적 처치 1회"),
        new Prompt("극한 회피 - SPACE", "극한 회피 - A 버튼", null, "수리검 극한 회피 1회"),
        new Prompt("우클릭 - 방어", "LB - 방어", null, "가드 1회", "가드를 놓고 패링 1회"),
        new Prompt("우클릭 - 방어", "LB - 방어", null,
            new[] { "가드 1회", "가드를 놓고 패링 1회", "검기 실제 발사" }, "좌클릭 - 검기", "RB - 검기", null, 2),
        new Prompt("우클릭 - 패링", "LB - 패링", null,
            new[] { "수리검 패링 1회", "궁극기로 적 8명 처치" },
            "좌클릭 홀드 - 궁극기", "RB 홀드 - 궁극기", null, 1, "기운 3개를 모으면 궁극기를 쓸 수 있습니다")
    };

    private Canvas _canvas;
    private Image _guidePanel;
    private Image _notePanel;
    private TextMeshProUGUI _noteText;
    private Image _objectivePanel;
    private Image _objectiveShine;
    private Image _instructionIcon;
    private TextMeshProUGUI _instructionText;
    private TextMeshProUGUI _objectiveTitle;
    private TextMeshProUGUI _objectiveText;
    private TextMeshProUGUI _guardTimingLabel;
    private Image _guardTimingBack;
    private Image _guardTimingFill;
    private CanvasGroup _stageFade;
    private CanvasGroup _completionGroup;
    private Coroutine _objectiveShineRoutine;
    private Coroutine _objectiveUpdateRoutine;
    private TextMeshProUGUI _completionTitle;
    private TextMeshProUGUI _completionDetail;
    private int _currentStage = -1;
    private int _currentObjective = -1;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    /// <summary>필요한 경우 온보딩 단계 안내 문구를 Inspector에서 교체할 배열을 반환한다.</summary>
    public Prompt[] Prompts => _prompts;

    void Awake()
    {
        BuildCanvas();
        InputDeviceDisplay.DeviceChanged += HandleDeviceChanged;
    }

    void OnDestroy()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
    }

    void Update()
    {
        if (_lastScreenWidth == Screen.width && _lastScreenHeight == Screen.height) return;
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        ApplyResponsiveLayout();
    }

    /// <summary>
    /// stage와 objectiveIndex의 교체 안내, 목표, 클릭 아이콘을 표시한다.
    /// emphasizeChange이면 목표 패널을 한 번 강조하고 화면 중앙에 목표 갱신 카드를 입력 제한 없이 띄운다.
    /// </summary>
    public void ShowObjective(int stage, int objectiveIndex, bool emphasizeChange = false)
    {
        if (_prompts == null || stage < 0 || stage >= _prompts.Length) return;
        if (_currentStage == stage && _currentObjective == objectiveIndex)
        {
            RefreshDevicePrompt();
            return;
        }

        _currentStage = stage;
        _currentObjective = objectiveIndex;
        Prompt prompt = _prompts[stage];
        _objectiveTitle.text = "목표";
        _objectiveText.text = prompt.GetObjective(objectiveIndex);
        _instructionIcon.sprite = prompt.GetIcon(objectiveIndex);
        _instructionIcon.enabled = _instructionIcon.sprite != null && !InputDeviceDisplay.IsGamepad;
        RefreshDevicePrompt();

        if (_objectiveShineRoutine != null) StopCoroutine(_objectiveShineRoutine);
        _objectiveShineRoutine = null;
        Color shineColor = _objectiveShine.color;
        shineColor.a = 0f;
        _objectiveShine.color = shineColor;
        if (!emphasizeChange) return;
        _objectiveShineRoutine = StartCoroutine(AnimateObjectiveShine());
        StopObjectiveUpdate();
        _objectiveUpdateRoutine = StartCoroutine(PlayCard("목표 갱신", prompt.GetObjective(objectiveIndex)));
    }

    /// <summary>진행 중인 목표 갱신 카드 코루틴을 멈춰 중앙 패널을 다른 카드가 이어서 쓰게 한다.</summary>
    private void StopObjectiveUpdate()
    {
        if (_objectiveUpdateRoutine != null) StopCoroutine(_objectiveUpdateRoutine);
        _objectiveUpdateRoutine = null;
    }

    /// <summary>visible, guarding, heldTime과 실제 방어 판정 및 시간창으로 가드 유지 시간과 현재 판정을 표시한다.</summary>
    public void SetGuardTiming(bool visible, bool guarding, float heldTime, DefenseOutcome outcome, float windowDuration)
    {
        _guardTimingLabel.gameObject.SetActive(visible);
        _guardTimingBack.gameObject.SetActive(visible);
        if (!visible) return;

        string judgement = !guarding ? "대기"
            : outcome == DefenseOutcome.PerfectParry ? "패링" : "가드";
        _guardTimingLabel.text = $"가드 {heldTime:0.00}초 · {judgement}";
        _guardTimingFill.fillAmount = guarding && windowDuration > 0f
            ? Mathf.Clamp01(heldTime / windowDuration) : 0f;
        _guardTimingFill.color = outcome == DefenseOutcome.PerfectParry
            ? new Color(1f, 0.82f, 0.3f, 1f)
            : new Color(0.65f, 0.7f, 0.73f, 1f);
    }

    /// <summary>재배치 전 검게 페이드하고 onBlack에서 단계 이동을 실행한 뒤 화면을 되돌린다.</summary>
    public IEnumerator FadeForReposition(Action onBlack)
    {
        yield return FadeStageOverlay(1f, 0.12f);
        onBlack?.Invoke();
        yield return FadeStageOverlay(0f, 0.12f);
    }

    /// <summary>
    /// 완료된 stage 번호와 다음 온보딩 단계 여부를 사용해 단계 전환 화면을 표시한다.
    /// 마지막 단계는 설명 없이 1막만 표시하며 남아 있는 목표 갱신 카드는 이 카드로 대체한다.
    /// </summary>
    public IEnumerator ShowStageCompletion(int stage, bool hasNextStage)
    {
        StopObjectiveUpdate();
        string heading = hasNextStage ? "단계 " + (stage + 1) + "/" + _prompts.Length + " 완료" : "1막";
        string detail = hasNextStage ? "다음 단계 " + (stage + 2) + "/" + _prompts.Length : string.Empty;
        yield return ShowCompletion(heading, detail, 0.65f);
    }

    /// <summary>
    /// stage의 시작을 단계 전환 카드와 같은 중앙 카드로 알리고 첫 목표 문구를 설명 줄에 표시한다.
    /// 목표 갱신 카드처럼 기다리지 않고 재생하므로 표시 중에도 입력을 막지 않는다.
    /// </summary>
    public void PlayStageStart(int stage)
    {
        StopObjectiveUpdate();
        string objective = stage >= 0 && stage < _prompts.Length ? _prompts[stage].GetObjective(0) : string.Empty;
        _objectiveUpdateRoutine = StartCoroutine(PlayCard("단계 " + (stage + 1) + "/" + _prompts.Length + " 시작", objective));
    }

    /// <summary>heading과 detail 카드를 중앙 패널에 한 번 표시하고 끝나면 진행 중인 카드 참조를 비운다.</summary>
    private IEnumerator PlayCard(string heading, string detail)
    {
        yield return ShowCompletion(heading, detail, 0.65f);
        _objectiveUpdateRoutine = null;
    }

    /// <summary>단계 암전 화면을 즉시 불투명하게 덮어 첫 장면이 보이지 않게 한다.</summary>
    public void CoverScreen()
    {
        _stageFade.alpha = 1f;
        _stageFade.blocksRaycasts = true;
    }

    /// <summary>
    /// 검은 화면 위에 title을 마지막 단계 카드와 같은 가운데 큰 제목으로 보인 뒤 fadeDuration 동안 제목과 암전을 함께 걷어 낸다.
    /// 암전이 완전히 사라지면 끝난다.
    /// </summary>
    public IEnumerator PlayOpening(string title, float fadeDuration)
    {
        StopObjectiveUpdate();
        CoverScreen();
        ApplyCompletionLayout(true);
        _completionTitle.text = title;
        _completionDetail.text = string.Empty;
        yield return FadeCompletion(1f, 0.16f);
        yield return new WaitForSecondsRealtime(0.65f);
        StartCoroutine(FadeCompletion(0f, fadeDuration));
        yield return FadeStageOverlay(0f, fadeDuration);
    }

    /// <summary>scale with screen size와 비율 기준 앵커를 적용한 최소 안내 Canvas를 만든다.</summary>
    private void BuildCanvas()
    {
        GameObject canvasObject = new GameObject("OnboardingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;

        _guidePanel = CreateImage("GuidePanel", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.78f));
        SetAnchoredRect(_guidePanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(760f, 104f));
        _instructionIcon = CreateImage("MouseHint", _guidePanel.transform, Color.white);
        _instructionIcon.preserveAspect = true;
        SetAnchoredRect(_instructionIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(40f, 60f));
        _instructionText = CreateText("Instruction", _guidePanel.transform, 34f, TextAlignmentOptions.MidlineLeft);
        SetStretchRect(_instructionText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(100f, 8f), new Vector2(-26f, -8f));
        _notePanel = CreateImage("GuideNotePanel", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.78f));
        SetAnchoredRect(_notePanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 174f), new Vector2(760f, 48f));
        _noteText = CreateText("GuideNote", _notePanel.transform, 24f, TextAlignmentOptions.Midline);
        SetStretchRect(_noteText.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 4f), new Vector2(-20f, -4f));
        _notePanel.gameObject.SetActive(false);

        _objectivePanel = CreateImage("ObjectivePanel", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.78f));
        SetAnchoredRect(_objectivePanel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-140f, 120f), new Vector2(400f, 170f));
        _objectivePanel.gameObject.AddComponent<RectMask2D>();
        _objectiveShine = CreateImage("ObjectiveShine", _objectivePanel.transform, new Color(0.94f, 0.97f, 1f, 0f));
        _objectiveShine.raycastTarget = false;
        SetAnchoredRect(_objectiveShine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 230f));
        _objectiveShine.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
        _objectiveTitle = CreateText("ObjectiveTitle", _objectivePanel.transform, 23f, TextAlignmentOptions.MidlineLeft);
        SetAnchoredRect(_objectiveTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(-44f, 34f));
        _objectiveText = CreateText("CurrentObjective", _objectivePanel.transform, 29f, TextAlignmentOptions.MidlineLeft);
        SetAnchoredRect(_objectiveText.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, -11f), new Vector2(-44f, 84f));
        _guardTimingLabel = CreateText("GuardTimingLabel", _objectivePanel.transform, 17f, TextAlignmentOptions.MidlineLeft);
        SetAnchoredRect(_guardTimingLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0f), new Vector2(22f, 34f), new Vector2(-44f, 18f));
        _guardTimingBack = CreateImage("GuardTimingTrack", _objectivePanel.transform, new Color(1f, 1f, 1f, 0.2f));
        SetAnchoredRect(_guardTimingBack.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(22f, 18f), new Vector2(-44f, 10f));
        _guardTimingFill = CreateImage("GuardTimingProgress", _guardTimingBack.transform, new Color(1f, 0.82f, 0.3f, 1f));
        _guardTimingFill.type = Image.Type.Filled;
        _guardTimingFill.fillMethod = Image.FillMethod.Horizontal;
        SetStretchRect(_guardTimingFill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _guardTimingLabel.gameObject.SetActive(false);
        _guardTimingBack.gameObject.SetActive(false);

        Image completionPanel = CreateImage("CompletionPanel", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.92f));
        SetAnchoredRect(completionPanel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 156f));
        _completionGroup = completionPanel.gameObject.AddComponent<CanvasGroup>();
        _completionGroup.alpha = 0f;
        _completionGroup.blocksRaycasts = false;
        _completionGroup.interactable = false;
        _completionTitle = CreateText("CompletionTitle", completionPanel.transform, 38f, TextAlignmentOptions.Center);
        SetAnchoredRect(_completionTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(-40f, 58f));
        _completionDetail = CreateText("CompletionDetail", completionPanel.transform, 24f, TextAlignmentOptions.Center);
        SetAnchoredRect(_completionDetail.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(-40f, 48f));

        GameObject fadeObject = new GameObject("StepFade", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        fadeObject.transform.SetParent(canvasObject.transform, false);
        RectTransform fadeRect = (RectTransform)fadeObject.transform;
        SetStretchRect(fadeRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        fadeObject.GetComponent<Image>().color = Color.black;
        _stageFade = fadeObject.GetComponent<CanvasGroup>();
        _stageFade.alpha = 0f;
        _stageFade.blocksRaycasts = false;
        _stageFade.interactable = false;
        // 서막 제목이 암전 위에 보이도록 중앙 카드를 가장 위에 그린다.
        _completionGroup.transform.SetAsLastSibling();
    }

    /// <summary>목표 상자 안에서 단색 UI 광택을 왼쪽에서 오른쪽으로 한 번 이동시킨다.</summary>
    private IEnumerator AnimateObjectiveShine()
    {
        RectTransform shineRect = _objectiveShine.rectTransform;
        float panelWidth = _objectivePanel.rectTransform.rect.width;
        float startX = -panelWidth * 0.8f;
        float endX = panelWidth * 0.8f;
        float elapsed = 0f;
        while (elapsed < OBJECTIVE_SHINE_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / OBJECTIVE_SHINE_DURATION);
            float movement = Mathf.SmoothStep(0f, 1f, progress);
            shineRect.anchoredPosition = new Vector2(Mathf.Lerp(startX, endX, movement), 0f);
            Color shineColor = _objectiveShine.color;
            shineColor.a = Mathf.Sin(progress * Mathf.PI) * OBJECTIVE_SHINE_PEAK_ALPHA;
            _objectiveShine.color = shineColor;
            yield return null;
        }

        Color finalColor = _objectiveShine.color;
        finalColor.a = 0f;
        _objectiveShine.color = finalColor;
        _objectiveShineRoutine = null;
    }

    /// <summary>좁은 화면에서는 중앙 안내와 우하단 목표 패널을 줄여 서로 겹치지 않게 배치한다.</summary>
    private void ApplyResponsiveLayout()
    {
        bool compact = (float)Screen.width / Mathf.Max(1, Screen.height) < 1.5f;
        _guidePanel.rectTransform.sizeDelta = compact ? new Vector2(640f, 104f) : new Vector2(760f, 104f);
        _guidePanel.rectTransform.anchoredPosition = new Vector2(0f, compact ? 42f : 62f);
        // 설명 줄은 안내 패널 바로 위에 같은 폭으로 붙인다.
        _notePanel.rectTransform.sizeDelta = new Vector2(_guidePanel.rectTransform.sizeDelta.x, 48f);
        _notePanel.rectTransform.anchoredPosition = new Vector2(0f, compact ? 154f : 174f);
        _objectivePanel.rectTransform.sizeDelta = new Vector2(400f, 170f);
        _objectivePanel.rectTransform.anchoredPosition = compact ? new Vector2(-62f, 82f) : new Vector2(-140f, 120f);
    }

    /// <summary>name과 parent를 사용해 단색 기본 Image를 만든다.</summary>
    private Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    /// <summary>name과 font, size, alignment로 화면 안내 TMP를 만들고 결과를 반환한다.</summary>
    private TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = _font;
        text.fontSize = fontSize;
        text.color = new Color(0.96f, 0.95f, 0.91f, 1f);
        text.alignment = alignment;
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSize * 0.55f;
        text.fontSizeMax = fontSize;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    /// <summary>heading과 detail을 중앙 완료 패널에 보이고 holdDuration 이후 다시 숨긴다.</summary>
    private IEnumerator ShowCompletion(string heading, string detail, float holdDuration)
    {
        ApplyCompletionLayout(string.IsNullOrEmpty(detail));
        _completionTitle.text = heading;
        _completionDetail.text = detail;
        yield return FadeCompletion(1f, 0.16f);
        yield return new WaitForSecondsRealtime(holdDuration);
        yield return FadeCompletion(0f, 0.16f);
    }

    /// <summary>
    /// titleOnly이면 제목을 패널 전체의 가로·세로 가운데에 1.4배 크기로 두고, 아니면 기본 위쪽 제목 배치로 되돌린다.
    /// 중앙 완료 패널 제목의 위치·정렬·글자 크기를 변경한다.
    /// </summary>
    private void ApplyCompletionLayout(bool titleOnly)
    {
        float size = titleOnly ? COMPLETION_TITLE_SIZE * TITLE_ONLY_SCALE : COMPLETION_TITLE_SIZE;
        _completionTitle.fontSizeMax = size;
        _completionTitle.fontSizeMin = size * 0.55f;
        _completionTitle.fontSize = size;
        if (titleOnly)
        {
            _completionTitle.alignment = TextAlignmentOptions.Midline;
            SetStretchRect(_completionTitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(20f, 10f), new Vector2(-20f, -10f));
            return;
        }
        _completionTitle.alignment = TextAlignmentOptions.Center;
        SetAnchoredRect(_completionTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(-40f, 58f));
    }

    /// <summary>중앙 완료 패널의 투명도를 unscaled 시간으로 target까지 보간한다.</summary>
    private IEnumerator FadeCompletion(float target, float duration)
    {
        float start = _completionGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _completionGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _completionGroup.alpha = target;
    }

    /// <summary>rect의 앵커·피벗·상대 위치·기준 크기를 지정한다.</summary>
    private void SetAnchoredRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
    }

    /// <summary>rect의 anchor를 부모 기준으로 늘리고 가장자리를 지정 inset만큼 조절한다.</summary>
    private void SetStretchRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    /// <summary>활성 입력 장치가 바뀐 경우 현재 단계 안내와 마우스 클릭 아이콘을 다시 표시한다.</summary>
    private void HandleDeviceChanged(bool gamepad)
    {
        RefreshDevicePrompt();
    }

    /// <summary>현재 단계의 지정 문구를 실제 입력 장치에 맞춰 보여 주고 등록된 설명이 있으면 안내 위 줄에 표시한다.</summary>
    private void RefreshDevicePrompt()
    {
        if (_currentStage < 0 || _currentStage >= _prompts.Length) return;
        Prompt prompt = _prompts[_currentStage];
        bool gamepad = InputDeviceDisplay.IsGamepad;
        _instructionText.text = prompt.GetHint(gamepad, _currentObjective);
        string note = prompt.GetNote(_currentObjective);
        _noteText.text = note;
        _notePanel.gameObject.SetActive(!string.IsNullOrEmpty(note));
        _instructionIcon.sprite = prompt.GetIcon(_currentObjective);
        _instructionIcon.enabled = _instructionIcon.sprite != null && !gamepad;
    }

    /// <summary>현재 페이드 알파를 unscaled 시간으로 target까지 변경한다.</summary>
    private IEnumerator FadeStageOverlay(float target, float duration)
    {
        float start = _stageFade.alpha;
        float elapsed = 0f;
        _stageFade.blocksRaycasts = target > 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            _stageFade.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        _stageFade.alpha = target;
        _stageFade.blocksRaycasts = target > 0f;
    }
}
