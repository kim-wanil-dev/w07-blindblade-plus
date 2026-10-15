using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 강화 공격을 암전 속 일섬 한 장면으로 실행한다.
/// 화면이 암흑으로 덮인 채 사선을 따라 큰 곡선 검격과 갈라진 선이 그어지고, 적이 아직 서 있는 동안 머문 뒤
/// 납도와 함께 화면이 돌아오며 대상이 동시에 쓰러지고 선이 먹 조각으로 흩어진다.
/// 수락 순간 비행 중인 투사체는 연출 없이 사라진다. 카메라 출력은 바꾸지 않으며 화면 표현은 EmpowerOverlay가 그린다.
/// 연출 시간은 일시정지를 제외한 실시간이다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EmpowerOverlay))]
public sealed class PlayerEmpowerStrike : MonoBehaviour
{
    private const string PATTERN_ID = "EmpowerStrike";

    [Header("암전")]
    [SerializeField, Min(0f), Tooltip("수락부터 납도까지 화면이 암흑으로 덮인 채 검격과 갈라진 선만 보이는 실시간.")]
    private float _blackoutDuration = 1f;
    [SerializeField, Range(-80f, 80f), Tooltip("갈라진 선과 곡선 검격의 화면 기울기(도).")]
    private float _cutAngle = 24f;
    [SerializeField, Min(1f), Tooltip("사선을 따라 그어지는 곡선 검격 지름(1920×1080 기준 픽셀).")]
    private float _cutSlashSize = 2400f;
    [SerializeField, Min(0.5f)] private float _seamThickness = 4f;
    [SerializeField, Range(0f, 1f)] private float _cutFlash = 0.55f;
    [SerializeField, Tooltip("납도 순간 화면이 돌아올 때 당겨지는 FOV 변화량(도).")]
    private float _fovPunch = 10f;
    [SerializeField, Min(0.01f)] private float _fovPunchDuration = 0.35f;
    [SerializeField, Min(0f), Tooltip("암전 동안 카메라가 앞으로 밀려 화면이 돌아오는 순간 남아 있는 FOV 감소량(도). 복귀 후 0.25초에 걸쳐 풀린다.")]
    private float _pushInFov = 6f;

    [Header("납도")]
    [SerializeField, Range(0f, 1f)] private float _sheatheFlash = 0.4f;
    [SerializeField, Min(0f)] private float _sheathePause = 0.3f;
    [SerializeField, Min(0)] private int _scatterCount = 48;
    [SerializeField, Min(0.01f), Tooltip("갈라진 선이 흩어진 먹 조각이 떠다니다 사라지는 실시간. 강화가 끝난 뒤에도 남는다.")]
    private float _scatterLifetime = 1.2f;

    [Header("전방 베기")]
    [SerializeField, Range(1f, 360f), Tooltip("수락 순간 시선 기준 베는 전방 각도.")]
    private float _strikeAngle = 180f;
    [SerializeField, Min(0f), Tooltip("수락 순간 위치 기준 베는 수평 반경(미터).")]
    private float _strikeRadius = 50f;

    [Header("상태")]
    private EmpowerOverlay _overlay;
    private PlayerMotor _motor;
    private Camera _camera;
    private Coroutine _routine;
    private float _baseFov;
    private float _fovPunchAge = -1f;
    private float _pushIn;
    private float _pushInTarget;
    private float _pushInRate;
    private bool _fovApplied;

    void Awake()
    {
        _overlay = GetComponent<EmpowerOverlay>();
        _motor = GetComponent<PlayerMotor>();
        _camera = GetComponentInChildren<Camera>();
        _baseFov = _camera.fieldOfView;
    }

    void OnDisable()
    {
        Stop();
    }

    void Update()
    {
        UpdateFov();
    }

    /// <summary>
    /// 강화 공격 연출을 처음부터 시작하고 납도까지 끝나면 onFinished를 한 번 호출한다.
    /// 이미 재생 중이면 앞의 연출을 멈추고 다시 시작한다.
    /// </summary>
    public void Play(Action onFinished)
    {
        Stop();
        _routine = StartCoroutine(RunSequence(onFinished));
    }

