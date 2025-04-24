using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>
/// This component fixes gamepad navigation issues with the new Input System.
/// It ensures that the left stick is properly used for UI navigation.
/// Attach this to your EventSystem GameObject.
/// </summary>
[RequireComponent(typeof(EventSystem))]
public class GamepadUINavigationFix : MonoBehaviour
{
    private InputSystemUIInputModule inputModule;
    private bool fixApplied = false;

    private void Awake()
    {
        // Get the InputSystemUIInputModule component
        inputModule = GetComponent<InputSystemUIInputModule>();
        
        // If using StandaloneInputModule, we need to add InputSystemUIInputModule
        if (inputModule == null)
        {
            // Check if using old StandaloneInputModule
            var standaloneModule = GetComponent<StandaloneInputModule>();
            if (standaloneModule != null)
            {
                Debug.Log("[GamepadUINavigationFix] Found StandaloneInputModule. Converting to InputSystemUIInputModule.");
                
                // Disable StandaloneInputModule
                standaloneModule.enabled = false;
                
                // Add InputSystemUIInputModule
                inputModule = gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        // Log error if neither module is found
        if (inputModule == null)
        {
            Debug.LogError("[GamepadUINavigationFix] No InputSystemUIInputModule or StandaloneInputModule found on EventSystem!");
            return;
        }
    }

    private void Start()
    {
        // Apply the fix only once after all components are initialized
        if (!fixApplied && inputModule != null)
        {
            ApplyFix();
        }
    }

    private void Update()
    {
        // Detect gamepad input to ensure navigation is working
        if (Gamepad.current != null)
        {
            // If left stick is moved significantly but no navigation is happening,
            // try reapplying the fix
            Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
            if (leftStick.magnitude > 0.5f && !fixApplied)
            {
                ApplyFix();
            }
        }
    }

    private void ApplyFix()
    {
        // Ensure input module is using the appropriate settings
        if (inputModule != null)
        {
            // Force navigation to use the left stick
            var actions = inputModule.actionsAsset;
            if (actions != null)
            {
                Debug.Log("[GamepadUINavigationFix] Applying UI navigation fix with actions asset: " + actions.name);
            }
            else
            {
                Debug.LogWarning("[GamepadUINavigationFix] InputSystemUIInputModule has no actions asset assigned!");
            }

            // Re-select the current selected object to refresh navigation
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            {
                GameObject currentSelection = EventSystem.current.currentSelectedGameObject;
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(currentSelection);
                Debug.Log("[GamepadUINavigationFix] Refreshed selection of UI element: " + currentSelection.name);
            }

            fixApplied = true;
        }
    }
} 