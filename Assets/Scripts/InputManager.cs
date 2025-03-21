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
    private bool _controllerNavigationEnabled = true;

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
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InitializeControls();
    }

    private void InitializeControls()
    {
        controls = new DefaultControls();

        // Gameplay controls
        controls.Gameplay.Throttle.performed += ctx => _throttleInput = ctx.ReadValue<float>();
        controls.Gameplay.Throttle.canceled += ctx => _throttleInput = 0f;

        controls.Gameplay.Brake.performed += ctx => _brakeInput = ctx.ReadValue<float>();
        controls.Gameplay.Brake.canceled += ctx => _brakeInput = 0f;

        // Jump action
        controls.Gameplay.Jump.performed += ctx => JumpPressed?.Invoke();

        // Honk handlers
        controls.Gameplay.Honk.performed += ctx =>
        {
            _isHonking = true;
            HornPressed?.Invoke();
        };
        controls.Gameplay.Honk.canceled += ctx => _isHonking = false;

        // Look input
        controls.Gameplay.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Look.canceled += ctx => _lookInput = Vector2.zero;

        // ShowScoreboard
        controls.Gameplay.ShowScoreboard.performed += ctx => ScoreboardToggled?.Invoke(true);
        controls.Gameplay.ShowScoreboard.canceled += ctx => ScoreboardToggled?.Invoke(false);

        // UI controls
        controls.UI.OpenMenu.performed += ctx => MenuToggled?.Invoke();
        controls.UI.Back.performed += ctx => BackPressed?.Invoke();
        controls.UI.Accept.performed += ctx => AcceptPressed?.Invoke();

        // Initial device check
        _usingGamepad = Gamepad.current != null && Gamepad.current.enabled;
    }

    private void OnEnable()
    {
        // Start with UI mode for main menu
        SwitchToUIMode();
        
        // Force controller navigation to be enabled by default
        _controllerNavigationEnabled = true;
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

        // First disable everything, then enable gameplay
        controls.UI.Disable();
        controls.Gameplay.Disable();
        controls.Gameplay.Enable();

        _currentInputState = InputState.Gameplay;
    }

    // Switch to UI controls
    public void SwitchToUIMode()
    {
        if (_currentInputState == InputState.UI)
            return;

        // First disable everything, then enable UI
        controls.UI.Disable();
        controls.Gameplay.Disable();
        controls.UI.Enable();

        _currentInputState = InputState.UI;

        // Reset input values when switching to UI
        _throttleInput = 0f;
        _brakeInput = 0f;
        _lookInput = Vector2.zero;
        _isHonking = false;
    }

    // Update to check for Escape key during gameplay and handle device detection
    private void Update()
    {
        // Monitor Escape key during gameplay to pause
        if (_currentInputState == InputState.Gameplay && Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                MenuToggled?.Invoke();
            }
        }

        // Only process device detection and controller input if navigation is enabled
        if (_controllerNavigationEnabled)
        {
            DetectInputDevice();
            
            // If we detect gamepad input in UI mode, make sure to handle it
            if (_currentInputState == InputState.UI && _usingGamepad)
            {
                // Let other systems know we're using a gamepad
                // This signal can be used by UI elements to adjust their behavior
            }
        }
        else
        {
            // When navigation is disabled (for text input), ensure we don't process 
            // small stick drift as navigation input
            _lookInput = Vector2.zero;
        }
    }

    // Device detection
    private void DetectInputDevice()
    {
        // Check if we're using gamepad based on last input
        if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame
            && Gamepad.current.CheckStateIsAtDefault() == false)
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
        return controls.Gameplay.enabled || controls.UI.enabled;
    }

    public void ForceEnableCurrentActionMap()
    {
        if (controls == null)
        {
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
    }
    public void SetControllerNavigationEnabled(bool enabled)
    {
        _controllerNavigationEnabled = enabled;
        
        // If we're re-enabling controller navigation, reset values
        if (enabled)
        {
            // Clear any accumulated input
            _lookInput = Vector2.zero;
            _throttleInput = 0f;
            _brakeInput = 0f;
        }
    }
}