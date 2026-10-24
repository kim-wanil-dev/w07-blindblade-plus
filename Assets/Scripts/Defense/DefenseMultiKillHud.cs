using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;

/// <summary>일반 웨이브 처치 문맥을 공격 실행별로 집계하고 처치 목표 아래 두 줄에 최신 결과를 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class DefenseMultiKillHud : MonoBehaviour
{
    [Serializable]
    private struct TierStyle
    {
        [SerializeField, Min(1), InspectorName("등급 시작 처치 수")]
        private int _minimumKills;
        [SerializeField, Min(0.1f), InspectorName("기본 크기 배율")]
        private float _sizeScale;
        [SerializeField, InspectorName("등급 색상")]
        private Color _color;

        public int MinimumKills => _minimumKills;
        public float SizeScale => _sizeScale;
        public Color Color => _color;

        /// <summary>처치 기준, 크기 배율과 글자 색상을 Inspector 기본 등급 값으로 저장한다.</summary>
        public TierStyle(int minimumKills, float sizeScale, Color color)
        {
            _minimumKills = minimumKills;
            _sizeScale = sizeScale;
            _color = color;
        }
    }

    private sealed class AttackState
    {
        public int KillCount { get; set; }
        public float LastKillAt { get; set; }
        public bool IsSwordWave { get; set; }
        public bool IsFinished { get; set; }
        public bool HasDisplayed { get; set; }
        public DisplayEntry Entry { get; set; }

    }

    private sealed class DisplayEntry
    {
        public AttackState State { get; }
        public float FirstShownAt { get; }
        public float PopStartedAt { get; set; }
        public float LastRenderedAt { get; set; }
        public int LastRenderedFrame { get; set; }
        public int RenderedTier { get; set; }
        public int RenderedKillCount { get; set; }
        public bool PendingRender { get; set; }

        /// <summary>state를 표시 목록에 넣고 첫 등장·갱신 시각을 저장한다.</summary>
        public DisplayEntry(AttackState state, float shownAt, int tier)
        {
            State = state;
            FirstShownAt = shownAt;
            PopStartedAt = shownAt;
            LastRenderedAt = shownAt;
            RenderedTier = tier;
            RenderedKillCount = 0;
        }
    }

    [SerializeField, HideInInspector]
    private BambooDefenseDirector _director;
    [SerializeField, HideInInspector]
    private TextMeshProUGUI _latestText;
    [SerializeField, HideInInspector]
    private TextMeshProUGUI _previousText;

    [Header("등급")]
    [SerializeField, Tooltip("5·10·20·30명 등급의 시작 수, 크기와 색상을 위에서부터 설정합니다.")]
    private TierStyle[] _tiers =
    {
        new TierStyle(5, 1f, new Color(0.96f, 0.93f, 0.82f, 1f)),
        new TierStyle(10, 1.15f, new Color(1f, 0.84f, 0.48f, 1f)),
        new TierStyle(20, 1.3f, new Color(1f, 0.69f, 0.2f, 1f)),
        new TierStyle(30, 1.5f, new Color(1f, 0.95f, 0.72f, 1f))
    };

    [Header("표시 시간")]
    [SerializeField, Min(0f), InspectorName("일반 등급 유지 시간 (초)")]
    private float _entryLifetime = 1.5f;
    [SerializeField, Min(0f), InspectorName("큰 성공 유지 시간 (초)")]
    private float _largeEntryLifetime = 2f;
    [SerializeField, Min(1), InspectorName("큰 성공 기준 처치 수")]
    private int _largeEntryMinimumKills = 30;
    [SerializeField, Min(0f), InspectorName("퇴장 시간 (초)")]
    private float _fadeDuration = 0.35f;
    [SerializeField, Min(0f), InspectorName("최소 가독 시간 (초)")]
    private float _minimumReadableDuration = 0.8f;
    [SerializeField, Min(0f), InspectorName("숫자 갱신 간격 (초)")]
    private float _countUpdateInterval = 0.08f;
    [SerializeField, Min(1f), InspectorName("실행 집계 보존 시간 (초)")]
    private float _attackStateRetention = 30f;

    [Header("표시 크기와 배치")]
    [SerializeField, Min(1f), InspectorName("기본 글자 크기")]
    private float _baseFontSize = 32f;
    [SerializeField, Range(0f, 1f), InspectorName("직전 결과 크기 배율")]
    private float _previousSizeScale = 0.75f;
    [SerializeField, Range(0f, 1f), InspectorName("직전 결과 불투명도")]
    private float _previousAlpha = 0.5f;
    [SerializeField, Range(0f, 1f), InspectorName("등장·등급 상승 확대량")]
    private float _popScale = 0.2f;
    [SerializeField, Min(0f), InspectorName("확대 정착 시간 (초)")]
    private float _popDuration = 0.15f;
    [SerializeField, Min(0f), InspectorName("퇴장 하강 거리 (px)")]
    private float _fadeOffset = 8f;
    [SerializeField, InspectorName("최신 줄 Y 위치")]
    private float _latestY = -62f;
    [SerializeField, InspectorName("직전 줄 Y 위치")]
    private float _previousY = -106f;

    private readonly Dictionary<int, AttackState> _attackStates = new Dictionary<int, AttackState>();
    private readonly HashSet<int> _finishedAttackIds = new HashSet<int>();
    private readonly List<int> _expiredAttackIds = new List<int>();
    private DisplayEntry _latestEntry;
    private DisplayEntry _previousEntry;
    private float _elapsed;

    void OnEnable()
    {
        ClearDisplay();
        _director.EnemyKilledWithContext += HandleEnemyKilledWithContext;
        MultiKillAttackRuntime.AttackFinished += HandleAttackFinished;
    }

    void OnDisable()
    {
        _director.EnemyKilledWithContext -= HandleEnemyKilledWithContext;
        MultiKillAttackRuntime.AttackFinished -= HandleAttackFinished;
        ClearDisplay();
    }

    void Update()
    {
        if (Game.IsInitialized && Game.Instance.GameStateManager.IsPaused)
            return;

        _elapsed += Time.unscaledDeltaTime;
        UpdateEntry(_latestEntry, true);
        UpdateEntry(_previousEntry, false);
        PruneAttackStates();
    }

    /// <summary>처치 문맥의 공격 ID와 패턴을 확인하고 실행별 처치 수 및 표시 상태를 갱신한다.</summary>
    private void HandleEnemyKilledWithContext(DamageContext context)
    {
        if (_director.IsRecordHidden || context.AttackId == 0 || !IsEligiblePattern(context.PatternId)
            || _finishedAttackIds.Contains(context.AttackId))
            return;

        if (!_attackStates.TryGetValue(context.AttackId, out AttackState state))
        {
            state = new AttackState();
            _attackStates.Add(context.AttackId, state);
        }

        state.KillCount++;
        state.LastKillAt = _elapsed;
        state.IsSwordWave |= IsSwordWavePattern(context.PatternId);
        if (!state.HasDisplayed)
        {
            if (state.KillCount >= GetMinimumDisplayCount())
                ShowEntry(state);
            return;
        }

        RefreshEntry(state);
    }

    /// <summary>일반 공격, IaidoFinisher, 일섬, 거합의 피해 패턴인지 반환한다.</summary>
    private static bool IsEligiblePattern(string patternId)
    {
        return patternId == nameof(PlayerAttackKind.Normal)
            || patternId == nameof(PlayerAttackKind.IaidoFinisher)
            || IsSwordWavePattern(patternId)
            || patternId == "EmpowerStrike";
    }

    /// <summary>돌진 또는 비행 검기 패턴이면 true를 반환한다.</summary>
    private static bool IsSwordWavePattern(string patternId)
    {
        return patternId == "PlayerSwordWaveDash" || patternId == "DefenseSwordWave";
    }

    /// <summary>공격 실행의 지연 처치가 끝났음을 기록해 늦은 이벤트가 새 결과를 만들지 못하게 한다.</summary>
    private void HandleAttackFinished(int attackId)
    {
        if (!_attackStates.TryGetValue(attackId, out AttackState state))
            return;
        state.IsFinished = true;
        _finishedAttackIds.Add(attackId);
    }

    /// <summary>state를 최신 칸에 추가하고 기존 최신 결과를 직전 칸으로 내린다.</summary>
    private void ShowEntry(AttackState state)
    {
        if (_latestEntry != null)
        {
            if (_previousEntry != null)
                RemoveEntry(_previousEntry);
            _previousEntry = _latestEntry;
        }
        _latestEntry = new DisplayEntry(state, _elapsed, GetTierIndex(state.KillCount));
        state.Entry = _latestEntry;
        state.HasDisplayed = true;
        RenderEntry(_latestEntry, true);
        ApplyEntryVisual(_latestEntry, true);
        if (_previousEntry != null)
            ApplyEntryVisual(_previousEntry, false);
    }

    /// <summary>state의 기존 표시 숫자와 등급을 갱신하되 이미 퇴장한 결과는 다시 띄우지 않는다.</summary>
    private void RefreshEntry(AttackState state)
    {
        DisplayEntry entry = state.Entry;
        if (entry == null)
            return;

        int tier = GetTierIndex(state.KillCount);
        bool sameFrame = Time.frameCount == entry.LastRenderedFrame;
        bool intervalPassed = _elapsed - entry.LastRenderedAt >= Mathf.Max(0f, _countUpdateInterval);
        if (sameFrame || intervalPassed)
            RenderEntry(entry, tier != entry.RenderedTier);
        else
            entry.PendingRender = true;

        ApplyEntryVisual(entry, entry == _latestEntry);
    }

    /// <summary>state의 최신 누적 수와 최종 등급을 TMP 슬롯에 반영하고 등급 변경 시 확대 연출을 시작한다.</summary>
    private void RenderEntry(DisplayEntry entry, bool animateTierChange)
    {
        int tier = GetTierIndex(entry.State.KillCount);
        entry.PendingRender = false;
        entry.RenderedTier = tier;
        entry.RenderedKillCount = entry.State.KillCount;
        entry.LastRenderedAt = _elapsed;
        entry.LastRenderedFrame = Time.frameCount;
        if (animateTierChange)
            entry.PopStartedAt = _elapsed;

        TextMeshProUGUI text = GetText(entry);
        text.text = $"<size=70%>+</size>{entry.RenderedKillCount}";
        text.fontSize = _baseFontSize;
    }

    /// <summary>entry의 보류 숫자를 갱신하고 현재 수명·확대·퇴장 상태를 해당 TMP 줄에 그린다.</summary>
    private void UpdateEntry(DisplayEntry entry, bool latest)
    {
        if (entry == null)
            return;
        if (entry.PendingRender && _elapsed - entry.LastRenderedAt >= Mathf.Max(0f, _countUpdateInterval))
        {
            int tier = GetTierIndex(entry.State.KillCount);
            RenderEntry(entry, tier != entry.RenderedTier);
        }
        ApplyEntryVisual(entry, latest);
    }

    /// <summary>entry의 위치, 등급 크기·색상, 직전 줄 강조와 현재 퇴장 진행을 TMP에 적용한다.</summary>
    private void ApplyEntryVisual(DisplayEntry entry, bool latest)
    {
        TextMeshProUGUI text = GetText(entry);
        if (!text.gameObject.activeSelf)
            text.gameObject.SetActive(true);
        string renderedText = $"<size=70%>+</size>{entry.RenderedKillCount}";
        if (text.text != renderedText)
            text.text = renderedText;
        text.fontSize = _baseFontSize;

        float keepUntil = Mathf.Max(entry.State.LastKillAt + GetLifetime(entry.State.KillCount),
            entry.FirstShownAt + Mathf.Max(0f, _minimumReadableDuration));
        float fadeProgress = _fadeDuration <= 0f
            ? (_elapsed >= keepUntil ? 1f : 0f)
            : Mathf.Clamp01((_elapsed - keepUntil) / _fadeDuration);
        if (fadeProgress >= 1f)
        {
            RemoveEntry(entry);
            return;
        }

        TierStyle style = GetTierStyle(entry.RenderedTier);
        float popProgress = _popDuration <= 0f
            ? 1f
            : Mathf.Clamp01((_elapsed - entry.PopStartedAt) / _popDuration);
        float popScale = 1f + _popScale * (1f - Mathf.SmoothStep(0f, 1f, popProgress));
        float rowScale = latest ? 1f : _previousSizeScale;
        float rowAlpha = latest ? 1f : _previousAlpha;
        float y = latest ? _latestY : _previousY;
        text.rectTransform.anchoredPosition = new Vector2(0f, y - _fadeOffset * fadeProgress);
        text.rectTransform.localScale = Vector3.one * style.SizeScale * rowScale * popScale;
        Color color = style.Color;
        color.a *= rowAlpha * (1f - fadeProgress);
        text.color = color;
    }

    /// <summary>entry와 연결된 TMP 줄의 퇴장을 마치고 공격 상태의 표시 연결을 비운다.</summary>
    private void RemoveEntry(DisplayEntry entry)
    {
        if (entry == null)
            return;
        if (_latestEntry == entry)
        {
            _latestEntry = null;
            HideText(_latestText);
        }
        else if (_previousEntry == entry)
        {
            _previousEntry = null;
            HideText(_previousText);
        }
        entry.State.Entry = null;
    }

    /// <summary>entry가 배치된 최신 또는 직전 줄의 TMP 컴포넌트를 반환한다.</summary>
    private TextMeshProUGUI GetText(DisplayEntry entry)
    {
        return entry == _latestEntry ? _latestText : _previousText;
    }

    /// <summary>text의 숫자를 비우고 슬롯 오브젝트를 숨긴다.</summary>
    private static void HideText(TextMeshProUGUI text)
    {
        text.text = string.Empty;
        text.gameObject.SetActive(false);
    }

    /// <summary>각 실행의 표시 여부와 지연 일섬 종료 상태를 보존하고 오래된 집계만 정리한다.</summary>
    private void PruneAttackStates()
    {
        _expiredAttackIds.Clear();
        foreach (KeyValuePair<int, AttackState> pair in _attackStates)
        {
            AttackState state = pair.Value;
            if (state.IsSwordWave && !state.IsFinished)
                continue;
            if (_elapsed - state.LastKillAt >= _attackStateRetention)
                _expiredAttackIds.Add(pair.Key);
        }

        foreach (int attackId in _expiredAttackIds)
            _attackStates.Remove(attackId);
    }

    /// <summary>모든 실행별 표시 상태를 비우고 두 TMP 슬롯을 숨긴다.</summary>
    private void ClearDisplay()
    {
        foreach (AttackState state in _attackStates.Values)
            state.Entry = null;
        _attackStates.Clear();
        _finishedAttackIds.Clear();
        _expiredAttackIds.Clear();
        _latestEntry = null;
        _previousEntry = null;
        _elapsed = 0f;
        HideText(_latestText);
        HideText(_previousText);
    }

    /// <summary>Inspector 등급 기준 중 가장 낮은 처치 수를 표시 시작값으로 반환한다.</summary>
    private int GetMinimumDisplayCount()
    {
        if (_tiers == null || _tiers.Length == 0)
            return 5;
        int minimum = int.MaxValue;
        foreach (TierStyle tier in _tiers)
            minimum = Mathf.Min(minimum, Mathf.Max(1, tier.MinimumKills));
        return minimum;
    }

    /// <summary>kills에 도달한 가장 높은 등급의 인덱스를 반환하며 기준 미만은 첫 등급을 사용한다.</summary>
    private int GetTierIndex(int kills)
    {
        if (_tiers == null || _tiers.Length == 0)
            return 0;
        int selected = 0;
        int selectedThreshold = int.MinValue;
        for (int i = 0; i < _tiers.Length; i++)
        {
            int threshold = Mathf.Max(1, _tiers[i].MinimumKills);
            if (kills < threshold || threshold < selectedThreshold)
                continue;
            selected = i;
            selectedThreshold = threshold;
        }
        return selected;
    }

    /// <summary>tierIndex에 맞는 크기·색상을 반환하고 등급 배열이 비었으면 기본 스타일을 사용한다.</summary>
    private TierStyle GetTierStyle(int tierIndex)
    {
        if (_tiers == null || _tiers.Length == 0)
            return new TierStyle(5, 1f, Color.white);
        return _tiers[Mathf.Clamp(tierIndex, 0, _tiers.Length - 1)];
    }

    /// <summary>kills가 큰 성공 기준에 도달하면 큰 성공 유지 시간, 아니면 일반 유지 시간을 반환한다.</summary>
    private float GetLifetime(int kills)
    {
        return kills >= _largeEntryMinimumKills
            ? Mathf.Max(_entryLifetime, _largeEntryLifetime)
            : _entryLifetime;
    }
}
