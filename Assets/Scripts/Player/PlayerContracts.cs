using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>Player actions that can be observed by the game and presentation systems.</summary>
public enum PlayerActionState
{
    Free,
    Guarding,
    Charging,
    NormalAttack,
    IaidoDash,
    IaidoSlash,
    Recovery,
    Paused,
    Dead
}

public enum PlayerLocomotionMode
{
    Stopped,
    Walking,
    GuardWalking,
    ChargingStill,
    ChargingWalking,
    Crouching,
    CrouchWalking,
    IaidoDash
}

public enum PlayerFootstepType
{
    Normal,
    Quiet
}

public enum PlayerAttackKind
{
    Normal,
    IaidoFinisher
}

public enum PlayerAttackOutcome
{
    None,
    Hit,
    Miss,
    WallBlocked
}

public enum PlayerActionStage
{
    StateChanged,
    ChargeStarted,
    ChargeReady,
    ChargeCancelled,
    ChargeEnded,
    GuardStarted,
    GuardEnded,
    GuardSucceeded,
    AttackStarted,
    DashStarted,
    DashArrived,
    DashBlocked,
    SlashStarted,
    HitConfirmed,
    Missed,
    DeathConfirmed,
    ActionEnded,
    RecoveryStarted,
    RecoveryEnded,
    RequestRejected
}

/// <summary>강화 공격 상태 사건의 단계.</summary>
public enum PlayerEmpowerStage
{
    Ready,
    Started,
    Ended
}

/// <summary>강화 공격 상태가 끝난 이유. Ended 단계가 아니면 None이다.</summary>
public enum PlayerEmpowerEndReason
{
    None,
    Completed,
    Expired,
    Hit,
    Dodged,
    Died,
    SessionEnded,
    SwordWave,
    Attack,
    Cancelled
}

/// <summary>회피 사건의 단계. PerfectDodge는 일반 극한 회피 보상, IaiDodge는 쌍둥이 거합의 감속·감각 피드백만 나타낸다.</summary>
public enum PlayerDodgeStage
{
    Started,
    Ended,
    PerfectDodge,
    IaiDodge
}

/// <summary>플레이어 주변 파동을 요청한 원인.</summary>
public enum PlayerRevealPulseCause
{
    PerfectParry,
    PerfectDodge
}

/// <summary>스태미너가 바뀐 이유.</summary>
public enum PlayerStaminaChangeReason
{
    DodgeCost,
    PerfectDodgeRefund,
    Regen,
    Reset,
    GuardCost
}

/// <summary>입력 기록의 종류. GuardHoldResume은 누르고 있는 우클릭으로 가드에 다시 들어간 경우다.</summary>
public enum PlayerInputKind
{
    Attack,
    GuardPress,
    GuardHoldResume,
    GuardRelease,
    Dodge,
    SwordWave,
    Ultimate
}

/// <summary>입력이 거부된 이유. 시작·예약·후속 기록에서는 None이다.</summary>
public enum PlayerInputRejectReason
{
    None,
    Unavailable,
    Attacking,
    ActionBusy,
    AlreadyBuffered,
    ChainNotOpen,
    TargetUnavailable,
    AlreadyDodging,
    DodgeCooldown,
    NotGrounded,
    NotEnoughStamina,
    NotEnoughSkillGauge
}

/// <summary>입력 처리 결과. BufferDropped는 예약된 입력이 시작되지 못하고 사라진 경우다.</summary>
public enum PlayerInputStatus
{
    Started,
    Buffered,
    Rejected,
    BufferDropped
}

/// <summary>
/// Immutable sensory context sent by the player to perception systems.
/// </summary>
public struct PlayerSenseContext
{
    public bool IsMoving { get; }
    public bool IsFocusing { get; }
    public bool IsCharging { get; }
    public PlayerLocomotionMode LocomotionMode { get; }

