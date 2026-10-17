using System;

using UnityEngine;

/// <summary>적 프리팹에 배치된 두 방향의 EnemySlash를 실제 베기 때 재생한다. 사용자 Transform을 유지한다.</summary>
[DisallowMultipleComponent]
public sealed class EnemyCombatEffects : MonoBehaviour
{
    [Header("근접 검기 연결")]
    [SerializeField] private ParticleSystem _upperLeftSlash;
    [SerializeField] private ParticleSystem _upperRightSlash;
    [SerializeField, Min(0f)] private float _trailFade = 0.15f;
    private ParticleSystem[] _leftSystems;
    private ParticleSystem[] _rightSystems;
    private float[] _leftSpeeds;
    private float[] _rightSpeeds;
    private ParticleSystem _currentSlash;
    private ParticleSystem[] _currentSystems;
    private float[] _currentSpeeds;
    private EnemyMeleeAttack _melee;
    private EnemyMeleeAttack _slashOwner;
    private float _releaseAt;

    [Header("기타 공격 표시 위치")]
    [SerializeField] private float _height = 1f;
    [SerializeField, Min(0f)] private float _forwardOffset = 0.35f;
    private EnemyAttackBehaviour[] _patterns;
    private EffectManager _effects;
    private bool _isPrimary;
    public bool HasSlashVisual => _currentSlash != null;
    public event Action SlashVisualFinished;

    void Awake()
    {
        _patterns = GetComponents<EnemyAttackBehaviour>();
        _isPrimary = GetComponent<EnemyCombatEffects>() == this;
        if (!_isPrimary) { enabled = false; return; }
        if (_upperLeftSlash == null) _upperLeftSlash = FindPrefabSlash("SlashEffect_UpperLeft");
        if (_upperRightSlash == null) _upperRightSlash = FindPrefabSlash("SlashEffect_UpperRight");
        CacheSlash(_upperLeftSlash, out _leftSystems, out _leftSpeeds);
        CacheSlash(_upperRightSlash, out _rightSystems, out _rightSpeeds);
    }

    void OnEnable()
    {
        if (!_isPrimary) return;
        foreach (EnemyAttackBehaviour pattern in _patterns)
        {
            pattern.AttackCommitted += HandleAttack;
            pattern.AttackEnded += HandleEnded;
            pattern.AttackCancelled += HandleCancelled;
        }
    }

    void Start()
    {
        _effects = Game.Instance.EffectManager;
    }

    void OnDisable()
    {
        if (!_isPrimary) return;
        foreach (EnemyAttackBehaviour pattern in _patterns)
        {
            pattern.AttackCommitted -= HandleAttack;
            pattern.AttackEnded -= HandleEnded;
            pattern.AttackCancelled -= HandleCancelled;
        }
        StopSlash();
    }

    void Update()
    {
        if (_currentSlash == null || _melee != null) return;
        if (_currentSlash.IsAlive(true) && Time.time < _releaseAt) return;

        StopSlash();
        SlashVisualFinished?.Invoke();
    }

    /// <summary>기존 씬 컴포넌트의 비어 있는 슬롯을 childName의 프리팹 자식 파티클로 연결한다. 자식이 없으면 null을 반환한다.</summary>
    private ParticleSystem FindPrefabSlash(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<ParticleSystem>() : null;
    }

