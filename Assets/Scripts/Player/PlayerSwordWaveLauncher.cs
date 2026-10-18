using System;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 퍼펙트 패링 준비 중 좌클릭으로 나간 일반 공격이 시작되면 시선 방향으로 돌진하고, 돌진이 끝나는 순간 그 자리에서 시선 앞으로 관통 검기를 발사한다.
/// 돌진이 없으면 베기 시작에 맞춰 발사하며, 그 경우 베기 전에 공격이 취소되거나 다른 행동으로 바뀌면 발사하지 않는다.
/// 검기는 볼록한 날이 진행 방향을 향하도록 거의 눕힌 뒤 궁극기 사선(PlayerEmpowerStrike.CutAngle)과 같은 화면 기울기로 날아간다.
/// 돌진이 시작되는 순간부터 끝나는 순간까지 FOV가 넓어졌다 돌아오고 세계가 더 느려지며, 그동안 플레이어 시계는 실시간으로 흐른다.
/// 돌진 중에는 플레이어 몸이 검기와 같은 반경으로 지나간 경로의 적에게 검기와 같은 피해를 주며, 검기와 맞은 적 목록을 함께 써서 피해가 겹치지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerActionDriver))]
[RequireComponent(typeof(PlayerEmpowerStrike))]
public sealed class PlayerSwordWaveLauncher : MonoBehaviour
{
    // 같은 소유자 요청은 겹쳐 등록할 수 없으므로 감속 배율을 바꿀 때 두 소유자를 번갈아 쓴다.
    private const string LAUNCH_TIME_OWNER_A = "PlayerSwordWave.LaunchA";
    private const string LAUNCH_TIME_OWNER_B = "PlayerSwordWave.LaunchB";
    private const float LAUNCH_SCALE_STEP = 0.005f;
    private const string DASH_PATTERN_ID = "PlayerSwordWaveDash";
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
    [SerializeField, Tooltip("돌진 중 몸 판정이 검사하는 레이어. 적이 아닌 접촉은 무시한다. 반경과 피해는 검기 프리팹 값을 쓴다.")]
    private LayerMask _dashHitMask = ~(1 << BambooGrove.DEBRIS_LAYER);
    private readonly RaycastHit[] _dashHits = new RaycastHit[32];
    // 같은 검기 공격의 돌진과 검기가 함께 쓰는 맞은 적 목록이다.
    private readonly HashSet<EnemyHealth> _struck = new HashSet<EnemyHealth>();
    private readonly HashSet<EnemyHealth> _blocked = new HashSet<EnemyHealth>();
    private CharacterController _controller;
    private bool _dashing;
    private Vector3 _dashLastCenter;

    [Header("발사 연출")]
    [SerializeField, Min(0f), Tooltip("돌진 동안 넓어지는 FOV(도). 0이면 FOV를 바꾸지 않는다. 연출 길이는 돌진 시간과 같다.")]
    private float _launchFovKick = 30f;
    [SerializeField, Range(0.01f, 1f), Tooltip("돌진 동안 세계 시간 배율. 이 동안 플레이어 시계는 실시간으로 흐른다.")]
    private float _launchTimeScale = 0.1f;
    [SerializeField, Range(0.01f, 0.9f), Tooltip("연출 시간 중 FOV가 넓어지고 감속이 걸리는 앞부분 비율. 나머지 동안 함께 돌아온다.")]
    private float _launchRiseRatio = 0.15f;
    private float _launchElapsed = -1f;
    private float _baseFov;
    private bool _fovApplied;
    private CombatSessionRuntime _timeRuntime;
    private CombatTimeToken _timeToken;
    private PlayerClock _clock;
    private float _appliedTimeScale = 1f;
    private bool _useLaunchOwnerB;

    [Header("상태")]
    private PlayerController _player;
    private PlayerActionDriver _timing;
    private KatanaCombatCoordinator _combat;
    private PlayerMotor _motor;
    private PlayerEmpowerStrike _ultimate;
    private Camera _camera;
    private bool _armed;
    private int _previousActionId;
    private int _pendingActionId;