    /// <summary>이동, 집중, 충전 상태와 보행 유형으로 감각 문맥 데이터를 생성합니다.</summary>
    public PlayerSenseContext(bool isMoving, bool isFocusing, bool isCharging, PlayerLocomotionMode locomotionMode)
    {
        IsMoving = isMoving;
        IsFocusing = isFocusing;
        IsCharging = isCharging;
        LocomotionMode = locomotionMode;
    }
}

/// <summary>
/// A sound-detected target. ValidUntil uses Unity scaled game time.
/// </summary>
public struct PlayerPerceivedTarget
{
    public string TargetId { get; }
    public Vector3 WorldPosition { get; }
    public float LastHeardTime { get; }
    public float ValidUntil { get; }

    /// <summary>식별자, 감지 위치, 감지 시점, 유효 종료 시각으로 감지 대상 데이터를 생성합니다.</summary>
    public PlayerPerceivedTarget(string targetId, Vector3 worldPosition, float lastHeardTime, float validUntil)
    {
        TargetId = targetId;
        WorldPosition = worldPosition;
        LastHeardTime = lastHeardTime;
        ValidUntil = validUntil;
    }
}

/// <summary>
/// Live combat geometry resolved from a perceived target by the enemy system.
/// </summary>
public struct PlayerCombatTarget
{
    public string TargetId { get; }
    public Vector3 WorldPosition { get; }
    public Collider HitCollider { get; }
    public float CollisionRadius { get; }
    public bool IsAlive { get; }

    /// <summary>적 식별자, 위치, 충돌체, 반경, 생존 상태로 전투 대상 데이터를 생성합니다.</summary>
    public PlayerCombatTarget(string targetId, Vector3 worldPosition, Collider hitCollider, float collisionRadius, bool isAlive)
    {
        TargetId = targetId;
        WorldPosition = worldPosition;
        HitCollider = hitCollider;
        CollisionRadius = Mathf.Max(0f, collisionRadius);
        IsAlive = isAlive;
    }
}

/// <summary>Data published when an actual player footstep occurs.</summary>
public struct PlayerFootstepEvent
{
    public Vector3 WorldPosition { get; }
    public float Time { get; }
    public PlayerFootstepType Type { get; }
    public float HorizontalSpeed { get; }
    public float NoiseStrength { get; }

    /// <summary>발걸음 위치, 시각, 유형, 속도, 소음 강도를 불변 이벤트 데이터로 생성합니다.</summary>
    public PlayerFootstepEvent(Vector3 worldPosition, float time, PlayerFootstepType type, float horizontalSpeed, float noiseStrength)
    {
        WorldPosition = worldPosition;
        Time = time;
        Type = type;
        HorizontalSpeed = horizontalSpeed;
        NoiseStrength = Mathf.Clamp01(noiseStrength);
    }
}

/// <summary>One attack attempt sent to the enemy combat owner for authoritative hit resolution.</summary>
public struct PlayerAttackRequest
{
    public int ActionId { get; }
    public PlayerAttackKind Kind { get; }
    public string PreferredTargetId { get; }
    public Vector3 Origin { get; }
    public Vector3 Direction { get; }
    public float Range { get; }
    public float IssuedAt { get; }

    /// <summary>행동 식별자와 공격 종류, 대상, 원점, 방향, 거리, 시각으로 판정 요청을 생성합니다.</summary>
    public PlayerAttackRequest(int actionId, PlayerAttackKind kind, string preferredTargetId, Vector3 origin, Vector3 direction, float range, float issuedAt)
    {
        ActionId = actionId;
        Kind = kind;
        PreferredTargetId = preferredTargetId;
        Origin = origin;
        Direction = direction.normalized;
        Range = Mathf.Max(0f, range);
        IssuedAt = issuedAt;
    }
}

/// <summary>Enemy-owned result for one attack request.</summary>
public struct PlayerAttackResolution
{
    public int ActionId { get; }
    public bool HitConfirmed { get; }
    public string HitTargetId { get; }
    public bool DeathConfirmed { get; }
    public bool IsComplete { get; }

