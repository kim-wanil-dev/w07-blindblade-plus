using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 카메라를 그리는 동안만 VFX 표시와 큐를 구분하고 이후 원본 상태를 복원한다.
/// </summary>
internal sealed class OutlineVFXRouting
{
    internal const uint SELECTED_MASK = 1u << 31;
    internal const uint ALWAYS_VISIBLE_MASK = 1u << 30;
    // PC_Renderer의 불투명·투명 Layer Mask에서 뺀 OutlineVfxCapture 레이어다. 윤곽선 캡처 패스는 모든 레이어를 그린다.
    internal const int CAPTURE_ONLY_LAYER = 9;
    private static readonly int _cameraOpaqueTextureId = Shader.PropertyToID("_CameraOpaqueTexture");
    private readonly HashSet<Renderer> _selected = new HashSet<Renderer>();
    private readonly HashSet<Renderer> _alwaysVisible = new HashSet<Renderer>();
    private readonly Dictionary<Renderer, Route> _cache = new Dictionary<Renderer, Route>();
    private readonly Dictionary<Material, CopyEntry> _selectedCopies = new Dictionary<Material, CopyEntry>();
    private readonly Dictionary<Material, CopyEntry> _routedCopies = new Dictionary<Material, CopyEntry>();
    private readonly List<Route> _changed = new List<Route>();
    private readonly List<Renderer> _removed = new List<Renderer>();
    private readonly List<Material> _removedMaterials = new List<Material>();
    private readonly List<Material> _materials = new List<Material>();
    private int _prepareId;

    private sealed class Route
    {
        public Renderer Renderer { get; set; }
        public Material[] Originals { get; set; }
        public Material[] Copies { get; set; }
        public uint Mask { get; set; }
        public int Layer { get; set; }
        public bool LayerChanged { get; set; }
    }

    private sealed class CopyEntry
    {
        public Material Copy { get; set; }
        public int PreparedId { get; set; }
    }

