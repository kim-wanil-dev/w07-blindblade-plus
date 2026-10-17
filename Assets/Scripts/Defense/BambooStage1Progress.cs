using UnityEngine;

/// <summary>대숲 스테이지 1 진입과 보스 재도전 지점을 로컬 플레이어 설정에 저장한다.</summary>
public static class BambooStage1Progress
{
    private const string STAGE1_STARTED_KEY = "BlindBlade.BambooStage1.Stage1Started";
    private const string BOSS_CHECKPOINT_KEY = "BlindBlade.BambooStage1.BossCheckpoint";
    private const string RAINY_STAGE_KEY = "BlindBlade.RainyStage.Reached";
    private const string RAINY_BOSS_CHECKPOINT_KEY = "BlindBlade.RainyStage.BossCheckpoint";

    public static bool BossCheckpointReached => PlayerPrefs.GetInt(BOSS_CHECKPOINT_KEY, 0) == 1;
    public static bool Stage1Started => PlayerPrefs.GetInt(STAGE1_STARTED_KEY, 0) == 1;
    public static bool RainyStageReached => PlayerPrefs.GetInt(RAINY_STAGE_KEY, 0) == 1;
    public static bool RainyBossCheckpointReached => PlayerPrefs.GetInt(RAINY_BOSS_CHECKPOINT_KEY, 0) == 1;
    public static bool HasContinueData => Stage1Started || BossCheckpointReached || RainyStageReached;

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

    /// <summary>1막 보스 격파를 저장해 이어 하기에서 RainyStage를 열도록 한다.</summary>
    public static void MarkRainyStageReached()
    {
        if (RainyStageReached) return;
        PlayerPrefs.SetInt(RAINY_STAGE_KEY, 1);
        PlayerPrefs.Save();
    }

    /// <summary>2막 보스 등장을 저장해 RainyStage 재도전과 이어 하기에서 일반 웨이브를 건너뛴다.</summary>
    public static void MarkRainyBossCheckpointReached()
    {
        if (RainyBossCheckpointReached) return;
        PlayerPrefs.SetInt(RAINY_BOSS_CHECKPOINT_KEY, 1);
        PlayerPrefs.Save();
    }

    /// <summary>저장된 스테이지 1 보스 재도전 지점을 지우고 다음 로드를 일반 웨이브로 되돌린다.</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(STAGE1_STARTED_KEY);
        PlayerPrefs.DeleteKey(BOSS_CHECKPOINT_KEY);
        PlayerPrefs.DeleteKey(RAINY_STAGE_KEY);
        PlayerPrefs.DeleteKey(RAINY_BOSS_CHECKPOINT_KEY);
        PlayerPrefs.Save();
    }
}
