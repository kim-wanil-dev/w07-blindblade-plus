using UnityEngine;

using TMPro;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>직렬화된 Image의 채움 비율과 텍스트만 갱신하는 자원 게이지다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatResourceGauge : MonoBehaviour
    {
        [Header("표시 연결")]
        [SerializeField] private UnityEngine.UI.Image _fill;
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private CanvasGroup _group;

        [Header("표시 설정")]
        [SerializeField, Min(0f)] private float _transitionDuration = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _unavailableAlpha = 0.35f;
        private float _targetFill;

        void Update()
        {
            _fill.fillAmount = _transitionDuration <= 0f ? _targetFill
                : Mathf.MoveTowards(_fill.fillAmount, _targetFill,
                    Time.unscaledDeltaTime / _transitionDuration);
        }

        /// <summary>
        /// current와 maximum으로 게이지 비율과 숫자 표시를 갱신한다.
        /// showPositiveSubOneAsOne이 참이고 current가 0 초과 1 이하이면 숫자만 1로 표시하며, immediate이면 채움을 즉시 반영한다.
        /// </summary>
        public void SetValue(
            float current,
            float maximum,
            bool immediate = false,
            bool showPositiveSubOneAsOne = false)
        {
            _targetFill = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            _group.alpha = 1f;

            float displayedCurrent = showPositiveSubOneAsOne && current > 0f && current <= 1f ? 1f : Mathf.Max(0f, current);

            _valueText.SetText("{0:0} / {1:0}", displayedCurrent, Mathf.Max(0f, maximum));

            if (immediate)
                _fill.fillAmount = _targetFill;
        }

        /// <summary>입력 없이 공급 전 상태를 빈 게이지·미연결 텍스트·설정 투명도로 표시한다.</summary>
        public void SetUnavailable()
        {
            _targetFill = 0f;
            _fill.fillAmount = 0f;
            _group.alpha = _unavailableAlpha;
            _valueText.text = "-- / --";
        }
    }
}
