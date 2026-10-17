using System;

using UnityEngine;

using UnityEngine.UI;

using TMPro;

/// <summary>인트로 설정 버튼을 영속 설정 팝업에 연결하고 메뉴 전환 연출 이벤트를 전달한다.</summary>
public sealed class VolumeControlUI : MonoBehaviour
{
    [Header("설정 버튼과 기존 음량 UI")]
    [SerializeField] private Button _volumeButton;
    [SerializeField] private GameObject _volumePanel;
    [SerializeField] private Slider _volumeSlider;

    private TMP_Text _buttonText;
    private GameSettingsPopupUI _settingsPopup;

    public event Action<Button, Action> OpenRequested;
    public event Action PanelClosed;

    void Awake()
    {
        InitializeSettingsPopup();
    }

    void OnEnable()
    {
        if (_settingsPopup == null)
            InitializeSettingsPopup();

        _volumeButton.onClick.AddListener(RequestOpen);
        if (_settingsPopup != null)
        {
            _settingsPopup.AttachIntroButton(_volumeButton);
            _settingsPopup.PanelClosed += HandlePanelClosed;
        }
    }

    void OnDisable()
    {
        _volumeButton.onClick.RemoveListener(RequestOpen);
        if (_settingsPopup != null)
        {
            _settingsPopup.PanelClosed -= HandlePanelClosed;
            _settingsPopup.DetachIntroButton(_volumeButton);
        }
        PlayerPrefs.Save();
    }

    /// <summary>저장 음량을 복원하고 인트로 버튼·기존 슬라이더를 영속 설정 팝업에 연결한다.</summary>
    private void InitializeSettingsPopup()
    {
        float volume = Mathf.Clamp01(PlayerPrefs.GetFloat(GameSettingsPopupUI.VOLUME_PREF_KEY, 1f));
        _volumeSlider.SetValueWithoutNotify(volume);
        GameSettingsPopupUI.ApplyVolume(volume);
        _buttonText = _volumeButton.GetComponentInChildren<TMP_Text>(true);
        _buttonText.text = "설정";
        _settingsPopup = GameSettingsPopupUI.ConfigureFromIntro(
            _volumeButton,
            _volumePanel.transform.parent.gameObject,
            _volumeSlider,
            _buttonText,
            _volumeButton.transform.parent.GetComponentsInChildren<Button>(true));
    }

    /// <summary>설정 버튼 입력을 메뉴 절단 연출 구독자에게 전달하고 완료 후 팝업을 연다.</summary>
    private void RequestOpen()
    {
        if (_settingsPopup.IsVisible)
            return;

        if (OpenRequested != null)
            OpenRequested(_volumeButton, OpenPanel);
        else
            OpenPanel();
    }

    /// <summary>인트로 설정 팝업을 열고 기존 메뉴 버튼 입력을 막는다.</summary>
    private void OpenPanel()
    {
        _settingsPopup.OpenFromIntro(_volumeButton);
    }

    /// <summary>설정 팝업이 닫히면 인트로 메뉴 구독자에게 알린다.</summary>
    private void HandlePanelClosed()
    {
        PanelClosed?.Invoke();
    }
}