    public float DashDistance => _dashDistance;
    public bool IsLaunchSlowActive => _launchElapsed >= 0f;
    public bool IsAttackInvulnerable => _armed || _dashing;
    public event Action<DefenseSwordWave> SwordWaveLaunched;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
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
        _armed = false;
        _dashing = false;
        StopLaunchEffect();
    }

    void Update()
    {
        UpdateLaunchEffect();
        if (_dashing && !_player.IsPaused) StrikeAlongDash();
        if (!_armed || _player.IsPaused) return;

        if (_pendingActionId == 0)
        {
            if (_player.CurrentActionId == _previousActionId)
            {
                // 예약된 공격이 버려지면 검기도 함께 버린다.
                if (!_combat.HasBufferedAttack) _armed = false;
                return;
            }
            if (_player.CurrentState != PlayerActionState.NormalAttack)
            {
                _armed = false;
                return;
            }
            _pendingActionId = _player.CurrentActionId;
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
            return;
        }
        _timing.GetStrikeWindow(PlayerAttackKind.Normal, out float start, out _);
        if (_timing.GetPresentationElapsed(_pendingActionId) < start) return;

        _armed = false;
        Launch();
    }

    /// <summary>
    /// previousActionId 뒤에 시작되는 다음 일반 공격이 시작되면 돌진하고, 돌진이 끝나는 순간(돌진이 없으면 베기 시작에) 검기 하나를 발사하도록 예약한다.
    /// 이미 시작된 공격이면 다음 Update에서 바로 돌진하며, 이전 예약은 이 예약으로 바뀐다.
    /// </summary>
    public void Arm(int previousActionId)
    {
        _armed = true;
        _previousActionId = previousActionId;
        _pendingActionId = 0;
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
        wave.Launch(gameObject, origin, direction, _struck, _blocked);
        // 초승달 메시는 볼록한 쪽이 위를 향하므로 앞으로 눕혀야 진행 방향으로 날을 세워 날아간다.
        // 진행 방향 축의 양수 회전은 화면에서 반시계 방향이라 궁극기 사선 UI 회전과 같은 쪽으로 기운다.
        // 검기는 위치만 직선 이동하므로 발사 뒤 바꾼 회전이 그대로 유지된다.
        wave.transform.rotation = Quaternion.LookRotation(direction)
            * Quaternion.Euler(0f, 0f, _ultimate.CutAngle) * Quaternion.Euler(_tiltAngle, 0f, 0f);
        SwordWaveLaunched?.Invoke(wave);
        if (_dashDistance <= 0f) _launchElapsed = 0f;
    }

    /// <summary>
    /// 이번 검기 공격의 맞은 적 목록을 비우고 시선 수평 방향으로 _dashDistance 돌진과 발사 연출을 함께 시작한다.
    /// 돌진 거리가 0이면 돌진과 몸 판정 없이 검기만 쓰며, 시작 위치를 _dashLastCenter에 저장하고 곧바로 몸 판정을 한 번 한다.
    /// </summary>
    private void BeginDash()
    {
        _struck.Clear();
        _blocked.Clear();
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
        int count = Physics.SphereCastNonAlloc(_dashLastCenter, _wavePrefab.HitRadius, direction, _dashHits,
            distance, _dashHitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            EnemyHealth enemy = _dashHits[i].collider.GetComponentInParent<EnemyHealth>();
            if (enemy == null || !enemy.IsAlive || _blocked.Contains(enemy)
                || _struck.Contains(enemy.SharedDamageKey)) continue;
            if (enemy.IsInvulnerable) _blocked.Add(enemy);
            else _struck.Add(enemy.SharedDamageKey);
            Vector3 point = _dashHits[i].distance > 0f ? _dashHits[i].point : _dashHits[i].collider.ClosestPoint(center);
            float amount = _wavePrefab.Damage > 0f ? _wavePrefab.Damage : Mathf.Max(enemy.CurrentHealth, 1f);
            enemy.TakeDamage(new DamageContext(amount, gameObject, point, direction, DASH_PATTERN_ID));
        }
        _dashLastCenter = center;
        if (_motor.IsCombatPushing) return;
        // 벽에 막혀 일찍 끝나도 FOV와 감속이 돌진과 같은 순간에 끝나게 하고, 돌진을 마친 자리에서 검기를 낸다.
        _dashing = false;
        StopLaunchEffect();
        Launch();
    }

    /// <summary>
    /// 발사 연출 진행도를 돌진과 같은 플레이어 시계로 늘리고 _dashDuration으로 나눠, 앞부분에 커졌다 나머지 동안 줄어드는 세기로 FOV와 세계 배율을 바꾼다.
    /// 연출이 끝나면 StopLaunchEffect로 FOV와 시간 요청을 원래대로 돌린다.
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
        if (_launchFovKick > 0f)
        {
            _camera.fieldOfView = _baseFov + _launchFovKick * weight;
            _fovApplied = true;
        }
        float scale = Mathf.Lerp(1f, _launchTimeScale, weight);
        if (Mathf.Abs(scale - _appliedTimeScale) >= LAUNCH_SCALE_STEP) ChangeTimeRequest(scale);
    }

    /// <summary>발사 연출을 끝내고 FOV를 기준 값으로 한 번 돌린 뒤 세계 시간 요청을 해제한다. _launchElapsed를 -1로 바꾼다.</summary>
    private void StopLaunchEffect()
    {
        _launchElapsed = -1f;
        if (_fovApplied) _camera.fieldOfView = _baseFov;
        _fovApplied = false;
        ReleaseTimeRequest();
    }

    /// <summary>
    /// scale 배율의 세계 시간 요청을 두 소유자 중 다른 쪽으로 새로 등록한 뒤 이전 요청을 해제해 배율이 끊기지 않게 바꾼다.
    /// 세션 시간 런타임이 없으면 요청 없이 진행하며 _timeRuntime·_timeToken·_appliedTimeScale을 갱신한다.
    /// </summary>
    private void ChangeTimeRequest(float scale)
    {
        CombatSessionRuntime runtime = CombatSessionRuntime.Active;
        CombatTimeToken previous = _timeToken;
        CombatSessionRuntime previousRuntime = _timeRuntime;
        _timeRuntime = null;
        _timeToken = default;
        _useLaunchOwnerB = !_useLaunchOwnerB;
        if (runtime != null && runtime.IsInitialized)
        {
            _timeToken = runtime.RequestSlowMotion(new CombatTimeRequest(runtime.Session,
                _useLaunchOwnerB ? LAUNCH_TIME_OWNER_B : LAUNCH_TIME_OWNER_A, CombatTimeReason.EmpoweredAttack, scale));
            if (_timeToken.IsValid) _timeRuntime = runtime;
        }
        if (previousRuntime != null && previous.IsValid) previousRuntime.Release(previous);
        _appliedTimeScale = scale;
    }

    /// <summary>등록한 세계 시간 요청이 있으면 해제하고 _timeRuntime·_timeToken을 비우며 _appliedTimeScale을 1로 돌린다.</summary>
    private void ReleaseTimeRequest()
    {
        if (_timeRuntime != null && _timeToken.IsValid) _timeRuntime.Release(_timeToken);
        _timeRuntime = null;
        _timeToken = default;
        _appliedTimeScale = 1f;
    }
}
