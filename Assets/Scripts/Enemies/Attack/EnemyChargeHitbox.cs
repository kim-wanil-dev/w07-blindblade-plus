using System.Collections.Generic;

using UnityEngine;


[DisallowMultipleComponent]
public sealed class EnemyChargeHitbox : MonoBehaviour
{
    [Header("돌진 타격")]
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField, Min(0.01f)] private float _radius = 0.5f;
    [SerializeField] private float _height = 1f;
    [SerializeField] private LayerMask _hitMask = ~0;
    [SerializeField] private LayerMask _obstacleMask = Physics.DefaultRaycastLayers;
    private readonly HashSet<IDamageable> _damaged = new HashSet<IDamageable>();
    private EnemyAttackBehaviour[] _attacks;
    private EnemyAttackBehaviour _activeAttack;
    private EnemyAttackContext _context;
    private Vector3 _previousCenter;

    [Header("공격 판정 기즈모")]
    [SerializeField] private bool _showAttackGizmos = true;
    [SerializeField, Min(0.1f)] private float _gizmoDuration = 2f;
#if UNITY_EDITOR
    private readonly List<Vector3> _debugCenters = new List<Vector3>();
    private float _debugUntil;
#endif

    void Awake()
    {
        _attacks = GetComponents<EnemyAttackBehaviour>();
    }

    void OnEnable()
    {
        foreach (EnemyAttackBehaviour attack in _attacks)
        {
            if (!(attack is EnemyChargeAttack) && !(attack is EnemyRhythmChargeAttack))
                continue;
            attack.TelegraphStarted += HandleTelegraph;
            attack.AttackEnded += HandleEnded;
            attack.AttackCancelled += HandleCancelled;
        }
    }

    void OnDisable()
    {
        foreach (EnemyAttackBehaviour attack in _attacks)
        {
            attack.TelegraphStarted -= HandleTelegraph;
            attack.AttackEnded -= HandleEnded;
            attack.AttackCancelled -= HandleCancelled;
        }
        _activeAttack = null;
        _damaged.Clear();
    }

    void LateUpdate()
    {
        Vector3 center = transform.position + Vector3.up * _height;
        if (_activeAttack != null && _activeAttack.Phase == EnemyAttackPhase.Active)
        {
            bool moving = _activeAttack is EnemyRhythmChargeAttack rhythm ? rhythm.IsMovingPulse :
                (_activeAttack is EnemyChargeAttack charge && charge.IsMoving);
            if (moving || (center - _previousCenter).sqrMagnitude > 0.000001f)
                CheckSegment(center);
        }
        _previousCenter = center;
    }

    /// <summary>
    /// context의 돌진 패턴과 시작 위치를 저장하고 이번 공격의 피해 수신 목록을 초기화한다.
    /// 전조에서는 피해를 적용하지 않으며 이후 Active 구간만 검사한다.
    /// </summary>
    private void HandleTelegraph(EnemyAttackContext context)
    {
        _context = context;
        _activeAttack = context.Pattern;
        _previousCenter = transform.position + Vector3.up * _height;
        _damaged.Clear();
#if UNITY_EDITOR
        _debugCenters.Clear();
#endif
    }

    /// <summary>
    /// context와 현재 돌진이 같으면 마지막 프레임의 이동 구간을 검사하고 타격 상태를 종료한다.
    /// 후딜 진입 프레임의 빠른 이동도 누락하지 않으며 정지한 쉼 구간에는 피해를 추가하지 않는다.
    /// </summary>
    private void HandleEnded(EnemyAttackContext context)
    {
        if (_activeAttack != context.Pattern)
            return;
        Vector3 center = transform.position + Vector3.up * _height;
        if ((center - _previousCenter).sqrMagnitude > 0.000001f)
            CheckSegment(center);
        _activeAttack = null;
    }

    /// <summary>
    /// context가 현재 돌진이면 타격 연결과 중복 피해 목록을 초기화한다.
    /// 취소 시 새로운 피해를 적용하지 않는다.
    /// </summary>
    private void HandleCancelled(EnemyAttackContext context)
    {
        if (_activeAttack != context.Pattern)
            return;
        _activeAttack = null;
        _damaged.Clear();
    }

    /// <summary>
    /// 이전 중심부터 center까지의 구체 이동과 양 끝 겹침을 검사하여 실제 통과한 대상에 피해를 요청한다.
    /// 빠른 돌진이 한 프레임에 Collider를 통과해도 검사하며 동일 공격의 수신자는 한 번만 처리한다.
    /// </summary>
    private void CheckSegment(Vector3 center)
    {
#if UNITY_EDITOR
        if (_debugCenters.Count == 0) _debugCenters.Add(_previousCenter);
        _debugCenters.Add(center);
        _debugUntil = Time.time + _gizmoDuration;
#endif
        foreach (Collider overlap in Physics.OverlapSphere(_previousCenter, _radius, _hitMask, QueryTriggerInteraction.Ignore))
            TryApplyDamage(overlap, overlap.ClosestPoint(_previousCenter));
        Vector3 delta = center - _previousCenter;
        if (delta.sqrMagnitude > 0.000001f)
        {
            foreach (RaycastHit hit in Physics.SphereCastAll(_previousCenter, _radius, delta.normalized,
                delta.magnitude, _hitMask, QueryTriggerInteraction.Ignore))
                TryApplyDamage(hit.collider, hit.point);
        }
        foreach (Collider overlap in Physics.OverlapSphere(center, _radius, _hitMask, QueryTriggerInteraction.Ignore))
            TryApplyDamage(overlap, overlap.ClosestPoint(center));
    }

    /// <summary>
    /// collider가 context의 대상 계층이고 차폐되지 않았으면 point와 공격 문맥으로 피해를 전달한다.
    /// 자신·다른 적·벽 뒤 대상 및 이미 맞은 수신자를 제외하고 공격당 한 번만 상태를 변경한다.
    /// </summary>
    private void TryApplyDamage(Collider collider, Vector3 point)
    {
        if (_context.Target == null || !collider.transform.IsChildOf(_context.Target) || collider.transform.IsChildOf(transform))
            return;
        IDamageable receiver = collider.GetComponentInParent<IDamageable>();
        if (receiver == null || _damaged.Contains(receiver))
            return;
        Vector3 delta = point - _previousCenter;
        foreach (RaycastHit hit in Physics.RaycastAll(_previousCenter, delta.normalized, delta.magnitude,
            _obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(_context.Target))
                return;
        }
        _damaged.Add(receiver);
        receiver.TakeDamage(new DamageContext(_damage, gameObject, point, _context.Direction, _context.PatternId, _context.AttackId, 1, BlindBlade.Combat.Contracts.IncomingAttackKind.Charge));
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!_showAttackGizmos || !isActiveAndEnabled || _debugCenters.Count < 2
            || !Application.isPlaying || Time.time > _debugUntil) return;
        for (int i = 1; i < _debugCenters.Count; i++)
            EnemyCombatService.DrawSweepGizmo(_debugCenters[i - 1], _debugCenters[i], _radius, Color.red);
        UnityEditor.Handles.Label(_debugCenters[_debugCenters.Count - 1], "적 돌진: 실제 검사 경로");
    }

    void OnDrawGizmosSelected()
    {
        if (!_showAttackGizmos || Application.isPlaying) return;
        Vector3 center = transform.position + Vector3.up * _height;
        EnemyCombatService.DrawSweepGizmo(center, center, _radius, Color.yellow);
        UnityEditor.Handles.Label(center, "적 돌진: 현재 히트박스 미리보기");
    }
#endif
}
