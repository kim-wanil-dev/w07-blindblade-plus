using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;


/// <summary>씬 공통 구성을 생성하고 참조 연결 및 초기화 순서를 제어한다.</summary>
[DefaultExecutionOrder(100)]
public sealed class GameSceneInitializer : MonoBehaviour
{
    [Header("공통 구성")]
    [SerializeField] private TerrainMemory _terrainMemoryPrefab;

    [Header("맵별 설정")]
    [SerializeField] private bool _useTerrainMemory;
    [SerializeField] private Vector2 _memoryMinimum = new Vector2(-24f, -6f);
    [SerializeField] private Vector2 _memorySize = new Vector2(200f, 200f);
    [SerializeField] private bool _useCorridorRule;
    [SerializeField] private bool _showGameplayControls;
    [Header("검 소음")]
    [SerializeField] private bool _useSwordNoise;
    [SerializeField, Min(1f)] private float _swordHearingDistance = 40f;
    [SerializeField, Min(0.1f)] private float _swordAlertDuration = 15f;
    private StageSwordNoise _swordNoise;
    private GameStateManager _state;
    private UIManager _ui;
    private CorridorRoundController _round;
    private TerrainMemory _memory;
    private PlayerController _player;
    private PlayerInputCommandAdapter _legacyInput;
    private KatanaInputCommandAdapter _katanaInput;
    private CombatSessionRuntime _combatSession;
    private RainyStageAudio _rainAudio;
    private bool _started;

    public bool SwordNoiseEnabled => _useSwordNoise;
    public float SwordHearingDistance => _swordHearingDistance;
    public float SwordAlertDuration => _swordAlertDuration;

    void Start()
    {
        _started = true;
        Initialize();
    }

    void OnEnable()
    {
        if (_started) 
            Initialize();
    }

    /// <summary>현재 플레이어와 공통 구성을 준비하고 초기화가 끝나면 입력을 허용한다.</summary>
    private void Initialize()
    {
        if (_state != null) return;
        _player = Game.Player;
        if (_player == null
            || (_useTerrainMemory && _terrainMemoryPrefab == null))
        {
            Debug.LogError("게임 씬의 플레이어 또는 공통 구성 프리팹 연결이 누락되었습니다.", this);
            enabled = false;
            return;
        }
        _combatSession = GetComponent<CombatSessionRuntime>();
        if (_combatSession == null)
            _combatSession = gameObject.AddComponent<CombatSessionRuntime>();
        _combatSession.Initialize();
        if (!Game.Instance.InitializeSession())
        {
            _combatSession.Shutdown();
            enabled = false;
            return;
        }
        _state = Game.Instance.GameStateManager;
        _ui = Game.Instance.UIManager;
        EnemyCombatService combat = _player.GetComponent<EnemyCombatService>();
        if (combat == null)
        {
            Debug.LogError("플레이어 프리팹에 EnemyCombatService가 없습니다.", _player);
            enabled = false;
            return;
        }
        combat.ConfigurePlayer(_player.transform);
        _player.ConfigureCombatService(combat);
        _legacyInput = _player.GetComponent<PlayerInputCommandAdapter>();
        _katanaInput = _player.GetComponent<KatanaInputCommandAdapter>();
        if (_legacyInput != null && _legacyInput.isActiveAndEnabled) _legacyInput.Initialize();
        if (_katanaInput != null && _katanaInput.isActiveAndEnabled) _katanaInput.Initialize();
        // if (_showGameplayControls) _ui.Show<GameplayControlsUI>();
        InitializeSensory();
        if (_useTerrainMemory)
        {
            _memory = Instantiate(_terrainMemoryPrefab, transform);
            _memory.enabled = true;
            _memory.Initialize(_player.transform, _memoryMinimum, _memorySize);
        }
        if (_useCorridorRule)
        {
            _round = gameObject.AddComponent<CorridorRoundController>();
            _round.Initialize(_state);
        }
        if (_useSwordNoise)
        {
            var enemies = new List<EnemyController>();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                enemies.AddRange(root.GetComponentsInChildren<EnemyController>(true));
            _swordNoise = gameObject.AddComponent<StageSwordNoise>();
            _swordNoise.Initialize(_player, enemies.ToArray(), _swordHearingDistance, _swordAlertDuration);
        }
        _state.BeginPlaying();
    }

    /// <summary>현재 플레이어를 감각 관측자로 연결하고 공통 표현과 발걸음 이벤트를 준비한다.</summary>
    private void InitializeSensory()
    {
        SensoryManager sensory = Game.Instance.SensoryManager;
        sensory.SetObserver(_player.transform);
        if (!sensory.IsReady) sensory.Initialize();
        sensory.GetComponent<SensoryPerceptionMask>().SetFootstepOnly(false);
        sensory.SetEffectsVisible(true);
        sensory.SetFootstepsSuppressed(false);
        Managers.Sound.SetFootstepOnlyOwner(null);
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            _rainAudio = root.GetComponentInChildren<RainyStageAudio>();
            if (_rainAudio != null) break;
        }
        _player.FootstepEmitted += HandlePlayerFootstep;
    }

    /// <summary>
    /// step의 소음 강도로 현재 플레이어에 붙은 발소리를 재생한다.
    /// 씬의 활성 RainyStageAudio가 물 발소리를 선택하면 RainFootstep을 사용하고 나머지는 기본 Footstep을 사용한다.
    /// </summary>
    private void HandlePlayerFootstep(PlayerFootstepEvent step)
    {
        SoundId soundId = _rainAudio != null && _rainAudio.isActiveAndEnabled && _rainAudio.UseWaterFootsteps
            ? SoundId.RainFootstep : SoundId.Footstep;
        Managers.Sound.PlayAttached(soundId, _player.gameObject, step.NoiseStrength);
    }

    void OnDisable()
    {
        if (_legacyInput != null) _legacyInput.Shutdown();
        if (_katanaInput != null) _katanaInput.Shutdown();
        if (_round != null) _round.Shutdown();
        if (_swordNoise != null) _swordNoise.Shutdown();
        if (_player != null) _player.FootstepEmitted -= HandlePlayerFootstep;
        if (Managers.IsInitialized) Managers.Sound.SetFootstepOnlyOwner(null);
        if (_state != null)
        {
            Game.Instance.ShutdownSession();
        }
        if (_combatSession != null)
            _combatSession.Shutdown();
        if (_memory != null) _memory.gameObject.SetActive(false);
        if (_memory != null) Destroy(_memory.gameObject);
        if (_round != null) Destroy(_round);
        if (_swordNoise != null) Destroy(_swordNoise);
        _state = null;
        _ui = null;
        _combatSession = null;
        _rainAudio = null;
    }
}
