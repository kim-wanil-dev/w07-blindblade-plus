using UnityEngine;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 원형 게이지의 실 액체 수면을 계산해 CombatThreadLiquidBody가 굵은 실 구체로 아래에서 위로 차오르게 그리게 하고, 그 위에 수면 실과 테두리 실을 그린다.
    /// 수면에는 물결을 따라 실 한 가닥이 놓이고, 끝까지 찬 원은 은은하게 빛난다. 가득 차면 빛무리가 더 퍼지고, RimThread가 켜져 있으면 굵은 마디 여러 개를 단 한 가닥 실이 원형 테두리를 감고 돈다.
    /// 마디는 평균 간격을 지키면서 칼집 실처럼 무작위로 앞서거나 처지고, 실을 따라 흐르는 파동이 굵기를 요란하게 바꾼다.
    /// 모든 움직임은 SetState가 넘기는 시간을 따른다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatThreadLiquidGauge : MaskableGraphic
    {
        private const float TWO_PI = Mathf.PI * 2f;
        private const float BRANCH_START = 0.15f;
        private const float BRANCH_END = 0.85f;
        private const float THICKNESS_FLOW_SPEED = 1.2f;
        private const float BRANCH_BREATH_SPEED = 2.2f;
        private const float MIN_LEVEL = 0.0005f;
        private const float LEVEL_MARGIN = 1.04f;
        private const int SURFACE_SEGMENTS = 40;
        private const int RIM_SEGMENTS = 120;
        private const int MAX_RIM_HEADS = 4;
        private const float RIM_HEAD_DAMPING = 2.5f;
        private const float MIN_RIM_WAVE = 0.15f;
        private const int GLOW_SEGMENTS = 48;
        private const float SURFACE_TAPER = 0.12f;
        private const float SECOND_WAVE_SCALE = 0.57f;
        private const float SLOSH_TILT = 0.15f;
        private const float SLOSH_FREQUENCY = 1.4f;
        private const float MAX_SLOSH = 1.5f;
        private const float WAVE_PHASE_PERIOD = 100f;
        // 테두리 고리 빛무리의 고리별 반지름 위치(빛 폭 대비)와 농도다.
        private static readonly float[] RING_GLOW_OFFSETS = { -1f, -0.35f, 0f, 0.35f, 1f };
        private static readonly float[] RING_GLOW_ALPHAS = { 0f, 0.55f, 1f, 0.55f, 0f };

        [Header("상태")]
        private CombatThreadLiquidStyle _style;
        private CombatThreadLiquidBody _body;
        private ThreadFlow _rimFlow;
        private float[] _rimOffsets;
        private float[] _rimVelocities;
        private float[] _rimNextKicks;
        private float[] _rimHeads;
        private Vector2[] _pathPoints;
        private Vector2[] _pathSides;
        private float[] _pathProfiles;
        private float[] _pathBranches;
        private bool _hasState;
        private float _level;
        private float _target;
        private float _slosh;
        private float _fullBlend;
        private float _filledBlend;
        private float _time;
        private float _glowTime;
        private Vector4 _surface;
        private float _surfaceTilt;
        private Vector2 _center;
        private float _radius;
        private float _liquidRadius;

        /// <summary>
        /// style 설정과 액체 본체 body를 저장하고 테두리 실 흐름, RimHeadCount개 머리의 간격 흔들림 상태와 경로 계산 버퍼를 만든다. 클릭을 막지 않도록 raycastTarget을 끈다.
        /// </summary>
        public void Initialize(CombatThreadLiquidStyle style, CombatThreadLiquidBody body)
        {
            _style = style;
            _body = body;
            _rimFlow = new ThreadFlow(41.3f, 0f, style.TugMeanInterval);
            int headCount = Mathf.Clamp(style.RimHeadCount, 1, MAX_RIM_HEADS);
            _rimOffsets = new float[headCount];
            _rimVelocities = new float[headCount];
            _rimNextKicks = new float[headCount];
            _rimHeads = new float[headCount];
            for (int head = 0; head < headCount; head++)
                _rimNextKicks[head] = Random.Range(0.1f, Mathf.Max(0.1f, style.TugMeanInterval));
            int capacity = Mathf.Max(SURFACE_SEGMENTS, RIM_SEGMENTS) + 1;
            _pathPoints = new Vector2[capacity];
            _pathSides = new Vector2[capacity];
            _pathProfiles = new float[capacity];
            _pathBranches = new float[capacity];
            raycastTarget = false;
        }

        /// <summary>
        /// 게이지 양 fill(0~1)을 목표 수면으로 삼아 deltaTime 동안 수면을 차오르게 하거나 빼고, 새로 차오른 양만큼 출렁임을 더한다.
        /// full이면 테두리 실과 빛을 나타내고 액체 흐름을 빠르게 한다. 액체 본체와 테두리 실의 흐름을 적분하고 수면을 계산해 본체를 다시 그린 뒤 메시를 다시 그리게 한다.
        /// 처음 호출되면 수면을 fill에 바로 맞춘다.
        /// </summary>
        public void SetState(float fill, bool full, float deltaTime)
        {
            fill = Mathf.Clamp01(fill);
            if (!_hasState)
            {
                _hasState = true;
                _level = fill;
                _target = fill;
            }

            bool wasShown = _level > MIN_LEVEL || _fullBlend > 0f;
            // 새로 차오른 양만큼 수면이 출렁이고 시간이 지나면 잦아든다.
            if (fill > _target)
                _slosh = Mathf.Min(MAX_SLOSH, _slosh + (fill - _target) * _style.SloshStrength);
            _target = fill;
            _slosh *= Mathf.Exp(-_style.SloshDamping * deltaTime);
            float sharpness = fill >= _level ? _style.FillSharpness : _style.DrainSharpness;
            _level = Mathf.Lerp(_level, fill, 1f - Mathf.Exp(-sharpness * deltaTime));
            if (Mathf.Abs(_level - fill) < MIN_LEVEL)
                _level = fill;

            _fullBlend = Mathf.MoveTowards(_fullBlend, full ? 1f : 0f, deltaTime / _style.FullTransitionDuration);
            // 차오르는 수면은 1에 지수적으로 다가가 늦으므로 끝까지 찬 상태는 실제 게이지 양으로 판단한다.
            _filledBlend = Mathf.MoveTowards(_filledBlend, fill >= 1f - MIN_LEVEL ? 1f : 0f, deltaTime / _style.FullTransitionDuration);
            // 출렁이는 동안에는 액체 실과 물결이 함께 빨라진다.
            float flowDelta = deltaTime * Mathf.Lerp(1f, _style.FullFlowMultiplier, _fullBlend) * (1f + _slosh * _style.SloshSwirl);
            _time += flowDelta;
            _glowTime += deltaTime;
            _body.Step(_time, flowDelta);
            _rimFlow.Step(_style.RimSpeed, _style, _glowTime, deltaTime);
            StepRimHeads(_glowTime, deltaTime);

            UpdateSurface();
            _body.Render(_time, _surface, _surfaceTilt, _level > MIN_LEVEL);
            if (wasShown || _level > MIN_LEVEL || _fullBlend > 0f)
                SetVerticesDirty();
        }

        /// <summary>
        /// 테두리 실 머리마다 고른 간격 자리에서 벗어난 거리 _rimOffsets(바퀴)를 time 기준 deltaTime만큼 진행한다.
        /// 제자리로 돌아가려는 용수철과 감쇠에, 지수 분포 간격으로 앞뒤 무작위 밀기를 더해 머리마다 따로 앞서거나 처지게 한다.
        /// 벗어난 거리는 머리 간격의 RimHeadWander배 안으로 묶어 머리끼리 순서가 바뀌지 않고 평균 간격을 지킨다.
        /// </summary>
        private void StepRimHeads(float time, float deltaTime)
        {
            float limit = _style.RimHeadWander / _rimOffsets.Length;
            for (int head = 0; head < _rimOffsets.Length; head++)
            {
                _rimVelocities[head] -= _style.RimHeadSpring * _rimOffsets[head] * deltaTime;
                _rimVelocities[head] *= Mathf.Exp(-RIM_HEAD_DAMPING * deltaTime);
                if (time >= _rimNextKicks[head])
                {
                    _rimVelocities[head] += _style.RimHeadKick * Random.Range(-1f, 1f);
                    // 지수 분포 간격이라 밀기가 몰려서 오거나 오래 쉬기도 한다.
                    float interval = -Mathf.Log(1f - Random.value * 0.999f) * _style.TugMeanInterval;
                    _rimNextKicks[head] = time + Mathf.Clamp(interval, 0.15f, 4f);
                }

                _rimOffsets[head] += _rimVelocities[head] * deltaTime;
                if (Mathf.Abs(_rimOffsets[head]) > limit)
                {
                    _rimOffsets[head] = Mathf.Sign(_rimOffsets[head]) * limit;
                    _rimVelocities[head] = 0f;
                }
            }
        }

        /// <summary>
        /// 현재 수면 높이·물결·출렁임 기울기를 액체 반지름 단위로 계산해 _surface(높이·물결 높이·물결 길이·위상)와 _surfaceTilt에 저장한다.
        /// 액체 본체의 합성 셰이더와 수면 실이 같은 값을 쓴다.
        /// </summary>
        private void UpdateSurface()
        {
            // 거의 비었거나 가득 찼을 때는 물결을 잔잔하게 한다.
            float calm = Mathf.Clamp01(Mathf.Min(_level, 1f - _level) * 8f);
            _surface.x = Mathf.Lerp(-LEVEL_MARGIN, LEVEL_MARGIN, _level);
            _surface.y = _style.WaveAmplitude * (1f + _slosh) * calm;
            _surface.z = 2f * _style.WaveLength;
            // 두 번째 물결 배율 0.73과 함께 이어지도록 100주기마다 되감아 셰이더 sin 정밀도를 지킨다.
            _surface.w = Mathf.Repeat(_time * _style.WaveSpeed, WAVE_PHASE_PERIOD);
            _surfaceTilt = _slosh * SLOSH_TILT * Mathf.Sin(_time * SLOSH_FREQUENCY * TWO_PI) * calm;
        }

        /// <summary>
        /// 액체 중심 기준 가로 위치 x(액체 반지름 단위)의 수면 높이를 같은 단위로 반환한다. 셰이더 BlindBlade/UIThreadLiquid와 같은 식이다.
        /// </summary>
        private float SurfaceHeight(float x)
        {
            float wave = 0.65f * Mathf.Sin(TWO_PI * (x / _surface.z - _surface.w))
                + 0.35f * Mathf.Sin(TWO_PI * (x / (_surface.z * SECOND_WAVE_SCALE) + _surface.w * 0.73f) + 1.3f);
            return _surface.x + _surface.y * wave + _surfaceTilt * x;
        }

        /// <summary>
        /// 끝까지 찬 원과 가득 참의 고리 빛무리, 수면 실, 테두리 실(RimThread가 켜져 있을 때) 순서로 vh에 메시를 채운다. 액체 본체는 이 그래픽 아래의 CombatThreadLiquidBody가 그린다.
        /// 원 반지름은 RectTransform 크기의 절반이고 액체 반지름은 그 LiquidRadius배다. 초기화 전이거나 보일 것이 없으면 비운다.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // AddComponent 직후 Initialize 전에 한 번 그려질 수 있다.
            if (_style == null || (_level <= MIN_LEVEL && _fullBlend <= 0f))
                return;

            Rect rect = rectTransform.rect;
            _center = rect.center;
            _radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            _liquidRadius = _radius * _style.LiquidRadius;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_glowTime * _style.GlowPulseSpeed * TWO_PI);
            float breath = 1f - _style.GlowPulseDepth * pulse;
            float glow = _fullBlend * breath;
            // 끝까지 찬 원은 은은하게, 가득 참일 때는 그 위에 더 밝게 빛난다.
            float ringGlow = glow * _style.RingGlowOpacity + _filledBlend * breath * _style.FilledGlowOpacity;
            if (ringGlow > 0f)
                BuildRingGlow(vh, ringGlow);

            if (_level > MIN_LEVEL)
                BuildSurface(vh);

            // 원마다 끝까지 찼을 때 도는 실(일섬 칸)과 전체가 가득 찼을 때 도는 실(거합)을 설정으로 고른다.
            float rimBlend = _style.RimWhenFilled ? _filledBlend : _fullBlend;
            if (rimBlend > 0f && _style.RimThread)
                BuildRim(vh, rimBlend, rimBlend * breath, pulse);
        }

        /// <summary>
        /// 현재 수면 높이에서 액체 원 안쪽 너비만큼 물결치는 실 한 가닥을 vh에 추가한다. 양 끝은 원 가장자리에서 가늘어진다.
        /// 수면이 바닥이나 꼭대기에 가까워 너비가 없으면 그리지 않는다.
        /// </summary>
        private void BuildSurface(VertexHelper vh)
        {
            float chord = Mathf.Sqrt(Mathf.Max(0f, 1f - _surface.x * _surface.x));
            if (chord * _liquidRadius < 1f)
                return;

            int count = SURFACE_SEGMENTS + 1;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SURFACE_SEGMENTS;
                float x = Mathf.Lerp(-chord, chord, t);
                _pathPoints[i] = _center + new Vector2(x, SurfaceHeight(x)) * _liquidRadius;
                _pathProfiles[i] = Mathf.SmoothStep(0f, 1f, t / SURFACE_TAPER) * Mathf.SmoothStep(0f, 1f, (1f - t) / SURFACE_TAPER);
                _pathBranches[i] = t;
            }

            for (int i = 0; i < count; i++)
            {
                Vector2 tangent = _pathPoints[Mathf.Min(i + 1, count - 1)] - _pathPoints[Mathf.Max(i - 1, 0)];
                _pathSides[i] = new Vector2(-tangent.y, tangent.x).normalized;
            }

            // 수면이 바닥이나 꼭대기에 가까워 짧아질수록 수면 실도 가늘게 한다.
            float halfMain = _liquidRadius * _style.SurfaceWidth * 0.5f * Mathf.Clamp01(chord / 0.35f);
            AppendStroke(vh, count, halfMain, _style.Feather, _style.Color * color, 91.7f, true);
        }

        /// <summary>
        /// 원형 테두리를 도는 실을 blend(0~1)만큼 굵고 진하게 vh에 추가한다. 굵은 머리 RimHeadCount개가 _rimFlow를 따라 시계 방향으로 돌며
        /// 머리마다 _rimOffsets만큼 고른 간격 자리에서 앞서거나 처진다. RimConnected면 각 머리 뒤가 가는 실로 다음 머리까지 이어진 한 가닥 실이고,
        /// 아니면 머리마다 RimStrokeLength 길이에서 끝나는 따로 떨어진 선이다.
        /// 실을 따라 흐르는 파동이 굵기를 RimWaveDepth만큼 바꾸고, glow가 있으면 실 아래에 넓고 옅은 빛 띠를 먼저 깔며, pulse(0~1) 맥동에 맞춰 실 굵기를 RimPulse만큼 바꾼다.
        /// </summary>
        private void BuildRim(VertexHelper vh, float blend, float glow, float pulse)
        {
            float radius = _radius * _style.RimRadius;
            // 빛이 가장 밝을 때 실도 가장 굵어져 맥동이 함께 보인다.
            float halfMain = _radius * _style.RimWidth * 0.5f * Mathf.Lerp(0.5f, 1f, blend)
                * (1f + _style.RimPulse * (1f - pulse));
            Color tint = _style.Color * color;
            tint.a *= blend;
            bool connected = _style.RimConnected;
            float hairline = connected ? _style.RimHairline : 0f;
            Color glowTint = tint;
            glowTint.a *= _style.RimGlowOpacity * glow;
            float glowHalf = halfMain * _style.RimGlowWidth;

            // 머리 위치는 시계 방향 진행량(바퀴)이며 오름차순이다. 흔들림 한도 덕분에 순서가 바뀌지 않는다.
            int headCount = _rimHeads.Length;
            for (int head = 0; head < headCount; head++)
                _rimHeads[head] = _rimFlow.Phase + head / (float)headCount + _rimOffsets[head];

            float leading = _rimHeads[headCount - 1];
            int ahead = headCount - 1;
            int count = RIM_SEGMENTS + 1;
            for (int i = 0; i < count; i++)
            {
                // 가장 앞선 머리에서 진행 반대 방향으로 한 바퀴를 돌아 같은 머리로 돌아온다.
                float position = leading - i / (float)RIM_SEGMENTS;
                while (ahead > 0 && _rimHeads[ahead - 1] >= position)
                    ahead--;

                float previous = ahead > 0 ? _rimHeads[ahead - 1] : leading - 1f;
                float behind = _rimHeads[ahead] - position;
                float angle = Mathf.PI * 0.5f - position * TWO_PI;
                Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // 두 파동을 겹쳐 굵기가 실을 따라 요란하게 흐르며 바뀐다.
                float wave = 0.65f * Mathf.Sin(TWO_PI * (position * _style.RimWaveCount - _glowTime * _style.RimWaveSpeed))
                    + 0.35f * Mathf.Sin(TWO_PI * (position * _style.RimWaveCount * 2.3f + _glowTime * _style.RimWaveSpeed * 1.7f) + 0.8f);
                _pathPoints[i] = _center + radial * radius;
                _pathSides[i] = radial;
                _pathProfiles[i] = RimProfile(behind, hairline) * Mathf.Max(MIN_RIM_WAVE, 1f + _style.RimWaveDepth * wave);
                // 떨어진 선은 잔가지도 선 길이 안에서만 나온다.
                float length = connected ? _rimHeads[ahead] - previous : _style.RimStrokeLength;
                _pathBranches[i] = behind / Mathf.Max(0.0001f, length);
            }

            // 빛 띠도 실 굵기를 따라 머리에서 넓고 가는 실에서는 좁다.
            if (glow > 0f)
                AppendStroke(vh, count, glowHalf, _style.Feather * 3f, glowTint, 47f, false);
            AppendStroke(vh, count, halfMain, _style.Feather, tint, 47f, true);
        }

        /// <summary>
        /// 가장 가까운 앞 머리에서 뒤로 behind(둘레 대비)만큼 떨어진 테두리 실의 두께 비율(hairline~1)을 반환한다.
        /// 머리 끝은 hairline 폭에서 시작해 RimHeadLength까지 둥글게 굵어지고, 굵은 채로 이어지다가
        /// RimStrokeLength의 마지막 RimTaperLength 구간에서 RimTaper 거듭제곱으로 hairline 폭까지 가늘어진다. hairline이 0이면 선이 거기서 끝난다.
        /// </summary>
        private float RimProfile(float behind, float hairline)
        {
            float x = Mathf.Clamp01(behind / _style.RimStrokeLength);
            float headLength = _style.RimHeadLength;
            float taperStart = Mathf.Max(headLength, 1f - _style.RimTaperLength);
            float rise = Mathf.Sin(0.5f * Mathf.PI * Mathf.Clamp01(x / headLength));
            float fall = Mathf.Pow(1f - Mathf.Clamp01((x - taperStart) / Mathf.Max(0.001f, 1f - taperStart)), _style.RimTaper);
            return Mathf.Lerp(hairline, 1f, rise * fall);
        }

        /// <summary>
        /// 테두리 실이 도는 원을 따라 안팎으로 옅어지는 고리 빛무리를 RING_GLOW_OFFSETS·RING_GLOW_ALPHAS 고리로 vh에 추가한다.
        /// 고리 폭은 원 반지름 × RingGlowWidth이고 고리별 농도에 alpha를 곱한다.
        /// </summary>
        private void BuildRingGlow(VertexHelper vh, float alpha)
        {
            float radius = _radius * _style.RimRadius;
            float width = _radius * _style.RingGlowWidth;
            Color tint = _style.Color * color;
            int ringCount = RING_GLOW_OFFSETS.Length;
            int first = vh.currentVertCount;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float ringRadius = Mathf.Max(0f, radius + RING_GLOW_OFFSETS[ring] * width);
                Color ringColor = tint;
                ringColor.a *= RING_GLOW_ALPHAS[ring] * alpha;
                for (int segment = 0; segment < GLOW_SEGMENTS; segment++)
                {
                    float angle = TWO_PI * segment / GLOW_SEGMENTS;
                    vh.AddVert(_center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius, ringColor, Vector4.zero);
                }
            }

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int inner = first + ring * GLOW_SEGMENTS;
                int outer = inner + GLOW_SEGMENTS;
                for (int segment = 0; segment < GLOW_SEGMENTS; segment++)
                {
                    int next = (segment + 1) % GLOW_SEGMENTS;
                    vh.AddTriangle(inner + segment, outer + segment, outer + next);
                    vh.AddTriangle(inner + segment, outer + next, inner + next);
                }
            }
        }

        /// <summary>
        /// _pathPoints의 count개 점을 따라 줄기와(withBranch이면) 잔가지 띠를 vh에 추가한다.
        /// halfMain은 줄기 최대 반폭, feather는 가장자리 흐림 폭(캔버스 픽셀), tint는 색이고 seed로 실마다 두께 변화를 다르게 한다.
        /// </summary>
        private void AppendStroke(VertexHelper vh, int count, float halfMain, float feather, Color tint, float seed, bool withBranch)
        {
            int passes = withBranch ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                bool branch = pass == 1;
                for (int i = 0; i < count - 1; i++)
                {
                    GetStroke(i, count, branch, halfMain, seed, out Vector2 start, out float startHalf);
                    GetStroke(i + 1, count, branch, halfMain, seed, out Vector2 end, out float endHalf);
                    if (startHalf <= 0f && endHalf <= 0f)
                        continue;

                    int first = vh.currentVertCount;
                    AddEdge(vh, start, _pathSides[i], startHalf, feather, tint);
                    AddEdge(vh, end, _pathSides[i + 1], endHalf, feather, tint);
                    for (int k = 0; k < 3; k++)
                    {
                        vh.AddTriangle(first + k, first + k + 1, first + 5 + k);
                        vh.AddTriangle(first + k, first + 5 + k, first + 4 + k);
                    }
                }
            }
        }

        /// <summary>
        /// _pathPoints의 index번째 점에서 줄기(branch false) 또는 잔가지(branch true)의 중심 point와 반폭 half를 계산한다.
        /// 줄기는 _pathProfiles 두께 비율을 따르고, 잔가지는 _pathBranches 위치가 15%~85%인 구간에서만 _pathSides 방향으로 벗어났다 돌아오며 그 밖은 반폭 0이다.
        /// 두께는 seed와 _time으로 실을 따라 흐르며 부드럽게 변한다.
        /// </summary>
        private void GetStroke(int index, int count, bool branch, float halfMain, float seed, out Vector2 point, out float half)
        {
            float t = index / (float)(count - 1);
            float thickness = 1f + _style.ThicknessVariation
                * (Mathf.PerlinNoise(t * 2.5f - _time * THICKNESS_FLOW_SPEED, seed + (branch ? 5.3f : 0f)) - 0.5f) * 2f;
            point = _pathPoints[index];
            if (!branch)
            {
                half = halfMain * _pathProfiles[index] * thickness;
                return;
            }

            float position = _pathBranches[index];
            if (position < BRANCH_START || position > BRANCH_END)
            {
                half = 0f;
                return;
            }

            // 양 끝에서 0인 반 사인이라 잔가지가 줄기 속에서 나와 다시 줄기로 들어간다. 가는 실에서는 덜 벗어난다.
            float bulge = Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.InverseLerp(BRANCH_START, BRANCH_END, position)));
            float breath = 0.6f + 0.4f * Mathf.Sin(_time * BRANCH_BREATH_SPEED + seed * 1.7f);
            point += _pathSides[index] * (halfMain * Mathf.Sqrt(_pathProfiles[index]) * 2f * _style.BranchSpread * bulge * breath);
            half = halfMain * _style.BranchWidthRatio * Mathf.Sqrt(bulge) * thickness;
        }

        /// <summary>
        /// 화면 점 point에서 side 방향으로 반폭 half만큼 떨어진 양쪽 가장자리와 feather만큼 더 나간 투명 가장자리, 모두 4점을 vh에 추가한다.
        /// </summary>
        private static void AddEdge(VertexHelper vh, Vector2 point, Vector2 side, float half, float feather, Color tint)
        {
            Color clear = tint;
            clear.a = 0f;
            Vector2 inner = side * half;
            Vector2 outer = side * (half + feather);
            vh.AddVert(point + outer, clear, Vector4.zero);
            vh.AddVert(point + inner, tint, Vector4.zero);
            vh.AddVert(point - inner, tint, Vector4.zero);
            vh.AddVert(point - outer, clear, Vector4.zero);
        }

        /// <summary>
        /// 실 한 가닥이 일정한 속도로 돌지 않도록 잡음 표류와 지수 분포 간격의 무작위 당김으로 진행량(바퀴)을 적분한다.
        /// </summary>
        private sealed class ThreadFlow
        {
            private readonly float _seed;
            private float _phase;
            private float _velocity;
            private float _nextTug;
            private float _damping = 3f;

            public float Phase => _phase;

            /// <summary>
            /// seed로 표류 잡음 줄을, startPhase(바퀴)로 시작 진행량을 정하고 첫 당김을 tugMeanInterval 안에서 무작위로 정한다.
            /// </summary>
            public ThreadFlow(float seed, float startPhase, float tugMeanInterval)
            {
                _seed = seed;
                _phase = startPhase;
                _nextTug = Random.Range(0.1f, Mathf.Max(0.1f, tugMeanInterval));
            }

            /// <summary>
            /// 평균 속도 baseSpeed(초당 바퀴)와 style의 당김·속도 오르내림 설정으로 time 시각 기준 deltaTime만큼 진행량 _phase를 늘린다.
            /// </summary>
            public void Step(float baseSpeed, CombatThreadLiquidStyle style, float time, float deltaTime)
            {
                float speed = baseSpeed * (1f + style.SpeedWander * (Mathf.PerlinNoise(time * 0.12f, 61.7f + _seed * 19.3f) - 0.5f) * 2f);
                float drift = speed * (0.4f + 1.2f * Mathf.PerlinNoise(time * 0.31f, 4.2f + _seed * 37.1f));
                _velocity = Mathf.Lerp(_velocity, drift, 1f - Mathf.Exp(-_damping * deltaTime));
                if (time >= _nextTug)
                {
                    _velocity += speed * style.TugStrength * Random.Range(0.3f, 1.2f);
                    _damping = Random.Range(1.5f, 5f);
                    // 지수 분포 간격이라 당김이 몰려서 오거나 오래 쉬기도 한다.
                    float interval = -Mathf.Log(1f - Random.value * 0.999f) * style.TugMeanInterval;
                    _nextTug = time + Mathf.Clamp(interval, 0.15f, 4f);
                }

                _phase += _velocity * deltaTime;
            }
        }
    }
}
