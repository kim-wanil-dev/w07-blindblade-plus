using System;

using UnityEngine;

/// <summary>
/// 일섬과 거합의 독립 게이지를 보유하고 보상 적립과 사용을 처리한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSkillGauge : MonoBehaviour
{
    private const float GAUGE_EPSILON = 0.0001f;
    private const float ULTIMATE_PARRY_REWARD_DIVISOR = 3f;

    [Header("일섬 게이지 설정")]
    [SerializeField, Min(0.01f)]
    private float _maxSwordWaveGauge = 3f;
    [SerializeField, Min(0f)]
    private float _swordWaveCost = 1f;
    [SerializeField, Min(0f)]
    private float _swordWavePerfectParryReward = 1f;
    [SerializeField, Min(0f)]
    private float _swordWavePerfectDodgeReward = 0.5f;
    [SerializeField, Min(0f), Tooltip("일반/대시 공격으로 일반 적을 처치했을 때의 일섬 게이지 보상. 0.1은 한 칸의 10%입니다.")]
    private float _swordWaveKillReward = 0.1f;

    [Header("거합 게이지 설정")]
    [SerializeField, Min(0.01f), Tooltip("거합 사용 비용은 최대 게이지 전체이며, 패링 보상은 최대값의 정확히 1/3입니다.")]
    private float _maxUltimateGauge = 100f;
    [SerializeField, Min(0f), Tooltip("극한 회피 성공 시 거합 게이지 증가량(%p).")]
    private float _ultimatePerfectDodgeReward = 20f;
    [SerializeField, Min(0f), Tooltip("일반/대시/일섬 공격으로 일반 적을 처치했을 때의 거합 게이지 증가량(%p).")]
    private float _ultimateKillReward = 2f;
    [SerializeField, Min(0f), Tooltip("일반/대시 공격으로 보스에게 유효 피해를 줬을 때의 거합 게이지 증가량(%p).")]
    private float _ultimateBossNormalHitReward = 5f;

    [Header("현재 게이지")]
    private float _swordWaveGauge;
    private float _ultimateGauge;

    /// <summary>일섬 게이지의 현재량을 반환한다.</summary>
    public float SwordWaveGauge => _swordWaveGauge;

    /// <summary>Inspector 설정을 반영한 일섬 게이지 최대량을 반환한다.</summary>
    public float MaxSwordWaveGauge => Mathf.Max(0f, _maxSwordWaveGauge);

    /// <summary>거합 게이지의 현재량을 반환한다.</summary>
    public float UltimateGauge => _ultimateGauge;

    /// <summary>거합 게이지의 0~1 정규화 값을 반환한다.</summary>
    public float UltimateNormalized => MaxUltimateGauge > 0f
        ? Mathf.Clamp01(_ultimateGauge / MaxUltimateGauge)
        : 0f;

    /// <summary>일섬 비용을 지불할 수 있는지 반환한다.</summary>
    public bool CanUseSwordWave => _swordWaveGauge + GAUGE_EPSILON >= Mathf.Max(0f, _swordWaveCost);

    /// <summary>거합 최대 게이지를 채워 사용할 수 있는지 반환한다.</summary>
    public bool CanUseUltimate => IsUltimateGaugeReady(_ultimateGauge);

    /// <summary>두 게이지가 실제로 바뀔 때 알린다.</summary>
    public event Action Changed;

    /// <summary>거합 게이지가 준비 상태로 전환될 때 알린다.</summary>
    public event Action UltimateBecameReady;

    private float MaxUltimateGauge => Mathf.Max(0f, _maxUltimateGauge);

    /// <summary>
    /// 퍼펙트 패링 보상으로 일섬 1칸과 거합 최대값의 정확히 1/3을 적립한다.
    /// 입력은 없으며 두 게이지를 상한까지 변경한다.
    /// </summary>
    public void RewardPerfectParry()
    {
        AddRewards(
            _swordWavePerfectParryReward,
            MaxUltimateGauge / ULTIMATE_PARRY_REWARD_DIVISOR);
    }

    /// <summary>
    /// 퍼펙트 회피 보상으로 Inspector 설정량만큼 두 게이지를 적립한다.
    /// 입력은 없으며 두 게이지를 상한까지 변경한다.
    /// </summary>
    public void RewardPerfectDodge()
    {
        AddRewards(_swordWavePerfectDodgeReward, _ultimatePerfectDodgeReward);
    }

    /// <summary>
    /// 일반 또는 대시 공격으로 일반 적을 처치한 보상으로 Inspector 설정량만큼 두 게이지를 적립한다.
    /// 입력은 없으며 두 게이지를 상한까지 변경한다.
    /// </summary>
    public void RewardKill()
    {
        AddRewards(_swordWaveKillReward, _ultimateKillReward);
    }

    /// <summary>
    /// 일섬으로 일반 적을 처치한 보상으로 거합 게이지만 Inspector 설정량만큼 적립한다.
    /// 입력은 없으며 거합 게이지를 상한까지 변경한다.
    /// </summary>
    public void RewardUltimateKill()
    {
        AddRewards(0f, _ultimateKillReward);
    }

    /// <summary>
    /// 보스에게 일반 또는 대시 공격이 유효 적중한 보상으로 거합 게이지만 적립한다.
    /// 입력은 없으며 거합 게이지를 상한까지 변경한다.
    /// </summary>
    public void RewardBossNormalHit()
    {
        AddRewards(0f, _ultimateBossNormalHitReward);
    }

    /// <summary>
    /// 일섬 비용을 지불하고 사용을 확정한다.
    /// 입력은 없으며 성공 시 일섬 게이지를 비용만큼 줄이고, 사용 불가 시 false를 반환한다.
    /// </summary>
    public bool TryConsumeSwordWave()
    {
        if (!CanUseSwordWave)
        {
            return false;
        }

        float cost = Mathf.Max(0f, _swordWaveCost);
        SetGaugeValues(_swordWaveGauge - cost, _ultimateGauge);
        return true;
    }

    /// <summary>
    /// 거합 최대 게이지 전체를 소비해 사용을 확정한다.
    /// 입력은 없으며 성공 시 거합 게이지를 0으로 만들고, 사용 불가 시 false를 반환한다.
    /// </summary>
    public bool TryConsumeUltimate()
    {
        if (!CanUseUltimate)
        {
            return false;
        }

        SetGaugeValues(_swordWaveGauge, 0f);
        return true;
    }

    /// <summary>
    /// 요청한 두 게이지 값을 상한 안으로 설정한다.
    /// swordWave와 ultimate를 사용하며, 실제 값이 바뀌면 변경 이벤트를 발생시킨다.
    /// </summary>
    public void SetGauges(float swordWave, float ultimate)
    {
        SetGaugeValues(swordWave, ultimate);
    }

    /// <summary>
    /// 두 보상량을 현재 게이지에 더해 각 최대값으로 제한한다.
    /// swordWaveReward와 ultimateReward를 사용하며, 변경된 상태를 이벤트로 알린다.
    /// </summary>
    private void AddRewards(float swordWaveReward, float ultimateReward)
    {
        SetGaugeValues(
            _swordWaveGauge + swordWaveReward,
            _ultimateGauge + ultimateReward);
    }

    /// <summary>
    /// 두 게이지를 정규화하고 실제 변화와 거합 준비 전이를 알린다.
    /// swordWave와 ultimate를 최대값으로 제한하고 필요한 이벤트를 발생시킨다.
    /// </summary>
    private void SetGaugeValues(float swordWave, float ultimate)
    {
        float nextSwordWaveGauge = ClampGauge(swordWave, MaxSwordWaveGauge);
        float nearestSlot = Mathf.Round(nextSwordWaveGauge);
        if (Mathf.Abs(nextSwordWaveGauge - nearestSlot) <= GAUGE_EPSILON)
            nextSwordWaveGauge = Mathf.Clamp(nearestSlot, 0f, MaxSwordWaveGauge);
        float nextUltimateGauge = ClampGauge(ultimate, MaxUltimateGauge);
        bool swordWaveChanged = nextSwordWaveGauge != _swordWaveGauge;
        bool ultimateChanged = nextUltimateGauge != _ultimateGauge;

        if (!swordWaveChanged && !ultimateChanged)
        {
            return;
        }

        bool wasUltimateReady = CanUseUltimate;
        _swordWaveGauge = nextSwordWaveGauge;
        _ultimateGauge = nextUltimateGauge;

        Changed?.Invoke();

        if (!wasUltimateReady && CanUseUltimate)
        {
            UltimateBecameReady?.Invoke();
        }
    }

    /// <summary>
    /// 게이지 값을 0과 최대값 사이로 제한하고 경계의 부동소수점 오차를 정규화한다.
    /// value와 maxValue를 사용하며 제한된 값을 반환한다.
    /// </summary>
    private float ClampGauge(float value, float maxValue)
    {
        float clampedValue = Mathf.Clamp(value, 0f, maxValue);

        if (clampedValue <= GAUGE_EPSILON)
        {
            return 0f;
        }

        if (maxValue - clampedValue <= GAUGE_EPSILON)
        {
            return maxValue;
        }

        return clampedValue;
    }

    /// <summary>
    /// 거합 게이지가 최대값에 도달했는지 오차 허용 범위로 판정한다.
    /// gauge를 현재 최대값과 비교하고 준비 여부를 반환한다.
    /// </summary>
    private bool IsUltimateGaugeReady(float gauge)
    {
        return MaxUltimateGauge > 0f && gauge + GAUGE_EPSILON >= MaxUltimateGauge;
    }
}
