using UnityEngine;

/// <summary>
/// 늑대 몸체 부위별 연기의 밀도·크기·길이·수명·결 속도와 바람 흔들림을 한곳에서 조정한다.
/// 각 부위 파티클의 기본값을 저장해 두고 배율을 곱해 적용하며 Play 중 값을 바꾸면 바로 반영한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class TwinBeastSmokeTuning : MonoBehaviour
{
    private const float PARTICLE_HEADROOM = 1.2f;

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
    }

    void Awake()
    {
        _smokes = GetComponentsInChildren<ParticleSystem>(true);
        _renderers = new ParticleSystemRenderer[_smokes.Length];
        _bases = new SmokeBase[_smokes.Length];
        for (int i = 0; i < _smokes.Length; i++)
        {
            ParticleSystem.MainModule main = _smokes[i].main;
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
                MaxParticles = main.maxParticles
            };
        }
        Apply();
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
}
