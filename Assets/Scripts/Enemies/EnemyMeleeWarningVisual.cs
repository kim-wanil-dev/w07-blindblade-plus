using System.Collections.Generic;

using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyMeleeAttack), typeof(EnemyCombatEffects), typeof(EnemyMotor))]
public sealed class EnemyMeleeWarningVisual : MonoBehaviour
{
    private static readonly HashSet<EnemyMeleeWarningVisual> _active =
        new HashSet<EnemyMeleeWarningVisual>();
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");

    private enum VisualPhase
    {
        Idle,
        TurningRed,
        HoldingRed,
        ReturningWhite
    }

    [Header("실루엣 대상")]
    [SerializeField] private Renderer[] _bodyRenderers;

    [Header("흰색 복귀")]
    [SerializeField, Min(0.01f)]
    private float _whiteReturnDuration = 0.12f;

    private EnemyMeleeAttack _attack;
    private EnemyCombatEffects _effects;
    private MaterialPropertyBlock _maskProperties;
    private EnemyMotor _motor;
    private VisualPhase _phase;
    private float _redStartAt;
    private float _redEndAt;
    private float _whiteStartAt;
    private bool _attackEnded;
    private bool _slashFinished;

    internal static IReadOnlyCollection<EnemyMeleeWarningVisual> ActiveInstances => _active;
    public IReadOnlyList<Renderer> BodyRenderers => _bodyRenderers;
    public float RedIntensity { get; private set; }
    public bool HoldPosition =>
        _phase == VisualPhase.HoldingRed ||
        _phase == VisualPhase.ReturningWhite;

    void Awake()
    {
        _attack = GetComponent<EnemyMeleeAttack>();
        _effects = GetComponent<EnemyCombatEffects>();
        _motor = GetComponent<EnemyMotor>();
        _maskProperties = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        ResetVisual();

        _attack.TelegraphStarted += HandleTelegraphStarted;
        _attack.FirstHeartbeatPlayed += HandleFirstHeartbeatPlayed;
        _attack.AttackEnded += HandleAttackEnded;
        _attack.AttackCancelled += HandleAttackCancelled;
        _effects.SlashVisualFinished += HandleSlashVisualFinished;

        _active.Add(this);
    }

    void OnDisable()
    {
        _active.Remove(this);

        _attack.TelegraphStarted -= HandleTelegraphStarted;
        _attack.FirstHeartbeatPlayed -= HandleFirstHeartbeatPlayed;
        _attack.AttackEnded -= HandleAttackEnded;
        _attack.AttackCancelled -= HandleAttackCancelled;
        _effects.SlashVisualFinished -= HandleSlashVisualFinished;

        ResetVisual();
        PublishWarningMaskInputs();
    }

    void Update()
    {
        if (_phase == VisualPhase.TurningRed)
        {
            float duration = Mathf.Max(0.001f, _redEndAt - _redStartAt);
            RedIntensity = Mathf.Clamp01((Time.time - _redStartAt) / duration);
            return;
        }

        if (_phase == VisualPhase.HoldingRed)
        {
            // 효과가 비활성화되는 등 종료 사건을 받지 못한 경우에도 잠금을 해제한다.
            if (_attackEnded && !_effects.HasSlashVisual)
            {
                _slashFinished = true;
                TryBeginWhiteReturn();
            }
            return;
        }

        if (_phase == VisualPhase.ReturningWhite)
        {
            float progress = Mathf.Clamp01(
                (Time.time - _whiteStartAt) / _whiteReturnDuration);

            RedIntensity = 1f - progress;

            if (progress >= 1f)
                ResetVisual();
        }
    }

    void LateUpdate()
    {
        PublishWarningMaskInputs();
    }

