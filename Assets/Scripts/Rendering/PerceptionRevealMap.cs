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
/// 걸음 파동 전선이 지나간 XZ 지점의 잔상 만료 시각을 GPU 시야 맵에 누적하고, 이동 시야원의 구형 공개 영역과 함께 윤곽선 셰이더에 전달한다.
/// 발생자 몸체를 밝히지 않는 파동은 별도 채널에 기록하고 등록된 발생자 주변에서는 그 채널을 제외한다.
/// 같은 규칙으로 CPU 가시성을 계산하며 걸음 파동의 바닥 링 시각도 함께 재생한다.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SensoryManager))]
public sealed class PerceptionRevealMap : MonoBehaviour
{
    private const int MAX_RIPPLES = 128;
    private const int MAX_SPHERES = 32;
    private const int MAX_HIDDEN_OWNERS = 32;
    private const float RING_SURFACE_OFFSET = 0.02f;
    private const float OWNER_HIDE_EDGE = 0.2f;
    private static readonly int _revealMapId = Shader.PropertyToID("_OutlineRevealMap");
    private static readonly int _revealMapBoundsId = Shader.PropertyToID("_OutlineRevealBounds");
    private static readonly int _revealMapEnabledId = Shader.PropertyToID("_OutlineRevealEnabled");
    private static readonly int _sphereCountId = Shader.PropertyToID("_OutlineRevealSphereCount");
    private static readonly int _spheresId = Shader.PropertyToID("_OutlineRevealSpheres");
    private static readonly int _sphereStrengthsId = Shader.PropertyToID("_OutlineRevealSphereStrengths");
    private static readonly int _hiddenOwnerCountId = Shader.PropertyToID("_OutlineHiddenOwnerCount");
    private static readonly int _hiddenOwnersId = Shader.PropertyToID("_OutlineHiddenOwners");
    private static readonly int _hiddenSceneryHeightId = Shader.PropertyToID("_OutlineHiddenSceneryHeight");
    private static readonly int _boundsId = Shader.PropertyToID("_RevealBounds");
    private static readonly int _timeId = Shader.PropertyToID("_RevealTime");
    private static readonly int _edgeFadeId = Shader.PropertyToID("_RevealEdgeFade");
    private static readonly int _clearId = Shader.PropertyToID("_RevealClear");
    private static readonly int _rippleCountId = Shader.PropertyToID("_RevealRippleCount");
    private static readonly int _ripplesId = Shader.PropertyToID("_RevealRipples");
    private static readonly int _rippleParamsId = Shader.PropertyToID("_RevealRippleParams");

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

    [Header("발생자 몸체 제외")]
    [SerializeField, Min(0.01f), Tooltip("몸체를 밝히지 않는 파동의 발생자 주변에서 그 파동 시야를 빼는 수평 반경(미터).")]
    private float _ownerHideRadius = 0.75f;
    private readonly List<Transform> _hiddenOwners = new List<Transform>(MAX_HIDDEN_OWNERS);
    private readonly Vector4[] _hiddenOwnerBuffer = new Vector4[MAX_HIDDEN_OWNERS];
    private int _hiddenOwnerCount;

    [Header("적 파동의 배경 높이 제한")]
    [SerializeField, Min(0f), Tooltip("몸체를 밝히지 않는 적 파동이 배경(대나무)을 온전히 밝히는 파동 바닥 기준 높이(미터).")]
    private float _hiddenSceneryRevealHeight = 1f;
    [SerializeField, Min(0.01f), Tooltip("위 높이부터 적 파동의 배경 공개가 0이 될 때까지 흐려지는 높이(미터).")]
    private float _hiddenSceneryFadeHeight = 1f;
    private float _hiddenGroundY;

    [Header("바닥 링")]
    [SerializeField, Tooltip("기존 파동 프리팹의 Ring SpriteRenderer. 시각 오브젝트만 복제한다.")]
    private SpriteRenderer _ringPrefab;
    [SerializeField, Range(0f, 1f), Tooltip("뒤따르는 링마다 곱하는 알파 배율.")]
    private float _laterRingAlpha = 0.7f;
    private readonly List<SpriteRenderer> _rings = new List<SpriteRenderer>(32);
    private Transform _ringRoot;

    [Header("런타임 상태")]
    private readonly List<Ripple> _ripples = new List<Ripple>(MAX_RIPPLES);
    private readonly List<Sphere> _pendingHeads = new List<Sphere>(MAX_SPHERES);
    private readonly List<Sphere> _heads = new List<Sphere>(MAX_SPHERES);
    private readonly List<Sphere> _trail = new List<Sphere>(MAX_SPHERES * 2);
    private readonly Vector4[] _rippleBuffer = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _rippleParamBuffer = new Vector4[MAX_RIPPLES];
    private readonly Vector4[] _sphereBuffer = new Vector4[MAX_SPHERES];
    private readonly Vector4[] _sphereStrengthBuffer = new Vector4[MAX_SPHERES];
    private SensoryManager _sensoryManager;

