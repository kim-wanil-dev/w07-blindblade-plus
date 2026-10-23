using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BlindBlade.UI.CombatHud
{
    /// <summary>
    /// 실 액체 게이지의 액체 본체를 그린다. 토큰·칼 구슬과 같은 굵은 실 구체 여러 겹을 RenderTexture에 더해 픽셀마다 실이 몇 겹 겹쳤는지 쌓는다.
    /// 같은 오브젝트의 RawImage는 BlindBlade/UIThreadLiquid 재질로 겹침 수를 단계별 색으로 바꾸고, 물결치는 수면 위와 액체 원 밖을 잘라 낸다.
    /// 겹마다 자전축이 다르게 기울고 뒤척여 실이 여러 방향으로 소용돌이친다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class CombatThreadLiquidBody : MonoBehaviour
    {
        private const int MIN_TEXTURE_SIZE = 32;
        private static readonly int SURFACE_PROPERTY = Shader.PropertyToID("_LiquidSurface");
        private static readonly int SHAPE_PROPERTY = Shader.PropertyToID("_LiquidShape");
        private static readonly int FILL_COLOR_PROPERTY = Shader.PropertyToID("_FillColor");
        private static readonly int LEVEL1_COLOR_PROPERTY = Shader.PropertyToID("_Level1Color");
        private static readonly int LEVEL2_COLOR_PROPERTY = Shader.PropertyToID("_Level2Color");
        private static readonly int LEVEL3_COLOR_PROPERTY = Shader.PropertyToID("_Level3Color");
        private static readonly int LEVEL4_COLOR_PROPERTY = Shader.PropertyToID("_Level4Color");
        private static readonly int THRESHOLDS_PROPERTY = Shader.PropertyToID("_LevelThresholds");
        private static readonly int LEVEL_SOFTNESS_PROPERTY = Shader.PropertyToID("_LevelSoftness");

        [Header("상태")]
        private CombatThreadLiquidStyle _style;
        private RawImage _image;
        private Material _densityMaterial;
        private Material _compositeMaterial;
        private RenderTexture _texture;
        private Mesh _mesh;
        private CommandBuffer _commandBuffer;
        private ThreadSphere[] _spheres;
        private ThreadSphereFlow[] _flows;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _indices = new List<int>();

        void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }

            if (_mesh != null) Destroy(_mesh);
            if (_compositeMaterial != null) Destroy(_compositeMaterial);
            _commandBuffer?.Release();
        }

        /// <summary>
        /// style 설정과 겹침 수를 더하는 densityMaterial(BlindBlade/ThreadLiquidDensity)을 저장하고, compositeMaterial(BlindBlade/UIThreadLiquid)을 복제해 RawImage에 쓴다.
        /// TextureSize 크기의 RenderTexture·동적 메시·CommandBuffer와 LayerCount 겹의 실 구체·흐름을 만들고, RawImage를 부모 원 안 액체 원을 감싸는 사각형에 맞춘다.
        /// </summary>
        public void Initialize(CombatThreadLiquidStyle style, Material densityMaterial, Material compositeMaterial)
        {
            _style = style;
            _densityMaterial = densityMaterial;
            _image = GetComponent<RawImage>();
            _compositeMaterial = new Material(compositeMaterial) { name = compositeMaterial.name + " (Instance)" };
            int size = Mathf.Max(MIN_TEXTURE_SIZE, style.TextureSize);
            _texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "ThreadLiquidDensity",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _texture.Create();
            _image.texture = _texture;
            _image.material = _compositeMaterial;
            _image.raycastTarget = false;
            _image.enabled = false;

            _mesh = new Mesh { name = "ThreadLiquidDensity" };
            _mesh.MarkDynamic();
            _commandBuffer = new CommandBuffer { name = "ThreadLiquidDensity" };
            int layerCount = Mathf.Max(1, style.LayerCount);
            _spheres = new ThreadSphere[layerCount];
            _flows = new ThreadSphereFlow[layerCount];
            for (int layer = 0; layer < layerCount; layer++)
            {
                _spheres[layer] = new ThreadSphere(style.BodyThreads);
                _flows[layer] = new ThreadSphereFlow(20f + layer * 2f);
                _flows[layer].Reset(style.BodyThreads, 0f);
            }

            // 텍스처의 -1~1 액체 좌표가 부모 원 안의 액체 원을 감싸는 사각형에 그대로 맞도록 한다.
            RectTransform rect = (RectTransform)transform;
            float half = style.LiquidRadius * 0.5f;
            rect.anchorMin = new Vector2(0.5f - half, 0.5f - half);
            rect.anchorMax = new Vector2(0.5f + half, 0.5f + half);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 액체 시각 time 기준으로 모든 겹의 두 실 진행 각도를 deltaTime만큼 적분한다.
        /// </summary>
        public void Step(float time, float deltaTime)
        {
            for (int layer = 0; layer < _flows.Length; layer++)
                _flows[layer].Step(_style.BodyThreads, time, deltaTime);
        }

        /// <summary>
        /// show가 거짓이면 RawImage를 끈다. 참이면 time 시각의 실 구체를 겹침 텍스처에 다시 쌓고,
        /// 수면 값 surface(높이·물결 높이·물결 길이·위상, 액체 반지름 단위)와 기울기 surfaceTilt, 단계 색을 합성 재질에 넘긴 뒤 RawImage를 켠다.
        /// </summary>
        public void Render(float time, Vector4 surface, float surfaceTilt, bool show)
        {
            _image.enabled = show;
            if (!show)
                return;

            // 그래픽 장치가 바뀌면 RenderTexture 내용이 사라질 수 있다.
            if (!_texture.IsCreated())
                _texture.Create();

            float liquidRadius = Mathf.Max(1f, ((RectTransform)transform).rect.width * 0.5f);
            BuildMesh(time, surface, surfaceTilt, liquidRadius);
            _commandBuffer.Clear();
            _commandBuffer.SetRenderTarget(_texture);
            _commandBuffer.ClearRenderTarget(false, true, Color.clear);
            _commandBuffer.DrawMesh(_mesh, Matrix4x4.identity, _densityMaterial, 0, 0);
            Graphics.ExecuteCommandBuffer(_commandBuffer);

            _compositeMaterial.SetVector(SURFACE_PROPERTY, surface);
            _compositeMaterial.SetVector(SHAPE_PROPERTY, new Vector4(surfaceTilt, _style.ClipSoftness / liquidRadius, 0f, 0f));
            _compositeMaterial.SetColor(FILL_COLOR_PROPERTY, _style.FillColor);
            _compositeMaterial.SetColor(LEVEL1_COLOR_PROPERTY, _style.Level1Color);
            _compositeMaterial.SetColor(LEVEL2_COLOR_PROPERTY, _style.Level2Color);
            _compositeMaterial.SetColor(LEVEL3_COLOR_PROPERTY, _style.Level3Color);
            _compositeMaterial.SetColor(LEVEL4_COLOR_PROPERTY, _style.Level4Color);
            _compositeMaterial.SetVector(THRESHOLDS_PROPERTY, _style.LevelThresholds);
            _compositeMaterial.SetFloat(LEVEL_SOFTNESS_PROPERTY, _style.LevelSoftness);
        }

        /// <summary>
        /// 겹마다 자전축을 서로 다른 수평축 둘레로 LayerTilt씩 기울이고 time에 따라 TumbleAngle까지 뒤척인 방향으로 실 구체를 돌려 액체 원에 직교 투영한 띠 메시를 _mesh에 만든다.
        /// 정점 알파는 그 실이 겹침 수에 더하는 무게이며, 구 뒤쪽은 BackOpacity만큼, 수면에서 깊을수록 DepthOpacity만큼 가볍다.
        /// surface·surfaceTilt로 수면보다 확실히 위인 구간은 건너뛰고, liquidRadius(캔버스 픽셀)로 가장자리 흐림 폭을 액체 반지름 단위로 바꾼다.
        /// </summary>
        private void BuildMesh(float time, Vector4 surface, float surfaceTilt, float liquidRadius)
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
            CombatTokenOrbitStyle threads = _style.BodyThreads;
            float halfMain = threads.MainWidth * 0.5f;
            float top = surface.x + Mathf.Abs(surface.y) + Mathf.Abs(surfaceTilt) + halfMain;
            float feather = threads.Feather / liquidRadius;
            Quaternion baseOrientation = Quaternion.Euler(threads.AxisPitch, 0f, threads.AxisRoll);
            for (int layer = 0; layer < _spheres.Length; layer++)
            {
                float phase = time * _style.TumbleSpeed + layer * 2.1f;
                Quaternion tumble = Quaternion.Euler(_style.TumbleAngle * Mathf.Sin(phase), 0f,
                    _style.TumbleAngle * Mathf.Cos(phase * 0.73f + layer * 1.3f));
                Vector3 tiltAxis = new Vector3(Mathf.Cos(layer * 2.4f), 0f, Mathf.Sin(layer * 2.4f));
                Quaternion orientation = tumble * Quaternion.AngleAxis(layer * _style.LayerTilt, tiltAxis) * baseOrientation;
                ThreadSphere sphere = _spheres[layer];
                // 겹마다 다른 시각으로 경로를 계산해 겹들이 같은 모양으로 포개지지 않게 한다.
                sphere.Sample(_flows[layer], time + layer * 7.7f);
                int count = sphere.SampleCount;
                for (int thread = 0; thread < ThreadSphere.THREAD_COUNT; thread++)
                {
                    for (int branch = 0; branch < 2; branch++)
                    {
                        for (int i = 0; i < count - 1; i++)
                        {
                            float startY = (orientation * sphere.GetPoint(thread, i)).y;
                            float endY = (orientation * sphere.GetPoint(thread, i + 1)).y;
                            if (Mathf.Min(startY, endY) > top)
                                continue;

                            sphere.GetStroke(thread, branch == 1, i, halfMain, out Vector3 start, out float startHalf);
                            sphere.GetStroke(thread, branch == 1, i + 1, halfMain, out Vector3 end, out float endHalf);
                            if (startHalf <= 0f && endHalf <= 0f)
                                continue;

                            int first = _vertices.Count;
                            AddEdge(orientation, start, sphere.GetSide(thread, i), startHalf, feather, surface.x);
                            AddEdge(orientation, end, sphere.GetSide(thread, i + 1), endHalf, feather, surface.x);
                            for (int k = 0; k < 3; k++)
                            {
                                _indices.Add(first + k);
                                _indices.Add(first + k + 1);
                                _indices.Add(first + 5 + k);
                                _indices.Add(first + k);
                                _indices.Add(first + 5 + k);
                                _indices.Add(first + 4 + k);
                            }
                        }
                    }
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_indices, 0);
        }

        /// <summary>
        /// 구 자체 좌표의 표면 점 point에서 side 방향으로 반폭 half만큼 떨어진 양쪽 가장자리를 구 표면에 두고 orientation으로 돌려 직교 투영한 띠 단면 4점을 추가한다.
        /// 바깥 두 점은 feather(액체 반지름 단위)만큼 더 나간 무게 0 점이며, level(액체 반지름 단위 수면 높이)로 깊이에 따른 무게를 정한다.
        /// </summary>
        private void AddEdge(Quaternion orientation, Vector3 point, Vector3 side, float half, float feather, float level)
        {
            CombatTokenOrbitStyle threads = _style.BodyThreads;
            Vector3 view = orientation * point;
            float weight = Mathf.Lerp(threads.BackOpacity, 1f, -view.z * 0.5f + 0.5f)
                * Mathf.Lerp(_style.DepthOpacity, 1f, Mathf.Clamp01((view.y + 1f) / Mathf.Max(0.01f, level + 1f)));
            Vector3 leftView = orientation * (point + side * half).normalized;
            Vector3 rightView = orientation * (point - side * half).normalized;
            Vector3 left = new Vector3(leftView.x, leftView.y, 0f);
            Vector3 right = new Vector3(rightView.x, rightView.y, 0f);
            // 구 가장자리에서 실이 옆으로 누워 양쪽이 겹치면 흐림 폭도 0이 된다.
            Vector3 outer = (left - right).normalized * feather;
            Color inner = new Color(1f, 1f, 1f, weight);
            Color clear = new Color(1f, 1f, 1f, 0f);
            _vertices.Add(left + outer);
            _vertices.Add(left);
            _vertices.Add(right);
            _vertices.Add(right - outer);
            _colors.Add(clear);
            _colors.Add(inner);
            _colors.Add(inner);
            _colors.Add(clear);
        }
    }
}
