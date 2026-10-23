using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public sealed class PlayerInteraction : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private Camera _viewCamera;
    [SerializeField, Min(0.1f)] private float _range = 3f;
    [SerializeField] private LayerMask _raycastMask = ~0;
    private PlayerController _player;
    private OpenDoorPromptUI _promptUI;

    void Awake()
    {
        _player = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (_player.IsPaused ||
            _player.CurrentState == PlayerActionState.Dead)
        {
            HidePrompt();
            return;
        }

        IInteractable target = FindTarget();

        if (target == null)
        {
            HidePrompt();
            return;
        }

        if (_promptUI == null)
            _promptUI = Game.Instance.UIManager.Get<OpenDoorPromptUI>();

        if (_promptUI != null)
            _promptUI.ShowFor(
                this, target.CanInteract, target.InteractionPrompt);

        if (target.CanInteract && Game.Instance.InputManager.WorldInteractPressedThisFrame)
        {
            target.Interact();
        }
    }

    void OnDisable()
    {
        HidePrompt();
        _promptUI = null;
    }

    /// <summary>Hides this player's cached prompt when its UI object still exists; never creates UI while hiding.</summary>
    private void HidePrompt()
    {
        if (_promptUI != null)
            _promptUI.HideFor(this);
    }

    /// <summary>
    /// Uses the camera, range, and mask to return the first visible
    /// interactable, or null when the ray hits another object.
    /// </summary>
    private IInteractable FindTarget()
    {
        Ray ray = _viewCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(
            ray, out RaycastHit hit, _range,
            _raycastMask, QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<IInteractable>();
    }
}
