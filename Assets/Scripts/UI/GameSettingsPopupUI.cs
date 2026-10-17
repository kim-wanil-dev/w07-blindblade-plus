using System;
using System.Collections;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

using TMPro;

/// <summary>인트로와 전투에서 공유하는 영속 설정 팝업과 입력·음량·시점 감도 적용을 관리한다.</summary>
public sealed class GameSettingsPopupUI : MonoBehaviour
{
    internal const string VOLUME_PREF_KEY = "IntroMenu.Volume";

    private const string LOOK_SENSITIVITY_PREF_KEY = "GameSettings.MouseSensitivity";
    private const float DEFAULT_LOOK_SENSITIVITY = 0.12f;
    private const float MIN_LOOK_SENSITIVITY = 0.02f;
    private const float MAX_LOOK_SENSITIVITY = 0.5f;
    private const float SHURIKEN_PREVIEW_DURATION = 0.8f;
    private static GameSettingsPopupUI _instance;

    [Header("영속 설정 UI")]
    private Canvas _canvas;
    private GameObject _modalRoot;
    private RectTransform _cardRect;
    private Button _closeButton;
    private TMP_Text _controlsText;
    private Slider _volumeSlider;
    private Slider _sensitivitySlider;
    private GameObject _previewAudioOwner;
    private Coroutine _previewRoutine;
    private Button _introButton;
    private Button[] _menuButtons;
    private bool[] _menuButtonStates;
    private bool _menuInputBlocked;
    private bool _openedForGameplay;

