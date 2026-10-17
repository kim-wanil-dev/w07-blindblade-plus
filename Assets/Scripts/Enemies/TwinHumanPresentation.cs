using UnityEngine;

/// <summary>
/// 인간 보스의 도형 몸체를 Play 중 숨겨 연기만 보이게 하고,
/// 사격을 준비할 때마다 자기 위치 주변 시야를 잠깐 드러낸다.
/// 스테이지1 보스처럼 피격 후 받아칠 수 있는 무적 방어 상태 동안 플레이어 쪽에 가드 자세 카타나를 띄운다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack))]
public sealed class TwinHumanPresentation : MonoBehaviour
{
    private const string GUARD_SCABBARD_NAME = "Saya";
    private static readonly int _guardHoldStateId = Animator.StringToHash("Base Layer.GuardHold");

    [Header("외형")]
    [SerializeField, Tooltip("편집 화면에서만 보이고 Play 중에는 숨겨 연기 방출 모양으로만 쓰는 도형 렌더러")]
    private Renderer[] _bodyRenderers;

    [Header("사격 준비 공개")]
    [SerializeField, Min(0f), Tooltip("사격 준비마다 보스 위치에서 드러내는 시야 반경(미터)")]
    private float _rangedRevealRadius = 5f;
    private HumanBossAttack _attack;
    private SensoryPerceptionMask _perceptionMask;

    [Header("방어 자세 칼")]
    [SerializeField, Tooltip("방어 상태 동안 인간 앞에 띄울 플레이어 카타나 모형(KatanaRig). 스테이지1 보스와 같다.")]
    private GameObject _guardWeaponPrefab;
    [SerializeField, Tooltip("플레이어와 같은 가드 자세 상태(GuardHold)를 가진 카타나 Animator Controller")]
    private RuntimeAnimatorController _guardWeaponController;
    [SerializeField, Tooltip("플레이어를 향한 방향 기준 카타나 모형의 위치(미터). x는 오른쪽, y는 높이, z는 플레이어 쪽으로 내미는 거리다.")]
    private Vector3 _guardWeaponOffset = new Vector3(0f, 1.6f, 0.6f);
    [SerializeField, Min(0.01f), Tooltip("카타나 모형 크기 배율. 1인칭용 모형이라 멀리서 보이도록 키운다.")]
    private float _guardWeaponScale = 1.5f;
    private TwinBossController _controller;
    private GameObject _guardWeapon;
    private Animator _guardWeaponAnimator;

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        _controller = GetComponentInParent<TwinBossController>();
        foreach (Renderer body in _bodyRenderers)
            body.enabled = false;
        CreateGuardWeapon();
    }

    void OnEnable()
    {
        _attack.ShotPreparing += HandleShotPreparing;
    }

    void OnDisable()
    {
        _attack.ShotPreparing -= HandleShotPreparing;
        if (_guardWeapon != null) _guardWeapon.SetActive(false);
    }

    void LateUpdate()
    {
        UpdateGuardWeapon();
    }

    void OnDrawGizmos()
    {
        // Play 중 숨긴 도형을 Scene 뷰에서만 확인할 수 있게 한다.
        SceneShapeGizmo.Draw(_bodyRenderers);
    }

    /// <summary>
    /// _guardWeaponPrefab을 인간 자식으로 한 번 복제해 _guardWeaponScale 크기로 두고 검집 렌더러를 끈 뒤 숨긴다.
    /// 복제본 Animator에 _guardWeaponController를 연결하며 위치와 방향은 표시 중에 갱신한다.
    /// </summary>
    private void CreateGuardWeapon()
    {
        _guardWeapon = Instantiate(_guardWeaponPrefab, transform);
        _guardWeapon.name = "TwinHumanGuardKatana";
        _guardWeapon.transform.localScale = Vector3.one * _guardWeaponScale;
        _guardWeaponAnimator = _guardWeapon.GetComponentInChildren<Animator>(true);
        _guardWeaponAnimator.runtimeAnimatorController = _guardWeaponController;
        // 가드 자세는 칼만 보이도록 검집 렌더러를 끈다.
        Transform scabbard = _guardWeapon.transform.Find(GUARD_SCABBARD_NAME);
        if (scabbard != null)
            foreach (Renderer part in scabbard.GetComponentsInChildren<Renderer>(true))
                part.enabled = false;
        _guardWeapon.SetActive(false);
    }

    /// <summary>
    /// 인간이 받아칠 수 있는 무적 방어 상태(TwinBossController.IsHumanDefending)이면 카타나 모형을 켜고 GuardHold 자세를 재생하며,
    /// 방어가 끝나거나 쳐내 반격을 시작하면 바로 숨긴다. 켜져 있는 동안 매 프레임 플레이어를 향하도록 _guardWeaponOffset 위치와 회전을 갱신한다.
    /// </summary>
    private void UpdateGuardWeapon()
    {
        bool defending = _controller.IsHumanDefending;
        if (_guardWeapon.activeSelf != defending)
        {
            _guardWeapon.SetActive(defending);
            if (defending) _guardWeaponAnimator.Play(_guardHoldStateId, 0, 0f);
        }
        if (!defending) return;
        Vector3 facing = Game.Player != null
            ? Vector3.ProjectOnPlane(Game.Player.transform.position - transform.position, Vector3.up) : Vector3.zero;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Quaternion rotation = Quaternion.LookRotation(facing.normalized);
        _guardWeapon.transform.SetPositionAndRotation(transform.position + rotation * _guardWeaponOffset, rotation);
    }

    /// <summary>사격 준비 사건마다 현재 위치에서 _rangedRevealRadius 반경의 시야를 공개한다.</summary>
    private void HandleShotPreparing()
    {
        if (_perceptionMask == null)
            _perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        _perceptionMask.RevealFootstep(transform.position, _rangedRevealRadius);
    }
}
