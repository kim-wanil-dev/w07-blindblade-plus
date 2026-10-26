using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>수리검 표시 스냅샷과 투척 전조를 HUD에 적용하고 실제 비행 속도와 플레이어 위치로 도달 예상 경고색을 정한다.</summary>
[DisallowMultipleComponent]
public sealed class ShurikenDirectionIndicator : MonoBehaviour
{
    private const float MOTION_EPSILON = 0.0001f;

    [Header("연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField, Tooltip("HUD 프리팹의 마커 목록. 배열 길이가 동시 표시 상한입니다.")]
    private DefenseDirectionMarker[] _graphics;
    private CombatThreatPresentationProvider _providerComponent;
    private IThreatPresentationProvider _provider;
    private PlayerMotor _motor;
    private CharacterController _playerBody;

    private readonly List<Marker> _markers = new List<Marker>();

    [Header("전조·비행 표현")]
    [SerializeField, Range(0f, 1f)] private float _telegraphAlpha = 0.55f;
    [SerializeField, Min(0.1f), Tooltip("전조 깜빡임 속도(초당 횟수)")]
    private float _pulseRate = 6f;
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.3f;
    [SerializeField, Min(0.1f), Tooltip("전조 방향 표시의 근거리 기준(미터)")]
    private float _nearDistance = 4f;
    [SerializeField, Min(0.1f), Tooltip("전조 방향 표시의 원거리 기준(미터)")]
    private float _farDistance = 30f;
    [SerializeField, Min(0.1f), Tooltip("가장 먼 거리에서의 아이콘 크기 배율")]
    private float _farScale = 0.8f;
    [SerializeField, Min(0.1f), Tooltip("가장 가까운 거리에서의 아이콘 크기 배율")]
    private float _nearScale = 3f;
    [SerializeField] private Color _telegraphColor = Color.white;

    [Header("비행 적중 예상 경고")]
    [SerializeField, Min(0f), Tooltip("플레이어 몸체에 도달하기까지 이 게임 시간(초) 이하로 남으면 노란색으로 표시한다.")]
    private float _warningLeadTime = 0.6f;
    [SerializeField, Min(0f), Tooltip("플레이어 몸체에 도달하기까지 이 게임 시간(초) 이하로 남으면 빨간색으로 표시한다.")]
    private float _dangerLeadTime = 0.2f;
    [SerializeField] private Color _flightReadyColor = Color.white;
    [SerializeField] private Color _flightWarningColor = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField] private Color _flightDangerColor = new Color(1f, 0.22f, 0.18f, 1f);

    private enum MarkerMode
    {
        Hidden,
        Telegraph,
        Flight,
        Fade
    }

    private sealed class Marker
    {
        public DefenseDirectionMarker Graphic { get; }
        public MarkerMode Mode { get; set; }
        public bool HasTarget { get; set; }
        public CombatSessionKey Session { get; set; }
        public CombatEntityKey Target { get; set; }
        public long Revision { get; set; }
        public Vector2 Direction { get; set; }
        public Vector3 Position { get; set; }
        public float Proximity { get; set; }
        public float TargetAlpha { get; set; }
        public float CurrentAlpha { get; set; }
        public Color Color { get; set; }
        public bool IsDanger { get; set; }
        public float StartedAt { get; set; }
        public float Duration { get; set; }
        public float FadeFromAlpha { get; set; }
        public bool HasMotionSample { get; set; }
        public Vector3 SamplePosition { get; set; }
        public float SampleTime { get; set; }
        public Vector3 FlightVelocity { get; set; }

        /// <summary>graphic을 숨김 상태로 재사용할 표시 슬롯에 저장한다.</summary>
        public Marker(DefenseDirectionMarker graphic)
        {
            Graphic = graphic;
            Mode = MarkerMode.Hidden;
        }
    }

    void Awake()
    {
        foreach (DefenseDirectionMarker graphic in _graphics)
        {
            Marker marker = new Marker(graphic);
            _markers.Add(marker);
            Hide(marker);
        }
    }

    void OnEnable()
    {
        _providerComponent = _director.GetComponent<CombatThreatPresentationProvider>();
        _provider = _providerComponent;
        if (_provider == null)
        {
            Debug.LogError("ShurikenDirectionIndicator에 CombatThreatPresentationProvider가 없습니다.", this);
            return;
        }

        _director.ThrowTelegraphed += HandleTelegraph;
        _provider.ThreatChanged += HandleThreatChanged;
        foreach (ThreatPresentationSnapshot snapshot in _provider.GetCurrentThreats())
            HandleThreatChanged(snapshot);
    }

    void OnDisable()
    {
        if (_director != null)
            _director.ThrowTelegraphed -= HandleTelegraph;
        if (_provider != null)
            _provider.ThreatChanged -= HandleThreatChanged;
        foreach (Marker marker in _markers)
            Hide(marker);
        _provider = null;
        _providerComponent = null;
    }

    void LateUpdate()
    {
        PlayerController player = Game.Player;
        if (player != null && (_motor == null || _motor.gameObject != player.gameObject))
        {
            _motor = player.GetComponent<PlayerMotor>();
            _playerBody = player.GetComponent<CharacterController>();
        }
        foreach (Marker marker in _markers)
        {
            if (marker.Mode != MarkerMode.Hidden)
                UpdateMarker(marker);
        }
    }

    /// <summary>position에서 시작하는 발사 전조를 duration 동안 빈 마커에 표시한다.</summary>
    private void HandleTelegraph(Vector3 position, float duration)
    {
        Marker marker = AcquireMarker();
        if (marker == null)
            return;
        marker.Mode = MarkerMode.Telegraph;
        marker.HasTarget = false;
        marker.Position = position;
        marker.Direction = Vector2.up;
        marker.Proximity = TelegraphProximity(position);
        marker.Color = _telegraphColor;
        marker.IsDanger = false;
        marker.TargetAlpha = _telegraphAlpha;
        marker.StartedAt = Time.time;
        marker.Duration = duration;
        marker.Graphic.color = _telegraphColor;
        marker.Graphic.SetShurikenIconColor(_telegraphColor);
        marker.Graphic.enabled = _providerComponent.ShowShurikenIndicators
            && BambooDefenseDirector.IsWithinSoundPerceptionRange(position);
        marker.Graphic.SetShurikenIconVisible(marker.Graphic.enabled);
    }

    /// <summary>snapshot의 세션·대상·revision과 청각 거리를 검사하고 위치로 비행 속도를 갱신해 방향·경고색·크기·투명도를 적용한다.</summary>
    private void HandleThreatChanged(ThreatPresentationSnapshot snapshot)
    {
        Marker marker = FindMarker(snapshot.Session, snapshot.Target);
        if (!BambooDefenseDirector.IsWithinSoundPerceptionRange(snapshot.WorldPosition))
        {
            if (marker != null)
                Hide(marker);
            return;
        }
        if (!snapshot.IsActive || !snapshot.IsEligible || !snapshot.UiVisible)
        {
            if (marker != null)
            {
                if (!snapshot.IsActive || !snapshot.IsEligible || snapshot.WorldVisible)
                    Hide(marker);
                else
                    BeginFade(marker, snapshot);
            }
            return;
        }

        if (marker == null)
            marker = AcquireMarker();
        if (marker == null || (marker.HasTarget && marker.Session.SessionId == snapshot.Session.SessionId
            && marker.Session.Generation == snapshot.Session.Generation && snapshot.Revision <= marker.Revision))
            return;

        UpdateFlightMotion(marker, snapshot.WorldPosition);
        marker.Mode = MarkerMode.Flight;
        marker.HasTarget = true;
        marker.Session = snapshot.Session;
        marker.Target = snapshot.Target;
        marker.Revision = snapshot.Revision;
        marker.Direction = snapshot.UiDirection;
        marker.Position = snapshot.WorldPosition;
        marker.Proximity = snapshot.Proximity;
        marker.Color = GetFlightColor(marker, out bool isDanger);
        marker.IsDanger = isDanger;
        marker.TargetAlpha = snapshot.Alpha;
        marker.CurrentAlpha = snapshot.Alpha;
        marker.Graphic.color = marker.Color;
        marker.Graphic.SetShurikenIconColor(marker.Color);
        marker.Graphic.enabled = _providerComponent.ShowShurikenIndicators && !IsMovingAway(marker);
        marker.Graphic.SetShurikenIconVisible(marker.Graphic.enabled);
    }

    /// <summary>해당 위협의 마지막 위치와 투명도를 저장하고 제거 또는 비표시 상태를 페이드한다.</summary>
    private void BeginFade(Marker marker, ThreatPresentationSnapshot snapshot)
    {
        if (marker.HasTarget && marker.Session.SessionId == snapshot.Session.SessionId
            && marker.Session.Generation == snapshot.Session.Generation && snapshot.Revision <= marker.Revision)
            return;
        bool alreadyFading = marker.Mode == MarkerMode.Fade && marker.HasTarget
            && marker.Session.SessionId == snapshot.Session.SessionId
            && marker.Session.Generation == snapshot.Session.Generation
            && marker.Target.Equals(snapshot.Target);
        marker.HasTarget = true;
        marker.Session = snapshot.Session;
        marker.Target = snapshot.Target;
        marker.Revision = snapshot.Revision;
        marker.Direction = snapshot.UiDirection;
        marker.Position = snapshot.WorldPosition;
        marker.Proximity = snapshot.Proximity;
        if (!alreadyFading)
        {
            marker.FadeFromAlpha = marker.CurrentAlpha;
            marker.Mode = MarkerMode.Fade;
            marker.StartedAt = Time.time;
        }
    }

    /// <summary>
    /// marker의 방향을 UI 회전으로 바꾸고 전조 깜빡임·C 투명도·페이드 수명을 갱신한다.
    /// 비행 마커는 현재 플레이어 위치와 추정 속도로 경고색을 갱신하고, 종료 페이드는 마지막 경고색을 유지한다.
    /// 일반 스테이지 인지 범위 밖에서는 표시를 숨기고 전조 수명은 계속 진행한다.
    /// </summary>
    private void UpdateMarker(Marker marker)
    {
        float elapsed = Time.time - marker.StartedAt;
        float alpha;
        bool isDanger;
        switch (marker.Mode)
        {
            case MarkerMode.Telegraph:
                if (elapsed >= marker.Duration)
                {
                    Hide(marker);
                    return;
                }
                marker.Direction = TelegraphDirection(marker.Position);
                marker.Proximity = TelegraphProximity(marker.Position);
                alpha = _telegraphAlpha * (0.6f + 0.4f
                    * (0.5f + 0.5f * Mathf.Sin(elapsed * _pulseRate * Mathf.PI * 2f)));
                break;
            case MarkerMode.Fade:
                if (elapsed >= _fadeDuration)
                {
                    Hide(marker);
                    return;
                }
                alpha = marker.FadeFromAlpha * (1f - elapsed / _fadeDuration);
                break;
            case MarkerMode.Flight:
                marker.Color = GetFlightColor(marker, out isDanger);
                marker.IsDanger = isDanger;
                alpha = marker.TargetAlpha;
                break;
            default:
                return;
        }

        marker.Graphic.enabled = _providerComponent.ShowShurikenIndicators
            && BambooDefenseDirector.IsWithinSoundPerceptionRange(marker.Position)
            && (marker.Mode != MarkerMode.Flight || !IsMovingAway(marker));
        marker.Graphic.SetShurikenIconVisible(marker.Graphic.enabled);
        if (!marker.Graphic.enabled)
        {
            marker.Graphic.SetWarningBadge(false, marker.Color, 0f);
            marker.CurrentAlpha = 0f;
            return;
        }
        float angle = Mathf.Atan2(marker.Direction.x, marker.Direction.y) * Mathf.Rad2Deg;
        marker.Graphic.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
        float closeness = marker.Proximity;
        marker.Graphic.SetIconScale(Mathf.Lerp(_farScale, _nearScale, closeness * closeness));
        marker.Graphic.color = marker.Color;
        marker.Graphic.SetShurikenIconColor(marker.Color);
        marker.Graphic.canvasRenderer.SetAlpha(alpha);
        marker.Graphic.SetShurikenIconAlpha(alpha);
        marker.Graphic.SetWarningBadge(marker.IsDanger, marker.Color, alpha);
        marker.CurrentAlpha = alpha;
    }

    /// <summary>
    /// position과 이전 스냅샷의 위치·게임 시각으로 marker의 실제 비행 속도를 갱신한다.
    /// 첫 표본에서는 기준 위치만 저장하고 같은 게임 시각의 중복 갱신이나 일시정지는 속도 계산에서 제외한다.
    /// </summary>
    private void UpdateFlightMotion(Marker marker, Vector3 position)
    {
        float now = Time.time;
        if (!marker.HasMotionSample)
        {
            marker.HasMotionSample = true;
            marker.SamplePosition = position;
            marker.SampleTime = now;
            marker.FlightVelocity = Vector3.zero;
            return;
        }
        float deltaTime = now - marker.SampleTime;
        if (deltaTime <= MOTION_EPSILON) return;
        marker.FlightVelocity = (position - marker.SamplePosition) / deltaTime;
        marker.SamplePosition = position;
        marker.SampleTime = now;
    }

    /// <summary>
    /// marker의 비행 속도와 현재 플레이어 중심 방향으로 지나쳐 멀어지는지 반환한다.
    /// 속도 표본이 없거나 플레이어 참조가 없으면 false이며 추적 상태는 유지한다.
    /// </summary>
    private bool IsMovingAway(Marker marker)
    {
        if (_motor == null || marker.FlightVelocity.sqrMagnitude <= MOTION_EPSILON * MOTION_EPSILON)
            return false;
        Vector3 playerPosition = _playerBody != null ? _playerBody.bounds.center : _motor.ViewOrigin;
        return Vector3.Dot(marker.FlightVelocity, playerPosition - marker.Position) <= 0f;
    }

    /// <summary>
    /// marker 위치에서 현재 플레이어 몸체까지의 거리와 접근 속도로 도달 예상 게임 시간을 반환한다.
    /// 몸체 경계 안이면 0, 속도 표본이 없거나 플레이어에게 접근하지 않으면 양의 무한대를 반환한다.
    /// </summary>
    private float EstimateImpactTime(Marker marker)
    {
        if (_motor == null) return float.PositiveInfinity;
        Vector3 target = _playerBody != null
            ? _playerBody.bounds.ClosestPoint(marker.Position) : _motor.ViewOrigin;
        Vector3 toPlayer = target - marker.Position;
        float distance = toPlayer.magnitude;
        if (distance <= MOTION_EPSILON) return 0f;
        float approachSpeed = Vector3.Dot(marker.FlightVelocity, toPlayer / distance);
        return approachSpeed > MOTION_EPSILON ? distance / approachSpeed : float.PositiveInfinity;
    }

    /// <summary>
    /// marker의 도달 예상 시간을 경고·위험 기준과 비교해 흰색·노란색·빨간색을 반환한다.
    /// 위험 구간 여부는 isDanger에 저장해 빨간 표시와 위험 배지를 같은 기준으로 갱신한다.
    /// </summary>
    private Color GetFlightColor(Marker marker, out bool isDanger)
    {
        float remaining = EstimateImpactTime(marker);
        float warning = Mathf.Max(0f, _warningLeadTime);
        float danger = Mathf.Clamp(_dangerLeadTime, 0f, warning);
        isDanger = remaining <= danger;
        if (isDanger) return _flightDangerColor;
        return remaining <= warning ? _flightWarningColor : _flightReadyColor;
    }

    /// <summary>position에서 플레이어 시점까지의 거리를 전조 마커 크기에 사용할 0~1 근접도로 반환한다.</summary>
    private float TelegraphProximity(Vector3 position)
    {
        if (_motor == null)
            return 0f;
        return Mathf.InverseLerp(_farDistance, _nearDistance, Vector3.Distance(position, _motor.ViewOrigin));
    }

    /// <summary>position과 플레이어 시점 전방의 수평 각도를 위쪽 기준 UI 방향으로 반환한다.</summary>
    private Vector2 TelegraphDirection(Vector3 position)
    {
        if (_motor == null)
            return Vector2.up;
        Vector3 offset = Vector3.ProjectOnPlane(position - _motor.ViewOrigin, Vector3.up);
        float angle = offset.sqrMagnitude > 0.0001f
            ? Vector3.SignedAngle(_motor.ViewForward, offset, Vector3.up)
            : 0f;
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
    }

    /// <summary>session과 target이 모두 일치하는 표시 마커를 반환하고 없으면 null을 반환한다.</summary>
    private Marker FindMarker(CombatSessionKey session, CombatEntityKey target)
    {
        foreach (Marker marker in _markers)
        {
            if (marker.HasTarget && marker.Session.SessionId == session.SessionId
                && marker.Session.Generation == session.Generation && marker.Target.Equals(target))
                return marker;
        }
        return null;
    }

    /// <summary>숨김 상태의 마커 슬롯을 반환한다. 모두 사용 중이면 null을 반환한다.</summary>
    private Marker AcquireMarker()
    {
        foreach (Marker marker in _markers)
        {
            if (marker.Mode == MarkerMode.Hidden)
                return marker;
        }
        return null;
    }

    /// <summary>marker를 숨기고 세션·대상 추적 상태와 알파를 초기화한다.</summary>
    private void Hide(Marker marker)
    {
        marker.Mode = MarkerMode.Hidden;
        marker.HasTarget = false;
        marker.HasMotionSample = false;
        marker.FlightVelocity = Vector3.zero;
        marker.CurrentAlpha = 0f;
        marker.IsDanger = false;
        marker.Graphic.canvasRenderer.SetAlpha(0f);
        marker.Graphic.enabled = false;
        marker.Graphic.SetShurikenIconVisible(false);
        marker.Graphic.SetShurikenIconAlpha(0f);
        marker.Graphic.SetWarningBadge(false, Color.clear, 0f);
    }
}
