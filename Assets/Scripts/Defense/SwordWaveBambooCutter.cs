using UnityEngine;

/// <summary>
/// 날아가는 검기가 매 프레임 지난 경로의 대나무를 BambooGrove로 벤다.
/// 베는 평면은 진행 방향을 품고 검기 초승달 면의 사선 기울기를 따르므로, 잘린 높이가 화면의 검기 사선과 같은 기울기로 이어진다.
/// 가운데의 대나무는 진행 방향으로, 양옆의 대나무는 각자 바깥쪽으로 조금씩 틀어 쓰러진다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DefenseSwordWave))]
public sealed class SwordWaveBambooCutter : MonoBehaviour
{
    [Header("베기 범위")]
    [SerializeField, Min(0f), Tooltip("크기 배율 1에서 진행 방향 좌우로 대나무를 베는 반폭(미터). 보이는 초승달 양 끝까지의 거리(호 반경 × sin 60°)에 맞춘다. 비행 중 커지는 배율을 곱한다.")]
    private float _halfWidth = 5.26f;
    [SerializeField, Min(0f), Tooltip("반폭 끝의 대나무가 쓰러지는 방향을 진행 방향에서 바깥쪽으로 트는 정도. 0이면 모두 진행 방향, 0.5면 끝에서 약 27°.")]
    private float _sideSpread = 0.5f;
    private Vector3 _previousPosition;
    private bool _hasPrevious;

    void LateUpdate()
    {
        Vector3 position = transform.position;
        if (_hasPrevious)
        {
            Vector3 path = position - _previousPosition;
            if (path.sqrMagnitude > 0.000001f)
            {
                // 초승달 면의 법선에서 진행 방향 성분을 빼 눕힌 각도는 무시하고 사선 기울기만 남긴다.
                Vector3 normal = Vector3.ProjectOnPlane(transform.forward, path.normalized);
                if (normal.sqrMagnitude < 0.0001f)
                    normal = Vector3.up;
                BambooGrove.CutAlongPath(_previousPosition, position, _halfWidth * transform.localScale.x,
                    normal.normalized, _sideSpread);
            }
        }
        _previousPosition = position;
        _hasPrevious = true;
    }
}
