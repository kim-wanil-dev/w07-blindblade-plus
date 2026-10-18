using UnityEngine;

/// <summary>
/// 공격 범위 안에서 공격 사이에 대상 주변을 움직이는 적 전용 이동을 EnemyController에 연결한다.
/// 구현이 없는 적은 기존처럼 공격 범위에서 멈춰 공격을 시도한다.
/// </summary>
public interface IEnemyEngagementMovement
{
    /// <summary>현재 교전 이동을 마치고 다음 공격을 시작해도 되면 true다.</summary>
    bool IsReadyToAttack { get; }

    /// <summary>
    /// target 주변의 교전 이동을 한 프레임 진행한다.
    /// EnemyController가 공격 범위 안에서 공격 중이 아닐 때 매 프레임 호출한다.
    /// </summary>
    void UpdateEngagement(Transform target);
}
