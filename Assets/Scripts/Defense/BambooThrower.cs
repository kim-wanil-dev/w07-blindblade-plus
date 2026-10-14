using System.Collections;

using UnityEngine;

/// <summary>
/// 대나무 숲 안의 높은 곳 또는 낮은 곳에서 전조음 뒤 수리검을 던지고 던질 때마다 위치를 옮긴다.
/// 던지는 순간에만 빨간 실루엣으로 드러나며 체력과 사망 처리는 EnemyHealth가 담당한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public sealed class BambooThrower : MonoBehaviour
{
    [Header("투척")]
    [SerializeField] private DefenseShuriken _shurikenPrefab;
    [SerializeField] private Transform _muzzle;
    [SerializeField, Min(0f)] private float _damage = 10f;
    [SerializeField, Min(0.1f)] private float _minimumCooldown = 1.5f;
    [SerializeField, Min(0.1f)] private float _maximumCooldown = 3f;
    [SerializeField, Min(0f), Tooltip("플레이어가 이 수평 거리보다 가까우면 던지기 전에 위치를 옮긴다.")]
    private float _minimumThrowDistance = 6f;
    [SerializeField, Min(1f), Tooltip("플레이어가 이 수평 거리보다 멀면 던지기 전에 위치를 옮긴다.")]
    private float _maximumThrowDistance = 32f;
    private EnemyHealth _health;
    private Collider _body;
    private BambooDefenseDirector _director;
    private DefenseShuriken _shuriken;
    private Coroutine _routine;
    private bool _reserved;

    [Header("투척 전조")]
    [SerializeField, Min(0.05f)] private float _telegraphDuration = 0.6f;
    [SerializeField] private SoundId _telegraphSound = SoundId.EnemyHeartbeat;
    [SerializeField] private SoundId _throwSound = SoundId.EnemyRangedAttackVoice;

    [Header("빨간 실루엣")]
    [SerializeField] private Renderer[] _bodyRenderers = System.Array.Empty<Renderer>();
    [SerializeField, Min(0.05f)] private float _revealDuration = 0.35f;
    private float _revealUntil;

    [Header("위치 이동")]
    [SerializeField, Min(0f), Tooltip("수리검이 끝난 뒤 다른 위치로 옮기기까지의 대기 시간(초)")]
    private float _relocateDelay = 0.6f;

    public EnemyHealth Health => _health;

    void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _body = GetComponent<Collider>();
        SetRevealed(false);
    }

    void OnEnable()
    {
        if (_director != null)
            BeginRoutine();
    }

    void Update()
    {
        if (_revealUntil > 0f && Time.time >= _revealUntil)
        {
            _revealUntil = 0f;
            SetRevealed(false);
        }
    }

    void OnDisable()
    {
        StopThrowCycle();
        _revealUntil = 0f;
        SetRevealed(false);
    }

    /// <summary>
    /// director와 position으로 투척수를 배치하고 투척 순환을 시작한다.
    /// 비활성 상태면 활성화하여 OnEnable에서 시작하고 이미 활성 상태면 즉시 다시 시작한다.
    /// </summary>
    internal void Activate(BambooDefenseDirector director, Vector3 position)
    {
        _director = director;
        transform.position = position;
        FacePlayer();
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        else
            BeginRoutine();
    }

    /// <summary>진행 중인 투척 순환을 정지하고 처음부터 다시 시작한다.</summary>
    private void BeginRoutine()
    {
        StopThrowCycle();
        _routine = StartCoroutine(RunThrowCycle());
    }

    /// <summary>진행 중 코루틴과 보유 예약·비행 구독을 정리한다. 이미 발사한 수리검의 수명은 유지한다.</summary>
    private void StopThrowCycle()
    {
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = null;
        CancelReservation();
        DetachShuriken();
    }

    /// <summary>
    /// 대기·투척 허가·전조·투척·결과 대기·위치 이동을 생존하는 동안 반복한다.
    /// 플레이어가 투척 거리 범위를 벗어나면 던지지 않고 위치만 옮기며 허가는 디렉터의 동시 투척 제한을 따른다.
    /// </summary>
    private IEnumerator RunThrowCycle()
    {
        while (_health.IsAlive)
        {
            yield return new WaitForSeconds(Random.Range(_minimumCooldown, Mathf.Max(_minimumCooldown, _maximumCooldown)));
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
            _director.NotifyThrowTelegraph(_body.bounds.center, _telegraphDuration);
            yield return new WaitForSeconds(_telegraphDuration);
            if (!CanThrow())
            {
                CancelReservation();
                continue;
            }
            Throw();
            while (_shuriken != null)
                yield return null;
            yield return new WaitForSeconds(_relocateDelay);
            Relocate();
        }
        _routine = null;
    }

    /// <summary>
    /// 디렉터의 플레이어 이동 속도로 예측한 조준점에 수리검을 던지고 짧게 실루엣을 드러낸다.
    /// 예약을 디렉터의 비행 수리검으로 넘기며 유도·반사 대상 조회용 디렉터를 함께 전달한다.
    /// </summary>
    private void Throw()
    {
        PlayerController player = Game.Player;
        Vector3 target = _shurikenPrefab.PredictAimPoint(_muzzle.position, player.GetComponent<PlayerMotor>(),
            _director.PlayerVelocity);
        Vector3 direction = (target - _muzzle.position).normalized;
        FacePlayer();
        _shuriken = Instantiate(_shurikenPrefab, _muzzle.position, Quaternion.LookRotation(direction));
        _shuriken.Released += HandleShurikenReleased;
        _shuriken.Launch(gameObject, player.GetComponent<PlayerHealth>(), _damage, direction, _director);
        _reserved = false;
        _director.NotifyShurikenLaunched(_shuriken);
        if (_throwSound != SoundId.None)
            Managers.Sound.PlayAttached(_throwSound, gameObject);
        _revealUntil = Time.time + _revealDuration;
        SetRevealed(true);
    }

    /// <summary>디렉터에서 빈 위치를 받아 이동하고 플레이어를 바라본다. 빈 위치가 없으면 현재 위치를 유지한다.</summary>
    private void Relocate()
    {
        if (!_director.TryFindThrowerPosition(this, out Vector3 position))
            return;
        transform.position = position;
        FacePlayer();
    }

    /// <summary>현재 플레이어와 자신이 생존하고 게임이 진행 중이면 true를 반환한다.</summary>
    private bool CanThrow()
    {
        return _health.IsAlive && Game.Player != null && Game.Player.CurrentState != PlayerActionState.Dead
            && Game.Instance.GameStateManager.State == GameplayState.Playing;
    }

    /// <summary>플레이어와의 수평 거리가 최소·최대 투척 거리 사이이면 true를 반환한다.</summary>
    private bool IsPlayerInThrowRange()
    {
        Vector3 offset = Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up);
        float distance = offset.sqrMagnitude;
        return distance >= _minimumThrowDistance * _minimumThrowDistance
            && distance <= _maximumThrowDistance * _maximumThrowDistance;
    }

    /// <summary>플레이어 방향의 수평 회전으로 몸을 돌린다. 같은 위치이면 회전을 유지한다.</summary>
    private void FacePlayer()
    {
        if (Game.Player == null)
            return;
        Vector3 offset = Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up);
        if (offset.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(offset);
    }

    /// <summary>visible에 따라 몸 렌더러를 켜거나 꺼 빨간 실루엣 표시를 전환한다.</summary>
    private void SetRevealed(bool visible)
    {
        foreach (Renderer body in _bodyRenderers)
            body.enabled = visible;
    }

    /// <summary>던지기 전 보유한 동시 투척 예약을 디렉터에 반환한다. 예약이 없으면 무시한다.</summary>
    private void CancelReservation()
    {
        if (!_reserved)
            return;
        _reserved = false;
        _director.CancelThrowReservation();
    }

    /// <summary>shuriken의 비행 종료 알림을 받아 보유 참조를 해제한다.</summary>
    private void HandleShurikenReleased(DefenseShuriken shuriken)
    {
        shuriken.Released -= HandleShurikenReleased;
        if (_shuriken == shuriken)
            _shuriken = null;
    }

    /// <summary>비행 중인 수리검의 구독만 해제한다. 수리검과 디렉터의 비행 수는 유지한다.</summary>
    private void DetachShuriken()
    {
        if (_shuriken == null)
            return;
        _shuriken.Released -= HandleShurikenReleased;
        _shuriken = null;
    }
}
