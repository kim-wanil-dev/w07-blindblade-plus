using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 쌍둥이 보스가 공격을 준비하는 동안 연기 몸체를 근접 적 전조와 같은 붉은 테두리로 점차 물들이고,
/// 공격이 끝나면 짧게 흰색으로 되돌린다. 붉은 정도는 화면 윤곽선의 근접 경고 마스크가 읽는다.
/// 돌진처럼 SetAlwaysVisible로 켠 동안에는 파동으로 드러나지 않아도 붉은 테두리를 그리도록 마스크에 따로 알린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBossWarningVisual : MonoBehaviour
{
    private static readonly HashSet<TwinBossWarningVisual> _active = new HashSet<TwinBossWarningVisual>();
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");
    private static readonly int _warningAlwaysVisibleId = Shader.PropertyToID("_MeleeWarningAlwaysVisible");
    private static readonly int _warningSeparateGlintId = Shader.PropertyToID("_MeleeWarningSeparateGlint");
    private static readonly int _warningThickGlintId = Shader.PropertyToID("_MeleeWarningThickGlint");

    [Header("연기 몸체")]
    [SerializeField, Tooltip("붉은 테두리를 적용할 연기 파티클 렌더러를 찾는 루트")]
    private Transform _smokeRoot;
    private ParticleSystemRenderer[] _renderers;
    private readonly List<Renderer> _warningRenderers = new List<Renderer>();
    private MaterialPropertyBlock _properties;

    [Header("흰색 복귀")]
    [SerializeField, Min(0.01f), Tooltip("공격이 끝난 뒤 흰색으로 돌아가는 시간(초)")]
    private float _whiteReturnDuration = 0.15f;
    private float _rampStartAt;
    private float _rampEndAt;
    private float _rampFrom;
    private bool _warning;
    private bool _alwaysVisible;
    private float _publishedIntensity = -1f;
    private bool _publishedAlwaysVisible;

    internal static IReadOnlyCollection<TwinBossWarningVisual> ActiveInstances => _active;
    public IReadOnlyList<Renderer> WarningRenderers => _warningRenderers;
    public float Intensity { get; private set; }

    void Awake()
    {
        _properties = new MaterialPropertyBlock();
        _renderers = _smokeRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
        _warningRenderers.AddRange(_renderers);
    }

    void OnEnable()
    {
        Intensity = 0f;
        _warning = false;
        _alwaysVisible = false;
        _active.Add(this);
    }

    void OnDisable()
    {
        _active.Remove(this);
        Intensity = 0f;
        _warning = false;
        _alwaysVisible = false;
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

    /// <summary>
    /// visible이 참인 동안 파동으로 드러나지 않아도 붉은 정도만큼 연기 몸체의 테두리를 붉게 그리게 한다.
    /// 돌진을 시작할 때 켜고 착지하면 끄며, EndWarning도 함께 끈다. _alwaysVisible을 바꾼다.
    /// </summary>
    public void SetAlwaysVisible(bool visible)
    {
        _alwaysVisible = visible;
    }

    /// <summary>
    /// root 아래 연기 파티클 렌더러를 붉은 테두리 대상(_renderers)에 더해 몸통 연기와 같은 경고 세기·항상 보임 여부를 받게 한다.
    /// 몸체 밖에서 만든 뒤 붙인 늑대 눈 연기처럼 Awake 뒤에 생긴 연기에 쓴다. 다음 LateUpdate에 지금 값을 다시 넘긴다.
    /// </summary>
    public void AddRenderers(Transform root)
    {
        ParticleSystemRenderer[] added = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
        ParticleSystemRenderer[] merged = new ParticleSystemRenderer[_renderers.Length + added.Length];
        _renderers.CopyTo(merged, 0);
        added.CopyTo(merged, _renderers.Length);
        _renderers = merged;
        _warningRenderers.AddRange(added);
        // 새 렌더러에도 값이 들어가도록 다음 Publish가 건너뛰지 않게 한다.
        _publishedIntensity = -1f;
    }

    /// <summary>
    /// root 아래 파티클 렌더러를 몸통 경고 세기와 상관없이 근접 경고 마스크 대상에 더한다.
    /// shape가 있으면 재질 텍스처 대신 그 모양을 사용한다. separateGlintOutline이 참이면 파티클 채움을 숨기고 몸통과 다른 채널에 눈 섬광 테두리를 기록한다.
    /// thickGlintOutline이 참이면 눈 섬광을 두꺼운 테두리 전용 채널에 기록한다. 속성 블록은 한 번만 넘기며 Publish가 덮어쓰지 않는다.
    /// </summary>
    public void AddRedFlashRenderers(Transform root, Texture shape = null, bool separateGlintOutline = false,
        bool thickGlintOutline = false)
    {
        foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            Material source = renderer.sharedMaterial;
            renderer.GetPropertyBlock(_properties);
            if (source != null && source.HasProperty(_baseMapId) && source.HasProperty(_baseColorId))
            {
                _properties.SetTexture(_baseMapId, shape != null ? shape : source.GetTexture(_baseMapId));
                _properties.SetColor(_baseColorId, separateGlintOutline ? Color.clear : source.GetColor(_baseColorId));
            }
            _properties.SetFloat(_warningIntensityId, 1f);
            _properties.SetFloat(_warningAlwaysVisibleId, separateGlintOutline ? 0f : 1f);
            _properties.SetFloat(_warningSeparateGlintId, separateGlintOutline ? 1f : 0f);
            _properties.SetFloat(_warningThickGlintId, separateGlintOutline && thickGlintOutline ? 1f : 0f);
            renderer.SetPropertyBlock(_properties);
            _warningRenderers.Add(renderer);
        }
    }

    /// <summary>
    /// root 아래 눈 섬광 파티클의 내부 채움을 숨기고 shape 모양을 몸통의 붉은 테두리와 분리된 마스크 채널에 기록한다.
    /// thickOutline이 참이면 기본 섬광보다 두꺼운 테두리로 그린다. 등록된 렌더러를 _warningRenderers에 더한다.
    /// </summary>
    public void AddGlintRenderers(Transform root, Texture shape, bool thickOutline = false)
    {
        AddRedFlashRenderers(root, shape, true, thickOutline);
    }

    /// <summary>붉은 유지 상태와 파동 없는 붉은 테두리를 끝내고 _whiteReturnDuration 동안 흰색으로 되돌린다.</summary>
    public void EndWarning()
    {
        _warning = false;
        _alwaysVisible = false;
    }

    /// <summary>
    /// 현재 Intensity나 파동 없는 테두리 여부가 바뀌었으면 연기 렌더러마다 파티클 텍스처·기본색과 경고 세기·항상 보임 여부를 속성 블록으로 넘긴다.
    /// 근접 경고 마스크 재질이 같은 속성을 읽으며 렌더러의 기존 속성 블록 값은 보존한다.
    /// </summary>
    private void Publish()
    {
        if (Mathf.Approximately(_publishedIntensity, Intensity) && _publishedAlwaysVisible == _alwaysVisible) return;
        _publishedIntensity = Intensity;
        _publishedAlwaysVisible = _alwaysVisible;
        foreach (ParticleSystemRenderer renderer in _renderers)
        {
            Material source = renderer.sharedMaterial;
            if (source == null || !source.HasProperty(_baseMapId) || !source.HasProperty(_baseColorId)) continue;
            renderer.GetPropertyBlock(_properties);
            _properties.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
            _properties.SetColor(_baseColorId, source.GetColor(_baseColorId));
            _properties.SetFloat(_warningIntensityId, Intensity);
            _properties.SetFloat(_warningAlwaysVisibleId, _alwaysVisible ? 1f : 0f);
            renderer.SetPropertyBlock(_properties);
        }
    }
}
