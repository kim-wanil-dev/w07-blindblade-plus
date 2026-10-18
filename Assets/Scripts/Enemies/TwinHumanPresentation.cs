using UnityEngine;

/// <summary>
/// 인간 보스의 도형 몸체를 Play 중 숨겨 연기만 보이게 하고,
/// 사격을 준비할 때마다 자기 위치 주변 시야를 잠깐 드러낸다.
/// 스테이지1 보스처럼 피격 후 받아칠 수 있는 무적 방어 상태 동안 플레이어 쪽에 가드 자세 카타나를 띄운다.
/// 몸체 연기는 움직이면 유체처럼 뒤로 밀리고 멈추면 출렁였다 잦아들며, 늑대에 타면 흘러내리지 않고 매달린 듯 뒤로 길게 휘날린다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack))]
public sealed class TwinHumanPresentation : MonoBehaviour
{
    private const string GUARD_SCABBARD_NAME = "Saya";
    private const float MAX_SWAY_DELTA_TIME = 0.05f;
    private static readonly int _guardHoldStateId = Animator.StringToHash("Base Layer.GuardHold");

    [Header("외형")]
    [SerializeField, Tooltip("편집 화면에서만 보이고 Play 중에는 숨겨 연기 방출 모양으로만 쓰는 도형 렌더러")]
    private Renderer[] _bodyRenderers;

    [Header("사격 준비 공개")]
    [SerializeField, Min(0f), Tooltip("사격 준비마다 보스 위치에서 드러내는 시야 반경(미터)")]
    private float _rangedRevealRadius = 5f;
    private HumanBossAttack _attack;
    private SensoryPerceptionMask _perceptionMask;

    [Header("방어 자세 칼")]
    [SerializeField, Tooltip("방어 상태 동안 인간 앞에 띄울 플레이어 카타나 모형(KatanaRig). 스테이지1 보스와 같다.")]
    private GameObject _guardWeaponPrefab;
    [SerializeField, Tooltip("플레이어와 같은 가드 자세 상태(GuardHold)를 가진 카타나 Animator Controller")]
    private RuntimeAnimatorController _guardWeaponController;
    [SerializeField, Tooltip("플레이어를 향한 방향 기준 카타나 모형의 위치(미터). x는 오른쪽, y는 높이, z는 플레이어 쪽으로 내미는 거리다.")]
    private Vector3 _guardWeaponOffset = new Vector3(0f, 1.6f, 0.6f);
    [SerializeField, Min(0.01f), Tooltip("카타나 모형 크기 배율. 1인칭용 모형이라 멀리서 보이도록 키운다.")]
    private float _guardWeaponScale = 1.5f;
    private TwinBossController _controller;
    private GameObject _guardWeapon;
    private Animator _guardWeaponAnimator;

