using UnityEngine;
using UnityEngine.UI;

/// <summary>1막 보스 소멸 뒤 완료 카드와 다음 막 버튼을 표시한다.</summary>
public sealed class ActCompleteUI : MonoBehaviour
{
    private const string NEXT_SCENE_NAME = "RainyTwinBoss";
    private const float TRANSITION_DURATION = 0.5f;

    [Header("1막 완료 UI")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Button _nextButton;

    private bool _transitioning;

    /// <summary>완료 패널을 열고 nextButton을 입력 포커스로 지정한다.</summary>
    public void Show()
    {
        _nextButton.onClick.RemoveListener(ContinueToNextAct);
        _nextButton.onClick.AddListener(ContinueToNextAct);
        _panel.SetActive(true);
        _nextButton.Select();
    }

    /// <summary>1막 완료 상태의 nextButton 입력으로 RainyTwinBoss 씬으로 페이드 전환한다.</summary>
    private void ContinueToNextAct()
    {
        if (_transitioning || Game.Instance.GameStateManager.State != GameplayState.ActComplete) return;
        if (!Application.CanStreamedLevelBeLoaded(NEXT_SCENE_NAME))
        {
            Debug.LogError("다음 막 씬이 빌드 설정에 등록되지 않았습니다: " + NEXT_SCENE_NAME, this);
            return;
        }
        _transitioning = true;
        _nextButton.interactable = false;
        SceneTransitionOverlay.TransitionToScene(NEXT_SCENE_NAME, TRANSITION_DURATION, null);
    }

    void OnDestroy()
    {
        if (_nextButton != null)
            _nextButton.onClick.RemoveListener(ContinueToNextAct);
    }
}
