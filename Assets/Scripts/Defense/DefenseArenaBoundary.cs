using UnityEngine;

/// <summary>
/// 현재 플레이어를 이 오브젝트 중심의 원형 공터 안에 머물게 한다.
/// 충돌체를 만들지 않으므로 적·투사체·검기의 이동과 판정에는 영향을 주지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseArenaBoundary : MonoBehaviour
{
    [Header("플레이어 이동 범위")]
    [SerializeField, Min(1f), Tooltip("플레이어 이동 경계 반경(m). 씬의 Arena에서 조정합니다.")]
    private float _radius = 23.6f;
    private PlayerController _player;
    private CharacterController _controller;

    public float Radius => _radius;

    void LateUpdate()
    {
        PlayerController player = Game.Player;
        if (player == null)
            return;
        if (_player != player)
        {
            _player = player;
            _controller = player.GetComponent<CharacterController>();
        }
        Vector3 offset = Vector3.ProjectOnPlane(player.transform.position - transform.position, Vector3.up);
        float distance = offset.magnitude;
        if (distance <= _radius)
            return;
        _controller.Move(-offset / distance * (distance - _radius));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        for (int i = 0; i < 64; i++)
        {
            float from = i / 64f * Mathf.PI * 2f;
            float to = (i + 1) / 64f * Mathf.PI * 2f;
            Gizmos.DrawLine(transform.position + new Vector3(Mathf.Cos(from), 0f, Mathf.Sin(from)) * _radius,
                transform.position + new Vector3(Mathf.Cos(to), 0f, Mathf.Sin(to)) * _radius);
        }
    }
}
