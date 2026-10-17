using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

using TMPro;

public class IntroMenu : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField] private UnityEngine.UI.Button startButton;
    [SerializeField] private UnityEngine.UI.Button _newGameButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private GameObject _tutorialPromptRoot;
    [SerializeField] private UnityEngine.UI.Button _tutorialYesButton;
    [SerializeField] private UnityEngine.UI.Button _tutorialNoButton;
    [SerializeField, FormerlySerializedAs("_gameSceneName")] private string _onboardingSceneName = "Onboarding";
    [SerializeField] private string _stage1SceneName = "BambooStage1";
    [SerializeField] private string _rainyStageSceneName = "RainyStage";
    [SerializeField, Min(0.01f)] private float _transitionFadeDuration = 0.5f;

    [Header("버튼 절단 상태")]
    private readonly List<IntroBambooButtonSliceEffect> _slicedButtonEffects =
        new List<IntroBambooButtonSliceEffect>();
    private VolumeControlUI _volumeControl;
    private IntroOnboardingTransition _onboardingTransition;
    private bool _isSlicing;
    private bool _tutorialChoiceConfirmed;
    private Color _continueButtonLabelColor;

    void Awake()
    {
        if (!enabled) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _volumeControl = GetComponent<VolumeControlUI>();
        _onboardingTransition = GetComponent<IntroOnboardingTransition>();
        _tutorialPromptRoot.SetActive(false);
        _continueButtonLabelColor = GetButtonLabel(startButton).color;
        RefreshContinueButton();
    }

    void OnEnable()
    {
        startButton.onClick.AddListener(OnStartClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        _newGameButton.onClick.AddListener(OnNewGameClicked);
        _tutorialYesButton.onClick.AddListener(OnTutorialYesClicked);
        _tutorialNoButton.onClick.AddListener(OnTutorialNoClicked);
        if (_volumeControl != null)
        {
            _volumeControl.OpenRequested += BeginSlice;
            _volumeControl.PanelClosed += RestoreMenuTexts;
        }
    }

    void OnDisable()
    {
        startButton.onClick.RemoveListener(OnStartClicked);
        quitButton.onClick.RemoveListener(OnQuitClicked);
        if (_newGameButton != null) _newGameButton.onClick.RemoveListener(OnNewGameClicked);
        if (_tutorialYesButton != null) _tutorialYesButton.onClick.RemoveListener(OnTutorialYesClicked);
        if (_tutorialNoButton != null) _tutorialNoButton.onClick.RemoveListener(OnTutorialNoClicked);
        if (_tutorialPromptRoot != null) _tutorialPromptRoot.SetActive(false);
        if (_volumeControl != null)
        {
            _volumeControl.OpenRequested -= BeginSlice;
            _volumeControl.PanelClosed -= RestoreMenuTexts;
        }
        _isSlicing = false;
        RestoreMenuTexts();
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 시작 버튼의 절단 연출을 요청하고 연출 후 게임 씬으로 전환한다.
    /// </summary>
    private void OnStartClicked()
    {
        if (BambooStage1Progress.HasContinueData)
            BeginSlice(startButton, ContinueGame);
    }

    /// <summary>
    /// 이어 하기 선택의 연출이 끝나면 저장된 Stage1 체크포인트를 확인하고 씬을 연다.
    /// </summary>
    private void ContinueGame()
    {
        if (!BambooStage1Progress.HasContinueData)
        {
            RefreshContinueButton();
            return;
        }

        string destinationScene = BambooStage1Progress.RainyStageReached
            ? _rainyStageSceneName : _stage1SceneName;
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            Debug.LogError("[IntroMenu] Continue scene is not in the build scene list: " + destinationScene);
            return;
        }

        PlayerPrefs.Save();
        SceneTransitionOverlay.TransitionToScene(destinationScene, _transitionFadeDuration, null);
    }

    /// <summary>새로 하기 버튼의 절단 연출을 요청하고 연출 후 튜토리얼 선택 창을 표시한다.</summary>
    private void OnNewGameClicked()
    {
        BeginSlice(_newGameButton, ShowTutorialPrompt);
    }

    /// <summary>튜토리얼 선택 창을 열고 메뉴 입력 대신 예 버튼에 UI 포커스를 둔다.</summary>
    private void ShowTutorialPrompt()
    {
        _tutorialChoiceConfirmed = false;
        _tutorialPromptRoot.SetActive(true);
        startButton.interactable = false;
        _newGameButton.interactable = false;
        quitButton.interactable = false;
        _tutorialYesButton.Select();
    }

    /// <summary>예 선택을 새 게임 흐름으로 전달해 온보딩 씬 진입을 시작한다.</summary>
    private void OnTutorialYesClicked()
    {
        BeginSlice(_tutorialYesButton, BeginNewGameWithTutorial);
    }

    /// <summary>아니오 선택을 새 게임 흐름으로 전달해 온보딩을 건너뛴다.</summary>
    private void OnTutorialNoClicked()
    {
        BeginSlice(_tutorialNoButton, BeginNewGameWithoutTutorial);
    }

    /// <summary>예 버튼 절단 연출이 끝난 뒤 튜토리얼을 포함한 새 게임을 시작한다.</summary>
    private void BeginNewGameWithTutorial()
    {
        BeginNewGame(true);
    }

    /// <summary>아니오 버튼 절단 연출이 끝난 뒤 튜토리얼을 건너뛰는 새 게임을 시작한다.</summary>
    private void BeginNewGameWithoutTutorial()
    {
        BeginNewGame(false);
    }

    /// <summary>새 게임의 진행 데이터를 초기화하고 tutorialChoice에 따라 온보딩 또는 1-1을 연다.</summary>
    private void BeginNewGame(bool tutorialChoice)
    {
        if (_tutorialChoiceConfirmed) return;

        string destinationScene = tutorialChoice ? _onboardingSceneName : _stage1SceneName;
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            Debug.LogError("[IntroMenu] Selected scene is not in the build scene list: " + destinationScene);
            return;
        }
        if (tutorialChoice && _onboardingTransition == null)
        {
            Debug.LogError("[IntroMenu] IntroOnboardingTransition is missing.", this);
            return;
        }

        _tutorialChoiceConfirmed = true;
        BambooStage1Progress.Clear();
        PlayerPrefs.Save();
        _tutorialPromptRoot.SetActive(false);

        if (tutorialChoice)
        {
            _onboardingTransition.Begin(_onboardingSceneName);
            return;
        }

        OnboardingFlowController.StartNewRun();
        SceneTransitionOverlay.TransitionToScene(_stage1SceneName, _transitionFadeDuration, null);
    }

    /// <summary>대나무 버튼 위에 표시되는 TMP 라벨을 찾고 없으면 기존 버튼 텍스트를 반환한다.</summary>
    private TMP_Text GetButtonLabel(Button button)
    {
        Transform bambooLabel = button.transform.Find("BambooButtonLabel");
        return bambooLabel != null
            ? bambooLabel.GetComponent<TMP_Text>()
            : button.GetComponentInChildren<TMP_Text>(true);
    }

    /// <summary>체크포인트 저장 여부로 이어 하기 입력과 라벨 색을 갱신한다.</summary>
    private void RefreshContinueButton()
    {
        bool canContinue = BambooStage1Progress.HasContinueData;
        startButton.interactable = canContinue;
        GetButtonLabel(startButton).color = canContinue
            ? _continueButtonLabelColor
            : Color.Lerp(_continueButtonLabelColor, Color.gray, 0.55f);
    }

    /// <summary>
    /// 종료 버튼의 절단 연출을 요청하고 연출 후 앱 또는 Play Mode를 종료한다.
    /// </summary>
    private void OnQuitClicked()
    {
        BeginSlice(quitButton, QuitGame);
    }

    /// <summary>
    /// 종료 선택의 연출이 끝나면 음량을 저장하고 앱 또는 Play Mode를 종료한다.
    /// </summary>
    private void QuitGame()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    
    /// <summary>
    /// button의 절단 컴포넌트를 실행하고 애니메이션이 끝난 뒤 onCompleted를 호출한다.
    /// </summary>
    private void BeginSlice(Button button, Action onCompleted)
    {
        if (_isSlicing)
            return;

        IntroBambooButtonSliceEffect sliceEffect = button.GetComponent<IntroBambooButtonSliceEffect>();
        if (sliceEffect == null)
        {
            Debug.LogError("[IntroMenu] 대나무 버튼 절단 컴포넌트 연결을 확인해주세요.", button);
            return;
        }

        _isSlicing = true;
        if (!_slicedButtonEffects.Contains(sliceEffect))
            _slicedButtonEffects.Add(sliceEffect);

        bool started = sliceEffect.Play(() => CompleteSlice(onCompleted));
        if (!started)
        {
            _slicedButtonEffects.Remove(sliceEffect);
            _isSlicing = false;
        }
    }

    /// <summary>
    /// 절단 애니메이션 완료 상태를 해제하고 다음 메뉴 동작을 실행한다.
    /// onCompleted를 사용하며 _isSlicing을 false로 변경한다.
    /// </summary>
    private void CompleteSlice(Action onCompleted)
    {
        _isSlicing = false;
        onCompleted?.Invoke();
    }

    /// <summary>
    /// 설정 팝업을 닫을 때 절단된 메뉴 버튼 이미지와 라벨을 원래 상태로 복원한다.
    /// _slicedButtonEffects를 사용하며 각 버튼의 표시 상태를 되돌린다.
    /// </summary>
    private void RestoreMenuTexts()
    {
        for (int i = 0; i < _slicedButtonEffects.Count; i++)
        {
            if (_slicedButtonEffects[i] != null)
                _slicedButtonEffects[i].RestoreOriginal();
        }
        _slicedButtonEffects.Clear();
    }
}
    
