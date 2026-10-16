using System;
using System.Collections;

using UnityEngine;

/// <summary>
/// 숲에서 스폰되어 전조 뒤 수리검 한 발을 발사한다.
/// 발사 후 실루엣을 잠시 표시하고 디스폰을 알린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BambooThrower : MonoBehaviour
{
    [Header("투척")]
    [SerializeField] private DefenseShuriken _shurikenPrefab;
    [SerializeField] private Transform _muzzle;
    [SerializeField] private Transform _signalPoint;
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField, Min(0.1f)] private float _minimumCooldown = 1.5f;
    [SerializeField, Min(0.1f)] private float _maximumCooldown = 3f;
    [SerializeField, Min(0f)] private float _minimumThrowDistance = 6f;
    [SerializeField, Min(1f)] private float _maximumThrowDistance = 32f;

    [Header("투척 전조")]
    [SerializeField, Min(0.05f)] private float _telegraphDuration = 0.6f;
    [SerializeField] private SoundId _telegraphSound = SoundId.EnemyHeartbeat;
    [SerializeField] private SoundId _throwSound = SoundId.EnemyRangedAttackVoice;

    [Header("발사 후 표시")]
    [SerializeField] private Renderer[] _bodyRenderers = Array.Empty<Renderer>();
    [SerializeField, Min(0f)] private float _revealDuration = 0.35f;

    [Header("동작 상태")]
    private BambooDefenseDirector _director;
    private Coroutine _routine;
    private bool _reserved;
    private int _telemetryAttackId;
    private bool _spawned;

    public event Action<BambooThrower> Despawned;

    void Awake()
    {
        SetRevealed(false);
    }

    void OnEnable()
    {
        if (_spawned)
            BeginRoutine();
    }

    void OnDisable()
    {
        bool notifyDespawn = _spawned;
        _spawned = false;

        StopThrowCycle();
        SetRevealed(false);

        if (notifyDespawn)
            Despawned?.Invoke(this);
    }

    /// <summary>
    /// director와 position을 저장하고 투척수를 활성화한다.
    /// 스폰 상태를 설정하여 이번 활성화의 단발 투척을 시작한다.
    /// </summary>
    internal void Activate(BambooDefenseDirector director, Vector3 position)
    {
        _director = director;
        transform.position = position;
        FacePlayer();
        _spawned = true;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        else
            BeginRoutine();
    }

    /// <summary>
    /// 기존 투척 코루틴과 예약을 정리한 뒤 새 투척을 시작한다.
    /// 시작한 코루틴을 _routine에 저장한다.
    /// </summary>
    private void BeginRoutine()
    {
        StopThrowCycle();
        _routine = StartCoroutine(RunThrowCycle());
    }

    /// <summary>
    /// _routine을 정지하고 투척 전에 보유한 예약을 반환한다.
    /// 코루틴 참조와 예약 상태를 정리한다.
    /// </summary>
    private void StopThrowCycle()
    {
        if (_routine != null)
            StopCoroutine(_routine);

        _routine = null;
        CancelReservation();
    }

    /// <summary>
    /// 대기·전조·노출 설정과 디렉터의 투척 허가를 사용한다.
    /// 수리검 한 발을 발사한 뒤 투척수를 비활성화한다.
    /// </summary>
    private IEnumerator RunThrowCycle()
    {
        while (_spawned)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(_minimumCooldown, Mathf.Max(_minimumCooldown, _maximumCooldown)));

            if (!CanThrow())
                continue;

            if (!IsPlayerInThrowRange())
            {
                Relocate();
                continue;
            }

            while (!_director.TryReserveThrow())
                yield return null;

            _reserved = true;

            if (_telegraphSound != SoundId.None)
                Managers.Sound.PlayAttached(_telegraphSound, gameObject);

            _telemetryAttackId = BlindBlade.Telemetry.CombatTelemetrySignals.Begin("BambooShuriken", "Projectile",
                _muzzle.position, Game.Player.transform.position);
            _director.NotifyThrowTelegraph(_signalPoint.position, _telegraphDuration);

            yield return new WaitForSeconds(_telegraphDuration);

            if (!CanThrow())
            {
                CancelReservation();
                continue;
            }

            Throw();

            if (_revealDuration > 0f)
                yield return new WaitForSeconds(_revealDuration);

            _routine = null;
            gameObject.SetActive(false);
            yield break;
        }

        _routine = null;
    }

    /// <summary>
    /// 플레이어의 예측 위치를 향해 수리검을 생성하고 유도·반사 대상 조회용 디렉터와 함께 발사한다.
    /// 예약을 비행 집계로 넘기고 발사음과 실루엣을 표시한다.
    /// </summary>
    private void Throw()
    {
        PlayerController player = Game.Player;
        Vector3 target = _shurikenPrefab.PredictAimPoint(_muzzle.position, player.GetComponent<PlayerMotor>(), _director.PlayerVelocity);

        Vector3 direction = (target - _muzzle.position).normalized;
        FacePlayer();

        DefenseShuriken shuriken = Instantiate(_shurikenPrefab, _muzzle.position, Quaternion.LookRotation(direction));

        shuriken.Launch(gameObject, player.GetComponent<PlayerHealth>(), _damage, direction, _director, _telemetryAttackId);
        _telemetryAttackId = 0;

        _reserved = false;
        _director.NotifyShurikenLaunched(shuriken);

        if (_throwSound != SoundId.None)
            Managers.Sound.Play(_throwSound, _signalPoint.position);

        SetRevealed(true);
    }

    /// <summary>
    /// 디렉터에 자신을 제외한 빈 투척 위치를 요청한다.
    /// 위치를 찾으면 투척수를 이동시키고 플레이어 방향으로 회전한다.
    /// </summary>
    private void Relocate()
    {
        if (!_director.TryFindThrowerPosition(this, out Vector3 position))
            return;

        transform.position = position;
        FacePlayer();
    }

    /// <summary>
    /// 현재 스폰 상태와 플레이어·게임 상태를 검사한다.
    /// 투척수가 활성 상태이고 플레이 중이면 true를 반환한다.
    /// </summary>
    private bool CanThrow()
    {
        return _spawned
            && isActiveAndEnabled
            && Game.Player != null
            && Game.Player.CurrentState != PlayerActionState.Dead
            && Game.Instance.GameStateManager.State == GameplayState.Playing;
    }

    /// <summary>
    /// 플레이어까지의 수평 거리와 최소·최대 투척 거리를 비교한다.
    /// 설정된 거리 범위 안이면 true를 반환한다.
    /// </summary>
    private bool IsPlayerInThrowRange()
    {
        Vector3 offset = Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up);

        float distance = offset.sqrMagnitude;
        return distance >= _minimumThrowDistance * _minimumThrowDistance
            && distance <= _maximumThrowDistance * _maximumThrowDistance;
    }

    /// <summary>
    /// 현재 플레이어 위치를 사용해 투척수의 수평 방향을 정한다.
    /// 유효한 방향이면 transform.rotation을 변경한다.
    /// </summary>
    private void FacePlayer()
    {
        if (Game.Player == null)
            return;

        Vector3 offset = Vector3.ProjectOnPlane(
            Game.Player.transform.position - transform.position,
            Vector3.up);

        if (offset.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(offset);
    }

    /// <summary>
    /// visible을 몸 렌더러에 적용한다.
    /// 연결된 모든 렌더러의 enabled 상태를 변경한다.
    /// </summary>
    private void SetRevealed(bool visible)
    {
        foreach (Renderer body in _bodyRenderers)
            body.enabled = visible;
    }

    /// <summary>
    /// _reserved가 참이면 디렉터에 미발사 예약 하나를 반환한다.
    /// 반환 전에 예약 상태를 해제하여 중복 반환을 막는다.
    /// </summary>
    private void CancelReservation()
    {
        if (!_reserved)
            return;

        _reserved = false;
        BlindBlade.Telemetry.CombatTelemetrySignals.End(_telemetryAttackId, "Cancelled", transform.position);
        _telemetryAttackId = 0;
        _director.CancelThrowReservation();
    }
}