    private readonly struct Ripple
    {
        public readonly Vector3 Center;
        public readonly float StartedAt;
        public readonly RevealRippleSettings Settings;
        public readonly bool HideOwner;

        /// <summary>파동 중심 center와 시작 시각 startedAt, 설정 settings, 발생자 몸체 제외 여부 hideOwner를 보관한다.</summary>
        public Ripple(Vector3 center, float startedAt, RevealRippleSettings settings, bool hideOwner)
        {
            Center = center;
            StartedAt = startedAt;
            Settings = settings;
            HideOwner = hideOwner;
        }

        public float LastRingStart => StartedAt + (Settings.RingCount - 1) * Settings.RingInterval;
        public float FrontEndTime => LastRingStart + Settings.ExpandDuration;
        public float ExpireTime => FrontEndTime + Settings.Afterimage;
    }

    private readonly struct Sphere
    {
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly float RecordedAt;
        public readonly float Afterimage;
        public readonly float Strength;

        /// <summary>구형 공개 영역의 중심 center와 radius, 기록 시각 recordedAt, 잔상 afterimage, 최대 강도 strength를 보관한다.</summary>
        public Sphere(Vector3 center, float radius, float recordedAt, float afterimage, float strength)
        {
            Center = center;
            Radius = radius;
            RecordedAt = recordedAt;
            Afterimage = afterimage;
            Strength = strength;
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
        Shader.SetGlobalInt(_sphereCountId, 0);
        Shader.SetGlobalInt(_hiddenOwnerCountId, 0);
        _ripples.Clear();
        _pendingHeads.Clear();
        _heads.Clear();
        _trail.Clear();
        _hiddenOwners.Clear();
        _hiddenOwnerCount = 0;
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
            Shader.SetGlobalInt(_sphereCountId, 0);
            Shader.SetGlobalInt(_hiddenOwnerCountId, 0);
            return;
        }

        float now = Time.time;
        PruneHistory(now);
        UpdateMap(now);
        UpdateRings(now);
        PublishSpheres(now);
        PublishHiddenOwners();
        Shader.SetGlobalVector(_hiddenSceneryHeightId,
            new Vector4(_hiddenGroundY, _hiddenSceneryRevealHeight, _hiddenSceneryFadeHeight, 0f));
        Shader.SetGlobalTexture(_revealMapId, _maps[_current]);
        Shader.SetGlobalVector(_revealMapBoundsId, new Vector4(_minimum.x, _minimum.y, _size.x, _size.y));
        Shader.SetGlobalFloat(_revealMapEnabledId, 1f);
    }

    /// <summary>
    /// position에서 settings의 겹 수만큼 시간차 링을 퍼뜨려 지나간 지점의 실루엣을 잔상 시간 동안 공개한다.
    /// owner를 넘기면 그 발생자 주변 몸체는 이 파동으로 밝히지 않고, 배경(대나무)은 position 높이 기준으로 일정 높이까지만 밝힌다.
    /// 바닥 링 시각도 함께 재생하며 등록했으면 true, 미준비 상태나 반경 0이면 false를 반환한다.
    /// </summary>
    public bool EmitRipple(Vector3 position, RevealRippleSettings settings, Transform owner = null)
    {
        if (!isActiveAndEnabled || !_sensoryManager.IsReady || settings.Radius <= 0f || settings.RingCount <= 0)
            return false;
        bool hideOwner = owner != null;
        if (hideOwner && !_hiddenOwners.Contains(owner) && _hiddenOwners.Count < MAX_HIDDEN_OWNERS)
            _hiddenOwners.Add(owner);
        // 적 파동 채널은 하나라서 가장 최근 적 파동의 바닥 높이를 배경 높이 제한 기준으로 쓴다.
        if (hideOwner) _hiddenGroundY = position.y;
        _ripples.Add(new Ripple(position, Time.time, settings, hideOwner));
        return true;
    }

    /// <summary>
    /// 이번 프레임 position을 중심으로 radius(미터) 구형 영역의 실루엣을 중심에서 가장자리로 흐려지게 강조한다.
    /// strength(0~1)는 중심의 최대 공개 정도이며, recordTrail이 참이면 그 위치를 afterimage 동안 흐려지는 잔상으로 남긴다.
    /// </summary>
    public void RevealAround(Vector3 position, float radius, float afterimage, bool recordTrail, float strength = 1f)
    {
        if (!isActiveAndEnabled || !_sensoryManager.IsReady || radius <= 0f || strength <= 0f)
            return;
        strength = Mathf.Clamp01(strength);
        if (_pendingHeads.Count < MAX_SPHERES)
            _pendingHeads.Add(new Sphere(position, radius, Time.time, afterimage, strength));
        if (recordTrail && afterimage > 0f)
            _trail.Add(new Sphere(position, radius, Time.time, afterimage, strength));
    }

