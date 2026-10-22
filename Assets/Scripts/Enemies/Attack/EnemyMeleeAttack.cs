using System;
using System.Collections.Generic;

using UnityEngine;

/// <summary>EnemySlash 베기 구간에 플레이어와 같은 칼날 접촉 조회로 근접 피해를 한 번 적용한다.</summary>
public class EnemyMeleeAttack : EnemyAttackBehaviour
{
    private const float MAX_SWEEP_STEP_ANGLE = 15f;

    [Header("근접 공격 설정")]
    [SerializeField, Min(0.1f)] private float _attackRange = 2f;
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField] private bool _upperLeftOnly;

    [Header("근접 타격 설정")]
    [SerializeField] private bool _useBuiltInHitDetection = true;
    [SerializeField] private BoxCollider _bladeHitbox;
    [SerializeField, Min(0.01f)] private float _hitRadius = 0.12f;
    [SerializeField] private float _hitHeight = 1.6f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;

    [Header("베기 궤적")]
    [SerializeField, Min(0.05f)] private float _swingDuration = 0.24f;
    [SerializeField] private float _startAngle = -70f;
    [SerializeField] private float _endAngle = 70f;
    [SerializeField, Tooltip("베기 시간(0~1)에 따른 칼날 진행도(0~1)입니다. 검 애니메이션의 빠르기에 판정을 맞출 때 사용합니다.")]
    private AnimationCurve _swingProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [SerializeField, Min(0f)] private float _bladeBaseOffset = 0.25f;
    [SerializeField, Min(0f)] private float _blockRecovery = 0.55f;
    [SerializeField, Min(0f)] private float _parryRecovery = 0.9f;
    private Vector3 _swingOrigin;
    private Vector3 _swingForward;
    private Vector3 _previousBase;
    private Vector3 _previousTip;
    private float _startedAt;
    private bool _contactConsumed;
    private int _diagonalSign = 1;
    private float _sweptProgress;

    [Header("내딛기")]
    [SerializeField, Min(0f), Tooltip("베기 시작과 함께 앞으로 미끄러져 들어가는 거리(m)입니다. 0이면 내딛지 않습니다.")]
    private float _stepInDistance;
    [SerializeField, Min(0.01f), Tooltip("내딛기에 걸리는 시간(초)입니다.")]
    private float _stepInDuration = 0.1f;
    private EnemyMotor _motor;

    [Header("공격 판정 기즈모")]
    [SerializeField] private bool _showAttackGizmos = true;
    [SerializeField] private bool _showBladeTrajectoryGizmos;
    [SerializeField, Min(0.1f)] private float _gizmoDuration = 2f;

    [Header("근접 공격 전조음")]
    [SerializeField] private SoundId _warningSound = SoundId.None;
#if UNITY_EDITOR
    private readonly List<Vector3> _debugBlades = new List<Vector3>();
    private float _debugUntil;
    private Vector3 _debugContactPoint;
    private bool _debugHadContact;
