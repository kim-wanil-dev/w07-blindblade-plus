using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using TMPro;

/// <summary>기존 대숲 방어 씬을 복제하고 온보딩 씬·인트로 연결·빌드 등록을 구성한다.</summary>
public static class OnboardingSceneBuilder
{
    private const string SOURCE_SCENE = "Assets/Scenes/Defense/BambooDefense.unity";
    private const string ONBOARDING_SCENE = "Assets/Scenes/Onboarding/Onboarding.unity";
    private const string INTRO_SCENE = "Assets/Scenes/IntroEndingScene/IntroScene.unity";
    private const string FONT_ASSET = "Assets/Fonts/ChosunCentennial_otf SDF.asset";
    private const string TORCH_AUDIO = "Assets/Onboarding/Media/횃불 소리.wav";
    private const string LEFT_MOUSE = "Assets/Onboarding/Media/MouseLeft.png";
    private const string RIGHT_MOUSE = "Assets/Onboarding/Media/MouseRight.png";

    /// <summary>온보딩 에셋을 불러오고 BambooDefense 복제 씬, 인트로, 빌드 목록을 저장한다.</summary>
    [MenuItem("Tools/Onboarding/Build Onboarding Scene")]
    public static void Build()
    {
        if (HasDirtySourceScene(SOURCE_SCENE) || HasDirtySourceScene(INTRO_SCENE))
            throw new InvalidOperationException("BambooDefense 또는 IntroScene에 저장되지 않은 변경이 있어 온보딩 씬 작성 전에 중단했습니다.");
        AssetDatabase.Refresh();
        ImportOnboardingMedia();
        CreateOnboardingScene();
        if (!UpdateIntroScene())
            throw new InvalidOperationException("IntroScene의 시작 버튼에 온보딩 전환을 연결하지 못했습니다.");
        RegisterBuildScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("온보딩 씬 구성 완료: " + ONBOARDING_SCENE);
    }

