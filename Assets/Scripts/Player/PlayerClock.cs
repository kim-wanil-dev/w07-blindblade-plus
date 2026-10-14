using UnityEngine;

/// <summary>
/// 플레이어 이동·모션·전투 판정·무적·스태미너가 함께 쓰는 시계다.
/// 평소에는 게임 시간과 같고, 강화 불릿 타임 중에는 PlayerEmpower의 플레이어 배율만큼 세계보다 덜 느리게 흐른다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerEmpower))]
public sealed class PlayerClock : MonoBehaviour
{
    [Header("상태")]
    private PlayerEmpower _empower;
    private float _now;
    private float _deltaTime;
    private float _scale = 1f;

    public float Now => _now;
    public float DeltaTime => _deltaTime;
    public float Scale => _scale;

    void Awake()
    {
        _empower = GetComponent<PlayerEmpower>();
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
    /// 불릿 타임이 아니거나 정지 중이면 1이고, 불릿 타임이면 플레이어 배율을 현재 세계 배율로 나눈 값이며 1보다 작아지지 않는다.
    /// </summary>
    private float CalculateScale()
    {
        if (!_empower.IsBulletTimeActive || Time.timeScale <= 0f) return 1f;
        return Mathf.Max(1f, _empower.PlayerTimeScale / Time.timeScale);
    }
}
