using UnityEngine;

/// <summary>
    /// 관측자와 수신한 소리 위치로 윤곽선의 공간·시간 인지 영역을 셰이더에 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SensoryManager))]
    public sealed class SensoryPerceptionMask : MonoBehaviour
    {
        private const int MAX_SOUND_REGIONS = 16;
        private static readonly int _observerRegionId = Shader.PropertyToID("_OutlineObserverRegion");
        private static readonly int _soundRegionsId = Shader.PropertyToID("_OutlineSoundRegions");
        private static readonly int _soundRegionRadiiId = Shader.PropertyToID("_OutlineSoundRegionRadii");
        private static readonly int _soundCountId = Shader.PropertyToID("_OutlineSoundCount");
        private static readonly int _soundStartRadiusId = Shader.PropertyToID("_OutlineSoundStartRadius");
        private static readonly int _soundEndRadiusId = Shader.PropertyToID("_OutlineSoundEndRadius");
        private static readonly int _soundLifetimeId = Shader.PropertyToID("_OutlineSoundLifetime");
        private static readonly int _soundFadeDurationId = Shader.PropertyToID("_OutlineSoundFadeDuration");
        private static readonly int _edgeFadeId = Shader.PropertyToID("_OutlineEdgeFade");
        private static readonly int _perceptionTimeId = Shader.PropertyToID("_OutlinePerceptionTime");
        private static readonly int _pulseRegionId = Shader.PropertyToID("_OutlinePulseRegion");
        private static readonly int _pulseVisibilityId = Shader.PropertyToID("_OutlinePulseVisibility");

        [Header("플레이어 주변 인지")]
        [SerializeField, Min(0f), Tooltip("관측자 주변을 상시 표시하는 반경(미터). 0이면 상시 표시하지 않는다.")]
        private float _playerRadius = 2f;

        [Header("소리 주변 인지")]
        [SerializeField, Min(0f), Tooltip("수신한 감각 사건 주변을 표시하는 반경(미터).")]
        private float _soundRadius = 4f;
        [SerializeField, Min(0.01f), Tooltip("소리 주변 윤곽선이 선형으로 사라지는 수명(초).")]
        private float _soundLifetime = 1f;
        [SerializeField, Min(0.01f), Tooltip("파동 확산이 끝난 뒤 시야가 서서히 사라지는 시간(초).")]
        private float _soundFadeDuration = 0.6f;
        private readonly Vector4[] _soundRegions = new Vector4[MAX_SOUND_REGIONS];
        private readonly Vector4[] _soundRegionRadii = new Vector4[MAX_SOUND_REGIONS];
        private int _soundCount;
        private bool _footstepOnly;

        public float SoundLifetime => _soundLifetime;

        [Header("인지 경계")]
        [SerializeField, Min(0.01f), Tooltip("인지 반경 안쪽에서 가장자리까지 흐려지는 거리(미터).")]
        private float _edgeFade = 0.75f;
        private SensoryManager _sensoryManager;
        private TerrainMemory _terrainMemory;

        [Header("퍼펙트 패링 파동")]
        private Vector4 _pulseRegion;
        private float _pulseVisibility;

        void Awake()
        {
            _sensoryManager = GetComponent<SensoryManager>();
        }

        void OnEnable()
        {
            _sensoryManager.PerceptionRequested += HandlePerceptionRequested;
        }

        void OnDisable()
        {
            _sensoryManager.PerceptionRequested -= HandlePerceptionRequested;
            ClearMask();
        }

        void LateUpdate()
        {
            PublishMask();
        }

        /// <summary>radius(미터)로 소리 인지 반경을 변경하고 이전 반경을 반환한다. 이전 인지 영역은 지운다.</summary>
        public float SetSoundRadius(float radius)
        {
            float previous = _soundRadius;
            _soundRadius = Mathf.Max(0f, radius);
            ClearMask();
            PublishMask();
            return previous;
        }

        /// <summary>enabled로 착지 전용 표시를 전환하고 이전 영역을 비운다.</summary>
        public void SetFootstepOnly(bool enabled)
        {
            _footstepOnly = enabled;
            ClearMask();
            PublishMask();
        }

        /// <summary>착지 position의 주변을 잠깐 표시하고 기존 수명으로 페이드한다.</summary>
        public void RevealFootstep(Vector3 position)
        {
            if (_sensoryManager.IsReady)
                RegisterRegion(position);
        }

        /// <summary>
        /// position 주변 시야와 지형 기억을 지정 radius까지 확장하며 공통 파동 반경은 변경하지 않는다.
        /// radius는 미터 단위의 최대 반경이며 0 이하이면 새 영역을 등록하지 않는다.
        /// </summary>
        public void RevealFootstep(Vector3 position, float radius)
        {
            if (_sensoryManager.IsReady && radius > 0f)
                RegisterRegion(position, radius);
        }

        /// <summary>
        /// 퍼펙트 패링 파동 영역을 center와 radius(미터), visibility(0~1)로 바꾼다.
        /// 확산과 페이드 시간은 호출하는 쪽이 매 프레임 계산하며 기존 소리 영역과 수명 규칙에는 영향을 주지 않는다.
        /// </summary>
        public void SetPulseRegion(Vector3 center, float radius, float visibility)
        {
            _pulseRegion = new Vector4(center.x, center.y, center.z, Mathf.Max(0f, radius));
            _pulseVisibility = Mathf.Clamp01(visibility);
        }

        /// <summary>퍼펙트 패링 파동 영역을 지워 더 이상 표시하지 않는다.</summary>
        public void ClearPulseRegion()
        {
            _pulseRegion = Vector4.zero;
            _pulseVisibility = 0f;
        }

        /// <summary>
        /// position의 XZ 수평거리와 현재 파동 반경·공간 경계·시간 페이드로 파동 가시성만 반환한다.
        /// 일반 소리·상시 인지·지형 기억은 제외하며 편집 모드·비활성·관측자 미준비 상태이면 0이다.
        /// </summary>
        public float GetPulseVisibility(Vector3 position)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !_sensoryManager.IsReady || _pulseRegion.w <= 0f)
                return 0f;
            Vector2 offset = new Vector2(position.x - _pulseRegion.x, position.z - _pulseRegion.z);
            float innerRadius = Mathf.Max(0f, _pulseRegion.w - Mathf.Max(_edgeFade, 0.0001f));
            float progress = Mathf.InverseLerp(innerRadius, _pulseRegion.w, offset.magnitude);
            return (1f - Mathf.SmoothStep(0f, 1f, progress)) * _pulseVisibility;
        }

        /// <summary>
        /// position의 현재 인지 가시성을 셰이더와 같은 반경·확산·페이드 규칙으로 반환한다.
        /// 지형 기억은 포함하지 않으며 인지 매니저가 준비되지 않으면 0을 반환한다.
        /// </summary>
        public float GetVisibility(Vector3 position)
        {
            if (!isActiveAndEnabled || !_sensoryManager.IsReady)
                return 0f;

            float visibility = RegionVisibility(position, _sensoryManager.Observer.position,
                _footstepOnly ? 0f : _playerRadius);
            float lifetime = _sensoryManager.CrouchPulseInterval;
            float waveEndRadius = _sensoryManager.WaveEndRadius;
            float endRadius = waveEndRadius > 0f ? waveEndRadius : _soundRadius;
            float startRadius = waveEndRadius > 0f ? _sensoryManager.WaveStartRadius : endRadius;
            for (int i = 0; i < _soundCount; i++)
            {
                Vector4 region = _soundRegions[i];
                float age = Mathf.Max(0f, Time.time - region.w);
                float expansion = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Mathf.Max(lifetime, 0.0001f)));
                Vector4 regionRadii = _soundRegionRadii[i];
                float radius = regionRadii.z > 0f
                    ? Mathf.Lerp(regionRadii.x, regionRadii.y, expansion)
                    : Mathf.Lerp(startRadius, endRadius, expansion);
                float fade = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((age - lifetime) / Mathf.Max(_soundFadeDuration, 0.0001f)));
                visibility = Mathf.Max(visibility,
                    RegionVisibility(position, new Vector3(region.x, region.y, region.z), radius) * fade);
            }
            visibility = Mathf.Max(visibility, GetPulseVisibility(position));
            return visibility;
        }

        /// <summary>
        /// position과 center의 3D 거리로 radius 안쪽의 가시성을 반환한다.
        /// 인지 경계의 _edgeFade 구간에서 셰이더와 동일하게 부드럽게 감소한다.
        /// </summary>
        private float RegionVisibility(Vector3 position, Vector3 center, float radius)
        {
            if (radius <= 0f)
                return 0f;
            float innerRadius = Mathf.Max(0f, radius - Mathf.Max(_edgeFade, 0.0001f));
            float progress = Mathf.InverseLerp(innerRadius, radius, Vector3.Distance(position, center));
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        /// <summary>
        /// 사건의 월드 위치와 발생 시간을 버퍼에 등록하고 가장 오래된 영역부터 교체한다.
        /// </summary>
        private void HandlePerceptionRequested(SensoryEvent sensoryEvent)
        {
            if (_footstepOnly)
                return;
            RegisterRegion(sensoryEvent.Position);
        }

        /// <summary>
        /// position에 임시 표시 영역을 등록하고 radius가 지정되면 해당 반경의 확산을 지형 기억에 전달한다.
        /// 가장 오래된 임시 표시 영역은 버퍼가 가득 차면 교체한다.
        /// </summary>
        private void RegisterRegion(Vector3 position, float radius = -1f)
        {
            if (_soundCount == MAX_SOUND_REGIONS)
            {
                for (int i = 1; i < _soundCount; i++)
                {
                    _soundRegions[i - 1] = _soundRegions[i];
                    _soundRegionRadii[i - 1] = _soundRegionRadii[i];
                }
                _soundCount--;
            }

            float startRadius = _sensoryManager.WaveStartRadius;
            float endRadius = _sensoryManager.WaveEndRadius;
            bool hasCustomRadius = radius >= 0f;
            if (hasCustomRadius)
            {
                endRadius = radius;
                startRadius = Mathf.Min(startRadius, endRadius);
            }
            _soundRegionRadii[_soundCount] = new Vector4(startRadius, endRadius, hasCustomRadius ? 1f : 0f, 0f);
            _soundRegions[_soundCount++] = new Vector4(position.x, position.y, position.z, Time.time);
            if (_terrainMemory == null)
                _terrainMemory = FindAnyObjectByType<TerrainMemory>();
            if (_terrainMemory != null)
            {
                _terrainMemory.RememberExpandingWave(
                    position,
                    startRadius,
                    endRadius,
                    _sensoryManager.CrouchPulseInterval);
            }
            PublishMask();
        }

        /// <summary>
        /// 만료된 사건을 제거하고 현재 관측자와 설정값 및 유효 사건을 셰이더에 전달한다.
        /// </summary>
        private void PublishMask()
        {
            if (!_sensoryManager.IsReady)
            {
                ClearMask();
                return;
            }

            float currentTime = Time.time;
            float revealLifetime = _sensoryManager.CrouchPulseInterval;
            int remainingCount = 0;
            for (int i = 0; i < _soundCount; i++)
            {
                if (currentTime - _soundRegions[i].w < revealLifetime + _soundFadeDuration)
                {
                    _soundRegionRadii[remainingCount] = _soundRegionRadii[i];
                    _soundRegions[remainingCount++] = _soundRegions[i];
                }
            }
            _soundCount = remainingCount;

            Vector3 observerPosition = _sensoryManager.Observer.position;
            Shader.SetGlobalVector(_observerRegionId,
                new Vector4(observerPosition.x, observerPosition.y, observerPosition.z, _footstepOnly ? 0f : _playerRadius));
            float waveEndRadius = _sensoryManager.WaveEndRadius;
            float revealEndRadius = waveEndRadius > 0f ? waveEndRadius : _soundRadius;
            float revealStartRadius = waveEndRadius > 0f
                ? _sensoryManager.WaveStartRadius
                : revealEndRadius;
            Shader.SetGlobalFloat(_soundStartRadiusId, revealStartRadius);
            Shader.SetGlobalFloat(_soundEndRadiusId, revealEndRadius);
            Shader.SetGlobalFloat(_soundLifetimeId, revealLifetime);
            Shader.SetGlobalFloat(_soundFadeDurationId, _soundFadeDuration);
            Shader.SetGlobalFloat(_edgeFadeId, _edgeFade);
            Shader.SetGlobalFloat(_perceptionTimeId, currentTime);
            Shader.SetGlobalVector(_pulseRegionId, _pulseRegion);
            Shader.SetGlobalFloat(_pulseVisibilityId, _pulseVisibility);
            Shader.SetGlobalInt(_soundCountId, _soundCount);
            if (_soundCount > 0)
            {
                Shader.SetGlobalVectorArray(_soundRegionsId, _soundRegions);
                Shader.SetGlobalVectorArray(_soundRegionRadiiId, _soundRegionRadii);
            }
        }

        /// <summary>
        /// 관측자 미연결 또는 세션 비활성 상태에서 모든 인지 영역을 숨기고 사건 버퍼를 비운다.
        /// </summary>
        private void ClearMask()
        {
            _soundCount = 0;
            ClearPulseRegion();
            Shader.SetGlobalVector(_observerRegionId, Vector4.zero);
            Shader.SetGlobalFloat(_pulseVisibilityId, 0f);
            Shader.SetGlobalInt(_soundCountId, 0);
        }
    }
