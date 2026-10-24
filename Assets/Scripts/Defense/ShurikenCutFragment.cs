using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>잘린 수리검 외형을 두 조각으로 나누고 중력으로 낙하시켜 바닥 접촉 때 제거한다.</summary>
public sealed class ShurikenCutFragment : MonoBehaviour
{
    private const float SEPARATION_SPEED = 1.1f;
    private const float SPLIT_OFFSET = 0.03f;
    private const float MIN_FALL_DELAY = 0.12f;
    private const float MAX_FALL_DELAY = 0.22f;
    private const float MIN_FACE_PLAYER_DURATION = 0.06f;
    private const float MAX_FACE_PLAYER_DURATION = 0.28f;
    private const float MIN_SPIN_SPEED = 60f;
    private const float MAX_SPIN_SPEED = 620f;
    private const float MAX_LIFETIME = 8f;
    private const float CONTACT_RADIUS = 0.02f;

    private static readonly Dictionary<Mesh, CutMesh[]> _halves = new Dictionary<Mesh, CutMesh[]>();

    [Header("낙하 상태")]
    private readonly RaycastHit[] _floorHits = new RaycastHit[32];
    private Vector3 _velocity;
    private Vector3 _spinAxis;
    private float _spinSpeed;
    private Quaternion _startRotation;
    private Quaternion _faceRotation;
    private float _faceDuration;
    private float _fallDelay;
    private float _elapsed;
    private int _collisionMask;

    /// <summary>
    /// source의 MeshFilter 외형을 중심에서 반으로 잘라 같은 위치와 크기의 낙하 조각 두 개를 생성한다.
    /// collisionMask로 바닥을 검사하고 두 조각을 수평으로 벌려 잠시 정지한 뒤 무작위 속도와 회전으로 낙하시킨다.
    /// 원본의 공격 컴포넌트와 비행음은 복제하지 않는다.
    /// </summary>
    public static void Spawn(Transform source, int collisionMask)
    {
        Camera playerCamera = Game.IsInitialized && Game.Player != null
            ? Game.Player.GetComponentInChildren<Camera>() : Camera.main;
        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.GetComponent<ShurikenVisualRotation>() == null) continue;
            Mesh mesh = filter.sharedMesh;
            MeshRenderer original = filter.GetComponent<MeshRenderer>();
            if (mesh == null || !mesh.isReadable || original == null) continue;
            if (!_halves.TryGetValue(mesh, out CutMesh[] pieces))
            {
                pieces = new[] { Slice(mesh, 1f), Slice(mesh, -1f) };
                _halves.Add(mesh, pieces);
            }

            Quaternion faceRotation = GetFacingRotation(filter.transform, playerCamera);
            Vector3 separationAxis = Vector3.ProjectOnPlane(faceRotation * Vector3.right, Vector3.up);
            if (separationAxis.sqrMagnitude < 0.0001f)
                separationAxis = Vector3.ProjectOnPlane(source.right, Vector3.up);
            if (separationAxis.sqrMagnitude < 0.0001f) separationAxis = Vector3.right;
            separationAxis.Normalize();
            Vector3 drift = Vector3.ProjectOnPlane(Random.insideUnitSphere, Vector3.up) * 1.2f;

