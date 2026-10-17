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
    private const int ULTIMATE_PARRY_OBJECTIVE = 2;
    private const int FINAL_STAGE_ULTIMATE_OBJECTIVE = 3;
    private const float ARENA_RADIUS = 23.6f;
    private const float MOVEMENT_IDLE_HINT_DELAY = 3f;
    private const float MOVEMENT_WAVE_GUIDE_MINIMUM_RADIUS = 2.5f;
    private const float MOVEMENT_WAVE_CENTER_TOLERANCE = 0.16f;
    private const float MOVEMENT_WAVE_VISIBLE_FALLBACK_PROGRESS = 0.55f;
    private const int MOVEMENT_WAVE_SCREEN_SAMPLES = 48;
    private const float MOVEMENT_GUIDE_PULSE_INTERVAL = 10f;
    private const int OPENING_TARGET_COUNT = 3;
    private const float PRACTICE_ENEMY_DISTANCE = 4f;
    private const float MELEE_STAGE_SIDE_OFFSET = 4f;
    private const float SWORD_WAVE_TARGET_DISTANCE = 9f;
    private const float ENEMY_REPLACE_DISTANCE = 18f;
    private const float WAVE_STAGE_START_DISTANCE = 5f;
    private const float ULTIMATE_TARGET_DISTANCE = 11f;
    private const float ULTIMATE_TARGET_SPACING = 2.4f;
    private const int ULTIMATE_TARGET_COUNT = 8;
    private const float OPENING_TARGET_FIRST_DISTANCE = 2f;
    private const float OPENING_TARGET_DISTANCE_STEP = 8.5f;
    private const int SWORD_WAVE_TARGET_COUNT = 2;
    private const float THROWER_DISTANCE = 12f;
    private const float THROW_INTERVAL = 2f;
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
    private static bool _parryMeterTutorialSeen;
    private static bool _parryRewardTutorialRunning;

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
    private readonly HashSet<int> _trainingEnemyAttackIds = new HashSet<int>();
    private readonly HashSet<int> _perfectDodgeAttackIds = new HashSet<int>();
    private readonly HashSet<int> _pendingPerfectDodgeAttackIds = new HashSet<int>();
    private readonly HashSet<int> _countedUltimateParryAttackIds = new HashSet<int>();
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
    private PlayerPerfectPulse _perfectPulse;
    private PlayerSwordWaveLauncher _swordWave;
    private PlayerDashTargetThread _dashTargetThread;
    private PlayerKatanaBeads _katanaBeads;
    private KatanaCombatCoordinator _combat;
    private BambooDefenseDirector _director;
    private GameStateManager _gameState;
    private InputManager _inputManager;
    private Camera _viewCamera;
    private BlindBlade.UI.CombatHud.CombatPerfectParryTokenHud _tokenHud;
    private GameObject _trainingEnemy;
    private EnemyHealth _trainingEnemyHealth;
    private BambooThrower _trainingThrower;
    private int _stage;
    private int _objectiveIndex;
    private int _activeDodgeId;
    private int _ultimateParryCount;
    private int _openingKillCount;
    private bool _advanceQueued;
    private bool _restartQueued;
    private bool _recoveryQueued;
    private bool _transitionInputLocked;
    private bool _inputWasEnabledBeforeTransition;
    private bool _completingOnboarding;
    private bool _playOpening;
    private bool _tutorialPaused;
    private bool _ultimateTutorialPending;
    private bool _showIdleMovementHint;
    private bool _tutorialAdvanceRequested;
    private bool _tutorialOwnsTransitionLock;
    private bool _attackAfterTutorialUnlock;
    private bool _movementWaveTutorialRunning;
    private bool _targetThreadTutorialRunning;
    private float _movementIdleElapsed;
    private float _movementGuideElapsed;
    private CombatSessionRuntime _tutorialTimeRuntime;
    private float _nextThrowAt;

    public event Action<int, int> ObjectiveEntered;
    public event Action<int, int> ObjectiveCompleted;
    public event Action<int> StageRetrying;
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
        _parryMeterTutorialSeen = false;
        _parryRewardTutorialRunning = false;
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
        if (!_completingOnboarding && _transitionInputLocked && _inputManager != null && _gameState != null
            && _gameState.State == GameplayState.Playing)
            UnlockTransitionInput();
        EndTutorialPause();
        _parryRewardTutorialRunning = false;
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

        if (_tutorialPaused)
        {
            UpdatePausedTutorialInput();
            return;
        }

        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked
            || _parryRewardTutorialRunning) return;
        if (_stage == 0) UpdateOpeningStage();

        MaintainTrainingEnemy();
        MaintainSwordWaveTargets();
        MaintainUltimateTargets();
        UpdateGuardTiming();
        UpdateRangedThrower();
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
        _perfectPulse = _player.GetComponent<PlayerPerfectPulse>();
        _swordWave = _player.GetComponent<PlayerSwordWaveLauncher>();
        _dashTargetThread = _player.GetComponent<PlayerDashTargetThread>();
        _katanaBeads = _player.GetComponent<PlayerKatanaBeads>();
        _viewCamera = _player.GetComponentInChildren<Camera>();
        _tokenHud = FindFirstObjectByType<BlindBlade.UI.CombatHud.CombatPerfectParryTokenHud>();
        _combat = _player.GetComponent<KatanaCombatCoordinator>();
        _director = FindFirstObjectByType<BambooDefenseDirector>();
        _inputManager = Game.Instance.InputManager;
        SuppressGameOverScreen();
        // 온보딩은 단계 안내가 같은 위치에 궁극기 안내를 직접 표시하므로 공용 안내를 끈다.
        _player.GetComponent<PlayerUltimateHint>().enabled = false;
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
        _ultimateParryCount = 0;
        _openingKillCount = 0;
        _movementIdleElapsed = 0f;
        _movementGuideElapsed = 0f;
        _showIdleMovementHint = false;
        _hud.SetIdleMovementHint(false);
        _hud.SetMovementControls(false);
        _tutorialAdvanceRequested = false;
        _ultimateTutorialPending = false;
        _perfectDodgeAttackIds.Clear();
        _pendingPerfectDodgeAttackIds.Clear();
        _countedUltimateParryAttackIds.Clear();
        _resolvedHitIds.Clear();
        _launchedShurikenAttackIds.Clear();

        if (_stage == RANGED_STAGE || _stage == FINAL_STAGE)
            _empower.PrepareOnboardingFollowUp(0);

        PositionPlayerForStage(_stage);
        _hud.ShowObjective(_stage, _objectiveIndex);
        ObjectiveEntered?.Invoke(_stage, _objectiveIndex);
        UpdateGuardTiming();

        if (_stage == 0)
            CreateOpeningTargets();
        else if (_stage == MELEE_STAGE)
            CreateTrainingEnemy(false, true);
        else if (_stage == RANGED_STAGE)
        {
            CreateSwordWaveTargets();
            CreateTrainingThrower();
        }
        else if (_stage == PERFECT_DODGE_STAGE)
            CreateTrainingThrower();
        else if (_stage == FINAL_STAGE)
        {
            CreateUltimateTargets();
            CreateTrainingThrower();
        }
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
            _trainingEnemyHealth.Died += HandleTrainingEnemyDied;
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

    /// <summary>경기장 기준 거합 연습 위치의 정면 11m에 비공격 적 8명을 중앙 이동 통로를 두고 배치한다.</summary>
    private void CreateUltimateTargets()
    {
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 center = _arenaCenter.position + forward * (WAVE_STAGE_START_DISTANCE + ULTIMATE_TARGET_DISTANCE);
        for (int i = 0; i < ULTIMATE_TARGET_COUNT; i++)
        {
            Vector3 position = center + right * ((i - (ULTIMATE_TARGET_COUNT - 1) * 0.5f)
                * ULTIMATE_TARGET_SPACING);
            _ultimateTargetPositions.Add(position);
            GameObject enemy = SpawnTrainingEnemy(position, true, false);
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
                * ULTIMATE_TARGET_SPACING);
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

    /// <summary>원거리 방어 단계에서 반사 수리검 등으로 쓰러진 표적을 플레이어가 자유 상태일 때 원래 위치에 다시 채운다.</summary>
    private void MaintainSwordWaveTargets()
    {
        if (_stage != RANGED_STAGE || _advanceQueued || _player.CurrentState != PlayerActionState.Free) return;
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
        if (_stage != FINAL_STAGE || _empower.IsStriking) return;
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

    /// <summary>궁극기 연습을 다시 할 수 있도록 기존 8명을 치우고 원래 전방 배열에 새 적을 배치한다.</summary>
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
        CreateUltimateTargets();
    }

    /// <summary>방어 단계의 가드·패링 목표에서 플레이어의 실제 가드 유지 시간과 현재 접촉 판정을 HUD에 전달한다.</summary>
    private void UpdateGuardTiming()
    {
        bool visible = _stage == MELEE_STAGE
            || (_stage == RANGED_STAGE && _objectiveIndex <= GetParryObjective())
            || (_stage == FINAL_STAGE && _objectiveIndex <= ULTIMATE_PARRY_OBJECTIVE);
        _hud.SetGuardTiming(visible, _player.IsGuarding, _combat.GuardHeldTime,
            _combat.GuardContactOutcome, _combat.GuardTimingWindow);
    }

    /// <summary>현재 2초 간격 시각에 BambooThrower가 예약 허용한 경우 실제 전조·투척 사이클을 시작한다.</summary>
    private void UpdateRangedThrower()
    {
        bool followUpStage = _stage == RANGED_STAGE || _stage == FINAL_STAGE;
        if (_stage != PERFECT_DODGE_STAGE && !followUpStage) return;
        bool waitingForUltimateFocus = _stage == FINAL_STAGE
            && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE && !_empower.IsReady;
        if (followUpStage && IsFollowUpObjective() && !waitingForUltimateFocus) return;
        if (_trainingThrower == null)
        {
            CreateTrainingThrower();
            return;
        }
        if (Time.unscaledTime < _nextThrowAt) return;
        if (_trainingThrower.TryThrowForOnboarding(_director))
            _nextThrowAt = Time.unscaledTime + THROW_INTERVAL;
        else
            _nextThrowAt = Time.unscaledTime + 0.2f;
    }

    /// <summary>실제 적 접촉의 DefenseResolved 결과로 현재 회피·가드·패링 목표만 한 번 집계한다.</summary>
    private void HandleDefenseResolved(DefenseResolution resolution)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked
            || _parryRewardTutorialRunning) return;
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
            if (resolution.Outcome == DefenseOutcome.PerfectParry && _stage == MELEE_STAGE
                && !_parryMeterTutorialSeen && !_parryRewardTutorialRunning)
            {
                _parryRewardTutorialRunning = true;
                StartCoroutine(ShowParryMeterTutorialThenAdvance(_stage, _objectiveIndex));
                return;
            }

            if (_stage == RANGED_STAGE && _objectiveIndex == 0
                && resolution.Outcome == DefenseOutcome.PerfectParry)
                CompleteCurrentObjective(RANGED_SWORD_WAVE_OBJECTIVE);
            else if ((_objectiveIndex == 0 && guardSuccess)
                || (_objectiveIndex == 1 && resolution.Outcome == DefenseOutcome.PerfectParry))
                CompleteCurrentObjective();
        }
        else if (_stage == FINAL_STAGE && _objectiveIndex <= ULTIMATE_PARRY_OBJECTIVE
            && _launchedShurikenAttackIds.Contains(resolution.AttackId)
            && resolution.Outcome == DefenseOutcome.PerfectParry
            && _countedUltimateParryAttackIds.Add(resolution.AttackId))
        {
            _ultimateParryCount = Mathf.Min(_ultimateParryCount + 1, ULTIMATE_PARRY_OBJECTIVE + 1);
            if (_ultimateParryCount >= ULTIMATE_PARRY_OBJECTIVE + 1)
                _ultimateTutorialPending = true;
            CompleteCurrentObjective();
        }
        else if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && !_empower.IsReady && !_ultimateTutorialPending
            && _launchedShurikenAttackIds.Contains(resolution.AttackId)
            && resolution.Outcome == DefenseOutcome.PerfectParry)
        {
            _ultimateTutorialPending = true;
            StartCoroutine(ShowUltimateTutorialAfterRefocus());
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

    /// <summary>현재 연계 단계에서 패링 목표의 순번을 반환한다. 원거리 방어는 가드 다음, 궁극기는 첫 목표다.</summary>
    private int GetParryObjective()
    {
        if (_stage == MELEE_STAGE || _stage == RANGED_STAGE) return 1;
        if (_stage == FINAL_STAGE) return ULTIMATE_PARRY_OBJECTIVE;
        return 0;
    }

    /// <summary>현재 목표가 패링 뒤의 검기·궁극기 연계 목표인지 반환한다.</summary>
    private bool IsFollowUpObjective()
    {
        return _objectiveIndex > GetParryObjective();
    }

    /// <summary>현재 회피 ID를 저장하고 실제 극한 회피 사건을 이번 실행 안에서 기록한다.</summary>
    private void HandleDodgeChanged(PlayerDodgeEvent dodgeEvent)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked) return;
        if (dodgeEvent.Stage == PlayerDodgeStage.Started)
            _activeDodgeId = dodgeEvent.DodgeId;
        else if (dodgeEvent.Stage == PlayerDodgeStage.Ended && _activeDodgeId == dodgeEvent.DodgeId)
            _activeDodgeId = 0;
        else if (dodgeEvent.Stage == PlayerDodgeStage.PerfectDodge)
        {
            _perfectDodgeAttackIds.Add(dodgeEvent.AttackId);
            if (_stage == PERFECT_DODGE_STAGE
                && _pendingPerfectDodgeAttackIds.Remove(dodgeEvent.AttackId))
                CompleteCurrentObjective();
        }
    }

    /// <summary>현재 강화 종료 이유와 목표 순번을 읽어 완료를 집계하거나 퍼펙트 패링 재시도를 예약한다.</summary>
    private void HandleEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued
            || empowerEvent.Stage != PlayerEmpowerStage.Ended) return;
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && empowerEvent.EndReason == PlayerEmpowerEndReason.Completed)
        {
            if (AreUltimateTargetsDefeated()) QueueAdvance();
            else StartCoroutine(RecoverFailedUltimate());
        }
        else if (_stage == RANGED_STAGE && IsFollowUpObjective()
            && empowerEvent.EndReason == PlayerEmpowerEndReason.Completed)
            StartCoroutine(RecoverWrongEmpowerUse(true));
        else if (_stage == FINAL_STAGE && _objectiveIndex <= FINAL_STAGE_ULTIMATE_OBJECTIVE
            && empowerEvent.EndReason == PlayerEmpowerEndReason.SwordWave)
            StartCoroutine(RecoverWrongEmpowerUse(false));
    }

    /// <summary>검기 런처가 실제 DefenseSwordWave를 생성한 사건으로 원거리 방어 단계의 검기 목표를 완료한다.</summary>
    private void HandleSwordWaveLaunched(DefenseSwordWave wave)
    {
        if (!_transitionInputLocked && _stage == RANGED_STAGE && IsFollowUpObjective()) QueueAdvance();
    }

    /// <summary>극한 회피·원거리 방어·궁극기 연습 중 실제 발사된 수리검 공격 ID를 기록한다.</summary>
    private void HandleShurikenLaunched(DefenseShuriken shuriken)
    {
        if (_transitionInputLocked || (_stage != PERFECT_DODGE_STAGE && _stage != RANGED_STAGE
            && _stage != FINAL_STAGE) || shuriken == null) return;
        _launchedShurikenAttackIds.Add(shuriken.AttackId);
    }

    /// <summary>실제 처치 가능 대상이 사망한 경우 공격 대상 처치 목표를 한 번 진행한다.</summary>
    private void HandleTrainingEnemyDied(EnemyHealth health)
    {
        if (_transitionInputLocked) return;
        if (_stage == 0 && _openingTargets.Remove(health))
        {
            health.Died -= HandleTrainingEnemyDied;
            _openingKillCount++;
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

    /// <summary>첫 실제 근접 쳐내기 뒤 HUD와 납도된 검 구슬을 차례로 강조하고 현재 목표를 계속 진행한다.</summary>
    private IEnumerator ShowParryMeterTutorialThenAdvance(int stage, int objective)
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if (_empower.IsReady) _empower.HoldOnboardingReady();
        while (_gameState.State == GameplayState.Playing && _health.IsAlive
            && (!_empower.IsReady || _player.CurrentState != PlayerActionState.Free
                || _combat.WeaponState != KatanaWeaponState.Sheathed))
            yield return null;
        if (_restartQueued || !_health.IsAlive)
        {
            _parryRewardTutorialRunning = false;
            yield break;
        }

        _parryMeterTutorialSeen = true;
        PauseTutorial();
        _hud.ShowTutorial(_hud.ParryHudTutorialText, _hud.ParryHudTutorialGamepadText);
        if (_tokenHud != null) _hud.SetTutorialTarget(_tokenHud.GetComponent<RectTransform>());
        yield return WaitForTutorialClick();

        _hud.ShowTutorial(_hud.ParryBeadsTutorialText, _hud.ParryBeadsTutorialGamepadText);
        if (_katanaBeads.TryGetBeadGuidePosition(out Vector3 beadPosition))
            _hud.SetTutorialTarget(beadPosition, _viewCamera, new Vector2(76f, 76f));
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _inputManager.ConsumeOnboardingAttackUntilRelease();
        EndTutorialPause();
        _parryRewardTutorialRunning = false;
        if (!_restartQueued && _stage == stage && _objectiveIndex == objective)
            CompleteCurrentObjective();
    }

    /// <summary>세 번째 궁극기 쳐내기 뒤 실제 거합 게이지를 강조하고 같은 누름을 차징 입력으로 넘긴다.</summary>
    private IEnumerator ShowUltimateTutorial()
    {
        _ultimateTutorialPending = false;
        PauseTutorial();
        _hud.ShowTutorial(_hud.UltimateReadyTutorialText, _hud.UltimateReadyTutorialGamepadText);
        if (_tokenHud != null) _hud.SetTutorialTarget(_tokenHud.GetComponent<RectTransform>());
        yield return WaitForTutorialClick();
        _hud.HideTutorial();
        _attackAfterTutorialUnlock = true;
        EndTutorialPause();
    }

    /// <summary>
    /// 거합 안내 전에 일섬을 써 집중이 끝나면 수리검 쳐내기 후 설명을 다시 표시한다.
    /// 안내를 닫은 입력을 거합 차징으로 전달하고 잠긴 전투 입력을 복원한다.
    /// </summary>
    private IEnumerator ShowUltimateTutorialAfterRefocus()
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if (_empower.IsReady) _empower.HoldOnboardingReady();
        while (_gameState.State == GameplayState.Playing && _health.IsAlive
            && (!_empower.IsReady || _player.CurrentState != PlayerActionState.Free))
            yield return null;
        if (_restartQueued || _stage != FINAL_STAGE || !_empower.IsReady) yield break;
        _empower.HoldOnboardingReady();
        ClearRangedPractice();
        yield return ShowUltimateTutorial();
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>대상 8명 처치에 실패한 궁극기 발동 뒤 표적과 게이지를 0으로 초기화하고 수리검 목표를 재개한다.</summary>
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
        _empower.PrepareOnboardingFollowUp(0);
        _ultimateParryCount = 0;
        _countedUltimateParryAttackIds.Clear();
        ResetUltimateTargets();
        _objectiveIndex = 0;
        _hud.ShowObjective(_stage, _objectiveIndex, true);
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
            ObjectiveCompleted?.Invoke(_stage, _objectiveIndex);
            StartCoroutine(AdvanceToNextObjective(nextObjective));
            return;
        }
        QueueAdvance();
    }

    /// <summary>
    /// nextObjective의 목표로 넘어가며 단계 완료 카드 없이 안내를 갱신한다.
    /// 거합 게이지를 모으는 중에는 집중을 고정하지 않고, 최종 거합 목표에서만 집중을 유지한다.
    /// </summary>
    private IEnumerator AdvanceToNextObjective(int nextObjective)
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if ((_stage == RANGED_STAGE || _stage == FINAL_STAGE) && nextObjective > GetParryObjective())
        {
            _empower.HoldOnboardingReady();
            ClearRangedPractice();
        }
        _objectiveIndex = nextObjective;
        _hud.ShowObjective(_stage, _objectiveIndex, true);
        ObjectiveEntered?.Invoke(_stage, _objectiveIndex);
        _advanceQueued = false;
        if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE
            && _ultimateTutorialPending)
            yield return ShowUltimateTutorial();
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>중복 이벤트를 막고 다음 단계 이동 코루틴을 예약한다.</summary>
    private void QueueAdvance()
    {
        if (_advanceQueued) return;
        _advanceQueued = true;
        ObjectiveCompleted?.Invoke(_stage, _objectiveIndex);
        StartCoroutine(AdvanceAfterResolvedEvent());
    }

    /// <summary>
    /// 실제 판정 호출이 끝난 프레임 말에 입력을 잠그고 완료 표시와 필요한 재배치를 진행한다.
    /// 극한 회피 단계는 파동 확산이 끝나면 안내를 멈춰 보여 준 뒤 파동이 사라질 때까지 기다린다.
    /// </summary>
    private IEnumerator AdvanceAfterResolvedEvent()
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if (_stage == PERFECT_DODGE_STAGE && !_perfectDodgeTutorialSeen)
        {
            while (_perfectPulse.IsRunning
                && _perfectPulse.ExpansionElapsed < _perfectPulse.ActiveExpandDuration)
            {
                if (_restartQueued) yield break;
                yield return null;
            }
            if (_perfectPulse.IsRunning && !_restartQueued)
            {
                PauseTutorial();
                _hud.ShowTutorial(_hud.PerfectDodgeTutorialText, _hud.PerfectDodgeTutorialGamepadText);
                yield return WaitForTutorialClick();
                _hud.HideTutorial();
                _inputManager.ConsumeOnboardingAttackUntilRelease();
                EndTutorialPause();
                _perfectDodgeTutorialSeen = true;
            }
        }
        // 다음 단계의 시작 파동이 극한 회피 파동을 덮어쓰지 않도록 끝까지 보여 준다.
        while (_stage == PERFECT_DODGE_STAGE && _perfectPulse.IsRunning)
        {
            if (_restartQueued) yield break;
            yield return null;
        }
        int nextStage = _stage + 1;
        yield return _hud.ShowStageCompletion(_stage, nextStage <= FINAL_STAGE);
        if (_restartQueued) yield break;
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

    /// <summary>
    /// 현재 단계와 목표의 집중 상태 및 플레이어 행동을 확인해 잠긴 입력을 복원한다.
    /// 거합 게이지 수집 목표에서는 집중 중에도 자유 상태가 되면 입력을 열어 다음 쳐내기를 허용한다.
    /// </summary>
    private IEnumerator ReadyStageAndUnlockInput()
    {
        while (_stage == RANGED_STAGE || _stage == FINAL_STAGE)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            if (!IsFollowUpObjective())
            {
                bool collectingUltimateTokens = _stage == FINAL_STAGE
                    && _objectiveIndex <= ULTIMATE_PARRY_OBJECTIVE;
                if ((collectingUltimateTokens || !_empower.IsReady) && !_empower.IsStriking
                    && _player.CurrentState == PlayerActionState.Free) break;
            }
            else
            {
                if (!_empower.IsReady && !_empower.IsStriking
                    && _player.CurrentState == PlayerActionState.Free)
                {
                    if (_stage == FINAL_STAGE && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE)
                    {
                        CreateThrowerIfMissing();
                        break;
                    }
                    // 집중이 끝나 연계를 쓸 수 없으면 가드가 아닌 패링 목표로 돌아간다.
                    _objectiveIndex = GetParryObjective();
                    _hud.ShowObjective(_stage, _objectiveIndex, true);
                    CreateTrainingThrower();
                    break;
                }
                if (_empower.IsReady && _empower.IsBulletTimeActive
                    && _player.CurrentState == PlayerActionState.Free) break;
            }
            yield return null;
        }
        UnlockTransitionInput();
        _hud.SetMovementControls(_stage == 0);
    }

    /// <summary>잘못된 연계 공격이 끝나면 spentUltimateTokens에 따라 토큰·표적을 준비하고 패링 목표로 돌아간다.</summary>
    private IEnumerator RecoverWrongEmpowerUse(bool spentUltimateTokens)
    {
        _recoveryQueued = true;
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        while (_player.CurrentState != PlayerActionState.Free)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            yield return null;
        }
        int startingTokens = _stage == FINAL_STAGE
            ? _empower.Tokens
            : 0;
        _empower.PrepareOnboardingFollowUp(startingTokens);
        if (_stage == FINAL_STAGE)
        {
            ResetUltimateTargets();
            if (spentUltimateTokens)
            {
                startingTokens = 0;
                _empower.PrepareOnboardingFollowUp(startingTokens);
                _ultimateParryCount = 0;
                _countedUltimateParryAttackIds.Clear();
                _objectiveIndex = 0;
            }
            else
                _objectiveIndex = _ultimateParryCount >= ULTIMATE_PARRY_OBJECTIVE + 1
                    ? FINAL_STAGE_ULTIMATE_OBJECTIVE
                    : Mathf.Min(_ultimateParryCount, ULTIMATE_PARRY_OBJECTIVE);
        }
        else
            _objectiveIndex = GetParryObjective();
        _hud.ShowObjective(_stage, _objectiveIndex, true);
        CreateThrowerIfMissing();
        if (_stage == FINAL_STAGE && !spentUltimateTokens
            && _objectiveIndex == FINAL_STAGE_ULTIMATE_OBJECTIVE && !_empower.IsReady)
        {
            _recoveryQueued = false;
            UnlockTransitionInput();
            yield break;
        }
        yield return ReadyStageAndUnlockInput();
        _recoveryQueued = false;
    }

    /// <summary>활성 원거리 목표에 투척수가 없으면 2초 간격 투척 연습을 다시 준비한다.</summary>
    private void CreateThrowerIfMissing()
    {
        if (_trainingThrower == null) CreateTrainingThrower();
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
        if (_trainingEnemyHealth != null) _trainingEnemyHealth.Died -= HandleTrainingEnemyDied;
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
