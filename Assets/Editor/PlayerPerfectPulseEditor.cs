using UnityEngine;

using UnityEditor;

using BlindBlade.Combat.Contracts;

/// <summary>A의 실제 파동 재생 상태와 발동 시 저장한 설정을 Inspector에 표시한다.</summary>
[CustomEditor(typeof(PlayerPerfectPulse))]
public sealed class PlayerPerfectPulseEditor : Editor
{
    private CombatSessionRuntime _previewRuntime;
    private CombatTimeToken _previewTimeToken;

    void OnDisable()
    {
        RestorePreviewSpeed();
    }

    /// <summary>Inspector 설정·진행 반경·페이드 시간·가시성을 읽고 Play 중 극한 회피 표현만 재생할 수 있게 한다.</summary>
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("패링 페이드는 실제 시간, 극한 회피 페이드는 게임 시간입니다. 반경·시간·링 색상 변경은 새 파동부터 적용합니다.", MessageType.Info);
        if (!Application.isPlaying)
            return;
        PlayerPerfectPulse pulse = (PlayerPerfectPulse)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("현재 파동 · 발동 시 저장한 설정", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("파동 재생 중", pulse.IsRunning);
            EditorGUILayout.Toggle("패링 파동", pulse.IsPerfectRunning);
            EditorGUILayout.Vector3Field("고정 중심", pulse.Center);
            EditorGUILayout.FloatField("최대 반경 (m)", pulse.ActiveRadius);
            EditorGUILayout.FloatField("확산 시간 (실제 초)", pulse.ActiveExpandDuration);
            EditorGUILayout.FloatField("페이드 시간", pulse.ActiveFadeDuration);
            EditorGUILayout.FloatField("확산 경과 (실제 초)", pulse.ExpansionElapsed);
            EditorGUILayout.FloatField(pulse.IsPerfectRunning ? "페이드 경과 (실제 초)" : "페이드 경과 (게임 초)", pulse.FadeElapsed);
            EditorGUILayout.FloatField("현재 반경 (m)", pulse.CurrentRadius);
            EditorGUILayout.FloatField("현재 공개 가시성", pulse.CurrentVisibility);
        }
        EditorGUILayout.HelpBox("아래 버튼은 파동 표현만 재생합니다. 실제 회피 판정·스태미너 환불·불릿타임은 발생시키지 않습니다. 패링 파동 중에는 사용할 수 없습니다.", MessageType.Info);
        using (new EditorGUI.DisabledScope(pulse.IsPerfectRunning || Time.timeScale <= 0f))
            if (GUILayout.Button("극한 회피 파동 표현 재생"))
                pulse.BeginPerfectDodge();
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        bool canObserve = runtime != null && runtime.IsInitialized
            && runtime.gameObject.scene == pulse.gameObject.scene;
        using (new EditorGUI.DisabledScope(!canObserve))
        {
            if (GUILayout.Button("0.1배속으로 시간 차이 관찰"))
            {
                RestorePreviewSpeed();
                _previewRuntime = runtime;
                _previewTimeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session,
                    "C/RewardPulsePreview", CombatTimeReason.DashTelegraph, 0.1f));
            }
            if (GUILayout.Button("관찰 배속 요청 해제"))
                RestorePreviewSpeed();
        }
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.FloatField("현재 세계 배속", Time.timeScale);
        EditorGUILayout.HelpBox("0.1배속에서는 회피 확산은 실제 약 1.2초, 이후 페이드는 실제 약 6초입니다. Player 선택을 바꾸거나 Inspector를 닫으면 이 관찰 요청만 해제합니다.", MessageType.Info);
    }

    /// <summary>이 Inspector가 등록한 D 시간 요청만 해제하고 저장한 런타임·토큰을 비운다.</summary>
    private void RestorePreviewSpeed()
    {
        if (_previewRuntime != null)
            _previewRuntime.Release(_previewTimeToken);
        _previewRuntime = null;
        _previewTimeToken = default;
    }

    /// <summary>Play 중 실제 파동의 시간과 가시성을 관찰하도록 Inspector 갱신 여부를 반환한다.</summary>
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
