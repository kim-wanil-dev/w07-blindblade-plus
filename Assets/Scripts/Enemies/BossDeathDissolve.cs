using System.Collections;
using System.Collections.Generic;

using UnityEngine;

/// <summary>사망 순간 보스의 메시와 연기 입자를 복제해 윤곽선을 유지하며 위에서 아래로 재를 흩뿌린다.</summary>
public sealed class BossDeathDissolve : MonoBehaviour
{
    public const float DURATION = 3f;

    private const int MAX_ASH_PER_FRAME = 30;

    private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int _dissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int _minYId = Shader.PropertyToID("_MinY");
    private static readonly int _heightId = Shader.PropertyToID("_Height");

    [Header("사망 잔상")]
    private readonly List<MeshRenderer> _ghosts = new List<MeshRenderer>();
    private readonly List<ParticleSystem> _smokeRemnants = new List<ParticleSystem>();
    private readonly Dictionary<ParticleSystem, ParticleSystem.Particle[]> _smokeBuffers =
        new Dictionary<ParticleSystem, ParticleSystem.Particle[]>();
    private readonly List<Vector3> _dustOrigins = new List<Vector3>();
    private readonly List<Mesh> _bakedMeshes = new List<Mesh>();
    private Mesh _dustMesh;
    private Material _ghostMaterial;
    private Material _dustMaterial;
    private ParticleSystem _dust;
    private float _minimumY;
    private float _maximumY;

    /// <summary>health가 비활성화되기 전 활성 외형을 복제하고 독립적인 사망 잔상을 시작한다.</summary>
    public static void Play(EnemyHealth health)
    {
        GameObject effect = new GameObject("BossDeathDissolve");
        BossDeathDissolve dissolve = effect.AddComponent<BossDeathDissolve>();
        dissolve.Capture(health.transform);
        // 기존 보스 연기와 동일한 화면 공간 윤곽선 경로에 모든 소멸 잔상을 등록한다.
        effect.AddComponent<OutlineVFX>();
        dissolve.StartCoroutine(dissolve.RunDissolve());
    }

    /// <summary>target을 화면 중앙으로 옮기도록 플레이어 시점을 실제 시간 1.25초 동안 부드럽게 회전한다.</summary>
    public static IEnumerator FocusCamera(Vector3 target)
    {
        PlayerController player = Game.Player;
        if (player == null) yield break;
        PlayerMotor motor = player.GetComponent<PlayerMotor>();
        float elapsed = 0f;
        while (elapsed < 1.25f)
        {
            elapsed += Time.unscaledDeltaTime;
            motor.SmoothFaceTarget(target, 5.5f, Time.unscaledDeltaTime);
            yield return null;
        }
    }

