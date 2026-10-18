using UnityEngine;

using UnityEngine.UI;

using BlindBlade.UI.CombatHud;

/// <summary>
/// 인간 보스 허리춤 기운(HumanBossAttack.Charges)을 화면 위 보스 체력바 왼쪽 아래에 플레이어 토큰 HUD와 같은 슬롯·실 구체로 표시한다.
/// 찬 슬롯은 붉은 실 구체로 채우고, 모두 차면 실이 빨리 돌며 붉은 빛무리가 맥동한다. 인간이 쓰러지면 숨긴다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack))]
public sealed class TwinHumanBeadHud : MonoBehaviour
{
    [Header("배치")]
    [SerializeField, Min(0f), Tooltip("화면 위 끝에서 슬롯 위 끝까지 거리(1280×720 기준 픽셀). 위 72·높이 16인 공유 보스 체력바 바로 아래에 둔다.")]
    private float _topOffset = 94f;
    [SerializeField, Tooltip("화면 가운데 기준 첫 슬롯 왼쪽 끝 x(1280×720 기준 픽셀). 폭 420 보스 체력바의 왼쪽 끝과 맞춘다.")]
    private float _leftEdge = -210f;
    [SerializeField, Min(1f), Tooltip("슬롯 한 변 길이(1280×720 기준 픽셀). 플레이어 토큰의 약 3/4 크기다.")]
    private float _slotSize = 28f;
    [SerializeField, Min(0f), Tooltip("슬롯 중심 사이 거리(1280×720 기준 픽셀)")]
    private float _slotSpacing = 32f;
    private HumanBossAttack _attack;
    private GameObject _canvasObject;

    [Header("슬롯")]
    [SerializeField, Tooltip("빈 슬롯 이미지. 플레이어 토큰 HUD와 같은 이미지를 쓴다.")]
    private Sprite _emptySprite;
    [SerializeField, Tooltip("빈 슬롯 이미지에 곱하는 색")]
    private Color _emptyColor = new Color(0.62f, 0.62f, 0.64f, 0.85f);
    private Image[] _slots;

    [Header("실 공전")]
    [SerializeField, Tooltip("찬 슬롯을 이루는 실 구체. 색을 붉게 두어 보스 기운을 나타내며 빛무리도 같은 색이다.")]
    private CombatTokenOrbitStyle _orbitStyle = new CombatTokenOrbitStyle();
    private CombatTokenOrbitThread[] _orbitThreads;
    private ThreadSphereFlow[] _orbitFlows;
    private float[] _orbitVisibilities;
    private float _orbitTime;

