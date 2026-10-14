using System;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

using BlindBlade.Combat.Contracts;
using BlindBlade.Perception;
using BlindBlade.UI.CombatHud;

/// <summary>C 전용 인지 설정·프리팹·작업 씬을 생성한다. 정식 게임 씬이나 프리팹을 저장하지 않는다.</summary>
public static class ThreatPresentationAuthoring
{
    private const string SETTINGS_PATH = "Assets/Settings/Perception/ThreatPresentationSettings.asset";
    private const string ROOT_PATH = "Assets/Scenes/Perception/C_ThreatPresentation";
    private const string SCENE_PATH = ROOT_PATH + "/C_ThreatPresentation.unity";
    private const string MELEE_PATH = ROOT_PATH + "/ThreatPreviewMelee.prefab";
    private const string PROJECTILE_PATH = ROOT_PATH + "/ThreatPreviewProjectile.prefab";
    private const string SURFACE_PATH = ROOT_PATH + "/ThreatPreviewSurface.mat";
    private const string FOOTSTEP_MARKER_PATH = "Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab";
    private const int FOOTSTEP_HUD_SLOTS = 24;
    private static CombatTimeToken _footstepPreviewTimeToken;

    /// <summary>
    /// 메뉴 또는 Unity CLI 입력 없이 C의 설정·전용 프리팹·작업 씬을 생성하고 연다.
    /// 기존 에셋과 편집값은 재생성하지 않으며 생성한 경로를 로그에 남긴다.
    /// </summary>
    [MenuItem("Tools/Perception/Open or Create C Threat Sandbox")]
    public static void OpenOrCreateSandbox()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("C 작업 씬은 Play를 종료한 뒤 여세요. 현재 씬에서는 대상을 바로 선택해 확인할 수 있습니다.");
            return;
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Directory.CreateDirectory("Assets/Settings/Perception");
        Directory.CreateDirectory(ROOT_PATH);
        AssetDatabase.Refresh();
        ThreatPresentationSettings settings = AssetDatabase.LoadAssetAtPath<ThreatPresentationSettings>(SETTINGS_PATH);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<ThreatPresentationSettings>();
            AssetDatabase.CreateAsset(settings, SETTINGS_PATH);
        }
        CreatePreviewMaterial();
        CreatePreviewPrefab(MELEE_PATH, PrimitiveType.Capsule, ThreatTargetKind.Melee);
        CreatePreviewPrefab(PROJECTILE_PATH, PrimitiveType.Sphere, ThreatTargetKind.Projectile);
        if (File.Exists(SCENE_PATH))
        {
            EditorSceneManager.OpenScene(SCENE_PATH);
            Debug.Log("C 인지 작업 씬 열기: " + SCENE_PATH);
            return;
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject observer = new GameObject("Observer");
        GameObject cameraObject = new GameObject("ViewCamera");
        cameraObject.transform.SetParent(observer.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Camera viewCamera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = Color.black;
        viewCamera.nearClipPlane = 0.1f;
        viewCamera.farClipPlane = 100f;
        GameObject presentation = new GameObject("ThreatPresentation");
        ThreatPresentationResolver resolver = presentation.AddComponent<ThreatPresentationResolver>();
        SerializedObject connection = new SerializedObject(resolver);
        connection.FindProperty("_settings").objectReferenceValue = settings;
        connection.FindProperty("_observer").objectReferenceValue = observer.transform;
        connection.FindProperty("_viewCamera").objectReferenceValue = viewCamera;
        connection.ApplyModifiedPropertiesWithoutUndo();

        CreateTarget("Near_Normal", new Vector3(0f, 0f, 2f), resolver);
        CreateTarget("Boundary_Normal", new Vector3(0f, 0f, 11.5f), resolver);
        CreateTarget("Outside_Normal", new Vector3(0f, 0f, 13f), resolver);
        CreateTarget("Offscreen_Normal", new Vector3(6f, 0f, 0f), resolver);
        CreateTarget("Charge_Default", new Vector3(-3f, 0f, 8f), resolver, charge: true);
        CreateTarget("Charge_Individual18m", new Vector3(4f, 0f, 15f), resolver, charge: true, radius: 18f);
        CreateTarget("Hostile_Projectile", new Vector3(-2f, 1f, 6f), resolver, projectile: true);
        CreateTarget("Reflected_Projectile", new Vector3(2f, 1f, 6f), resolver, projectile: true, reflected: true);
        CreateTarget("Passed_Projectile", new Vector3(0f, 1f, -6f), resolver, projectile: true, passed: true);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = presentation;
        Debug.Log("C 인지 작업 에셋 생성 완료: " + SCENE_PATH + " / " + SETTINGS_PATH);
    }

    /// <summary>
    /// path가 없을 때 primitive 외형과 kind를 가진 비활성 C 전용 프리팹을 저장한다.
    /// Resolver는 씬 인스턴스에 연결하며 게임 전투용 프리팹에는 변경을 적용하지 않는다.
    /// </summary>
    private static void CreatePreviewPrefab(string path, PrimitiveType primitive, ThreatTargetKind kind)
    {
        if (File.Exists(path))
            return;
        GameObject root = GameObject.CreatePrimitive(primitive);
        root.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SURFACE_PATH);
        root.name = Path.GetFileNameWithoutExtension(path);
        root.SetActive(false);
        ThreatPresentationTarget targetComponent = root.AddComponent<ThreatPresentationTarget>();
        SerializedObject data = new SerializedObject(targetComponent);
        data.FindProperty("_kind").enumValueIndex = (int)kind;
        SerializedProperty renderers = data.FindProperty("_renderers");
        renderers.arraySize = 1;
        renderers.GetArrayElementAtIndex(0).objectReferenceValue = root.GetComponent<Renderer>();
        data.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        UnityEngine.Object.DestroyImmediate(root);
    }

    /// <summary>
    /// name·position과 표시 조건으로 C 전용 미리보기를 배치하고 resolver에 연결한다.
    /// charge·radius·projectile·reflected·passed를 Inspector 초기값에 저장하고 원본 프리팹을 유지한다.
    /// </summary>
    private static void CreateTarget(string name, Vector3 position, ThreatPresentationResolver resolver,
        bool charge = false, float radius = -1f, bool projectile = false, bool reflected = false, bool passed = false)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(projectile ? PROJECTILE_PATH : MELEE_PATH);
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        root.name = name;
        root.transform.position = position;
        ThreatPresentationTarget targetComponent = root.GetComponent<ThreatPresentationTarget>();
        SerializedObject data = new SerializedObject(targetComponent);
        data.FindProperty("_resolver").objectReferenceValue = resolver;
        data.FindProperty("_meleeType").enumValueIndex = charge ? (int)MeleeThreatType.Charge : (int)MeleeThreatType.Normal;
        data.FindProperty("_overrideEnemyRadius").boolValue = radius >= 0f;
        data.FindProperty("_enemyRadius").floatValue = radius >= 0f ? radius : 12f;
        data.FindProperty("_ownership").enumValueIndex = reflected ? (int)ThreatOwnership.Reflected : (int)ThreatOwnership.Hostile;
        data.FindProperty("_hasPassedPlayer").boolValue = passed;
        data.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(true);
        resolver.RefreshTarget(targetComponent);
    }

    /// <summary>
    /// 전용 씬의 기존 배치를 유지하면서 경계·겹침·벽 뒤 표시용 대상을 추가하고 저장한다.
    /// 메뉴 또는 Unity CLI에서 호출하며 정식 게임 씬·Renderer 설정·원본 프리팹은 변경하지 않는다.
    /// </summary>
    [MenuItem("Tools/Perception/Add Rendering Preview Cases")]
    public static void AddRenderingPreviewCases()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("렌더링 미리보기 배치는 Play를 종료한 뒤 실행하세요.");
            return;
        }
        if (EditorSceneManager.GetActiveScene().path != SCENE_PATH)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(SCENE_PATH);
        }
        GameObject presentation = FindPreviewRoot("ThreatPresentation");
        if (presentation == null)
        {
            Debug.LogError("전용 씬의 ThreatPresentation 오브젝트를 확인하세요.");
            return;
        }
        CreatePreviewMaterial();
        ThreatPresentationResolver resolver = presentation.GetComponent<ThreatPresentationResolver>();
        CreateRenderingTarget("Rendering_Boundary", new Vector3(-4f, 1.6f, Mathf.Sqrt(11.5f * 11.5f - 16f)), resolver);
        CreateRenderingTarget("Rendering_NearOverlap", new Vector3(1.5f, 1.6f, 2f), resolver);
        CreateRenderingTarget("Rendering_FarOverlap", new Vector3(9f, 1.6f, 12f), resolver, 18f);
        CreateRenderingTarget("Rendering_BehindWall", new Vector3(-7f, 1.6f, 15f), resolver, 20f);
        if (FindPreviewRoot("Rendering_Wall") == null)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Rendering_Wall";
            wall.transform.position = new Vector3(-3.5f, 1.6f, 7.5f);
            wall.transform.localScale = new Vector3(2f, 4f, 0.3f);
            wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(SURFACE_PATH);
            wall.AddComponent<OutlineLayer>();
        }
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        AssetDatabase.SaveAssets();
        Debug.Log("C 위협 렌더링 미리보기 배치 완료: " + SCENE_PATH);
    }

    /// <summary>name의 미리보기 루트가 없을 때 position·resolver·radius로 대상을 생성해 기존 편집값을 보존한다.</summary>
    private static void CreateRenderingTarget(string name, Vector3 position, ThreatPresentationResolver resolver, float radius = -1f)
    {
        if (FindPreviewRoot(name) == null)
            CreateTarget(name, position, resolver, radius: radius);
    }

    /// <summary>현재 전용 씬의 루트 이름을 name과 비교해 해당 오브젝트 또는 null을 반환한다.</summary>
    private static GameObject FindPreviewRoot(string name)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == name)
                return root;
        return null;
    }

    /// <summary>전용 미리보기 표면 재질이 없으면 URP Unlit 회색 재질을 생성하고 기존 에셋은 유지한다.</summary>
    private static void CreatePreviewMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(SURFACE_PATH) != null)
            return;
        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ThreatPreviewSurface" };
        material.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f, 1f));
        AssetDatabase.CreateAsset(material, SURFACE_PATH);
    }

    /// <summary>편집 중인 C 전용 씬에 공통 어댑터·D 공급자·세션 참조를 연결하고 현재 배치를 저장한다.</summary>
    [MenuItem("Tools/Perception/Add Common Contract Preview")]
    public static void AddCommonContractPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != SCENE_PATH)
        {
            Debug.LogWarning("Play를 종료하고 C Threat Sandbox 씬에서 공통 계약 미리보기를 추가하세요.");
            return;
        }
        GameObject root = FindPreviewRoot("ThreatPresentation");
        if (root == null)
        {
            Debug.LogError("C 전용 씬의 ThreatPresentation 루트를 확인하세요.");
            return;
        }
        CombatSessionRuntime runtime = root.GetComponent<CombatSessionRuntime>();
        if (runtime == null)
            runtime = Undo.AddComponent<CombatSessionRuntime>(root);
        ThreatPresentationContractAdapter adapter = root.GetComponent<ThreatPresentationContractAdapter>();
        if (adapter == null)
            adapter = Undo.AddComponent<ThreatPresentationContractAdapter>(root);
        CombatThreatPresentationProvider provider = root.GetComponent<CombatThreatPresentationProvider>();
        if (provider == null)
            provider = Undo.AddComponent<CombatThreatPresentationProvider>(root);
        SerializedObject adapterData = new SerializedObject(adapter);
        adapterData.FindProperty("_resolver").objectReferenceValue = root.GetComponent<ThreatPresentationResolver>();
        adapterData.FindProperty("_sessionRuntime").objectReferenceValue = runtime;
        adapterData.ApplyModifiedProperties();
        SerializedObject providerData = new SerializedObject(provider);
        providerData.FindProperty("_threatPresentationSource").objectReferenceValue = adapter;
        providerData.ApplyModifiedProperties();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        Selection.activeGameObject = root;
        Debug.Log("C 공통 계약 미리보기 연결 완료. Play 후 Tools/Perception/Start C Contract Preview Session을 실행하세요.");
    }

    /// <summary>Play 중 C 전용 씬의 D 세션을 시작하거나 재시작한다. 정식 게임의 시간·씬은 변경하지 않는다.</summary>
    [MenuItem("Tools/Perception/Start C Contract Preview Session")]
    public static void StartCommonContractPreviewSession()
    {
        if (!EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().path != SCENE_PATH)
        {
            Debug.LogWarning("공통 계약 세션 시작은 C Threat Sandbox를 Play한 상태에서 실행하세요.");
            return;
        }
        GameObject root = FindPreviewRoot("ThreatPresentation");
        CombatSessionRuntime runtime = root != null ? root.GetComponent<CombatSessionRuntime>() : null;
        if (runtime == null)
        {
            Debug.LogError("Play 종료 후 Add Common Contract Preview로 먼저 연결하세요.");
            return;
        }
        runtime.Initialize();
        ThreatPresentationResolver resolver = root.GetComponent<ThreatPresentationResolver>();
        SensoryManager sensory = root.GetComponent<SensoryManager>();
        if (sensory != null)
        {
            sensory.Initialize();
            sensory.SetObserver(resolver.Observer);
        }
        resolver.BeginSession(resolver.Observer, resolver.ViewCamera);
        resolver.ResolveNow();
        Selection.activeGameObject = root;
        Debug.Log("C 공통 계약 미리보기 세션 시작: " + runtime.Session.SessionId);
    }

    /// <summary>
    /// C 전용 씬을 열거나 생성한 뒤 누락된 보상 파동 대상과 연결을 복구하고 저장한다.
    /// 메뉴 또는 Unity 일괄 실행에서 호출하며 기존 대상과 사용자 편집값은 유지한다.
    /// </summary>
    [MenuItem("Tools/Perception/Open or Repair C Reward Pulse Sandbox")]
    public static void OpenOrRepairRewardPulseSandbox()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("보상 파동 대상 복구는 Play를 종료한 뒤 실행하세요.");
            return;
        }
        OpenOrCreateSandbox();
        if (EditorSceneManager.GetActiveScene().path == SCENE_PATH)
            AddRewardPulsePreview();
    }

    /// <summary>
    /// C 전용 씬에 보상 파동 마스크와 거리 밖·인지 거리 0·높이·화면 밖 확인 대상을 추가하고 저장한다.
    /// A의 파동 재생기는 복제하지 않으며 실제 파동 시간은 PlayerPerfectPulse Inspector에서 별도로 확인한다.
    /// </summary>
    [MenuItem("Tools/Perception/Add Reward Pulse Preview")]
    public static void AddRewardPulsePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != SCENE_PATH)
        {
            Debug.LogWarning("Play를 종료하고 C Threat Sandbox 씬에서 보상 파동 미리보기를 추가하세요.");
            return;
        }
        AddCommonContractPreview();
        GameObject root = FindPreviewRoot("ThreatPresentation");
        ThreatPresentationResolver resolver = root.GetComponent<ThreatPresentationResolver>();
        SensoryPerceptionMask mask = root.GetComponent<SensoryPerceptionMask>();
        if (mask == null)
        {
            mask = Undo.AddComponent<SensoryPerceptionMask>(root);
            SerializedObject maskData = new SerializedObject(mask);
            maskData.FindProperty("_playerRadius").floatValue = 0f;
            maskData.ApplyModifiedProperties();
        }
        SerializedObject resolverData = new SerializedObject(resolver);
        resolverData.FindProperty("_rewardPulseMask").objectReferenceValue = mask;
        resolverData.ApplyModifiedProperties();
        CreateRenderingTarget("Pulse_OutsideDistance", new Vector3(0f, 1.6f, 14f), resolver);
        CreateRenderingTarget("Pulse_ZeroRadius", new Vector3(2f, 1.6f, 10f), resolver, 0f);
        CreateRenderingTarget("Pulse_HighZeroRadius", new Vector3(-2f, 6f, 10f), resolver, 0f);
        CreateRenderingTarget("Pulse_Offscreen", new Vector3(14f, 1.6f, -4f), resolver);
        CreateRenderingTarget("Pulse_OutsidePulse", new Vector3(0f, 1.6f, 18f), resolver);
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        Selection.activeObject = mask;
        Debug.Log("C 보상 파동 확인 대상 5개 준비 완료: Pulse_OutsideDistance, Pulse_ZeroRadius, Pulse_HighZeroRadius, Pulse_Offscreen, Pulse_OutsidePulse. Play 후 Start C Contract Preview Session을 실행하고 Sensory Perception Mask에서 확인하세요.");
    }

    /// <summary>편집 중인 C 전용 씬에 발소리 후보·D 전달 슬롯과 청각 경계 확인용 대상을 연결하고 저장한다.</summary>
    [MenuItem("Tools/Perception/Add Footstep Candidate Preview")]
    public static void AddFootstepCandidatePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().path != SCENE_PATH)
        {
            Debug.LogWarning("Play를 종료하고 C Threat Sandbox 씬에서 발소리 미리보기를 추가하세요.");
            return;
        }
        AddCommonContractPreview();
        GameObject root = FindPreviewRoot("ThreatPresentation");
        if (root == null)
            return;
        ThreatPresentationResolver resolver = root.GetComponent<ThreatPresentationResolver>();
        EnemyFootstepCandidates candidates = root.GetComponent<EnemyFootstepCandidates>();
        if (candidates == null)
            candidates = Undo.AddComponent<EnemyFootstepCandidates>(root);
        SerializedObject candidateData = new SerializedObject(candidates);
        candidateData.FindProperty("_resolver").objectReferenceValue = resolver;
        candidateData.FindProperty("_sessionRuntime").objectReferenceValue = root.GetComponent<CombatSessionRuntime>();
        candidateData.ApplyModifiedProperties();
        SerializedObject providerData = new SerializedObject(root.GetComponent<CombatThreatPresentationProvider>());
        providerData.FindProperty("_footstepCandidateSource").objectReferenceValue = candidates;
        providerData.ApplyModifiedProperties();
        CreateRenderingTarget("Footstep_HearingEdge", new Vector3(0f, 1.6f, 19f), resolver);
        CreateRenderingTarget("Footstep_OutsideHearing", new Vector3(0f, 1.6f, 21f), resolver);
        CreateRenderingTarget("Footstep_Visible", new Vector3(-1f, 1.6f, 5f), resolver);
        CreateRenderingTarget("Footstep_Offscreen", new Vector3(8f, 1.6f, -8f), resolver);
        ConnectFootstepPreviewHud(root.GetComponent<CombatThreatPresentationProvider>());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        Selection.activeGameObject = root;
        Debug.Log("C 발소리 후보 연결 완료. Play 후 세션을 시작하고 Enemy Footstep Candidates Inspector에서 실제 후보를 발생시키세요.");
    }

    /// <summary>provider를 소비하는 C 전용 Overlay HUD와 D 마커 프리팹 24개를 연결한다. 기존 HUD·프리팹 에셋은 수정하지 않는다.</summary>
    private static void ConnectFootstepPreviewHud(CombatThreatPresentationProvider provider)
    {
        GameObject markerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FOOTSTEP_MARKER_PATH);
        if (markerPrefab == null || markerPrefab.GetComponent<CombatFootstepMarker>() == null)
            throw new InvalidOperationException("D 발소리 마커 프리팹을 확인하세요: " + FOOTSTEP_MARKER_PATH);
        GameObject root = FindPreviewRoot("C_FootstepHudPreview");
        if (root == null)
        {
            root = new GameObject("C_FootstepHudPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(root, "C 발소리 HUD 미리보기 생성");
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }
        if (root.GetComponent<Canvas>() == null)
            throw new InvalidOperationException("C_FootstepHudPreview 이름의 기존 오브젝트가 Canvas가 아닙니다.");
        Transform existing = root.transform.Find("FootstepMarkers");
        RectTransform container;
        if (existing == null)
        {
            GameObject markers = new GameObject("FootstepMarkers", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(markers, "C 발소리 HUD 컨테이너 생성");
            container = markers.GetComponent<RectTransform>();
            container.SetParent(root.transform, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;
        }
        else
            container = existing.GetComponent<RectTransform>();
        if (container == null)
            throw new InvalidOperationException("C 발소리 HUD의 FootstepMarkers가 RectTransform이 아닙니다.");
        CombatFootstepHud hud = container.GetComponent<CombatFootstepHud>();
        if (hud == null)
            hud = Undo.AddComponent<CombatFootstepHud>(container.gameObject);
        SerializedObject settings = new SerializedObject(hud);
        settings.FindProperty("_provider").objectReferenceValue = provider;
        settings.FindProperty("_container").objectReferenceValue = container;
        SerializedProperty slots = settings.FindProperty("_markers");
        slots.arraySize = FOOTSTEP_HUD_SLOTS;
        for (int i = 0; i < FOOTSTEP_HUD_SLOTS; i++)
        {
            string name = "FootstepMarker_" + i.ToString("00");
            Transform marker = container.Find(name);
            if (marker == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab, container);
                Undo.RegisterCreatedObjectUndo(instance, "C 발소리 HUD 마커 생성");
                instance.name = name;
                marker = instance.transform;
            }
            CombatFootstepMarker component = marker.GetComponent<CombatFootstepMarker>();
            if (component == null)
                throw new InvalidOperationException("C 발소리 HUD 마커 연결을 확인하세요: " + name);
            slots.GetArrayElementAtIndex(i).objectReferenceValue = component;
        }
        settings.ApplyModifiedProperties();
    }

    /// <summary>C 전용 Play 세션에 0.1배 시간 요청을 등록해 짧은 게임 시간 후보를 수동 관찰할 수 있게 한다.</summary>
    [MenuItem("Tools/Perception/Footstep Preview/Slow to 0.1")]
    public static void SlowFootstepPreview()
    {
        CombatSessionRuntime runtime = GetFootstepPreviewRuntime();
        if (runtime == null)
            return;
        runtime.Release(_footstepPreviewTimeToken);
        _footstepPreviewTimeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session,
            "C/FootstepPreview", CombatTimeReason.DashTelegraph, 0.1f));
    }

    /// <summary>C 전용 Play 세션의 후보 시간 관찰 요청만 해제해 다른 시간 요청과 합성된 기본 배율로 복귀한다.</summary>
    [MenuItem("Tools/Perception/Footstep Preview/Restore Speed")]
    public static void RestoreFootstepPreviewSpeed()
    {
        CombatSessionRuntime runtime = GetFootstepPreviewRuntime();
        if (runtime == null)
            return;
        runtime.Release(_footstepPreviewTimeToken);
        _footstepPreviewTimeToken = default;
    }

    /// <summary>C 전용 Play 세션에 명시적 정지를 요청한다. 후보의 게임 시간 수명과 D 공통 시계가 함께 정지한다.</summary>
    [MenuItem("Tools/Perception/Footstep Preview/Freeze")]
    public static void FreezeFootstepPreview()
    {
        GetFootstepPreviewRuntime()?.SetFrozen(true);
    }

    /// <summary>C 전용 Play 세션의 명시적 정지를 해제한다. 살아 있는 0.1배 관찰 요청은 유지한다.</summary>
    [MenuItem("Tools/Perception/Footstep Preview/Resume")]
    public static void ResumeFootstepPreview()
    {
        GetFootstepPreviewRuntime()?.SetFrozen(false);
    }

    /// <summary>현재 C 전용 Play 세션이 초기화됐으면 런타임을 반환하고 정식 게임이나 미초기화 상태이면 안내 후 null을 반환한다.</summary>
    private static CombatSessionRuntime GetFootstepPreviewRuntime()
    {
        if (EditorApplication.isPlaying && EditorSceneManager.GetActiveScene().path == SCENE_PATH)
        {
            GameObject root = FindPreviewRoot("ThreatPresentation");
            CombatSessionRuntime runtime = root != null ? root.GetComponent<CombatSessionRuntime>() : null;
            if (runtime != null && runtime.IsInitialized)
                return runtime;
        }
        Debug.LogWarning("C Threat Sandbox를 Play하고 Start C Contract Preview Session으로 먼저 세션을 시작하세요.");
        return null;
    }
}
