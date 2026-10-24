using System;

using UnityEngine;

/// <summary>일섬·거합의 분리 실행 ID와 지연 공격 종료 신호를 관리한다.</summary>
internal static class MultiKillAttackRuntime
{
    private static int _nextAttackId;

    internal static event Action<int> AttackFinished;

    /// <summary>플레이어 행동 ID와 겹치지 않도록 다음 음수 실행 ID를 발급한다.</summary>
    internal static int AllocateAttackId()
    {
        return --_nextAttackId;
    }

    /// <summary>attackId에 해당하는 실행의 지연 피해가 끝났음을 알린다.</summary>
    internal static void CompleteAttack(int attackId)
    {
        if (attackId < 0)
            AttackFinished?.Invoke(attackId);
    }

    /// <summary>새 플레이 세션에서 ID와 종료 구독을 초기화한다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        _nextAttackId = 0;
        AttackFinished = null;
    }
}
