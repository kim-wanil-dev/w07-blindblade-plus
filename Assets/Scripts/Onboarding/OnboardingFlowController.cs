using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using BlindBlade.Combat.Contracts;

/// <summary>BambooDefense 복제 씬의 순차 연습, 전투 판정 집계, 재시도와 BambooStage1 전환을 관리한다.</summary>
[DisallowMultipleComponent]
public sealed class OnboardingFlowController : MonoBehaviour
{
    private const int FINAL_STAGE = 8;
    private const float TARGET_DISTANCE = 5f;
    private const float TARGET_ARRIVAL_RADIUS = 1f;
    private const float PRACTICE_ENEMY_DISTANCE = 4f;
    private const float ENEMY_REPLACE_DISTANCE = 18f;
    private const float FOREST_EDGE_DISTANCE = 19.5f;
    private const float THROWER_DISTANCE = 12f;
    private const float THROW_INTERVAL = 2f;
    private const float HEALTH_REGEN_PER_SECOND = 45f;
    private const float STAMINA_REGEN_PER_SECOND = 100f;
    private const float STAGE1_FADE_DURATION = 0.5f;

    private static int _savedRunStage;
    private static int _restartStage;
    private static bool _retryPending;

    [Header("씬 연습 자산")]
    [SerializeField] private GameObject _meleePrefab;
    [SerializeField] private BambooThrower _throwerPrefab;
    [SerializeField] private FootstepWave _targetWavePrefab;
    [SerializeField] private Transform _unitRoot;
    [SerializeField] private Transform _arenaCenter;

    [Header("단계 연결")]
    [SerializeField] private OnboardingHud _hud;
    [SerializeField] private string _stage1SceneName = "BambooStage1";

    private readonly HashSet<long> _resolvedHitIds = new HashSet<long>();
    private readonly HashSet<int> _launchedShurikenAttackIds = new HashSet<int>();
    private readonly HashSet<int> _perfectDodgeAttackIds = new HashSet<int>();
    private readonly HashSet<int> _pendingPerfectDodgeAttackIds = new HashSet<int>();
    private PlayerController _player;
    private PlayerMotor _motor;
    private PlayerHealth _health;
    private PlayerStamina _stamina;
    private PlayerEmpower _empower;
    private PlayerPerfectPulse _perfectPulse;
    private PlayerSwordWaveLauncher _swordWave;
    private KatanaCombatCoordinator _combat;
    private BambooDefenseDirector _director;
    private GameStateManager _gameState;
    private InputManager _inputManager;
    private GameObject _trainingEnemy;
    private EnemyHealth _trainingEnemyHealth;
    private EnemyAttackSelector _trainingEnemyAttacks;
    private BambooThrower _trainingThrower;
    private FootstepWave _targetWave;
    private Vector3 _moveTarget;
    private int _stage;
    private int _objectiveIndex;
    private int _activeDodgeId;
    private bool _dodgeWasStarted;
    private bool _advanceQueued;
    private bool _restartQueued;
    private bool _recoveryQueued;
    private bool _transitionInputLocked;
    private bool _inputWasEnabledBeforeTransition;
    private bool _completingOnboarding;
    private float _nextThrowAt;

    /// <summary>새 Intro 시작을 위해 이번 온보딩 실행의 단계 기록과 재시도 플래그를 초기화한다.</summary>
    public static void StartNewRun()
    {
        _savedRunStage = 0;
        _restartStage = 0;
        _retryPending = false;
    }

    void Start()
    {
        StartCoroutine(BeginWhenGameplayIsReady());
    }

    void OnDisable()
    {
        if (!_completingOnboarding && _transitionInputLocked && _inputManager != null && _gameState != null
            && _gameState.State == GameplayState.Playing)
            UnlockTransitionInput();
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

        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked) return;
        if (_stage == 0 && HorizontalDistance(_player.transform.position, _moveTarget) <= TARGET_ARRIVAL_RADIUS)
            CompleteCurrentObjective();
        else if (_stage == 1 && _dodgeWasStarted && !_motor.IsDodging)
            CompleteCurrentObjective();

