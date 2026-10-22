using UnityEngine;

/// <summary>DefenseMelee의 전조·박동·공격 사건을 적 전용 검 애니메이션 상태와 섬광에 연결한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttack), typeof(EnemyCombatEffects))]
public sealed class DefenseMeleeSwordAnimator : MonoBehaviour
{
    private static readonly int _idleState = Animator.StringToHash("Idle");
    private static readonly int _prepareLeftState = Animator.StringToHash("PrepareUpperLeft");
    private static readonly int _swingLeftState = Animator.StringToHash("SwingUpperLeft");
    private static readonly int _returnLeftState = Animator.StringToHash("ReturnUpperLeft");
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static Texture2D _sharedGlintShape;

    [Header("검 애니메이션")]
    [SerializeField] private Animator _swordAnimator;
    [SerializeField] private AnimationClip _prepareUpperLeft;
    [SerializeField] private AnimationClip _swingUpperLeft;
    [SerializeField] private AnimationClip _returnUpperLeft;

    [Header("검 끝 섬광")]
    [SerializeField] private Transform _swordTip;
    [SerializeField] private Material _glintMaterial;
    [SerializeField, Min(0.01f)] private float _glintSize = 0.28f;
    [SerializeField, Min(0.01f)] private float _glintDuration = 0.4f;

    private EnemyMeleeAttack _attack;
    private EnemyCombatEffects _effects;
    private ParticleSystem _glint;
    private bool _attackEnded;
    private bool _returning;
    private float _returnEndAt;

    void Awake()
    {
        _attack = GetComponent<EnemyMeleeAttack>();
        _effects = GetComponent<EnemyCombatEffects>();
        CreateGlint();
    }

    void OnEnable()
    {
        _attack.TelegraphStarted += HandleTelegraphStarted;
        _attack.FirstHeartbeatPlayed += HandleFirstHeartbeatPlayed;
        _attack.AttackCommitted += HandleAttackCommitted;
        _attack.AttackEnded += HandleAttackEnded;
        _attack.AttackCancelled += HandleAttackCancelled;
        _effects.SlashVisualFinished += HandleSlashVisualFinished;
        ResetToIdle();
    }

    void OnDisable()
    {
        _attack.TelegraphStarted -= HandleTelegraphStarted;
        _attack.FirstHeartbeatPlayed -= HandleFirstHeartbeatPlayed;
        _attack.AttackCommitted -= HandleAttackCommitted;
        _attack.AttackEnded -= HandleAttackEnded;
        _attack.AttackCancelled -= HandleAttackCancelled;
        _effects.SlashVisualFinished -= HandleSlashVisualFinished;
        _attackEnded = false;
        _returning = false;
        if (_glint != null)
            _glint.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void Update()
    {
        if (_attackEnded && !_returning && !_effects.HasSlashVisual)
            BeginReturn();
        if (_returning && Time.time >= _returnEndAt)
            ResetToIdle();
    }

    /// <summary>전조 시작 시 UpperLeft 준비 클립을 첫 박동까지 재생하고 복귀 상태를 초기화한다.</summary>
    private void HandleTelegraphStarted(EnemyAttackContext context)
    {
        _attackEnded = false;
        _returning = false;
        _glint.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        float duration = Mathf.Max(0.01f, _attack.FirstHeartbeatAt - Time.time);
        Play(_prepareLeftState, _prepareUpperLeft.length / duration, 0f);
    }

    /// <summary>첫 박동에서 준비 클립의 마지막 검 자세를 고정하고 swordTip 위치의 십자 섬광을 한 번 재생한다.</summary>
    private void HandleFirstHeartbeatPlayed(EnemyAttackContext context)
    {
        Play(_prepareLeftState, 0f, 1f);
        _glint.Clear(false);
        _glint.Play(false);
    }

    /// <summary>공격 확정 시 UpperLeft 휘두르기 클립을 context의 ActiveDuration에 맞춰 재생한다.</summary>
    private void HandleAttackCommitted(EnemyAttackContext context)
    {
        Play(_swingLeftState,
            _swingUpperLeft.length / Mathf.Max(0.01f, context.ActiveDuration), 0f);
    }

    /// <summary>공격 종료 시 휘두르기 마지막 자세를 유지하고 검격 이펙트 종료를 기다린다.</summary>
    private void HandleAttackEnded(EnemyAttackContext context)
    {
        _attackEnded = true;
        Play(_swingLeftState, 0f, 1f);
        if (!_effects.HasSlashVisual)
            BeginReturn();
    }

    /// <summary>검격 이펙트가 끝나고 공격 판정도 끝났다면 복귀 클립을 시작한다.</summary>
    private void HandleSlashVisualFinished()
    {
        if (_attackEnded)
            BeginReturn();
    }

    /// <summary>취소된 공격의 애니메이션과 섬광을 초기 대기 자세로 돌린다.</summary>
    private void HandleAttackCancelled(EnemyAttackContext context)
    {
        ResetToIdle();
    }

    /// <summary>UpperLeft 복귀 클립을 시작하고 종료 시각을 _returnEndAt에 기록한다.</summary>
    private void BeginReturn()
    {
        if (_returning) return;
        _returning = true;
        _returnEndAt = Time.time + _returnUpperLeft.length;
        Play(_returnLeftState, 1f, 0f);
    }

    /// <summary>stateHash·speed·normalizedTime으로 검 Animator 상태를 재생하고 즉시 첫 자세를 샘플링한다.</summary>
    private void Play(int stateHash, float speed, float normalizedTime)
    {
        _swordAnimator.speed = speed;
        _swordAnimator.Play(stateHash, 0, normalizedTime);
        _swordAnimator.Update(0f);
    }

    /// <summary>현재 검을 대기 클립으로 되돌리고 복귀·섬광 상태를 지운다.</summary>
    private void ResetToIdle()
    {
        _attackEnded = false;
        _returning = false;
        if (_glint != null)
            _glint.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        Play(_idleState, 1f, 0f);
    }

    /// <summary>swordTip과 glintMaterial로 늑대와 같은 형태·색의 일회성 섬광을 생성해 _glint에 저장한다.</summary>
    private void CreateGlint()
    {
        if (_sharedGlintShape == null)
            _sharedGlintShape = TwinBeastEyes.CreateGlintShape();
        GameObject glintObject = new GameObject("SwordTipGlint");
        glintObject.transform.SetParent(_swordTip, false);
        glintObject.transform.localScale = Vector3.one / Mathf.Max(0.001f, _swordTip.lossyScale.x);
        _glint = glintObject.AddComponent<ParticleSystem>();
        _glint.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = _glint.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = _glintDuration;
        main.startLifetime = _glintDuration;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = _glintSize;
        main.startSizeY = _glintSize * 1.5f;
        main.startSizeZ = _glintSize;
        main.startColor = new Color(0.5691823f, 0.8328799f, 1f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 1;
        ParticleSystem.EmissionModule emission = _glint.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule shape = _glint.shape;
        shape.enabled = false;
        ParticleSystem.SizeOverLifetimeModule size = _glint.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));
        ParticleSystemRenderer renderer = glintObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = -100f;
        renderer.maxParticleSize = 4f;
        renderer.sharedMaterial = _glintMaterial;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetTexture(_baseMapId, _sharedGlintShape);
        properties.SetColor(_baseColorId, Color.white);
        renderer.SetPropertyBlock(properties);
    }
}
