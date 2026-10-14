using System.Collections.Generic;

using UnityEngine;

namespace BlindBlade.Perception
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-20)]
    public sealed class ThreatPresentationResolver : MonoBehaviour
    {
        [Header("세션 연결")]
        [SerializeField] private ThreatPresentationSettings _settings;
        [SerializeField] private Transform _observer;
        [SerializeField] private Camera _viewCamera;
        [SerializeField, Tooltip("A가 갱신하는 보상 파동 마스크. 연결하면 거리 밖 근접 적도 파동 도달 시 공개한다.")]
        private SensoryPerceptionMask _rewardPulseMask;
        private readonly List<ThreatPresentationTarget> _targets = new List<ThreatPresentationTarget>();
        private readonly List<ThreatPresentationSnapshot> _snapshots = new List<ThreatPresentationSnapshot>();
        private readonly Plane[] _cameraPlanes = new Plane[6];
        private readonly Plane[] _exitPlanes = new Plane[6];
        private bool _sessionActive;

        public ThreatPresentationSettings Settings => _settings;
        public Transform Observer => _observer;
        public Camera ViewCamera => _viewCamera;
        public bool IsSessionActive => _sessionActive;
        public SensoryPerceptionMask RewardPulseMask => _rewardPulseMask;
        public IReadOnlyList<ThreatPresentationSnapshot> Snapshots => _snapshots;

        void OnEnable()
        {
            _sessionActive = true;
        }

        void OnDisable()
        {
            EndSession();
        }

        void LateUpdate()
        {
            if (_sessionActive)
                ResolveNow();
        }

        /// <summary>target을 이 세션의 표시 대상에 중복 없이 등록한다. 전역 목록이나 전투 상태를 변경하지 않는다.</summary>
        public void Register(ThreatPresentationTarget target)
        {
            if (!_targets.Contains(target))
                _targets.Add(target);
        }

        /// <summary>target을 해제하고 이전 스냅샷을 제거한다. 나머지 대상의 식별·세대는 유지한다.</summary>
        public void Unregister(ThreatPresentationTarget target)
        {
            _targets.Remove(target);
            if (_sessionActive)
                ResolveNow();
            else
                _snapshots.Clear();
        }

        /// <summary>
        /// observer와 viewCamera를 새 세션에 연결하고 이전 외부 입력·임시 거리·표시 결과를 초기화한다.
        /// 활성 대상의 공급자는 이후 ApplySourceState로 새 생명의 정보를 다시 전달한다.
        /// </summary>
        public void BeginSession(Transform observer, Camera viewCamera)
        {
            EndSession();
            _observer = observer;
            _viewCamera = viewCamera;
            _sessionActive = true;
        }

        /// <summary>입력 없이 이 세션의 표시 계산을 멈추고 대상의 외부 입력·임시 거리·스냅샷을 비운다.</summary>
        public void EndSession()
        {
            _sessionActive = false;
            _snapshots.Clear();
            if (_rewardPulseMask != null)
                _rewardPulseMask.ClearPulseRegion();
            foreach (ThreatPresentationTarget target in _targets)
                target.ResetSource();
        }

        /// <summary>
        /// 현재 설정·관측자·카메라로 등록된 활성 대상을 계산하고 이번 프레임의 스냅샷 목록을 교체한다.
        /// 필수 Inspector 연결이 없으면 결과를 비워 이전 표시를 유지하지 않는다.
        /// </summary>
        public void ResolveNow()
        {
            _snapshots.Clear();
            if (!_sessionActive || _settings == null || _observer == null || _viewCamera == null)
            {
                foreach (ThreatPresentationTarget target in _targets)
                    target.ApplySnapshot(default);
                return;
            }
            PrepareCameraPlanes();
            foreach (ThreatPresentationTarget target in _targets)
            {
                if (target.isActiveAndEnabled)
                    _snapshots.Add(ResolveTarget(target));
            }
        }

        /// <summary>
        /// target의 거리·외형·소유권·통과 상태로 월드와 UI가 공유할 표시 결과를 반환하고 저장한다.
        /// Inspector 미리보기에서도 사용하며 Renderer·Collider·전투 컴포넌트는 변경하지 않는다.
        /// </summary>
        public ThreatPresentationSnapshot RefreshTarget(ThreatPresentationTarget target)
        {
            if ((Application.isPlaying && !_sessionActive)
                || _settings == null || _observer == null || _viewCamera == null)
            {
                target.ApplySnapshot(default);
                return default;
            }
            PrepareCameraPlanes();
            return ResolveTarget(target);
        }

        /// <summary>
        /// 현재 카메라와 설정의 화면 이탈 여유로 기본·확장 프러스텀 평면을 갱신한다.
        /// 장면 차폐를 검사하지 않아 벽 뒤 위협에도 동일한 화면 분류를 적용한다.
        /// </summary>
        private void PrepareCameraPlanes()
        {
            Matrix4x4 projection = _viewCamera.projectionMatrix;
            GeometryUtility.CalculateFrustumPlanes(projection * _viewCamera.worldToCameraMatrix, _cameraPlanes);
            float scale = 1f / (1f + 2f * _settings.ViewportExitMargin);
            Matrix4x4 expanded = Matrix4x4.Scale(new Vector3(scale, scale, 1f)) * projection;
            GeometryUtility.CalculateFrustumPlanes(expanded * _viewCamera.worldToCameraMatrix, _exitPlanes);
        }

        /// <summary>
        /// target과 준비된 카메라 평면으로 거리·안정된 화면 분류·방향·색상·투명도를 계산한다.
        /// 근접 적은 거리와 보상 파동 중 높은 가시성을 사용하고 반사·통과 수리검의 UI를 제외한다.
        /// </summary>
        private ThreatPresentationSnapshot ResolveTarget(ThreatPresentationTarget target)
        {
            ThreatSourceState source = target.GetSourceState();
            Vector3 position = target.transform.position;
            Vector3 offset = position - _observer.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            ThreatRadiusSource radiusSource = ThreatRadiusSource.Default;
            bool melee = source.Kind == ThreatTargetKind.Melee;
            float radius = melee ? target.GetEnemyRadius(_settings, source.MeleeType, out radiusSource) : 0f;
            float alpha = melee ? _settings.GetEnemyVisibility(distance, radius) : 1f;
            if (melee && _rewardPulseMask != null)
                alpha = Mathf.Max(alpha, _rewardPulseMask.GetPulseVisibility(position));
            bool hasBounds = target.TryGetBounds(out Bounds bounds);
            bool intersects = hasBounds && GeometryUtility.TestPlanesAABB(_cameraPlanes, bounds);
            bool stableInCamera = intersects || (hasBounds && target.Snapshot.IsInCamera
                && GeometryUtility.TestPlanesAABB(_exitPlanes, bounds));
            bool eligible = source.IsActive && hasBounds && alpha > 0f;
            Color color = source.Ownership == ThreatOwnership.Reflected
                ? _settings.ReflectedColor : _settings.GetDistanceColor(distance);
            Vector3 forward = Vector3.ProjectOnPlane(_viewCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(_observer.forward, Vector3.up);
            float direction = offset.sqrMagnitude > 0.0001f
                ? Vector3.SignedAngle(forward, offset, Vector3.up) : 0f;
            ThreatPresentationSnapshot result = new ThreatPresentationSnapshot(source, position,
                distance, radius, radiusSource, eligible, intersects, stableInCamera,
                direction, color, eligible ? alpha : 0f);
            target.ApplySnapshot(result);
            return result;
        }
    }
}
