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
    private const float MIN_FOOTSTEP_SOUND_INTERVAL = 0.18f;

    [Header("발소리")]
    [SerializeField, Min(0f)] private float _volume = 1.7782794f;
    [SerializeField, Min(0f), Tooltip("비 내리는 스테이지의 물 발소리(RainFootstep) 음량. 플레이어 일반 보행 배율은 0.6이다.")]
    private float _waterVolume = 1.2f;
    private NavMeshAgent _agent;
    private FootstepEmitter _emitter;
    private RainyStageAudio _rainAudio;
    private HumanBossAttack _humanAttack;
    private TwinBeastWolfVisual _beastVisual;
    private float _nextFootstepSoundTime;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _emitter = GetComponent<FootstepEmitter>();
        _rainAudio = FindFirstObjectByType<RainyStageAudio>();
        _humanAttack = GetComponent<HumanBossAttack>();
        _beastVisual = GetComponentInChildren<TwinBeastWolfVisual>(true);
    }

    void OnEnable()
    {
        _nextFootstepSoundTime = float.NegativeInfinity;
        if (_beastVisual != null)
            _beastVisual.PawPlanted += HandleBeastPawPlanted;
        else
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
        if (_humanAttack != null && _humanAttack.IsSlashing)
            return;

        PlayFootstep(position);
    }

    /// <summary>
    /// Beast 시각화가 알린 실제 발 디딤 위치에 물 발소리가 아닌 일반 적 발소리를 재생한다.
    /// 물 발소리는 BeastBossAudio가 같은 발 접촉 사건에서 재생한다.
    /// </summary>
    private void HandleBeastPawPlanted(Vector3 position)
    {
        if (_rainAudio != null && _rainAudio.isActiveAndEnabled && _rainAudio.UseWaterFootsteps)
            return;

        PlayFootstep(position);
    }

    /// <summary>
    /// position에 씬의 물 발소리 옵션에 맞는 효과음을 재생한다.
    /// 최소 0.18초 간격을 유지하고 같은 보스가 재생 중인 이전 발소리를 먼저 정지한다.
    /// </summary>
    private void PlayFootstep(Vector3 position)
    {
        SoundId sound;
        float volume;
        if (_rainAudio != null && _rainAudio.isActiveAndEnabled && _rainAudio.UseWaterFootsteps)
        {
            sound = SoundId.EnemyWaterFootstep;
            volume = _waterVolume;
        }
        else
        {
            sound = EnemySoundSettings.GetSound(EnemySoundAction.Footstep, EnemySoundStage.Action);
            volume = _volume;
        }

        if (sound == SoundId.None || Time.time < _nextFootstepSoundTime)
            return;

        _nextFootstepSoundTime = Time.time + MIN_FOOTSTEP_SOUND_INTERVAL;
        Managers.Sound.StopFor(gameObject, sound, forceStop: true);
        Managers.Sound.Play(sound, position, volume: volume, owner: gameObject, playDuringMovement: true);
    }

    void OnDisable()
    {
        if (_beastVisual != null)
            _beastVisual.PawPlanted -= HandleBeastPawPlanted;
        else
            _emitter.OnFootstepEmitted -= HandleFootstep;
        _emitter.enabled = false;
    }
}
