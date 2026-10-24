using UnityEngine;


/// <summary>
/// 기존 플레이어 행동·접촉 이벤트를 구독해 검 접촉 감지·타격·혈흔을 요청한다.
/// 전투 판정과 사운드는 변경하지 않는다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(150)]
public sealed class PlayerCombatEffects : MonoBehaviour
{
    [Header("플레이어 연결")]
    [SerializeField] private PlayerController _player;
    [SerializeField] private PlayerActionDriver _timing;
    [SerializeField] private EnemyCombatService _contacts;
    [SerializeField] private SwordEffectEmitter _physicalContacts;
    [SerializeField] private KatanaCombatCoordinator _katana;

    [Header("적중 반동")]
    [SerializeField, Min(0f)] private float _hitRecoilStrength = 0.25f;
    private PlayerMotor _motor;

    [Header("플레이어 피격 표시")]
    [SerializeField, Min(0.1f)] private float _damageEffectDistance = 0.8f;
    [SerializeField] private float _damageEffectVerticalOffset = -0.1f;
    private PlayerHealth _health;
    private Camera _damageCamera;

    [Header("방어 섬광 표시")]
    [SerializeField, Min(0.1f)] private float _defenseEffectDistance = 0.6f;
    [SerializeField, Min(0f)] private float _defenseEffectScreenOffset = 0.12f;

    private EffectManager _effects;
    private int _actionId;
    private int _lastContactAction = -1;
    private float _beginTime;
    private float _endTime;
    private bool _tracking;
    private bool _started;

    void OnEnable()
    {
        EnemyCombatService playerContacts = _player.GetComponent<EnemyCombatService>();
        if (playerContacts != null) _contacts = playerContacts;
        _motor = _player.GetComponent<PlayerMotor>();
        _health = _player.GetComponent<PlayerHealth>();
        _damageCamera = _player.GetComponentInChildren<Camera>();
        _health.DamageReceived += HandlePlayerDamaged;
        if (_contacts != null) _contacts.AttackContactResolved += HandleHitFeedback;
        _player.ActionEvent += HandleAction;
        if (_contacts != null && _physicalContacts == null)
            _contacts.AttackContactResolved += HandleContact;
        _katana.DefenseSucceeded += HandleDefense;
        _lastContactAction = -1;
    }

    void Start()
    {
        _effects = Game.Instance.EffectManager;
        if (!_effects.IsReady)
            _effects.Initialize();
    }

    void OnDisable()
    {
        _health.DamageReceived -= HandlePlayerDamaged;
        _player.ActionEvent -= HandleAction;
        if (_contacts != null) _contacts.AttackContactResolved -= HandleHitFeedback;
        if (_contacts != null && _physicalContacts == null)
            _contacts.AttackContactResolved -= HandleContact;
        _katana.DefenseSucceeded -= HandleDefense;
        CancelContactDetection();
    }

    void LateUpdate()
    {
        if (!_tracking || _player.IsPaused || Time.deltaTime <= 0f)
            return;
        if (_effects == null || !_effects.IsReady || _player.CurrentActionId != _actionId)
        {
            CancelContactDetection();
            return;
        }
        float elapsed = _timing.GetPresentationElapsed(_actionId);
        if (elapsed >= _endTime)
        {
            EndContactDetection();
            return;
        }
        if (!_started && elapsed >= _beginTime)
        {
            if (_physicalContacts != null)
                _started = _physicalContacts.BeginSwing();
        }
    }

    /// <summary>
    /// action의 일반·발도 베기 시작과 종료·사망을 관찰한다.
    /// action의 표현 타이밍으로 접촉 감지 구간을 예약하고 정상 종료 또는 취소한다.
    /// </summary>
    private void HandleAction(PlayerActionEvent action)
    {
        bool normal = action.Stage == PlayerActionStage.AttackStarted
            && action.State == PlayerActionState.NormalAttack;
        bool iaido = action.Stage == PlayerActionStage.SlashStarted;
        if (normal || iaido)
        {
            CancelContactDetection();
            _actionId = action.ActionId;
            _timing.GetStrikeWindow(iaido ? PlayerAttackKind.IaidoFinisher : PlayerAttackKind.Normal,
                out _beginTime, out _endTime);
            _tracking = _physicalContacts != null;
        }
        else if (action.Stage == PlayerActionStage.ActionEnded && action.ActionId == _actionId)
            EndContactDetection();
        else if (action.Stage == PlayerActionStage.StateChanged && action.State == PlayerActionState.Dead)
            CancelContactDetection();
    }

