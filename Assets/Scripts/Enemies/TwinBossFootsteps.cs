using UnityEngine;

using UnityEngine.AI;

/// <summary>
/// 쌍둥이의 지상 이동 상태를 공용 발자국 생성기와 발소리에 연결한다.
/// 비 내리는 스테이지가 물 발소리를 쓰면 플레이어와 같은 찰박이는 물 발소리를 낸다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(NavMeshAgent), typeof(FootstepEmitter))]
public sealed class TwinBossFootsteps : MonoBehaviour
{
    [Header("발소리")]
    [SerializeField, Min(0f)] private float _volume = 1.7782794f;
    [SerializeField, Min(0f), Tooltip("비 내리는 스테이지의 물 발소리(RainFootstep) 음량. 플레이어 일반 보행 배율은 0.6이다.")]
    private float _waterVolume = 1.2f;
    private NavMeshAgent _agent;
    private FootstepEmitter _emitter;
    private RainyStageAudio _rainAudio;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _emitter = GetComponent<FootstepEmitter>();
        _rainAudio = FindFirstObjectByType<RainyStageAudio>();
    }

    void OnEnable()
    {
        _emitter.OnFootstepEmitted += HandleFootstep;
    }

    void LateUpdate()
    {
        _emitter.enabled = _agent.isActiveAndEnabled && _agent.isOnNavMesh && _agent.updatePosition;
    }

    /// <summary>
    /// character의 지면 접촉 position에 발소리를 재생한다. 씬의 RainyStageAudio가 물 발소리를 쓰면 RainFootstep을 _waterVolume으로,
    /// 아니면 공용 적 발소리를 _volume으로 재생한다.
    /// </summary>
    private void HandleFootstep(Transform character, Vector3 position)
    {
        if (_rainAudio != null && _rainAudio.isActiveAndEnabled && _rainAudio.UseWaterFootsteps)
        {
            Managers.Sound.Play(SoundId.RainFootstep, position, volume: _waterVolume, owner: gameObject,
                playDuringMovement: true);
            return;
        }
        SoundId sound = EnemySoundSettings.GetSound(EnemySoundAction.Footstep, EnemySoundStage.Action);
        if (sound != SoundId.None)
            Managers.Sound.Play(sound, position, volume: _volume, owner: gameObject, playDuringMovement: true);
    }

    void OnDisable()
    {
        _emitter.OnFootstepEmitted -= HandleFootstep;
        _emitter.enabled = false;
    }
}
