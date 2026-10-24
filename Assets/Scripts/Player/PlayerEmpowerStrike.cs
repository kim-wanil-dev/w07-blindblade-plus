using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>
/// 강화 공격을 암전 속 일섬 한 장면으로 실행한다.
/// 화면이 암흑으로 덮인 채 사선을 따라 큰 곡선 검격과 갈라진 선이 그어지고, 적이 아직 서 있는 동안 머문 뒤
/// 납도와 함께 화면이 돌아오며 범위 안의 적이 동시에 쓰러지고 선이 먹 조각으로 흩어진다.
/// 범위 안의 적대 투사체는 납도 순간 가드 소리를 내며 사라진다. 카메라 출력은 바꾸지 않으며 화면 표현은 EmpowerOverlay가 그린다.
/// 연출 시간은 일시정지를 제외한 실시간이다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EmpowerOverlay), typeof(PlayerUltimateGroundMark))]
public sealed class PlayerEmpowerStrike : MonoBehaviour
{
    private const string PATTERN_ID = "EmpowerStrike";

    [Header("공격 피해")]
    [SerializeField, Min(0f)] private float _damage = 100f;

    [Header("암전")]
    [SerializeField, Min(0f), Tooltip("수락부터 납도까지 화면이 암흑으로 덮인 채 검격과 갈라진 선만 보이는 실시간.")]
    private float _blackoutDuration = 1f;
    [SerializeField, Range(-80f, 80f), Tooltip("갈라진 선과 곡선 검격의 화면 기울기(도).")]
    private float _cutAngle = 24f;
    [SerializeField, Min(1f), Tooltip("사선을 따라 그어지는 곡선 검격 지름(1920×1080 기준 픽셀).")]
    private float _cutSlashSize = 2400f;
    [SerializeField, Min(0.5f)] private float _seamThickness = 4f;
    [SerializeField, Range(0f, 1f)] private float _cutFlash = 0.55f;
    [SerializeField, Min(0f), Tooltip("보스 거합 받아치기에서 적의 붉은 사선 → 덮는 대칭 사선 → 기본 사선을 차례로 긋는 간격(실시간 초).")]
    private float _counterLayerDelay = 0.15f;
    [SerializeField, Tooltip("납도 순간 화면이 돌아올 때 당겨지는 FOV 변화량(도).")]
    private float _fovPunch = 10f;
    [SerializeField, Min(0.01f)] private float _fovPunchDuration = 0.35f;
    [SerializeField, Min(0f), Tooltip("암전 동안 카메라가 앞으로 밀려 화면이 돌아오는 순간 남아 있는 FOV 감소량(도). 복귀 후 0.25초에 걸쳐 풀린다.")]
    private float _pushInFov = 6f;

    [Header("납도")]
    [SerializeField, Range(0f, 1f)] private float _sheatheFlash = 0.4f;
    [SerializeField, Min(0f)] private float _sheathePause = 0.3f;
    [SerializeField, Min(0f), Tooltip("RainyStage에서 암전이 걷히고 납도 섬광(0.2초)이 지난 뒤 사선 평면 빗방울이 터지기까지의 실시간(초). 납도 멈춤 시간 안에서 터진다.")]
    private float _rainBurstDelay = 0.2f;
    [SerializeField, Min(0)] private int _scatterCount = 48;
    [SerializeField, Min(0.01f), Tooltip("갈라진 선이 흩어진 먹 조각이 떠다니다 사라지는 실시간. 강화가 끝난 뒤에도 남는다.")]
    private float _scatterLifetime = 1.2f;

    [Header("소리")]
    [SerializeField, Tooltip("암전이 시작되며 곡선 검격이 그어지는 순간의 효과음.")]
    private SoundId _blackoutSound = SoundId.EmpowerBlackoutSlash;
    [SerializeField, Tooltip("암흑이 걷히며 납도하는 순간의 효과음.")]
    private SoundId _sheatheSound = SoundId.EmpowerBlackoutSheathe;

    [Header("공격 범위")]
    [SerializeField, Range(1f, 360f), Tooltip("납도 순간 시선 기준 베는 전방 각도.")]
    private float _strikeAngle = 180f;
    [SerializeField, Min(0f), Tooltip("전방 베기 범위의 수평 반경(미터).")]
    private float _strikeRadius = 50f;
    [SerializeField, Min(0f), Tooltip("방향과 관계없이 베는 플레이어 주변 수평 반경(미터).")]
    private float _nearbyStrikeRadius = 20f;

