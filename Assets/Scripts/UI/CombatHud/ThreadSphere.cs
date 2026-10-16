using UnityEngine;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 굵은 실 두 가닥이 단위 구 표면을 나선으로 감싸며 엇갈려 공전하는 경로와 띠 폭을 계산한다.
    /// 자전축이 +Y인 구 자체 좌표계에서 계산하며, 화면 UI(토큰)와 3D 구슬이 각자 회전·투영해 같은 모양으로 그린다.
    /// 각 실은 칼집을 도는 돌진 대상 실처럼 유선형 줄기와 1/5 굵기 잔가지로 이뤄지며 두께가 계속 부드럽게 변한다.
    /// </summary>
    public sealed class ThreadSphere
    {
        public const int THREAD_COUNT = 2;
        private const float TWO_PI = Mathf.PI * 2f;
        private const float STREAMLINE_PEAK = 0.357f;
        private const float BRANCH_START = 0.15f;
        private const float BRANCH_END = 0.85f;
        private const float THICKNESS_FLOW_SPEED = 1.2f;
        private const float BRANCH_BREATH_SPEED = 2.2f;

        private readonly CombatTokenOrbitStyle _style;
        private readonly Vector3[][] _points = new Vector3[THREAD_COUNT][];
        private readonly Vector3[][] _sides = new Vector3[THREAD_COUNT][];
        private float _time;

        public int SampleCount => _points[0].Length;

        /// <summary>
        /// style 설정을 저장하고 실마다 Segments + 1개의 경로 점·옆 방향 배열을 만든다.
        /// </summary>
        public ThreadSphere(CombatTokenOrbitStyle style)
        {
            _style = style;
            for (int thread = 0; thread < THREAD_COUNT; thread++)
            {
                _points[thread] = new Vector3[style.Segments + 1];
                _sides[thread] = new Vector3[style.Segments + 1];
            }
        }

        /// <summary>
        /// 두 실의 머리 진행 각도(라디안 누적값) flow와 시각 time으로 꼬리부터 머리까지 단위 구 표면 경로 점과, 경로에 수직인 구 표면 방향을 계산해 저장한다.
        /// 각 점의 궤도면은 +Y 자전축에서 Inclination만큼 기울어 있고, 진행 각도 × Twist와 시간 × PrecessionSpeed만큼 자전축 둘레로 돌아간다.
        /// </summary>
        public void Sample(ThreadSphereFlow flow, float time)
        {
            _time = time;
            float inclination = _style.Inclination * Mathf.Deg2Rad;
            float length = _style.StrokeLength * TWO_PI;
            for (int thread = 0; thread < THREAD_COUNT; thread++)
            {
                Vector3[] points = _points[thread];
                int count = points.Length;
                float azimuthOffset = thread * _style.CounterAzimuth * Mathf.Deg2Rad + time * _style.PrecessionSpeed;
                float head = flow.GetHead(thread);
                for (int i = 0; i < count; i++)
                {
                    float theta = head - (1f - i / (float)(count - 1)) * length;
                    float azimuth = azimuthOffset + theta * _style.Twist;
                    Vector3 normal = new Vector3(
                        Mathf.Sin(inclination) * Mathf.Cos(azimuth),
                        Mathf.Cos(inclination),
                        -Mathf.Sin(inclination) * Mathf.Sin(azimuth));
                    Vector3 across = Vector3.Cross(Vector3.up, normal).normalized;
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
        }

        /// <summary>thread 실의 index번째 경로 점(단위 구 표면, 구 자체 좌표)을 반환한다.</summary>
        public Vector3 GetPoint(int thread, int index)
        {
            return _points[thread][index];
        }

        /// <summary>thread 실의 index번째 점에서 경로에 수직인 구 표면 방향(단위 벡터, 구 자체 좌표)을 반환한다.</summary>
        public Vector3 GetSide(int thread, int index)
        {
            return _sides[thread][index];
        }

        /// <summary>
        /// thread 실의 index번째 점에서 줄기(branch false) 또는 잔가지(branch true)의 구 표면 중심 point와 반폭 half(구 반지름 단위)를 계산한다.
        /// halfMain은 줄기 최대 반폭이다. 잔가지는 꼬리 15%~머리 85% 구간에서만 줄기 옆으로 벗어났다 돌아오며 그 밖은 반폭 0이다.
        /// </summary>
        public void GetStroke(int thread, bool branch, int index, float halfMain, out Vector3 point, out float half)
        {
            float t = index / (float)(SampleCount - 1);
            float thickness = 1f + _style.ThicknessVariation
                * (Mathf.PerlinNoise(t * 2.5f - _time * THICKNESS_FLOW_SPEED, thread * 13.7f + (branch ? 5.3f : 0f)) - 0.5f) * 2f;
            point = _points[thread][index];
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
        /// 머리(fromHead=0)에서 꼬리(fromHead=1)까지의 유선형 두께 비율(0~1)을 반환한다. 머리는 둥글고 꼬리는 길게 가늘어진다.
        /// </summary>
        private static float Streamline(float fromHead)
        {
            float x = Mathf.Clamp01(fromHead);
            return Mathf.Clamp01(Mathf.Sqrt(x) * Mathf.Pow(1f - x, 1.2f) / STREAMLINE_PEAK);
        }
    }
}
