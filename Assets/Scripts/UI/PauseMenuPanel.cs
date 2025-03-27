using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Controls the PauseMenuPanel UI, handling button styling and functionality
/// </summary>
public class PauseMenuPanel : MonoBehaviour
{
    [Header("Button References")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button lobbySettingsButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button quitButton;
    
    [Header("Button Styling")]
    [SerializeField] private Color normalColor = new Color(0, 1, 0, 1); // Bright green
    [SerializeField] private Color hoverColor = new Color(0.7f, 1, 0.7f, 1); // Lighter green
    [SerializeField] private Color pressedColor = new Color(0, 0.7f, 0, 1); // Darker green
    [SerializeField] private float hoverScaleAmount = 1.1f;
    
    [Header("References")]
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private TextMeshProUGUI pausedText;
    [SerializeField] private TextMeshProUGUI joinCodeText;
    
    private Vector3 originalResumeButtonScale;
    private Vector3 originalSettingsButtonScale;
    private Vector3 originalLobbySettingsButtonScale;
    private Vector3 originalDisconnectButtonScale;
    private Vector3 originalQuitButtonScale;

    public static bool CanOpenPauseMenu()
    {
        // Check if the player is in a valid state to open the pause menu
        if (MenuManager.Instance == null)
        {
            Debug.LogWarning("Cannot open pause menu: MenuManager.Instance is null");
            return false;
        }
        
        // Temporarily DISABLE the check for other active menus to help diagnose the issue
        // bool noOtherMenusActive = !MenuManager.Instance.IsAnyMenuActive();
        bool noOtherMenusActive = true; // Force to true to bypass this check
        
        // Simplify the check for valid game state
        // We'll assume we're in a valid state if any instance exists to help diagnose
        bool validGameState = (GameManager.Instance != null);
        
        if (!validGameState)
        {
            Debug.LogWarning("Cannot open pause menu: GameManager.Instance is null");
        }
        
        bool canOpen = noOtherMenusActive && validGameState;
        Debug.Log("Pause menu can be opened: " + canOpen);
        
        return canOpen;
    }

    private void Awake()
    {
        // Store original button scales
        if (resumeButton != null)
            originalResumeButtonScale = resumeButton.transform.localScale;
        
        if (settingsButton != null)
            originalSettingsButtonScale = settingsButton.transform.localScale;
        
        if (lobbySettingsButton != null)
            originalLobbySettingsButtonScale = lobbySettingsButton.transform.localScale;
            
        if (disconnectButton != null)
            originalDisconnectButtonScale = disconnectButton.transform.localScale;
            
        if (quitButton != null)
            originalQuitButtonScale = quitButton.transform.localScale;
            
        // Find MenuManager if not assigned
        if (menuManager == null)
            menuManager = MenuManager.Instance;
    }
    
    private void OnEnable()
    {
        SetupButtons();
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        // Display join code if available
        if (joinCodeText != null && ConnectionManager.Instance != null && !string.IsNullOrEmpty(ConnectionManager.Instance.joinCode))
        {
            joinCodeText.text = "CODE: " + ConnectionManager.Instance.joinCode;
        }
        
        // Only show the lobby settings button for the host
        if (lobbySettingsButton != null)
        {
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
            lobbySettingsButton.gameObject.SetActive(isHost);
        }
    }
    
    private void SetupButtons()
    {
        // Setup Resume button
        if (resumeButton != null)
        {
            SetupButtonColors(resumeButton);
            
            // Add hover event listeners
            AddHoverHandlers(resumeButton.gameObject, originalResumeButtonScale);
            
            // Add resume button functionality
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.onClick.AddListener(OnResumeClicked);
        }
        
        // Setup Settings button
        if (settingsButton != null)
        {
            SetupButtonColors(settingsButton);
            
            // Add hover event listeners
            AddHoverHandlers(settingsButton.gameObject, originalSettingsButtonScale);
            
            // Add settings button functionality
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(OnSettingsClicked);
        }
        
        // Setup Lobby Settings button (host only)
        if (lobbySettingsButton != null)
        {
            SetupButtonColors(lobbySettingsButton);
            
            // Add hover event listeners
            AddHoverHandlers(lobbySettingsButton.gameObject, originalLobbySettingsButtonScale);
            
            // IMPORTANT: Remove old listeners and directly connect to our implementation
            lobbySettingsButton.onClick.RemoveAllListeners();
            lobbySettingsButton.onClick.AddListener(OnLobbySettingsClicked);
            Debug.Log("DIRECTLY connected lobby settings button in PauseMenuPanel");
            
            // Only show for host
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            lobbySettingsButton.gameObject.SetActive(isHost);
            Debug.Log($"Lobby Settings Button active: {isHost}, interactable: {lobbySettingsButton.interactable}");
        }
        
        // Setup Disconnect button
        if (disconnectButton != null)
        {
            SetupButtonColors(disconnectButton);
            
            // Add hover event listeners
            AddHoverHandlers(disconnectButton.gameObject, originalDisconnectButtonScale);
            
            // Add disconnect button functionality
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
        }
        
        // Setup Quit button
        if (quitButton != null)
        {
            SetupButtonColors(quitButton);
            
            // Add hover event listeners
            AddHoverHandlers(quitButton.gameObject, originalQuitButtonScale);
            
            // Add quit button functionality
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);
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
    
    // Button event handlers
    public void OnResumeClicked()
    {
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
            menuManager.Resume();
        }
    }
    
    public void OnSettingsClicked()
    {
        Debug.Log("PauseMenuPanel.OnSettingsClicked called");
        
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
            menuManager.Settings();
        }
        else
        {
            Debug.LogError("MenuManager reference is null in PauseMenuPanel");
        }
    }
    
    public void OnLobbySettingsClicked()
    {
        Debug.Log("[PauseMenuPanel] Lobby Settings button clicked");
        
        // Play button sound
        if (menuManager != null)
            menuManager.ButtonClickAudio();
        
        // CRITICAL: Hide the pause menu FIRST
        gameObject.SetActive(false);
        
        // Open the lobby settings menu
        if (menuManager != null)
        {
            menuManager.OpenLobbySettingsMenu();
        }
        else
        {
            Debug.LogError("[PauseMenuPanel] MenuManager reference is missing");
        }
    }
    
    public void OnDisconnectClicked()
    {
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
            menuManager.Disconnect();
        }
    }
    
    public void OnQuitClicked()
    {
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
            menuManager.QuitGame();
        }
    }
} 