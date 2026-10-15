using UnityEngine;


[DisallowMultipleComponent]
[RequireComponent(typeof(Poolable))]
public sealed class FootstepWave : SensoryEffect
{
    [Header("파동 표시")]
    [SerializeField] private SpriteRenderer _ringRenderer;
    [SerializeField, Tooltip("기본 파동 링 색상")]
    private Color _ringColor = Color.white;
    [SerializeField, Min(0.01f), Tooltip("확산 및 표시 수명(초)")]
    private float _duration = 1.4f;
    [SerializeField, Min(0f), Tooltip("시작 반경(미터), 원본 링 지름은 1미터 기준")]
    private float _startRadius = 0.05f;
    [SerializeField, Min(0f), Tooltip("종료 반경(미터)")]
    private float _endRadius = 1.5f;
    private bool _isConfigured;
    private Color _playColor;
    private float _playEndRadius;

    public float Duration => _duration;
    public float StartRadius => _startRadius;
    public float EndRadius => _endRadius;

    void Awake()
    {
        _isConfigured = _ringRenderer != null;
        if (!_isConfigured)
        {
            Debug.LogError("파동 프리팹에 링 SpriteRenderer를 연결해야 합니다.", this);
            return;
        }
        _ringRenderer.color = _ringColor;
        _ringRenderer.enabled = false;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_isConfigured)
            _ringRenderer.enabled = false;
    }

    /// <summary>
    /// 가시성과 인스펙터의 수명·반경으로 파동을 재생한다.
    /// visibility를 사용하며, 풀에서 재사용할 때 링 알파와 시작 크기를 초기화한다.
    /// </summary>
    public void Play(float visibility = 1f)
    {
        Play(_ringColor, _endRadius, _duration, visibility);
    }

    /// <summary>
    /// color, endRadius, duration, visibility로 이번 링의 색·크기·수명을 설정하고 재생한다.
    /// 재생 중인 링의 표시 상태만 변경하며 프리팹 기본값은 유지한다.
    /// </summary>
    public void Play(Color color, float endRadius, float duration, float visibility = 1f)
    {
        if (!_isConfigured)
        {
            Managers.Resource.Destroy(gameObject);
            return;
        }
        _playColor = color;
        _playEndRadius = Mathf.Max(_startRadius, endRadius);
        Color startColor = _playColor;
        startColor.a *= Mathf.Clamp01(visibility);
        _ringRenderer.color = startColor;
        _ringRenderer.transform.localScale = Vector3.one * (_startRadius * 2f);
        _ringRenderer.enabled = true;
        BeginPlayback(duration, visibility);
    }

    /// <summary>
    /// 정규화 진행률과 외부 가시성으로 링 반경과 페이드 알파를 갱신한다.
    /// </summary>
    protected override void ApplyFrame(float elapsed, float progress)
    {
        float expansion = Mathf.SmoothStep(0f, 1f, progress);
        _ringRenderer.transform.localScale = Vector3.one
            * (Mathf.Lerp(_startRadius, _playEndRadius, expansion) * 2f);
        Color color = _playColor;
        color.a *= (1f - Mathf.SmoothStep(0f, 1f, progress)) * Visibility;
        _ringRenderer.color = color;
    }
}
