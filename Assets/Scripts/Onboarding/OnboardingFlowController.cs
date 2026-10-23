using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

using BlindBlade.Combat.Contracts;
using BlindBlade.Telemetry;

/// <summary>BambooDefense 복제 씬의 순차 연습, 전투 판정 집계, 재시도와 BambooStage1 전환을 관리한다.</summary>
[DisallowMultipleComponent]
public sealed class OnboardingFlowController : MonoBehaviour
{
    private const int FINAL_STAGE = 4;
    private const int RANGED_STAGE = 3;
    private const int PERFECT_DODGE_STAGE = 1;
    private const int MELEE_STAGE = 2;
    private const int RANGED_SWORD_WAVE_OBJECTIVE = 2;
    private const int RANGED_THROW_BURST_COUNT = 3;
    private const int FINAL_STAGE_ULTIMATE_OBJECTIVE = 1;
    private const float ARENA_RADIUS = 23.6f;
    private const float MOVEMENT_IDLE_HINT_DELAY = 3f;
    private const float MOVEMENT_WAVE_GUIDE_MINIMUM_RADIUS = 2.5f;
    private const float MOVEMENT_WAVE_CENTER_TOLERANCE = 0.16f;
    private const float MOVEMENT_WAVE_VISIBLE_FALLBACK_PROGRESS = 0.55f;
    private const int MOVEMENT_WAVE_SCREEN_SAMPLES = 48;
    private const float MOVEMENT_GUIDE_PULSE_INTERVAL = 10f;
    private const int OPENING_TARGET_COUNT = 3;
    private const float PRACTICE_ENEMY_DISTANCE = 4f;
    private const float MELEE_HINT_ENTER_DISTANCE = 3.5f;
    private const float MELEE_HINT_EXIT_DISTANCE = 4.5f;
    private const float MELEE_HINT_REPEAT_DELAY = 1.5f;
    private const float MELEE_STAGE_SIDE_OFFSET = 4f;
    private const float SWORD_WAVE_TARGET_DISTANCE = 9f;
    private const float ENEMY_REPLACE_DISTANCE = 18f;
    private const float WAVE_STAGE_START_DISTANCE = 5f;
    private const float SWORD_WAVE_TARGET_SPACING = 2.4f;
    private const int ULTIMATE_TARGET_COUNT = 8;
    private const float OPENING_TARGET_FIRST_DISTANCE = 2f;
    private const float OPENING_TARGET_DISTANCE_STEP = 8.5f;
    private const int SWORD_WAVE_TARGET_COUNT = 2;
    private const float THROWER_DISTANCE = 12f;
    private const float THROW_INTERVAL = 2f;
    private const float RANGED_THROW_BURST_GAP = 0.3f;
    private const float HEALTH_REGEN_PER_SECOND = 45f;
    private const float STAMINA_REGEN_PER_SECOND = 100f;
    private const float STAGE1_FADE_DURATION = 0.5f;
    private const float OPENING_FADE_DURATION = 0.6f;

    private static int _savedRunStage;
    private static int _restartStage;
    private static bool _retryPending;
    private static bool _movementWaveTutorialSeen;
    private static bool _targetThreadTutorialSeen;
    private static bool _perfectDodgeTutorialSeen;
    private static bool _ultimateMeterTutorialSeen;
    private static bool _swordWaveTutorialSeen;

    [Header("씬 연습 자산")]
    [SerializeField] private GameObject _meleePrefab;
    [SerializeField] private BambooThrower _throwerPrefab;
    [SerializeField] private Transform _unitRoot;
    [SerializeField] private Transform _arenaCenter;

    [Header("단계 연결")]
    [SerializeField] private OnboardingHud _hud;
    [SerializeField] private string _stage1SceneName = "BambooStage1";

    private readonly HashSet<long> _resolvedHitIds = new HashSet<long>();
    private readonly HashSet<int> _launchedShurikenAttackIds = new HashSet<int>();
    private readonly List<DefenseShuriken> _hintShurikens = new List<DefenseShuriken>();
    private readonly HashSet<int> _trainingEnemyAttackIds = new HashSet<int>();
    private readonly HashSet<int> _perfectDodgeAttackIds = new HashSet<int>();
    private readonly HashSet<int> _pendingPerfectDodgeAttackIds = new HashSet<int>();
    private readonly HashSet<EnemyHealth> _openingTargets = new HashSet<EnemyHealth>();
    private readonly HashSet<EnemyHealth> _ultimateTargetHealths = new HashSet<EnemyHealth>();
    private readonly HashSet<EnemyHealth> _defeatedUltimateTargets = new HashSet<EnemyHealth>();
    private readonly List<GameObject> _ultimateTargets = new List<GameObject>();
    private readonly List<Vector3> _ultimateTargetPositions = new List<Vector3>();
    private readonly List<GameObject> _swordWaveTargets = new List<GameObject>();
    private readonly List<Vector3> _swordWaveTargetPositions = new List<Vector3>();
    private PlayerController _player;
    private FootstepRipple _footstepRipple;
    private PlayerMotor _motor;
    private PlayerHealth _health;
    private PlayerStamina _stamina;
    private PlayerEmpower _empower;
    private PlayerSkillGauge _skillGauge;
    private PlayerUltimateHint _ultimateHint;
    private PlayerPerfectPulse _perfectPulse;
    private PlayerSwordWaveLauncher _swordWave;
    private PlayerDashTargetThread _dashTargetThread;
    private KatanaCombatCoordinator _combat;
    private BambooDefenseDirector _director;
    private GameStateManager _gameState;
    private InputManager _inputManager;
    private Camera _viewCamera;
    private BlindBlade.UI.CombatHud.CombatPerfectParryTokenHud _tokenHud;
    private GameObject _trainingEnemy;
    private EnemyHealth _trainingEnemyHealth;
    private BambooThrower _trainingThrower;
    private DefenseShuriken _hintShuriken;
    private int _stage;
    private int _objectiveIndex;
    private int _activeDodgeId;
    private float _stageEntrySwordWaveGauge;
    private float _stageEntryUltimateGauge;
    private int _openingKillCount;
    private bool _advanceQueued;
    private bool _restartQueued;
    private bool _recoveryQueued;
    private bool _transitionInputLocked;
    private bool _inputWasEnabledBeforeTransition;
    private bool _completingOnboarding;
    private bool _playOpening;
    private bool _tutorialPaused;
    private bool _stageChoiceActive;
    private bool _repeatStageSelected;
    private bool _stageChoiceReceived;
    private bool _stageChoiceOwnsCursor;
    private bool _cursorVisibleBeforeStageChoice;
    private CursorLockMode _cursorLockBeforeStageChoice;
    private bool _ultimateTutorialPending;
    private bool _ultimateStrikeStarted;
    private bool _ultimateMeterTutorialRunning;
    private bool _meleeHintNearby;
    private bool _showIdleMovementHint;
    private bool _tutorialAdvanceRequested;
    private bool _tutorialOwnsTransitionLock;
    private bool _attackAfterTutorialUnlock;
    private bool _movementWaveTutorialRunning;
    private bool _targetThreadTutorialRunning;
    private float _movementIdleElapsed;
    private float _movementGuideElapsed;
    private float _lastMeleeHintEmphasisAt;
    private CombatSessionRuntime _tutorialTimeRuntime;
    private float _nextThrowAt;

    public event Action<int, int> ObjectiveEntered;
    public event Action<int, int> ObjectiveCompleted;
    public event Action<int> StageRetrying;
    public event Action<int> StageRepeated;
    public event Action OnboardingCompleted;
    public event Action<DefenseResolution> ObjectiveDefenseResolved;
    public bool IsObjectiveInputAvailable => !_transitionInputLocked && !_advanceQueued && !_restartQueued
        && !_recoveryQueued && !_tutorialPaused && _gameState != null && _gameState.State == GameplayState.Playing;

    /// <summary>새 Intro 시작을 위해 이번 온보딩 실행의 단계 기록과 재시도 플래그를 초기화한다.</summary>
    public static void StartNewRun()
    {
        _savedRunStage = 0;
        _restartStage = 0;
        _retryPending = false;
        _movementWaveTutorialSeen = false;
        _targetThreadTutorialSeen = false;
        _perfectDodgeTutorialSeen = false;
        _ultimateMeterTutorialSeen = false;
        _swordWaveTutorialSeen = false;
        PlayerPerfectPulse.ResetOnboardingStartPulseFade();
        OnboardingTelemetryRecorder.BeginNewRun();
    }