            for (int i = 0; i < pieces.Length; i++)
            {
                if (pieces[i].Mesh.vertexCount == 0) continue;
                float side = i == 0 ? 1f : -1f;
                GameObject fragment = new GameObject("Shuriken cut half");
                SceneManager.MoveGameObjectToScene(fragment, source.gameObject.scene);
                fragment.transform.SetPositionAndRotation(filter.transform.TransformPoint(pieces[i].Center),
                    filter.transform.rotation);
                fragment.transform.position += separationAxis * (side * SPLIT_OFFSET);
                fragment.transform.localScale = filter.transform.lossyScale;
                fragment.AddComponent<MeshFilter>().sharedMesh = pieces[i].Mesh;
                MeshRenderer renderer = fragment.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials;
                renderer.renderingLayerMask = (original.renderingLayerMask & ~OutlineLayer.SCENERY_MASK)
                    | OutlineLayer.THREAT_MASK;
                renderer.shadowCastingMode = original.shadowCastingMode;
                renderer.receiveShadows = original.receiveShadows;

                ShurikenCutFragment motion = fragment.AddComponent<ShurikenCutFragment>();
                Vector3 separation = Quaternion.AngleAxis(Random.Range(-80f, 80f), Vector3.up)
                    * separationAxis * side;
                motion._velocity = separation * (SEPARATION_SPEED * Random.Range(0.35f, 2.4f))
                    + drift;
                motion._fallDelay = Random.Range(MIN_FALL_DELAY, MAX_FALL_DELAY);
                motion._startRotation = filter.transform.rotation;
                motion._faceRotation = Quaternion.Slerp(filter.transform.rotation, faceRotation,
                    Random.Range(0.35f, 1f)) * Quaternion.Euler(
                    Random.Range(-35f, 35f), Random.Range(-180f, 180f), Random.Range(-35f, 35f));
                motion._faceDuration = Random.Range(MIN_FACE_PLAYER_DURATION, MAX_FACE_PLAYER_DURATION);
                motion._spinAxis = Random.onUnitSphere;
                motion._spinSpeed = Random.Range(MIN_SPIN_SPEED, MAX_SPIN_SPEED);
                motion._collisionMask = collisionMask;
            }
        }
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;
        _elapsed += deltaTime;
        if (_elapsed >= MAX_LIFETIME)
        {
            Destroy(gameObject);
            return;
        }

        float fallDeltaTime = Mathf.Clamp(_elapsed - _fallDelay, 0f, deltaTime);
        Vector3 movement = _velocity * fallDeltaTime
            + Physics.gravity * (0.5f * fallDeltaTime * fallDeltaTime);
        _velocity += Physics.gravity * fallDeltaTime;
        if (TouchesFloor(movement))
        {
            Destroy(gameObject);
            return;
        }

        transform.position += movement;
        float faceBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _faceDuration));
        float tumbleTime = Mathf.Max(0f, _elapsed - _faceDuration);
        transform.rotation = Quaternion.AngleAxis(_spinSpeed * tumbleTime, _spinAxis)
            * Quaternion.Slerp(_startRotation, _faceRotation, faceBlend);
    }

    /// <summary>
    /// model의 윗면 법선을 playerCamera 방향으로 향하게 한 기준 회전을 반환한다.
    /// 호출부가 조각마다 기준 회전의 적용 비율·추가 각도·전환 시간을 무작위로 정한다.
    /// 카메라가 없으면 수리검 진행 방향의 반대쪽을 기준으로 윗면이 드러나게 한다.
    /// </summary>
    private static Quaternion GetFacingRotation(Transform model, Camera playerCamera)
    {
        Vector3 toViewer = playerCamera != null
            ? playerCamera.transform.position - model.position : -model.forward;
        if (toViewer.sqrMagnitude < 0.0001f) return model.rotation;
        Quaternion facing = Quaternion.FromToRotation(model.up, toViewer.normalized) * model.rotation;
        return facing;
    }

    /// <summary>
    /// 이번 movement를 구 형태로 검사하여 플레이어·적을 제외한 위쪽 표면에 닿는지 반환한다.
    /// 벽과 트리거는 바닥으로 취급하지 않으며 낙하 중에만 접촉으로 처리한다.
    /// </summary>
    private bool TouchesFloor(Vector3 movement)
    {
        if (movement.y >= 0f || movement.sqrMagnitude < 0.000001f) return false;
        int count = Physics.SphereCastNonAlloc(transform.position, CONTACT_RADIUS, movement.normalized,
            _floorHits, movement.magnitude + CONTACT_RADIUS, _collisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _floorHits[i];
            if (hit.normal.y < 0.5f) continue;
            if (hit.collider.GetComponentInParent<EnemyHealth>() != null
                || hit.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>새 플레이 시작 시 이전 실행에서 캐시한 절단 메시를 해제하고 캐시 상태를 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMeshes()
    {
        foreach (CutMesh[] pieces in _halves.Values)
            foreach (CutMesh piece in pieces)
                if (piece.Mesh != null) Destroy(piece.Mesh);
        _halves.Clear();
    }

    /// <summary>
    /// source 메시의 중심 X 평면에서 side 방향 절반에 속한 삼각형을 잘라 새 메시로 반환한다.
    /// 교차하는 삼각형은 교차점을 추가하여 원본 UV·법선·서브메시를 유지한다.
    /// </summary>
    private static CutMesh Slice(Mesh source, float side)
    {
        Vector3[] positions = source.vertices;
        Vector3[] normals = source.normals;
        Vector2[] uv = source.uv;
        List<Vector3> output = new List<Vector3>();
        List<Vector3> outputNormals = new List<Vector3>();
        List<Vector2> outputUv = new List<Vector2>();
        List<int>[] triangles = new List<int>[source.subMeshCount];
        Vertex[] input = new Vertex[3];
        Vertex[] polygon = new Vertex[4];
        float center = source.bounds.center.x;

        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            triangles[submesh] = new List<int>();
            int[] indices = source.GetTriangles(submesh);
            for (int triangle = 0; triangle < indices.Length; triangle += 3)
            {
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    int index = indices[triangle + vertex];
                    input[vertex] = new Vertex
                    {
                        Position = positions[index],
                        Normal = normals.Length == positions.Length ? normals[index] : Vector3.up,
                        Uv = uv.Length == positions.Length ? uv[index] : Vector2.zero
                    };
                }

                int count = ClipTriangle(input, polygon, center, side);
                int first = output.Count;
                for (int vertex = 0; vertex < count; vertex++)
                {
                    output.Add(polygon[vertex].Position);
                    outputNormals.Add(polygon[vertex].Normal);
                    outputUv.Add(polygon[vertex].Uv);
                }
                for (int vertex = 1; vertex < count - 1; vertex++)
                {
                    triangles[submesh].Add(first);
                    triangles[submesh].Add(first + vertex);
                    triangles[submesh].Add(first + vertex + 1);
                }
            }
        }

        Mesh result = new Mesh { name = source.name + " cut half", indexFormat = source.indexFormat };
        result.SetVertices(output);
        result.SetNormals(outputNormals);
        result.SetUVs(0, outputUv);
        result.subMeshCount = triangles.Length;
        for (int submesh = 0; submesh < triangles.Length; submesh++)
            result.SetTriangles(triangles[submesh], submesh);
        result.RecalculateBounds();
        Vector3 pivot = result.bounds.center;
        for (int i = 0; i < output.Count; i++) output[i] -= pivot;
        result.SetVertices(output);
        result.RecalculateBounds();
        return new CutMesh { Mesh = result, Center = pivot };
    }

    /// <summary>
    /// input 삼각형을 center 평면의 side 쪽으로 잘라 output에 순서대로 저장하고 정점 수를 반환한다.
    /// 평면을 가로지르는 변은 위치·UV·법선을 보간해 최대 네 정점으로 만든다.
    /// </summary>
    private static int ClipTriangle(Vertex[] input, Vertex[] output, float center, float side)
    {
        int count = 0;
        Vertex previous = input[2];
        float previousDistance = (previous.Position.x - center) * side;
        for (int i = 0; i < 3; i++)
        {
            Vertex current = input[i];
            float distance = (current.Position.x - center) * side;
            if ((distance >= 0f) != (previousDistance >= 0f))
            {
                float t = previousDistance / (previousDistance - distance);
                output[count++] = new Vertex
                {
                    Position = Vector3.LerpUnclamped(previous.Position, current.Position, t),
                    Normal = Vector3.LerpUnclamped(previous.Normal, current.Normal, t).normalized,
                    Uv = Vector2.LerpUnclamped(previous.Uv, current.Uv, t)
                };
            }
            if (distance >= 0f) output[count++] = current;
            previous = current;
            previousDistance = distance;
        }
        return count;
    }

    private struct Vertex
    {
        public Vector3 Position { get; set; }
        public Vector3 Normal { get; set; }
        public Vector2 Uv { get; set; }
    }

    private sealed class CutMesh
    {
        public Mesh Mesh { get; set; }
        public Vector3 Center { get; set; }
    }
}
