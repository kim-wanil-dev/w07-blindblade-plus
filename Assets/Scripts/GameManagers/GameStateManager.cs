using System;

using UnityEngine;

using UnityEngine.SceneManagement;

using BlindBlade.Combat.Contracts;

public enum GameplayState { Initializing, Playing, GameOver, Cleared, Restarting, Paused, ActComplete }

/// <summary>게임 상태와 시간·커서 수명을 관리하고 입력 및 UI에 상태 변경을 알린다.</summary>
public sealed class GameStateManager : MonoBehaviour
{
    private PlayerController _player;
    private string _scenePath;
    private CursorLockMode _previousCursorLock;
    private bool _previousCursorVisible;
    private bool _frozen;
    public GameplayState State { get; private set; } = GameplayState.Initializing;
    public event Action<GameplayState> StateChanged;

    public bool IsPaused => State == GameplayState.Paused;

    /// <summary>player와 scenePath를 현재 세션에 연결하고 초기화 중 상태로 전환한다.</summary>
    public void Initialize(PlayerController player, string scenePath)
    {
        Shutdown();
        _player = player;
        _scenePath = scenePath;
        State = GameplayState.Initializing;
        _player.ActionEvent += HandleAction;
    }

    /// <summary>초기화가 끝난 세션을 플레이 중 상태로 변경한다.</summary>
    public void BeginPlaying()
    {
        if (State == GameplayState.Initializing) ChangeState(GameplayState.Playing);
    }

    /// <summary>플레이 중 플레이어와 세션 시계를 정지하고 커서를 표시한다. 정지에 성공하면 true를 반환한다.</summary>
    public bool PauseForSettings()
    {
        if (State != GameplayState.Playing || _player == null)
            return false;

        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _frozen = true;
        _player.SetPaused(true);
        if (CombatSessionRuntime.Active != null)
            CombatSessionRuntime.Active.SetFrozen(true);
        Managers.Sound.SetEffectsPaused(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ChangeState(GameplayState.Paused);
        return true;
    }

    /// <summary>설정 팝업에서 복귀해 플레이어·세션 시계·커서를 다시 플레이 상태로 복원한다.</summary>
    public bool ResumeFromSettings()
    {
        if (State != GameplayState.Paused)
            return false;

        if (_player != null)
            _player.SetPaused(false);
        if (CombatSessionRuntime.Active != null)
            CombatSessionRuntime.Active.SetFrozen(false);
        Managers.Sound.SetEffectsPaused(false);
        _frozen = false;
        ChangeState(GameplayState.Playing);
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
        return true;
    }

    /// <summary>action의 플레이어 사망을 게임오버로 전환한다.</summary>
    private void HandleAction(PlayerActionEvent action)
    {
        if (action.State == PlayerActionState.Dead) RequestGameOver();
    }

    /// <summary>플레이 중인 게임을 한 번만 종료하고 시간·커서·사운드와 플레이어 사망을 처리한다.</summary>
    public void RequestGameOver()
    {
        if (State != GameplayState.Playing) return;
        _previousCursorLock = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
        _frozen = true;
        ChangeState(GameplayState.GameOver);
        _player.SetDead();
        Managers.Sound.SetFootstepOnlyOwner(null);
        Managers.Sound.StopEffects();
        CombatSessionRuntime.Active.SetFrozen(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// 게임을 Cleared 상태로 전환해 게임플레이 입력을 차단하고 커서를 표시한다.
    /// 전투 세션의 시간 배율은 변경하지 않는다.
    /// </summary>
    public void RequestGameClear()
    {
        if (State != GameplayState.Playing) return;

        Managers.Sound.SetFootstepOnlyOwner(null);
        Managers.Sound.StopEffects();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ChangeState(GameplayState.Cleared);
    }

    /// <summary>1막 보스 사망 뒤 게임플레이 입력을 끄고 완료 버튼을 위한 커서를 표시한다.</summary>
    public void RequestActComplete()
    {
        if (State != GameplayState.Playing) return;
        Managers.Sound.SetFootstepOnlyOwner(null);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ChangeState(GameplayState.ActComplete);
    }

    /// <summary>게임오버 또는 설정 일시정지 상태에서 현재 씬을 재시작하고 플레이어·효과음·커서를 복구한다.</summary>
    public void Restart()
    {
        bool wasPaused = State == GameplayState.Paused;
        if (!wasPaused && State != GameplayState.GameOver) return;
        if (wasPaused)
        {
            if (_player != null)
                _player.SetPaused(false);
            Managers.Sound.SetEffectsPaused(false);
        }
        ChangeState(GameplayState.Restarting);
        RestoreTimeAndCursor();
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            _scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(_scenePath);
#endif
    }

    /// <summary>state로 현재 상태를 변경하고 구독자에게 전달한다.</summary>
    private void ChangeState(GameplayState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>게임 정지를 해제하고 Cleared면 커서를 표시하며 그 외에는 진입 전 상태를 복구한다.</summary>
    private void RestoreTimeAndCursor()
    {
        if (!_frozen) return;
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        if (runtime != null)
            runtime.SetFrozen(false);
        if (State == GameplayState.Cleared)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
        }
        _frozen = false;
    }

    /// <summary>이전 플레이어 구독과 게임오버 전역 상태를 해제한다.</summary>
    public void Shutdown()
    {
        if (State == GameplayState.Paused && _player != null)
            _player.SetPaused(false);
        if (State == GameplayState.Paused && Managers.IsInitialized)
            Managers.Sound.SetEffectsPaused(false);
        RestoreTimeAndCursor();
        if (_player != null) _player.ActionEvent -= HandleAction;
        _player = null;
        _scenePath = null;
        State = GameplayState.Initializing;
    }

    void OnDestroy()
    {
        Shutdown();
    }
}

