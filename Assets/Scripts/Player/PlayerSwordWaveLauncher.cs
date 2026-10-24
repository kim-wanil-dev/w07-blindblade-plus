using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 일섬 입력으로 시작된 베기와 함께 시선 방향으로 돌진하고, 돌진이 끝나면 관통 검기를 발사한다.
/// 돌진이 없으면 베기 시작에 맞춰 발사하며, 그 경우 베기 전에 공격이 취소되거나 다른 행동으로 바뀌면 발사하지 않는다.
/// 검기는 볼록한 날이 진행 방향을 향하도록 거의 눕힌 뒤 궁극기 사선(PlayerEmpowerStrike.CutAngle)과 같은 화면 기울기로 날아간다.
/// 돌진 동안 FOV가 넓어졌다 돌아오며, 집중 감속의 수명은 PlayerEmpower가 별도로 관리한다.
/// 돌진 중에는 플레이어 몸이 검기와 같은 반경으로 지나간 경로의 적에게 검기와 같은 피해를 주며, 검기와 맞은 적 목록을 함께 써서 피해가 겹치지 않는다.
/// 돌진 몸 판정이 적을 새로 벨 때마다 세계와 플레이어를 아주 짧게 멈춰 타격감을 준다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerActionDriver))]
[RequireComponent(typeof(PlayerEmpowerStrike))]
public sealed class PlayerSwordWaveLauncher : MonoBehaviour
{
    private const string DASH_PATTERN_ID = "PlayerSwordWaveDash";
    private const string HIT_STOP_TIME_OWNER = "PlayerSwordWave.HitStop";
    private const float MIN_SWEEP_DISTANCE = 0.01f;

    [Header("검기")]
    [SerializeField, Tooltip("발사할 검기 프리팹. 크기·속도·발사음·피해는 프리팹의 DefenseSwordWave 값을 쓴다.")]
    private DefenseSwordWave _wavePrefab;
    [SerializeField, Range(0f, 90f), Tooltip("검기 면을 눕힌 각도(도). 90이면 바닥과 나란한 가로 베기, 0이면 화면을 정면으로 마주 본다. 90보다 조금 작으면 뒤에서 면이 조금 보인다.")]
    private float _tiltAngle = 75f;

    [Header("발사 위치")]
    [SerializeField, Min(0f), Tooltip("카메라에서 시선 수평 방향으로 떨어진 발사 거리(미터).")]
    private float _spawnDistance = 1f;
    [SerializeField, Tooltip("카메라 높이 기준 발사 높이 차(미터). 음수면 아래에서 나가 위에서 내려다보인다.")]
    private float _spawnHeightOffset = -0.5f;

    [Header("돌진")]
    [SerializeField, Min(0f), Tooltip("검기 공격이 시작될 때 시선 수평 방향으로 돌진하는 거리(미터). 기본값은 회피 거리 2.4m의 7.5배다. 0이면 돌진하지 않는다.")]
    private float _dashDistance = 18f;
    [SerializeField, Min(0.01f), Tooltip("돌진 거리를 지나는 플레이어 시간(초). 벽이나 이동 경계에 닿으면 멈춘다.")]
    private float _dashDuration = 0.3f;
    [SerializeField, Min(0f), Tooltip("일섬을 누른 뒤 집중 감속 속에서 제자리에 멈춰 있다가 돌진을 시작하기까지의 실시간(초). 일시정지 동안 멈춘다.")]
    private float _dashStartDelay = 0.08f;
    [SerializeField, Tooltip("돌진 중 몸 판정이 검사하는 레이어. 적이 아닌 접촉은 무시한다. 반경과 피해는 검기 프리팹 값을 쓴다.")]
    private LayerMask _dashHitMask = ~(1 << BambooGrove.DEBRIS_LAYER);
    private readonly List<EnemyCapsuleHit> _dashHits = new List<EnemyCapsuleHit>();
    private SharedDamageHitTracker _hitTracker;
    private CharacterController _controller;
    private bool _dashing;
    private Vector3 _dashLastCenter;
    private float _dashStartDelayRemaining;
    // 돌진·검기가 함께 쓰는 피해 기록이다. 돌진 판정 전후 개수로 새로 벤 적이 있는지 확인한다.
    private HashSet<EnemyHealth> _dashStruck;

