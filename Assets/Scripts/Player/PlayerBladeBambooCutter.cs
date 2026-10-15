using UnityEngine;

/// <summary>
/// 일반 공격과 돌진 일섬의 공통 베기 구간 동안 칼날이 쓸고 지나는 면으로 대나무와 날아가는 조각을 벤다.
/// 적 판정과 같은 칼날 양 끝을 쓰며, 1인칭 뷰모델 칼끝은 공격 사거리만큼 늘려 월드 칼날로 맞춘다. 적 판정 결과와는 따로 움직인다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(PlayerActionDriver), typeof(PlayerMotor))]
public sealed class PlayerBladeBambooCutter : MonoBehaviour
{
    [Header("칼날")]
    [SerializeField, Tooltip("적 판정과 같은 칼날 손잡이 쪽 끝.")]
    private Transform _bladeBase;
    [SerializeField, Tooltip("적 판정과 같은 칼끝.")]
    private Transform _bladeTip;
    [SerializeField, Min(0f), Tooltip("일반 공격이 대나무를 베는 시선 원점 기준 사거리(미터). 플레이어 이동 경계와 숲 사이가 1m라 적 판정 사거리(1.5m)보다 길게 둔다. 적 판정에는 영향이 없다.")]
    private float _normalReach = 4.5f;
    [SerializeField, Min(0f), Tooltip("돌진 일섬이 대나무를 베는 시선 원점 기준 사거리(미터). 적 판정에는 영향이 없다.")]
    private float _iaidoReach = 4.5f;

    [Header("디버그")]
    [SerializeField, Tooltip("켜면 베기 구간 동안 칼날이 프레임마다 쓸고 지난 면을 Scene 뷰(Game 뷰는 Gizmos 켬)에 선으로 그린다.")]
    private bool _drawSweep = true;
    [SerializeField, Min(0f), Tooltip("칼날 경로 선이 남는 실시간(초).")]
    private float _drawDuration = 2f;

    [Header("상태")]
    private PlayerController _player;
    private PlayerActionDriver _timing;
    private PlayerMotor _motor;
    private PlayerAttackKind _kind;
    private Vector3 _previousBase;
    private Vector3 _previousTip;
    private int _actionId;
    private bool _active;
    private bool _hasPrevious;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _timing = GetComponent<PlayerActionDriver>();
        _motor = GetComponent<PlayerMotor>();
    }

    void OnEnable()
    {
        _player.ActionEvent += HandleAction;
    }

    void OnDisable()
    {
        _player.ActionEvent -= HandleAction;
        _active = false;
    }

    void LateUpdate()
    {
        if (!_active || _player.IsPaused)
            return;
        if (_player.CurrentActionId != _actionId || (_player.CurrentState != PlayerActionState.NormalAttack
            && _player.CurrentState != PlayerActionState.IaidoSlash))
        {
            _active = false;
            return;
        }
        _timing.GetStrikeWindow(_kind, out float start, out float end);
        float elapsed = _timing.GetPresentationElapsed(_actionId);
        if (elapsed < start)
            return;
        Vector3 currentBase = _bladeBase.position;
        Vector3 currentTip = GetWorldTip(currentBase, _kind == PlayerAttackKind.IaidoFinisher ? _iaidoReach : _normalReach);
        if (_hasPrevious)
        {
            BambooGrove.CutBySweep(_previousBase, _previousTip, currentBase, currentTip);
            if (_drawSweep)
                DrawSweep(_previousBase, _previousTip, currentBase, currentTip);
        }
        _previousBase = currentBase;
        _previousTip = currentTip;
        _hasPrevious = true;
        if (elapsed >= end)
            _active = false;
    }

    /// <summary>
    /// action이 일반 공격 시작이나 돌진 일섬 시작이면 그 행동의 베기 구간 칼날 추적을 새로 시작한다.
    /// 이전 행동의 칼날 위치는 버려 서로 다른 공격 사이를 잇지 않는다.
    /// </summary>
    private void HandleAction(PlayerActionEvent action)
    {
        bool normal = action.Stage == PlayerActionStage.AttackStarted && action.State == PlayerActionState.NormalAttack;
        if (!normal && action.Stage != PlayerActionStage.SlashStarted)
            return;
        _actionId = action.ActionId;
        _kind = normal ? PlayerAttackKind.Normal : PlayerAttackKind.IaidoFinisher;
        _active = true;
        _hasPrevious = false;
    }

    /// <summary>
    /// 이전 칼날(previousBase→previousTip)과 지금 칼날(currentBase→currentTip)이 이루는 면의 테두리와 대각선을 _drawDuration 동안 그린다.
    /// 지금 칼날은 노란색, 나머지는 하늘색이다. 에디터에서만 보인다.
    /// </summary>
    private void DrawSweep(Vector3 previousBase, Vector3 previousTip, Vector3 currentBase, Vector3 currentTip)
    {
        Debug.DrawLine(currentBase, currentTip, Color.yellow, _drawDuration);
        Debug.DrawLine(previousTip, currentTip, Color.cyan, _drawDuration);
        Debug.DrawLine(previousBase, currentBase, Color.cyan, _drawDuration);
        Debug.DrawLine(previousBase, currentTip, Color.cyan, _drawDuration);
    }

    /// <summary>
    /// bladeBase에서 뷰모델 칼날 방향으로, 시선 원점에서 reach까지 닿도록 늘린 월드 칼끝을 반환한다.
    /// 적 판정의 뷰모델 사거리 보정과 같은 식이다.
    /// </summary>
    private Vector3 GetWorldTip(Vector3 bladeBase, float reach)
    {
        Vector3 direction = (_bladeTip.position - bladeBase).normalized;
        float length = Mathf.Max(0f, reach - Vector3.Distance(_motor.ViewOrigin, bladeBase));
        return bladeBase + direction * length;
    }
}
