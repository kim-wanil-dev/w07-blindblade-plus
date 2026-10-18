using UnityEngine;


/// <summary>
/// 실제 적 피해·사망·추적 상태 사건을 중앙 사운드 설정에 연결한다.
/// 사망 판정은 적 시스템에 맡기고 사망음은 적 루트 비활성화 후에도 자연 종료한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyDamageAudio : MonoBehaviour
{
    [Header("Damage Sound")]
    [SerializeField] private SoundId _damageSound = SoundId.SwordHit;
    [SerializeField, Min(0f)] private float _volume = 0.8f;
    [SerializeField, Range(0.25f, 4f)] private float _pitch = 1f;
    private EnemyHealth _health;

    [Header("사망음")]
    [SerializeField] private SoundId _deathSound = SoundId.None;

    [Header("사망음 랜덤 피치")]
    [SerializeField, Range(-12f, 0f), Tooltip("낮아지는 피치의 최소 반음입니다. 0은 원래 피치입니다.")]
    private float _minimumDeathPitchSemitones = -4f;
    [SerializeField, Range(-12f, 0f), Tooltip("낮아지는 피치의 최대 반음입니다. 양수 피치는 사용하지 않습니다.")]
    private float _maximumDeathPitchSemitones = -1f;
    [SerializeField, Range(0f, 1f), Tooltip("피치를 바꾸지 않고 원래 음성을 재생할 확률입니다.")]
    private float _originalDeathPitchChance = 0.3f;
    private float _deathPitch = 1f;

    [Header("적 상태 연결")]
    private EnemyController _controller;
    private EnemySensor _sensor;
    private Vector3 _lastDamagePoint;
    private bool _hasDamagePoint;
    private bool _deathPlayed;
    private bool _discoveryPlayed;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _controller = GetComponent<EnemyController>();
        _sensor = GetComponent<EnemySensor>();
    }

    void OnEnable()
    {
        _health.Damaged += HandleDamage;
        _health.Died += HandleDeath;
        _deathPlayed = false;
        _deathPitch = 1f;
        _discoveryPlayed = false;
        _hasDamagePoint = false;
        if (_controller != null)
        {
            _controller.StateChanged += HandleState;
            HandleState(_controller.State);
        }
    }

    void OnDisable()
    {
        _health.Damaged -= HandleDamage;
        _health.Died -= HandleDeath;
        if (_controller != null) _controller.StateChanged -= HandleState;
    }

    /// <summary>
    /// context의 피해 지점과 적용 후 체력으로 일반 피격 또는 치명타격을 한 번 재생한다.
    /// 생존 피격에는 검 적중음과 적 고통 음성을 재생하고 치명타·사망에는 일반 고통 음성을 추가하지 않는다.
    /// </summary>
    private void HandleDamage(DamageContext context)
    {
        _lastDamagePoint = context.Point;
        _hasDamagePoint = true;
        if (_health.CurrentHealth <= 0f)
        {
            _deathPitch = GetDeathPitch();
            Managers.Sound.PlayCue(CombatSoundCue.EnemyFinished, context.Point, context.Instigator, pitch: _deathPitch);
        }
        else
        {
            Managers.Sound.Play(_damageSound, context.Point, _volume, context.Instigator, pitch: _pitch);
            Managers.Sound.StopFor(gameObject, SoundId.EnemyDamaged, forceStop: true);
            Managers.Sound.PlayAttached(SoundId.EnemyDamaged, gameObject);
        }
    }

    /// <summary>
    /// health의 사망 확정에 마지막 피해 지점 또는 적 위치에서 사망음을 한 번 재생한다.
    /// 인간형 전용 사망음에는 이벤트의 기본 -3반음에 더할 ±2반음 배율을 적용한다.
    /// 다른 전용음 또는 공통 사망음은 기존 연결을 사용하며 적 비활성화 후에도 소리의 끝을 유지한다.
    /// </summary>
    private void HandleDeath(EnemyHealth health)
    {
        if (_deathPlayed) return;
        _deathPlayed = true;
        if (!_hasDamagePoint) _deathPitch = GetDeathPitch();
        Vector3 position = _hasDamagePoint ? _lastDamagePoint : health.transform.position;
        if (_deathSound == SoundId.HumanEnemyDie)
            Managers.Sound.Play(_deathSound, position, playDuringMovement: true, pitch: GetHumanDeathPitch());
        else if (_deathSound != SoundId.None)
            Managers.Sound.Play(_deathSound, position, playDuringMovement: true);
        else
            Managers.Sound.PlayCue(CombatSoundCue.EnemyDie, position, pitch: _deathPitch);
    }

    /// <summary>soundId를 이 적의 전용 사망음으로 저장한다. None이면 기존 공통 사망음을 사용한다.</summary>
    internal void ConfigureDeathSound(SoundId soundId)
    {
        _deathSound = soundId;
    }

    /// <summary>
    /// 원래 피치 확률과 최소·최대 반음 범위로 이번 사망의 피치 배율을 반환한다.
    /// 0반음은 1배이며 음수를 2의 반음/12승으로 변환해 피치를 낮추고 양수 변주는 제한한다.
    /// </summary>
    private float GetDeathPitch()
    {
        if (Random.value < Mathf.Clamp01(_originalDeathPitchChance)) return 1f;
        float minimum = Mathf.Clamp(_minimumDeathPitchSemitones, -12f, 0f);
        float maximum = Mathf.Clamp(_maximumDeathPitchSemitones, minimum, 0f);
        return Mathf.Pow(2f, Random.Range(minimum, maximum) / 12f);
    }

    /// <summary>
    /// 인간형 사망음의 FMOD 기본 -3반음에 더할 -2~+2반음을 매 재생마다 선택한다.
    /// 선택한 반음을 피치 배율로 변환해 반환한다.
    /// </summary>
    private float GetHumanDeathPitch()
    {
        return Mathf.Pow(2f, Random.Range(-2f, 2f) / 12f);
    }

    /// <summary>
    /// state가 실제 시야 확보 후 추적으로 전환되면 발견 전조음을 한 번 재생한다.
    /// 수색·대기·순찰로 돌아가면 재발견을 허용하며 반복 공격 전환에서는 중복 재생하지 않는다.
    /// </summary>
    private void HandleState(EnemyState state)
    {
        if (state == EnemyState.Idle || state == EnemyState.Patrolling || state == EnemyState.Searching)
        {
            _discoveryPlayed = false;
            return;
        }
        if (state != EnemyState.Chasing || _discoveryPlayed || !_health.IsAlive || !_sensor.CanSeeTarget()) return;
        _discoveryPlayed = true;
        Managers.Sound.PlayCue(CombatSoundCue.EnemyPrepare, transform.position, gameObject, true);
    }
}
