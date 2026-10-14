using UnityEngine;

using UnityEditor;

using BlindBlade.Perception;

using ContractSnapshot = BlindBlade.Combat.Contracts.ThreatPresentationSnapshot;

/// <summary>공통 계약으로 변환된 C 결과와 D 공급자에게 전달된 결과를 Inspector에서 보여준다.</summary>
[CustomEditor(typeof(ThreatPresentationContractAdapter))]
public sealed class ThreatPresentationContractAdapterEditor : Editor
{
    /// <summary>현재 어댑터의 연결·표시 목록과 같은 오브젝트의 D 전달 목록을 읽어 진단 결과를 표시한다.</summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        ThreatPresentationContractAdapter adapter = (ThreatPresentationContractAdapter)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("C → 공통 계약 → D 전달 결과", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("C 공통 위협 수", adapter.GetCurrentThreats().Count);
            CombatThreatPresentationProvider provider = adapter.GetComponent<CombatThreatPresentationProvider>();
            if (provider != null)
                EditorGUILayout.IntField("D 전달 위협 수", provider.GetCurrentThreats().Count);
            foreach (ContractSnapshot snapshot in adapter.GetCurrentThreats())
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(GetTargetName(adapter, snapshot), EditorStyles.boldLabel);
                EditorGUILayout.TextField("대상 키", snapshot.Target.EntityId);
                EditorGUILayout.IntField("생성 세대", snapshot.Target.SpawnGeneration);
                EditorGUILayout.TextField("세션 ID", snapshot.Session.SessionId);
                EditorGUILayout.LongField("갱신 순번", snapshot.Revision);
                EditorGUILayout.Toggle("표시 자격", snapshot.IsEligible);
                EditorGUILayout.Toggle("월드 표시", snapshot.WorldVisible);
                EditorGUILayout.Toggle("UI 표시", snapshot.UiVisible);
                EditorGUILayout.Vector2Field("UI 방향", snapshot.UiDirection);
                EditorGUILayout.Slider("HUD 근접도", snapshot.Proximity, 0f, 1f);
                EditorGUILayout.ColorField("색상", snapshot.Color);
                EditorGUILayout.FloatField("Alpha", snapshot.Alpha);
                if (provider != null)
                    EditorGUILayout.Toggle("D 전달 값 일치", MatchesForwarded(provider, snapshot));
            }
        }
        EditorGUILayout.HelpBox("C 전용 씬에서는 Play 후 Start C Contract Preview Session 메뉴로 세션을 시작합니다. 발소리는 기존 공급자 경로를 유지합니다.", MessageType.Info);
    }

    /// <summary>입력 없이 Play 상태를 반환해 실행 중 공통 계약 진단을 계속 갱신한다.</summary>
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    /// <summary>source의 세션·대상과 같은 D 결과를 찾아 재판정 없이 전달한 표시 값이 모두 같으면 true를 반환한다.</summary>
    private static bool MatchesForwarded(CombatThreatPresentationProvider provider, ContractSnapshot source)
    {
        foreach (ContractSnapshot forwarded in provider.GetCurrentThreats())
        {
            if (forwarded.Session.SessionId == source.Session.SessionId
                && forwarded.Session.Generation == source.Session.Generation
                && forwarded.Target.EntityId == source.Target.EntityId
                && forwarded.Target.SpawnGeneration == source.Target.SpawnGeneration)
            {
                return forwarded.IsActive == source.IsActive && forwarded.IsEligible == source.IsEligible
                    && forwarded.IsInCamera == source.IsInCamera && forwarded.WorldVisible == source.WorldVisible
                    && forwarded.UiVisible == source.UiVisible && forwarded.UiDirection == source.UiDirection
                    && forwarded.WorldPosition == source.WorldPosition && forwarded.Proximity == source.Proximity
                    && forwarded.Color == source.Color && forwarded.Alpha == source.Alpha;
            }
        }
        return false;
    }

    /// <summary>adapter와 같은 Resolver를 쓰는 대상의 공통 키를 snapshot과 비교해 진단에 표시할 이름을 반환한다.</summary>
    private static string GetTargetName(ThreatPresentationContractAdapter adapter, ContractSnapshot snapshot)
    {
        foreach (ThreatPresentationTarget candidate in Object.FindObjectsByType<ThreatPresentationTarget>(FindObjectsSortMode.None))
        {
            if (candidate.Resolver != adapter.Resolver)
                continue;
            ThreatPresentationSnapshot result = candidate.Snapshot;
            string entityId = string.IsNullOrWhiteSpace(result.EntityId)
                ? $"Perception/{result.Kind}/{result.TargetId}" : result.EntityId;
            if (entityId == snapshot.Target.EntityId && result.SpawnGeneration == snapshot.Target.SpawnGeneration)
                return candidate.name;
        }
        return snapshot.Target.EntityId;
    }
}