    /// <summary>actionId·적중 대상·사망 및 isComplete로 피해 판정 결과를 생성한다. 미완료 궤적 검사를 구분한다.</summary>
    public PlayerAttackResolution(int actionId, bool hitConfirmed, string hitTargetId, bool deathConfirmed, bool isComplete = true)
    {
        ActionId = actionId;
        HitConfirmed = hitConfirmed;
        HitTargetId = hitTargetId;
        DeathConfirmed = deathConfirmed;
        IsComplete = isComplete;
    }
}

/// <summary>Map-owned response to a wall contact during iaido travel.</summary>
public struct PlayerWallResolution
{
    public bool IsDestructible { get; }
    public bool BreakSucceeded { get; }
    public bool IsPassable { get; }

    /// <summary>파괴 가능 여부, 파괴 성공 여부, 통과 여부로 맵 충돌 결과를 생성합니다.</summary>
    public PlayerWallResolution(bool isDestructible, bool breakSucceeded, bool isPassable)
    {
        IsDestructible = isDestructible;
        BreakSucceeded = breakSucceeded;
        IsPassable = isPassable;
    }

    public bool AllowsDashContinuation => IsDestructible && BreakSucceeded && IsPassable;
}

/// <summary>One action-stage notification for gameplay, animation, effects, sound, and UI consumers.</summary>
public struct PlayerActionEvent
{
    public int ActionId { get; }
    public PlayerActionState State { get; }
    public PlayerActionStage Stage { get; }
    public PlayerAttackOutcome Outcome { get; }
    public string TargetId { get; }
    public Vector3 WorldPosition { get; }

    /// <summary>행동 식별자, 상태, 단계, 결과, 대상, 위치로 외부 구독용 이벤트를 생성합니다.</summary>
    public PlayerActionEvent(int actionId, PlayerActionState state, PlayerActionStage stage, PlayerAttackOutcome outcome, string targetId, Vector3 worldPosition)
    {
        ActionId = actionId;
        State = state;
        Stage = stage;
        Outcome = outcome;
        TargetId = targetId;
        WorldPosition = worldPosition;
    }
}

/// <summary>강화 공격 준비·시작·종료 사건. 아직 발행하는 구현이 없다.</summary>
public readonly struct PlayerEmpowerEvent
{
    public int EmpowerId { get; }
    public PlayerEmpowerStage Stage { get; }
    public PlayerEmpowerEndReason EndReason { get; }
    public int SourceAttackId { get; }
    public int ActionId { get; }
    public float GameTime { get; }
    public double RealTime { get; }

    /// <summary>
    /// empowerId, stage, endReason과 퍼펙트 패링한 공격 sourceAttackId, 강화 공격 행동 actionId, 시각으로 사건을 만든다.
    /// actionId는 Started 이후에만 값이 있고 그 전에는 0이다.
    /// </summary>
    public PlayerEmpowerEvent(int empowerId, PlayerEmpowerStage stage, PlayerEmpowerEndReason endReason,
        int sourceAttackId, int actionId, float gameTime, double realTime)
    {
        EmpowerId = empowerId;
        Stage = stage;
        EndReason = endReason;
        SourceAttackId = sourceAttackId;
        ActionId = actionId;
        GameTime = gameTime;
        RealTime = realTime;
    }
}

/// <summary>회피 시작·종료·일반 극한 회피 보상·쌍둥이 거합 회피 피드백 사건. KatanaDamageReceiver가 회피 판정 구간에 맞춰 발행한다.</summary>
public readonly struct PlayerDodgeEvent
{
    public int DodgeId { get; }
    public PlayerDodgeStage Stage { get; }
    public int AttackId { get; }
    public Vector3 Position { get; }
    public float GameTime { get; }
    public double RealTime { get; }

    /// <summary>
    /// dodgeId, stage, 극한 회피를 만든 공격 attackId, 플레이어 position과 시각으로 사건을 만든다.
    /// attackId는 PerfectDodge 또는 IaiDodge 단계에서만 값이 있고 그 외에는 0이다.
    /// </summary>
    public PlayerDodgeEvent(int dodgeId, PlayerDodgeStage stage, int attackId, Vector3 position,
        float gameTime, double realTime)
    {
        DodgeId = dodgeId;
        Stage = stage;
        AttackId = attackId;
        Position = position;
        GameTime = gameTime;
        RealTime = realTime;
    }
}