    /// <summary>재생 중인 연출을 멈추고 화면 연출과 FOV를 원래대로 돌린다. onFinished는 호출하지 않는다.</summary>
    public void Stop()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        _overlay.ClearAll();
        _fovPunchAge = -1f;
        _pushIn = 0f;
        _pushInTarget = 0f;
        if (_fovApplied) _camera.fieldOfView = _baseFov;
        _fovApplied = false;
    }

    /// <summary>
    /// 투사체를 없앤 뒤 _blackoutDuration 동안 암흑 위에 곡선 검격과 갈라진 선을 긋고 머문다.
    /// 납도 순간 암흑을 걷어 화면을 되돌리며 대상 적을 동시에 처치하고 선을 흩뜨린 다음 onFinished를 호출한다.
    /// 처치 대상은 수락 순간 시선 기준 전방 범위의 적이다.
    /// </summary>
    private IEnumerator RunSequence(Action onFinished)
    {
        List<EnemyHealth> targets = CollectFrontEnemies();
        ReleaseProjectiles();

        // 암전: 화면이 검게 덮인 채 곡선 검격과 갈라진 선만 그어지고 적은 아직 서 있다.
        _overlay.SetVoid(1f);
        SlashAcross();
        _overlay.ShowCutLine(_cutAngle, _seamThickness, 0.08f);
        _overlay.Flash(_cutFlash, 0.2f);
        // 암흑 아래에서 카메라를 밀어 두어 화면이 돌아오는 순간 당겨졌다 풀리는 느낌을 준다.
        PushIn(_pushInFov, _blackoutDuration);
        yield return WaitReal(_blackoutDuration);

        // 납도: 암흑이 걷히는 순간 선이 흩어지고 대상 적이 동시에 쓰러진다.
        _overlay.SetVoid(0f);
        StrikeEnemies(targets);
        _overlay.ScatterCutLine(_scatterCount, _scatterLifetime);
        _overlay.Flash(_sheatheFlash, 0.2f);
        PunchFov();
        PushIn(0f, 0.25f);
        yield return WaitReal(_sheathePause);

        _routine = null;
        onFinished?.Invoke();
    }

    /// <summary>
    /// 세계를 가르는 사선(_cutAngle)을 따라 화면을 가로지르는 큰 곡선 검격을 긋는다.
    /// 초승달의 시위가 사선과 나란하고 휜 바깥 날이 화면 중심을 지나도록 놓는다.
    /// </summary>
    private void SlashAcross()
    {
        float rotation = _cutAngle - 90f;
        Vector2 bulge = new Vector2(Mathf.Cos(rotation * Mathf.Deg2Rad), Mathf.Sin(rotation * Mathf.Deg2Rad));
        _overlay.Slash(-bulge * (_cutSlashSize * 0.42f), _cutSlashSize, rotation, false, 0.1f, 0.5f,
            _overlay.AccentColor);
    }

    /// <summary>seconds만큼 일시정지를 제외한 실시간을 기다린다.</summary>
    private static IEnumerator WaitReal(float seconds)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            yield return null;
            elapsed += EmpowerOverlay.RealDeltaTime();
        }
    }

    /// <summary>현재 비행 중인 모든 DefenseShuriken을 없앤다.</summary>
    private static void ReleaseProjectiles()
    {
        foreach (DefenseShuriken projectile in FindObjectsByType<DefenseShuriken>(FindObjectsSortMode.None))
            if (projectile.IsFlying) projectile.Release();
    }

    /// <summary>살아 있는 적 중 지금 위치와 시선 기준 전방 각도·수평 반경 안에 있는 적을 모아 반환한다.</summary>
    private List<EnemyHealth> CollectFrontEnemies()
    {
        List<EnemyHealth> result = new List<EnemyHealth>();
        Vector3 origin = transform.position;
        Vector3 forward = _motor.ViewForward;
        float halfAngle = _strikeAngle * 0.5f;
        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive) continue;
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - origin, Vector3.up);
            if (toEnemy.sqrMagnitude > _strikeRadius * _strikeRadius) continue;
            if (toEnemy.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toEnemy) > halfAngle) continue;
            result.Add(enemy);
        }
        return result;
    }

    /// <summary>
    /// targets 중 아직 살아 있는 적에게 최대 체력만큼 피해를 준다.
    /// 피해는 적 담당의 EnemyHealth.TakeDamage로 전달해 사망·처치 기록을 기존 경로로 처리한다.
    /// </summary>
    private void StrikeEnemies(List<EnemyHealth> targets)
    {
        Vector3 origin = transform.position;
        foreach (EnemyHealth enemy in targets)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - origin, Vector3.up);
            Vector3 direction = toEnemy.sqrMagnitude > 0.0001f ? toEnemy.normalized : _motor.ViewForward;
            enemy.TakeDamage(new DamageContext(enemy.MaxHealth, gameObject, enemy.transform.position + Vector3.up,
                direction, PATTERN_ID, 0, 0, IncomingAttackKind.PlayerOwned));
        }
    }

    /// <summary>FOV 펀치를 처음부터 다시 시작한다.</summary>
    private void PunchFov()
    {
        _fovPunchAge = 0f;
    }

    /// <summary>카메라가 앞으로 밀리는 FOV 감소량을 target으로 duration 실시간 동안 바꾸게 한다.</summary>
    private void PushIn(float target, float duration)
    {
        _pushInTarget = target;
        _pushInRate = Mathf.Abs(target - _pushIn) / Mathf.Max(0.01f, duration);
    }

    /// <summary>FOV 펀치와 밀어 넣기를 실시간으로 합쳐 기준 FOV에 더하고, 모두 끝나면 한 번만 기준으로 돌린다.</summary>
    private void UpdateFov()
    {
        float dt = EmpowerOverlay.RealDeltaTime();
        float punch = 0f;
        if (_fovPunchAge >= 0f)
        {
            _fovPunchAge += dt;
            float t = _fovPunchAge / _fovPunchDuration;
            if (t >= 1f) _fovPunchAge = -1f;
            else punch = _fovPunch * (1f - t) * (1f - t);
        }
        _pushIn = Mathf.MoveTowards(_pushIn, _pushInTarget, _pushInRate * dt);
        if (_fovPunchAge < 0f && _pushIn <= 0f && _pushInTarget <= 0f)
        {
            // 연출이 없을 때는 한 번만 기준으로 돌려 다른 FOV 변경과 다투지 않는다.
            if (_fovApplied) _camera.fieldOfView = _baseFov;
            _fovApplied = false;
            return;
        }
        _camera.fieldOfView = _baseFov + punch - _pushIn;
        _fovApplied = true;
    }
}
