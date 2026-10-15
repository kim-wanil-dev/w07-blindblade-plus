using System;
using System.Collections.Generic;

using UnityEngine;

using FMODUnity;

[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Sound/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Header("Events")]
    [SerializeField] private SoundEntry[] _sounds = Array.Empty<SoundEntry>();

    [Header("Mixer")]
    [SerializeField] private string _bgmBusPath = "bus:/BGM_Bus";
    [SerializeField] private string _effectBusPath = "bus:/SFX_Bus";
    [SerializeField, Tooltip("불릿 타임 먹먹함·느려짐을 받지 않는 연출음 버스. 효과음 음량 옵션을 함께 따른다.")]
    private string _cinematicBusPath = "bus:/Cinematic_Bus";

    [Header("전투 신호 청취")]
    [SerializeField, Min(0f)] private float _combatFocusDuration = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _combatFootstepVolume = 0.35f;

    internal string BgmBusPath => _bgmBusPath;
    internal string EffectBusPath => _effectBusPath;
    internal string CinematicBusPath => _cinematicBusPath;
    internal float CombatFocusDuration => _combatFocusDuration;
    internal float CombatFootstepVolume => _combatFootstepVolume;

    /// <summary>
    /// soundId의 공통 재생 사용 여부·음량·pitch 배율 및 호출부 Label 사용 여부를 조회한다.
    /// 이전 플레이어 피격 ID는 공통 항목으로 해석하며 삭제된 전조·발도 ID는 미등록 시 재생하지 않는다.
    /// </summary>
    internal void GetPlaybackSettings(SoundId soundId, out bool enabled, out float volume, out float pitch, out bool useCallerLabel)
    {
        soundId = ResolveId(soundId);
        enabled = soundId != SoundId.WarCry && soundId != SoundId.SwordUnsheath && soundId != SoundId.EnemyAttackPrepare;
        volume = 1f;
        pitch = 1f;
        useCallerLabel = true;
        foreach (SoundEntry entry in _sounds)
        {
            if (entry.Id != soundId) continue;
            enabled = entry.Enabled;
            volume = entry.VolumeMultiplier;
            pitch = entry.PitchMultiplier;
            useCallerLabel = entry.UseCallerLabel;
            return;
        }
    }

    /// <summary>
    /// soundId가 이전 플레이어 피격 ID이면 공통 피격 ID를 반환한다.
    /// 기존 호출부와 enum 값은 보존하고 다른 ID는 입력값 그대로 반환한다.
    /// </summary>
    private static SoundId ResolveId(SoundId soundId)
    {
        return soundId == SoundId.PlayerDamaged ? SoundId.SwordHit : soundId;
    }

    /// <summary>
    /// soundId의 개별 정지 요청에서 자연 종료를 허용하는 설정을 조회한다.
    /// 단계 전환과 취소 시 음원 끝을 유지할지 반환하며 소유자 전체 정지에는 적용하지 않는다.
    /// </summary>
    internal bool ShouldFinishNaturally(SoundId soundId)
    {
        soundId = ResolveId(soundId);
        foreach (SoundEntry entry in _sounds)
            if (entry.Id == soundId) return entry.FinishNaturally;
        return false;
    }

    /// <summary>
    /// soundId의 전투 신호와 2D 공간 보정 설정을 조회한다.
    /// 등록된 항목의 플래그를 반환 인자로 채우며 미등록 ID는 두 설정을 끈다.
    /// </summary>
    internal void GetCueSettings(SoundId soundId, out bool combatCue, out bool spatialize2D)
    {
        soundId = ResolveId(soundId);
        combatCue = false;
        spatialize2D = false;
        foreach (SoundEntry entry in _sounds)
        {
            if (entry.Id != soundId) continue;
            combatCue = entry.CombatCue;
            spatialize2D = entry.Spatialize2D;
            return;
        }
    }

    /// <summary>
    /// soundId에 연결된 FMOD 이벤트와 사운드 종류를 조회한다.
    /// 등록이 유효하면 eventReference와 soundType을 채우고 true를 반환하며, 누락 또는 중복이면 false를 반환한다.
    /// </summary>
    internal bool TryGetEvent(SoundId soundId, out EventReference eventReference, out SoundType soundType)
    {
        soundId = ResolveId(soundId);
        eventReference = default;
        soundType = SoundType.Effect;
        SoundEntry match = null;
        foreach (SoundEntry entry in _sounds)
        {
            if (entry.Id != soundId)
                continue;
            if (match != null)
            {
                Debug.LogError("[SoundManager] Duplicate SoundId: " + soundId, this);
                return false;
            }
            match = entry;
        }
        if (soundId == SoundId.None || match == null || match.Event.IsNull)
        {
            Debug.LogWarning("[SoundManager] No event registered for SoundId: " + soundId, this);
            return false;
        }
        eventReference = match.Event;
        soundType = match.Type;
        return true;
    }

    void OnValidate()
    {
        var ids = new HashSet<SoundId>();
        foreach (SoundEntry entry in _sounds)
        {
            if (entry.Id == SoundId.None || !ids.Add(entry.Id))
                Debug.LogWarning("[SoundManager] SoundId must not be None or duplicated: " + entry.Id, this);
        }
    }

    [Serializable]
    private class SoundEntry
    {
        [Header("Sound")]
        [SerializeField] private SoundId _id;
        [SerializeField] private SoundType _type = SoundType.Effect;
        [SerializeField] private EventReference _event;
        [SerializeField] private bool _enabled = true;
        [SerializeField, Min(0f)] private float _volumeMultiplier = 1f;
        [SerializeField, Range(0.25f, 4f)] private float _pitchMultiplier = 1f;
        [SerializeField, Tooltip("끄면 기존 호출부의 A/B/C Label을 전달하지 않고 FMOD 이벤트 자체의 랜덤 재생을 사용한다.")]
        private bool _useCallerLabel = true;
        [SerializeField] private bool _combatCue;
        [SerializeField, Tooltip("2D 이벤트에 청취자 기준 좌우 패닝과 거리 감쇠를 적용한다. 3D 이벤트는 FMOD 설정을 따른다.")]
        private bool _spatialize2D;
        [SerializeField, Tooltip("이 ID만 정지하는 요청을 무시하고 단발음을 끝까지 재생한다. 공격 취소에도 꼬리가 남으며 소유자 정리·전체 정지는 유지한다.")]
        private bool _finishNaturally;

        internal SoundId Id => _id;
        internal SoundType Type => _type;
        internal EventReference Event => _event;
        internal bool CombatCue => _combatCue;
        internal bool Spatialize2D => _spatialize2D;
        internal bool FinishNaturally => _finishNaturally;
        internal bool Enabled => _enabled;
        internal float VolumeMultiplier => Mathf.Max(0f, _volumeMultiplier);
        internal float PitchMultiplier => Mathf.Clamp(_pitchMultiplier, 0.25f, 4f);
        internal bool UseCallerLabel => _useCallerLabel;
    }
}
