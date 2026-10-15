using System.Collections;

using UnityEngine;
using UnityEngine.AI;

/// <summary>일반 적 처치 수에 따라 보스 경고, 원형 공터 가장자리 등장 연출과 스테이지 완료를 관리한다.</summary>
[DefaultExecutionOrder(1000)]
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
        _warning.alpha = 0f;
        _warning.blocksRaycasts = false;
        _warning.interactable = false;
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
        // PlayerMotor가 같은 프레임에 카메라 기본 위치를 갱신한 뒤 등장 흔들림을 더한다.
        _playerCamera.transform.localPosition += offset;
    }

    void OnDisable()
    {
        _bossHealth.Died -= HandleBossDied;
        if (_warningRoutine != null)
            StopCoroutine(_warningRoutine);
        if (_entranceWaveRoutine != null)
            StopCoroutine(_entranceWaveRoutine);
        _warningRoutine = null;
        _entranceWaveRoutine = null;
        _warning.alpha = 0f;
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

        Vector3 toPlayer = Game.Player.transform.position - position;
        toPlayer.y = 0f;
        _boss.transform.SetPositionAndRotation(position,
            toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity);
        _boss.GetComponent<BossController>().ConfigureArena(_arena, _edgeInset);
        _boss.SetActive(true);
        _phase = StagePhase.Boss;
        _entranceWaveRoutine = StartCoroutine(PlayEntranceWaves(position));
        _playerCamera = Game.Player.GetComponentInChildren<Camera>();
        _shakeElapsed = 0f;
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

    /// <summary>bossHealth가 현재 보스의 사망을 알리면 스테이지 완료 상태로 변경한다.</summary>
    private void HandleBossDied(EnemyHealth bossHealth)
    {
        if (bossHealth == _bossHealth && _phase == StagePhase.Boss)
            _phase = StagePhase.Complete;
    }
}
