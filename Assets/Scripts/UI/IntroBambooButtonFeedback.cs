using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using TMPro;

/// <summary>대나무 버튼의 기본·호버·선택 색과 크기를 Inspector 설정으로 표현한다.</summary>
public sealed class IntroBambooButtonFeedback : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("버튼 참조")]
    [SerializeField] private Button _button;
    [SerializeField] private Image _visual;
    [SerializeField] private TMP_Text _label;

    [Header("호버 상태")]
    [SerializeField] private Color _hoverVisualColor = Color.gray;
    [SerializeField] private Color _hoverLabelColor = Color.gray;
    [SerializeField, Min(0.1f)] private float _hoverVisualScale = 1.1f;
    [SerializeField, Min(0.1f)] private float _hoverLabelScale = 1.1f;

    [Header("선택 상태")]
    [SerializeField] private Color _selectedVisualColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color _selectedLabelColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField, Min(0.1f)] private float _selectedVisualScale = 1f;
    [SerializeField, Min(0.1f)] private float _selectedLabelScale = 1f;

    [Header("기본 상태")]
    private Color _originalVisualColor;
    private Color _originalLabelColor;
    private Vector3 _originalVisualScale;
    private Vector3 _originalLabelScale;
    private bool _hasCapturedAppearance;
    private bool _pointerOver;
    private bool _pointerDown;
    private bool _selected;

    void Start()
    {
        CaptureOriginalAppearance();
        ApplyOriginalAppearance();
    }

    void OnEnable()
    {
        _pointerOver = false;
        _pointerDown = false;
        _selected = false;
        ApplyOriginalAppearance();
    }

    void OnDisable()
    {
        _pointerOver = false;
        _pointerDown = false;
        _selected = false;
        ApplyOriginalAppearance();
    }

    /// <summary>초기 색과 로컬 크기를 저장해 모든 상태에서 돌아갈 기준으로 사용한다.</summary>
    private void CaptureOriginalAppearance()
    {
        _originalVisualColor = _visual.color;
        _originalLabelColor = _label.color;
        _originalVisualScale = _visual.transform.localScale;
        _originalLabelScale = _label.transform.localScale;
        _hasCapturedAppearance = true;
    }

    /// <summary>포인터·선택 상태에 맞는 색과 기준 크기 배율을 두 시각 요소에 적용한다.</summary>
    private void ApplyCurrentAppearance()
    {
        if (!_hasCapturedAppearance)
            return;

        if (!_button.interactable)
        {
            ApplyOriginalAppearance();
            return;
        }

        if (_pointerDown || _selected)
        {
            ApplyAppearance(_selectedVisualColor, _selectedLabelColor,
                _selectedVisualScale, _selectedLabelScale);
            return;
        }

        if (_pointerOver)
        {
            ApplyAppearance(_hoverVisualColor, _hoverLabelColor,
                _hoverVisualScale, _hoverLabelScale);
            return;
        }

        ApplyOriginalAppearance();
    }

    /// <summary>지정한 두 색과 기준 크기 배율을 대나무 이미지와 라벨에 적용한다.</summary>
    private void ApplyAppearance(Color visualColor, Color labelColor, float visualScale, float labelScale)
    {
        _visual.color = visualColor;
        _label.color = labelColor;
        _visual.transform.localScale = _originalVisualScale * visualScale;
        _label.transform.localScale = _originalLabelScale * labelScale;
    }

    /// <summary>대나무 이미지와 라벨을 캡처한 기본 색과 크기로 되돌린다.</summary>
    private void ApplyOriginalAppearance()
    {
        if (!_hasCapturedAppearance)
            return;

        _visual.color = _originalVisualColor;
        _label.color = _originalLabelColor;
        _visual.transform.localScale = _originalVisualScale;
        _label.transform.localScale = _originalLabelScale;
    }

    /// <summary>포인터가 버튼에 들어오면 선택 입력이 없는 경우 호버 표현을 적용한다.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerOver = true;
        ApplyCurrentAppearance();
    }

    /// <summary>포인터가 버튼을 벗어나면 포인터·선택 표현을 해제하고 기본 모습으로 복원한다.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerOver = false;
        _pointerDown = false;
        _selected = false;
        ApplyOriginalAppearance();
    }

    /// <summary>포인터로 버튼을 누르면 선택 색과 크기를 적용한다.</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerDown = true;
        ApplyCurrentAppearance();
    }

    /// <summary>포인터를 놓으면 포인터 위치에 따라 호버 또는 기본 표현으로 전환한다.</summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        _pointerDown = false;
        ApplyCurrentAppearance();
    }

    /// <summary>키보드나 게임패드로 버튼이 선택되면 선택 표현을 적용한다.</summary>
    public void OnSelect(BaseEventData eventData)
    {
        _selected = true;
        ApplyCurrentAppearance();
    }

    /// <summary>키보드나 게임패드 포커스가 이동하면 호버 또는 기본 표현으로 복원한다.</summary>
    public void OnDeselect(BaseEventData eventData)
    {
        _selected = false;
        ApplyCurrentAppearance();
    }
}
