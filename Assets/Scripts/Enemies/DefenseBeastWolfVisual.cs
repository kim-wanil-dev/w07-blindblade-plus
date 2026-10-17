using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 일반 Beast의 이동·전조·도약 상태를 Wolf 리그에 표시한다.
/// DefenseBeastLeapAttack과 연결하며 TwinBoss 전용 공격·잠수 상태를 참조하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseBeastWolfVisual : MonoBehaviour
{
    private const int LEG_COUNT = 4;
    private const float MAX_MEASURED_SPEED = 20f;

    private static readonly float[] LEG_PHASE_OFFSETS = { 0f, 0.5f, 0.5f, 0f };
    private static readonly HashSet<DefenseBeastWolfVisual> _active = new HashSet<DefenseBeastWolfVisual>();
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");

    [Header("몸체 연결")]
    [SerializeField] private DefenseBeastLeapAttack _defenseBeast;
    [SerializeField] private Transform _spine;
    [SerializeField] private Transform _head;
    [SerializeField] private Transform _tail;
    [SerializeField] private Transform[] _hips = new Transform[LEG_COUNT];
    [SerializeField] private Transform[] _knees = new Transform[LEG_COUNT];
    [SerializeField] private Transform[] _ankles = new Transform[LEG_COUNT];
    [SerializeField] private Renderer[] _bodyRenderers;
    [SerializeField] private TwinBossWarningVisual _warningVisual;

    [Header("걸음")]
    [SerializeField, Min(0.1f), Tooltip("걸음 한 주기 동안 이동하는 거리(m)")]
    private float _strideLength = 1.8f;
    [SerializeField, Min(0.1f), Tooltip("발 들기가 최대가 되는 속도(m/s)")]
    private float _fullStrideSpeed = 3f;
    [SerializeField, Min(0.1f), Tooltip("이 속도 이상에서 질주 리듬이 최대가 된다(m/s)")]
    private float _gallopSpeed = 8f;
    [SerializeField, Min(0.1f)] private float _speedSmoothing = 10f;
    [SerializeField, Min(0f), Tooltip("걸을 때 엉덩이 관절을 앞뒤로 움직이는 각도")]
    private float _hipSwing = 32f;
    [SerializeField, Min(0f), Tooltip("발을 들 때 무릎을 굽히는 각도")]
    private float _kneeBend = 55f;
    [SerializeField, Range(0f, 1f), Tooltip("엉덩이·무릎 움직임을 발목에 따라 반영하는 비율")]
    private float _ankleFollow = 0.6f;
    [SerializeField, Min(0f)] private float _bodyBob = 0.06f;
    [SerializeField, Tooltip("질주할 때 척추를 앞뒤로 기울이는 각도")]
    private float _gallopPitch = 6f;
    [SerializeField, Min(0f)] private float _headNod = 6f;
    [SerializeField, Min(0f)] private float _tailSway = 18f;
    [SerializeField, Min(0f)] private float _tailRaise = 12f;
    [SerializeField, Min(0f)] private float _breathing = 0.012f;

    [Header("전투 자세")]
    [SerializeField, Min(0f), Tooltip("도약 전조 동안 몸을 낮추는 깊이(m)")]
    private float _crouchDepth = 0.2f;
    [SerializeField, Tooltip("도약 전조 동안 머리를 숙이는 각도")]
    private float _crouchPitch = 9f;
    [SerializeField, Tooltip("도약 중 척추를 기울이는 각도")]
    private float _leapPitch = -14f;
    [SerializeField, Min(0f), Tooltip("도약 중 앞다리와 뒷다리를 뻗는 각도")]
    private float _leapStretch = 55f;
    [SerializeField, Min(0.1f)] private float _poseBlendSpeed = 10f;
    [SerializeField, Min(0.01f)] private float _flinchDuration = 0.3f;
    [SerializeField, Min(0f)] private float _flinchPitch = 10f;

    [Header("런타임 상태")]
    private EnemyHealth _health;
    private Transform _beastRoot;
    private Vector3 _previousPosition;
    private Vector3 _spineBasePosition;
    private Quaternion _spineBaseRotation;
    private Quaternion _headBaseRotation;
    private Quaternion _tailBaseRotation;
    private readonly Quaternion[] _hipBaseRotations = new Quaternion[LEG_COUNT];
    private readonly Quaternion[] _kneeBaseRotations = new Quaternion[LEG_COUNT];
    private readonly Quaternion[] _ankleBaseRotations = new Quaternion[LEG_COUNT];
    private float _speed;
    private float _phase;
    private float _idleTime;
    private float _crouch;
    private float _leap;
    private float _flinchAt = -1f;
    private MaterialPropertyBlock _maskProperties;
    private float _publishedWarningIntensity = -1f;

    internal static IReadOnlyCollection<DefenseBeastWolfVisual> ActiveInstances => _active;
    public IReadOnlyList<Renderer> BodyRenderers => _bodyRenderers;
    public float WarningIntensity => _warningVisual.Intensity;

    void Awake()
    {
        _beastRoot = _defenseBeast.transform;
        _health = _defenseBeast.GetComponent<EnemyHealth>();
        _spineBasePosition = _spine.localPosition;
        _spineBaseRotation = _spine.localRotation;
        _headBaseRotation = _head.localRotation;
        _tailBaseRotation = _tail.localRotation;
        _maskProperties = new MaterialPropertyBlock();

        for (int i = 0; i < LEG_COUNT; i++)
        {
            _hipBaseRotations[i] = _hips[i].localRotation;
            _kneeBaseRotations[i] = _knees[i].localRotation;
            _ankleBaseRotations[i] = _ankles[i].localRotation;
        }
    }

    void OnDrawGizmos()
    {
        SceneShapeGizmo.Draw(_bodyRenderers);
    }

    void OnEnable()
    {
        _previousPosition = _beastRoot.position;
        _speed = 0f;
        _crouch = 0f;
        _leap = 0f;
        _flinchAt = -1f;
        _publishedWarningIntensity = -1f;
        _health.Damaged += HandleDamaged;
        _active.Add(this);
    }

    void OnDisable()
    {
        _health.Damaged -= HandleDamaged;
        _active.Remove(this);
    }

    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
            return;

        UpdateSpeed(deltaTime);
        float blend = 1f - Mathf.Exp(-_poseBlendSpeed * deltaTime);
        float quickBlend = 1f - Mathf.Exp(-_poseBlendSpeed * 3f * deltaTime);
        bool isJumping = _defenseBeast.IsJumping;
        _crouch = Mathf.Lerp(_crouch, _defenseBeast.IsPreparing ? 1f : 0f, blend);
        _leap = Mathf.Lerp(_leap, isJumping ? 1f : 0f, blend);

        if (!isJumping)
            _phase = Mathf.Repeat(_phase + _speed / _strideLength * deltaTime, 1f);

        _idleTime += deltaTime;
        ApplyPose(quickBlend);
        PublishWarningMask();
    }

    /// <summary>
    /// Beast 루트의 수평 이동량과 deltaTime으로 속도를 측정하고, 측정값을 _speedSmoothing으로 부드럽게 반영한다.
    /// 순간이동에 해당하는 큰 측정값은 MAX_MEASURED_SPEED로 제한한다.
    /// </summary>
    private void UpdateSpeed(float deltaTime)
    {
        Vector3 position = _beastRoot.position;
        Vector3 movement = Vector3.ProjectOnPlane(position - _previousPosition, Vector3.up);
        _previousPosition = position;
        float measuredSpeed = Mathf.Min(MAX_MEASURED_SPEED, movement.magnitude / deltaTime);
        _speed = Mathf.Lerp(_speed, measuredSpeed, 1f - Mathf.Exp(-_speedSmoothing * deltaTime));
    }

    /// <summary>
    /// 속도와 DefenseBeastLeapAttack의 전조·도약 상태를 사용해 척추·머리·꼬리·네 다리의 자세를 갱신한다.
    /// 각 관절은 저장한 기본 회전에 걸음·웅크림·도약·피격 보정을 더한다.
    /// </summary>
    private void ApplyPose(float quickBlend)
    {
        float stride = Mathf.Clamp01(_speed / _fullStrideSpeed) * (1f - _leap);
        float gallop = Mathf.InverseLerp(_gallopSpeed * 0.6f, _gallopSpeed, _speed);
        float run = gallop * stride;
        float cycle = _phase * Mathf.PI * 2f;
        float flinch = _flinchAt >= 0f
            ? 1f - Mathf.Clamp01((Time.time - _flinchAt) / _flinchDuration)
            : 0f;

        float bob = Mathf.Sin(cycle * 2f) * _bodyBob * stride
            + Mathf.Sin(_idleTime * 2.2f) * _breathing * (1f - stride)
            - _crouchDepth * _crouch;
        float spinePitch = Mathf.Sin(cycle) * _gallopPitch * run
            + _crouchPitch * _crouch + _leapPitch * _leap - _flinchPitch * flinch;
        _spine.localPosition = _spineBasePosition + Vector3.up * bob;
        _spine.localRotation = _spineBaseRotation * Quaternion.Euler(spinePitch, 0f, 0f);

        float headPitch = Mathf.Sin(cycle * 2f + 0.6f) * _headNod * stride
            + _crouchPitch * _crouch + 6f * run - 12f * _leap;
        _head.localRotation = _headBaseRotation * Quaternion.Euler(headPitch - _flinchPitch * flinch, 0f, 0f);
        float tailPitch = _tailRaise * _leap - 6f * _crouch;
        float tailYaw = Mathf.Sin(cycle) * _tailSway * stride
            + Mathf.Sin(_idleTime * 1.4f) * _tailSway * 0.4f * (1f - stride);
        _tail.localRotation = _tailBaseRotation * Quaternion.Euler(tailPitch, tailYaw, 0f);

        for (int i = 0; i < LEG_COUNT; i++)
        {
            float legPhase = Mathf.Repeat(_phase - LEG_PHASE_OFFSETS[i], 1f);
            float legCycle = legPhase * Mathf.PI * 2f;
            float hipSwing = Mathf.Sin(legCycle) * _hipSwing * stride;
            float kneeBend = Mathf.Max(0f, Mathf.Sin(legCycle + Mathf.PI * 0.5f)) * _kneeBend * stride;
            float ankleFollow = -hipSwing * _ankleFollow - kneeBend * 0.35f;
            bool frontLeg = i < 2;
            hipSwing = Mathf.Lerp(hipSwing, frontLeg ? -_leapStretch : _leapStretch, _leap);
            kneeBend = Mathf.Lerp(kneeBend, 0f, _leap);
            ankleFollow = Mathf.Lerp(ankleFollow, 0f, _leap);

            _hips[i].localRotation = _hipBaseRotations[i] * Quaternion.Euler(hipSwing, 0f, 0f);
            _knees[i].localRotation = _kneeBaseRotations[i] * Quaternion.Euler(kneeBend, 0f, 0f);
            _ankles[i].localRotation = Quaternion.Slerp(
                _ankles[i].localRotation,
                _ankleBaseRotations[i] * Quaternion.Euler(ankleFollow, 0f, 0f),
                quickBlend);
        }
    }

    /// <summary>
    /// EnemyHealth의 피해 문맥을 받아 피격 자세의 시작 시각을 갱신한다.
    /// flinch 보정은 ApplyPose에서 _flinchDuration 동안 줄어든다.
    /// </summary>
    private void HandleDamaged(DamageContext context)
    {
        _flinchAt = Time.time;
    }

    /// <summary>
    /// 전조의 현재 붉은 세기를 몸체 렌더러의 속성 블록에 전달한다.
    /// 화면 연기 마스크는 이 값을 읽고 외곽선 색을 흰색에서 붉은색으로 바꾼다.
    /// </summary>
    private void PublishWarningMask()
    {
        float intensity = _warningVisual.Intensity;
        if (Mathf.Approximately(_publishedWarningIntensity, intensity))
            return;

        _publishedWarningIntensity = intensity;
        foreach (Renderer renderer in _bodyRenderers)
        {
            renderer.GetPropertyBlock(_maskProperties);
            _maskProperties.SetFloat(_warningIntensityId, intensity);
            renderer.SetPropertyBlock(_maskProperties);
        }
    }
}
