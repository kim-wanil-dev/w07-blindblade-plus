using System.Collections;

using UnityEngine;

/// <summary>엔딩 복귀 요청이 있을 때만 메뉴 화면의 검은 오버레이를 서서히 제거한다.</summary>
[DisallowMultipleComponent]
public sealed class IntroFadeInController : MonoBehaviour
{
    private static bool _pending;
    private static bool _startupShown;
    private static bool _startupNoticeComplete = true;

    /// <summary>인트로 시작 안내가 끝났는지 반환한다.</summary>
    public static bool IsStartupNoticeComplete => _startupNoticeComplete;

    [Header("메뉴 페이드인")]
    [SerializeField] private CanvasGroup _overlay;
    [SerializeField] private CanvasGroup _menu;
    [SerializeField, Min(0.01f)] private float _duration = 2f;

    [Header("시작 헤드폰 안내")]
    [SerializeField] private CanvasGroup _headphoneNotice;
    [SerializeField, Min(.01f)] private float _noticeFadeDuration = 2f;
    [SerializeField, Min(0f)] private float _noticeHoldDuration = 2f;

    private float _elapsed;
    private bool _fading;
    private bool _menuWasInteractable;
    private bool _showNotice;

    /// <summary>새 실행 세션에서 이전 실행의 페이드 요청을 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRequest()
    {
        _pending = false;
        _startupShown = false;
        _startupNoticeComplete = true;
    }

    /// <summary>다음 IntroScene 진입 시 검은 화면에서 페이드인하도록 요청한다.</summary>
    public static void RequestFadeIn()
    {
        _pending = true;
    }

    void Awake()
    {
        bool requested = _pending;
        _pending = false;
        if (_overlay == null || _menu == null)
        {
            Debug.LogError("메뉴 페이드 Overlay와 Menu 참조를 연결하세요.", this);
            _startupNoticeComplete = true;
            enabled = false;
            return;
        }
        _showNotice = !requested && !_startupShown && _headphoneNotice != null;
        _startupNoticeComplete = !_showNotice;
        if (_showNotice) _startupShown = true;
        _overlay.alpha = requested || _showNotice ? 1f : 0f;
        _overlay.blocksRaycasts = requested || _showNotice;
        _overlay.interactable = false;
        _fading = requested || _showNotice;
        if (_headphoneNotice != null)
        {
            _headphoneNotice.alpha = 0f;
            _headphoneNotice.gameObject.SetActive(_showNotice);
        }
        _menuWasInteractable = _menu.interactable;
        if (_fading) _menu.interactable = false;
    }

    void Start()
    {
        if (_showNotice) StartCoroutine(ShowHeadphoneNotice());
    }

    void Update()
    {
        if (!_fading || _showNotice) return;
        _elapsed += Time.unscaledDeltaTime;
        _overlay.alpha = 1f - Mathf.Clamp01(_elapsed / Mathf.Max(.01f, _duration));
        if (_overlay.alpha > 0f) return;
        _overlay.blocksRaycasts = false;
        _menu.interactable = _menuWasInteractable;
        _fading = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (_showNotice)
        {
            _showNotice = false;
            _startupNoticeComplete = true;
        }
        if (!_fading) return;
        if (_menu != null) _menu.interactable = _menuWasInteractable;
        _fading = false;
    }

    /// <summary>빌드의 Unity 로고 종료 후 안내 문구를 페이드인·유지·페이드아웃하고 기존 메뉴 페이드인으로 이어간다.</summary>
    private IEnumerator ShowHeadphoneNotice()
    {
        while (!Application.isEditor && !UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
        yield return FadeNotice(0f, 1f);
        float held = 0f;
        while (held < _noticeHoldDuration)
        {
            held += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return FadeNotice(1f, 0f);
        _headphoneNotice.gameObject.SetActive(false);
        _elapsed = 0f;
        _showNotice = false;
        _startupNoticeComplete = true;
    }

    /// <summary>헤드폰 안내 알파를 from에서 to까지 지정된 시간 동안 부드럽게 변경한다.</summary>
    private IEnumerator FadeNotice(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < _noticeFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _headphoneNotice.alpha = Mathf.Lerp(from, to,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _noticeFadeDuration)));
            yield return null;
        }
        _headphoneNotice.alpha = to;
    }
}
