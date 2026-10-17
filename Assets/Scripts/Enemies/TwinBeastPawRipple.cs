using UnityEngine;

/// <summary>
/// 늑대 몸체의 네 발이 땅에 닿을 때마다 그 자리에 작은 물 파동을 시야 맵에 등록한다.
/// 발자국 대신 파동만 남겨 늑대가 달리는 리듬을 드러내며, 파동은 짐승 몸을 밝히지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TwinBeastWolfVisual))]
public sealed class TwinBeastPawRipple : MonoBehaviour
{
    [Header("발 파동")]
    [SerializeField] private RevealRippleSettings _ripple = new RevealRippleSettings(3.5f, 0.5f, 2, 0.08f, 0.8f,
        new Color(1f, 1f, 1f, 0.6f), true);
    [SerializeField, Tooltip("파동이 짐승 몸을 밝히지 않도록 넘기는 짐승 루트")]
    private Transform _owner;
    private TwinBeastWolfVisual _visual;
    private PerceptionRevealMap _revealMap;

    void Awake()
    {
        _visual = GetComponent<TwinBeastWolfVisual>();
    }

    void OnEnable()
    {
        _visual.PawPlanted += HandlePawPlanted;
    }

    void OnDisable()
    {
        _visual.PawPlanted -= HandlePawPlanted;
    }

    /// <summary>발이 닿은 position에 _ripple 설정의 파동을 _owner 몸을 밝히지 않게 등록한다. 감각 매니저가 없으면 건너뛴다.</summary>
    private void HandlePawPlanted(Vector3 position)
    {
        if (_revealMap == null)
        {
            if (!Game.IsInitialized || Game.Instance.SensoryManager == null) return;
            _revealMap = Game.Instance.SensoryManager.GetComponent<PerceptionRevealMap>();
        }
        _revealMap.EmitRipple(position, _ripple, _owner);
    }
}
