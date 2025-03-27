using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Unity.Netcode;

/// <summary>
/// Controls the Lobby Settings Panel UI, allowing the host to configure game settings
/// </summary>
public class LobbySettingsPanel : MonoBehaviour
{
    [Header("Button References")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button copyCodeButton;
    
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI gameModeText;
    [SerializeField] private Button gameModeLeftButton;
    [SerializeField] private Button gameModeRightButton;
    [SerializeField] private GameObject teamSettingsPanel;
    [SerializeField] private Slider teamCountSlider;
    [SerializeField] private TextMeshProUGUI teamCountText;
    [SerializeField] private Slider roundCountSlider;
    [SerializeField] private TextMeshProUGUI roundCountText;
    
    [Header("Button Styling")]
    [SerializeField] private Color normalColor = new Color(0, 1, 0, 1); // Bright green
    [SerializeField] private Color hoverColor = new Color(0.7f, 1, 0.7f, 1); // Lighter green
    [SerializeField] private Color pressedColor = new Color(0, 0.7f, 0, 1); // Darker green
    [SerializeField] private float hoverScaleAmount = 1.1f;
    
    [Header("References")]
    [SerializeField] private MenuManager menuManager;
    
    private Vector3 originalStartButtonScale;
    private Vector3 originalCloseButtonScale;
    private Vector3 originalCopyButtonScale;

    private bool isInitialized = false;

    private void Awake()
    {
        // Store original button scales
        if (startGameButton != null)
            originalStartButtonScale = startGameButton.transform.localScale;
        
        if (closeButton != null)
            originalCloseButtonScale = closeButton.transform.localScale;
            
        if (copyCodeButton != null)
            originalCopyButtonScale = copyCodeButton.transform.localScale;
            
        // Find MenuManager if not assigned
        if (menuManager == null)
            menuManager = FindAnyObjectByType<MenuManager>();
            
        Debug.Log("[LobbySettingsPanel] Awake called");
    }
    
    private void OnEnable()
    {
        Debug.Log("[LobbySettingsPanel] OnEnable called with stacktrace:");
        Debug.Log(System.Environment.StackTrace);
        
        // CRITICAL FIX: Never ever disable this panel during OnEnable
        // Instead just configure the buttons based on roles
        
        if (menuManager == null)
        {
            Debug.LogWarning("[LobbySettingsPanel] MenuManager reference is null - trying to find it");
            menuManager = FindAnyObjectByType<MenuManager>();
        }
        
        // Basic initialization to avoid duplicating code
        if (!isInitialized)
        {
            Debug.Log("[LobbySettingsPanel] First initialization");
            isInitialized = true;
            
            // Setup UI and buttons
            SetupButtons();
            UpdateUI();
            
            // Schedule periodic updates
            CancelInvoke("StartPeriodicUpdates");
            Invoke("StartPeriodicUpdates", 0.1f);
        }
        else
        {
            Debug.Log("[LobbySettingsPanel] Panel was already initialized, just refreshing UI");
            // Just refresh the UI for subsequent activations
            UpdateUI();
        }
        
        // Always verify state 
        Invoke("VerifyStillEnabled", 0.2f);
    }
    
    private void OnDisable()
    {
        Debug.Log("[LobbySettingsPanel] OnDisable called");
        
        // Simple cleanup - just cancel coroutines and invokes
        StopAllCoroutines();
        CancelInvoke();
        
        // Important: Don't reset isInitialized here!
        // This avoids the initialization cycle if the panel gets rapidly enabled/disabled
    }
    
    // Method to verify we're still enabled after initialization
    private void VerifyStillEnabled()
    {
        if (this == null || gameObject == null)
            return;
            
        if (!enabled)
        {
            Debug.LogWarning("[LobbySettingsPanel] Panel was disabled after initialization! Re-enabling.");
            enabled = true;
        }
        
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[LobbySettingsPanel] GameObject was deactivated after initialization! Re-activating.");
            gameObject.SetActive(true);
        }
    }
    