/// <summary>퍼펙트 패링·극한 회피 때 플레이어 주변 파동을 요청하는 사건. KatanaDamageReceiver가 판정 확정 시 발행한다.</summary>
public readonly struct PlayerRevealPulseEvent
{
    public int PulseId { get; }
    public PlayerRevealPulseCause Cause { get; }
    public int AttackId { get; }
    public Vector3 Center { get; }
    public float GameTime { get; }
    public double RealTime { get; }

    /// <summary>
    /// pulseId, cause, 파동을 만든 공격 attackId, 파동 중심 center와 시각으로 파동 요청 사건을 만든다.
    /// center는 요청 시점의 플레이어 위치이며 attackId가 없으면 0이다.
    /// </summary>
    public PlayerRevealPulseEvent(int pulseId, PlayerRevealPulseCause cause, int attackId, Vector3 center,
        float gameTime, double realTime)
    {
        PulseId = pulseId;
        Cause = cause;
        AttackId = attackId;
        Center = center;
        GameTime = gameTime;
        RealTime = realTime;
    }
}

/// <summary>스태미너 변경 사건. PlayerStamina가 회피 비용·가드 비용·극한 회피 환불·회복 완료·초기화 때 발행한다.</summary>
public readonly struct PlayerStaminaEvent
{
    public float Current { get; }
    public float Max { get; }
    public float Delta { get; }
    public PlayerStaminaChangeReason Reason { get; }
    public int DodgeId { get; }
    public float GameTime { get; }
    public double RealTime { get; }

    /// <summary>
    /// 변경 후 current, max, 변화량 delta, reason과 관련 dodgeId, 시각으로 사건을 만든다.
    /// dodgeId는 회피 비용·환불일 때만 값이 있고 그 외에는 0이다.
    /// </summary>
    public PlayerStaminaEvent(float current, float max, float delta, PlayerStaminaChangeReason reason, int dodgeId,
        float gameTime, double realTime)
    {
        Current = current;
        Max = max;
        Delta = delta;
        Reason = reason;
        DodgeId = dodgeId;
        GameTime = gameTime;
        RealTime = realTime;
    }
}

/// <summary>실제 입력 1회의 처리 결과 기록. 같은 우클릭의 누름과 뗌은 같은 InputId를 사용한다.</summary>
public readonly struct PlayerInputRecord
{
    public int InputId { get; }
    public PlayerInputKind Kind { get; }
    public PlayerInputStatus Status { get; }
    public PlayerInputRejectReason RejectReason { get; }
    public int ActionId { get; }
    public double DeviceTime { get; }
    public double FrameRealTime { get; }
    public float GameTime { get; }

    /// <summary>
    /// inputId, kind, status, 거부 이유 rejectReason, 시작된 행동 actionId와 장치 입력 시각 deviceTime, 처리 프레임의 실시간·게임 시간으로 기록을 만든다.
    /// rejectReason은 Rejected일 때만 값이 있고, actionId는 공격이 즉시 시작된 경우에만 값이 있으며 그 외에는 None·0이다.
    /// </summary>
    public PlayerInputRecord(int inputId, PlayerInputKind kind, PlayerInputStatus status,
        PlayerInputRejectReason rejectReason, int actionId, double deviceTime, double frameRealTime, float gameTime)
    {
        InputId = inputId;
        Kind = kind;
        Status = status;
        RejectReason = rejectReason;
        ActionId = actionId;
        DeviceTime = deviceTime;
        FrameRealTime = frameRealTime;
        GameTime = gameTime;
    }
}

