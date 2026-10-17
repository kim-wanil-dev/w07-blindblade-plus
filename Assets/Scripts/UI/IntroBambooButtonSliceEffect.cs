using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.UI;

using TMPro;

/// <summary>대나무 버튼 이미지와 TMP 라벨의 절단 조각 애니메이션 및 원본 복원을 관리한다.</summary>
public sealed class IntroBambooButtonSliceEffect : MonoBehaviour
{
    [Header("버튼 참조")]
    [SerializeField] private Image _visual;
    [SerializeField] private TMP_Text _label;

    [Header("대나무 버튼 소리")]
    [SerializeField] private string _sliceSoundStreamingPath = "UI/BambooButtonCut.mp3";
    [SerializeField, Min(0f)] private float _sliceSoundVolume = 0.8f;
    [SerializeField, Range(0.25f, 4f)] private float _sliceSoundPitch = 1f;

    [Header("절단 연출")]
    [SerializeField, Min(0.01f)] private float _sliceDuration = 0.2f;
    [SerializeField, Range(-90f, 90f)] private float _sliceAngle = -15f;
    [SerializeField, Min(0f)] private float _sliceDistance = 28f;
    [SerializeField, Min(0.01f)] private float _sliceGlowWidth = 2f;
    [SerializeField] private Color _sliceGlowColor = new Color(1f, 0.8f, 0.4f, 1f);
    [SerializeField, Min(0f)] private float _sliceKickMultiplier = 1.8f;
    [SerializeField, Min(1f)] private float _slicePopScale = 1.18f;
    [SerializeField, Range(0f, 1f)] private float _sliceEndScale = 0.65f;
    [SerializeField, Range(0f, 45f)] private float _sliceRotation = 12f;

    [Header("절단 런타임 상태")]
    private readonly List<GameObject> _sliceObjects = new List<GameObject>();
    private readonly List<Vector3> _sliceOffsets = new List<Vector3>();
    private readonly List<Mesh> _sliceMeshes = new List<Mesh>();
    private readonly List<Material> _sliceMaterials = new List<Material>();
    private Shader _sliceShader;
    private Shader _imageSliceShader;
    private Coroutine _sliceRoutine;
    private bool _originalLabelEnabled;
    private bool _originalVisualEnabled;
    private bool _hasCapturedOriginalState;
    private bool _isSlicing;

    void Awake()
    {
        _sliceShader = Resources.Load<Shader>("Effects/IntroTextSlice");
        _imageSliceShader = Resources.Load<Shader>("Effects/IntroBambooImageSlice");
    }

    void Start()
    {
        CaptureOriginalState();
    }

    void OnDisable()
    {
        RestoreOriginal();
    }

    /// <summary>이미지와 라벨의 최초 활성 상태를 저장해 절단 뒤 복원에 사용한다.</summary>
    private void CaptureOriginalState()
    {
        _originalVisualEnabled = _visual.enabled;
        _originalLabelEnabled = _label.enabled;
        _hasCapturedOriginalState = true;
    }

