using UnityEngine;

/// <summary>
/// 잘린 대나무 조각이 바닥 받침이나 다른 조각에 부딪힐 때 부딪힌 위치와 속도를 BambooGrove에 알린다.
/// 조각끼리 부딪히면 두 조각 모두 충돌을 받으므로 한 쌍에서 한 번만 알린다. 절단면 충돌은 알리지 않는다.
/// BambooGrove가 조각을 처음 물리로 바꿀 때 붙인다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BambooDebrisImpact : MonoBehaviour
{
    [Header("연결")]
    private BambooGrove _grove;
    private Collider _floor;

    void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount == 0)
            return;
        if (collision.collider != _floor)
        {
            // 상대가 조각이면 인스턴스 번호가 작은 쪽만 알려 같은 충돌을 두 번 세지 않는다.
            if (!collision.collider.TryGetComponent(out BambooDebrisImpact other)
                || other.GetInstanceID() < GetInstanceID())
                return;
        }
        // 접촉면에 수직으로 닿는 속도만 충격 세기로 쓴다.
        ContactPoint contact = collision.GetContact(0);
        float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        _grove.NotifyImpact(contact.point, speed);
    }

    /// <summary>충돌을 알릴 grove와 바닥 받침 충돌체 floor를 연결한다.</summary>
    public void Bind(BambooGrove grove, Collider floor)
    {
        _grove = grove;
        _floor = floor;
    }
}
