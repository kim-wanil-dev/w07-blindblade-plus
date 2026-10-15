using UnityEngine;

/// <summary>대숲 스테이지 1의 보스 재도전 지점을 로컬 플레이어 설정에 저장한다.</summary>
public static class BambooStage1Progress
{
    private const string BOSS_CHECKPOINT_KEY = "BlindBlade.BambooStage1.BossCheckpoint";

    public static bool BossCheckpointReached => PlayerPrefs.GetInt(BOSS_CHECKPOINT_KEY, 0) == 1;

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
        PlayerPrefs.DeleteKey(BOSS_CHECKPOINT_KEY);
        PlayerPrefs.Save();
    }
}
