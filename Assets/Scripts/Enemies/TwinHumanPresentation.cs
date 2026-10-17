using UnityEngine;

/// <summary>
/// 인간 보스의 도형 몸체를 Play 중 숨겨 연기만 보이게 하고,
/// 사격을 준비할 때마다 자기 위치 주변 시야를 잠깐 드러낸다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack))]
public sealed class TwinHumanPresentation : MonoBehaviour
{
    [Header("외형")]
    [SerializeField, Tooltip("편집 화면에서만 보이고 Play 중에는 숨겨 연기 방출 모양으로만 쓰는 도형 렌더러")]
    private Renderer[] _bodyRenderers;

    [Header("사격 준비 공개")]
    [SerializeField, Min(0f), Tooltip("사격 준비마다 보스 위치에서 드러내는 시야 반경(미터)")]
    private float _rangedRevealRadius = 5f;
    private HumanBossAttack _attack;
    private SensoryPerceptionMask _perceptionMask;

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        foreach (Renderer body in _bodyRenderers)
            body.enabled = false;
    }

    void OnEnable()
    {
        _attack.ShotPreparing += HandleShotPreparing;
    }

    void OnDisable()
    {
        _attack.ShotPreparing -= HandleShotPreparing;
    }

    void OnDrawGizmos()
    {
        // Play 중 숨긴 도형을 Scene 뷰에서만 확인할 수 있게 한다.
        SceneShapeGizmo.Draw(_bodyRenderers);
    }

    /// <summary>사격 준비 사건마다 현재 위치에서 _rangedRevealRadius 반경의 시야를 공개한다.</summary>
    private void HandleShotPreparing()
    {
        if (_perceptionMask == null)
            _perceptionMask = Game.Instance.SensoryManager.GetComponent<SensoryPerceptionMask>();
        _perceptionMask.RevealFootstep(transform.position, _rangedRevealRadius);
    }
}
