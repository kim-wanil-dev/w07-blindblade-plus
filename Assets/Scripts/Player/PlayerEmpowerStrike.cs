using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 강화 공격을 세계 가르기 한 장면으로 실행한다.
/// 사선을 따라 큰 곡선 검격이 그어지며 실시간 화면이 둘로 갈라지고, 적이 아직 서 있는 채로 머문 뒤 납도와 함께 닫히며 대상이 동시에 쓰러진다.
/// 수락 순간 비행 중인 투사체는 연출 없이 사라진다. 연출 시간은 일시정지를 제외한 실시간이며 화면 표현은 EmpowerOverlay가 그린다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EmpowerOverlay))]
public sealed class PlayerEmpowerStrike : MonoBehaviour
{
    private const string PATTERN_ID = "EmpowerStrike";

    [Header("세계 가르기")]
    [SerializeField, Range(-80f, 80f), Tooltip("세계를 가르는 사선의 화면 기울기(도).")]
    private float _cutAngle = 24f;
    [SerializeField, Min(1f), Tooltip("사선을 따라 그어지는 곡선 검격 지름(1920×1080 기준 픽셀).")]
    private float _cutSlashSize = 2400f;
    [SerializeField, Min(0f), Tooltip("갈라진 두 절반이 벌어지는 거리(1920×1080 기준 픽셀).")]
    private float _splitDistance = 70f;
    [SerializeField, Min(0.01f)] private float _splitOpenDuration = 0.25f;
    [SerializeField, Min(0f), Tooltip("갈라진 세계를 보는 동안 적이 아직 서 있는 실시간.")]
    private float _splitHoldDuration = 1.2f;
    [SerializeField, Min(0.5f)] private float _seamThickness = 4f;
    [SerializeField, Range(0f, 1f)] private float _cutFlash = 0.55f;
    [SerializeField, Tooltip("갈라지는 순간과 납도 순간 화면이 당겨지는 FOV 변화량(도).")]
    private float _fovPunch = 10f;
    [SerializeField, Min(0.01f)] private float _fovPunchDuration = 0.35f;
    [SerializeField, Min(0f), Tooltip("갈라진 세계를 보는 동안 카메라가 천천히 앞으로 밀리는 FOV 감소량(도).")]
    private float _pushInFov = 6f;

    [Header("납도")]
    [SerializeField, Min(0.01f)] private float _splitCloseDuration = 0.08f;
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
    private RenderTexture _liveTexture;
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

    void OnDestroy()
    {
        ReleaseLiveTexture();
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

    /// <summary>재생 중인 연출을 멈추고 화면 연출·카메라 출력·FOV를 원래대로 돌린다. onFinished는 호출하지 않는다.</summary>
    public void Stop()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = null;
        EndLiveSplit();
        _overlay.ClearAll();
        _fovPunchAge = -1f;
        _pushIn = 0f;
        _pushInTarget = 0f;
        if (_fovApplied) _camera.fieldOfView = _baseFov;
        _fovApplied = false;
    }

    /// <summary>
    /// 투사체를 없앤 뒤 세계를 사선으로 갈라 머물고, 납도와 함께 닫으며 대상 적을 동시에 처치한 다음 onFinished를 호출한다.
    /// 처치 대상은 수락 순간 시선 기준 전방 범위의 적이다.
    /// </summary>
    private IEnumerator RunSequence(Action onFinished)
    {
        List<EnemyHealth> targets = CollectFrontEnemies();
        ReleaseProjectiles();

        // 세계 가르기: 큰 곡선 검격과 함께 실시간 세계가 둘로 갈라진 채 적은 아직 서 있다.
        SlashAcross();
        BeginLiveSplit();
        _overlay.ShowCutLine(_cutAngle, _seamThickness, 0.08f);
        _overlay.Flash(_cutFlash, 0.2f);
        PunchFov();
        yield return AnimateSplit(0f, _splitDistance, _splitOpenDuration);
        PushIn(_pushInFov, _splitHoldDuration);
        yield return WaitReal(_splitHoldDuration);

        // 납도: 갈라진 세계가 닫히는 순간 선이 흩어지고 대상 적이 동시에 쓰러진다.
        yield return AnimateSplit(_splitDistance, 0f, _splitCloseDuration);
        EndLiveSplit();
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

    /// <summary>카메라가 렌더 텍스처에 그리게 바꾸고 그 실시간 화면을 사선 두 절반으로 오버레이에 보인다. 틈은 암흑으로 검게 보인다.</summary>
    private void BeginLiveSplit()
    {
        ReleaseLiveTexture();
        _liveTexture = new RenderTexture(Screen.width, Screen.height, 24);
        _camera.targetTexture = _liveTexture;
        _overlay.SetVoid(1f);
        _overlay.SetSplit(_liveTexture, _cutAngle, 0f);
    }

    /// <summary>separation을 from에서 to로 duration 동안 바꿔 갈라진 두 절반을 벌리거나 닫는다.</summary>
    private IEnumerator AnimateSplit(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
            _overlay.SetSplit(_liveTexture, _cutAngle, Mathf.Lerp(from, to, t));
            yield return null;
            elapsed += EmpowerOverlay.RealDeltaTime();
        }
        _overlay.SetSplit(_liveTexture, _cutAngle, to);
    }

    /// <summary>카메라를 다시 화면에 직접 그리게 돌리고 사선 절반과 암흑을 숨긴 뒤 렌더 텍스처를 해제한다.</summary>
    private void EndLiveSplit()
    {
        if (_liveTexture == null) return;
        _camera.targetTexture = null;
        _overlay.SetSplit(null, 0f, 0f);
        _overlay.SetVoid(0f);
        ReleaseLiveTexture();
    }

    /// <summary>만든 렌더 텍스처가 있으면 해제·파괴하고 _liveTexture를 비운다.</summary>
    private void ReleaseLiveTexture()
    {
        if (_liveTexture == null) return;
        if (_camera != null && _camera.targetTexture == _liveTexture) _camera.targetTexture = null;
        _liveTexture.Release();
        Destroy(_liveTexture);
        _liveTexture = null;
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
