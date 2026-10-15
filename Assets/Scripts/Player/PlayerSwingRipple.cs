using UnityEngine;

/// <summary>
/// 일반 공격과 돌진 일섬으로 검을 휘두를 때마다 플레이어 발밑에서 파동을 한 번 낸다.
/// 검기 이펙트와 같은 순간(공통 베기 구간 시작)에 걸음 파동과 같은 시야 맵 파동으로 낸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(PlayerActionDriver))]
public sealed class PlayerSwingRipple : MonoBehaviour
{
    [Header("휘두르기 파동")]
    [SerializeField, Tooltip("한 번 휘두를 때 내는 파동. 회당 한 번이라 링은 1겹이 기본이다.")]
    private RevealRippleSettings _ripple = new RevealRippleSettings(14.4f, 1.2f, 1, 0.15f, 1.2f, Color.white);

    [Header("상태")]
    private PlayerController _player;
    private PlayerActionDriver _timing;
    private PerceptionRevealMap _revealMap;
    private PlayerAttackKind _kind;
    private int _actionId;
    private bool _pending;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _timing = GetComponent<PlayerActionDriver>();
    }

    void OnEnable()
    {
        _player.ActionEvent += HandleAction;
    }

    void OnDisable()
    {
        _player.ActionEvent -= HandleAction;
        _pending = false;
    }

    void Update()
    {
        if (!_pending || _player.IsPaused)
            return;
        if (_player.CurrentActionId != _actionId || (_player.CurrentState != PlayerActionState.NormalAttack
            && _player.CurrentState != PlayerActionState.IaidoSlash))
        {
            _pending = false;
            return;
        }
        _timing.GetStrikeWindow(_kind, out float start, out _);
        if (_timing.GetPresentationElapsed(_actionId) < start)
            return;
        _pending = false;
        Emit();
    }

    /// <summary>
    /// action이 일반 공격 시작이나 돌진 일섬 시작이면 그 행동의 베기 구간 시작에 파동을 한 번 내도록 예약한다.
    /// 이전 예약은 버린다.
    /// </summary>
    private void HandleAction(PlayerActionEvent action)
    {
        bool normal = action.Stage == PlayerActionStage.AttackStarted && action.State == PlayerActionState.NormalAttack;
        if (!normal && action.Stage != PlayerActionStage.SlashStarted)
            return;
        _actionId = action.ActionId;
        _kind = normal ? PlayerAttackKind.Normal : PlayerAttackKind.IaidoFinisher;
        _pending = true;
    }

    /// <summary>플레이어 발밑 위치에서 _ripple 파동을 시야 맵에 등록한다. 시야 맵이 없으면 무시한다.</summary>
    private void Emit()
    {
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null)
                return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
            if (_revealMap == null)
                return;
        }
        _revealMap.EmitRipple(transform.position, _ripple);
    }
}
