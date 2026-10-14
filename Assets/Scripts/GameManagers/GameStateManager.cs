using System;

using UnityEngine;

using UnityEngine.SceneManagement;

using BlindBlade.Combat.Contracts;

public enum GameplayState { Initializing, Playing, GameOver, Restarting }

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

    /// <summary>게임오버 상태의 현재 씬을 재시작하며 시간과 커서를 복구한다.</summary>
    public void Restart()
    {
        if (State != GameplayState.GameOver) return;
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

    /// <summary>게임오버가 요청한 정지를 해제하고 진입 전에 보관한 커서 상태를 복구한다.</summary>
    private void RestoreTimeAndCursor()
    {
        if (!_frozen) return;
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        if (runtime != null)
            runtime.SetFrozen(false);
        Cursor.lockState = _previousCursorLock;
        Cursor.visible = _previousCursorVisible;
        _frozen = false;
    }

    /// <summary>이전 플레이어 구독과 게임오버 전역 상태를 해제한다.</summary>
    public void Shutdown()
    {
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

