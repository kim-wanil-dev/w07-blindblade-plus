using UnityEngine;
using UnityEngine.UI;

/// <summary>게임 클리어 상태를 화면에 표시하고 타이틀 복귀 버튼을 제공한다.</summary>
public sealed class GameClearUI : GameUI
{
    private const string TITLE_SCENE_NAME = "IntroScene";
    private const float TITLE_TRANSITION_DURATION = 0.5f;

    [Header("게임 클리어 UI")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Button _titleButton;

    private GameStateManager _stateManager;
    private bool _returningToTitle;

    /// <summary>게임 상태 이벤트와 타이틀 버튼을 연결하고 현재 상태에 맞게 화면을 초기화한다.</summary>
    public override void Initialize()
    {
        Shutdown();
        _stateManager = Game.Instance.GameStateManager;
        _stateManager.StateChanged += HandleStateChanged;
        _titleButton.onClick.AddListener(ReturnToTitle);
        HandleStateChanged(_stateManager.State);
    }

    /// <summary>state가 Cleared인지에 따라 게임 클리어 화면을 표시하거나 숨긴다.</summary>
    private void HandleStateChanged(GameplayState state)
    {
        SetVisible(state == GameplayState.Cleared);
    }

    /// <summary>visible에 따라 화면 패널을 표시하고, 보일 때 타이틀 복귀 버튼에 선택을 둔다.</summary>
    public override void SetVisible(bool visible)
    {
        base.SetVisible(visible);
        _panel.SetActive(visible);
        if (visible) _titleButton.Select();
    }

    /// <summary>타이틀 씬 등록을 확인하고 페이드 전환으로 돌아간다.</summary>
    private void ReturnToTitle()
    {
        if (_returningToTitle || _stateManager.State != GameplayState.Cleared) return;
        if (!Application.CanStreamedLevelBeLoaded(TITLE_SCENE_NAME))
        {
            Debug.LogError("타이틀 씬이 빌드 설정에 등록되지 않았습니다: " + TITLE_SCENE_NAME, this);
            return;
        }

        _returningToTitle = true;
        _titleButton.interactable = false;
        SceneTransitionOverlay.TransitionToScene(TITLE_SCENE_NAME, TITLE_TRANSITION_DURATION, null);
    }

    /// <summary>상태 이벤트와 버튼 요청을 해제하고 외부 매니저 참조를 정리한다.</summary>
    public override void Shutdown()
    {
        if (_stateManager != null) _stateManager.StateChanged -= HandleStateChanged;
        if (_titleButton != null) _titleButton.onClick.RemoveListener(ReturnToTitle);
        _stateManager = null;
        _returningToTitle = false;
    }

    void OnDestroy()
    {
        Shutdown();
    }
}
