using System.Collections.Generic;

using UnityEngine;

using FMODUnity;

/// <summary>
/// 게임 코드의 SoundId 재생 요청을 FMOD에 전달하는 공통 사운드 서비스다.
/// 단발 효과음과 BGM의 수명 및 음량을 내부에서 관리하여 호출부에 FMOD 타입을 노출하지 않는다.
/// </summary>
public class SoundManager
{
    [Header("Configuration")]
    private readonly SoundLibrary _library;
    private readonly CombatSoundProfile _combatProfile;
    private GameObject _footstepOnlyOwner;

    [Header("Effects")]
    private readonly List<ActiveEffect> _effects = new List<ActiveEffect>();
    private float _combatFocusUntil;
    private readonly List<PendingCue> _pendingCues = new List<PendingCue>();
    private bool _processingCues;
#if UNITY_EDITOR
    private readonly List<string> _recentPlayback = new List<string>();
    public IReadOnlyList<string> RecentPlayback => _recentPlayback;
#endif

    [Header("BGM")]
    private FMOD.Studio.EventInstance _bgm;
    private SoundId _bgmId = SoundId.None;
    private bool _bgmReleaseRequested;
    private GameObject _bgmOwner;
    private bool _hasBgmOwner;
    private bool _loopBgm;
    private float _bgmPlaybackVolume = 1f;

    [Header("Mixer")]
    private FMOD.Studio.Bus _bgmBus;
    private FMOD.Studio.Bus _effectBus;
    private FMOD.Studio.Bus _cinematicBus;
    private bool _busesReady;
    private float _bgmVolume = 1f;
    private float _effectVolume = 1f;

    [Header("Time Warp")]
    private readonly FMOD.DSP[] _warpFilters = new FMOD.DSP[3];
    private readonly bool[] _hasWarpFilters = new bool[3];

    /// <summary>
    /// library의 사운드 ID와 FMOD 이벤트 연결을 보관한다.
    /// FMOD 재생 시스템은 실제 재생 요청 시 초기화하며, 매니저의 설정 상태를 준비한다.
    /// </summary>
    internal SoundManager(SoundLibrary library)
    {
        _library = library;
        _combatProfile = Resources.Load<CombatSoundProfile>(CombatSoundProfile.RESOURCE_PATH);
    }

    /// <summary>
    /// BGM Bus의 공통 음량 배율을 0~1 범위로 설정하거나 조회한다.
    /// 개별 음원 크기는 FMOD Studio에서 정하며 이 값은 옵션 음량에 사용한다.
    /// </summary>
    public float BgmVolume
    {
        get => _bgmVolume;
        set
        {
            _bgmVolume = Mathf.Clamp01(value);
            if (RuntimeManager.IsInitialized && TryInitializeBuses())
                _bgmBus.setVolume(_bgmVolume);
        }
    }

    /// <summary>
    /// 효과음 Bus와 연출음 Bus의 공통 음량 배율을 0~1 범위로 설정하거나 조회한다.
    /// 이미 재생 중인 효과음과 이후 재생할 효과음에 함께 적용한다.
    /// </summary>
    public float EffectVolume
    {
        get => _effectVolume;
        set
        {
            _effectVolume = Mathf.Clamp01(value);
            if (RuntimeManager.IsInitialized && TryInitializeBuses())
            {
                _effectBus.setVolume(_effectVolume);
                if (_cinematicBus.isValid())
                    _cinematicBus.setVolume(_effectVolume);
            }
        }
    }

