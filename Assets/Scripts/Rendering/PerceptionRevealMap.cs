using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 걸음 파동의 크기·확산·겹 수·잔상 길이와 링 색상을 묶은 설정이다.
/// </summary>
[Serializable]
public struct RevealRippleSettings
{
    [SerializeField, Min(0.01f), Tooltip("파동이 시야를 여는 최대 반경(미터).")]
    private float _radius;
    [SerializeField, Min(0.01f), Tooltip("각 링이 최대 반경까지 퍼지는 게임 시간(초).")]
    private float _expandDuration;
    [SerializeField, Range(1, 8), Tooltip("한 걸음에 연달아 퍼지는 링 수.")]
    private int _ringCount;
    [SerializeField, Min(0f), Tooltip("다음 링이 출발하는 간격(초).")]
    private float _ringInterval;
    [SerializeField, Min(0.01f), Tooltip("링이 지나간 실루엣이 남아 있다가 사라지는 잔상 시간(초).")]
    private float _afterimage;
    [SerializeField, Tooltip("바닥 링 색상. 알파는 첫 링의 진하기다.")]
    private Color _ringColor;

    public float Radius => _radius;
    public float ExpandDuration => _expandDuration;
    public int RingCount => _ringCount;
    public float RingInterval => _ringInterval;
    public float Afterimage => _afterimage;
    public Color RingColor => _ringColor;

    /// <summary>
    /// radius·expandDuration·ringCount·ringInterval·afterimage·ringColor로 파동 설정을 만든다.
    /// </summary>
    public RevealRippleSettings(float radius, float expandDuration, int ringCount, float ringInterval,
        float afterimage, Color ringColor)
    {
        _radius = radius;
        _expandDuration = expandDuration;
        _ringCount = ringCount;
        _ringInterval = ringInterval;
        _afterimage = afterimage;
        _ringColor = ringColor;
    }
}

/// <summary>
/// 걸음 파동 전선과 이동 시야원이 지나간 XZ 지점의 잔상 만료 시각을 GPU 시야 맵에 누적해 윤곽선 셰이더에 전달한다.
/// 같은 규칙으로 CPU 가시성을 계산하며 걸음 파동의 바닥 링 시각도 함께 재생한다.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SensoryManager))]
public sealed class PerceptionRevealMap : MonoBehaviour
{
    private const int MAX_RIPPLES = 128;
    private const int MAX_STAMPS = 32;
    private const float RING_SURFACE_OFFSET = 0.02f;
    private static readonly int _revealMapId = Shader.PropertyToID("_OutlineRevealMap");
    private static readonly int _revealMapBoundsId = Shader.PropertyToID("_OutlineRevealBounds");
    private static readonly int _revealMapEnabledId = Shader.PropertyToID("_OutlineRevealEnabled");
    private static readonly int _boundsId = Shader.PropertyToID("_RevealBounds");
    private static readonly int _timeId = Shader.PropertyToID("_RevealTime");
    private static readonly int _edgeFadeId = Shader.PropertyToID("_RevealEdgeFade");
    private static readonly int _clearId = Shader.PropertyToID("_RevealClear");
    private static readonly int _rippleCountId = Shader.PropertyToID("_RevealRippleCount");
    private static readonly int _ripplesId = Shader.PropertyToID("_RevealRipples");
    private static readonly int _rippleParamsId = Shader.PropertyToID("_RevealRippleParams");
    private static readonly int _stampCountId = Shader.PropertyToID("_RevealStampCount");
    private static readonly int _stampsId = Shader.PropertyToID("_RevealStamps");

    [Header("시야 맵")]
    [SerializeField, Tooltip("Hidden/BlindBlade/PerceptionRevealUpdate 셰이더.")]
    private Shader _updateShader;
    [SerializeField, Tooltip("시야 맵이 덮는 XZ 최소 좌표(미터).")]
    private Vector2 _minimum = new Vector2(-40f, -40f);
    [SerializeField, Tooltip("시야 맵이 덮는 XZ 크기(미터).")]
    private Vector2 _size = new Vector2(80f, 80f);
    [SerializeField, Range(64, 1024), Tooltip("시야 맵 한 변의 텍셀 수.")]
    private int _resolution = 512;
    [SerializeField, Min(0.01f), Tooltip("공개 영역 바깥 경계에서 잔상이 짧아지며 흐려지는 거리(미터).")]
    private float _edgeFade = 0.5f;
    private RenderTexture[] _maps;
    private int _current;
    private Material _updateMaterial;
    private bool _clearPending;

