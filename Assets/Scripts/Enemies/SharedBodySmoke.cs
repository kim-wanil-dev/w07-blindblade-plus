using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 근접 잡몹의 로컬 공간 몸 연기를 적마다 시뮬레이션하지 않고 공유 템플릿 입자로 그리도록 등록한다.
/// 템플릿 다섯 개는 처음 등록한 적의 몸 연기를 복제해 만들고, 화면 윤곽선 기능이 각 적의 몸 연기 위치에 템플릿 입자를 인스턴스로 그린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SharedBodySmoke : MonoBehaviour
{
    private const int TEMPLATE_COUNT = 5;
    private static readonly List<SharedBodySmoke> _active = new List<SharedBodySmoke>();
    private static readonly List<ParticleSystem> _templates = new List<ParticleSystem>();
    private static GameObject _templateRoot;
    private static ParticleSystemRenderer _templateRenderer;
    private static int _nextTemplate;

    [Header("공유 몸 연기")]
    [SerializeField] private ParticleSystemRenderer _bodySmoke;
    [SerializeField, Tooltip("켜면 몸 연기를 직접 시뮬레이션하지 않고 공유 템플릿 입자로 그립니다. 끄면 기존 파티클 시스템을 그대로 씁니다. Play 중에도 바꿀 수 있습니다.")]
    private bool _useSharedTemplate = true;
    private EnemyMeleeWarningVisual _warning;
    private bool _shared;
    private bool _modeApplied;
    private int _templateIndex;

    public static IReadOnlyList<SharedBodySmoke> Active => _active;
    public static IReadOnlyList<ParticleSystem> Templates => _templates;
    /// <summary>템플릿 복제본의 렌더러다. 머티리얼·최대 입자 크기·색 공간 설정을 읽는 데 쓰며 화면에는 그리지 않는다.</summary>
    public static ParticleSystemRenderer TemplateRenderer => _templateRenderer;
    public int TemplateIndex => _templateIndex;
    /// <summary>근접 전조의 현재 붉은 세기다. 기존 몸 연기 렌더러가 전조 마스크에 그려지던 조건과 같게 컴포넌트가 꺼지면 0이다.</summary>
    public float WarningIntensity => _warning.isActiveAndEnabled ? _warning.RedIntensity : 0f;

    /// <summary>
    /// 몸 연기 위치·회전과 자신의 localScale로 템플릿 로컬 입자를 월드로 옮기는 행렬을 반환한다.
    /// 몸 연기의 Scaling Mode가 Local이라 부모 스케일은 쓰지 않는다.
    /// </summary>
    public Matrix4x4 LocalToWorld
    {
        get
        {
            Transform smoke = _bodySmoke.transform;
            return Matrix4x4.TRS(smoke.position, smoke.rotation, smoke.localScale);
        }
    }

    void Awake()
    {
        _warning = GetComponent<EnemyMeleeWarningVisual>();
    }

    void OnEnable()
    {
        // 부모가 활성화되는 도중에는 자식 활성 상태를 바꾸지 않고 첫 Update에서 적용한다.
        _modeApplied = false;
    }

    void OnDisable()
    {
        if (_shared)
            _active.Remove(this);
        _shared = false;
    }

    void Update()
    {
        if (!_modeApplied || _useSharedTemplate != _shared)
            ApplyMode();
    }

    /// <summary>
    /// _useSharedTemplate에 맞춰 몸 연기 오브젝트를 끄고 공유 목록에 등록하거나, 다시 켜고 목록에서 뺀다.
    /// 공유를 처음 시작하면 템플릿을 준비하고 다섯 템플릿 중 하나를 차례로 배정한다. _shared와 _modeApplied를 갱신한다.
    /// </summary>
    private void ApplyMode()
    {
        _modeApplied = true;
        if (_useSharedTemplate)
        {
            EnsureTemplates();
            _bodySmoke.gameObject.SetActive(false);
            if (!_shared)
            {
                _templateIndex = _nextTemplate;
                _nextTemplate = (_nextTemplate + 1) % TEMPLATE_COUNT;
                _active.Add(this);
            }
        }
        else
        {
            if (_shared)
                _active.Remove(this);
            // 다시 켜지면 원래 Play On Awake·Prewarm으로 연기가 바로 차오른다.
            _bodySmoke.gameObject.SetActive(true);
        }
        _shared = _useSharedTemplate;
    }

    /// <summary>
    /// 템플릿이 없거나 씬과 함께 파괴됐으면 이 적의 몸 연기를 다섯 번 복제해 이 씬의 숨은 루트 아래에 만든다.
    /// 복제본은 원점에서 로컬 공간으로 계속 시뮬레이션하며, 렌더러는 꺼서 화면 밖 정지와 기존 렌더링 경로를 피한다.
    /// </summary>
    private void EnsureTemplates()
    {
        if (_templateRoot != null)
            return;
        _templates.Clear();
        _templateRoot = new GameObject("SharedBodySmokeTemplates");
        SceneManager.MoveGameObjectToScene(_templateRoot, gameObject.scene);
        for (int i = 0; i < TEMPLATE_COUNT; i++)
        {
            GameObject template = Instantiate(_bodySmoke.gameObject, _templateRoot.transform);
            template.name = "BodySmokeTemplate" + i;
            template.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            template.transform.localScale = _bodySmoke.transform.localScale;
            ParticleSystem system = template.GetComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ParticleSystemRenderer renderer = template.GetComponent<ParticleSystemRenderer>();
            renderer.enabled = false;
            template.SetActive(true);
            _templates.Add(system);
            _templateRenderer = renderer;
        }
    }

    /// <summary>
    /// 플레이 시작 시 입력 없이 이전 실행의 공유 목록과 템플릿 참조를 비운다.
    /// Domain Reload를 끈 경우에도 파괴된 템플릿을 다시 쓰지 않는다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        _active.Clear();
        _templates.Clear();
        _templateRoot = null;
        _templateRenderer = null;
        _nextTemplate = 0;
    }
}
