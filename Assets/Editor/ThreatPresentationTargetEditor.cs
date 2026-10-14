using UnityEngine;

using UnityEditor;

using BlindBlade.Perception;

[CustomEditor(typeof(ThreatPresentationTarget))]
public sealed class ThreatPresentationTargetEditor : Editor
{
    /// <summary>
    /// Inspector 입력을 편집하고 선택한 대상의 현재 거리·설정 출처·월드/UI 결과를 읽기 전용으로 표시한다.
    /// 편집 모드에서는 연결된 Resolver로 미리 계산하며 Play에서는 실제 LateUpdate 결과를 읽는다.
    /// </summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        ThreatPresentationTarget targetComponent = (ThreatPresentationTarget)target;
        if (!Application.isPlaying && targetComponent.Resolver != null)
            targetComponent.Resolver.RefreshTarget(targetComponent);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("현재 표시 결과 · 임시 계약 v1", EditorStyles.boldLabel);
        if (targetComponent.Resolver == null)
        {
            EditorGUILayout.HelpBox("씬 인스턴스의 Resolver를 연결하세요.", MessageType.Info);
            return;
        }
        ThreatPresentationSnapshot snapshot = targetComponent.Snapshot;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("대상 ID", snapshot.TargetId);
            EditorGUILayout.IntField("생성 세대", snapshot.SpawnGeneration);
            EditorGUILayout.FloatField("수평 거리 (m)", snapshot.HorizontalDistance);
            EditorGUILayout.FloatField("적용 인지 거리 (m)", snapshot.EnemyRadius);
            EditorGUILayout.EnumPopup("거리 설정 출처", snapshot.RadiusSource);
            EditorGUILayout.Toggle("표시 자격", snapshot.IsEligible);
            EditorGUILayout.Toggle("실제 화면 교차", snapshot.IntersectsCamera);
            EditorGUILayout.Toggle("전환 여유 적용 화면 안", snapshot.IsInCamera);
            EditorGUILayout.Toggle("월드 표시 요청", snapshot.ShowWorld);
            EditorGUILayout.Toggle("UI 표시 요청", snapshot.ShowUi);
            EditorGUILayout.FloatField("방향 각도 (도)", snapshot.DirectionDegrees);
            EditorGUILayout.ColorField("표시 색상", snapshot.Color);
            EditorGUILayout.FloatField("표시 투명도", snapshot.Alpha);
        }
        EditorGUILayout.HelpBox("Play의 연결된 게임 카메라는 이 결과로 윤곽선 색상·숨김을 적용합니다. Scene View는 원본 외형을 유지하고 HUD 연결은 별도입니다.", MessageType.Info);
    }

    /// <summary>입력 없이 Play 중인지 반환해 실행 중 Inspector 진단을 지속 갱신한다.</summary>
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    /// <summary>
    /// 선택 대상의 루트 위치와 관측자를 읽어 인지 반경·거리 선·현재 표시 요청을 Scene View에 그린다.
    /// Gizmo만 표시하며 실제 Renderer와 원본 머티리얼은 변경하지 않는다.
    /// </summary>
    void OnSceneGUI()
    {
        ThreatPresentationTarget targetComponent = (ThreatPresentationTarget)target;
        ThreatPresentationResolver resolver = targetComponent.Resolver;
        if (resolver == null || resolver.Observer == null)
            return;
        if (!Application.isPlaying)
            resolver.RefreshTarget(targetComponent);
        ThreatPresentationSnapshot snapshot = targetComponent.Snapshot;
        Handles.color = snapshot.Color;
        Handles.DrawWireDisc(resolver.Observer.position, Vector3.up, snapshot.EnemyRadius);
        Handles.DrawDottedLine(resolver.Observer.position, targetComponent.transform.position, 4f);
        Handles.Label(targetComponent.transform.position + Vector3.up * 2f,
            $"{snapshot.HorizontalDistance:F2}m / {snapshot.EnemyRadius:F2}m ({snapshot.RadiusSource})\n"
            + $"World={snapshot.ShowWorld} UI={snapshot.ShowUi} Alpha={snapshot.Alpha:F2}");
    }
}