    public static GameSettingsPopupUI Instance => _instance;
    public bool IsVisible => _modalRoot != null && _modalRoot.activeSelf;
    public event Action PanelClosed;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        InputDeviceDisplay.DeviceChanged += HandleDeviceChanged;
    }

    void OnDisable()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        StopShurikenPreviewImmediately();
    }

    /// <summary>실행 중인 수리검 미리듣기 코루틴과 FMOD 효과음을 즉시 정지한다.</summary>
    private void StopShurikenPreviewImmediately()
    {
        if (_previewRoutine != null)
        {
            StopCoroutine(_previewRoutine);
            _previewRoutine = null;
        }
        if (Managers.IsInitialized && _previewAudioOwner != null)
            Managers.Sound.StopFor(_previewAudioOwner, SoundId.ShurikenFlight, false, true);
    }

    void OnDestroy()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        StopShurikenPreviewImmediately();
        if (_instance == this)
            _instance = null;
    }

    /// <summary>domain 재시작 시 영속 팝업의 정적 인스턴스 참조를 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance()
    {
        _instance = null;
    }

    /// <summary>저장 음량을 복원하고 인트로 버튼·기존 슬라이더를 영속 설정 팝업에 연결한다.</summary>
    public static GameSettingsPopupUI ConfigureFromIntro(
        Button settingsButton,
        GameObject sourcePanelContainer,
        Slider volumeSlider,
        TMP_Text styleSource,
        Button[] menuButtons)
    {
        if (_instance == null)
        {
            GameSettingsPopupUI staleInstance = FindFirstObjectByType<GameSettingsPopupUI>(FindObjectsInactive.Include);
            if (staleInstance != null)
                Destroy(staleInstance.gameObject);

            GameObject host = new GameObject("PersistentSettingsCanvas", typeof(RectTransform));
            host.layer = 5;
            Canvas canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            CanvasScaler scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            host.AddComponent<GraphicRaycaster>();
            _instance = host.AddComponent<GameSettingsPopupUI>();
        }

        _instance.Configure(settingsButton, sourcePanelContainer, volumeSlider, styleSource, menuButtons);
        return _instance;
    }

    /// <summary>기존 슬라이더와 메뉴 참조를 새 인트로 씬의 설정 팝업에 연결하고 저장값을 복원한다.</summary>
    private void Configure(
        Button settingsButton,
        GameObject sourcePanelContainer,
        Slider volumeSlider,
        TMP_Text styleSource,
        Button[] menuButtons)
    {
        _canvas = GetComponent<Canvas>();
        _introButton = settingsButton;
        _menuButtons = menuButtons;
        _menuButtonStates = new bool[_menuButtons.Length];

        if (_modalRoot == null)
            CreateModal(volumeSlider, styleSource);

        if (_volumeSlider != null && _volumeSlider != volumeSlider)
        {
            _volumeSlider.onValueChanged.RemoveListener(ChangeVolume);
            Destroy(_volumeSlider.gameObject);
        }

        _volumeSlider = volumeSlider;
        _volumeSlider.transform.SetParent(_cardRect, false);
        ConfigureSlider(_volumeSlider, new Vector2(0f, 112f), 0f, 1f);
        _volumeSlider.onValueChanged.RemoveListener(ChangeVolume);
        _volumeSlider.onValueChanged.AddListener(ChangeVolume);

        float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VOLUME_PREF_KEY, 1f));
        _volumeSlider.SetValueWithoutNotify(volume);
        ApplyVolume(volume);

        float fallbackSensitivity = GetCurrentLookSensitivity();
        float sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(LOOK_SENSITIVITY_PREF_KEY, fallbackSensitivity),
            MIN_LOOK_SENSITIVITY, MAX_LOOK_SENSITIVITY);
        _sensitivitySlider.SetValueWithoutNotify(sensitivity);
        _sensitivitySlider.onValueChanged.RemoveListener(ChangeLookSensitivity);
        _sensitivitySlider.onValueChanged.AddListener(ChangeLookSensitivity);
        ApplyLookSensitivity(sensitivity);

        if (sourcePanelContainer != null)
            sourcePanelContainer.SetActive(false);
        RefreshControlsText();
    }

    /// <summary>화면 중앙 카드, 60% 어두운 배경, 기존 슬라이더 복제본과 조작 안내를 만든다.</summary>
    private void CreateModal(Slider sourceSlider, TMP_Text styleSource)
    {
        _modalRoot = new GameObject("GameSettingsModal", typeof(RectTransform), typeof(Image));
        _modalRoot.layer = gameObject.layer;
        RectTransform modalRect = (RectTransform)_modalRoot.transform;
        modalRect.SetParent(_canvas.transform, false);
        modalRect.anchorMin = Vector2.zero;
        modalRect.anchorMax = Vector2.one;
        modalRect.offsetMin = Vector2.zero;
        modalRect.offsetMax = Vector2.zero;
        Image backdrop = _modalRoot.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.6f);
        backdrop.raycastTarget = true;

        GameObject card = new GameObject("SettingsCard", typeof(RectTransform), typeof(Image));
        card.layer = gameObject.layer;
        card.transform.SetParent(_modalRoot.transform, false);
        _cardRect = (RectTransform)card.transform;
        _cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        _cardRect.pivot = new Vector2(0.5f, 0.5f);
        _cardRect.sizeDelta = new Vector2(760f, 660f);
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.045f, 0.04f, 0.035f, 0.98f);
        cardImage.raycastTarget = true;

        CreateText("SettingsTitle", "설정", styleSource, _cardRect, new Vector2(-35f, 270f),
            new Vector2(440f, 58f), 42f, TextAlignmentOptions.Center);
        CreateText("VolumeLabel", "전체 음량", styleSource, _cardRect, new Vector2(0f, 176f),
            new Vector2(520f, 40f), 28f, TextAlignmentOptions.Center);
        CreateText("SensitivityLabel", "마우스 감도", styleSource, _cardRect, new Vector2(0f, 42f),
            new Vector2(520f, 40f), 28f, TextAlignmentOptions.Center);
        CreateText("ControlsTitle", "조작법", styleSource, _cardRect, new Vector2(0f, -82f),
            new Vector2(520f, 42f), 28f, TextAlignmentOptions.Center);
        _controlsText = CreateText("ControlsText", string.Empty, styleSource, _cardRect,
            new Vector2(0f, -202f), new Vector2(640f, 184f), 24f, TextAlignmentOptions.Center);

        GameObject sensitivitySliderObject = Instantiate(sourceSlider.gameObject, _cardRect, false);
        sensitivitySliderObject.name = "MouseSensitivitySlider";
        _sensitivitySlider = sensitivitySliderObject.GetComponent<Slider>();
        ConfigureSlider(_sensitivitySlider, new Vector2(0f, -8f), MIN_LOOK_SENSITIVITY, MAX_LOOK_SENSITIVITY);

        _closeButton = CreateButton("SettingsCloseButton", "닫기", styleSource, _cardRect,
            new Vector2(302f, 270f), new Vector2(112f, 48f));
        _closeButton.onClick.AddListener(ClosePopup);

        _previewAudioOwner = new GameObject("SettingsShurikenPreview");
        _previewAudioOwner.transform.SetParent(transform, false);
        _modalRoot.SetActive(false);
    }

    /// <summary>source의 TMP 글꼴과 정렬을 사용해 카드 안에 위치·크기·문구가 지정된 텍스트를 만든다.</summary>
    private TMP_Text CreateText(
        string objectName,
        string value,
        TMP_Text source,
        RectTransform parent,
        Vector2 position,
        Vector2 size,
        float fontSize,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = gameObject.layer;
        textObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = source.font;
        text.fontSharedMaterial = source.fontSharedMaterial;
        text.text = value;
        text.fontSize = fontSize;
        text.color = new Color(0.94f, 0.9f, 0.82f, 1f);
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>source의 글꼴 색상과 배경색을 사용해 지정 위치와 크기의 닫기 버튼을 만든다.</summary>
    private Button CreateButton(
        string objectName,
        string label,
        TMP_Text source,
        RectTransform parent,
        Vector2 position,
        Vector2 size)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.layer = gameObject.layer;
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)buttonObject.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.2f, 0.16f, 0.1f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        TMP_Text text = CreateText("Label", label, source, rect, Vector2.zero, size, 24f,
            TextAlignmentOptions.Center);
        text.transform.SetAsLastSibling();
        return button;
    }

    /// <summary>slider를 카드 좌표에 맞추고 minValue와 maxValue로 조작 범위를 지정한다.</summary>
    private static void ConfigureSlider(Slider slider, Vector2 position, float minValue, float maxValue)
    {
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        RectTransform rect = (RectTransform)slider.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(440f, 32f);
        rect.localScale = Vector3.one;
    }

    /// <summary>인트로에서 설정 팝업을 열고 닫기 버튼을 UI 포커스로 선택한다.</summary>
    public void OpenFromIntro(Button settingsButton)
    {
        _openedForGameplay = false;
        _introButton = settingsButton;
        OpenPopup();
    }

    /// <summary>플레이 중 상태를 일시정지로 바꾸고 설정 팝업을 연다. 상태 전환 성공 여부를 반환한다.</summary>
    public bool OpenForGameplay()
    {
        if (IsVisible || Game.Instance == null || Game.Instance.GameStateManager == null
            || !Game.Instance.GameStateManager.PauseForSettings())
            return false;

        _openedForGameplay = true;
        _introButton = null;
        OpenPopup();
        return true;
    }

    /// <summary>전투 일시정지 중 설정 팝업을 닫고 원래 플레이어 상태와 커서를 복구한다.</summary>
    public bool CloseForGameplay()
    {
        if (!IsVisible || !_openedForGameplay)
            return false;

        ClosePopup();
        return true;
    }

    /// <summary>전투 상태에서 영속 설정 팝업을 열 수 있으면 열고 성공 여부를 반환한다.</summary>
    public static bool TryOpenForGameplay()
    {
        return _instance != null && _instance.OpenForGameplay();
    }

    /// <summary>전투 일시정지 상태에서 열려 있는 설정 팝업을 닫을 수 있으면 닫고 성공 여부를 반환한다.</summary>
    public static bool TryCloseForGameplay()
    {
        return _instance != null && _instance.CloseForGameplay();
    }

    /// <summary>현재 인트로 설정 버튼을 열기 전·닫힌 뒤 UI 포커스 복원 대상으로 저장한다.</summary>
    public void AttachIntroButton(Button settingsButton)
    {
        _introButton = settingsButton;
    }

    /// <summary>인트로 씬이 닫힐 때 해당 씬의 버튼 참조를 영속 팝업에서 제거한다.</summary>
    public void DetachIntroButton(Button settingsButton)
    {
        if (_introButton == settingsButton)
            _introButton = null;
    }

    /// <summary>60% 검은 배경과 설정 카드를 표시하고 전투 메뉴 입력을 차단한다.</summary>
    private void OpenPopup()
    {
        for (int i = 0; i < _menuButtons.Length; i++)
        {
            if (_menuButtons[i] == null)
                continue;
            _menuButtonStates[i] = _menuButtons[i].interactable;
            _menuButtons[i].interactable = false;
        }
        _menuInputBlocked = true;
        _modalRoot.transform.SetAsLastSibling();
        EnsureEventSystem();
        _modalRoot.SetActive(true);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(_closeButton.gameObject);
    }

    /// <summary>현재 씬에 EventSystem이 없으면 New Input System UI 입력 모듈을 포함해 생성한다.</summary>
    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        new GameObject("SettingsEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    /// <summary>설정 창을 닫고 저장·버튼 입력·일시정지 플레이어 상태를 복원한다.</summary>
    private void ClosePopup()
    {
        if (!IsVisible)
            return;

        StopShurikenPreviewImmediately();
        _modalRoot.SetActive(false);
        RestoreMenuInput();
        PlayerPrefs.Save();

        bool resumeGameplay = _openedForGameplay;
        _openedForGameplay = false;
        if (resumeGameplay && Game.Instance != null && Game.Instance.GameStateManager != null)
            Game.Instance.GameStateManager.ResumeFromSettings();
        else if (EventSystem.current != null && _introButton != null)
            EventSystem.current.SetSelectedGameObject(_introButton.gameObject);

        PanelClosed?.Invoke();
    }

    /// <summary>팝업 진입 전에 기록한 인트로 버튼별 interactable 상태를 원래 값으로 복원한다.</summary>
    private void RestoreMenuInput()
    {
        if (!_menuInputBlocked)
            return;

        for (int i = 0; i < _menuButtons.Length; i++)
        {
            if (_menuButtons[i] != null)
                _menuButtons[i].interactable = _menuButtonStates[i];
        }
        _menuInputBlocked = false;
    }

    /// <summary>volume을 0~1로 제한해 BGM·효과음·AudioListener에 적용한다.</summary>
    public static void ApplyVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        Managers.Sound.BgmVolume = volume;
        Managers.Sound.EffectVolume = volume;
        AudioListener.volume = volume;
    }

    /// <summary>음량을 적용·저장하고 수리검 비행음을 선택된 효과음 크기로 미리 재생한다.</summary>
    private void ChangeVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        ApplyVolume(volume);
        PlayerPrefs.SetFloat(VOLUME_PREF_KEY, volume);
        PlayShurikenPreview();
    }

    /// <summary>sensitivity를 저장하고 현재 플레이어의 마우스 시점 회전량을 변경한다.</summary>
    private void ChangeLookSensitivity(float sensitivity)
    {
        ApplyLookSensitivity(sensitivity);
        PlayerPrefs.SetFloat(LOOK_SENSITIVITY_PREF_KEY, sensitivity);
    }

    /// <summary>sensitivity를 유효 범위로 제한해 현재 PlayerMotor에 적용한다.</summary>
    private static void ApplyLookSensitivity(float sensitivity)
    {
        sensitivity = Mathf.Clamp(sensitivity, MIN_LOOK_SENSITIVITY, MAX_LOOK_SENSITIVITY);
        PlayerController player = Game.Player;
        if (player == null)
            return;

        PlayerMotor motor = player.GetComponent<PlayerMotor>();
        if (motor != null)
            motor.SetLookSensitivity(sensitivity);
    }

    /// <summary>현재 플레이어 모터 또는 기본 설정에서 적용할 마우스 감도를 읽어 반환한다.</summary>
    private static float GetCurrentLookSensitivity()
    {
        PlayerController player = Game.Player;
        if (player != null)
        {
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (motor != null)
                return motor.LookSensitivity;
        }
        return DEFAULT_LOOK_SENSITIVITY;
    }

    /// <summary>음량을 조작할 때 수리검 비행음을 짧게 재생하고 제한 시간 뒤 정지한다.</summary>
    private void PlayShurikenPreview()
    {
        if (_previewRoutine != null)
            return;

        Camera previewCamera = Camera.main;
        if (previewCamera != null)
            _previewAudioOwner.transform.position = previewCamera.transform.position;

        if (!Managers.Sound.PlayAttachedLoop(SoundId.ShurikenFlight, _previewAudioOwner))
            return;
        _previewRoutine = StartCoroutine(StopShurikenPreview());
    }

    /// <summary>실시간 대기 후 현재 팝업 소유자의 수리검 미리듣기를 정지한다.</summary>
    private IEnumerator StopShurikenPreview()
    {
        yield return new WaitForSecondsRealtime(SHURIKEN_PREVIEW_DURATION);
        if (Managers.IsInitialized && _previewAudioOwner != null)
            Managers.Sound.StopFor(_previewAudioOwner, SoundId.ShurikenFlight, false, true);
        _previewRoutine = null;
    }

    /// <summary>최근 입력 장치에 맞는 이동·시점·전투·메뉴 조작 안내를 갱신한다.</summary>
    private void RefreshControlsText()
    {
        if (_controlsText == null)
            return;

        const string keyboardControls =
            "이동: WASD    시점: MOUSE\n공격: 좌클릭    방어/패링: 우클릭\n회피: SPACE    상호작용: [F]\n설정 / 일시정지: ESC";
        _controlsText.text = InputDeviceDisplay.FormatKeys(keyboardControls);
    }

    /// <summary>패드 사용 여부 변경에 따라 조작 안내의 키와 버튼 표기를 갱신한다.</summary>
    private void HandleDeviceChanged(bool isGamepad)
    {
        RefreshControlsText();
    }
}
