using System.Collections;

using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

using TMPro;

/// <summary>비 내리는 공터에서 일반 적 50명을 처치하면 쌍둥이 보스를 등장시키고 둘의 사망 시 클리어를 요청한다.</summary>
// 등장 흔들림은 PlayerMotor(0)와 GameSceneInitializer(100) 뒤, 칼 궤적(200)·돌진 대상 실(300)·칼 구슬(310) 앞에 적용한다.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
public sealed class RainyStageController : MonoBehaviour
{
    private const int ENTRANCE_WAVE_LAYERS = 5;
    private const float ACT_TITLE_DURATION = 1.5f;
    private const float CLEAR_AFTER_DISSOLVE_DELAY = 1.5f;
    private const float FINAL_DISSOLVE_PAIR_DISTANCE = 5f;
    private const float FINAL_DISSOLVE_BEAST_OFFSET = 2.3f;
    private const float WAVE_DURATION = 100f;
    private const float SPAWN_RAMP_INTERVAL = 10f;

    [Header("스테이지 연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField] private DefenseArenaBoundary _arena;
    [SerializeField] private GameObject _twinBoss;
    [SerializeField] private EnemyHealth _human;
    [SerializeField] private EnemyHealth _beast;
    [SerializeField] private CanvasGroup _warning;
    [SerializeField] private RainStageWeather _weather;
    [SerializeField] private RainyStageAudio _rainAudio;

    [Header("보스 등장 조건")]
    [SerializeField]
    [InspectorName("시작 시 즉시 보스전")]
    [Tooltip("켜면 일반 적 웨이브와 보스 경고를 건너뛰고 쌍둥이 보스전으로 시작합니다. 보스 체크포인트가 저장되므로 일반 웨이브로 돌아가려면 스테이지 진행 정보를 초기화해야 합니다.")]
    private bool _startBossImmediately;
    [SerializeField, Min(1), Tooltip("이 수만큼 일반 적을 처치하면 보스 등장 경고가 시작됩니다.")]
    private int _requiredKills = 50;
    [SerializeField, Range(256, 8192), Tooltip("보스가 등장할 때 적용할 가까운 빗방울 수입니다.")]
    private int _bossDropletCount = 2662;

    [Header("보스 등장 위치")]
    [SerializeField, Min(0f)] private float _warningDuration = 2f;
    [SerializeField, Min(0f)] private float _edgeInset = 2f;
    [SerializeField, Min(0.1f)] private float _spawnSampleRadius = 2f;
    [SerializeField, Min(0f), Tooltip("등장 지점에서 인간과 짐승 사이의 좌우 간격(m)")]
    private float _pairGap = 4f;

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

    private Coroutine _warningRoutine;
    private Coroutine _actTitleRoutine;
    private Coroutine _entranceWaveRoutine;
    private Coroutine _deathFocusRoutine;
    private Camera _playerCamera;
    private float _shakeElapsed;
    private StagePhase _phase;

    public bool BossDefeated => _phase == StagePhase.Complete;
    public string CurrentTelemetryStageId => _phase == StagePhase.Boss || _phase == StagePhase.Complete ? "2-2" : "2-1";
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
        _director.SetMeleeSpawnLimit(_requiredKills);
        _director.SetKillGoal(_requiredKills);
        _director.ConfigureTimedMeleeWave(_requiredKills, WAVE_DURATION, SPAWN_RAMP_INTERVAL);
        _warning.alpha = 0f;
        _warning.blocksRaycasts = false;
        _warning.interactable = false;
    }

    void Start()
    {
        if (SceneManager.GetActiveScene().name == "RainyTwinBoss")
            _actTitleRoutine = StartCoroutine(ShowActTitle());
        _twinBoss.GetComponent<TwinBossController>().ConfigureArena(_arena, _edgeInset);
        BambooStage1Progress.MarkRainyStageReached();
        CurrentTelemetryEntryMode = _startBossImmediately ? "editor_skip"
            : BambooStage1Progress.RainyBossCheckpointReached ? "checkpoint_retry" : CombatTelemetryRunContext.EntryMode;
        if (_startBossImmediately || BambooStage1Progress.RainyBossCheckpointReached)
        {
            string startReason = _startBossImmediately ? "Inspector 설정" : "저장된 보스 체크포인트";
            Debug.Log($"RainyStage {startReason}으로 일반 적 웨이브를 건너뛰고 보스전을 시작합니다.", this);
            PlayerController player = Game.Player;
            if (!TryFindBossPosition(out Vector3 retryPosition))
            {
                Debug.LogError("RainyStage 보스 재도전 위치를 NavMesh에서 찾지 못했습니다.", this);
                return;
            }
            _director.StopWave();
            Vector3 center = _arena.transform.position;
            player.transform.position = new Vector3(center.x, player.transform.position.y, center.z);
            player.GetComponent<PlayerPerfectPulse>().SuppressStartPulse();
            ActivateBoss(retryPosition, false);
            player.GetComponent<PlayerMotor>().FaceTarget(_human.transform.position + Vector3.up);
            return;
        }
        BlindBlade.Telemetry.CombatTelemetrySignals.Stage("2-1");
    }

    void OnEnable()
    {
        _human.Dying += HandleBossDying;
        _beast.Dying += HandleBossDying;
        _human.Died += HandleBossDied;
        _beast.Died += HandleBossDied;
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
        _human.Dying -= HandleBossDying;
        _beast.Dying -= HandleBossDying;
        _human.Died -= HandleBossDied;
        _beast.Died -= HandleBossDied;
        if (_warningRoutine != null)
            StopCoroutine(_warningRoutine);
        if (_actTitleRoutine != null)
            StopCoroutine(_actTitleRoutine);
        if (_entranceWaveRoutine != null)
            StopCoroutine(_entranceWaveRoutine);
        if (_deathFocusRoutine != null)
            StopCoroutine(_deathFocusRoutine);
        _warningRoutine = null;
        _actTitleRoutine = null;
        _entranceWaveRoutine = null;
        _deathFocusRoutine = null;
        // 씬 해제 때는 경고 Canvas가 먼저 파괴될 수 있으므로 남아 있을 때만 숨긴다.
        if (_warning != null)
            _warning.alpha = 0f;
        _playerCamera = null;
    }

    /// <summary>RainyTwinBoss 씬 시작에 2막 제목을 warning 캔버스로 잠시 표시한다.</summary>
    private IEnumerator ShowActTitle()
    {
        _warning.GetComponentInChildren<TMP_Text>(true).text = "2막";
        _warning.alpha = 1f;
        yield return new WaitForSecondsRealtime(ACT_TITLE_DURATION);
        _warning.alpha = 0f;
        _actTitleRoutine = null;
    }

    /// <summary>일반 적 처치 목표에 도달하면 웨이브를 멈추고 경고와 보스 등장 대기를 시작한다.</summary>
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
        _warning.alpha = 1f;
        _warningRoutine = StartCoroutine(ShowWarningThenBoss());
    }

