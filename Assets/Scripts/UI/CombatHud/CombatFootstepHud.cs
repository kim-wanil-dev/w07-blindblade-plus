using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>C가 발행한 고정 원점 발소리 사건을 세션별 Image 풀로 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatFootstepHud : MonoBehaviour
    {
        [Header("공급자와 이미지 연결")]
        [SerializeField] private CombatThreatPresentationProvider _provider;
        [SerializeField] private CombatFootstepMarker[] _markers;
        [SerializeField] private RectTransform _container;

        [Header("발소리 표시 배치")]
        [SerializeField, Min(0f)] private float _radius = 310f;
        [SerializeField, Min(1f)] private float _farSize = 24f;
        [SerializeField, Min(1f)] private float _nearSize = 36f;
        private readonly Dictionary<CombatEntityKey, int> _slots = new Dictionary<CombatEntityKey, int>();
        private readonly Dictionary<CombatEntityKey, long> _revisions = new Dictionary<CombatEntityKey, long>();
        private bool[] _used;
        private CombatSessionKey _session;
        private bool _hasSession;
        private bool _subscribed;

        void Awake()
        {
            _used = new bool[_markers.Length];
            for (int i = 0; i < _markers.Length; i++)
                _markers[i].Hide();
        }

        void OnEnable()
        {
            if (_provider == null)
                return;
            _provider.FootstepChanged += HandleFootstep;
            _subscribed = true;
            foreach (FootstepPresentationSnapshot snapshot in _provider.GetCurrentFootsteps())
                HandleFootstep(snapshot);
        }

        void LateUpdate()
        {
            CombatSessionRuntime runtime = CombatSessionRuntime.Active;
            if (_hasSession && (runtime == null || !runtime.IsInitialized
                || !IsSameSession(_session, runtime.Session)))
                ClearMarkers();
        }

        void OnDisable()
        {
            if (_subscribed && _provider != null)
                _provider.FootstepChanged -= HandleFootstep;
            _subscribed = false;
            ClearMarkers();
        }

        /// <summary>snapshot의 현재 세션·revision·표시 자격을 확인해 마커를 배치하거나 반환한다. 풀이 가득 차면 새 표시를 생략한다.</summary>
        private void HandleFootstep(FootstepPresentationSnapshot snapshot)
        {
            ThreatPresentationSnapshot presentation = snapshot.Presentation;
            CombatSessionRuntime runtime = CombatSessionRuntime.Active;
            if (runtime == null || !runtime.IsInitialized
                || !IsSameSession(presentation.Session, runtime.Session))
                return;
            if (!_hasSession || !IsSameSession(_session, presentation.Session))
            {
                ClearMarkers();
                _session = presentation.Session;
                _hasSession = true;
            }
            if (_revisions.TryGetValue(presentation.Target, out long revision)
                && revision >= presentation.Revision)
                return;
            _revisions[presentation.Target] = presentation.Revision;

            if (!presentation.IsActive || !presentation.IsEligible || !presentation.UiVisible)
            {
                ReleaseMarker(presentation.Target);
                if (!presentation.IsActive)
                    _revisions.Remove(presentation.Target);
                return;
            }

            if (!_slots.TryGetValue(presentation.Target, out int slot))
            {
                slot = FindFreeSlot();
                if (slot < 0)
                    return;
                _slots.Add(presentation.Target, slot);
                _used[slot] = true;
            }
            float size = Mathf.Lerp(_farSize, _nearSize, presentation.Proximity);
            float availableRadius = Mathf.Max(0f,
                Mathf.Min(_container.rect.width, _container.rect.height) * 0.5f - size);
            _markers[slot].Show(presentation, Mathf.Min(_radius, availableRadius), size);
        }

        /// <summary>target에 배정된 마커를 숨기고 풀에 반환한다. 다른 사건의 표시는 변경하지 않는다.</summary>
        private void ReleaseMarker(CombatEntityKey target)
        {
            if (!_slots.TryGetValue(target, out int slot))
                return;
            _markers[slot].Hide();
            _used[slot] = false;
            _slots.Remove(target);
        }

        /// <summary>입력 없이 사용하지 않는 풀 인덱스를 반환한다. 빈 슬롯이 없으면 -1을 반환한다.</summary>
        private int FindFreeSlot()
        {
            for (int i = 0; i < _used.Length; i++)
            {
                if (!_used[i])
                    return i;
            }
            return -1;
        }

        /// <summary>입력 없이 모든 표시·슬롯·revision과 이전 세션 연결을 비운다.</summary>
        private void ClearMarkers()
        {
            if (_markers != null)
            {
                for (int i = 0; i < _markers.Length; i++)
                {
                    _markers[i].Hide();
                    _used[i] = false;
                }
            }
            _slots.Clear();
            _revisions.Clear();
            _hasSession = false;
        }

        /// <summary>left와 right의 세션 ID·세대가 모두 같으면 true를 반환한다.</summary>
        private static bool IsSameSession(CombatSessionKey left, CombatSessionKey right)
        {
            return left.Generation == right.Generation && left.SessionId == right.SessionId;
        }
    }
}
