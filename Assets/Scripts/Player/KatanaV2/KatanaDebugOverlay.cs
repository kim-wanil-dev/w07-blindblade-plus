using System.Collections.Generic;
using System.Text;

using UnityEngine;

using BlindBlade.Combat.Contracts;


/// <summary>기존 플레이어 로그를 보완하는 카타나 상태 표시다. 게임 판정은 변경하지 않는다.</summary>
[DisallowMultipleComponent]
public sealed class KatanaDebugOverlay : MonoBehaviour
{
    private const float HIT_AFTER_DODGE_LOG_RANGE = 1f;

    [Header("Debug switches")]
    [SerializeField] private bool _showOverlay = true;
    [SerializeField] private bool _logEvents = true;

    [Header("전투 성공 피드백")]
    [SerializeField] private bool _showCombatFeedback = true;
    [SerializeField, Min(0.1f)] private float _feedbackDuration = 0.8f;
    private GUIStyle _feedbackStyle;
    private string _feedbackNotice;
    private string _lastDefense = "아직 방어 판정 없음";
    private bool _lastWasParry;
    private float _feedbackUntil;

    [Header("판정 시간 기록")]
    private int _lastGuardInputId;
    private float _lastGuardStartTime;
    private int _lastDodgeId;
    private float _lastDodgeStartTime;
    private bool _dodgeWindowActive;
    private int _perfectDodgeCount;
    private int _normalDodgeCount;

    private readonly Queue<string> _recent = new Queue<string>();
    private readonly StringBuilder _text = new StringBuilder(600);
    private KatanaCombatCoordinator _combat;
    private KatanaWeaponVisual _visual;
    private KatanaDamageReceiver _receiver;
    private string _lastResult = "No hit yet";

    void Awake()
    {
        _combat = GetComponent<KatanaCombatCoordinator>();
        _visual = GetComponent<KatanaWeaponVisual>();
        _receiver = GetComponent<KatanaDamageReceiver>();
    }

    void OnEnable()
    {
        _combat.EventRaised += HandleEvent;
        _combat.Player.ActionEvent += HandlePlayer;
        _receiver.DamageReceived += HandleDamage;
        _combat.DefenseSucceeded += HandleDefense;
        _combat.Events.InputResolved += HandleInputResolved;
        _combat.Events.DefenseResolved += HandleDefenseResolved;
        _combat.Events.DodgeChanged += HandleDodgeChanged;
        _combat.Events.RevealPulseRequested += HandleRevealPulse;
        _combat.Events.StaminaChanged += HandleStaminaChanged;
        _combat.Events.EmpowerChanged += HandleEmpowerChanged;
    }

    void OnDisable()
    {
        _combat.EventRaised -= HandleEvent;
        _combat.Player.ActionEvent -= HandlePlayer;
        _receiver.DamageReceived -= HandleDamage;
        _combat.DefenseSucceeded -= HandleDefense;
        _combat.Events.InputResolved -= HandleInputResolved;
        _combat.Events.DefenseResolved -= HandleDefenseResolved;
        _combat.Events.DodgeChanged -= HandleDodgeChanged;
        _combat.Events.RevealPulseRequested -= HandleRevealPulse;
        _combat.Events.StaminaChanged -= HandleStaminaChanged;
        _combat.Events.EmpowerChanged -= HandleEmpowerChanged;
    }

    void OnGUI()
    {
        // 모든 씬에서 가드·패링·반격 안내 텍스트 표시를 비활성화한다.
        // if (_showCombatFeedback) DrawCombatFeedback();
        if (!_showOverlay) return;
        _text.Clear();
        _text.AppendLine("MELEE | LMB attack / RMB guard + timed parry / Ctrl + C crouch / Esc cursor");
        _text.Append("Action: ").Append(_combat.Player.CurrentState)
            .Append("  Recovery: ").AppendLine(_combat.Player.RecoveryRemaining.ToString("0.00"));
        _text.Append("Weapon: ").Append(_combat.WeaponState).Append("  Attack: ").AppendLine(_combat.IsCounterAttack ? "패링 반격" : _combat.Strike + "타");
        _text.Append("Defense: ").Append(_lastDefense).Append(" | timing window: ").Append(_combat.IsParryWindowOpen ? "open " : "closed ")
            .Append(_combat.ParryRemaining.ToString("0.00")).Append("  Crouch: ")
            .AppendLine(_combat.Player.IsCrouching ? "on" : "off");
        _text.Append("Animator: ").AppendLine(_visual.AnimatorState);
        _text.AppendLine(_lastResult);
        foreach (string line in _recent) _text.AppendLine(line);
        // GUI.Box(new Rect(12f, 12f, 575f, 265f), GUIContent.none);
        // GUI.Label(new Rect(24f, 22f, 550f, 245f), _text.ToString());
    }

    /// <summary>실제 방어 성공 result의 종류를 기록하고 짧은 성공 알림을 예약한다. 게임 판정은 변경하지 않는다.</summary>
    private void HandleDefense(KatanaDefenseResult result)
    {
        _lastWasParry = result.IsParry;
        _lastDefense = result.IsParry ? "패링 성공" : "가드 성공";
        _feedbackNotice = _lastDefense;
        _feedbackUntil = Time.time + _feedbackDuration;
    }