    /// <summary>request의 적 피해 확정에만 동일한 시각 반동을 한 번 적용한다. 물리 접촉 이펙트와 피해·논리 조준은 변경하지 않는다.</summary>
    private void HandleHitFeedback(PlayerAttackRequest request, bool hasContact, Vector3 point, bool enemyDamaged)
    {
        if (!enemyDamaged || request.ActionId != _player.CurrentActionId) return;
        _motor.AddCombatRecoil(request.Direction, _hitRecoilStrength);
    }

    /// <summary>
    /// request의 실제 접촉 hasContact와 월드 point로 공용 타격을 요청한다.
    /// 같은 행동의 접촉 알림은 한 번만 처리하며 혈흔은 적의 실제 피해 처리에서 재생한다.
    /// </summary>
    private void HandleContact(PlayerAttackRequest request, bool hasContact, Vector3 point, bool enemyDamaged)
    {
        if (request.ActionId != _player.CurrentActionId || _lastContactAction == request.ActionId)
            return;
        _lastContactAction = request.ActionId;
        if (!hasContact || _effects == null || !_effects.IsReady)
            return;
        _effects.PlayHit(point);
    }

    /// <summary>
    /// 실제 피해 context를 받으면 현재 시야 앞에 PlayerDamageHit를 재생한다.
    /// 표시 거리와 수직 오프셋을 사용하며 가드·패링으로 차단된 피해에는 호출되지 않는다.
    /// </summary>
    private void HandlePlayerDamaged(DamageContext context)
    {
        Vector3 forward = _damageCamera.transform.forward;
        Vector3 point = _damageCamera.transform.position + forward * _damageEffectDistance
            + _damageCamera.transform.up * _damageEffectVerticalOffset;
        _effects.Play(EffectId.PlayerDamageHit, point,
            Quaternion.LookRotation(-forward, _damageCamera.transform.up));
    }

    /// <summary>
    /// result의 방어 종류로 가드·패링 섬광을 선택한다.
    /// 접촉 방향을 화면 중앙 근처에 표시하고 최소 거리를 확보하여 섬광이 잘리지 않게 한다.
    /// </summary>
    private void HandleDefense(KatanaDefenseResult result)
    {
        if (_effects == null || !_effects.IsReady) return;
        Vector3 forward = _motor.ViewForward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 0.001f) right = _player.transform.right;
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Vector3 offset = result.Point - _motor.ViewOrigin;
        float depth = Mathf.Max(_defenseEffectDistance, Vector3.Dot(offset, forward));
        Vector3 point = _motor.ViewOrigin + forward * depth
            + right * Mathf.Clamp(Vector3.Dot(offset, right), -_defenseEffectScreenOffset, _defenseEffectScreenOffset)
            + up * Mathf.Clamp(Vector3.Dot(offset, up), -_defenseEffectScreenOffset, _defenseEffectScreenOffset);
        _effects.Play(result.IsParry ? EffectId.ParryClash : EffectId.GuardClash, point,
            Quaternion.LookRotation(-forward, up));
    }

    /// <summary>
    /// 입력 없이 연결된 검의 접촉 감지를 정상 종료한다.
    /// 현재 공격의 예약 상태를 해제하며 재생된 타격 효과는 자연 종료한다.
    /// </summary>
    private void EndContactDetection()
    {
        if (_physicalContacts != null)
            _physicalContacts.EndSwing();
        _tracking = false;
        _started = false;
    }

    /// <summary>
    /// 입력 없이 연결된 검의 접촉 감지와 예약을 즉시 취소한다.
    /// 비활성화·사망·새 공격 시작 시 이전 감지 상태를 남기지 않는다.
    /// </summary>
    private void CancelContactDetection()
    {
        if (_physicalContacts != null)
            _physicalContacts.CancelSwing();
        _tracking = false;
        _started = false;
    }
}
