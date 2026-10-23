using UnityEngine;

using FMODUnity;

/// <summary>씬별 비 소리, 물 발소리와 적·투사체 사운드의 먹먹함을 선택하고 약한 비에서 강한 비로 전환한다.</summary>
[DisallowMultipleComponent]
public sealed class RainyStageAudio : MonoBehaviour
{
    private const string LIGHT_RAIN_EVENT = "event:/Ambience/Rain_Light";
    private const string HEAVY_RAIN_EVENT = "event:/Ambience/Rain_Heavy";

    [Header("비 소리")]
    [SerializeField, Tooltip("이 씬의 약한 비와 강한 비 소리를 재생합니다.")] private bool _playRainSound = true;
    [SerializeField, Min(0.01f)] private float _crossfadeDuration = 3f;
    [SerializeField, Range(0f, 1f)] private float _rainVolume = 0.7f;

    [Header("젖은 발소리")]
    [SerializeField, Tooltip("플레이어가 걸을 때 두 물 발소리 중 하나를 재생합니다.")] private bool _useWaterFootsteps;

    [Header("비 속 적과 투사체")]
    [SerializeField, Tooltip("적과 투사체 소리에만 저역 통과와 음량 감소를 적용합니다.")] private bool _muffleEnemySoundsInRain;
    [SerializeField, Range(1000f, 22000f)] private float _enemyRainLowpassCutoff = 6500f;
    [SerializeField, Range(0f, 1f)] private float _enemyRainVolume = 0.85f;

    private FMOD.Studio.EventInstance _light;
    private FMOD.Studio.EventInstance _heavy;
    private float _blend;
    private bool _heavyRequested;
    private bool _started;
    private bool _mixConfigured;
    private bool _lastMuffleEnabled;
    private float _lastLowpassCutoff;
    private float _lastEnemyVolume;

    public bool UseWaterFootsteps => _useWaterFootsteps;

    void Update()
    {
        if (Managers.IsInitialized && (!_mixConfigured || _lastMuffleEnabled != _muffleEnemySoundsInRain
            || !Mathf.Approximately(_lastLowpassCutoff, _enemyRainLowpassCutoff)
            || !Mathf.Approximately(_lastEnemyVolume, _enemyRainVolume)))
        {
            Managers.Sound.SetEnemyRainMuffle(_muffleEnemySoundsInRain, _enemyRainLowpassCutoff, _enemyRainVolume);
            _lastMuffleEnabled = _muffleEnemySoundsInRain;
            _lastLowpassCutoff = _enemyRainLowpassCutoff;
            _lastEnemyVolume = _enemyRainVolume;
            _mixConfigured = true;
        }
        if (!_playRainSound)
        {
            if (_started) StopRain();
            return;
        }
        if (!Game.IsInitialized || Game.Instance.GameStateManager == null)
            return;

        GameplayState state = Game.Instance.GameStateManager.State;
        if (!_started && state == GameplayState.Playing)
            StartRain();
        if (!_started)
            return;

        bool paused = state == GameplayState.Paused;
        if (_light.isValid()) _light.setPaused(paused);
        _heavy.setPaused(paused);
        if (state == GameplayState.GameOver || state == GameplayState.Cleared)
        {
            StopRain();
            return;
        }
        if (paused || !_heavyRequested)
            return;

        _blend = Mathf.MoveTowards(_blend, 1f, Time.unscaledDeltaTime / _crossfadeDuration);
        float blend = Mathf.Clamp01(_blend);

        float lightVolume = blend >= 1f ? 0f : _rainVolume * Mathf.Cos(blend * Mathf.PI * 0.5f);
        float heavyVolume = _rainVolume * Mathf.Sin(blend * Mathf.PI * 0.5f);

        if (_light.isValid())
            _light.setVolume(Mathf.Max(0f, lightVolume));
        _heavy.setVolume(Mathf.Max(0f, heavyVolume));
        if (_blend >= 1f && _light.isValid())
        {
            _light.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _light.release();
            _light.clearHandle();
        }
    }

    void OnDisable()
    {
        StopRain();
        if (Managers.IsInitialized)
            Managers.Sound.SetEnemyRainMuffle(false, _enemyRainLowpassCutoff, 1f);
        _mixConfigured = false;
    }

    /// <summary>FMOD의 두 반복 비 이벤트를 만들고 약한 비만 들리도록 시작한다. 실패 시 다음 프레임에 재시도한다.</summary>
    private void StartRain()
    {
        try
        {
            _light = RuntimeManager.CreateInstance(LIGHT_RAIN_EVENT);
            _heavy = RuntimeManager.CreateInstance(HEAVY_RAIN_EVENT);
            _light.setVolume(_rainVolume);
            _heavy.setVolume(0f);
            if (_light.start() != FMOD.RESULT.OK || _heavy.start() != FMOD.RESULT.OK)
            {
                StopRain();
                return;
            }
            _started = true;
        }
        catch (EventNotFoundException)
        {
            StopRain();
        }
    }

    /// <summary>보스 등장 요청을 저장하고 이후 프레임에서 두 비 이벤트의 음량을 crossfadeDuration 동안 교차시킨다.</summary>
    public void TransitionToHeavy()
    {
        _heavyRequested = true;
    }

    /// <summary>씬 종료 또는 게임 종료 시 비 이벤트를 정지하고 FMOD 인스턴스를 해제한다.</summary>
    private void StopRain()
    {
        if (_light.isValid())
        {
            _light.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _light.release();
            _light.clearHandle();
        }
        if (_heavy.isValid())
        {
            _heavy.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            _heavy.release();
            _heavy.clearHandle();
        }
        _started = false;
    }
}