    [Header("대나무 베기")]
    [SerializeField, Range(0f, 0.5f), Tooltip("갈라진 선(_cutAngle, 화면 중심)이 대나무를 가로지르는 점이 화면 가장자리 밖이어도 베는 여유 범위(화면 크기 비율). 선에 걸치지 않은 대나무는 베지 않는다.")]
    private float _bambooViewMargin = 0.1f;

    [Header("상태")]
    private EmpowerOverlay _overlay;
    private PlayerUltimateGroundMark _groundMark;
    private PlayerMotor _motor;
    private Camera _camera;
    private Coroutine _routine;
    private float _baseFov;
    private float _fovPunchAge = -1f;
    private float _pushIn;
    private float _pushInTarget;
    private float _pushInRate;
    private bool _fovApplied;

    public float CutAngle => _cutAngle;

    void Awake()
    {
        _overlay = GetComponent<EmpowerOverlay>();
        _groundMark = GetComponent<PlayerUltimateGroundMark>();
        _motor = GetComponent<PlayerMotor>();
        _camera = GetComponentInChildren<Camera>();
        _baseFov = _camera.fieldOfView;
    }

    void Start()
    {
        // 샘플 데이터를 재생 때 불러오면 암전 순간 소리가 늦어지므로 미리 올려 둔다.
        if (_blackoutSound != SoundId.None) Managers.Sound.PreloadSampleData(_blackoutSound);
        if (_sheatheSound != SoundId.None) Managers.Sound.PreloadSampleData(_sheatheSound);
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
        int attackId = MultiKillAttackRuntime.AllocateAttackId();
        _routine = StartCoroutine(RunSequence(onFinished, false, Color.clear, attackId));
    }