    [Header("가득 참")]
    [SerializeField, Min(1f), Tooltip("기운이 모두 찼을 때 실 구체가 도는 속도 배율")]
    private float _fullSpeedMultiplier = 2.5f;
    [SerializeField, Min(0f), Tooltip("기운이 모두 찼을 때 실 구체 뒤에 퍼지는 붉은 빛무리 세기")]
    private float _fullGlowStrength = 1f;
    [SerializeField, Min(1f), Tooltip("빛무리 바깥 끝 반지름(구 반지름 대비)")]
    private float _fullGlowRadius = 1.7f;
    [SerializeField, Range(0f, 1f), Tooltip("빛무리가 맥동하며 옅어지는 정도. 0이면 맥동하지 않는다.")]
    private float _fullGlowPulseDepth = 0.35f;
    [SerializeField, Min(0f), Tooltip("빛무리 맥동 속도(초당 횟수)")]
    private float _fullGlowPulseSpeed = 1.2f;
    [SerializeField, Min(0.01f), Tooltip("가득 찬 상태로 바뀌거나 풀릴 때 빛·속도가 바뀌는 실시간(초)")]
    private float _fullTransitionDuration = 0.3f;
    private float _fullBlend;
    private float _glowTime;

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        CreateCanvas();
    }

    void OnDisable()
    {
        _canvasObject.SetActive(false);
    }

    void Update()
    {
        bool alive = _attack.Health.IsAlive;
        if (_canvasObject.activeSelf != alive) _canvasObject.SetActive(alive);
        if (alive) UpdateSlots(_attack.Charges);
    }

    /// <summary>
    /// 보스 체력바와 같은 1280×720 기준 화면 덮개 캔버스를 인간 아래에 만들고 _attack.MaxCharges개 슬롯을 체력바 왼쪽 끝(_leftEdge)부터 늘어놓는다.
    /// 슬롯마다 빈 이미지와 실 구체 그래픽, 다른 잡음 줄의 흐름을 만들어 _canvasObject, _slots, _orbitThreads, _orbitFlows에 저장한다.
    /// </summary>
    private void CreateCanvas()
    {
        _canvasObject = new GameObject("BossBeadCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _canvasObject.transform.SetParent(transform, false);
        Canvas canvas = _canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        CanvasScaler scaler = _canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        int count = _attack.MaxCharges;
        _slots = new Image[count];
        _orbitThreads = new CombatTokenOrbitThread[count];
        _orbitFlows = new ThreadSphereFlow[count];
        _orbitVisibilities = new float[count];
        for (int i = 0; i < count; i++)
        {
            GameObject slotObject = new GameObject("Bead_" + i, typeof(RectTransform), typeof(Image));
            slotObject.transform.SetParent(_canvasObject.transform, false);
            Image slot = slotObject.GetComponent<Image>();
            slot.sprite = _emptySprite;
            slot.color = _emptyColor;
            slot.preserveAspect = true;
            slot.raycastTarget = false;
            RectTransform rect = slot.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(_leftEdge + _slotSize * 0.5f + i * _slotSpacing, -_topOffset - _slotSize * 0.5f);
            rect.sizeDelta = new Vector2(_slotSize, _slotSize);
            _slots[i] = slot;

            GameObject threadObject = new GameObject(slotObject.name + "_Orbit", typeof(RectTransform), typeof(CanvasRenderer));
            threadObject.transform.SetParent(rect, false);
            _orbitThreads[i] = threadObject.AddComponent<CombatTokenOrbitThread>();
            _orbitThreads[i].Initialize(_orbitStyle);
            RectTransform thread = _orbitThreads[i].rectTransform;
            thread.anchorMin = Vector2.zero;
            thread.anchorMax = Vector2.one;
            thread.offsetMin = Vector2.zero;
            thread.offsetMax = Vector2.zero;
            _orbitFlows[i] = new ThreadSphereFlow(20f + i * 2f);
        }
    }

    /// <summary>
    /// charges보다 앞의 찬 슬롯은 빈 이미지를 끄고 붉은 실 구체를 실시간 FadeDuration에 걸쳐 나타내며, 빈 슬롯은 반대로 한다.
    /// 모든 슬롯이 차면 _fullTransitionDuration에 걸쳐 공전 속도를 _fullSpeedMultiplier배로 올리고 맥동하는 붉은 빛무리를 켠다.
    /// 슬롯 표시와 _orbitVisibilities, _orbitTime, _fullBlend를 변경한다.
    /// </summary>
    private void UpdateSlots(int charges)
    {
        float deltaTime = Time.unscaledDeltaTime;
        _fullBlend = Mathf.MoveTowards(_fullBlend, charges >= _slots.Length ? 1f : 0f, deltaTime / _fullTransitionDuration);
        float orbitDelta = deltaTime * Mathf.Lerp(1f, _fullSpeedMultiplier, _fullBlend);
        _orbitTime += orbitDelta;
        _glowTime += deltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_glowTime * _fullGlowPulseSpeed * Mathf.PI * 2f);
        float glow = _fullBlend * _fullGlowStrength * (1f - _fullGlowPulseDepth * pulse);
        for (int i = 0; i < _slots.Length; i++)
        {
            bool filled = i < charges;
            _slots[i].enabled = !filled;
            float previous = _orbitVisibilities[i];
            if (filled && previous <= 0f) _orbitFlows[i].Reset(_orbitStyle, _orbitTime);

            _orbitVisibilities[i] = Mathf.MoveTowards(previous, filled ? 1f : 0f, deltaTime / _orbitStyle.FadeDuration);
            if (_orbitVisibilities[i] <= 0f && previous <= 0f) continue;

            _orbitFlows[i].Step(_orbitStyle, _orbitTime, orbitDelta);
            _orbitThreads[i].SetState(_orbitVisibilities[i], _orbitTime, _orbitFlows[i], glow, _fullGlowRadius);
        }
    }
}