    [Header("발사 연출")]
    [SerializeField, Min(0f), Tooltip("돌진 동안 넓어지는 FOV(도). 0이면 FOV를 바꾸지 않는다. 연출 길이는 돌진 시간과 같다.")]
    private float _launchFovKick = 30f;
    [SerializeField, Range(0.01f, 0.9f), Tooltip("연출 시간 중 FOV가 넓어지고 감속이 걸리는 앞부분 비율. 나머지 동안 함께 돌아온다.")]
    private float _launchRiseRatio = 0.15f;
    private float _launchElapsed = -1f;
    private float _baseFov;
    private bool _fovApplied;
    // 이번 일섬의 FOV를 돌진 곡선 대신 집중 감속의 페이드 인·아웃에 맞춰 움직이는지와 그 비중이다.
    private bool _focusFov;
    private float _focusFovWeight;
    private PlayerClock _clock;

    [Header("타격 정지")]
    [SerializeField, Min(0f), Tooltip("일섬 돌진이 적을 벨 때마다 세계와 플레이어를 거의 멈추는 실시간(초). 불릿 타임이 아닐 때의 값이며 0이면 쓰지 않는다. 일시정지 동안 멈춘다.")]
    private float _hitStopDuration = 0.03f;
    [SerializeField, Min(0f), Tooltip("불릿 타임 중 타격 정지의 최대 실시간(초). 불릿 타임에는 기본 정지 시간을 세계 배율로 나눠 늘리되 이 값을 넘기지 않는다.")]
    private float _hitStopBulletTimeMaxDuration = 0.03f;
    [SerializeField, Range(0.01f, 1f), Tooltip("타격 정지 동안 세계 시간 배율. 플레이어도 같은 배율로 멈춘다.")]
    private float _hitStopTimeScale = 0.02f;
    private float _hitStopRemaining;
    private CombatSessionRuntime _hitStopRuntime;
    private CombatTimeToken _hitStopToken;

    [Header("상태")]
    private PlayerController _player;
    private PlayerEmpower _empower;
    private PlayerActionDriver _timing;
    private KatanaCombatCoordinator _combat;
    private PlayerMotor _motor;
    private PlayerEmpowerStrike _ultimate;
    private Camera _camera;
    private bool _armed;
    private int _previousActionId;
    private int _pendingActionId;
    private int _skillActionId;
    private int _multiKillAttackId;

    public float DashDistance => _dashDistance;
    /// <summary>일섬 출발 대기 또는 몸 돌진이 진행 중이면 true를 반환해 집중 감속의 종료 시점을 알린다.</summary>
    public bool IsDashSequenceActive => _dashStartDelayRemaining > 0f || _dashing;
    /// <summary>일섬 돌진이 지난 거리 비율(0~1)이다. 출발 대기 중이면 0, 돌진 중이 아니면 1이다.</summary>
    public float DashProgress => _dashStartDelayRemaining > 0f ? 0f : _dashing ? _motor.CombatPushProgress : 1f;
    public bool IsLaunchSlowActive => _launchElapsed >= 0f;
    public bool IsAttackInvulnerable => _armed || _dashing || _dashStartDelayRemaining > 0f;
    public bool IsSkillActive => _armed || _dashing || _dashStartDelayRemaining > 0f || _launchElapsed >= 0f;
    public bool IsHitStopActive => _hitStopRemaining > 0f;

    /// <summary>actionId가 이 런처로 시작한 일섬 행동이면 true를 반환한다. 일반 베기의 보스 충전 보상을 구분한다.</summary>
    public bool IsSkillAction(int actionId) => actionId > 0 && actionId == _skillActionId;

