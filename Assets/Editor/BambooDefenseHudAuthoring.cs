using System;

using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using BlindBlade.Combat.Contracts;
using BlindBlade.UI.CombatHud;

/// <summary>방어 HUD 프리팹을 씬의 표시 담당자와 연결한다. 기존 UI 인스턴스와 편집값은 재생성하지 않는다.</summary>
public static class BambooDefenseHudAuthoring
{
    private const string PREFAB_PATH = "Assets/Scenes/Defense/UI/BambooDefenseHud.prefab";

    /// <summary>director 하위에 HUD 프리팹을 한 번 배치하고 기록 텍스트·방향 마커 참조를 연결한다.</summary>
    public static void Connect(BambooDefenseDirector director)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("HUD 연결은 편집 모드에서 수행하세요.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
            throw new InvalidOperationException("방어 HUD 프리팹이 없습니다: " + PREFAB_PATH);
        Transform existing = director.transform.Find(prefab.name);
        if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) != prefab)
            throw new InvalidOperationException("같은 이름의 다른 HUD가 있습니다. 기존 연결을 확인하세요.");
        GameObject root;
        if (existing != null)
            root = existing.gameObject;
        else
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, director.transform);
            Undo.RegisterCreatedObjectUndo(root, "방어 HUD 배치");
        }
        CombatHudReferences references = root.GetComponent<CombatHudReferences>();
        if (references == null)
            throw new InvalidOperationException("전투 HUD 설치 메뉴로 명시적 참조를 먼저 이관하세요.");
        TMPro.TextMeshProUGUI text = references.RecordText;
        DefenseDirectionMarker[] graphics = references.DirectionMarkers;
        if (text == null || graphics == null || graphics.Length == 0)
            throw new InvalidOperationException("HUD의 Record 텍스트와 방향 마커를 확인하세요.");
        DefenseRecordHud record = director.GetComponent<DefenseRecordHud>();
        if (record == null)
            record = Undo.AddComponent<DefenseRecordHud>(director.gameObject);
        ShurikenDirectionIndicator indicator = director.GetComponent<ShurikenDirectionIndicator>();
        if (indicator == null)
            indicator = Undo.AddComponent<ShurikenDirectionIndicator>(director.gameObject);
        SerializedObject recordSettings = new SerializedObject(record);
        recordSettings.FindProperty("_director").objectReferenceValue = director;
        recordSettings.FindProperty("_text").objectReferenceValue = text;
        recordSettings.ApplyModifiedProperties();
        SerializedObject indicatorSettings = new SerializedObject(indicator);
        indicatorSettings.FindProperty("_director").objectReferenceValue = director;
        SerializedProperty markers = indicatorSettings.FindProperty("_graphics");
        markers.arraySize = graphics.Length;
        for (int i = 0; i < graphics.Length; i++)
            markers.GetArrayElementAtIndex(i).objectReferenceValue = graphics[i];
        indicatorSettings.ApplyModifiedProperties();
        CombatThreatPresentationProvider provider = director.GetComponent<CombatThreatPresentationProvider>();
        if (provider == null)
            provider = Undo.AddComponent<CombatThreatPresentationProvider>(director.gameObject);
        PlayerHealth health = null;
        foreach (PlayerHealth candidate in UnityEngine.Object.FindObjectsByType<PlayerHealth>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene == director.gameObject.scene)
            {
                if (health != null)
                    throw new InvalidOperationException("씬의 PlayerHealth가 둘 이상입니다. HUD 원본을 확인하세요.");
                health = candidate;
            }
        }
        SerializedObject playerHudSettings = new SerializedObject(references.PlayerHud);
        playerHudSettings.FindProperty("_health").objectReferenceValue = health;
        if (health != null && playerHudSettings.FindProperty("_staminaSource").objectReferenceValue == null)
        {
            foreach (MonoBehaviour component in health.GetComponents<MonoBehaviour>())
            {
                if (component is IPlayerStaminaReadModel)
                {
                    playerHudSettings.FindProperty("_staminaSource").objectReferenceValue = component;
                    break;
                }
            }
        }
        playerHudSettings.ApplyModifiedProperties();
        SerializedObject footstepSettings = new SerializedObject(references.FootstepHud);
        footstepSettings.FindProperty("_provider").objectReferenceValue = provider;
        footstepSettings.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
    }
}