    [Header("바닥 링")]
    [SerializeField, Tooltip("기존 파동 프리팹의 Ring SpriteRenderer. 시각 오브젝트만 복제한다.")]
    private SpriteRenderer _ringPrefab;
    [SerializeField, Range(0f, 1f), Tooltip("뒤따르는 링마다 곱하는 알파 배율.")]
    private float _laterRingAlpha = 0.7f;
    private readonly List<SpriteRenderer> _rings = new List<SpriteRenderer>(32);
    private Transform _ringRoot;

    [Header("런타임 상태")]
    private readonly List<Ripple> _ripples = new List<Ripple>(MAX_RIPPLES);
    private readonly List<Vector4> _pendingStamps = new List<Vector4>(MAX_STAMPS);
    private readonly List<Stamp> _stampHistory = new List<Stamp>(128);
    private readonly Vector4[] _rippleBuffer = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _rippleParamBuffer = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _stampBuffer = new Vector4[MAX_STAMPS];
    private SensoryManager _sensoryManager;

    private readonly struct Ripple
    {
        public readonly Vector3 Center;
        public readonly float StartedAt;
        public readonly RevealRippleSettings Settings;

        /// <summary>파동 중심 center와 시작 시각 startedAt, 설정 settings를 보관한다.</summary>
        public Ripple(Vector3 center, float startedAt, RevealRippleSettings settings)
        {
            Center = center;
            StartedAt = startedAt;
            Settings = settings;
        }

        public float LastRingStart => StartedAt + (Settings.RingCount - 1) * Settings.RingInterval;
        public float FrontEndTime => LastRingStart + Settings.ExpandDuration;
        public float ExpireTime => FrontEndTime + Settings.Afterimage;
    }

    private readonly struct Stamp
    {
        public readonly Vector2 Center;
        public readonly float Radius;
        public readonly float StampedAt;
        public readonly float Afterimage;

        /// <summary>이동 시야원의 XZ 중심 center와 radius, 기록 시각 stampedAt, 잔상 afterimage를 보관한다.</summary>
        public Stamp(Vector2 center, float radius, float stampedAt, float afterimage)
        {
            Center = center;
            Radius = radius;
            StampedAt = stampedAt;
            Afterimage = afterimage;
        }
    }

    void Awake()
    {
        _sensoryManager = GetComponent<SensoryManager>();
    }

    void OnEnable()
    {
        _clearPending = true;
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat(_revealMapEnabledId, 0f);
        _ripples.Clear();
        _pendingStamps.Clear();
        _stampHistory.Clear();
        foreach (SpriteRenderer ring in _rings)
            ring.enabled = false;
    }

    void OnDestroy()
    {
        ReleaseMaps();
        if (_updateMaterial != null)
            Destroy(_updateMaterial);
    }

    void LateUpdate()
    {
        if (!_sensoryManager.IsReady || !EnsureResources())
        {
            Shader.SetGlobalFloat(_revealMapEnabledId, 0f);
            return;
        }

        float now = Time.time;
        PruneHistory(now);
        UpdateMap(now);
        UpdateRings(now);
        Shader.SetGlobalTexture(_revealMapId, _maps[_current]);
        Shader.SetGlobalVector(_revealMapBoundsId, new Vector4(_minimum.x, _minimum.y, _size.x, _size.y));
        Shader.SetGlobalFloat(_revealMapEnabledId, 1f);
    }

    /// <summary>
    /// position에서 settings의 겹 수만큼 시간차 링을 퍼뜨려 지나간 지점의 실루엣을 잔상 시간 동안 공개한다.
    /// 바닥 링 시각도 함께 재생하며 감각 매니저가 준비되지 않았거나 반경이 0이면 무시한다.
    /// </summary>
    public void EmitRipple(Vector3 position, RevealRippleSettings settings)
    {
        if (!isActiveAndEnabled || !_sensoryManager.IsReady || settings.Radius <= 0f || settings.RingCount <= 0)
            return;
        _ripples.Add(new Ripple(position, Time.time, settings));
    }

    /// <summary>
    /// 이번 프레임 position 주변 radius(미터)를 공개하고 afterimage 동안 잔상을 남긴다.
    /// 매 프레임 호출하면 이동 경로를 따라 짧은 잔상 꼬리가 생긴다.
    /// </summary>
    public void RevealAround(Vector3 position, float radius, float afterimage)
    {
        if (!isActiveAndEnabled || !_sensoryManager.IsReady || radius <= 0f || afterimage <= 0f)
            return;
        if (_pendingStamps.Count == MAX_STAMPS)
            return;
        _pendingStamps.Add(new Vector4(position.x, position.z, radius, afterimage));
        _stampHistory.Add(new Stamp(new Vector2(position.x, position.z), radius, Time.time, afterimage));
    }

