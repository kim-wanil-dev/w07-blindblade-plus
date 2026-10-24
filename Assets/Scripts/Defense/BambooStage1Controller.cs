using System.Collections;

using UnityEngine;
using UnityEngine.AI;

using TMPro;

/// <summary>일반 적 처치 수에 따라 보스 경고, 원형 공터 가장자리 등장 연출과 스테이지 완료를 관리한다.</summary>
// 등장 흔들림은 PlayerMotor(0)와 GameSceneInitializer(100) 뒤, 칼 궤적(200)·돌진 대상 실(300)·칼 구슬(310) 앞에 적용한다.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
public sealed class BambooStage1Controller : MonoBehaviour
{
    private const int ENTRANCE_WAVE_LAYERS = 5;
    private const float ACT_TITLE_DURATION = 1.5f;
    private const float ACT_COMPLETE_DELAY = 1.5f;

    [Header("스테이지 연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField] private DefenseArenaBoundary _arena;
    [SerializeField] private GameObject _boss;
    [SerializeField] private CanvasGroup _warning;

    [Header("보스 등장 조건과 위치")]
    [SerializeField]
    [InspectorName("시작 시 즉시 보스전")]
    [Tooltip("켜면 일반 적 웨이브와 보스 경고를 건너뛰고 보스전으로 시작합니다. 보스 체크포인트가 저장되므로 일반 웨이브로 돌아가려면 스테이지 진행 정보를 초기화해야 합니다.")]
    private bool _startBossImmediately;
    [SerializeField, Min(1)] private int _requiredKills = 5;
    [SerializeField, Min(0f)] private float _warningDuration = 2f;
    [SerializeField, Min(0f)] private float _edgeInset = 2f;
    [SerializeField, Min(0.1f)] private float _spawnSampleRadius = 2f;

    [Header("보스 등장 연출")]
    [SerializeField] private GameObject _entranceWavePrefab;
    [SerializeField] private Color _entranceWaveColor = new Color(1f, 0.08f, 0.08f, 1f);
    [SerializeField, Min(0f)] private float _entranceWaveRadius = 48f;
    [SerializeField, Min(0f)] private float _entranceWaveThickness = 2f;
    [SerializeField, Range(1, 6)] private int _entranceWaveCount = 3;
    [SerializeField, Min(0.01f)] private float _entranceWaveInterval = 0.32f;
    [SerializeField, Min(0.01f)] private float _entranceWaveDuration = 1.5f;
    [SerializeField, Min(0f)] private float _cameraShakeAmplitude = 0.13f;
    [SerializeField, Min(0.01f)] private float _cameraShakeDuration = 0.55f;
    [SerializeField, Min(0.1f)] private float _cameraShakeFrequency = 24f;

    private EnemyHealth _bossHealth;
    private Coroutine _warningRoutine;
    private Coroutine _actTitleRoutine;
    private Coroutine _entranceWaveRoutine;
    private Coroutine _stageTransitionRoutine;
    private Coroutine _deathFocusRoutine;
    private ActCompleteUI _actCompleteUi;
    private Camera _playerCamera;
    private float _shakeElapsed;
    private StagePhase _phase;

    public bool BossDefeated => _phase == StagePhase.Complete;
    public string CurrentTelemetryStageId => _phase == StagePhase.Boss || _phase == StagePhase.Complete ? "1-2" : "1-1";
    public string CurrentTelemetryEntryMode { get; private set; } = "fresh";
    public int TelemetryRequiredKills => _requiredKills;
    public bool TelemetryStartsBossImmediately => _startBossImmediately;
    public Vector3 TelemetryArenaCenter => _arena.transform.position;
    public float TelemetryArenaRadius => _arena.Radius;

    private enum StagePhase
    {
        Wave,
        Warning,
        Boss,
        Complete
    }

    void Awake()
    {
        _bossHealth = _boss.GetComponent<EnemyHealth>();
        _director.SetKillGoal(_requiredKills);
        _warning.alpha = 0f;
        _warning.blocksRaycasts = false;
        _warning.interactable = false;
    }

    void Start()
    {
        _actTitleRoutine = StartCoroutine(ShowActTitle());
        BambooStage1Progress.MarkStage1Started();
        CurrentTelemetryEntryMode = _startBossImmediately ? "editor_skip"
            : BambooStage1Progress.BossCheckpointReached ? "checkpoint_retry" : CombatTelemetryRunContext.EntryMode;
        if (!_startBossImmediately && !BambooStage1Progress.BossCheckpointReached) return;

        string startReason = _startBossImmediately ? "Inspector 설정" : "저장된 보스 체크포인트";
        Debug.Log($"BambooStage1 {startReason}으로 일반 적 웨이브를 건너뛰고 보스전을 시작합니다.", this);

        PlayerController player = Game.Player;
        Vector3 forward = Vector3.ProjectOnPlane(player.GetComponent<PlayerMotor>().ViewForward, Vector3.up);
        if (!TryFindBossRetryPosition(forward, out Vector3 bossPosition))
        {
            Debug.LogError("BambooStage1 보스 재도전 위치를 NavMesh에서 찾지 못했습니다.", this);
            return;
        }

        _director.StopWave();
        Vector3 center = _arena.transform.position;
        player.transform.position = new Vector3(center.x, player.transform.position.y, center.z);
        player.GetComponent<PlayerPerfectPulse>().SuppressStartPulse();
        ActivateBoss(bossPosition, false);
        player.GetComponent<PlayerMotor>().FaceTarget(_boss.transform.position + Vector3.up);
    }

    void OnEnable()
    {
        _bossHealth.Dying += HandleBossDying;
        _bossHealth.Died += HandleBossDied;
    }

    void Update()
    {
        if (_phase != StagePhase.Wave || !Game.IsInitialized || Game.Instance.GameStateManager == null
            || Game.Instance.GameStateManager.State != GameplayState.Playing
            || _director.Kills < _requiredKills)
            return;

        BeginBossWarning();
    }

    void LateUpdate()
    {
        if (_playerCamera == null || _shakeElapsed >= _cameraShakeDuration)
            return;

        _shakeElapsed = Mathf.Min(_cameraShakeDuration, _shakeElapsed + Time.unscaledDeltaTime);
        float fade = 1f - _shakeElapsed / _cameraShakeDuration;
        float phase = _shakeElapsed * _cameraShakeFrequency * Mathf.PI * 2f;
        Vector3 offset = new Vector3(Mathf.Sin(phase), Mathf.Cos(phase * 1.37f), 0f)
            * (_cameraShakeAmplitude * fade);
        // PlayerMotor가 카메라 기본 위치를 갱신한 뒤, 칼 실처럼 월드 좌표로 그리는 표현보다 먼저 흔들림을 더한다.
        _playerCamera.transform.localPosition += offset;
    }

    void OnDisable()
    {
        _bossHealth.Dying -= HandleBossDying;
        _bossHealth.Died -= HandleBossDied;
        if (_warningRoutine != null)
            StopCoroutine(_warningRoutine);
        if (_actTitleRoutine != null)
            StopCoroutine(_actTitleRoutine);
        if (_entranceWaveRoutine != null)
            StopCoroutine(_entranceWaveRoutine);
        if (_stageTransitionRoutine != null)
            StopCoroutine(_stageTransitionRoutine);
        if (_deathFocusRoutine != null)
            StopCoroutine(_deathFocusRoutine);
        _warningRoutine = null;
        _actTitleRoutine = null;
        _entranceWaveRoutine = null;
        _stageTransitionRoutine = null;
        _deathFocusRoutine = null;
        if (_actCompleteUi != null) Destroy(_actCompleteUi.gameObject);
        _actCompleteUi = null;
        if (_warning != null) _warning.alpha = 0f;
        _playerCamera = null;
    }

    /// <summary>Director의 처치 목표 달성 시 웨이브를 끝내고 1-1 완료 로그를 발행한 뒤 보스 경고를 시작한다.</summary>
    private void BeginBossWarning()
    {
        if (_actTitleRoutine != null)
        {
            StopCoroutine(_actTitleRoutine);
            _actTitleRoutine = null;
        }
        _warning.GetComponentInChildren<TMP_Text>(true).text = "강적 출현";
        _phase = StagePhase.Warning;
        _director.StopWave();
        BlindBlade.Telemetry.CombatTelemetrySignals.Stage("1-1", true);
        _warning.alpha = 1f;
        _warningRoutine = StartCoroutine(ShowWarningThenBoss());
    }

    /// <summary>warningDuration 뒤 플레이어에서 가장 먼 공터 NavMesh 지점에 보스를 켜고 등장 연출을 시작한다.</summary>
    private IEnumerator ShowWarningThenBoss()
    {
        float remaining = _warningDuration;

        while (true)
        {
            GameplayState state = Game.Instance.GameStateManager.State;

            if (state == GameplayState.Paused)
            {
                yield return null;
                continue;
            }

            if (state != GameplayState.Playing || Game.Player == null)
            {
                _warning.alpha = 0f;
                _warningRoutine = null;
                yield break;
            }

            if (remaining <= 0f)
                break;

            remaining -= Time.unscaledDeltaTime;
            yield return null;
        }

        _warning.alpha = 0f;
        _warningRoutine = null;
        if (Game.Instance.GameStateManager.State != GameplayState.Playing || Game.Player == null)
            yield break;

        if (!TryFindBossPosition(out Vector3 position))
        {
            Debug.LogError("BambooStage1 보스 등장 위치를 NavMesh에서 찾지 못했습니다.", this);
            yield break;
        }

        ActivateBoss(position, true);
    }

    /// <summary>
    /// position에 보스를 배치하고 공터 이동 범위와 체크포인트를 설정하며 플레이어 체력을 최대로 회복한다.
    /// 등장 파동은 첫 진입과 재도전 모두 시작하며 shakeCamera가 참인 첫 진입에서만 카메라를 흔든다.
    /// 보스는 마지막 등장 파동이 끝날 때까지 행동하지 않는다.
    /// </summary>
    private void ActivateBoss(Vector3 position, bool shakeCamera)
    {
        PlayerHealth playerHealth = Game.Player.GetComponent<PlayerHealth>();
        playerHealth.Heal(playerHealth.MaxHealth);
        Vector3 toPlayer = Game.Player.transform.position - position;
        toPlayer.y = 0f;
        _boss.transform.SetPositionAndRotation(position,
            toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity);
        BossController bossController = _boss.GetComponent<BossController>();
        bossController.ConfigureArena(_arena, _edgeInset);
        bossController.HoldForEntrance(GetEntranceWaveDuration());
        BlindBlade.Telemetry.CombatTelemetrySignals.Stage("1-2");
        _boss.SetActive(true);
        _director.HideRecord();
        _phase = StagePhase.Boss;
        BambooStage1Progress.MarkBossCheckpointReached();
        _entranceWaveRoutine = StartCoroutine(PlayEntranceWaves(position));
        if (!shakeCamera) return;
        _playerCamera = Game.Player.GetComponentInChildren<Camera>();
        _shakeElapsed = 0f;
    }

    /// <summary>
    /// forward 방향 공터 끝에서 중심 플레이어까지 NavMesh 경로가 연결된 위치를 찾는다.
    /// 가장 바깥쪽 유효 지점을 position으로 반환하며 탐색 실패 시 false를 반환한다.
    /// </summary>
    private bool TryFindBossRetryPosition(Vector3 forward, out Vector3 position)
    {
        position = default;
        Vector3 center = _arena.transform.position;
        forward.Normalize();
        if (forward.sqrMagnitude < 0.0001f
            || !NavMesh.SamplePosition(center, out NavMeshHit playerHit, _spawnSampleRadius, NavMesh.AllAreas))
            return false;

        float outerRadius = Mathf.Max(1f, _arena.Radius - _edgeInset);
        NavMeshPath path = new NavMeshPath();
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = Mathf.Max(1f, outerRadius - ring * _spawnSampleRadius);
            Vector3 candidate = center + forward * radius;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, _spawnSampleRadius, NavMesh.AllAreas))
                continue;
            Vector3 offset = Vector3.ProjectOnPlane(hit.position - center, Vector3.up);
            if (offset.magnitude > outerRadius || Vector3.Dot(offset.normalized, forward) < 0.98f
                || !NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete)
                continue;
            position = hit.position;
            return true;
        }
        return false;
    }

    /// <summary>arena의 중심과 반경을 사용해 플레이어에서 가장 먼 유효 NavMesh 위치를 position에 반환한다.</summary>
    private bool TryFindBossPosition(out Vector3 position)
    {
        Vector3 center = _arena.transform.position;
        Vector3 playerPosition = Game.Player.transform.position;
        Vector3 away = Vector3.ProjectOnPlane(center - playerPosition, Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
            away = -Game.Player.GetComponent<PlayerMotor>().ViewForward;
        away.Normalize();

        if (!NavMesh.SamplePosition(playerPosition, out NavMeshHit playerHit,
            _spawnSampleRadius, NavMesh.AllAreas))
        {
            position = default;
            return false;
        }

        float outerRadius = Mathf.Max(1f, _arena.Radius - _edgeInset);
        float bestDistance = -1f;
        position = default;
        NavMeshPath path = new NavMeshPath();
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = Mathf.Max(1f, outerRadius - ring * _spawnSampleRadius);
            for (int angle = 0; angle < 32; angle++)
            {
                Vector3 direction = Quaternion.Euler(0f, angle * 360f / 32f, 0f) * away;
                Vector3 candidate = center + direction * radius;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, _spawnSampleRadius, NavMesh.AllAreas)
                    || Vector3.ProjectOnPlane(hit.position - center, Vector3.up).magnitude > outerRadius
                    || !NavMesh.CalculatePath(hit.position, playerHit.position, NavMesh.AllAreas, path)
                    || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                float distance = Vector3.ProjectOnPlane(hit.position - playerPosition, Vector3.up).sqrMagnitude;
                if (distance <= bestDistance)
                    continue;
                bestDistance = distance;
                position = hit.position;
            }
        }
        return bestDistance >= 0f;
    }

    /// <summary>
    /// 첫 등장 파동부터 마지막 파동이 사라질 때까지의 시간을 반환한다.
    /// _entranceWaveCount·_entranceWaveInterval·_entranceWaveDuration을 사용한다.
    /// </summary>
    private float GetEntranceWaveDuration()
    {
        return (_entranceWaveCount - 1) * _entranceWaveInterval + _entranceWaveDuration;
    }

    /// <summary>position을 중심으로 설정된 횟수만큼 interval 간격으로 보스 등장 파동을 재생한다.</summary>
    private IEnumerator PlayEntranceWaves(Vector3 position)
    {
        for (int pulse = 0; pulse < _entranceWaveCount; pulse++)
        {
            PlayEntranceWave(position);
            yield return new WaitForSecondsRealtime(_entranceWaveInterval);
        }
        _entranceWaveRoutine = null;
    }

    /// <summary>position에서 반경이 조금씩 다른 링을 겹쳐 지정 굵기의 붉은 파동 하나를 만든다.</summary>
    private void PlayEntranceWave(Vector3 position)
    {
        if (_entranceWavePrefab == null)
            return;
        for (int layer = 0; layer < ENTRANCE_WAVE_LAYERS; layer++)
        {
            float spread = layer / (float)(ENTRANCE_WAVE_LAYERS - 1) - 0.5f;
            float radius = Mathf.Max(0.1f, _entranceWaveRadius + spread * _entranceWaveThickness);
            Vector3 wavePosition = position + Vector3.up * (0.03f + layer * 0.004f);
            GameObject wave = Managers.Resource.Instantiate(_entranceWavePrefab, wavePosition,
                Quaternion.identity);
            Color color = _entranceWaveColor;
            if (layer != ENTRANCE_WAVE_LAYERS / 2)
                color.a *= 0.75f;
            wave.GetComponent<FootstepWave>().Play(color, radius, _entranceWaveDuration);
        }
    }

    /// <summary>bossHealth가 비활성화되기 전 보스 외형을 가루 잔상으로 복제하고 카메라를 향하게 한다.</summary>
    private void HandleBossDying(EnemyHealth bossHealth)
    {
        if (bossHealth != _bossHealth || _phase != StagePhase.Boss) return;
        BossDeathDissolve.Play(bossHealth);
        if (_deathFocusRoutine != null) StopCoroutine(_deathFocusRoutine);
        Collider bossCollider = bossHealth.GetComponent<Collider>();
        Vector3 target = bossCollider != null ? bossCollider.bounds.center : bossHealth.transform.position + Vector3.up;
        _deathFocusRoutine = StartCoroutine(BossDeathDissolve.FocusCamera(target));
    }

    /// <summary>bossHealth의 사망을 확정하면 1막 완료 상태로 전환하고 소멸 뒤 완료 UI 표시를 예약한다.</summary>
    private void HandleBossDied(EnemyHealth bossHealth)
    {
        if (bossHealth == _bossHealth && _phase == StagePhase.Boss)
        {
            _phase = StagePhase.Complete;
            BlindBlade.Telemetry.CombatTelemetrySignals.Stage("1-2", true);
            BambooStage1Progress.MarkRainyStageReached();
            Game.Instance.GameStateManager.RequestActComplete();
            _stageTransitionRoutine = StartCoroutine(ShowActCompleteAfterDeath());
        }
    }

    /// <summary>씬 시작에 1막 제목을 warning 캔버스로 잠시 표시하고 보스 경고 전에는 숨긴다.</summary>
    private IEnumerator ShowActTitle()
    {
        _warning.GetComponentInChildren<TMP_Text>(true).text = "1막";
        _warning.alpha = 1f;
        yield return new WaitForSecondsRealtime(ACT_TITLE_DURATION);
        _warning.alpha = 0f;
        _actTitleRoutine = null;
    }

    /// <summary>보스 소멸 종료 1.5초 뒤 공용 UI 캔버스 중앙에 1막 완료 카드를 열고 버튼을 선택한다.</summary>
    private IEnumerator ShowActCompleteAfterDeath()
    {
        yield return new WaitForSecondsRealtime(BossDeathDissolve.DURATION + ACT_COMPLETE_DELAY);
        _stageTransitionRoutine = null;
        if (Game.Instance.GameStateManager.State != GameplayState.ActComplete)
            yield break;
        ActCompleteUI prefab = Resources.Load<ActCompleteUI>("Prefabs/UI/ActCompleteUI");
        Canvas canvas = Game.Instance.UIManager.GetComponentInChildren<Canvas>(true);
        if (prefab == null || canvas == null)
        {
            Debug.LogError("1막 완료 UI 프리팹 또는 공용 캔버스를 찾지 못했습니다.", this);
            yield break;
        }
        _actCompleteUi = Instantiate(prefab, canvas.transform, false);
        _actCompleteUi.Show();
    }
}
