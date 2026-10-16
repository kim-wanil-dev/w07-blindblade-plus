using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using TMPro;

public class IntroMenu : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField] private UnityEngine.UI.Button startButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;
    [SerializeField] private string _gameSceneName = "SampleScene";

    [Header("텍스트 절단")]
    [SerializeField, Min(0.01f), Tooltip("절단 연출 시간(초)")] private float _sliceDuration = 0.2f;
    [SerializeField, Range(-90f, 90f), Tooltip("절단선 각도(도)")] private float _sliceAngle = -15f;
    [SerializeField, Min(0f), Tooltip("각 조각의 이동 거리(UI 로컬 단위)")] private float _sliceDistance = 28f;
    [SerializeField, Min(0.01f), Tooltip("빛나는 절단면 폭(UI 로컬 단위)")] private float _sliceGlowWidth = 2f;
    [SerializeField] private Color _sliceGlowColor = new Color(1f, 0.8f, 0.4f, 1f);
    [SerializeField, Min(0f), Tooltip("절단 조각이 튀어나가는 거리 배율")] private float _sliceKickMultiplier = 1.8f;
    [SerializeField, Min(1f), Tooltip("절단 순간 최대 확대 배율")] private float _slicePopScale = 1.18f;
    [SerializeField, Range(0f, 1f), Tooltip("사라질 때 최종 크기 배율")] private float _sliceEndScale = 0.65f;
    [SerializeField, Range(0f, 45f), Tooltip("각 조각이 반대로 회전하는 최종 각도(도)")] private float _sliceRotation = 12f;
    private readonly List<GameObject> _sliceObjects = new List<GameObject>();
    private readonly List<Vector3> _sliceOffsets = new List<Vector3>();
    private readonly List<Mesh> _sliceMeshes = new List<Mesh>();
    private readonly List<Material> _sliceMaterials = new List<Material>();
    private Shader _sliceShader;
    private readonly List<TMP_Text> _slicedTexts = new List<TMP_Text>();
    private VolumeControlUI _volumeControl;
    private IntroOnboardingTransition _onboardingTransition;
    private Coroutine _sliceRoutine;
    private bool _isSlicing;

    void Awake()
    {
        _sliceShader = Resources.Load<Shader>("Effects/IntroTextSlice");
        _volumeControl = GetComponent<VolumeControlUI>();
        _onboardingTransition = GetComponent<IntroOnboardingTransition>();
    }

    void OnEnable()
    {
        startButton.onClick.AddListener(OnStartClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        if (_volumeControl != null)
        {
            _volumeControl.OpenRequested += BeginSlice;
            _volumeControl.PanelClosed += RestoreMenuTexts;
        }
    }

    void OnDisable()
    {
        startButton.onClick.RemoveListener(OnStartClicked);
        quitButton.onClick.RemoveListener(OnQuitClicked);
        if (_volumeControl != null)
        {
            _volumeControl.OpenRequested -= BeginSlice;
            _volumeControl.PanelClosed -= RestoreMenuTexts;
        }
        if (_sliceRoutine != null)
        {
            StopCoroutine(_sliceRoutine);
            _sliceRoutine = null;
        }
        ClearSlice();
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 시작 버튼의 절단 연출을 요청하고 연출 후 게임 씬으로 전환한다.
    /// </summary>
    private void OnStartClicked()
    {
        BeginSlice(startButton, LoadGameScene);
    }

    /// <summary>
    /// 시작 선택의 연출이 끝나면 지정 씬의 등록을 확인하고 씬을 전환한다.
    /// </summary>
    private void LoadGameScene()
    {
        if (!Application.CanStreamedLevelBeLoaded(_gameSceneName))
        {
            Debug.LogError("[IntroMenu] Game scene is not in the build scene list: " + _gameSceneName);
            return;
        }

        PlayerPrefs.Save();
        if (_onboardingTransition != null)
        {
            _onboardingTransition.Begin(_gameSceneName);
            return;
        }

        SceneManager.LoadScene(_gameSceneName);
    }

    /// <summary>
    /// 종료 버튼의 절단 연출을 요청하고 연출 후 앱 또는 Play Mode를 종료한다.
    /// </summary>
    private void OnQuitClicked()
    {
        BeginSlice(quitButton, QuitGame);
    }

    /// <summary>
    /// 종료 선택의 연출이 끝나면 음량을 저장하고 앱 또는 Play Mode를 종료한다.
    /// </summary>
    private void QuitGame()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    
    /// <summary>
    /// button의 TMP 메시를 두 조각으로 만들고, 절단 완료 후 onCompleted를 실행한다.
    /// </summary>
    private void BeginSlice(UnityEngine.UI.Button button, Action onCompleted)
    {
        if (_isSlicing)
        {
            return;
        }

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (_sliceShader == null || !_sliceShader.isSupported || text == null)
        {
            Debug.LogError("[IntroMenu] 절단 셰이더 또는 메뉴 TMP 텍스트를 확인해주세요.", this);
            return;
        }

        if (!text.enabled)
        {
            return;
        }

        _isSlicing = true;
        _slicedTexts.Add(text);
        text.ForceMeshUpdate();
        Vector3 center = text.textBounds.center;
        float angle = _sliceAngle * Mathf.Deg2Rad;
        Vector4 normal = new Vector4(-Mathf.Sin(angle), Mathf.Cos(angle), 0f, 0f);
        Material[] sourceMaterials = text.fontSharedMaterials;
        for (int i = 0; i < text.textInfo.materialCount; i++)
        {
            TMP_MeshInfo meshInfo = text.textInfo.meshInfo[i];
            if (meshInfo.vertexCount == 0)
            {
                continue;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                CreateSlicePiece(text, meshInfo.mesh, sourceMaterials[i], center, normal, side);
            }
        }

        text.enabled = false;
        _sliceRoutine = StartCoroutine(AnimateSlice(onCompleted));
    }

    /// <summary>
    /// text의 로컬 좌표를 유지하는 메시와 전용 머티리얼을 생성하고 side 쪽 절반을 표시한다.
    /// </summary>
    private void CreateSlicePiece(
        TMP_Text text, Mesh sourceMesh, Material sourceMaterial,
        Vector3 center, Vector4 normal, int side)
    {
        GameObject piece = new GameObject("IntroTextSlice", typeof(RectTransform), typeof(CanvasRenderer));
        piece.layer = text.gameObject.layer;
        RectTransform rect = (RectTransform)piece.transform;
        RectTransform sourceRect = text.rectTransform;
        // 메뉴 레이아웃의 항목을 늘리지 않도록 선택한 텍스트 내부에 조각을 배치한다.
        rect.SetParent(sourceRect, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = sourceRect.pivot;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Mesh mesh = Instantiate(sourceMesh);
        Vector3[] vertices = mesh.vertices;
        List<Vector4> uvs = new List<Vector4>(vertices.Length);
        mesh.GetUVs(0, uvs);
        // UI 배칭의 좌표 변환에 영향받지 않도록 로컬 절단 거리를 UV에 저장한다.
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector4 uv = uvs[i];
            uv.z = (vertices[i].x - center.x) * normal.x
                + (vertices[i].y - center.y) * normal.y;
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

    /// <summary>
    /// 실제 경과 시간으로 절단 진행도를 갱신하고, 종료 후 텍스트를 숨긴 채 onCompleted를 실행한다.
    /// </summary>
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
        ClearSlice();
        onCompleted();
    }

    /// <summary>
    /// 생성한 절단 메시·머티리얼을 해제하고 연출 중 상태를 해제한다. 원본 텍스트는 숨김을 유지한다.
    /// </summary>
    private void ClearSlice()
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
        _isSlicing = false;
    }
    /// <summary>
    /// 음량 패널에서 메인 메뉴로 돌아오면 잘린 메뉴 텍스트를 모두 복원한다.
    /// </summary>
    private void RestoreMenuTexts()
    {
        for (int i = 0; i < _slicedTexts.Count; i++)
        {
            if (_slicedTexts[i] != null)
            {
                _slicedTexts[i].enabled = true;
            }
        }
        _slicedTexts.Clear();
    }
}
    
