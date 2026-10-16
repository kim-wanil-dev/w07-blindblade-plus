using UnityEngine;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 굵은 실 두 가닥이 구 표면을 감싸며 서로 엇갈려 공전해 구체를 이루는 모습을 UI 메시로 그린다.
    /// 실의 폭은 구 표면 위에서 정하므로 구 가장자리에서는 자연스럽게 얇아 보이고, 실이 구 밖으로 삐져나오지 않는다.
    /// 각 실은 칼집을 도는 돌진 대상 실처럼 유선형 줄기와 1/5 굵기 잔가지로 이뤄지며 두께가 계속 부드럽게 변한다.
    /// 궤도면이 실의 진행에 따라 자전축 둘레로 돌아가 실이 구를 나선으로 감싸고, 구 뒤쪽 구간을 먼저 옅게 그린 뒤 앞쪽 구간을 그린다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatTokenOrbitThread : MaskableGraphic
    {
        private const int THREAD_COUNT = 2;
        private const float TWO_PI = Mathf.PI * 2f;
        private const float STREAMLINE_PEAK = 0.357f;
        private const float BRANCH_START = 0.15f;
        private const float BRANCH_END = 0.85f;
        private const float THICKNESS_FLOW_SPEED = 1.2f;
        private const float BRANCH_BREATH_SPEED = 2.2f;

        [Header("상태")]
        private CombatTokenOrbitStyle _style;
        private float _visibility;
        private float _time;
        private Vector2 _center;
        private float _radius;
        private readonly float[] _heads = new float[THREAD_COUNT];
        private Vector3[][] _points;
        private Vector3[][] _sides;

        /// <summary>
        /// style 설정을 저장하고 두 실의 경로 계산 배열을 만든다. 클릭을 막지 않도록 raycastTarget을 끈다.
        /// </summary>
        public void Initialize(CombatTokenOrbitStyle style)
        {
            _style = style;
            raycastTarget = false;
            int count = style.Segments + 1;
            _points = new Vector3[THREAD_COUNT][];
            _sides = new Vector3[THREAD_COUNT][];
            for (int thread = 0; thread < THREAD_COUNT; thread++)
            {
                _points[thread] = new Vector3[count];
                _sides[thread] = new Vector3[count];
            }
        }

        /// <summary>
        /// 보임 정도 visibility(0~1), 궤도면 회전·두께 변화 시각 time, 두 실의 머리 진행 각도(라디안, 감지 않은 누적값) firstHead·secondHead를 저장하고 메시를 다시 그리게 한다.
        /// </summary>
        public void SetState(float visibility, float time, float firstHead, float secondHead)
        {
            _visibility = visibility;
            _time = time;
            _heads[0] = firstHead;
            _heads[1] = secondHead;
            SetVerticesDirty();
        }

        /// <summary>
        /// 두 실의 경로를 단위 구 표면에서 계산한 뒤 구 뒤쪽 구간, 앞쪽 구간 순서로 줄기·잔가지를 vh에 띠 메시로 채운다.
        /// 화면상 구 반지름은 RectTransform 크기(토큰 지름) × SphereScale의 절반이다. 초기화 전이나 보이지 않으면 비운다.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // AddComponent 직후 Initialize 전에 한 번 그려질 수 있다.
            if (_style == null || _visibility <= 0f)
                return;

            Rect rect = rectTransform.rect;
            _center = rect.center;
            _radius = Mathf.Min(rect.width, rect.height) * 0.5f * _style.SphereScale;
            for (int thread = 0; thread < THREAD_COUNT; thread++)
                SamplePath(thread);

            // 앞쪽 구간이 뒤쪽 구간을 덮도록 레이어 순서대로 그린다.
            for (int pass = 0; pass < 2; pass++)
            {
                bool front = pass == 1;
                for (int thread = 0; thread < THREAD_COUNT; thread++)
                {
                    BuildStroke(vh, thread, false, front);
                    BuildStroke(vh, thread, true, front);
                }
            }
        }

        /// <summary>
        /// thread 실의 꼬리부터 머리(_heads[thread])까지 경로를 단위 구 표면에서 나눠 _points에, 경로에 수직인 구 표면 방향을 _sides에 저장한다.
        /// 각 점의 궤도면은 자전축에서 Inclination만큼 기울어 있고, 진행 각도 × Twist와 시간 × PrecessionSpeed만큼 자전축 둘레로 돌아간다.
        /// 점의 z가 음수이면 화면 쪽(앞)이다.
        /// </summary>
        private void SamplePath(int thread)
        {
            Vector3[] points = _points[thread];
            int count = points.Length;
            Quaternion orientation = Quaternion.Euler(_style.AxisPitch, 0f, _style.AxisRoll);
            Vector3 axis = orientation * Vector3.up;
            Vector3 sideA = orientation * Vector3.right;
            Vector3 sideB = orientation * Vector3.back;
            float inclination = _style.Inclination * Mathf.Deg2Rad;
            float azimuthOffset = thread * _style.CounterAzimuth * Mathf.Deg2Rad + _time * _style.PrecessionSpeed;
            float length = _style.StrokeLength * TWO_PI;
            for (int i = 0; i < count; i++)
            {
                float theta = _heads[thread] - (1f - i / (float)(count - 1)) * length;
                float azimuth = azimuthOffset + theta * _style.Twist;
                Vector3 normal = Mathf.Cos(inclination) * axis
                    + Mathf.Sin(inclination) * (Mathf.Cos(azimuth) * sideA + Mathf.Sin(azimuth) * sideB);
                Vector3 across = Vector3.Cross(axis, normal).normalized;
                Vector3 along = Vector3.Cross(normal, across);
                points[i] = Mathf.Cos(theta) * across + Mathf.Sin(theta) * along;
            }

            Vector3[] sides = _sides[thread];
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = points[Mathf.Min(i + 1, count - 1)] - points[Mathf.Max(i - 1, 0)];
                sides[i] = Vector3.Cross(points[i], tangent).normalized;
            }
        }

        /// <summary>
        /// thread 실의 줄기(branch false) 또는 잔가지(branch true)에서 front 레이어(구 앞쪽이면 true)에 속한 구간을 vh에 추가한다.
        /// 구간 가운데의 깊이로 앞·뒤를 나누며, 양 끝 폭이 모두 0인 구간은 건너뛴다.
        /// </summary>
        private void BuildStroke(VertexHelper vh, int thread, bool branch, bool front)
        {
            Color tint = _style.Color * color;
            tint.a *= _visibility;
            float halfMain = _style.MainWidth * 0.5f * Mathf.Lerp(0.5f, 1f, _visibility);
            Vector3[] points = _points[thread];
            int count = points.Length;
            for (int i = 0; i < count - 1; i++)
            {
                if (points[i].z + points[i + 1].z <= 0f != front)
                    continue;

                SampleStroke(thread, branch, i, halfMain, out Vector3 start, out float startHalf, out float startAlpha);
                SampleStroke(thread, branch, i + 1, halfMain, out Vector3 end, out float endHalf, out float endAlpha);
                if (startHalf <= 0f && endHalf <= 0f)
                    continue;

                int first = vh.currentVertCount;
                AddEdge(vh, start, _sides[thread][i], startHalf, WithAlpha(tint, startAlpha));
                AddEdge(vh, end, _sides[thread][i + 1], endHalf, WithAlpha(tint, endAlpha));
                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(first + k, first + k + 1, first + 5 + k);
                    vh.AddTriangle(first + k, first + 5 + k, first + 4 + k);
                }
            }
        }

        /// <summary>
        /// thread 실의 index번째 점에서 줄기 또는 잔가지(branch)의 구 표면 중심 point, 반폭 half(구 반지름 단위), 농도 배율 alpha를 계산한다.
        /// halfMain은 줄기 최대 반폭이다. 구 뒤쪽일수록 옅으며, 잔가지는 꼬리 15%~머리 85% 구간에서만 줄기 옆으로 벗어났다 돌아온다.
        /// </summary>
        private void SampleStroke(int thread, bool branch, int index, float halfMain, out Vector3 point, out float half, out float alpha)
        {
            float t = index / (float)(_points[thread].Length - 1);
            float thickness = 1f + _style.ThicknessVariation
                * (Mathf.PerlinNoise(t * 2.5f - _time * THICKNESS_FLOW_SPEED, thread * 13.7f + (branch ? 5.3f : 0f)) - 0.5f) * 2f;
            point = _points[thread][index];
            alpha = Mathf.Lerp(_style.BackOpacity, 1f, -point.z * 0.5f + 0.5f);
            if (!branch)
            {
                half = halfMain * Streamline(1f - t) * thickness;
                return;
            }

            if (t < BRANCH_START || t > BRANCH_END)
            {
                half = 0f;
                return;
            }

            // 양 끝에서 0인 반 사인이라 잔가지가 줄기 속에서 나와 다시 줄기로 들어간다.
            float bulge = Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.InverseLerp(BRANCH_START, BRANCH_END, t)));
            float breath = 0.6f + 0.4f * Mathf.Sin(_time * BRANCH_BREATH_SPEED + thread * 1.7f);
            point = (point + _sides[thread][index] * (halfMain * 2f * _style.BranchSpread * bulge * breath)).normalized;
            half = halfMain * _style.BranchWidthRatio * Mathf.Sqrt(bulge) * thickness;
        }

        /// <summary>
        /// 구 표면 점 point에서 side 방향으로 반폭 half만큼 떨어진 양쪽 가장자리를 구 표면에 두고 화면으로 투영해 띠 단면 꼭짓점 4개를 vh에 추가한다.
        /// 안쪽 두 점은 edgeColor, 바깥 두 점은 화면에서 _style.Feather 폭만큼 더 나간 투명 점이라 가장자리가 부드럽게 사라진다.
        /// </summary>
        private void AddEdge(VertexHelper vh, Vector3 point, Vector3 side, float half, Color edgeColor)
        {
            Vector2 left = Project((point + side * half).normalized);
            Vector2 right = Project((point - side * half).normalized);
            // 구 가장자리에서 실이 옆으로 누워 양쪽이 겹치면 흐림 폭도 0이 된다.
            Vector2 feather = (left - right).normalized * _style.Feather;
            Color clear = WithAlpha(edgeColor, 0f);
            vh.AddVert(left + feather, clear, Vector4.zero);
            vh.AddVert(left, edgeColor, Vector4.zero);
            vh.AddVert(right, edgeColor, Vector4.zero);
            vh.AddVert(right - feather, clear, Vector4.zero);
        }

        /// <summary>단위 구 표면 점 point를 화면 중심 _center, 반지름 _radius의 캔버스 좌표로 직교 투영해 반환한다.</summary>
        private Vector2 Project(Vector3 point)
        {
            return _center + new Vector2(point.x, point.y) * _radius;
        }

        /// <summary>source 색의 알파에 multiplier를 곱한 색을 반환한다.</summary>
        private static Color WithAlpha(Color source, float multiplier)
        {
            source.a *= multiplier;
            return source;
        }

        /// <summary>
        /// 머리(fromHead=0)에서 꼬리(fromHead=1)까지의 유선형 두께 비율(0~1)을 반환한다. 머리는 둥글고 꼬리는 길게 가늘어진다.
        /// </summary>
        private static float Streamline(float fromHead)
        {
            float x = Mathf.Clamp01(fromHead);
            return Mathf.Clamp01(Mathf.Sqrt(x) * Mathf.Pow(1f - x, 1.2f) / STREAMLINE_PEAK);
        }
    }
}