    /// <summary>
    /// position의 XZ 지점이 걸음 파동과 이동 시야원 잔상으로 보이는 정도를 셰이더와 같은 규칙으로 반환한다.
    /// 0은 보이지 않음, 1은 완전 공개이며 비활성·미준비 상태이면 0이다.
    /// </summary>
    public float GetVisibility(Vector3 position)
    {
        if (!isActiveAndEnabled || _sensoryManager == null || !_sensoryManager.IsReady)
            return 0f;

        float now = Time.time;
        Vector2 point = new Vector2(position.x, position.z);
        float expireTime = float.NegativeInfinity;
        float afterimage = 1f;
        foreach (Ripple ripple in _ripples)
        {
            RevealRippleSettings settings = ripple.Settings;
            float distance = Vector2.Distance(point, new Vector2(ripple.Center.x, ripple.Center.z));
            if (distance > settings.Radius)
                continue;
            float hitTime = HitTime(distance, settings);
            float edgeAfterimage = settings.Afterimage * EdgeFactor(distance, settings.Radius);
            for (int ring = 0; ring < settings.RingCount; ring++)
            {
                float ringStart = ripple.StartedAt + ring * settings.RingInterval;
                float candidate = ringStart + hitTime + edgeAfterimage;
                if (now >= ringStart + hitTime && candidate > expireTime)
                {
                    expireTime = candidate;
                    afterimage = settings.Afterimage;
                }
            }
        }
        foreach (Stamp stamp in _stampHistory)
        {
            float distance = Vector2.Distance(point, stamp.Center);
            if (distance > stamp.Radius)
                continue;
            float candidate = stamp.StampedAt + stamp.Afterimage * EdgeFactor(distance, stamp.Radius);
            if (candidate > expireTime)
            {
                expireTime = candidate;
                afterimage = stamp.Afterimage;
            }
        }
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((expireTime - now) / Mathf.Max(afterimage, 0.0001f)));
    }

    /// <summary>
    /// distance(미터) 지점을 settings의 감속 확산 전선이 지나는 링 출발 후 경과 시간을 반환한다.
    /// 반경 r = R(1-(1-t)^2)의 역함수이며 셰이더와 같은 식을 사용한다.
    /// </summary>
    private static float HitTime(float distance, RevealRippleSettings settings)
    {
        return settings.ExpandDuration * (1f - Mathf.Sqrt(Mathf.Clamp01(1f - distance / settings.Radius)));
    }

    /// <summary>distance가 radius 바깥 경계의 _edgeFade 구간에 들어갈수록 0에 가까워지는 잔상 배율을 반환한다.</summary>
    private float EdgeFactor(float distance, float radius)
    {
        float innerRadius = Mathf.Max(0f, radius - Mathf.Max(_edgeFade, 0.0001f));
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, radius, distance));
    }

    /// <summary>now 기준으로 잔상까지 끝난 파동과 이동 시야원 기록을 제거한다.</summary>
    private void PruneHistory(float now)
    {
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            if (now >= _ripples[i].ExpireTime)
                _ripples.RemoveAt(i);
        }
        for (int i = _stampHistory.Count - 1; i >= 0; i--)
        {
            if (now >= _stampHistory[i].StampedAt + _stampHistory[i].Afterimage)
                _stampHistory.RemoveAt(i);
        }
    }

    /// <summary>
    /// 전선이 아직 퍼지는 파동과 이번 프레임 이동 시야원을 갱신 셰이더에 넘겨 시야 맵을 다음 버퍼에 기록한다.
    /// 기록할 원천이 없으면 이전 맵을 그대로 두며 now는 게임 시간이다.
    /// </summary>
    private void UpdateMap(float now)
    {
        int rippleCount = 0;
        for (int i = _ripples.Count - 1; i >= 0 && rippleCount < MAX_RIPPLES; i--)
        {
            Ripple ripple = _ripples[i];
            if (now >= ripple.FrontEndTime + Time.deltaTime)
                continue;
            RevealRippleSettings settings = ripple.Settings;
            _rippleBuffer[rippleCount] = new Vector4(ripple.Center.x, ripple.Center.z, ripple.StartedAt, settings.Radius);
            _rippleParamBuffer[rippleCount++] = new Vector4(
                settings.ExpandDuration, settings.RingInterval, settings.Afterimage, settings.RingCount);
        }
        int stampCount = _pendingStamps.Count;
        for (int i = 0; i < stampCount; i++)
            _stampBuffer[i] = _pendingStamps[i];
        _pendingStamps.Clear();
        if (rippleCount == 0 && stampCount == 0 && !_clearPending)
            return;

        _updateMaterial.SetVector(_boundsId, new Vector4(_minimum.x, _minimum.y, _size.x, _size.y));
        _updateMaterial.SetFloat(_timeId, now);
        _updateMaterial.SetFloat(_edgeFadeId, _edgeFade);
        _updateMaterial.SetFloat(_clearId, _clearPending ? 1f : 0f);
        _updateMaterial.SetInt(_rippleCountId, rippleCount);
        _updateMaterial.SetVectorArray(_ripplesId, _rippleBuffer);
        _updateMaterial.SetVectorArray(_rippleParamsId, _rippleParamBuffer);
        _updateMaterial.SetInt(_stampCountId, stampCount);
        _updateMaterial.SetVectorArray(_stampsId, _stampBuffer);
        int next = 1 - _current;
        Graphics.Blit(_maps[_current], _maps[next], _updateMaterial);
        _current = next;
        _clearPending = false;
    }

    /// <summary>
    /// 퍼지는 중인 각 링에 풀의 바닥 링을 배정해 반경·알파를 now에 맞추고 남는 링은 숨긴다.
    /// 링 반경은 시야 전선과 같은 감속 확산식을 사용한다.
    /// </summary>
    private void UpdateRings(float now)
    {
        int used = 0;
        foreach (Ripple ripple in _ripples)
        {
            if (now >= ripple.FrontEndTime)
                continue;
            RevealRippleSettings settings = ripple.Settings;
            for (int ring = 0; ring < settings.RingCount; ring++)
            {
                float age = now - (ripple.StartedAt + ring * settings.RingInterval);
                if (age < 0f || age >= settings.ExpandDuration)
                    continue;
                float progress = age / settings.ExpandDuration;
                float radius = settings.Radius * (1f - (1f - progress) * (1f - progress));
                SpriteRenderer renderer = GetRing(used++);
                if (renderer == null)
                    return;
                renderer.transform.position = ripple.Center + Vector3.up * RING_SURFACE_OFFSET;
                renderer.transform.localScale = Vector3.one * (radius * 2f);
                Color color = settings.RingColor;
                color.a *= Mathf.Pow(_laterRingAlpha, ring) * (1f - Mathf.SmoothStep(0.5f, 1f, progress));
                renderer.color = color;
                renderer.enabled = true;
            }
        }
        for (int i = used; i < _rings.Count; i++)
            _rings[i].enabled = false;
    }

    /// <summary>index번째 풀 링을 반환하고 부족하면 원본 링을 복제해 바닥에 눕힌다. 원본이 없으면 null이다.</summary>
    private SpriteRenderer GetRing(int index)
    {
        if (_ringPrefab == null)
            return null;
        if (_ringRoot == null)
        {
            _ringRoot = new GameObject("RevealRings").transform;
            _ringRoot.SetParent(transform, false);
        }
        while (_rings.Count <= index)
        {
            SpriteRenderer ring = Instantiate(_ringPrefab, _ringRoot);
            ring.name = "RevealRing";
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ring.enabled = false;
            _rings.Add(ring);
        }
        return _rings[index];
    }

    /// <summary>
    /// 갱신 머티리얼과 두 장의 RGFloat 시야 맵을 준비하고 준비 성공 여부를 반환한다.
    /// 해상도가 바뀌면 다시 만들고 다음 갱신에서 맵을 비운다.
    /// </summary>
    private bool EnsureResources()
    {
        if (_updateShader == null)
            return false;
        if (_updateMaterial == null)
            _updateMaterial = new Material(_updateShader) { hideFlags = HideFlags.HideAndDontSave };
        if (_maps != null && _maps[0] != null && _maps[0].width == _resolution)
            return true;

        ReleaseMaps();
        _maps = new RenderTexture[2];
        for (int i = 0; i < _maps.Length; i++)
        {
            _maps[i] = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.RGFloat,
                RenderTextureReadWrite.Linear)
            {
                name = "Perception reveal " + i,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            _maps[i].Create();
        }
        _current = 0;
        _clearPending = true;
        return true;
    }

    /// <summary>생성한 시야 맵을 해제하고 참조를 비운다.</summary>
    private void ReleaseMaps()
    {
        if (_maps == null)
            return;
        foreach (RenderTexture map in _maps)
        {
            if (map == null)
                continue;
            map.Release();
            Destroy(map);
        }
        _maps = null;
    }
}
