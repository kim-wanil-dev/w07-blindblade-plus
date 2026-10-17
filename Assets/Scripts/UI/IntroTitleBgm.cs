using System.Collections;

using UnityEngine;

/// <summary>
/// IntroScene에서 파일형 타이틀 BGM을 시작하고 씬 이탈 시 정리한다.
/// </summary>
public sealed class IntroTitleBgm : MonoBehaviour
{
    [Header("타이틀 BGM")]
    [SerializeField] private string _streamingPath = "Music/TitleBgm.mp3";
    [SerializeField, Min(0f)] private float _volume = 1f;

    IEnumerator Start()
    {
        while (isActiveAndEnabled)
        {
            bool soundReady = Managers.Sound.IsStreamingBgmReady;
            if (IntroFadeInController.IsStartupNoticeComplete && soundReady)
                break;

            yield return null;
        }

        if (isActiveAndEnabled)
            Managers.Sound.PlayStreamingBgm(_streamingPath, _volume);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        StopTitleBgm();
    }

    /// <summary>
    /// 이 컴포넌트가 시작한 파일형 BGM만 정지한다.
    /// _streamingPath를 사용해 다른 장면의 BGM은 유지한다.
    /// </summary>
    private void StopTitleBgm()
    {
        if (Managers.IsInitialized)
            Managers.Sound.StopStreamingBgm(_streamingPath);
    }
}