    /// <summary>
    /// 컴포넌트로 선택한 렌더러와 기본 불투명 파티클을 카메라용으로 표시한다.
    /// 원본 조명용 렌더링 비트는 유지하며 선택한 효과만 임시 큐로 이동한다.
    /// </summary>
    public void Prepare()
    {
        Restore();
        _prepareId++;
        OutlineVFX.Collect(_selected, _alwaysVisible);
        foreach (Renderer renderer in _selected)
            Apply(renderer, true);
        foreach (ParticleSystemRenderer renderer in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
        {
            if (_selected.Contains(renderer) || !renderer.enabled || !renderer.gameObject.scene.IsValid())
                continue;
            renderer.GetSharedMaterials(_materials);
            bool opaque = false;
            foreach (Material material in _materials)
                opaque |= material != null && material.renderQueue <= 2500;
            if (opaque)
                Apply(renderer, false);
        }
        _removed.Clear();
        foreach (var item in _cache)
            if (item.Key == null)
                _removed.Add(item.Key);
        foreach (Renderer renderer in _removed)
            _cache.Remove(renderer);
        RemoveDestroyedCopies(_selectedCopies);
        RemoveDestroyedCopies(_routedCopies);
    }

    /// <summary>
    /// renderer의 머티리얼·마스크·레이어를 기억하고 selected 여부에 맞는 공유 임시 머티리얼로 표시한다.
    /// 투명 셰이더의 합성 설정은 유지하며 큐만 윤곽선 처리 이전으로 옮긴다. Play 중 원래 투명한 선택 효과는
    /// 결과가 윤곽선 합성에서 버려지는 기본 불투명 패스에서 빼도록 캡처 전용 레이어로 옮긴다.
    /// </summary>
    private void Apply(Renderer renderer, bool selected)
    {
        renderer.GetSharedMaterials(_materials);
        if (!_cache.TryGetValue(renderer, out Route route))
        {
            route = new Route { Renderer = renderer };
            _cache.Add(renderer, route);
        }
        bool changed = route.Originals == null || route.Originals.Length != _materials.Count;
        if (!changed)
            for (int i = 0; i < _materials.Count; i++)
                changed |= route.Originals[i] != _materials[i];
        if (changed)
        {
            route.Originals = _materials.ToArray();
            route.Copies = new Material[_materials.Count];
        }
        route.Mask = renderer.renderingLayerMask;
        bool transparentOnly = route.Originals.Length > 0;
        if (route.Originals.Length > 0)
        {
            for (int i = 0; i < route.Originals.Length; i++)
            {
                Material original = route.Originals[i];
                if (original == null)
                {
                    transparentOnly = false;
                    continue;
                }
                route.Copies[i] = GetCopy(original, selected);
                transparentOnly &= original.renderQueue > 2500;
            }
            renderer.sharedMaterials = route.Copies;
        }
        renderer.renderingLayerMask = selected
            ? (route.Mask & ~(SELECTED_MASK | ALWAYS_VISIBLE_MASK))
                | (_alwaysVisible.Contains(renderer) ? ALWAYS_VISIBLE_MASK : SELECTED_MASK)
            : route.Mask;
        // 불투명 재질은 기본 패스가 깊이를 기록하므로 그대로 둔다. 편집 모드는 장면 상태를 바꾸지 않도록 기존 경로를 유지한다.
        route.LayerChanged = selected && transparentOnly && Application.isPlaying;
        if (route.LayerChanged)
        {
            route.Layer = renderer.gameObject.layer;
            renderer.gameObject.layer = CAPTURE_ONLY_LAYER;
        }
        _changed.Add(route);
    }

    /// <summary>
    /// original과 selected 조합의 공유 임시 머티리얼을 반환한다. 같은 원본을 쓰는 렌더러가 같은 복제본을 써서
    /// 복사 비용이 렌더러 수가 아니라 원본 수에 비례하고 같은 머티리얼끼리 배칭될 수 있다.
    /// 이번 준비에서 처음 쓰일 때만 원본 셰이더·속성을 복사하고 큐와 깊이 패스를 설정한다.
    /// </summary>
    private Material GetCopy(Material original, bool selected)
    {
        Dictionary<Material, CopyEntry> copies = selected ? _selectedCopies : _routedCopies;
        if (!copies.TryGetValue(original, out CopyEntry entry))
        {
            entry = new CopyEntry { Copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave } };
            copies.Add(original, entry);
        }
        if (entry.PreparedId == _prepareId)
            return entry.Copy;
        entry.PreparedId = _prepareId;
        Material copy = entry.Copy;
        copy.shader = original.shader;
        copy.CopyPropertiesFromMaterial(original);
        if (selected)
        {
            // 배경을 읽는 왜곡 셰이더가 월드 경계를 VFX 윤곽선으로 가져오지 않게 한다.
            copy.SetTexture(_cameraOpaqueTextureId, Texture2D.blackTexture);
        }
        copy.renderQueue = selected ? 2500
            : (original.renderQueue <= 2500 ? 3000 : original.renderQueue);
        if (selected && original.renderQueue > 2500)
        {
            // 원래 투명 재질은 불투명 깊이·노멀 프리패스에서 제외한다.
            copy.SetShaderPassEnabled("DepthOnly", false);
            copy.SetShaderPassEnabled("DepthNormals", false);
            copy.SetShaderPassEnabled("DepthNormalsOnly", false);
        }
        return copy;
    }

    /// <summary>
    /// 이 카메라에서 바꾼 렌더러의 원본 머티리얼·마스크·레이어를 복원한다.
    /// 저장 및 다음 카메라에는 임시 큐나 표시 비트를 남기지 않는다.
    /// </summary>
    public void Restore()
    {
        foreach (Route route in _changed)
        {
            if (route.Renderer == null)
                continue;
            route.Renderer.sharedMaterials = route.Originals;
            route.Renderer.renderingLayerMask = route.Mask;
            if (route.LayerChanged)
                route.Renderer.gameObject.layer = route.Layer;
        }
        _changed.Clear();
    }

    /// <summary>
    /// copies에서 원본 머티리얼이 파괴된 항목의 임시 머티리얼을 파괴하고 목록에서 뺀다. 원본 에셋은 유지한다.
    /// </summary>
    private void RemoveDestroyedCopies(Dictionary<Material, CopyEntry> copies)
    {
        _removedMaterials.Clear();
        foreach (var item in copies)
            if (item.Key == null)
                _removedMaterials.Add(item.Key);
        foreach (Material material in _removedMaterials)
        {
            UnityEngine.Rendering.CoreUtils.Destroy(copies[material].Copy);
            copies.Remove(material);
        }
    }

    /// <summary>
    /// 렌더러 상태를 복원하고 모든 공유 임시 머티리얼을 해제한다.
    /// </summary>
    public void Dispose()
    {
        Restore();
        foreach (CopyEntry entry in _selectedCopies.Values)
            UnityEngine.Rendering.CoreUtils.Destroy(entry.Copy);
        foreach (CopyEntry entry in _routedCopies.Values)
            UnityEngine.Rendering.CoreUtils.Destroy(entry.Copy);
        _selectedCopies.Clear();
        _routedCopies.Clear();
        _cache.Clear();
    }
}
