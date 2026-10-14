using System.Collections.Generic;

using UnityEngine;

using UnityEngine.Rendering;

namespace BlindBlade.Perception
{
    /// <summary>게임 카메라에 속한 위협의 원본 표시를 임시 대체하고 개별 색상 캡처 목록을 제공한다.</summary>
    internal sealed class ThreatPresentationRouting
    {
        private const uint OUTLINE_MASKS = (1u << 28) | (1u << 29) | (1u << 30) | (1u << 31);
        private static readonly int _colorId = Shader.PropertyToID("_PresentationColor");
        private static readonly int _fillId = Shader.PropertyToID("_PresentationFill");
        private static readonly int _objectId = Shader.PropertyToID("_PresentationObjectId");
        private readonly Dictionary<Renderer, Route> _cache = new Dictionary<Renderer, Route>();
        private readonly HashSet<Renderer> _claimed = new HashSet<Renderer>();
        private readonly List<Route> _changed = new List<Route>();
        private readonly List<Renderer> _removed = new List<Renderer>();
        private readonly List<ThreatDraw> _draws = new List<ThreatDraw>();
        private readonly Material _hiddenMaterial;

        public int DrawCount => _draws.Count;

        internal readonly struct ThreatDraw
        {
            public Renderer Renderer { get; }
            public Material Material { get; }
            public int SubMeshCount { get; }

            /// <summary>renderer·material·subMeshCount를 이번 카메라의 명시적 캡처 명령으로 보관한다.</summary>
            public ThreatDraw(Renderer renderer, Material material, int subMeshCount)
            {
                Renderer = renderer;
                Material = material;
                SubMeshCount = subMeshCount;
            }
        }

        private sealed class Route
        {
            public Renderer Renderer { get; set; }
            public Material[] Originals { get; set; }
            public Material[] Hidden { get; set; }
            public Material Capture { get; set; }
            public uint Mask { get; set; }
        }

        /// <summary>
        /// template의 셰이더로 임시 기본 표시 재질을 생성한다.
        /// template이 없으면 원본 렌더러를 변경하지 않는 비활성 경로로 유지한다.
        /// </summary>
        public ThreatPresentationRouting(Material template)
        {
            if (template != null)
                _hiddenMaterial = CoreUtils.CreateEngineMaterial(template.shader);
        }

        /// <summary>
        /// camera와 연결된 활성 대상의 스냅샷으로 원본 표시를 임시 대체하고 캡처 목록을 만든다.
        /// Scene View·다른 게임 카메라의 원본 표시와 Collider·GameObject 활성 상태는 변경하지 않는다.
        /// </summary>
        public void Prepare(Camera camera)
        {
            Restore();
            _draws.Clear();
            _claimed.Clear();
            if (_hiddenMaterial == null || camera.cameraType != CameraType.Game || !Application.isPlaying)
                return;

            int objectIndex = 0;
            foreach (ThreatPresentationTarget target in Object.FindObjectsByType<ThreatPresentationTarget>(FindObjectsSortMode.None))
            {
                ThreatPresentationResolver resolver = target.Resolver;
                if (!target.isActiveAndEnabled || resolver == null || !resolver.isActiveAndEnabled || resolver.Settings == null
                    || resolver.ViewCamera != camera)
                    continue;
                ThreatPresentationSnapshot snapshot = target.Snapshot;
                objectIndex++;
                foreach (Renderer renderer in target.Renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                        || !_claimed.Add(renderer))
                        continue;
                    int subMeshes = GetSubMeshCount(renderer);
                    if (subMeshes > 0)
                        Apply(renderer, snapshot, resolver.Settings.FillOpacity, objectIndex, subMeshes);
                }
            }
            _removed.Clear();
            foreach (var item in _cache)
                if (item.Key == null || !_claimed.Contains(item.Key))
                    _removed.Add(item.Key);
            foreach (Renderer renderer in _removed)
            {
                CoreUtils.Destroy(_cache[renderer].Capture);
                _cache.Remove(renderer);
            }
        }

        /// <summary>
        /// renderer의 원본 재질·마스크를 보관하고 snapshot의 월드 표시 요청에 맞는 캡처 재질을 준비한다.
        /// fill·objectIndex·subMeshes로 내부 채움과 동일 대상 외형의 경계 식별을 설정한다.
        /// </summary>
        private void Apply(Renderer renderer, ThreatPresentationSnapshot snapshot, float fill, int objectIndex, int subMeshes)
        {
            if (!_cache.TryGetValue(renderer, out Route route))
            {
                route = new Route { Renderer = renderer, Capture = CoreUtils.CreateEngineMaterial(_hiddenMaterial.shader) };
                _cache.Add(renderer, route);
            }
            route.Originals = renderer.sharedMaterials;
            route.Mask = renderer.renderingLayerMask;
            if (route.Hidden == null || route.Hidden.Length != route.Originals.Length)
            {
                route.Hidden = new Material[route.Originals.Length];
                for (int i = 0; i < route.Hidden.Length; i++)
                    route.Hidden[i] = _hiddenMaterial;
            }
            renderer.sharedMaterials = route.Hidden;
            renderer.renderingLayerMask = route.Mask & ~OUTLINE_MASKS;
            _changed.Add(route);
            if (!snapshot.ShowWorld)
                return;
            Color color = snapshot.Color;
            color.a *= snapshot.Alpha;
            if (color.a <= 0f)
                return;
            route.Capture.SetColor(_colorId, color);
            route.Capture.SetFloat(_fillId, fill);
            route.Capture.SetFloat(_objectId, objectIndex);
            _draws.Add(new ThreatDraw(renderer, route.Capture, subMeshes));
        }

        /// <summary>
        /// renderer의 실제 Mesh 또는 SkinnedMesh를 읽어 캡처할 서브메시 수를 반환한다.
        /// 메시 외형이 없는 Renderer는 0을 반환하며 임시 재질로 대체하지 않는다.
        /// </summary>
        private static int GetSubMeshCount(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh != null ? skinned.sharedMesh.subMeshCount : 0;
            if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter))
                return filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
            return 0;
        }

        /// <summary>입력 없이 현재 카메라의 캡처 목록을 복사해 RenderGraph 실행 중 목록 변경을 방지한다.</summary>
        public ThreatDraw[] CopyDraws()
        {
            return _draws.ToArray();
        }

        /// <summary>입력 없이 이번 카메라에서 변경한 원본 재질·렌더링 비트를 복원한다.</summary>
        public void Restore()
        {
            foreach (Route route in _changed)
            {
                if (route.Renderer == null)
                    continue;
                route.Renderer.sharedMaterials = route.Originals;
                route.Renderer.renderingLayerMask = route.Mask;
            }
            _changed.Clear();
        }

        /// <summary>입력 없이 원본 표시를 복원하고 이 경로가 생성한 임시 재질·목록을 해제한다.</summary>
        public void Dispose()
        {
            Restore();
            foreach (Route route in _cache.Values)
                CoreUtils.Destroy(route.Capture);
            _cache.Clear();
            _draws.Clear();
            CoreUtils.Destroy(_hiddenMaterial);
        }
    }
}
