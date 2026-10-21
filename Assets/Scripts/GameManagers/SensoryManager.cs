using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 사건 정보를 받아 관측 거리에 맞는 독립 시각 효과를 생성한다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SensoryManager : MonoBehaviour
{
    [Header("세션 연결")]
    [SerializeField] private Transform _observer;

    [Header("관측 거리")]
    [SerializeField, Min(0f), Tooltip("효과를 완전히 표시하는 거리(미터)")]
    private float _fullVisibilityDistance = 5f;
    [SerializeField, Min(0.01f), Tooltip("효과를 숨기는 거리(미터), 완전 표시 거리보다 커야 함")]
    private float _maximumDistance = 15f;

    [Header("독립 효과")]
    [SerializeField] private FootstepObject _footstepPrefab;
    [SerializeField] private FootstepWave _wavePrefab;
    private readonly List<ActiveEffect> _activeEffects = new List<ActiveEffect>(64);
    private bool _initialized;
    private bool _reportedUnavailable;
    private bool _effectsVisible = true;
    private bool _footstepsSuppressed;
    public bool IsReady => _initialized && _observer != null && isActiveAndEnabled;
    public Transform Observer => _observer;
    public int ActiveEffectCount => _activeEffects.Count;
    public float MaximumDistance => _maximumDistance;
    public float CrouchPulseInterval
    {
        get
        {
            SensoryPerceptionMask perceptionMask = GetComponent<SensoryPerceptionMask>();
            float perceptionDuration = perceptionMask != null ? perceptionMask.SoundLifetime : 0f;
            float waveDuration = _wavePrefab != null ? _wavePrefab.Duration : 0f;
            return Mathf.Max(perceptionDuration, waveDuration);
        }
    }
    public float WaveStartRadius => _wavePrefab != null ? _wavePrefab.StartRadius : 0f;
    public float WaveEndRadius => _wavePrefab != null ? _wavePrefab.EndRadius : 0f;
    public event Action<SensoryEvent> PerceptionRequested;

    private readonly struct ActiveEffect
    {
        public readonly SensoryEffect Effect;
        public readonly int Version;
        public readonly Vector3 Position;
        public readonly bool ShowWhileMoving;

        /// <summary>
        /// 활성 효과와 사건 위치, 재생 버전 및 이동 중 표시 예외를 보관한다.
        /// </summary>
        public ActiveEffect(SensoryEffect effect, Vector3 position, bool showWhileMoving)
        {
            Effect = effect;
            Version = effect.PlaybackVersion;
            Position = position;
            ShowWhileMoving = showWhileMoving;
        }
    }

    void OnValidate()
    {
        if (_maximumDistance <= _fullVisibilityDistance)
            Debug.LogError("Maximum Distance는 Full Visibility Distance보다 커야 합니다.", this);
    }

    void OnEnable()
    {
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
    }

    void OnDisable()
    {
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        EndSession();
    }

    void LateUpdate()
    {
        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            ActiveEffect entry = _activeEffects[i];
            if (entry.Effect == null || !entry.Effect.IsPlaying
                || entry.Effect.PlaybackVersion != entry.Version)
            {
                _activeEffects.RemoveAt(i);
                continue;
            }
            bool allowed = !_footstepsSuppressed || entry.ShowWhileMoving || !(entry.Effect is FootstepObject);
            float visibility = _effectsVisible && allowed && _observer != null
                ? CalculateVisibility(Vector3.Distance(entry.Position, _observer.position))
                : 0f;
            entry.Effect.SetVisibility(visibility);
        }
    }

    /// <summary>
    /// 연결된 효과 프리팹으로 세션에서 사용할 기존 풀을 준비한다.
    /// </summary>
    public bool Initialize()
    {
        _initialized = false;
        if (_footstepPrefab != null)
            Managers.Pool.CreatePool(_footstepPrefab.gameObject);
        if (_wavePrefab != null)
            Managers.Pool.CreatePool(_wavePrefab.gameObject);
        _initialized = true;
        _reportedUnavailable = false;
        return true;
    }

    /// <summary>
    /// 새 관측자 참조로 거리 기준을 교체하고 다음 요청의 준비 상태를 갱신한다.
    /// </summary>
    public void SetObserver(Transform observer)
    {
        _observer = observer;
        _reportedUnavailable = false;
        LateUpdate();
    }

    /// <summary>
    /// 관측자를 해제하고 남아 있는 효과의 표시를 즉시 숨긴다.
    /// </summary>
    public void ClearObserver()
    {
        SetObserver(null);
    }

    /// <summary>visible로 발자국·웨이브 표시를 제어하고 현재 효과에 즉시 반영한다. 인지 사건 전달은 유지한다.</summary>
    public void SetEffectsVisible(bool visible)
    {
        _effectsVisible = visible;
        LateUpdate();
    }

    /// <summary>suppressed로 일반 발자국을 숨긴다. 공격 이동 중 생성한 발자국은 거리와 수명에 따라 계속 표시한다.</summary>
    public void SetFootstepsSuppressed(bool suppressed)
    {
        _footstepsSuppressed = suppressed;
        LateUpdate();
    }

    /// <summary>
    /// 사건 위치와 관측자 거리로 웨이브를 생성하고 생성 성공 여부를 반환한다.
    /// </summary>
    public bool Emit(SensoryEvent sensoryEvent)
    {
        return EmitEffects(sensoryEvent, false);
    }

    /// <summary>
    /// 사건 위치와 좌우 선택으로 발자국만 생성하고 생성 성공 여부를 반환한다.
    /// showWhileMoving이면 플레이어 이동 중 숨김을 우회하되 전체 표시 설정과 거리 제한은 유지한다.
    /// </summary>
    public bool EmitFootstep(SensoryEvent sensoryEvent, bool isLeft, bool showWhileMoving = false)
    {
        return EmitEffects(sensoryEvent, true, isLeft, showWhileMoving);
    }

    /// <summary>
    /// 공통 거리 및 표시 조건을 통과해 효과를 재생하고, 생성 성공한 웨이브만 인지 알림으로 전달한다.
    /// 발자국 요청은 좌우 선택과 표시 정책을 적용하며 주변 시야 영역은 확장하지 않는다.
    /// </summary>
    private bool EmitEffects(SensoryEvent sensoryEvent, bool showFootstep, bool isLeft = false, bool showWhileMoving = false)
    {
        if (!IsReady)
        {
            if (!_reportedUnavailable)
            {
                Debug.LogError("SensoryManager의 세션 설정 또는 관측자가 준비되지 않아 요청을 거부합니다.", this);
                _reportedUnavailable = true;
            }
            return false;
        }
        float visibility = CalculateVisibility(Vector3.Distance(sensoryEvent.Position, _observer.position));
        if (visibility <= 0f)
            return false;
        if (!_effectsVisible || (showFootstep && _footstepsSuppressed && !showWhileMoving))
            return false;
        Quaternion rotation = Quaternion.LookRotation(sensoryEvent.Direction, sensoryEvent.SurfaceNormal);
        bool spawned = false;
        if (showFootstep && _footstepPrefab != null)
        {
            GameObject instance = Managers.Resource.Instantiate(_footstepPrefab.gameObject,
                sensoryEvent.Position + rotation * _footstepPrefab.transform.localPosition, rotation);
            if (instance != null)
            {
                FootstepObject effect = instance.GetComponent<FootstepObject>();
                effect.Play(isLeft, visibility);
                spawned |= Track(effect, sensoryEvent.Position, showWhileMoving);
            }
        }
        if (!showFootstep && _wavePrefab != null)
        {
            GameObject instance = Managers.Resource.Instantiate(_wavePrefab.gameObject,
                sensoryEvent.Position + rotation * _wavePrefab.transform.localPosition, rotation);
            if (instance != null)
            {
                FootstepWave effect = instance.GetComponent<FootstepWave>();
                effect.Play(visibility);
                spawned |= Track(effect, sensoryEvent.Position);
            }
        }
        if (spawned && !showFootstep)
            PerceptionRequested?.Invoke(sensoryEvent);
        return spawned;
    }

    /// <summary>
    /// 세션의 유효한 재생 효과를 반환하고 관측자와 초기화 상태를 해제한다.
    /// </summary>
    public void EndSession()
    {
        foreach (ActiveEffect entry in _activeEffects)
        {
            if (entry.Effect == null || !entry.Effect.IsPlaying || entry.Effect.PlaybackVersion != entry.Version)
                continue;
            if (Managers.IsInitialized)
                Managers.Resource.Destroy(entry.Effect.gameObject);
            else
                Destroy(entry.Effect.gameObject);
        }
        _activeEffects.Clear();
        _observer = null;
        _initialized = false;
        _reportedUnavailable = false;
    }

    /// <summary>
    /// 재생 중인 효과와 위치를 등록하고 생성 성공 여부를 반환한다.
    /// </summary>
    private bool Track(SensoryEffect effect, Vector3 position, bool showWhileMoving = false)
    {
        if (!effect.IsPlaying)
            return false;
        _activeEffects.Add(new ActiveEffect(effect, position, showWhileMoving));
        return true;
    }

    /// <summary>
    /// 거리로 선형 감쇠한 가시성을 반환하며 최대 거리 이상에서는 0을 반환한다.
    /// </summary>
    private float CalculateVisibility(float distance)
    {
        if (distance >= _maximumDistance)
            return 0f;
        if (distance <= _fullVisibilityDistance)
            return 1f;
        float progress = Mathf.InverseLerp(_fullVisibilityDistance, _maximumDistance, distance);
        return 1f - progress;
    }

    /// <summary>
    /// 관측자 소유 씬이 언로드되면 이전 세션의 효과와 참조를 정리한다.
    /// </summary>
    private void HandleSceneUnloaded(Scene scene)
    {
        if (_initialized && (_observer == null || _observer.gameObject.scene == scene))
            EndSession();
    }
}
