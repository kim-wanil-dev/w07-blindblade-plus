using System.Collections.Generic;

using UnityEngine;

using BlindBlade.Combat.Contracts;

/// <summary>C가 확정한 수리검 표시 스냅샷과 투척 전조를 HUD 마커에 적용한다.</summary>
[DisallowMultipleComponent]
public sealed class ShurikenDirectionIndicator : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private BambooDefenseDirector _director;
    [SerializeField, Tooltip("HUD 프리팹의 마커 목록. 배열 길이가 동시 표시 상한입니다.")]
    private DefenseDirectionMarker[] _graphics;
    private CombatThreatPresentationProvider _providerComponent;
    private IThreatPresentationProvider _provider;
    private PlayerMotor _motor;

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
        public float StartedAt { get; set; }
        public float Duration { get; set; }
        public float FadeFromAlpha { get; set; }

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
            _motor = player.GetComponent<PlayerMotor>();
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
        marker.TargetAlpha = _telegraphAlpha;
        marker.StartedAt = Time.time;
        marker.Duration = duration;
        marker.Graphic.color = _telegraphColor;
        marker.Graphic.enabled = true;
    }

    /// <summary>snapshot의 세션·대상·revision을 확인해 C가 허용한 방향·색·크기·투명도를 적용한다.</summary>
    private void HandleThreatChanged(ThreatPresentationSnapshot snapshot)
    {
        Marker marker = FindMarker(snapshot.Session, snapshot.Target);
        if (!snapshot.IsActive || !snapshot.IsEligible || !snapshot.UiVisible)
        {
            if (marker != null)
            {
                if (snapshot.WorldVisible)
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

        marker.Mode = MarkerMode.Flight;
        marker.HasTarget = true;
        marker.Session = snapshot.Session;
        marker.Target = snapshot.Target;
        marker.Revision = snapshot.Revision;
        marker.Direction = snapshot.UiDirection;
        marker.Position = snapshot.WorldPosition;
        marker.Proximity = snapshot.Proximity;
        marker.Color = snapshot.Color;
        marker.TargetAlpha = snapshot.Alpha;
        marker.CurrentAlpha = snapshot.Alpha;
        marker.Graphic.color = snapshot.Color;
        marker.Graphic.enabled = true;
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
        marker.Color = snapshot.Color;
        if (!alreadyFading)
        {
            marker.FadeFromAlpha = marker.CurrentAlpha;
            marker.Mode = MarkerMode.Fade;
            marker.StartedAt = Time.time;
        }
    }

    /// <summary>
    /// marker의 방향을 UI 회전으로 바꾸고 전조 깜빡임·C 투명도·페이드 수명을 갱신한다.
    /// 비행 마커의 근접도와 색은 스냅샷 값을 사용하며 전조만 플레이어 시점으로 계산한다.
    /// </summary>
    private void UpdateMarker(Marker marker)
    {
        float elapsed = Time.time - marker.StartedAt;
        float alpha;
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
            default:
                alpha = marker.TargetAlpha;
                break;
        }

        float angle = Mathf.Atan2(marker.Direction.x, marker.Direction.y) * Mathf.Rad2Deg;
        marker.Graphic.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
        float closeness = marker.Proximity;
        marker.Graphic.SetIconScale(Mathf.Lerp(_farScale, _nearScale, closeness * closeness));
        marker.Graphic.color = marker.Color;
        marker.Graphic.canvasRenderer.SetAlpha(alpha);
        marker.CurrentAlpha = alpha;
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
        marker.CurrentAlpha = 0f;
        marker.Graphic.canvasRenderer.SetAlpha(0f);
        marker.Graphic.enabled = false;
    }
}
