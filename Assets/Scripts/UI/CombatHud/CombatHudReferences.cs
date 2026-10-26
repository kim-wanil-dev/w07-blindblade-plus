using UnityEngine;

using TMPro;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>HUD 연결 도구가 사용하는 프리팹 내부 참조다. 표시 대상은 Inspector에서 명시적으로 지정한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatHudReferences : MonoBehaviour
    {
        [Header("기존 표시 연결")]
        [SerializeField] private TextMeshProUGUI _recordText;
        [SerializeField] private DefenseDirectionMarker[] _directionMarkers;
        [SerializeField] private TextMeshProUGUI _multiKillLatestText;
        [SerializeField] private TextMeshProUGUI _multiKillPreviousText;

        [Header("전투 HUD 연결")]
        [SerializeField] private CombatPlayerHud _playerHud;
        [SerializeField] private CombatFootstepHud _footstepHud;

        [Header("수리검 방향 인디케이터")]
        [SerializeField, InspectorName("수리검 인디케이터 이미지"), Tooltip("모든 방향 마커가 공유하는 흰색 Sprite입니다.")]
        private Sprite _shurikenDirectionSprite;

        public TextMeshProUGUI RecordText => _recordText;
        public DefenseDirectionMarker[] DirectionMarkers => _directionMarkers;
        public TextMeshProUGUI MultiKillLatestText => _multiKillLatestText;
        public TextMeshProUGUI MultiKillPreviousText => _multiKillPreviousText;
        public CombatPlayerHud PlayerHud => _playerHud;
        public CombatFootstepHud FootstepHud => _footstepHud;

        void Awake()
        {
            ApplyDirectionMarkerSprite();
        }

        void OnValidate()
        {
            ApplyDirectionMarkerSprite();
        }

        /// <summary>Inspector의 공유 Sprite를 각 방향 마커의 자식 Image에 적용한다.</summary>
        private void ApplyDirectionMarkerSprite()
        {
            if (_directionMarkers == null)
                return;
            foreach (DefenseDirectionMarker marker in _directionMarkers)
            {
                if (marker != null)
                    marker.SetShurikenIconSprite(_shurikenDirectionSprite);
            }
        }
    }
}
