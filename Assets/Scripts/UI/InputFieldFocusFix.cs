using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// Add this component to TMP_InputField objects to improve focus retention
/// and fix the cursor disappearing issue when moving the mouse within the field.
/// </summary>
public class InputFieldFocusFix : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private TMP_InputField inputField;
    private bool pointerInsideField = false;
    private bool wasFocused = false;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        if (inputField == null)
        {
            Debug.LogError("InputFieldFocusFix must be attached to a GameObject with a TMP_InputField component!");
            enabled = false;
        }
    }

    private void Update()
    {
        // Check if we clicked inside the field using the new Input System
        if (pointerInsideField && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Register that we've clicked inside
            wasFocused = true;
        }

        // Handle focus restoration if needed
        if (wasFocused && !inputField.isFocused && pointerInsideField)
        {
            // The field should be focused but isn't - restore focus
            inputField.ActivateInputField();
            inputField.Select();

            // Position cursor at end of text when re-focusing
            inputField.caretPosition = inputField.text != null ? inputField.text.Length : 0;
            inputField.ForceLabelUpdate();
        }

        // Update our tracking of whether field is focused
        wasFocused = inputField.isFocused;
    }

    // Called when the pointer enters the input field area
    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInsideField = true;
    }

    // Called when the pointer exits the input field area
    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInsideField = false;
    }

    // Called when the pointer clicks on the input field
    public void OnPointerClick(PointerEventData eventData)
    {
        // Ensure the field is focused when clicked
        inputField.ActivateInputField();
        inputField.Select();
        wasFocused = true;
    }
}