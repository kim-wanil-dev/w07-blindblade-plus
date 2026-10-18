using System;

using UnityEngine;


[DisallowMultipleComponent]
public sealed class FootstepEmitter : MonoBehaviour
{
    public event Action<Transform, Vector3> OnFootstepEmitted;
    private SensoryManager _sensoryManager;
    private EnemyAttackBehaviour[] _attacks;
    private bool _showGroundFootprints = true;

    [Header("걸음")]
    [SerializeField, Min(0.01f), Tooltip("발자국 사이 이동 거리(미터)")]
    private float _stepDistance = 0.7f;
    [SerializeField, Min(0f), Tooltip("중심에서 각 발까지의 거리(미터)")]
    private float _footOffset = 0.15f;
    [SerializeField, Min(0.01f), Tooltip("프레임 이동이 이 거리 이상이면 순간이동으로 처리(미터)")]
    private float _teleportDistance = 5f;
    private Vector3 _previousPosition;
    private float _accumulatedDistance;
    private bool _isLeft = true;

    [Header("지면")]
    [SerializeField] private LayerMask _groundLayers;
    [SerializeField, Min(0f), Tooltip("발 위치 위에서 레이를 시작하는 높이(미터)")]
    private float _rayHeight = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("발 위치 아래까지 검사할 깊이(미터)")]
    private float _rayDepth = 0.5f;
    void OnEnable()
    {
        _attacks = GetComponents<EnemyAttackBehaviour>();
        _previousPosition = transform.position;
        _accumulatedDistance = 0f;
        _isLeft = true;
    }

    void Start()
    {
        if (_groundLayers.value == 0 || _stepDistance <= 0f || _teleportDistance <= _stepDistance
            || !Game.IsInitialized || Game.Instance.SensoryManager == null)
        {
            Debug.LogError("FootstepEmitter의 지면·걸음 거리 설정과 Game의 공통 Emitter 연결을 확인해야 합니다.", this);
            enabled = false;
            return;
        }
        _sensoryManager = Game.Instance.SensoryManager;
        _attacks = GetComponents<EnemyAttackBehaviour>();
    }

    void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        Vector3 previousPosition = _previousPosition;
        _previousPosition = currentPosition;
        Vector3 movement = currentPosition - previousPosition;
        if (movement.magnitude >= _teleportDistance)
        {
            _accumulatedDistance = 0f;
            return;
        }
        movement.y = 0f;
        float distance = movement.magnitude;
        if (distance <= Mathf.Epsilon)
            return;
        Vector3 direction = movement / distance;
        float stepDistance = _stepDistance;
        float nextDistance = stepDistance - _accumulatedDistance;
        while (nextDistance <= distance)
        {
            Vector3 stepPosition = Vector3.Lerp(previousPosition, currentPosition, nextDistance / distance);
            EmitFootstep(stepPosition, direction);
            nextDistance += stepDistance;
        }
        _accumulatedDistance = Mathf.Repeat(_accumulatedDistance + distance, stepDistance);
    }

    /// <summary>
    /// 이동 경로의 발 위치와 방향으로 지면을 검사하고 공통 사건과 캐릭터 접촉 이벤트를 전달한다.
    /// </summary>
    private void EmitFootstep(Vector3 position, Vector3 direction)
    {
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        position += right * (_isLeft ? -_footOffset : _footOffset);
        bool isLeft = _isLeft;
        _isLeft = !_isLeft;
        
        if (!Physics.Raycast(position + Vector3.up * _rayHeight, Vector3.down,
                out RaycastHit hit, _rayHeight + _rayDepth, _groundLayers,
                QueryTriggerInteraction.Ignore))
            return;

        OnFootstepEmitted?.Invoke(transform, hit.point);
        if (_sensoryManager == null)
        {
            Debug.LogError("공통 감각 Emitter가 파괴되어 걸음 전달을 중단합니다.", this);
            enabled = false;
            return;
        }
        bool attackMovement = false;
        foreach (EnemyAttackBehaviour attack in _attacks)
        {
            if (attack.isActiveAndEnabled && attack.Phase == EnemyAttackPhase.Active && attack.AllowsMovement)
            {
                attackMovement = true;
                break;
            }
        }
        if (_showGroundFootprints)
            _sensoryManager.EmitFootstep(new SensoryEvent(hit.point, hit.normal, direction), isLeft, attackMovement);
    }

    /// <summary>
    /// 지면 발자국 표시 여부를 설정한다.
    /// visible이 false여도 발걸음 이벤트 전달은 유지한다.
    /// </summary>
    public void SetGroundFootprintsVisible(bool visible)
    {
        _showGroundFootprints = visible;
    }
}
