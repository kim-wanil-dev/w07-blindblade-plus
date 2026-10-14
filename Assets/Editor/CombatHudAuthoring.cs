using System;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using TMPro;

using BlindBlade.UI.CombatHud;

/// <summary>기존 HUD 프리팹에 임시 Sprite 기반 게이지·발소리 표시를 추가하고 현재 BambooDefense 씬에 연결한다.</summary>
public static class CombatHudAuthoring
{
    private const string HUD_PATH = "Assets/Scenes/Defense/UI/BambooDefenseHud.prefab";
    private const string MARKER_PATH = "Assets/Scenes/Defense/UI/CombatFootstepMarker.prefab";
    private const string SPRITE_FOLDER = "Assets/Scenes/Defense/UI/Placeholders";

    /// <summary>입력 없이 임시 전투 HUD 설치를 실행하고 저장한 에셋·씬 결과를 Console에 기록한다.</summary>
    [MenuItem("BlindBlade/대숲 방어/HUD/임시 전투 HUD 설치·연결")]
    public static void InstallFromMenu()
    {
        Debug.Log(Install());
    }

    /// <summary>현재 BambooDefense 씬과 기존 HUD를 확인해 새 표시만 추가·연결·저장하고 적용 결과를 반환한다.</summary>
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("HUD 설치는 편집 모드에서 수행하세요.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Defense/BambooDefense.unity")
            throw new InvalidOperationException("BambooDefense 씬을 연 뒤 설치하세요.");
        BambooDefenseDirector director = UnityEngine.Object.FindFirstObjectByType<BambooDefenseDirector>();
        if (director == null || director.gameObject.scene != scene)
            throw new InvalidOperationException("현재 씬의 BambooDefenseDirector가 없습니다.");
        EnsureFolder(SPRITE_FOLDER);
        Sprite bar = EnsureSprite("Bar", 256, 32, IsBarPixel);
        Sprite health = EnsureSprite("Health", 64, 64, IsHeartPixel);
        Sprite stamina = EnsureSprite("Stamina", 64, 64, IsLightningPixel);
        Sprite footstep = EnsureSprite("Footstep", 64, 64, IsFootstepPixel);
        CombatFootstepMarker marker = EnsureMarkerPrefab(footstep);
        UpdateHudPrefab(bar, health, stamina, marker);
        BambooDefenseHudAuthoring.Connect(director);
        EditorSceneManager.SaveScene(scene);
        return "HP·스태미너·발소리 HUD 프리팹과 BambooDefense 연결을 저장했습니다.";
    }

    /// <summary>path의 에셋 폴더가 없으면 상위 경로부터 생성한다. 기존 폴더의 내용은 유지한다.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    /// <summary>name·크기·mask로 흰색 임시 PNG Sprite를 최초 생성해 반환한다. 이미 존재하는 이미지는 덮어쓰지 않는다.</summary>
    private static Sprite EnsureSprite(string name, int width, int height, Func<float, float, bool> mask)
    {
        string path = SPRITE_FOLDER + "/" + name + "Placeholder.png";
        if (!File.Exists(path))
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            if (mask((x + (sx + 0.5f) * 0.5f) / width,
                                (y + (sy + 0.5f) * 0.5f) / height))
                                covered++;
                        }
                    }
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(covered * 255 / 4));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>x·y의 정규화 좌표가 임시 가로 막대 안이면 true를 반환한다.</summary>
    private static bool IsBarPixel(float x, float y)
    {
        return x >= 0.01f && x <= 0.99f && y >= 0.08f && y <= 0.92f;
    }

    /// <summary>x·y의 정규화 좌표를 심장 곡선에 대입해 HP 아이콘 내부인지 반환한다.</summary>
    private static bool IsHeartPixel(float x, float y)
    {
        x = (x - 0.5f) * 2.7f;
        y = (y - 0.48f) * 2.7f;
        float curve = x * x + y * y - 1f;
        return curve * curve * curve - x * x * y * y * y <= 0f;
    }

    /// <summary>x·y가 임시 번개 도형 안이면 true를 반환해 스태미너 아이콘에 사용한다.</summary>
    private static bool IsLightningPixel(float x, float y)
    {
        if (y >= 0.5f && y <= 0.94f)
            return x >= Mathf.Lerp(0.2f, 0.5f, (y - 0.5f) / 0.44f)
                && x <= Mathf.Lerp(0.52f, 0.8f, (y - 0.5f) / 0.44f);
        if (y >= 0.06f && y < 0.5f)
            return x >= Mathf.Lerp(0.22f, 0.5f, (y - 0.06f) / 0.44f)
                && x <= Mathf.Lerp(0.3f, 0.8f, (y - 0.06f) / 0.44f);
        return false;
    }

    /// <summary>x·y가 발바닥·뒤꿈치·발가락 타원 중 하나에 속하면 true를 반환한다.</summary>
    private static bool IsFootstepPixel(float x, float y)
    {
        return IsEllipse(x, y, 0.44f, 0.2f, 0.14f, 0.16f)
            || IsEllipse(x, y, 0.5f, 0.52f, 0.19f, 0.23f)
            || IsEllipse(x, y, 0.64f, 0.84f, 0.1f, 0.12f)
            || IsEllipse(x, y, 0.46f, 0.85f, 0.065f, 0.09f)
            || IsEllipse(x, y, 0.32f, 0.8f, 0.06f, 0.08f);
    }

    /// <summary>x·y를 중심 cx·cy, 반지름 rx·ry의 타원에 대입해 내부 여부를 반환한다.</summary>
    private static bool IsEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        return Mathf.Pow((x - cx) / rx, 2f) + Mathf.Pow((y - cy) / ry, 2f) <= 1f;
    }

    /// <summary>sprite를 Source Image로 연결한 최초 발소리 마커 프리팹을 생성하거나 기존 프리팹을 반환한다.</summary>
    private static CombatFootstepMarker EnsureMarkerPrefab(Sprite sprite)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MARKER_PATH);
        if (existing != null)
            return existing.GetComponent<CombatFootstepMarker>();
        GameObject root = new GameObject("CombatFootstepMarker", typeof(RectTransform));
        try
        {
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = Vector2.one * 32f;
            UnityEngine.UI.Image image = root.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            CombatFootstepMarker marker = root.AddComponent<CombatFootstepMarker>();
            SetReference(marker, "_rect", rect);
            SetReference(marker, "_icon", image);
            return PrefabUtility.SaveAsPrefabAsset(root, MARKER_PATH).GetComponent<CombatFootstepMarker>();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    /// <summary>기존 HUD에 bar·아이콘·marker를 쓰는 새 Canvas만 추가하고 명시적 연결 컴포넌트를 저장한다.</summary>
    private static void UpdateHudPrefab(Sprite bar, Sprite healthIcon, Sprite staminaIcon, CombatFootstepMarker marker)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HUD_PATH);
        try
        {
            TextMeshProUGUI record = root.transform.Find("DefenseRecordCanvas/Record").GetComponent<TextMeshProUGUI>();
            Transform canvas = root.transform.Find("CombatStatusCanvas");
            CombatPlayerHud playerHud;
            CombatFootstepHud footstepHud;
            if (canvas == null)
            {
                RectTransform canvasRect = CreateRect("CombatStatusCanvas", root.transform, Vector2.zero, Vector2.one);
                Canvas uiCanvas = canvasRect.gameObject.AddComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                uiCanvas.sortingOrder = 3;
                UnityEngine.UI.CanvasScaler scaler = canvasRect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                TMP_FontAsset font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset : record.font;
                CombatResourceGauge health = CreateGauge(canvasRect, "HealthGauge", new Vector2(48f, 98f),
                    new Vector2(360f, 34f), healthIcon, bar, new Color(0.85f, 0.22f, 0.2f), font, "100 / 100");
                CombatResourceGauge stamina = CreateGauge(canvasRect, "StaminaGauge", new Vector2(48f, 56f),
                    new Vector2(300f, 28f), staminaIcon, bar, new Color(0.55f, 0.8f, 0.3f), font, "-- / --");
                playerHud = canvasRect.gameObject.AddComponent<CombatPlayerHud>();
                SetReference(playerHud, "_healthGauge", health);
                SetReference(playerHud, "_staminaGauge", stamina);
                RectTransform container = CreateRect("FootstepMarkers", canvasRect, Vector2.zero, Vector2.one);
                footstepHud = container.gameObject.AddComponent<CombatFootstepHud>();
                SetReference(footstepHud, "_container", container);
            }
            else
            {
                playerHud = canvas.GetComponent<CombatPlayerHud>();
                footstepHud = canvas.Find("FootstepMarkers").GetComponent<CombatFootstepHud>();
            }
            EnsureMarkerPool(footstepHud, marker);

            CombatHudReferences references = root.GetComponent<CombatHudReferences>();
            if (references == null)
                references = root.AddComponent<CombatHudReferences>();
            SetReference(references, "_recordText", record);
            SetReference(references, "_playerHud", playerHud);
            SetReference(references, "_footstepHud", footstepHud);
            SerializedObject settings = new SerializedObject(references);
            SerializedProperty markers = settings.FindProperty("_directionMarkers");
            markers.arraySize = 12;
            for (int i = 0; i < markers.arraySize; i++)
                markers.GetArrayElementAtIndex(i).objectReferenceValue = root.transform.Find(
                    "ShurikenDirectionCanvas/Marker_" + i).GetComponent<DefenseDirectionMarker>();
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HUD_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>hud의 슬롯이 비어 있으면 marker 중첩 프리팹 24개를 편집 모드에 배치해 직렬화한다. 기존 슬롯과 이미지는 유지한다.</summary>
    private static void EnsureMarkerPool(CombatFootstepHud hud, CombatFootstepMarker marker)
    {
        SerializedObject settings = new SerializedObject(hud);
        SerializedProperty markers = settings.FindProperty("_markers");
        if (markers.arraySize > 0)
            return;
        markers.arraySize = 24;
        for (int i = 0; i < markers.arraySize; i++)
        {
            string name = "Footstep_" + i;
            Transform existing = hud.transform.Find(name);
            GameObject instance = existing != null ? existing.gameObject
                : (GameObject)PrefabUtility.InstantiatePrefab(marker.gameObject, hud.transform);
            instance.name = name;
            UnityEngine.UI.Image image = instance.GetComponent<UnityEngine.UI.Image>();
            image.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            PrefabUtility.RecordPrefabInstancePropertyModifications(image);
            markers.GetArrayElementAtIndex(i).objectReferenceValue = instance.GetComponent<CombatFootstepMarker>();
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>parent 아래 name의 RectTransform을 anchorMin·anchorMax로 생성해 전체 가용 영역을 채우고 반환한다.</summary>
    private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        RectTransform rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    /// <summary>rect에 sprite·color의 표시용 Image를 추가하고 입력을 가로채지 않도록 설정해 반환한다.</summary>
    private static UnityEngine.UI.Image CreateImage(RectTransform rect, Sprite sprite, Color color)
    {
        UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>parent에 name·배치·크기·아이콘·bar·색·font의 게이지를 생성하고 표시 참조를 연결해 반환한다.</summary>
    private static CombatResourceGauge CreateGauge(Transform parent, string name, Vector2 position, Vector2 size,
        Sprite icon, Sprite bar, Color color, TMP_FontAsset font, string initialText)
    {
        RectTransform root = CreateRect(name, parent, Vector2.zero, Vector2.zero);
        root.pivot = Vector2.zero;
        root.sizeDelta = size;
        root.anchoredPosition = position;
        CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        RectTransform iconRect = CreateRect("Icon", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.sizeDelta = Vector2.one * size.y;
        CreateImage(iconRect, icon, color).preserveAspect = true;
        RectTransform backgroundRect = CreateRect("Background", root, Vector2.zero, Vector2.one);
        backgroundRect.offsetMin = new Vector2(44f, 3f);
        backgroundRect.offsetMax = new Vector2(0f, -3f);
        CreateImage(backgroundRect, bar, new Color(0.08f, 0.09f, 0.12f, 0.85f));
        RectTransform fillRect = CreateRect("Fill", root, Vector2.zero, Vector2.one);
        fillRect.offsetMin = backgroundRect.offsetMin;
        fillRect.offsetMax = backgroundRect.offsetMax;
        UnityEngine.UI.Image fill = CreateImage(fillRect, bar, color);
        fill.type = UnityEngine.UI.Image.Type.Filled;
        fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = name == "HealthGauge" ? 1f : 0f;
        RectTransform textRect = CreateRect("Value", root, Vector2.zero, Vector2.one);
        textRect.offsetMin = new Vector2(48f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);
        TextMeshProUGUI value = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        value.font = font;
        value.fontSize = 18f;
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.color = Color.white;
        value.raycastTarget = false;
        value.text = initialText;
        if (name == "StaminaGauge")
            group.alpha = 0.35f;
        CombatResourceGauge gauge = root.gameObject.AddComponent<CombatResourceGauge>();
        SetReference(gauge, "_fill", fill);
        SetReference(gauge, "_valueText", value);
        SetReference(gauge, "_group", group);
        return gauge;
    }

    /// <summary>component의 field에 value를 직렬화해 프리팹·씬에 저장할 참조를 연결한다.</summary>
    private static void SetReference(UnityEngine.Object component, string field, UnityEngine.Object value)
    {
        SerializedObject settings = new SerializedObject(component);
        settings.FindProperty(field).objectReferenceValue = value;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
