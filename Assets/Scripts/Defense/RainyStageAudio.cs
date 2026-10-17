using UnityEngine;

using FMODUnity;

/// <summary>Ambient와 함께 비 소리를 반복 재생하고 보스 등장 때 약한 비에서 강한 비로 겹쳐 전환한다.</summary>
[DisallowMultipleComponent]
public sealed class RainyStageAudio : MonoBehaviour
{
    private const string LIGHT_RAIN_EVENT = "event:/Ambience/Rain_Light";
    private const string HEAVY_RAIN_EVENT = "event:/Ambience/Rain_Heavy";

    [Header("비 소리")]
    [SerializeField, Min(0.01f)] private float _crossfadeDuration = 3f;
    [SerializeField, Range(0f, 1f)] private float _rainVolume = 0.7f;

    private FMOD.Studio.EventInstance _light;
    private FMOD.Studio.EventInstance _heavy;
    private float _blend;
    private bool _heavyRequested;
    private bool _started;

    void Update()
    {
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
        if (_light.isValid())
            _light.setVolume(_rainVolume * Mathf.Cos(_blend * Mathf.PI * 0.5f));
        _heavy.setVolume(_rainVolume * Mathf.Sin(_blend * Mathf.PI * 0.5f));
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