    /// <summary>대나무 이미지와 라벨을 두 조각으로 나누어 재생하고 완료 시 callback을 호출한다.</summary>
    public bool Play(Action onCompleted)
    {
        if (_isSlicing)
            return false;

        if (!_hasCapturedOriginalState)
            CaptureOriginalState();

        if (_sliceShader == null || !_sliceShader.isSupported || _imageSliceShader == null
            || !_imageSliceShader.isSupported || !_label.enabled || !_visual.enabled)
        {
            Debug.LogError("[IntroBambooButtonSliceEffect] 절단 셰이더와 대나무 버튼 참조를 확인해주세요.", this);
            return false;
        }

        _isSlicing = true;
        _label.ForceMeshUpdate();
        float angle = _sliceAngle * Mathf.Deg2Rad;
        Vector4 normal = new Vector4(-Mathf.Sin(angle), Mathf.Cos(angle), 0f, 0f);
        Material[] sourceMaterials = _label.fontSharedMaterials;
        for (int i = 0; i < _label.textInfo.materialCount; i++)
        {
            TMP_MeshInfo meshInfo = _label.textInfo.meshInfo[i];
            if (meshInfo.vertexCount == 0)
                continue;

            for (int side = -1; side <= 1; side += 2)
                CreateLabelSlicePiece(meshInfo.mesh, sourceMaterials[i], _label.textBounds.center, normal, side);
        }

        Mesh visualSourceMesh = _visual.canvasRenderer.GetMesh();
        if (visualSourceMesh.vertexCount > 0)
        {
            CreateVisualSlicePieces(visualSourceMesh, normal);
            _visual.enabled = false;
        }

        Managers.Sound.PlayStreamingEffect(_sliceSoundStreamingPath, _sliceSoundVolume, _sliceSoundPitch, gameObject);
        _label.enabled = false;
        _sliceRoutine = StartCoroutine(AnimateSlice(onCompleted));
        return true;
    }

    /// <summary>이미지 메시와 절단 방향으로 양쪽 이미지 조각을 만들어 애니메이션 목록에 등록한다.</summary>
    private void CreateVisualSlicePieces(Mesh sourceMesh, Vector4 normal)
    {
        Vector3 center = sourceMesh.bounds.center;
        for (int side = -1; side <= 1; side += 2)
            CreateVisualSlicePiece(sourceMesh, center, normal, side);
    }

    /// <summary>이미지의 한쪽 절단 메시와 셰이더 머티리얼을 만들고 이동값을 저장한다.</summary>
    private void CreateVisualSlicePiece(Mesh sourceMesh, Vector3 center, Vector4 normal, int side)
    {
        GameObject piece = new GameObject("IntroBambooSlice", typeof(RectTransform), typeof(CanvasRenderer));
        piece.layer = _visual.gameObject.layer;
        RectTransform rect = (RectTransform)piece.transform;
        rect.SetParent(_visual.rectTransform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = _visual.rectTransform.pivot;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Mesh mesh = Instantiate(sourceMesh);
        Vector3[] vertices = mesh.vertices;
        List<Vector4> uvs = new List<Vector4>(vertices.Length);
        mesh.GetUVs(0, uvs);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector4 uv = uvs[i];
            uv.z = (vertices[i].x - center.x) * normal.x + (vertices[i].y - center.y) * normal.y;
            uvs[i] = uv;
        }
        mesh.SetUVs(0, uvs);

        Material material = new Material(_imageSliceShader);
        material.SetFloat("_SliceSide", side);
        material.SetFloat("_SliceProgress", 0f);
        material.SetFloat("_SliceGlowWidth", _sliceGlowWidth);
        material.SetColor("_SliceGlowColor", _sliceGlowColor);
        CanvasRenderer renderer = piece.GetComponent<CanvasRenderer>();
        renderer.materialCount = 1;
        renderer.SetMaterial(material, 0);
        renderer.SetTexture(_visual.sprite.texture);
        renderer.SetMesh(mesh);
        renderer.SetColor(_visual.canvasRenderer.GetColor());

        _sliceObjects.Add(piece);
        _sliceOffsets.Add(new Vector3(normal.x, normal.y, 0f) * side * _sliceDistance);
        _sliceMeshes.Add(mesh);
        _sliceMaterials.Add(material);
    }