    /// <summary>
    /// 보스 거합 받아치기용 강화 공격 연출을 시작한다. 암전 속에 적의 거합 사선(기본 사선의 화면 세로축 대칭)을 enemyCutColor로 먼저 긋고
    /// 같은 자리에 플레이어의 대칭 사선을 덮은 뒤 기본 사선을 더해 X자를 이룬다. 땅·대나무·빗방울도 두 사선으로 베고, 판정과 피해는 평소처럼 한 번이다.
    /// 납도까지 끝나면 onFinished를 한 번 호출한다.
    /// </summary>
    public void PlayCounter(Action onFinished, Color enemyCutColor)
    {
        Stop();
        int attackId = MultiKillAttackRuntime.AllocateAttackId();
        _routine = StartCoroutine(RunSequence(onFinished, true, enemyCutColor, attackId));
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
    /// attackId를 피해 문맥에 보존해 _blackoutDuration 동안 암흑 위에 곡선 검격과 갈라진 선을 긋고 머문다.
    /// 납도 순간 암흑을 걷어 화면을 되돌리며 범위 안의 적을 공격하고 적대 투사체를 가드 소리와 함께 없앤 뒤 선을 흩뜨리고 onFinished를 호출한다.
    /// 적과 투사체는 납도 순간 위치·시선 기준 전방 반경 및 주변 반경의 합집합으로 판정한다. 수락 순간 화면의 갈라진 선에 걸친 대나무는 거리와 상관없이 그 선을 따라 납도 순간 벤다. 같은 선을 따라 땅에 흰 흔적을 남긴다.
    /// RainyStage에서는 암전이 걷히고 _rainBurstDelay 뒤에 같은 사선 평면 주변 빗방울이 카메라 옆·조금 뒤까지 평면 바깥쪽으로 터진다.
    /// counter이면 보스 거합 받아치기로, enemyCutColor의 적 사선 위에 대칭 사선을 덮고 기본 사선을 더한 X자를 _counterLayerDelay 간격으로 긋고 세계도 두 사선으로 벤다.
    /// </summary>
    private IEnumerator RunSequence(Action onFinished, bool counter, Color enemyCutColor, int attackId)
    {
        Vector3 bambooOrigin = transform.position;
        Matrix4x4 bambooView = _camera.projectionMatrix * _camera.worldToCameraMatrix;
        Vector3 bambooCutPoint = _camera.transform.position;
        Vector3 bambooCutNormal = CalculateBambooCutNormal(_cutAngle);
        Vector3 mirrorCutNormal = CalculateBambooCutNormal(-_cutAngle);

        // 암전: 화면이 검게 덮인 채 곡선 검격과 갈라진 선만 그어지고 적은 아직 서 있다.
        _overlay.SetVoid(1f);
        PlaySound(_blackoutSound);
        // 암흑 아래에서 카메라를 밀어 두어 화면이 돌아오는 순간 당겨졌다 풀리는 느낌을 준다.
        PushIn(_pushInFov, _blackoutDuration);
        float layered = 0f;
        if (counter)
        {
            // 받아치기: 적의 붉은 사선 위에 같은 대칭 사선을 덮어 베고, 이어 기본 사선을 더해 X자로 되받아 친다.
            SlashAcross(-_cutAngle, true, enemyCutColor);
            _overlay.ShowCutLine(-_cutAngle, _seamThickness, 0.08f, enemyCutColor);
            yield return WaitReal(_counterLayerDelay);
            SlashAcross(-_cutAngle, true, _overlay.AccentColor);
            _overlay.ShowCutLine(-_cutAngle, _seamThickness, 0.08f);
            _overlay.Flash(_cutFlash, 0.2f);
            yield return WaitReal(_counterLayerDelay);
            SlashAcross(_cutAngle, false, _overlay.AccentColor);
            _overlay.ShowCrossLine(_cutAngle, _seamThickness, 0.08f);
            layered = _counterLayerDelay * 2f;
        }
        else
        {
            SlashAcross(_cutAngle, false, _overlay.AccentColor);
            _overlay.ShowCutLine(_cutAngle, _seamThickness, 0.08f);
        }
        _overlay.Flash(_cutFlash, 0.2f);
        yield return WaitReal(Mathf.Max(0f, _blackoutDuration - layered));

        // 납도: 암흑이 걷히는 순간 현재 위치·시선의 범위로 적과 투사체를 판정한다.
        _overlay.SetVoid(0f);
        PlaySound(_sheatheSound);
        Vector3 strikeOrigin = transform.position;
        Vector3 strikeForward = _motor.ViewForward;
        List<EnemyHealth> targets = CollectStrikeTargets(strikeOrigin, strikeForward);
        StrikeEnemies(targets, strikeOrigin, strikeForward, attackId);
        MultiKillAttackRuntime.CompleteAttack(attackId);
        ReleaseProjectiles(strikeOrigin, strikeForward);
        BambooGrove.CutInView(bambooView, _bambooViewMargin, bambooOrigin, bambooCutPoint, bambooCutNormal);
        _groundMark.Show(bambooCutPoint, bambooCutNormal, bambooView);
        if (counter)
        {
            BambooGrove.CutInView(bambooView, _bambooViewMargin, bambooOrigin, bambooCutPoint, mirrorCutNormal);
            _groundMark.Show(bambooCutPoint, mirrorCutNormal, bambooView);
        }
        _overlay.ScatterCutLine(_scatterCount, _scatterLifetime);
        _overlay.Flash(_sheatheFlash, 0.2f);
        PunchFov();
        PushIn(0f, 0.25f);
        // 빗방울은 암전과 납도 섬광이 걷혀 화면이 보이는 뒤에 사선을 따라 터진다.
        float burstDelay = Mathf.Min(_rainBurstDelay, _sheathePause);
        yield return WaitReal(burstDelay);
        RainStageWeather.RecordCutPlaneBurst(bambooCutPoint, bambooCutNormal, _camera.transform.forward);
        if (counter) RainStageWeather.RecordCutPlaneBurst(bambooCutPoint, mirrorCutNormal, _camera.transform.forward);
        yield return WaitReal(_sheathePause - burstDelay);

        _routine = null;
        onFinished?.Invoke();
    }

    /// <summary>
    /// 세계를 가르는 사선(cutAngle)을 따라 화면을 가로지르는 큰 color 색 곡선 검격을 긋는다.
    /// 초승달의 시위가 사선과 나란하고 휜 바깥 날이 화면 중심을 지나도록 놓으며, mirror이면 검격의 좌우를 뒤집는다.
    /// </summary>
    private void SlashAcross(float cutAngle, bool mirror, Color color)
    {
        float rotation = cutAngle - 90f;
        Vector2 bulge = new Vector2(Mathf.Cos(rotation * Mathf.Deg2Rad), Mathf.Sin(rotation * Mathf.Deg2Rad));
        _overlay.Slash(-bulge * (_cutSlashSize * 0.42f), _cutSlashSize, rotation, mirror, 0.1f, 0.5f, color);
    }

    /// <summary>soundId가 지정되어 있으면 플레이어 위치에서 효과음을 한 번 재생한다.</summary>
    private void PlaySound(SoundId soundId)
    {
        if (soundId != SoundId.None)
            Managers.Sound.Play(soundId, transform.position);
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

    /// <summary>
    /// origin과 forward를 사용하는 공통 범위 판정 함수를 세 투사체 형식에 전달해 범위 안의 적대 투사체만 가드 성공 소리와 함께 없앤다.
    /// </summary>
    private void ReleaseProjectiles(Vector3 origin, Vector3 forward)
    {
        Predicate<Vector3> isInRange = position => IsWithinStrikeRange(origin, forward, position);
        DefenseShuriken.ReleaseInRange(isInRange);
        EnemyProjectile.ReleaseInRange(isInRange);
        TwinBossProjectile.ReleaseInRange(isInRange);
    }

    /// <summary>
    /// 화면 중심을 지나 cutAngle만큼 기운 갈라진 선(EmpowerOverlay.ShowCutLine)과 겹쳐 보이도록, 카메라를 지나는 평면의 법선을 반환한다.
    /// 화면 선 위 두 점으로 향하는 시선 광선의 외적을 쓴다.
    /// </summary>
    private Vector3 CalculateBambooCutNormal(float cutAngle)
    {
        Vector2 center = new Vector2(_camera.pixelWidth * 0.5f, _camera.pixelHeight * 0.5f);
        float radians = cutAngle * Mathf.Deg2Rad;
        Vector2 along = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * (_camera.pixelHeight * 0.25f);
        Vector3 left = _camera.ScreenPointToRay(center - along).direction;
        Vector3 right = _camera.ScreenPointToRay(center + along).direction;
        return Vector3.Cross(left, right).normalized;
    }

    /// <summary>origin과 forward 기준 두 범위 중 하나에 있는 살아 있는 적을 모아 반환한다.</summary>
    private List<EnemyHealth> CollectStrikeTargets(Vector3 origin, Vector3 forward)
    {
        List<EnemyHealth> result = new List<EnemyHealth>();
        foreach (EnemyHealth enemy in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
        {
            if (!enemy.IsAlive) continue;
            if (!IsWithinStrikeRange(origin, forward, enemy.transform.position)) continue;
            result.Add(enemy);
        }
        return result;
    }

    /// <summary>origin, forward, targetPosition을 사용해 대상이 전방 각도·반경 또는 주변 수평 반경 안에 있는지 반환한다.</summary>
    private bool IsWithinStrikeRange(Vector3 origin, Vector3 forward, Vector3 targetPosition)
    {
        Vector3 offset = Vector3.ProjectOnPlane(targetPosition - origin, Vector3.up);
        float distanceSquared = offset.sqrMagnitude;
        if (distanceSquared <= _nearbyStrikeRadius * _nearbyStrikeRadius) return true;
        if (distanceSquared > _strikeRadius * _strikeRadius) return false;
        return distanceSquared <= 0.0001f || Vector3.Angle(forward, offset) <= _strikeAngle * 0.5f;
    }

    /// <summary>
    /// targets, origin, forward, attackId를 사용해 살아 있는 적에게 _damage만큼 피해를 주되 공유 체력 묶음에는 한 번만 적용한다.
    /// 공유 피해 우선(HasSharedDamagePriority) 적을 먼저 처리하고, 무적인 적은 개별적으로 막고 같은 묶음의 다른 적은 계속 검사한다.
    /// 실제 피해는 EnemyHealth.TakeDamage에 전달하며, 묶음 상대가 이미 맞아 피해를 건너뛴 적은 혈흔만 재생한다.
    /// </summary>
    private void StrikeEnemies(List<EnemyHealth> targets, Vector3 origin, Vector3 forward, int attackId)
    {
        HashSet<EnemyHealth> struck = new HashSet<EnemyHealth>();
        // 같은 묶음이 함께 걸리면 우선 적이 피격 반응을 받도록 앞에 둔다.
        targets.Sort((a, b) => (b != null && b.HasSharedDamagePriority).CompareTo(a != null && a.HasSharedDamagePriority));
        foreach (EnemyHealth enemy in targets)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            if (struck.Contains(enemy.SharedDamageKey))
            {
                enemy.PlaySharedHitEffect(enemy.transform.position + Vector3.up);
                continue;
            }
            if (!enemy.IsInvulnerable) struck.Add(enemy.SharedDamageKey);
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - origin, Vector3.up);
            Vector3 direction = toEnemy.sqrMagnitude > 0.0001f ? toEnemy.normalized : forward;
            enemy.TakeDamage(new DamageContext(_damage, gameObject, enemy.transform.position + Vector3.up,
                direction, PATTERN_ID, attackId, 0, IncomingAttackKind.PlayerOwned));
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
