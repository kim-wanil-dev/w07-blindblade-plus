using UnityEngine;

/// <summary>Intro 시작 절단이 끝난 뒤 횃불 소리와 온보딩 씬 전환을 한 번 실행한다.</summary>
public sealed class IntroOnboardingTransition : MonoBehaviour
{
    [Header("온보딩 진입")]
    [SerializeField] private AudioClip _torchClip;
    [SerializeField] private float _fadeDuration = 0.5f;

    private bool _started;

    /// <summary>sceneName을 검증하고 새 온보딩 실행을 위해 횃불 오디오·암전 전환을 시작한다.</summary>
    public void Begin(string sceneName)
    {
        if (_started) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("온보딩 씬이 빌드 설정에 등록되지 않았습니다: " + sceneName, this);
            return;
        }

        _started = true;
        SceneTransitionOverlay.BeginIntroTransition(_torchClip, sceneName, _fadeDuration);
    }
}
