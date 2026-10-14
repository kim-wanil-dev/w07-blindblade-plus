using UnityEngine;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>발소리 Image 프리팹에 C의 방향·색·투명도를 적용한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatFootstepMarker : MonoBehaviour
    {
        [Header("표시 연결")]
        [SerializeField] private RectTransform _rect;
        [SerializeField] private UnityEngine.UI.Image _icon;

        /// <summary>presentation의 방향·색·투명도를 radius·size 배치로 표시한다. 원점이나 인지 규칙은 계산하지 않는다.</summary>
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
        }

        /// <summary>입력 없이 아이콘을 숨긴다. 재사용을 위해 프리팹 이미지와 배치는 유지한다.</summary>
        public void Hide()
        {
            _icon.enabled = false;
        }
    }
}
