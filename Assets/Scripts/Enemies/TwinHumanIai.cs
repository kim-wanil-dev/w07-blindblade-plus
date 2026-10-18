using System.Collections;

using UnityEngine;
using UnityEngine.AI;

using BlindBlade.Combat.Contracts;
using BlindBlade.Telemetry;

/// <summary>
/// 인간 보스가 공터 먼 곳에서 거합을 차징하며 일정 간격으로 스테이지1 보스 쳐내기와 같은 붉은 파동을 점점 넓고 진하게 내고,
/// 끝까지 끊기지 않으면 세계를 늦춘 채 플레이어를 베고 지나가 암전·붉은 사선·땅 흔적·대나무 베기와 함께 큰 피해를 준다.
/// 차징 중 인간이 맞으면 제어자가 패턴을 취소하고 Cancel이 연출을 정리한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack), typeof(NavMeshAgent), typeof(TwinBossWarningVisual))]
public sealed class TwinHumanIai : MonoBehaviour
{
    private const string TIME_OWNER_A = "TwinHumanIai.SlowA";
    private const string TIME_OWNER_B = "TwinHumanIai.SlowB";
    private const int CHARGE_POSITION_DIRECTIONS = 32;

    [Header("차징 위치")]
    [SerializeField, Range(0.1f, 1f), Tooltip("공터 반경에 곱해 차징 위치 후보를 찾는 원의 반경 비율. 1이면 공터 가장자리에서 플레이어와 가장 먼 반대편에 선다.")]
    private float _chargeRingRatio = 1f;
    [SerializeField] private LayerMask _placementObstacleMask = Physics.DefaultRaycastLayers;
    private readonly Collider[] _placementContacts = new Collider[16];
    private NavMeshPath _path;

    [Header("차징")]
    [SerializeField, Min(0.1f), Tooltip("거합을 차징하는 게임 시간(초)")]
    private float _chargeDuration = 10f;
    [SerializeField, Min(0.1f), Tooltip("붉은 파동을 내는 간격(초). 차징 시작 뒤 이 간격마다 낸다(10초 차징이면 3·6·9초).")]
    private float _pulseInterval = 3f;
    [SerializeField, Tooltip("차징을 시작할 때 소리. 기 모으는 소리는 파동 때만 내도록 기본값은 None이다.")]
    private SoundId _chargeStartSound = SoundId.None;
    [SerializeField, Range(0.1f, 3f)] private float _chargeStartPitch = 0.8f;
    [SerializeField, Tooltip("파동마다 인간에게서 울리는 소리. 퍼펙트 패링음을 낮춰 쓴다.")]
    private SoundId _pulseSound = SoundId.TwinBossChargePulse;
    [SerializeField, Range(0.1f, 3f)] private float _pulsePitch = 0.4f;
    [SerializeField, Tooltip("마지막 파동에서만 울리는 소리. 플레이어 토큰 3개 쳐내기 소리(UltimateReady)를 입체로 등록한 ID를 낮춰 쓴다.")]
    private SoundId _finalPulseSound = SoundId.TwinBossChargeFinalPulse;
    [SerializeField, Range(0.1f, 3f)] private float _finalPulsePitch = 0.4f;
    [SerializeField, Min(0f), Tooltip("첫 파동 소리 음량. 마지막 파동까지 Last Pulse Volume으로 커진다.")]
    private float _firstPulseVolume = 1f;
    [SerializeField, Min(0f)] private float _lastPulseVolume = 1.8f;

