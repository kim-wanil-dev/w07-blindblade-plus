using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SharedBodySmoke 템플릿 입자를 GPU 버퍼에 올리고 등록된 근접 잡몹마다 같은 입자를 인스턴스로 그린다.
/// 기존 몸 연기 렌더러가 거치던 선택 VFX 캡처·투명 깊이·근접 전조 마스크에 같은 모양을 기록한다.
/// </summary>
internal sealed class SharedBodySmokeRenderer
{
    internal const int COLOR_PASS = 0;
    internal const int DEPTH_PASS = 1;
    internal const int WARNING_PASS = 2;
    private const int PARTICLE_STRIDE = 3;
    private const int ANCHOR_STRIDE = 4;
    private const int VERTICES_PER_PARTICLE = 6;
    private static readonly int _particlesId = Shader.PropertyToID("_SharedSmokeParticles");
    private static readonly int _anchorsId = Shader.PropertyToID("_SharedSmokeAnchors");
    private static readonly int _particlesPerTemplateId = Shader.PropertyToID("_SharedSmokeParticlesPerTemplate");
    private static readonly int _maxParticleSizeId = Shader.PropertyToID("_SharedSmokeMaxParticleSize");
    private static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _transparentThresholdId = Shader.PropertyToID("_TransparentThreshold");
    private readonly Material _material;
    private ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[0];
    private Vector4[] _particleData = new Vector4[0];
    private Vector4[] _anchorData = new Vector4[0];
    private GraphicsBuffer _particleBuffer;
    private GraphicsBuffer _anchorBuffer;
    private int _particlesPerTemplate;
    private int _anchorCount;
    private int _preparedFrame = -1;
    private bool _hasWarning;

    internal Material Material => _material;
    internal bool HasInstances => _anchorCount > 0;
    internal bool HasWarning => _hasWarning;
    internal int VertexCount => _particlesPerTemplate * VERTICES_PER_PARTICLE;
    internal int InstanceCount => _anchorCount;

    /// <summary>shader로 인스턴스 머티리얼을 만들고 투명 깊이 누적 방식을 기존 깊이 머티리얼과 같게 맞춘다.</summary>
    internal SharedBodySmokeRenderer(Shader shader)
    {
        _material = CoreUtils.CreateEngineMaterial(shader);
        _material.SetFloat("_DepthBlendOp", SystemInfo.usesReversedZBuffer ? 4f : 3f);
    }

    /// <summary>threshold를 근접 전조 마스크와 같은 밝기 기준으로 설정한다.</summary>
    internal void SetTransparentThreshold(float threshold)
    {
        _material.SetFloat(_transparentThresholdId, threshold);
    }