    /// <summary>warningDuration 뒤 플레이어에서 가장 먼 공터 NavMesh 지점에 쌍둥이 보스를 켜고 등장 연출을 시작한다.</summary>
    private IEnumerator ShowWarningThenBoss()
    {
        yield return new WaitForSecondsRealtime(_warningDuration);
        _warning.alpha = 0f;
        _warningRoutine = null;
        if (Game.Instance.GameStateManager.State != GameplayState.Playing || Game.Player == null)
            yield break;

        if (!TryFindBossPosition(out Vector3 position))
        {
            Debug.LogError("RainyStage 보스 등장 위치를 NavMesh에서 찾지 못했습니다.", this);
            yield break;
        }

        ActivateBoss(position, true);
    }

    /// <summary>
    /// position 좌우에 인간과 짐승을 플레이어를 향하게 배치하고 쌍둥이 보스를 활성화한다.
    /// 플레이어 체력을 최대로 회복하고 단계를 Boss로 바꾸며 등장 파동을 시작한다. shakeCamera가 참이면 카메라도 흔든다.
    /// 쌍둥이 보스는 첫 진입과 재도전 모두 마지막 등장 파동이 끝날 때까지 행동하지 않는다.
    /// </summary>
    private void ActivateBoss(Vector3 position, bool shakeCamera)
    {
        PlayerHealth playerHealth = Game.Player.GetComponent<PlayerHealth>();
        playerHealth.Heal(playerHealth.MaxHealth);
        Vector3 toPlayer = Vector3.ProjectOnPlane(Game.Player.transform.position - position, Vector3.up);
        Quaternion facing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
        Vector3 side = facing * Vector3.right * (_pairGap * 0.5f);
        _human.transform.SetPositionAndRotation(SampleNear(position - side, position), facing);
        _beast.transform.SetPositionAndRotation(SampleNear(position + side, position), facing);
        _weather.SetDropletCount(_bossDropletCount);
        _rainAudio.TransitionToHeavy();
        BlindBlade.Telemetry.CombatTelemetrySignals.Stage("2-2");
        _twinBoss.GetComponent<TwinBossController>().HoldForEntrance(GetEntranceWaveDuration());
        _twinBoss.SetActive(true);
        _director.HideRecord();
        _phase = StagePhase.Boss;
        BambooStage1Progress.MarkRainyBossCheckpointReached();
        _entranceWaveRoutine = StartCoroutine(PlayEntranceWaves(position));
        if (!shakeCamera) return;
        _playerCamera = Game.Player.GetComponentInChildren<Camera>();
        _shakeElapsed = 0f;
    }

