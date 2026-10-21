using UnityEngine;

/// <summary>회피 시작 위치에 남는 피해 없는 감지용 캡슐이다. 공격 쪽에서 실제 궤적으로만 조회한다.</summary>
public sealed class DodgeAfterimage : MonoBehaviour
{
    private const int AFTERIMAGE_LAYER = 8;
    private const int AFTERIMAGE_MASK = 1 << AFTERIMAGE_LAYER;

    private static DodgeAfterimage _active;

    [Header("잔상 판정")]
    private KatanaDamageReceiver _receiver;

    public Vector3 Origin => transform.position;
    public static bool IsActive => _active != null;
    public static Vector3 ActiveOrigin => _active != null ? _active.Origin : Vector3.zero;

    /// <summary>receiver의 현재 위치에 controller와 같은 캡슐을 만들고 활성 잔상으로 등록해 반환한다.</summary>
    public static DodgeAfterimage Create(KatanaDamageReceiver receiver, CharacterController controller)
    {
        GameObject marker = new GameObject("Dodge Afterimage");
        marker.layer = AFTERIMAGE_LAYER;
        marker.transform.SetPositionAndRotation(receiver.transform.position, receiver.transform.rotation);
        DodgeAfterimage afterimage = marker.AddComponent<DodgeAfterimage>();
        afterimage._receiver = receiver;
        CapsuleCollider capsule = marker.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.center = controller.center;
        capsule.radius = controller.radius;
        capsule.height = controller.height;
        _active = afterimage;
        Physics.SyncTransforms();
        return afterimage;
    }

    /// <summary>start부터 end까지 radius의 공격 구체가 잔상에 닿으면 context를 수신기에 후보로 전달한다.</summary>
    public static void ReportSphereSweep(Vector3 start, Vector3 end, float radius, DamageContext context)
    {
        if (_active == null || context.Amount <= 0f) return;
        Vector3 delta = end - start;
        bool contact = Physics.CheckSphere(start, radius, AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (!contact && delta.sqrMagnitude > 0.000001f)
            contact = Physics.SphereCast(start, radius, delta.normalized, out _, delta.magnitude,
                AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (!contact) contact = Physics.CheckSphere(end, radius, AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (contact) _active._receiver.RecordAfterimageContact(context);
    }

    /// <summary>bottom·top의 공격 캡슐을 delta만큼 이동시켜 잔상과 닿으면 context를 후보로 전달한다.</summary>
    public static void ReportCapsuleSweep(Vector3 bottom, Vector3 top, float radius, Vector3 delta, DamageContext context)
    {
        if (_active == null || context.Amount <= 0f) return;
        bool contact = Physics.CheckCapsule(bottom, top, radius, AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (!contact && delta.sqrMagnitude > 0.000001f)
            contact = Physics.CapsuleCast(bottom, top, radius, delta.normalized, out _, delta.magnitude,
                AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (!contact) contact = Physics.CheckCapsule(bottom + delta, top + delta, radius,
            AFTERIMAGE_MASK, QueryTriggerInteraction.Collide);
        if (contact) _active._receiver.RecordAfterimageContact(context);
    }

    /// <summary>이전 칼날과 현재 칼날의 양 끝 및 중간 이동 궤적으로 잔상 접촉을 조회한다.</summary>
    public static void ReportBladeSweep(Vector3 previousBase, Vector3 previousTip,
        Vector3 bladeBase, Vector3 bladeTip, float radius, DamageContext context)
    {
        if (_active == null || context.Amount <= 0f) return;
        if (Physics.CheckCapsule(bladeBase, bladeTip, radius, AFTERIMAGE_MASK, QueryTriggerInteraction.Collide))
        {
            _active._receiver.RecordAfterimageContact(context);
            return;
        }
        for (int index = 0; index <= 4; index++)
        {
            float progress = index * 0.25f;
            Vector3 start = Vector3.Lerp(previousBase, previousTip, progress);
            Vector3 end = Vector3.Lerp(bladeBase, bladeTip, progress);
            ReportSphereSweep(start, end, radius, context);
        }
    }

    void OnDisable()
    {
        if (_active == this) _active = null;
    }
}
