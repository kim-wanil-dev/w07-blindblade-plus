using UnityEngine;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 플레이어의 퍼펙트 패링 토큰 수를 Image 슬롯으로 표시한다.
    /// 찬 슬롯과 빈 슬롯은 Inspector의 Sprite·색으로 그려 나중에 이미지만 바꿀 수 있고, 궁극기 차징 중에는 찬 슬롯이 진행도만큼 커진다.
    /// 찬 슬롯은 원 이미지 대신 돌진 대상 실과 같은 모양의 실 두 가닥이 서로 엇갈려 공전하며 이루는 구체로 표시한다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    public sealed class CombatPerfectParryTokenHud : MonoBehaviour
    {
        [Header("원본 연결")]
        [SerializeField, Tooltip("토큰을 읽을 PlayerEmpower. 비우면 Game.Player에서 한 번 찾습니다.")]
        private PlayerEmpower _empower;

        [Header("슬롯")]
        [SerializeField, Tooltip("왼쪽부터 채워지는 토큰 Image. 개수는 최대 토큰 수(3)와 같게 둡니다.")]
        private Image[] _slots;
        [SerializeField, Tooltip("찬 토큰 이미지. 완성 이미지로 바꿀 때 교체합니다.")]
        private Sprite _filledSprite;
        [SerializeField, Tooltip("빈 토큰 이미지. 완성 이미지로 바꿀 때 교체합니다.")]
        private Sprite _emptySprite;
        [SerializeField, Tooltip("찬 토큰 이미지에 곱하는 색. 완성 이미지가 자체 색을 가지면 흰색으로 둡니다.")]
        private Color _filledColor = new Color(0.86f, 0.1f, 0.1f, 1f);
        [SerializeField, Tooltip("빈 토큰 이미지에 곱하는 색.")]
        private Color _emptyColor = new Color(0.45f, 0.45f, 0.45f, 0.75f);
        private int _shownTokens = -1;

        [Header("차징")]
        [SerializeField, Min(1f), Tooltip("궁극기 차징이 다 찼을 때 찬 토큰의 크기 배율. 차징 진행도만큼 커진다.")]
        private float _chargeScale = 1.5f;

        [Header("실 공전")]
        [SerializeField, Tooltip("찬 토큰을 이루는 실 구체의 크기·궤도·모양·흐름")]
        private CombatTokenOrbitStyle _orbitStyle = new CombatTokenOrbitStyle();
        private CombatTokenOrbitThread[] _orbitThreads;
        private ThreadSphereFlow[] _orbitFlows;
        private float[] _orbitVisibilities;
        private float _orbitTime;

        void Awake()
        {
            CreateOrbitThreads();
        }

        void OnEnable()
        {
            _shownTokens = -1;
            ApplyTokens(_empower != null ? _empower.Tokens : 0);
        }

        void Update()
        {
            if (_empower == null)
            {
                if (!Game.IsInitialized || Game.Player == null) return;
                _empower = Game.Player.GetComponent<PlayerEmpower>();
            }

            int tokens = _empower.Tokens;
            if (tokens != _shownTokens) ApplyTokens(tokens);
            float scale = Mathf.Lerp(1f, _chargeScale, _empower.ChargeProgress);
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].rectTransform.localScale = Vector3.one * (i < tokens ? scale : 1f);
            UpdateOrbitThreads(tokens);
        }

        /// <summary>
        /// tokens 개수만큼 왼쪽 슬롯부터 찬 Sprite·색으로, 나머지는 빈 Sprite·색으로 바꾼다.
        /// 찬 슬롯은 실 구체만 보이도록 Image를 끄고 빈 슬롯은 켠다. 표시한 개수를 _shownTokens에 저장하며 토큰 수 자체는 바꾸지 않는다.
        /// </summary>
        private void ApplyTokens(int tokens)
        {
            _shownTokens = tokens;
            for (int i = 0; i < _slots.Length; i++)
            {
                bool filled = i < tokens;
                _slots[i].sprite = filled ? _filledSprite : _emptySprite;
                _slots[i].color = filled ? _filledColor : _emptyColor;
                _slots[i].enabled = !filled;
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
                // 슬롯 자식이라 차징 중 슬롯이 커지면 구체도 같은 배율로 커진다.
                RectTransform thread = _orbitThreads[i].rectTransform;
                thread.anchorMin = Vector2.zero;
                thread.anchorMax = Vector2.one;
                thread.offsetMin = Vector2.zero;
                thread.offsetMax = Vector2.zero;
                _orbitFlows[i] = new ThreadSphereFlow(i * 2f);
            }
        }

        /// <summary>
        /// tokens보다 앞의 찬 슬롯은 실을 실시간 FadeDuration에 걸쳐 나타내고, 빈 슬롯은 같은 시간에 걸쳐 숨긴다.
        /// 새로 나타나는 슬롯은 흐름을 다시 시작하고, 보이는 슬롯은 두 실의 흐름을 적분해 실 구체 그래픽에 넘긴다.
        /// </summary>
        private void UpdateOrbitThreads(int tokens)
        {
            float deltaTime = Time.unscaledDeltaTime;
            _orbitTime += deltaTime;
            for (int i = 0; i < _slots.Length; i++)
            {
                float previous = _orbitVisibilities[i];
                bool filled = i < tokens;
                if (filled && previous <= 0f)
                    _orbitFlows[i].Reset(_orbitStyle, _orbitTime);

                _orbitVisibilities[i] = Mathf.MoveTowards(previous, filled ? 1f : 0f, deltaTime / _orbitStyle.FadeDuration);
                if (_orbitVisibilities[i] <= 0f && previous <= 0f)
                    continue;

                _orbitFlows[i].Step(_orbitStyle, _orbitTime, deltaTime);
                _orbitThreads[i].SetState(_orbitVisibilities[i], _orbitTime, _orbitFlows[i]);
            }
        }
    }
}