    /// <summary>TMP 메시의 한쪽 절단 조각을 만들고 글꼴 머티리얼과 이동값을 저장한다.</summary>
    private void CreateLabelSlicePiece(Mesh sourceMesh, Material sourceMaterial,
        Vector3 center, Vector4 normal, int side)
    {
        GameObject piece = new GameObject("IntroTextSlice", typeof(RectTransform), typeof(CanvasRenderer));
        piece.layer = _label.gameObject.layer;
        RectTransform rect = (RectTransform)piece.transform;
        rect.SetParent(_label.rectTransform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = _label.rectTransform.pivot;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Mesh mesh = Instantiate(sourceMesh);
        Vector3[] vertices = mesh.vertices;
        List<Vector4> uvs = new List<Vector4>(vertices.Length);
        mesh.GetUVs(0, uvs);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector4 uv = uvs[i];
            uv.z = (vertices[i].x - center.x) * normal.x + (vertices[i].y - center.y) * normal.y;
            uvs[i] = uv;
        }
        mesh.SetUVs(0, uvs);

        Material material = new Material(sourceMaterial);
        material.shader = _sliceShader;
        material.SetFloat("_SliceSide", side);
        material.SetFloat("_SliceProgress", 0f);
        material.SetFloat("_SliceGlowWidth", _sliceGlowWidth);
        material.SetColor("_SliceGlowColor", _sliceGlowColor);
        CanvasRenderer renderer = piece.GetComponent<CanvasRenderer>();
        renderer.materialCount = 1;
        renderer.SetMaterial(material, 0);
        renderer.SetTexture(material.mainTexture);
        renderer.SetMesh(mesh);

        _sliceObjects.Add(piece);
        _sliceOffsets.Add(new Vector3(normal.x, normal.y, 0f) * side * _sliceDistance);
        _sliceMeshes.Add(mesh);
        _sliceMaterials.Add(material);
    }

    /// <summary>절단 진행도와 조각 이동·회전을 갱신하고 끝나면 원본을 숨긴 채 callback을 실행한다.</summary>
    private IEnumerator AnimateSlice(Action onCompleted)
    {
        float elapsed = 0f;
        while (elapsed < _sliceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _sliceDuration));
            float remaining = 1f - progress;
            float kick = 1f - remaining * remaining * remaining;
            float scale = progress < 0.15f
                ? Mathf.Lerp(1f, _slicePopScale, progress / 0.15f)
                : Mathf.Lerp(_slicePopScale, _sliceEndScale, (progress - 0.15f) / 0.85f);
            for (int i = 0; i < _sliceMaterials.Count; i++)
            {
                _sliceMaterials[i].SetFloat("_SliceProgress", progress);
                Transform piece = _sliceObjects[i].transform;
                piece.localPosition = _sliceOffsets[i] * (kick * _sliceKickMultiplier);
                piece.localScale = Vector3.one * scale;
                float side = i % 2 == 0 ? -1f : 1f;
                piece.localRotation = Quaternion.Euler(0f, 0f, side * _sliceRotation * kick);
            }
            yield return null;
        }

        _sliceRoutine = null;
        ClearSlicePieces();
        _isSlicing = false;
        onCompleted?.Invoke();
    }

    /// <summary>생성한 메시와 머티리얼을 해제하고 절단 조각 목록을 비운다.</summary>
    private void ClearSlicePieces()
    {
        for (int i = 0; i < _sliceObjects.Count; i++)
        {
            _sliceObjects[i].SetActive(false);
            Destroy(_sliceObjects[i]);
            Destroy(_sliceMeshes[i]);
            Destroy(_sliceMaterials[i]);
        }
        _sliceObjects.Clear();
        _sliceOffsets.Clear();
        _sliceMeshes.Clear();
        _sliceMaterials.Clear();
    }

    /// <summary>진행 중 연출을 취소하고 버튼 이미지와 라벨을 저장된 활성 상태로 복원한다.</summary>
    public void RestoreOriginal()
    {
        if (_sliceRoutine != null)
        {
            StopCoroutine(_sliceRoutine);
            _sliceRoutine = null;
        }
        ClearSlicePieces();
        _isSlicing = false;
        if (!_hasCapturedOriginalState)
            return;

        _visual.enabled = _originalVisualEnabled;
        _label.enabled = _originalLabelEnabled;
    }
}