#endif

    public override float Range => _attackRange;
    public float BladeRadius => _hitRadius;
    public bool IsLeftToRightSlash => _upperLeftOnly || _diagonalSign > 0;
    public Vector3 BladeBase { get; private set; }
    public Vector3 BladeTip { get; private set; }

    /// <summary>진행 중인 실제 칼날과 cylinder가 겹치면 현재 공격 ID를 attackId에 저장하고 참을 반환한다.</summary>
    public bool TryGetDodgeThreat(DodgeThreatCylinder cylinder, out int attackId)
    {
        attackId = 0;
        if (!isActiveAndEnabled || Phase != EnemyAttackPhase.Active || _contactConsumed
            || !_useBuiltInHitDetection || _damage <= 0f
            || !cylinder.OverlapsCapsule(BladeBase, BladeTip, _hitRadius)) return false;
        attackId = Context.AttackId;
        return true;
    }
    public event Action<EnemyAttackContext> MeleeAttack;
    public event Action<bool> Deflected;
    protected override float ActiveDuration => _swingDuration;
    protected override EnemySoundAction SoundAction => EnemySoundAction.Melee;

    /// <summary>context의 전조 시작에 _upperLeftOnly 설정 또는 무작위 선택으로 베기 방향을 정하고 공격 종료까지 유지한다.</summary>
    protected override void PrepareTelegraph(EnemyAttackContext context)
    {
        _diagonalSign = _upperLeftOnly ? 1 : UnityEngine.Random.value < 0.5f ? 1 : -1;
    }

    /// <summary>
    /// context의 수평 방향과 현재 몸 위치로 칼날을 준비하고 베기 시간·진행도 및 접촉 기록을 초기화한다.
    /// _stepInDistance가 있으면 같은 방향으로 내딛기 이동을 시작한다.
    /// </summary>
    protected override void PerformAttack(EnemyAttackContext context)
    {
        _swingOrigin = transform.position + Vector3.up * _hitHeight;
        _swingForward = Vector3.ProjectOnPlane(context.Direction, Vector3.up).normalized;
        if (_swingForward.sqrMagnitude < 0.001f) _swingForward = transform.forward;
        _startedAt = Time.time;
        _sweptProgress = 0f;
        _contactConsumed = false;
        if (_stepInDistance > 0f)
            _motor.ApplyHitPush(_swingForward, _stepInDistance, _stepInDuration);
#if UNITY_EDITOR
        _debugBlades.Clear();
        _debugHadContact = false;
#endif
        UpdateHitboxPose(0f);
        _previousBase = BladeBase;
        _previousTip = BladeTip;
        _bladeHitbox.enabled = false;
        MeleeAttack?.Invoke(context);
    }

    protected override void Awake()
    {
        base.Awake();
        _motor = GetComponent<EnemyMotor>();
        _bladeHitbox.enabled = false;
    }

    void FixedUpdate()
    {
        if (Phase != EnemyAttackPhase.Active || _contactConsumed) return;
        if (Time.time >= _startedAt + _swingDuration)
        {
            BeginRecovery();
            return;
        }
        // 내딛는 동안에는 칼날 원점이 몸을 따라 앞으로 나간다.
        if (_stepInDistance > 0f)
            _swingOrigin = transform.position + Vector3.up * _hitHeight;
        float progress = _swingProgress.Evaluate(Mathf.Clamp01((Time.time - _startedAt) / _swingDuration));
        // 한 물리 프레임에 크게 도는 빠른 베기는 호를 작은 구간으로 나눠 칼끝 쪽 접촉을 놓치지 않는다.
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(progress - _sweptProgress)
            * Mathf.Abs(_endAngle - _startAngle) / MAX_SWEEP_STEP_ANGLE));
        float sweptFrom = _sweptProgress;
        _sweptProgress = progress;
        for (int step = 1; step <= steps; step++)
        {
            UpdateHitboxPose(Mathf.Lerp(sweptFrom, progress, (float)step / steps));
            if (!_useBuiltInHitDetection) continue;
            SweepBladeContact();
            if (Phase != EnemyAttackPhase.Active || _contactConsumed) return;
        }
    }

    /// <summary>
    /// 직전·현재 칼날 좌표로 회피 잔상 보고와 대상 접촉 조회를 한 번 수행한다.
    /// 접촉한 Collider가 있으면 TryHit으로 피해 또는 회복을 확정한다.
    /// </summary>
    private void SweepBladeContact()
    {
        Vector3 ghostPoint = DodgeAfterimage.ActiveOrigin;
        if (DodgeAfterimage.IsActive
            && !IsBlocked(Context, ghostPoint + Vector3.up * _hitHeight,
                Context.Target != null ? Context.Target.GetComponent<IDamageable>() : null))
        {
            Vector3 sweepDirection = (BladeTip - _previousTip).normalized;
            if (sweepDirection.sqrMagnitude < 0.001f) sweepDirection = _swingForward;
            DodgeAfterimage.ReportBladeSweep(_previousBase, _previousTip, BladeBase, BladeTip, _hitRadius,
                new DamageContext(_damage, gameObject, ghostPoint, sweepDirection,
                    Context.PatternId, Context.AttackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Melee));
        }
        Collider contact = EnemyCombatService.FindBladeContact(transform, Context.Target,
            _previousBase, _previousTip, BladeBase, BladeTip, _hitRadius,
            _hitMask.value | _obstacleMask.value, out Vector3 point);
        if (contact != null) TryHit(contact);
    }

    /// <summary>progress로 칼날의 이전·현재 좌표와 표시용 BoxCollider의 위치·회전·크기를 갱신한다.</summary>
    private void UpdateHitboxPose(float progress)
    {
        _previousBase = BladeBase;
        _previousTip = BladeTip;
        GetBladePose(progress, out Vector3 bladeBase, out Vector3 bladeTip);
        BladeBase = bladeBase;
        BladeTip = bladeTip;
        Vector3 blade = bladeTip - bladeBase;
        _bladeHitbox.transform.SetPositionAndRotation((bladeBase + bladeTip) * 0.5f,
            Quaternion.LookRotation(blade, Vector3.up));
        Vector3 scale = _bladeHitbox.transform.lossyScale;
        _bladeHitbox.center = Vector3.zero;
        _bladeHitbox.size = new Vector3(_hitRadius * 2f / Mathf.Abs(scale.x),
            _hitRadius * 2f / Mathf.Abs(scale.y), blade.magnitude / Mathf.Abs(scale.z));
#if UNITY_EDITOR
        if (_showBladeTrajectoryGizmos)
        {
            _debugBlades.Add(bladeBase);
            _debugBlades.Add(bladeTip);
        }
        _debugUntil = Time.time + _gizmoDuration;
#endif
    }

    /// <summary>
    /// progress와 사거리·각도·몸 높이로 애니메이션과 독립된 월드 칼날의 밑점과 끝점을 반환한다.
    /// 계산한 판정 좌표를 bladeBase와 bladeTip에 저장한다.
    /// </summary>
    public void GetBladePose(float progress, out Vector3 bladeBase, out Vector3 bladeTip)
    {
        GetDiagonalBladePose(progress, _upperLeftOnly ? 1 : _diagonalSign, out bladeBase, out bladeTip);
    }

    /// <summary>progress와 diagonalSign으로 대상이 보는 좌상단→우하단 또는 우상단→좌하단 칼날 좌표를 반환한다.</summary>
    private void GetDiagonalBladePose(float progress, int diagonalSign, out Vector3 bladeBase, out Vector3 bladeTip)
    {
        Vector3 origin = Phase == EnemyAttackPhase.Active ? _swingOrigin : transform.position + Vector3.up * _hitHeight;
        Vector3 forward = Phase == EnemyAttackPhase.Active ? _swingForward
            : Vector3.ProjectOnPlane(Context.Direction, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        if (Phase == EnemyAttackPhase.Ready)
        {
            EnemySensor sensor = GetComponent<EnemySensor>();
            if (sensor.Target != null) forward = Vector3.ProjectOnPlane(sensor.Target.position - transform.position, Vector3.up).normalized;
        }
        float offset = Mathf.Min(_bladeBaseOffset, _attackRange * 0.5f);
        bladeBase = origin + forward * offset;
        float angle = Mathf.Lerp(_startAngle, _endAngle, Mathf.Clamp01(progress));
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 swingAxis = (right - Vector3.up * diagonalSign).normalized;
        bladeTip = bladeBase + Quaternion.AngleAxis(angle, swingAxis) * forward * (_attackRange - offset);
    }

    /// <summary>other가 베기 중 대상 또는 대상을 차폐한 장애물인지 검사한다. 유효한 최초 접촉만 피해 또는 회복으로 확정한다.</summary>
    private void TryHit(Collider other)
    {
        if (Phase != EnemyAttackPhase.Active || !_useBuiltInHitDetection || _contactConsumed
            || Time.time < _startedAt || Time.time >= _startedAt + _swingDuration
            || other.isTrigger || other.transform.IsChildOf(transform)) return;
        EnemyAttackContext context = Context;
        bool isTarget = context.Target != null && other.transform.IsChildOf(context.Target);
        int layer = 1 << other.gameObject.layer;
        if (isTarget ? (_hitMask.value & layer) == 0 : (_obstacleMask.value & layer) == 0) return;
        if (!isTarget && other.GetComponentInParent<EnemyHealth>() != null) return;
        if (!isTarget && context.Target != null
            && !IsBlocked(context, context.Target.position + Vector3.up * _hitHeight,
                context.Target.GetComponent<IDamageable>())) return;
        Vector3 point = other.ClosestPoint(_bladeHitbox.transform.position);
#if UNITY_EDITOR
        _debugContactPoint = point;
        _debugHadContact = true;
#endif
        _contactConsumed = true;
        if (!isTarget)
        {
            BeginRecovery();
            return;
        }
        IDamageable receiver = other.GetComponentInParent<IDamageable>();
        if (receiver == null || IsBlocked(context, point, receiver)) { BeginRecovery(); return; }
        Vector3 direction = (BladeTip - _previousTip).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = _swingForward;
        receiver.TakeDamage(new DamageContext(_damage, gameObject, point, direction, context.PatternId, context.AttackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Melee));
        if (Phase == EnemyAttackPhase.Active) BeginRecovery();
    }

    /// <summary>context의 공격 종료 또는 cancelled 취소 시 칼날 Trigger를 비활성화하여 추가 접촉을 막는다.</summary>
    protected override void OnAttackEnded(EnemyAttackContext context, bool cancelled)
    {
        _bladeHitbox.enabled = false;
    }

    /// <summary>
    /// perfect에 따라 방어당한 공격을 중단하고 반격 가능한 최소 회복 시간을 적용한다. point는 방어 접촉 위치다.
    /// 회복 시작 전에 Deflected로 막기(false)·쳐내기(true)를 알려 연출이 공격 종료보다 먼저 반응하게 한다.
    /// </summary>
    public void Deflect(bool perfect, Vector3 point)
    {
        if (Phase != EnemyAttackPhase.Active) return;
        _contactConsumed = true;
        BladeTip = point;
        Deflected?.Invoke(perfect);
        BeginRecovery(perfect ? _parryRecovery : _blockRecovery);
#if UNITY_EDITOR
        _debugContactPoint = point;
        _debugHadContact = true;
#endif
    }

    /// <summary>현재 칼날 원점에서 point까지 receiver와 자신을 제외한 차폐물이 있으면 true를 반환한다.</summary>
    protected virtual bool IsBlocked(EnemyAttackContext context, Vector3 point, IDamageable receiver)
    {
        Vector3 delta = point - _swingOrigin;
        foreach (RaycastHit hit in Physics.RaycastAll(_swingOrigin, delta.normalized, delta.magnitude, _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<IDamageable>() == receiver
                || hit.collider.GetComponentInParent<EnemyHealth>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// stage와 이 공격의 전조음 설정으로 재생할 소리 ID를 반환한다.
    /// 전조음이 지정되면 첫 박동을 그 소리로 바꾸고 검격 직전 기합음을 제외한다.
    /// </summary>
    protected override SoundId ResolveActionSound(EnemySoundStage stage)
    {
        if (_warningSound != SoundId.None)
        {
            if (stage == EnemySoundStage.Heartbeat)
                return _warningSound;
            if (stage == EnemySoundStage.AttackVoice)
                return SoundId.None;
        }

        return base.ResolveActionSound(stage);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_showAttackGizmos || !isActiveAndEnabled || !Application.isPlaying || Time.time > _debugUntil) return;
        DrawAttackRangeGizmo(Color.red);
        if (!_showBladeTrajectoryGizmos) return;
        for (int i = 0; i + 1 < _debugBlades.Count; i += 2)
            DrawHitboxGizmo(_debugBlades[i], _debugBlades[i + 1], Color.red);
        if (_debugHadContact)
        {
            Color previous = Gizmos.color;
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(_debugContactPoint, 0.05f);
            Gizmos.color = previous;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!_showAttackGizmos || Application.isPlaying) return;
        DrawAttackRangeGizmo(Color.yellow);
        if (!_showBladeTrajectoryGizmos) return;
        for (int i = 0; i <= 12; i++)
        {
            GetDiagonalBladePose(i / 12f, 1, out Vector3 bladeBase, out Vector3 bladeTip);
            DrawHitboxGizmo(bladeBase, bladeTip, Color.yellow);
            if (_upperLeftOnly) continue;
            GetDiagonalBladePose(i / 12f, -1, out bladeBase, out bladeTip);
            DrawHitboxGizmo(bladeBase, bladeTip, Color.cyan);
        }
    }
    /// <summary>현재 사거리와 대각선 베기의 수평 투영 각도로 color의 부채꼴을 그린다. 실제 Trigger 부피를 대신하는 판정은 아니다.</summary>
    private void DrawAttackRangeGizmo(Color color)
    {
        GetBladePose(0f, out Vector3 startBase, out Vector3 startTip);
        GetBladePose(0.5f, out Vector3 middleBase, out Vector3 middleTip);
        GetBladePose(1f, out Vector3 endBase, out Vector3 endTip);
        Vector3 origin = Phase == EnemyAttackPhase.Active ? _swingOrigin : transform.position + Vector3.up * _hitHeight;
        Vector3 forward = Vector3.ProjectOnPlane(middleTip - origin, Vector3.up).normalized;
        float halfAngle = Mathf.Max(Vector3.Angle(forward, Vector3.ProjectOnPlane(startTip - origin, Vector3.up)),
            Vector3.Angle(forward, Vector3.ProjectOnPlane(endTip - origin, Vector3.up)));
        float thicknessAngle = Mathf.Atan2(_hitRadius, _attackRange) * Mathf.Rad2Deg;
        Vector3 fanOrigin = new Vector3(origin.x, transform.position.y + 0.02f, origin.z);
        EnemyCombatService.DrawAttackFanGizmo(fanOrigin, forward, _attackRange, (halfAngle + thicknessAngle) * 2f, color);
    }

    /// <summary>bladeBase·bladeTip과 판정 두께로 color의 박스 기즈모를 그린 뒤 Gizmos 상태를 복원한다.</summary>
    private void DrawHitboxGizmo(Vector3 bladeBase, Vector3 bladeTip, Color color)
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Vector3 blade = bladeTip - bladeBase;
        Gizmos.matrix = Matrix4x4.TRS((bladeBase + bladeTip) * 0.5f,
            Quaternion.LookRotation(blade, Vector3.up), Vector3.one);
        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(_hitRadius * 2f, _hitRadius * 2f, blade.magnitude));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
#endif
}
