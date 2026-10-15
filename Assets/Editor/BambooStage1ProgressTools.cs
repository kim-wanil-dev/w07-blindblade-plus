using UnityEngine;

using UnityEditor;

/// <summary>에디터 Tools 메뉴에서 로컬 대숲 스테이지 진행 정보를 초기화한다.</summary>
public static class BambooStage1ProgressTools
{
    /// <summary>현재 에디터 플레이어 설정에 저장된 스테이지 1 보스 지점을 지운다.</summary>
    [MenuItem("Tools/Blind Blade/Reset Stage Progress")]
    private static void ResetStageProgress()
    {
        BambooStage1Progress.Clear();
        Debug.Log("대숲 스테이지 1 보스 재도전 저장 정보를 초기화했습니다. 다음 씬 로드부터 적용됩니다.");
    }
}