    /// <summary>
    /// 입력 record가 거부됐으면 종류와 거부 이유를 기록한다. 우클릭이 가드를 실제로 시작했으면 그 InputId와 게임 시간을 저장한다.
    /// 누르고 있는 우클릭의 재진입도 새 패링 창을 열므로 같은 방식으로 갱신한다. 게임 판정은 변경하지 않는다.
    /// </summary>
    private void HandleInputResolved(PlayerInputRecord record)
    {
        if (record.Status == PlayerInputStatus.Rejected)
            RecordCombat("입력 거부 " + record.Kind + " | " + record.RejectReason);
        if (record.Status != PlayerInputStatus.Started
            || (record.Kind != PlayerInputKind.GuardPress && record.Kind != PlayerInputKind.GuardHoldResume)) return;
        _lastGuardInputId = record.InputId;
        _lastGuardStartTime = record.GameTime;
    }

    /// <summary>
    /// 방어 판정 resolution의 결과와 퍼펙트·일반 회피 또는 반격 무적 구분, 피해, 가드 시작 또는 회피 시작 뒤 지난 시간을 기록한다.
    /// 회피 판정 구간 중의 회피·무적 결과이면 _perfectDodgeCount 또는 _normalDodgeCount를 올린다. 회피 구간 밖 피격도 마지막 회피 시작 뒤 1초 안이면 지난 시간을 남긴다.
    /// </summary>
    private void HandleDefenseResolved(DefenseResolution resolution)
    {
        string timing = "";
        bool dodged = IsDodgeContact(resolution);
        if (resolution.GuardInputId != 0 && resolution.GuardInputId == _lastGuardInputId)
            timing = " | 가드 시작 후 " + (resolution.GameTime - _lastGuardStartTime).ToString("0.000") + "초";
        else if (dodged)
        {
            timing = " | 회피 시작 후 " + (resolution.GameTime - _lastDodgeStartTime).ToString("0.000") + "초";
            if (resolution.Outcome == DefenseOutcome.DodgeAvoided) _perfectDodgeCount++;
            else _normalDodgeCount++;
        }
        else if (resolution.Outcome == DefenseOutcome.Hit && _lastDodgeId != 0
            && resolution.GameTime - _lastDodgeStartTime < HIT_AFTER_DODGE_LOG_RANGE)
            timing = " | 회피 #" + _lastDodgeId + " 시작 후 " + (resolution.GameTime - _lastDodgeStartTime).ToString("0.000") + "초(구간 밖)";
        RecordCombat("판정 " + resolution.Outcome + GetOutcomeLabel(resolution.Outcome, dodged) + " | 피해 "
            + resolution.AppliedDamage.ToString("0.#") + timing);
    }

    /// <summary>
    /// resolution이 체력 손실 없는 회피 판정 구간에 난 DodgeAvoided 또는 Invulnerable인지 반환한다.
    /// 불릿 타임 중이나 이미 피격된 회피의 무적 접촉은 회피 성공 횟수에 포함하지 않는다.
    /// </summary>
    private bool IsDodgeContact(DefenseResolution resolution)
    {
        return _dodgeWindowActive && _receiver.IsDodgeAvoiding
            && (resolution.Outcome == DefenseOutcome.DodgeAvoided
            || resolution.Outcome == DefenseOutcome.Invulnerable);
    }

    /// <summary>
    /// outcome과 회피 구간 접촉 여부 dodged로 읽기 쉬운 구분을 반환한다.
    /// DodgeAvoided는 퍼펙트 회피, 회피 구간의 Invulnerable은 일반 회피, 그 밖의 Invulnerable은 보상 없는 무적이며 그 외에는 빈 문자열이다.
    /// </summary>
    private static string GetOutcomeLabel(DefenseOutcome outcome, bool dodged)
    {
        if (outcome == DefenseOutcome.DodgeAvoided) return "(퍼펙트 회피)";
        if (outcome != DefenseOutcome.Invulnerable) return "";
        return dodged ? "(일반 회피)" : "(무적)";
    }

    /// <summary>
    /// 회피 사건 dodgeEvent를 기록한다. Started이면 회피 ID와 시작 게임 시간을 저장하고 회피 횟수를 초기화하며 _dodgeWindowActive를 켠다.
    /// Ended이면 _dodgeWindowActive를 끄고 그 회피에서 피한 퍼펙트·일반 회피 횟수를 함께 남긴다.
    /// </summary>
    private void HandleDodgeChanged(PlayerDodgeEvent dodgeEvent)
    {
        string summary = "";
        if (dodgeEvent.Stage == PlayerDodgeStage.Started)
        {
            _lastDodgeId = dodgeEvent.DodgeId;
            _lastDodgeStartTime = dodgeEvent.GameTime;
            _dodgeWindowActive = true;
            _perfectDodgeCount = 0;
            _normalDodgeCount = 0;
        }
        else if (dodgeEvent.Stage == PlayerDodgeStage.Ended && dodgeEvent.DodgeId == _lastDodgeId)
        {
            _dodgeWindowActive = false;
            summary = _perfectDodgeCount + _normalDodgeCount == 0 ? " | 피한 공격 없음"
                : " | 퍼펙트 회피 " + _perfectDodgeCount + " · 일반 회피 " + _normalDodgeCount;
        }
        RecordCombat("회피 " + dodgeEvent.Stage + " #" + dodgeEvent.DodgeId + summary);
    }

