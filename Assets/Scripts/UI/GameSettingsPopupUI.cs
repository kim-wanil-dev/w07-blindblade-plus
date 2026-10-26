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
    private const string POPUP_RESOURCE_PATH = "Prefabs/UI/Intro/GameSettingsPopup";
    private static GameSettingsPopupUI _instance;

    [Header("영속 설정 UI")]
    [SerializeField] private GameObject _modalRoot;
    [SerializeField] private Button _closeButton;
    [SerializeField] private Button _retryButton;
    [SerializeField] private IntroBambooButtonSliceEffect _retryButtonSliceEffect;
    [SerializeField] private Button _quitButton;
    [SerializeField] private IntroBambooButtonSliceEffect _quitButtonSliceEffect;
    [SerializeField] private TMP_Text _controlsText;
    [SerializeField] private Slider _volumeSlider;
    [SerializeField] private Slider _sensitivitySlider;
    [SerializeField] private GameObject _previewAudioOwner;
    [SerializeField, TextArea] private string keyboardControls;
    private Button _boundCloseButton;
    private Coroutine _previewRoutine;
    private bool _bgmPausedForPreview;
    private Button _introButton;
    private Button[] _menuButtons = Array.Empty<Button>();
    private bool[] _menuButtonStates = Array.Empty<bool>();
    private bool _menuInputBlocked;
    private bool _openedForGameplay;
    private bool _isCloseAnimating;

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
        _modalRoot.SetActive(false);
        BindCloseButton();
        _retryButton.onClick.AddListener(RequestRetry);
        _quitButton.onClick.AddListener(RequestQuit);
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
        ResumeBgmAfterPreview();
    }

    /// <summary>
    /// 수리검 미리듣기로 멈춘 BGM을 다시 재생한다.
    /// 미리듣기 중지 상태를 해제하고 SoundManager에 재개를 전달한다.
    /// </summary>
    private void ResumeBgmAfterPreview()
    {
        if (!_bgmPausedForPreview)
            return;

        if (Managers.IsInitialized)
            Managers.Sound.SetBgmPaused(false);
        _bgmPausedForPreview = false;
    }

    void OnDestroy()
    {
        InputDeviceDisplay.DeviceChanged -= HandleDeviceChanged;
        if (_boundCloseButton != null)
            _boundCloseButton.onClick.RemoveListener(RequestClose);
        _retryButton.onClick.RemoveListener(RequestRetry);
        _quitButton.onClick.RemoveListener(RequestQuit);
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

    /// <summary>scenePopup 또는 기존 영속 팝업을 골라 settingsButton·menuButtons와 저장된 음량·감도를 연결하고 팝업을 반환한다.</summary>
    public static GameSettingsPopupUI ConfigureFromIntro(
        GameSettingsPopupUI scenePopup, Button settingsButton, Button[] menuButtons)
    {
        if (_instance == null)
        {
            _instance = scenePopup;
            if (_instance == null)
                _instance = FindFirstObjectByType<GameSettingsPopupUI>(FindObjectsInactive.Include);
        }

        if (_instance == null)
        {
            Debug.LogError("[GameSettingsPopupUI] IntroScene의 GameSettingsPopup 프리팹 연결을 확인해주세요.");
            return null;
        }

        _instance._introButton = settingsButton;
        _instance._menuButtons = menuButtons;
        _instance._menuButtonStates = new bool[menuButtons.Length];
        _instance.Configure();
        return _instance;
    }

    /// <summary>직렬화된 슬라이더에 저장값과 변경 리스너를 연결하고 조작 안내를 갱신한다.</summary>
    private void Configure()
    {
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
        RefreshControlsText();
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

    /// <summary>
    /// 전투 설정 입력을 받아 영속 팝업이 없으면 Resources 프리팹으로 준비하고 연다.
    /// 인트로를 거치지 않은 씬 실행에서도 저장된 설정을 연결하며, 일시정지 진입 성공 여부를 반환한다.
    /// </summary>
    public static bool TryOpenForGameplay()
    {
        if (_instance == null)
        {
            GameObject prefab = Resources.Load<GameObject>(POPUP_RESOURCE_PATH);
            _instance = Instantiate(prefab).GetComponent<GameSettingsPopupUI>();
            _instance.Configure();
        }

        return _instance.OpenForGameplay();
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
        BindCloseButton();
        _isCloseAnimating = false;
        _closeButton.interactable = true;
        _retryButton.gameObject.SetActive(_openedForGameplay);
        _retryButton.interactable = true;
        _retryButtonSliceEffect.RestoreOriginal();
        _quitButton.gameObject.SetActive(_openedForGameplay);
        _quitButton.interactable = true;
        _quitButtonSliceEffect.RestoreOriginal();
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

        _isCloseAnimating = false;
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

    /// <summary>X 버튼 입력으로 설정 팝업을 닫고 인트로 메뉴 또는 일시정지 게임을 복원한다.</summary>
    private void RequestClose()
    {
        if (!IsVisible || _isCloseAnimating)
            return;

        ClosePopup();
    }

    /// <summary>
    /// Inspector의 현재 _closeButton을 클릭 리스너에 연결한다.
    /// 이전 버튼의 리스너를 해제하고 새 버튼이 설정 팝업을 닫도록 한다.
    /// </summary>
    private void BindCloseButton()
    {
        if (_boundCloseButton == _closeButton)
            return;

        if (_boundCloseButton != null)
            _boundCloseButton.onClick.RemoveListener(RequestClose);

        _boundCloseButton = _closeButton;
        _boundCloseButton.onClick.AddListener(RequestClose);
    }

    /// <summary>전투 중 재도전 버튼을 누르면 대나무 절단 연출을 시작하고 현재 스테이지 재시작을 예약한다.</summary>
    private void RequestRetry()
    {
        if (!IsVisible || !_openedForGameplay || _isCloseAnimating)
            return;

        if (_retryButtonSliceEffect.Play(RetryStage))
        {
            _isCloseAnimating = true;
            _retryButton.interactable = false;
            return;
        }

        RetryStage();
    }

    /// <summary>일시정지 팝업의 미리듣기와 설정을 정리하고 현재 씬의 새 도전을 시작한다.</summary>
    private void RetryStage()
    {
        GameStateManager state = Game.Instance != null ? Game.Instance.GameStateManager : null;
        if (state == null || state.State != GameplayState.Paused)
            return;

        StopShurikenPreviewImmediately();
        PlayerPrefs.Save();
        _modalRoot.SetActive(false);
        RestoreMenuInput();
        _openedForGameplay = false;
        _isCloseAnimating = false;
        PanelClosed?.Invoke();
        state.Restart();
    }

    /// <summary>
    /// 게임 중 설정 팝업에서 종료 입력을 받고 대나무 절단 연출을 시작한다.
    /// 팝업 표시 상태와 gameplay 진입 여부를 확인하고, 완료 후 종료 처리를 예약한다.
    /// </summary>
    private void RequestQuit()
    {
        if (!IsVisible || !_openedForGameplay || _isCloseAnimating)
            return;

        if (_quitButtonSliceEffect.Play(CompleteQuitSlice))
        {
            _isCloseAnimating = true;
            _quitButton.interactable = false;
            return;
        }

        QuitGame();
    }

    /// <summary>
    /// 종료 버튼 절단 연출이 끝난 뒤 설정값을 저장하고 앱을 종료한다.
    /// 완료된 애니메이션 뒤 PlayerPrefs를 저장하고 Editor 또는 플레이어를 닫는다.
    /// </summary>
    private void CompleteQuitSlice()
    {
        QuitGame();
    }

    /// <summary>
    /// 미리듣기와 설정값을 정리한 뒤 현재 앱 또는 Editor Play Mode를 종료한다.
    /// 종료 시 활성 미리듣기를 중지하고 PlayerPrefs를 저장한 다음 실행 환경을 닫는다.
    /// </summary>
    private void QuitGame()
    {
        StopShurikenPreviewImmediately();
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        Managers.Sound.SetBgmPaused(true);
        _bgmPausedForPreview = true;
        _previewRoutine = StartCoroutine(StopShurikenPreview());
    }

    /// <summary>실시간 대기 후 현재 팝업 소유자의 수리검 미리듣기를 정지한다.</summary>
    private IEnumerator StopShurikenPreview()
    {
        yield return new WaitForSecondsRealtime(SHURIKEN_PREVIEW_DURATION);
        if (Managers.IsInitialized && _previewAudioOwner != null)
            Managers.Sound.StopFor(_previewAudioOwner, SoundId.ShurikenFlight, false, true);
        ResumeBgmAfterPreview();
        _previewRoutine = null;
    }

    /// <summary>최근 입력 장치에 맞는 이동·시점·전투·메뉴 조작 안내를 갱신한다.</summary>
    private void RefreshControlsText()
    {
        if (_controlsText == null)
            return;

        //const string keyboardControls =
        //    "이동: WASD    시점: MOUSE\n공격: 좌클릭    방어: 우클릭\n회피: SPACE    \n설정 / 일시정지: ESC";
        _controlsText.text = InputDeviceDisplay.FormatKeys(keyboardControls);
    }

    /// <summary>패드 사용 여부 변경에 따라 조작 안내의 키와 버튼 표기를 갱신한다.</summary>
    private void HandleDeviceChanged(bool isGamepad)
    {
        RefreshControlsText();
    }
}
