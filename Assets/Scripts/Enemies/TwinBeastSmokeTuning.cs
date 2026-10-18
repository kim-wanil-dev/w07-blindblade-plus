using UnityEngine;

/// <summary>
/// 늑대 몸체 부위별 연기의 밀도·크기·길이·수명·결 속도와 바람 흔들림을 한곳에서 조정한다.
/// 각 부위 파티클의 기본값을 저장해 두고 배율을 곱해 적용하며 Play 중 값을 바꾸면 바로 반영한다.
/// 인간 연기처럼 늑대가 움직이면 털이 유체처럼 뒤로 밀리고 멈추면 출렁였다 잦아든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBeastSmokeTuning : MonoBehaviour
{
    private const float PARTICLE_HEADROOM = 1.2f;
    private const float MAX_SWAY_DELTA_TIME = 0.05f;

    [Header("연기 조정")]
    [SerializeField, Min(0f), Tooltip("부위별 초당 방출 수에 곱하는 배율")]
    private float _densityScale = 3f;
    [SerializeField, Min(0f), Tooltip("부위별 가닥 굵기(시작 크기)에 곱하는 배율")]
    private float _sizeScale = 4f;
    [SerializeField, Min(0f), Tooltip("가닥을 늘이는 길이(Length Scale)에 곱하는 배율")]
    private float _lengthScale = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("가닥이 살아 있는 시간에 곱하는 배율")]
    private float _lifetimeScale = 1f;
    [SerializeField, Min(0f), Tooltip("결 방향으로 흐르는 속도에 곱하는 배율")]
    private float _flowSpeedScale = 1f;
    [SerializeField, Min(0f), Tooltip("결대로 흐르던 가닥이 끝으로 갈수록 위로 타오르게 하는 위쪽 가속도(m/s²). 0이면 끈다.")]
    private float _riseForce = 3f;
    private ParticleSystem[] _smokes;
    private ParticleSystemRenderer[] _renderers;
    private SmokeBase[] _bases;

    [Header("바람 흔들림")]
    [SerializeField, Min(0f), Tooltip("표면 바깥(법선)으로 밀어내는 시작 속도 배율. 0이면 바람 방향 결만 남는다.")]
    private float _surfacePushScale = 0f;
    [SerializeField, Min(0f), Tooltip("털이 바람에 펄럭이는 흔들림 세기. 0이면 끈다.")]
    private float _flutterStrength = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("흔들림 무늬의 촘촘함. 클수록 잘게 떨린다.")]
    private float _flutterFrequency = 1.5f;
    [SerializeField, Min(0f), Tooltip("흔들림 무늬가 시간에 따라 흘러가는 속도")]
    private float _flutterScrollSpeed = 1.2f;

    [Header("움직임 휘날림")]
    [SerializeField, Tooltip("켜면 모든 털 연기를 월드 공간으로 계산해 이미 나온 연기가 몸을 따라오지 않고 지나온 자리에 남는다.")]
    private bool _worldSpace = true;
    [SerializeField, Min(0f), Tooltip("늑대가 1m/s 움직일 때 털이 몸에 비해 반대쪽으로 밀리는 속도(미터/초). 0이면 끈다.")]
    private float _swayScale = 2.25f;
    [SerializeField, Min(0f), Tooltip("털이 몸에 비해 밀리는 속도 최대값(미터/초). 빠른 질주에서 털이 몸을 떠나지 않게 한다.")]
    private float _maxSway = 6f;
    [SerializeField, Min(0.01f), Tooltip("털이 밀렸다 되돌아오며 출렁이는 빠르기(초당 횟수)")]
    private float _swayFrequency = 1.2f;
    [SerializeField, Range(0.05f, 1f), Tooltip("출렁임이 잦아드는 정도. 작을수록 멈춘 뒤 앞뒤로 더 오래 출렁이고 1이면 출렁이지 않는다.")]
    private float _swayDamping = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("한 프레임에 이보다 멀리 옮겨지면 순간이동으로 보고 움직임에 넣지 않는다(미터)")]
    private float _teleportThreshold = 3f;
    private Vector3 _previousPosition;
    private Vector3 _moveVelocity;
    private Vector3 _sway;
    private Vector3 _swayVelocity;

    private struct SmokeBase
    {
        public float Rate;
        public float MinSpeed;
        public float MaxSpeed;
        public float MinSize;
        public float MaxSize;
        public float MinLifetime;
        public float MaxLifetime;
        public Vector3 Velocity;
        public float LengthScale;
        public int MaxParticles;
        public bool World;
    }

    void Awake()
    {
        _smokes = GetComponentsInChildren<ParticleSystem>(true);
        _renderers = new ParticleSystemRenderer[_smokes.Length];
        _bases = new SmokeBase[_smokes.Length];
        for (int i = 0; i < _smokes.Length; i++)
        {
            ParticleSystem.MainModule main = _smokes[i].main;
            // 몸을 따라 끌려다니던 잔상이 지나온 자리에 남도록 월드 공간으로 바꾼다.
            if (_worldSpace) main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.VelocityOverLifetimeModule velocity = _smokes[i].velocityOverLifetime;
            _renderers[i] = _smokes[i].GetComponent<ParticleSystemRenderer>();
            _bases[i] = new SmokeBase
            {
                Rate = _smokes[i].emission.rateOverTimeMultiplier,
                MinSpeed = main.startSpeed.constantMin,
                MaxSpeed = main.startSpeed.constantMax,
                MinSize = main.startSize.constantMin,
                MaxSize = main.startSize.constantMax,
                MinLifetime = main.startLifetime.constantMin,
                MaxLifetime = main.startLifetime.constantMax,
                Velocity = new Vector3(velocity.xMultiplier, velocity.yMultiplier, velocity.zMultiplier),
                LengthScale = _renderers[i].lengthScale,
                MaxParticles = main.maxParticles,
                World = main.simulationSpace == ParticleSystemSimulationSpace.World
            };
        }
        _previousPosition = transform.position;
        Apply();
    }

    void LateUpdate()
    {
        UpdateSway(Mathf.Min(Time.deltaTime, MAX_SWAY_DELTA_TIME));
    }

    void OnValidate()
    {
        // Play 중 Inspector에서 값을 바꾸면 기본값이 저장된 뒤에만 다시 적용한다.
        if (Application.isPlaying && _bases != null)
            Apply();
    }

    /// <summary>
    /// 저장한 부위별 기본값에 밀도·크기·길이·수명·결 속도·표면 밀어내기 배율을 곱하고
    /// _riseForce 위쪽 가속도와 바람 흔들림 노이즈를 모든 연기 파티클에 적용한다.
    /// 늘어난 방출 수와 수명에 맞춰 최대 입자 수도 함께 늘려 방출이 잘리지 않게 한다.
    /// </summary>
    private void Apply()
    {
        for (int i = 0; i < _smokes.Length; i++)
        {
            SmokeBase smoke = _bases[i];
            ParticleSystem.MainModule main = _smokes[i].main;
            ParticleSystem.EmissionModule emission = _smokes[i].emission;
            ParticleSystem.VelocityOverLifetimeModule velocity = _smokes[i].velocityOverLifetime;
            float rate = smoke.Rate * _densityScale;
            float maxLifetime = smoke.MaxLifetime * _lifetimeScale;
            emission.rateOverTimeMultiplier = rate;
            // 표면 법선 방향 시작 속도는 구 아래쪽에서 털을 아래로 뻗게 하므로 기본으로 끈다.
            main.startSpeed = new ParticleSystem.MinMaxCurve(smoke.MinSpeed * _surfacePushScale,
                smoke.MaxSpeed * _surfacePushScale);
            main.startSize = new ParticleSystem.MinMaxCurve(smoke.MinSize * _sizeScale, smoke.MaxSize * _sizeScale);
            main.startLifetime = new ParticleSystem.MinMaxCurve(smoke.MinLifetime * _lifetimeScale, maxLifetime);
            main.maxParticles = Mathf.Max(smoke.MaxParticles, Mathf.CeilToInt(rate * maxLifetime * PARTICLE_HEADROOM));
            velocity.xMultiplier = smoke.Velocity.x * _flowSpeedScale;
            velocity.yMultiplier = smoke.Velocity.y * _flowSpeedScale;
            velocity.zMultiplier = smoke.Velocity.z * _flowSpeedScale;
            // 처음에는 결 방향 속도가 앞서고 시간이 지날수록 위쪽 가속이 커져 가닥 끝이 위로 휜다.
            ParticleSystem.ForceOverLifetimeModule force = _smokes[i].forceOverLifetime;
            force.enabled = _riseForce > 0f;
            force.yMultiplier = _riseForce;
            ParticleSystem.NoiseModule noise = _smokes[i].noise;
            noise.enabled = _flutterStrength > 0f;
            noise.strengthMultiplier = _flutterStrength;
            noise.frequency = _flutterFrequency;
            noise.scrollSpeedMultiplier = _flutterScrollSpeed;
            _renderers[i].lengthScale = smoke.LengthScale * _lengthScale;
        }
    }

    /// <summary>
    /// deltaTime 동안 늑대가 수평으로 움직인 속도의 _swayScale배만큼 반대쪽을 목표로, 털이 몸에 비해 밀리는 속도 _sway를
    /// _swayFrequency·_swayDamping 스프링으로 따라가게 해 달리면 털이 뒤로 밀리고 멈추면 앞으로 출렁였다 잦아들게 한다.
    /// 부위마다 결 방향 속도(기본값 × _flowSpeedScale)에 _sway를 그 부위 로컬 방향으로 더하고, 지나온 자리에 남는 월드 연기는 이미 남는 만큼을 뺀다.
    /// _sway·_swayVelocity·_moveVelocity를 갱신한다.
    /// </summary>
    private void UpdateSway(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        Vector3 position = transform.position;
        Vector3 displacement = Vector3.ProjectOnPlane(position - _previousPosition, Vector3.up);
        _previousPosition = position;
        // 잠수 뒤 다른 곳에서 나오는 것처럼 한 번에 멀리 옮겨진 프레임은 직전 움직임을 그대로 쓴다.
        if (displacement.sqrMagnitude <= _teleportThreshold * _teleportThreshold) _moveVelocity = displacement / deltaTime;

        // 감쇠 스프링이라 늑대가 멈춰도 털이 관성으로 앞으로 넘어갔다가 되돌아오며 잦아든다.
        float frequency = _swayFrequency * Mathf.PI * 2f;
        Vector3 target = Vector3.ClampMagnitude(-_moveVelocity * _swayScale, _maxSway);
        _swayVelocity += ((target - _sway) * frequency * frequency - _swayVelocity * (2f * _swayDamping * frequency)) * deltaTime;
        _sway = Vector3.ClampMagnitude(_sway + _swayVelocity * deltaTime, _maxSway);

        for (int i = 0; i < _smokes.Length; i++)
        {
            Vector3 sway = _bases[i].World ? _sway + Vector3.ClampMagnitude(_moveVelocity, _maxSway) : _sway;
            Vector3 localSway = _smokes[i].transform.InverseTransformDirection(sway);
            Vector3 flow = _bases[i].Velocity * _flowSpeedScale;
            ParticleSystem.VelocityOverLifetimeModule velocity = _smokes[i].velocityOverLifetime;
            velocity.xMultiplier = flow.x + localSway.x;
            velocity.yMultiplier = flow.y + localSway.y;
            velocity.zMultiplier = flow.z + localSway.z;
        }
    }
}
