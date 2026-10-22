using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 한 방어 세션에서 근접 적의 공격 차례를 동시 상한과 지급 간격 안에서 오래 기다린 순서로 나눠 준다.
/// 차례는 공격 한 번에 쓰이며 공격이 끝나면 반납되고, 받은 뒤 공격을 시작하지 못하면 제한 시간 뒤 회수된다.
/// </summary>
internal sealed class DefenseAttackTurnPool : IEnemyAttackGate
{
    private const float FRESH_REQUEST_WINDOW = 0.2f;
    private const float QUEUE_MEMORY = 5f;

    private readonly List<EnemyController> _queue = new List<EnemyController>();
    private readonly Dictionary<EnemyController, float> _lastRequestedAt = new Dictionary<EnemyController, float>();
    private readonly Dictionary<EnemyController, float> _grantedAt = new Dictionary<EnemyController, float>();
    private readonly HashSet<EnemyController> _attacking = new HashSet<EnemyController>();
    private readonly List<EnemyController> _expired = new List<EnemyController>();
    private int _capacity = 1;
    private float _engageRadius;
    private float _grantGap;
    private float _nextGrantAt;

    /// <summary>
    /// now, capacity, engageRadius, grantGap, turnTimeout으로 동시 상한과 대기 반경을 갱신한다.
    /// 공격을 시작하지 못한 채 turnTimeout을 넘긴 차례와 오래 갱신되지 않은 대기 요청을 제거한 뒤 빈 차례를 지급한다.
    /// </summary>
    internal void Tick(float now, int capacity, float engageRadius, float grantGap, float turnTimeout)
    {
        _capacity = Mathf.Max(1, capacity);
        _engageRadius = engageRadius;
        _grantGap = grantGap;

        _expired.Clear();
        foreach (KeyValuePair<EnemyController, float> pair in _grantedAt)
        {
            if (!_attacking.Contains(pair.Key) && now - pair.Value >= turnTimeout)
                _expired.Add(pair.Key);
        }
        foreach (EnemyController enemy in _expired)
            ReleaseTurn(enemy);

        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            EnemyController enemy = _queue[i];
            if (now - _lastRequestedAt[enemy] <= QUEUE_MEMORY)
                continue;
            _queue.RemoveAt(i);
            _lastRequestedAt.Remove(enemy);
        }
        TryGrant(now);
    }

    /// <summary>
    /// enemy가 플레이어와 대기 반경 안에 있으면 대기열에 넣거나 요청 시각을 갱신하고 빈 차례 지급을 시도한다.
    /// enemy가 차례를 가지고 있으면 true를 반환하며 반경 밖이면 대기열에 넣지 않고 false를 반환한다.
    /// </summary>
    public bool RequestTurn(EnemyController enemy)
    {
        if (_grantedAt.ContainsKey(enemy))
            return true;

        Vector3 offset = enemy.transform.position - Game.Player.transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > _engageRadius * _engageRadius)
            return false;

        float now = Time.time;
        if (!_lastRequestedAt.ContainsKey(enemy))
            _queue.Add(enemy);
        _lastRequestedAt[enemy] = now;
        TryGrant(now);
        return _grantedAt.ContainsKey(enemy);
    }

    /// <summary>차례를 가진 enemy를 공격 중으로 기록해 Tick의 미사용 차례 회수에서 제외한다.</summary>
    public void NotifyAttackStarted(EnemyController enemy)
    {
        if (_grantedAt.ContainsKey(enemy))
            _attacking.Add(enemy);
    }

    /// <summary>enemy의 차례·공격 기록·대기 요청을 제거한다. 다시 요청하면 대기열 맨 뒤에 들어간다.</summary>
    public void ReleaseTurn(EnemyController enemy)
    {
        _grantedAt.Remove(enemy);
        _attacking.Remove(enemy);
        if (_lastRequestedAt.Remove(enemy))
            _queue.Remove(enemy);
    }

    /// <summary>세션 종료 시 모든 차례와 대기 요청, 다음 지급 시각을 초기화한다.</summary>
    internal void Clear()
    {
        _queue.Clear();
        _lastRequestedAt.Clear();
        _grantedAt.Clear();
        _attacking.Clear();
        _nextGrantAt = 0f;
    }

    /// <summary>
    /// now가 다음 지급 시각을 지났고 빈 차례가 있으면 대기열 앞쪽부터 지금 요청 중인 적 하나에게 차례를 지급한다.
    /// 지급하면 _grantedAt에 기록하고 대기열에서 빼며 _nextGrantAt을 grantGap만큼 미룬다.
    /// </summary>
    private void TryGrant(float now)
    {
        if (_grantedAt.Count >= _capacity || now < _nextGrantAt)
            return;

        for (int i = 0; i < _queue.Count; i++)
        {
            EnemyController enemy = _queue[i];
            // 잠시 원을 도느라 요청을 멈춘 적은 순서만 유지하고 지금 요청 중인 다음 적에게 넘긴다.
            if (now - _lastRequestedAt[enemy] > FRESH_REQUEST_WINDOW)
                continue;
            _queue.RemoveAt(i);
            _lastRequestedAt.Remove(enemy);
            _grantedAt[enemy] = now;
            _nextGrantAt = now + _grantGap;
            return;
        }
    }
}
