using System;
using System.Collections.Generic;

using UnityEngine;

namespace BlindBlade.Combat.Contracts
{
    /// <summary>C가 계산하여 D에 전달하는 대상별 최종 표시 결과다.</summary>
    public readonly struct ThreatPresentationSnapshot
    {
        public CombatSessionKey Session { get; }
        public CombatEntityKey Target { get; }
        public long Revision { get; }
        public bool IsActive { get; }
        public bool IsEligible { get; }
        public bool IsInCamera { get; }
        public bool WorldVisible { get; }
        public bool UiVisible { get; }
        public Vector2 UiDirection { get; }
        public Vector3 WorldPosition { get; }
        public float Proximity { get; }
        public Color Color { get; }
        public float Alpha { get; }

        /// <summary>
        /// session, target, revision과 active/eligible/camera/world/UI 상태 및 uiDirection, color, alpha를 보관한다.
        /// C의 계산 결과를 변경하지 않고 UI가 적용할 불변 스냅샷을 반환한다.
        /// </summary>
        public ThreatPresentationSnapshot(
            CombatSessionKey session, CombatEntityKey target, long revision,
            bool isActive, bool isEligible, bool isInCamera, bool worldVisible, bool uiVisible,
            Vector2 uiDirection, Vector3 worldPosition, float proximity, Color color, float alpha)
        {
            Session = session;
            Target = target;
            Revision = revision;
            IsActive = isActive;
            IsEligible = isEligible;
            IsInCamera = isInCamera;
            WorldVisible = worldVisible;
            UiVisible = uiVisible;
            UiDirection = uiDirection;
            WorldPosition = worldPosition;
            Proximity = Mathf.Clamp01(proximity);
            Color = color;
            Alpha = alpha;
        }
    }

    /// <summary>실제 발소리의 고정 원점과 발행자의 현재 위치에 대한 C의 표시 결과다.</summary>
    public readonly struct FootstepPresentationSnapshot
    {
        public CombatEventIdentity EventIdentity { get; }
        public CombatEntityKey Emitter { get; }
        public Vector3 Origin { get; }
        public CombatTimestamp OccurredAt { get; }
        public ThreatPresentationSnapshot Presentation { get; }

        /// <summary>
        /// eventIdentity, emitter, origin, occurredAt과 presentation을 한 발소리의 표시 값으로 저장한다.
        /// 발생 원점을 보존하며 카메라 변화에 따른 표시 갱신에서도 같은 사건 식별을 사용한다.
        /// </summary>
        public FootstepPresentationSnapshot(
            CombatEventIdentity eventIdentity, CombatEntityKey emitter, Vector3 origin,
            CombatTimestamp occurredAt, ThreatPresentationSnapshot presentation)
        {
            EventIdentity = eventIdentity;
            Emitter = emitter;
            Origin = origin;
            OccurredAt = occurredAt;
            Presentation = presentation;
        }
    }

    public enum AttackIndicatorKind
    {
        CrossedKatana,
        Fangs
    }

    /// <summary>한 공격 시도의 진행도와 공격자 방향 표시를 HUD에 전달한다.</summary>
    public readonly struct AttackTelegraphPresentationSnapshot
    {
        public CombatEntityKey Attacker { get; }
        public int AttackId { get; }
        public AttackIndicatorKind Kind { get; }
        public float Progress { get; }
        public bool IsCommitted { get; }
        public ThreatPresentationSnapshot Presentation { get; }

        /// <summary>
        /// attacker와 attackId의 공격에 kind·progress·isCommitted 및 presentation을 묶는다.
        /// 생성된 값은 해당 공격 시도의 현재 방향 UI 상태를 나타낸다.
        /// </summary>
        public AttackTelegraphPresentationSnapshot(CombatEntityKey attacker, int attackId,
            AttackIndicatorKind kind, float progress, bool isCommitted,
            ThreatPresentationSnapshot presentation)
        {
            Attacker = attacker;
            AttackId = attackId;
            Kind = kind;
            Progress = Mathf.Clamp01(progress);
            IsCommitted = isCommitted;
            Presentation = presentation;
        }
    }

    public interface IThreatPresentationProvider
    {
        event Action<ThreatPresentationSnapshot> ThreatChanged;
        event Action<FootstepPresentationSnapshot> FootstepChanged;

        /// <summary>
        /// 현재 활성 위협의 표시 목록을 읽기 전용으로 반환한다.
        /// 인수를 받지 않으며 구독 직후 초기 표시를 맞추는 데 사용한다.
        /// </summary>
        public IReadOnlyList<ThreatPresentationSnapshot> GetCurrentThreats();

        /// <summary>
        /// 현재 유효한 발소리 사건의 표시 목록을 읽기 전용으로 반환한다.
        /// 인수를 받지 않으며 발소리 원점이나 대상의 상태는 변경하지 않는다.
        /// </summary>
        public IReadOnlyList<FootstepPresentationSnapshot> GetCurrentFootsteps();
    }
}
