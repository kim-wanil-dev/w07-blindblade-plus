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
    private static readonly string[] STAGE_CARD_FIELDS =
    {
        "_stageStartTitle", "_stageStartDetail", "_stageCompleteTitle",
        "_stageCompleteKeyboardDetail", "_stageCompleteGamepadDetail"
    };
    private static readonly string[] STAGE_BUTTON_FIELDS =
    {
        "_nextStageButtonText", "_finalStageButtonText", "_repeatStageButtonText"
    };

    /// <summary>온보딩 에셋을 불러오고 BambooDefense 복제 씬, 인트로, 빌드 목록을 저장한다.</summary>
    [MenuItem("Tools/Onboarding/Build Onboarding Scene")]
    public static void Build()
    {
        if (HasDirtySourceScene(SOURCE_SCENE) || HasDirtySourceScene(INTRO_SCENE) || HasDirtySourceScene(ONBOARDING_SCENE))
            throw new InvalidOperationException("온보딩 씬 작성에 사용하는 씬에 저장되지 않은 변경이 있어 중단했습니다.");
        (string[][] cardTexts, string[] buttonTexts) = ReadCardTexts();
        AssetDatabase.Refresh();
        ImportOnboardingMedia();
        CreateOnboardingScene(cardTexts, buttonTexts);
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

    /// <summary>기존 온보딩 씬의 단계 카드와 버튼 Inspector 문구를 읽어 재생성 후에도 유지할 값을 반환한다.</summary>
    private static (string[][] cardTexts, string[] buttonTexts) ReadCardTexts()
    {
        if (!File.Exists(ONBOARDING_SCENE))
            throw new InvalidOperationException("온보딩 씬이 없어 Inspector의 단계 카드 문구를 읽을 수 없습니다.");

        Scene scene = SceneManager.GetSceneByPath(ONBOARDING_SCENE);
        bool openedHere = !scene.IsValid();
        if (openedHere) scene = EditorSceneManager.OpenScene(ONBOARDING_SCENE, OpenSceneMode.Additive);
        try
        {
            OnboardingHud hud = FindComponents<OnboardingHud>(scene).FirstOrDefault();
            if (hud == null) throw new InvalidOperationException("온보딩 씬에 OnboardingHud가 없습니다.");
            SerializedObject serializedHud = new SerializedObject(hud);
            SerializedProperty prompts = serializedHud.FindProperty("_prompts");
            if (prompts.arraySize != 5)
                throw new InvalidOperationException("온보딩 단계 수가 5개가 아니어서 기존 카드 문구를 보존할 수 없습니다.");
            string[][] cardTexts = new string[prompts.arraySize][];
            for (int i = 0; i < prompts.arraySize; i++)
            {
                cardTexts[i] = new string[STAGE_CARD_FIELDS.Length];
                SerializedProperty prompt = prompts.GetArrayElementAtIndex(i);
                for (int field = 0; field < STAGE_CARD_FIELDS.Length; field++)
                    cardTexts[i][field] = prompt.FindPropertyRelative(STAGE_CARD_FIELDS[field]).stringValue;
            }

            string[] buttonTexts = new string[STAGE_BUTTON_FIELDS.Length];
            for (int field = 0; field < STAGE_BUTTON_FIELDS.Length; field++)
                buttonTexts[field] = serializedHud.FindProperty(STAGE_BUTTON_FIELDS[field]).stringValue;
            return (cardTexts, buttonTexts);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, false);
        }
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
    private static void CreateOnboardingScene(string[][] cardTexts, string[] buttonTexts)
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
        ConfigureHud(hud, font, cardTexts, buttonTexts);
        OnboardingFlowController flow = controllerObject.AddComponent<OnboardingFlowController>();
        ConfigureFlow(flow, director, spawner, hud, FindTransform(scene, "Arena"));
        FindTransform(scene, "Ground").gameObject.AddComponent<OnboardingGroundGuide>();
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

    /// <summary>font, cardTexts, buttonTexts와 입력 장치별 목표·마우스 이미지를 새 HUD의 Inspector 값으로 저장한다.</summary>
    private static void ConfigureHud(OnboardingHud hud, TMP_FontAsset font, string[][] cardTexts, string[] buttonTexts)
    {
        SerializedObject serializedHud = new SerializedObject(hud);
        serializedHud.FindProperty("_font").objectReferenceValue = font;
        SerializedProperty prompts = serializedHud.FindProperty("_prompts");
        prompts.arraySize = 5;

        string[] keyboardHints =
        {
            "이동 - WASD\n회피 - Space\n공격 - 마우스 왼쪽 버튼 클릭",
            "극한 회피 - 적의 공격에 맞춰 Space로 회피",
            "막기: 마우스 오른쪽 버튼 홀드\n쳐내기: 적이 공격할 때 막기 (우클릭)",
            "막기: 마우스 오른쪽 버튼 홀드\n쳐내기: 적이 공격할 때 막기 (우클릭)",
            "막기: 마우스 오른쪽 버튼 홀드\n쳐내기: 적이 공격할 때 막기 (우클릭)"
        };
        string[] gamepadHints =
        {
            "이동 - 왼쪽 스틱\n회피 - A 버튼\n공격 - RB",
            "극한 회피 - 적의 공격에 맞춰 A 버튼으로 회피",
            "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)",
            "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)",
            "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)"
        };
        string[] followUpKeyboardHints =
        {
            "", "", "", "일섬: Shift", "거합: Q"
        };
        string[] followUpGamepadHints =
        {
            "", "", "", "일섬: RT", "거합: Y"
        };
        string[][] objectives =
        {
            new[] { "전방의 적 처치 0/3" },
            new[] { "수리검 극한 회피 1회" },
            new[] { "적의 공격 막기", "공격 직전에 막아 쳐내기" },
            new[] { "수리검 막기 1회", "공격 쳐내기 0/1", "일섬 발동 1회" },
            new[] { "거합 게이지 채우기 0%", "거합으로 적 처치" }
        };
        Sprite rightMouse = AssetDatabase.LoadAssetAtPath<Sprite>(RIGHT_MOUSE);
        int[] rightIconStages = { 2, 3, 4 };
        int[] followUpObjectives = { 1, 1, 1, 2, 1 };

        for (int i = 0; i < prompts.arraySize; i++)
        {
            SerializedProperty prompt = prompts.GetArrayElementAtIndex(i);
            prompt.FindPropertyRelative("_keyboardHint").stringValue = keyboardHints[i];
            prompt.FindPropertyRelative("_gamepadHint").stringValue = gamepadHints[i];
            prompt.FindPropertyRelative("_followUpKeyboardHint").stringValue = followUpKeyboardHints[i];
            prompt.FindPropertyRelative("_followUpGamepadHint").stringValue = followUpGamepadHints[i];
            prompt.FindPropertyRelative("_followUpNote").stringValue = string.Empty;
            prompt.FindPropertyRelative("_followUpObjective").intValue = followUpObjectives[i];
            for (int field = 0; field < STAGE_CARD_FIELDS.Length; field++)
                prompt.FindPropertyRelative(STAGE_CARD_FIELDS[field]).stringValue = cardTexts[i][field];
            SerializedProperty icon = prompt.FindPropertyRelative("_keyboardIcon");
            if (System.Array.IndexOf(rightIconStages, i) >= 0) icon.objectReferenceValue = rightMouse;
            else icon.objectReferenceValue = null;
            SerializedProperty followUpIcon = prompt.FindPropertyRelative("_followUpKeyboardIcon");
            followUpIcon.objectReferenceValue = null;

            SerializedProperty objectiveArray = prompt.FindPropertyRelative("_objectives");
            objectiveArray.arraySize = objectives[i].Length;
            for (int objectiveIndex = 0; objectiveIndex < objectiveArray.arraySize; objectiveIndex++)
                objectiveArray.GetArrayElementAtIndex(objectiveIndex).stringValue = objectives[i][objectiveIndex];
        }

        for (int field = 0; field < STAGE_BUTTON_FIELDS.Length; field++)
            serializedHud.FindProperty(STAGE_BUTTON_FIELDS[field]).stringValue = buttonTexts[field];

        serializedHud.FindProperty("_movementWaveTutorialText").stringValue = "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_movementWaveTutorialGamepadText").stringValue = "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_perfectDodgeTutorialText").stringValue = "극한 회피에 성공하면 파동이 발생해 주위를 잠시 밝히고, 시간이 느려집니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_perfectDodgeTutorialGamepadText").stringValue = "극한 회피에 성공하면 파동이 발생해 주위를 잠시 밝히고, 시간이 느려집니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_perfectDodgeMeterTutorialText").stringValue = "극한 회피는 일섬 게이지를 반 칸, 거합 게이지를 15% 채웁니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_perfectDodgeMeterTutorialGamepadText").stringValue = "극한 회피는 일섬 게이지를 반 칸, 거합 게이지를 15% 채웁니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_targetThreadTutorialText").stringValue = "검에서 나온 기운이 적에게 연결됩니다.\n마우스 좌클릭으로 발도 실행";
        serializedHud.FindProperty("_targetThreadTutorialGamepadText").stringValue = "검에서 나온 기운이 적에게 연결됩니다.\nRB 버튼으로 발도 실행";
        serializedHud.FindProperty("_parryHudTutorialText").stringValue = "화면의 원형 표시가 거합 게이지입니다. 극한 회피는 15%, 쳐내기는 약 33%를 채웁니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_parryHudTutorialGamepadText").stringValue = "화면의 원형 표시가 거합 게이지입니다. 극한 회피는 15%, 쳐내기는 약 33%를 채웁니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_parryBeadsTutorialText").stringValue = "검의 방울 세 개와 화면의 세 칸은 일섬 게이지를 보여줍니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_parryBeadsTutorialGamepadText").stringValue = "검의 방울 세 개와 화면의 세 칸은 일섬 게이지를 보여줍니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_swordWaveTutorialText").stringValue = "화면의 세 칸과 검의 방울은 일섬 게이지입니다. 쳐내기로 한 칸을 채웁니다.\nShift를 누르면 한 칸을 써서 일섬을 발동합니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_swordWaveTutorialGamepadText").stringValue = "화면의 세 칸과 검의 방울은 일섬 게이지입니다. 쳐내기로 한 칸을 채웁니다.\nRT를 누르면 한 칸을 써서 일섬을 발동합니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_ultimateReadyTutorialText").stringValue = "원형 거합 게이지가 100%가 되면 Q를 눌러 발동합니다.\n마우스 좌클릭으로 계속 진행";
        serializedHud.FindProperty("_ultimateReadyTutorialGamepadText").stringValue = "원형 거합 게이지가 100%가 되면 Y를 눌러 발동합니다.\nRB 버튼으로 계속 진행";
        serializedHud.FindProperty("_idleMovementText").stringValue = "WASD키로 이동하기";
        serializedHud.FindProperty("_idleMovementGamepadText").stringValue = "왼쪽 스틱으로 이동하기";

        serializedHud.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>기존 근접·투척수 프리팹과 씬 단위 연결을 새 단계 제어기에 저장한다.</summary>
    private static void ConfigureFlow(OnboardingFlowController flow, BambooDefenseDirector director,
        DefenseEnemySpawner spawner, OnboardingHud hud, Transform arenaCenter)
    {
        GameObject meleePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Defense/DefenseMelee.prefab");
        BambooThrower throwerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Defense/DefenseThrower.prefab")
            ?.GetComponent<BambooThrower>();
        Transform unitRoot = null;
        if (spawner != null)
            unitRoot = new SerializedObject(spawner).FindProperty("_unitRoot").objectReferenceValue as Transform;
        SerializedObject serializedFlow = new SerializedObject(flow);
        serializedFlow.FindProperty("_meleePrefab").objectReferenceValue = meleePrefab;
        serializedFlow.FindProperty("_throwerPrefab").objectReferenceValue = throwerPrefab;
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
