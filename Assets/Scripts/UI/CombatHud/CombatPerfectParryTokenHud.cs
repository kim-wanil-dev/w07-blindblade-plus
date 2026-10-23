using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// PlayerSkillGauge의 일섬 게이지를 세 개의 원형 슬롯으로 표시한다.
    /// 빈 슬롯 배경 위에서 각 칸의 충전량 Clamp01(잔량-index)만큼 굵은 실 구체가 아래에서 위로 일렁이는 액체처럼 차오르고, 실이 겹친 수에 따라 단계별 색을 띤다.
    /// 세 칸이 모두 차면 빛무리가 퍼지고 실이 빨라지며, 거합 게이지와 달리 테두리를 도는 실은 없다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CombatPerfectParryTokenHud : MonoBehaviour
    {
        [Header("원본 연결")]
        [SerializeField, Tooltip("일섬 게이지를 소유하는 PlayerEmpower. 비우면 Game.Player에서 한 번 찾습니다.")]
        private PlayerEmpower _empower;

        [Header("슬롯")]
        [SerializeField, Tooltip("왼쪽부터 채워지는 일섬 게이지 슬롯 Image. 개수는 세 개로 둡니다.")]
        private UnityEngine.UI.Image[] _slots;
        [SerializeField, Tooltip("찬 원형 Sprite. 거합 게이지가 배경 Sprite를 따로 받지 않으면 이 Sprite를 빌려 씁니다.")]
        private Sprite _filledSprite;
        [SerializeField, Tooltip("항상 보이는 빈 슬롯 배경 Sprite. 완성 이미지로 바꿀 때 교체합니다.")]
        private Sprite _emptySprite;
        [SerializeField, Tooltip("빈 슬롯 배경 이미지에 곱하는 색.")]
        private Color _emptyColor = new Color(0.45f, 0.45f, 0.45f, 0.75f);

        [Header("실 액체")]
        [SerializeField, Tooltip("실 구체가 겹친 수를 텍스처에 더하는 BlindBlade/ThreadLiquidDensity 재질")]
        private Material _liquidDensityMaterial;
        [SerializeField, Tooltip("겹침 수를 단계별 색으로 바꾸고 수면 위를 잘라 내는 BlindBlade/UIThreadLiquid 재질. 칸마다 복제해 쓴다.")]
        private Material _liquidCompositeMaterial;
        [SerializeField, Tooltip("칸마다 아래에서 위로 차오르는 실 액체의 모양·흐름·겹침 단계 색. 테두리 실(Rim Thread)은 꺼 둔다.")]
        private CombatThreadLiquidStyle _liquidStyle = new CombatThreadLiquidStyle();

        [Header("런타임")]
        private PlayerSkillGauge _skillGauge;
        private CombatThreadLiquidGauge[] _liquids;

        /// <summary>
        /// HUD 원형 게이지에 사용하는 찬 Sprite를 반환한다.
        /// 거합 HUD가 같은 프로젝트 원형 Sprite를 재사용할 수 있게 제공한다.
        /// </summary>
        public Sprite FilledSprite => _filledSprite;

        void Awake()
        {
            ApplySlotBackgrounds();
            CreateLiquids();
        }

        void Update()
        {
            PlayerSkillGauge skillGauge = ResolveSkillGauge();
            if (skillGauge == null)
                return;

            float gauge = Mathf.Clamp(skillGauge.SwordWaveGauge, 0f, skillGauge.MaxSwordWaveGauge);
            // 세 칸이 모두 찼을 때만 빛무리와 빠른 흐름을 켠다. HUD라 불릿 타임에도 느려지지 않는다.
            bool full = Mathf.FloorToInt(gauge) >= _slots.Length;
            float deltaTime = Time.unscaledDeltaTime;
            for (int i = 0; i < _liquids.Length; i++)
                _liquids[i].SetState(Mathf.Clamp01(gauge - i), full, deltaTime);
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
        /// 모든 슬롯 Image에 빈 슬롯 배경 Sprite와 색을 적용해 항상 보이게 한다.
        /// </summary>
        private void ApplySlotBackgrounds()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].sprite = _emptySprite;
                _slots[i].color = _emptyColor;
                _slots[i].enabled = true;
            }
        }

        /// <summary>
        /// 슬롯마다 실 액체 본체(RawImage와 CombatThreadLiquidBody)와 그 위의 수면 실·빛무리 그래픽(CombatThreadLiquidGauge)을 자식으로 만든다.
        /// 본체를 먼저 두어 수면 실이 위에 그려지게 하고, 만든 그래픽을 _liquids에 저장한다.
        /// </summary>
        private void CreateLiquids()
        {
            _liquids = new CombatThreadLiquidGauge[_slots.Length];
            for (int i = 0; i < _slots.Length; i++)
            {
                RectTransform slot = _slots[i].rectTransform;
                GameObject bodyObject = new GameObject(slot.name + "_LiquidBody", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(UnityEngine.UI.RawImage));
                bodyObject.layer = gameObject.layer;
                bodyObject.transform.SetParent(slot, false);
                CombatThreadLiquidBody body = bodyObject.AddComponent<CombatThreadLiquidBody>();
                body.Initialize(_liquidStyle, _liquidDensityMaterial, _liquidCompositeMaterial);

                GameObject liquidObject = new GameObject(slot.name + "_Liquid", typeof(RectTransform), typeof(CanvasRenderer));
                liquidObject.layer = gameObject.layer;
                liquidObject.transform.SetParent(slot, false);
                _liquids[i] = liquidObject.AddComponent<CombatThreadLiquidGauge>();
                _liquids[i].Initialize(_liquidStyle, body);
                // 슬롯 자식이라 슬롯 크기가 바뀌면 액체도 같은 크기로 바뀐다.
                RectTransform liquid = _liquids[i].rectTransform;
                liquid.anchorMin = Vector2.zero;
                liquid.anchorMax = Vector2.one;
                liquid.offsetMin = Vector2.zero;
                liquid.offsetMax = Vector2.zero;
            }
        }
    }
}
