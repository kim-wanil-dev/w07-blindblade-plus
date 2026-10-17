using UnityEngine;

/// <summary>대숲 스테이지 1 진입과 보스 재도전 지점을 로컬 플레이어 설정에 저장한다.</summary>
public static class BambooStage1Progress
{
    private const string STAGE1_STARTED_KEY = "BlindBlade.BambooStage1.Stage1Started";
    private const string BOSS_CHECKPOINT_KEY = "BlindBlade.BambooStage1.BossCheckpoint";

    public static bool BossCheckpointReached => PlayerPrefs.GetInt(BOSS_CHECKPOINT_KEY, 0) == 1;
    public static bool Stage1Started => PlayerPrefs.GetInt(STAGE1_STARTED_KEY, 0) == 1;
    public static bool HasContinueData => Stage1Started || BossCheckpointReached;

    /// <summary>1-1 진행을 저장해 타이틀의 이어 하기를 활성화한다.</summary>
    public static void MarkStage1Started()
    {
        if (Stage1Started) return;
        PlayerPrefs.SetInt(STAGE1_STARTED_KEY, 1);
        PlayerPrefs.Save();
    }

    /// <summary>보스전 시작을 기록해 다음 스테이지 1 로드에서 보스 재도전을 허용한다.</summary>
    public static void MarkBossCheckpointReached()
    {
        if (BossCheckpointReached) return;
        PlayerPrefs.SetInt(BOSS_CHECKPOINT_KEY, 1);
        PlayerPrefs.Save();
    }

    /// <summary>저장된 스테이지 1 보스 재도전 지점을 지우고 다음 로드를 일반 웨이브로 되돌린다.</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(STAGE1_STARTED_KEY);
        PlayerPrefs.DeleteKey(BOSS_CHECKPOINT_KEY);
        PlayerPrefs.Save();
    }
}
