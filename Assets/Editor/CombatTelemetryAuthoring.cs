using System;
using System.Collections.Generic;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using BlindBlade.Telemetry;

/// <summary>열린 BambooDefense 씬에 읽기 전용 로그 소비자를 추가하고 기존 공급자만 연결한다.</summary>
public static class CombatTelemetryAuthoring
{
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
        string configPath = System.IO.Path.Combine(logDirectory, "upload-config.json");
        if (!System.IO.File.Exists(configPath))
            System.IO.File.WriteAllText(configPath, "{\n  \"enabled\": false,\n  \"endpoint\": \"\",\n  \"token\": \"\"\n}\n");
        return new Dictionary<string, object> { ["scene"] = scene.path, ["recorder"] = recorder.gameObject.name,
            ["logDirectory"] = logDirectory,
            ["health"] = health.name, ["director"] = director.name, ["saved"] = !scene.isDirty };
    }
}
