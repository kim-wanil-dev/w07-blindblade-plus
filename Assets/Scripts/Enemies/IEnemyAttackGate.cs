/// <summary>
/// 여러 적이 같은 대상을 동시에 공격하지 않도록 공격 차례를 나눠 주는 제한을 EnemyController에 연결한다.
/// 연결이 없는 적은 기존처럼 차례 제한 없이 공격한다.
/// </summary>
public interface IEnemyAttackGate
{
    /// <summary>
    /// enemy의 공격 차례를 요청하고 대기열 요청 시각을 갱신한다.
    /// enemy가 이미 차례를 가졌거나 이번 요청으로 받으면 true를 반환한다.
    /// </summary>
    bool RequestTurn(EnemyController enemy);

    /// <summary>차례를 가진 enemy가 실제 공격을 시작했음을 기록해 미사용 차례 회수 대상에서 제외한다.</summary>
    void NotifyAttackStarted(EnemyController enemy);

    /// <summary>enemy가 가진 차례와 대기열 요청을 모두 제거한다.</summary>
    void ReleaseTurn(EnemyController enemy);
}
