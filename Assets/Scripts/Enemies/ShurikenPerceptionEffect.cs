using UnityEngine;

/// <summary>
/// 수리검의 현재 인지 밖 외형을 숨기고 기존 파동의 링만 작은 위치 신호로 반복 표시한다.
/// 컴포넌트를 끄면 원래 렌더러 상태를 복원하며 감각 사건과 지형 기억은 변경하지 않는다.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class ShurikenPerceptionEffect : MonoBehaviour
{
    [Header("인지 밖 파동 표시")]
    [SerializeField, Tooltip("기존 파동 프리팹의 Ring SpriteRenderer. 시각 오브젝트만 복제한다.")]
    private SpriteRenderer _waveVisualPrefab;
    [SerializeField, Min(0.01f), Tooltip("인지 밖에서 작은 파동을 시작하는 간격(초)")]
    private float _pulseInterval = 0.3f;
    [SerializeField, Min(0.01f)] private float _pulseDuration = 0.25f;
    [SerializeField, Min(0f)] private float _startRadius = 0.04f;
    [SerializeField, Min(0f)] private float _endRadius = 0.25f;
    [SerializeField] private Color _pulseColor = Color.white;
    private SpriteRenderer _waveVisual;
    private float _nextPulseTime;
    private float _pulseStartedAt;

    [Header("런타임 연결")]
    private Renderer[] _modelRenderers;
    private bool[] _originalVisibility;
    private SensoryPerceptionMask _perception;
    private CombatThreatPresentationProvider _presentationProvider;
    private Camera _playerCamera;

    void Awake()
    {
        _modelRenderers = GetComponentsInChildren<Renderer>(true);
        _originalVisibility = new bool[_modelRenderers.Length];
        for (int i = 0; i < _modelRenderers.Length; i++)
            _originalVisibility[i] = _modelRenderers[i].enabled;
        _pulseStartedAt = float.NegativeInfinity;
    }

    void LateUpdate()
    {
        if (!Game.IsInitialized || Game.Player == null)
        {
            SetModelVisible(false);
            HidePulse();
            return;
        }
        if (_perception == null)
            _perception = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        if (_presentationProvider == null)
            _presentationProvider = FindFirstObjectByType<CombatThreatPresentationProvider>();
        if (_playerCamera == null)
            _playerCamera = Game.Player.GetComponentInChildren<Camera>();

        float visibilityThreshold = _presentationProvider != null
            ? _presentationProvider.VisibilityThreshold : 0f;
        bool visible = _perception.GetVisibility(transform.position) > visibilityThreshold;
        SetModelVisible(visible);
        if (visible)
        {
            HidePulse();
            return;
        }
        UpdatePulse();
    }

    void OnDisable()
    {
        if (_modelRenderers != null)
            SetModelVisible(true);
        HidePulse();
    }

    /// <summary>
    /// visible에 따라 모델 렌더러를 숨기거나 Awake에서 기록한 활성 상태로 복원한다.
    /// 이동·충돌·피해 판정과 원본 머티리얼은 변경하지 않는다.
    /// </summary>
    private void SetModelVisible(bool visible)
    {
        for (int i = 0; i < _modelRenderers.Length; i++)
            _modelRenderers[i].enabled = visible && _originalVisibility[i];
    }

    /// <summary>
    /// 현재 시각과 파동 설정으로 기존 링의 확산·페이드를 반복 갱신한다.
    /// 링은 수리검을 따라가며 플레이어 카메라를 향하고 인지 영역을 생성하지 않는다.
    /// </summary>
    private void UpdatePulse()
    {
        if (_waveVisualPrefab == null || _playerCamera == null)
            return;
        if (_waveVisual == null)
        {
            _waveVisual = Instantiate(_waveVisualPrefab, transform);
            _waveVisual.name = "Shuriken perception pulse";
            _waveVisual.transform.localPosition = Vector3.zero;
        }
        if (Time.time >= _nextPulseTime)
        {
            _pulseStartedAt = Time.time;
            _nextPulseTime = Time.time + _pulseInterval;
        }
        float progress = Mathf.Clamp01((Time.time - _pulseStartedAt) / _pulseDuration);
        _waveVisual.enabled = progress < 1f;
        _waveVisual.transform.rotation = _playerCamera.transform.rotation;
        _waveVisual.transform.localScale = Vector3.one
            * (Mathf.Lerp(_startRadius, _endRadius, Mathf.SmoothStep(0f, 1f, progress)) * 2f);
        Color color = _pulseColor;
        color.a *= 1f - Mathf.SmoothStep(0f, 1f, progress);
        _waveVisual.color = color;
    }

    /// <summary>
    /// 입력 없이 파동을 숨기고 반복 시각을 초기화한다.
    /// 다음 인지 밖 진입에서는 첫 파동을 즉시 시작한다.
    /// </summary>
    private void HidePulse()
    {
        if (_waveVisual != null)
            _waveVisual.enabled = false;
        _nextPulseTime = 0f;
        _pulseStartedAt = float.NegativeInfinity;
    }
}
