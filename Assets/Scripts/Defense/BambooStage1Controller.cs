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

    [Header("스테이지 연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField] private DefenseArenaBoundary _arena;
    [SerializeField] private GameObject _boss;
    [SerializeField] private CanvasGroup _warning;

    [Header("보스 등장 조건과 위치")]
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
    private Coroutine _entranceWaveRoutine;
    private Coroutine _stageTransitionRoutine;
    private Camera _playerCamera;
    private float _shakeElapsed;
    private StagePhase _phase;

    public bool BossDefeated => _phase == StagePhase.Complete;

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
        // 보스 출현에 필요한 처치 수보다 많은 근접 적은 내보내지 않는다.
        _director.SetMeleeSpawnLimit(_requiredKills);
        _director.SetKillGoal(_requiredKills);
        _warning.alpha = 0f;
        _warning.blocksRaycasts = false;
        _warning.interactable = false;
    }

    void Start()
    {
        BambooStage1Progress.MarkStage1Started();
        if (!BambooStage1Progress.BossCheckpointReached) return;

        Debug.Log("BambooStage1 저장 데이터에 보스 체크포인트가 있어 일반 적 웨이브를 건너뛰고 보스전을 시작합니다.", this);

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
        _bossHealth.Died -= HandleBossDied;
        if (_warningRoutine != null)
            StopCoroutine(_warningRoutine);
        if (_entranceWaveRoutine != null)
            StopCoroutine(_entranceWaveRoutine);
        if (_stageTransitionRoutine != null)
            StopCoroutine(_stageTransitionRoutine);
        _warningRoutine = null;
        _entranceWaveRoutine = null;
        _stageTransitionRoutine = null;
        if (_warning != null) _warning.alpha = 0f;
        _playerCamera = null;
    }

    /// <summary>Director의 누적 처치가 목표에 도달하면 웨이브를 끝내고 경고 표시를 시작한다.</summary>
    private void BeginBossWarning()
    {
        _phase = StagePhase.Warning;
        _director.StopWave();
        _warning.alpha = 1f;
        _warningRoutine = StartCoroutine(ShowWarningThenBoss());
    }

    /// <summary>warningDuration 뒤 플레이어에서 가장 먼 공터 NavMesh 지점에 보스를 켜고 등장 연출을 시작한다.</summary>
    private IEnumerator ShowWarningThenBoss()
    {
        yield return new WaitForSecondsRealtime(_warningDuration);
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
    /// </summary>
    private void ActivateBoss(Vector3 position, bool shakeCamera)
    {
        PlayerHealth playerHealth = Game.Player.GetComponent<PlayerHealth>();
        playerHealth.Heal(playerHealth.MaxHealth);
        Vector3 toPlayer = Game.Player.transform.position - position;
        toPlayer.y = 0f;
        _boss.transform.SetPositionAndRotation(position,
            toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity);
        _boss.GetComponent<BossController>().ConfigureArena(_arena, _edgeInset);
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

    /// <summary>bossHealth가 현재 보스의 사망을 알리면 2막 진입을 저장하고 전환 메시지를 표시한다.</summary>
    private void HandleBossDied(EnemyHealth bossHealth)
    {
        if (bossHealth == _bossHealth && _phase == StagePhase.Boss)
        {
            _phase = StagePhase.Complete;
            BlindBlade.Telemetry.CombatTelemetrySignals.Stage("1-2", true);
            BambooStage1Progress.MarkRainyStageReached();
            _stageTransitionRoutine = StartCoroutine(ShowActTwoThenTransition());
        }
    }

    /// <summary>2막 문구를 잠시 보여 준 뒤 페이드와 함께 RainyTwinBoss 씬으로 이동한다.</summary>
    private IEnumerator ShowActTwoThenTransition()
    {
        _warning.GetComponentInChildren<TMP_Text>(true).text = "2막";
        _warning.alpha = 1f;
        yield return new WaitForSecondsRealtime(2f);
        _warning.alpha = 0f;
        _stageTransitionRoutine = null;
        if (Game.Instance.GameStateManager.State != GameplayState.Playing)
            yield break;
        SceneTransitionOverlay.TransitionToScene("RainyTwinBoss", 0.5f, null);
    }
}