    /// <summary>
    /// position이 걸음 파동 잔상과 구형 공개 영역으로 보이는 정도를 셰이더와 같은 규칙으로 반환한다.
    /// 몸체를 밝히지 않는 파동은 등록된 발생자 주변에서 제외하며, 0은 보이지 않음, 1은 완전 공개다. 미준비 상태이면 0이다.
    /// </summary>
    public float GetVisibility(Vector3 position)
    {
        if (!isActiveAndEnabled || _sensoryManager == null || !_sensoryManager.IsReady)
            return 0f;

        float now = Time.time;
        Vector2 point = new Vector2(position.x, position.z);
        float generalExpire = float.NegativeInfinity;
        float generalAfterimage = 1f;
        float hiddenExpire = float.NegativeInfinity;
        float hiddenAfterimage = 1f;
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
                if (now < ringStart + hitTime)
                    continue;
                float candidate = ringStart + hitTime + edgeAfterimage;
                if (ripple.HideOwner && candidate > hiddenExpire)
                {
                    hiddenExpire = candidate;
                    hiddenAfterimage = settings.Afterimage;
                }
                else if (!ripple.HideOwner && candidate > generalExpire)
                {
                    generalExpire = candidate;
                    generalAfterimage = settings.Afterimage;
                }
            }
        }
        float visibility = ExpireVisibility(generalExpire, generalAfterimage, now);
        float hiddenVisibility = ExpireVisibility(hiddenExpire, hiddenAfterimage, now);
        if (hiddenVisibility > 0f)
            visibility = Mathf.Max(visibility, hiddenVisibility * (1f - OwnerExclusion(point)));
        foreach (Sphere head in _heads)
            visibility = Mathf.Max(visibility, SphereVisibility(position, head, head.Strength));
        foreach (Sphere sample in _trail)
            visibility = Mathf.Max(visibility,
                SphereVisibility(position, sample, TrailStrength(sample, now) * sample.Strength));
        return visibility;
    }

    /// <summary>
    /// distance(미터) 지점을 settings의 감속 확산 전선이 지나는 링 출발 후 경과 시간을 반환한다.
    /// 반경 r = R(1-(1-t)^2)의 역함수이며 셰이더와 같은 식을 사용한다.
    /// </summary>
    private static float HitTime(float distance, RevealRippleSettings settings)
    {
        return settings.ExpandDuration * (1f - Mathf.Sqrt(Mathf.Clamp01(1f - distance / settings.Radius)));
    }

    /// <summary>expireTime과 afterimage로 now 시점의 잔상 가시성을 0~1로 반환한다.</summary>
    private static float ExpireVisibility(float expireTime, float afterimage, float now)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((expireTime - now) / Mathf.Max(afterimage, 0.0001f)));
    }

    /// <summary>position과 sphere 중심의 3D 거리로 중심 1에서 가장자리 0까지 흐려지는 가시성에 strength를 곱해 반환한다.</summary>
    private static float SphereVisibility(Vector3 position, Sphere sphere, float strength)
    {
        float progress = Vector3.Distance(position, sphere.Center) / Mathf.Max(sphere.Radius, 0.0001f);
        return (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress))) * strength;
    }

    /// <summary>sample의 기록 후 경과 시간으로 now 시점의 잔상 강도를 1에서 0으로 줄여 반환한다.</summary>
    private static float TrailStrength(Sphere sample, float now)
    {
        return 1f - Mathf.Clamp01((now - sample.RecordedAt) / Mathf.Max(sample.Afterimage, 0.0001f));
    }

    /// <summary>XZ point가 등록된 발생자 주변 _ownerHideRadius 안에 있을수록 1에 가까운 제외 배율을 반환한다.</summary>
    private float OwnerExclusion(Vector2 point)
    {
        float exclusion = 0f;
        float innerRadius = Mathf.Max(0f, _ownerHideRadius - OWNER_HIDE_EDGE);
        for (int i = 0; i < _hiddenOwnerCount; i++)
        {
            Vector4 owner = _hiddenOwnerBuffer[i];
            float distance = Vector2.Distance(point, new Vector2(owner.x, owner.z));
            exclusion = Mathf.Max(exclusion,
                1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, _ownerHideRadius, distance)));
        }
        return exclusion;
    }

    /// <summary>distance가 radius 바깥 경계의 _edgeFade 구간에 들어갈수록 0에 가까워지는 잔상 배율을 반환한다.</summary>
    private float EdgeFactor(float distance, float radius)
    {
        float innerRadius = Mathf.Max(0f, radius - Mathf.Max(_edgeFade, 0.0001f));
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, radius, distance));
    }

    /// <summary>now 기준으로 잔상까지 끝난 파동과 구형 잔상을 제거하고 이번 프레임 구형 중심을 현재 목록으로 옮긴다.</summary>
    private void PruneHistory(float now)
    {
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            if (now >= _ripples[i].ExpireTime)
                _ripples.RemoveAt(i);
        }
        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            if (now >= _trail[i].RecordedAt + _trail[i].Afterimage)
                _trail.RemoveAt(i);
        }
        _heads.Clear();
        _heads.AddRange(_pendingHeads);
        _pendingHeads.Clear();
    }

    /// <summary>
    /// 전선이 아직 퍼지는 파동을 갱신 셰이더에 넘겨 시야 맵을 다음 버퍼에 기록한다.
    /// 몸체를 밝히지 않는 파동은 겹 수를 음수로 넘기며, 기록할 파동이 없으면 이전 맵을 그대로 둔다.
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
            _rippleParamBuffer[rippleCount++] = new Vector4(settings.ExpandDuration, settings.RingInterval,
                settings.Afterimage, ripple.HideOwner ? -settings.RingCount : settings.RingCount);
        }
        if (rippleCount == 0 && !_clearPending)
            return;

        _updateMaterial.SetVector(_boundsId, new Vector4(_minimum.x, _minimum.y, _size.x, _size.y));
        _updateMaterial.SetFloat(_timeId, now);
        _updateMaterial.SetFloat(_edgeFadeId, _edgeFade);
        _updateMaterial.SetFloat(_clearId, _clearPending ? 1f : 0f);
        _updateMaterial.SetInt(_rippleCountId, rippleCount);
        _updateMaterial.SetVectorArray(_ripplesId, _rippleBuffer);
        _updateMaterial.SetVectorArray(_rippleParamsId, _rippleParamBuffer);
        int next = 1 - _current;
        Graphics.Blit(_maps[_current], _maps[next], _updateMaterial);
        _current = next;
        _clearPending = false;
    }

    /// <summary>
    /// 이번 프레임 구형 중심과 잔상 표본을 중심·반경과 강도 배열로 셰이더에 전달한다.
    /// 최대 개수를 넘으면 현재 중심을 우선하고 최근 잔상부터 채운다.
    /// </summary>
    private void PublishSpheres(float now)
    {
        int count = 0;
        foreach (Sphere head in _heads)
        {
            if (count == MAX_SPHERES)
                break;
            _sphereBuffer[count] = new Vector4(head.Center.x, head.Center.y, head.Center.z, head.Radius);
            _sphereStrengthBuffer[count++] = new Vector4(head.Strength, 0f, 0f, 0f);
        }
        for (int i = _trail.Count - 1; i >= 0 && count < MAX_SPHERES; i--)
        {
            Sphere sample = _trail[i];
            _sphereBuffer[count] = new Vector4(sample.Center.x, sample.Center.y, sample.Center.z, sample.Radius);
            _sphereStrengthBuffer[count++] = new Vector4(TrailStrength(sample, now) * sample.Strength, 0f, 0f, 0f);
        }
        Shader.SetGlobalInt(_sphereCountId, count);
        Shader.SetGlobalVectorArray(_spheresId, _sphereBuffer);
        Shader.SetGlobalVectorArray(_sphereStrengthsId, _sphereStrengthBuffer);
    }

    /// <summary>비활성·파괴된 발생자를 목록에서 빼고 남은 발생자의 위치와 제외 반경을 셰이더에 전달한다.</summary>
    private void PublishHiddenOwners()
    {
        _hiddenOwnerCount = 0;
        for (int i = _hiddenOwners.Count - 1; i >= 0; i--)
        {
            Transform owner = _hiddenOwners[i];
            if (owner == null || !owner.gameObject.activeInHierarchy)
            {
                _hiddenOwners.RemoveAt(i);
                continue;
            }
            Vector3 position = owner.position;
            _hiddenOwnerBuffer[_hiddenOwnerCount++] = new Vector4(position.x, position.y, position.z, _ownerHideRadius);
        }
        Shader.SetGlobalInt(_hiddenOwnerCountId, _hiddenOwnerCount);
        Shader.SetGlobalVectorArray(_hiddenOwnersId, _hiddenOwnerBuffer);
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
    /// 갱신 머티리얼과 두 장의 ARGBFloat 시야 맵을 준비하고 준비 성공 여부를 반환한다.
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
            _maps[i] = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.ARGBFloat,
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
