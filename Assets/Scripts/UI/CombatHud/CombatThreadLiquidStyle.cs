using System;

using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 원형 게이지를 실 액체로 채우는 CombatThreadLiquidBody·CombatThreadLiquidGauge의 액체·겹침 단계·수면·가득 참·실 모양 설정값이다.
    /// 액체는 토큰·칼 구슬과 같은 굵은 실 구체 여러 겹으로 이루고 겹친 수에 따라 단계별 색을 칠한다. 수면·테두리 실은 돌진 대상 실과 같은 유선형 줄기와 잔가지로 그린다.
    /// </summary>
    [Serializable]
    public sealed class CombatThreadLiquidStyle
    {
        [Header("액체")]
        [SerializeField, Range(0.3f, 1f), Tooltip("원 반지름 대비 실 액체가 차는 구의 반지름")]
        private float _liquidRadius = 0.86f;
        [SerializeField, Tooltip("액체를 이루는 굵은 실 구체 한 겹의 모양·흐름. 겹마다 실 두 가닥이다. 뒤쪽 농도는 구 뒤쪽 실의 겹침 무게이고, 색·구체 크기·나타남 시간 항목은 쓰지 않으며 가장자리 흐림은 캔버스 픽셀이다.")]
        private CombatTokenOrbitStyle _bodyThreads = new CombatTokenOrbitStyle(1.1f, 0.7f, 2f);
        [SerializeField, Range(1, 4), Tooltip("액체를 이루는 실 구체 겹 수. 시작할 때 한 번 정한다.")]
        private int _layerCount = 3;
        [SerializeField, Range(0f, 90f), Tooltip("겹마다 자전축을 서로 다른 수평축 둘레로 기울이는 각도(도). 실이 여러 방향으로 돈다.")]
        private float _layerTilt = 55f;
        [SerializeField, Range(0f, 90f), Tooltip("겹마다 자전축이 천천히 뒤척이는 최대 각도(도)")]
        private float _tumbleAngle = 30f;
        [SerializeField, Min(0f), Tooltip("자전축이 뒤척이는 속도(라디안/초)")]
        private float _tumbleSpeed = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("가장 깊은 곳 실의 겹침 무게(수면 가까운 곳 대비). 깊을수록 낮은 단계 색이 된다.")]
        private float _depthOpacity = 0.45f;

        [Header("겹침 단계")]
        [SerializeField, Range(32, 1024), Tooltip("겹침 수를 쌓는 텍스처의 한 변 픽셀 수. 시작할 때 한 번 정한다.")]
        private int _textureSize = 256;
        [SerializeField, Tooltip("실이 없는 곳(0단계)을 채우는 옅은 액체 바탕 색")]
        private Color _fillColor = new Color(0.85f, 0.88f, 0.95f, 0.08f);
        [SerializeField, Tooltip("실 한 겹(1단계) 색")]
        private Color _level1Color = new Color(0.42f, 0.45f, 0.52f, 0.8f);
        [SerializeField, Tooltip("실 두 겹(2단계) 색")]
        private Color _level2Color = new Color(0.66f, 0.69f, 0.75f, 0.9f);
        [SerializeField, Tooltip("실 세 겹(3단계) 색")]
        private Color _level3Color = new Color(0.86f, 0.88f, 0.92f, 0.96f);
        [SerializeField, Tooltip("실 네 겹 이상(4단계) 색")]
        private Color _level4Color = Color.white;
        [SerializeField, Tooltip("1·2·3·4단계 색이 시작되는 겹침 수(x·y·z·w). 앞쪽 실 한 겹이 1, 뒤쪽 실은 뒤쪽 농도만큼만 센다.")]
        private Vector4 _levelThresholds = new Vector4(0.25f, 1.15f, 2.1f, 3.05f);
        [SerializeField, Range(0f, 0.5f), Tooltip("단계 경계가 부드럽게 바뀌는 폭(겹침 수 단위). 0에 가까울수록 경계가 또렷하다.")]
        private float _levelSoftness = 0.06f;

        [Header("수면")]
        [SerializeField, Range(0.01f, 1f), Tooltip("수면에 놓인 실 줄기의 가장 굵은 곳 폭(액체 반지름 대비)")]
        private float _surfaceWidth = 0.1f;
        [SerializeField, Min(0f), Tooltip("수면 물결 높이(액체 반지름 대비)")]
        private float _waveAmplitude = 0.07f;
        [SerializeField, Min(0.05f), Tooltip("주 물결 한 개의 길이(액체 지름 대비)")]
        private float _waveLength = 0.9f;
        [SerializeField, Tooltip("물결이 수면을 따라 흐르는 속도(초당 물결 수)")]
        private float _waveSpeed = 0.6f;
        [SerializeField, Min(0.01f), Tooltip("게이지가 늘면 수면이 그 높이까지 차오르는 빠르기")]
        private float _fillSharpness = 4f;
        [SerializeField, Min(0.01f), Tooltip("게이지를 쓰면 수면이 빠지는 빠르기")]
        private float _drainSharpness = 8f;
        [SerializeField, Min(0f), Tooltip("차오른 양(0~1) 대비 수면이 출렁이는 세기")]
        private float _sloshStrength = 3f;
        [SerializeField, Min(0f), Tooltip("출렁임이 잦아드는 빠르기")]
        private float _sloshDamping = 2.2f;
        [SerializeField, Min(0f), Tooltip("출렁이는 동안 액체 실과 물결이 빨라지는 정도(출렁임 1당 배율 증가)")]
        private float _sloshSwirl = 1.5f;
        [SerializeField, Min(0.1f), Tooltip("수면과 액체 원 경계에서 실이 잘리며 흐려지는 폭(캔버스 픽셀)")]
        private float _clipSoftness = 1.2f;

        [Header("가득 참")]
        [SerializeField, Tooltip("가득 찼을 때 원형 테두리를 감는 실을 그린다. 끄면 빛무리와 빨라지는 흐름만 남는다.")]
        private bool _rimThread = true;
        [SerializeField, Tooltip("켜면 이 원이 끝까지 찼을 때 테두리 실을 그리고, 끄면 가득 참(전체)일 때 그린다.")]
        private bool _rimWhenFilled;
        [SerializeField, Tooltip("켜면 머리들이 가는 실로 이어진 한 가닥 실이 되고, 끄면 머리마다 따로 떨어진 선이 돈다.")]
        private bool _rimConnected = true;
        [SerializeField, Range(0.3f, 1.2f), Tooltip("원 반지름 대비 테두리 실이 도는 원의 반지름")]
        private float _rimRadius = 0.95f;
        [SerializeField, Range(0.01f, 0.6f), Tooltip("테두리 실 머리의 가장 굵은 곳 폭(원 반지름 대비)")]
        private float _rimWidth = 0.24f;
        [SerializeField, Range(1, 4), Tooltip("테두리를 감는 한 가닥 실에 달린 굵은 마디(머리) 수. 시작할 때 한 번 정한다.")]
        private int _rimHeadCount = 3;
        [SerializeField, Range(0.02f, 0.5f), Tooltip("머리마다 굵은 부분의 길이(둘레 대비). 나머지는 다음 머리까지 이어지는 가는 실이다.")]
        private float _rimStrokeLength = 0.2f;
        [SerializeField, Range(0.01f, 0.5f), Tooltip("머리 끝부터 가장 굵어지는 곳까지의 길이(굵은 부분 대비). 작을수록 머리가 뭉툭하다.")]
        private float _rimHeadLength = 0.12f;
        [SerializeField, Range(0.05f, 1f), Tooltip("굵은 부분 끝에서 가는 실로 가늘어지는 구간의 길이(굵은 부분 대비). 작을수록 굵은 곳과 가는 곳이 또렷이 나뉜다.")]
        private float _rimTaperLength = 0.3f;
        [SerializeField, Min(0.5f), Tooltip("가늘어지는 구간에서 굵기가 줄어드는 급한 정도")]
        private float _rimTaper = 1.2f;
        [SerializeField, Range(0f, 1f), Tooltip("머리 사이를 잇는 가는 실의 폭(머리 대비)")]
        private float _rimHairline = 0.18f;
        [SerializeField, Range(0f, 0.9f), Tooltip("실을 따라 흐르는 파동이 굵기를 바꾸는 정도. 0.5면 50~150% 사이를 오간다.")]
        private float _rimWaveDepth = 0.5f;
        [SerializeField, Min(0f), Tooltip("한 바퀴에 들어가는 굵기 파동 수")]
        private float _rimWaveCount = 3f;
        [SerializeField, Tooltip("굵기 파동이 실을 따라 흐르는 속도(초당 파동 수)")]
        private float _rimWaveSpeed = 1.2f;
        [SerializeField, Range(0f, 1f), Tooltip("빛 맥동에 맞춰 테두리 실 전체가 굵어졌다 가늘어지는 정도")]
        private float _rimPulse = 0.2f;
        [SerializeField, Min(0f), Tooltip("테두리 실의 평균 공전 속도(초당 바퀴)")]
        private float _rimSpeed = 0.45f;
        [SerializeField, Range(0f, 0.45f), Tooltip("머리마다 고른 간격 자리에서 앞서거나 처질 수 있는 최대 거리(머리 간격 대비). 평균 간격은 유지된다.")]
        private float _rimHeadWander = 0.35f;
        [SerializeField, Min(0f), Tooltip("머리를 무작위로 밀어 앞서거나 처지게 하는 세기(초당 바퀴). 당김 간격은 Tug Mean Interval을 따른다.")]
        private float _rimHeadKick = 0.25f;
        [SerializeField, Min(0f), Tooltip("밀린 머리가 고른 간격 자리로 돌아가려는 세기")]
        private float _rimHeadSpring = 6f;
        [SerializeField, Min(1f), Tooltip("테두리 실 아래에 까는 빛 띠의 폭(실 폭 대비)")]
        private float _rimGlowWidth = 2.2f;
        [SerializeField, Range(0f, 1f), Tooltip("테두리 실 빛 띠의 농도")]
        private float _rimGlowOpacity = 0.3f;
        [SerializeField, Min(0f), Tooltip("테두리 원을 따라 퍼지는 고리 빛무리의 안팎 폭(원 반지름 대비)")]
        private float _ringGlowWidth = 0.22f;
        [SerializeField, Range(0f, 1f), Tooltip("가득 찼을 때 테두리 고리 빛무리의 농도")]
        private float _ringGlowOpacity = 0.18f;
        [SerializeField, Range(0f, 1f), Tooltip("액체가 끝까지 찬 원에 은은하게 퍼지는 고리 빛무리의 농도. 가득 참 빛무리와 따로 더해진다.")]
        private float _filledGlowOpacity;
        [SerializeField, Range(0f, 1f), Tooltip("빛이 맥동하며 옅어지는 정도. 0이면 맥동하지 않는다.")]
        private float _glowPulseDepth = 0.35f;
        [SerializeField, Min(0f), Tooltip("빛 맥동 속도(초당 횟수)")]
        private float _glowPulseSpeed = 1.2f;
        [SerializeField, Min(1f), Tooltip("가득 찼을 때 액체 실이 흐르는 속도 배율")]
        private float _fullFlowMultiplier = 1.6f;
        [SerializeField, Min(0.01f), Tooltip("가득 차거나 비워질 때 테두리 실·빛이 나타나고 사라지는 실시간(초)")]
        private float _fullTransitionDuration = 0.3f;

        [Header("수면·테두리 실 모양")]
        [SerializeField, Range(0.05f, 1f), Tooltip("굵은 줄기 대비 잔가지 두께")]
        private float _branchWidthRatio = 0.2f;
        [SerializeField, Min(0f), Tooltip("잔가지가 줄기에서 벗어나는 최대 거리(줄기 폭 대비)")]
        private float _branchSpread = 0.75f;
        [SerializeField, Range(0f, 0.9f), Tooltip("줄기·잔가지 두께가 부드럽게 굵어졌다 가늘어지는 정도")]
        private float _thicknessVariation = 0.35f;
        [SerializeField, Min(0f), Tooltip("획 가장자리를 부드럽게 흐리는 폭(캔버스 픽셀)")]
        private float _feather = 1f;
        [SerializeField] private Color _color = Color.white;
        [SerializeField, Min(0f), Tooltip("테두리 실을 한 번 당길 때 더해지는 속도(평균 속도 대비). 당길 때마다 30~120% 사이로 무작위다.")]
        private float _tugStrength = 1.6f;
        [SerializeField, Min(0.05f), Tooltip("다음 당김까지 평균 간격(초). 간격은 지수 분포라 몰려서 오거나 오래 쉬기도 한다.")]
        private float _tugMeanInterval = 1.2f;
        [SerializeField, Range(0f, 0.9f), Tooltip("테두리 실의 평균 속도가 천천히 오르내리는 정도")]
        private float _speedWander = 0.4f;

        public float LiquidRadius => _liquidRadius;
        public CombatTokenOrbitStyle BodyThreads => _bodyThreads;
        public int LayerCount => _layerCount;
        public float LayerTilt => _layerTilt;
        public float TumbleAngle => _tumbleAngle;
        public float TumbleSpeed => _tumbleSpeed;
        public float DepthOpacity => _depthOpacity;
        public int TextureSize => _textureSize;
        public Color FillColor => _fillColor;
        public Color Level1Color => _level1Color;
        public Color Level2Color => _level2Color;
        public Color Level3Color => _level3Color;
        public Color Level4Color => _level4Color;
        public Vector4 LevelThresholds => _levelThresholds;
        public float LevelSoftness => _levelSoftness;
        public float SurfaceWidth => _surfaceWidth;
        public float WaveAmplitude => _waveAmplitude;
        public float WaveLength => _waveLength;
        public float WaveSpeed => _waveSpeed;
        public float FillSharpness => _fillSharpness;
        public float DrainSharpness => _drainSharpness;
        public float SloshStrength => _sloshStrength;
        public float SloshDamping => _sloshDamping;
        public float SloshSwirl => _sloshSwirl;
        public float ClipSoftness => _clipSoftness;
        public bool RimThread => _rimThread;
        public bool RimWhenFilled => _rimWhenFilled;
        public bool RimConnected => _rimConnected;
        public float RimRadius => _rimRadius;
        public float RimWidth => _rimWidth;
        public int RimHeadCount => _rimHeadCount;
        public float RimStrokeLength => _rimStrokeLength;
        public float RimHeadLength => _rimHeadLength;
        public float RimTaperLength => _rimTaperLength;
        public float RimTaper => _rimTaper;
        public float RimHairline => _rimHairline;
        public float RimWaveDepth => _rimWaveDepth;
        public float RimWaveCount => _rimWaveCount;
        public float RimWaveSpeed => _rimWaveSpeed;
        public float RimPulse => _rimPulse;
        public float RimSpeed => _rimSpeed;
        public float RimHeadWander => _rimHeadWander;
        public float RimHeadKick => _rimHeadKick;
        public float RimHeadSpring => _rimHeadSpring;
        public float RimGlowWidth => _rimGlowWidth;
        public float RimGlowOpacity => _rimGlowOpacity;
        public float RingGlowWidth => _ringGlowWidth;
        public float RingGlowOpacity => _ringGlowOpacity;
        public float FilledGlowOpacity => _filledGlowOpacity;
        public float GlowPulseDepth => _glowPulseDepth;
        public float GlowPulseSpeed => _glowPulseSpeed;
        public float FullFlowMultiplier => _fullFlowMultiplier;
        public float FullTransitionDuration => _fullTransitionDuration;
        public float BranchWidthRatio => _branchWidthRatio;
        public float BranchSpread => _branchSpread;
        public float ThicknessVariation => _thicknessVariation;
        public float Feather => _feather;
        public Color Color => _color;
        public float TugStrength => _tugStrength;
        public float TugMeanInterval => _tugMeanInterval;
        public float SpeedWander => _speedWander;
    }
}
