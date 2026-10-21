using System;

using UnityEngine;

/// <summary>게임 진행 구간을 연결할 텔레메트리 실행 식별자와 최초 진입 방식을 보관한다.</summary>
public static class CombatTelemetryRunContext
{
    private const string RUN_ID_KEY = "combatTelemetryRunId";
    private const string ATTEMPT_PREFIX = "combatTelemetryAttempt:";

    public static string CurrentRunId { get; private set; }
    public static string EntryMode { get; private set; } = "fresh";

    /// <summary>새 게임의 runId를 만들고 저장한 뒤 entryMode를 설정해 반환한다.</summary>
    public static string StartNewRun(string entryMode = "fresh")
    {
        CurrentRunId = Guid.NewGuid().ToString("N");
        EntryMode = entryMode;
        PlayerPrefs.SetString(RUN_ID_KEY, CurrentRunId);
        return CurrentRunId;
    }

    /// <summary>저장된 진행의 runId를 불러오거나 없으면 만든 뒤 entryMode를 설정해 반환한다.</summary>
    public static string ResumeRun(string entryMode = "continue")
    {
        if (string.IsNullOrEmpty(CurrentRunId))
            CurrentRunId = PlayerPrefs.GetString(RUN_ID_KEY, string.Empty);
        if (string.IsNullOrEmpty(CurrentRunId))
            CurrentRunId = Guid.NewGuid().ToString("N");
        EntryMode = entryMode;
        PlayerPrefs.SetString(RUN_ID_KEY, CurrentRunId);
        return CurrentRunId;
    }

    /// <summary>게임 씬에서 호출해 runId를 보장하고 현재 값을 반환한다.</summary>
    public static string EnsureRunId()
    {
        return string.IsNullOrEmpty(CurrentRunId) ? ResumeRun() : CurrentRunId;
    }

    /// <summary>
    /// Opens a distinct stage attempt for runId and stageId.
    /// retryIndex starts at zero and increases for each repeated attempt; previousAttemptSessionId identifies the prior attempt.
    /// </summary>
    public static string BeginStageAttempt(string runId, string stageId, out int retryIndex,
        out string previousAttemptSessionId)
    {
        string key = ATTEMPT_PREFIX + runId + ":" + stageId;
        retryIndex = PlayerPrefs.GetInt(key + ":retryIndex", -1) + 1;
        previousAttemptSessionId = PlayerPrefs.GetString(key + ":attemptSessionId", string.Empty);
        string attemptSessionId = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetInt(key + ":retryIndex", retryIndex);
        PlayerPrefs.SetString(key + ":attemptSessionId", attemptSessionId);
        PlayerPrefs.Save();
        return attemptSessionId;
    }
}
