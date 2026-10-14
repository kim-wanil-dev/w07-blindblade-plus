using System;

using UnityEngine;

using UnityEditor;

using BlindBlade.Combat.Contracts;
using BlindBlade.Perception;

/// <summary>실제 후보의 원점·현재 적 위치·수명·D 전달 결과를 보여주며 C 전용 씬에서 수동 발걸음을 입력한다.</summary>
[CustomEditor(typeof(EnemyFootstepCandidates))]
public sealed class EnemyFootstepCandidatesEditor : Editor
{
    private ThreatPresentationTarget _previewEmitter;

    /// <summary>현재 후보와 D 전달 목록을 읽고 C 전용 Play 환경에서만 후보 발생·공통 시간 관찰 버튼을 제공한다.</summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EnemyFootstepCandidates candidates = (EnemyFootstepCandidates)target;
        CombatThreatPresentationProvider provider = candidates.GetComponent<CombatThreatPresentationProvider>();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("최신 발소리 후보 · 현재 적 위치 추적", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("C 보관 후보 수", candidates.CandidateCount);
            if (provider != null)
                EditorGUILayout.IntField("D 전달 발소리 수", provider.GetCurrentFootsteps().Count);
        }
        bool preview = Application.isPlaying && candidates.gameObject.scene.path
            == "Assets/Scenes/Perception/C_ThreatPresentation/C_ThreatPresentation.unity";
        if (preview)
            DrawPreviewControls(candidates);
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        double now = runtime != null && runtime.IsInitialized ? runtime.CaptureTimestamp().GameplaySeconds : 0d;
        foreach (FootstepPresentationSnapshot snapshot in candidates.GetCurrentFootsteps())
        {
            candidates.GetTiming(snapshot.Emitter, out float lifetime, out float fadeDuration);
            double age = Math.Max(0d, now - snapshot.OccurredAt.GameplaySeconds);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(candidates.GetEmitterName(snapshot.Emitter), EditorStyles.boldLabel);
                EditorGUILayout.TextField("발행자", snapshot.Emitter.EntityId);
                EditorGUILayout.IntField("생성 세대", snapshot.Emitter.SpawnGeneration);
                EditorGUILayout.LongField("걸음 순번", snapshot.EventIdentity.SourceSequence);
                EditorGUILayout.Vector3Field("발생 원점 · 고정", snapshot.Origin);
                EditorGUILayout.Vector3Field("표시 위치 · 현재 적", snapshot.Presentation.WorldPosition);
                EditorGUILayout.FloatField("이 후보 총수명", lifetime);
                EditorGUILayout.FloatField("이 후보 페이드", fadeDuration);
                EditorGUILayout.DoubleField("경과 게임 시간", age);
                EditorGUILayout.DoubleField("남은 게임 시간", Math.Max(0d, lifetime - age));
                EditorGUILayout.Toggle("청각 범위 안", snapshot.Presentation.IsEligible);
                EditorGUILayout.Toggle("적 월드 표시", snapshot.Presentation.WorldVisible);
                EditorGUILayout.Toggle("발소리 UI 표시", snapshot.Presentation.UiVisible);
                EditorGUILayout.Vector2Field("UI 방향", snapshot.Presentation.UiDirection);
                EditorGUILayout.ColorField("색상", snapshot.Presentation.Color);
                EditorGUILayout.FloatField("Alpha", snapshot.Presentation.Alpha);
                if (provider != null)
                    EditorGUILayout.Toggle("D 전달 값 일치", MatchesForwarded(provider, snapshot));
            }
        }
        EditorGUILayout.HelpBox("발걸음이 없으면 후보도 없습니다. 청각 범위·색상은 즉시 반영하고 수명·페이드는 새 걸음에 복사합니다. 후보 개수에는 별도 상한이 없습니다.", MessageType.Info);
    }

    /// <summary>candidates와 같은 Resolver의 미리보기 대상을 선택해 발걸음을 기록하고 공통 시간 제어를 호출한다.</summary>
    private void DrawPreviewControls(EnemyFootstepCandidates candidates)
    {
        if (_previewEmitter == null)
        {
            foreach (ThreatPresentationTarget candidate in UnityEngine.Object.FindObjectsByType<ThreatPresentationTarget>(FindObjectsSortMode.None))
                if (candidate.Resolver == candidates.Resolver && candidate.name == "Footstep_HearingEdge")
                    _previewEmitter = candidate;
        }
        _previewEmitter = (ThreatPresentationTarget)EditorGUILayout.ObjectField("발걸음 발생 대상", _previewEmitter,
            typeof(ThreatPresentationTarget), true);
        using (new EditorGUI.DisabledScope(_previewEmitter == null || _previewEmitter.Resolver != candidates.Resolver))
        {
            if (GUILayout.Button("선택 대상의 새 발걸음 발생"))
                candidates.RecordFootstep(_previewEmitter, _previewEmitter.transform.position);
            if (GUILayout.Button("같은 적의 발걸음 2회 발생"))
            {
                candidates.RecordFootstep(_previewEmitter, _previewEmitter.transform.position);
                candidates.RecordFootstep(_previewEmitter, _previewEmitter.transform.position);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("0.1배속"))
                ThreatPresentationAuthoring.SlowFootstepPreview();
            if (GUILayout.Button("기본 배속"))
                ThreatPresentationAuthoring.RestoreFootstepPreviewSpeed();
            if (GUILayout.Button("정지"))
                ThreatPresentationAuthoring.FreezeFootstepPreview();
            if (GUILayout.Button("재개"))
                ThreatPresentationAuthoring.ResumeFootstepPreview();
        }
    }

    /// <summary>source와 같은 발행자·걸음의 D 결과가 원점·시각·현재 표시 값을 보존했으면 true를 반환한다.</summary>
    private static bool MatchesForwarded(CombatThreatPresentationProvider provider, FootstepPresentationSnapshot source)
    {
        foreach (FootstepPresentationSnapshot forwarded in provider.GetCurrentFootsteps())
        {
            if (forwarded.Emitter.Equals(source.Emitter) && forwarded.EventIdentity.Equals(source.EventIdentity))
            {
                var left = source.Presentation;
                var right = forwarded.Presentation;
                return forwarded.Origin == source.Origin && forwarded.OccurredAt.Equals(source.OccurredAt)
                    && left.IsActive == right.IsActive && left.IsEligible == right.IsEligible
                    && left.IsInCamera == right.IsInCamera && left.WorldVisible == right.WorldVisible
                    && left.UiVisible == right.UiVisible && left.UiDirection == right.UiDirection
                    && left.WorldPosition == right.WorldPosition && left.Proximity == right.Proximity
                    && left.Color == right.Color && left.Alpha == right.Alpha;
            }
        }
        return false;
    }

    /// <summary>입력 없이 Play 상태를 반환해 짧은 후보 수명·표시 진단을 계속 갱신한다.</summary>
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
