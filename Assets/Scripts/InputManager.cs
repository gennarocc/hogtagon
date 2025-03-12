// HOG INPUT MANAGER
// This script handles all input processing and provides clean access to inputs for other components

using UnityEngine;
using UnityEngine.InputSystem;
using System;

[DefaultExecutionOrder(-100)] // Ensure this script runs before others
public class InputManager : MonoBehaviour
{
    // Singleton pattern for easy access
    public static InputManager Instance { get; private set; }

    // Reference to the Input Actions asset
    private DefaultControls controls;

    // Input values
    private float _throttleInput;
    private float _brakeInput;
    private Vector2 _lookInput;
    private bool _isHonking;
    private bool _isJumping;

    // Current action map
    private enum InputState { Gameplay, UI }
    private InputState _currentInputState = InputState.UI; // Start in UI mode for main menu

    // Input events
    public event Action JumpPressed;
    public event Action HornPressed;
    public event Action MenuToggled;
    public event Action BackPressed;
    public event Action AcceptPressed;
    public event Action<bool> ScoreboardToggled;

    // Device state
    private bool _usingGamepad = false;

    // Public accessors
    public float ThrottleInput => _throttleInput;
    public float BrakeInput => _brakeInput;
    public Vector2 LookInput => _lookInput;
    public bool IsHonking => _isHonking;
    public bool IsUsingGamepad => _usingGamepad;

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple InputManager instances found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Debug.Log("InputManager Awake - setting singleton instance");
        Instance = this;

        // Initialize controls
        InitializeControls();
    }

    private void InitializeControls()
    {
        Debug.Log("InputManager InitializeControls");
        controls = new DefaultControls();

        // Gameplay controls - exactly matching your Input Actions Asset
        controls.Gameplay.Throttle.performed += ctx =>
        {
            _throttleInput = ctx.ReadValue<float>();
            Debug.Log($"Throttle input: {_throttleInput}");
        };
        controls.Gameplay.Throttle.canceled += ctx => _throttleInput = 0f;

        controls.Gameplay.Brake.performed += ctx =>
        {
            _brakeInput = ctx.ReadValue<float>();
            Debug.Log($"Brake input: {_brakeInput}");
        };
        controls.Gameplay.Brake.canceled += ctx => _brakeInput = 0f;

        // Jump action
        controls.Gameplay.Jump.performed += ctx =>
        {
            Debug.Log("Jump pressed");
            JumpPressed?.Invoke();
        };

        // Honk handlers
        controls.Gameplay.Honk.performed += ctx =>
        {
            _isHonking = true;
            Debug.Log("Honk pressed");
            HornPressed?.Invoke();
        };
        controls.Gameplay.Honk.canceled += ctx => _isHonking = false;

        // Look input
        controls.Gameplay.Look.performed += ctx =>
        {
            _lookInput = ctx.ReadValue<Vector2>();
            // Don't log look input as it would spam the console
        };
        controls.Gameplay.Look.canceled += ctx => _lookInput = Vector2.zero;

        // ShowScoreboard
        controls.Gameplay.ShowScoreboard.performed += ctx =>
        {
            Debug.Log("Scoreboard button pressed");
            ScoreboardToggled?.Invoke(true);
        };
        controls.Gameplay.ShowScoreboard.canceled += ctx =>
        {
            Debug.Log("Scoreboard button released");
            ScoreboardToggled?.Invoke(false);
        };

        // UI controls
        controls.UI.OpenMenu.performed += ctx =>
        {
            Debug.Log("Menu button pressed via UI map");
            MenuToggled?.Invoke();
        };

        controls.UI.Back.performed += ctx =>
        {
            Debug.Log("Back button pressed");
            BackPressed?.Invoke();
        };

        controls.UI.Accept.performed += ctx =>
        {
            Debug.Log("Accept button pressed");
            AcceptPressed?.Invoke();
        };


        // Initial device check
        _usingGamepad = Gamepad.current != null && Gamepad.current.enabled;
    }

    private void OnEnable()
    {
        Debug.Log("InputManager OnEnable - enabling initial action map");
        // Start with UI mode for main menu
        SwitchToUIMode();
    }

    private void OnDisable()
    {
        controls.Disable();
    }

 
    // Switch to gameplay controls
    public void SwitchToGameplayMode()
    {
        if (_currentInputState == InputState.Gameplay)
            return;

        Debug.Log("Switching to GAMEPLAY input mode");

        // First disable everything
        controls.UI.Disable();
        controls.Gameplay.Disable();

        // Then enable gameplay
        controls.Gameplay.Enable();

        _currentInputState = InputState.Gameplay;

        Debug.Log($"Action maps after switching to gameplay: UI={controls.UI.enabled}, Gameplay={controls.Gameplay.enabled}");
    }

    // Switch to UI controls
    public void SwitchToUIMode()
    {
        if (_currentInputState == InputState.UI)
            return;

        Debug.Log("Switching to UI input mode");

        // First disable everything
        controls.UI.Disable();
        controls.Gameplay.Disable();

        // Then enable UI
        controls.UI.Enable();

        _currentInputState = InputState.UI;

        // Reset input values when switching to UI
        _throttleInput = 0f;
        _brakeInput = 0f;
        _lookInput = Vector2.zero;
        _isHonking = false;

        Debug.Log($"Action maps after switching to UI: UI={controls.UI.enabled}, Gameplay={controls.Gameplay.enabled}");
    }

    // Update to check for Escape key during gameplay
    private void Update()
    {
        // Monitor Escape key during gameplay to pause
        if (_currentInputState == InputState.Gameplay && Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Debug.Log("Escape key pressed during gameplay - toggling menu");
                MenuToggled?.Invoke();
            }
        }

        // Check if we're using gamepad based on last input
        if (Gamepad.current != null && (Gamepad.current.wasUpdatedThisFrame
            && Gamepad.current.CheckStateIsAtDefault() == false))
        {
            _usingGamepad = true;
        }

        // Check if we're using mouse/keyboard
        if ((Mouse.current != null && Mouse.current.delta.ReadValue().sqrMagnitude > 0)
            || (Keyboard.current != null && Keyboard.current.wasUpdatedThisFrame))
        {
            _usingGamepad = false;
        }
    }

    // Helper method to check current input mode
    public bool IsInGameplayMode()
    {
        return _currentInputState == InputState.Gameplay;
    }

    // Helper method to check if input actions are enabled
    public bool AreInputActionsEnabled()
    {
        if (controls == null) return false;

        bool gameplayEnabled = controls.Gameplay.enabled;
        bool uiEnabled = controls.UI.enabled;

        Debug.Log($"Input actions status: Gameplay={gameplayEnabled}, UI={uiEnabled}");

        return gameplayEnabled || uiEnabled; // At least one should be enabled
    }
    public void ForceEnableCurrentActionMap()
    {
        Debug.Log($"Force-enabling action map for state: {_currentInputState}");

        if (controls == null)
        {
            Debug.LogError("Controls are null in ForceEnableCurrentActionMap!");
            controls = new DefaultControls();
            InitializeControls();
        }

        if (_currentInputState == InputState.Gameplay)
        {
            controls.UI.Disable();
            controls.Gameplay.Enable();
        }
        else
        {
            controls.Gameplay.Disable();
            controls.UI.Enable();
        }

        Debug.Log($"After force-enable: UI={controls.UI.enabled}, Gameplay={controls.Gameplay.enabled}");
    }
}