    /// <summary>열린 씬 중 path와 같은 씬에 저장되지 않은 수정이 있는지 반환한다.</summary>
    private static bool HasDirtySourceScene(string path)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.path == path && scene.isDirty) return true;
        }
        return false;
    }

    /// <summary>횃불 WAV를 PCM으로 가져오고 클릭 PNG가 있을 경우 Sprite 타입으로 설정한다.</summary>
    private static void ImportOnboardingMedia()
    {
        AudioImporter audioImporter = AssetImporter.GetAtPath(TORCH_AUDIO) as AudioImporter;
        if (audioImporter != null)
        {
            audioImporter.loadInBackground = false;
            AudioImporterSampleSettings settings = audioImporter.defaultSampleSettings;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.quality = 1f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            audioImporter.defaultSampleSettings = settings;
            audioImporter.SaveAndReimport();
        }

        SetSpriteImport(LEFT_MOUSE);
        SetSpriteImport(RIGHT_MOUSE);
    }

    /// <summary>path의 PNG가 있으면 Single Sprite와 투명도 설정을 저장한다.</summary>
    private static void SetSpriteImport(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
    }

    /// <summary>BambooDefense를 복제한 씬에서 웨이브·HUD·게임오버를 끄고 온보딩 제어기를 배치한다.</summary>
    private static void CreateOnboardingScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Onboarding"))
            AssetDatabase.CreateFolder("Assets/Scenes", "Onboarding");
        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        if (previousActiveScene.path == SOURCE_SCENE && previousActiveScene.isDirty)
        {
            Debug.LogError("BambooDefense에 저장되지 않은 변경이 있어 복제 전에 중단했습니다.");
            return;
        }

        File.Copy(SOURCE_SCENE, ONBOARDING_SCENE, true);
        AssetDatabase.Refresh();
        Scene scene = EditorSceneManager.OpenScene(ONBOARDING_SCENE, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(scene);

        BambooDefenseDirector director = FindComponents<BambooDefenseDirector>(scene).FirstOrDefault();
        DefenseEnemySpawner spawner = FindComponents<DefenseEnemySpawner>(scene).FirstOrDefault();
        ConfigureDirector(director, spawner);

        foreach (DefenseRecordHud recordHud in FindComponents<DefenseRecordHud>(scene))
        {
            recordHud.enabled = false;
            TMP_Text recordText = new SerializedObject(recordHud).FindProperty("_text").objectReferenceValue as TMP_Text;
            if (recordText != null) recordText.gameObject.SetActive(false);
        }
        foreach (DefenseHealthRegen healthRegen in FindComponents<DefenseHealthRegen>(scene))
            healthRegen.enabled = false;
        foreach (GameOverUI gameOver in FindComponents<GameOverUI>(scene))
            gameOver.enabled = false;

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_ASSET);
        foreach (TMP_Text text in FindComponents<TMP_Text>(scene))
            text.font = font;

        GameObject controllerObject = new GameObject("OnboardingFlow");
        OnboardingHud hud = controllerObject.AddComponent<OnboardingHud>();
        ConfigureHud(hud, font);
        OnboardingFlowController flow = controllerObject.AddComponent<OnboardingFlowController>();
        ConfigureFlow(flow, director, spawner, hud, FindTransform(scene, "Arena"));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.SetActiveScene(previousActiveScene);
        EditorSceneManager.CloseScene(scene, false);
    }

    /// <summary>scene의 모든 root 아래에서 컴포넌트를 찾아 다른 열린 씬의 오브젝트를 섞지 않고 반환한다.</summary>
    private static T[] FindComponents<T>(Scene scene) where T : Component
    {
        List<T> components = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            components.AddRange(root.GetComponentsInChildren<T>(true));
        return components.ToArray();
    }

    /// <summary>scene의 root와 자식에서 name이 같은 Transform을 찾아 반환한다.</summary>
    private static Transform FindTransform(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root.transform;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
        }
        return null;
    }

    /// <summary>기존 디렉터를 유지하면서 시작 지연을 줄이고 자동 스폰만 복제 씬에서 억제한다.</summary>
    private static void ConfigureDirector(BambooDefenseDirector director, DefenseEnemySpawner spawner)
    {
        if (director != null)
        {
            SerializedObject serializedDirector = new SerializedObject(director);
            serializedDirector.FindProperty("_startDelay").floatValue = 0.1f;
            serializedDirector.ApplyModifiedPropertiesWithoutUndo();
        }

        if (spawner == null) return;
        SerializedObject serializedSpawner = new SerializedObject(spawner);
        serializedSpawner.FindProperty("_initialThrowers").intValue = 0;
        serializedSpawner.FindProperty("_throwersPerMinute").floatValue = 0f;
        serializedSpawner.FindProperty("_meleeStartTime").floatValue = 1000000f;
        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>고정 폰트와 입력 장치별 단계 문구·목표·마우스 이미지를 HUD에 연결한다.</summary>
    private static void ConfigureHud(OnboardingHud hud, TMP_FontAsset font)
    {
        SerializedObject serializedHud = new SerializedObject(hud);
        serializedHud.FindProperty("_font").objectReferenceValue = font;
        SerializedProperty prompts = serializedHud.FindProperty("_prompts");
        prompts.arraySize = 9;

        string[][] keyboardHints =
        {
            new[] { "이동 - WASD", "" },
            new[] { "돌진/회피 - SPACE", "" },
            new[] { "좌클릭 - 공격", "" },
            new[] { "극한 회피 - SPACE", "" },
            new[] { "우클릭 - 방어", "" },
            new[] { "우클릭 - 패링", "" },
            new[] { "우클릭 - 방어", "" },
            new[] { "우클릭 - 패링", "" },
            new[] { "우클릭 - 패링", "" }
        };
        string[][] gamepadHints =
        {
            new[] { "이동 - 왼쪽 스틱", "" },
            new[] { "돌진/회피 - A 버튼", "" },
            new[] { "RB - 공격", "" },
            new[] { "극한 회피 - A 버튼", "" },
            new[] { "LB - 방어", "" },
            new[] { "LB - 패링", "" },
            new[] { "LB - 방어", "" },
            new[] { "LB - 패링", "" },
            new[] { "LB - 패링", "" }
        };
        string[] followUpKeyboardHints =
        {
            "", "", "", "", "", "좌클릭 - 검기", "", "좌클릭 - 검기", "좌클릭 홀드 - 궁극기"
        };
        string[] followUpGamepadHints =
        {
            "", "", "", "", "", "RB - 검기", "", "RB - 검기", "RB 홀드 - 궁극기"
        };
        string[][] objectives =
        {
            new[] { "파동 중심 1m 이내 도착" },
            new[] { "돌진 1회" },
            new[] { "근접 적 처치 1회" },
            new[] { "수리검 극한 회피 1회" },
            new[] { "가드 1회", "가드를 놓고 퍼펙트 패링 1회" },
            new[] { "수리검 퍼펙트 패링 1회", "검기 실제 발사" },
            new[] { "가드 1회", "가드를 놓고 퍼펙트 패링 1회" },
            new[] { "수리검 퍼펙트 패링 1회", "검기 실제 발사" },
            new[] { "수리검 퍼펙트 패링 1회", "궁극기로 적 8명 처치" }
        };
        Sprite leftMouse = AssetDatabase.LoadAssetAtPath<Sprite>(LEFT_MOUSE);
        Sprite rightMouse = AssetDatabase.LoadAssetAtPath<Sprite>(RIGHT_MOUSE);
        int[] leftIconStages = { 2 };
        int[] rightIconStages = { 4, 5, 6, 7, 8 };
        int[] followUpIconStages = { 5, 7, 8 };

        for (int i = 0; i < prompts.arraySize; i++)
        {
            SerializedProperty prompt = prompts.GetArrayElementAtIndex(i);
            prompt.FindPropertyRelative("_keyboardHint").stringValue = keyboardHints[i][0];
            prompt.FindPropertyRelative("_gamepadHint").stringValue = gamepadHints[i][0];
            prompt.FindPropertyRelative("_followUpKeyboardHint").stringValue = followUpKeyboardHints[i];
            prompt.FindPropertyRelative("_followUpGamepadHint").stringValue = followUpGamepadHints[i];
            SerializedProperty icon = prompt.FindPropertyRelative("_keyboardIcon");
            if (System.Array.IndexOf(leftIconStages, i) >= 0) icon.objectReferenceValue = leftMouse;
            else if (System.Array.IndexOf(rightIconStages, i) >= 0) icon.objectReferenceValue = rightMouse;
            else icon.objectReferenceValue = null;
            SerializedProperty followUpIcon = prompt.FindPropertyRelative("_followUpKeyboardIcon");
            followUpIcon.objectReferenceValue = System.Array.IndexOf(followUpIconStages, i) >= 0 ? leftMouse : null;

            SerializedProperty objectiveArray = prompt.FindPropertyRelative("_objectives");
            objectiveArray.arraySize = objectives[i].Length;
            for (int objectiveIndex = 0; objectiveIndex < objectiveArray.arraySize; objectiveIndex++)
                objectiveArray.GetArrayElementAtIndex(objectiveIndex).stringValue = objectives[i][objectiveIndex];
        }

        serializedHud.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>기존 근접·투척수·Wave 프리팹과 씬 단위 연결을 새 단계 제어기에 저장한다.</summary>
    private static void ConfigureFlow(OnboardingFlowController flow, BambooDefenseDirector director,
        DefenseEnemySpawner spawner, OnboardingHud hud, Transform arenaCenter)
    {
        GameObject meleePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Defense/DefenseMelee.prefab");
        BambooThrower throwerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Defense/DefenseThrower.prefab")
            ?.GetComponent<BambooThrower>();
        FootstepWave wavePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Wave/Wave.prefab")
            ?.GetComponent<FootstepWave>();
        Transform unitRoot = null;
        if (spawner != null)
            unitRoot = new SerializedObject(spawner).FindProperty("_unitRoot").objectReferenceValue as Transform;
        SerializedObject serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("_meleePrefab").objectReferenceValue = meleePrefab;
        serializedFlow.FindProperty("_throwerPrefab").objectReferenceValue = throwerPrefab;
        serializedFlow.FindProperty("_targetWavePrefab").objectReferenceValue = wavePrefab;
        serializedFlow.FindProperty("_unitRoot").objectReferenceValue = unitRoot;
        serializedFlow.FindProperty("_arenaCenter").objectReferenceValue = arenaCenter;
        serializedFlow.FindProperty("_hud").objectReferenceValue = hud;
        serializedFlow.FindProperty("_stage1SceneName").stringValue = "BambooStage1";
        serializedFlow.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>활성 IntroMenu의 씬 목적지를 온보딩으로 바꾸고 횃불 오디오 전환을 연결한다.</summary>
    private static bool UpdateIntroScene()
    {
        Scene previousActiveScene = EditorSceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenScene(INTRO_SCENE, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(scene);
        IntroMenu[] menus = FindComponents<IntroMenu>(scene);
        IntroMenu activeMenu = menus.FirstOrDefault(menu => menu != null && menu.enabled && menu.gameObject.activeInHierarchy);
        if (activeMenu == null)
        {
            Debug.LogError("IntroScene에서 활성 IntroMenu를 찾지 못했습니다.");
            EditorSceneManager.SetActiveScene(previousActiveScene);
            EditorSceneManager.CloseScene(scene, false);
            return false;
        }

        SerializedObject serializedMenu = new SerializedObject(activeMenu);
        serializedMenu.FindProperty("_gameSceneName").stringValue = "Onboarding";
        serializedMenu.ApplyModifiedPropertiesWithoutUndo();
        IntroOnboardingTransition transition = activeMenu.GetComponent<IntroOnboardingTransition>();
        if (transition == null) transition = Undo.AddComponent<IntroOnboardingTransition>(activeMenu.gameObject);
        SerializedObject serializedTransition = new SerializedObject(transition);
        serializedTransition.FindProperty("_torchClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(TORCH_AUDIO);
        serializedTransition.FindProperty("_fadeDuration").floatValue = 0.5f;
        serializedTransition.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorSceneManager.SetActiveScene(previousActiveScene);
        EditorSceneManager.CloseScene(scene, false);
        return true;
    }

    /// <summary>기존 빌드 순서를 보존하면서 onboarding 씬을 마지막 인덱스로 등록한다.</summary>
    private static void RegisterBuildScene()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.All(scene => scene.path != ONBOARDING_SCENE))
            scenes.Add(new EditorBuildSettingsScene(ONBOARDING_SCENE, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