        MaintainTrainingEnemy();
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
        _motor = _player.GetComponent<PlayerMotor>();
        _health = _player.GetComponent<PlayerHealth>();
        _stamina = _player.GetComponent<PlayerStamina>();
        _empower = _player.GetComponent<PlayerEmpower>();
        _perfectPulse = _player.GetComponent<PlayerPerfectPulse>();
        _swordWave = _player.GetComponent<PlayerSwordWaveLauncher>();
        _combat = _player.GetComponent<KatanaCombatCoordinator>();
        _director = FindFirstObjectByType<BambooDefenseDirector>();
        _inputManager = Game.Instance.InputManager;
        SuppressGameOverScreen();
        if (_arenaCenter == null)
        {
            GameObject arena = GameObject.Find("Arena");
            _arenaCenter = arena != null ? arena.transform : transform;
        }
        if (_hud == null) _hud = GetComponent<OnboardingHud>();
        BindEvents();
        _stage = _retryPending ? _restartStage : _savedRunStage;
        _retryPending = false;
        LockTransitionInput();
        EnterStage(_stage);
        yield return ReadyStageAndUnlockInput();
    }

    /// <summary>실제 플레이어·투척수 사건에 구독해 전투 사실을 온보딩 목표에 연결한다.</summary>
    private void BindEvents()
    {
        _combat.Events.DefenseResolved += HandleDefenseResolved;
        _combat.Events.DodgeChanged += HandleDodgeChanged;
        _combat.Events.EmpowerChanged += HandleEmpowerChanged;
        _motor.DodgeStarted += HandleDodgeStarted;
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
        if (_motor != null) _motor.DodgeStarted -= HandleDodgeStarted;
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
        _dodgeWasStarted = false;
        _activeDodgeId = 0;
        _perfectDodgeAttackIds.Clear();
        _pendingPerfectDodgeAttackIds.Clear();
        _resolvedHitIds.Clear();
        _launchedShurikenAttackIds.Clear();

        PositionPlayerForStage(_stage);
        _hud.ShowObjective(_stage, _objectiveIndex);
        UpdateGuardTiming();

        if (_stage == 0)
            CreateMovementTarget();
        else if (_stage == 2 || _stage == 4)
            CreateTrainingEnemy(_stage == 2, _stage != 2);
        else if (_stage == 3 || _stage == 6)
            CreateTrainingThrower();
        else if (_stage == FINAL_STAGE)
            _empower.BeginOnboardingUltimate();
    }

    /// <summary>필요한 연습 위치를 NavMesh에서 찾고 플레이어와 시점을 단계에 맞게 배치한다.</summary>
    private void PositionPlayerForStage(int stage)
    {
        Vector3 center = _arenaCenter.position;
        Vector3 position = _player.transform.position;
        Vector3 facing = _motor.ViewForward;
        if (stage == 2)
        {
            position = center + Vector3.right * 4f;
            facing = Vector3.forward;
        }
        else if (stage == 5 || stage == 7 || stage == FINAL_STAGE)
        {
            position = center + Vector3.forward * FOREST_EDGE_DISTANCE;
            facing = Vector3.forward;
        }
        else if (stage == 6)
        {
            position = center;
            facing = Vector3.forward;
        }

        if (stage == 2 || stage == 5 || stage == 6 || stage == 7 || stage == FINAL_STAGE)
            MovePlayer(position, facing);
    }

    /// <summary>목표 위치를 플레이어 정면 TARGET_DISTANCE에 만들고 기존 Wave 프리팹으로 표시한다.</summary>
    private void CreateMovementTarget()
    {
        Vector3 forward = Vector3.ProjectOnPlane(_motor.ViewForward, Vector3.up).normalized;
        _moveTarget = _player.transform.position + forward * TARGET_DISTANCE;
        _targetWave = Instantiate(_targetWavePrefab, _moveTarget + Vector3.up * 0.01f, Quaternion.identity);
        _targetWave.Play(new Color(0.35f, 0.9f, 1f, 0.9f), 0.8f, 1800f);
    }

    /// <summary>기존 근접 적을 앞에 배치하고 killable 여부와 공격 활성 상태를 현재 단계에 맞춘다.</summary>
    private void CreateTrainingEnemy(bool killable, bool attacksPlayer)
    {
        Vector3 forward = Vector3.ProjectOnPlane(_motor.ViewForward, Vector3.up).normalized;
        Vector3 position = _player.transform.position + forward * PRACTICE_ENEMY_DISTANCE;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) position = hit.position;
        Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(_player.transform.position - position, Vector3.up));
        _trainingEnemy = Instantiate(_meleePrefab, position, rotation, _unitRoot);
        _trainingEnemyHealth = _trainingEnemy.GetComponent<EnemyHealth>();
        _trainingEnemyAttacks = _trainingEnemy.GetComponent<EnemyAttackSelector>();
        if (_trainingEnemyHealth != null)
        {
            _trainingEnemyHealth.SetInvulnerable(!killable);
            _trainingEnemyHealth.Died += HandleTrainingEnemyDied;
        }

        EnemyController controller = _trainingEnemy.GetComponent<EnemyController>();
        if (controller != null) controller.SetTarget(attacksPlayer ? _player.transform : null);
        foreach (EnemyAttackBehaviour attack in _trainingEnemy.GetComponents<EnemyAttackBehaviour>())
            attack.enabled = attacksPlayer;
        if (_trainingEnemyAttacks != null) _trainingEnemyAttacks.enabled = attacksPlayer;
    }

    /// <summary>극한 회피 또는 원거리 방어 단계에서 BambooThrower를 플레이어 앞에 두고 예약 투척을 준비한다.</summary>
    private void CreateTrainingThrower()
    {
        Vector3 forward = Vector3.ProjectOnPlane(_motor.ViewForward, Vector3.up).normalized;
        Vector3 position = _player.transform.position + forward * THROWER_DISTANCE + Vector3.up * 3f;
        _trainingThrower = Instantiate(_throwerPrefab, position, Quaternion.LookRotation(-forward), _unitRoot);
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

    /// <summary>현재 연습 적이 사라지거나 18m보다 멀어지면 같은 단계에 다시 배치한다.</summary>
    private void MaintainTrainingEnemy()
    {
        if ((_stage != 2 && _stage != 4) || _advanceQueued) return;
        if (_trainingEnemy == null || _trainingEnemyHealth == null || !_trainingEnemyHealth.IsAlive)
        {
            bool killable = _stage == 2;
            CreateTrainingEnemy(killable, !killable);
            return;
        }
        if (HorizontalDistance(_player.transform.position, _trainingEnemy.transform.position) <= ENEMY_REPLACE_DISTANCE) return;
        bool canDie = _stage == 2;
        DestroyTrainingEnemy();
        CreateTrainingEnemy(canDie, !canDie);
    }

    /// <summary>방어 단계에서 플레이어의 실제 가드 유지 시간과 현재 접촉 판정을 HUD에 전달한다.</summary>
    private void UpdateGuardTiming()
    {
        bool visible = _stage == 4 || _stage == 6;
        _hud.SetGuardTiming(visible, _player.IsGuarding, _combat.GuardHeldTime,
            _combat.GuardContactOutcome, _combat.GuardTimingWindow);
    }

    /// <summary>현재 2초 간격 시각에 BambooThrower가 예약 허용한 경우 실제 전조·투척 사이클을 시작한다.</summary>
    private void UpdateRangedThrower()
    {
        if (_stage != 3 && _stage != 6) return;
        if (_trainingThrower == null)
        {
            CreateTrainingThrower();
            return;
        }
        if (Time.unscaledTime < _nextThrowAt) return;
        RepositionTrainingThrowerIfNeeded();
        if (_trainingThrower.TryThrowForOnboarding(_director))
            _nextThrowAt = Time.unscaledTime + THROW_INTERVAL;
        else
            _nextThrowAt = Time.unscaledTime + 0.2f;
    }

    /// <summary>플레이어와 투척수의 거리가 발사 범위를 벗어나면 현재 시선 정면 12m로 투척수를 옮긴다.</summary>
    private void RepositionTrainingThrowerIfNeeded()
    {
        float distance = HorizontalDistance(_player.transform.position, _trainingThrower.transform.position);
        if (distance >= 7f && distance <= 28f) return;
        Vector3 forward = Vector3.ProjectOnPlane(_motor.ViewForward, Vector3.up).normalized;
        _trainingThrower.transform.position = _player.transform.position + forward * THROWER_DISTANCE + Vector3.up * 3f;
        _trainingThrower.transform.rotation = Quaternion.LookRotation(-forward);
    }

    /// <summary>실제 적 접촉의 DefenseResolved 결과로 현재 회피·가드·패링 목표만 한 번 집계한다.</summary>
    private void HandleDefenseResolved(DefenseResolution resolution)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked) return;
        if (resolution.AttackId != 0 && resolution.HitId != 0)
        {
            long hitKey = ((long)resolution.AttackId << 32) | (uint)resolution.HitId;
            if (!_resolvedHitIds.Add(hitKey)) return;
        }

        if (_stage == 3 && _activeDodgeId != 0 && _launchedShurikenAttackIds.Contains(resolution.AttackId))
        {
            if (resolution.Outcome == DefenseOutcome.DodgeAvoided)
            {
                _pendingPerfectDodgeAttackIds.Add(resolution.AttackId);
                if (_perfectDodgeAttackIds.Contains(resolution.AttackId)) CompleteCurrentObjective();
            }
            return;
        }

        if ((_stage == 4 || _stage == 6) && IsCurrentStageAttack(resolution.AttackId))
        {
            bool guardSuccess = resolution.Outcome == DefenseOutcome.Guard
                || resolution.Outcome == DefenseOutcome.NormalParry
                || resolution.Outcome == DefenseOutcome.PerfectParry;
            if ((_objectiveIndex == 0 && guardSuccess)
                || (_objectiveIndex == 1 && resolution.Outcome == DefenseOutcome.PerfectParry))
                CompleteCurrentObjective();
        }
    }

    /// <summary>현재 근접 적 또는 2초 주기 투척수가 시작한 공격 ID인지 확인한다.</summary>
    private bool IsCurrentStageAttack(int attackId)
    {
        if (_stage == 6) return _launchedShurikenAttackIds.Contains(attackId);
        return attackId == 0 && _trainingEnemy != null && _trainingEnemy.activeInHierarchy;
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
            if (_stage == 3
                && _pendingPerfectDodgeAttackIds.Remove(dodgeEvent.AttackId))
                CompleteCurrentObjective();
        }
    }

    /// <summary>기존 회피 이동의 실제 시작을 기록해 종료 뒤에만 돌진 연습을 완료한다.</summary>
    private void HandleDodgeStarted()
    {
        if (!_transitionInputLocked && _stage == 1) _dodgeWasStarted = true;
    }

    /// <summary>현재 강화 종료 이유를 읽어 궁극기 완료를 집계하거나 잘못 쓴 기술의 준비를 복구한다.</summary>
    private void HandleEmpowerChanged(PlayerEmpowerEvent empowerEvent)
    {
        if (_advanceQueued || _restartQueued || _recoveryQueued || _transitionInputLocked
            || empowerEvent.Stage != PlayerEmpowerStage.Ended) return;
        if (_stage == FINAL_STAGE && empowerEvent.EndReason == PlayerEmpowerEndReason.Completed)
            QueueAdvance();
        else if ((_stage == 5 || _stage == 7) && empowerEvent.EndReason == PlayerEmpowerEndReason.Completed)
            StartCoroutine(RecoverWrongEmpowerUse());
        else if (_stage == FINAL_STAGE && empowerEvent.EndReason == PlayerEmpowerEndReason.SwordWave)
            StartCoroutine(RecoverWrongEmpowerUse());
    }

    /// <summary>검기 런처가 실제 DefenseSwordWave를 생성한 사건으로 검기 목표를 완료한다.</summary>
    private void HandleSwordWaveLaunched(DefenseSwordWave wave)
    {
        if (!_transitionInputLocked && (_stage == 5 || _stage == 7)) QueueAdvance();
    }

    /// <summary>극한 회피·원거리 방어 중 실제 발사된 수리검 공격 ID를 기록한다.</summary>
    private void HandleShurikenLaunched(DefenseShuriken shuriken)
    {
        if (_transitionInputLocked || (_stage != 3 && _stage != 6) || shuriken == null) return;
        _launchedShurikenAttackIds.Add(shuriken.AttackId);
    }

    /// <summary>실제 처치 가능 대상이 사망한 경우 공격 대상 처치 목표를 한 번 진행한다.</summary>
    private void HandleTrainingEnemyDied(EnemyHealth health)
    {
        if (!_transitionInputLocked && _stage == 2 && health == _trainingEnemyHealth) CompleteCurrentObjective();
    }

    /// <summary>목표를 완료하고 다음 목표 문구 또는 다음 온보딩 단계로 진행한다.</summary>
    private void CompleteCurrentObjective()
    {
        if (_advanceQueued) return;
        OnboardingHud.Prompt prompt = _hud.Prompts[_stage];
        int nextObjective = _objectiveIndex + 1;
        if (nextObjective < prompt.ObjectiveCount)
        {
            _advanceQueued = true;
            StartCoroutine(AdvanceToNextObjective(nextObjective));
            return;
        }
        QueueAdvance();
    }

    /// <summary>nextObjective 앞에서 현재 목표 완료 표시를 보여 주고 같은 단계의 다음 목표 문구로 바꾼다.</summary>
    private IEnumerator AdvanceToNextObjective(int nextObjective)
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        yield return _hud.ShowObjectiveCompletion();
        if (_restartQueued) yield break;
        _objectiveIndex = nextObjective;
        _hud.ShowObjective(_stage, _objectiveIndex);
        _advanceQueued = false;
        UnlockTransitionInput();
    }

    /// <summary>중복 이벤트를 막고 다음 단계 이동 코루틴을 예약한다.</summary>
    private void QueueAdvance()
    {
        if (_advanceQueued) return;
        _advanceQueued = true;
        StartCoroutine(AdvanceAfterResolvedEvent());
    }

    /// <summary>실제 판정 호출이 끝난 프레임 말에 입력을 잠그고 완료 표시와 필요한 재배치를 진행한다.</summary>
    private IEnumerator AdvanceAfterResolvedEvent()
    {
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        if (_stage == 4 || _stage == 6) _empower.HoldOnboardingReady();
        int nextStage = _stage + 1;
        yield return _hud.ShowStageCompletion(_stage, nextStage <= FINAL_STAGE);
        if (_restartQueued) yield break;
        if (nextStage > FINAL_STAGE)
        {
            CompleteOnboarding();
            yield break;
        }

        if (nextStage == 2 || nextStage == 5 || nextStage == 6 || nextStage == 7)
            yield return _hud.FadeForReposition(() => EnterStage(nextStage));
        else
            EnterStage(nextStage);
        if (_restartQueued) yield break;
        _perfectPulse.ReplayStartPulseForOnboarding();
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
    }

    /// <summary>검기·궁극기 단계의 준비와 불릿 타임을 확인한 뒤 잠근 입력을 복원한다.</summary>
    private IEnumerator ReadyStageAndUnlockInput()
    {
        while (_stage == 5 || _stage == 7 || _stage == FINAL_STAGE)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            if (_stage == FINAL_STAGE)
            {
                if ((!_empower.IsReady || _empower.Tokens != _empower.MaxTokens)
                    && !_empower.IsStriking) _empower.BeginOnboardingUltimate();
            }
            else
                _empower.EnsureOnboardingSwordWaveReady();
            if (_empower.IsReady && _empower.IsBulletTimeActive && _player.CurrentState == PlayerActionState.Free
                && (_stage == FINAL_STAGE ? _empower.Tokens == _empower.MaxTokens
                    : _empower.Tokens < _empower.MaxTokens)) break;
            yield return null;
        }
        UnlockTransitionInput();
    }

    /// <summary>검기·궁극기를 목표와 다르게 사용하면 현재 행동 종료 뒤 해당 단계의 집중과 토큰을 다시 채운다.</summary>
    private IEnumerator RecoverWrongEmpowerUse()
    {
        _recoveryQueued = true;
        yield return new WaitForEndOfFrame();
        LockTransitionInput();
        while (_player.CurrentState != PlayerActionState.Free)
        {
            if (_restartQueued || _gameState.State != GameplayState.Playing || !_health.IsAlive) yield break;
            yield return null;
        }
        yield return ReadyStageAndUnlockInput();
        _recoveryQueued = false;
    }

    /// <summary>온보딩 완료 후 0.5초 암전에서 보스 체크포인트를 지우고 BambooStage1을 처음부터 연다.</summary>
    private void CompleteOnboarding()
    {
        _completingOnboarding = true;
        _retryPending = false;
        SceneTransitionOverlay.TransitionToScene(_stage1SceneName, STAGE1_FADE_DURATION, () =>
        {
            BambooStage1Progress.Clear();
            _savedRunStage = 0;
            _restartStage = 0;
        });
    }

    /// <summary>사망 상태와 게임오버 정지가 확정되면 씬을 다시 불러 현재 단계 목표부터 시작한다.</summary>
    private IEnumerator RestartCurrentStage()
    {
        _restartQueued = true;
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
        if (_trainingThrower != null)
        {
            Destroy(_trainingThrower.gameObject);
            _trainingThrower = null;
        }
        if (_targetWave != null)
        {
            Destroy(_targetWave.gameObject);
            _targetWave = null;
        }
        DefenseShuriken[] shurikens = FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None);
        foreach (DefenseShuriken shuriken in shurikens)
            if (shuriken != null && shuriken.gameObject.scene == gameObject.scene && shuriken.IsFlying)
                shuriken.Release();
    }

    /// <summary>현재 근접 적의 사망 구독을 해제하고 씬에서 제거한다.</summary>
    private void DestroyTrainingEnemy()
    {
        if (_trainingEnemyHealth != null) _trainingEnemyHealth.Died -= HandleTrainingEnemyDied;
        if (_trainingEnemy != null) Destroy(_trainingEnemy);
        _trainingEnemy = null;
        _trainingEnemyHealth = null;
        _trainingEnemyAttacks = null;
    }
}
