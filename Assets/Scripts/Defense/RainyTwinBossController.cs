using System.Collections;

using UnityEngine;
using UnityEngine.AI;

/// <summary>비 내리는 공터에서 일반 웨이브 없이 경고 뒤 쌍둥이 보스를 등장시키고 두 보스 처치 시 클리어를 요청한다.</summary>
// 등장 흔들림은 PlayerMotor(0)와 GameSceneInitializer(100) 뒤, 칼 궤적(200)·돌진 대상 실(300)·칼 구슬(310) 앞에 적용한다.
[DefaultExecutionOrder(120)]
[DisallowMultipleComponent]
public sealed class RainyTwinBossController : MonoBehaviour
{
    private const int ENTRANCE_WAVE_LAYERS = 5;

    [Header("스테이지 연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField] private DefenseArenaBoundary _arena;
    [SerializeField] private GameObject _twinBoss;
    [SerializeField] private EnemyHealth _human;
    [SerializeField] private EnemyHealth _beast;
    [SerializeField] private CanvasGroup _warning;

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
    private Coroutine _entranceWaveRoutine;
    private Camera _playerCamera;
    private float _shakeElapsed;
    private StagePhase _phase;

    public bool BossDefeated => _phase == StagePhase.Complete;

    private enum StagePhase
    {
        Waiting,
        Warning,
        Boss,
        Complete
    }

    void Awake()
    {
        _warning.alpha = 0f;
        _warning.blocksRaycasts = false;
        _warning.interactable = false;
    }

    void Start()
    {
        // 이 씬은 보스전만 진행하므로 방어 웨이브와 처치 기록을 처음부터 끈다.
        _director.StopWave();
        _director.HideRecord();
    }

    void OnEnable()
    {
        _human.Died += HandleBossDied;
        _beast.Died += HandleBossDied;
    }

    void Update()
    {
        if (_phase != StagePhase.Waiting || !Game.IsInitialized || Game.Instance.GameStateManager == null
            || Game.Instance.GameStateManager.State != GameplayState.Playing)
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
        _human.Died -= HandleBossDied;
        _beast.Died -= HandleBossDied;
        if (_warningRoutine != null)
            StopCoroutine(_warningRoutine);
        if (_entranceWaveRoutine != null)
            StopCoroutine(_entranceWaveRoutine);
        _warningRoutine = null;
        _entranceWaveRoutine = null;
        _warning.alpha = 0f;
        _playerCamera = null;
    }

    /// <summary>플레이가 시작되면 경고 표시를 켜고 보스 등장 대기 코루틴을 시작하며 단계를 Warning으로 바꾼다.</summary>
    private void BeginBossWarning()
    {
        _phase = StagePhase.Warning;
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
            Debug.LogError("RainyTwinBoss 보스 등장 위치를 NavMesh에서 찾지 못했습니다.", this);
            yield break;
        }

        ActivateBoss(position);
    }

    /// <summary>
    /// position 좌우에 인간과 짐승을 플레이어를 향하게 배치하고 쌍둥이 보스를 활성화한다.
    /// 플레이어 체력을 최대로 회복하고 단계를 Boss로 바꾸며 등장 파동과 카메라 흔들림을 시작한다.
    /// </summary>
    private void ActivateBoss(Vector3 position)
    {
        PlayerHealth playerHealth = Game.Player.GetComponent<PlayerHealth>();
        playerHealth.Heal(playerHealth.MaxHealth);
        Vector3 toPlayer = Vector3.ProjectOnPlane(Game.Player.transform.position - position, Vector3.up);
        Quaternion facing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
        Vector3 side = facing * Vector3.right * (_pairGap * 0.5f);
        _human.transform.SetPositionAndRotation(SampleNear(position - side, position), facing);
        _beast.transform.SetPositionAndRotation(SampleNear(position + side, position), facing);
        _twinBoss.SetActive(true);
        _phase = StagePhase.Boss;
        _entranceWaveRoutine = StartCoroutine(PlayEntranceWaves(position));
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

    /// <summary>bossHealth 사망 시 인간과 짐승이 모두 쓰러졌으면 단계를 Complete로 바꾸고 게임 클리어를 요청한다.</summary>
    private void HandleBossDied(EnemyHealth bossHealth)
    {
        // 사망 사건 시점에 죽은 쪽은 체력 0이므로 남은 쪽의 생존 여부만 확인하면 된다.
        if (_phase != StagePhase.Boss || _human.IsAlive || _beast.IsAlive)
            return;

        _phase = StagePhase.Complete;
        Game.Instance.GameStateManager.RequestGameClear();
    }
}
