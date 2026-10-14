using UnityEngine;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>발소리 Image와 그 아래 원호에 C의 방향·색·투명도를 적용한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatFootstepMarker : MonoBehaviour
    {
        [Header("표시 연결")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private UnityEngine.UI.Image _icon;
        [SerializeField] private DefenseDirectionMarker _arc;

        [Header("방향 호")]
        [SerializeField, Min(1f)] private float _arcThickness = 4f;
        [SerializeField, Range(5f, 90f)] private float _arcAngle = 18f;
        [SerializeField, Min(0f)] private float _iconArcGap = 6f;
        [SerializeField] private Color _arcTint = Color.white;

        /// <summary>presentation의 방향·색·투명도를 radius·size 배치로 표시하고 아이콘 아래에 설정 두께·각도의 원호를 그린다.</summary>
        public void Show(ThreatPresentationSnapshot presentation, float radius, float size)
        {
            _rect.anchoredPosition = presentation.UiDirection * radius;
            _rect.sizeDelta = Vector2.one * size;
            _rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(presentation.UiDirection.y, presentation.UiDirection.x) * Mathf.Rad2Deg - 90f);
            Color color = presentation.Color;
            color.a = presentation.Alpha;
            _icon.color = color;
            _icon.enabled = true;
            float arcRadius = Mathf.Max(0f, radius - size * 0.5f - _iconArcGap - _arcThickness * 0.5f);
            _arc.rectTransform.anchoredPosition = new Vector2(0f, -radius);
            _arc.rectTransform.sizeDelta = Vector2.one * (radius * 2f);
            _arc.Configure(arcRadius, 1f, _arcThickness, _arcAngle);
            _arc.color = color * _arcTint;
            _arc.enabled = true;
        }

        /// <summary>입력 없이 아이콘과 원호를 함께 숨긴다. 재사용할 이미지·원호 연결은 유지한다.</summary>
        public void Hide()
        {
            _icon.enabled = false;
            _arc.enabled = false;
        }
    }
}