    // New method to start the periodic updates after a short delay
    public void StartPeriodicUpdates()
    {
        Debug.Log("[LobbySettingsPanel] StartPeriodicUpdates called");
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(PeriodicUIUpdate());
        }
    }
    
    private IEnumerator PeriodicUIUpdate()
    {
        while (true)
        {
            // Update player count and start button every second
            UpdatePlayerCount();
            UpdateStartButtonInteractability();
            
            yield return new WaitForSeconds(1f);
        }
    }
    
    private void SetupButtons()
    {
        Debug.Log("[LobbySettingsPanel] SetupButtons called");
        
        // CRITICAL: NEVER disable this GameObject!
        
        // Check if we're the host (server)
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool hasNetwork = NetworkManager.Singleton != null;
        
        Debug.Log($"[LobbySettingsPanel] Network status - HasNetwork: {hasNetwork}, IsHost: {isHost}");
        
        // Configure all buttons based on role, but NEVER disable this panel
        
        // SETUP START GAME BUTTON
        if (startGameButton != null)
        {
            // Start game only available to host with at least 2 players
            bool canStartGame = isHost && hasNetwork && NetworkManager.Singleton.ConnectedClients.Count >= 2;
            startGameButton.interactable = canStartGame;
            
            // Always setup the button, just disable interaction if needed
            SetupButtonColors(startGameButton);
            AddHoverHandlers(startGameButton.gameObject, originalStartButtonScale);
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGameClicked);
        }
        
        // SETUP CLOSE BUTTON - Always interactive
        if (closeButton != null)
        {
            closeButton.interactable = true;
            SetupButtonColors(closeButton);
            AddHoverHandlers(closeButton.gameObject, originalCloseButtonScale);
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(OnCloseClicked);
        }
        
        // SETUP COPY CODE BUTTON - Available to everyone in a valid lobby
        if (copyCodeButton != null)
        {
            copyCodeButton.interactable = hasNetwork && ConnectionManager.Instance != null && 
                                      !string.IsNullOrEmpty(ConnectionManager.Instance.joinCode);
            
            SetupButtonColors(copyCodeButton);
            AddHoverHandlers(copyCodeButton.gameObject, originalCopyButtonScale);
            copyCodeButton.onClick.RemoveAllListeners();
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        }
        
        // SETUP GAME MODE BUTTONS - Only available to host
        if (gameModeLeftButton != null)
        {
            gameModeLeftButton.interactable = isHost;
            SetupButtonColors(gameModeLeftButton);
            gameModeLeftButton.onClick.RemoveAllListeners();
            gameModeLeftButton.onClick.AddListener(OnGameModeLeftClicked);
        }
        
        if (gameModeRightButton != null)
        {
            gameModeRightButton.interactable = isHost;
            SetupButtonColors(gameModeRightButton);
            gameModeRightButton.onClick.RemoveAllListeners();
            gameModeRightButton.onClick.AddListener(OnGameModeRightClicked);
        }
        
        // SETUP TEAM COUNT SLIDER - Only available to host in team battle mode
        if (teamCountSlider != null)
        {
            bool isTeamMode = menuManager != null && menuManager.selectedGameMode == MenuManager.GameMode.TeamBattle;
            teamCountSlider.interactable = isHost && isTeamMode;
            
            teamCountSlider.onValueChanged.RemoveAllListeners();
            teamCountSlider.onValueChanged.AddListener(OnTeamCountChanged);
            teamCountSlider.minValue = 2;
            teamCountSlider.maxValue = 4;
            teamCountSlider.wholeNumbers = true;
        }

        // SETUP ROUND COUNT SLIDER - Available to host
        if (roundCountSlider != null)
        {
            roundCountSlider.interactable = isHost;
            
            // Get the valid round counts from MenuManager
            int[] validRoundCounts = new int[] { 1, 3, 5, 7, 9 };
            roundCountSlider.minValue = 0;
            roundCountSlider.maxValue = validRoundCounts.Length - 1;
            roundCountSlider.wholeNumbers = true;
            
            // Find the index of the current round count
            int currentRoundCount = menuManager != null ? menuManager.roundCount : 5;
            int currentIndex = System.Array.IndexOf(validRoundCounts, currentRoundCount);
            if (currentIndex == -1) currentIndex = 2; // Default to middle (5 rounds) if not found
            
            // Set initial value
            roundCountSlider.value = currentIndex;
            
            // Add listener for value changes
            roundCountSlider.onValueChanged.RemoveAllListeners();
            roundCountSlider.onValueChanged.AddListener(OnRoundCountChanged);
            
            // Update text display
            if (roundCountText != null)
            {
                roundCountText.text = $"{validRoundCounts[currentIndex]} ROUNDS";
            }
        }
        
        Debug.Log("[LobbySettingsPanel] Button setup completed successfully");
    }
    
    public void UpdateUI()
    {
        Debug.Log("[LobbySettingsPanel] UpdateUI called");
        
        // Update lobby code
        if (lobbyCodeText != null && ConnectionManager.Instance != null)
        {
            lobbyCodeText.text = "Lobby Code: " + ConnectionManager.Instance.joinCode;
        }
        
        // Update player count
        UpdatePlayerCount();
        
        // Update game mode display based on current game mode
        if (GameManager.Instance != null && menuManager != null)
        {
            // Update game mode text
            if (gameModeText != null)
            {
                gameModeText.text = menuManager.selectedGameMode.ToString();
            }
            
            // Show/hide team settings based on game mode
            if (teamSettingsPanel != null)
            {
                teamSettingsPanel.SetActive(menuManager.selectedGameMode == MenuManager.GameMode.TeamBattle);
            }
            
            // Set team count slider
            if (teamCountSlider != null && teamCountText != null)
            {
                teamCountSlider.value = menuManager.teamCount;
                teamCountText.text = menuManager.teamCount.ToString() + " Teams";
            }
        }
    }
    
    private void UpdatePlayerCount()
    {
        if (playerCountText != null && NetworkManager.Singleton != null)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
            playerCountText.text = "Connected Players: " + playerCount + 
                (playerCount < 2 ? "\n(Need at least 2 players to start)" : "");
        }
    }
    
    private void UpdateStartButtonInteractability()
    {
        if (startGameButton != null && NetworkManager.Singleton != null)
        {
            bool canStart = NetworkManager.Singleton.ConnectedClients.Count >= 2;
            startGameButton.interactable = canStart;
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
    
    // Button Event Handlers
    public void OnStartGameClicked()
    {
        if (menuManager != null && NetworkManager.Singleton != null && 
            NetworkManager.Singleton.IsServer && NetworkManager.Singleton.ConnectedClients.Count >= 2)
        {
            menuManager.ButtonClickAudio();
            StartGameFromLobby();
        }
    }
    
    public void OnCloseClicked()
    {
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
            menuManager.CloseLobbySettingsMenu();
        }
    }
    
    public void OnCopyCodeClicked()
    {
        if (menuManager != null && ConnectionManager.Instance != null && 
            !string.IsNullOrEmpty(ConnectionManager.Instance.joinCode))
        {
            menuManager.ButtonClickAudio();
            GUIUtility.systemCopyBuffer = ConnectionManager.Instance.joinCode;
            
            // Show feedback (could add a temporary text popup)
            Debug.Log("Lobby code copied to clipboard: " + ConnectionManager.Instance.joinCode);
            
            // Flash the text to show it was copied
            StartCoroutine(FlashLobbyCodeText());
        }
    }
    
    private IEnumerator FlashLobbyCodeText()
    {
        if (lobbyCodeText != null)
        {
            Color originalColor = lobbyCodeText.color;
            lobbyCodeText.color = Color.yellow;
            yield return new WaitForSeconds(0.2f);
            lobbyCodeText.color = originalColor;
        }
    }
    
    // Replace the toggle handlers with button click handlers
    public void OnGameModeLeftClicked()
    {
        if (menuManager != null)
        {
            menuManager.OnGameModeDirectionClicked(true);
            UpdateUI();
        }
    }
    
    public void OnGameModeRightClicked()
    {
        if (menuManager != null)
        {
            menuManager.OnGameModeDirectionClicked(false);
            UpdateUI();
        }
    }
    
    public void OnTeamCountChanged(float value)
    {
        if (menuManager != null)
        {
            int teams = Mathf.RoundToInt(value);
            
            // Update UI
            if (teamCountText != null)
                teamCountText.text = teams.ToString() + " Teams";
                
            // Update game settings
            menuManager.SetTeamCount(teams);
        }
    }

    private void OnRoundCountChanged(float value)
    {
        if (menuManager == null) return;
        
        int[] validRoundCounts = new int[] { 1, 3, 5, 7, 9 };
        int index = Mathf.RoundToInt(value);
        int newRoundCount = validRoundCounts[index];
        
        // Update the text display
        if (roundCountText != null)
        {
            roundCountText.text = $"{newRoundCount} ROUNDS";
        }
        
        // Update the game settings if we're the host
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && GameManager.Instance != null)
        {
            GameManager.Instance.SetRoundCount(newRoundCount);
        }
        
        // Play button sound
        if (menuManager != null)
        {
            menuManager.ButtonClickAudio();
        }
    }

    private void StartGameFromLobby()
    {
        menuManager.StartGame();
    }
} 