    /// <summary>desired 근처의 NavMesh 위치를 반환하며 찾지 못하면 fallback을 반환한다.</summary>
    private Vector3 SampleNear(Vector3 desired, Vector3 fallback)
    {
        return NavMesh.SamplePosition(desired, out NavMeshHit hit, _spawnSampleRadius, NavMesh.AllAreas)
            ? hit.position : fallback;
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

    /// <summary>bossHealth가 사라지기 전에 가루 잔상을 만들고 카메라를 그 보스 쪽으로 부드럽게 돌린다.</summary>
    private void HandleBossDying(EnemyHealth bossHealth)
    {
        if (_phase != StagePhase.Boss && _phase != StagePhase.Complete) return;
        bool bothDead = !_human.IsAlive && !_beast.IsAlive;
        if (bothDead) TeleportBeastBesideHuman();
        BossDeathDissolve.Play(bossHealth);
        if (_deathFocusRoutine != null) StopCoroutine(_deathFocusRoutine);
        Collider bossCollider = bossHealth.GetComponent<Collider>();
        Vector3 target = bothDead ? _human.transform.position + Vector3.up * 1.3f
            : bossCollider != null ? bossCollider.bounds.center : bossHealth.transform.position + Vector3.up;
        _deathFocusRoutine = StartCoroutine(BossDeathDissolve.FocusCamera(target));
    }

    /// <summary>최종 사망 시 beast가 human과 5m 이상 떨어져 있으면 NavMesh의 인간 옆 2.3m 지점으로 옮긴다.</summary>
    private void TeleportBeastBesideHuman()
    {
        Vector3 humanPosition = _human.transform.position;
        Vector3 offset = Vector3.ProjectOnPlane(_beast.transform.position - humanPosition, Vector3.up);
        if (offset.sqrMagnitude <= FINAL_DISSOLVE_PAIR_DISTANCE * FINAL_DISSOLVE_PAIR_DISTANCE)
            return;
        Vector3 side = Vector3.ProjectOnPlane(_human.transform.right, Vector3.up).normalized;
        Vector3 desired = humanPosition + side * FINAL_DISSOLVE_BEAST_OFFSET;
        Vector3 center = _arena.transform.position;
        float limit = Mathf.Max(1f, _arena.Radius - _edgeInset);
        if (Vector3.ProjectOnPlane(desired - center, Vector3.up).magnitude > limit)
            desired = humanPosition - side * FINAL_DISSOLVE_BEAST_OFFSET;
        Vector3 destination = SampleNear(desired, humanPosition + side * 1.2f);
        NavMeshAgent agent = _beast.GetComponent<NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled)
            agent.Warp(destination);
        // 돌진 중 NavMesh의 Transform 자동 갱신이 꺼져 있어도 사망 외형은 즉시 새 위치를 써야 한다.
        _beast.transform.position = destination;
    }

    /// <summary>bossHealth 사망 시 두 보스가 쓰러졌으면 소멸 시간과 1.5초를 기다려 클리어 UI를 표시한다.</summary>
    private void HandleBossDied(EnemyHealth bossHealth)
    {
        // 사망 사건 시점에 죽은 쪽은 체력 0이므로 남은 쪽의 생존 여부만 확인하면 된다.
        if (_phase != StagePhase.Boss || _human.IsAlive || _beast.IsAlive)
            return;

        _phase = StagePhase.Complete;
        BlindBlade.Telemetry.CombatTelemetrySignals.Stage("2-2", true);
        Game.Instance.UIManager.Get<GameClearUI>().SetNextDisplayDelay(
            BossDeathDissolve.DURATION + CLEAR_AFTER_DISSOLVE_DELAY);
        Game.Instance.GameStateManager.RequestGameClear();
    }
}
