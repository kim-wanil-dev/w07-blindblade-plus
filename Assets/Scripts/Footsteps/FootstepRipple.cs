using UnityEngine;

/// <summary>
/// 같은 오브젝트의 플레이어 발걸음 또는 적 FootstepEmitter 지면 접촉을 세어 지정한 걸음 수마다 시간차 다중 링 파동을 시야 맵에 낸다.
/// 파동이 지나간 실루엣은 설정한 잔상 시간 동안 보인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class FootstepRipple : MonoBehaviour
{
    [Header("걸음 파동")]
    [SerializeField]
    private RevealRippleSettings _ripple = new RevealRippleSettings(5f, 0.6f, 3, 0.1f, 1.2f, Color.white);
    [SerializeField, Min(1), Tooltip("이 걸음 수마다 한 번 파동을 낸다. 2이면 빈도가 절반이다.")]
    private int _stepsPerRipple = 1;
    private int _stepCount;
    private PlayerController _player;
    private FootstepEmitter _emitter;
    private PerceptionRevealMap _revealMap;
    private EnemyMeleeAttack[] _meleeAttacks;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _emitter = GetComponent<FootstepEmitter>();
        _meleeAttacks = GetComponents<EnemyMeleeAttack>();
    }

    void OnEnable()
    {
        if (_player != null)
            _player.FootstepEmitted += HandlePlayerFootstep;
        if (_emitter != null)
            _emitter.OnFootstepEmitted += HandleGroundContact;
        foreach (EnemyMeleeAttack attack in _meleeAttacks)
        {
            attack.FirstHeartbeatPlayed += HandleMeleeTelegraph;
            attack.AttackCommitted += HandleMeleeAttack;
        }
    }

    void OnDisable()
    {
        if (_player != null)
            _player.FootstepEmitted -= HandlePlayerFootstep;
        if (_emitter != null)
            _emitter.OnFootstepEmitted -= HandleGroundContact;
        foreach (EnemyMeleeAttack attack in _meleeAttacks)
        {
            attack.FirstHeartbeatPlayed -= HandleMeleeTelegraph;
            attack.AttackCommitted -= HandleMeleeAttack;
        }
    }

    /// <summary>플레이어 발걸음 step의 착지 위치에서 파동을 낸다.</summary>
    private void HandlePlayerFootstep(PlayerFootstepEvent step)
    {
        Emit(step.WorldPosition, null);
    }

    /// <summary>적 character의 지면 접촉 position에서 파동을 내며 이 파동은 character 자신의 몸체를 밝히지 않는다.</summary>
    private void HandleGroundContact(Transform character, Vector3 position)
    {
        Emit(position, character);
    }

    /// <summary>
    /// context의 첫 박동 전조음 재생 직후 적 발밑에서 근접 공격 파동을 낸다.
    /// 공격 위치와 발생자 참조를 사용하여 이동 파동과 같은 시야 효과를 적용한다.
    /// </summary>
    private void HandleMeleeTelegraph(EnemyAttackContext context)
    {
        EmitRipple(context.Origin, context.Source);
    }

    /// <summary>
    /// context의 근접 공격 확정 시 검격 표시와 같은 프레임에 적 발밑 파동을 낸다.
    /// 고정된 공격 원점과 발생자 참조로 파동 중심과 자기 몸 제외를 유지한다.
    /// </summary>
    private void HandleMeleeAttack(EnemyAttackContext context)
    {
        EmitRipple(context.Origin, context.Source);
    }

    /// <summary>
    /// 걸음을 _stepCount에 세어 _stepsPerRipple번째 걸음마다 시야 맵에 position 중심의 _ripple 파동을 등록한다.
    /// hiddenOwner가 있으면 그 몸체를 이 파동으로 밝히지 않는다. 시야 맵이 없으면 무시한다.
    /// </summary>
    private void Emit(Vector3 position, Transform hiddenOwner)
    {
        _stepCount = (_stepCount + 1) % _stepsPerRipple;
        if (_stepCount != 0)
            return;
        EmitRipple(position, hiddenOwner);
    }

    /// <summary>
    /// position과 hiddenOwner를 사용해 기존 _ripple 설정으로 시야 파동을 등록한다.
    /// 이동·공격 모두 걸음 횟수와 독립적으로 같은 PerceptionRevealMap 효과를 사용한다.
    /// </summary>
    private void EmitRipple(Vector3 position, Transform hiddenOwner)
    {
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null)
                return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
            if (_revealMap == null)
                return;
        }
        _revealMap.EmitRipple(position, _ripple, hiddenOwner);
    }
}