    /// <summary>
    /// 이미 시작된 actionId의 일반 베기를 새 실행 ID를 가진 일섬으로 연결한다. 돌진이 있으면 _dashStartDelay 실시간 동안 제자리에서 기다린 뒤 돌진하고,
    /// 대기가 0이면 바로 돌진한다. 돌진이 없으면 기존 베기 시점까지 발사를 기다린다. FOV는 집중 감속의 페이드 인·아웃을 따른다.
    /// </summary>
    public void PlaySkill(int actionId)
    {
        FinishPendingMultiKillAttack();
        _focusFov = true;
        _skillActionId = actionId;
        _pendingActionId = actionId;
        _multiKillAttackId = MultiKillAttackRuntime.AllocateAttackId();
        _armed = _dashDistance <= 0f;
        if (_dashDistance > 0f && _dashStartDelay > 0f)
        {
            _dashStartDelayRemaining = _dashStartDelay;
            return;
        }
        BeginDash();
    }

    /// <summary>사망 또는 종료 시 일섬 예약·출발 대기와 돌진 피해 판정을 취소한다. 입력 없이 발사 연출·타격 정지와 공유 타격 기록을 정리한다.</summary>
    public void CancelSkill()
    {
        FinishPendingMultiKillAttack();
        _armed = false;
        _dashing = false;
        _dashStartDelayRemaining = 0f;
        _hitTracker = null;
        StopLaunchEffect();
        ResetFov();
        EndHitStop();
    }

    /// <summary>아직 검기에 넘기지 않은 일섬 실행 ID를 완료 처리하고 로컬 참조를 비운다.</summary>
    private void FinishPendingMultiKillAttack()
    {
        if (_multiKillAttackId == 0)
            return;
        MultiKillAttackRuntime.CompleteAttack(_multiKillAttackId);
        _multiKillAttackId = 0;
    }