    [Header("연기 결 휘날림")]
    [SerializeField, Tooltip("인간 몸체 연기(EnemySmokeVisual) 루트. 움직임에 따라 아래 연기 파티클이 유체처럼 밀리고 출렁인다.")]
    private Transform _smokeRoot;
    [SerializeField, Min(0f), Tooltip("인간이 1m/s 움직일 때 연기가 몸에 비해 반대쪽으로 밀리는 속도(미터/초). 1이면 잡몹 꼬리 연기처럼 지나온 자리에 그대로 남는다.")]
    private float _swayScale = 2.25f;
    [SerializeField, Min(0f), Tooltip("연기가 몸에 비해 밀리는 속도 최대값(미터/초). 빠른 돌진에서 연기가 몸을 떠나지 않게 한다.")]
    private float _maxSway = 6f;
    [SerializeField, Min(0.01f), Tooltip("연기가 밀렸다 되돌아오며 출렁이는 빠르기(초당 횟수)")]
    private float _swayFrequency = 1.2f;
    [SerializeField, Range(0.05f, 1f), Tooltip("출렁임이 잦아드는 정도. 작을수록 멈춘 뒤 앞뒤로 더 오래 출렁이고 1이면 출렁이지 않는다.")]
    private float _swayDamping = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("한 프레임에 이보다 멀리 옮겨지면 순간이동으로 보고 움직임에 넣지 않는다(미터)")]
    private float _teleportThreshold = 3f;
    [SerializeField, Range(0f, 1f), Tooltip("늑대에 탔을 때 연기를 아래로 끌어내리는 중력에 곱하는 배율. 작을수록 결이 기울어진 몸을 따라 곧게 날린다.")]
    private float _mountedFallScale = 0.15f;
    [SerializeField, Min(0f), Tooltip("늑대에 탔을 때 연기 흔들림 세기에 곱하는 배율")]
    private float _mountedFlutterScale = 1.8f;
    private ParticleSystem[] _smokes;
    private SmokeFlow[] _smokeFlows;
    private Vector3 _previousPosition;
    private Vector3 _moveVelocity;
    private Vector3 _sway;
    private Vector3 _swayVelocity;

    private struct SmokeFlow
    {
        public Vector3 Velocity;
        public float Gravity;
        public float Flutter;
        public bool World;
    }

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        _controller = GetComponentInParent<TwinBossController>();
        foreach (Renderer body in _bodyRenderers)
            body.enabled = false;
        CreateGuardWeapon();
        StoreSmokeFlows();
    }

    void OnEnable()
    {
        _attack.ShotPreparing += HandleShotPreparing;
    }

    void OnDisable()
    {
        _attack.ShotPreparing -= HandleShotPreparing;
        if (_guardWeapon != null) _guardWeapon.SetActive(false);
    }

    void LateUpdate()
    {
        UpdateGuardWeapon();
        UpdateSmokeFlow(Mathf.Min(Time.deltaTime, MAX_SWAY_DELTA_TIME));
    }

    void OnDrawGizmos()
    {
        // Play 중 숨긴 도형을 Scene 뷰에서만 확인할 수 있게 한다.
        SceneShapeGizmo.Draw(_bodyRenderers);
    }

    /// <summary>
    /// _guardWeaponPrefab을 인간 자식으로 한 번 복제해 _guardWeaponScale 크기로 두고 검집 렌더러를 끈 뒤 숨긴다.
    /// 복제본 Animator에 _guardWeaponController를 연결하며 위치와 방향은 표시 중에 갱신한다.
    /// </summary>
    private void CreateGuardWeapon()
    {
        _guardWeapon = Instantiate(_guardWeaponPrefab, transform);
        _guardWeapon.name = "TwinHumanGuardKatana";
        _guardWeapon.transform.localScale = Vector3.one * _guardWeaponScale;
        _guardWeaponAnimator = _guardWeapon.GetComponentInChildren<Animator>(true);
        _guardWeaponAnimator.runtimeAnimatorController = _guardWeaponController;
        // 가드 자세는 칼만 보이도록 검집 렌더러를 끈다.
        Transform scabbard = _guardWeapon.transform.Find(GUARD_SCABBARD_NAME);
        if (scabbard != null)
            foreach (Renderer part in scabbard.GetComponentsInChildren<Renderer>(true))
                part.enabled = false;
        _guardWeapon.SetActive(false);
    }

    /// <summary>
    /// 인간이 무적인 동안(TwinBossController.IsHumanDefending, 무적은 곧 받아칠 수 있는 방어 상태다) 카타나 모형을 켜고 GuardHold 자세를 재생하며,
    /// 무적이 끝나면(쳐내 반격을 시작하면 무적도 바로 끝난다) 바로 숨긴다. 켜져 있는 동안 매 프레임 플레이어를 향하도록 _guardWeaponOffset 위치와 회전을 갱신한다.
    /// </summary>
    private void UpdateGuardWeapon()
    {
        bool defending = _controller.IsHumanDefending;
        if (_guardWeapon.activeSelf != defending)
        {
            _guardWeapon.SetActive(defending);
            if (defending) _guardWeaponAnimator.Play(_guardHoldStateId, 0, 0f);
        }
        if (!defending) return;
        Vector3 facing = Game.Player != null
            ? Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up) : Vector3.zero;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Quaternion rotation = Quaternion.LookRotation(facing.normalized);
        _guardWeapon.transform.SetPositionAndRotation(transform.position + rotation * _guardWeaponOffset, rotation);
    }

    /// <summary>
    /// _smokeRoot 아래 연기 파티클마다 원래 흐름 속도·중력·흔들림 세기와 월드 공간 시뮬레이션 여부를 _smokeFlows에 저장하고
    /// 움직임 측정 시작 위치를 _previousPosition에 기록한다.
    /// </summary>
    private void StoreSmokeFlows()
    {
        _smokes = _smokeRoot.GetComponentsInChildren<ParticleSystem>(true);
        _smokeFlows = new SmokeFlow[_smokes.Length];
        for (int i = 0; i < _smokes.Length; i++)
        {
            ParticleSystem.MainModule main = _smokes[i].main;
            ParticleSystem.VelocityOverLifetimeModule velocity = _smokes[i].velocityOverLifetime;
            _smokeFlows[i] = new SmokeFlow
            {
                Velocity = new Vector3(velocity.xMultiplier, velocity.yMultiplier, velocity.zMultiplier),
                Gravity = main.gravityModifierMultiplier,
                Flutter = _smokes[i].noise.strengthMultiplier,
                World = main.simulationSpace == ParticleSystemSimulationSpace.World
            };
        }
        _previousPosition = transform.position;
    }

    /// <summary>
    /// deltaTime 동안 인간이 수평으로 움직인 속도의 _swayScale배만큼 반대쪽을 목표로, 연기가 몸에 비해 밀리는 속도 _sway를
    /// _swayFrequency·_swayDamping 스프링으로 따라가게 해 움직이면 뒤로 밀리고 멈추면 앞으로 출렁였다 잦아들게 한다.
    /// 몸을 따라다니는 연기에는 _sway를, 지나온 자리에 남는 월드 연기에는 이미 남는 만큼을 뺀 값을 결 흐름 속도에 더한다.
    /// 연기 루트를 몸체 탑승 기울기(HumanBossAttack.BodyLean)대로 돌려 결 흐름이 누운 몸을 따르게 하고,
    /// 늑대에 타면 중력에 _mountedFallScale, 흔들림에 _mountedFlutterScale을 곱한다. _sway·_swayVelocity·_moveVelocity를 갱신한다.
    /// </summary>
    private void UpdateSmokeFlow(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        Vector3 position = transform.position;
        Vector3 displacement = Vector3.ProjectOnPlane(position - _previousPosition, Vector3.up);
        _previousPosition = position;
        // 물속 순간이동이나 관통 베기처럼 한 번에 멀리 옮겨진 프레임은 직전 움직임을 그대로 쓴다.
        if (displacement.sqrMagnitude <= _teleportThreshold * _teleportThreshold) _moveVelocity = displacement / deltaTime;

        // 감쇠 스프링이라 몸이 멈춰도 연기가 관성으로 앞으로 넘어갔다가 되돌아오며 잦아든다.
        float frequency = _swayFrequency * Mathf.PI * 2f;
        Vector3 target = Vector3.ClampMagnitude(-_moveVelocity * _swayScale, _maxSway);
        _swayVelocity += ((target - _sway) * frequency * frequency - _swayVelocity * (2f * _swayDamping * frequency)) * deltaTime;
        _sway = Vector3.ClampMagnitude(_sway + _swayVelocity * deltaTime, _maxSway);

        // 늑대 위에 사선으로 누우면 연기 결도 몸을 따라 기울어 목 쪽에서 몸 끝 뒤로 흘러간다.
        _smokeRoot.localRotation = _attack.BodyLean;
        bool mounted = _attack.IsMounted;
        float fall = mounted ? _mountedFallScale : 1f;
        float flutter = mounted ? _mountedFlutterScale : 1f;
        for (int i = 0; i < _smokes.Length; i++)
        {
            SmokeFlow flow = _smokeFlows[i];
            // 월드 연기는 가만히 있어도 몸이 지나간 만큼 뒤에 남으므로 그만큼은 더하지 않고, 빠른 돌진에서는 잡몹처럼 그대로 남는다.
            Vector3 sway = flow.World ? _sway + Vector3.ClampMagnitude(_moveVelocity, _maxSway) : _sway;
            Vector3 localSway = _smokes[i].transform.InverseTransformDirection(sway);
            ParticleSystem.VelocityOverLifetimeModule smokeVelocity = _smokes[i].velocityOverLifetime;
            smokeVelocity.xMultiplier = flow.Velocity.x + localSway.x;
            smokeVelocity.yMultiplier = flow.Velocity.y + localSway.y;
            smokeVelocity.zMultiplier = flow.Velocity.z + localSway.z;
            ParticleSystem.MainModule main = _smokes[i].main;
            main.gravityModifierMultiplier = flow.Gravity * fall;
            ParticleSystem.NoiseModule noise = _smokes[i].noise;
            noise.strengthMultiplier = flow.Flutter * flutter;
        }
    }

    /// <summary>사격 준비 사건마다 현재 위치에서 _rangedRevealRadius 반경의 시야를 공개한다.</summary>
    private void HandleShotPreparing()
    {
        if (_perceptionMask == null)
            _perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        _perceptionMask.RevealFootstep(transform.position, _rangedRevealRadius);
    }
}
