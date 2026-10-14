using UnityEngine;
using UnityEngine.Serialization;

/// <summary>실제 플레이어 피해에서 짧은 붉은 비네팅을 표시한다. 기존 컴포넌트 이름과 GUID는 유지한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerLowHealthVignette : MonoBehaviour
{
    [Header("피격 비네팅")]
    [SerializeField] private Color _vignetteColor = new Color(0.85f, 0f, 0f, 1f);
    [SerializeField, Min(0.01f), Tooltip("피격 후 비네팅이 완전히 사라질 때까지의 실시간(초)")]
    private float _duration = 0.3f;
    [SerializeField, Range(0f, 1f), FormerlySerializedAs("_maxVignetteOpacity")]
    private float _peakOpacity = 0.45f;
    [SerializeField, Range(0.01f, 1f), FormerlySerializedAs("_maxVignetteWidth")]
    private float _width = 0.22f;
    [SerializeField, Range(0f, 0.1f), Tooltip("가장자리 일렁임의 두께 변화 진폭")]
    private float _rippleStrength = 0.02f;
    [SerializeField, Min(0f), Tooltip("일렁임 속도(초당 주기)")]
    private float _rippleSpeed = 0.8f;

    [Header("런타임 연결")]
    private PlayerHealth _health;
    private GameObject _canvasObject;
    private UnityEngine.UI.Image _vignette;
    private Material _vignetteMaterial;
    private float _startedAt;
    private bool _pulseActive;

    void Awake()
    {
        _health = GetComponent<PlayerHealth>();
        _canvasObject = new GameObject("LowHealthVignetteCanvas", typeof(RectTransform), typeof(Canvas));
        _canvasObject.transform.SetParent(transform, false);
        Canvas canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4;
        CreateVignette();
    }

    void OnEnable()
    {
        _health.DamageReceived += HandleDamageReceived;
        _canvasObject.SetActive(true);
        ResetPulse();
    }

    void Update()
    {
        if (!_pulseActive)
            return;
        float progress = Mathf.Clamp01((Time.unscaledTime - _startedAt) / _duration);
        ApplyVignette(1f - Mathf.SmoothStep(0f, 1f, progress));
        if (progress >= 1f)
            ResetPulse();
    }

    void OnDisable()
    {
        _health.DamageReceived -= HandleDamageReceived;
        ResetPulse();
        _canvasObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (_vignetteMaterial != null)
            Destroy(_vignetteMaterial);
    }

    /// <summary>context의 실제 피해 알림에서 펄스를 최대 강도로 다시 시작한다. HP 비율과 피해량은 강도에 반영하지 않는다.</summary>
    private void HandleDamageReceived(DamageContext context)
    {
        _startedAt = Time.unscaledTime;
        _pulseActive = true;
        ApplyVignette(1f);
    }

    /// <summary>입력 없이 전체 화면 Image와 전용 비네팅 머티리얼을 생성한다. 셰이더를 사용할 수 없으면 컴포넌트를 끈다.</summary>
    private void CreateVignette()
    {
        Shader shader = Resources.Load<Shader>("Effects/LowHealthVignette");
        if (shader == null || !shader.isSupported)
        {
            Debug.LogError("피격 비네팅 셰이더가 없거나 지원되지 않습니다.", this);
            enabled = false;
            return;
        }
        _vignetteMaterial = new Material(shader);
        GameObject imageObject = new GameObject("LowHealthVignette", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        imageObject.transform.SetParent(_canvasObject.transform, false);
        _vignette = imageObject.GetComponent<UnityEngine.UI.Image>();
        _vignette.color = Color.white;
        _vignette.raycastTarget = false;
        RectTransform rect = _vignette.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        _vignette.material = _vignetteMaterial;
        _vignette.enabled = false;
    }

    /// <summary>strength를 설정 불투명도에 곱해 표시한다. 화면 두께와 색상은 HP와 무관하게 Inspector 설정을 사용한다.</summary>
    private void ApplyVignette(float strength)
    {
        _vignetteMaterial.SetColor("_Color", _vignetteColor);
        _vignetteMaterial.SetFloat("_RippleStrength", _rippleStrength);
        _vignetteMaterial.SetFloat("_RippleSpeed", _rippleSpeed);
        _vignetteMaterial.SetFloat("_Opacity", _peakOpacity * strength);
        _vignetteMaterial.SetFloat("_Width", _width);
        _vignette.enabled = strength > 0f;
    }

    /// <summary>입력 없이 진행 중인 펄스를 취소하고 이미지를 숨긴다. 원본 체력은 변경하지 않는다.</summary>
    private void ResetPulse()
    {
        _pulseActive = false;
        if (_vignette != null)
            _vignette.enabled = false;
    }
}
