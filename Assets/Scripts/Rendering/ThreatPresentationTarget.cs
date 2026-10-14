using System;
using System.Collections.Generic;

using UnityEngine;

namespace BlindBlade.Perception
{
    [DisallowMultipleComponent]
    public sealed class ThreatPresentationTarget : MonoBehaviour
    {
        [Header("표시 연결")]
        [SerializeField] private ThreatPresentationResolver _resolver;
        [SerializeField, Tooltip("외형의 화면 교차를 판정한다. 적 루트와 Collider의 활성 상태는 변경하지 않는다.")]
        private Renderer[] _renderers;

        [Header("임시 공급자 기본값")]
        [SerializeField] private ThreatTargetKind _kind;
        [SerializeField] private MeleeThreatType _meleeType;
        [SerializeField] private ThreatOwnership _ownership;
        [SerializeField] private bool _hasPassedPlayer;
        private int _localGeneration;
        private bool _usesSourceState;
        private ThreatSourceState _sourceState;

        [Header("개별 인지 거리")]
        [SerializeField] private bool _overrideEnemyRadius;
        [SerializeField, Min(0f)] private float _enemyRadius = 12f;
        private bool _hasRuntimeRadius;
        private float _runtimeRadius;

        public ThreatPresentationResolver Resolver => _resolver;
        public IReadOnlyList<Renderer> Renderers => _renderers ?? Array.Empty<Renderer>();
        public ThreatPresentationSnapshot Snapshot { get; private set; }

        void OnEnable()
        {
            _localGeneration++;
            ResetSource();
            if (_resolver == null)
            {
                Debug.LogError("ThreatPresentationTarget의 Resolver를 연결하세요.", this);
                return;
            }
            _resolver.Register(this);
        }

        void OnDisable()
        {
            if (_resolver != null)
                _resolver.Unregister(this);
            ResetSource();
        }

        void OnValidate()
        {
            _enemyRadius = Mathf.Max(0f, _enemyRadius);
        }

        /// <summary>
        /// 연결된 sourceState 또는 Inspector 기본값에서 현재 표시 입력을 반환한다.
        /// 공급자가 없으면 GetInstanceID와 활성화 세대를 표시 전용 식별자로 사용한다.
        /// </summary>
        public ThreatSourceState GetSourceState()
        {
            return _usesSourceState ? _sourceState : new ThreatSourceState(GetInstanceID(),
                _localGeneration, _kind, _meleeType, _ownership, _hasPassedPlayer, isActiveAndEnabled);
        }

        /// <summary>
        /// B의 어댑터가 전달한 state를 저장하고 새 식별자·세대에서는 임시 거리와 화면 이력을 초기화한다.
        /// 같은 투사체 생명에서 확정된 통과 상태는 false 입력으로 되돌리지 않는다.
        /// </summary>
        public void ApplySourceState(ThreatSourceState state)
        {
            bool sameLife = _usesSourceState && _sourceState.TargetId == state.TargetId
                && _sourceState.SpawnGeneration == state.SpawnGeneration && _sourceState.EntityId == state.EntityId;
            if (!sameLife)
            {
                _hasRuntimeRadius = false;
                Snapshot = default;
            }
            bool passed = state.HasPassedPlayer || (sameLife && _sourceState.HasPassedPlayer);
            _sourceState = new ThreatSourceState(state.TargetId, state.SpawnGeneration, state.Kind,
                state.MeleeType, state.Ownership, passed, state.IsActive, state.EntityId);
            _usesSourceState = true;
        }

        /// <summary>
        /// radius를 현재 생명의 최우선 인지 거리로 저장한다. 0은 평상시 거리 인지를 끈다.
        /// 새 생명·비활성화·세션 종료에서는 이 런타임 지정값을 제거한다.
        /// </summary>
        public void SetRuntimeRadius(float radius)
        {
            _runtimeRadius = Mathf.Max(0f, radius);
            _hasRuntimeRadius = true;
        }

        /// <summary>입력 없이 현재 생명의 런타임 거리 지정을 해제하고 Inspector·유형·공통값을 다시 사용한다.</summary>
        public void ClearRuntimeRadius()
        {
            _hasRuntimeRadius = false;
        }

        /// <summary>
        /// settings와 meleeType으로 런타임·개별·유형·공통 우선순위의 거리를 반환한다.
        /// source에 최종 선택 출처를 기록하며 프리팹과 씬 인스턴스의 개별값을 지원한다.
        /// </summary>
        public float GetEnemyRadius(ThreatPresentationSettings settings, MeleeThreatType meleeType,
            out ThreatRadiusSource source)
        {
            if (_hasRuntimeRadius || _overrideEnemyRadius)
            {
                source = _hasRuntimeRadius ? ThreatRadiusSource.Runtime : ThreatRadiusSource.Individual;
                return _hasRuntimeRadius ? _runtimeRadius : Mathf.Max(0f, _enemyRadius);
            }
            return settings.GetEnemyRadius(meleeType, out source);
        }

        /// <summary>
        /// 연결한 활성 Renderer의 월드 Bounds를 합쳐 bounds에 반환한다.
        /// 유효 외형이 없으면 false를 반환해 화면과 UI 모두 표시하지 않는다.
        /// </summary>
        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (_renderers == null)
                return false;
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (found)
                    bounds.Encapsulate(renderer.bounds);
                else
                    bounds = renderer.bounds;
                found = true;
            }
            return found;
        }

        /// <summary>snapshot을 현재 대상의 읽기 전용 진단과 다음 프레임의 화면 분류로 저장한다.</summary>
        internal void ApplySnapshot(ThreatPresentationSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        /// <summary>입력 없이 외부 상태·런타임 거리·표시 결과를 지우고 Inspector 초기 설정으로 되돌린다.</summary>
        internal void ResetSource()
        {
            _usesSourceState = false;
            _hasRuntimeRadius = false;
            Snapshot = default;
        }
    }
}