    /// <summary>
    /// 전조 사건을 받아 현재 시각부터 첫 박동 예정 시각까지 붉은색 변화를 예약한다.
    /// RedIntensity를 0으로 초기화하며 이동 잠금은 아직 시작하지 않는다.
    /// </summary>
    private void HandleTelegraphStarted(EnemyAttackContext context)
    {
        ResetVisual();

        _redStartAt = Time.time;
        _redEndAt = Mathf.Max(_redStartAt, _attack.FirstHeartbeatAt);
        _phase = VisualPhase.TurningRed;
    }

    /// <summary>
    /// 첫 박동 사건을 시전음 신호로 받아 실루엣을 빨갛게 확정한다.
    /// RedIntensity를 1로 만들고 추적 이동을 즉시 중지한다.
    /// </summary>
    private void HandleFirstHeartbeatPlayed(EnemyAttackContext context)
    {
        if (_phase != VisualPhase.TurningRed)
            return;

        RedIntensity = 1f;
        _phase = VisualPhase.HoldingRed;
        _motor.Stop();
    }

    /// <summary>
    /// 공격 판정 종료 사건을 받아 검격 입자가 남았는지 확인한다.
    /// 입자까지 끝난 경우에만 흰색 복귀를 시작한다.
    /// </summary>
    private void HandleAttackEnded(EnemyAttackContext context)
    {
        if (_phase != VisualPhase.HoldingRed)
            return;

        _attackEnded = true;
        _slashFinished = !_effects.HasSlashVisual;
        TryBeginWhiteReturn();
    }

    /// <summary>
    /// 검격 입자의 정상 종료 사건을 받아 시각 효과 종료를 기록한다.
    /// 공격 판정도 끝났다면 흰색 복귀를 시작한다.
    /// </summary>
    private void HandleSlashVisualFinished()
    {
        if (_phase != VisualPhase.HoldingRed)
            return;

        _slashFinished = true;
        TryBeginWhiteReturn();
    }

    /// <summary>
    /// 취소된 공격의 사건을 받아 색상과 추적 잠금을 즉시 초기화한다.
    /// 검격 입자의 정리는 EnemyCombatEffects가 담당한다.
    /// </summary>
    private void HandleAttackCancelled(EnemyAttackContext context)
    {
        ResetVisual();
    }

    /// <summary>
    /// 공격 판정과 검격 입자가 모두 종료됐는지 검사한다.
    /// 두 조건이 충족되면 현재 시각부터 흰색 복귀를 시작한다.
    /// </summary>
    private void TryBeginWhiteReturn()
    {
        if (_phase != VisualPhase.HoldingRed ||
            !_attackEnded || !_slashFinished)
            return;

        _whiteStartAt = Time.time;
        _phase = VisualPhase.ReturningWhite;
    }

    /// <summary>
    /// 새 공격 또는 비활성화를 위해 색상 진행도와 종료 기록을 지운다.
    /// RedIntensity를 0으로, HoldPosition을 false로 되돌린다.
    /// </summary>
    private void ResetVisual()
    {
        _phase = VisualPhase.Idle;
        RedIntensity = 0f;
        _attackEnded = false;
        _slashFinished = false;
    }

    /// <summary>
    /// 연결된 몸체 렌더러의 파티클 텍스처·기본색과 현재 RedIntensity를 전달한다.
    /// 각 렌더러의 기존 속성 블록을 보존하면서 경고 마스크 입력을 갱신한다.
    /// </summary>
    private void PublishWarningMaskInputs()
    {
        foreach (Renderer renderer in _bodyRenderers)
        {
            if (renderer == null)
                continue;

            Material source = renderer.sharedMaterial;
            if (source == null || !source.HasProperty(_baseMapId) || !source.HasProperty(_baseColorId))
                continue;

            renderer.GetPropertyBlock(_maskProperties);
            _maskProperties.SetTexture(_baseMapId, source.GetTexture(_baseMapId));
            _maskProperties.SetColor(_baseColorId, source.GetColor(_baseColorId));
            _maskProperties.SetFloat(_warningIntensityId, RedIntensity);
            renderer.SetPropertyBlock(_maskProperties);
        }
    }
}