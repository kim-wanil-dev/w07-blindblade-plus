//------------------------------------------------------------------------------
// Input action wrapper synchronized with Assets/InputSystem_Actions.inputactions.
// The Unity Input System code generator can replace this file when the asset is imported.
//------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// Provides access to the Player input actions defined in the input actions asset.
/// </summary>
public partial class @InputSystem_Actions : IInputActionCollection2, IDisposable
{
    /// <summary>Provides access to the underlying action asset.</summary>
    public InputActionAsset asset { get; }

    private readonly InputActionMap m_Player;
    private readonly InputAction m_Player_Move;
    private readonly InputAction m_Player_Look;
    private readonly InputAction m_Player_Attack;
    private readonly InputAction m_Player_Guard;
    private readonly InputAction m_Player_Dodge;
    private readonly InputAction m_Player_SwordWave;
    private readonly InputAction m_Player_Ultimate;
    private readonly InputAction m_Player_Menu;
    private readonly InputAction m_Player_WorldInteract;
    private readonly List<IPlayerActions> m_PlayerActionsCallbackInterfaces = new List<IPlayerActions>();
    private int m_KeyboardMouseSchemeIndex = -1;
    private int m_GamepadSchemeIndex = -1;

    /// <summary>Loads the action JSON and caches the Player map and its actions.</summary>
    public @InputSystem_Actions()
    {
        asset = InputActionAsset.FromJson(@"{
    ""name"":  ""InputSystem_Actions"",
    ""maps"":  [
                 {
                     ""name"":  ""Player"",
                     ""id"":  ""df70fa95-8a34-4494-b137-73ab6b9c7d37"",
                     ""actions"":  [
                                     {
                                         ""name"":  ""Move"",
                                         ""type"":  ""Value"",
                                         ""id"":  ""351f2ccd-1f9f-44bf-9bec-d62ac5c5f408"",
                                         ""expectedControlType"":  ""Vector2"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  true
                                     },
                                     {
                                         ""name"":  ""Look"",
                                         ""type"":  ""Value"",
                                         ""id"":  ""6b444451-8a00-4d00-a97e-f47457f736a8"",
                                         ""expectedControlType"":  ""Vector2"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  true
                                     },
                                     {
                                         ""name"":  ""Attack"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""6c2ab1b8-8984-453a-af3d-a3c78ae1679a"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""Guard"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""0c973186-3ee1-453d-a6b8-09d8f9816b59"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""Dodge"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""f0181a78-4da8-4a00-9e7b-c9930e17ec68"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""SwordWave"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""51e89738-9d72-4b87-8cb3-e6af32b2f398"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""Ultimate"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""66ecbca7-1b43-4f6d-b0c7-a39be4df1532"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""Menu"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""bd1275c0-05bd-49f8-afcc-d3f05315440e"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     },
                                     {
                                         ""name"":  ""WorldInteract"",
                                         ""type"":  ""Button"",
                                         ""id"":  ""b7a220a2-49c5-4b84-878b-0fb6ced96826"",
                                         ""expectedControlType"":  ""Button"",
                                         ""processors"":  """",
                                         ""interactions"":  """",
                                         ""initialStateCheck"":  false
                                     }
                                 ],
                     ""bindings"":  [
                                      {
                                          ""name"":  """",
                                          ""id"":  ""978bfe49-cc26-4a3d-ab7b-7d7a29327403"",
                                          ""path"":  ""\u003cGamepad\u003e/leftStick"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  ""WASD"",
                                          ""id"":  ""00ca640b-d935-4593-8157-c05846ea39b3"",
                                          ""path"":  ""Dpad"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  """",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  true,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  ""up"",
                                          ""id"":  ""e2062cb9-1b15-46a2-838c-2f8d72a0bdd9"",
                                          ""path"":  ""\u003cKeyboard\u003e/w"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""up"",
                                          ""id"":  ""8180e8bd-4097-4f4e-ab88-4523101a6ce9"",
                                          ""path"":  ""\u003cKeyboard\u003e/upArrow"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""down"",
                                          ""id"":  ""320bffee-a40b-4347-ac70-c210eb8bc73a"",
                                          ""path"":  ""\u003cKeyboard\u003e/s"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""down"",
                                          ""id"":  ""1c5327b5-f71c-4f60-99c7-4e737386f1d1"",
                                          ""path"":  ""\u003cKeyboard\u003e/downArrow"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""left"",
                                          ""id"":  ""d2581a9b-1d11-4566-b27d-b92aff5fabbc"",
                                          ""path"":  ""\u003cKeyboard\u003e/a"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""left"",
                                          ""id"":  ""2e46982e-44cc-431b-9f0b-c11910bf467a"",
                                          ""path"":  ""\u003cKeyboard\u003e/leftArrow"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""right"",
                                          ""id"":  ""fcfe95b8-67b9-4526-84b5-5d0bc98d6400"",
                                          ""path"":  ""\u003cKeyboard\u003e/d"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  ""right"",
                                          ""id"":  ""77bff152-3580-4b21-b6de-dcd0c7e41164"",
                                          ""path"":  ""\u003cKeyboard\u003e/rightArrow"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Move"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  true
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""c1f7a91b-d0fd-4a62-997e-7fb9b69bf235"",
                                          ""path"":  ""\u003cGamepad\u003e/rightStick"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Look"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""8c8e490b-c610-4785-884f-f04217b23ca4"",
                                          ""path"":  ""\u003cPointer\u003e/delta"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Look"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""143bb1cd-cc10-4eca-a2f0-a3664166fe91"",
                                          ""path"":  ""\u003cGamepad\u003e/rightShoulder"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Attack"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""05f6913d-c316-48b2-a6bb-e225f14c7960"",
                                          ""path"":  ""\u003cMouse\u003e/leftButton"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Attack"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""ea775bec-57d7-440a-b4c2-db5855d059d9"",
                                          ""path"":  ""\u003cMouse\u003e/rightButton"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Guard"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""6a5bd829-8996-411a-9409-e78665944798"",
                                          ""path"":  ""\u003cGamepad\u003e/leftShoulder"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Guard"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""d41659d5-59a9-4e1d-9c9a-0c987a65c1f7"",
                                          ""path"":  ""\u003cKeyboard\u003e/space"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Dodge"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""40f9f968-eedd-454d-9b75-155c4fb6786b"",
                                          ""path"":  ""\u003cGamepad\u003e/buttonSouth"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Dodge"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""d86aa064-51f6-4227-82be-fce596c7ac0e"",
                                          ""path"":  ""\u003cKeyboard\u003e/leftShift"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""SwordWave"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""cbb6fceb-2859-49ec-87ea-85274c083726"",
                                          ""path"":  ""\u003cKeyboard\u003e/rightShift"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""SwordWave"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""7e402161-ecf8-4399-a858-730dc6696328"",
                                          ""path"":  ""\u003cGamepad\u003e/rightTrigger"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""SwordWave"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""26923f5d-8d25-4665-952e-58c7fa601af9"",
                                          ""path"":  ""\u003cKeyboard\u003e/q"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Ultimate"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""fb1cc472-1ff6-4734-a06e-a6f3641cb8de"",
                                          ""path"":  ""\u003cGamepad\u003e/buttonNorth"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Ultimate"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""c981c38b-9902-45a6-9604-fdd3e0978cc7"",
                                          ""path"":  ""\u003cKeyboard\u003e/escape"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""Menu"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""6da1ab5d-79dc-4386-83c7-ad0434c07b6a"",
                                          ""path"":  ""\u003cGamepad\u003e/startButton"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""Menu"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""bfd7a449-4f39-4d98-a57d-12c11403070b"",
                                          ""path"":  ""\u003cKeyboard\u003e/f"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Keyboard\u0026Mouse"",
                                          ""action"":  ""WorldInteract"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      },
                                      {
                                          ""name"":  """",
                                          ""id"":  ""13bbeea8-e87d-49db-9de8-4d62453644e6"",
                                          ""path"":  ""\u003cGamepad\u003e/buttonWest"",
                                          ""interactions"":  """",
                                          ""processors"":  """",
                                          ""groups"":  ""Gamepad"",
                                          ""action"":  ""WorldInteract"",
                                          ""isComposite"":  false,
                                          ""isPartOfComposite"":  false
                                      }
                                  ]
                 }
             ],
    ""controlSchemes"":  [
                           {
                               ""name"":  ""Keyboard\u0026Mouse"",
                               ""bindingGroup"":  ""Keyboard\u0026Mouse"",
                               ""devices"":  [
                                               {
                                                   ""devicePath"":  ""\u003cKeyboard\u003e"",
                                                   ""isOptional"":  false,
                                                   ""isOR"":  false
                                               },
                                               {
                                                   ""devicePath"":  ""\u003cMouse\u003e"",
                                                   ""isOptional"":  false,
                                                   ""isOR"":  false
                                               }
                                           ]
                           },
                           {
                               ""name"":  ""Gamepad"",
                               ""bindingGroup"":  ""Gamepad"",
                               ""devices"":  [
                                               {
                                                   ""devicePath"":  ""\u003cGamepad\u003e"",
                                                   ""isOptional"":  false,
                                                   ""isOR"":  false
                                               }
                                           ]
                           }
                       ]
}");
        m_Player = asset.FindActionMap("Player", throwIfNotFound: true);
        m_Player_Move = m_Player.FindAction("Move", throwIfNotFound: true);
        m_Player_Look = m_Player.FindAction("Look", throwIfNotFound: true);
        m_Player_Attack = m_Player.FindAction("Attack", throwIfNotFound: true);
        m_Player_Guard = m_Player.FindAction("Guard", throwIfNotFound: true);
        m_Player_Dodge = m_Player.FindAction("Dodge", throwIfNotFound: true);
        m_Player_SwordWave = m_Player.FindAction("SwordWave", throwIfNotFound: true);
        m_Player_Ultimate = m_Player.FindAction("Ultimate", throwIfNotFound: true);
        m_Player_Menu = m_Player.FindAction("Menu", throwIfNotFound: true);
        m_Player_WorldInteract = m_Player.FindAction("WorldInteract", throwIfNotFound: true);
    }

    /// <summary>Reports an enabled action map that was not disabled before wrapper collection.</summary>
    ~@InputSystem_Actions()
    {
        UnityEngine.Debug.Assert(!m_Player.enabled, "This will cause a leak and performance issues, InputSystem_Actions.Player.Disable() has not been called.");
    }

    /// <summary>Destroys the underlying action asset.</summary>
    public void Dispose()
    {
        UnityEngine.Object.Destroy(asset);
    }

    /// <summary>Gets or sets the optional binding mask for all actions.</summary>
    public InputBinding? bindingMask
    {
        get => asset.bindingMask;
        set => asset.bindingMask = value;
    }

    /// <summary>Gets or sets the devices used to resolve action bindings.</summary>
    public ReadOnlyArray<InputDevice>? devices
    {
        get => asset.devices;
        set => asset.devices = value;
    }

    /// <summary>Returns the control schemes defined by the asset.</summary>
    public ReadOnlyArray<InputControlScheme> controlSchemes => asset.controlSchemes;

    /// <summary>Returns whether action belongs to the underlying asset.</summary>
    public bool Contains(InputAction action) => asset.Contains(action);

    /// <summary>Enumerates actions in the underlying asset.</summary>
    public IEnumerator<InputAction> GetEnumerator() => asset.GetEnumerator();

    /// <summary>Enumerates actions through the non-generic collection interface.</summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enables every action in the underlying asset.</summary>
    public void Enable() => asset.Enable();

    /// <summary>Disables every action in the underlying asset.</summary>
    public void Disable() => asset.Disable();

    /// <summary>Returns all bindings in the underlying asset.</summary>
    public IEnumerable<InputBinding> bindings => asset.bindings;

    /// <summary>Finds an action by name or ID.</summary>
    public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false)
        => asset.FindAction(actionNameOrId, throwIfNotFound);

    /// <summary>Finds the first binding matching mask and returns its action.</summary>
    public int FindBinding(InputBinding mask, out InputAction action)
        => asset.FindBinding(mask, out action);

    /// <summary>Returns the Keyboard&Mouse control scheme.</summary>
    public InputControlScheme KeyboardMouseScheme
    {
        get
        {
            if (m_KeyboardMouseSchemeIndex == -1)
                m_KeyboardMouseSchemeIndex = asset.FindControlSchemeIndex("Keyboard&Mouse");
            return asset.controlSchemes[m_KeyboardMouseSchemeIndex];
        }
    }

    /// <summary>Returns the Gamepad control scheme.</summary>
    public InputControlScheme GamepadScheme
    {
        get
        {
            if (m_GamepadSchemeIndex == -1)
                m_GamepadSchemeIndex = asset.FindControlSchemeIndex("Gamepad");
            return asset.controlSchemes[m_GamepadSchemeIndex];
        }
    }

    /// <summary>Returns the generated-access wrapper for the Player action map.</summary>
    public PlayerActions @Player => new PlayerActions(this);

    /// <summary>Provides typed access and callback registration for the Player action map.</summary>
    public struct PlayerActions
    {
        private @InputSystem_Actions m_Wrapper;

        /// <summary>Creates the Player map accessor for wrapper.</summary>
        public PlayerActions(@InputSystem_Actions wrapper) { m_Wrapper = wrapper; }

        public InputAction @Move => m_Wrapper.m_Player_Move;
        public InputAction @Look => m_Wrapper.m_Player_Look;
        public InputAction @Attack => m_Wrapper.m_Player_Attack;
        public InputAction @Guard => m_Wrapper.m_Player_Guard;
        public InputAction @Dodge => m_Wrapper.m_Player_Dodge;
        public InputAction @SwordWave => m_Wrapper.m_Player_SwordWave;
        public InputAction @Ultimate => m_Wrapper.m_Player_Ultimate;
        public InputAction @Menu => m_Wrapper.m_Player_Menu;
        public InputAction @WorldInteract => m_Wrapper.m_Player_WorldInteract;

        /// <summary>Returns the underlying Player action map.</summary>
        public InputActionMap Get() => m_Wrapper.m_Player;

        /// <summary>Enables the Player action map.</summary>
        public void Enable() => Get().Enable();

        /// <summary>Disables the Player action map.</summary>
        public void Disable() => Get().Disable();

        /// <summary>Returns whether the Player action map is enabled.</summary>
        public bool enabled => Get().enabled;

        /// <summary>Converts the accessor to its underlying action map.</summary>
        public static implicit operator InputActionMap(PlayerActions set) => set.Get();

        /// <summary>Registers instance once and subscribes it to all Player action callbacks.</summary>
        public void AddCallbacks(IPlayerActions instance)
        {
            if (instance == null || m_Wrapper.m_PlayerActionsCallbackInterfaces.Contains(instance)) return;
            m_Wrapper.m_PlayerActionsCallbackInterfaces.Add(instance);
            @Move.started += instance.OnMove;
            @Move.performed += instance.OnMove;
            @Move.canceled += instance.OnMove;
            @Look.started += instance.OnLook;
            @Look.performed += instance.OnLook;
            @Look.canceled += instance.OnLook;
            @Attack.started += instance.OnAttack;
            @Attack.performed += instance.OnAttack;
            @Attack.canceled += instance.OnAttack;
            @Guard.started += instance.OnGuard;
            @Guard.performed += instance.OnGuard;
            @Guard.canceled += instance.OnGuard;
            @Dodge.started += instance.OnDodge;
            @Dodge.performed += instance.OnDodge;
            @Dodge.canceled += instance.OnDodge;
            @SwordWave.started += instance.OnSwordWave;
            @SwordWave.performed += instance.OnSwordWave;
            @SwordWave.canceled += instance.OnSwordWave;
            @Ultimate.started += instance.OnUltimate;
            @Ultimate.performed += instance.OnUltimate;
            @Ultimate.canceled += instance.OnUltimate;
            @Menu.started += instance.OnMenu;
            @Menu.performed += instance.OnMenu;
            @Menu.canceled += instance.OnMenu;
            @WorldInteract.started += instance.OnWorldInteract;
            @WorldInteract.performed += instance.OnWorldInteract;
            @WorldInteract.canceled += instance.OnWorldInteract;
        }

        /// <summary>Unsubscribes instance from all Player action callbacks.</summary>
        private void UnregisterCallbacks(IPlayerActions instance)
        {
            @Move.started -= instance.OnMove;
            @Move.performed -= instance.OnMove;
            @Move.canceled -= instance.OnMove;
            @Look.started -= instance.OnLook;
            @Look.performed -= instance.OnLook;
            @Look.canceled -= instance.OnLook;
            @Attack.started -= instance.OnAttack;
            @Attack.performed -= instance.OnAttack;
            @Attack.canceled -= instance.OnAttack;
            @Guard.started -= instance.OnGuard;
            @Guard.performed -= instance.OnGuard;
            @Guard.canceled -= instance.OnGuard;
            @Dodge.started -= instance.OnDodge;
            @Dodge.performed -= instance.OnDodge;
            @Dodge.canceled -= instance.OnDodge;
            @SwordWave.started -= instance.OnSwordWave;
            @SwordWave.performed -= instance.OnSwordWave;
            @SwordWave.canceled -= instance.OnSwordWave;
            @Ultimate.started -= instance.OnUltimate;
            @Ultimate.performed -= instance.OnUltimate;
            @Ultimate.canceled -= instance.OnUltimate;
            @Menu.started -= instance.OnMenu;
            @Menu.performed -= instance.OnMenu;
            @Menu.canceled -= instance.OnMenu;
            @WorldInteract.started -= instance.OnWorldInteract;
            @WorldInteract.performed -= instance.OnWorldInteract;
            @WorldInteract.canceled -= instance.OnWorldInteract;
        }

        /// <summary>Unregisters instance when it was previously added.</summary>
        public void RemoveCallbacks(IPlayerActions instance)
        {
            if (m_Wrapper.m_PlayerActionsCallbackInterfaces.Remove(instance))
                UnregisterCallbacks(instance);
        }

        /// <summary>Replaces registered callback instances with instance.</summary>
        public void SetCallbacks(IPlayerActions instance)
        {
            foreach (var item in m_Wrapper.m_PlayerActionsCallbackInterfaces)
                UnregisterCallbacks(item);
            m_Wrapper.m_PlayerActionsCallbackInterfaces.Clear();
            AddCallbacks(instance);
        }
    }

    /// <summary>Receives Player action lifecycle callbacks.</summary>
    public interface IPlayerActions
    {
        void OnMove(InputAction.CallbackContext context);
        void OnLook(InputAction.CallbackContext context);
        void OnAttack(InputAction.CallbackContext context);
        void OnGuard(InputAction.CallbackContext context);
        void OnDodge(InputAction.CallbackContext context);
        void OnSwordWave(InputAction.CallbackContext context);
        void OnUltimate(InputAction.CallbackContext context);
        void OnMenu(InputAction.CallbackContext context);
        void OnWorldInteract(InputAction.CallbackContext context);
    }
}
