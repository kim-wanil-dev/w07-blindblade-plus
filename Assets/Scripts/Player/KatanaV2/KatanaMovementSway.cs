using UnityEngine;

/// <summary>실제 보행 방향 반대로 검과 검집에 작은 위치 관성을 더한다. 원본 애니메이션과 플레이어 이동 판정은 수정하지 않는다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMotor), typeof(KatanaWeaponVisual))]
[DefaultExecutionOrder(200)]
public sealed class KatanaMovementSway : MonoBehaviour
{
    private const float TELEPORT_DISTANCE_SQUARED = 4f;

    [Header("연결")]
    [SerializeField] private PlayerMotor _motor;
    [SerializeField] private KatanaWeaponVisual _weapon;
    private PlayerController _player;
    private PlayerClock _playerClock;
    private Transform _pivot;
    private Transform _rig;
    private Vector3 _previousPosition;
    private Vector3 _offset;
    private Vector3 _offsetVelocity;

    [Header("작은 이동 관성")]
    [SerializeField] private bool _enableSway = true;
    [SerializeField, Min(0f), Tooltip("정상 보행 시 이동 반대 방향으로 밀리는 좌우 거리(m). 0.008은 8mm입니다.")]
    private float _horizontalOffset = 0.008f;
    [SerializeField, Min(0f), Tooltip("앞으로 걸으면 뒤로, 뒤로 걸으면 앞으로 밀리는 거리(m).")]
    private float _forwardOffset = 0.006f;
    [SerializeField, Min(0.01f), Tooltip("최대 관성에 도달하는 실제 보행 속도(m/s). 느린 보행은 더 작게 움직입니다.")]
    private float _referenceSpeed = 1.4f;
    [SerializeField, Min(0.01f)] private float _followSmoothTime = 0.12f;
    [SerializeField, Min(0.01f)] private float _returnSmoothTime = 0.18f;

    void Awake()
    {
        if (_motor == null) _motor = GetComponent<PlayerMotor>();
        if (_weapon == null) _weapon = GetComponent<KatanaWeaponVisual>();
        _player = GetComponent<PlayerController>();
        _playerClock = GetComponent<PlayerClock>();
        if (_weapon.Animator == null)
        {
            enabled = false;
            return;
        }
        _rig = _weapon.Animator.transform;
        GameObject pivotObject = new GameObject("KatanaMovementSwayPivot");
        _pivot = pivotObject.transform;
        _pivot.SetParent(_rig.parent, false);
        _rig.SetParent(_pivot, false);
        _previousPosition = transform.position;
    }

    void OnEnable()
    {
        _previousPosition = transform.position;
    }

    void LateUpdate()
    {
        Vector3 displacement = transform.position - _previousPosition;
        _previousPosition = transform.position;
        if (_pivot == null || _player.IsPaused || _playerClock.DeltaTime <= 0f) return;
        displacement.y = 0f;
        if (displacement.sqrMagnitude > TELEPORT_DISTANCE_SQUARED) displacement = Vector3.zero;

        Vector3 target = CalculateOffset(displacement / _playerClock.DeltaTime);
        float smoothTime = target.sqrMagnitude > 0.00000001f ? _followSmoothTime : _returnSmoothTime;
        _offset = Vector3.SmoothDamp(_offset, target, ref _offsetVelocity, smoothTime,
            Mathf.Infinity, _playerClock.DeltaTime);
        _pivot.localPosition = _offset;
    }

    void OnDisable()
    {
        _offset = Vector3.zero;
        _offsetVelocity = Vector3.zero;
        if (_pivot != null) _pivot.localPosition = Vector3.zero;
    }

    void OnDestroy()
    {
        if (_pivot == null) return;
        if (_rig != null && _rig.parent == _pivot) _rig.SetParent(_pivot.parent, false);
        Destroy(_pivot.gameObject);
    }

    /// <summary>worldVelocity를 시점의 수평 전후·좌우 속도로 변환해 이동 반대 방향 오프셋을 반환한다. 공격·회피·사망 중 목표는 0이다.</summary>
    private Vector3 CalculateOffset(Vector3 worldVelocity)
    {
        PlayerActionState state = _player.CurrentState;
        if (!_enableSway || _motor.IsDodging || state == PlayerActionState.NormalAttack
            || state == PlayerActionState.IaidoDash || state == PlayerActionState.IaidoSlash
            || state == PlayerActionState.Dead) return Vector3.zero;
        Vector3 forward = _motor.ViewForward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector2 movement = Vector2.ClampMagnitude(new Vector2(Vector3.Dot(worldVelocity, right),
            Vector3.Dot(worldVelocity, forward)) / _referenceSpeed, 1f);
        return new Vector3(-movement.x * _horizontalOffset, 0f, -movement.y * _forwardOffset);
    }
}
