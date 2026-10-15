using UnityEngine;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 플레이어의 퍼펙트 패링 토큰 수를 Image 슬롯으로 표시한다.
    /// 찬 슬롯과 빈 슬롯은 Inspector의 Sprite·색으로 그려 나중에 이미지만 바꿀 수 있고, 궁극기 차징 중에는 찬 슬롯이 진행도만큼 커진다.
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
        }

        /// <summary>
        /// tokens 개수만큼 왼쪽 슬롯부터 찬 Sprite·색으로, 나머지는 빈 Sprite·색으로 바꾼다.
        /// 표시한 개수를 _shownTokens에 저장하며 토큰 수 자체는 바꾸지 않는다.
        /// </summary>
        private void ApplyTokens(int tokens)
        {
            _shownTokens = tokens;
            for (int i = 0; i < _slots.Length; i++)
            {
                bool filled = i < tokens;
                _slots[i].sprite = filled ? _filledSprite : _emptySprite;
                _slots[i].color = filled ? _filledColor : _emptyColor;
            }
        }
    }
}
