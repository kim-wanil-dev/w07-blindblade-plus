using System;

namespace BlindBlade.Combat.Contracts
{
    /// <summary>A의 스태미너 원본에서 읽은 현재 값과 최대치다.</summary>
    public readonly struct StaminaSnapshot
    {
        public float Current { get; }
        public float Maximum { get; }

        /// <summary>
        /// current와 maximum을 스태미너 표시 값으로 저장한다.
        /// 소모·회복·행동 허용 상태를 변경하지 않고 공급자의 값을 그대로 보존한다.
        /// </summary>
        public StaminaSnapshot(float current, float maximum)
        {
            Current = current;
            Maximum = maximum;
        }
    }

    public interface IPlayerStaminaReadModel
    {
        StaminaSnapshot Current { get; }
        event Action<StaminaSnapshot> Changed;
    }
}
