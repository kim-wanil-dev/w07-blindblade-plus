using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

using Unity.AI.Navigation;

/// <summary>
/// 대숲 방어 씬과 전용 머티리얼·프리팹을 한 번에 생성한다.
/// 원형 공터·공터를 둘러싼 대나무 고리·NavMesh·공통 프리팹·디렉터를 배치하고 빌드 목록에 씬을 등록한다.
/// </summary>
public static class BambooDefenseSceneBuilder
{
    private const string SCENE_PATH = "Assets/Scenes/Defense/BambooDefense.unity";
    private const string NAVMESH_PATH = "Assets/Scenes/Defense/BambooDefense/NavMesh-Arena.asset";
    private const string ASSET_FOLDER = "Assets/Resources/Defense";
    private const string THROWER_PREFAB_PATH = ASSET_FOLDER + "/DefenseThrower.prefab";
    private const string MELEE_PREFAB_PATH = ASSET_FOLDER + "/DefenseMelee.prefab";
    private const string UNLIT_SHADER = "Universal Render Pipeline/Unlit";
    private const float CLEARING_RADIUS = 24f;
    private const float PLAYER_RADIUS = 23.6f;
    private const float FOREST_INNER_RADIUS = 24.6f;
    private const float FOREST_OUTER_RADIUS = 33f;
    private const float BAMBOO_SPACING = 1.5f;
    private const int SEED = 7;
    private const float SHURIKEN_SCALE = 2f;
    private const float MELEE_SPEED = 4.8f;
    private const float MELEE_SPEED_MULTIPLIER = 1.5f;

