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
        [SerializeField, TextArea(1, 4)] private string _keyboardHint;
        [SerializeField, TextArea(1, 4)] private string _gamepadHint;
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
    private const int CANVAS_SORTING_ORDER = 102;
    private const float OBJECTIVE_SHINE_DURATION = 0.62f;
    private const float OBJECTIVE_SHINE_PEAK_ALPHA = 0.42f;
    private const float COMPLETION_TITLE_SIZE = 38f;
    private const float INSTRUCTION_FONT_SIZE = 34f;
    private const float TITLE_ONLY_SCALE = 1.4f;
    private static readonly Color TUTORIAL_PAPER = new Color32(202, 197, 176, 255);
    private static readonly Color TUTORIAL_GOLD = new Color(1f, 0.72f, 0.24f, 1f);

    [Header("표시 자산")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private Prompt[] _prompts =
    {
        new Prompt("이동 - WASD\n회피 - Space\n공격 - 마우스 왼쪽 버튼 클릭", "이동 - 왼쪽 스틱\n회피 - A 버튼\n공격 - RB", null,
            "발도로 적 3명 처치"),
        new Prompt("극한 회피 - 적의 공격에 맞춰 Space로 회피", "극한 회피 - 적의 공격에 맞춰 A 버튼으로 회피", null, "수리검 극한 회피 1회"),
        new Prompt("우클릭 - 막기\n쳐내기 - 적의 공격 시점에 맞춰 막기", "LB - 막기\n쳐내기 - 적의 공격 시점에 맞춰 LB 누르기", null,
            new[] { "막기 1회", "공격 쳐내기 0/1" }),
        new Prompt("우클릭 - 막기\n쳐내기 - 적의 공격 시점에 맞춰 막기", "LB - 막기\n쳐내기 - 적의 공격 시점에 맞춰 LB 누르기", null,
            new[] { "수리검 막기 1회", "공격 쳐내기 0/1", "일섬 발동 1회" },
            "일섬 - 마우스 왼쪽 버튼 클릭",
            "일섬 - RB 버튼", null, 2),
        new Prompt("우클릭 - 막기\n쳐내기 - 적의 공격 시점에 맞춰 막기", "LB - 막기\n쳐내기 - 적의 공격 시점에 맞춰 LB 누르기", null,
            new[] { "거합 게이지 채우기 0/3\n(극한 회피, 쳐내기로 획득)", "거합 게이지 채우기 1/3\n(극한 회피, 쳐내기로 획득)", "거합 게이지 채우기 2/3\n(극한 회피, 쳐내기로 획득)", "거합으로 적 8명 처치" },
            "마우스 왼쪽 버튼 홀드 - 거합 발동. 일격으로 주변의 적을 쓰러뜨립니다.",
            "RB 홀드 - 거합 발동. 일격으로 주변의 적을 쓰러뜨립니다.", null, 3)
    };

    [Header("집중 안내")]
    [SerializeField, TextArea(2, 4)] private string _movementWaveTutorialText = "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.\n마우스 좌클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _movementWaveTutorialGamepadText = "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _perfectDodgeTutorialText = "극한 회피에 성공하면 파동이 발생해 주위를 잠시 밝히고, 시간이 느려집니다.\n마우스 좌클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _perfectDodgeTutorialGamepadText = "극한 회피에 성공하면 파동이 발생해 주위를 잠시 밝히고, 시간이 느려집니다.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _perfectDodgeMeterTutorialText = "극한 회피는 거합 게이지를 한 칸 채웁니다.\n마우스 좌클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _perfectDodgeMeterTutorialGamepadText = "극한 회피는 거합 게이지를 한 칸 채웁니다.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _targetThreadTutorialText = "검에서 나온 기운이 적에게 연결됩니다.\n마우스 좌클릭으로 발도 실행";
    [SerializeField, TextArea(2, 4)] private string _targetThreadTutorialGamepadText = "검에서 나온 기운이 적에게 연결됩니다.\nRB 버튼으로 발도 실행";
    [SerializeField, TextArea(2, 4)] private string _parryHudTutorialText = "쳐내기에 성공하면 집중 상태가 되고, 거합 게이지가 차오릅니다. 화면의 거합 게이지를 확인해 보세요.\n마우스 좌클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _parryHudTutorialGamepadText = "쳐내기에 성공하면 집중 상태가 되고, 거합 게이지가 차오릅니다. 화면의 거합 게이지를 확인해 보세요.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _parryBeadsTutorialText = "검에 달린 방울도 같은 거합 게이지를 보여줍니다.\n마우스 좌클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _parryBeadsTutorialGamepadText = "검에 달린 방울도 같은 거합 게이지를 보여줍니다.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _swordWaveTutorialText = "쳐내기 성공 시 공격 대신 일섬을 사용할 수 있습니다.\n경로상의 투사체를 없애는 검기를 날리며 돌진합니다.\n마우스 왼쪽 버튼 클릭으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _swordWaveTutorialGamepadText = "쳐내기 성공 시 공격 대신 일섬을 사용할 수 있습니다.\n경로상의 투사체를 없애는 검기를 날리며 돌진합니다.\nRB 버튼으로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _ultimateReadyTutorialText = "게이지가 3칸일 때 극한 회피나 쳐내기 후 느려진 시간에\n좌클릭 홀드로 거합을 발동할 수 있습니다.\n마우스 왼쪽 버튼 홀드로 계속 진행";
    [SerializeField, TextArea(2, 4)] private string _ultimateReadyTutorialGamepadText = "게이지가 3칸일 때 극한 회피나 쳐내기 후 느려진 시간에\nRB 홀드로 거합을 발동할 수 있습니다.\nRB 홀드로 계속 진행";
    [SerializeField, TextArea(1, 2)] private string _idleMovementText = "WASD키로 이동하기";
    [SerializeField, TextArea(1, 2)] private string _idleMovementGamepadText = "왼쪽 스틱으로 이동하기";

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
    private Image _movementPanel;
    private TextMeshProUGUI _movementText;
    private Image _tutorialPanel;
    private RectTransform _tutorialPanelRect;
    private TextMeshProUGUI _tutorialText;
    private Image _tutorialTargetFrame;
    private RectTransform _tutorialTargetRect;
    private Image _tutorialConnector;
    private string _tutorialKeyboardBody;
    private string _tutorialGamepadBody;
    private bool _movementControlsVisible;
    private bool _idleMovementVisible;
    private int _currentStage = -1;
    private int _currentObjective = -1;
    private int _lastScreenWidth;
    private int _lastScreenHeight;

    /// <summary>필요한 경우 온보딩 단계 안내 문구를 Inspector에서 교체할 배열을 반환한다.</summary>
    public Prompt[] Prompts => _prompts;
    public string MovementWaveTutorialText => _movementWaveTutorialText;
    public string MovementWaveTutorialGamepadText => _movementWaveTutorialGamepadText;
    public string PerfectDodgeTutorialText => _perfectDodgeTutorialText;
    public string PerfectDodgeTutorialGamepadText => _perfectDodgeTutorialGamepadText;
    public string PerfectDodgeMeterTutorialText => _perfectDodgeMeterTutorialText;
    public string PerfectDodgeMeterTutorialGamepadText => _perfectDodgeMeterTutorialGamepadText;
    public string TargetThreadTutorialText => _targetThreadTutorialText;
    public string TargetThreadTutorialGamepadText => _targetThreadTutorialGamepadText;
    public string ParryHudTutorialText => _parryHudTutorialText;
    public string ParryHudTutorialGamepadText => _parryHudTutorialGamepadText;
    public string ParryBeadsTutorialText => _parryBeadsTutorialText;
    public string ParryBeadsTutorialGamepadText => _parryBeadsTutorialGamepadText;
    public string SwordWaveTutorialText => _swordWaveTutorialText;
    public string SwordWaveTutorialGamepadText => _swordWaveTutorialGamepadText;
    public string UltimateReadyTutorialText => _ultimateReadyTutorialText;
    public string UltimateReadyTutorialGamepadText => _ultimateReadyTutorialGamepadText;

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
            : outcome == DefenseOutcome.PerfectParry ? "쳐내기" : "막기";
        _guardTimingLabel.text = $"막기 {heldTime:0.00}초 · {judgement}";
        _guardTimingFill.fillAmount = guarding && windowDuration > 0f
            ? Mathf.Clamp01(heldTime / windowDuration) : 0f;
        _guardTimingFill.color = outcome == DefenseOutcome.PerfectParry
            ? new Color(1f, 0.82f, 0.3f, 1f)
            : new Color(0.65f, 0.7f, 0.73f, 1f);
    }

    /// <summary>stage 0의 세 가지 조작을 중앙 패널에 표시하거나 숨긴다.</summary>
    public void SetMovementControls(bool visible)
    {
        _movementControlsVisible = visible;
        if (!visible) _idleMovementVisible = false;
        _movementPanel.gameObject.SetActive(visible);
        if (visible) RefreshMovementPrompt(false);
    }

    /// <summary>이동 정체 안내를 켜거나 끄고, 켜졌을 때 중앙 조작 안내를 지정 문구로 바꾼다.</summary>
    public void SetIdleMovementHint(bool visible)
    {
        if (!_movementControlsVisible) return;
        _idleMovementVisible = visible;
        RefreshMovementPrompt(visible);
    }

    /// <summary>body 문구를 밝은 안내판에 표시하고 tutorial overlay를 보인다.</summary>
    public void ShowTutorial(string keyboardBody, string gamepadBody)
    {
        _tutorialKeyboardBody = keyboardBody;
        _tutorialGamepadBody = string.IsNullOrEmpty(gamepadBody) ? keyboardBody : gamepadBody;
        RefreshTutorialText();
        _tutorialPanel.gameObject.SetActive(true);
    }

    /// <summary>targetRect의 화면 범위를 계산해 금색 프레임과 안내판 연결선을 배치한다.</summary>
    public void SetTutorialTarget(RectTransform targetRect)
    {
        if (targetRect == null) return;
        Vector3[] corners = new Vector3[4];
        targetRect.GetWorldCorners(corners);
        Vector2 screenBottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 screenTopRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        Vector2 center = (screenBottomLeft + screenTopRight) * 0.5f;
        Vector2 size = (screenTopRight - screenBottomLeft) / Mathf.Max(0.01f, _canvas.scaleFactor);
        SetTutorialTargetScreen(center, size + new Vector2(32f, 24f));
    }

    /// <summary>현재 튜토리얼 대상 프레임과 연결선을 숨긴다.</summary>
    public void ClearTutorialTarget()
    {
        _tutorialTargetFrame.gameObject.SetActive(false);
        _tutorialConnector.gameObject.SetActive(false);
    }

    /// <summary>worldPosition의 카메라 화면 지점에 금색 프레임과 안내판 연결선을 배치한다.</summary>
    public void SetTutorialTarget(Vector3 worldPosition, Camera viewCamera, Vector2 size)
    {
        Vector3 screen = viewCamera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
        {
            _tutorialTargetFrame.gameObject.SetActive(false);
            _tutorialConnector.gameObject.SetActive(false);
            return;
        }
        SetTutorialTargetScreen(screen, size);
    }

    /// <summary>
    /// worldPosition의 화면 좌표에 안내선만 연결하고 대상 프레임은 숨긴다.
    /// viewCamera로 투영할 수 없는 위치면 안내선도 숨긴다.
    /// </summary>
    public void SetTutorialConnectorTarget(Vector3 worldPosition, Camera viewCamera)
    {
        Vector3 screen = viewCamera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
        {
            _tutorialTargetFrame.gameObject.SetActive(false);
            _tutorialConnector.gameObject.SetActive(false);
            return;
        }
        SetTutorialTargetScreen(screen, Vector2.zero, false);
    }

    /// <summary>worldBounds에 포함된 실제 연결 실의 화면 범위를 금색 프레임과 안내 연결선으로 강조한다.</summary>
    public void SetTutorialTarget(Bounds worldBounds, Camera viewCamera, Vector2 padding)
    {
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        Vector2 screenMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 screenMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool hasVisibleCorner = false;

        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
        {
            Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                z == 0 ? min.z : max.z);
            Vector3 screen = viewCamera.WorldToScreenPoint(corner);
            if (screen.z <= 0f) continue;
            screenMin = Vector2.Min(screenMin, screen);
            screenMax = Vector2.Max(screenMax, screen);
            hasVisibleCorner = true;
        }

        if (!hasVisibleCorner)
        {
            _tutorialTargetFrame.gameObject.SetActive(false);
            _tutorialConnector.gameObject.SetActive(false);
            return;
        }

        Vector2 center = (screenMin + screenMax) * 0.5f;
        Vector2 size = (screenMax - screenMin) / Mathf.Max(0.01f, _canvas.scaleFactor) + padding;
        SetTutorialTargetScreen(center, size);
    }

    /// <summary>화면 좌표 targetScreenPoint를 Canvas 좌표로 바꾸고 프레임·연결선을 갱신한다.</summary>
    private void SetTutorialTargetScreen(Vector2 targetScreenPoint, Vector2 size, bool showFrame = true)
    {
        RectTransform canvasRect = (RectTransform)_canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, targetScreenPoint, null, out Vector2 targetLocal);
        _tutorialTargetRect.anchoredPosition = targetLocal;
        _tutorialTargetRect.sizeDelta = size;
        _tutorialTargetFrame.gameObject.SetActive(showFrame);
        Vector2 panelPoint = _tutorialPanelRect.anchoredPosition;
        Vector2 connectorStart = GetConnectorEdge(_tutorialPanelRect, targetLocal);
        Vector2 connectorEnd = showFrame ? GetConnectorEdge(_tutorialTargetRect, panelPoint) : targetLocal;
        Vector2 delta = connectorEnd - connectorStart;
        _tutorialConnector.rectTransform.anchoredPosition = (connectorStart + connectorEnd) * 0.5f;
        _tutorialConnector.rectTransform.sizeDelta = new Vector2(delta.magnitude, 4f);
        _tutorialConnector.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        _tutorialConnector.gameObject.SetActive(delta.sqrMagnitude > 1f);
    }

    /// <summary>
    /// rect 중심에서 toward 방향으로 뻗는 선이 사각형의 바깥 가장자리에 닿는 좌표를 반환한다.
    /// 테두리와 안내선이 겹치지 않도록 사각형 반너비·반높이에 5픽셀 여백을 더한다.
    /// </summary>
    private Vector2 GetConnectorEdge(RectTransform rect, Vector2 toward)
    {
        Vector2 center = rect.anchoredPosition;
        Vector2 direction = toward - center;
        if (direction.sqrMagnitude < 0.001f) return center;
        Vector2 extent = rect.rect.size * 0.5f + Vector2.one * 5f;
        float xScale = Mathf.Abs(direction.x) > 0.001f ? extent.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float yScale = Mathf.Abs(direction.y) > 0.001f ? extent.y / Mathf.Abs(direction.y) : float.PositiveInfinity;
        return center + direction * Mathf.Min(xScale, yScale);
    }

    /// <summary>현재 튜토리얼 안내판·대상 프레임·연결선을 함께 숨긴다.</summary>
    public void HideTutorial()
    {
        _tutorialPanel.gameObject.SetActive(false);
        _tutorialTargetFrame.gameObject.SetActive(false);
        _tutorialConnector.gameObject.SetActive(false);
    }

    /// <summary>재배치 전 검게 페이드하고 onBlack에서 단계 이동을 실행한 뒤 화면을 되돌린다.</summary>
    public IEnumerator FadeForReposition(Action onBlack)
    {
        yield return FadeStageOverlay(1f, 0.12f);
        onBlack?.Invoke();
        yield return FadeStageOverlay(0f, 0.12f);
    }

    /// <summary>
    /// 완료된 stage 번호를 사용해 다음 온보딩 단계의 전환 화면을 표시한다.
    /// 남아 있는 목표 갱신 카드는 이 카드로 대체한다.
    /// </summary>
    public IEnumerator ShowStageCompletion(int stage)
    {
        StopObjectiveUpdate();
        string heading = "단계 " + (stage + 1) + "/" + _prompts.Length + " 완료";
        string detail = "다음 단계 " + (stage + 2) + "/" + _prompts.Length;
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
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = CANVAS_SORTING_ORDER;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
        scaler.matchWidthOrHeight = 0.5f;

        _guidePanel = CreateImage("GuidePanel", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.78f));
        SetAnchoredRect(_guidePanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(760f, 104f));
        _instructionIcon = CreateImage("MouseHint", _guidePanel.transform, Color.white);
        _instructionIcon.preserveAspect = true;
        SetAnchoredRect(_instructionIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(40f, 60f));
        _instructionText = CreateText("Instruction", _guidePanel.transform, INSTRUCTION_FONT_SIZE, TextAlignmentOptions.MidlineLeft);
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

        _movementPanel = CreateImage("MovementControls", canvasObject.transform, new Color(0.015f, 0.02f, 0.025f, 0.92f));
        SetAnchoredRect(_movementPanel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(760f, 184f));
        _movementText = CreateText("MovementControlsText", _movementPanel.transform, 30f, TextAlignmentOptions.Center);
        _movementText.color = new Color(0.96f, 0.95f, 0.91f, 1f);
        _movementText.textWrappingMode = TextWrappingModes.Normal;
        SetStretchRect(_movementText.rectTransform, Vector2.zero, Vector2.one, new Vector2(28f, 18f), new Vector2(-28f, -18f));
        _movementPanel.gameObject.SetActive(false);

        _tutorialConnector = CreateImage("TutorialTargetConnector", canvasObject.transform, TUTORIAL_GOLD);
        _tutorialConnector.raycastTarget = false;
        _tutorialConnector.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _tutorialConnector.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _tutorialConnector.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        _tutorialConnector.gameObject.SetActive(false);
        _tutorialTargetFrame = CreateImage("TutorialTargetFrame", canvasObject.transform, Color.clear);
        _tutorialTargetFrame.raycastTarget = false;
        _tutorialTargetRect = _tutorialTargetFrame.rectTransform;
        Image targetTop = CreateImage("TopBorder", _tutorialTargetFrame.transform, TUTORIAL_GOLD);
        targetTop.raycastTarget = false;
        SetAnchoredRect(targetTop.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f));
        Image targetBottom = CreateImage("BottomBorder", _tutorialTargetFrame.transform, TUTORIAL_GOLD);
        targetBottom.raycastTarget = false;
        SetAnchoredRect(targetBottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 4f));
        Image targetLeft = CreateImage("LeftBorder", _tutorialTargetFrame.transform, TUTORIAL_GOLD);
        targetLeft.raycastTarget = false;
        SetAnchoredRect(targetLeft.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(0f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
        Image targetRight = CreateImage("RightBorder", _tutorialTargetFrame.transform, TUTORIAL_GOLD);
        targetRight.raycastTarget = false;
        SetAnchoredRect(targetRight.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(1f, 0.5f), Vector2.zero, new Vector2(4f, 0f));
        _tutorialTargetFrame.gameObject.SetActive(false);

        _tutorialPanel = CreateImage("TutorialCard", canvasObject.transform, TUTORIAL_PAPER);
        _tutorialPanelRect = _tutorialPanel.rectTransform;
        SetAnchoredRect(_tutorialPanelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 270f), new Vector2(820f, 206f));
        Outline tutorialOutline = _tutorialPanel.gameObject.AddComponent<Outline>();
        tutorialOutline.effectColor = TUTORIAL_GOLD;
        tutorialOutline.effectDistance = new Vector2(4f, 4f);
        _tutorialText = CreateText("TutorialCardText", _tutorialPanel.transform, 31f, TextAlignmentOptions.Center);
        _tutorialText.color = new Color(0.12f, 0.11f, 0.09f, 1f);
        _tutorialText.textWrappingMode = TextWrappingModes.Normal;
        SetStretchRect(_tutorialText.rectTransform, Vector2.zero, Vector2.one, new Vector2(34f, 24f), new Vector2(-34f, -24f));
        _tutorialPanel.gameObject.SetActive(false);

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
        _movementPanel.rectTransform.sizeDelta = compact ? new Vector2(640f, 184f) : new Vector2(760f, 184f);
        _movementPanel.rectTransform.anchoredPosition = new Vector2(0f, compact ? 42f : 62f);
        _tutorialPanel.rectTransform.sizeDelta = compact ? new Vector2(700f, 206f) : new Vector2(820f, 206f);
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
        if (_movementControlsVisible) RefreshMovementPrompt(_idleMovementVisible);
        if (_tutorialPanel.gameObject.activeSelf) RefreshTutorialText();
    }

    /// <summary>현재 입력 장치에 맞는 설명 본문을 선택해 튜토리얼 카드에 표시한다.</summary>
    private void RefreshTutorialText()
    {
        _tutorialText.text = InputDeviceDisplay.IsGamepad ? _tutorialGamepadBody : _tutorialKeyboardBody;
    }

    /// <summary>현재 단계의 지정 문구를 실제 입력 장치에 맞춰 보여 주고 등록된 설명이 있으면 안내 위 줄에 표시한다.</summary>
    private void RefreshDevicePrompt()
    {
        if (_currentStage < 0 || _currentStage >= _prompts.Length) return;
        _guidePanel.gameObject.SetActive(_currentStage != 0);
        Prompt prompt = _prompts[_currentStage];
        bool gamepad = InputDeviceDisplay.IsGamepad;
        _instructionText.text = prompt.GetHint(gamepad, _currentObjective);
        bool swordWaveHint = _currentStage == 3 && _currentObjective == 2;
        _instructionText.enableAutoSizing = !swordWaveHint;
        if (swordWaveHint) _instructionText.fontSize = INSTRUCTION_FONT_SIZE;
        string note = prompt.GetNote(_currentObjective);
        _noteText.text = note;
        _notePanel.gameObject.SetActive(!string.IsNullOrEmpty(note));
        _instructionIcon.sprite = prompt.GetIcon(_currentObjective);
        _instructionIcon.enabled = _instructionIcon.sprite != null && !gamepad;
    }

    /// <summary>키보드·마우스 또는 게임패드 표기로 중앙 이동 안내를 갱신한다.</summary>
    private void RefreshMovementPrompt(bool idle)
    {
        if (_prompts == null || _prompts.Length == 0) return;
        if (idle)
            _movementText.text = InputDeviceDisplay.IsGamepad ? _idleMovementGamepadText : _idleMovementText;
        else
            _movementText.text = _prompts[0].GetHint(InputDeviceDisplay.IsGamepad, 0);
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
