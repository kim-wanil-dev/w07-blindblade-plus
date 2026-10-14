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

        [Header("전투 HUD 연결")]
        [SerializeField] private CombatPlayerHud _playerHud;
        [SerializeField] private CombatFootstepHud _footstepHud;

        public TextMeshProUGUI RecordText => _recordText;
        public DefenseDirectionMarker[] DirectionMarkers => _directionMarkers;
        public CombatPlayerHud PlayerHud => _playerHud;
        public CombatFootstepHud FootstepHud => _footstepHud;
    }
}
