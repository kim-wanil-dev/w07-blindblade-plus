using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 쌍둥이 보스(인간·짐승) 공격이 플레이어 체력을 실제로 줄일 때마다 플레이어 칼집 위 실 교차점에서 짧은 붉은 실 한 가닥을 뽑아
/// 인간 허리춤의 다음 빈 구슬로 휘어 들여보내고, 실 머리가 구슬에 닿는 순간 HumanBossAttack 기운을 하나 채운다.
/// 실은 발도 대상 실(PlayerDashTargetThread)과 같은 DashThread 재질의 유선형 줄기와 잔가지로 그리며 정점 색으로 붉게 물들인다.
/// 플레이어와 인간을 잇지 않고 실 조각만 날아가며, 거합 피해는 기운을 채우지 않는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HumanBossAttack), typeof(TwinHumanBeads))]
public sealed class TwinHumanChargeStreak : MonoBehaviour
{
    private const int MAX_STREAKS = 8;
    private const string IAI_PATTERN_ID = "TwinHumanIai";
    private const float STREAMLINE_PEAK = 0.357f;
    private const float BRANCH_START = 0.15f;
    private const float BRANCH_END = 0.85f;

    [Header("연결")]
    [SerializeField, Tooltip("함께 싸우는 짐승 보스. 짐승 공격의 피해도 인간 기운으로 모은다.")]
    private BeastBossAttack _beast;
    [SerializeField, Tooltip("BlindBlade/DashThread 셰이더 재질. 발도 대상 실과 같은 재질을 쓴다.")]
    private Material _material;
    private HumanBossAttack _attack;
    private TwinHumanBeads _beads;
    private KatanaDamageReceiver _player;
    private PlayerDashTargetThread _playerThread;

    [Header("모양")]
    [SerializeField, Range(8, 64), Tooltip("실 한 가닥을 나누는 점 개수")]
    private int _segments = 24;
    [SerializeField, Min(0.05f), Tooltip("날아가는 실 한 가닥의 길이(m)")]
    private float _length = 2.2f;
    [SerializeField, Min(0.0005f), Tooltip("줄기 가장 굵은 곳 두께(화면 높이 비율). 발도 대상 실과 같다.")]
    private float _mainWidth = 0.0192f;
    [SerializeField, Range(0.05f, 1f), Tooltip("줄기 대비 잔가지 두께")]
    private float _branchWidthRatio = 0.2f;
    [SerializeField, Min(0f), Tooltip("잔가지가 줄기에서 벗어나는 최대 거리(줄기 두께 대비)")]
    private float _branchSpread = 1.2f;
    [SerializeField, Min(0f), Tooltip("잔가지가 벗어났다 돌아오는 빠르기(라디안/초)")]
    private float _branchWaveSpeed = 14f;
    [SerializeField, Tooltip("실 색. 재질의 흰 실을 정점 색으로 물들이며 허리춤 구슬 실 구체와 같은 붉은색이다.")]
    private Color _color = new Color(1f, 0.1f, 0.06f, 1f);
    [SerializeField, Min(0f), Tooltip("머리 쪽이 밝아지는 정도")]
    private float _headGlow = 1.2f;