    [Header("붉은 파동")]
    [SerializeField, Tooltip("스테이지1 보스 쳐내기 파동처럼 바닥 링과 파동이 밝힌 윤곽선에 쓰는 색")]
    private Color _pulseColor = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Min(1f)] private float _firstPulseRadius = 12f;
    [SerializeField, Min(1f)] private float _lastPulseRadius = 40f;
    [SerializeField, Range(0f, 1f), Tooltip("첫 파동의 바닥 링 진하기와 윤곽선 물들임 정도. 마지막 파동까지 Last Pulse Strength로 진해진다.")]
    private float _firstPulseStrength = 0.4f;
    [SerializeField, Range(0f, 1f)] private float _lastPulseStrength = 1f;

    [Header("거합")]
    [SerializeField, Min(0f)] private float _damage = 60f;
    [SerializeField, Min(1f), Tooltip("거합 돌진을 시작할 때 인간 발밑에서 퍼지는 붉은 파동 반경(미터)")]
    private float _dashPulseRadius = 30f;
    [SerializeField, Min(0.01f), Tooltip("거합 돌진 붉은 파동이 반경까지 퍼지는 시간(초). 차징 파동(1초)보다 빠르다.")]
    private float _dashPulseExpand = 0.25f;
    [SerializeField, Min(0.01f), Tooltip("플레이어를 베고 지나가는 돌진의 실제 시간(초)")]
    private float _dashDuration = 0.18f;
    [SerializeField, Min(0f), Tooltip("플레이어를 지나 멈추는 거리(미터)")]
    private float _exitDistance = 4f;
    [SerializeField, Min(0.01f), Tooltip("베인 뒤 화면이 어두운 채 머무는 실제 시간(초)")]
    private float _blackoutDuration = 0.8f;
    [SerializeField, Min(0f), Tooltip("납도 뒤 감속이 풀리기 시작할 때까지의 실제 시간(초)")]
    private float _sheathePause = 0.35f;
    [SerializeField] private SoundId _slashSound = SoundId.EmpowerBlackoutSlash;
    [SerializeField] private SoundId _sheatheSound = SoundId.EmpowerBlackoutSheathe;

    [Header("보스 불릿 타임")]
    [SerializeField, Min(0f), Tooltip("세계가 불릿 타임 배율까지 느려지는 실제 시간(초)")]
    private float _slowFadeIn = 0.1f;
    [SerializeField, Min(0f), Tooltip("납도 뒤 시간이 원래대로 돌아오는 실제 시간(초)")]
    private float _slowFadeOut = 0.35f;
    private CombatSessionRuntime _timeRuntime;
    private CombatTimeToken _timeToken;
    private bool _useTimeOwnerB;
    private float _appliedTimeScale = 1f;
    private float _slowWeight;
    private float _slowTarget;

    [Header("화면 베기")]
    [SerializeField] private Color _cutColor = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Tooltip("화면 중심을 지나는 붉은 사선의 기울기(도)")]
    private float _screenCutAngle = -24f;
    [SerializeField, Min(0.5f)] private float _seamThickness = 5f;
    [SerializeField, Min(100f)] private float _cutSlashSize = 2400f;
    [SerializeField, Range(0f, 1f)] private float _cutFlash = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _sheatheFlash = 0.3f;
    [SerializeField, Min(0)] private int _scatterCount = 40;
    [SerializeField, Min(0.01f)] private float _scatterLifetime = 1.2f;
    [SerializeField, Min(0f)] private float _cameraKickPitch = 6f;
    [SerializeField, Min(0f)] private float _cameraShake = 0.08f;
    [SerializeField, Min(0.01f)] private float _cameraShakeDuration = 0.4f;

    [Header("세계 베기")]
    [SerializeField, Tooltip("인간 시점에서 플레이어를 지나는 베는 평면의 기울기(도)")]
    private float _worldCutTilt = 24f;
    [SerializeField, Min(0.1f), Tooltip("베는 평면이 지나는 인간 눈높이(미터)")]
    private float _eyeHeight = 1.7f;
    [SerializeField, Range(10f, 170f), Tooltip("땅 흔적과 대나무를 베는 범위로 쓰는 인간 시야각(도)")]
    private float _cutViewFov = 70f;
    [SerializeField, Min(1f)] private float _cutViewDistance = 80f;
    [SerializeField, Range(0f, 0.5f)] private float _bambooViewMargin = 0.1f;

    [Header("퍼펙트 패링 반격")]
    [SerializeField, Min(0f), Tooltip("돌진하는 인간 몸 앞쪽이 플레이어에게 이만큼(미터) 다가온 순간 퍼펙트 패링을 확인한다.")]
    private float _parryReach = 1f;
    [SerializeField, Range(1f, 180f), Tooltip("플레이어 시점 정면 기준 거합을 받아칠 수 있는 각도(도). 플레이어 칼 정면 방어 각도와 같다.")]
    private float _parryFrontArc = 120f;
    [SerializeField, Min(0f), Tooltip("받아친 뒤 반대 사선 강화 공격을 내기 전 좌클릭을 꾹 누르던 궁극기 모으기 연출을 보이는 실제 시간(초). 0이면 플레이어 궁극기 차징 시간을 쓴다.")]
    private float _counterFocusDuration = 0f;
    [SerializeField, Min(0f), Tooltip("거합이 퍼펙트 패링에 막혔을 때 인간이 떨며 뒤로 밀려나는 거리(미터)")]
    private float _parryRecoilDistance = 15f;
    [SerializeField, Min(0.01f), Tooltip("밀려나는 실제 시간(초)")]
    private float _parryRecoilDuration = 0.6f;
    [SerializeField, Min(0f), Tooltip("받아치기 순간 플레이어 발밑에서 퍼지는 퍼펙트 패링 파동의 반경 배율(평소 퍼펙트 패링 대비)")]
    private float _counterPulseRadiusScale = 2f;
    [SerializeField, Min(0f), Tooltip("받아치기 퍼펙트 패링 파동이 다 퍼진 뒤 평소보다 더 남아 있는 잔상 시간(초)")]
    private float _counterPulseExtraAfterimage = 2f;

    private HumanBossAttack _attack;
    private NavMeshAgent _agent;
    private TwinBossWarningVisual _warning;
    private KatanaDamageReceiver _player;
    private PlayerMotor _playerMotor;
    private PlayerEmpower _playerEmpower;
    private PlayerBulletTimeAudio _playerBulletTimeAudio;
    private PlayerPerfectPulse _playerPerfectPulse;
    private EmpowerOverlay _overlay;
    private PlayerUltimateGroundMark _groundMark;
    private KatanaCombatCoordinator _playerCombat;
    private bool _releasing;
    private bool _blackout;
    private int _telemetryAttackId;
    private string _telemetryParentPatternId;
    private string _telemetryPatternInstanceId;

    public bool IsCharging { get; private set; }
    public bool IsReleasing => _releasing;
    public event System.Action CounterStarted;
    public float ChargeProgress { get; private set; }

    /// <summary>
    /// 부모 패턴 ID와 인스턴스 ID를 저장해 이후 거합 공격을 해당 패턴에 연결한다.
    /// 두 식별자를 받아 텔레메트리 상관 상태를 변경한다.
    /// </summary>
    public void SetTelemetryParent(string parentPatternId, string patternInstanceId)
    {
        _telemetryParentPatternId = parentPatternId;
        _telemetryPatternInstanceId = patternInstanceId;
    }

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        _agent = GetComponent<NavMeshAgent>();
        _warning = GetComponent<TwinBossWarningVisual>();
        _path = new NavMeshPath();
    }

    void Update()
    {
        UpdateSlow();
    }

    void OnDisable()
    {
        Cancel();
        _slowTarget = 0f;
        _slowWeight = 0f;
        if (_playerBulletTimeAudio != null) _playerBulletTimeAudio.SetExternalWarp(0f, 0f);
        ReleaseTime();
    }

    /// <summary>
    /// arenaCenter·arenaRadius 공터에서 player와 가장 먼 빈 지면으로 물속을 지나 순간이동(HumanBossAttack.TeleportThroughWater)해 _chargeDuration 동안 거합을 차징한다.
    /// 차징 시작부터 _pulseInterval마다 점점 넓고 진한 붉은 파동을 내고 연기를 점차 붉게 물들이며, 끝나면 거합을 낸다.
    /// </summary>
    public IEnumerator Charge(KatanaDamageReceiver player, Vector3 arenaCenter, float arenaRadius)
    {
        CachePlayer(player);
        _attack.StopShooting();
        if (TryGetChargePosition(arenaCenter, arenaRadius, out Vector3 position))
            yield return _attack.TeleportThroughWater(position);
        FacePlayer();
        IsCharging = true;
        ChargeProgress = 0f;
        _warning.BeginWarning(_chargeDuration);
        if (_chargeStartSound != SoundId.None)
            Managers.Sound.Play(_chargeStartSound, transform.position, 1f, gameObject, true, _chargeStartPitch);
        int pulseCount = Mathf.Max(1, Mathf.FloorToInt((_chargeDuration - 0.0001f) / _pulseInterval));
        int pulse = 0;
        float startedAt = Time.time;
        while (Time.time < startedAt + _chargeDuration && CanStrike())
        {
            if (pulse < pulseCount && Time.time >= startedAt + (pulse + 1) * _pulseInterval)
            {
                StartPulse(pulseCount > 1 ? pulse / (pulseCount - 1f) : 1f, pulse == pulseCount - 1);
                pulse++;
            }
            ChargeProgress = Mathf.Clamp01((Time.time - startedAt) / _chargeDuration);
            FacePlayer();
            yield return null;
        }
        IsCharging = false;
        if (CanStrike()) yield return Release();
        _warning.EndWarning();
    }

    /// <summary>
    /// 차징·거합 연출을 멈추고 암전·감속과 받아치기 무적을 걷어낸다. 차징 중이었으면 울리고 있는 파동 소리를 끊는다. 이미 퍼진 붉은 파동은 퍼펙트 파동 규칙대로 사라진다.
    /// 제어자가 패턴을 취소하거나 이 오브젝트가 꺼질 때 호출한다.
    /// </summary>
    public void Cancel()
    {
        EndTelemetryAttack("Cancelled");
        // 낮춰 늘어진 파동 소리가 취소 뒤에도 울려 거합이 이어지는 것처럼 들리지 않게 끊는다.
        if (IsCharging && Managers.IsInitialized)
        {
            if (_pulseSound != SoundId.None) Managers.Sound.StopFor(gameObject, _pulseSound, true, true);
            if (_finalPulseSound != SoundId.None) Managers.Sound.StopFor(gameObject, _finalPulseSound, true, true);
        }
        IsCharging = false;
        ChargeProgress = 0f;
        _warning.EndWarning();
        if (_blackout)
        {
            _blackout = false;
            _overlay.SetVoid(0f);
            _overlay.ScatterCutLine(_scatterCount, _scatterLifetime);
        }
        // 받아치기 집중은 감속을 직접 걸었으므로 페이드를 기다리지 않고 바로 푼다.
        if (_releasing) StopSlowNow();
        if (_playerEmpower != null)
        {
            _playerEmpower.SetChargePresentation(0f, 0f);
            _playerEmpower.SetCounterFocusInvulnerable(false);
        }
        _releasing = false;
        _slowTarget = 0f;
    }

    /// <summary>player와 시점·불릿 타임·화면 연출·땅 흔적 담당자를 캐싱한다.</summary>
    private void CachePlayer(KatanaDamageReceiver player)
    {
        _player = player;
        _playerMotor = player.GetComponent<PlayerMotor>();
        _playerEmpower = player.GetComponent<PlayerEmpower>();
        _playerBulletTimeAudio = player.GetComponent<PlayerBulletTimeAudio>();
        _playerPerfectPulse = player.GetComponent<PlayerPerfectPulse>();
        _overlay = player.GetComponent<EmpowerOverlay>();
        _groundMark = player.GetComponent<PlayerUltimateGroundMark>();
        _playerCombat = player.GetComponent<KatanaCombatCoordinator>();
    }

    /// <summary>
    /// 허리춤 기운을 모두 쓰고 보스의 불릿 타임으로 세계를 늦춘 채 화면을 붉은 사선으로 가르며 _blackoutDuration 동안 암전한다.
    /// 암전이 끝나면 감속을 바로 풀어 플레이어가 제 속도로 패링할 수 있게 한 뒤, 발밑에서 강하고 빠른 붉은 파동(_dashPulseRadius, _dashPulseExpand초)을 일으키며 그 순간 플레이어 쪽으로 _exitDistance 너머까지 실제 시간 _dashDuration 동안 돌진한다.
    /// 돌진하는 동안은 파동으로 드러나지 않아도 붉은 테두리가 계속 보이며, 차징을 끝내는 EndWarning이 함께 끈다.
    /// 돌진하는 몸이 플레이어에게 _parryReach만큼 다가온 순간 퍼펙트 패링에 막히면 Countered로 넘어가고,
    /// 막히지 않으면 그 자리에서 납도음과 함께 큰 피해·카메라 흔들림·땅 흔적·대나무 베기·빗방울 폭발을 낸다.
    /// </summary>
    private IEnumerator Release()
    {
        _releasing = true;
        _attack.ConsumeCharges();
        _telemetryAttackId = CombatTelemetrySignals.BeginBossAttack("TwinHumanIai", "Melee",
            transform.position, _player.transform.position, _telemetryParentPatternId,
            _telemetryPatternInstanceId, "Human");

        // 서걱: 보스의 불릿 타임 속에서 화면이 어두워지고 붉은 사선이 화면을 가른다.
        _slowTarget = 1f;
        _blackout = true;
        _overlay.SetVoid(1f);
        SlashAcross();
        _overlay.ShowCutLine(_screenCutAngle, _seamThickness, 0.08f, _cutColor);
        _overlay.Flash(_cutFlash, 0.2f);
        PlaySound(_slashSound, _player.transform.position);
        yield return WaitReal(_blackoutDuration);

        // 암전이 걷히면 감속을 바로 풀고 사선이 흩어지는 사이 돌진한다.
        _blackout = false;
        _overlay.SetVoid(0f);
        _overlay.ScatterCutLine(_scatterCount, _scatterLifetime);
        StopSlowNow();
        if (!CanStrike())
        {
            EndTelemetryAttack("Cancelled");
            _releasing = false;
            yield break;
        }

        Vector3 start = transform.position;
        Vector3 eye = start + Vector3.up * _eyeHeight;
        Vector3 toPlayer = _playerMotor.ViewOrigin - eye;
        Vector3 forward = Vector3.ProjectOnPlane(toPlayer, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        Vector3 cutDirection = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : forward;
        Vector3 cutNormal = GetCutNormal(cutDirection);
        Matrix4x4 cutView = GetCutViewProjection(eye, cutDirection);
        Vector3 exit = GetExitPosition(forward);
        CombatTelemetrySignals.Commit(_telemetryAttackId, start);
        float contactAlong = Vector3.Dot(_player.transform.position - start, forward) - _parryReach;
        // 돌진을 알리는 강하고 빠른 붉은 파동을 일으키며 출발하고, 돌진 동안 파동 없이도 붉은 테두리가 보이게 한다.
        _playerPerfectPulse.BeginAt(start, Vector3.up, _pulseColor, _pulseColor, _dashPulseRadius, 1f, true, _dashPulseExpand);
        _warning.SetAlwaysVisible(true);
        bool contacted = false;
        float elapsed = 0f;
        while (elapsed < _dashDuration)
        {
            elapsed += EmpowerOverlay.RealDeltaTime();
            Vector3 next = Vector3.Lerp(start, exit, Mathf.Clamp01(elapsed / _dashDuration));
            _agent.Move(next - transform.position);
            transform.rotation = Quaternion.LookRotation(forward);
            if (!contacted && Vector3.Dot(transform.position - start, forward) >= contactAlong)
            {
                contacted = true;
                if (TryPerfectParry(forward))
                {
                    yield return Countered(forward);
                    yield break;
                }
                StrikePlayer(forward, start, eye, cutDirection, cutNormal, cutView);
            }
            yield return null;
        }
        _agent.Warp(exit);
        transform.rotation = Quaternion.LookRotation(forward);
        if (!contacted) StrikePlayer(forward, start, eye, cutDirection, cutNormal, cutView);
        yield return WaitReal(_sheathePause);
        EndTelemetryAttack("Miss");
        _releasing = false;
    }

    /// <summary>
    /// 돌진이 플레이어를 지나는 순간 납도음과 함께 forward 방향 _damage 피해(퍼펙트 패링 외 방어 불가)를 주고 섬광·카메라 흔들림을 낸다.
    /// 돌진 시작 위치 start, 인간 눈 eye에서 플레이어를 향한 cutDirection과 베는 평면 cutNormal·인간 시야 cutView로 땅 흔적·대나무 베기·빗방울 폭발을 낸다.
    /// </summary>
    private void StrikePlayer(Vector3 forward, Vector3 start, Vector3 eye, Vector3 cutDirection, Vector3 cutNormal, Matrix4x4 cutView)
    {
        PlaySound(_sheatheSound, _player.transform.position);
        if (_player.IsAlive)
            _player.TakeDamage(new DamageContext(_damage, gameObject, _playerMotor.ViewOrigin, forward,
                "TwinHumanIai", _telemetryAttackId, 1, IncomingAttackKind.Charge));
        _overlay.Flash(_sheatheFlash, 0.2f);
        _playerMotor.AddCameraKick(_cameraKickPitch, _cameraShake, _cameraShakeDuration);
        _groundMark.Show(eye, cutNormal, cutView);
        BambooGrove.CutInView(cutView, _bambooViewMargin, start, eye, cutNormal);
        RainStageWeather.RecordCutPlaneBurst(eye, cutNormal, cutDirection);
    }

    /// <summary>
    /// 받아친 뒤 플레이어가 좌클릭을 꾹 눌러 궁극기를 모으던 연출을 실제 시간으로 재생한다. 기간은 _counterFocusDuration(0이면 플레이어 궁극기 차징 시간)이다.
    /// 세계는 플레이어 차징처럼 ChargeSlowDuration에 걸쳐 ChargeWorldTimeScale까지 느려지고 불릿 타임 소리가 걸리며,
    /// 차징 비네트·토큰 HUD는 PlayerEmpower.SetChargePresentation으로 같은 진행도를 보인다. 끝나면 연출 진행도만 비우고 감속은 X자 암전 동안 유지한다.
    /// </summary>
    private IEnumerator Focus()
    {
        float duration = _counterFocusDuration > 0f ? _counterFocusDuration : _playerEmpower.UltimateChargeDuration;
        float slowDuration = Mathf.Max(0.01f, _playerEmpower.ChargeSlowDuration);
        _slowTarget = 0f;
        _slowWeight = 0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += EmpowerOverlay.RealDeltaTime();
            float slow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slowDuration));
            _playerEmpower.SetChargePresentation(elapsed / duration, slow);
            // 플레이어 궁극기처럼 효과음·BGM만 먹먹하게 하고 암전음이 나가는 연출음 버스는 그대로 둔다.
            _playerBulletTimeAudio.SetExternalWarp(slow, 0f);
            float scale = Mathf.Lerp(1f, _playerEmpower.ChargeWorldTimeScale, slow);
            if (Mathf.Abs(scale - _appliedTimeScale) >= 0.001f)
            {
                ChangeTime(scale);
                _appliedTimeScale = scale;
            }
            yield return null;
        }
        _playerEmpower.SetChargePresentation(0f, 0f);
    }

    /// <summary>보스의 불릿 타임 감속과 불릿 타임 소리를 페이드 없이 바로 풀고 등록한 시간 요청을 해제한다.</summary>
    private void StopSlowNow()
    {
        _slowTarget = 0f;
        _slowWeight = 0f;
        if (_playerBulletTimeAudio != null) _playerBulletTimeAudio.SetExternalWarp(0f, 0f);
        ReleaseTime();
    }

    /// <summary>
    /// 플레이어가 지금 퍼펙트 패링 구간이고 forward로 들어오는 거합이 시점 정면 _parryFrontArc 안이면 플레이어 칼의 막기 판정에 넘겨 막혔는지 반환한다.
    /// 가드만 열려 있으면 판정을 넘기지 않아 거합은 기존처럼 막을 수 없다. 막기 동작·소리만 나고 실제 퍼펙트 패링 불릿 타임·토큰·파동은 시작하지 않는다.
    /// </summary>
    private bool TryPerfectParry(Vector3 forward)
    {
        if (_playerCombat == null || _playerCombat.GuardContactOutcome != DefenseOutcome.PerfectParry) return false;
        Vector3 incoming = Vector3.ProjectOnPlane(-forward, Vector3.up);
        bool front = incoming.sqrMagnitude > 0.0001f && Vector3.Angle(_playerMotor.ViewForward, incoming) <= _parryFrontArc * 0.5f;
        DamageContext context = new DamageContext(_damage, gameObject, _playerMotor.ViewOrigin, forward,
            "TwinHumanIai", _telemetryAttackId, 1, IncomingAttackKind.Charge);
        // 막기 동작·소리만 내고 퍼펙트 패링 불릿 타임·토큰은 시작하지 않도록 방어 판정 보고를 거치지 않는다.
        bool defended = _playerCombat.TryDefendIncoming(context, front, false);
        if (defended)
        {
            DefenseResolution resolution = new DefenseResolution(_playerCombat.GuardContactOutcome,
                _telemetryAttackId, 1, 0, 0f, context.Point, Time.time, Time.realtimeSinceStartupAsDouble);
            CombatTelemetrySignals.Contact(context, resolution, false);
            _telemetryAttackId = 0;
        }
        return defended;
    }

    /// <summary>
    /// 퍼펙트 패링에 막힌 거합을 멈추고 플레이어 발밑에서 평소보다 _counterPulseRadiusScale배 넓고 _counterPulseExtraAfterimage초 더 남는 퍼펙트 패링 파동을 먼저 낸 뒤,
    /// 인간이 forward 반대쪽으로 _parryRecoilDistance만큼 떨며 밀려나는 동안 좌클릭을 꾹 눌러 궁극기를 모으던 연출(Focus)로 시간이 느려진다.
    /// 연출이 끝나면 감속을 풀고 거합의 붉은 사선(_cutColor) 위에 플레이어 대칭 사선을 덮고 기본 사선을 더한 X자 강화 공격을 한 번 발동한다
    /// (PlayerEmpower.BeginCounterStrike). 판정과 피해는 평소 강화 공격과 같이 한 번이다. 받아친 순간부터 강화 공격이 시작될 때까지 플레이어는 무적이다.
    /// </summary>
    private IEnumerator Countered(Vector3 forward)
    {
        // 받아치기가 발동하면 제어자가 인간의 피격 무적을 초기화한다.
        CounterStarted?.Invoke();
        // 받아친 순간부터 X자 강화 공격이 나갈 때까지 플레이어는 어떤 공격도 받지 않는다.
        _playerEmpower.SetCounterFocusInvulnerable(true);
        _warning.EndWarning();
        _overlay.Flash(_cutFlash, 0.15f);
        // 퍼펙트 패링에 성공한 순간 플레이어 발밑에서 퍼펙트 패링 파동이 먼저 퍼진다.
        _playerPerfectPulse.Begin(_counterPulseRadiusScale, _counterPulseExtraAfterimage);
        // 인간이 떨며 밀려나는 동안 좌클릭을 꾹 눌러 궁극기를 모으던 연출로 시간이 느려진다.
        StartCoroutine(_attack.Recoil(-forward, _parryRecoilDistance, _parryRecoilDuration, true));
        yield return Focus();
        StopSlowNow();
        _releasing = false;
        // 거합의 붉은 사선 위에 플레이어 대칭 사선을 덮고 기본 사선을 더한 강화 공격을 한 번 발동한다.
        _playerEmpower.BeginCounterStrike(_cutColor);
        // 강화 공격이 시작되면 강화 공격 무적이 이어받는다.
        _playerEmpower.SetCounterFocusInvulnerable(false);
    }

    /// <summary>
    /// 인간 눈에서 플레이어를 향한 cutDirection을 품고 수평에서 _worldCutTilt만큼 기운 베는 평면의 법선을 반환한다.
    /// 평면은 플레이어 눈을 지나므로 땅과는 옆으로 비껴 만나며 화면의 붉은 사선과 같은 기울기의 흔적을 남긴다.
    /// </summary>
    private Vector3 GetCutNormal(Vector3 cutDirection)
    {
        Vector3 right = Vector3.Cross(Vector3.up, cutDirection);
        if (right.sqrMagnitude < 0.0001f) right = transform.right;
        Vector3 up = Vector3.Cross(cutDirection, right.normalized).normalized;
        return Quaternion.AngleAxis(_worldCutTilt, cutDirection) * up;
    }

    /// <summary>eye에서 cutDirection을 바라보는 인간 시야를 카메라와 같은 방식의 투영·시점 행렬로 만들어 반환한다.</summary>
    private Matrix4x4 GetCutViewProjection(Vector3 eye, Vector3 cutDirection)
    {
        Matrix4x4 view = Matrix4x4.TRS(eye, Quaternion.LookRotation(cutDirection), new Vector3(1f, 1f, -1f)).inverse;
        Matrix4x4 projection = Matrix4x4.Perspective(_cutViewFov, 16f / 9f, 0.1f, _cutViewDistance);
        return projection * view;
    }

    /// <summary>플레이어 뒤로 forward 방향 _exitDistance 지점을 NavMesh 안으로 줄여 반환한다. 찾지 못하면 플레이어 발밑을 쓴다.</summary>
    private Vector3 GetExitPosition(Vector3 forward)
    {
        Vector3 desired = _player.transform.position + forward * _exitDistance;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
        if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 2f, filter)) return hit.position;
        return NavMesh.SamplePosition(_player.transform.position, out hit, 2f, filter) ? hit.position : transform.position;
    }

    /// <summary>
    /// arenaCenter 둘레 _chargeRingRatio 반경 원 위 후보 중 빈 NavMesh 지면이고 경로가 이어지며 플레이어와 가장 먼 곳을 position으로 반환한다.
    /// 후보가 없으면 false를 반환해 제자리에서 차징한다.
    /// </summary>
    private bool TryGetChargePosition(Vector3 arenaCenter, float arenaRadius, out Vector3 position)
    {
        position = transform.position;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = _agent.areaMask };
        float best = -1f;
        for (int i = 0; i < CHARGE_POSITION_DIRECTIONS; i++)
        {
            float angle = i * Mathf.PI * 2f / CHARGE_POSITION_DIRECTIONS;
            Vector3 desired = arenaCenter + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (arenaRadius * _chargeRingRatio);
            desired.y = _agent.nextPosition.y;
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 1f, filter) || IsPlacementBlocked(hit.position)
                || !_agent.CalculatePath(hit.position, _path) || _path.status != NavMeshPathStatus.PathComplete) continue;
            float distance = Vector3.ProjectOnPlane(hit.position - _player.transform.position, Vector3.up).sqrMagnitude;
            if (distance <= best) continue;
            best = distance;
            position = hit.position;
        }
        return best >= 0f;
    }

    /// <summary>position에 인간 이동 캡슐을 세웠을 때 자신을 제외한 장애물과 겹치는지 반환한다.</summary>
    private bool IsPlacementBlocked(Vector3 position)
    {
        float radius = _agent.radius;
        int count = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * (radius + 0.05f),
            position + Vector3.up * Mathf.Max(radius + 0.05f, _agent.height - radius), radius,
            _placementContacts, _placementObstacleMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!_placementContacts[i].transform.IsChildOf(transform)) return true;
        }
        return false;
    }

    /// <summary>
    /// 차징 진행 비율 t(0~1)로 반경·세기를 보간해 스테이지1 보스 쳐내기와 같은 붉은 퍼펙트 파동을 인간 발밑에서 시작한다.
    /// 바닥 링 알파와 윤곽선 물들임 정도가 함께 진해지고, 인간에게서 낮춘 퍼펙트 패링음이 커지며 울린다. final이면 마지막 파동 소리를 쓴다.
    /// </summary>
    private void StartPulse(float t, bool final)
    {
        float radius = Mathf.Lerp(_firstPulseRadius, _lastPulseRadius, t);
        float strength = Mathf.Lerp(_firstPulseStrength, _lastPulseStrength, t);
        Color ring = _pulseColor;
        ring.a *= strength;
        _playerPerfectPulse.BeginAt(transform.position, Vector3.up, ring, _pulseColor, radius, strength, true);
        SoundId sound = final ? _finalPulseSound : _pulseSound;
        if (sound != SoundId.None)
            Managers.Sound.Play(sound, transform.position, Mathf.Lerp(_firstPulseVolume, _lastPulseVolume, t),
                gameObject, true, final ? _finalPulsePitch : _pulsePitch);
    }

    /// <summary>
    /// _slowTarget 쪽으로 감속 비중을 실제 시간 페이드로 옮기고, 비중만큼 세계를 플레이어 불릿 타임 배율로 늦추며 효과음·BGM에만 불릿 타임 소리를 건다(연출음 버스 제외).
    /// 일시정지 중에는 진행하지 않으며 배율이 바뀔 때만 시간 요청을 다시 등록한다.
    /// </summary>
    private void UpdateSlow()
    {
        if (_playerEmpower == null || Time.timeScale <= 0f) return;
        if (Mathf.Approximately(_slowWeight, _slowTarget) && _slowWeight <= 0f) return;
        float fade = _slowTarget > _slowWeight ? _slowFadeIn : _slowFadeOut;
        _slowWeight = fade > 0f
            ? Mathf.MoveTowards(_slowWeight, _slowTarget, Time.unscaledDeltaTime / fade) : _slowTarget;
        // 연출음 버스까지 먹먹하게 하면 암전음(발도음)이 거의 들리지 않으므로 효과음·BGM에만 건다.
        _playerBulletTimeAudio.SetExternalWarp(_slowWeight, 0f);
        float scale = Mathf.Lerp(1f, _playerEmpower.WorldTimeScale, _slowWeight);
        if (_slowWeight <= 0f)
        {
            ReleaseTime();
            return;
        }
        if (Mathf.Abs(scale - _appliedTimeScale) < 0.001f) return;
        ChangeTime(scale);
        _appliedTimeScale = scale;
    }

    /// <summary>
    /// scale로 세계 시간 요청을 다른 소유자 이름으로 새로 등록한 뒤 이전 요청을 해제해 배율이 끊기지 않게 바꾼다.
    /// 세션 시간 런타임이 없으면 요청 없이 진행하며 _timeRuntime과 _timeToken을 갱신한다.
    /// </summary>
    private void ChangeTime(float scale)
    {
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        CombatTimeToken previous = _timeToken;
        CombatSessionRuntime previousRuntime = _timeRuntime;
        _timeRuntime = null;
        _timeToken = default;
        if (runtime != null && runtime.IsInitialized)
        {
            _useTimeOwnerB = !_useTimeOwnerB;
            _timeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session,
                _useTimeOwnerB ? TIME_OWNER_B : TIME_OWNER_A, CombatTimeReason.DashTelegraph, scale));
            if (_timeToken.IsValid) _timeRuntime = runtime;
        }
        if (previousRuntime != null && previous.IsValid) previousRuntime.Release(previous);
    }

    /// <summary>등록한 감속 요청이 있으면 해제하고 _timeRuntime·_timeToken·적용 배율을 초기화한다.</summary>
    private void ReleaseTime()
    {
        if (_timeRuntime != null && _timeToken.IsValid) _timeRuntime.Release(_timeToken);
        _timeRuntime = null;
        _timeToken = default;
        _appliedTimeScale = 1f;
    }

    /// <summary>화면 중심을 지나 _screenCutAngle만큼 기운 큰 붉은 곡선 검격을 화면에 긋는다.</summary>
    private void SlashAcross()
    {
        float rotation = _screenCutAngle - 90f;
        Vector2 bulge = new Vector2(Mathf.Cos(rotation * Mathf.Deg2Rad), Mathf.Sin(rotation * Mathf.Deg2Rad));
        _overlay.Slash(-bulge * (_cutSlashSize * 0.42f), _cutSlashSize, rotation, true, 0.1f, 0.5f, _cutColor);
    }

    /// <summary>자신과 플레이어가 살아 있고 지면 이동이 가능해 거합을 이어갈 수 있는지 반환한다.</summary>
    private bool CanStrike()
    {
        return _attack.Health.IsAlive && _player != null && _player.IsAlive && _attack.IsReady;
    }

    /// <summary>플레이어 쪽으로 수평 방향만 돌려 바라본다.</summary>
    private void FacePlayer()
    {
        Vector3 facing = Vector3.ProjectOnPlane(_player.transform.position - transform.position, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(facing);
    }

    /// <summary>soundId가 지정되어 있으면 position에서 효과음을 한 번 재생한다.</summary>
    private void PlaySound(SoundId soundId, Vector3 position)
    {
        if (soundId != SoundId.None)
            Managers.Sound.Play(soundId, position, 1f, gameObject, true);
    }

    /// <summary>seconds만큼 일시정지를 제외한 실제 시간을 기다린다.</summary>
    private static IEnumerator WaitReal(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += EmpowerOverlay.RealDeltaTime();
            yield return null;
        }
    }

    /// <summary>
    /// 현재 거합 공격 ID를 outcome으로 한 번 종료하고 다음 공격을 위해 식별자를 비운다.
    /// outcome을 받아 취소 또는 빗나감 결과를 이벤트로 전달한다.
    /// </summary>
    private void EndTelemetryAttack(string outcome)
    {
        if (_telemetryAttackId == 0) return;
        CombatTelemetrySignals.End(_telemetryAttackId, outcome, transform.position);
        _telemetryAttackId = 0;
    }
}