    /// <summary>slash의 원래 파티클 속도를 systems·speeds에 보관하고 비활성화한다. 비근접 프리팹의 미연결 슬롯은 빈 배열로 처리한다.</summary>
    private void CacheSlash(ParticleSystem slash, out ParticleSystem[] systems, out float[] speeds)
    {
        systems = slash != null ? slash.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];
        speeds = new float[systems.Length];
        for (int i = 0; i < systems.Length; i++) speeds[i] = systems[i].main.simulationSpeed;
        if (slash == null) return;
        slash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        slash.gameObject.SetActive(false);
    }

    /// <summary>context의 근접 베기는 방향별 자식 검기를, 돌진은 공용 검기를 재생한다. 원거리 발사는 투사체 표현을 사용한다.</summary>
    private void HandleAttack(EnemyAttackContext context)
    {
        if (context.Pattern is EnemyRangedAttack) return;
        if (!(context.Pattern is EnemyMeleeAttack melee))
        {
            if (_effects == null || !_effects.IsReady) return;
            Vector3 direction = Vector3.ProjectOnPlane(context.Direction, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
            _effects.Play(EffectId.EnemySlash, transform.position + Vector3.up * _height + direction * _forwardOffset,
                Quaternion.LookRotation(direction, Vector3.up));
            return;
        }
        StopSlash();
        bool left = melee.IsLeftToRightSlash;
        _currentSlash = left ? _upperLeftSlash : _upperRightSlash;
        if (_currentSlash == null)
        {
            Debug.LogError("적 프리팹에 해당 방향 EnemySlash 연결이 없습니다.", this);
            return;
        }
        _slashOwner = melee;
        _melee = melee;        
        _currentSystems = left ? _leftSystems : _rightSystems;
        _currentSpeeds = left ? _leftSpeeds : _rightSpeeds;
        float visualDuration = GetVisualDuration(_currentSystems, _currentSpeeds);
        float multiplier = Mathf.Max(0.01f, visualDuration) / Mathf.Max(0.01f, context.ActiveDuration);
        for (int i = 0; i < _currentSystems.Length; i++)
        {
            ParticleSystem.MainModule main = _currentSystems[i].main;
            main.simulationSpeed = _currentSpeeds[i] * multiplier;
        }
        _currentSlash.gameObject.SetActive(true);
        _currentSlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _currentSlash.Play(true);
    }

    /// <summary>systems의 방출·Burst·입자 수명과 speeds로 원본 표시 시간의 최댓값을 반환한다.</summary>
    private float GetVisualDuration(ParticleSystem[] systems, float[] speeds)
    {
        float duration = 0f;
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (!system.emission.enabled) continue;
            ParticleSystem.MainModule main = system.main;
            ParticleSystem.EmissionModule emission = system.emission;
            float emissionEnd = emission.rateOverTime.constantMax > 0f || emission.rateOverDistance.constantMax > 0f
                ? main.duration : 0f;
            for (int j = 0; j < emission.burstCount; j++)
            {
                ParticleSystem.Burst burst = emission.GetBurst(j);
                emissionEnd = Mathf.Max(emissionEnd, burst.time + Mathf.Max(0, burst.cycleCount - 1) * burst.repeatInterval);
            }
            duration = Mathf.Max(duration,
                (main.startDelay.constantMax + emissionEnd + main.startLifetime.constantMax) / Mathf.Max(0.01f, speeds[i]));
        }
        return duration;
    }

    /// <summary>context의 베기 종료 시 자식 검기의 방출을 중단하고 최대 _trailFade 동안 남은 입자를 유지한다.</summary>
    private void HandleEnded(EnemyAttackContext context)
    {
        if (_melee != context.Pattern || _currentSlash == null) return;
        _currentSlash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        _melee = null;
        _releaseAt = Time.time + _trailFade;
    }

    /// <summary>context의 베기가 취소되면 진행 중인 검기를 즉시 비활성화한다.</summary>
    private void HandleCancelled(EnemyAttackContext context)
    {
        if (_slashOwner == context.Pattern)
            StopSlash();
    }

    /// <summary>현재 검기 입자를 정리하고 원래 재생 속도를 복원한다. 자식 GameObject를 비활성화하며 Transform은 유지한다.</summary>
    private void StopSlash()
    {
        if (_currentSlash != null)
        {
            _currentSlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            for (int i = 0; i < _currentSystems.Length; i++)
            {
                ParticleSystem.MainModule main = _currentSystems[i].main;
                main.simulationSpeed = _currentSpeeds[i];
            }
            _currentSlash.gameObject.SetActive(false);
        }
        _currentSlash = null;
        _currentSystems = null;
        _currentSpeeds = null;
        _melee = null;
        _slashOwner = null;
    }
}
