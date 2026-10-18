using UnityEngine;

/// <summary>짐승의 공격·피격·사망·대기 사건을 전용 FMOD 소리에 연결한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BeastBossAttack))]
public sealed class BeastBossAudio : MonoBehaviour
{
    [Header("짐승 음성")]
    [SerializeField] private SoundId _attackSound = SoundId.BeastBossAttack;
    [SerializeField] private SoundId _hitSound = SoundId.BeastBossHit;
    [SerializeField] private SoundId _deathSound = SoundId.BeastBossDie;
    [SerializeField] private SoundId _idleSound = SoundId.BeastBossIdle;
    [SerializeField] private SoundId _chargeBarkSound = SoundId.BeastBossChargeBark;

    [Header("물 발소리")]
    [SerializeField, Min(0f), Tooltip("플레이어 일반 보행의 물 발소리 배율 0.6 대비 약 10dB 큰 짐승 발소리 배율")]
    private float _waterFootstepVolume = 1.9f;
    [SerializeField, Min(0.18f)] private float _waterFootstepInterval = 0.18f;

    [Header("칼 적중음")]
    [SerializeField, Min(0f)] private float _swordHitVolume = 1.1f;
    [SerializeField, Range(0.25f, 4f)] private float _swordHitPitch = 1.15f;
    private BeastBossAttack _attack;
    private TwinBeastWolfVisual _wolfVisual;
    private RainyStageAudio _rainAudio;
    private EnemyHealth _health;
    private TwinBossController _controller;
    private float _nextWaterFootstepTime;

    void Awake()
    {
        _attack = GetComponent<BeastBossAttack>();
        _wolfVisual = GetComponentInChildren<TwinBeastWolfVisual>(true);
        _rainAudio = FindFirstObjectByType<RainyStageAudio>();
        _health = GetComponent<EnemyHealth>();
        _controller = GetComponentInParent<TwinBossController>();
    }

    void OnEnable()
    {
        _nextWaterFootstepTime = float.NegativeInfinity;
        _attack.AttackPreparing += HandleAttack;
        _attack.PounceApproaching += HandlePounceApproaching;
        _wolfVisual.PawPlanted += HandlePawPlanted;
        _health.Damaged += HandleDamage;
        _health.Died += HandleDeath;
        _controller.PatternChanged += HandlePattern;
    }

    /// <summary>공격 전조가 시작되면 이전 대기음을 정지하고 짐승 공격 음성을 재생한다.</summary>
    private void HandleAttack()
    {
        Managers.Sound.StopFor(gameObject, _idleSound, forceStop: true);
        Managers.Sound.Play(_attackSound, transform.position, owner: gameObject, playDuringMovement: true);
    }

    /// <summary>짐승의 덮치기 도약 전 정해진 시각에 낮춘 짖음 이벤트를 한 번 재생한다.</summary>
    private void HandlePounceApproaching()
    {
        Managers.Sound.Play(_chargeBarkSound, transform.position, owner: gameObject,
            playDuringMovement: true);
    }

    /// <summary>짐승의 네 발 중 하나가 실제 지면에 닿으면 짧은 간격을 두고 물 발소리를 재생한다.</summary>
    private void HandlePawPlanted(Vector3 position)
    {
        PlayWaterFootstep(position);
    }

    /// <summary>씬의 물 발소리 옵션이 켜졌을 때 position에 물 발소리를 재생하고 다음 허용 시각을 갱신한다.</summary>
    private void PlayWaterFootstep(Vector3 position)
    {
        if (_rainAudio == null || !_rainAudio.isActiveAndEnabled || !_rainAudio.UseWaterFootsteps)
            return;
        if (Time.time < _nextWaterFootstepTime) return;
        _nextWaterFootstepTime = Time.time + _waterFootstepInterval;
        Managers.Sound.StopFor(gameObject, SoundId.EnemyWaterFootstep, forceStop: true);
        Managers.Sound.Play(SoundId.EnemyWaterFootstep, position, volume: _waterFootstepVolume,
            owner: gameObject, playDuringMovement: true);
    }

    /// <summary>context의 실제 적중 위치에서 칼 적중음을 재생하고 생존 피격에 짐승 고통 음성을 추가한다.</summary>
    private void HandleDamage(DamageContext context)
    {
        Managers.Sound.StopFor(gameObject, _attackSound, forceStop: true);
        Managers.Sound.StopFor(gameObject, _idleSound, forceStop: true);
        Managers.Sound.Play(SoundId.SwordHit, context.Point, _swordHitVolume,
            context.Instigator, pitch: _swordHitPitch);
        if (_health.CurrentHealth <= 0f) return;
        Managers.Sound.StopFor(gameObject, _hitSound, forceStop: true);
        Managers.Sound.Play(_hitSound, transform.position, owner: gameObject, playDuringMovement: true);
    }

    /// <summary>health 사망 위치에 소유자 없는 짐승 사망음을 재생해 비활성화 뒤에도 끝까지 유지한다.</summary>
    private void HandleDeath(EnemyHealth health)
    {
        Managers.Sound.StopFor(gameObject, _hitSound, forceStop: true);
        Managers.Sound.Play(_deathSound, health.transform.position, playDuringMovement: true);
    }

    /// <summary>pattern이 대기로 바뀌면 생존 짐승의 대기음을 한 번 재생하고 다른 패턴에서는 정지한다.</summary>
    private void HandlePattern(TwinBossPattern pattern)
    {
        Managers.Sound.StopFor(gameObject, _idleSound, forceStop: true);
        if (pattern == TwinBossPattern.Idle && _health.IsAlive)
            Managers.Sound.Play(_idleSound, transform.position, owner: gameObject, playDuringMovement: true);
    }

    void OnDisable()
    {
        _attack.AttackPreparing -= HandleAttack;
        _attack.PounceApproaching -= HandlePounceApproaching;
        _wolfVisual.PawPlanted -= HandlePawPlanted;
        _health.Damaged -= HandleDamage;
        _health.Died -= HandleDeath;
        _controller.PatternChanged -= HandlePattern;
    }
}