    /// <summary>파동 요청 pulseEvent의 원인과 요청 ID를 기록한다.</summary>
    private void HandleRevealPulse(PlayerRevealPulseEvent pulseEvent)
    {
        RecordCombat("파동 요청 " + pulseEvent.Cause + " #" + pulseEvent.PulseId);
    }

    /// <summary>스태미너 사건 staminaEvent의 이유와 변화량, 변경 후 값과 관련 회피 ID를 기록한다. 게임 판정은 변경하지 않는다.</summary>
    private void HandleStaminaChanged(PlayerStaminaEvent staminaEvent)
    {
        string dodge = staminaEvent.DodgeId != 0 ? " #" + staminaEvent.DodgeId : "";
        RecordCombat("스태미너 " + staminaEvent.Reason + " " + staminaEvent.Delta.ToString("+0.#;-0.#;0")
            + " → " + staminaEvent.Current.ToString("0.#") + "/" + staminaEvent.Max.ToString("0.#") + dodge);
    }

    /// <summary>강화 사건 empowerEvent의 단계와 강화 ID, 원인 공격, 끝난 이유를 기록한다. 게임 판정은 변경하지 않는다.</summary>
    private void HandleEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        string reason = empowerEvent.Stage == PlayerEmpowerStage.Ended ? " | " + empowerEvent.EndReason : "";
        RecordCombat("강화 " + empowerEvent.Stage + " #" + empowerEvent.EmpowerId + " | 원인 공격 "
            + empowerEvent.SourceAttackId + reason);
    }

    /// <summary>전투 판정 text를 최근 목록에 추가하고 설정에 따라 [Combat] 로그를 한 번 남긴다.</summary>
    private void RecordCombat(string text)
    {
        _recent.Enqueue(text);
        while (_recent.Count > 5) _recent.Dequeue();
        if (_logEvents) Debug.Log("[Combat] " + text, this);
    }

    /// <summary>현재 가드·실제 성공·반격 기회를 화면 아래 중앙에 표시한다. 기술 디버그 패널이 꺼져 있어도 설정에 따라 표시한다.</summary>
    private void DrawCombatFeedback()
    {
        string notice;
        Color color = Color.white;
        if (_combat.IsCounterAttack) { notice = "반격"; color = Color.cyan; }
        else if (_combat.CanCounterAttack)
        {
            notice = "패링 성공 · 좌클릭 반격  " + _combat.CounterRemaining.ToString("0.0") + "초";
            color = Color.cyan;
        }
        else if (Time.time < _feedbackUntil)
        {
            notice = _lastWasParry ? _feedbackNotice + " · 반격 기회 종료" : _feedbackNotice;
            color = _lastWasParry ? Color.cyan : Color.white;
        }
        else if (_combat.Player.IsGuarding) notice = _combat.IsParryWindowOpen ? "가드 · 패링 타이밍" : "가드 중";
        else return;
        if (_feedbackStyle == null)
            _feedbackStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
        Rect area = new Rect((Screen.width - 440f) * 0.5f, Screen.height * 0.68f, 440f, 44f);
        Color previous = GUI.color;
        GUI.Box(area, GUIContent.none);
        GUI.color = color;
        GUI.Label(area, notice, _feedbackStyle);
        GUI.color = previous;
    }

    /// <summary>카타나 주요 이벤트 e를 최근 목록에 추가하고 설정에 따라 한 번만 로그를 남긴다.</summary>
    private void HandleEvent(KatanaEvent e)
    {
        string text = e.Stage + " | strike=" + e.Strike + " | " + e.Detail;
        _recent.Enqueue(text);
        while (_recent.Count > 5) _recent.Dequeue();
        if (_logEvents) Debug.Log("[Katana] " + text, this);
    }

    /// <summary>기존 판정 이벤트 e의 결과만 표시한다. 기존 PlayerDebugOverlay가 전투 Console 로그를 담당한다.</summary>
    private void HandlePlayer(PlayerActionEvent e)
    {
        if (e.Stage == PlayerActionStage.HitConfirmed || e.Stage == PlayerActionStage.Missed || e.Stage == PlayerActionStage.DeathConfirmed)
            _lastResult = "Outgoing: " + e.Stage + " | action=" + e.ActionId;
    }

    /// <summary>차단되지 않은 context를 표시한다. 플레이어 체력은 이 디버그 컴포넌트에서 변경하지 않는다.</summary>
    private void HandleDamage(DamageContext context)
    {
        _lastResult = "Incoming damage forwarded: " + context.Amount + " | " + context.PatternId;
        if (_logEvents) Debug.Log("[Katana] " + _lastResult, this);
    }
}
