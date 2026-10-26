using UnityEngine;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>발소리 또는 공격 전조 Image와 그 아래 방향 원호를 함께 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatFootstepMarker : MonoBehaviour
    {
        private const float KATANA_VISIBLE_FRACTION = 0.70f;
        private const float FANGS_VISIBLE_FRACTION = 0.70f;

        [Header("표시 연결")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private UnityEngine.UI.Image _icon;
        [SerializeField] private DefenseDirectionMarker _arc;

        [Header("공격 전조 이미지")]
        [SerializeField, InspectorName("쌍검 스프라이트")] private Sprite _crossedKatanaSprite;
        [SerializeField, InspectorName("이빨 스프라이트")] private Sprite _fangsSprite;
        [SerializeField, InspectorName("위험 배지 이미지")] private UnityEngine.UI.Image _warningBadge;

        [Header("방향 호")]
        [SerializeField, Min(1f)] private float _arcThickness = 4f;
        [SerializeField, Range(5f, 90f)] private float _arcAngle = 18f;
        [SerializeField, Min(0f)] private float _iconArcGap = 6f;
        [SerializeField] private Color _arcTint = Color.white;
        private Sprite _footstepSprite;

        void Awake()
        {
            _footstepSprite = _icon.sprite;
        }

        /// <summary>presentation의 방향·색·투명도를 radius·size 배치로 표시하고 아이콘 아래에 설정 두께·각도의 원호를 그린다.</summary>
        public void Show(ThreatPresentationSnapshot presentation, float radius, float size)
        {
            _icon.sprite = _footstepSprite;
            if (_warningBadge != null)
                _warningBadge.enabled = false;
            Color color = presentation.Color;
            color.a = presentation.Alpha;
            ShowMarker(presentation, radius, size, size, color);
        }

        /// <summary>
        /// snapshot의 공격 유형과 진행도로 Sprite·색·크기·위험 배지를 선택한다.
        /// readyColor·warningColor·dangerColor와 emphasisScale을 적용해 기존 방향 원호도 함께 갱신한다.
        /// </summary>
        public void ShowAttack(AttackTelegraphPresentationSnapshot snapshot, float radius, float size,
            Color readyColor, Color warningColor, Color dangerColor, float emphasisScale)
        {
            float progress = snapshot.Progress;
            bool danger = progress >= 0.75f || snapshot.IsCommitted;
            Color color = danger
                ? dangerColor : Color.Lerp(readyColor, warningColor, Mathf.Clamp01(progress / 0.75f));
            color.a *= snapshot.Presentation.Alpha;
            float pulse = snapshot.IsCommitted ? 1f
                : danger ? 1f - Mathf.Clamp01((progress - 0.75f) / 0.125f) : 0f;
            bool isFangs = snapshot.Kind == AttackIndicatorKind.Fangs;
            float displaySize = size * Mathf.Lerp(1f, emphasisScale, pulse);
            float visibleSize = displaySize * (isFangs ? FANGS_VISIBLE_FRACTION : KATANA_VISIBLE_FRACTION);
            _icon.sprite = isFangs ? _fangsSprite : _crossedKatanaSprite;
            ShowMarker(snapshot.Presentation, radius, displaySize, visibleSize, color);
            if (_warningBadge != null)
            {
                _warningBadge.color = color;
                _warningBadge.enabled = danger;
            }
        }

        /// <summary>presentation 방향에 아이콘과 원호를 놓고, 위험 배지를 원호 중앙에 화면 고정 방향으로 배치하며 color를 적용한다.</summary>
        private void ShowMarker(ThreatPresentationSnapshot presentation, float radius, float size,
            float visibleSize, Color color)
        {
            _rect.anchoredPosition = presentation.UiDirection * radius;
            _rect.sizeDelta = Vector2.one * size;
            _rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(presentation.UiDirection.y, presentation.UiDirection.x) * Mathf.Rad2Deg - 90f);
            _icon.color = color;
            _icon.enabled = true;
            float arcRadius = Mathf.Max(0f, radius - visibleSize * 0.5f - _iconArcGap - _arcThickness * 0.5f);
            _arc.rectTransform.anchoredPosition = new Vector2(0f, -radius);
            _arc.rectTransform.sizeDelta = Vector2.one * (radius * 2f);
            _arc.Configure(arcRadius, 1f, _arcThickness, _arcAngle);
            _arc.color = color * _arcTint;
            _arc.enabled = true;
            if (_warningBadge != null)
            {
                Quaternion inverse = Quaternion.Inverse(_rect.rotation);
                _warningBadge.rectTransform.anchoredPosition = new Vector2(0f, -radius + arcRadius);
                _warningBadge.rectTransform.localRotation = inverse;
                _warningBadge.rectTransform.sizeDelta = Vector2.one * (Mathf.Max(12f, visibleSize * 0.28f) * 1.5f);
            }
        }

        /// <summary>입력 없이 아이콘과 원호를 함께 숨긴다. 재사용할 이미지·원호 연결은 유지한다.</summary>
        public void Hide()
        {
            _icon.enabled = false;
            _arc.enabled = false;
            if (_warningBadge != null)
                _warningBadge.enabled = false;
        }
    }
}
