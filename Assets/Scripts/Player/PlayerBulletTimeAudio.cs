using UnityEngine;

/// <summary>
/// 퍼펙트 패링 뒤 강화 준비와 강화 공격(암전·납도) 동안 효과음·BGM 버스의 소리를 느리고 낮고 먹먹하게 바꾼다.
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
    [SerializeField, Range(100f, 5000f), Tooltip("불릿 타임 동안 이 주파수(Hz) 위를 걸러 먹먹하게 한다.")]
    private float _lowpassCutoff = 800f;
    [SerializeField, Min(0f), Tooltip("퍼펙트 패링 순간 효과가 걸리는 실시간(초).")]
    private float _enterDuration = 0.1f;
    [SerializeField, Min(0f), Tooltip("강화 준비·공격이 끝나 원래 소리로 돌아오는 실시간(초).")]
    private float _exitDuration = 0.3f;
    private PlayerEmpower _empower;
    private float _weight;
    private bool _applied;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
    }

    void OnDisable()
    {
        _weight = 0f;
        if (_applied && Managers.IsInitialized)
            Managers.Sound.SetTimeWarp(1f, 0f);
        _applied = false;
    }

    void Update()
    {
        bool warped = _empower.IsReady || _empower.IsStriking;
        float duration = warped ? _enterDuration : _exitDuration;
        float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
        _weight = Mathf.MoveTowards(_weight, warped ? 1f : 0f, step);
        ApplyWarp();
    }

    /// <summary>
    /// _weight로 재생 배율과 저역 통과 주파수를 원래 값과 설정값 사이에서 보간해 SoundManager에 적용한다.
    /// 주파수는 로그 보간해 귀에 고르게 줄어들게 하며, 효과가 꺼진 상태가 이어지면 다시 호출하지 않는다.
    /// </summary>
    private void ApplyWarp()
    {
        if (!_applied && _weight <= 0f)
            return;
        float pitch = Mathf.Lerp(1f, _pitch, _weight);
        float cutoff = _weight > 0f
            ? Mathf.Exp(Mathf.Lerp(Mathf.Log(FULL_BAND_FREQUENCY), Mathf.Log(_lowpassCutoff), _weight))
            : 0f;
        Managers.Sound.SetTimeWarp(pitch, cutoff);
        _applied = _weight > 0f;
    }
}