    void Start()
    {
        if (_hud == null) _hud = GetComponent<OnboardingHud>();
        int startingStage = _retryPending ? _restartStage : _savedRunStage;
        // 첫 단계부터 시작하는 실행은 첫 화면부터 암전으로 덮어 서막 전에 장면이 보이지 않게 한다.
        _playOpening = !_retryPending && _savedRunStage == 0;
        if (_playOpening) _hud.CoverScreen();
        // 극한 회피 단계에서 재시작하면 게임 시작 자동 파동도 생략해 대나무를 드러내지 않는다.
        if (startingStage == PERFECT_DODGE_STAGE) Game.Player.GetComponent<PlayerPerfectPulse>().SuppressStartPulse();
        StartCoroutine(BeginWhenGameplayIsReady());
    }

    void OnDisable()
    {
        RestoreStageChoiceCursor();
        if (!_completingOnboarding && _transitionInputLocked && _inputManager != null && _gameState != null
            && _gameState.State == GameplayState.Playing)
            UnlockTransitionInput();
        EndTutorialPause();
        _ultimateMeterTutorialRunning = false;
        UnbindEvents();
        CleanupTrainingContent();
    }

    void Update()
    {
        if (_player == null || _health == null || _gameState == null) return;
        if (_health.IsAlive) _health.Heal(HEALTH_REGEN_PER_SECOND * Time.unscaledDeltaTime);
        if (_health.IsAlive) _stamina.RestoreForOnboarding(STAMINA_REGEN_PER_SECOND * Time.unscaledDeltaTime);
        if (_player.CurrentState == PlayerActionState.Dead && _gameState.State == GameplayState.GameOver)
        {
            if (!_restartQueued) StartCoroutine(RestartCurrentStage());
            return;
        }

        if (_stageChoiceActive) return;
        if (_tutorialPaused)
        {
            UpdatePausedTutorialInput();
            return;
        }

        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked
            || _ultimateMeterTutorialRunning) return;
        if (_stage == FINAL_STAGE && _objectiveIndex == 0)
        {
            if (!_ultimateMeterTutorialSeen && _skillGauge.UltimateGauge > 0f)
            {
                _ultimateMeterTutorialRunning = true;
                StartCoroutine(ShowUltimateMeterTutorial());
                return;
            }
            if (_skillGauge.CanUseUltimate)
            {
                _ultimateTutorialPending = true;
                CompleteCurrentObjective();
                return;
            }
        }
        if (_stage == 0) UpdateOpeningStage();

