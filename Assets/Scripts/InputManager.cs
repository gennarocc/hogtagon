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
    
    // Input events
    public event Action JumpPressed;
    public event Action HornPressed; // New event for horn press
    public event Action MenuToggled;
    public event Action BackPressed;
    public event Action AcceptPressed;
    
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
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        // Optional: Set this object to persist between scenes
        // DontDestroyOnLoad(gameObject);
        
        // Initialize controls
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
        
        // Modified honk handlers
        controls.Gameplay.Honk.performed += ctx => {
            _isHonking = true;
            // Trigger the event only on initial press
            HornPressed?.Invoke();
        };
        controls.Gameplay.Honk.canceled += ctx => _isHonking = false;
        
        controls.Gameplay.Jump.performed += ctx => JumpPressed?.Invoke();
        
        controls.Gameplay.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Look.canceled += ctx => _lookInput = Vector2.zero;
        
        // UI controls
        controls.UI.OpenMenu.performed += ctx => MenuToggled?.Invoke();
        controls.UI.Back.performed += ctx => BackPressed?.Invoke();
        controls.UI.Accept.performed += ctx => AcceptPressed?.Invoke();
        
        // Device change detection
        InputSystem.onDeviceChange += OnDeviceChange;
        
        // Initial device check
        _usingGamepad = Gamepad.current != null && Gamepad.current.enabled;
    }
    
    private void OnEnable()
    {
        controls.Enable();
    }
    
    private void OnDisable()
    {
        controls.Disable();
    }
    
    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
    }
    
    // Handle device changes
    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device is Gamepad)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                _usingGamepad = true;
            }
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                _usingGamepad = false;
            }
        }
    }
    
    // Update check for current input method
    private void Update()
    {
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
    
    // Methods to toggle between input action maps
    public void EnableGameplayControls()
    {
        controls.Gameplay.Enable();
        controls.UI.Disable();
    }
    
    public void EnableUIControls()
    {
        controls.Gameplay.Disable();
        controls.UI.Enable();
    }
}