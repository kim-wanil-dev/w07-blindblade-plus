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

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _emitter = GetComponent<FootstepEmitter>();
    }

    void OnEnable()
    {
        if (_player != null)
            _player.FootstepEmitted += HandlePlayerFootstep;
        if (_emitter != null)
            _emitter.OnFootstepEmitted += HandleGroundContact;
    }

    void OnDisable()
    {
        if (_player != null)
            _player.FootstepEmitted -= HandlePlayerFootstep;
        if (_emitter != null)
            _emitter.OnFootstepEmitted -= HandleGroundContact;
    }

    /// <summary>플레이어 발걸음 step의 착지 위치에서 파동을 낸다.</summary>
    private void HandlePlayerFootstep(PlayerFootstepEvent step)
    {
        Emit(step.WorldPosition);
    }

    /// <summary>character의 지면 접촉 position에서 파동을 낸다.</summary>
    private void HandleGroundContact(Transform character, Vector3 position)
    {
        Emit(position);
    }

    /// <summary>
    /// 걸음을 _stepCount에 세어 _stepsPerRipple번째 걸음마다 시야 맵에 position 중심의 _ripple 파동을 등록한다.
    /// 시야 맵이 없으면 무시한다.
    /// </summary>
    private void Emit(Vector3 position)
    {
        _stepCount = (_stepCount + 1) % _stepsPerRipple;
        if (_stepCount != 0)
            return;
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null)
                return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
            if (_revealMap == null)
                return;
        }
        _revealMap.EmitRipple(position, _ripple);
    }
}
