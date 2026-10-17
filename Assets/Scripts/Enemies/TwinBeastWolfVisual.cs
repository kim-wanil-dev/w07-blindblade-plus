using UnityEngine;

/// <summary>짐승 보스의 도형 늑대 몸체를 이동 속도와 공격 상태에 맞춰 걷기·질주·웅크림·도약 자세로 움직인다.</summary>
[DisallowMultipleComponent]
public sealed class TwinBeastWolfVisual : MonoBehaviour
{
    private const int LEG_COUNT = 4;
    private const float MAX_MEASURED_SPEED = 20f;

    // 앞왼쪽, 앞오른쪽, 뒤왼쪽, 뒤오른쪽 다리의 걸음 위상 차이다.
    private static readonly float[] TROT_OFFSETS = { 0f, 0.5f, 0.5f, 0f };
    private static readonly float[] GALLOP_OFFSETS = { 0f, 0.12f, 0.55f, 0.67f };

    [Header("몸체 연결")]
    [SerializeField] private BeastBossAttack _beast;
    [SerializeField] private Transform _spine;
    [SerializeField] private Transform _head;
    [SerializeField] private Transform _tail;
    [SerializeField, Tooltip("앞왼쪽, 앞오른쪽, 뒤왼쪽, 뒤오른쪽 순서의 어깨·엉덩이 관절")]
    private Transform[] _hips = new Transform[LEG_COUNT];
    [SerializeField, Tooltip("Hips와 같은 순서의 무릎 관절")]
    private Transform[] _knees = new Transform[LEG_COUNT];
    [SerializeField, Tooltip("Hips와 같은 순서의 발목 관절. 앞다리는 손목, 뒷다리는 뒤꿈치(hock)다.")]
    private Transform[] _ankles = new Transform[LEG_COUNT];
    [SerializeField, Tooltip("편집 화면에서만 보이고 Play 중에는 숨겨 연기 방출 모양으로만 쓰는 도형 렌더러")]
    private Renderer[] _bodyRenderers;

    [Header("걸음")]
    [SerializeField, Min(0.1f), Tooltip("걸음 한 주기 동안 이동하는 거리(m)")]
    private float _strideLength = 1.8f;
    [SerializeField, Min(0.1f), Tooltip("다리 흔들림이 최대가 되는 속도(m/s)")]
    private float _fullStrideSpeed = 3f;
    [SerializeField, Min(0.1f), Tooltip("이 속도 이상이면 질주 리듬으로 바뀐다(m/s)")]
    private float _gallopSpeed = 8f;
    [SerializeField, Min(0.1f)] private float _speedSmoothing = 10f;
    [SerializeField] private float _hipSwing = 32f;
    [SerializeField] private float _kneeBend = 55f;
    [SerializeField, Range(0f, 1f), Tooltip("무릎 굽힘을 발목이 되돌려 발을 지면 쪽으로 세우는 비율")]
    private float _ankleFollow = 0.6f;
    [SerializeField] private float _bodyBob = 0.06f;
    [SerializeField] private float _gallopPitch = 6f;
    [SerializeField] private float _headNod = 6f;
    [SerializeField] private float _tailSway = 18f;
    [SerializeField] private float _tailRaise = 12f;
    [SerializeField] private float _breathing = 0.012f;
    private Vector3 _previousPosition;
    private float _speed;
    private float _phase;
    private float _idleTime;

    [Header("공격 자세")]
    [SerializeField, Tooltip("공격 전조 동안 몸을 낮추는 깊이(m)")]
    private float _crouchDepth = 0.2f;
    [SerializeField, Tooltip("공격 전조 동안 머리를 숙이는 각도")]
    private float _crouchPitch = 9f;
    [SerializeField, Tooltip("도약 중 머리를 드는 각도")]
    private float _leapPitch = -14f;
    [SerializeField, Tooltip("도약 중 앞다리는 앞으로, 뒷다리는 뒤로 뻗는 각도")]
    private float _leapStretch = 55f;
    [SerializeField, Min(0.1f)] private float _poseBlendSpeed = 10f;
    private float _crouch;
    private float _leap;
    private Vector3 _spineBasePosition;
    private Quaternion _spineBaseRotation;
    private Quaternion _headBaseRotation;
    private Quaternion _tailBaseRotation;
    private readonly Quaternion[] _hipBaseRotations = new Quaternion[LEG_COUNT];
    private readonly Quaternion[] _kneeBaseRotations = new Quaternion[LEG_COUNT];
    private readonly Quaternion[] _ankleBaseRotations = new Quaternion[LEG_COUNT];

    void Awake()
    {
        // 게임 화면에는 도형 윤곽선 대신 표면에서 피어오르는 연기만 보이게 한다.
        foreach (Renderer body in _bodyRenderers)
            body.enabled = false;
        _spineBasePosition = _spine.localPosition;
        _spineBaseRotation = _spine.localRotation;
        _headBaseRotation = _head.localRotation;
        _tailBaseRotation = _tail.localRotation;
        for (int i = 0; i < LEG_COUNT; i++)
        {
            _hipBaseRotations[i] = _hips[i].localRotation;
            _kneeBaseRotations[i] = _knees[i].localRotation;
            _ankleBaseRotations[i] = _ankles[i].localRotation;
        }
    }

