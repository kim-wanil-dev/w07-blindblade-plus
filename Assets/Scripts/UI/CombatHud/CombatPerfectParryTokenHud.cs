using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// PlayerSkillGauge의 일섬 게이지를 세 개의 부분 충전 Image 슬롯으로 표시한다.
    /// 빈 배경과 부분 Fill을 유지하고, 완충 슬롯은 돌진 대상 실과 같은 모양의 실 두 가닥이 공전하는 구체로 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CombatPerfectParryTokenHud : MonoBehaviour
    {
        [Header("원본 연결")]
        [SerializeField, Tooltip("일섬 게이지를 소유하는 PlayerEmpower. 비우면 Game.Player에서 한 번 찾습니다.")]
        private PlayerEmpower _empower;

        [Header("슬롯")]
        [SerializeField, Tooltip("왼쪽부터 채워지는 일섬 게이지 Image. 개수는 세 개로 둡니다.")]
        private UnityEngine.UI.Image[] _slots;
        [SerializeField, Tooltip("부분 Fill에 사용하는 찬 원형 Sprite. 완성 이미지로 바꿀 때 교체합니다.")]
        private Sprite _filledSprite;
        [SerializeField, Tooltip("항상 보이는 빈 슬롯 배경 Sprite. 완성 이미지로 바꿀 때 교체합니다.")]
        private Sprite _emptySprite;
        [SerializeField, Tooltip("부분 Fill 이미지에 곱하는 색. 완성 이미지가 자체 색을 가지면 흰색으로 둡니다.")]
        private Color _filledColor = new Color(0.86f, 0.1f, 0.1f, 1f);
        [SerializeField, Tooltip("빈 슬롯 배경 이미지에 곱하는 색.")]
        private Color _emptyColor = new Color(0.45f, 0.45f, 0.45f, 0.75f);

        [Header("런타임")]
        private PlayerSkillGauge _skillGauge;
        private UnityEngine.UI.Image[] _partialFills;
        private float _shownGauge = float.NaN;

        [Header("실 공전")]
        [SerializeField, Tooltip("완충 슬롯을 이루는 실 구체의 크기·궤도·모양·흐름")]
        private CombatTokenOrbitStyle _orbitStyle = new CombatTokenOrbitStyle();
        private CombatTokenOrbitThread[] _orbitThreads;
        private ThreadSphereFlow[] _orbitFlows;
        private float[] _orbitVisibilities;
        private float _orbitTime;

        [Header("가득 참")]
        [SerializeField, Min(1f), Tooltip("세 슬롯이 모두 찼을 때 실 구체가 도는 속도 배율")]
        private float _fullSpeedMultiplier = 2.5f;
        [SerializeField, Min(0f), Tooltip("세 슬롯이 모두 찼을 때 실 구체 뒤에 퍼지는 빛무리 세기")]
        private float _fullGlowStrength = 1f;
        [SerializeField, Min(1f), Tooltip("빛무리 바깥 끝 반지름(구 반지름 대비)")]
        private float _fullGlowRadius = 1.7f;
        [SerializeField, Range(0f, 1f), Tooltip("빛무리가 맥동하며 옅어지는 정도. 0이면 맥동하지 않는다.")]
        private float _fullGlowPulseDepth = 0.35f;
        [SerializeField, Min(0f), Tooltip("빛무리 맥동 속도(초당 횟수)")]
        private float _fullGlowPulseSpeed = 1.2f;
        [SerializeField, Min(0.01f), Tooltip("세 슬롯이 모두 차거나 비워질 때 빛·속도가 바뀌는 실시간(초)")]
        private float _fullTransitionDuration = 0.3f;
        private float _fullBlend;
        private float _glowTime;

        /// <summary>
        /// HUD partial-fill 원형에 사용하는 찬 Sprite를 반환한다.
        /// 거합 HUD가 같은 프로젝트 원형 Sprite를 재사용할 수 있게 제공한다.
        /// </summary>
        public Sprite FilledSprite => _filledSprite;

        void Awake()
        {
            CreatePartialFills();
            CreateOrbitThreads();
        }

        void OnEnable()
        {
            _shownGauge = float.NaN;
            PlayerSkillGauge skillGauge = ResolveSkillGauge();
            if (skillGauge != null)
                ApplySwordWaveGauge(skillGauge.SwordWaveGauge);
        }

        void Update()
        {
            PlayerSkillGauge skillGauge = ResolveSkillGauge();
            if (skillGauge == null)
                return;

            float gauge = Mathf.Clamp(skillGauge.SwordWaveGauge, 0f, skillGauge.MaxSwordWaveGauge);
            if (gauge != _shownGauge)
                ApplySwordWaveGauge(gauge);

            int fullSlots = Mathf.Clamp(Mathf.FloorToInt(gauge), 0, _slots.Length);
            UpdateOrbitThreads(fullSlots);
        }

        /// <summary>
        /// Game.Player 또는 직렬화된 PlayerEmpower에서 PlayerSkillGauge를 찾아 반환한다.
        /// 게이지 컴포넌트가 아직 없으면 null을 반환하고 이후 프레임에서 다시 찾는다.
        /// </summary>
        private PlayerSkillGauge ResolveSkillGauge()
        {
            if (_skillGauge != null)
                return _skillGauge;

            if (_empower != null)
            {
                _skillGauge = _empower.GetComponent<PlayerSkillGauge>();
                return _skillGauge;
            }

            if (!Game.IsInitialized || Game.Player == null)
                return null;

            _empower = Game.Player.GetComponent<PlayerEmpower>();
            _skillGauge = Game.Player.GetComponent<PlayerSkillGauge>();
            return _skillGauge;
        }

        /// <summary>
        /// gauge 양을 각 슬롯의 Clamp01(gauge - index) Fill에 표시한다.
        /// 빈 배경은 계속 켜 두고 부분 충전 Image만 사용하며, 완충 수치는 _shownGauge에 저장한다.
        /// </summary>
        private void ApplySwordWaveGauge(float gauge)
        {
            _shownGauge = gauge;
            for (int i = 0; i < _slots.Length; i++)
            {
                float fillAmount = Mathf.Clamp01(gauge - i);
                _slots[i].sprite = _emptySprite;
                _slots[i].color = _emptyColor;
                _slots[i].enabled = true;
                _partialFills[i].fillAmount = fillAmount;
                _partialFills[i].enabled = fillAmount > 0f && fillAmount < 1f;
            }
        }

        /// <summary>
        /// 슬롯별 원형 Fill Image를 슬롯 자식으로 만들고 세로 아래쪽부터 채우도록 설정한다.
        /// 각 Image에는 Inspector의 찬 Sprite·색을 적용하며 입력을 가로채지 않게 한다.
        /// </summary>
        private void CreatePartialFills()
        {
            _partialFills = new UnityEngine.UI.Image[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                UnityEngine.UI.Image slot = _slots[i];
                GameObject fillObject = new GameObject(slot.name + "_PartialFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                fillObject.layer = gameObject.layer;
                fillObject.transform.SetParent(slot.transform, false);

                UnityEngine.UI.Image fill = fillObject.GetComponent<UnityEngine.UI.Image>();
                fill.sprite = _filledSprite;
                fill.color = _filledColor;
                fill.type = UnityEngine.UI.Image.Type.Filled;
                fill.fillMethod = UnityEngine.UI.Image.FillMethod.Vertical;
                fill.fillOrigin = (int)UnityEngine.UI.Image.OriginVertical.Bottom;
                fill.preserveAspect = true;
                fill.raycastTarget = false;
                fill.enabled = false;

                RectTransform fillRect = fill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                _partialFills[i] = fill;
            }
        }

        /// <summary>
        /// _slots마다 실 구체를 그리는 그래픽을 슬롯 자식으로 만들어 슬롯 크기에 맞추고, 구체마다 다른 잡음 줄의 흐름을 만든다.
        /// </summary>
        private void CreateOrbitThreads()
        {
            int count = _slots.Length;
            _orbitThreads = new CombatTokenOrbitThread[count];
            _orbitFlows = new ThreadSphereFlow[count];
            _orbitVisibilities = new float[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform slot = _slots[i].rectTransform;
                GameObject threadObject = new GameObject(slot.name + "_Orbit", typeof(RectTransform), typeof(CanvasRenderer));
                threadObject.layer = gameObject.layer;
                threadObject.transform.SetParent(slot, false);
                _orbitThreads[i] = threadObject.AddComponent<CombatTokenOrbitThread>();
                _orbitThreads[i].Initialize(_orbitStyle);
                // 슬롯 자식이라 슬롯 크기가 바뀌면 구체도 같은 크기로 바뀐다.
                RectTransform thread = _orbitThreads[i].rectTransform;
                thread.anchorMin = Vector2.zero;
                thread.anchorMax = Vector2.one;
                thread.offsetMin = Vector2.zero;
                thread.offsetMax = Vector2.zero;
                _orbitFlows[i] = new ThreadSphereFlow(i * 2f);
            }
        }

        /// <summary>
        /// fullSlots 수만큼의 완충 슬롯은 실을 실시간 FadeDuration에 걸쳐 나타내고, 나머지는 같은 시간에 걸쳐 숨긴다.
        /// 새로 나타나는 슬롯은 흐름을 다시 시작하고, 보이는 슬롯은 두 실의 흐름을 적분해 실 구체 그래픽에 넘긴다.
        /// 세 슬롯이 모두 차면 _fullTransitionDuration에 걸쳐 공전 속도를 _fullSpeedMultiplier배로 올리고 맥동하는 빛무리를 켠다.
        /// </summary>
        private void UpdateOrbitThreads(int fullSlots)
        {
            float deltaTime = Time.unscaledDeltaTime;
            _fullBlend = Mathf.MoveTowards(_fullBlend, fullSlots >= _slots.Length ? 1f : 0f, deltaTime / _fullTransitionDuration);
            // 공전·궤도면 회전·두께 변화를 모두 빠르게 하도록 실 구체의 시계를 배율만큼 빨리 돌린다.
            float orbitDelta = deltaTime * Mathf.Lerp(1f, _fullSpeedMultiplier, _fullBlend);
            _orbitTime += orbitDelta;
            _glowTime += deltaTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_glowTime * _fullGlowPulseSpeed * Mathf.PI * 2f);
            float glow = _fullBlend * _fullGlowStrength * (1f - _fullGlowPulseDepth * pulse);
            for (int i = 0; i < _slots.Length; i++)
            {
                float previous = _orbitVisibilities[i];
                bool filled = i < fullSlots;
                if (filled && previous <= 0f)
                    _orbitFlows[i].Reset(_orbitStyle, _orbitTime);

                _orbitVisibilities[i] = Mathf.MoveTowards(previous, filled ? 1f : 0f, deltaTime / _orbitStyle.FadeDuration);
                if (_orbitVisibilities[i] <= 0f && previous <= 0f)
                    continue;

                _orbitFlows[i].Step(_orbitStyle, _orbitTime, orbitDelta);
                _orbitThreads[i].SetState(_orbitVisibilities[i], _orbitTime, _orbitFlows[i], glow, _fullGlowRadius);
            }
        }
    }
}
