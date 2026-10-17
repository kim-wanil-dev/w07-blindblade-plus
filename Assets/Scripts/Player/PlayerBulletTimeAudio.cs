using UnityEngine;

/// <summary>
/// 퍼펙트 패링 뒤 강화 준비와 강화 공격(암전·납도) 동안 효과음·BGM 버스의 소리를 느리고 낮고 먹먹하게 바꾼다.
/// 궁극기 차징 중에는 세계가 더 느려지는 진행도를 따라 소리도 더 느려진다.
/// 암전음 같은 연출음은 연출음 버스로 나가 영향을 받지 않으며, 강화가 끝나면 서서히 원래 소리로 되돌린다.
/// 전환 시간은 불릿 타임에 늘어나지 않도록 일시정지를 제외한 실시간으로 잰다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerEmpower))]
public sealed class PlayerBulletTimeAudio : MonoBehaviour
{
    private const float FULL_BAND_FREQUENCY = 22000f;

    [Header("불릿 타임 소리")]
    [SerializeField, Range(0.25f, 1f), Tooltip("불릿 타임 동안 모든 소리의 재생 배율. 낮을수록 느리고 음높이도 낮아진다.")]
    private float _pitch = 0.6f;
    [SerializeField, Range(0.25f, 1f), Tooltip("궁극기 차징으로 세계가 더 느려질 때 함께 내려가 닿는 재생 배율. 세계 감속과 같은 진행도로 바뀐다. SoundManager가 0.25 아래는 자른다.")]
    private float _chargePitch = 0.3f;
    [SerializeField, Range(100f, 5000f), Tooltip("불릿 타임 동안 이 주파수(Hz) 위를 걸러 먹먹하게 한다.")]
    private float _lowpassCutoff = 800f;
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간 효과가 걸리는 실시간(초).")]
    private float _enterDuration = 0.1f;
    [SerializeField, Min(0f), Tooltip("강화 준비·공격이 끝나 원래 소리로 돌아오는 실시간(초).")]
    private float _exitDuration = 0.3f;
    private PlayerEmpower _empower;
    private float _weight;
    private float _chargeWeight;
    private float _externalWeight;
    private float _externalCinematicWeight;
    private bool _applied;
    private bool _cinematicApplied;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
    }

    void OnDisable()
    {
        _weight = 0f;
        _chargeWeight = 0f;
        _externalWeight = 0f;
        _externalCinematicWeight = 0f;
        if (_applied && Managers.IsInitialized)
            Managers.Sound.SetTimeWarp(1f, 0f);
        if (_cinematicApplied && Managers.IsInitialized)
            Managers.Sound.SetCinematicTimeWarp(1f, 0f);
        _applied = false;
        _cinematicApplied = false;
    }

    void Update()
    {
        bool warped = _empower.IsReady || _empower.IsStriking;
        float duration = warped ? _enterDuration : _exitDuration;
        float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
        _weight = Mathf.MoveTowards(_weight, warped ? 1f : 0f, step);
        // 차징 감속은 세계 감속을 그대로 따라가고, 차징이 끝나면 복귀 시간에 걸쳐 풀어 음높이가 튀지 않게 한다.
        float chargeTarget = Mathf.SmoothStep(0f, 1f, _empower.ChargeSlowProgress);
        float exitStep = _exitDuration > 0f ? Time.unscaledDeltaTime / _exitDuration : 1f;
        _chargeWeight = chargeTarget >= _chargeWeight ? chargeTarget : Mathf.MoveTowards(_chargeWeight, chargeTarget, exitStep);
        ApplyWarp();
    }

    /// <summary>
    /// 보스 반격 감속처럼 강화 밖의 연출이 weight(0~1)만큼 효과음·BGM에, cinematicWeight(0~1)만큼 연출음에 불릿 타임 소리를 걸도록 요청하고 바로 적용한다.
    /// 효과음·BGM은 강화 소리와 겹치면 더 강한 쪽을 쓰며 0이면 요청을 해제한다. 페이드는 호출자가 비중으로 조절한다.
    /// </summary>
    public void SetExternalWarp(float weight, float cinematicWeight = 0f)
    {
        _externalWeight = Mathf.Clamp01(weight);
        _externalCinematicWeight = Mathf.Clamp01(cinematicWeight);
        ApplyWarp();
    }

    /// <summary>
    /// _weight와 _externalWeight 중 큰 비중으로 재생 배율과 저역 통과 주파수를 원래 값과 설정값 사이에서 보간해 SoundManager에 적용한다.
    /// 궁극기 차징 중이면 불릿 타임 배율을 _chargeWeight만큼 _chargePitch 쪽으로 더 낮춘다.
    /// 연출음 버스에는 _externalCinematicWeight만 같은 방식으로 적용하며, 효과가 꺼진 상태가 이어지면 다시 호출하지 않는다.
    /// </summary>
    private void ApplyWarp()
    {
        float weight = Mathf.Max(_weight, _externalWeight);
        if (_applied || weight > 0f)
        {
            float warpedPitch = Mathf.Lerp(_pitch, _chargePitch, _chargeWeight);
            Managers.Sound.SetTimeWarp(Mathf.Lerp(1f, warpedPitch, weight), GetCutoff(weight));
            _applied = weight > 0f;
        }
        if (_cinematicApplied || _externalCinematicWeight > 0f)
        {
            Managers.Sound.SetCinematicTimeWarp(Mathf.Lerp(1f, _pitch, _externalCinematicWeight),
                GetCutoff(_externalCinematicWeight));
            _cinematicApplied = _externalCinematicWeight > 0f;
        }
    }

    /// <summary>weight(0~1)로 전 대역과 _lowpassCutoff 사이를 로그 보간한 저역 통과 주파수를 반환한다. weight가 0이면 필터 해제 값 0을 반환한다.</summary>
    private float GetCutoff(float weight)
    {
        return weight > 0f
            ? Mathf.Exp(Mathf.Lerp(Mathf.Log(FULL_BAND_FREQUENCY), Mathf.Log(_lowpassCutoff), weight))
            : 0f;
    }
}
