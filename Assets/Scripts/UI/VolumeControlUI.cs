using System;

using UnityEngine;

using UnityEngine.UI;

/// <summary>인트로 설정 버튼을 영속 설정 팝업에 연결하고 메뉴 전환 연출 이벤트를 전달한다.</summary>
public sealed class VolumeControlUI : MonoBehaviour
{
    [Header("설정 버튼과 팝업 프리팹")]
    [SerializeField] private Button _volumeButton;
    [SerializeField] private GameSettingsPopupUI _settingsPopup;

    /// <summary>인트로 메뉴가 대나무 배경과 절단 연출을 연결할 설정 버튼을 반환한다.</summary>
    internal Button SettingsButton => _volumeButton;

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

    /// <summary>settingsPopup에 설정 버튼과 메뉴 입력 대상을 전달해 직렬화된 슬라이더의 저장값을 복원한다.</summary>
    private void InitializeSettingsPopup()
    {
        _settingsPopup = GameSettingsPopupUI.ConfigureFromIntro(
            _settingsPopup,
            _volumeButton,
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
