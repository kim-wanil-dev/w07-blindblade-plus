using UnityEngine;

/// <summary>
/// 공격의 준비·베기·종료 시간을 관리하고 Animator 평가 뒤 실제 칼날 피해 검사를 요청한다.
/// 판정, 표현, 연계 입력 계산은 같은 공격 시간 프로필 표를 읽는다.
/// </summary>
public sealed class PlayerActionDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController _player;
    [SerializeField] private PlayerAnimationEventRelay _animationEventRelay;
    private KatanaCombatCoordinator _combat;
    private KatanaWeaponVisual _weaponVisual;
    private PlayerClock _playerClock;

    [Header("1타·발도 시간")]
    [SerializeField, Min(0f), InspectorName("적중 시각 (초)")]
    [Tooltip("1타, 1타 시간을 쓰는 교체 2타, 충전 발도의 베기 판정 중심입니다. 1타 후속 입력 예약도 이 시각에 끝납니다.")]
    private float _iaidoHitTime = 0.42f;
    [SerializeField, Min(0f), InspectorName("행동 종료 (초)")]
    [Tooltip("이 시각에 공격 종료를 알리고 회복으로 넘어갑니다.")]
    private float _iaidoSlashDuration = 0.72f;
    [SerializeField, Min(0.01f), InspectorName("베기 판정 폭 (초)")]
    [Tooltip("적중 시각을 중심으로 칼날 피해·접촉 효과·검기·휘두르기 소리를 시작하는 구간 폭입니다.")]
    private float _strikeWindow = 0.22f;

    [Header("기본 2타 시간")]
    [SerializeField, Min(0f), InspectorName("적중 시각 (초)")]
    [Tooltip("교체 2타가 1타 시간을 사용하지 않을 때의 2타 판정 중심입니다. 사용 여부는 KatanaWeaponVisual의 교체 2타 설정입니다.")]
    private float _normalHitTime = 0.15f;
    [SerializeField, Min(0f), InspectorName("행동 종료 (초)")]
    private float _normalActionDuration = 0.4f;
    [SerializeField, Min(0.01f), InspectorName("베기 판정 폭 (초)")]
    [Tooltip("기본 2타의 판정 폭입니다. 대상 돌진 베기의 검기·휘두르기 소리 시작 구간도 이 폭을 사용합니다.")]
    private float _normalStrikeWindow = 0.16f;

    [Header("대상 돌진 연계 타이밍")]
    [SerializeField, Min(0.01f), InspectorName("적중 시각 (초)")]
    [Tooltip("대상 돌진 1·2타의 표현 적중 시각입니다. 피해는 돌진 도착 시점에 판정합니다.")]
    private float _targetedComboHitTime = 0.12f;
    [SerializeField, Min(0.01f), InspectorName("행동 종료 (초)")]
    private float _targetedComboActionDuration = 0.32f;

    [Header("패링 반격 타이밍")]
    [SerializeField, Min(0.01f), InspectorName("적중 시각 (초)")]
    [Tooltip("정확한 패링 뒤 제자리 또는 돌진 단발 반격의 판정 중심입니다.")]
    private float _counterHitTime = 0.12f;
    [SerializeField, Min(0.01f), InspectorName("행동 종료 (초)")]
    private float _counterActionDuration = 0.34f;
    [SerializeField, Min(0.01f), InspectorName("베기 판정 폭 (초)")]
    private float _counterStrikeWindow = 0.12f;

    [Header("행동 진행 상태")]
    private PlayerActionState _activeState;
    private int _activeActionId;
    private AttackTimingProfile _activeNormalAttackProfile;
    private float _elapsed;
    private bool _impactSent;
    private bool _endSent;
    private bool _sweepPending;
    private bool _sweepFinalSample;
    private bool _endPending;

    /// <summary>판정·표현·연계 입력 계산이 공유하는 공격 시간 묶음이다.</summary>
    private enum AttackTimingProfile
    {
        Normal,
        Iaido,
        TargetedCombo,
        Counter
    }

    void Awake()
    {
        if (_player == null)
            _player = GetComponent<PlayerController>();
        if (_animationEventRelay == null)
            _animationEventRelay = GetComponent<PlayerAnimationEventRelay>();
        _combat = GetComponent<KatanaCombatCoordinator>();
        _playerClock = GetComponent<PlayerClock>();
        _weaponVisual = GetComponent<KatanaWeaponVisual>();
    }

    void Update()
    {
        if (_player == null || _player.IsPaused)
            return;

        PlayerActionState state = _player.CurrentState;
        if (state != PlayerActionState.NormalAttack && state != PlayerActionState.IaidoSlash)
        {
            _activeState = state;
            _activeActionId = _player.CurrentActionId;
            _elapsed = 0f;
            _impactSent = false;
            _endSent = false;
            _sweepPending = false;
            _endPending = false;
            return;
        }

        if (state != _activeState || _player.CurrentActionId != _activeActionId)
        {
            _activeState = state;
            _activeActionId = _player.CurrentActionId;
            _elapsed = 0f;
            _impactSent = false;
            _endSent = false;
            // 일반 공격의 시간 묶음은 시작 프레임의 연계·반격 상태로 고정한다.
            if (state == PlayerActionState.NormalAttack)
                _activeNormalAttackProfile = ResolveNormalAttackProfile();
        }

        if (_endSent)
            return;

        _elapsed += _playerClock.DeltaTime;
        AttackTimingProfile profile = state == PlayerActionState.NormalAttack
            ? _activeNormalAttackProfile : ResolveIaidoFinisherProfile();
        GetProfileTiming(profile, out float hitTime, out float actionDuration, out float strikeWindow);
        CalculateStrikeWindow(hitTime, actionDuration, strikeWindow, out float strikeStart, out float strikeEnd);
        if (state == PlayerActionState.NormalAttack && !_impactSent && _elapsed >= strikeStart)
        {
            bool finalSample = _elapsed >= strikeEnd;
            _sweepPending = true;
            _sweepFinalSample = finalSample;
            _impactSent = finalSample;
        }

        if (state == PlayerActionState.IaidoSlash && !_impactSent && _elapsed >= hitTime)
        {
            _impactSent = true;
            if (_animationEventRelay != null) _animationEventRelay.IaidoSlashImpactEvent();
            else _player.NotifyIaidoSlashImpact(_activeActionId);
        }

        if (_elapsed < actionDuration)
            return;

        _endSent = true;
        if (state == PlayerActionState.NormalAttack && _sweepPending)
        {
            _endPending = true;
            return;
        }
        if (state == PlayerActionState.NormalAttack)
        {
            if (_animationEventRelay != null)
                _animationEventRelay.NormalAttackEndedEvent();
            else
                _player.NotifyNormalAttackAnimationEnded(_activeActionId);
        }
        else
        {
            if (_animationEventRelay != null)
                _animationEventRelay.IaidoSlashEndedEvent();
            else
                _player.NotifyIaidoSlashAnimationEnded(_activeActionId);
        }
    }

    void LateUpdate()
    {
        if (!_sweepPending || _player.IsPaused) return;
        _sweepPending = false;
        _player.NotifyNormalAttackSweep(_activeActionId, _sweepFinalSample);
        if (_endPending)
        {
            _endPending = false;
            _player.NotifyNormalAttackAnimationEnded(_activeActionId);
        }
    }

    void OnValidate()
    {
        _normalHitTime = Mathf.Min(_normalHitTime, _normalActionDuration);
        _iaidoHitTime = Mathf.Min(_iaidoHitTime, _iaidoSlashDuration);
        _targetedComboHitTime = Mathf.Min(_targetedComboHitTime, _targetedComboActionDuration);
        _counterHitTime = Mathf.Min(_counterHitTime, _counterActionDuration);
    }

    /// <summary>
    /// kind와 현재 연계·반격 상태로 시간 묶음을 골라 적중·종료 시각을 반환한다.
    /// 시각 모션과 연계 입력 계산에 사용하며 진행 중인 판정 시간은 변경하지 않는다.
    /// </summary>
    public void GetPresentationTiming(PlayerAttackKind kind, out float impactTime, out float duration)
    {
        GetProfileTiming(ResolvePresentationProfile(kind), out impactTime, out duration, out _);
    }

    /// <summary>
    /// kind의 시간 묶음에서 적중 시각과 베기 판정 폭으로 공통 베기 구간을 계산한다.
    /// 피해·접촉·검기·소리 표현이 같은 start와 end를 사용하며 상태는 변경하지 않는다.
    /// </summary>
    public void GetStrikeWindow(PlayerAttackKind kind, out float start, out float end)
    {
        GetProfileTiming(ResolvePresentationProfile(kind), out float impact, out float duration, out float window);
        CalculateStrikeWindow(impact, duration, window, out start, out end);
    }

    /// <summary>actionId가 현재 진행 행동이면 게임 시간 경과를 반환한다. 아직 시작되지 않았거나 다른 행동이면 0을 반환한다.</summary>
    public float GetPresentationElapsed(int actionId)
    {
        return actionId == _activeActionId ? _elapsed : 0f;
    }

    /// <summary>
    /// 현재 연계·반격 상태로 일반 공격의 시간 묶음을 고른다.
    /// 반격을 먼저 확인하고 1타, 1타 시간을 쓰는 교체 2타 순서로 발도 시간을 선택해 반환한다.
    /// </summary>
    private AttackTimingProfile ResolveNormalAttackProfile()
    {
        if (_combat != null && _combat.IsCounterAttack)
            return AttackTimingProfile.Counter;
        if (_combat != null && (_combat.IsOpeningAttack || UsesOpeningTimingForSecondAttack()))
            return AttackTimingProfile.Iaido;
        return AttackTimingProfile.Normal;
    }

    /// <summary>발도 베기가 대상 돌진 연계인지 PlayerController의 연계 타수로 확인해 대상 돌진 또는 발도 시간 묶음을 반환한다.</summary>
    private AttackTimingProfile ResolveIaidoFinisherProfile()
    {
        return _player != null && _player.TargetedComboStrike > 0
            ? AttackTimingProfile.TargetedCombo : AttackTimingProfile.Iaido;
    }

    /// <summary>표현 조회의 kind를 일반 공격 또는 발도 베기 규칙으로 해석해 시간 묶음을 반환한다. 다음 공격 예약은 소비하지 않는다.</summary>
    private AttackTimingProfile ResolvePresentationProfile(PlayerAttackKind kind)
    {
        return kind == PlayerAttackKind.IaidoFinisher
            ? ResolveIaidoFinisherProfile() : ResolveNormalAttackProfile();
    }

    /// <summary>
    /// profile에 해당하는 Inspector 값을 읽어 적중 시각, 행동 종료 시각, 베기 판정 폭을 반환한다.
    /// 대상 돌진 베기는 기본 2타 판정 폭을 사용하며 상태를 변경하지 않는다.
    /// </summary>
    private void GetProfileTiming(AttackTimingProfile profile, out float hitTime, out float duration, out float strikeWindow)
    {
        switch (profile)
        {
            case AttackTimingProfile.Counter:
                hitTime = _counterHitTime;
                duration = _counterActionDuration;
                strikeWindow = _counterStrikeWindow;
                return;
            case AttackTimingProfile.TargetedCombo:
                hitTime = _targetedComboHitTime;
                duration = _targetedComboActionDuration;
                strikeWindow = _normalStrikeWindow;
                return;
            case AttackTimingProfile.Iaido:
                hitTime = _iaidoHitTime;
                duration = _iaidoSlashDuration;
                strikeWindow = _strikeWindow;
                return;
            default:
                hitTime = _normalHitTime;
                duration = _normalActionDuration;
                strikeWindow = _normalStrikeWindow;
                return;
        }
    }

    /// <summary>hitTime을 중심으로 window 폭의 베기 구간을 0과 duration 사이로 제한해 start와 end에 반환한다.</summary>
    private static void CalculateStrikeWindow(float hitTime, float duration, float window, out float start, out float end)
    {
        start = Mathf.Max(0f, hitTime - window * 0.5f);
        end = Mathf.Min(duration, hitTime + window * 0.5f);
    }

    /// <summary>현재 일반 2타가 교체 클립이며 1타 타이밍 사용 설정이 켜졌는지 검사해 동일 타이밍 적용 여부를 반환한다.</summary>
    private bool UsesOpeningTimingForSecondAttack()
    {
        return _combat != null && _combat.Strike == 2 && !_combat.IsCounterAttack
            && _weaponVisual != null && _weaponVisual.UsesOpeningTimingForSecondAttack;
    }
}