    /// <summary>owner 지정 시 플레이어 전투음·WarCry·이동 중 재생 예외를 유지한다. 다른 효과음을 정지하고 null이면 제한과 BGM 음소거를 해제한다.</summary>
    public void SetFootstepOnlyOwner(GameObject owner)
    {
        if (_footstepOnlyOwner == owner)
            return;
        _footstepOnlyOwner = owner;
        if (owner != null)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                if (CanPlayDuringMovement(_effects[i].Id, _effects[i].Owner, _effects[i].PlayDuringMovement))
                    continue;
                if (RuntimeManager.IsInitialized)
                    StopEffect(_effects[i].Instance, false);
                _effects.RemoveAt(i);
            }
        }
        if (_bgm.isValid() && RuntimeManager.IsInitialized)
            _bgm.setVolume(owner != null ? 0f : _bgmPlaybackVolume);
    }

    /// <summary>
    /// soundId의 단발 효과음을 position에서 volume·pitch 배율로 재생한다.
    /// owner로 수명을 연결하고 playDuringMovement로 이동 예외를 지정한다. 공통 피격은 프로필 값으로 재생·예약하며 성공 시 true를 반환한다.
    /// </summary>
    public bool Play(SoundId soundId, Vector3 position = default, float volume = 1f, GameObject owner = null, bool playDuringMovement = false, float pitch = 1f)
    {
        if (_combatProfile != null && (soundId == SoundId.SwordHit || soundId == SoundId.PlayerDamaged))
            return PlayCue(CombatSoundCue.Damage, position, owner, false);
        return PlayEffect(soundId, position, owner, false, volume, playDuringMovement, pitch: pitch);
    }

    /// <summary>
    /// soundId의 단발 효과음을 source에 연결하여 volume·pitch 배율로 재생한다.
    /// source의 위치를 추적하며 공통 피격은 프로필 값으로 재생·예약한다. 성공 시 true를 반환한다.
    /// </summary>
    public bool PlayAttached(SoundId soundId, GameObject source, float volume = 1f, float pitch = 1f)
    {
        if (source == null)
        {
            Debug.LogWarning("[SoundManager] PlayAttached requires a source GameObject.");
            return false;
        }
        if (_combatProfile != null && (soundId == SoundId.SwordHit || soundId == SoundId.PlayerDamaged))
            return PlayCue(CombatSoundCue.Damage, source.transform.position, source, true);
        return PlayEffect(soundId, source.transform.position, source, true, volume, pitch: pitch);
    }

    /// <summary>
    /// soundId의 반복 효과음을 source에 연결하여 volume 배율로 재생한다.
    /// 루프 구간이 있는 이벤트도 허용하며 StopFor로 정지하거나 source가 비활성·파괴되면 정지한다. 성공 시 true를 반환한다.
    /// </summary>
    public bool PlayAttachedLoop(SoundId soundId, GameObject source, float volume = 1f)
    {
        if (source == null)
        {
            Debug.LogWarning("[SoundManager] PlayAttachedLoop requires a source GameObject.");
            return false;
        }
        return PlayEffect(soundId, source.transform.position, source, true, volume, allowLooping: true);
    }

    /// <summary>
    /// soundId의 단발음을 source에 연결하고 parameterName의 Label을 재생 전에 지정한다.
    /// label과 volume·pitch를 FMOD에 전달하며 이벤트 설정과 재생이 성공하면 true를 반환한다.
    /// </summary>
    public bool PlayAttachedWithLabel(SoundId soundId, GameObject source, string parameterName, string label, float volume = 1f, float pitch = 1f)
    {
        if (source == null || string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(label))
        {
            Debug.LogWarning("[SoundManager] A source, parameter name and label are required.");
            return false;
        }
        return PlayEffect(soundId, source.transform.position, source, true, volume, parameterName: parameterName, label: label, pitch: pitch);
    }

    /// <summary>
    /// entry의 중앙 설정을 source 또는 position에 적용해 효과음을 재생한다.
    /// 직접 이벤트·음량과 entry.Pitch에 pitch 배율을 적용하며 재생 성공 여부를 반환한다.
    /// </summary>
    public bool PlayCombatCue(CombatSoundEntry entry, GameObject source, Vector3 position, bool attach, float pitch = 1f)
    {
        if (!entry.Enabled || (attach && source == null)) return false;
        return PlayEffect(entry.SoundId, attach ? source.transform.position : position, source, attach, entry.Volume,
            pitch: entry.Pitch * pitch,
            eventOverride: entry.EventOverride.IsNull ? null : entry.EventOverride, cueName: entry.Cue.ToString());
    }

    /// <summary>
    /// cue·position·owner·attach로 등록된 상황의 중앙 사운드 설정을 재생하거나 예약한다.
    /// 프로필 값에 pitch 배율을 곱하며 지연 예약에도 같은 배율을 저장하고 재생 또는 예약 성공 여부를 반환한다.
    /// </summary>
    public bool PlayCue(CombatSoundCue cue, Vector3 position, GameObject owner = null, bool attach = false, float pitch = 1f)
    {
        if (_combatProfile == null) return false;
        bool accepted = false;
        foreach (CombatSoundEntry entry in _combatProfile.Entries)
        {
            if (entry.Cue != cue || !entry.Enabled) continue;
            if (entry.TimeOffset <= 0f) accepted |= PlayCombatCue(entry, owner, position, attach, pitch);
            else
            {
                _pendingCues.Add(new PendingCue(entry, owner, position, attach, Time.time + entry.TimeOffset, pitch));
                accepted = true;
            }
        }
        return accepted;
    }

    /// <summary>
    /// 게임 시간에 도달한 상황별 예약을 재생하고 비활성화된 소유자의 예약을 제거한다.
    /// 예약을 먼저 제거하고 재진입을 차단하여 매니저 Update 호출에서도 중복 재생하지 않는다.
    /// </summary>
    private void UpdatePendingCues()
    {
        if (_processingCues) return;
        _processingCues = true;
        try
        {
            for (int i = 0; i < _pendingCues.Count;)
            {
                PendingCue pending = _pendingCues[i];
                if (pending.HasOwner && (pending.Owner == null || !pending.Owner.activeInHierarchy))
                {
                    _pendingCues.RemoveAt(i);
                    continue;
                }
                if (Time.time < pending.DueTime) { i++; continue; }
                _pendingCues.RemoveAt(i);
                PlayCombatCue(pending.Entry, pending.Owner, pending.Position, pending.Attach, pending.Pitch);
            }
        }
        finally { _processingCues = false; }
    }

    /// <summary>
    /// soundId의 2D BGM을 재생하고 다른 곡이면 이전 BGM을 즉시 정지한다.
    /// Library의 사용 여부·음량·pitch를 적용하고 같은 곡은 owner·loop·volume을 갱신한다. 재생 또는 갱신 성공 여부를 반환한다.
    /// </summary>
    public bool PlayBgm(SoundId soundId, GameObject owner = null, bool loop = false, float volume = 1f)
    {
        if (!ReferenceEquals(owner, null) && (owner == null || !owner.activeInHierarchy))
            return false;
        if (!TryResolveEvent(soundId, SoundType.BGM, out EventReference eventReference))
            return false;
        _library.GetPlaybackSettings(soundId, out bool enabled, out float volumeMultiplier, out float pitchMultiplier, out _);
        if (!enabled) return false;
        volume *= volumeMultiplier;
        if (_bgm.isValid() && _bgmId == soundId && _bgm.getPlaybackState(out var state) == FMOD.RESULT.OK && state != FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            if (state == FMOD.Studio.PLAYBACK_STATE.STOPPING)
                return false;
            _bgmOwner = owner;
            _hasBgmOwner = owner != null;
            _loopBgm = loop;
            _bgmPlaybackVolume = Mathf.Clamp01(volume);
            _bgm.setVolume(_footstepOnlyOwner != null ? 0f : _bgmPlaybackVolume);
            _bgm.setPitch(pitchMultiplier);
            return true;
        }
        try
        {
            var description = RuntimeManager.GetEventDescription(eventReference);
            if (description.is3D(out bool is3D) != FMOD.RESULT.OK || is3D)
            {
                Debug.LogWarning("[SoundManager] BGM requires a 2D event: " + soundId);
                return false;
            }
            StopBgm(false);
            _bgm = RuntimeManager.CreateInstance(eventReference);
            _bgmId = soundId;
            _bgmReleaseRequested = false;
            _bgmOwner = owner;
            _hasBgmOwner = owner != null;
            _loopBgm = loop;
            _bgmPlaybackVolume = Mathf.Clamp01(volume);
            _bgm.setVolume(_footstepOnlyOwner != null ? 0f : _bgmPlaybackVolume);
            _bgm.setPitch(pitchMultiplier);
            FMOD.RESULT result = _bgm.start();
            if (result == FMOD.RESULT.OK)
                return true;
            Debug.LogError("[SoundManager] Failed to start BGM: " + soundId + ", " + result);
            StopBgm(false);
        }
        catch (EventNotFoundException exception)
        {
            Debug.LogWarning("[SoundManager] BGM event not found in loaded banks: " + exception.Message);
        }
        return false;
    }

    /// <summary>
    /// allowFadeOut에 따라 BGM을 페이드 또는 즉시 정지하고 해제를 요청한다.
    /// 페이드 중 핸들은 중복 재생 방지와 Clear의 즉시 정지를 위해 보관하며 FMOD가 종료 후 자원을 반환한다.
    /// </summary>
    public void StopBgm(bool allowFadeOut = true)
    {
        _loopBgm = false;
        if (_bgm.isValid() && RuntimeManager.IsInitialized)
        {
            _bgm.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
            if (!_bgmReleaseRequested)
            {
                _bgm.release();
                _bgmReleaseRequested = true;
            }
            if (allowFadeOut)
                return;
        }
        _bgm.clearHandle();
        _bgmId = SoundId.None;
        _bgmReleaseRequested = false;
        _bgmOwner = null;
        _hasBgmOwner = false;
    }

    /// <summary>현재 유효한 BGM의 소유자가 owner인지 반환한다. 씬 종료에서 다른 소유자의 음악을 덮어쓰지 않도록 사용한다.</summary>
    public bool IsBgmOwnedBy(GameObject owner)
    {
        return owner != null && _hasBgmOwner && _bgmOwner == owner && _bgm.isValid();
    }

    /// <summary>
    /// owner의 효과음과 BGM만 allowFadeOut 설정으로 정지한다.
    /// 오브젝트 비활성화 등의 정리에 사용하며, 다른 소유자의 재생은 유지하고 null 요청은 무시한다.
    /// </summary>
    public void StopFor(GameObject owner, bool allowFadeOut = false)
    {
        if (owner == null)
            return;
        _pendingCues.RemoveAll(pending => pending.HasOwner && pending.Owner == owner);
        foreach (ActiveEffect effect in _effects)
        {
            if (effect.HasOwner && effect.Owner == owner)
                StopEffect(effect.Instance, allowFadeOut);
        }
        if (_hasBgmOwner && _bgmOwner == owner)
            StopBgm(allowFadeOut);
    }

    /// <summary>
    /// owner가 재생한 soundId 효과음만 allowFadeOut 방식으로 정지한다.
    /// 같은 소유자의 해당 ID 예약을 제거하고 forceStop이면 자연 종료 설정을 무시하여 공격 취소 신호를 정리한다.
    /// </summary>
    public void StopFor(GameObject owner, SoundId soundId, bool allowFadeOut = false, bool forceStop = false)
    {
        if (owner == null) return;
        if (soundId == SoundId.SwordHit || soundId == SoundId.PlayerDamaged)
            _pendingCues.RemoveAll(pending => pending.HasOwner && pending.Owner == owner);
        else
            _pendingCues.RemoveAll(pending => pending.HasOwner && pending.Owner == owner && pending.Entry.SoundId == soundId);
        if (!forceStop && _library.ShouldFinishNaturally(soundId)) return;
        foreach (ActiveEffect effect in _effects)
            if (effect.HasOwner && effect.Owner == owner && effect.Id == soundId)
                StopEffect(effect.Instance, allowFadeOut);
    }

    /// <summary>
    /// 외부 입력 없이 이 매니저의 단발 효과음을 모두 즉시 정지한다.
    /// BGM은 유지하며 보관한 효과음 목록을 비운다.
    /// </summary>
    public void StopEffects()
    {
        _pendingCues.Clear();
        if (RuntimeManager.IsInitialized)
        {
            foreach (ActiveEffect effect in _effects)
                StopEffect(effect.Instance, false);
        }
        _effects.Clear();
        _combatFocusUntil = 0f;
    }

    /// <summary>
    /// type이 BGM이면 음악을 페이드 정지하고 Effect이면 효과음을 즉시 정지한다.
    /// 지정된 종류의 매니저 재생 상태를 정리한다.
    /// </summary>
    public void Stop(SoundType type = SoundType.Effect)
    {
        if (type == SoundType.BGM)
            StopBgm();
        else
            StopEffects();
    }

    /// <summary>
    /// 외부 입력 없이 보유한 BGM과 효과음을 즉시 정지하고 정리한다.
    /// Bank와 다른 컴포넌트의 인스턴스는 유지하며 다음 요청에서 Bus 연결을 다시 확인한다.
    /// </summary>
    public void Clear()
    {
        _footstepOnlyOwner = null;
        StopBgm(false);
        StopEffects();
        SetTimeWarp(1f, 0f);
        SetCinematicTimeWarp(1f, 0f);
        _busesReady = false;
    }

    /// <summary>
    /// soundId 이벤트의 샘플 데이터를 미리 메모리에 올려 재생할 때 불러오는 지연을 없앤다.
    /// 등록된 효과음 이벤트이고 요청이 성공하면 true를 반환한다.
    /// </summary>
    public bool PreloadSampleData(SoundId soundId)
    {
        if (!TryResolveEvent(soundId, SoundType.Effect, out EventReference eventReference))
            return false;
        try
        {
            return RuntimeManager.GetEventDescription(eventReference).loadSampleData() == FMOD.RESULT.OK;
        }
        catch (EventNotFoundException exception)
        {
            Debug.LogWarning("[SoundManager] Effect event not found in loaded banks: " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// 효과음·BGM 버스의 모든 소리를 pitch 배율로 재생해 음높이와 속도를 함께 바꾸고, lowpassCutoff(Hz) 위를 걸러 먹먹하게 한다.
    /// 연출음 버스는 영향을 받지 않는다. pitch 1과 lowpassCutoff 0 이하이면 원래 소리로 되돌리며 FMOD가 초기화되지 않았으면 아무것도 하지 않는다.
    /// </summary>
    public void SetTimeWarp(float pitch, float lowpassCutoff)
    {
        if (!RuntimeManager.IsInitialized || !TryInitializeBuses())
            return;
        ApplyTimeWarp(_effectBus, 0, pitch, lowpassCutoff);
        ApplyTimeWarp(_bgmBus, 1, pitch, lowpassCutoff);
    }

    /// <summary>
    /// 연출음 버스의 소리를 pitch 배율과 lowpassCutoff(Hz) 저역 통과로 바꾼다. 보스 반격 감속처럼 연출음도 먹먹해야 할 때만 쓴다.
    /// pitch 1과 lowpassCutoff 0 이하이면 원래 소리로 되돌리며 연출음 버스가 없거나 FMOD가 초기화되지 않았으면 아무것도 하지 않는다.
    /// </summary>
    public void SetCinematicTimeWarp(float pitch, float lowpassCutoff)
    {
        if (!RuntimeManager.IsInitialized || !TryInitializeBuses() || !_cinematicBus.isValid())
            return;
        ApplyTimeWarp(_cinematicBus, 2, pitch, lowpassCutoff);
    }

    /// <summary>
    /// bus 채널 그룹에 pitch와 index번째 저역 통과 필터의 lowpassCutoff(Hz)를 적용한다.
    /// 채널 그룹이 아직 만들어지지 않았으면 다음 호출에서 다시 시도하며, 필터는 처음 먹먹하게 할 때 한 번 만든다.
    /// </summary>
    private void ApplyTimeWarp(FMOD.Studio.Bus bus, int index, float pitch, float lowpassCutoff)
    {
        if (bus.getChannelGroup(out FMOD.ChannelGroup group) != FMOD.RESULT.OK)
            return;
        group.setPitch(Mathf.Clamp(pitch, 0.25f, 1f));
        bool muffled = lowpassCutoff > 0f;
        if (muffled && !_hasWarpFilters[index]
            && RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.MULTIBAND_EQ, out _warpFilters[index]) == FMOD.RESULT.OK)
        {
            _warpFilters[index].setParameterInt((int)FMOD.DSP_MULTIBAND_EQ.A_FILTER, (int)FMOD.DSP_MULTIBAND_EQ_FILTER_TYPE.LOWPASS_24DB);
            group.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, _warpFilters[index]);
            _hasWarpFilters[index] = true;
        }
        if (!_hasWarpFilters[index])
            return;
        if (muffled)
            _warpFilters[index].setParameterFloat((int)FMOD.DSP_MULTIBAND_EQ.A_FREQUENCY, Mathf.Clamp(lowpassCutoff, 20f, 22000f));
        _warpFilters[index].setBypass(!muffled);
    }

    /// <summary>
    /// 보유 재생을 확인하여 비활성화 또는 파괴된 소유자의 사운드를 즉시 정지한다.
    /// 자연 종료된 핸들을 제거하고 전투 신호에 따른 발소리 음량·2D 패닝과 BGM 수명을 갱신한다.
    /// </summary>
    internal void Update()
    {
        UpdatePendingCues();
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            ActiveEffect effect = _effects[i];
            if (effect.HasOwner && (effect.Owner == null || !effect.Owner.activeInHierarchy))
                StopEffect(effect.Instance, false);
            if (!RuntimeManager.IsInitialized || !effect.Instance.isValid())
            {
                _effects.RemoveAt(i);
                continue;
            }
            UpdateEffectMix(effect);
        }
        if (_hasBgmOwner && (_bgmOwner == null || !_bgmOwner.activeInHierarchy))
            StopBgm(false);
        else if (!_bgm.isValid() || !RuntimeManager.IsInitialized)
            StopBgm(false);
        else if (_bgm.getPlaybackState(out var state) == FMOD.RESULT.OK && state == FMOD.Studio.PLAYBACK_STATE.STOPPED)
        {
            if (!_loopBgm || _bgm.start() != FMOD.RESULT.OK) StopBgm(false);
        }
    }

    /// <summary>
    /// soundId의 단발 효과음을 position 또는 source에서 volume·pitch 배율로 재생한다.
    /// attach로 위치 추적과 playDuringMovement로 이동 예외를 선택하고 parameterName과 label을 시작 전에 적용한다.
    /// eventOverride와 cueName으로 중앙 설정의 음원 및 기록을 적용하며 재생 성공 여부를 반환한다.
    /// allowLooping이 거짓이면 단발 이벤트만 재생한다.
    /// </summary>
    private bool PlayEffect(SoundId soundId, Vector3 position, GameObject source, bool attach, float volume, bool playDuringMovement = false, string parameterName = null, string label = null, float pitch = 1f,
        EventReference? eventOverride = null, string cueName = null, bool allowLooping = false)
    {
        _library.GetPlaybackSettings(soundId, out bool enabled, out float volumeMultiplier, out float pitchMultiplier, out bool useCallerLabel);
        if (!enabled) return false;
        volume *= volumeMultiplier;
        pitch *= pitchMultiplier;
        if (!useCallerLabel && cueName == null) parameterName = null;
        EventReference eventReference;
        if (eventOverride.HasValue) eventReference = eventOverride.Value;
        else if (!TryResolveEvent(soundId, SoundType.Effect, out eventReference)) return false;
        _library.GetCueSettings(soundId, out bool combatCue, out bool spatialize2D);
        playDuringMovement |= combatCue;
        if (!CanPlayDuringMovement(soundId, source, playDuringMovement))
            return false;
        if (!ReferenceEquals(source, null) && (source == null || !source.activeInHierarchy))
        {
            Debug.LogWarning("[SoundManager] The sound owner must be active.");
            return false;
        }
        Update();
        try
        {
            var description = RuntimeManager.GetEventDescription(eventReference);
            if (description.isOneshot(out bool isOneShot) != FMOD.RESULT.OK || (!isOneShot && !allowLooping))
            {
                Debug.LogWarning("[SoundManager] Play and PlayAttached require a one-shot event: " + soundId);
                return false;
            }
            var instance = RuntimeManager.CreateInstance(eventReference);
            description.is3D(out bool is3D);

            bool dopplerEnabled = false;
            if (is3D && description.isDopplerEnabled(out bool dopplerenabled) == FMOD.RESULT.OK)
                dopplerEnabled = dopplerenabled;

            description.getMinMaxDistance(out float minimumDistance, out float maximumDistance);
            if (attach)
            {
                if (is3D && dopplerEnabled)
                    AttachInstanceWithVelocity(instance, source);
                else
                    RuntimeManager.AttachInstanceToGameObject(instance, source);
            }
            else
            {
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
            }
            FMOD.RESULT result = instance.setVolume(Mathf.Max(0f, volume));
            if (result == FMOD.RESULT.OK)
                result = instance.setPitch(Mathf.Clamp(pitch, 0.25f, 4f));
            if (result == FMOD.RESULT.OK && parameterName != null)
                result = instance.setParameterByNameWithLabel(parameterName, label);
            if (result == FMOD.RESULT.OK)
                result = instance.start();
            if (result != FMOD.RESULT.OK)
            {
                RuntimeManager.DetachInstanceFromGameObject(instance);
                instance.release();
                Debug.LogError("[SoundManager] Failed to start effect playback: " + soundId + ", " + result);
                return false;
            }
            instance.release();
            if (combatCue)
                _combatFocusUntil = Time.unscaledTime + _library.CombatFocusDuration;
            var effect = new ActiveEffect(instance, source, soundId, playDuringMovement, position, attach,
                volume, spatialize2D && !is3D, minimumDistance, maximumDistance);
            _effects.Add(effect);
#if UNITY_EDITOR
            _recentPlayback.Add($"{Time.time:F2}s | {cueName ?? soundId.ToString()} | {eventReference.Path} | {source?.name ?? "world"} | {label} | volume={volume:F2}");
            if (_recentPlayback.Count > 16) _recentPlayback.RemoveAt(0);
#endif
            foreach (ActiveEffect active in _effects) UpdateEffectMix(active);
            return true;
        }
        catch (EventNotFoundException exception)
        {
            Debug.LogWarning("[SoundManager] Effect event not found in loaded banks: " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// effect의 원래 음량에 Combat Cue 재생 직후의 발소리 감쇠와 선택된 2D 공간 보정을 합성한다.
    /// FMOD 0번 Listener의 위치·방향과 이동 소유자를 사용하며 채널 생성 전 패닝은 다음 Update에서 재시도한다.
    /// </summary>
    private void UpdateEffectMix(ActiveEffect effect)
    {
        if (!effect.Instance.isValid()) return;
        float volume = Mathf.Max(0f, effect.Volume);
        if (Time.unscaledTime < _combatFocusUntil && (effect.Id == SoundId.Footstep || effect.Id == SoundId.EnemyFootstep))
            volume *= _library.CombatFootstepVolume;
        if (effect.Spatialize2D && StudioListener.ListenerCount > 0
            && RuntimeManager.StudioSystem.getListenerAttributes(0, out FMOD.ATTRIBUTES_3D listener) == FMOD.RESULT.OK)
        {
            Vector3 position = effect.Attach && effect.Owner != null ? effect.Owner.transform.position : effect.Position;
            Vector3 offset = position - new Vector3(listener.position.x, listener.position.y, listener.position.z);
            float distance = offset.magnitude;
            volume *= 1f - Mathf.InverseLerp(effect.MinimumDistance, Mathf.Max(effect.MinimumDistance + 0.01f, effect.MaximumDistance), distance);
            Vector3 right = Vector3.Cross(new Vector3(listener.up.x, listener.up.y, listener.up.z),
                new Vector3(listener.forward.x, listener.forward.y, listener.forward.z)).normalized;
            float pan = distance > 0.001f ? Mathf.Clamp(Vector3.Dot(offset / distance, right), -1f, 1f) : 0f;
            if (effect.Instance.getChannelGroup(out FMOD.ChannelGroup group) == FMOD.RESULT.OK)
                group.setPan(pan);
        }
        effect.Instance.setVolume(volume);
    }

    /// <summary>
    /// soundId와 source, playDuringMovement를 기존 이동 중 사운드 정책에 대조한다.
    /// 플레이어 발걸음과 전투음, 발도음 및 기존 전조 예외가 허용되면 true를 반환한다.
    /// </summary>
    private bool CanPlayDuringMovement(SoundId soundId, GameObject source, bool playDuringMovement)
    {
        return _footstepOnlyOwner == null || playDuringMovement || soundId == SoundId.WarCry
            || (source == _footstepOnlyOwner && (soundId == SoundId.Footstep || soundId == SoundId.SwordSwing
                || soundId == SoundId.SwordHit || soundId == SoundId.SwordUnsheath));
    }

    /// <summary>
    /// source에 FMOD 이벤트를 연결하고, Rigidbody 또는 Transform에서 얻은 속도를 전달한다.
    /// instance와 source를 입력받으며, 연결된 이벤트의 3D 속성 갱신을 FMOD에 맡긴다.
    /// </summary>
    private void AttachInstanceWithVelocity(FMOD.Studio.EventInstance instance, GameObject source)
    {
        Rigidbody rigidbody = source.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            RuntimeManager.AttachInstanceToGameObject(instance, source, rigidbody);
            return;
        }

        RuntimeManager.AttachInstanceToGameObject(instance, source, true);
    }

    /// <summary>
    /// instance를 allowFadeOut 설정에 따라 페이드 또는 즉시 정지한다.
    /// 단발음은 시작 시 해제를 요청했으므로 즉시 정지할 때 위치 추적만 추가로 해제한다.
    /// </summary>
    private static void StopEffect(FMOD.Studio.EventInstance instance, bool allowFadeOut)
    {
        if (!RuntimeManager.IsInitialized || !instance.isValid())
            return;
        instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
        if (!allowFadeOut)
            RuntimeManager.DetachInstanceFromGameObject(instance);
    }

    /// <summary>
    /// soundId의 등록과 expectedType을 확인하고 FMOD Bus를 준비한다.
    /// 유효하면 eventReference를 반환 인자로 채우고 true를 반환한다.
    /// </summary>
    private bool TryResolveEvent(SoundId soundId, SoundType expectedType, out EventReference eventReference)
    {
        eventReference = default;
        if (_library == null)
        {
            Debug.LogWarning("[SoundManager] SoundLibrary is not assigned.");
            return false;
        }
        if (!_library.TryGetEvent(soundId, out eventReference, out SoundType type))
            return false;
        if (type != expectedType)
        {
            Debug.LogWarning("[SoundManager] The playback API does not match the registered sound type: " + soundId + ", " + type);
            return false;
        }
        return TryInitializeBuses();
    }

    /// <summary>
    /// library의 BGM 및 효과음 Bus를 조회하고 보관한 음량을 적용한다.
    /// 연결을 확보하면 true를 반환하고 실패 시 Bus 오류를 보고한다.
    /// </summary>
    private bool TryInitializeBuses()
    {
        if (_busesReady && _bgmBus.isValid() && _effectBus.isValid())
            return true;
        if (_library == null)
            return false;
        try
        {
            _bgmBus = RuntimeManager.GetBus(_library.BgmBusPath);
            _effectBus = RuntimeManager.GetBus(_library.EffectBusPath);
            FMOD.RESULT bgmResult = _bgmBus.setVolume(_bgmVolume);
            FMOD.RESULT effectResult = _effectBus.setVolume(_effectVolume);
            _busesReady = bgmResult == FMOD.RESULT.OK && effectResult == FMOD.RESULT.OK;
            if (!_busesReady)
                Debug.LogError("[SoundManager] Failed to set bus volume: " + bgmResult + ", " + effectResult);
            // 불릿 타임 효과가 바로 걸리도록 두 버스의 채널 그룹을 항상 유지한다.
            _bgmBus.lockChannelGroup();
            _effectBus.lockChannelGroup();
            TryInitializeCinematicBus();
            return _busesReady;
        }
        catch (BusNotFoundException exception)
        {
            Debug.LogWarning("[SoundManager] Bus not found in loaded banks: " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// 연출음 Bus를 찾아 효과음 음량 옵션을 적용한다. 뱅크에 없으면 경고 없이 비워 두어 기존 버스 준비를 막지 않는다.
    /// _cinematicBus를 변경한다.
    /// </summary>
    private void TryInitializeCinematicBus()
    {
        if (string.IsNullOrEmpty(_library.CinematicBusPath))
            return;
        try
        {
            _cinematicBus = RuntimeManager.GetBus(_library.CinematicBusPath);
            _cinematicBus.setVolume(_effectVolume);
            // 보스 반격 감속 소리가 첫 연출음부터 걸리도록 채널 그룹을 유지한다.
            _cinematicBus.lockChannelGroup();
        }
        catch (BusNotFoundException)
        {
            _cinematicBus = default;
        }
    }

    private readonly struct PendingCue
    {
        [Header("상황별 재생 예약")]
        private readonly CombatSoundEntry _entry;
        private readonly GameObject _owner;
        private readonly bool _hasOwner;
        private readonly Vector3 _position;
        private readonly bool _attach;
        private readonly float _dueTime;
        private readonly float _pitch;

        internal CombatSoundEntry Entry => _entry;
        internal GameObject Owner => _owner;
        internal bool HasOwner => _hasOwner;
        internal Vector3 Position => _position;
        internal bool Attach => _attach;
        internal float DueTime => _dueTime;
        internal float Pitch => _pitch;

        /// <summary>
        /// entry·owner·position·attach·dueTime·pitch로 상황별 음원의 예약과 소유 여부 및 피치를 저장한다.
        /// 게임의 피해 상태는 변경하지 않고 매니저가 재생할 입력만 보관한다.
        /// </summary>
        internal PendingCue(CombatSoundEntry entry, GameObject owner, Vector3 position, bool attach, float dueTime, float pitch)
        {
            _entry = entry;
            _owner = owner;
            _hasOwner = owner != null;
            _position = position;
            _attach = attach;
            _dueTime = dueTime;
            _pitch = pitch;
        }
    }

    private readonly struct ActiveEffect
    {
        [Header("Effect")]
        private readonly FMOD.Studio.EventInstance _instance;
        private readonly GameObject _owner;
        private readonly bool _hasOwner;
        private readonly SoundId _id;
        private readonly bool _playDuringMovement;
        private readonly Vector3 _position;
        private readonly bool _attach;
        private readonly float _volume;
        private readonly bool _spatialize2D;
        private readonly float _minimumDistance;
        private readonly float _maximumDistance;

        internal FMOD.Studio.EventInstance Instance => _instance;
        internal GameObject Owner => _owner;
        internal bool HasOwner => _hasOwner;
        internal SoundId Id => _id;
        internal bool PlayDuringMovement => _playDuringMovement;
        internal Vector3 Position => _position;
        internal bool Attach => _attach;
        internal float Volume => _volume;
        internal bool Spatialize2D => _spatialize2D;
        internal float MinimumDistance => _minimumDistance;
        internal float MaximumDistance => _maximumDistance;

        /// <summary>
        /// instance·owner·soundId·playDuringMovement와 위치·부착·음량·거리 입력을 보관한다.
        /// 공간 보정 플래그와 소유 여부를 기록하여 재생 수명 및 매 프레임 믹싱에 사용한다.
        /// </summary>
        internal ActiveEffect(FMOD.Studio.EventInstance instance, GameObject owner, SoundId soundId, bool playDuringMovement,
            Vector3 position, bool attach, float volume, bool spatialize2D, float minimumDistance, float maximumDistance)
        {
            _instance = instance;
            _owner = owner;
            _hasOwner = owner != null;
            _id = soundId;
            _playDuringMovement = playDuringMovement;
            _position = position;
            _attach = attach;
            _volume = volume;
            _spatialize2D = spatialize2D;
            _minimumDistance = minimumDistance;
            _maximumDistance = maximumDistance;
        }
    }
}
