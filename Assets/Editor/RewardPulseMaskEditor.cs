using UnityEngine;

using UnityEditor;
using UnityEditor.SceneManagement;

using BlindBlade.Perception;

/// <summary>C 전용 씬에서 고정 파동 영역을 입력하고 실제 Resolver·공통 계약의 공개 결과를 관찰한다.</summary>
[CustomEditor(typeof(SensoryPerceptionMask))]
public sealed class RewardPulseMaskEditor : Editor
{
    private const string PREVIEW_SCENE = "Assets/Scenes/Perception/C_ThreatPresentation/C_ThreatPresentation.unity";
    private Vector3 _center;
    private float _radius = 16f;
    private float _visibility = 1f;

    /// <summary>선택한 마스크의 Inspector 설정과 C 전용 고정 영역 입력·대상별 공개 결과를 표시한다.</summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        if (EditorSceneManager.GetActiveScene().path != PREVIEW_SCENE)
            return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("C 보상 파동 연결 확인 · 고정 영역", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Play 후 Start C Contract Preview Session을 먼저 실행하세요. 이 입력은 영역 연결 확인용이며 파동 시간을 재생하지 않습니다.", MessageType.Info);
        _center = EditorGUILayout.Vector3Field("고정 중심", _center);
        _radius = Mathf.Max(0f, EditorGUILayout.FloatField("고정 반경 (m)", _radius));
        _visibility = EditorGUILayout.Slider("공개 가시성", _visibility, 0f, 1f);
        SensoryPerceptionMask mask = (SensoryPerceptionMask)target;
        SensoryManager sensory = mask.GetComponent<SensoryManager>();
        ThreatPresentationResolver resolver = mask.GetComponent<ThreatPresentationResolver>();
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !sensory.IsReady || resolver == null))
        {
            if (GUILayout.Button("고정 파동 영역 적용"))
            {
                mask.SetPulseRegion(_center, _radius, _visibility);
                resolver.ResolveNow();
            }
            if (GUILayout.Button("파동 영역 제거"))
            {
                mask.ClearPulseRegion();
                resolver.ResolveNow();
            }
        }
        if (resolver == null)
            return;
        using (new EditorGUI.DisabledScope(true))
        {
            foreach (ThreatPresentationSnapshot snapshot in resolver.Snapshots)
            {
                if (snapshot.Kind != ThreatTargetKind.Melee)
                    continue;
                EditorGUILayout.LabelField(GetTargetName(snapshot.TargetId), EditorStyles.boldLabel);
                EditorGUILayout.FloatField("파동만의 가시성", mask.GetPulseVisibility(snapshot.Position));
                EditorGUILayout.FloatField("최종 표시 투명도", snapshot.Alpha);
                EditorGUILayout.Toggle("표시 자격", snapshot.IsEligible);
                EditorGUILayout.Toggle("월드 표시", snapshot.ShowWorld);
                EditorGUILayout.Toggle("방향 UI 표시", snapshot.ShowUi);
            }
        }
    }

    /// <summary>targetId와 일치하는 씬의 C 대상을 찾아 오브젝트 이름을 반환하고 없으면 ID를 반환한다.</summary>
    private static string GetTargetName(int targetId)
    {
        foreach (ThreatPresentationTarget candidate in Object.FindObjectsByType<ThreatPresentationTarget>(FindObjectsSortMode.None))
            if (candidate.GetSourceState().TargetId == targetId)
                return candidate.name;
        return targetId.ToString();
    }

    /// <summary>Play 중 실제 공개 결과를 계속 읽도록 Inspector 갱신 여부를 반환한다.</summary>
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
