using System;

using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(NavMeshAgent), typeof(FootstepEmitter), typeof(EnemySensor))]
public sealed class EnemyFootsteps : MonoBehaviour
{
    private const float FOOTSTEP_VOLUME = 1.7782794f; // +5 dB = 10^(5/20).
    private const float MIN_FOOTSTEP_SOUND_INTERVAL = 0.18f;

    [Header("발걸음 설정")]
    [SerializeField] private bool _useAnimationEvents;
    [SerializeField, Min(0.01f)] private float _stepDistance = 1.2f;
    [SerializeField, Min(0f)] private float _minimumSpeed = 0.1f;
    [SerializeField, Min(0.01f)] private float _teleportDistance = 5f;
    private NavMeshAgent _agent;
    private FootstepEmitter _emitter;
    private EnemySensor _sensor;
    private RainyStageAudio _rainAudio;
    private EnemyAttackBehaviour[] _attacks;
    private DefenseBeastWolfVisual _beastVisual;
    private Vector3 _previousPosition;
    private float _distanceSinceStep;
    private float _nextFootstepSoundTime;

    public event Action<Vector3> Footstep;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _emitter = GetComponent<FootstepEmitter>();
        _sensor = GetComponent<EnemySensor>();
        _rainAudio = FindFirstObjectByType<RainyStageAudio>();
        _attacks = GetComponents<EnemyAttackBehaviour>();
        _beastVisual = GetComponentInChildren<DefenseBeastWolfVisual>(true);
    }

    void Start()
    {
        SensoryManager manager = Game.Instance.SensoryManager;
        if (manager.Observer == null)
            manager.SetObserver(_sensor.Target);
    }

    void OnEnable()
    {
        _previousPosition = transform.position;
        _distanceSinceStep = 0f;
        _nextFootstepSoundTime = float.NegativeInfinity;
        if (_emitter != null)
            _emitter.OnFootstepEmitted += HandleGroundContact;
        if (_useAnimationEvents && _beastVisual != null)
            _beastVisual.PawPlanted += HandleAnimationFootstep;
    }

    void OnDisable()
    {
        if (_emitter != null)
            _emitter.OnFootstepEmitted -= HandleGroundContact;
        if (_useAnimationEvents && _beastVisual != null)
            _beastVisual.PawPlanted -= HandleAnimationFootstep;
    }

    void LateUpdate()
    {
        Vector3 displacement = transform.position - _previousPosition;
        _previousPosition = transform.position;
        if (_emitter != null && _emitter.isActiveAndEnabled)
        {
            _distanceSinceStep = 0f;
            return;
        }
        displacement.y = 0f;
        float distance = displacement.magnitude;

        if (_useAnimationEvents || !IsWalking() || distance > _teleportDistance)
        {
            _distanceSinceStep = 0f;
            return;
        }

        _distanceSinceStep += distance;
        if (_distanceSinceStep >= _stepDistance)
        {
            _distanceSinceStep %= _stepDistance;
            EmitFootstep(transform.position);
        }
    }

    /// <summary>
    /// 애니메이션 이벤트 모드와 현재 이동 상태를 확인하여 루트 위치의 발걸음 이벤트를 발생시킨다.
    /// 공통 Emitter가 활성 상태면 중복 알림을 생략하고 비활성 상태에서만 현재 위치를 전달한다.
    /// </summary>
    public void NotifyFootstep()
    {
        if (_useAnimationEvents && IsWalking())
            EmitFootstep(transform.position);
    }

    /// <summary>
    /// animation mode에서 전달된 발 접촉 위치 position에 발소리를 재생하고 Footstep 이벤트를 발생시킨다.
    /// </summary>
    public void NotifyFootstep(Vector3 position)
    {
        if (_useAnimationEvents)
            EmitFootstep(position);
    }

    /// <summary>
    /// character가 발생시킨 공통 Emitter의 지면 접촉 position을 기존 적 발걸음 이벤트에 전달한다.
    /// 별도 효과를 생성하지 않아 발자국 생성과 접촉 알림이 한 경로에서 처리된다.
    /// </summary>
    private void HandleGroundContact(Transform character, Vector3 position)
    {
        if (!_useAnimationEvents)
            EmitFootstep(position);
    }

    /// <summary>
    /// DefenseBeastWolfVisual이 알린 실제 발 접촉 위치에서 발소리를 재생하고 Footstep 이벤트를 발생시킨다.
    /// </summary>
    private void HandleAnimationFootstep(Vector3 position)
    {
        EmitFootstep(position);
    }

    /// <summary>
    /// 활성 상태, NavMesh 상태, 속도 설정을 검사하여 유닛이 걷고 있으면 true를 반환한다.
    /// 에이전트가 멈췄거나 이동 속도가 minimumSpeed 이하이면 false를 반환한다.
    /// </summary>
    private bool IsWalking()
    {
        return isActiveAndEnabled && _agent.isActiveAndEnabled && _agent.isOnNavMesh &&
            !_agent.isStopped && _agent.velocity.sqrMagnitude > _minimumSpeed * _minimumSpeed;
    }

    /// <summary>
    /// position에서 공통 발걸음 소리를 5dB 증폭하여 최소 간격으로 재생하고 C# 이벤트에 위치를 전달한다.
    /// 같은 적의 이전 발소리는 즉시 정지하고 동시 재생 수는 SoundLibrary 최대값을 따르며, None 설정이어도 위치 이벤트는 전달한다.
    /// </summary>
    private void EmitFootstep(Vector3 position)
    {
        SoundId soundId = _rainAudio != null && _rainAudio.isActiveAndEnabled && _rainAudio.UseWaterFootsteps
            ? SoundId.EnemyWaterFootstep
            : EnemySoundSettings.GetSound(EnemySoundAction.Footstep, EnemySoundStage.Action);
        bool attackMovement = false;
        foreach (EnemyAttackBehaviour attack in _attacks)
        {
            if (attack.isActiveAndEnabled && attack.Phase == EnemyAttackPhase.Active && attack.AllowsMovement)
            {
                attackMovement = true;
                break;
            }
        }
        if (soundId != SoundId.None && Time.time >= _nextFootstepSoundTime)
        {
            _nextFootstepSoundTime = Time.time + MIN_FOOTSTEP_SOUND_INTERVAL;
            Managers.Sound.PlayEnemyFootstep(soundId, position, FOOTSTEP_VOLUME, gameObject, attackMovement);
        }
        Footstep?.Invoke(position);
    }
}
