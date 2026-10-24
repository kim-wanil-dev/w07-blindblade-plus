using System.Collections.Generic;

using UnityEngine;

/// <summary>
/// 이 오브젝트와 자식의 VFX 렌더러를 선택적 윤곽선 처리 대상으로 등록한다.
/// 원본 머티리얼과 충돌 레이어는 변경하지 않는다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Effects/Outline VFX")]
public sealed class OutlineVFX : MonoBehaviour
{
    private static readonly HashSet<OutlineVFX> _active = new HashSet<OutlineVFX>();
    private static readonly List<Renderer> _renderers = new List<Renderer>();

    [Header("인지 표시")]
    [SerializeField] private bool _ignorePerception;
    internal bool IgnorePerception { get => _ignorePerception; set => _ignorePerception = value; }

    void OnEnable() { _active.Add(this); }
    void OnDisable() { _active.Remove(this); }

    /// <summary>
    /// 활성 컴포넌트의 렌더러를 result에, 인지 예외 렌더러를 alwaysVisible에 모은다.
    /// 동적 자식도 포함하며 같은 렌더러는 한 번만 등록한다. 자식 렌더러는 재사용 목록에 모아 매 카메라 GC 할당을 만들지 않는다.
    /// </summary>
    internal static void Collect(HashSet<Renderer> result, HashSet<Renderer> alwaysVisible)
    {
        result.Clear();
        alwaysVisible.Clear();
        foreach (OutlineVFX marker in _active)
        {
            if (marker == null || !marker.isActiveAndEnabled || !marker.gameObject.scene.IsValid())
                continue;
            marker.GetComponentsInChildren(_renderers);
            foreach (Renderer renderer in _renderers)
                if (renderer.enabled)
                {
                    result.Add(renderer);
                    if (marker._ignorePerception)
                        alwaysVisible.Add(renderer);
                }
        }
    }
}
