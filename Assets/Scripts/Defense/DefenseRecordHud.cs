using UnityEngine;
using System;

using TMPro;

/// <summary>
/// 대숲 방어의 준비 카운트다운, 생존 시간과 처치 수를 화면 상단에 표시한다.
/// 목표 처치 수가 있는 스테이지는 생존 시간 대신 처치 수/목표 처치 수를 표시하며 기록을 숨기면 문구를 끈다.
/// 표시 전용이며 게임 진행 상태는 변경하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DefenseRecordHud : MonoBehaviour
{
    [Header("기록 연결")]
    [SerializeField] private BambooDefenseDirector _director;

    [SerializeField, Tooltip("HUD 프리팹의 Record. 글꼴·크기·색·배치는 해당 UI에서 조정합니다.")]
    private TextMeshProUGUI _text;
    private int _shownSeconds = -1;
    private int _shownKills = -1;
    private bool _shownStarted;

    void Update()
    {
        // 보스 등장 등으로 기록을 숨기면 문구를 끄고 갱신하지 않는다.
        _text.enabled = !_director.IsRecordHidden;
        if (_director.IsRecordHidden)
            return;
        bool started = _director.HasStarted;
        int seconds = started ? Mathf.FloorToInt(_director.SurvivalTime) : Mathf.CeilToInt(_director.Countdown);
        int kills = _director.Kills;
        if (started == _shownStarted && seconds == _shownSeconds && kills == _shownKills)
            return;
        _shownStarted = started;
        _shownSeconds = seconds;
        int goal = _director.KillGoal;
        _shownKills = Math.Clamp(kills, 0, goal);
        if (!started)
            _text.text = "준비 " + seconds;
        else
            _text.text = goal > 0 ? $"처치 {kills}/{goal}" : $"생존 {seconds / 60:00}:{seconds % 60:00}    처치 {kills}";
    }
}