    [Header("비행")]
    [SerializeField, Min(0.1f), Tooltip("실이 날아가는 평균 속도(미터/초). 거리를 이 속도로 나눈 시간을 최소·최대 시간 사이로 자른다.")]
    private float _speed = 16f;
    [SerializeField, Min(0.05f), Tooltip("가까워도 실이 꼬리까지 구슬에 들어가는 데 걸리는 최소 시간(초)")]
    private float _minimumTravelTime = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("멀어도 실이 꼬리까지 구슬에 다 들어가는 최대 시간(초). 멀면 이 시간에 맞춰 더 빨리 날아간다.")]
    private float _maximumTravelTime = 1.2f;
    [SerializeField, Min(0f), Tooltip("칼에서 천천히 뽑혀 나와 구슬 쪽으로 기하급수적으로 빨라지는 정도. 0이면 일정한 속도다.")]
    private float _acceleration = 3f;
    [SerializeField, Min(0f), Tooltip("경로가 옆·위로 휘는 최대 정도(직선 거리 비율)")]
    private float _curve = 0.2f;

    [Header("상태")]
    private GameObject _meshObject;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<Vector4> _uv0 = new List<Vector4>();
    private readonly List<Vector4> _uv1 = new List<Vector4>();
    private readonly List<int> _indices = new List<int>();
    private Vector3[] _mainPoints;
    private Vector3[] _branchPoints;
    private float[] _mainWidths;
    private float[] _branchWidths;
    private readonly bool[] _active = new bool[MAX_STREAKS];
    private readonly bool[] _arrived = new bool[MAX_STREAKS];
    private readonly float[] _startTimes = new float[MAX_STREAKS];
    private readonly float[] _durations = new float[MAX_STREAKS];
    private readonly float[] _seeds = new float[MAX_STREAKS];
    private readonly Vector3[] _origins = new Vector3[MAX_STREAKS];
    private readonly Vector2[] _bends = new Vector2[MAX_STREAKS];
    private readonly int[] _targetBeads = new int[MAX_STREAKS];
    private int _lastCharges;

    void Awake()
    {
        _attack = GetComponent<HumanBossAttack>();
        _beads = GetComponent<TwinHumanBeads>();
        int count = _segments + 1;
        _mainPoints = new Vector3[count];
        _branchPoints = new Vector3[count];
        _mainWidths = new float[count];
        _branchWidths = new float[count];
        CreateMeshObject();
    }

    void OnDisable()
    {
        UnbindPlayer();
        ClearStreaks();
        // 씬 종료 중에는 실 오브젝트가 먼저 파괴될 수 있다.
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }

    void OnDestroy()
    {
        if (_meshObject != null) Destroy(_meshObject);
        if (_mesh != null) Destroy(_mesh);
    }

    void LateUpdate()
    {
        // 보스가 켜질 때 플레이어가 아직 등록되지 않았을 수 있고, 재도전으로 플레이어가 바뀔 수 있다.
        if (_player == null) BindPlayer();

        // 거합으로 기운을 비운 뒤 날아가던 실이 기운을 다시 채우지 않게 지운다.
        if (_attack.Charges < _lastCharges || !_attack.Health.IsAlive) ClearStreaks();
        float time = Time.time;
        UpdateStreaks(time);
        _lastCharges = _attack.Charges;

        Camera view = Camera.main;
        bool visible = view != null && HasStreaks();
        _meshRenderer.enabled = visible;
        if (visible) BuildMesh(view, time);
    }

    /// <summary>
    /// 월드 좌표 실 띠를 그릴 동적 메시와 MeshRenderer를 보스와 따로 만든다. 그림자·광원 탐침을 끄고 처음에는 숨긴다.
    /// 만든 오브젝트를 _meshObject, _meshRenderer, _mesh에 저장한다.
    /// </summary>
    private void CreateMeshObject()
    {
        _meshObject = new GameObject("TwinHumanChargeStreak");
        _mesh = new Mesh { name = "TwinHumanChargeStreak" };
        _mesh.MarkDynamic();
        _meshObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _meshRenderer = _meshObject.AddComponent<MeshRenderer>();
        _meshRenderer.sharedMaterial = _material;
        _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        _meshRenderer.allowOcclusionWhenDynamic = false;
        _meshRenderer.enabled = false;
    }

    /// <summary>
    /// 현재 Game.Player의 피해 수신자와 발도 대상 실을 찾아 _player, _playerThread에 저장하고 실제 피해 사건을 구독한다.
    /// 플레이어가 없으면 아무것도 바꾸지 않아 다음 프레임에 다시 찾는다.
    /// </summary>
    private void BindPlayer()
    {
        PlayerController player = Game.Player;
        if (player == null) return;
        _player = player.GetComponent<KatanaDamageReceiver>();
        _playerThread = player.GetComponent<PlayerDashTargetThread>();
        if (_player != null) _player.DamageReceived += HandlePlayerDamaged;
    }

    /// <summary>구독한 플레이어 피해 사건을 해제하고 _player, _playerThread를 비운다.</summary>
    private void UnbindPlayer()
    {
        if (_player != null) _player.DamageReceived -= HandlePlayerDamaged;
        _player = null;
        _playerThread = null;
    }

    /// <summary>
    /// 플레이어가 실제로 체력을 잃은 피해 context가 살아 있는 인간·짐승의 거합 외 공격이면 다음 빈 구슬로 붉은 실을 쏜다.
    /// 막기·쳐내기·회피·무적은 이 사건이 오지 않으므로 기운이 차지 않는다.
    /// </summary>
    private void HandlePlayerDamaged(DamageContext context)
    {
        GameObject instigator = context.Instigator;
        if (!_attack.Health.IsAlive || context.PatternId == IAI_PATTERN_ID
            || (instigator != gameObject && instigator != _beast.gameObject)) return;
        Launch();
    }

    /// <summary>
    /// 빈 슬롯에 실 하나를 지금 시각으로 등록한다. 시작점은 칼집 교차점이고, 목표는 이미 찬 기운과 날아가는 실 수 다음의 구슬이다.
    /// 처음 직선 거리를 _speed로 나눈 시간을 _minimumTravelTime~_maximumTravelTime으로 잘라 꼬리까지 들어가는 전체 시간으로 정하고,
    /// 무작위로 옆·위로 휘는 정도를 정해 슬롯 배열에 저장한다.
    /// 빈 슬롯이 없거나 찬 기운과 날아가는 실 수가 _attack.MaxCharges에 이르면 쏘지 않는다.
    /// </summary>
    private void Launch()
    {
        int slot = -1;
        int pending = 0;
        for (int i = 0; i < MAX_STREAKS; i++)
        {
            if (!_active[i])
            {
                if (slot < 0) slot = i;
            }
            else if (!_arrived[i])
            {
                pending++;
            }
        }
        // 이미 찬 기운과 날아가는 실로 구슬이 모두 차면 더 쏘지 않는다.
        int target = _attack.Charges + pending;
        if (slot < 0 || target >= _attack.MaxCharges) return;

        Vector3 origin = GetLaunchOrigin();
        float distance = Vector3.Distance(origin, _beads.GetBeadPosition(target));
        _active[slot] = true;
        _arrived[slot] = false;
        _startTimes[slot] = Time.time;
        _durations[slot] = Mathf.Clamp(distance / _speed, _minimumTravelTime, _maximumTravelTime);
        _seeds[slot] = Random.value * 10f;
        _origins[slot] = origin;
        _bends[slot] = new Vector2(Random.Range(-1f, 1f), Random.Range(0.3f, 1f));
        _targetBeads[slot] = target;
    }

    /// <summary>
    /// 실이 시작할 월드 위치를 반환한다. 발도 대상 실이 보이면 칼집 위 X자 교차점을,
    /// 숨겨져 있으면(막기·사망 등) 플레이어 가슴 높이를 쓴다.
    /// </summary>
    private Vector3 GetLaunchOrigin()
    {
        if (_playerThread != null && _playerThread.TryGetSwordJunction(out Vector3 junction)) return junction;
        return _player.transform.position + Vector3.up;
    }

    /// <summary>
    /// time 기준으로 날아가는 실마다 GetTravel 진행도를 계산해 머리가 목표 구슬에 처음 닿으면 HumanBossAttack.AddCharge로 기운을 채운다.
    /// 전체 시간 _durations가 지나 꼬리까지 구슬에 들어가면 슬롯을 비운다. _arrived와 _active를 변경한다.
    /// </summary>
    private void UpdateStreaks(float time)
    {
        for (int i = 0; i < MAX_STREAKS; i++)
        {
            if (!_active[i]) continue;
            float distance = Vector3.Distance(_origins[i], _beads.GetBeadPosition(_targetBeads[i]));
            if (!_arrived[i] && GetTravel(i, time, StreakSpan(distance)) >= 1f)
            {
                _arrived[i] = true;
                _attack.AddCharge();
            }
            if (time - _startTimes[i] >= _durations[i]) _active[i] = false;
        }
    }

    /// <summary>
    /// streak 실이 time까지 경로를 따라 나아간 정도(0~1+span)를 반환한다. span은 경로 대비 실 길이 비율이며 1을 넘으면 머리는 구슬에 있고 꼬리가 들어가는 중이다.
    /// 전체 시간 _durations 동안 _acceleration만큼 기하급수적으로 빨라지는 곡선을 따라 1+span까지 가므로 꼬리까지 그 시간 안에 들어간다.
    /// </summary>
    private float GetTravel(int streak, float time, float span)
    {
        float x = Mathf.Clamp01((time - _startTimes[streak]) / _durations[streak]);
        // 칼에서 천천히 뽑혀 나와 구슬에 가까울수록 지수적으로 빨라진다.
        float eased = _acceleration > 0.001f ? (Mathf.Exp(_acceleration * x) - 1f) / (Mathf.Exp(_acceleration) - 1f) : x;
        return eased * (1f + span);
    }

    /// <summary>날아가는 실이 하나라도 있으면 true를 반환한다.</summary>
    private bool HasStreaks()
    {
        for (int i = 0; i < MAX_STREAKS; i++)
            if (_active[i]) return true;
        return false;
    }

    /// <summary>날아가는 실을 모두 지운다. 지운 실은 기운을 채우지 않는다.</summary>
    private void ClearStreaks()
    {
        for (int i = 0; i < MAX_STREAKS; i++)
            _active[i] = false;
    }

    /// <summary>distance 길이 경로 대비 실 한 가닥 길이 _length의 비율(0~1)을 반환한다.</summary>
    private float StreakSpan(float distance)
    {
        return Mathf.Min(1f, _length / Mathf.Max(0.01f, distance));
    }

    /// <summary>
    /// origin에서 target으로 가는 경로의 2차 베지어 조절점을 반환한다. 가운데에서 bend.x만큼 옆, bend.y만큼 위로
    /// 직선 거리 × _curve 비율만큼 벗어난다. target이 움직이면 매 프레임 다시 구해 실이 구슬을 따라 휘어 들어간다.
    /// </summary>
    private Vector3 GetControlPoint(Vector3 origin, Vector3 target, Vector2 bend)
    {
        Vector3 direction = target - origin;
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right;
        return (origin + target) * 0.5f + (side * bend.x + Vector3.up * bend.y) * (direction.magnitude * _curve);
    }

    /// <summary>origin, control, target 2차 베지어의 u(0~1) 위치를 반환한다.</summary>
    private static Vector3 GetPathPoint(Vector3 origin, Vector3 control, Vector3 target, float u)
    {
        float inverse = 1f - u;
        return origin * (inverse * inverse) + control * (2f * inverse * u) + target * (u * u);
    }

    /// <summary>origin, control, target 2차 베지어의 u(0~1) 위치 접선 벡터를 반환한다.</summary>
    private static Vector3 GetPathTangent(Vector3 origin, Vector3 control, Vector3 target, float u)
    {
        return (control - origin) * (2f * (1f - u)) + (target - control) * (2f * u);
    }

    /// <summary>
    /// 머리(fromHead=0)에서 꼬리(fromHead=1)까지의 유선형 두께 비율(0~1)을 반환한다. 발도 대상 실 줄기와 같은 모양이다.
    /// </summary>
    private static float Streamline(float fromHead)
    {
        float x = Mathf.Clamp01(fromHead);
        return Mathf.Clamp01(Mathf.Sqrt(x) * Mathf.Pow(1f - x, 1.2f) / STREAMLINE_PEAK);
    }

    /// <summary>잔가지의 v(0 나오는 곳~1 들어가는 곳) 위치 두께 비율을 반환한다. 양 끝은 줄기 속으로 가늘게 사라진다.</summary>
    private static float BranchProfile(float v)
    {
        // sin(π)는 부동소수 오차로 아주 작은 음수가 되어 거듭제곱이 NaN이 되므로 0 아래를 자른다.
        return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v))), 0.6f);
    }

    /// <summary>
    /// point의 카메라 앞쪽 깊이와 screenScale(2·tan(시야각/2))로 화면 높이 1에 해당하는 월드 길이를 반환한다.
    /// </summary>
    private static float ScreenToWorld(Camera view, Vector3 point, Vector3 cameraPosition, Vector3 cameraForward,
        float screenScale)
    {
        float depth = Mathf.Max(view.nearClipPlane, Vector3.Dot(point - cameraPosition, cameraForward));
        return depth * screenScale;
    }

    /// <summary>
    /// view 카메라와 time 시각으로 날아가는 실마다 꼬리~머리 구간의 줄기와 잔가지 점·반폭을 계산해 띠 메시로 _mesh에 기록한다.
    /// 머리는 기하급수적으로 빨라지는 진행도(GetTravel)만큼 경로를 따라가다 구슬에서 멈추고 꼬리가 뒤따라 들어가며, 잔가지는 줄기 옆에서 흔들렸다 돌아온다.
    /// </summary>
    private void BuildMesh(Camera view, float time)
    {
        Transform cameraTransform = view.transform;
        Vector3 cameraPosition = cameraTransform.position;
        Vector3 cameraForward = cameraTransform.forward;
        float screenScale = 2f * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _vertices.Clear();
        _colors.Clear();
        _uv0.Clear();
        _uv1.Clear();
        _indices.Clear();

        for (int streak = 0; streak < MAX_STREAKS; streak++)
        {
            if (!_active[streak]) continue;
            Vector3 origin = _origins[streak];
            Vector3 target = _beads.GetBeadPosition(_targetBeads[streak]);
            Vector3 control = GetControlPoint(origin, target, _bends[streak]);
            float span = StreakSpan(Vector3.Distance(origin, target));
            float travel = GetTravel(streak, time, span);
            float head = Mathf.Min(1f, travel);
            float tail = Mathf.Clamp01(travel - span);
            float wave = time * _branchWaveSpeed + _seeds[streak];
            for (int i = 0; i <= _segments; i++)
            {
                // s는 꼬리 0에서 머리 1까지다. 처음에는 칼에서 자라나고 끝에는 구슬 속으로 줄어든다.
                float s = i / (float)_segments;
                Vector3 main = GetPathPoint(origin, control, target, Mathf.Lerp(tail, head, s));
                _mainPoints[i] = main;
                _mainWidths[i] = _mainWidth * 0.5f * Streamline(1f - s)
                    * ScreenToWorld(view, main, cameraPosition, cameraForward, screenScale);

                float branchU = Mathf.Lerp(tail, head, Mathf.Lerp(BRANCH_START, BRANCH_END, s));
                Vector3 branchBase = GetPathPoint(origin, control, target, branchU);
                Vector3 side = Vector3.Cross(GetPathTangent(origin, control, target, branchU), cameraPosition - branchBase);
                side = side.sqrMagnitude > 0.0000001f ? side.normalized : Vector3.zero;
                float branchScale = ScreenToWorld(view, branchBase, cameraPosition, cameraForward, screenScale);
                float offset = Mathf.Sin(Mathf.PI * s) * Mathf.Sin(wave + s * 4f) * _branchSpread * _mainWidth * branchScale;
                _branchPoints[i] = branchBase + side * offset;
                _branchWidths[i] = _mainWidth * _branchWidthRatio * 0.5f * BranchProfile(s) * branchScale;
            }

            AppendRibbon(_mainPoints, _mainWidths, false, cameraPosition);
            AppendRibbon(_branchPoints, _branchWidths, true, cameraPosition);
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetColors(_colors);
        _mesh.SetUVs(0, _uv0);
        _mesh.SetUVs(1, _uv1);
        _mesh.SetTriangles(_indices, 0);
        _mesh.RecalculateBounds();
    }

    /// <summary>
    /// points를 카메라를 향한 띠로 펼쳐 메시 버퍼에 추가한다. halfWidths는 점마다의 반폭, branch는 잔가지 여부다.
    /// 정점 색은 _color, 밝기는 머리로 갈수록 _headGlow까지 커지며 앞면(front 1)으로 그려 재질의 뒷면 옅음을 쓰지 않는다.
    /// </summary>
    private void AppendRibbon(Vector3[] points, float[] halfWidths, bool branch, Vector3 cameraPosition)
    {
        int count = _segments + 1;
        int baseIndex = _vertices.Count;
        Vector4 kind = new Vector4(branch ? 1f : 0f, 0f, 0f, 0f);
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
            Vector3 side = Vector3.Cross(tangent, cameraPosition - points[i]);
            side = side.sqrMagnitude > 0.0000001f ? side.normalized * halfWidths[i] : Vector3.zero;
            float s = i / (float)_segments;
            float glow = _headGlow * s * s;
            _vertices.Add(points[i] - side);
            _vertices.Add(points[i] + side);
            _colors.Add(_color);
            _colors.Add(_color);
            _uv0.Add(new Vector4(s, 0f, glow, 1f));
            _uv0.Add(new Vector4(s, 1f, glow, 1f));
            _uv1.Add(kind);
            _uv1.Add(kind);
            if (i == 0) continue;

            int index = baseIndex + (i - 1) * 2;
            _indices.Add(index);
            _indices.Add(index + 1);
            _indices.Add(index + 2);
            _indices.Add(index + 1);
            _indices.Add(index + 3);
            _indices.Add(index + 2);
        }
    }
}