    public event Action<DefenseSwordWave> SwordWaveLaunched;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
        _empower = GetComponent<PlayerEmpower>();
        _timing = GetComponent<PlayerActionDriver>();
        _combat = GetComponent<KatanaCombatCoordinator>();
        _motor = GetComponent<PlayerMotor>();
        _ultimate = GetComponent<PlayerEmpowerStrike>();
        _controller = GetComponent<CharacterController>();
        _clock = GetComponent<PlayerClock>();
        _camera = GetComponentInChildren<Camera>();
        _baseFov = _camera.fieldOfView;
    }

    void OnDisable()
    {
        FinishPendingMultiKillAttack();
        _hitTracker = null;
        _armed = false;
        _dashing = false;
        _dashStartDelayRemaining = 0f;
        StopLaunchEffect();
        ResetFov();
        EndHitStop();
    }

    void Update()
    {
        // 타격 정지와 출발 대기는 감속에 늘어나지 않도록 일시정지를 제외한 실시간으로 잰다.
        if (_hitStopRemaining > 0f && !_player.IsPaused)
        {
            _hitStopRemaining -= Time.unscaledDeltaTime;
            if (_hitStopRemaining <= 0f) EndHitStop();
        }
        if (_dashStartDelayRemaining > 0f && !_player.IsPaused)
        {
            _dashStartDelayRemaining -= Time.unscaledDeltaTime;
            if (_dashStartDelayRemaining <= 0f)
            {
                _dashStartDelayRemaining = 0f;
                BeginDash();
            }
        }
        UpdateLaunchEffect();
        UpdateFocusFov();
        if (_dashing && !_player.IsPaused) StrikeAlongDash();
        if (!_armed || _player.IsPaused) return;

        if (_pendingActionId == 0)
        {
            if (_player.CurrentActionId == _previousActionId)
            {
                // 예약된 공격이 버려지면 검기도 함께 버린다.
                if (!_combat.HasBufferedAttack)
                {
                    _armed = false;
                    FinishPendingMultiKillAttack();
                }
                return;
            }
            if (_player.CurrentState != PlayerActionState.NormalAttack)
            {
                _armed = false;
                FinishPendingMultiKillAttack();
                return;
            }
            _pendingActionId = _player.CurrentActionId;
            if (_multiKillAttackId == 0)
                _multiKillAttackId = MultiKillAttackRuntime.AllocateAttackId();
            BeginDash();
            // 돌진이 있으면 검기는 돌진이 끝나는 순간 StrikeAlongDash가 발사한다.
            if (_dashing)
            {
                _armed = false;
                return;
            }
        }

        if (_player.CurrentActionId != _pendingActionId || _player.CurrentState != PlayerActionState.NormalAttack)
        {
            _armed = false;
            FinishPendingMultiKillAttack();
            return;
        }
        _timing.GetStrikeWindow(PlayerAttackKind.Normal, out float start, out _);
        if (_timing.GetPresentationElapsed(_pendingActionId) < start) return;

        _armed = false;
        Launch();
    }

    /// <summary>
    /// previousActionId 뒤에 시작되는 다음 일반 공격을 새 실행 ID를 가진 일섬으로 연결하고, 돌진이 끝나는 순간(돌진이 없으면 베기 시작에) 검기 하나를 발사하도록 예약한다.
    /// 이미 시작된 공격이면 다음 Update에서 바로 돌진하며, 이전 예약은 이 예약으로 바뀐다.
    /// </summary>
    public void Arm(int previousActionId)
    {
        FinishPendingMultiKillAttack();
        _focusFov = false;
        _armed = true;
        _previousActionId = previousActionId;
        _pendingActionId = 0;
        _multiKillAttackId = MultiKillAttackRuntime.AllocateAttackId();
    }

    /// <summary>
    /// 카메라 앞 발사 위치에서 시선 수평 방향으로 검기를 발사한다. 돌진이 없으면 이때 발사 연출을 돌진 시간만큼 시작한다.
    /// 볼록한 날이 앞을 향하도록 _tiltAngle만큼 눕히고, 진행 방향 축으로 궁극기 사선 각도(_ultimate.CutAngle)만큼 굴려 화면에서 같은 사선으로 보이게 한다.
    /// 피해·발사음·이동은 생성한 DefenseSwordWave가 맡는다.
    /// </summary>
    private void Launch()
    {
        Vector3 direction = _motor.ViewForward;
        Vector3 origin = _camera.transform.position + direction * _spawnDistance + Vector3.up * _spawnHeightOffset;
        DefenseSwordWave wave = Instantiate(_wavePrefab);
        wave.Launch(gameObject, origin, direction, _hitTracker, _multiKillAttackId);
        _hitTracker = null;
        _multiKillAttackId = 0;
        BlindBlade.Telemetry.CombatTelemetrySignals.SkillActivated("SwordWave", "manual",
            Guid.NewGuid().ToString("N"), transform.position);
        // 초승달 메시는 볼록한 쪽이 위를 향하므로 앞으로 눕혀야 진행 방향으로 날을 세워 날아간다.
        // 진행 방향 축의 양수 회전은 화면에서 반시계 방향이라 궁극기 사선 UI 회전과 같은 쪽으로 기운다.
        // 검기는 위치만 직선 이동하므로 발사 뒤 바꾼 회전이 그대로 유지된다.
        wave.transform.rotation = Quaternion.LookRotation(direction)
            * Quaternion.Euler(0f, 0f, _ultimate.CutAngle) * Quaternion.Euler(_tiltAngle, 0f, 0f);
        SwordWaveLaunched?.Invoke(wave);
        if (_dashDistance <= 0f) _launchElapsed = 0f;
    }

    /// <summary>
    /// 이번 검기 공격의 공유 피격 추적기를 새로 만들고 시선 수평 방향으로 _dashDistance 돌진과 발사 연출을 함께 시작한다.
    /// 돌진 거리가 0이면 돌진과 몸 판정 없이 검기만 쓰며, 시작 위치를 _dashLastCenter에 저장하고 곧바로 몸 판정을 한 번 한다.
    /// </summary>
    private void BeginDash()
    {
        _dashStruck = new HashSet<EnemyHealth>();
        _hitTracker = new SharedDamageHitTracker(_dashStruck, new HashSet<EnemyHealth>());
        _dashing = false;
        if (_dashDistance <= 0f) return;
        _motor.ApplyCombatPush(_motor.ViewForward, _dashDistance, _dashDuration);
        _dashing = true;
        _launchElapsed = 0f;
        _dashLastCenter = _controller.bounds.center;
        StrikeAlongDash();
    }

    /// <summary>
    /// 지난 몸 판정 위치에서 지금 플레이어 몸 중심까지 검기 판정 반경의 구를 쓸어 그 사이 생존 적에게 검기와 같은 피해를 준다.
    /// 한 프레임에 멀리 움직여도 사이의 적을 건너뛰지 않으며, 이미 피해를 받은 공유 체력 묶음과 막힌 개별 적은 다시 맞히지 않는다. 같은 구간의 적대 수리검은 없앤다.
    /// RainyStage에서는 같은 구간 둘레의 빗방울도 경로 바깥쪽으로 터뜨린다.
    /// 돌진이 끝나면 _dashing을 끄고 발사 연출도 함께 끝낸 뒤 그 자리에서 검기를 발사한다.
    /// </summary>
    private void StrikeAlongDash()
    {
        Vector3 center = _controller.bounds.center;
        Vector3 travel = center - _dashLastCenter;
        float distance = Mathf.Max(travel.magnitude, MIN_SWEEP_DISTANCE);
        Vector3 direction = travel.sqrMagnitude > 0.0001f ? travel.normalized : _motor.ViewForward;
        DefenseShuriken.ReleaseInSweep(_dashLastCenter, center, _wavePrefab.HitRadius);
        EnemyProjectile.ReleaseInSweep(_dashLastCenter, center, _wavePrefab.HitRadius);
        TwinBossProjectile.ReleaseInSweep(_dashLastCenter, center, _wavePrefab.HitRadius);
        RainStageWeather.RecordDashSweep(_dashLastCenter, center);
        // 피해로 적이 비활성화되면 활성 캡슐 목록이 바뀌므로 접촉을 먼저 모두 모은 뒤 피해를 준다.
        EnemyHitCapsule.SweepAll(_dashLastCenter, _dashLastCenter, _wavePrefab.HitRadius, direction,
            distance, _dashHitMask, _dashHits);
        int struckBefore = _dashStruck.Count;
        foreach (EnemyCapsuleHit hit in _dashHits)
        {
            EnemyHealth enemy = hit.Capsule.Health;
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 point = hit.Distance > 0f ? hit.Point : hit.Capsule.ClosestPoint(center);
            float amount = _wavePrefab.Damage > 0f ? _wavePrefab.Damage : Mathf.Max(enemy.CurrentHealth, 1f);
            _hitTracker.ProcessHit(enemy, new DamageContext(amount, gameObject, point, direction,
                DASH_PATTERN_ID, _multiKillAttackId, 0, IncomingAttackKind.Unknown));
        }
        if (_dashStruck.Count > struckBefore) BeginHitStop();
        _dashLastCenter = center;
        if (_motor.IsCombatPushing) return;
        // 벽에 막혀 일찍 끝나도 FOV와 감속이 돌진과 같은 순간에 끝나게 하고, 돌진을 마친 자리에서 검기를 낸다.
        _dashing = false;
        StopLaunchEffect();
        Launch();
    }

    /// <summary>
    /// 돌진과 같은 플레이어 시계로 _launchElapsed를 진행해 _dashDuration 동안 FOV를 넓혔다가 기준값으로 복귀시킨다.
    /// Shift 일섬(_focusFov)의 FOV는 UpdateFocusFov가 맡으므로 여기서는 연출 수명만 진행한다. 일섬의 시간 감속은 PlayerEmpower가 별도로 관리한다.
    /// </summary>
    private void UpdateLaunchEffect()
    {
        if (_launchElapsed < 0f) return;
        // 돌진도 플레이어 시계로 진행하므로 같은 시계를 써야 시작과 끝이 맞는다.
        if (!_player.IsPaused) _launchElapsed += _clock.DeltaTime;
        float t = _launchElapsed / _dashDuration;
        if (t >= 1f)
        {
            StopLaunchEffect();
            return;
        }

        float weight = t < _launchRiseRatio
            ? Mathf.SmoothStep(0f, 1f, t / _launchRiseRatio)
            : 1f - Mathf.SmoothStep(0f, 1f, (t - _launchRiseRatio) / (1f - _launchRiseRatio));
        if (_launchFovKick > 0f && !_focusFov)
        {
            _camera.fieldOfView = _baseFov + _launchFovKick * weight;
            _fovApplied = true;
        }
        // 일섬의 감속 수명은 PlayerEmpower의 0.2초 집중이 관리한다.
    }

    /// <summary>
    /// Shift 일섬이면 집중 감속이 걸려 있는 동안 FOV 비중을 PlayerEmpower의 일섬 페이드 인 시간에 걸쳐 1로 올리고, 감속이 풀리면 페이드 아웃 시간에 걸쳐 0으로 내린다.
    /// PlayerEmpower.IsSwordWaveFocusing과 일시정지를 제외한 실시간을 쓰며 _focusFovWeight·카메라 FOV·_fovApplied를 바꾼다.
    /// </summary>
    private void UpdateFocusFov()
    {
        if (!_focusFov || _launchFovKick <= 0f) return;
        float target = _empower.IsSwordWaveFocusing ? 1f : 0f;
        if (target <= 0f && _focusFovWeight <= 0f) return;

        float fade = target > _focusFovWeight ? _empower.SwordWaveFocusFadeIn : _empower.SwordWaveFocusFadeOut;
        _focusFovWeight = Mathf.MoveTowards(_focusFovWeight, target, EmpowerOverlay.RealDeltaTime() / fade);
        _camera.fieldOfView = _baseFov + _launchFovKick * _focusFovWeight;
        _fovApplied = _focusFovWeight > 0f;
    }

    /// <summary>집중 FOV 비중을 0으로 돌리고 적용 중인 FOV를 기준값으로 즉시 복구한다. _focusFovWeight·_fovApplied를 바꾼다.</summary>
    private void ResetFov()
    {
        _focusFovWeight = 0f;
        if (_fovApplied) _camera.fieldOfView = _baseFov;
        _fovApplied = false;
    }

    /// <summary>
    /// _hitStopTimeScale 세계 시간 요청을 건다. 정지 시간은 평소 _hitStopDuration이고, 불릿 타임 중에는 이를 현재 세계 배율로 나눠 늘리되
    /// _hitStopBulletTimeMaxDuration을 넘기지 않는다. 이미 멈춰 있으면 남은 시간을 그대로 둔다.
    /// 시간이 0이거나 세션 시간 런타임이 없으면 멈추지 않으며 _hitStopRemaining·_hitStopRuntime·_hitStopToken을 바꾼다.
    /// </summary>
    private void BeginHitStop()
    {
        if (_hitStopDuration <= 0f || IsHitStopActive) return;
        _hitStopRemaining = _empower.IsBulletTimeActive && Time.timeScale > 0f
            ? Mathf.Clamp(_hitStopDuration / Time.timeScale, _hitStopDuration,
                Mathf.Max(_hitStopDuration, _hitStopBulletTimeMaxDuration))
            : _hitStopDuration;
        if (_hitStopToken.IsValid) return;
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        if (runtime == null || !runtime.IsInitialized) return;
        _hitStopToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session, HIT_STOP_TIME_OWNER,
            CombatTimeReason.EmpoweredAttack, _hitStopTimeScale));
        if (_hitStopToken.IsValid) _hitStopRuntime = runtime;
    }

    /// <summary>등록한 타격 정지 시간 요청을 해제하고 _hitStopRemaining·_hitStopRuntime·_hitStopToken을 비운다.</summary>
    private void EndHitStop()
    {
        _hitStopRemaining = 0f;
        if (_hitStopRuntime != null && _hitStopToken.IsValid) _hitStopRuntime.Release(_hitStopToken);
        _hitStopRuntime = null;
        _hitStopToken = default;
    }

    /// <summary>발사 연출을 끝내고 _launchElapsed를 -1로 바꾼다. 집중을 따르는 Shift 일섬 FOV는 페이드 아웃에 맡기고, 그 외에는 FOV를 기준값으로 복구한다.</summary>
    private void StopLaunchEffect()
    {
        _launchElapsed = -1f;
        if (_focusFov) return;
        if (_fovApplied) _camera.fieldOfView = _baseFov;
        _fovApplied = false;
    }

}
