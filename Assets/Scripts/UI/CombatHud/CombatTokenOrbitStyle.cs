using System;

using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 찬 퍼펙트 패링 토큰을 이루는 실 구체의 크기·궤도·모양·흐름 설정값이다.
    /// 칼집을 도는 돌진 대상 실과 같은 유선형 줄기·잔가지·두께 변화·불규칙한 흐름을 토큰 크기에 맞춘 값으로 보관한다.
    /// </summary>
    [Serializable]
    public sealed class CombatTokenOrbitStyle
    {
        [Header("구체")]
        [SerializeField, Min(0.1f), Tooltip("토큰 슬롯 지름 대비 실 구체 지름")]
        private float _sphereScale = 1f;
        [SerializeField, Range(-90f, 90f), Tooltip("구체 자전축이 화면에서 기울어진 각도(도)")]
        private float _axisRoll = 20f;
        [SerializeField, Range(-90f, 90f), Tooltip("구체 자전축이 화면 쪽으로 숙여진 각도(도). 0이 아니면 극 주변이 보인다.")]
        private float _axisPitch = 25f;
        [SerializeField, Range(5f, 90f), Tooltip("각 실이 도는 큰 원이 자전축에서 기울어진 각도(도)")]
        private float _inclination = 65f;
        [SerializeField, Tooltip("실이 한 바퀴 돌 때 자기 궤도면이 자전축 둘레로 돌아가는 비율. 실이 구를 감싸듯 나선으로 돈다.")]
        private float _twist = 0.3f;
        [SerializeField, Tooltip("두 실의 궤도면이 자전축 둘레로 함께 도는 속도(라디안/초)")]
        private float _precessionSpeed = 0.4f;
        [SerializeField, Range(0f, 360f), Tooltip("두 번째 실 궤도면의 자전축 둘레 각도 차이(도)")]
        private float _counterAzimuth = 90f;

        [Header("모양")]
        [SerializeField, Range(16, 128), Tooltip("실 하나를 나누는 점 개수")]
        private int _segments = 64;
        [SerializeField, Range(0.05f, 3f), Tooltip("굵은 줄기의 가장 굵은 곳 폭(구 반지름 대비, 구 표면 위). 클수록 두 실이 구를 넓게 덮는다.")]
        private float _mainWidth = 1.2f;
        [SerializeField, Range(0.05f, 1f), Tooltip("굵은 줄기 대비 잔가지 두께")]
        private float _branchWidthRatio = 0.2f;
        [SerializeField, Range(0.05f, 0.95f), Tooltip("궤도 한 바퀴 대비 줄기 획의 길이")]
        private float _strokeLength = 0.75f;
        [SerializeField, Range(0f, 0.9f), Tooltip("줄기·잔가지 두께가 부드럽게 굵어졌다 가늘어지는 정도")]
        private float _thicknessVariation = 0.35f;
        [SerializeField, Min(0f), Tooltip("잔가지가 줄기에서 벗어나는 최대 거리(줄기 폭 대비)")]
        private float _branchSpread = 0.75f;
        [SerializeField, Range(0f, 1f), Tooltip("구 가장 뒤쪽을 지날 때의 농도")]
        private float _backOpacity = 0.3f;
        [SerializeField, Min(0f), Tooltip("획 가장자리를 부드럽게 흐리는 폭. 토큰 HUD는 캔버스 픽셀, 칼 구슬은 구 반지름 대비 비율이다.")]
        private float _feather = 0.8f;
        [SerializeField] private Color _color = Color.white;

        [Header("흐름")]
        [SerializeField, Min(0f), Tooltip("첫 실의 평균 공전 속도(초당 바퀴 수)")]
        private float _orbitSpeed = 0.32f;
        [SerializeField, Range(0.1f, 3f), Tooltip("첫 실 대비 두 번째 실의 평균 공전 속도")]
        private float _counterSpeedRatio = 0.8f;
        [SerializeField, Min(0f), Tooltip("한 번 당길 때 더해지는 속도(평균 공전 속도 대비). 당길 때마다 30~120% 사이로 무작위다.")]
        private float _tugStrength = 1.6f;
        [SerializeField, Min(0.05f), Tooltip("다음 당김까지 평균 간격(초). 간격은 지수 분포라 몰려서 오거나 오래 쉬기도 한다.")]
        private float _tugMeanInterval = 1.2f;
        [SerializeField, Range(0f, 0.9f), Tooltip("실마다 평균 속도가 따로 천천히 오르내리는 정도")]
        private float _speedWander = 0.4f;
        [SerializeField, Min(0.01f), Tooltip("토큰을 얻거나 쓸 때 실이 나타나고 사라지는 실시간(초)")]
        private float _fadeDuration = 0.25f;

        public float SphereScale => _sphereScale;
        public float AxisRoll => _axisRoll;
        public float AxisPitch => _axisPitch;
        public float Inclination => _inclination;
        public float Twist => _twist;
        public float PrecessionSpeed => _precessionSpeed;
        public float CounterAzimuth => _counterAzimuth;
        public int Segments => _segments;
        public float MainWidth => _mainWidth;
        public float BranchWidthRatio => _branchWidthRatio;
        public float StrokeLength => _strokeLength;
        public float ThicknessVariation => _thicknessVariation;
        public float BranchSpread => _branchSpread;
        public float BackOpacity => _backOpacity;
        public float Feather => _feather;
        public Color Color => _color;
        public float OrbitSpeed => _orbitSpeed;
        public float CounterSpeedRatio => _counterSpeedRatio;
        public float TugStrength => _tugStrength;
        public float TugMeanInterval => _tugMeanInterval;
        public float SpeedWander => _speedWander;
        public float FadeDuration => _fadeDuration;

        /// <summary>토큰 HUD 기본값으로 만든다.</summary>
        public CombatTokenOrbitStyle()
        {
        }

        /// <summary>
        /// 다른 용도(칼 구슬 등)의 기본값으로 만든다. mainWidth·strokeLength·feather만 바꾸고 나머지는 토큰 HUD 기본값을 쓴다.
        /// </summary>
        public CombatTokenOrbitStyle(float mainWidth, float strokeLength, float feather)
        {
            _mainWidth = mainWidth;
            _strokeLength = strokeLength;
            _feather = feather;
        }
    }
}
