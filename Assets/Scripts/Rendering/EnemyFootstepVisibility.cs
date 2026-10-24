using System.Collections.Generic;

using UnityEngine;

namespace BlindBlade.Perception
{
    /// <summary>발걸음 인디케이터의 화면 진입을 적 몸체로 판정하고 눈 섬광·연기·궤적 이펙트의 경계를 제외한다.</summary>
    public static class EnemyFootstepVisibility
    {
        /// <summary>
        /// bodyCapsule의 몸체 경계와 cameraPlanes가 겹치는지 반환한다.
        /// 몸체가 없으면 renderers의 활성 메시와 발행자 위치인 pointInCamera를 사용한다.
        /// </summary>
        public static bool IsInCamera(EnemyHitCapsule bodyCapsule, IReadOnlyList<Renderer> renderers,
            Plane[] cameraPlanes, bool pointInCamera = false)
        {
            if (bodyCapsule != null && bodyCapsule.IsPresent)
                return GeometryUtility.TestPlanesAABB(cameraPlanes, bodyCapsule.Bounds);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (GeometryUtility.TestPlanesAABB(cameraPlanes, renderer.bounds)) return true;
            }
            return pointInCamera;
        }
    }
}
