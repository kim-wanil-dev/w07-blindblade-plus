using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Experimental.Rendering;

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

using BlindBlade.Perception;

public sealed class ScreenSpaceOutlineFeature : ScriptableRendererFeature
{
    private static readonly int _lineColorId = Shader.PropertyToID("_LineColor");
    private static readonly int _lineWidthId = Shader.PropertyToID("_LineWidth");
    private static readonly int _depthSensitivityId = Shader.PropertyToID("_DepthSensitivity");
    private static readonly int _normalSensitivityId = Shader.PropertyToID("_NormalSensitivity");
    private static readonly int _usePerceptionMaskId = Shader.PropertyToID("_UsePerceptionMask");
    private static readonly int _transparentThresholdId = Shader.PropertyToID("_TransparentThreshold");
    private static readonly int _sceneryColorId = Shader.PropertyToID("_SceneryColor");
    private static readonly int _threatColorId = Shader.PropertyToID("_ThreatColor");
    private static readonly int _threatFillOpacityId = Shader.PropertyToID("_ThreatFillOpacity");
    private static readonly int _threatHighlightFillOpacityId = Shader.PropertyToID("_ThreatHighlightFillOpacity");
    private static readonly int _meleeWarningColorId = Shader.PropertyToID("_MeleeWarningColor");
    private static readonly int _beastOutlineExclusionMaskId = Shader.PropertyToID("_OutlineBeastOutlineExclusionMask");

    [Header("윤곽선 표현")]
    [SerializeField] private Color _lineColor = Color.white;
    [SerializeField, Range(1, 6)] private int _lineWidth = 1;
    [SerializeField, Range(1f, 200f)] private float _depthSensitivity = 50f;
    [SerializeField, Range(1f, 20f)] private float _normalSensitivity = 4f;
    [SerializeField, Range(0.001f, 0.5f), Tooltip("선택 VFX의 RGB 밝기 검출 기준이다.")]
    private float _transparentThreshold = 0.015f;

