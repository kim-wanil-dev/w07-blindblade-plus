using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using TMPro;

using BlindBlade.Telemetry;

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
    private const string ONBOARDING_CONFIG_VERSION = "onboarding-2026-10-26-dodge-movement-end";
    private static readonly string[] STAGE_CARD_FIELDS =
    {
        "_stageStartTitle", "_stageStartDetail", "_stageCompleteTitle",
        "_stageCompleteKeyboardDetail", "_stageCompleteGamepadDetail"
    };
    private static readonly string[] STAGE_BUTTON_FIELDS =
    {
        "_nextStageButtonText", "_finalStageButtonText", "_repeatStageButtonText"
    };
    private static readonly string[] PROMPT_HINT_FIELDS =
    {
        "_keyboardHint", "_gamepadHint", "_followUpKeyboardHint", "_followUpGamepadHint",
        "_followUpNote", "_followUpGamepadNote"
    };
    private static readonly string[] HUD_TUTORIAL_TEXT_FIELDS =
    {
        "_movementWaveTutorialText", "_movementWaveTutorialGamepadText",
        "_swordWaveTutorialText", "_swordWaveTutorialGamepadText",
        "_ultimateReadyTutorialText", "_ultimateReadyTutorialGamepadText",
        "_idleMovementText", "_idleMovementGamepadText"
    };

    private sealed class PromptTextSettings
    {
        public string[] Hints { get; set; }
        public string[] Objectives { get; set; }
        public string[] CardTexts { get; set; }
    }

    private sealed class HudTextSettings
    {
        public PromptTextSettings[] Prompts { get; set; }
        public Dictionary<string, string> TutorialTexts { get; set; }
        public string[] ButtonTexts { get; set; }
        public float InformationNoteDuration { get; set; }
        public float InformationNoteFadeDuration { get; set; }
    }

    /// <summary>온보딩 에셋을 불러오고 BambooDefense 복제 씬, 인트로, 빌드 목록을 저장한다.</summary>
    [MenuItem("Tools/Onboarding/Build Onboarding Scene")]
    public static void Build()
    {
        if (HasDirtySourceScene(SOURCE_SCENE) || HasDirtySourceScene(INTRO_SCENE) || HasDirtySourceScene(ONBOARDING_SCENE))
            throw new InvalidOperationException("온보딩 씬 작성에 사용하는 씬에 저장되지 않은 변경이 있어 중단했습니다.");
        HudTextSettings textSettings = ReadHudTextSettings();
        AssetDatabase.Refresh();
        ImportOnboardingMedia();
        CreateOnboardingScene(textSettings);
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

    /// <summary>기존 온보딩 씬에서 Inspector 안내·목표·카드·버튼 문구를 읽어 재생성 후 보존할 값을 반환한다.</summary>
    private static HudTextSettings ReadHudTextSettings()
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
            PromptTextSettings[] promptSettings = new PromptTextSettings[prompts.arraySize];
            for (int i = 0; i < prompts.arraySize; i++)
            {
                SerializedProperty prompt = prompts.GetArrayElementAtIndex(i);
                string[] hints = new string[PROMPT_HINT_FIELDS.Length];
                for (int field = 0; field < PROMPT_HINT_FIELDS.Length; field++)
                    hints[field] = prompt.FindPropertyRelative(PROMPT_HINT_FIELDS[field]).stringValue;
                SerializedProperty objectives = prompt.FindPropertyRelative("_objectives");
                string[] objectiveTexts = new string[objectives.arraySize];
                for (int objectiveIndex = 0; objectiveIndex < objectives.arraySize; objectiveIndex++)
                    objectiveTexts[objectiveIndex] = objectives.GetArrayElementAtIndex(objectiveIndex).stringValue;
                string[] cardTexts = new string[STAGE_CARD_FIELDS.Length];
                for (int field = 0; field < STAGE_CARD_FIELDS.Length; field++)
                    cardTexts[field] = prompt.FindPropertyRelative(STAGE_CARD_FIELDS[field]).stringValue;
                promptSettings[i] = new PromptTextSettings
                {
                    Hints = hints,
                    Objectives = objectiveTexts,
                    CardTexts = cardTexts
                };
            }

            string[] buttonTexts = new string[STAGE_BUTTON_FIELDS.Length];
            for (int field = 0; field < STAGE_BUTTON_FIELDS.Length; field++)
                buttonTexts[field] = serializedHud.FindProperty(STAGE_BUTTON_FIELDS[field]).stringValue;
            Dictionary<string, string> tutorialTexts = new Dictionary<string, string>();
            foreach (string field in HUD_TUTORIAL_TEXT_FIELDS)
                tutorialTexts[field] = serializedHud.FindProperty(field).stringValue;
            SerializedProperty duration = serializedHud.FindProperty("_informationNoteDuration");
            SerializedProperty fadeDuration = serializedHud.FindProperty("_informationNoteFadeDuration");
            return new HudTextSettings
            {
                Prompts = promptSettings,
                TutorialTexts = tutorialTexts,
                ButtonTexts = buttonTexts,
                InformationNoteDuration = duration != null ? duration.floatValue : 4f,
                InformationNoteFadeDuration = fadeDuration != null ? fadeDuration.floatValue : 0.2f
            };
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
    private static void CreateOnboardingScene(HudTextSettings textSettings)
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
        ConfigureHud(hud, font, textSettings);
        OnboardingFlowController flow = controllerObject.AddComponent<OnboardingFlowController>();
        ConfigureFlow(flow, director, spawner, hud, FindTransform(scene, "Arena"));
        OnboardingTelemetryRecorder telemetry = controllerObject.GetComponent<OnboardingTelemetryRecorder>();
        if (telemetry == null) telemetry = controllerObject.AddComponent<OnboardingTelemetryRecorder>();
        ConfigureTelemetry(telemetry);
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

    /// <summary>font와 보존한 Inspector 문구를 새 HUD에 저장하고 현재 입력·목표 흐름에 맞는 기본값만 이관한다.</summary>
    private static void ConfigureHud(OnboardingHud hud, TMP_FontAsset font, HudTextSettings textSettings)
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
            "쳐내기: 적이 공격할 때 막기 (우클릭)",
            "거합: Q 홀드"
        };
        string[] gamepadHints =
        {
            "이동 - 왼쪽 스틱\n회피 - A 버튼\n공격 - RB",
            "극한 회피 - 적의 공격에 맞춰 A 버튼으로 회피",
            "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)",
            "쳐내기: 적이 공격할 때 막기 (LB)",
            "거합: Y 홀드"
        };
        string[] followUpKeyboardHints =
        {
            "", "", "", "일섬: 좌클릭", ""
        };
        string[] followUpGamepadHints =
        {
            "", "", "", "일섬: RB", ""
        };
        string[] followUpKeyboardNotes =
        {
            "", "", "", "다시 쳐낸 뒤 집중 중 좌클릭으로 일섬을 발동합니다.",
            ""
        };
        string[] followUpGamepadNotes =
        {
            "", "", "", "다시 쳐낸 뒤 집중 중 RB로 일섬을 발동합니다.",
            ""
        };
        string[][] objectives =
        {
            new[] { "전방의 적 처치 0/3" },
            new[] { "수리검 극한 회피 1회" },
            new[] { "적의 공격 막기", "공격 직전에 막아 쳐내기" },
            new[] { "수리검 쳐내기 1회", "일섬 발동 1회" },
            new[] { "거합으로 적 처치" }
        };
        string[] tutorialDefaults =
        {
            "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.",
            "당신을 포함한 무언가 움직일 때 파동이 생겨 주변을 밝힙니다.",
            "쳐내기에 성공하면 시간이 느려집니다.\n집중 중 좌클릭으로 일섬을 발동합니다.",
            "쳐내기에 성공하면 시간이 느려집니다.\n집중 중 RB로 일섬을 발동합니다.",
            "적 처치, 극한 회피, 쳐내기로 거합 게이지를 채울 수 있습니다.\n거합 게이지를 모두 채운 뒤 Q를 홀드해 거합을 발동합니다.",
            "적 처치, 극한 회피, 쳐내기로 거합 게이지를 채울 수 있습니다.\n거합 게이지를 모두 채운 뒤 Y를 홀드해 거합을 발동합니다.",
            "WASD키로 이동하기",
            "왼쪽 스틱으로 이동하기"
        };
        Sprite rightMouse = AssetDatabase.LoadAssetAtPath<Sprite>(RIGHT_MOUSE);
        int[] rightIconStages = { 2, 3 };
        int[] followUpObjectives = { 1, 1, 1, 1, 1 };

        for (int i = 0; i < prompts.arraySize; i++)
        {
            SerializedProperty prompt = prompts.GetArrayElementAtIndex(i);
            PromptTextSettings savedPrompt = textSettings.Prompts[i];
            prompt.FindPropertyRelative("_keyboardHint").stringValue = MigratePromptHint(i, 0,
                savedPrompt.Hints[0], keyboardHints[i]);
            prompt.FindPropertyRelative("_gamepadHint").stringValue = MigratePromptHint(i, 1,
                savedPrompt.Hints[1], gamepadHints[i]);
            prompt.FindPropertyRelative("_followUpKeyboardHint").stringValue = MigratePromptHint(i, 2,
                savedPrompt.Hints[2], followUpKeyboardHints[i]);
            prompt.FindPropertyRelative("_followUpGamepadHint").stringValue = MigratePromptHint(i, 3,
                savedPrompt.Hints[3], followUpGamepadHints[i]);
            prompt.FindPropertyRelative("_followUpNote").stringValue = MigrateText(
                savedPrompt.Hints[4], followUpKeyboardNotes[i], string.Empty);
            prompt.FindPropertyRelative("_followUpGamepadNote").stringValue = MigrateText(
                savedPrompt.Hints[5], followUpGamepadNotes[i], string.Empty);
            if (i == 4)
            {
                prompt.FindPropertyRelative("_followUpKeyboardHint").stringValue = string.Empty;
                prompt.FindPropertyRelative("_followUpGamepadHint").stringValue = string.Empty;
                prompt.FindPropertyRelative("_followUpNote").stringValue = string.Empty;
                prompt.FindPropertyRelative("_followUpGamepadNote").stringValue = string.Empty;
            }
            prompt.FindPropertyRelative("_followUpObjective").intValue = followUpObjectives[i];
            for (int field = 0; field < STAGE_CARD_FIELDS.Length; field++)
                prompt.FindPropertyRelative(STAGE_CARD_FIELDS[field]).stringValue = MigrateStageCardText(
                    i, field, savedPrompt.CardTexts[field]);
            SerializedProperty icon = prompt.FindPropertyRelative("_keyboardIcon");
            if (System.Array.IndexOf(rightIconStages, i) >= 0) icon.objectReferenceValue = rightMouse;
            else icon.objectReferenceValue = null;
            SerializedProperty followUpIcon = prompt.FindPropertyRelative("_followUpKeyboardIcon");
            followUpIcon.objectReferenceValue = null;

            SerializedProperty objectiveArray = prompt.FindPropertyRelative("_objectives");
            objectiveArray.arraySize = objectives[i].Length;
            for (int objectiveIndex = 0; objectiveIndex < objectiveArray.arraySize; objectiveIndex++)
                objectiveArray.GetArrayElementAtIndex(objectiveIndex).stringValue = GetObjectiveText(
                    i, objectiveIndex, savedPrompt.Objectives, objectives[i]);
        }

        for (int field = 0; field < STAGE_BUTTON_FIELDS.Length; field++)
            serializedHud.FindProperty(STAGE_BUTTON_FIELDS[field]).stringValue = textSettings.ButtonTexts[field];

        for (int field = 0; field < HUD_TUTORIAL_TEXT_FIELDS.Length; field++)
        {
            string propertyName = HUD_TUTORIAL_TEXT_FIELDS[field];
            serializedHud.FindProperty(propertyName).stringValue = MigrateTutorialText(propertyName,
                textSettings.TutorialTexts[propertyName], tutorialDefaults[field]);
        }
        serializedHud.FindProperty("_informationNoteDuration").floatValue = textSettings.InformationNoteDuration;
        serializedHud.FindProperty("_informationNoteFadeDuration").floatValue = textSettings.InformationNoteFadeDuration;

        serializedHud.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>저장된 입력 안내가 이전 4단계 기본값이면 새 입력 안내로 바꾸고 Inspector 사용자 문구는 유지한다.</summary>
    private static string MigratePromptHint(int stageIndex, int hintIndex, string savedText, string newDefault)
    {
        if (stageIndex == 4 && hintIndex == 0)
            return MigrateText(savedText, newDefault,
                "막기: 마우스 오른쪽 버튼 홀드\n쳐내기: 적이 공격할 때 막기 (우클릭)");
        if (stageIndex == 4 && hintIndex == 1)
            return MigrateText(savedText, newDefault,
                "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)");
        if (stageIndex == 4 && hintIndex == 2)
            return MigrateText(savedText, newDefault, "거합: Q");
        if (stageIndex == 4 && hintIndex == 3)
            return MigrateText(savedText, newDefault, "거합: Y");
        if (stageIndex != 3) return savedText ?? newDefault;
        if (hintIndex == 0)
            return MigrateText(savedText, newDefault, "막기: 마우스 오른쪽 버튼 홀드\n쳐내기: 적이 공격할 때 막기 (우클릭)");
        if (hintIndex == 1)
            return MigrateText(savedText, newDefault, "막기: LB 홀드\n쳐내기: 적이 공격할 때 막기 (LB)");
        if (hintIndex == 2)
            return MigrateText(savedText, newDefault, "일섬: Shift");
        if (hintIndex == 3)
            return MigrateText(savedText, newDefault, "일섬: RT");
        return savedText ?? newDefault;
    }

    /// <summary>기존 기본 문구와 같은 저장값만 newDefault로 교체해 사용자 Inspector 편집 문구를 보존한다.</summary>
    private static string MigrateText(string savedText, string newDefault, params string[] previousDefaults)
    {
        if (savedText == null) return newDefault;
        foreach (string previousDefault in previousDefaults)
            if (savedText == previousDefault) return newDefault;
        return savedText;
    }

    /// <summary>4단계와 5단계의 이전 목표 배열에서 현재 목표 문구를 선택하고 Inspector 문구를 반환한다.</summary>
    private static string GetObjectiveText(int stageIndex, int objectiveIndex, string[] savedObjectives,
        string[] newDefaults)
    {
        if (stageIndex == 4 && savedObjectives != null && savedObjectives.Length == 2)
            return savedObjectives[1];
        if (stageIndex != 3)
            return savedObjectives != null && savedObjectives.Length == newDefaults.Length
                ? savedObjectives[objectiveIndex] : newDefaults[objectiveIndex];

        if (savedObjectives == null) return newDefaults[objectiveIndex];
        if (savedObjectives.Length == 3)
        {
            int legacyIndex = objectiveIndex + 1;
            string savedText = savedObjectives[legacyIndex];
            return objectiveIndex == 0
                ? MigrateText(savedText, newDefaults[objectiveIndex], "공격 쳐내기 0/1", "공격 쳐내기 1회")
                : savedText;
        }
        if (savedObjectives.Length == 2)
        {
            string savedText = savedObjectives[objectiveIndex];
            return objectiveIndex == 0
                ? MigrateText(savedText, newDefaults[objectiveIndex], "공격 쳐내기 0/1", "공격 쳐내기 1회")
                : savedText;
        }
        return newDefaults[objectiveIndex];
    }

    /// <summary>4·5단계의 낡은 일섬·거합 완료 안내만 새 조작 규칙으로 교체한다.</summary>
    private static string MigrateStageCardText(int stageIndex, int fieldIndex, string savedText)
    {
        if (savedText == null) return string.Empty;
        if (stageIndex == 3 && fieldIndex == 3
            && (savedText.Contains("Shift") || savedText.Contains("일섬 게이지")))
            return "쳐내기 후 집중 중 좌클릭으로 일섬을 발동합니다.";
        if (stageIndex == 3 && fieldIndex == 4
            && (savedText.Contains("RT") || savedText.Contains("일섬 게이지")))
            return "쳐내기 후 집중 중 RB로 일섬을 발동합니다.";
        if (stageIndex == 4 && fieldIndex == 3
            && (savedText.Contains("Q를 눌러") || savedText.Contains("100%")))
            return "거합 게이지를 모두 채운 뒤 Q를 홀드해 거합을 발동합니다.";
        if (stageIndex == 4 && fieldIndex == 4
            && (savedText.Contains("Y를 눌러") || savedText.Contains("100%")))
            return "거합 게이지를 모두 채운 뒤 Y를 홀드해 거합을 발동합니다.";
        return savedText;
    }

    /// <summary>구형 게이지·Shift/RT·Q/Y 누름 튜토리얼만 새 안내로 바꾸고 다른 Inspector 문구는 보존한다.</summary>
    private static string MigrateTutorialText(string fieldName, string savedText, string newDefault)
    {
        if (savedText == null) return newDefault;
        savedText = savedText.Replace("\n마우스 좌클릭으로 계속 진행", string.Empty)
            .Replace("\nRB 버튼으로 계속 진행", string.Empty);
        bool obsolete = fieldName.StartsWith("_swordWaveTutorial", StringComparison.Ordinal)
            ? savedText.Contains("일섬 게이지") || savedText.Contains("Shift") || savedText.Contains("RT")
            : fieldName.StartsWith("_ultimateReadyTutorial", StringComparison.Ordinal)
                && (savedText == "거합 게이지를 모두 채웠습니다.\nQ를 홀드해 거합을 발동합니다."
                    || savedText == "거합 게이지를 모두 채웠습니다.\nY를 홀드해 거합을 발동합니다."
                    || savedText.Contains("Q를 눌러") || savedText.Contains("Y를 눌러"));
        return obsolete ? newDefault : savedText;
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

    /// <summary>온보딩 텔레메트리 설정의 행동 안내 버전을 저장한다.</summary>
    private static void ConfigureTelemetry(OnboardingTelemetryRecorder telemetry)
    {
        SerializedObject serializedTelemetry = new SerializedObject(telemetry);
        serializedTelemetry.FindProperty("_configVersion").stringValue = ONBOARDING_CONFIG_VERSION;
        serializedTelemetry.ApplyModifiedPropertiesWithoutUndo();
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
