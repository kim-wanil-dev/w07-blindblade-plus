using System;
using System.Collections;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>씬 사이에 검은 화면을 유지하고 지정된 페이드와 오디오 대기를 수행한다.</summary>
public sealed class SceneTransitionOverlay : MonoBehaviour
{
    private static SceneTransitionOverlay _instance;

    private CanvasGroup _canvasGroup;
    private AudioSource _audioSource;
    private Coroutine _transitionRoutine;
    private bool _transitioning;

    /// <summary>전환 오버레이를 준비하고 새 온보딩 실행의 페이드·소리·씬 전환을 시작한다.</summary>
    public static void BeginIntroTransition(AudioClip torchClip, string sceneName, float fadeDuration)
    {
        SceneTransitionOverlay overlay = EnsureInstance();
        if (overlay._transitioning) return;
        overlay._transitionRoutine = overlay.StartCoroutine(overlay.RunIntroTransition(torchClip, sceneName, fadeDuration));
    }

    /// <summary>현재 실행의 다음 씬으로 페이드 전환하고 암전 상태에서 beforeLoad를 한 번 실행한다.</summary>
    public static void TransitionToScene(string sceneName, float fadeDuration, Action beforeLoad)
    {
        SceneTransitionOverlay overlay = EnsureInstance();
        if (overlay._transitioning) return;
        overlay._transitionRoutine = overlay.StartCoroutine(overlay.RunSceneTransition(sceneName, fadeDuration, beforeLoad));
    }

    /// <summary>씬 교체 중에도 남을 전환 오브젝트를 만들거나 이미 존재하는 인스턴스를 반환한다.</summary>
    private static SceneTransitionOverlay EnsureInstance()
    {
        if (_instance != null) return _instance;
        GameObject overlayObject = new GameObject("SceneTransitionOverlay", typeof(RectTransform));
        return overlayObject.AddComponent<SceneTransitionOverlay>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        CreateOverlay();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>전체 화면 검은 Image와 CanvasGroup을 준비해 씬 사이에 유지한다.</summary>
    private void CreateOverlay()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable = false;

        Image image = gameObject.AddComponent<Image>();
        image.color = Color.black;
        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>횃불 소리를 시작하고 0.5초 암전 뒤 재생 종료까지 기다린 다음 onboarding 씬을 연다.</summary>
    private IEnumerator RunIntroTransition(AudioClip torchClip, string sceneName, float fadeDuration)
    {
        _transitioning = true;
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;
        _audioSource.ignoreListenerPause = true;
        _audioSource.loop = false;
        _audioSource.clip = torchClip;
        float soundStartedAt = Time.unscaledTime;
        if (torchClip != null)
            _audioSource.Play();
        else
            Debug.LogError("온보딩 진입 횃불 오디오 클립이 연결되지 않았습니다.", this);

        yield return FadeTo(1f, fadeDuration);
        if (torchClip != null)
        {
            float soundDuration = torchClip.length;
            while (Time.unscaledTime - soundStartedAt < soundDuration)
                yield return null;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("온보딩 씬이 빌드 설정에 등록되지 않았습니다: " + sceneName, this);
            yield return FadeTo(0f, fadeDuration);
            _transitioning = false;
            _transitionRoutine = null;
            yield break;
        }

        OnboardingFlowController.StartNewRun();
        SceneManager.LoadScene(sceneName);
        yield return null;
        yield return FadeTo(0f, fadeDuration);
        _audioSource.Stop();
        _transitioning = false;
        _transitionRoutine = null;
    }

    /// <summary>검게 페이드한 뒤 beforeLoad를 실행하고 지정된 씬을 연 다음 화면을 밝힌다.</summary>
    private IEnumerator RunSceneTransition(string sceneName, float fadeDuration, Action beforeLoad)
    {
        _transitioning = true;
        yield return FadeTo(1f, fadeDuration);
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("전환할 씬이 빌드 설정에 등록되지 않았습니다: " + sceneName, this);
            yield return FadeTo(0f, fadeDuration);
            _transitioning = false;
            _transitionRoutine = null;
            yield break;
        }

        beforeLoad?.Invoke();
        SceneManager.LoadScene(sceneName);
        yield return null;
        yield return FadeTo(0f, fadeDuration);
        _transitioning = false;
        _transitionRoutine = null;
    }

    /// <summary>_canvasGroup의 알파를 unscaled 시간으로 from에서 지정 알파까지 부드럽게 변경한다.</summary>
    private IEnumerator FadeTo(float alpha, float duration)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);
        _canvasGroup.blocksRaycasts = alpha > 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, alpha, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration)));
            yield return null;
        }
        _canvasGroup.alpha = alpha;
        _canvasGroup.blocksRaycasts = alpha > 0f;
    }
}