/// <summary>
/// The only player-facing contracts required from unfinished team systems.
/// Implementations belong to those systems, not to the Player folder.
/// </summary>
public interface IPlayerPerceptionProvider
{
    /// <summary>현재 유효한 청각 감지 대상을 반환합니다.</summary>
    IReadOnlyList<PlayerPerceivedTarget> GetCurrentTargets();
    /// <summary>플레이어의 이동·집중·충전 문맥을 사운드 감지 시스템에 전달합니다.</summary>
    void SetPlayerSenseContext(PlayerSenseContext context);
}

public interface IPlayerCombatService
{
    /// <summary>식별자 또는 충돌체에 대응하는 적의 전투 정보를 반환합니다.</summary>
    bool TryGetTarget(string targetId, out PlayerCombatTarget target);
    /// <summary>식별자 또는 충돌체에 대응하는 적의 전투 정보를 반환합니다.</summary>
    bool TryGetTarget(Collider collider, out PlayerCombatTarget target);
    /// <summary>공격 요청을 판정하고 적중·즉사 결과를 행동 식별자와 함께 반환합니다.</summary>
    PlayerAttackResolution ResolvePlayerAttack(PlayerAttackRequest request);
}

public interface IPlayerMapService
{
    /// <summary>대시 충돌 대상에 대한 파괴 요청 결과와 실제 통과 가능 여부를 반환합니다.</summary>
    PlayerWallResolution ResolveDashWallContact(Collider wall, Vector3 contactPoint, Vector3 contactNormal);
}

public interface IPlayerDefenseResolver
{
    /// <summary>
    /// context의 공격 1회를 판정하고 결과를 즉시 반환한다. 결과가 Hit이면 구현체가 체력 감소까지 처리한다.
    /// 호출자는 같은 접촉에 한 번만 호출하며 결과와 관계없이 TakeDamage를 다시 호출하지 않는다.
    /// </summary>
    DefenseResolution ResolveIncomingAttack(DamageContext context);
}

/// <summary>
/// 플레이어 전투 사실을 구독자에게 전달하는 사건 묶음이며 KatanaCombatCoordinator.Events로 얻는다.
/// 플레이어 전투 구성 요소만 사실이 확정된 뒤 한 번 발행하고 구독자는 받은 값으로 전투 결과를 바꾸지 않는다.
/// </summary>
public sealed class PlayerCombatEvents
{
    public event Action<DefenseResolution> DefenseResolved;
    public event Action<PlayerEmpowerEvent> EmpowerChanged;
    public event Action<PlayerDodgeEvent> DodgeChanged;
    public event Action<PlayerStaminaEvent> StaminaChanged;
    public event Action<PlayerRevealPulseEvent> RevealPulseRequested;
    public event Action<PlayerInputRecord> InputResolved;

    /// <summary>resolution을 DefenseResolved 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseDefenseResolved(DefenseResolution resolution) => DefenseResolved?.Invoke(resolution);

    /// <summary>empowerEvent를 EmpowerChanged 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseEmpowerChanged(PlayerEmpowerEvent empowerEvent) => EmpowerChanged?.Invoke(empowerEvent);

    /// <summary>dodgeEvent를 DodgeChanged 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseDodgeChanged(PlayerDodgeEvent dodgeEvent) => DodgeChanged?.Invoke(dodgeEvent);

    /// <summary>staminaEvent를 StaminaChanged 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseStaminaChanged(PlayerStaminaEvent staminaEvent) => StaminaChanged?.Invoke(staminaEvent);

    /// <summary>pulseEvent를 RevealPulseRequested 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseRevealPulseRequested(PlayerRevealPulseEvent pulseEvent) => RevealPulseRequested?.Invoke(pulseEvent);

    /// <summary>record를 InputResolved 구독자에게 전달한다. 상태는 변경하지 않는다.</summary>
    public void RaiseInputResolved(PlayerInputRecord record) => InputResolved?.Invoke(record);
}
