using UnityEngine;

namespace BlindBlade.Perception
{
    [CreateAssetMenu(menuName = "BlindBlade/Threat Presentation Settings")]
    public sealed class ThreatPresentationSettings : ScriptableObject
    {
        [Header("근접 적 공통 인지")]
        [SerializeField, Min(0f)] private float _defaultEnemyRadius = 12f;
        [SerializeField, Min(0f)] private float _enemyEdgeFade = 1f;

        [Header("적 유형별 인지 덮어쓰기")]
        [SerializeField] private bool _overrideNormalRadius;
        [SerializeField, Min(0f)] private float _normalRadius = 12f;
        [SerializeField] private bool _overrideChargeRadius;
        [SerializeField, Min(0f)] private float _chargeRadius = 12f;

        [Header("공통 거리 색상")]
        [SerializeField, Min(0f)] private float _nearColorDistance = 3f;
        [SerializeField, Min(0.01f)] private float _farColorDistance = 12f;
        [SerializeField] private Color _nearColor = new Color(1f, 0.1f, 0.06f, 1f);
        [SerializeField] private Color _farColor = Color.white;
        [SerializeField, Tooltip("가로축은 근접도(먼 거리 0, 가까운 거리 1), 세로축은 붉은색 혼합 비율이다.")]
        private AnimationCurve _colorCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private Color _reflectedColor = Color.white;
        [SerializeField, Range(0f, 1f)] private float _fillOpacity = 0f;

        [Header("화면 경계 전환")]
        [SerializeField, Range(0f, 0.25f), Tooltip("화면 밖으로 이 비율만큼 더 벗어나면 방향 UI로 전환한다. 0.02는 폭·높이의 2%다.")]
        private float _viewportExitMargin = 0.02f;

        [Header("발소리 청각 인지")]
        [SerializeField, Min(0f), Tooltip("적의 현재 위치와 관측자 루트의 수평 거리로 판단하는 청각 범위다.")]
        private float _footstepHearingRadius = 20f;
        [SerializeField, Min(0.01f), Tooltip("새 발걸음에서 복사하는 게임 시간 기준 후보 총수명이다.")]
        private float _footstepLifetime = 0.8f;
        [SerializeField, Min(0f), Tooltip("새 발걸음에서 복사하는 총수명 끝부분의 게임 시간 페이드 길이다.")]
        private float _footstepFadeDuration = 0.3f;

        public float ViewportExitMargin => _viewportExitMargin;
        public Color ReflectedColor => _reflectedColor;
        public float FillOpacity => _fillOpacity;
        public float FootstepHearingRadius => _footstepHearingRadius;
        public float FootstepLifetime => _footstepLifetime;
        public float FootstepFadeDuration => _footstepFadeDuration;

        void OnValidate()
        {
            _defaultEnemyRadius = Mathf.Max(0f, _defaultEnemyRadius);
            _enemyEdgeFade = Mathf.Max(0f, _enemyEdgeFade);
            _normalRadius = Mathf.Max(0f, _normalRadius);
            _chargeRadius = Mathf.Max(0f, _chargeRadius);
            _nearColorDistance = Mathf.Max(0f, _nearColorDistance);
            _farColorDistance = Mathf.Max(_nearColorDistance + 0.01f, _farColorDistance);
            _viewportExitMargin = Mathf.Clamp(_viewportExitMargin, 0f, 0.25f);
            _fillOpacity = Mathf.Clamp01(_fillOpacity);
            _footstepHearingRadius = Mathf.Max(0f, _footstepHearingRadius);
            _footstepLifetime = Mathf.Max(0.01f, _footstepLifetime);
            _footstepFadeDuration = Mathf.Clamp(_footstepFadeDuration, 0f, _footstepLifetime);
        }

        /// <summary>
        /// meleeType에 해당하는 유형 덮어쓰기 또는 공통 인지 반경을 반환한다.
        /// source에 Type 또는 Default를 기록하며 개체별 지정은 호출자가 우선 적용한다.
        /// </summary>
        public float GetEnemyRadius(MeleeThreatType meleeType, out ThreatRadiusSource source)
        {
            bool useType = meleeType == MeleeThreatType.Charge ? _overrideChargeRadius : _overrideNormalRadius;
            source = useType ? ThreatRadiusSource.Type : ThreatRadiusSource.Default;
            return Mathf.Max(0f, useType
                ? (meleeType == MeleeThreatType.Charge ? _chargeRadius : _normalRadius)
                : _defaultEnemyRadius);
        }

        /// <summary>
        /// distance와 radius로 근접 적의 거리 가시성을 0~1로 반환한다.
        /// 인지 반경은 넘지 않으며 경계 페이드 폭을 radius 이하로 제한한다.
        /// </summary>
        public float GetEnemyVisibility(float distance, float radius)
        {
            if (radius <= 0f || distance >= radius)
                return 0f;
            float width = Mathf.Min(_enemyEdgeFade, radius);
            return width <= 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(radius - width, radius, distance));
        }

        /// <summary>
        /// distance의 공통 근접도를 곡선에 적용해 먼색에서 가까운색으로 보간한 색상을 반환한다.
        /// 개체별 인지 반경을 사용하지 않아 같은 거리의 대상은 같은 색을 얻는다.
        /// </summary>
        public Color GetDistanceColor(float distance)
        {
            float closeness = GetProximity(distance);
            float blend = Mathf.Clamp01(_colorCurve.Evaluate(closeness));
            return Color.Lerp(_farColor, _nearColor, blend);
        }

        /// <summary>수평 distance를 공통 색상 거리 구간에 대조해 가까울수록 1인 HUD 근접도를 반환한다.</summary>
        public float GetProximity(float distance)
        {
            return 1f - Mathf.InverseLerp(_nearColorDistance, _farColorDistance, distance);
        }
    }
}
