using System;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 저장된 대숲 방어 씬의 대나무 숲 바깥쪽에 기존과 같은 간격의 대나무 고리를 더 심는다.
/// 적이 생성되고 걸어 나오는 안쪽 띠는 건드리지 않으므로 NavMesh를 다시 굽지 않는다.
/// </summary>
public static class BambooForestExtension
{
    private const string SCENE_PATH = "Assets/Scenes/Defense/BambooDefense.unity";
    private const string OUTER_PREFIX = "BambooOuter_";
    // 기존 숲의 바깥 반경과 격자 간격이다. 시작 좌표가 같은 격자에 맞도록 확장 폭은 간격의 정수배로 둔다.
    private const float EXISTING_OUTER_RADIUS = 33f;
    private const float EXTENDED_OUTER_RADIUS = 60f;
    private const float BAMBOO_SPACING = 1.5f;
    private const int SEED = 11;

    /// <summary>
    /// BambooForest 아래의 이전 확장 대나무를 지우고 반경 33~60m 고리에 흔들린 격자로 다시 심은 뒤 지면을 넓히고 씬을 저장한다.
    /// 높이·굵기·기울기·재질·윤곽선 분류는 대숲 방어 씬 생성 도구와 같은 규칙을 쓴다.
    /// </summary>
    [MenuItem("BlindBlade/대숲 방어/대나무 숲 뒤쪽 확장")]
    public static void Extend()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
            || scene.path != SCENE_PATH || scene.isDirty)
            throw new InvalidOperationException("저장된 BambooDefense 씬의 편집 모드에서 실행하세요.");
        GameObject forestObject = GameObject.Find("BambooForest");
        if (forestObject == null)
            throw new InvalidOperationException("BambooForest 오브젝트를 찾지 못했습니다.");
        Transform forest = forestObject.transform;

        Material material = null;
        for (int i = forest.childCount - 1; i >= 0; i--)
        {
            Transform child = forest.GetChild(i);
            if (child.name.StartsWith(OUTER_PREFIX))
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            else if (material == null && child.TryGetComponent(out MeshRenderer existing))
                material = existing.sharedMaterial;
        }

        System.Random random = new System.Random(SEED);
        int count = 0;
        for (float x = -EXTENDED_OUTER_RADIUS; x <= EXTENDED_OUTER_RADIUS; x += BAMBOO_SPACING)
        {
            for (float z = -EXTENDED_OUTER_RADIUS; z <= EXTENDED_OUTER_RADIUS; z += BAMBOO_SPACING)
            {
                Vector3 point = new Vector3(x + Range(random, -0.4f, 0.4f) * BAMBOO_SPACING, 0f,
                    z + Range(random, -0.4f, 0.4f) * BAMBOO_SPACING);
                float radius = new Vector2(point.x, point.z).magnitude;
                if (radius <= EXISTING_OUTER_RADIUS || radius > EXTENDED_OUTER_RADIUS)
                    continue;
                CreateStalk(forest, material, point, OUTER_PREFIX + count++, random);
            }
        }

        // 바깥 고리까지 지면이 덮이도록 넓힌다. Plane 기본 크기는 10m다.
        GameObject ground = GameObject.Find("Arena/Ground");
        if (ground != null)
        {
            float scale = (EXTENDED_OUTER_RADIUS * 2f + 10f) / 10f;
            if (ground.transform.localScale.x < scale)
                ground.transform.localScale = new Vector3(scale, ground.transform.localScale.y, scale);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"대나무 숲 뒤쪽 확장 완료: {count}그루 (반경 {EXISTING_OUTER_RADIUS}~{EXTENDED_OUTER_RADIUS}m)");
    }

    /// <summary>
    /// forest 아래 point 바닥에 name의 충돌체 없는 대나무 실린더를 material로 만든다.
    /// random으로 높이 8~14m·굵기 0.12~0.22m·기울기 ±4°를 정하고 배경 윤곽선 비트를 켠다.
    /// </summary>
    private static void CreateStalk(Transform forest, Material material, Vector3 point, string name, System.Random random)
    {
        float height = Range(random, 8f, 14f);
        float diameter = Range(random, 0.12f, 0.22f);
        GameObject stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stalk.name = name;
        UnityEngine.Object.DestroyImmediate(stalk.GetComponent<Collider>());
        stalk.transform.SetParent(forest, false);
        stalk.transform.localRotation = Quaternion.Euler(Range(random, -4f, 4f), Range(random, 0f, 360f), Range(random, -4f, 4f));
        stalk.transform.localPosition = point + stalk.transform.localRotation * Vector3.up * (height * 0.5f);
        stalk.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        MeshRenderer renderer = stalk.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.renderingLayerMask |= OutlineLayer.SCENERY_MASK;
    }

    /// <summary>random에서 minimum 이상 maximum 미만의 값을 반환한다.</summary>
    private static float Range(System.Random random, float minimum, float maximum)
    {
        return minimum + (float)random.NextDouble() * (maximum - minimum);
    }
}
