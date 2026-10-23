using UnityEngine;

/// <summary>
/// 플레이어 이동·모션·전투 판정·무적·스태미너가 함께 쓰는 시계다.
/// 평소에는 게임 시간과 같고, 강화 불릿 타임 중에는 PlayerEmpower의 플레이어 배율만큼 세계보다 덜 느리게 흐른다.
/// 일섬 집중 중에는 일섬 전용 플레이어 배율로 느려지고, 일섬 타격 정지 중에는 세계와 함께 거의 멈춘다.
/// 집중이 끝난 뒤 남은 검기 발사 연출 동안에는 실시간으로 흐른다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerEmpower))]
[RequireComponent(typeof(PlayerSwordWaveLauncher))]
public sealed class PlayerClock : MonoBehaviour
{
    [Header("상태")]
    private PlayerEmpower _empower;
    private PlayerSwordWaveLauncher _swordWave;
    private float _now;
    private float _deltaTime;
    private float _scale = 1f;

    public float Now => _now;
    public float DeltaTime => _deltaTime;
    public float Scale => _scale;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
        _swordWave = GetComponent<PlayerSwordWaveLauncher>();
        _now = Time.time;
    }

    void Update()
    {
        _scale = CalculateScale();
        _deltaTime = Time.deltaTime * _scale;
        _now += _deltaTime;
    }

    /// <summary>
    /// 게임 시간 대비 플레이어 시간 배율을 계산해 반환한다.
    /// 일섬 타격 정지 중이면 1이라 플레이어도 세계와 함께 멈춘다. 일섬 집중 중이면 일섬 플레이어 배율을, 그 외 불릿 타임이면 플레이어 배율을 현재 세계 배율로 나눈 값이며 1보다 작아지지 않는다.
    /// 불릿 타임이 아닌 검기 발사 연출 중이면 1을 현재 세계 배율로 나눠 실시간과 같게 흐르고, 그 외나 정지 중이면 1이다.
    /// </summary>
    private float CalculateScale()
    {
        if (Time.timeScale <= 0f) return 1f;
        if (_swordWave.IsHitStopActive) return 1f;
        // 일섬 돌진의 앞부분은 일섬 전용 배율로 플레이어도 느려져 감속이 풀리는 순간의 리듬을 살린다.
        if (_empower.IsSwordWaveFocusing) return Mathf.Max(1f, _empower.SwordWaveFocusPlayerTimeScale / Time.timeScale);
        if (_empower.IsBulletTimeActive) return Mathf.Max(1f, _empower.PlayerTimeScale / Time.timeScale);
        if (_swordWave.IsLaunchSlowActive) return Mathf.Max(1f, 1f / Time.timeScale);
        return 1f;
    }
}