        MaintainTrainingEnemy();
        UpdateMeleeHint();
        MaintainSwordWaveTargets();
        MaintainUltimateTargets();
        UpdateGuardTiming();
        UpdateRangedThrower();
        UpdateShurikenHint();
    }

    /// <summary>게임플레이가 시작된 뒤 플레이어·전투 이벤트를 연결하고 현재 또는 재시도 단계를 시작한다.</summary>
    private IEnumerator BeginWhenGameplayIsReady()
    {
        _gameState = FindFirstObjectByType<GameStateManager>();
        while (Game.Player == null || _gameState == null || _gameState.State != GameplayState.Playing)
            yield return null;

        _player = Game.Player;
        _footstepRipple = _player.GetComponent<FootstepRipple>();
        _motor = _player.GetComponent<PlayerMotor>();
        _health = _player.GetComponent<PlayerHealth>();
        _stamina = _player.GetComponent<PlayerStamina>();
        _empower = _player.GetComponent<PlayerEmpower>();
        _skillGauge = _player.GetComponent<PlayerSkillGauge>();
        _ultimateHint = _player.GetComponent<PlayerUltimateHint>();
        _perfectPulse = _player.GetComponent<PlayerPerfectPulse>();
        _swordWave = _player.GetComponent<PlayerSwordWaveLauncher>();
        _dashTargetThread = _player.GetComponent<PlayerDashTargetThread>();
        _viewCamera = _player.GetComponentInChildren<Camera>();
        _tokenHud = FindFirstObjectByType<BlindBlade.UI.CombatHud.CombatPerfectParryTokenHud>();
        _combat = _player.GetComponent<KatanaCombatCoordinator>();
        _director = FindFirstObjectByType<BambooDefenseDirector>();
        _inputManager = Game.Instance.InputManager;
        SuppressGameOverScreen();
        _ultimateHint.SetOnboardingLayout();
        if (_arenaCenter == null)
        {
            GameObject arena = GameObject.Find("Arena");
            _arenaCenter = arena != null ? arena.transform : transform;
        }
        if (_hud == null) _hud = GetComponent<OnboardingHud>();
        BindEvents();
        _footstepRipple.PlayerRippleEmitted += HandlePlayerRippleEmitted;
        _stage = _retryPending ? _restartStage : _savedRunStage;
        _retryPending = false;
        LockTransitionInput();
        EnterStage(_stage);
        // 서막 제목과 암전이 완전히 걷히면 단계 시작 카드를 띄우고 카드와 함께 입력을 연다.
        if (_playOpening) yield return _hud.PlayOpening("서막", OPENING_FADE_DURATION);
        _hud.PlayStageStart(_stage);
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>실제 플레이어·투척수 사건에 구독해 전투 사실을 온보딩 목표에 연결한다.</summary>
    private void BindEvents()
    {
        _combat.Events.DefenseResolved += HandleDefenseResolved;
        _combat.Events.DodgeChanged += HandleDodgeChanged;
        _combat.Events.EmpowerChanged += HandleEmpowerChanged;
        _skillGauge.Changed += HandleSkillGaugeChanged;
        _swordWave.SwordWaveLaunched += HandleSwordWaveLaunched;
        _director.ShurikenLaunched += HandleShurikenLaunched;
    }

    /// <summary>온보딩 실행 중 GameOverUI를 숨기고 상태 변경 구독을 해제해 사망 화면을 표시하지 않는다.</summary>
    private void SuppressGameOverScreen()
    {
        GameOverUI gameOver = Game.Instance.UIManager.Get<GameOverUI>();
        if (gameOver == null) return;
        gameOver.SetVisible(false);
        gameOver.Shutdown();
    }

    /// <summary>BindEvents에서 추가한 전투 사건 구독을 모두 해제한다.</summary>
    private void UnbindEvents()
    {
        if (_combat != null)
        {
            _combat.Events.DefenseResolved -= HandleDefenseResolved;
            _combat.Events.DodgeChanged -= HandleDodgeChanged;
            _combat.Events.EmpowerChanged -= HandleEmpowerChanged;
        }
        if (_footstepRipple != null) _footstepRipple.PlayerRippleEmitted -= HandlePlayerRippleEmitted;
        if (_swordWave != null) _swordWave.SwordWaveLaunched -= HandleSwordWaveLaunched;
        if (_skillGauge != null) _skillGauge.Changed -= HandleSkillGaugeChanged;
        if (_director != null)
        {
            _director.ShurikenLaunched -= HandleShurikenLaunched;
        }
    }

    /// <summary>stage에 필요한 재배치·적·투척수·HUD를 설정하고 해당 단계의 목표를 0부터 시작한다.</summary>
    private void EnterStage(int stage)
    {
        CleanupTrainingContent();
        _stage = Mathf.Clamp(stage, 0, FINAL_STAGE);
        _savedRunStage = _stage;
        _objectiveIndex = 0;
        _advanceQueued = false;
        _activeDodgeId = 0;
        _openingKillCount = 0;
        _movementIdleElapsed = 0f;
        _movementGuideElapsed = 0f;
        _showIdleMovementHint = false;
        _hud.SetIdleMovementHint(false);
        _hud.SetMovementControls(false);
        _tutorialAdvanceRequested = false;
        _ultimateTutorialPending = false;
        _ultimateStrikeStarted = false;
        _ultimateMeterTutorialRunning = false;
        _meleeHintNearby = false;
        _lastMeleeHintEmphasisAt = Time.unscaledTime - MELEE_HINT_REPEAT_DELAY;
        _hud.SetDefenseHint(false, false, false);
        _perfectDodgeAttackIds.Clear();
        _pendingPerfectDodgeAttackIds.Clear();
        _resolvedHitIds.Clear();
        _launchedShurikenAttackIds.Clear();

        PositionPlayerForStage(_stage);
        _hud.ShowObjective(_stage, _objectiveIndex);
        if (_stage == 0) _hud.SetOpeningKillProgress(0, OPENING_TARGET_COUNT);
        if (_stage == FINAL_STAGE) _hud.SetUltimateGaugeProgress(_skillGauge.UltimateNormalized);
        _stageEntrySwordWaveGauge = _skillGauge.SwordWaveGauge;
        _stageEntryUltimateGauge = _skillGauge.UltimateGauge;
        ObjectiveEntered?.Invoke(_stage, _objectiveIndex);
        UpdateGuardTiming();

        if (_stage == 0)
            CreateOpeningTargets();
        else if (_stage == MELEE_STAGE)
        {
            CreateTrainingEnemy(false, true);
            _hud.SetDefenseHint(true, false, false);
        }
        else if (_stage == RANGED_STAGE)
        {
            CreateTrainingThrower();
        }
        else if (_stage == PERFECT_DODGE_STAGE)
            CreateTrainingThrower();
        else if (_stage == FINAL_STAGE)
            CreateTrainingThrower();
    }

    /// <summary>필요한 연습 위치를 NavMesh에서 찾고 플레이어와 시점을 단계에 맞게 배치한다.</summary>
    private void PositionPlayerForStage(int stage)
    {
        Vector3 center = _arenaCenter.position;
        Vector3 position = _player.transform.position;
        Vector3 facing = _motor.ViewForward;
        if (stage == 0)
        {
            facing = Vector3.forward;
            position = center - facing * (ARENA_RADIUS / 3f);
        }
        else if (stage == MELEE_STAGE)
        {
            position = center + Vector3.right * MELEE_STAGE_SIDE_OFFSET;
            facing = Vector3.forward;
        }
        else if (stage == PERFECT_DODGE_STAGE || stage == RANGED_STAGE || stage == FINAL_STAGE)
        {
            position = center + Vector3.forward * WAVE_STAGE_START_DISTANCE;
            facing = Vector3.forward;
        }

        if (stage == 0 || stage == MELEE_STAGE || stage == PERFECT_DODGE_STAGE
            || stage == RANGED_STAGE || stage == FINAL_STAGE)
            MovePlayer(position, facing);
    }

    /// <summary>전방에 기존 비공격 근접 적 세 명을 배치하고 각 실제 사망을 첫 단계 처치 목표에 연결한다.</summary>
    private void CreateOpeningTargets()
    {
        Vector3 center = _arenaCenter.position;
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        float[] offsets = { -0.8f, 0.45f, -0.35f };
        for (int i = 0; i < OPENING_TARGET_COUNT; i++)
        {
            float distance = OPENING_TARGET_FIRST_DISTANCE + OPENING_TARGET_DISTANCE_STEP * i;
            Vector3 position = center + forward * distance + right * offsets[i];
            GameObject enemy = SpawnTrainingEnemy(position, true, false);
            EnemyHealth health = enemy.GetComponent<EnemyHealth>();
            if (health == null) continue;
            health.Died += HandleTrainingEnemyDied;
            _openingTargets.Add(health);
        }
    }

    /// <summary>
    /// 경기장 기준 근접 연습 위치에서 distance 앞에 적을 배치한다.
    /// killable과 attacksPlayer로 처치·공격을 설정하고 공격 적이면 AttackCommitted를 구독한다.
    /// </summary>
    private void CreateTrainingEnemy(bool killable, bool attacksPlayer, float distance = PRACTICE_ENEMY_DISTANCE)
    {
        Vector3 position = _arenaCenter.position + Vector3.right * MELEE_STAGE_SIDE_OFFSET
            + Vector3.forward * distance;
        _trainingEnemy = SpawnTrainingEnemy(position, killable, attacksPlayer);
        _trainingEnemyHealth = _trainingEnemy.GetComponent<EnemyHealth>();
        if (_trainingEnemyHealth != null)
        {
            _trainingEnemyHealth.Died += HandleTrainingEnemyDied;
            _trainingEnemyHealth.DamageBlocked += HandleMeleeDamageBlocked;
        }
        if (attacksPlayer)
        {
            foreach (EnemyAttackBehaviour attack in _trainingEnemy.GetComponents<EnemyAttackBehaviour>())
                attack.AttackCommitted += HandleTrainingEnemyAttackCommitted;
        }
    }

    /// <summary>position의 NavMesh에 근접 적을 만들고 killable·attacksPlayer에 맞춰 피해와 공격·추적을 설정해 반환한다.</summary>
    private GameObject SpawnTrainingEnemy(Vector3 position, bool killable, bool attacksPlayer)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) position = hit.position;
        Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(_player.transform.position - position, Vector3.up));
        GameObject enemy = Instantiate(_meleePrefab, position, rotation, _unitRoot);
        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health != null) health.SetInvulnerable(!killable);
        EnemyController controller = enemy.GetComponent<EnemyController>();
        if (controller != null)
        {
            controller.SetTarget(attacksPlayer ? _player.transform : null);
            controller.enabled = attacksPlayer;
        }
        foreach (EnemyAttackBehaviour attack in enemy.GetComponents<EnemyAttackBehaviour>())
            attack.enabled = attacksPlayer;
        EnemyAttackSelector selector = enemy.GetComponent<EnemyAttackSelector>();
        if (selector != null) selector.enabled = attacksPlayer;
        DefenseChaser chaser = enemy.GetComponent<DefenseChaser>();
        if (chaser != null) chaser.enabled = attacksPlayer;
        return enemy;
    }

    /// <summary>거합 목표에서 플레이어 전방의 서로 다른 깊이와 좌우 위치에 비공격 적 8명을 배치한다.</summary>
    private void CreateUltimateTargets()
    {
        Vector3 origin = _player.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(_motor.ViewForward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        if (HorizontalDistance(origin + forward * 14.5f, _arenaCenter.position) > ARENA_RADIUS - 1f)
        {
            forward = Vector3.ProjectOnPlane(_arenaCenter.position - origin, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.back;
            _motor.FaceTarget(origin + forward * 10f + Vector3.up * 1.5f);
        }
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector2[] formation =
        {
            new Vector2(-6.5f, 8f), new Vector2(-1f, 7f), new Vector2(4.8f, 9f),
            new Vector2(-4f, 11.5f), new Vector2(3.5f, 10f),
            new Vector2(-6f, 14f), new Vector2(0.7f, 13f), new Vector2(6.4f, 14.5f)
        };
        for (int i = 0; i < ULTIMATE_TARGET_COUNT; i++)
        {
            Vector3 position = origin + right * formation[i].x + forward * formation[i].y;
            Vector3 arenaOffset = Vector3.ProjectOnPlane(position - _arenaCenter.position, Vector3.up);
            if (arenaOffset.magnitude > ARENA_RADIUS - 1f)
                position = _arenaCenter.position + arenaOffset.normalized * (ARENA_RADIUS - 1f);
            GameObject enemy = SpawnTrainingEnemy(position, true, false);
            _ultimateTargetPositions.Add(enemy.transform.position);
            _ultimateTargets.Add(enemy);
            EnemyHealth health = enemy.GetComponent<EnemyHealth>();
            health.Died += HandleUltimateTargetDied;
            _ultimateTargetHealths.Add(health);
        }
    }

    /// <summary>경기장 기준 원거리 연습 위치의 정면 SWORD_WAVE_TARGET_DISTANCE에 비공격 표적 2명을 배치한다.</summary>
    private void CreateSwordWaveTargets()
    {
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = _arenaCenter.position + forward * (WAVE_STAGE_START_DISTANCE + SWORD_WAVE_TARGET_DISTANCE);
        for (int i = 0; i < SWORD_WAVE_TARGET_COUNT; i++)
        {
            Vector3 position = center + right * ((i - (SWORD_WAVE_TARGET_COUNT - 1) * 0.5f)
                * SWORD_WAVE_TARGET_SPACING);
            _swordWaveTargetPositions.Add(position);
            _swordWaveTargets.Add(SpawnTrainingEnemy(position, true, false));
        }
    }

    /// <summary>극한 회피 또는 원거리 방어 단계에서 BambooThrower를 경기장 기준 고정 위치에 만들고 예약 투척을 준비한다.</summary>
    private void CreateTrainingThrower()
    {
        Vector3 position = _arenaCenter.position
            + Vector3.forward * (WAVE_STAGE_START_DISTANCE + THROWER_DISTANCE) + Vector3.up * 3f;
        _trainingThrower = Instantiate(_throwerPrefab, position, Quaternion.LookRotation(Vector3.back), _unitRoot);
        _nextThrowAt = Time.unscaledTime + 0.5f;
    }

    /// <summary>stage가 요구하는 실제 위치로 플레이어를 옮기고 입력·이동을 초기화한다.</summary>
    private void MovePlayer(Vector3 requestedPosition, Vector3 facing)
    {
        Vector3 position = requestedPosition;
        if (NavMesh.SamplePosition(requestedPosition, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            position = hit.position;
        CharacterController characterController = _player.GetComponent<CharacterController>();
        _motor.ClearInput();
        characterController.enabled = false;
        _player.transform.position = position;
        characterController.enabled = true;
        _motor.FaceTarget(position + Vector3.ProjectOnPlane(facing, Vector3.up).normalized * 10f + Vector3.up * 1.5f);
    }

    /// <summary>근접 연습 적이 사라지거나 경기장 기준 위치에서 18m 이상 벗어나면 같은 자리에 다시 배치한다.</summary>
    private void MaintainTrainingEnemy()
    {
        if (_stage != MELEE_STAGE || _advanceQueued) return;
        if (_trainingEnemy == null || _trainingEnemyHealth == null || !_trainingEnemyHealth.IsAlive)
        {
            DestroyTrainingEnemy();
            CreateTrainingEnemy(false, true);
            return;
        }
        Vector3 spawnPosition = _arenaCenter.position + Vector3.right * MELEE_STAGE_SIDE_OFFSET
            + Vector3.forward * PRACTICE_ENEMY_DISTANCE;
        if (HorizontalDistance(spawnPosition, _trainingEnemy.transform.position) <= ENEMY_REPLACE_DISTANCE) return;
        DestroyTrainingEnemy();
        CreateTrainingEnemy(false, true);
    }

    /// <summary>현재 근접 적과 플레이어의 거리 및 목표 순번으로 막기·쳐내기 힌트를 갱신한다.</summary>
    private void UpdateMeleeHint()
    {
        if (_stage != MELEE_STAGE || _trainingEnemy == null) return;
        float distance = HorizontalDistance(_player.transform.position, _trainingEnemy.transform.position);
        _meleeHintNearby = _meleeHintNearby
            ? distance <= MELEE_HINT_EXIT_DISTANCE : distance <= MELEE_HINT_ENTER_DISTANCE;
        _hud.SetDefenseHint(true, _meleeHintNearby, _objectiveIndex > 0);
        _hud.SetDefenseHintTarget(_trainingEnemy.transform.position + Vector3.up * 2.4f, _viewCamera);
    }

    /// <summary>근접 연습 적에게 막힌 공격을 받으면 짧은 간격으로 현재 방어 힌트를 다시 강조한다.</summary>
    private void HandleMeleeDamageBlocked(DamageContext context)
    {
        if (_stage != MELEE_STAGE || _advanceQueued || _transitionInputLocked
            || Time.unscaledTime - _lastMeleeHintEmphasisAt < MELEE_HINT_REPEAT_DELAY) return;
        _lastMeleeHintEmphasisAt = Time.unscaledTime;
        _hud.EmphasizeDefenseHint();
    }

    /// <summary>원거리 방어 단계에서 반사 수리검 등으로 쓰러진 표적을 플레이어가 자유 상태일 때 원래 위치에 다시 채운다.</summary>
    private void MaintainSwordWaveTargets()
    {
        if (_stage != RANGED_STAGE || _objectiveIndex != RANGED_SWORD_WAVE_OBJECTIVE
            || _advanceQueued || _player.CurrentState != PlayerActionState.Free) return;
        for (int i = 0; i < _swordWaveTargets.Count; i++)
        {
            GameObject enemy = _swordWaveTargets[i];
            EnemyHealth health = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            if (health != null && health.IsAlive) continue;
            if (enemy != null) Destroy(enemy);
            _swordWaveTargets[i] = SpawnTrainingEnemy(_swordWaveTargetPositions[i], true, false);
        }
    }

    /// <summary>궁극기 수락 전 사라진 대상이 있으면 원래 정면 배열 위치에 비공격 적을 다시 채운다.</summary>
    private void MaintainUltimateTargets()
    {
        if (_stage != FINAL_STAGE || _objectiveIndex != FINAL_STAGE_ULTIMATE_OBJECTIVE
            || _empower.IsStriking) return;
        for (int i = 0; i < _ultimateTargets.Count; i++)
        {
            GameObject enemy = _ultimateTargets[i];
            EnemyHealth health = enemy != null ? enemy.GetComponent<EnemyHealth>() : null;
            if (health != null && health.IsAlive) continue;
            if (enemy != null) Destroy(enemy);
            GameObject replacement = SpawnTrainingEnemy(_ultimateTargetPositions[i], true, false);
            _ultimateTargets[i] = replacement;
            EnemyHealth replacementHealth = replacement.GetComponent<EnemyHealth>();
            replacementHealth.Died += HandleUltimateTargetDied;
            _ultimateTargetHealths.Add(replacementHealth);
        }
    }

    /// <summary>현재 궁극기 목표의 8명이 모두 실제로 쓰러졌는지 확인해 완료 판정에 반환한다.</summary>
    private bool AreUltimateTargetsDefeated()
    {
        if (_ultimateTargets.Count != ULTIMATE_TARGET_COUNT) return false;
        foreach (GameObject enemy in _ultimateTargets)
        {
            if (enemy == null) continue;
            EnemyHealth health = enemy.GetComponent<EnemyHealth>();
            if (health != null && (health.IsAlive || !_defeatedUltimateTargets.Contains(health))) return false;
        }
        return _defeatedUltimateTargets.Count >= ULTIMATE_TARGET_COUNT;
    }

    /// <summary>궁극기 연습을 다시 할 수 있도록 기존 8명을 치우고 다음 거합 목표에서 새 적을 배치한다.</summary>
    private void ResetUltimateTargets()
    {
        foreach (EnemyHealth health in _ultimateTargetHealths)
            if (health != null) health.Died -= HandleUltimateTargetDied;
        _ultimateTargetHealths.Clear();
        _defeatedUltimateTargets.Clear();
        foreach (GameObject enemy in _ultimateTargets)
            if (enemy != null) Destroy(enemy);
        _ultimateTargets.Clear();
        _ultimateTargetPositions.Clear();
    }

    /// <summary>근접·원거리 방어 학습에서만 실제 막기 유지 시간과 현재 접촉 판정을 HUD에 전달한다.</summary>
    private void UpdateGuardTiming()
    {
        bool visible = _stage == MELEE_STAGE
            || (_stage == RANGED_STAGE && _objectiveIndex <= GetParryObjective());
        _hud.SetGuardTiming(visible, _player.IsGuarding, _combat.GuardHeldTime,
            _combat.GuardContactOutcome, _combat.GuardTimingWindow);
    }

    /// <summary>현재 2초 간격 시각에 연습 단계별 전조·투척을 시작한다. 극한 회피와 일섬 단계는 3발을 연속 발사한다.</summary>
    private void UpdateRangedThrower()
    {
        bool followUpStage = _stage == RANGED_STAGE || _stage == FINAL_STAGE;
        if (_stage != PERFECT_DODGE_STAGE && !followUpStage) return;
        if (_stage == FINAL_STAGE && (_empower.IsStriking || _empower.IsAutomaticCharging)) return;
        bool waitingForUltimateGauge = _stage == FINAL_STAGE && !_skillGauge.CanUseUltimate;
        if (followUpStage && IsFollowUpObjective() && !waitingForUltimateGauge) return;
        if (_trainingThrower == null)
        {
            CreateTrainingThrower();
            return;
        }
        if (Time.unscaledTime < _nextThrowAt) return;
        int shotCount = _stage == PERFECT_DODGE_STAGE || _stage == RANGED_STAGE
            ? RANGED_THROW_BURST_COUNT : 1;
        if (_trainingThrower.TryThrowForOnboarding(_director, shotCount, RANGED_THROW_BURST_GAP))
            _nextThrowAt = Time.unscaledTime + THROW_INTERVAL;
        else
            _nextThrowAt = Time.unscaledTime + 0.2f;
    }

    /// <summary>실제 적 접촉의 DefenseResolved 결과로 근접·원거리 방어 목표를 집계한다.</summary>
    private void HandleDefenseResolved(DefenseResolution resolution)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked
            || _ultimateMeterTutorialRunning) return;
        if (resolution.AttackId != 0 && resolution.HitId != 0)
        {
            long hitKey = ((long)resolution.AttackId << 32) | (uint)resolution.HitId;
            if (!_resolvedHitIds.Add(hitKey)) return;
        }
        ObjectiveDefenseResolved?.Invoke(resolution);

        if (_stage == PERFECT_DODGE_STAGE && _activeDodgeId != 0
            && _launchedShurikenAttackIds.Contains(resolution.AttackId))
        {
            if (resolution.Outcome == DefenseOutcome.DodgeAvoided)
            {
                _pendingPerfectDodgeAttackIds.Add(resolution.AttackId);
                if (_perfectDodgeAttackIds.Contains(resolution.AttackId)) CompleteCurrentObjective();
            }
            return;
        }

        if ((_stage == MELEE_STAGE || (_stage == RANGED_STAGE && _objectiveIndex <= GetParryObjective()))
            && IsCurrentStageAttack(resolution.AttackId))
        {
            bool guardSuccess = resolution.Outcome == DefenseOutcome.Guard
                || resolution.Outcome == DefenseOutcome.NormalParry
                || resolution.Outcome == DefenseOutcome.PerfectParry;
            if (_stage == RANGED_STAGE && _objectiveIndex == 0
                && resolution.Outcome == DefenseOutcome.PerfectParry)
                CompleteCurrentObjective(RANGED_SWORD_WAVE_OBJECTIVE);
            else if ((_objectiveIndex == 0 && guardSuccess)
                || (_objectiveIndex == 1 && resolution.Outcome == DefenseOutcome.PerfectParry))
                CompleteCurrentObjective();
        }
    }

    /// <summary>원거리 단계의 발사 ID 또는 훈련 적이 실제 실행한 공격 ID인지 확인한다.</summary>
    private bool IsCurrentStageAttack(int attackId)
    {
        if (_stage == RANGED_STAGE || _stage == FINAL_STAGE)
            return _launchedShurikenAttackIds.Contains(attackId);
        return _trainingEnemy != null && _trainingEnemy.activeInHierarchy
            && _trainingEnemyAttackIds.Contains(attackId);
    }

    /// <summary>훈련 적의 AttackCommitted context에서 AttackId를 받아 접촉 판정에서 출처를 확인하도록 저장한다.</summary>
    private void HandleTrainingEnemyAttackCommitted(EnemyAttackContext context)
    {
        _trainingEnemyAttackIds.Add(context.AttackId);
    }

    /// <summary>현재 연계 단계에서 방어 목표의 순번을 반환한다. 원거리 방어는 가드 다음 목표다.</summary>
    private int GetParryObjective()
    {
        if (_stage == MELEE_STAGE || _stage == RANGED_STAGE) return 1;
        return 0;
    }

    /// <summary>현재 목표가 패링 뒤의 검기·궁극기 연계 목표인지 반환한다.</summary>
    private bool IsFollowUpObjective()
    {
        return _objectiveIndex > GetParryObjective();
    }

    /// <summary>현재 회피 ID를 저장하고 실제 극한 회피 사건으로 회피 목표를 진행한다.</summary>
    private void HandleDodgeChanged(PlayerDodgeEvent dodgeEvent)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked) return;
        if (dodgeEvent.Stage == PlayerDodgeStage.Started)
        {
            _activeDodgeId = dodgeEvent.DodgeId;
        }
        else if (dodgeEvent.Stage == PlayerDodgeStage.Ended && _activeDodgeId == dodgeEvent.DodgeId)
        {
            _activeDodgeId = 0;
        }
        else if (dodgeEvent.Stage == PlayerDodgeStage.PerfectDodge)
        {
            _perfectDodgeAttackIds.Add(dodgeEvent.AttackId);
            if (_stage == PERFECT_DODGE_STAGE
                && _launchedShurikenAttackIds.Contains(dodgeEvent.AttackId))
            {
                _pendingPerfectDodgeAttackIds.Remove(dodgeEvent.AttackId);
                CompleteCurrentObjective();
            }
        }
    }

    /// <summary>실제 독립 게이지 변경을 마지막 단계의 백분율 목표 표시에 반영한다.</summary>
    private void HandleSkillGaugeChanged()
    {
        if (_stage != FINAL_STAGE || _objectiveIndex != 0) return;
        _hud.SetUltimateGaugeProgress(_skillGauge.UltimateNormalized);
        if (!_ultimateMeterTutorialSeen && !_ultimateMeterTutorialRunning
            && _skillGauge.UltimateGauge > 0f && !_transitionInputLocked && !_restartQueued)
        {
            _ultimateMeterTutorialRunning = true;
            StartCoroutine(ShowUltimateMeterTutorial());
            return;
        }
        if (_skillGauge.CanUseUltimate && _ultimateMeterTutorialSeen && !_advanceQueued
            && !_restartQueued && !_recoveryQueued && !_transitionInputLocked && !_ultimateMeterTutorialRunning)
        {
            _ultimateTutorialPending = true;
            CompleteCurrentObjective();
        }
    }

    /// <summary>강화 시작·종료 사건으로 실제 거합 시도를 구분하고 표적 처치 결과를 판정한다.</summary>
    private void HandleEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued) return;
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && empowerEvent.Stage == PlayerEmpowerStage.Started)
        {
            _ultimateStrikeStarted = true;
            return;
        }
        if (empowerEvent.Stage != PlayerEmpowerStage.Ended) return;
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && _ultimateStrikeStarted && empowerEvent.EndReason == PlayerEmpowerEndReason.Completed)
        {
            _ultimateStrikeStarted = false;
            if (AreUltimateTargetsDefeated()) QueueAdvance();
            else StartCoroutine(RecoverFailedUltimate());
        }
        else if (_stage == FINAL_STAGE && _ultimateStrikeStarted)
            _ultimateStrikeStarted = false;
    }

    /// <summary>검기 런처가 실제 DefenseSwordWave를 생성한 사건으로 원거리 방어 단계의 검기 목표를 완료한다.</summary>
    private void HandleSwordWaveLaunched(DefenseSwordWave wave)
    {
        if (!_transitionInputLocked && _stage == RANGED_STAGE && IsFollowUpObjective()) QueueAdvance();
    }

    /// <summary>
    /// 극한 회피·원거리 방어·거합 연습 중 실제 발사된 수리검 공격 ID를 기록한다.
    /// 목표 전환 중 발사된 수리검도 기록하고, 완료된 극한 회피 단계의 후속탄은 무해화한다.
    /// </summary>
    private void HandleShurikenLaunched(DefenseShuriken shuriken)
    {
        if ((_stage != PERFECT_DODGE_STAGE && _stage != RANGED_STAGE
            && _stage != FINAL_STAGE) || shuriken == null) return;
        _launchedShurikenAttackIds.Add(shuriken.AttackId);
        if (_stage == RANGED_STAGE || _stage == FINAL_STAGE)
        {
            _hintShurikens.Add(shuriken);
            shuriken.Released += HandleHintShurikenReleased;
        }
        if (_stage == PERFECT_DODGE_STAGE && _advanceQueued)
            shuriken.MakeHarmlessForOnboarding();
    }

    /// <summary>released 수리검을 안내 추적 목록에서 제거하고 현재 안내 대상이면 다시 선택하도록 비운다.</summary>
    private void HandleHintShurikenReleased(DefenseShuriken released)
    {
        released.Released -= HandleHintShurikenReleased;
        _hintShurikens.Remove(released);
        if (_hintShuriken == released) _hintShuriken = null;
    }

    /// <summary>4·5단계에서 화면에 보이는 적대 수리검 하나를 추적해 목표에 맞는 막기·쳐내기 문구를 표시한다.</summary>
    private void UpdateShurikenHint()
    {
        bool rangedDefense = _stage == RANGED_STAGE && _objectiveIndex <= GetParryObjective();
        if (!rangedDefense && _stage != FINAL_STAGE)
        {
            _hud.HideShurikenHint();
            return;
        }

        if (!IsVisibleHintShuriken(_hintShuriken))
        {
            _hintShuriken = null;
            float closestDistance = float.PositiveInfinity;
            foreach (DefenseShuriken shuriken in _hintShurikens)
            {
                if (!IsVisibleHintShuriken(shuriken)) continue;
                float distance = Vector3.SqrMagnitude(shuriken.transform.position - _player.transform.position);
                if (distance >= closestDistance) continue;
                closestDistance = distance;
                _hintShuriken = shuriken;
            }
        }
        if (_hintShuriken == null)
        {
            _hud.HideShurikenHint();
            return;
        }
        _hud.SetShurikenHint(_hintShuriken.transform.position, _viewCamera,
            _stage == FINAL_STAGE || _objectiveIndex > 0);
    }

    /// <summary>shuriken이 현재 씬에서 플레이어를 향해 비행 중이고 화면에 보이면 true를 반환한다.</summary>
    private bool IsVisibleHintShuriken(DefenseShuriken shuriken)
    {
        if (shuriken == null || !shuriken.IsHostile || shuriken.HasPassedPlayer
            || shuriken.gameObject.scene != gameObject.scene) return false;
        Vector3 viewport = _viewCamera.WorldToViewportPoint(shuriken.transform.position);
        return viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
            && viewport.y >= 0f && viewport.y <= 1f;
    }

    /// <summary>극한 회피 목표가 끝난 뒤 이미 비행 중인 훈련 수리검을 무해화해 남은 감속 연출 동안 피해를 막는다.</summary>
    private void MakePerfectDodgeShurikensHarmless()
    {
        DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
        foreach (DefenseShuriken shuriken in shurikens)
        {
            if (shuriken != null && shuriken.gameObject.scene == gameObject.scene && shuriken.IsFlying
                && _launchedShurikenAttackIds.Contains(shuriken.AttackId))
                shuriken.MakeHarmlessForOnboarding();
        }
    }

    /// <summary>실제 처치 가능 대상이 사망한 경우 공격 대상 처치 목표를 한 번 진행한다.</summary>
    private void HandleTrainingEnemyDied(EnemyHealth health)
    {
        if (_transitionInputLocked) return;
        if (_stage == 0 && _openingTargets.Remove(health))
        {
            health.Died -= HandleTrainingEnemyDied;
            _openingKillCount++;
            _hud.SetOpeningKillProgress(_openingKillCount, OPENING_TARGET_COUNT);
            _movementIdleElapsed = 0f;
            _movementGuideElapsed = 0f;
            if (_openingKillCount >= OPENING_TARGET_COUNT) CompleteCurrentObjective();
            return;
        }
        if (_stage == MELEE_STAGE && health == _trainingEnemyHealth) CompleteCurrentObjective();
    }

    /// <summary>단계 4의 기존 표적에서 실제 Died 사건을 받아 거합 시도 중 처치한 고유 대상을 기록한다.</summary>
    private void HandleUltimateTargetDied(EnemyHealth health)
    {
        if (_stage == FINAL_STAGE && _empower.IsStriking && _ultimateTargetHealths.Contains(health))
            _defeatedUltimateTargets.Add(health);
    }

    /// <summary>단계 0에서 3초간 실제 수평 이동이 없으면 중앙 안내를 띄우고 움직이면 즉시 감춘다.</summary>
    private void UpdateOpeningStage()
    {
        if (_motor.HorizontalSpeed > 0.05f)
        {
            _movementIdleElapsed = 0f;
            if (_showIdleMovementHint)
            {
                _showIdleMovementHint = false;
                _hud.SetIdleMovementHint(false);
            }
        }
        else
        {
            _movementIdleElapsed += Time.unscaledDeltaTime;
            if (!_showIdleMovementHint && _movementIdleElapsed >= MOVEMENT_IDLE_HINT_DELAY)
            {
                _showIdleMovementHint = true;
                _hud.SetIdleMovementHint(true);
            }
        }

        _movementGuideElapsed += Time.unscaledDeltaTime;
        if (!_movementWaveTutorialRunning && !_targetThreadTutorialRunning
            && _movementGuideElapsed >= MOVEMENT_GUIDE_PULSE_INTERVAL && !_perfectPulse.IsRunning)
        {
            _perfectPulse.ReplayStartPulseForOnboarding();
            _movementGuideElapsed = 0f;
        }

        if (_movementWaveTutorialSeen && !_targetThreadTutorialSeen && !_movementWaveTutorialRunning
            && !_targetThreadTutorialRunning && _dashTargetThread.HasLinkedTarget)
        {
            _targetThreadTutorialRunning = true;
            StartCoroutine(ShowTargetThreadTutorial(_dashTargetThread.LinkedTargetPosition));
        }
    }

    /// <summary>실제로 등록된 플레이어 이동 파동의 중심·설정·시작 시각을 받아 최초 설명을 예약한다.</summary>
    private void HandlePlayerRippleEmitted(Vector3 position, RevealRippleSettings settings, float startedAt)
    {
        if (_stage != 0 || _movementWaveTutorialSeen || _movementWaveTutorialRunning) return;
        _movementWaveTutorialRunning = true;
        StartCoroutine(ShowMovementWaveTutorial(position, settings, startedAt));
    }

    /// <summary>실제 이동 파동이 화면 중앙 부근까지 퍼질 때까지 기다려 링에 안내선을 잇고 최초 설명을 보인다.</summary>
    private IEnumerator ShowMovementWaveTutorial(Vector3 wavePosition, RevealRippleSettings settings, float startedAt)
    {
        Vector3 waveFront = wavePosition;
        bool reachedCenter = false;
        while (Time.time - startedAt < settings.ExpandDuration)
        {
            if (_stage != 0 || _restartQueued || _gameState.State != GameplayState.Playing)
            {
                _movementWaveTutorialRunning = false;
                yield break;
            }
            if (TryGetMovementWaveCenterPoint(wavePosition, settings, startedAt, out waveFront))
            {
                reachedCenter = true;
                break;
            }
            yield return null;
        }
        if (!reachedCenter)
        {
            _movementWaveTutorialRunning = false;
            yield break;
        }

        yield return new WaitForEndOfFrame();
        if (_stage != 0 || _restartQueued || _gameState.State != GameplayState.Playing)
        {
            _movementWaveTutorialRunning = false;
            yield break;
        }

        _movementWaveTutorialSeen = true;
        PauseTutorial();
        _hud.ShowTutorial(_hud.MovementWaveTutorialText, _hud.MovementWaveTutorialGamepadText);
        _hud.SetTutorialConnectorTarget(waveFront, _viewCamera);
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _inputManager.ConsumeOnboardingAttackUntilRelease();
        EndTutorialPause();
        _movementWaveTutorialRunning = false;
    }

    /// <summary>
    /// 실제 파동 설정과 시작 시각으로 현재 바닥 링을 계산해 화면 중앙에 가장 가까운 월드 좌표를 반환한다.
    /// 링이 중앙 부근에 닿거나 충분히 퍼진 뒤 중앙 시야의 바닥에 보일 때 true를 반환한다.
    /// </summary>
    private bool TryGetMovementWaveCenterPoint(Vector3 wavePosition, RevealRippleSettings settings,
        float startedAt, out Vector3 waveFront)
    {
        waveFront = wavePosition;
        float progress = Mathf.Clamp01((Time.time - startedAt) / settings.ExpandDuration);
        float radius = settings.Radius * (1f - (1f - progress) * (1f - progress));
        if (radius < MOVEMENT_WAVE_GUIDE_MINIMUM_RADIUS) return false;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float nearestDistance = float.PositiveInfinity;
        bool visibleNearCenter = false;
        for (int i = 0; i < MOVEMENT_WAVE_SCREEN_SAMPLES; i++)
        {
            float angle = i * Mathf.PI * 2f / MOVEMENT_WAVE_SCREEN_SAMPLES;
            Vector3 point = wavePosition + new Vector3(Mathf.Cos(angle) * radius, 0.02f,
                Mathf.Sin(angle) * radius);
            Vector3 screen = _viewCamera.WorldToScreenPoint(point);
            if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width
                || screen.y < 0f || screen.y > Screen.height) continue;
            if (Mathf.Abs(screen.x - screenCenter.x) <= Screen.width * 0.1f
                && screen.y >= Screen.height * 0.2f && screen.y <= Screen.height * 0.8f)
                visibleNearCenter = true;
            float distance = Vector2.Distance(screenCenter, new Vector2(screen.x, screen.y));
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            waveFront = point;
        }

        return nearestDistance <= Mathf.Min(Screen.width, Screen.height) * MOVEMENT_WAVE_CENTER_TOLERANCE
            || (progress >= MOVEMENT_WAVE_VISIBLE_FALLBACK_PROGRESS && visibleNearCenter);
    }

    /// <summary>연결된 발도 실의 검 쪽 시작점에 안내선을 잇고 같은 좌클릭으로 기존 공격 입력을 실행한다.</summary>
    private IEnumerator ShowTargetThreadTutorial(Vector3 targetPosition)
    {
        _targetThreadTutorialSeen = true;
        PauseTutorial();
        _hud.ShowTutorial(_hud.TargetThreadTutorialText, _hud.TargetThreadTutorialGamepadText);
        Vector3 guidePosition = _dashTargetThread.TryGetLinkedThreadOrigin(out Vector3 threadOrigin)
            ? threadOrigin : targetPosition;
        _hud.SetTutorialConnectorTarget(guidePosition, _viewCamera);
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _attackAfterTutorialUnlock = true;
        EndTutorialPause();
        _targetThreadTutorialRunning = false;
    }

    /// <summary>시간 정지 중 마우스 좌클릭 또는 현재 패드 공격 버튼이 한 번 눌릴 때까지 기다린다.</summary>
    private IEnumerator WaitForTutorialClick()
    {
        _tutorialAdvanceRequested = false;
        while (!_tutorialAdvanceRequested) yield return null;
        _tutorialAdvanceRequested = false;
    }

    /// <summary>TimeScale이 0이어도 마우스와 게임패드 공격 누름을 읽어 대기 중인 설명 입력으로 저장한다.</summary>
    private void UpdatePausedTutorialInput()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            _tutorialAdvanceRequested = true;
        if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame)
            _tutorialAdvanceRequested = true;
    }

    /// <summary>현재 온보딩 세션의 시간만 정지하고 입력을 잠가 안내가 끝날 때까지 상태를 보존한다.</summary>
    private void PauseTutorial()
    {
        _tutorialOwnsTransitionLock = !_transitionInputLocked;
        LockTransitionInput();
        _hud.HideShurikenHint();
        _tutorialPaused = true;
        _tutorialTimeRuntime = CombatSessionRuntime.Active;
        if (_tutorialTimeRuntime != null && _tutorialTimeRuntime.IsInitialized
            && _tutorialTimeRuntime.gameObject.scene == gameObject.scene)
            _tutorialTimeRuntime.SetFrozen(true);
    }

    /// <summary>안내가 멈춘 시간 상태를 풀고 안내가 소유한 경우에만 입력 잠금을 복원한다.</summary>
    private void EndTutorialPause()
    {
        if (!_tutorialPaused) return;
        _tutorialPaused = false;
        if (_tutorialTimeRuntime != null && _tutorialTimeRuntime.IsInitialized)
            _tutorialTimeRuntime.SetFrozen(false);
        _tutorialTimeRuntime = null;
        if (_tutorialOwnsTransitionLock) UnlockTransitionInput();
        _tutorialOwnsTransitionLock = false;
    }

    /// <summary>거합 단계의 첫 게이지 표시를 실제 원형 HUD에 연결해 소개한다.</summary>
    private IEnumerator ShowUltimateMeterTutorial()
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        while (_gameState.State == GameplayState.Playing && _health.IsAlive
            && (_player.CurrentState != PlayerActionState.Free
                || _combat.WeaponState != KatanaWeaponState.Sheathed))
            yield return null;
        if (_restartQueued || !_health.IsAlive)
        {
            _ultimateMeterTutorialRunning = false;
            yield break;
        }

        _ultimateMeterTutorialSeen = true;
        PauseTutorial();
        _hud.ShowTutorial(_hud.ParryHudTutorialText, _hud.ParryHudTutorialGamepadText);
        _hud.SetTutorialTarget(_ultimateHint.GaugeRect);
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _inputManager.ConsumeOnboardingAttackUntilRelease();
        EndTutorialPause();
        yield return ReadyStageAndUnlockInput();
        _ultimateMeterTutorialRunning = false;
    }

    /// <summary>거합 게이지가 가득 차면 실제 원형 HUD와 Q/Y 자동 차징 조작을 안내한다.</summary>
    private IEnumerator ShowUltimateTutorial()
    {
        _ultimateTutorialPending = false;
        PauseTutorial();
        _hud.ShowTutorial(_hud.UltimateReadyTutorialText, _hud.UltimateReadyTutorialGamepadText);
        _hud.SetTutorialTarget(_ultimateHint.GaugeRect);
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _inputManager.ConsumeOnboardingAttackUntilRelease();
        EndTutorialPause();
    }

    /// <summary>거합으로 대상 8명을 처치하지 못하면 표적만 복구하고 실제 게이지를 다시 채우게 한다.</summary>
    private IEnumerator RecoverFailedUltimate()
    {
        _recoveryQueued = true;
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        while (_player.CurrentState != PlayerActionState.Free)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            yield return null;
        }
        ResetUltimateTargets();
        _objectiveIndex = 0;
        _hud.ShowObjective(_stage, _objectiveIndex, true);
        _hud.SetUltimateGaugeProgress(_skillGauge.UltimateNormalized);
        CreateTrainingThrower();
        yield return ReadyStageAndUnlockInput();
        _recoveryQueued = false;
    }

    /// <summary>
    /// 현재 목표를 완료하고 nextObjectiveOverride가 지정되면 해당 목표로, 아니면 다음 목표로 진행한다.
    /// 목표 완료 사건을 발행하고 HUD의 현재 목표를 갱신한다.
    /// </summary>
    private void CompleteCurrentObjective(int nextObjectiveOverride = -1)
    {
        if (_advanceQueued) return;
        OnboardingHud.Prompt prompt = _hud.Prompts[_stage];
        int nextObjective = nextObjectiveOverride >= 0 ? nextObjectiveOverride : _objectiveIndex + 1;
        if (nextObjective < prompt.ObjectiveCount)
        {
            _advanceQueued = true;
            _hud.HideShurikenHint();
            ObjectiveCompleted?.Invoke(_stage, _objectiveIndex);
            StartCoroutine(AdvanceToNextObjective(nextObjective));
            return;
        }
        QueueAdvance();
    }

    /// <summary>
    /// nextObjective의 목표로 넘어가며 단계 완료 카드 없이 안내를 갱신한다.
    /// 첫 일섬 목표와 거합 목표에는 해당 전용 입력 설명을 보여 준다.
    /// </summary>
    private IEnumerator AdvanceToNextObjective(int nextObjective)
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if ((_stage == RANGED_STAGE || _stage == FINAL_STAGE) && nextObjective > GetParryObjective())
            ClearRangedPractice();
        bool showSwordWaveTutorial = _stage == RANGED_STAGE
            && nextObjective == RANGED_SWORD_WAVE_OBJECTIVE && !_swordWaveTutorialSeen;
        _objectiveIndex = nextObjective;
        if (_stage == RANGED_STAGE && _objectiveIndex == RANGED_SWORD_WAVE_OBJECTIVE
            && _swordWaveTargets.Count == 0)
            CreateSwordWaveTargets();
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && _ultimateTargets.Count == 0)
            CreateUltimateTargets();
        _hud.ShowObjective(_stage, _objectiveIndex, !showSwordWaveTutorial);
        if (_stage == MELEE_STAGE) UpdateMeleeHint();
        ObjectiveEntered?.Invoke(_stage, _objectiveIndex);
        _advanceQueued = false;
        if (showSwordWaveTutorial)
        {
            _swordWaveTutorialSeen = true;
            PauseTutorial();
            _hud.ShowTutorial(_hud.SwordWaveTutorialText, _hud.SwordWaveTutorialGamepadText);
            if (_tokenHud != null) _hud.SetTutorialTarget(_tokenHud.GetComponent<RectTransform>());
            yield return WaitForTutorialClick();
            _hud.HideTutorial();
            _inputManager.ConsumeOnboardingAttackUntilRelease();
            EndTutorialPause();
        }
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && _ultimateTutorialPending)
            yield return ShowUltimateTutorial();
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>
    /// 현재 단계의 중복 완료를 막고 다음 단계 전환을 예약한다.
    /// 극한 회피 단계의 남은 수리검은 무해화하고 마지막 단계의 수리검은 정리한다.
    /// </summary>
    private void QueueAdvance()
    {
        if (_advanceQueued) return;
        _advanceQueued = true;
        _hud.HideShurikenHint();
        if (_stage == PERFECT_DODGE_STAGE)
            MakePerfectDodgeShurikensHarmless();
        if (_stage == FINAL_STAGE)
            ClearRangedPractice();
        ObjectiveCompleted?.Invoke(_stage, _objectiveIndex);
        StartCoroutine(AdvanceAfterResolvedEvent());
    }

    /// <summary>
    /// 실제 판정 호출이 끝난 프레임 말에 입력을 잠그고 완료 선택에 따라 단계 반복·다음 단계·첫 막 전환을 진행한다.
    /// 극한 회피 단계는 수리검을 강조한 뒤 파동과 수리검이 정리될 때까지 기다린다.
    /// </summary>
    private IEnumerator AdvanceAfterResolvedEvent()
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if (_stage == PERFECT_DODGE_STAGE && !_perfectDodgeTutorialSeen)
        {
            PauseTutorial();
            _hud.ShowTutorial(_hud.PerfectDodgeTutorialText, _hud.PerfectDodgeTutorialGamepadText);
            if (TryGetVisibleShurikenPosition(out Vector3 shurikenPosition))
                _hud.SetTutorialTarget(shurikenPosition, _viewCamera, new Vector2(100f, 100f));
            else
                _hud.ClearTutorialTarget();
            yield return WaitForTutorialClick();

            _hud.HideTutorial();
            _inputManager.ConsumeOnboardingAttackUntilRelease();
            EndTutorialPause();
            _perfectDodgeTutorialSeen = true;
        }
        // 다음 단계의 시작 파동이 극한 회피 파동을 덮어쓰지 않고 남은 수리검 연출도 끝나도록 기다린다.
        while (_stage == PERFECT_DODGE_STAGE && (_perfectPulse.IsRunning || HasLaunchedShurikenInFlight()))
        {
            if (_restartQueued) yield break;
            yield return null;
        }
        PauseTutorial();
        _stageChoiceReceived = false;
        _stageChoiceActive = true;
        _cursorLockBeforeStageChoice = Cursor.lockState;
        _cursorVisibleBeforeStageChoice = Cursor.visible;
        _stageChoiceOwnsCursor = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        yield return _hud.ShowStageChoice(_stage, _stage == FINAL_STAGE, HandleStageChoiceSelected);
        while (!_stageChoiceReceived && !_restartQueued) yield return null;
        _stageChoiceActive = false;
        if (_restartQueued) yield break;
        yield return _hud.HideStageChoice();
        RestoreStageChoiceCursor();
        EndTutorialPause();

        if (_repeatStageSelected)
        {
            StageRepeated?.Invoke(_stage);
            _skillGauge.SetGauges(_stageEntrySwordWaveGauge, _stageEntryUltimateGauge);
        }
        int nextStage = _repeatStageSelected ? _stage : _stage + 1;
        if (nextStage > FINAL_STAGE)
        {
            CompleteOnboarding();
            yield break;
        }

        yield return _hud.FadeForReposition(() => EnterStage(nextStage));
        if (_restartQueued) yield break;
        // 극한 회피 단계는 대나무가 드러나지 않도록 시작 파동을 생략한다.
        if (_stage != PERFECT_DODGE_STAGE) _perfectPulse.ReplayStartPulseForOnboarding();
        _hud.PlayStageStart(_stage);
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>repeat 선택을 저장해 완료 화면의 대기 상태를 해제한다.</summary>
    private void HandleStageChoiceSelected(bool repeat)
    {
        if (_stageChoiceReceived) return;
        _repeatStageSelected = repeat;
        _stageChoiceReceived = true;
    }

    /// <summary>완료 선택 전에 사용하던 커서 잠금과 표시 상태를 복원하고 선택 화면의 커서 소유를 해제한다.</summary>
    private void RestoreStageChoiceCursor()
    {
        if (!_stageChoiceOwnsCursor) return;
        Cursor.lockState = _cursorLockBeforeStageChoice;
        Cursor.visible = _cursorVisibleBeforeStageChoice;
        _stageChoiceOwnsCursor = false;
    }

    /// <summary>현재 회피 학습 단계에서 발사된 수리검이 아직 비행 중인지 반환한다.</summary>
    private bool HasLaunchedShurikenInFlight()
    {
        DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
        foreach (DefenseShuriken shuriken in shurikens)
        {
            if (shuriken != null && shuriken.gameObject.scene == gameObject.scene && shuriken.IsFlying
                && _launchedShurikenAttackIds.Contains(shuriken.AttackId)) return true;
        }
        return false;
    }

    /// <summary>현재 씬에서 화면 안에 보이는 비행 수리검 하나의 위치를 찾아 안내 프레임에 반환한다.</summary>
    private bool TryGetVisibleShurikenPosition(out Vector3 position)
    {
        position = Vector3.zero;
        float closestToScreenCenter = float.PositiveInfinity;
        DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
        foreach (DefenseShuriken shuriken in shurikens)
        {
            if (shuriken == null || shuriken.gameObject.scene != gameObject.scene || !shuriken.IsFlying
                || !_launchedShurikenAttackIds.Contains(shuriken.AttackId)) continue;
            Vector3 viewport = _viewCamera.WorldToViewportPoint(shuriken.transform.position);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f
                || viewport.y < 0f || viewport.y > 1f) continue;
            float distance = Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f));
            if (distance >= closestToScreenCenter) continue;
            closestToScreenCenter = distance;
            position = shuriken.transform.position;
        }
        return !float.IsPositiveInfinity(closestToScreenCenter);
    }

    /// <summary>전환 카드와 재배치가 끝날 때까지 입력을 끄고 현재 입력 허용 상태를 기억한다.</summary>
    private void LockTransitionInput()
    {
        if (_transitionInputLocked) return;
        _inputWasEnabledBeforeTransition = _inputManager.IsGameplayInputEnabled;
        _transitionInputLocked = true;
        _inputManager.SetGameplayInputEnabled(false);
    }

    /// <summary>전환 전 입력 허용 상태를 복원하고 새 목표의 판정 입력을 다시 받는다.</summary>
    private void UnlockTransitionInput()
    {
        if (!_transitionInputLocked) return;
        _inputManager.SetGameplayInputEnabled(_inputWasEnabledBeforeTransition);
        _transitionInputLocked = false;
        if (_attackAfterTutorialUnlock && _inputManager.IsGameplayInputEnabled)
        {
            _attackAfterTutorialUnlock = false;
            _inputManager.ApplyOnboardingAttackInput();
        }
    }

    /// <summary>새 단계에서 플레이어가 자유 행동으로 돌아오면 전투 입력을 복원한다.</summary>
    private IEnumerator ReadyStageAndUnlockInput()
    {
        while (_stage == RANGED_STAGE || _stage == FINAL_STAGE)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            if (!_empower.IsStriking && !_empower.IsAutomaticCharging
                && !_empower.IsSwordWaveCasting && _player.CurrentState == PlayerActionState.Free) break;
            yield return null;
        }
        UnlockTransitionInput();
        _hud.SetMovementControls(_stage == 0);
    }

    /// <summary>온보딩 완료 후 0.5초 암전에서 1-1 진행 정보를 저장하고 BambooStage1을 연다.</summary>
    private void CompleteOnboarding()
    {
        _completingOnboarding = true;
        OnboardingCompleted?.Invoke();
        _retryPending = false;
        SceneTransitionOverlay.TransitionToScene(_stage1SceneName, STAGE1_FADE_DURATION, () =>
        {
            BambooStage1Progress.MarkStage1Started();
            _savedRunStage = 0;
            _restartStage = 0;
        });
    }

    /// <summary>사망 상태와 게임오버 정지가 확정되면 씬을 다시 불러 현재 단계 목표부터 시작한다.</summary>
    private IEnumerator RestartCurrentStage()
    {
        _restartQueued = true;
        StageRetrying?.Invoke(_stage);
        _restartStage = _stage;
        _retryPending = true;
        Game.Instance.UIManager.Hide<GameOverUI>();
        _gameState.Restart();
        yield break;
    }

    /// <summary>현재 위치·플레이어 위치의 수평 거리를 계산해 도착과 적 재배치 판정에 반환한다.</summary>
    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        Vector3 offset = Vector3.ProjectOnPlane(first - second, Vector3.up);
        return offset.magnitude;
    }

    /// <summary>적·투척수·수리검·이동 목표를 정리해 단계 변경과 사망 재시도에서 남은 입력·위협을 제거한다.</summary>
    private void CleanupTrainingContent()
    {
        DestroyTrainingEnemy();
        foreach (GameObject enemy in _ultimateTargets)
            if (enemy != null) Destroy(enemy);
        _ultimateTargets.Clear();
        _ultimateTargetPositions.Clear();
        foreach (GameObject enemy in _swordWaveTargets)
            if (enemy != null) Destroy(enemy);
        _swordWaveTargets.Clear();
        _swordWaveTargetPositions.Clear();
        ClearRangedPractice();
        foreach (EnemyHealth health in _openingTargets)
        {
            if (health == null) continue;
            health.Died -= HandleTrainingEnemyDied;
            Destroy(health.gameObject);
        }
        _openingTargets.Clear();
        foreach (EnemyHealth health in _ultimateTargetHealths)
            if (health != null) health.Died -= HandleUltimateTargetDied;
        _ultimateTargetHealths.Clear();
        _defeatedUltimateTargets.Clear();
    }

    /// <summary>현재 연습 투척 예약과 비행 수리검을 정리해 다음 목표에 남지 않도록 한다.</summary>
    private void ClearRangedPractice()
    {
        _hud.HideShurikenHint();
        _hintShuriken = null;
        foreach (DefenseShuriken shuriken in _hintShurikens)
            if (shuriken != null) shuriken.Released -= HandleHintShurikenReleased;
        _hintShurikens.Clear();
        if (_trainingThrower != null)
        {
            _trainingThrower.enabled = false;
            Destroy(_trainingThrower.gameObject);
            _trainingThrower = null;
        }
        DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
        foreach (DefenseShuriken shuriken in shurikens)
            if (shuriken != null && shuriken.gameObject.scene == gameObject.scene && shuriken.IsFlying)
                shuriken.Release();
    }

    /// <summary>현재 근접 적의 사망·AttackCommitted 구독을 해제하고 추적한 공격 ID를 지운 뒤 제거한다.</summary>
    private void DestroyTrainingEnemy()
    {
        if (_trainingEnemyHealth != null)
        {
            _trainingEnemyHealth.Died -= HandleTrainingEnemyDied;
            _trainingEnemyHealth.DamageBlocked -= HandleMeleeDamageBlocked;
        }
        if (_trainingEnemy != null)
        {
            foreach (EnemyAttackBehaviour attack in _trainingEnemy.GetComponents<EnemyAttackBehaviour>())
                attack.AttackCommitted -= HandleTrainingEnemyAttackCommitted;
            Destroy(_trainingEnemy);
        }
        _trainingEnemyAttackIds.Clear();
        _trainingEnemy = null;
        _trainingEnemyHealth = null;
    }
}
