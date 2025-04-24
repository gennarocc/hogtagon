// HOG INPUT MANAGER
// This script handles all input processing and provides clean access to inputs for other components

using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections;

[DefaultExecutionOrder(-100)] // Ensure this script runs before others
public class InputManager : MonoBehaviour
{
    // Singleton pattern for easy access
    public static InputManager Instance { get; private set; }

    // Reference to the Input Actions asset
    private DefaultControls controls;

    // Action Maps for direct control
    private InputActionMap playerActions;
    private InputActionMap pauseActions;
    private InputActionMap uiActions;

    // Input values
    private float _throttleInput;
    private float _brakeInput;
    private float _steerInput; // Added for steering
    private Vector2 _lookInput;
    private bool _isHonking;

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

    // Add a cooldown timer to prevent multiple toggles too quickly
    private float menuToggleCooldown = 0.5f;
    private float lastMenuToggleTime = 0f;

    // Public accessors
    public float ThrottleInput => _throttleInput;
    public float BrakeInput => _brakeInput;
    public float SteerInput => _steerInput; // Added for steering
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

        // Store action maps for direct control
        playerActions = controls.Gameplay;
        uiActions = controls.UI;

        // Gameplay controls
        controls.Gameplay.Throttle.performed += ctx => _throttleInput = ctx.ReadValue<float>();
        controls.Gameplay.Throttle.canceled += ctx => _throttleInput = 0f;

        controls.Gameplay.Brake.performed += ctx => _brakeInput = ctx.ReadValue<float>();
        controls.Gameplay.Brake.canceled += ctx => _brakeInput = 0f;

        // Steering controls (added)
        controls.Gameplay.Steer.performed += ctx => _steerInput = ctx.ReadValue<float>();
        controls.Gameplay.Steer.canceled += ctx => _steerInput = 0f;

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
        
        // Pause Menu
        controls.Gameplay.PauseMenu.performed += ctx => MenuToggled?.Invoke();

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
        
        // Disable UI action map and enable gameplay action map
        uiActions.Disable();
        playerActions.Enable();

        _currentInputState = InputState.Gameplay;
        
        // Apply cursor locking
        ForceCursorLock();
    }
    
    // Force the cursor to be locked and hidden
    private void ForceCursorLock()
    {
        // Ensure cursor is locked for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    
    // Coroutine to enforce cursor lock over several frames
    private IEnumerator EnforceCursorLock()
    {
        // Apply locks at various intervals to overcome any overrides
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(0.1f * i);
            
            // Only apply if still in gameplay mode
            if (_currentInputState == InputState.Gameplay)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                // If we switched back to UI mode, exit the coroutine
                yield break;
            }
        }
    }

    // Switch to UI controls
    public void SwitchToUIMode()
    {
        if (_currentInputState == InputState.UI)
            return;

        // Disable gameplay action map and enable UI action map
        playerActions.Disable();
        uiActions.Enable();

        _currentInputState = InputState.UI;

        // Reset input values when switching to UI
        _throttleInput = 0f;
        _brakeInput = 0f;
        _steerInput = 0f; // Reset steering input
        _lookInput = Vector2.zero;
        _isHonking = false;
    }

    // Update to check for Escape key during gameplay and handle device detection
    private void Update()
    {
        // Monitor Escape key during gameplay to pause
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Check if enough time has passed since last toggle
            if (Time.unscaledTime - lastMenuToggleTime > menuToggleCooldown)
            {
                // Always invoke MenuToggled when escape is pressed, regardless of current mode
                MenuToggled?.Invoke();
                
                // Record the time of this toggle
                lastMenuToggleTime = Time.unscaledTime;
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
            _steerInput = 0f; // Reset steering input when navigation is disabled
        }
        
        // If we're in UI mode (menus are open), force look input to zero
        // This will prevent the camera from being controlled while menus are open
        if (_currentInputState == InputState.UI)
        {
            _lookInput = Vector2.zero;
            _steerInput = 0f; // Reset steering input in UI mode
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

    // Check if we're currently in gameplay input mode
    public bool IsInGameplayMode()
    {
        return _currentInputState == InputState.Gameplay;
    }
    
    // Check if we're currently in UI input mode
    public bool IsInUIMode()
    {
        return _currentInputState == InputState.UI;
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
            _steerInput = 0f; // Reset steering input
        }
    }

    // Helper method to check if the pause menu or settings menu is active
    public bool AreMenusActive()
    {
        if (MenuManager.Instance == null) return false;
        return MenuManager.Instance.gameIsPaused;
    }
}