    /// <summary>boss의 활성 메시와 현재 연기 입자를 캡처하고 재 방출기를 준비한다.</summary>
    private void Capture(Transform boss)
    {
        _minimumY = float.PositiveInfinity;
        _maximumY = float.NegativeInfinity;
        Shader ghostShader = Resources.Load<Shader>("Effects/BossDeathDissolve");
        Shader dustShader = Resources.Load<Shader>("Effects/BossDeathDust");
        if (ghostShader != null) _ghostMaterial = new Material(ghostShader);
        if (dustShader != null) _dustMaterial = new Material(dustShader);

        foreach (MeshRenderer source in boss.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy) continue;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                CaptureMesh(source, filter.sharedMesh);
        }
        foreach (SkinnedMeshRenderer source in boss.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy) continue;
            Mesh baked = new Mesh();
            source.BakeMesh(baked);
            _bakedMeshes.Add(baked);
            CaptureMesh(source, baked);
        }
        foreach (ParticleSystem smoke in boss.GetComponentsInChildren<ParticleSystem>(false))
            CaptureSmoke(smoke);

        if (_dustOrigins.Count == 0)
            _dustOrigins.Add(boss.position + Vector3.up);
        if (float.IsInfinity(_minimumY))
        {
            _minimumY = boss.position.y;
            _maximumY = _minimumY + 2f;
        }
        CreateDustEmitter();
    }

    /// <summary>source의 메시를 현재 월드 위치에 고정 복제하고 원래 색상과 표면 표본을 저장한다.</summary>
    private void CaptureMesh(Renderer source, Mesh mesh)
    {
        Bounds bounds = source.bounds;
        _minimumY = Mathf.Min(_minimumY, bounds.min.y);
        _maximumY = Mathf.Max(_maximumY, bounds.max.y);
        if (_ghostMaterial != null)
        {
            GameObject ghost = new GameObject("DissolvingPart", typeof(MeshFilter), typeof(MeshRenderer));
            ghost.transform.SetParent(transform, false);
            ghost.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            ghost.transform.localScale = source.transform.lossyScale;
            ghost.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ghost.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _ghostMaterial;
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            Material sourceMaterial = source.sharedMaterial;
            Color color = sourceMaterial != null && sourceMaterial.HasProperty(_baseColorId)
                ? sourceMaterial.GetColor(_baseColorId) : Color.gray;
            properties.SetColor(_baseColorId, Color.Lerp(color, new Color(0.65f, 0.6f, 0.52f), 0.25f));
            renderer.SetPropertyBlock(properties);
            _ghosts.Add(renderer);
        }
        if (mesh.isReadable)
        {
            Vector3[] vertices = mesh.vertices;
            int step = Mathf.Max(1, vertices.Length / 80);
            for (int index = 0; index < vertices.Length; index += step)
                _dustOrigins.Add(source.transform.TransformPoint(vertices[index]));
        }
        else
            _dustOrigins.Add(bounds.center);
    }

    /// <summary>smoke의 살아 있는 입자를 월드 좌표의 독립 잔상으로 옮겨 사망 후 서서히 흩어지게 한다.</summary>
    private void CaptureSmoke(ParticleSystem smoke)
    {
        if (!smoke.gameObject.activeInHierarchy || smoke.particleCount == 0) return;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[smoke.particleCount];
        int count = smoke.GetParticles(particles);
        if (count == 0) return;
        GameObject remnant = new GameObject("SmokeRemnant", typeof(ParticleSystem));
        remnant.transform.SetParent(transform, false);
        ParticleSystem clone = remnant.GetComponent<ParticleSystem>();
        clone.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = clone.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startSize3D = true;
        main.maxParticles = count + 8;
        ParticleSystem.EmissionModule emission = clone.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = clone.shape;
        shape.enabled = false;
        ParticleSystemRenderer sourceRenderer = smoke.GetComponent<ParticleSystemRenderer>();
        ParticleSystemRenderer cloneRenderer = clone.GetComponent<ParticleSystemRenderer>();
        cloneRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        cloneRenderer.renderMode = sourceRenderer.renderMode;
        cloneRenderer.alignment = sourceRenderer.alignment;
        cloneRenderer.sortMode = sourceRenderer.sortMode;
        cloneRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        cloneRenderer.sortingOrder = sourceRenderer.sortingOrder;
        List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>();
        sourceRenderer.GetActiveVertexStreams(streams);
        cloneRenderer.SetActiveVertexStreams(streams);
        if (sourceRenderer.renderMode == ParticleSystemRenderMode.Mesh)
            cloneRenderer.mesh = sourceRenderer.mesh;
        bool worldSpace = smoke.main.simulationSpace == ParticleSystemSimulationSpace.World;
        for (int index = 0; index < count; index++)
        {
            ParticleSystem.Particle particle = particles[index];
            particle.position = worldSpace ? particle.position : smoke.transform.TransformPoint(particle.position);
            particle.startColor = particle.GetCurrentColor(smoke);
            particle.startSize3D = particle.GetCurrentSize3D(smoke);
            particle.velocity = (worldSpace ? particle.velocity : smoke.transform.TransformVector(particle.velocity)) * 0.08f;
            particle.startLifetime = DURATION + 0.1f;
            particle.remainingLifetime = particle.startLifetime;
            particles[index] = particle;
            if (index % 3 == 0)
                _dustOrigins.Add(particle.position);
            _minimumY = Mathf.Min(_minimumY, particle.position.y);
            _maximumY = Mathf.Max(_maximumY, particle.position.y);
        }
        clone.Play();
        clone.SetParticles(particles, count);
        _smokeRemnants.Add(clone);
        _smokeBuffers.Add(clone, particles);
    }

    /// <summary>가루 셰이더로 실제 시간 기반 월드 입자 방출기를 생성하고 _dust에 저장한다.</summary>
    private void CreateDustEmitter()
    {
        if (_dustMaterial == null) return;
        GameObject dustObject = new GameObject("AshParticles", typeof(ParticleSystem));
        dustObject.transform.SetParent(transform, false);
        _dust = dustObject.GetComponent<ParticleSystem>();
        _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = _dust.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startSize3D = true;
        main.maxParticles = 2000;
        ParticleSystem.EmissionModule emission = _dust.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = _dust.shape;
        shape.enabled = false;
        ParticleSystem.VelocityOverLifetimeModule velocity = _dust.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.45f),
            new Keyframe(0.18f, 0.6f),
            new Keyframe(0.53f, 1f),
            new Keyframe(0.78f, 0.55f),
            new Keyframe(1f, 0.12f)));
        ParticleSystem.NoiseModule noise = _dust.noise;
        noise.enabled = true;
        noise.strengthMultiplier = 0.5f;
        noise.frequency = 2.5f;
        noise.scrollSpeedMultiplier = 1.3f;
        ParticleSystem.ColorOverLifetimeModule color = _dust.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(fade);
        ParticleSystemRenderer dustRenderer = _dust.GetComponent<ParticleSystemRenderer>();
        _dustMesh = CreateAshMesh();
        dustRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        dustRenderer.alignment = ParticleSystemRenderSpace.View;
        dustRenderer.mesh = _dustMesh;
        dustRenderer.sharedMaterial = _dustMaterial;
        _dust.Play();
    }

    /// <summary>아홉 방향의 반지름을 달리한 재 조각 메시를 만들어 반환한다.</summary>
    private Mesh CreateAshMesh()
    {
        const int points = 9;
        float[] radii = { 0.44f, 0.31f, 0.48f, 0.34f, 0.41f, 0.29f, 0.46f, 0.33f, 0.39f };
        Vector3[] vertices = new Vector3[points + 1];
        Vector2[] uv = new Vector2[points + 1];
        int[] triangles = new int[points * 3];
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int index = 0; index < points; index++)
        {
            float angle = index * Mathf.PI * 2f / points;
            Vector2 point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radii[index];
            vertices[index + 1] = new Vector3(point.x, point.y, 0f);
            uv[index + 1] = point + new Vector2(0.5f, 0.5f);
            triangles[index * 3] = 0;
            triangles[index * 3 + 1] = index + 1;
            triangles[index * 3 + 2] = (index + 1) % points + 1;
        }
        Mesh mesh = new Mesh { name = "IrregularBossAsh" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>DURATION 동안 메시와 연기 윤곽선을 위에서 아래로 침식시키고 재를 방출한다.</summary>
    private IEnumerator RunDissolve()
    {
        float elapsed = 0f;
        float dustBudget = 0f;
        while (elapsed < DURATION)
        {
            elapsed = Mathf.Min(DURATION, elapsed + Time.unscaledDeltaTime);
            float progress = elapsed / DURATION;
            foreach (MeshRenderer renderer in _ghosts)
            {
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetFloat(_dissolveId, progress);
                properties.SetFloat(_minYId, _minimumY);
                properties.SetFloat(_heightId, _maximumY - _minimumY);
                renderer.SetPropertyBlock(properties);
            }
            ErodeSmoke(progress);
            float emissionRate = Mathf.Lerp(450f, 1600f, Mathf.Sin(progress * Mathf.PI));
            dustBudget += Time.unscaledDeltaTime * emissionRate;
            while (dustBudget >= 1f)
            {
                EmitDust(progress);
                dustBudget -= 1f;
            }
            yield return null;
        }
        foreach (MeshRenderer renderer in _ghosts)
            if (renderer != null) renderer.enabled = false;
        Destroy(gameObject);
    }

    /// <summary>progress가 지나간 연기 입자를 위에서 아래로 불규칙하게 제거하고 사라진 위치에서 재를 날린다.</summary>
    private void ErodeSmoke(float progress)
    {
        float height = Mathf.Max(0.1f, _maximumY - _minimumY);
        int emitted = 0;
        foreach (ParticleSystem smoke in _smokeRemnants)
        {
            ParticleSystem.Particle[] particles = _smokeBuffers[smoke];
            int count = smoke.GetParticles(particles);
            bool changed = false;
            for (int index = 0; index < count; index++)
            {
                Vector3 position = particles[index].position;
                float noise = Mathf.PerlinNoise(position.x * 3.1f + position.z * 1.7f,
                    position.y * 3.7f + position.z * 2.3f);
                float erosion = Mathf.Clamp01((_maximumY - position.y) / height + (noise - 0.5f) * 0.28f);
                if (progress < erosion) continue;
                particles[index].remainingLifetime = 0f;
                changed = true;
                if (emitted >= MAX_ASH_PER_FRAME || Random.value > 0.34f) continue;
                EmitAsh(position, DURATION * (1f - progress));
                emitted++;
            }
            if (changed) smoke.SetParticles(particles, count);
        }
    }

    /// <summary>progress에 맞는 소멸 경계의 원본 표면을 찾아 재를 방출한다.</summary>
    private void EmitDust(float progress)
    {
        if (_dust == null || _dustOrigins.Count == 0) return;
        float remaining = DURATION * (1f - progress);
        if (remaining <= 0.05f) return;
        float height = Mathf.Max(0.1f, _maximumY - _minimumY);
        Vector3 origin = _dustOrigins[Random.Range(0, _dustOrigins.Count)];
        float closest = Mathf.Abs((_maximumY - origin.y) / height - progress);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 candidate = _dustOrigins[Random.Range(0, _dustOrigins.Count)];
            float distance = Mathf.Abs((_maximumY - candidate.y) / height - progress);
            if (distance >= closest) continue;
            origin = candidate;
            closest = distance;
            if (closest < 0.08f) break;
        }
        EmitAsh(origin, remaining);
    }

    /// <summary>origin에서 remaining 안에 소멸하는 작고 부드러운 회색 재 조각을 방출한다.</summary>
    private void EmitAsh(Vector3 origin, float remaining)
    {
        if (_dust == null || remaining <= 0.05f) return;
        float phase = 1f - remaining / DURATION;
        float stageSpeed = Mathf.Lerp(0.65f, 1f, Mathf.Sin(phase * Mathf.PI));
        Vector2 drift = Random.insideUnitCircle.normalized * Random.Range(1f, 2.2f) * stageSpeed;
        ParticleSystem.EmitParams ash = new ParticleSystem.EmitParams
        {
            position = origin,
            velocity = new Vector3(drift.x, Random.Range(0.08f, 0.7f), drift.y),
            startLifetime = Mathf.Min(Random.Range(0.7f, 1.1f), remaining),
            startSize3D = new Vector3(Random.Range(0.0027f, 0.0077f),
                Random.Range(0.0023f, 0.0067f), 1f),
            rotation = Random.Range(0f, 360f),
            startColor = Color.Lerp(new Color(0.42f, 0.4f, 0.39f, 0.6f),
                new Color(0.85f, 0.82f, 0.79f, 0.85f), Random.value)
        };
        _dust.Emit(ash, 1);
    }

    void OnDestroy()
    {
        if (_ghostMaterial != null) Destroy(_ghostMaterial);
        if (_dustMaterial != null) Destroy(_dustMaterial);
        if (_dustMesh != null) Destroy(_dustMesh);
        foreach (Mesh mesh in _bakedMeshes)
            if (mesh != null) Destroy(mesh);
    }
}
