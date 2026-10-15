using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using Newtonsoft.Json.Linq;

using BlindBlade.Telemetry;

/// <summary>열린 BambooDefense 씬에 읽기 전용 로그 소비자를 추가하고 기존 공급자만 연결한다.</summary>
public static class CombatTelemetryAuthoring
{
    private const string UPLOAD_ASSET_DIRECTORY = "Assets/Resources/Telemetry";
    private const string UPLOAD_ASSET_PATH = UPLOAD_ASSET_DIRECTORY + "/CombatTelemetryUploadSettings.asset";

    /// <summary>열린 정식 씬의 초기화 오브젝트에 Recorder를 설치하고 공급자 참조를 연결한 뒤 씬을 저장한다.</summary>
    [MenuItem("BlindBlade/대숲 방어/로그/D5 로컬 로그 연결")]
    public static void InstallFromMenu()
    {
        Install();
    }

    /// <summary>편집 중인 BambooDefense에만 중복 없이 컴포넌트를 추가한다. 연결 결과를 CLI용 사전으로 반환한다.</summary>
    public static Dictionary<string, object> Install()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Play를 종료한 뒤 로그를 연결하세요.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Defense/BambooDefense.unity")
            throw new InvalidOperationException("BambooDefense 씬을 연 뒤 로그를 연결하세요.");
        GameSceneInitializer initializer = UnityEngine.Object.FindFirstObjectByType<GameSceneInitializer>();
        PlayerHealth health = UnityEngine.Object.FindFirstObjectByType<PlayerHealth>();
        BambooDefenseDirector director = UnityEngine.Object.FindFirstObjectByType<BambooDefenseDirector>();
        DefenseEnemySpawner spawner = UnityEngine.Object.FindFirstObjectByType<DefenseEnemySpawner>();
        CombatThreatPresentationProvider presentation = UnityEngine.Object.FindFirstObjectByType<CombatThreatPresentationProvider>();
        if (initializer == null || health == null || director == null || spawner == null || presentation == null)
            throw new InvalidOperationException("정식 씬의 사건 공급자 연결이 누락되었습니다.");
        CombatTelemetryRecorder recorder = initializer.GetComponent<CombatTelemetryRecorder>();
        if (recorder == null) recorder = Undo.AddComponent<CombatTelemetryRecorder>(initializer.gameObject);
        SerializedObject serialized = new SerializedObject(recorder);
        serialized.FindProperty("_health").objectReferenceValue = health;
        serialized.FindProperty("_director").objectReferenceValue = director;
        serialized.FindProperty("_spawner").objectReferenceValue = spawner;
        serialized.FindProperty("_presentation").objectReferenceValue = presentation;
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        string logDirectory = System.IO.Path.Combine(Application.persistentDataPath, "CombatTelemetry");
        System.IO.Directory.CreateDirectory(logDirectory);
        return new Dictionary<string, object> { ["scene"] = scene.path, ["recorder"] = recorder.gameObject.name,
            ["logDirectory"] = logDirectory,
            ["health"] = health.name, ["director"] = director.name, ["saved"] = !scene.isDirty };
    }

    /// <summary>현재 PC의 로컬 전송 설정을 빌드 기본 에셋으로 복사한다. 토큰은 Console에 출력하지 않는다.</summary>
    [MenuItem("BlindBlade/대숲 방어/로그/현재 로컬 설정을 빌드 기본값으로 저장")]
    public static void ImportLocalUploadDefaultsFromMenu()
    {
        ImportLocalUploadDefaults();
    }

    /// <summary>현재 persistentDataPath 설정의 URL·token·활성 여부를 Resources 에셋에 저장하고 비밀 값 없는 결과를 반환한다.</summary>
    public static Dictionary<string, object> ImportLocalUploadDefaults()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Play를 종료한 뒤 빌드 기본 설정을 저장하세요.");
        string configPath = Path.Combine(Application.persistentDataPath, "CombatTelemetry", "upload-config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("복사할 로컬 upload-config.json이 없습니다.");
        JObject configuration = JObject.Parse(File.ReadAllText(configPath));
        bool enabled = configuration.Value<bool?>("enabled") == true;
        string endpoint = configuration.Value<string>("endpoint");
        string token = configuration.Value<string>("token");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps
            || (token?.Length ?? 0) < 32)
            throw new InvalidDataException("로컬 설정의 HTTPS endpoint와 32자 이상 token을 확인하세요.");
        if (!AssetDatabase.IsValidFolder(UPLOAD_ASSET_DIRECTORY)) AssetDatabase.CreateFolder("Assets/Resources", "Telemetry");
        CombatTelemetryUploadSettings settings = AssetDatabase.LoadAssetAtPath<CombatTelemetryUploadSettings>(UPLOAD_ASSET_PATH);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<CombatTelemetryUploadSettings>();
            AssetDatabase.CreateAsset(settings, UPLOAD_ASSET_PATH);
        }
        Undo.RecordObject(settings, "Save telemetry upload defaults");
        SerializedObject serialized = new SerializedObject(settings);
        serialized.FindProperty("_enabled").boolValue = enabled;
        serialized.FindProperty("_endpoint").stringValue = endpoint;
        serialized.FindProperty("_uploadToken").stringValue = token;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return new Dictionary<string, object> { ["asset"] = UPLOAD_ASSET_PATH, ["enabled"] = enabled,
            ["endpointConfigured"] = true, ["tokenConfigured"] = true, ["saved"] = true };
    }
}
