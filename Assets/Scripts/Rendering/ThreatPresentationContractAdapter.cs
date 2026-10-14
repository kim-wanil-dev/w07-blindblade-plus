using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

using ContractSnapshot = BlindBlade.Combat.Contracts.ThreatPresentationSnapshot;

namespace BlindBlade.Perception
{
    /// <summary>C의 월드 판정을 공통 표시 계약으로 변환하며 세션·대상 종료와 갱신 순번을 제공한다.</summary>
    [DefaultExecutionOrder(-10)]
    [DisallowMultipleComponent]
    public sealed class ThreatPresentationContractAdapter : MonoBehaviour, IThreatPresentationProvider
    {
        [Header("공통 계약 연결")]
        [SerializeField] private ThreatPresentationResolver _resolver;
        [SerializeField] private CombatSessionRuntime _sessionRuntime;
        private readonly Dictionary<CombatEntityKey, ContractSnapshot> _previous = new Dictionary<CombatEntityKey, ContractSnapshot>();
        private readonly List<ContractSnapshot> _current = new List<ContractSnapshot>();
        private readonly HashSet<CombatEntityKey> _seen = new HashSet<CombatEntityKey>();
        private readonly List<CombatEntityKey> _ended = new List<CombatEntityKey>();
        private CombatSessionKey _session;
        private bool _hasSession;
        private long _revision;

        public ThreatPresentationResolver Resolver => _resolver;
        public long Revision => _revision;
        public event Action<ContractSnapshot> ThreatChanged;
        public event Action<FootstepPresentationSnapshot> FootstepChanged { add { } remove { } }

        void LateUpdate()
        {
            PublishNow();
        }

        void OnDisable()
        {
            ClearCurrent();
        }

        /// <summary>현재 활성 세션의 위협 목록을 반환한다. 비활성화 또는 세션 종료 상태에서는 빈 목록이다.</summary>
        public IReadOnlyList<ContractSnapshot> GetCurrentThreats()
        {
            return _current;
        }

        /// <summary>발소리는 기존 공급자가 소유하므로 입력 없이 빈 발소리 목록을 반환한다.</summary>
        public IReadOnlyList<FootstepPresentationSnapshot> GetCurrentFootsteps()
        {
            return Array.Empty<FootstepPresentationSnapshot>();
        }

        /// <summary>
        /// 연결된 Resolver 결과를 현재 D 세션의 공통 스냅샷으로 발행한다.
        /// 표시 자격·월드/UI 분류·색·Alpha를 재판정하지 않고 변경 순번과 종료 결과를 갱신한다.
        /// </summary>
        public void PublishNow()
        {
            CombatSessionRuntime runtime = _sessionRuntime != null ? _sessionRuntime : CombatSessionRuntime.Active;
            if (_resolver == null || !_resolver.isActiveAndEnabled || !_resolver.IsSessionActive
                || _resolver.Settings == null || _resolver.ViewCamera == null
                || runtime == null || !runtime.IsInitialized || runtime.gameObject.scene != gameObject.scene)
            {
                if (_hasSession && _resolver != null && _resolver.IsSessionActive
                    && (runtime == null || !runtime.IsInitialized || runtime.gameObject.scene != gameObject.scene))
                    _resolver.EndSession();
                ClearCurrent();
                return;
            }
            if (!_hasSession || !IsSameSession(_session, runtime.Session))
            {
                bool changedSession = !string.IsNullOrEmpty(_session.SessionId) && !IsSameSession(_session, runtime.Session);
                ClearCurrent();
                _session = runtime.Session;
                _hasSession = true;
                _revision = 0;
                if (changedSession)
                {
                    _resolver.BeginSession(_resolver.Observer, _resolver.ViewCamera);
                    _resolver.ResolveNow();
                }
            }
            _current.Clear();
            _seen.Clear();
            foreach (ThreatPresentationSnapshot result in _resolver.Snapshots)
            {
                if (!result.IsActive)
                    continue;
                string entityId = string.IsNullOrWhiteSpace(result.EntityId)
                    ? $"Perception/{result.Kind}/{result.TargetId}" : result.EntityId;
                CombatEntityKey key = new CombatEntityKey(entityId, result.SpawnGeneration);
                if (!_seen.Add(key))
                {
                    Debug.LogError("같은 공통 대상 키가 C 결과에 중복됐습니다: " + entityId, this);
                    continue;
                }
                Color color = result.Color;
                float alpha = result.Alpha * color.a;
                color.a = 1f;
                ContractSnapshot snapshot = new ContractSnapshot(_session, key, ++_revision,
                    result.IsActive, result.IsEligible, result.IsInCamera, result.ShowWorld, result.ShowUi,
                    GetUiDirection(result.Position), result.Position,
                    _resolver.Settings.GetProximity(result.HorizontalDistance), color, alpha);
                _current.Add(snapshot);
                _previous[key] = snapshot;
                ThreatChanged?.Invoke(snapshot);
            }
            _ended.Clear();
            foreach (CombatEntityKey key in _previous.Keys)
                if (!_seen.Contains(key))
                    _ended.Add(key);
            foreach (CombatEntityKey key in _ended)
            {
                PublishEnded(_previous[key]);
                _previous.Remove(key);
            }
        }

        /// <summary>positionを接続カメラに投影し、右がx正・上がy正のHUD方向を返す。方向未確定なら上を返す。</summary>
        private Vector2 GetUiDirection(Vector3 position)
        {
            Vector3 viewport = _resolver.ViewCamera.WorldToViewportPoint(position);
            Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (viewport.z < 0f)
                direction = -direction;
            return direction.sqrMagnitude < 0.0001f ? Vector2.up : direction.normalized;
        }

        /// <summary>previous의 식별·마지막 위치·색을 보존한 종료 스냅샷을 발행하고 갱신 순번을 증가시킨다.</summary>
        private void PublishEnded(ContractSnapshot previous)
        {
            ThreatChanged?.Invoke(new ContractSnapshot(previous.Session, previous.Target, ++_revision,
                false, false, previous.IsInCamera, false, false, previous.UiDirection,
                previous.WorldPosition, previous.Proximity, previous.Color, 0f));
        }

        /// <summary>입력 없이 기존 세션의 위협을 종료 발행하고 현재 목록·세션 연결을 비운다.</summary>
        private void ClearCurrent()
        {
            foreach (ContractSnapshot previous in _previous.Values)
                PublishEnded(previous);
            _previous.Clear();
            _current.Clear();
            _hasSession = false;
        }

        /// <summary>left와 right의 문자열 ID와 세대가 모두 같을 때 같은 세션으로 반환한다.</summary>
        private static bool IsSameSession(CombatSessionKey left, CombatSessionKey right)
        {
            return left.SessionId == right.SessionId && left.Generation == right.Generation;
        }
    }
}
