using System.Collections;

using UnityEngine;

using UnityEngine.UI;

/// <summary>보스 체력 사건을 화면 상단 체력바에 연결하고 보스와 함께 표시 수명을 관리한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public sealed class BossHealthBar : MonoBehaviour
{
    [Header("체력바 표시")]
    [SerializeField] private Vector2 _size = new Vector2(420f, 16f);
    [SerializeField, Min(0f)] private float _topOffset = 72f;
    [SerializeField, Min(0.01f), Tooltip("지연 바가 현재 체력에 도달하는 시간(초)")] private float _followDuration = 0.8f;
    [SerializeField] private Color _followColor = Color.red;
    private EnemyHealth _health;
    private GameObject _canvasObject;
    private Image _fill;
    private Image _followFill;
    private float _followRatio;
    private Coroutine _followRoutine;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _canvasObject = new GameObject("BossHealthCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _canvasObject.transform.SetParent(transform, false);
        Canvas canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        CanvasScaler scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        Image background = CreateImage("Background", _canvasObject.transform, new Color(0.15f, 0.15f, 0.15f, 0.9f));
        RectTransform rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -_topOffset);
        rect.sizeDelta = _size;
        _followFill = CreateImage("DelayedHealth", rect, _followColor);
        _followFill.rectTransform.anchorMin = Vector2.zero;
        _followFill.rectTransform.anchorMax = Vector2.one;
        _followFill.rectTransform.offsetMin = new Vector2(2f, 2f);
        _followFill.rectTransform.offsetMax = new Vector2(-2f, -2f);
        _fill = CreateImage("Health", rect, Color.white);
        _fill.rectTransform.anchorMin = Vector2.zero;
        _fill.rectTransform.anchorMax = Vector2.one;
        _fill.rectTransform.offsetMin = new Vector2(2f, 2f);
        _fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
    }

    void OnEnable()
    {
        _health.HealthChanged += HandleHealthChanged;
        _canvasObject.SetActive(true);
        ResetBars();
    }

    void Start()
    {
        ResetBars();
    }

    void OnDisable()
    {
        _health.HealthChanged -= HandleHealthChanged;
        StopFollowing();
        _canvasObject.SetActive(false);
    }

    /// <summary>name과 color로 입력을 가로채지 않는 UI 이미지를 parent 아래 생성해 반환한다.</summary>
    private Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject instance = new GameObject(name, typeof(RectTransform), typeof(Image));
        instance.transform.SetParent(parent, false);
        Image image = instance.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>current와 maximum의 비율을 즉시 바에 반영하고 지연 바를 새 목표로 이동시킨다.</summary>
    private void HandleHealthChanged(float current, float maximum)
    {
        float ratio = Mathf.Clamp01(current / maximum);
        SetRatio(_fill, ratio);
        StopFollowing();
        // 회복 시에도 두 값의 차이를 볼 수 있도록 지연 바를 앞에 표시한다.
        if (_followRatio < ratio)
        {
            _followFill.transform.SetAsLastSibling();
        }
        else
        {
            _followFill.transform.SetAsFirstSibling();
        }
        if (Mathf.Approximately(_followRatio, ratio))
        {
            SetRatio(_followFill, ratio);
            return;
        }
        _followRoutine = StartCoroutine(FollowHealth(ratio));
    }

    /// <summary>최신 체력으로 두 바를 즉시 맞춰 최초 표시와 재활성화 시 이전 잔상을 제거한다.</summary>
    private void ResetBars()
    {
        StopFollowing();
        _followRatio = Mathf.Clamp01(_health.CurrentHealth / _health.MaxHealth);
        SetRatio(_fill, _followRatio);
        SetRatio(_followFill, _followRatio);
        _followFill.transform.SetAsFirstSibling();
    }

    /// <summary>지연 바의 현재 비율에서 targetRatio까지 설정된 실제 시간 동안 부드럽게 이동한다.</summary>
    private IEnumerator FollowHealth(float targetRatio)
    {
        float startRatio = _followRatio;
        float elapsed = 0f;
        while (elapsed < _followDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _followDuration));
            _followRatio = Mathf.Lerp(startRatio, targetRatio, Mathf.SmoothStep(0f, 1f, progress));
            SetRatio(_followFill, _followRatio);
            yield return null;
        }
        _followRatio = targetRatio;
        SetRatio(_followFill, targetRatio);
        _followFill.transform.SetAsFirstSibling();
        _followRoutine = null;
    }

    /// <summary>진행 중인 체력 추적 코루틴을 중지하고 저장한 실행 참조를 정리한다.</summary>
    private void StopFollowing()
    {
        if (_followRoutine == null)
        {
            return;
        }
        StopCoroutine(_followRoutine);
        _followRoutine = null;
    }

    /// <summary>image의 오른쪽 경계와 표시 여부를 ratio에 맞춰 갱신한다.</summary>
    private void SetRatio(Image image, float ratio)
    {
        image.rectTransform.anchorMax = new Vector2(ratio, 1f);
        image.enabled = ratio > 0f;
    }
}
