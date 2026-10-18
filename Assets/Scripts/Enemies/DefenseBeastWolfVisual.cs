using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 일반 Beast의 도형 늑대 몸체를 짐승 보스와 같은 관절 기반 전투 자세로 움직인다.
/// 머리를 낮추고 귀를 젖힌 사냥 자세를 기본으로, 발 위치를 먼저 정하고 두 마디 다리를 풀어 걷고 질주하며
/// DefenseBeastLeapAttack 상태에 따라 웅크림·뛰어들기·물어뜯기·방어 반동·피격 움찔 자세를 섞는다.
/// 전조의 붉은 세기는 몸체 렌더러에 전달해 화면 연기 마스크가 읽게 한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseBeastWolfVisual : MonoBehaviour
{
    private const int LEG_COUNT = 4;
    private const float MAX_MEASURED_SPEED = 20f;
    private const float LAST_FRONT_PLANT = 0.55f;
    private const float MIN_SUSPENSION_WIDTH = 0.1f;
    private const float SNAP_TURN_STIFFNESS_SCALE = 4f;
    private const int IDLE_ACTION_COUNT = 4;
    private const int IDLE_SCAN = 0;
    private const int IDLE_SNIFF = 1;
    private const int IDLE_SHIFT = 2;
    private const int IDLE_GROWL = 3;

    // 앞왼쪽, 앞오른쪽, 뒤왼쪽, 뒤오른쪽 다리가 땅에 닿는 위상이다. 짐승 보스와 같은 걷기·엇갈림 질주 순서다.
    private static readonly float[] WALK_OFFSETS = { 0.25f, 0.75f, 0f, 0.5f };
    private static readonly float[] GALLOP_OFFSETS = { 0.45f, 0.55f, 0f, 0.1f };
    private static readonly HashSet<DefenseBeastWolfVisual> _active = new HashSet<DefenseBeastWolfVisual>();
    private static readonly int _warningIntensityId = Shader.PropertyToID("_MeleeWarningIntensity");

    [Header("몸체 연결")]
    [SerializeField] private DefenseBeastLeapAttack _defenseBeast;
    [SerializeField] private Transform _spine;
    [SerializeField] private Transform _head;
    [SerializeField] private Transform _tail;
    [SerializeField, Tooltip("머리 안의 아래턱. 뛰어들 때 벌리고 물 때 닫는다.")]
    private Transform _jaw;
    [SerializeField, Tooltip("왼쪽·오른쪽 귀. 전투 자세에서 뒤로 젖힌다.")]
    private Transform[] _ears = new Transform[2];
    [SerializeField, Tooltip("앞왼쪽, 앞오른쪽, 뒤왼쪽, 뒤오른쪽 순서의 어깨·엉덩이 관절")]
    private Transform[] _hips = new Transform[LEG_COUNT];
    [SerializeField, Tooltip("Hips와 같은 순서의 무릎 관절")]
    private Transform[] _knees = new Transform[LEG_COUNT];
    [SerializeField, Tooltip("Hips와 같은 순서의 발목 관절. 앞다리는 손목, 뒷다리는 뒤꿈치(hock)다.")]
    private Transform[] _ankles = new Transform[LEG_COUNT];
    [SerializeField] private Renderer[] _bodyRenderers;
    [SerializeField] private TwinBossWarningVisual _warningVisual;
    private EnemyHealth _health;
    private EnemySensor _sensor;
    private Transform _beastRoot;

    [Header("걸음")]
    [SerializeField, Min(0.1f), Tooltip("걷기 한 주기(네 발이 한 번씩 딛는 동안) 이동하는 거리(m)")]
    private float _walkStride = 2.8f;
    [SerializeField, Min(0.1f), Tooltip("질주 한 주기 동안 이동하는 거리(m). 클수록 보폭이 넓고 발놀림이 느긋하다.")]
    private float _gallopStride = 4.8f;
    [SerializeField, Min(0.1f), Tooltip("보폭과 발 들기가 최대가 되는 속도(m/s)")]
    private float _fullStrideSpeed = 4f;
    [SerializeField, Min(0.1f), Tooltip("이 속도 이상이면 질주 리듬으로 바뀐다(m/s)")]
    private float _gallopSpeed = 9f;
    [SerializeField, Tooltip("켜면 속도와 상관없이 걷기 없이 항상 질주 리듬으로 발을 놀린다.")]
    private bool _alwaysGallop = true;
    [SerializeField, Min(0.1f)] private float _speedSmoothing = 6f;
    [SerializeField] private float _bodyBob = 0.06f;
    [SerializeField, Tooltip("질주할 때 척추를 굽혔다 펴며 앞뒤로 기울이는 각도")]
    private float _gallopPitch = 14f;
    [SerializeField, Tooltip("질주 한 걸음마다 네 발이 모두 떠 몸이 솟구치는 높이(m)")]
    private float _gallopBound = 0.32f;
    [SerializeField] private float _headNod = 4f;
    [SerializeField, Tooltip("걸을 때 걸음에 맞춰 어깨를 좌우로 굴리는 각도")]
    private float _shoulderRoll = 5f;
    [SerializeField, Tooltip("걸을 때 머리를 좌우로 흔드는 각도")]
    private float _headSway = 5f;
    [SerializeField] private float _tailSway = 14f;
    [SerializeField, Tooltip("질주할 때 꼬리를 뒤로 쭉 펴 올리는 각도")]
    private float _tailRaise = 20f;
    [SerializeField] private float _breathing = 0.012f;
    private Vector3 _previousPosition;
    private float _speed;
    private float _phase;
    private float _idleTime;
    private float _gaitDirection = 1f;
    private readonly float[] _previousLegPhases = new float[LEG_COUNT];

    [Header("발 디딤")]
    [SerializeField, Range(0.1f, 0.9f), Tooltip("걷기 한 주기 중 발이 땅을 딛고 있는 비율")]
    private float _walkDuty = 0.65f;
    [SerializeField, Range(0.1f, 0.9f), Tooltip("질주 한 주기 중 발이 땅을 딛고 있는 비율")]
    private float _gallopDuty = 0.2f;
    [SerializeField, Min(0f), Tooltip("걸을 때 발을 드는 높이(m)")]
    private float _walkLift = 0.15f;
    [SerializeField, Min(0f), Tooltip("질주할 때 발을 드는 높이(m)")]
    private float _gallopLift = 0.36f;
    [SerializeField, Tooltip("발을 들어 옮기는 동안 발바닥을 접는 각도")]
    private float _swingPawCurl = 35f;
    [SerializeField, Range(0.5f, 0.99f), Tooltip("다리 길이에 대한 이 비율부터 다리를 천천히 펴 무릎이 끝까지 펴지며 튀지 않게 한다.")]
    private float _softReachStart = 0.85f;
    private readonly float[] _thighLengths = new float[LEG_COUNT];
    private readonly float[] _shinLengths = new float[LEG_COUNT];
    private readonly float[] _kneeSigns = new float[LEG_COUNT];
    private readonly float[] _restHipAngles = new float[LEG_COUNT];
    private readonly float[] _restKneeAngles = new float[LEG_COUNT];
    private readonly float[] _restAnkleAngles = new float[LEG_COUNT];
    private readonly Vector2[] _pawOffsets = new Vector2[LEG_COUNT];
    private readonly Vector3[] _restPaws = new Vector3[LEG_COUNT];

    [Header("반발")]
    [SerializeField, Min(0f), Tooltip("움직이는 동안 몸을 낮춰 관절을 땅 가까이 굽히는 깊이(m). 걸음 비중에 비례한다.")]
    private float _moveLowering = 0.16f;
    [SerializeField, Min(0f), Tooltip("발이 땅에 닿을 때 몸을 눌러 내리는 속도(m/s). 스프링으로 되튀어 땅을 밀어내는 반발력을 보인다.")]
    private float _contactImpulse = 0.3f;
    [SerializeField, Min(1f), Tooltip("몸 출렁임 스프링 세기. 클수록 빠르게 되튄다.")]
    private float _springStiffness = 140f;
    [SerializeField, Range(0f, 2f), Tooltip("몸 출렁임 감쇠 비율. 1이면 되튐 없이 돌아오고 작을수록 크게 되튄다.")]
    private float _springDampingRatio = 0.6f;
    private float _springOffset;
    private float _springVelocity;

    [Header("감속·대기")]
    [SerializeField, Min(0.1f), Tooltip("멈출 때 걸음 속도가 줄어드는 빠르기. 작을수록 걸음을 천천히 늦추며 멈춘다.")]
    private float _decelerationSmoothing = 2.5f;
    [SerializeField, Min(0f), Tooltip("멈춰 있을 때 제자리에서 발을 바꿔 딛는 높이(m)")]
    private float _idleStepLift = 0.06f;
    [SerializeField, Min(0f), Tooltip("멈춰 있을 때 제자리 발 바꿈 주기(초당 횟수)")]
    private float _idleStepCadence = 0.4f;
    [SerializeField, Min(0.1f), Tooltip("대기 동작(살피기·냄새 맡기·무게 옮기기·으르렁) 하나를 이어가는 최소 시간(초)")]
    private float _idleActionMin = 1.8f;
    [SerializeField, Min(0.1f)] private float _idleActionMax = 3.5f;
    [SerializeField, Min(0.1f), Tooltip("대기 동작이 다음 동작으로 섞여 넘어가는 빠르기(초당)")]
    private float _idleActionBlend = 1.5f;
    [SerializeField, Tooltip("냄새 맡을 때 머리를 숙이는 각도")]
    private float _sniffPitch = 26f;
    [SerializeField, Tooltip("무게를 옮길 때 몸을 좌우로 기울이는 각도")]
    private float _shiftRoll = 6f;
    [SerializeField, Tooltip("무게를 옮길 때 몸을 좌우로 미는 거리(m)")]
    private float _shiftSway = 0.06f;
    private readonly float[] _idleWeights = new float[IDLE_ACTION_COUNT];
    private int _idleAction;
    private float _nextIdleActionAt;

    [Header("몸·고개 돌림")]
    [SerializeField, Min(0.1f), Tooltip("Beast 방향이 바뀔 때 도형 몸이 따라 도는 스프링 세기. 작을수록 천천히 부드럽게 돈다.")]
    private float _bodyTurnStiffness = 90f;
    [SerializeField, Min(0.1f), Tooltip("머리가 몸보다 늦게 따라 도는 스프링 세기")]
    private float _headTurnStiffness = 35f;
    [SerializeField, Min(0f)] private float _maxBodyTurnLag = 60f;
    [SerializeField, Min(0f)] private float _maxHeadTurnLag = 70f;
    private float _lastRootYaw;
    private float _bodyTurnLag;
    private float _bodyTurnVelocity;
    private float _headTurnLag;
    private float _headTurnVelocity;

    [Header("시선")]
    [SerializeField, Min(0f), Tooltip("원을 그리며 달리는 동안 몸 방향과 상관없이 대상 쪽으로 고개를 돌리는 최대 각도")]
    private float _maxHeadLook = 80f;
    [SerializeField, Min(0.1f), Tooltip("고개가 대상 쪽을 따라가는 스프링 세기")]
    private float _headLookStiffness = 30f;
    private float _headLook;
    private float _headLookVelocity;

    [Header("전투 자세")]
    [SerializeField, Tooltip("평소에도 몸을 낮추는 깊이(m)")]
    private float _stanceDrop = 0.08f;
    [SerializeField, Tooltip("평소 앞쪽을 낮춰 몸을 앞으로 기울이는 각도")]
    private float _stancePitch = 6f;
    [SerializeField, Tooltip("평소 머리를 낮게 깔아 노려보는 각도")]
    private float _stanceHeadPitch = 16f;
    [SerializeField, Tooltip("평소 꼬리를 내리는 각도")]
    private float _stanceTailDrop = 8f;
    [SerializeField, Tooltip("귀를 뒤로 젖히는 각도")]
    private float _earPinBack = 35f;
    [SerializeField, Tooltip("멈춰 있을 때 좌우를 살피는 머리 각도")]
    private float _idleScanYaw = 10f;
    [SerializeField, Tooltip("웅크린 전조 동안 이빨을 드러내는 아래턱 각도")]
    private float _growlJaw = 12f;

    [Header("공격 자세")]
    [SerializeField, Tooltip("공격 전조 동안 몸을 낮추는 깊이(m)")]
    private float _crouchDepth = 0.2f;
    [SerializeField, Tooltip("공격 전조 동안 머리를 숙이는 각도")]
    private float _crouchPitch = 9f;
    [SerializeField, Tooltip("뛰어들어 몸이 떴을 때 머리를 드는 각도")]
    private float _leapPitch = -14f;
    [SerializeField, Tooltip("뛰어들어 몸이 떴을 때 앞다리는 앞으로, 뒷다리는 뒤로 뻗는 각도")]
    private float _leapStretch = 55f;
    [SerializeField, Tooltip("뛰어드는 동안 앞다리 무릎(앞발)을 살짝 굽히는 각도")]
    private float _pounceFrontKneeBend = 25f;
    [SerializeField, Tooltip("뛰어드는 동안 머리를 앞으로 내미는 거리(m)")]
    private float _biteLunge = 0.2f;
    [SerializeField, Tooltip("뛰어드는 동안 아래턱을 벌리는 각도")]
    private float _jawOpen = 38f;
    [SerializeField, Tooltip("머리 기준 아래턱이 벌어지는 경첩 위치")]
    private Vector3 _jawHinge = new Vector3(0f, 0.13f, 0.31f);
    [SerializeField, Tooltip("물어뜯는 동안 머리를 좌우로 흔드는 각도")]
    private float _tearShake = 14f;
    [SerializeField, Min(0.1f), Tooltip("물어뜯는 동안 머리를 흔드는 빠르기(초당 횟수)")]
    private float _tearFrequency = 7f;
    [SerializeField, Min(0.1f)] private float _poseBlendSpeed = 10f;
    private float _crouch;
    private float _leap;
    private float _bite;
    private float _tear;
    private float _recoil;

    [Header("피격")]
    [SerializeField, Tooltip("맞았을 때 몸을 뒤로 젖히는 각도")]
    private float _flinchPitch = 10f;
    [SerializeField, Min(0.01f)] private float _flinchDuration = 0.3f;
    private float _flinchAt = -1f;

    [Header("런타임 상태")]
    private Vector3 _spineBasePosition;
    private Quaternion _spineBaseRotation;
    private Vector3 _headBasePosition;
    private Quaternion _headBaseRotation;
    private Quaternion _tailBaseRotation;
    private Vector3 _jawBasePosition;
    private Quaternion _jawBaseRotation;
    private readonly Quaternion[] _earBaseRotations = new Quaternion[2];
    private MaterialPropertyBlock _maskProperties;
    private float _publishedWarningIntensity = -1f;

    internal static IReadOnlyCollection<DefenseBeastWolfVisual> ActiveInstances => _active;
    public IReadOnlyList<Renderer> BodyRenderers => _bodyRenderers;
    public float WarningIntensity => _warningVisual.Intensity;

    void Awake()
    {
        _beastRoot = _defenseBeast.transform;
        // 짐승 보스처럼 게임 화면에는 도형 몸체 대신 표면에서 피어오르는 연기만 보이게 한다.
        foreach (Renderer body in _bodyRenderers)
            body.enabled = false;
        _health = _defenseBeast.GetComponent<EnemyHealth>();
        _sensor = _defenseBeast.GetComponent<EnemySensor>();
        _spineBasePosition = _spine.localPosition;
        _spineBaseRotation = _spine.localRotation;
        _headBasePosition = _head.localPosition;
        _headBaseRotation = _head.localRotation;
        _tailBaseRotation = _tail.localRotation;
        _jawBasePosition = _jaw.localPosition;
        _jawBaseRotation = _jaw.localRotation;
        for (int i = 0; i < _ears.Length; i++)
            _earBaseRotations[i] = _ears[i].localRotation;
        for (int i = 0; i < LEG_COUNT; i++)
            CacheLeg(i);
        _maskProperties = new MaterialPropertyBlock();
    }

    /// <summary>
    /// leg 다리의 기본 자세에서 허벅지·정강이 길이, 엉덩이·무릎·발목의 앞뒤 회전 각도, 무릎이 굽는 방향,
    /// 발목 기준 발바닥 위치와 늑대 루트 기준 발바닥 기본 위치를 저장한다. 다리 관절은 모두 X축으로만 돈다고 본다.
    /// </summary>
    private void CacheLeg(int leg)
    {
        _thighLengths[leg] = _knees[leg].localPosition.magnitude;
        _shinLengths[leg] = _ankles[leg].localPosition.magnitude;
        _restHipAngles[leg] = GetPitch(_hips[leg].localRotation);
        _restKneeAngles[leg] = GetPitch(_knees[leg].localRotation);
        _restAnkleAngles[leg] = GetPitch(_ankles[leg].localRotation);
        _kneeSigns[leg] = _restKneeAngles[leg] >= 0f ? 1f : -1f;
        // 발목 아래에서 가장 낮은 부위를 발바닥으로 본다.
        Vector3 paw = Vector3.zero;
        foreach (Transform child in _ankles[leg])
            if (child.localPosition.y < paw.y) paw = child.localPosition;
        _pawOffsets[leg] = new Vector2(paw.y, paw.z);
        _restPaws[leg] = transform.InverseTransformPoint(_ankles[leg].TransformPoint(paw));
    }

    /// <summary>X축으로만 도는 rotation의 회전 각도(도)를 반환한다.</summary>
    private static float GetPitch(Quaternion rotation)
    {
        return 2f * Mathf.Atan2(rotation.x, rotation.w) * Mathf.Rad2Deg;
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
        _bite = 0f;
        _tear = 0f;
        _recoil = 0f;
        _flinchAt = -1f;
        _lastRootYaw = _beastRoot.eulerAngles.y;
        _bodyTurnLag = 0f;
        _headTurnLag = 0f;
        _headLook = 0f;
        _gaitDirection = 1f;
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
        bool lunging = _defenseBeast.IsLunging;
        bool biting = _defenseBeast.IsBiting;
        _crouch = Mathf.Lerp(_crouch, _defenseBeast.IsPreparing ? 1f : 0f, blend);
        // 뛰어드는 순간 몸이 뜬 만큼 다리를 뻗고, 물면 바로 내려와 물어뜯는다.
        _leap = Mathf.Lerp(_leap, _defenseBeast.LungeAir, quickBlend);
        _bite = Mathf.Lerp(_bite, lunging ? 1f : 0f, quickBlend);
        _tear = Mathf.Lerp(_tear, biting ? 1f : 0f, quickBlend);
        _recoil = Mathf.Lerp(_recoil, _defenseBeast.IsBouncingBack ? 1f : 0f, quickBlend);
        // 멈춰 있어도 제자리에서 발을 바꿔 딛도록 최소 걸음 주기를 두고, 뒷걸음질은 걸음 주기를 거꾸로 돌린다.
        _phase = Mathf.Repeat(_phase + _gaitDirection * Mathf.Max(_speed / GetStride(), _idleStepCadence) * deltaTime, 1f);
        _idleTime += deltaTime;
        UpdateSpring(deltaTime);
        UpdateIdleAction(deltaTime);
        UpdateTurnLag(deltaTime, lunging || biting || _defenseBeast.IsBouncingBack);
        UpdateHeadLook(deltaTime);
        ApplyPose();
        PublishWarningMask();
    }

    /// <summary>
    /// 몸 출렁임 스프링 _springOffset·_springVelocity를 deltaTime만큼 진행한다.
    /// _springStiffness와 _springDampingRatio로 발 디딤 충격 뒤 눌렸다가 되튀는 움직임을 만든다.
    /// </summary>
    private void UpdateSpring(float deltaTime)
    {
        float damping = 2f * _springDampingRatio * Mathf.Sqrt(_springStiffness);
        _springVelocity += (-_springStiffness * _springOffset - damping * _springVelocity) * deltaTime;
        _springOffset += _springVelocity * deltaTime;
    }

    /// <summary>
    /// 정해진 시간마다 살피기·냄새 맡기·무게 옮기기·으르렁 중 직전과 다른 대기 동작을 고르고,
    /// 각 동작 비중 _idleWeights를 _idleActionBlend 빠르기로 옮겨 동작이 끊기지 않고 섞이며 넘어가게 한다.
    /// </summary>
    private void UpdateIdleAction(float deltaTime)
    {
        if (Time.time >= _nextIdleActionAt)
        {
            _idleAction = (_idleAction + Random.Range(1, IDLE_ACTION_COUNT)) % IDLE_ACTION_COUNT;
            _nextIdleActionAt = Time.time + Random.Range(_idleActionMin, Mathf.Max(_idleActionMin, _idleActionMax));
        }
        for (int i = 0; i < IDLE_ACTION_COUNT; i++)
            _idleWeights[i] = Mathf.MoveTowards(_idleWeights[i], i == _idleAction ? 1f : 0f, _idleActionBlend * deltaTime);
    }

    /// <summary>
    /// Beast 루트의 수평 방향 변화만큼 몸·머리 회전 지연을 쌓고 임계 감쇠 스프링으로 부드럽게 따라가게 한다.
    /// 머리는 몸보다 약한 스프링이라 더 늦게 돈다. snap이 참인 뛰어들기·물기·반동 중에는 스프링을 세게 해
    /// 지연을 한 번에 지우지 않고 빠르게 따라잡아 몸이 튀지 않게 한다.
    /// </summary>
    private void UpdateTurnLag(float deltaTime, bool snap)
    {
        float yaw = _beastRoot.eulerAngles.y;
        float delta = Mathf.DeltaAngle(_lastRootYaw, yaw);
        _lastRootYaw = yaw;
        float scale = snap ? SNAP_TURN_STIFFNESS_SCALE : 1f;
        _bodyTurnLag = Mathf.Clamp(_bodyTurnLag - delta, -_maxBodyTurnLag, _maxBodyTurnLag);
        _headTurnLag = Mathf.Clamp(_headTurnLag - delta, -_maxHeadTurnLag, _maxHeadTurnLag);
        StepCriticalSpring(ref _bodyTurnLag, ref _bodyTurnVelocity, _bodyTurnStiffness * scale, deltaTime);
        StepCriticalSpring(ref _headTurnLag, ref _headTurnVelocity, _headTurnStiffness * scale, deltaTime);
    }

    /// <summary>
    /// EnemySensor의 대상이 있으면 몸 정면에서 대상까지의 수평 각도를 _maxHeadLook 안으로 잘라 목표로 삼고, 없으면 정면을 목표로 한다.
    /// 원을 그리며 달리는 동안에도 고개가 대상을 노려보도록 _headLook을 임계 감쇠 스프링으로 deltaTime만큼 다가가게 한다.
    /// </summary>
    private void UpdateHeadLook(float deltaTime)
    {
        float target = 0f;
        Transform lookTarget = _sensor.Target;
        if (lookTarget != null && lookTarget.gameObject.activeInHierarchy)
        {
            Vector3 forward = Vector3.ProjectOnPlane(_beastRoot.forward, Vector3.up);
            Vector3 toTarget = Vector3.ProjectOnPlane(lookTarget.position - _beastRoot.position, Vector3.up);
            if (forward.sqrMagnitude > 0.0001f && toTarget.sqrMagnitude > 0.0001f)
                target = Mathf.Clamp(Vector3.SignedAngle(forward, toTarget, Vector3.up), -_maxHeadLook, _maxHeadLook);
        }
        float offset = _headLook - target;
        StepCriticalSpring(ref offset, ref _headLookVelocity, _headLookStiffness, deltaTime);
        _headLook = target + offset;
    }

    /// <summary>value를 0으로 되돌리는 임계 감쇠 스프링을 stiffness 세기로 deltaTime만큼 진행해 value·velocity를 갱신한다.</summary>
    private static void StepCriticalSpring(ref float value, ref float velocity, float stiffness, float deltaTime)
    {
        velocity += (-stiffness * value - 2f * Mathf.Sqrt(stiffness) * velocity) * deltaTime;
        value += velocity * deltaTime;
    }

    /// <summary>
    /// Beast 루트의 수평 이동 거리와 deltaTime으로 현재 속도를 구해 _speed를 부드럽게 갱신한다.
    /// 순간이동처럼 비정상적으로 큰 값은 상한으로 자르고, 몸 정면 기준 앞뒤 이동 방향을 _gaitDirection에 기록한다.
    /// </summary>
    private void UpdateSpeed(float deltaTime)
    {
        Vector3 position = _beastRoot.position;
        Vector3 moved = Vector3.ProjectOnPlane(position - _previousPosition, Vector3.up);
        _previousPosition = position;
        float measured = Mathf.Min(MAX_MEASURED_SPEED, moved.magnitude / deltaTime);
        float forwardSpeed = Vector3.Dot(moved, _beastRoot.forward) / deltaTime;
        if (forwardSpeed < -0.3f) _gaitDirection = -1f;
        else if (forwardSpeed > 0.3f) _gaitDirection = 1f;
        // 멈출 때는 더 천천히 줄여 걸음이 점점 느려지며 서게 한다.
        float smoothing = measured >= _speed ? _speedSmoothing : _decelerationSmoothing;
        _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-smoothing * deltaTime));
    }

    /// <summary>현재 속도의 질주 비중으로 걷기와 질주 사이 한 주기 이동 거리(m)를 반환한다.</summary>
    private float GetStride()
    {
        return Mathf.Lerp(_walkStride, _gallopStride, GetGallop());
    }

    /// <summary>질주 리듬 비중(0~1)을 반환한다. _alwaysGallop이면 1, 아니면 현재 속도 _speed로 걷기와 질주 사이를 정한다.</summary>
    private float GetGallop()
    {
        return _alwaysGallop ? 1f : Mathf.InverseLerp(_gallopSpeed * 0.6f, _gallopSpeed, _speed);
    }

    /// <summary>
    /// _speed·_phase·_crouch·_leap·_bite·_tear·_recoil과 피격 시각으로 몸 전체 자세를 계산한다.
    /// 척추·머리·아래턱·귀·꼬리, 네 다리의 엉덩이·무릎·발목 회전을 시작 시 저장한 기본 자세 기준으로 덮어쓴다.
    /// </summary>
    private void ApplyPose()
    {
        float stride = Mathf.Clamp01(_speed / _fullStrideSpeed) * (1f - _leap);
        float gallop = GetGallop();
        float run = gallop * stride;
        float cycle = _phase * Mathf.PI * 2f;
        float idle = 1f - stride;
        float flinch = Mathf.Max(_recoil, _flinchAt >= 0f ? 1f - Mathf.Clamp01((Time.time - _flinchAt) / _flinchDuration) : 0f);
        float tearWave = Mathf.Sin(_idleTime * _tearFrequency * Mathf.PI * 2f);
        // 대기 동작은 멈춰 있고 웅크림·뛰어들기·물기가 아닐 때만 섞는다.
        float idleMix = idle * (1f - _crouch) * (1f - Mathf.Max(_leap, _tear));
        float scan = Smooth(_idleWeights[IDLE_SCAN]) * idleMix;
        float sniff = Smooth(_idleWeights[IDLE_SNIFF]) * idleMix;
        float shift = Smooth(_idleWeights[IDLE_SHIFT]) * idleMix;
        float growl = Smooth(_idleWeights[IDLE_GROWL]) * idleMix;
        float shiftWave = Mathf.Sin(_idleTime * 0.9f);

        // 걷기는 한 주기에 두 번 출렁이고 어깨를 굴리며, 질주는 척추를 굽혔다 펴며 앞발이 땅을 민 뒤 몸이 솟구친다.
        // 솟구치는 구간은 마지막 앞발이 떨어진 뒤부터 다음 뒷발이 닿기 전까지로, 디딤 비율이 작을수록 길어진다.
        float duty = Mathf.Lerp(_walkDuty, _gallopDuty, gallop);
        float frontLift = LAST_FRONT_PLANT + duty;
        float suspensionCenter = (frontLift + 1f) * 0.5f;
        float suspensionWidth = Mathf.Max(MIN_SUSPENSION_WIDTH, 1f - frontLift);
        float suspension = Mathf.Abs(Mathf.DeltaAngle(_phase * 360f, suspensionCenter * 360f)) / 360f;
        float bound = Mathf.Cos(Mathf.Clamp01(suspension / suspensionWidth) * Mathf.PI * 0.5f);
        float bob = (1f - Mathf.Cos(cycle * 2f)) * 0.5f * _bodyBob * stride * (1f - gallop)
            + bound * _gallopBound * run
            + Mathf.Sin(_idleTime * 2.2f) * _breathing * idle
            - _crouchDepth * Mathf.Max(_crouch, 0.6f * _tear) - _stanceDrop
            // 움직일수록 몸을 낮춰 관절을 땅 가까이 굽히고, 발 디딤 스프링으로 눌렸다 되튄다.
            - _moveLowering * stride + _springOffset - 0.03f * growl;
        float spinePitch = Mathf.Sin(cycle) * _gallopPitch * run + _crouchPitch * Mathf.Max(_crouch, _tear) + _leapPitch * _leap
            + _stancePitch + 6f * _bite - _flinchPitch * flinch + 4f * sniff;
        float spineRoll = Mathf.Sin(cycle) * _shoulderRoll * stride * (1f - gallop) + shiftWave * _shiftRoll * shift
            + tearWave * _tearShake * 0.3f * _tear;
        _spine.localPosition = _spineBasePosition + Vector3.up * bob + Vector3.right * (shiftWave * _shiftSway * shift);
        _spine.localRotation = _spineBaseRotation * Quaternion.Euler(spinePitch, 0f, spineRoll);

        // 머리는 낮게 깔고 노려보다가 질주하면 앞으로 뻗고, 뛰어들 때 내밀며, 물면 좌우로 흔들어 물어뜯는다.
        float headPitch = _stanceHeadPitch + Mathf.Sin(cycle * 2f + 0.6f) * _headNod * stride
            + _crouchPitch * _crouch + 6f * run - 8f * _bite + 10f * _tear - 12f * flinch
            + (_sniffPitch + Mathf.Sin(_idleTime * 9f) * 2f) * sniff;
        float headYaw = Mathf.Sin(_idleTime * 0.6f) * _idleScanYaw * Mathf.Max(scan, 0.3f * idleMix)
            + Mathf.Sin(cycle) * _headSway * stride * (1f - gallop) + _headTurnLag - _bodyTurnLag
            + _headLook * (1f - Mathf.Max(_bite, _tear)) + tearWave * _tearShake * _tear;
        _head.localPosition = _headBasePosition + Vector3.forward * (_biteLunge * Mathf.Max(_bite, _tear));
        _head.localRotation = _headBaseRotation * Quaternion.Euler(headPitch, headYaw, 0f);

        // 뛰어드는 동안 입을 크게 벌렸다가 물면 닫고, 물어뜯는 동안 턱을 빠르게 다문다.
        float growlJaw = _growlJaw * 0.6f * growl * (0.7f + 0.3f * Mathf.Sin(_idleTime * 23f));
        float tearJaw = _jawOpen * 0.35f * _tear * (0.5f + 0.5f * tearWave);
        float jaw = Mathf.Max(Mathf.Max(_growlJaw * _crouch, growlJaw), Mathf.Max(_jawOpen * _bite, tearJaw));
        Quaternion jawTurn = Quaternion.Euler(jaw, 0f, 0f);
        _jaw.localPosition = _jawHinge + jawTurn * (_jawBasePosition - _jawHinge);
        _jaw.localRotation = jawTurn * _jawBaseRotation;
        Quaternion earTurn = Quaternion.Euler(-_earPinBack + 15f * scan * Mathf.Sin(_idleTime * 0.6f + 1f), 0f, 0f);
        for (int i = 0; i < _ears.Length; i++)
            _ears[i].localRotation = earTurn * _earBaseRotations[i];

        // 꼬리는 평소 낮게 두고 전조 중 채찍처럼 떨며, 질주·뛰어들기하면 뒤로 쭉 편다.
        float tailYaw = Mathf.Sin(cycle) * _tailSway * stride
            + Mathf.Sin(_idleTime * 1.4f) * _tailSway * 0.4f * idle
            + Mathf.Sin(_idleTime * 14f) * _tailSway * 0.5f * Mathf.Max(Mathf.Max(_crouch, growl), _tear);
        float tailPitch = _tailRaise * Mathf.Max(run, _leap) - _stanceTailDrop * (1f - run) - 6f * _crouch;
        _tail.localRotation = _tailBaseRotation * Quaternion.Euler(tailPitch, tailYaw, 0f);

        // 멈춰 있어도 제자리에서 발을 바꿔 디디며, 무게를 옮길 때는 조금 더 들어 올린다.
        float lift = Mathf.Lerp(_walkLift, _gallopLift, gallop) * stride
            + _idleStepLift * idle * (1f - _leap) * (1f - _tear) * (1f + shift);
        // 발이 땅을 딛는 동안 몸이 나아간 만큼만 발이 뒤로 가야 미끄러지지 않는다.
        float reach = duty * GetStride() * stride;
        for (int i = 0; i < LEG_COUNT; i++)
        {
            float legPhase = Mathf.Repeat(_phase - LerpPhase(WALK_OFFSETS[i], GALLOP_OFFSETS[i], gallop), 1f);
            Vector3 paw = GetPawTarget(i, legPhase, duty, reach, lift, out float pawPitch);
            SolveLeg(i, paw, pawPitch, out float hip, out float knee, out float ankle);
            // 몸이 뜨면 앞다리는 앞으로, 뒷다리는 뒤로 쭉 뻗고 앞발 무릎을 살짝 굽혀 덮치는 자세를 섞는다.
            bool front = i < 2;
            hip = Mathf.Lerp(hip, _restHipAngles[i] + (front ? -_leapStretch : _leapStretch), _leap);
            float frontBend = front ? _kneeSigns[i] * _pounceFrontKneeBend * _bite : 0f;
            knee = Mathf.Lerp(knee, _restKneeAngles[i] + frontBend, _leap);
            ankle = Mathf.Lerp(ankle, _restAnkleAngles[i], _leap);
            _hips[i].localRotation = Quaternion.Euler(hip, 0f, 0f);
            _knees[i].localRotation = Quaternion.Euler(knee, 0f, 0f);
            _ankles[i].localRotation = Quaternion.Euler(ankle, 0f, 0f);
            NotifyPawContact(i, legPhase, duty, stride * (1f - _leap));
        }
    }

    /// <summary>0~1 비중 weight를 양 끝이 완만한 곡선으로 바꿔 반환한다. 동작이 직선적으로 켜지고 꺼지지 않게 한다.</summary>
    private static float Smooth(float weight)
    {
        return Mathf.SmoothStep(0f, 1f, weight);
    }

    /// <summary>위상 from과 to를 0~1 원 위의 가까운 쪽으로 t만큼 보간해 반환한다.</summary>
    private static float LerpPhase(float from, float to, float t)
    {
        return Mathf.Repeat(from + Mathf.DeltaAngle(from * 360f, to * 360f) / 360f * t, 1f);
    }

    /// <summary>
    /// leg 다리의 legPhase(0에서 발이 닿음)로 늑대 루트 기준 발바닥 목표 위치를 반환한다.
    /// duty 구간에는 발이 땅에 붙은 채 reach만큼 뒤로 밀리고, 나머지 구간에는 lift 높이 호를 그리며 앞으로 옮겨 디딘다.
    /// 옮기는 동안 발바닥을 접는 각도를 pawPitch로 출력한다.
    /// </summary>
    private Vector3 GetPawTarget(int leg, float legPhase, float duty, float reach, float lift, out float pawPitch)
    {
        float forward;
        float height = 0f;
        pawPitch = 0f;
        if (legPhase < duty)
        {
            forward = reach * (0.5f - legPhase / duty);
        }
        else
        {
            float swing = (legPhase - duty) / (1f - duty);
            forward = reach * (Mathf.SmoothStep(0f, 1f, swing) - 0.5f);
            height = lift * Mathf.Sin(swing * Mathf.PI);
            pawPitch = -_swingPawCurl * Mathf.Sin(swing * Mathf.PI) * Mathf.Clamp01(lift * 20f);
        }
        return _restPaws[leg] + new Vector3(0f, height, forward);
    }

    /// <summary>
    /// 늑대 루트 기준 발바닥 위치 paw에 발이 닿도록 척추 기준 앞뒤 평면에서 두 마디 다리를 풀어
    /// 엉덩이·무릎 각도와 발바닥이 pawPitch만큼 기울도록 하는 발목 각도(도)를 출력한다. 닿지 않으면 다리를 곧게 뻗는다.
    /// </summary>
    private void SolveLeg(int leg, Vector3 paw, float pawPitch, out float hip, out float knee, out float ankle)
    {
        Vector3 local = _spine.InverseTransformPoint(transform.TransformPoint(paw));
        float pitch = pawPitch * Mathf.Deg2Rad;
        Vector2 offset = _pawOffsets[leg];
        // 발바닥 위치에서 기울어진 발 길이만큼 되돌아가 발목 목표를 구한다.
        Vector2 ankleTarget = new Vector2(local.y, local.z) - new Vector2(
            offset.x * Mathf.Cos(pitch) - offset.y * Mathf.Sin(pitch),
            offset.x * Mathf.Sin(pitch) + offset.y * Mathf.Cos(pitch));
        Vector3 hipPosition = _hips[leg].localPosition;
        Vector2 target = ankleTarget - new Vector2(hipPosition.y, hipPosition.z);
        float thigh = _thighLengths[leg];
        float shin = _shinLengths[leg];
        float distance = Mathf.Clamp(SoftenReach(target.magnitude, thigh + shin),
            Mathf.Abs(thigh - shin) + 0.01f, thigh + shin - 0.001f);
        float direction = Mathf.Atan2(-target.y, -target.x);
        float spread = Mathf.Acos(Mathf.Clamp((thigh * thigh + distance * distance - shin * shin) / (2f * thigh * distance), -1f, 1f));
        float inner = Mathf.Acos(Mathf.Clamp((thigh * thigh + shin * shin - distance * distance) / (2f * thigh * shin), -1f, 1f));
        float sign = _kneeSigns[leg];
        hip = (direction - sign * spread) * Mathf.Rad2Deg;
        knee = sign * (Mathf.PI - inner) * Mathf.Rad2Deg;
        ankle = pawPitch - hip - knee;
    }

    /// <summary>
    /// 엉덩이에서 발목 목표까지의 거리 distance가 다리 길이 legLength의 _softReachStart 비율을 넘으면
    /// 남은 길이에 지수 곡선으로 다가가게 줄여 반환한다. 다리가 다 펴지는 순간 무릎 각도가 갑자기 꺾이지 않게 한다.
    /// </summary>
    private float SoftenReach(float distance, float legLength)
    {
        float soft = legLength * _softReachStart;
        if (distance <= soft)
            return distance;
        float range = legLength - soft;
        return soft + range * (1f - Mathf.Exp(-(distance - soft) / range));
    }

    /// <summary>
    /// leg 다리의 이번 위상 legPhase가 발이 닿는 순간을 지나면 걸음 비중 stride만큼 몸을 눌러 스프링 반발을 일으킨다.
    /// 앞으로 걸을 때는 위상이 한 바퀴 돌아 0을, 뒷걸음질할 때는 거꾸로 돌며 디딤 비율 duty를 지나는 순간이 발이 닿는 때다.
    /// 지난 위상은 _previousLegPhases에 저장한다.
    /// </summary>
    private void NotifyPawContact(int leg, float legPhase, float duty, float stride)
    {
        float previous = _previousLegPhases[leg];
        _previousLegPhases[leg] = legPhase;
        bool planted = _gaitDirection > 0f
            ? previous - legPhase >= 0.5f
            : previous >= duty && legPhase < duty && previous - legPhase < 0.5f;
        if (!planted) return;
        // 제자리 발 바꿈도 약하게 눌러 숨 쉬듯 출렁이게 한다.
        _springVelocity -= _contactImpulse * Mathf.Max(stride, 0.15f);
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