    void OnDrawGizmos()
    {
        // Play 중 숨긴 도형을 Scene 뷰에서만 확인할 수 있게 한다.
        SceneShapeGizmo.Draw(_bodyRenderers);
    }

    void OnEnable()
    {
        _previousPosition = _beast.transform.position;
        _speed = 0f;
        _crouch = 0f;
        _leap = 0f;
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        UpdateSpeed(deltaTime);
        float blend = 1f - Mathf.Exp(-_poseBlendSpeed * deltaTime);
        _crouch = Mathf.Lerp(_crouch, _beast.IsPreparing ? 1f : 0f, blend);
        _leap = Mathf.Lerp(_leap, _beast.IsJumping ? 1f : 0f, blend);
        // 도약 중에는 다리를 뻗은 자세로 고정하므로 걸음 주기를 진행하지 않는다.
        if (!_beast.IsJumping)
            _phase = Mathf.Repeat(_phase + _speed / _strideLength * deltaTime, 1f);
        _idleTime += deltaTime;
        ApplyPose();
    }

    /// <summary>
    /// 짐승 루트의 수평 이동 거리와 deltaTime으로 현재 속도를 구해 _speed를 부드럽게 갱신한다.
    /// 순간이동처럼 비정상적으로 큰 값은 상한으로 자른다.
    /// </summary>
    private void UpdateSpeed(float deltaTime)
    {
        Vector3 position = _beast.transform.position;
        Vector3 moved = Vector3.ProjectOnPlane(position - _previousPosition, Vector3.up);
        _previousPosition = position;
        float measured = Mathf.Min(MAX_MEASURED_SPEED, moved.magnitude / deltaTime);
        _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-_speedSmoothing * deltaTime));
    }

    /// <summary>
    /// _speed, _phase, _crouch, _leap를 사용해 몸통 출렁임·머리 끄덕임·꼬리 흔들림·네 다리의 엉덩이·무릎·발목 회전을 적용한다.
    /// 모든 회전은 시작 시 저장한 기본 자세를 기준으로 덮어쓴다.
    /// </summary>
    private void ApplyPose()
    {
        float stride = Mathf.Clamp01(_speed / _fullStrideSpeed) * (1f - _leap);
        float gallop = Mathf.InverseLerp(_gallopSpeed * 0.6f, _gallopSpeed, _speed);
        float cycle = _phase * Mathf.PI * 2f;
        float idle = 1f - stride;

        // 걷기는 한 주기에 두 번 위아래로 출렁이고, 질주는 앞뒤로 흔들리듯 몸을 기울인다.
        float bob = (1f - Mathf.Cos(cycle * 2f)) * 0.5f * _bodyBob * stride
            + Mathf.Sin(_idleTime * 2.2f) * _breathing * idle
            - _crouchDepth * _crouch;
        float spinePitch = Mathf.Sin(cycle) * _gallopPitch * gallop * stride
            + _crouchPitch * _crouch + _leapPitch * _leap;
        _spine.localPosition = _spineBasePosition + Vector3.up * bob;
        _spine.localRotation = _spineBaseRotation * Quaternion.Euler(spinePitch, 0f, 0f);

        float headPitch = Mathf.Sin(cycle * 2f + 0.6f) * _headNod * stride + _crouchPitch * _crouch;
        _head.localRotation = _headBaseRotation * Quaternion.Euler(headPitch, 0f, 0f);

        float tailYaw = Mathf.Sin(cycle) * _tailSway * stride
            + Mathf.Sin(_idleTime * 1.4f) * _tailSway * 0.4f * idle;
        float tailPitch = -_tailRaise * Mathf.Max(stride, _leap) + _tailRaise * 0.5f * _crouch;
        _tail.localRotation = _tailBaseRotation * Quaternion.Euler(tailPitch, tailYaw, 0f);

        for (int i = 0; i < LEG_COUNT; i++)
        {
            bool front = i < 2;
            float legCycle = (_phase + Mathf.Lerp(TROT_OFFSETS[i], GALLOP_OFFSETS[i], gallop)) * Mathf.PI * 2f;
            // 양수 회전은 발을 뒤로 보내므로 앞으로 내딛는 구간에서 무릎을 접는다.
            float hip = -Mathf.Sin(legCycle) * _hipSwing * stride;
            float knee = Mathf.Max(0f, Mathf.Cos(legCycle)) * _kneeBend * stride;
            hip += (front ? -_leapStretch : _leapStretch) * _leap - 12f * _crouch;
            knee += 35f * _crouch;
            _hips[i].localRotation = _hipBaseRotations[i] * Quaternion.Euler(hip, 0f, 0f);
            _knees[i].localRotation = _kneeBaseRotations[i] * Quaternion.Euler(knee, 0f, 0f);
            // 발목은 무릎 굽힘을 일부 되돌려 발바닥이 지면 쪽을 향하게 한다.
            _ankles[i].localRotation = _ankleBaseRotations[i] * Quaternion.Euler(-knee * _ankleFollow, 0f, 0f);
        }
    }
}
