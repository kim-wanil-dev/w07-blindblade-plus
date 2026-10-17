using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 퍼펙트 패링과 강화 공격의 화면 연출을 그리는 플레이어 전용 오버레이 캔버스다.
/// 고정 층(비네트·암흑·섬광·칼날 빛줄기·갈라진 선)과 곡선 검격·먹 조각 같은 작은 UI 입자를 제공한다.
/// 모든 시간은 일시정지를 제외한 실시간이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class EmpowerOverlay : MonoBehaviour
{
    private const int CANVAS_SORTING_ORDER = 100;
    private static readonly Vector2 REFERENCE_RESOLUTION = new Vector2(1920f, 1080f);

    [Header("색")]
    [SerializeField, Tooltip("무채색 화면 위에 쓰는 단 하나의 강조색. 칼날 빛줄기·곡선 검격·갈라진 선·먹 조각에 쓴다.")]
    private Color _accentColor = Color.white;

    [Header("비네트")]
    [SerializeField, Min(0.01f), Tooltip("비네트가 목표 진하기로 바뀌는 데 걸리는 실시간.")]
    private float _vignetteBlendTime = 0.25f;
    [SerializeField, Min(1f), Tooltip("차징 비네트를 시작할 때의 크기 배율. 화면보다 크게 시작해 차징이 찰수록 1배로 줄어들며 짙은 부분이 안쪽으로 번진다. 1보다 작아지지 않아 이미지 경계가 화면에 보이지 않는다.")]
    private float _chargeVignetteStartScale = 2f;
    private Image _chargeVignette;
    private float _chargeVignetteProgress;
    private float _chargeVignetteTargetProgress;
    private float _chargeVignetteStrength;

    [Header("칼날 빛줄기")]
    [SerializeField, Min(1f)] private float _glintThickness = 4f;

    [Header("상태")]
    private GameObject _canvasRoot;
    private RectTransform _canvasRect;
    private RectTransform _effectRoot;
    private Image _vignette;
    private Image _void;
    private Image _flash;
    private Image _glint;
    private Image _cutLine;
    private readonly List<Particle> _particles = new List<Particle>();
    private float _vignetteTarget;
    private float _vignetteBase;
    private float _vignettePulseStrength;
    private float _vignettePulseDuration;
    private float _vignettePulseAge;
    private float _flashStrength;
    private float _flashDuration;
    private float _flashAge;
    private float _glintDuration;
    private float _glintAge = -1f;
    private float _cutExtend;
    private float _cutAge = -1f;
    private float _cutFadeAge = -1f;
    private float _cutFadeDuration;
    private Color _cutLineColor = Color.white;

    public Color AccentColor => _accentColor;
    public Vector2 Size => _canvasRect.rect.size;

    /// <summary>화면에 떠 있는 작은 이미지 하나의 움직임과 수명이다.</summary>
    private sealed class Particle
    {
        public Image Image { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public Vector2 Size { get; set; }
        public float Growth { get; set; }
        public float Age { get; set; }
        public float Lifetime { get; set; }
        public float FadeIn { get; set; }
        public float Alpha { get; set; }
        public float Sweep { get; set; }
    }

    void Awake()
    {
        CreateCanvas();
    }

    void OnDestroy()
    {
        if (_canvasRoot != null) Destroy(_canvasRoot);
    }

    void Update()
    {
        float dt = RealDeltaTime();
        UpdateVignette(dt);
        UpdateFlash(dt);
        UpdateGlint(dt);
        UpdateCutLine(dt);
        UpdateParticles(dt);
    }

    /// <summary>이번 프레임의 실시간 간격을 반환한다. 게임이 정지돼 있으면 0이다.</summary>
    public static float RealDeltaTime()
    {
        return Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
    }

    /// <summary>비네트 진하기 목표를 target(0~1)으로 바꾼다. 실제 진하기는 _vignetteBlendTime에 걸쳐 따라간다.</summary>
    public void SetVignette(float target)
    {
        _vignetteTarget = Mathf.Clamp01(target);
    }

    /// <summary>
    /// 궁극기 차징 비네트를 progress(0~1)와 최대 진하기 strength로 정한다. 진행도가 오를수록 비네트가 진해지며 안쪽으로 번진다.
    /// 오를 때는 바로 따라가고 내려갈 때는 _vignetteBlendTime에 걸쳐 풀리며, _chargeVignetteTargetProgress·_chargeVignetteStrength를 바꾼다.
    /// </summary>
    public void SetChargeVignette(float progress, float strength)
    {
        _chargeVignetteTargetProgress = Mathf.Clamp01(progress);
        _chargeVignetteStrength = Mathf.Clamp01(strength);
    }

    /// <summary>
    /// strength(0~1) 진하기의 비네트를 duration 실시간 동안 짧게 번졌다 사라지게 한다. SetVignette의 진하기와 겹치면 큰 쪽을 보인다.
    /// 진행 중인 짧은 비네트보다 약하면 무시하며 _vignettePulseStrength·_vignettePulseDuration·_vignettePulseAge를 바꾼다.
    /// </summary>
    public void PulseVignette(float strength, float duration)
    {
        if (strength <= 0f || strength < CurrentVignettePulse()) return;
        _vignettePulseStrength = Mathf.Clamp01(strength);
        _vignettePulseDuration = Mathf.Max(0.01f, duration);
        _vignettePulseAge = 0f;
    }

    /// <summary>흰 섬광을 strength 투명도로 띄우고 duration 동안 사라지게 한다. 진행 중인 섬광보다 약하면 무시한다.</summary>
    public void Flash(float strength, float duration)
    {
        if (strength < CurrentFlash()) return;
        _flashStrength = strength;
        _flashDuration = Mathf.Max(0.01f, duration);
        _flashAge = 0f;
    }

    /// <summary>화면 가운데를 가로지르는 칼날 반사 빛줄기를 duration 동안 가운데에서 양옆으로 뻗었다가 사라지게 한다.</summary>
    public void Glint(float duration)
    {
        _glintDuration = Mathf.Max(0.01f, duration);
        _glintAge = 0f;
        _glint.gameObject.SetActive(true);
    }

    /// <summary>화면을 alpha 투명도의 암흑으로 덮는다. 강화 공격의 암전에 쓰며 HUD보다 위에 그린다.</summary>
    public void SetVoid(float alpha)
    {
        if (_canvasRoot == null) return;
        SetAlpha(_void, alpha);
    }

    /// <summary>
    /// sprite 입자 하나를 position·size·angle에 띄우고 반환한다. velocity(픽셀/초)로 움직이며 color의 투명도까지 fadeIn 동안 나타나고,
    /// lifetime까지 크기가 growth배가 되며 사라진다. 쉬는 입자를 다시 쓰고 없으면 새로 만든다.
    /// </summary>
    private Particle SpawnParticle(Sprite sprite, Color color, Vector2 position, Vector2 size, float angle,
        Vector2 velocity, float lifetime, float fadeIn, float growth)
    {
        Particle particle = _particles.Find(item => !item.Image.gameObject.activeSelf);
        if (particle == null)
        {
            particle = new Particle { Image = CreateImage("Particle", Color.white, null, _effectRoot) };
            particle.Image.rectTransform.anchorMin = particle.Image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _particles.Add(particle);
        }
        particle.Image.sprite = sprite;
        particle.Image.type = Image.Type.Simple;
        particle.Image.rectTransform.localScale = Vector3.one;
        particle.Sweep = 0f;
        particle.Image.color = new Color(color.r, color.g, color.b, 0f);
        particle.Image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        particle.Image.rectTransform.SetAsLastSibling();
        particle.Position = position;
        particle.Velocity = velocity;
        particle.Size = size;
        particle.Growth = growth;
        particle.Age = 0f;
        particle.Lifetime = lifetime;
        particle.FadeIn = Mathf.Max(0.0001f, fadeIn);
        particle.Alpha = color.a;
        ApplyParticle(particle);
        particle.Image.gameObject.SetActive(true);
        return particle;
    }

    /// <summary>
    /// position에 지름 size의 초승달 검격을 angle도로 띄운다. 검격은 sweep 동안 날 경로를 따라 그어지고 lifetime 안에 사라진다.
    /// mirror이면 좌우를 뒤집어 반대 방향으로 휘두른 검격이 된다. 색은 color를 쓴다.
    /// </summary>
    public void Slash(Vector2 position, float size, float angle, bool mirror, float sweep, float lifetime, Color color)
    {
        Particle particle = SpawnParticle(EmpowerInkTextures.Arc, color, position, new Vector2(size, size),
            angle, Vector2.zero, lifetime, 0.01f, 1.08f);
        Image image = particle.Image;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = (int)Image.Origin360.Bottom;
        image.fillClockwise = false;
        image.rectTransform.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        particle.Sweep = Mathf.Max(0.01f, sweep);
        ApplyParticle(particle);
    }

    /// <summary>화면 중심을 지나 angle도로 기운 강조색 한 줄을 thickness 두께로 extendDuration 동안 가운데에서 양 끝까지 뻗게 한다.</summary>
    public void ShowCutLine(float angle, float thickness, float extendDuration)
    {
        ShowCutLine(angle, thickness, extendDuration, _accentColor);
    }

    /// <summary>
    /// 화면 중심을 지나 angle도로 기운 color 색 한 줄을 thickness 두께로 extendDuration 동안 가운데에서 양 끝까지 뻗게 한다.
    /// 보스 거합처럼 강조색이 아닌 줄에 쓰며 ScatterCutLine의 먹 조각도 같은 색을 쓴다.
    /// </summary>
    public void ShowCutLine(float angle, float thickness, float extendDuration, Color color)
    {
        RectTransform rect = _cutLine.rectTransform;
        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        rect.sizeDelta = new Vector2(0f, thickness);
        _cutExtend = Mathf.Max(0.01f, extendDuration);
        _cutAge = 0f;
        _cutFadeAge = -1f;
        _cutLineColor = color;
        _cutLine.color = color;
        _cutLine.gameObject.SetActive(true);
    }

    /// <summary>
    /// 갈라진 선을 count개의 먹 조각으로 흩뜨린다. 조각은 줄을 따라 흩어져 위로 떠오르며 lifetime 동안 사라지고,
    /// 줄 자체는 lifetime의 앞 15% 동안 빠르게 사라진다.
    /// </summary>
    public void ScatterCutLine(int count, float lifetime)
    {
        if (!_cutLine.gameObject.activeSelf) return;
        RectTransform rect = _cutLine.rectTransform;
        float angle = rect.localEulerAngles.z * Mathf.Deg2Rad;
        Vector2 along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        Vector2 normal = new Vector2(-along.y, along.x);
        float halfLength = rect.sizeDelta.x * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Vector2 position = along * Random.Range(-halfLength, halfLength) + normal * Random.Range(-6f, 6f);
            Vector2 velocity = normal * Random.Range(-60f, 60f) + Vector2.up * Random.Range(20f, 90f);
            float size = Random.Range(8f, 24f);
            SpawnParticle(EmpowerInkTextures.Splat, _cutLineColor, position, new Vector2(size, size),
                Random.Range(0f, 360f), velocity, lifetime * Random.Range(0.6f, 1f), 0.02f, 0.4f);
        }
        _cutFadeDuration = Mathf.Max(0.01f, lifetime * 0.15f);
        _cutFadeAge = 0f;
    }

    /// <summary>모든 연출을 즉시 지우고 비네트 목표도 0으로 돌린다. 씬 종료로 캔버스가 먼저 사라졌으면 아무것도 하지 않는다.</summary>
    public void ClearAll()
    {
        if (_canvasRoot == null) return;
        _vignetteTarget = 0f;
        _vignetteBase = 0f;
        _vignettePulseStrength = 0f;
        SetAlpha(_vignette, 0f);
        _chargeVignetteProgress = 0f;
        _chargeVignetteTargetProgress = 0f;
        ApplyChargeVignette();
        _flashStrength = 0f;
        SetAlpha(_flash, 0f);
        _glintAge = -1f;
        _glint.gameObject.SetActive(false);
        _cutAge = -1f;
        _cutFadeAge = -1f;
        _cutLine.gameObject.SetActive(false);
        SetVoid(0f);
        foreach (Particle particle in _particles) particle.Image.gameObject.SetActive(false);
    }

    /// <summary>
    /// 지속 비네트 진하기를 _vignetteTarget으로 dt만큼 옮기고, 짧은 비네트 나이를 늘린 뒤 둘 중 큰 진하기를 비네트 이미지에 적용한다.
    /// 차징 비네트 진행도도 오를 때는 바로, 내려갈 때는 dt만큼 따라가게 한 뒤 적용한다.
    /// </summary>
    private void UpdateVignette(float dt)
    {
        _vignetteBase = Mathf.MoveTowards(_vignetteBase, _vignetteTarget, dt / _vignetteBlendTime);
        _vignettePulseAge += dt;
        SetAlpha(_vignette, Mathf.Max(_vignetteBase, CurrentVignettePulse()));
        _chargeVignetteProgress = _chargeVignetteTargetProgress >= _chargeVignetteProgress
            ? _chargeVignetteTargetProgress
            : Mathf.MoveTowards(_chargeVignetteProgress, _chargeVignetteTargetProgress, dt / _vignetteBlendTime);
        ApplyChargeVignette();
    }

    /// <summary>
    /// _chargeVignetteProgress로 차징 비네트의 진하기와 크기를 정한다. 크기는 _chargeVignetteStartScale에서 1배로 줄어 짙은 부분이 안쪽으로 들어오고,
    /// 진하기는 진행도에 _chargeVignetteStrength를 곱한 값이다. 이미지는 늘 화면 이상 크기라 경계가 보이지 않는다.
    /// </summary>
    private void ApplyChargeVignette()
    {
        float eased = Mathf.SmoothStep(0f, 1f, _chargeVignetteProgress);
        _chargeVignette.rectTransform.localScale = Vector3.one * Mathf.Lerp(_chargeVignetteStartScale, 1f, eased);
        SetAlpha(_chargeVignette, eased * _chargeVignetteStrength);
    }

    /// <summary>짧은 비네트의 현재 진하기를 반환한다. 앞 15% 동안 빠르게 번지고 나머지 동안 부드럽게 사라지며 끝나면 0이다.</summary>
    private float CurrentVignettePulse()
    {
        if (_vignettePulseStrength <= 0f || _vignettePulseAge >= _vignettePulseDuration) return 0f;
        float t = _vignettePulseAge / _vignettePulseDuration;
        float rise = Mathf.Clamp01(t / 0.15f);
        float fall = 1f - Mathf.Clamp01((t - 0.15f) / 0.85f);
        return _vignettePulseStrength * rise * fall * fall;
    }

    /// <summary>섬광 나이를 dt만큼 늘리고 남은 세기를 적용한다.</summary>
    private void UpdateFlash(float dt)
    {
        _flashAge += dt;
        SetAlpha(_flash, CurrentFlash());
    }

    /// <summary>현재 섬광 세기를 나이와 수명으로 계산해 반환한다.</summary>
    private float CurrentFlash()
    {
        if (_flashStrength <= 0f || _flashAge >= _flashDuration) return 0f;
        float t = _flashAge / _flashDuration;
        return _flashStrength * (1f - t) * (1f - t);
    }

    /// <summary>칼날 빛줄기 나이를 dt만큼 늘려 앞 30% 동안 양옆으로 뻗고 나머지 동안 흐려지게 하며 끝나면 숨긴다.</summary>
    private void UpdateGlint(float dt)
    {
        if (_glintAge < 0f) return;
        _glintAge += dt;
        float t = _glintAge / _glintDuration;
        if (t >= 1f)
        {
            _glintAge = -1f;
            _glint.gameObject.SetActive(false);
            return;
        }
        float extend = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / 0.3f), 3f);
        _glint.rectTransform.sizeDelta = new Vector2(Size.x * 1.2f * extend, _glintThickness);
        SetAlpha(_glint, _accentColor.a * (1f - Mathf.Clamp01((t - 0.3f) / 0.7f)));
    }

    /// <summary>남은 한 줄을 뻗는 동안 길이를 늘리고, 흩어지는 중이면 투명하게 만든 뒤 숨긴다.</summary>
    private void UpdateCutLine(float dt)
    {
        if (_cutAge < 0f) return;
        _cutAge += dt;
        RectTransform rect = _cutLine.rectTransform;
        float extend = 1f - Mathf.Pow(1f - Mathf.Clamp01(_cutAge / _cutExtend), 3f);
        rect.sizeDelta = new Vector2(Size.magnitude * 1.1f * extend, rect.sizeDelta.y);
        if (_cutFadeAge < 0f) return;
        _cutFadeAge += dt;
        float t = _cutFadeAge / _cutFadeDuration;
        if (t >= 1f)
        {
            _cutAge = -1f;
            _cutLine.gameObject.SetActive(false);
            return;
        }
        SetAlpha(_cutLine, _cutLineColor.a * (1f - t));
    }

    /// <summary>보이는 입자를 dt만큼 움직이고 나이에 맞춰 크기·투명도를 바꾸며 수명이 끝나면 숨긴다.</summary>
    private void UpdateParticles(float dt)
    {
        foreach (Particle particle in _particles)
        {
            if (!particle.Image.gameObject.activeSelf) continue;
            particle.Age += dt;
            particle.Position += particle.Velocity * dt;
            if (particle.Lifetime > 0f && particle.Age >= particle.Lifetime)
            {
                particle.Image.gameObject.SetActive(false);
                continue;
            }
            ApplyParticle(particle);
        }
    }

    /// <summary>particle의 나이로 위치·크기·투명도를 계산해 이미지에 적용한다.</summary>
    private static void ApplyParticle(Particle particle)
    {
        float appear = Mathf.Clamp01(particle.Age / particle.FadeIn);
        float progress = particle.Lifetime > 0f ? Mathf.Clamp01(particle.Age / particle.Lifetime) : appear;
        float fade = particle.Lifetime > 0f
            ? 1f - Mathf.Clamp01((particle.Age - particle.FadeIn) / Mathf.Max(0.0001f, particle.Lifetime - particle.FadeIn))
            : 1f;
        float scale = Mathf.Lerp(1f, particle.Growth, 1f - Mathf.Pow(1f - progress, 3f));
        RectTransform rect = particle.Image.rectTransform;
        rect.anchoredPosition = particle.Position;
        rect.sizeDelta = particle.Size * scale;
        SetAlpha(particle.Image, particle.Alpha * appear * fade);
        if (particle.Sweep > 0f)
        {
            // 검격은 날 끝이 지나간 자리부터 휘어진 경로를 따라 그어진다.
            float sweep = 1f - Mathf.Pow(1f - Mathf.Clamp01(particle.Age / particle.Sweep), 3f);
            float start = (90f - EmpowerInkTextures.ArcHalfSpan) / 360f;
            float end = (90f + EmpowerInkTextures.ArcHalfSpan) / 360f;
            particle.Image.fillAmount = Mathf.Lerp(start, end, sweep);
        }
    }

    /// <summary>
    /// 오버레이 캔버스와 고정 층을 아래부터 비네트·암흑·입자 층·갈라진 선·칼날 빛줄기·섬광 순서로 만든다.
    /// 캔버스는 HUD보다 위에 그리며 입력을 가로채지 않는다.
    /// </summary>
    private void CreateCanvas()
    {
        _canvasRoot = new GameObject("EmpowerOverlay");
        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CANVAS_SORTING_ORDER;
        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = REFERENCE_RESOLUTION;
        scaler.matchWidthOrHeight = 0.5f;
        _canvasRect = (RectTransform)_canvasRoot.transform;

        _vignette = CreateImage("Vignette", Color.black, EmpowerInkTextures.Vignette, _canvasRect);
        Stretch(_vignette.rectTransform);
        _chargeVignette = CreateImage("ChargeVignette", Color.black, EmpowerInkTextures.ChargeVignette, _canvasRect);
        Stretch(_chargeVignette.rectTransform);
        _void = CreateImage("Void", Color.black, null, _canvasRect);
        Stretch(_void.rectTransform);
        GameObject effectRoot = new GameObject("Effects", typeof(RectTransform));
        effectRoot.transform.SetParent(_canvasRect, false);
        _effectRoot = (RectTransform)effectRoot.transform;
        Stretch(_effectRoot);
        _cutLine = CreateCentered("CutLine", _accentColor, EmpowerInkTextures.Line);
        _glint = CreateCentered("Glint", _accentColor, EmpowerInkTextures.Line);
        _flash = CreateImage("Flash", Color.white, null, _canvasRect);
        Stretch(_flash.rectTransform);
        ClearAll();
    }

    /// <summary>objectName·color·sprite로 화면 중심에 고정된 이미지를 캔버스 아래에 만들어 반환한다.</summary>
    private Image CreateCentered(string objectName, Color color, Sprite sprite)
    {
        Image image = CreateImage(objectName, color, sprite, _canvasRect);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        return image;
    }

    /// <summary>objectName·color·sprite로 입력을 가로채지 않는 이미지를 parent 아래에 만들어 반환한다.</summary>
    private static Image CreateImage(string objectName, Color color, Sprite sprite, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        Image image = child.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>rect를 부모 전체에 맞게 늘린다.</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>image의 색은 유지하고 투명도만 alpha로 바꾼다.</summary>
    private static void SetAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
}