    [Header("항상 보이는 배경")]
    [SerializeField, Tooltip("OutlineLayer Scenery 대상의 윤곽선 색상이다. 인지 범위·편집 모드와 무관하게 이 색으로 표시한다.")]
    private Color _sceneryColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    [Header("위협 실루엣")]
    [SerializeField, Tooltip("OutlineLayer Threat 대상의 실루엣 색상이다. 배경과 다른 선 위에 덮어 그린다.")]
    private Color _threatColor = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("위협 실루엣 내부를 채우는 불투명도다.")]
    private float _threatFillOpacity = 0.6f;
    [SerializeField, Range(0f, 1f), Tooltip("불릿 타임과 극한 회피 파동 동안 위협 실루엣 내부를 채우는 불투명도다.")]
    private float _threatHighlightFillOpacity = 0.6f;

    [Header("근접 공격 전조")]
    [SerializeField] private Color _meleeWarningColor = new Color(1f, 0.1f, 0.06f, 1f);

    [Header("렌더링 연결")]
    [SerializeField] private Shader _shader;
    [SerializeField] private Shader _transparentDepthShader;
    [SerializeField] private Shader _layerMaskShader;
    [SerializeField] private Shader _forcedRevealShader;
    [SerializeField] private Shader _meleeWarningMaskShader;
    private Material _material;
    private Material _depthMaterial;
    private Material _sceneryMaskMaterial;
    private Material _threatMaskMaterial;
    private Material _forcedRevealMaterial;
    private Material _meleeWarningMaskMaterial;
    private Material _beastOutlineExclusionMaskMaterial;
    private readonly OutlineVFXRouting _routing = new OutlineVFXRouting();
    private CapturePass _capture;
    private CapturePass _alwaysVisibleCapture;
    private TransparentDepthPass _depth;
    private LayerMaskPass _sceneryMask;
    private LayerMaskPass _threatMask;
    private ForcedRevealPass _forcedReveal;
    private ThreatPresentationRouting _threatRouting;
    private ThreatPresentationPass _presentation;
    private MeleeWarningMaskPass _meleeWarningMask;
    private BeastOutlineExclusionPass _beastOutlineExclusion;
    private OutlinePass _outline;

    /// <summary>
    /// 연결된 셰이더로 기존 캡처·마스크와 대상별 위협 색상·최종 윤곽선 패스를 생성한다.
    /// 기존 패스 리소스를 해제하고 현재 설정으로 재생성한다.
    /// </summary>
    public override void Create()
    {
        ReleaseResources();
        if (_shader == null)
        {
            Debug.LogError("ScreenSpaceOutlineFeature에 윤곽선 셰이더를 연결해야 합니다.", this);
            return;
        }
        if (_transparentDepthShader == null)
            _transparentDepthShader = Shader.Find("Hidden/BlindBlade/OutlineTransparentDepth");
        if (_transparentDepthShader == null)
        {
            Debug.LogError("ScreenSpaceOutlineFeature에 투명 깊이 셰이더를 연결해야 합니다.", this);
            return;
        }
        if (_layerMaskShader == null)
            _layerMaskShader = Shader.Find("Hidden/BlindBlade/OutlineLayerMask");
        if (_layerMaskShader == null)
        {
            Debug.LogError("ScreenSpaceOutlineFeature에 배경·위협 마스크 셰이더를 연결해야 합니다.", this);
            return;
        }
        if (_forcedRevealShader == null)
            _forcedRevealShader = Shader.Find("Hidden/BlindBlade/OutlineForcedReveal");
        if (_forcedRevealShader == null)
        {
            Debug.LogError("ScreenSpaceOutlineFeature에 강제 공개 마스크 셰이더를 연결해야 합니다.", this);
            return;
        }
        if (_meleeWarningMaskShader == null)
            _meleeWarningMaskShader = Shader.Find("Hidden/BlindBlade/OutlineMeleeWarningMask");
        if (_meleeWarningMaskShader == null)
        {
            Debug.LogError("ScreenSpaceOutlineFeature에 근접 공격 전조 마스크 셰이더를 연결해야 합니다.", this);
            return;
        }
        _material = CoreUtils.CreateEngineMaterial(_shader);
        _depthMaterial = CoreUtils.CreateEngineMaterial(_transparentDepthShader);
        _depthMaterial.SetFloat("_DepthBlendOp", SystemInfo.usesReversedZBuffer ? 4f : 3f);
        // 배경은 가려진 부분을 제외하고, 위협은 대나무 뒤에서도 보이도록 깊이 비교 없이 기록한다.
        _sceneryMaskMaterial = CoreUtils.CreateEngineMaterial(_layerMaskShader);
        _sceneryMaskMaterial.SetFloat("_MaskZTest", (float)CompareFunction.LessEqual);
        _threatMaskMaterial = CoreUtils.CreateEngineMaterial(_layerMaskShader);
        _threatMaskMaterial.SetFloat("_MaskZTest", (float)CompareFunction.Always);
        _capture = new CapturePass(OutlineVFXRouting.SELECTED_MASK, false);
        _alwaysVisibleCapture = new CapturePass(OutlineVFXRouting.ALWAYS_VISIBLE_MASK, true);
        _depth = new TransparentDepthPass(_depthMaterial);
        _sceneryMask = new LayerMaskPass(OutlineLayer.SCENERY_MASK, _sceneryMaskMaterial, false);
        _threatMask = new LayerMaskPass(OutlineLayer.THREAT_MASK, _threatMaskMaterial, true);
        _forcedRevealMaterial = CoreUtils.CreateEngineMaterial(_forcedRevealShader);
        _forcedReveal = new ForcedRevealPass(_forcedRevealMaterial);
        _threatRouting = new ThreatPresentationRouting(Resources.Load<Material>("Perception/ThreatPresentationMask"));
        _presentation = new ThreatPresentationPass(_threatRouting);
        _meleeWarningMaskMaterial = CoreUtils.CreateEngineMaterial(_meleeWarningMaskShader);
        _meleeWarningMask = new MeleeWarningMaskPass(_meleeWarningMaskMaterial);
        _beastOutlineExclusionMaskMaterial = CoreUtils.CreateEngineMaterial(_layerMaskShader);
        _beastOutlineExclusionMaskMaterial.SetFloat("_MaskZTest", (float)CompareFunction.LessEqual);
        _beastOutlineExclusion = new BeastOutlineExclusionPass(_beastOutlineExclusionMaskMaterial);
        _outline = new OutlinePass(_material, _capture, _depth, _alwaysVisibleCapture, _sceneryMask, _threatMask,
            _forcedReveal, _presentation, _meleeWarningMask, _beastOutlineExclusion);
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    /// <summary>
    /// 지원 카메라의 컬링 전에 선택 VFX와 위협의 임시 재질·표시 경로를 준비한다.
    /// 기능 비활성 상태에서는 원본 URP 렌더링을 유지한다.
    /// </summary>
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (!isActive || _outline == null || (camera.cameraType != CameraType.Game
            && camera.cameraType != CameraType.SceneView))
            return;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data != null && data.renderType == CameraRenderType.Overlay)
            return;
        _threatRouting.Restore();
        _routing.Prepare();
        _threatRouting.Prepare(camera);
    }

    /// <summary>
    /// 카메라 실행 후 원본 렌더러 상태를 복원한다.
    /// </summary>
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        _threatRouting?.Restore();
        _routing.Restore();
    }

    /// <summary>
    /// 기본 게임 카메라와 Scene View에 설정을 전달하고 기존 윤곽선 패스와 Beast 몸체 제외 마스크를 등록한다.
    /// Overlay·Preview·Reflection 카메라는 기존처럼 제외한다.
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var camera = renderingData.cameraData;
        if (_outline == null || camera.renderType == CameraRenderType.Overlay
            || (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
            return;
        _material.SetColor(_lineColorId, _lineColor);
        _material.SetFloat(_lineWidthId, _lineWidth);
        _material.SetFloat(_depthSensitivityId, _depthSensitivity);
        _material.SetFloat(_normalSensitivityId, _normalSensitivity);
        _material.SetFloat(_transparentThresholdId, _transparentThreshold);
        _meleeWarningMaskMaterial.SetFloat(_transparentThresholdId, _transparentThreshold);
        _sceneryMaskMaterial.SetFloat(_transparentThresholdId, _transparentThreshold);
        _threatMaskMaterial.SetFloat(_transparentThresholdId, _transparentThreshold);
        _material.SetFloat(_usePerceptionMaskId, Application.isPlaying ? 1f : 0f);
        _material.SetColor(_sceneryColorId, _sceneryColor);
        _material.SetColor(_threatColorId, _threatColor);
        _material.SetFloat(_threatFillOpacityId, _threatFillOpacity);
        _material.SetFloat(_threatHighlightFillOpacityId, _threatHighlightFillOpacity);
        _material.SetColor(_meleeWarningColorId, _meleeWarningColor);
        renderer.EnqueuePass(_capture);
        renderer.EnqueuePass(_alwaysVisibleCapture);
        renderer.EnqueuePass(_depth);
        renderer.EnqueuePass(_sceneryMask);
        renderer.EnqueuePass(_threatMask);
        renderer.EnqueuePass(_forcedReveal);
        renderer.EnqueuePass(_presentation);
        renderer.EnqueuePass(_beastOutlineExclusion);
        renderer.EnqueuePass(_meleeWarningMask);
        renderer.EnqueuePass(_outline);
    }

    /// <summary>
    /// 기능 제거 시 생성한 머티리얼과 호환 모드 타깃을 해제한다.
    /// disposing 값과 무관하게 이 기능의 리소스만 정리한다.
    /// </summary>
    protected override void Dispose(bool disposing) { ReleaseResources(); }

    /// <summary>
    /// 현재 패스의 머티리얼·타깃과 참조를 정리한다.
    /// 재생성 또는 제거 이후 이전 리소스를 재사용하지 않는다.
    /// </summary>
    private void ReleaseResources()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        _threatRouting?.Dispose();
        _routing.Dispose();
        CoreUtils.Destroy(_material);
        CoreUtils.Destroy(_depthMaterial);
        CoreUtils.Destroy(_sceneryMaskMaterial);
        CoreUtils.Destroy(_threatMaskMaterial);
        CoreUtils.Destroy(_forcedRevealMaterial);
        CoreUtils.Destroy(_meleeWarningMaskMaterial);
        CoreUtils.Destroy(_beastOutlineExclusionMaskMaterial);
        _capture?.Release();
        _alwaysVisibleCapture?.Release();
        _depth?.Release();
        _sceneryMask?.Release();
        _threatMask?.Release();
        _forcedReveal?.Release();
        _presentation?.Release();
        _meleeWarningMask?.Release();
        _beastOutlineExclusion?.Release();
        _outline?.Release();
        _material = null;
        _depthMaterial = null;
        _sceneryMaskMaterial = null;
        _threatMaskMaterial = null;
        _forcedRevealMaterial = null;
        _meleeWarningMaskMaterial = null;
        _beastOutlineExclusionMaskMaterial = null;
        _capture = null;
        _alwaysVisibleCapture = null;
        _depth = null;
        _sceneryMask = null;
        _threatMask = null;
        _forcedReveal = null;
        _threatRouting = null;
        _presentation = null;
        _meleeWarningMask = null;
        _beastOutlineExclusion = null;
        _outline = null;
    }

    private sealed class FrameTextures : ContextItem
    {
        public TextureHandle Opaque { get; set; }
        public TextureHandle AlwaysVisibleVfx { get; set; }
        public TextureHandle TransparentDepth { get; set; }
        public TextureHandle SceneryMask { get; set; }
        public TextureHandle ThreatMask { get; set; }
        public TextureHandle ForcedReveal { get; set; }
        public TextureHandle PresentationColor { get; set; }
        public TextureHandle PresentationData { get; set; }
        public TextureHandle MeleeWarningMask { get; set; }
        public TextureHandle BeastOutlineExclusion { get; set; }

        /// <summary>
        /// 프레임 종료 시 모든 핸들을 비워 다른 카메라나 프레임에 남기지 않는다.
        /// </summary>
        public override void Reset()
        {
            Opaque = TextureHandle.nullHandle;
            AlwaysVisibleVfx = TextureHandle.nullHandle;
            TransparentDepth = TextureHandle.nullHandle;
            SceneryMask = TextureHandle.nullHandle;
            ThreatMask = TextureHandle.nullHandle;
            ForcedReveal = TextureHandle.nullHandle;
            PresentationColor = TextureHandle.nullHandle;
            PresentationData = TextureHandle.nullHandle;
            MeleeWarningMask = TextureHandle.nullHandle;
            BeastOutlineExclusion = TextureHandle.nullHandle;
        }
    }

    private sealed class ThreatPresentationPass : ScriptableRenderPass
    {
        private readonly ThreatPresentationRouting _routing;
        private RTHandle _legacyColor;
        private RTHandle _legacyData;
        private RTHandle _legacyDepth;
        public Texture ColorTexture => _routing.DrawCount > 0 && _legacyColor != null
            ? _legacyColor.rt : Texture2D.blackTexture;
        public Texture DataTexture => _routing.DrawCount > 0 && _legacyData != null
            ? _legacyData.rt : Texture2D.blackTexture;

        private sealed class PresentationData
        {
            public ThreatPresentationRouting.ThreatDraw[] Draws { get; set; }
        }

        /// <summary>routing의 카메라별 위협 캡처 목록을 윤곽선 합성 전에 그리도록 연결한다.</summary>
        public ThreatPresentationPass(ThreatPresentationRouting routing)
        {
            _routing = routing;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// graph·frameData로 색상·경계 데이터와 위협끼리만 비교하는 깊이 타깃을 생성한다.
        /// 표시할 대상이 없으면 검정 기본 텍스처를 제공해 이전 카메라의 결과를 남기지 않는다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var frame = frameData.GetOrCreate<FrameTextures>();
            // 이 프로젝트의 URP에서는 XR 기본 텍스처가 초기화되지 않으므로 일반 2D 기본값을 사용한다.
            frame.PresentationColor = graph.defaultResources.blackTexture;
            frame.PresentationData = graph.defaultResources.blackTexture;
            if (_routing.DrawCount == 0)
                return;
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = "Threat Presentation Color";
            desc.depthBufferBits = DepthBits.None;
            desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.msaaSamples = MSAASamples.None;
            desc.filterMode = FilterMode.Point;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            frame.PresentationColor = graph.CreateTexture(desc);
            desc.name = "Threat Presentation Data";
            frame.PresentationData = graph.CreateTexture(desc);
            desc.name = "Threat Presentation Depth";
            desc.colorFormat = GraphicsFormat.None;
            desc.depthBufferBits = DepthBits.Depth32;
            var depth = graph.CreateTexture(desc);
            using var builder = graph.AddRasterRenderPass<PresentationData>("Threat Presentation", out var data);
            data.Draws = _routing.CopyDraws();
            builder.SetRenderAttachment(frame.PresentationColor, 0, AccessFlags.Write);
            builder.SetRenderAttachment(frame.PresentationData, 1, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(depth, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PresentationData>(DrawPresentation);
        }

        /// <summary>data의 위협 외형을 전용 재질로 그려 대상별 색상·가시성·깊이를 기록한다.</summary>
        private static void DrawPresentation(PresentationData data, RasterGraphContext context)
        {
            foreach (var draw in data.Draws)
                for (int i = 0; i < draw.SubMeshCount; i++)
                    context.cmd.DrawRenderer(draw.Renderer, draw.Material, i, 0);
        }

        /// <summary>입력 없이 호환 모드에서 할당한 색상·데이터·깊이 타깃을 해제한다.</summary>
        public void Release()
        {
            _legacyColor?.Release();
            _legacyData?.Release();
            _legacyDepth?.Release();
        }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// context·renderingData로 호환 모드의 개별 위협 색상과 데이터를 기록한다.
        /// 장면 깊이 대신 전용 깊이를 사용해 벽 차폐와 위협끼리의 앞뒤를 분리한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_routing.DrawCount == 0)
                return;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyColor, desc, FilterMode.Point, name: "Threat Presentation Color");
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyData, desc, FilterMode.Point, name: "Threat Presentation Data");
            desc.graphicsFormat = GraphicsFormat.None;
            desc.depthStencilFormat = GraphicsFormat.D32_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyDepth, desc, name: "Threat Presentation Depth");
            var cmd = CommandBufferPool.Get("Threat Presentation");
            cmd.SetRenderTarget(new RenderTargetIdentifier[] { _legacyColor.nameID, _legacyData.nameID }, _legacyDepth.nameID);
            cmd.ClearRenderTarget(true, true, Color.clear);
            foreach (var draw in _routing.CopyDraws())
                for (int i = 0; i < draw.SubMeshCount; i++)
                    cmd.DrawRenderer(draw.Renderer, draw.Material, i, 0);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class LayerMaskPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> _tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
        private readonly FilteringSettings _filter;
        private readonly Material _material;
        private readonly bool _threat;
        private RTHandle _legacyMask;
        public RTHandle LegacyMask => _legacyMask;

        private sealed class MaskData { public RendererListHandle Renderers { get; set; } }

        /// <summary>
        /// renderingMask로 표시 대상을 고르고 material의 깊이 비교로 마스크를 기록하도록 준비한다.
        /// threat는 결과 핸들을 위협 또는 배경 슬롯 중 어디에 저장할지 결정한다.
        /// </summary>
        public LayerMaskPass(uint renderingMask, Material material, bool threat)
        {
            _filter = new FilteringSettings(RenderQueueRange.all, -1, renderingMask);
            _material = material;
            _threat = threat;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// 카메라 크기의 단일 채널 타깃에 표시 대상이 덮는 영역을 기록한다.
        /// 장면 깊이는 읽기만 하며 결과 핸들은 현재 프레임에만 보관한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = _threat ? "Outline Threat Mask" : "Outline Scenery Mask";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.colorFormat = GraphicsFormat.R8_UNorm;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            var target = graph.CreateTexture(desc);
            var drawing = RenderingUtils.CreateDrawingSettings(_tags, frameData.Get<UniversalRenderingData>(),
                frameData.Get<UniversalCameraData>(), frameData.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var parameters = new RendererListParams(frameData.Get<UniversalRenderingData>().cullResults, drawing, _filter);
            using var builder = graph.AddRasterRenderPass<MaskData>(desc.name, out var data);
            data.Renderers = graph.CreateRendererList(parameters);
            builder.UseRendererList(data.Renderers);
            builder.AllowPassCulling(false);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc<MaskData>(DrawMask);
            var frame = frameData.GetOrCreate<FrameTextures>();
            if (_threat)
                frame.ThreatMask = target;
            else
                frame.SceneryMask = target;
        }

        /// <summary>
        /// 현재 카메라의 표시 대상 렌더러 목록을 마스크 전용 머티리얼로 출력한다.
        /// </summary>
        private static void DrawMask(MaskData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.Renderers);
        }

        /// <summary>
        /// 호환 모드에서 할당한 마스크 타깃을 해제한다.
        /// </summary>
        public void Release() { _legacyMask?.Release(); }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 표시 대상이 덮는 영역을 단일 채널 타깃에 기록한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyMask, desc,
                name: _threat ? "Outline Threat Mask" : "Outline Scenery Mask");
            var cmd = CommandBufferPool.Get("Outline Layer Mask");
            CoreUtils.SetRenderTarget(cmd, _legacyMask, renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            var drawing = CreateDrawingSettings(_tags, ref renderingData, SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var filter = _filter;
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class ForcedRevealPass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> _tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
        private readonly FilteringSettings _filter = new FilteringSettings(RenderQueueRange.all,
            -1, OutlineLayer.FORCED_REVEAL_MASK);
        private readonly Material _material;
        private RTHandle _legacyMask;
        public RTHandle LegacyMask => _legacyMask;

        private sealed class MaskData { public RendererListHandle Renderers { get; set; } }

        /// <summary>
        /// 강제 공개 비트를 가진 렌더러를 material로 그려 렌더러별 세기를 기록하도록 준비한다.
        /// </summary>
        public ForcedRevealPass(Material material)
        {
            _material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// 카메라 크기의 RGBA 타깃에 강제 공개 대상의 세기(r)와 덮임(b)을 기록한다.
        /// 장면 깊이는 읽기만 하며 가려진 부분은 기록하지 않는다. 결과 핸들은 현재 프레임에만 보관한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = "Outline Forced Reveal";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            var target = graph.CreateTexture(desc);
            var drawing = RenderingUtils.CreateDrawingSettings(_tags, frameData.Get<UniversalRenderingData>(),
                frameData.Get<UniversalCameraData>(), frameData.Get<UniversalLightData>(), SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var parameters = new RendererListParams(frameData.Get<UniversalRenderingData>().cullResults, drawing, _filter);
            using var builder = graph.AddRasterRenderPass<MaskData>(desc.name, out var data);
            data.Renderers = graph.CreateRendererList(parameters);
            builder.UseRendererList(data.Renderers);
            builder.AllowPassCulling(false);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc<MaskData>(DrawMask);
            frameData.GetOrCreate<FrameTextures>().ForcedReveal = target;
        }

        /// <summary>
        /// 현재 카메라의 강제 공개 렌더러 목록을 전용 머티리얼로 출력한다.
        /// </summary>
        private static void DrawMask(MaskData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.Renderers);
        }

        /// <summary>
        /// 호환 모드에서 할당한 마스크 타깃을 해제한다.
        /// </summary>
        public void Release() { _legacyMask?.Release(); }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 강제 공개 대상의 세기를 RGBA 타깃에 기록한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyMask, desc, name: "Outline Forced Reveal");
            var cmd = CommandBufferPool.Get("Outline Forced Reveal");
            CoreUtils.SetRenderTarget(cmd, _legacyMask, renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            var drawing = CreateDrawingSettings(_tags, ref renderingData, SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var filter = _filter;
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class CapturePass : ScriptableRenderPass
    {
        private readonly List<ShaderTagId> _tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
        private readonly FilteringSettings _filter;
        private readonly bool _alwaysVisible;
        private sealed class ColorData { public RendererListHandle Renderers { get; set; } }
        private RTHandle _legacyColor;
        public RTHandle LegacyColor => _legacyColor;

        /// <summary>
        /// renderingMask와 alwaysVisible로 캡처 대상을 구분하고 중간 카메라 타깃을 요청한다.
        /// </summary>
        public CapturePass(uint renderingMask, bool alwaysVisible)
        {
            _filter = new FilteringSettings(RenderQueueRange.all, -1, renderingMask);
            _alwaysVisible = alwaysVisible;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
        }

        /// <summary>
        /// activeColorTexture 형식의 빈 타깃에 선택 VFX만 원래 셰이더로 기록한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = _alwaysVisible ? "Outline Always Visible VFX" : "Outline Opaque Color";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            var copy = graph.CreateTexture(desc);
            var drawing = RenderingUtils.CreateDrawingSettings(_tags, frameData.Get<UniversalRenderingData>(),
                frameData.Get<UniversalCameraData>(), frameData.Get<UniversalLightData>(), SortingCriteria.CommonTransparent);
            var parameters = new RendererListParams(frameData.Get<UniversalRenderingData>().cullResults, drawing, _filter);
            using var builder = graph.AddRasterRenderPass<ColorData>(desc.name, out var data);
            data.Renderers = graph.CreateRendererList(parameters);
            builder.UseRendererList(data.Renderers);
            builder.AllowPassCulling(false);
            builder.UseAllGlobalTextures(true);
            builder.SetRenderAttachment(copy, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            builder.SetRenderFunc<ColorData>(DrawColor);
            var frame = frameData.GetOrCreate<FrameTextures>();
            if (_alwaysVisible)
                frame.AlwaysVisibleVfx = copy;
            else
                frame.Opaque = copy;
        }

        /// <summary>
        /// 선택한 VFX를 원래 셰이더로 별도 투명 타깃에 그려 알파 모양을 보관한다.
        /// </summary>
        private static void DrawColor(ColorData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.Renderers);
        }

        /// <summary>
        /// 호환 모드에서 할당한 색상 타깃을 해제한다.
        /// </summary>
        public void Release() { _legacyColor?.Release(); }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 선택 VFX만 원래 셰이더로 기록한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyColor, desc,
                name: _alwaysVisible ? "Outline Always Visible VFX" : "Outline Opaque Color");
            var cmd = CommandBufferPool.Get("Outline Opaque Capture");
            CoreUtils.SetRenderTarget(cmd, LegacyColor, renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            var drawing = CreateDrawingSettings(_tags, ref renderingData, SortingCriteria.CommonTransparent);
            var filter = _filter;
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class TransparentDepthPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly List<ShaderTagId> _tags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit")
            };
        private readonly FilteringSettings _filter = new FilteringSettings(RenderQueueRange.all,
            -1, OutlineVFXRouting.SELECTED_MASK);
        private RTHandle _legacyDepth;
        public RTHandle LegacyDepth => _legacyDepth;

        private sealed class DepthData { public RendererListHandle Renderers { get; set; } }

        /// <summary>
        /// material로 투명 기하 깊이를 수집하도록 합성 후 실행 순서를 지정한다.
        /// </summary>
        public TransparentDepthPass(Material material)
        {
            _material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// 카메라 깊이와 같은 크기의 타깃에 투명 기하의 최근접 깊이를 기록한다.
        /// 불투명 깊이는 읽기만 하며 결과 핸들은 현재 프레임에만 보관한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeDepthTexture.GetDescriptor(graph);
            desc.name = "Outline Transparent Depth";
            desc.depthBufferBits = DepthBits.None;
            desc.colorFormat = GraphicsFormat.R32_SFloat;
            desc.clearBuffer = true;
            desc.clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white;
            var target = graph.CreateTexture(desc);
            var drawing = RenderingUtils.CreateDrawingSettings(_tags, frameData.Get<UniversalRenderingData>(),
                frameData.Get<UniversalCameraData>(), frameData.Get<UniversalLightData>(), SortingCriteria.CommonTransparent);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var parameters = new RendererListParams(frameData.Get<UniversalRenderingData>().cullResults, drawing, _filter);
            using var builder = graph.AddRasterRenderPass<DepthData>("Outline Transparent Depth", out var data);
            data.Renderers = graph.CreateRendererList(parameters);
            builder.UseRendererList(data.Renderers);
            builder.AllowPassCulling(false);
            builder.SetRenderAttachment(target, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc<DepthData>(DrawDepth);
            frameData.GetOrCreate<FrameTextures>().TransparentDepth = target;
        }

        /// <summary>
        /// 현재 카메라의 투명 렌더러 목록을 깊이 전용 머티리얼로 출력한다.
        /// </summary>
        private static void DrawDepth(DepthData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.Renderers);
        }

        /// <summary>
        /// 호환 모드에서 할당한 투명 깊이 타깃을 해제한다.
        /// </summary>
        public void Release() { _legacyDepth?.Release(); }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 불투명 표면 앞의 투명 기하 깊이를 별도 색상 타깃에 기록한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.graphicsFormat = GraphicsFormat.R32_SFloat;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyDepth, desc, FilterMode.Point, name: "Outline Transparent Depth");
            var cmd = CommandBufferPool.Get("Outline Transparent Depth");
            CoreUtils.SetRenderTarget(cmd, LegacyDepth, renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color, SystemInfo.usesReversedZBuffer ? Color.black : Color.white);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
            var drawing = CreateDrawingSettings(_tags, ref renderingData, SortingCriteria.CommonTransparent);
            drawing.overrideMaterial = _material;
            drawing.overrideMaterialPassIndex = 0;
            var filter = _filter;
            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class MeleeWarningMaskPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private RTHandle _legacyMask;

        public RTHandle LegacyMask => _legacyMask;

        private sealed class MaskData
        {
            public Material WarningMaterial { get; set; }
            public Renderer[] WarningRenderers { get; set; }
        }

        /// <summary>
        /// material은 전조 중인 렌더러의 붉은 세기를 R 채널에 기록한다.
        /// material은 공격 중 렌더러 배열에서 사용한다.
        /// </summary>
        public MeleeWarningMaskPass(Material material)
        {
            _material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// 활성 전조 컴포넌트와 도약 전조 중인 DefenseBeast, 돌진 중인 보스의 몸체 렌더러를 수집한다.
        /// 현재 카메라 패스에서 사용할 중복 없는 렌더러 배열을 반환한다.
        /// </summary>
        private static Renderer[] CollectRenderers()
        {
            var collected = new HashSet<Renderer>();

            foreach (EnemyMeleeWarningVisual warning
                in EnemyMeleeWarningVisual.ActiveInstances)
            {
                if (warning == null ||
                    !warning.isActiveAndEnabled ||
                    warning.RedIntensity <= 0f)
                    continue;

                foreach (Renderer renderer in warning.BodyRenderers)
                {
                    if (renderer != null &&
                        renderer.enabled &&
                        renderer.gameObject.activeInHierarchy)
                        collected.Add(renderer);
                }
            }

            // 돌진 패턴 중인 보스의 연기 몸체도 같은 붉은 테두리 마스크에 그린다.
            foreach (BossPresentation boss in BossPresentation.ActiveChargeWarnings)
            {
                if (boss == null || !boss.isActiveAndEnabled || boss.ChargeWarningIntensity <= 0f)
                    continue;

                foreach (Renderer renderer in boss.ChargeWarningRenderers)
                {
                    if (renderer != null &&
                        renderer.enabled &&
                        renderer.gameObject.activeInHierarchy)
                        collected.Add(renderer);
                }
            }

            // 공격을 준비하는 쌍둥이 보스의 연기 몸체도 붉어지는 정도만큼 같은 마스크에 그린다.
            foreach (TwinBossWarningVisual twin in TwinBossWarningVisual.ActiveInstances)
            {
                if (twin == null || !twin.isActiveAndEnabled || twin.Intensity <= 0f)
                    continue;

                foreach (Renderer renderer in twin.WarningRenderers)
                {
                    if (renderer != null &&
                        renderer.enabled &&
                        renderer.gameObject.activeInHierarchy)
                        collected.Add(renderer);
                }
            }

            foreach (DefenseBeastWolfVisual beast in DefenseBeastWolfVisual.ActiveInstances)
            {
                if (beast == null || !beast.isActiveAndEnabled || beast.WarningIntensity <= 0f)
                    continue;

                foreach (Renderer renderer in beast.BodyRenderers)
                {
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        collected.Add(renderer);
                }
            }

            var result = new Renderer[collected.Count];
            collected.CopyTo(result);
            return result;
        }

        /// <summary>
        /// 현재 카메라 크기의 마스크를 만들고 경고 렌더러를 등록한다.
        /// 대상이 없는 프레임에도 검정으로 초기화해 이전 프레임의 붉은색을 제거한다.
        /// </summary>
        public override void RecordRenderGraph(
            RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = "Melee Warning Mask";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            desc.colorFormat = GraphicsFormat.R8_UNorm;
            desc.filterMode = FilterMode.Point;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;

            var mask = graph.CreateTexture(desc);

            using var builder =
                graph.AddRasterRenderPass<MaskData>(
                    "Melee Warning Mask", out var data);

            data.WarningMaterial = _material;
            data.WarningRenderers = CollectRenderers();

            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(
                resources.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<MaskData>(DrawMask);

            frameData.GetOrCreate<FrameTextures>().MeleeWarningMask = mask;
        }

        /// <summary>
        /// 전조 중인 입자와 몸체를 붉은색 세기 마스크에 기록한다.
        /// 원본 머티리얼과 GameObject 상태는 변경하지 않는다.
        /// </summary>
        private static void DrawMask(
            MaskData data, RasterGraphContext context)
        {
            foreach (Renderer renderer in data.WarningRenderers)
            {
                if (renderer != null)
                    context.cmd.DrawRenderer(renderer, data.WarningMaterial, 0, 0);
            }
        }

        /// <summary>
        /// 호환 모드의 마스크 타깃을 해제한다.
        /// 이후 프레임에서 이전 타깃을 재사용하지 않는다.
        /// </summary>
        public void Release()
        {
            _legacyMask?.Release();
        }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 전조 파티클과 몸체를 붉은색 세기 마스크에 기록한다.
        /// 카메라 깊이를 읽어 가려진 표면은 마스크에서 제외한다.
        /// </summary>
        public override void Execute(
            ScriptableRenderContext context,
            ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;

            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _legacyMask, desc, FilterMode.Point,
                name: "Melee Warning Mask");

            var cmd = CommandBufferPool.Get("Melee Warning Mask");
            CoreUtils.SetRenderTarget(
                cmd,
                _legacyMask,
                renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color,
                Color.clear);

            foreach (Renderer renderer in CollectRenderers())
            {
                if (renderer != null)
                    cmd.DrawRenderer(renderer, _material, 0, 0);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class BeastOutlineExclusionPass : ScriptableRenderPass
    {
        private readonly Material _material;
        private RTHandle _legacyMask;

        public RTHandle LegacyMask => _legacyMask;

        private sealed class MaskData
        {
            public Material Material { get; set; }
            public Renderer[] Renderers { get; set; }
        }

        /// <summary>
        /// Beast 몸체 렌더러를 깊이 검사하는 제외 마스크에 연결한다.
        /// material은 화면 윤곽선이 몸체 메시의 조각 경계를 제외할 때 사용한다.
        /// </summary>
        public BeastOutlineExclusionPass(Material material)
        {
            _material = material;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <summary>
        /// 활성 DefenseBeast의 보이는 몸체 렌더러를 중복 없이 모아 반환한다.
        /// 수집 결과는 해당 카메라의 제외 마스크에서 사용한다.
        /// </summary>
        private static Renderer[] CollectRenderers()
        {
            var collected = new HashSet<Renderer>();

            foreach (DefenseBeastWolfVisual beast in DefenseBeastWolfVisual.ActiveInstances)
            {
                if (beast == null || !beast.isActiveAndEnabled)
                    continue;

                foreach (Renderer renderer in beast.BodyRenderers)
                {
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                        collected.Add(renderer);
                }
            }

            var result = new Renderer[collected.Count];
            collected.CopyTo(result);
            return result;
        }

        /// <summary>
        /// 보이는 Beast 표면을 R8 마스크에 기록하고 결과를 현재 프레임에 연결한다.
        /// 활성 카메라 깊이를 읽어 가려진 Beast 표면은 제외한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var frame = frameData.GetOrCreate<FrameTextures>();
            frame.BeastOutlineExclusion = graph.defaultResources.blackTexture;
            Renderer[] renderers = CollectRenderers();
            if (renderers.Length == 0)
                return;

            var resources = frameData.Get<UniversalResourceData>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = "Beast Outline Exclusion Mask";
            desc.depthBufferBits = DepthBits.None;
            desc.colorFormat = GraphicsFormat.R8_UNorm;
            desc.msaaSamples = MSAASamples.None;
            desc.filterMode = FilterMode.Point;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            var mask = graph.CreateTexture(desc);

            using var builder = graph.AddRasterRenderPass<MaskData>(
                "Beast Outline Exclusion Mask", out var data);
            data.Material = _material;
            data.Renderers = renderers;
            builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<MaskData>(DrawMask);
            frame.BeastOutlineExclusion = mask;
        }

        /// <summary>
        /// 현재 Beast 몸체 렌더러를 제외 마스크에 그린다.
        /// 머티리얼과 오브젝트 상태는 변경하지 않는다.
        /// </summary>
        private static void DrawMask(MaskData data, RasterGraphContext context)
        {
            foreach (Renderer renderer in data.Renderers)
            {
                if (renderer != null)
                    context.cmd.DrawRenderer(renderer, data.Material, 0, 0);
            }
        }

        /// <summary>
        /// 호환 모드용 제외 마스크 타깃을 해제한다.
        /// </summary>
        public void Release()
        {
            _legacyMask?.Release();
        }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 보이는 Beast 몸체를 깊이 검사하며 제외 마스크에 기록한다.
        /// 마스크는 같은 프레임의 화면 윤곽선 패스가 읽는다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.graphicsFormat = GraphicsFormat.R8_UNorm;
            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _legacyMask, desc, FilterMode.Point, name: "Beast Outline Exclusion Mask");

            var cmd = CommandBufferPool.Get("Beast Outline Exclusion Mask");
            CoreUtils.SetRenderTarget(
                cmd,
                _legacyMask,
                renderingData.cameraData.renderer.cameraDepthTargetHandle,
                ClearFlag.Color,
                Color.clear);

            foreach (Renderer renderer in CollectRenderers())
            {
                if (renderer != null)
                    cmd.DrawRenderer(renderer, _material, 0, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
    }

    private sealed class OutlinePass : ScriptableRenderPass
    {
        private readonly Material _material;
        private readonly CapturePass _capture;
        private readonly TransparentDepthPass _depth;
        private readonly CapturePass _alwaysVisibleCapture;
        private readonly LayerMaskPass _sceneryMask;
        private readonly LayerMaskPass _threatMask;
        private readonly ForcedRevealPass _forcedReveal;
        private readonly ThreatPresentationPass _presentation;
        private readonly MeleeWarningMaskPass _meleeWarningMask;
        private readonly BeastOutlineExclusionPass _beastOutlineExclusion;
        private RTHandle _legacyResult;

        private sealed class PassData
        {
            public Material Material { get; set; }
            public TextureHandle Source { get; set; }
            public TextureHandle Opaque { get; set; }
            public TextureHandle AlwaysVisibleVfx { get; set; }
            public TextureHandle TransparentDepth { get; set; }
            public TextureHandle SceneryMask { get; set; }
            public TextureHandle ThreatMask { get; set; }
            public TextureHandle ForcedReveal { get; set; }
            public TextureHandle PresentationColor { get; set; }
            public TextureHandle PresentationData { get; set; }
            public TextureHandle MeleeWarningMask { get; set; }
            public TextureHandle BeastOutlineExclusion { get; set; }
            public TextureHandle SceneDepth { get; set; }
            public TextureHandle Normals { get; set; }
        }

        /// <summary>
        /// material과 기존 캡처·마스크·강제 공개 마스크 및 presentation을 연결해 윤곽선 입력을 준비한다.
        /// 깊이·노멀 입력과 중간 카메라 타깃을 요청한다.
        /// </summary>
        public OutlinePass(Material material, CapturePass capture, TransparentDepthPass depth,
            CapturePass alwaysVisibleCapture, LayerMaskPass sceneryMask, LayerMaskPass threatMask,
            ForcedRevealPass forcedReveal, ThreatPresentationPass presentation,
            MeleeWarningMaskPass meleeWarningMask, BeastOutlineExclusionPass beastOutlineExclusion)
        {
            _material = material;
            _capture = capture;
            _depth = depth;
            _alwaysVisibleCapture = alwaysVisibleCapture;
            _sceneryMask = sceneryMask;
            _threatMask = threatMask;
            _forcedReveal = forcedReveal;
            _presentation = presentation;
            _meleeWarningMask = meleeWarningMask;
            _beastOutlineExclusion = beastOutlineExclusion;
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        /// <summary>
        /// 최종 색상·불투명 색상·투명 깊이를 명시적으로 읽어 별도 합성 타깃에 출력한다.
        /// 같은 텍스처의 동시 읽기·쓰기를 피한 뒤 카메라 색상으로 복사한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var frame = frameData.Get<FrameTextures>();
            var desc = resources.activeColorTexture.GetDescriptor(graph);
            desc.name = "Outline Composite";
            desc.depthBufferBits = DepthBits.None;
            desc.msaaSamples = MSAASamples.None;
            var result = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PassData>("Screen Space Outline", out var data))
            {
                data.Material = _material;
                data.Source = resources.activeColorTexture;
                data.Opaque = frame.Opaque;
                data.AlwaysVisibleVfx = frame.AlwaysVisibleVfx;
                data.TransparentDepth = frame.TransparentDepth;
                data.SceneryMask = frame.SceneryMask;
                data.ThreatMask = frame.ThreatMask;
                data.ForcedReveal = frame.ForcedReveal;
                data.PresentationColor = frame.PresentationColor;
                data.PresentationData = frame.PresentationData;
                data.MeleeWarningMask = frame.MeleeWarningMask;
                data.BeastOutlineExclusion = frame.BeastOutlineExclusion;
                data.SceneDepth = resources.cameraDepthTexture;
                data.Normals = resources.cameraNormalsTexture;
                builder.UseTexture(data.Source, AccessFlags.Read);
                builder.UseTexture(data.Opaque, AccessFlags.Read);
                builder.UseTexture(data.AlwaysVisibleVfx, AccessFlags.Read);
                builder.UseTexture(data.TransparentDepth, AccessFlags.Read);
                builder.UseTexture(data.SceneryMask, AccessFlags.Read);
                builder.UseTexture(data.ThreatMask, AccessFlags.Read);
                builder.UseTexture(data.ForcedReveal, AccessFlags.Read);
                builder.UseTexture(data.PresentationColor, AccessFlags.Read);
                builder.UseTexture(data.PresentationData, AccessFlags.Read);
                builder.UseTexture(data.MeleeWarningMask, AccessFlags.Read);
                builder.UseTexture(data.BeastOutlineExclusion, AccessFlags.Read);
                builder.UseTexture(data.SceneDepth, AccessFlags.Read);
                builder.UseTexture(data.Normals, AccessFlags.Read);
                builder.SetRenderAttachment(result, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc<PassData>(DrawOutline);
            }
            graph.AddBlitPass(result, resources.activeColorTexture, Vector2.one, Vector2.zero);
        }

        /// <summary>
        /// 현재 프레임의 모든 텍스처를 속성 블록에 연결해 전체 화면 윤곽선을 그린다.
        /// </summary>
        private static void DrawOutline(PassData data, RasterGraphContext context)
        {
            var properties = context.renderGraphPool.GetTempMaterialPropertyBlock();
            properties.SetTexture("_BlitTexture", data.Source);
            properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
            properties.SetTexture("_OutlineOpaqueColor", data.Opaque);
            properties.SetTexture("_OutlineAlwaysVisibleVfxColor", data.AlwaysVisibleVfx);
            properties.SetTexture("_OutlineTransparentDepth", data.TransparentDepth);
            properties.SetTexture("_OutlineSceneryMask", data.SceneryMask);
            properties.SetTexture("_OutlineThreatMask", data.ThreatMask);
            properties.SetTexture("_OutlineForcedReveal", data.ForcedReveal);
            properties.SetTexture("_ThreatPresentationColor", data.PresentationColor);
            properties.SetTexture("_ThreatPresentationData", data.PresentationData);
            properties.SetTexture("_OutlineMeleeWarningMask", data.MeleeWarningMask);
            properties.SetTexture(_beastOutlineExclusionMaskId, data.BeastOutlineExclusion);
            properties.SetTexture("_CameraDepthTexture", data.SceneDepth);
            properties.SetTexture("_CameraNormalsTexture", data.Normals);
            context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3, 1, properties);
        }

        /// <summary>
        /// 호환 모드에서 할당한 합성 결과 타깃을 해제한다.
        /// </summary>
        public void Release() { _legacyResult?.Release(); }

#pragma warning disable CS0618, CS0672
        /// <summary>
        /// 호환 모드에서 저장한 색상과 투명 깊이로 최종 윤곽선을 합성한다.
        /// </summary>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _legacyResult, desc, name: "Outline Composite");
            _material.SetTexture("_OutlineOpaqueColor", _capture.LegacyColor);
            _material.SetTexture("_OutlineAlwaysVisibleVfxColor", _alwaysVisibleCapture.LegacyColor);
            _material.SetTexture("_OutlineTransparentDepth", _depth.LegacyDepth);
            _material.SetTexture("_OutlineSceneryMask", _sceneryMask.LegacyMask);
            _material.SetTexture("_OutlineThreatMask", _threatMask.LegacyMask);
            _material.SetTexture("_OutlineForcedReveal", _forcedReveal.LegacyMask);
            _material.SetTexture("_ThreatPresentationColor", _presentation.ColorTexture);
            _material.SetTexture("_ThreatPresentationData", _presentation.DataTexture);
            _material.SetTexture("_OutlineMeleeWarningMask", _meleeWarningMask.LegacyMask);
            _material.SetTexture(_beastOutlineExclusionMaskId, _beastOutlineExclusion.LegacyMask);
            var cmd = CommandBufferPool.Get("Screen Space Outline");
            Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle,
                _legacyResult, _material, 0);
            Blitter.BlitCameraTexture(cmd, _legacyResult, renderingData.cameraData.renderer.cameraColorTargetHandle);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#pragma warning restore CS0618, CS0672
    }
}
