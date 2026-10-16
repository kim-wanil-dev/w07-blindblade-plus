using UnityEngine;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// ThreadSphere가 계산한 굵은 실 두 가닥의 구체를 화면 UI 메시로 그린다.
    /// 실의 폭은 구 표면 위에서 정하므로 구 가장자리에서는 자연스럽게 얇아 보이고, 실이 구 밖으로 삐져나오지 않는다.
    /// 구 뒤쪽 구간을 먼저 옅게 그린 뒤 앞쪽 구간을 그린다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CombatTokenOrbitThread : MaskableGraphic
    {
        [Header("상태")]
        private CombatTokenOrbitStyle _style;
        private ThreadSphere _sphere;
        private Quaternion _orientation;
        private float _visibility;
        private Vector2 _center;
        private float _radius;

        /// <summary>
        /// style 설정을 저장하고 실 구체 계산기를 만든다. 자전축 기울기는 style의 AxisPitch·AxisRoll로 정하며, 클릭을 막지 않도록 raycastTarget을 끈다.
        /// </summary>
        public void Initialize(CombatTokenOrbitStyle style)
        {
            _style = style;
            _sphere = new ThreadSphere(style);
            _orientation = Quaternion.Euler(style.AxisPitch, 0f, style.AxisRoll);
            raycastTarget = false;
        }

        /// <summary>
        /// 보임 정도 visibility(0~1)를 저장하고 flow의 두 실 진행 각도와 시각 time으로 구체 경로를 계산한 뒤 메시를 다시 그리게 한다.
        /// </summary>
        public void SetState(float visibility, float time, ThreadSphereFlow flow)
        {
            _visibility = visibility;
            _sphere.Sample(flow, time);
            SetVerticesDirty();
        }

        /// <summary>
        /// 구 뒤쪽 구간, 앞쪽 구간 순서로 두 실의 줄기·잔가지를 vh에 띠 메시로 채운다.
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

            // 앞쪽 구간이 뒤쪽 구간을 덮도록 레이어 순서대로 그린다.
            for (int pass = 0; pass < 2; pass++)
            {
                bool front = pass == 1;
                for (int thread = 0; thread < ThreadSphere.THREAD_COUNT; thread++)
                {
                    BuildStroke(vh, thread, false, front);
                    BuildStroke(vh, thread, true, front);
                }
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
            int count = _sphere.SampleCount;
            for (int i = 0; i < count - 1; i++)
            {
                float depth = (_orientation * _sphere.GetPoint(thread, i)).z + (_orientation * _sphere.GetPoint(thread, i + 1)).z;
                if (depth <= 0f != front)
                    continue;

                _sphere.GetStroke(thread, branch, i, halfMain, out Vector3 start, out float startHalf);
                _sphere.GetStroke(thread, branch, i + 1, halfMain, out Vector3 end, out float endHalf);
                if (startHalf <= 0f && endHalf <= 0f)
                    continue;

                int first = vh.currentVertCount;
                AddEdge(vh, start, _sphere.GetSide(thread, i), startHalf, tint);
                AddEdge(vh, end, _sphere.GetSide(thread, i + 1), endHalf, tint);
                for (int k = 0; k < 3; k++)
                {
                    vh.AddTriangle(first + k, first + k + 1, first + 5 + k);
                    vh.AddTriangle(first + k, first + 5 + k, first + 4 + k);
                }
            }
        }

        /// <summary>
        /// 구 자체 좌표의 표면 점 point에서 side 방향으로 반폭 half만큼 떨어진 양쪽 가장자리를 구 표면에 두고 화면으로 투영해 띠 단면 꼭짓점 4개를 vh에 추가한다.
        /// tint에 구 뒤쪽일수록 BackOpacity까지 옅어지는 농도를 곱하며, 바깥 두 점은 화면에서 Feather 폭만큼 더 나간 투명 점이다.
        /// </summary>
        private void AddEdge(VertexHelper vh, Vector3 point, Vector3 side, float half, Color tint)
        {
            Vector3 view = _orientation * point;
            tint.a *= Mathf.Lerp(_style.BackOpacity, 1f, -view.z * 0.5f + 0.5f);
            Vector2 left = Project(_orientation * (point + side * half).normalized);
            Vector2 right = Project(_orientation * (point - side * half).normalized);
            // 구 가장자리에서 실이 옆으로 누워 양쪽이 겹치면 흐림 폭도 0이 된다.
            Vector2 feather = (left - right).normalized * _style.Feather;
            Color clear = tint;
            clear.a = 0f;
            vh.AddVert(left + feather, clear, Vector4.zero);
            vh.AddVert(left, tint, Vector4.zero);
            vh.AddVert(right, tint, Vector4.zero);
            vh.AddVert(right - feather, clear, Vector4.zero);
        }

        /// <summary>화면 기준 단위 구 표면 점 view를 중심 _center, 반지름 _radius의 캔버스 좌표로 직교 투영해 반환한다.</summary>
        private Vector2 Project(Vector3 view)
        {
            return _center + new Vector2(view.x, view.y) * _radius;
        }
    }
}
