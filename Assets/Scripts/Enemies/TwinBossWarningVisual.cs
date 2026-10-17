using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 쌍둥이 보스가 공격을 준비하는 동안 연기 몸체를 근접 적 전조와 같은 붉은 테두리로 점차 물들이고,
/// 공격이 끝나면 짧게 흰색으로 되돌린다. 붉은 정도는 화면 윤곽선의 근접 경고 마스크가 읽는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBossWarningVisual : MonoBehaviour
{
    private static readonly HashSet<TwinBossWarningVisual> _active = new HashSet<TwinBossWarningVisual>();
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");

    [Header("연기 몸체")]
    [SerializeField, Tooltip("붉은 테두리를 적용할 연기 파티클 렌더러를 찾는 루트")]
    private Transform _smokeRoot;
    private ParticleSystemRenderer[] _renderers;
    private MaterialPropertyBlock _properties;

    [Header("흰색 복귀")]
    [SerializeField, Min(0.01f), Tooltip("공격이 끝난 뒤 흰색으로 돌아가는 시간(초)")]
    private float _whiteReturnDuration = 0.15f;
    private float _rampStartAt;
    private float _rampEndAt;
    private float _rampFrom;
    private bool _warning;
    private float _publishedIntensity = -1f;

    internal static IReadOnlyCollection<TwinBossWarningVisual> ActiveInstances => _active;
    public IReadOnlyList<Renderer> WarningRenderers => _renderers;
    public float Intensity { get; private set; }

    void Awake()
    {
        _properties = new MaterialPropertyBlock();
        _renderers = _smokeRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
    }

    void OnEnable()
    {
        Intensity = 0f;
        _warning = false;
        _active.Add(this);
    }

    void OnDisable()
    {
        _active.Remove(this);
        Intensity = 0f;
        _warning = false;
        Publish();
    }

    void LateUpdate()
    {
        if (_warning)
        {
            float duration = Mathf.Max(0.001f, _rampEndAt - _rampStartAt);
            Intensity = Mathf.Lerp(_rampFrom, 1f, Mathf.Clamp01((Time.time - _rampStartAt) / duration));
        }
        else
        {
            Intensity = Mathf.MoveTowards(Intensity, 0f, Time.deltaTime / _whiteReturnDuration);
        }
        Publish();
    }

    /// <summary>
    /// 지금 붉은 정도에서 시작해 duration(초, 게임 시간) 동안 1까지 붉게 물들이고 EndWarning 전까지 유지한다.
    /// _rampStartAt·_rampEndAt·_rampFrom을 기록하며 duration이 0이면 바로 붉게 만든다.
    /// </summary>
    public void BeginWarning(float duration)
    {
        _rampFrom = Intensity;
        _rampStartAt = Time.time;
        _rampEndAt = Time.time + Mathf.Max(0f, duration);
        _warning = true;
    }

    /// <summary>붉은 유지 상태를 끝내고 _whiteReturnDuration 동안 흰색으로 되돌린다.</summary>
    public void EndWarning()
    {
        _warning = false;
    }

    /// <summary>
    /// 현재 Intensity가 바뀌었으면 연기 렌더러마다 파티클 텍스처·기본색과 경고 세기를 속성 블록으로 넘긴다.
    /// 근접 경고 마스크 재질이 같은 속성을 읽으며 렌더러의 기존 속성 블록 값은 보존한다.
    /// </summary>
    private void Publish()
    {
        if (Mathf.Approximately(_publishedIntensity, Intensity)) return;
        _publishedIntensity = Intensity;
        foreach (ParticleSystemRenderer renderer in _renderers)
        {
            Material source = renderer.sharedMaterial;
            if (source == null || !source.HasProperty(_baseMapId) || !source.HasProperty(_baseColorId)) continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
            _properties.SetColor(_baseColorId, source.GetColor(_baseColorId));
            _properties.SetFloat(_warningIntensityId, Intensity);
            renderer.SetPropertyBlock(_properties);
        }
    }
}
