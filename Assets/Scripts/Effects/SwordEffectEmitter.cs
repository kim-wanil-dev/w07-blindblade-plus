using System.Collections.Generic;

using UnityEngine;


/// <summary>
/// 베기 구간에 검의 이동 경로와 접촉을 관찰해 공용 Hit를 자동 요청한다.
/// 피해·가드·사망 판정은 수행하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SwordEffectEmitter : MonoBehaviour
{
    private const int CONTACT_CAPACITY = 32;
    private const int SWEEP_SAMPLES = 5;

    [Header("검 연결")]
    [SerializeField] private Transform _bladeBase;
    [SerializeField] private Transform _bladeTip;
    [SerializeField] private Transform _ownerRoot;

    [Header("시각적 접촉 감지")]
    [SerializeField] private LayerMask _contactLayers = Physics.DefaultRaycastLayers;
    [SerializeField, Min(0.001f)] private float _contactRadius = 0.04f;
    [SerializeField, Min(0.1f)] private float _teleportDistance = 3f;
    private readonly RaycastHit[] _castHits = new RaycastHit[CONTACT_CAPACITY];
    private readonly Collider[] _overlaps = new Collider[CONTACT_CAPACITY];
    private readonly List<EnemyCapsuleHit> _capsuleHits = new List<EnemyCapsuleHit>();
    private readonly List<EnemyHitCapsule> _capsuleOverlaps = new List<EnemyHitCapsule>();
    private readonly HashSet<Object> _reportedContacts = new HashSet<Object>();
    private EffectManager _effects;
    private Vector3 _previousBase;
    private Vector3 _previousTip;
    private bool _swinging;
    private bool _reportedCapacity;
    private int _effectiveContactLayers;
    public bool IsSwinging => _swinging;

    void Start()
    {
        _effects = Game.Instance.EffectManager;
    }

    void LateUpdate()
    {
        if (!_swinging || Time.deltaTime <= 0f)
            return;
        if (_effects == null || !_effects.IsReady || !HasActiveEndpoints())
        {
            CancelSwing();
            return;
        }
        DetectContacts();
    }

    /// <summary>
    /// 검의 이전·현재 끝점과 감지 설정으로 이동 경로 및 현재 접촉을 확인한다.
    /// 고정된 공용 Hit를 요청하고 다음 감지에 사용할 끝점 위치를 저장한다.
    /// </summary>
    private void DetectContacts()
    {
        Vector3 bladeBase = _bladeBase.position;
        Vector3 bladeTip = _bladeTip.position;
        float movement = Mathf.Max(Vector3.Distance(_previousBase, bladeBase),
            Vector3.Distance(_previousTip, bladeTip));
        if (movement < _teleportDistance)
        {
            for (int i = 0; i < SWEEP_SAMPLES; i++)
            {
                float progress = i / (float)(SWEEP_SAMPLES - 1);
                SweepContact(Vector3.Lerp(_previousBase, _previousTip, progress),
                    Vector3.Lerp(bladeBase, bladeTip, progress));
            }
        }
        SweepContact(bladeBase, bladeTip);
        DetectOverlaps(bladeBase, bladeTip);
        _previousBase = bladeBase;
        _previousTip = bladeTip;
    }

    void OnDisable()
    {
        CancelSwing();
    }

    /// <summary>
    /// Inspector의 검 끝점·소유자·레이어 설정으로 접촉 감지를 시작한다.
    /// 이전 감지를 취소하고 시작 위치와 중복 기록을 초기화하며 성공 여부를 반환한다.
    /// </summary>
    public bool BeginSwing()
    {
        if (!isActiveAndEnabled || _effects == null || !_effects.IsReady
            || !HasActiveEndpoints() || _ownerRoot == null)
        {
            Debug.LogWarning("[SwordEffectEmitter] BeginSwing requires Start initialization, active blade endpoints and an owner root.", this);
            return false;
        }
        CancelSwing();
        _effectiveContactLayers = _contactLayers.value;
        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if (enemy.gameObject.scene == gameObject.scene && enemy.IsAlive)
                _effectiveContactLayers |= 1 << enemy.CreateCombatTarget().HitCapsule.gameObject.layer;
        }
        _previousBase = _bladeBase.position;
        _previousTip = _bladeTip.position;
        _reportedCapacity = false;
        _swinging = true;
        return true;
    }

    /// <summary>
    /// 입력 없이 마지막 검 접촉을 확인하고 현재 접촉 감지를 종료한다.
    /// 감지 상태와 중복 기록을 해제하며 이미 재생된 Hit는 자연 종료한다.
    /// </summary>
    public void EndSwing()
    {
        if (_swinging && Time.deltaTime > 0f && _effects != null
            && _effects.IsReady && HasActiveEndpoints())
            DetectContacts();
        _swinging = false;
        _reportedContacts.Clear();
    }

    /// <summary>
    /// 입력 없이 진행 중인 접촉 감지를 즉시 중단한다.
    /// 취소·무기 교체·비활성화 시 감지 상태와 중복 기록을 해제한다.
    /// </summary>
    public void CancelSwing()
    {
        _swinging = false;
        _reportedContacts.Clear();
    }

    /// <summary>
    /// 설정한 검 양 끝점의 참조와 계층 활성 상태를 확인한다.
    /// 두 끝점이 모두 존재하고 활성 상태일 때 true를 반환한다.
    /// </summary>
    private bool HasActiveEndpoints()
    {
        return _bladeBase != null && _bladeTip != null
            && _bladeBase.gameObject.activeInHierarchy && _bladeTip.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// from에서 to까지 구를 이동시켜 감지 레이어의 가장 가까운 외부 접촉점을 찾는다.
    /// 장애물은 물리 쿼리로, 적은 피격 캡슐 계산으로 찾고 자기 소유 충돌체를 제외한 실제 접촉 위치에 공용 Hit를 요청한다.
    /// </summary>
    private void SweepContact(Vector3 from, Vector3 to)
    {
        Vector3 movement = to - from;
        float distance = movement.magnitude;
        if (distance <= 0.0001f)
            return;
        int count = Physics.SphereCastNonAlloc(from, _contactRadius, movement / distance,
            _castHits, distance, _effectiveContactLayers, QueryTriggerInteraction.Collide);
        ReportCapacity(count);
        float nearestDistance = float.PositiveInfinity;
        Object nearestContact = null;
        Vector3 nearestPoint = to;
        for (int i = 0; i < count; i++)
        {
            if (!IsExternalCollider(_castHits[i].collider) || _castHits[i].distance <= 0f
                || _castHits[i].distance >= nearestDistance)
                continue;
            nearestDistance = _castHits[i].distance;
            nearestContact = _castHits[i].collider;
            nearestPoint = _castHits[i].point;
        }
        EnemyHitCapsule.SweepAll(from, from, _contactRadius, movement / distance, distance,
            _effectiveContactLayers, _capsuleHits);
        foreach (EnemyCapsuleHit hit in _capsuleHits)
        {
            if (hit.Distance <= 0f || hit.Distance >= nearestDistance)
                continue;
            nearestDistance = hit.Distance;
            nearestContact = hit.Capsule;
            nearestPoint = hit.Point;
        }
        if (nearestContact != null)
            PlayContact(nearestContact, nearestPoint);
    }

    /// <summary>
    /// 현재 검 구간과 겹치는 감지 레이어 장애물과 적 피격 캡슐을 찾아 시작 중첩과 정지 접촉을 보완한다.
    /// 검의 이전 양 끝점·현재 양 끝점 중 가장 가까운 표면 후보 위치에 Hit를 요청한다.
    /// </summary>
    private void DetectOverlaps(Vector3 bladeBase, Vector3 bladeTip)
    {
        int count = Physics.OverlapCapsuleNonAlloc(bladeBase, bladeTip, _contactRadius,
            _overlaps, _effectiveContactLayers, QueryTriggerInteraction.Collide);
        ReportCapacity(count);
        for (int i = 0; i < count; i++)
        {
            Collider collider = _overlaps[i];
            if (!IsExternalCollider(collider) || _reportedContacts.Contains(collider))
                continue;
            Vector3 reference = bladeBase;
            float nearestDistance = float.PositiveInfinity;
            SelectSurfaceReference(collider.ClosestPoint(_previousBase), _previousBase, ref reference, ref nearestDistance);
            SelectSurfaceReference(collider.ClosestPoint(_previousTip), _previousTip, ref reference, ref nearestDistance);
            SelectSurfaceReference(collider.ClosestPoint(bladeBase), bladeBase, ref reference, ref nearestDistance);
            SelectSurfaceReference(collider.ClosestPoint(bladeTip), bladeTip, ref reference, ref nearestDistance);
            PlayContact(collider, collider.ClosestPoint(reference));
        }
        EnemyHitCapsule.OverlapAll(bladeBase, bladeTip, _contactRadius, _effectiveContactLayers, _capsuleOverlaps);
        foreach (EnemyHitCapsule capsule in _capsuleOverlaps)
        {
            if (_reportedContacts.Contains(capsule))
                continue;
            Vector3 reference = bladeBase;
            float nearestDistance = float.PositiveInfinity;
            SelectSurfaceReference(capsule.ClosestPoint(_previousBase), _previousBase, ref reference, ref nearestDistance);
            SelectSurfaceReference(capsule.ClosestPoint(_previousTip), _previousTip, ref reference, ref nearestDistance);
            SelectSurfaceReference(capsule.ClosestPoint(bladeBase), bladeBase, ref reference, ref nearestDistance);
            SelectSurfaceReference(capsule.ClosestPoint(bladeTip), bladeTip, ref reference, ref nearestDistance);
            PlayContact(capsule, capsule.ClosestPoint(reference));
        }
    }

    /// <summary>
    /// candidate와 그 위치에서 가장 가까운 표면점 surface 사이 거리를 비교한다.
    /// 표면 바깥의 candidate가 더 가까우면 reference와 nearestDistance를 갱신하고 내부 후보는 제외한다.
    /// </summary>
    private void SelectSurfaceReference(Vector3 surface, Vector3 candidate,
        ref Vector3 reference, ref float nearestDistance)
    {
        float distance = (candidate - surface).sqrMagnitude;
        if (distance > 0.000001f && distance < nearestDistance)
        {
            reference = candidate;
            nearestDistance = distance;
        }
    }

    /// <summary>
    /// collider가 검 컴포넌트와 소유자 계층 바깥에 있고 적 계층이 아닌지 확인한다.
    /// 적은 몸 충돌체가 남아 있어도 피격 캡슐로 따로 감지하며, 유효한 외부 장애물이면 true를 반환한다.
    /// </summary>
    private bool IsExternalCollider(Collider collider)
    {
        return collider != null && !collider.transform.IsChildOf(transform)
            && (_ownerRoot == null || !collider.transform.IsChildOf(_ownerRoot))
            && collider.GetComponentInParent<EnemyHealth>() == null;
    }

    /// <summary>
    /// 장애물 충돌체 또는 적 피격 캡슐 contact의 접촉 position에 Library에 등록된 공용 타격 효과를 요청한다.
    /// 성공한 접촉 대상을 기록하며 적 혈흔은 EnemyHealth의 실제 피해 처리에 맡긴다.
    /// </summary>
    private void PlayContact(Object contact, Vector3 position)
    {
        if (!_reportedContacts.Contains(contact) && _effects.PlayHit(position))
        {
            _reportedContacts.Add(contact);
        }
    }

    /// <summary>
    /// count가 접촉 버퍼 용량에 도달했는지 확인한다.
    /// 결과 누락 가능성을 베기당 한 번 알리고 안내 상태를 기록한다.
    /// </summary>
    private void ReportCapacity(int count)
    {
        if (count < CONTACT_CAPACITY || _reportedCapacity)
            return;
        _reportedCapacity = true;
        Debug.LogWarning("[SwordEffectEmitter] Contact buffer is full. Narrow the contact layers; some contacts may be omitted.", this);
    }
}