    /// <summary>
    /// 이번 프레임에 한 번만 템플릿 입자의 로컬 위치·현재 크기·현재 색·회전과 등록 적의 행렬·템플릿·전조 세기를 버퍼에 올린다.
    /// 색은 원래 렌더러처럼 선형 색 공간이면 선형으로 바꾸고, 머티리얼 텍스처·색과 최대 입자 크기는 템플릿 렌더러에서 읽는다.
    /// 등록 적이나 템플릿이 없으면 인스턴스 수를 0으로 둔다.
    /// </summary>
    internal void Prepare()
    {
        if (_preparedFrame == Time.frameCount)
            return;
        _preparedFrame = Time.frameCount;
        _anchorCount = 0;
        _hasWarning = false;
        IReadOnlyList<SharedBodySmoke> smokes = SharedBodySmoke.Active;
        IReadOnlyList<ParticleSystem> templates = SharedBodySmoke.Templates;
        // 씬을 내리는 중에는 템플릿이 먼저 파괴될 수 있다.
        if (smokes.Count == 0 || templates.Count == 0 || templates[0] == null)
            return;

        ParticleSystemRenderer source = SharedBodySmoke.TemplateRenderer;
        int perTemplate = templates[0].main.maxParticles;
        if (_particles.Length < perTemplate)
            _particles = new ParticleSystem.Particle[perTemplate];
        EnsureCapacity(ref _particleData, ref _particleBuffer, templates.Count * perTemplate * PARTICLE_STRIDE);
        EnsureCapacity(ref _anchorData, ref _anchorBuffer, smokes.Count * ANCHOR_STRIDE);

        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear && source.applyActiveColorSpace;
        for (int t = 0; t < templates.Count; t++)
        {
            ParticleSystem system = templates[t];
            int count = system.GetParticles(_particles, perTemplate);
            for (int i = 0; i < perTemplate; i++)
            {
                int index = (t * perTemplate + i) * PARTICLE_STRIDE;
                if (i >= count)
                {
                    // 크기 0 입자는 네 모서리가 한 점에 모여 그려지지 않는다.
                    _particleData[index] = Vector4.zero;
                    _particleData[index + 1] = Vector4.zero;
                    _particleData[index + 2] = Vector4.zero;
                    continue;
                }
                ParticleSystem.Particle particle = _particles[i];
                Vector3 position = particle.position;
                Color color = particle.GetCurrentColor(system);
                if (linear)
                    color = color.linear;
                _particleData[index] = new Vector4(position.x, position.y, position.z, particle.GetCurrentSize(system));
                _particleData[index + 1] = color;
                _particleData[index + 2] = new Vector4(particle.rotation * Mathf.Deg2Rad, 0f, 0f, 0f);
            }
        }

        for (int a = 0; a < smokes.Count; a++)
        {
            SharedBodySmoke smoke = smokes[a];
            Matrix4x4 matrix = smoke.LocalToWorld;
            float warning = smoke.WarningIntensity;
            int index = a * ANCHOR_STRIDE;
            _anchorData[index] = matrix.GetRow(0);
            _anchorData[index + 1] = matrix.GetRow(1);
            _anchorData[index + 2] = matrix.GetRow(2);
            _anchorData[index + 3] = new Vector4(smoke.TemplateIndex % templates.Count, warning, 0f, 0f);
            _hasWarning |= warning > 0f;
        }

        _particleBuffer.SetData(_particleData, 0, 0, templates.Count * perTemplate * PARTICLE_STRIDE);
        _anchorBuffer.SetData(_anchorData, 0, 0, smokes.Count * ANCHOR_STRIDE);
        Material sourceMaterial = source.sharedMaterial;
        _material.SetTexture(_baseMapId, sourceMaterial.GetTexture(_baseMapId));
        _material.SetTextureScale(_baseMapId, sourceMaterial.GetTextureScale(_baseMapId));
        _material.SetTextureOffset(_baseMapId, sourceMaterial.GetTextureOffset(_baseMapId));
        _material.SetColor(_baseColorId, sourceMaterial.GetColor(_baseColorId));
        _material.SetFloat(_maxParticleSizeId, source.maxParticleSize);
        _material.SetInt(_particlesPerTemplateId, perTemplate);
        _material.SetBuffer(_particlesId, _particleBuffer);
        _material.SetBuffer(_anchorsId, _anchorBuffer);
        _particlesPerTemplate = perTemplate;
        _anchorCount = smokes.Count;
    }

    /// <summary>pass의 셰이더 패스로 등록 적 전체의 몸 연기를 한 번에 그린다. 인스턴스가 없으면 그리지 않는다.</summary>
    internal void Draw(RasterCommandBuffer cmd, int pass)
    {
        if (_anchorCount > 0)
            cmd.DrawProcedural(Matrix4x4.identity, _material, pass, MeshTopology.Triangles, VertexCount, _anchorCount);
    }

    /// <summary>호환 모드의 cmd에 pass의 셰이더 패스로 등록 적 전체의 몸 연기를 그린다. 인스턴스가 없으면 그리지 않는다.</summary>
    internal void Draw(CommandBuffer cmd, int pass)
    {
        if (_anchorCount > 0)
            cmd.DrawProcedural(Matrix4x4.identity, _material, pass, MeshTopology.Triangles, VertexCount, _anchorCount);
    }

    /// <summary>
    /// data 배열과 buffer가 required개의 float4를 담을 수 있게 늘린다. 부족할 때만 두 배 여유로 다시 만들고 기존 버퍼는 해제한다.
    /// </summary>
    private static void EnsureCapacity(ref Vector4[] data, ref GraphicsBuffer buffer, int required)
    {
        if (data.Length >= required && buffer != null)
            return;
        int capacity = Mathf.Max(required, data.Length * 2, 16);
        data = new Vector4[capacity];
        buffer?.Release();
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, 16);
    }

    /// <summary>GPU 버퍼와 인스턴스 머티리얼을 해제한다.</summary>
    internal void Dispose()
    {
        _particleBuffer?.Release();
        _anchorBuffer?.Release();
        _particleBuffer = null;
        _anchorBuffer = null;
        CoreUtils.Destroy(_material);
    }
}
