using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Controls the PlayMenuPanel UI, handling button styling and navigation
/// </summary>
public class PlayMenuPanel : MonoBehaviour
{
    [Header("Button References")]
    [SerializeField] private Button startLobbyButton;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_InputField joinCodeField;
    
    [Header("Button Styling")]
    [SerializeField] private Color normalColor = new Color(0, 1, 0, 1); // Bright green
    [SerializeField] private Color hoverColor = new Color(0.7f, 1, 0.7f, 1); // Lighter green
    [SerializeField] private Color pressedColor = new Color(0, 0.7f, 0, 1); // Darker green
    [SerializeField] private float hoverScaleAmount = 1.1f;
    
    [Header("References")]
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private PlayMenuCameraController cameraController;
    
    private Vector3 originalStartButtonScale;
    private Vector3 originalJoinButtonScale;
    private Vector3 originalBackButtonScale;

    private void Awake()
    {
        // Store original button scales
        if (startLobbyButton != null)
            originalStartButtonScale = startLobbyButton.transform.localScale;
        
        if (joinLobbyButton != null)
            originalJoinButtonScale = joinLobbyButton.transform.localScale;
        
        if (backButton != null)
            originalBackButtonScale = backButton.transform.localScale;
            
        // Find MenuManager if not assigned
        if (menuManager == null)
            menuManager = FindObjectOfType<MenuManager>();
            
        // Find PlayMenuCameraController if not assigned
        if (cameraController == null)
            cameraController = GetComponentInChildren<PlayMenuCameraController>();
    }
    
    private void OnEnable()
    {
        SetupButtons();
        
        // Ensure camera controller is active
        if (cameraController != null)
        {
            cameraController.enabled = true;
        }
    }
    
    private void OnDisable()
    {
        // Ensure play menu camera is disabled when panel is hidden
        if (cameraController != null)
        {
            cameraController.enabled = false;
        }
    }
    
    private void SetupButtons()
    {
        // Setup Start Lobby button
        if (startLobbyButton != null)
        {
            SetupButtonColors(startLobbyButton);
            
            // Add hover event listeners
            AddHoverHandlers(startLobbyButton.gameObject, originalStartButtonScale);
        }
        
        // Setup Join Lobby button
        if (joinLobbyButton != null)
        {
            SetupButtonColors(joinLobbyButton);
            
            // Add hover event listeners
            AddHoverHandlers(joinLobbyButton.gameObject, originalJoinButtonScale);
        }
        
        // Setup Back button
        if (backButton != null)
        {
            SetupButtonColors(backButton);
            
            // Add hover event listeners
            AddHoverHandlers(backButton.gameObject, originalBackButtonScale);
            
            // Add back button functionality
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButtonClicked);
        }
    }
    
    private void SetupButtonColors(Button button)
    {
        ColorBlock colors = button.colors;
        colors.normalColor = normalColor;
        colors.highlightedColor = hoverColor;
        colors.pressedColor = pressedColor;
        colors.selectedColor = hoverColor;
        button.colors = colors;
    }
    
    private void AddHoverHandlers(GameObject buttonObj, Vector3 originalScale)
    {
        // Add hover handlers using EventTrigger component
        EventTrigger trigger = buttonObj.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = buttonObj.AddComponent<EventTrigger>();
            
        // Clear existing entries
        trigger.triggers.Clear();
        
        // Add pointer enter event (hover)
        EventTrigger.Entry enterEntry = new EventTrigger.Entry();
        enterEntry.eventID = EventTriggerType.PointerEnter;
        enterEntry.callback.AddListener((data) => {
            buttonObj.transform.localScale = originalScale * hoverScaleAmount;
        });
        trigger.triggers.Add(enterEntry);
        
        // Add pointer exit event (exit hover)
        EventTrigger.Entry exitEntry = new EventTrigger.Entry();
        exitEntry.eventID = EventTriggerType.PointerExit;
        exitEntry.callback.AddListener((data) => {
            buttonObj.transform.localScale = originalScale;
        });
        trigger.triggers.Add(exitEntry);
    }
    
    public void OnBackButtonClicked()
    {
        if (menuManager != null)
        {
            // Camera controller will handle lowering its own priority in OnDisable
            menuManager.ShowMainMenu();
        }
        else
        {
            Debug.LogWarning("MenuManager reference is missing. Cannot navigate back to main menu.");
        }
    }
} 