    /// <summary>
    /// 열린 씬에 저장되지 않은 변경이 없을 때 대숲 방어 씬을 새로 생성하고 저장한다.
    /// 기존 씬이 있으면 중단하여 Inspector 조정값과 NavMesh 연결을 보존한다.
    /// </summary>
    [MenuItem("BlindBlade/대숲 방어/없는 씬만 신규 생성")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) != null)
        {
            Debug.LogWarning("기존 대숲 방어 씬은 재생성하지 않습니다. Inspector에서 수정하거나 이전 도구를 사용하세요.");
            return;
        }

        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            if (!EditorSceneManager.GetSceneAt(i).isDirty)
                continue;
            Debug.LogError("저장되지 않은 씬 변경이 있어 대숲 방어 씬 생성을 중단합니다. 먼저 저장해 주세요.");
            return;
        }

        EnsureFolder("Assets/Scenes/Defense");
        EnsureFolder("Assets/Scenes/Defense/BambooDefense");
        EnsureFolder(ASSET_FOLDER);
        Material bamboo = LoadOrCreateMaterial("Bamboo", new Color(0.35f, 0.45f, 0.3f, 1f));
        Material ground = LoadOrCreateMaterial("Ground", new Color(0.2f, 0.18f, 0.15f, 1f));
        Material body = LoadOrCreateMaterial("ThrowerBody", new Color(0.3f, 0.05f, 0.05f, 1f));
        DefenseShuriken shurikenPrefab = LoadOrCreateShurikenPrefab();
        LoadOrCreateThrowerPrefab(shurikenPrefab, body);
        LoadOrCreateMeleePrefab();
        AssetDatabase.SaveAssets();

        // 새 씬 생성은 사용하지 않는 에셋을 내리므로 이전 참조 대신 경로로 다시 불러와 연결한다.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        bamboo = AssetDatabase.LoadAssetAtPath<Material>($"{ASSET_FOLDER}/Bamboo.mat");
        ground = AssetDatabase.LoadAssetAtPath<Material>($"{ASSET_FOLDER}/Ground.mat");
        CreateLight();
        CreateArena(ground, bamboo);
        InstantiatePrefab("Assets/Resources/Prefabs/Player.prefab", Vector3.zero, Quaternion.identity);
        ConfigureInitializer(InstantiatePrefab("Assets/Resources/Prefabs/Scene/GameSceneInitializer.prefab",
            Vector3.zero, Quaternion.identity));
        InstantiatePrefab("Assets/Resources/Prefabs/GameManagers/UIManager.prefab", Vector3.zero, Quaternion.identity);
        CreateDirector(AssetDatabase.LoadAssetAtPath<GameObject>(THROWER_PREFAB_PATH).GetComponent<BambooThrower>(),
            AssetDatabase.LoadAssetAtPath<GameObject>(MELEE_PREFAB_PATH));

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SCENE_PATH);
        AddSceneToBuild();
        Debug.Log("대숲 방어 씬을 생성했습니다: " + SCENE_PATH);
    }

    /// <summary>path 폴더가 없으면 부모 경로 아래에 생성한다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    /// <summary>name의 URP Unlit 머티리얼을 찾거나 color로 새로 생성해 반환한다. GPU 인스턴싱을 켠다.</summary>
    private static Material LoadOrCreateMaterial(string name, Color color)
    {
        string path = $"{ASSET_FOLDER}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;
        material = new Material(Shader.Find(UNLIT_SHADER)) { name = name, enableInstancing = true };
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// 기존 Shuriken3의 모델과 회전을 재사용해 두 배 크기의 빨간 실루엣 수리검 프리팹을 찾거나 생성해 반환한다.
    /// 기존 투사체·인지 숨김 컴포넌트는 복제본에서만 제거한다.
    /// </summary>
    private static DefenseShuriken LoadOrCreateShurikenPrefab()
    {
        string path = $"{ASSET_FOLDER}/DefenseShuriken.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing.GetComponent<DefenseShuriken>();
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Enemies/Shuriken3.prefab");
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = "DefenseShuriken";
        root.transform.Find("Model").localScale *= SHURIKEN_SCALE;
        Object.DestroyImmediate(root.GetComponent<ShurikenPerceptionEffect>());
        Object.DestroyImmediate(root.GetComponent<EnemyProjectile>());
        root.AddComponent<DefenseShuriken>();
        SetField(root.AddComponent<OutlineLayer>(), "_kind", (int)OutlineLayerKind.Threat);
        return SavePrefab(root, path).GetComponent<DefenseShuriken>();
    }

    /// <summary>
    /// 몸·머리 실루엣과 발사 위치를 가진 투척수 프리팹을 찾거나 생성해 반환한다.
    /// 한 번의 유효 타격에 사망하고 플레이어 돌진 대상에서 제외되며 shurikenPrefab을 던진다.
    /// </summary>
    private static BambooThrower LoadOrCreateThrowerPrefab(DefenseShuriken shurikenPrefab, Material body)
    {
        string path = THROWER_PREFAB_PATH;
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing.GetComponent<BambooThrower>();
        GameObject root = new GameObject("DefenseThrower") { layer = 2 };
        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.height = 1.5f;
        collider.radius = 0.35f;
        Renderer torso = CreateVisual(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, -0.15f, 0f),
            new Vector3(0.6f, 0.6f, 0.6f), body);
        Renderer head = CreateVisual(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 0.55f, 0f),
            Vector3.one * 0.38f, body);
        Transform muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(0f, 0.35f, 0.4f);
        EnemyHealth health = root.AddComponent<EnemyHealth>();
        SetField(health, "_maxHealth", 1f);
        SetField(health, "_dieOnAnyHit", true);
        SetField(health, "_hitCollider", collider);
        SetField(health, "_dashTargetable", false);
        BambooThrower thrower = root.AddComponent<BambooThrower>();
        SetField(thrower, "_shurikenPrefab", shurikenPrefab);
        SetField(thrower, "_muzzle", muzzle);
        SetArray(thrower, "_bodyRenderers", new Object[] { torso, head });
        SetField(root.AddComponent<OutlineLayer>(), "_kind", (int)OutlineLayerKind.Threat);
        return SavePrefab(root, path).GetComponent<BambooThrower>();
    }

    /// <summary>
    /// Enemy_SingleMelee의 변형으로 항상 빠르게 달려오는 근접 적 프리팹을 찾거나 생성해 반환한다.
    /// 이동 속도를 올리고 가속도·발걸음 간격을 같은 배율로 늘리며 순찰을 끄고 추적 고정 컴포넌트를 추가한다.
    /// </summary>
    private static GameObject LoadOrCreateMeleePrefab()
    {
        string path = MELEE_PREFAB_PATH;
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Enemies/Enemy_SingleMelee.prefab");
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        root.name = "DefenseMelee";
        SetField(root.GetComponent<EnemyMotor>(), "_moveSpeed", MELEE_SPEED);
        MultiplyField(root.GetComponent<EnemyMotor>(), "_acceleration", MELEE_SPEED_MULTIPLIER);
        // 빠른 이동에서도 초당 발걸음 수가 늘지 않도록 걸음 간격을 같은 배율로 늘린다.
        MultiplyField(root.GetComponent<FootstepEmitter>(), "_stepDistance", MELEE_SPEED_MULTIPLIER);
        root.GetComponent<EnemyPatrol>().enabled = false;
        root.AddComponent<DefenseChaser>();
        return SavePrefab(root, path);
    }

    /// <summary>type 기본 도형을 parent 아래 위치·크기·material로 만들고 충돌체를 제거한 렌더러를 반환한다.</summary>
    private static Renderer CreateVisual(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(type);
        visual.name = name;
        visual.layer = 2;
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = position;
        visual.transform.localScale = scale;
        Renderer renderer = visual.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return renderer;
    }

    /// <summary>root를 path 프리팹으로 저장하고 씬의 임시 오브젝트를 제거한 뒤 저장된 에셋을 반환한다.</summary>
    private static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>은은한 방향광을 생성한다. 윤곽선 표현에는 영향이 적으며 기존 씬 구성과 맞춘다.</summary>
    private static void CreateLight()
    {
        Light light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.8f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    /// <summary>
    /// 지면·대나무 고리·플레이어 경계·NavMesh를 가진 경기장 루트를 생성해 반환한다.
    /// 대나무는 충돌체 없이 렌더러로만 배치해 NavMesh 장애물과 항상 보이는 배경으로 사용한다.
    /// </summary>
    private static GameObject CreateArena(Material groundMaterial, Material bambooMaterial)
    {
        GameObject arena = new GameObject("Arena");
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(arena.transform, false);
        ground.transform.localScale = Vector3.one * (FOREST_OUTER_RADIUS * 2f + 10f) / 10f;
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        CreateBambooForest(arena.transform, bambooMaterial);
        SetField(arena.AddComponent<DefenseArenaBoundary>(), "_radius", PLAYER_RADIUS);

        NavMeshSurface surface = arena.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NAVMESH_PATH) != null)
            AssetDatabase.DeleteAsset(NAVMESH_PATH);
        AssetDatabase.CreateAsset(surface.navMeshData, NAVMESH_PATH);
        return arena;
    }

    /// <summary>
    /// 공터를 빈틈없이 둘러싸는 원형 고리 영역에 흔들린 격자로 대나무 실린더를 배치한다.
    /// 높이·굵기·기울기는 고정 시드로 무작위화하며 편집 모드에서도 회색 배경 윤곽선으로 보이도록 분류 비트를 저장한다.
    /// </summary>
    private static void CreateBambooForest(Transform parent, Material material)
    {
        Transform forest = new GameObject("BambooForest").transform;
        forest.SetParent(parent, false);
        System.Random random = new System.Random(SEED);
        int count = 0;
        for (float x = -FOREST_OUTER_RADIUS; x <= FOREST_OUTER_RADIUS; x += BAMBOO_SPACING)
        {
            for (float z = -FOREST_OUTER_RADIUS; z <= FOREST_OUTER_RADIUS; z += BAMBOO_SPACING)
            {
                Vector3 point = new Vector3(x + Range(random, -0.4f, 0.4f) * BAMBOO_SPACING, 0f,
                    z + Range(random, -0.4f, 0.4f) * BAMBOO_SPACING);
                float radius = new Vector2(point.x, point.z).magnitude;
                if (radius < FOREST_INNER_RADIUS || radius > FOREST_OUTER_RADIUS)
                    continue;
                float height = Range(random, 8f, 14f);
                float diameter = Range(random, 0.12f, 0.22f);
                GameObject stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stalk.name = "Bamboo_" + count++;
                Object.DestroyImmediate(stalk.GetComponent<Collider>());
                stalk.transform.SetParent(forest, false);
                stalk.transform.localRotation = Quaternion.Euler(Range(random, -4f, 4f), Range(random, 0f, 360f), Range(random, -4f, 4f));
                stalk.transform.localPosition = point + stalk.transform.localRotation * Vector3.up * (height * 0.5f);
                stalk.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
                MeshRenderer renderer = stalk.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.renderingLayerMask |= OutlineLayer.SCENERY_MASK;
            }
        }
        SetField(forest.gameObject.AddComponent<OutlineLayer>(), "_kind", (int)OutlineLayerKind.Scenery);
    }

    /// <summary>path 프리팹을 현재 씬에 연결된 인스턴스로 position·rotation에 배치하고 반환한다.</summary>
    private static GameObject InstantiatePrefab(string path, Vector3 position, Quaternion rotation)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        instance.transform.SetPositionAndRotation(position, rotation);
        return instance;
    }

    /// <summary>initializer의 지형 기억·복도 규칙·검 소음을 끄고 공통 세션 초기화만 사용하게 한다.</summary>
    private static void ConfigureInitializer(GameObject initializer)
    {
        GameSceneInitializer component = initializer.GetComponent<GameSceneInitializer>();
        SetField(component, "_useTerrainMemory", false);
        SetField(component, "_useCorridorRule", false);
        SetField(component, "_useSwordNoise", false);
    }

    /// <summary>세션 기록·스폰·회복·HUD 연결을 생성하고 생성 시에만 기본 숲 범위를 기록한다.</summary>
    private static void CreateDirector(BambooThrower throwerPrefab, GameObject meleePrefab)
    {
        GameObject root = new GameObject("DefenseDirector");
        Transform units = new GameObject("Units").transform;
        units.SetParent(root.transform, false);
        BambooDefenseDirector director = root.AddComponent<BambooDefenseDirector>();
        DefenseEnemySpawner spawner = root.AddComponent<DefenseEnemySpawner>();
        SetField(director, "_spawner", spawner);
        SetField(spawner, "_unitRoot", units);
        SetField(spawner, "_throwerPrefab", throwerPrefab);
        SetField(spawner, "_throwerMinimumRadius", FOREST_INNER_RADIUS + 0.8f);
        SetField(spawner, "_throwerMaximumRadius", FOREST_INNER_RADIUS + 4.4f);
        SetField(spawner, "_meleePrefab", meleePrefab);
        SetField(spawner, "_meleeSpawnMinimumRadius", CLEARING_RADIUS);
        SetField(spawner, "_meleeSpawnMaximumRadius", FOREST_INNER_RADIUS + 4f);
        root.AddComponent<DefenseHealthRegen>();
        BambooDefenseHudAuthoring.Connect(director);
    }

    /// <summary>
    /// 현재 씬 경로를 Build Settings 목록 끝에 비활성 상태로 추가한다. 이미 있으면 기존 설정을 유지한다.
    /// 단독 씬이므로 기존 씬의 다음 빌드 인덱스 전환에 끼어들지 않게 한다.
    /// </summary>
    private static void AddSceneToBuild()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(scene => scene.path == SCENE_PATH))
            return;
        scenes.Add(new EditorBuildSettingsScene(SCENE_PATH, false));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>target의 직렬화 필드 name에 value를 기록한다. 지원 형식은 bool·int·float·Object 참조다.</summary>
    private static void SetField(Object target, string name, object value)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        switch (value)
        {
            case bool flag: property.boolValue = flag; break;
            case int number when property.propertyType == SerializedPropertyType.Enum: property.enumValueIndex = number; break;
            case int number: property.intValue = number; break;
            case float number: property.floatValue = number; break;
            default: property.objectReferenceValue = (Object)value; break;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>target의 실수 직렬화 필드 name에 factor를 곱해 기록한다.</summary>
    private static void MultiplyField(Object target, string name, float factor)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        property.floatValue *= factor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>target의 배열 필드 name을 values 참조 목록으로 교체한다.</summary>
    private static void SetArray(Object target, string name, Object[] values)
    {
        SerializedObject serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(name);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>random으로 minimum과 maximum 사이의 실수를 반환한다.</summary>
    private static float Range(System.Random random, float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }
}
