using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using UnityEngine.EventSystems;
using System.Linq;
using System.Collections.Generic;

public class MenuManager : NetworkBehaviour
{
    public static MenuManager instance;
    public bool gameIsPaused = false;

    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playMenuPanel;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private GameObject settingsMenuUI;
    [SerializeField] private GameObject newOptionsMenuUI;
    [SerializeField] private GameObject tempUI;
    [SerializeField] public GameObject jumpUI;
    [SerializeField] public GameObject lobbySettingsMenuUI; // Make sure this is public

    [Header("Main Menu Components")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;

    [Header("Game Menu Components")]
    [SerializeField] public Button startGameButton;
    [SerializeField] public TextMeshProUGUI joinCodeText;
    [SerializeField] public TextMeshProUGUI countdownText;
    [SerializeField] public TextMeshProUGUI winnerText;
    [SerializeField] public TextMeshProUGUI connectionRefusedReasonText;
    [SerializeField] public GameObject connectionPending;

    [Header("References")]
    [SerializeField] public Camera startCamera;

    [Header("Scoreboard")]
    [SerializeField] private GameObject scoreboardUI;
    [SerializeField] private Scoreboard scoreboard;

    [Header("Default Button Selection")]
    [SerializeField] private Button defaultPauseMenuButton;
    [SerializeField] private Button defaultMainMenuButton;
    [SerializeField] private Button defaultSettingsMenuButton;
    [SerializeField] private Button defaultPlayMenuButton;

    [Header("UI Navigation")]
    [SerializeField] private EventSystem eventSystem;
    private bool controllerSelectionEnabled = false;
    [Header("Text Input Fields")]
    [SerializeField] private TMP_InputField[] inputFields; // Assign your input fields here
    private bool isEditingText = false;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event MenuMusicOn;
    [SerializeField] private AK.Wwise.Event PauseOn;
    [SerializeField] private AK.Wwise.Event PauseOff;
    [SerializeField] private AK.Wwise.Event uiClick;
    [SerializeField] private AK.Wwise.Event uiConfirm;
    [SerializeField] private AK.Wwise.Event uiCancel;
    private int countdownTime;

    [SerializeField] private CinemachineVirtualCamera virtualCamera;
    [SerializeField] private float rotationSpeed = 0.01f;
    private CinemachineOrbitalTransposer orbitalTransposer;

    // Reference to Input Manager
    private InputManager inputManager;

    // Add a timestamp to track when the menu was last toggled
    private float lastMenuToggleTime = 0f;
    private float menuToggleCooldown = 0.5f;

    // Add tracking for whether settings was opened from pause menu
    private bool settingsOpenedFromPauseMenu = false;

    // These references are kept for backward compatibility
    private Camera mainCamera;
    private Cinemachine.CinemachineInputProvider cameraInputProvider;
    private Cinemachine.CinemachineBrain cinemachineBrain;
    
    [Header("Lobby Settings")]
    [SerializeField] private TextMeshProUGUI lobbyCodeDisplay;
    [SerializeField] private Button lobbyCodeCopyButton;
    [SerializeField] private Button startGameFromLobbyButton;
    [SerializeField] private TextMeshProUGUI connectedPlayersText;
    [SerializeField] private TextMeshProUGUI gameModeText;
    [SerializeField] private Button gameModeLeftButton;
    [SerializeField] private Button gameModeRightButton;
    [SerializeField] private GameObject teamSettingsPanel;
    [SerializeField] private Slider teamCountSlider;
    [SerializeField] private TextMeshProUGUI teamCountText;

    // Game mode settings
    private GameMode _selectedGameMode = GameMode.FreeForAll;
    private int _teamCount = 2;

    // Game Mode enum
    public enum GameMode { FreeForAll, TeamBattle }
    
    // Public properties for game mode settings
    public GameMode selectedGameMode => _selectedGameMode;
    public int teamCount => _teamCount;

    [Header("Pause Menu References")]
    [SerializeField] private Button pauseResumeButton;
    [SerializeField] private Button pauseOptionsButton;
    [SerializeField] private Button pauseLobbySettingsButton; // Reference to the Lobby Settings button in pause menu
    [SerializeField] private Button pauseQuitButton;

    // Diagnostic code to help identify what's disabling the lobby settings menu
    private bool _prevLobbyMenuActiveState = false;

    private void Awake()
    {
        // Set singleton instance
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Find the input manager
        inputManager = InputManager.Instance;
        
        // Find the player's camera input provider
        var playerCameras = FindObjectsByType<Cinemachine.CinemachineFreeLook>(FindObjectsSortMode.None);
        var playerCamera = playerCameras.Length > 0 ? playerCameras[0] : null;
        if (playerCamera != null)
        {
            cameraInputProvider = playerCamera.GetComponent<Cinemachine.CinemachineInputProvider>();
            if (cameraInputProvider == null)
                Debug.LogWarning("CinemachineInputProvider not found on player camera");
        }
        else
        {
            Debug.LogWarning("CinemachineFreeLook camera not found in scene");
        }
        
        // Find the main camera's CinemachineBrain
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cinemachineBrain = mainCamera.GetComponent<Cinemachine.CinemachineBrain>();
            if (cinemachineBrain == null)
                Debug.LogWarning("CinemachineBrain not found on main camera");
        }
        else
        {
            Debug.LogWarning("Main camera not found in scene");
        }
    }

    private void OnEnable()
    {
        // Try to find InputManager if it wasn't found in Awake
        if (inputManager == null)
            inputManager = InputManager.Instance;

        if (inputManager != null)
        {
            // Unsubscribe first to avoid duplicate subscriptions
            inputManager.MenuToggled -= OnMenuToggled;
            inputManager.BackPressed -= OnBackPressed;
            inputManager.AcceptPressed -= OnAcceptPressed;
            inputManager.ScoreboardToggled -= HandleScoreboardToggle;

            // Now subscribe
            inputManager.MenuToggled += OnMenuToggled;
            inputManager.BackPressed += OnBackPressed;
            inputManager.AcceptPressed += OnAcceptPressed;
            inputManager.ScoreboardToggled += HandleScoreboardToggle;
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from input events
        if (inputManager != null)
        {
            inputManager.MenuToggled -= OnMenuToggled;
            inputManager.BackPressed -= OnBackPressed;
            inputManager.AcceptPressed -= OnAcceptPressed;
            inputManager.ScoreboardToggled -= HandleScoreboardToggle;
        }
    }

    private void Start()
    {
        // Get reference to EventSystem if not assigned
        if (eventSystem == null)
            eventSystem = EventSystem.current;

        // Set up explicit navigation for main menu buttons
        SetupButtonNavigation();
        
        // Set up lobby settings button in pause menu
        ConnectLobbySettingsButton();

        // Clear selection by default
        ClearSelection();
        // Try to find InputManager again if it wasn't found in Awake/OnEnable
        if (inputManager == null)
        {
            inputManager = InputManager.Instance;
            if (inputManager != null)
            {
                // Subscribe to events if we just found the InputManager
                inputManager.MenuToggled += OnMenuToggled;
                inputManager.BackPressed += OnBackPressed;
                inputManager.AcceptPressed += OnAcceptPressed;
                inputManager.ScoreboardToggled += HandleScoreboardToggle;
            }
        }
        
        // Check if input actions are enabled
        if (inputManager != null && !inputManager.AreInputActionsEnabled())
            inputManager.ForceEnableCurrentActionMap();

        // Initialize main menu
        ShowMainMenu();
        startCamera.cullingMask = 31;

        // Get camera reference if not set
        if (virtualCamera == null)
            virtualCamera = GetComponent<CinemachineVirtualCamera>();
            
        // Start monitoring the lobby settings menu activation
        StartCoroutine(MonitorLobbySettingsMenuActivation());
    }

    // Coroutine to monitor and prevent the lobby settings menu from being mysteriously disabled
    private IEnumerator MonitorLobbySettingsMenuActivation()
    {
        // Wait a bit for everything to initialize
        yield return new WaitForSeconds(1f);
        
        while (true)
        {
            // Only check when the menu should be active
            if (_prevLobbyMenuActiveState && lobbySettingsMenuUI != null)
            {
                // If the menu is supposed to be active but isn't, reactivate it
                if (!lobbySettingsMenuUI.activeInHierarchy)
                {
                    Debug.LogWarning("[MenuManager] Lobby settings menu was mysteriously deactivated! Reactivating...");
                    
                    // First check its parent
                    Transform parent = lobbySettingsMenuUI.transform.parent;
                    if (parent != null && !parent.gameObject.activeInHierarchy)
                    {
                        Debug.Log("[MenuManager] Parent is inactive, activating parent first");
                        parent.gameObject.SetActive(true);
                    }
                    
                    // Reactivate menu
                    lobbySettingsMenuUI.SetActive(true);
                    
                    // Check if the LobbySettingsPanel script is disabled
                    LobbySettingsPanel panel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>(true);
                    if (panel != null && !panel.enabled)
                    {
                        Debug.Log("[MenuManager] LobbySettingsPanel component was disabled, re-enabling");
                        panel.enabled = true;
                    }
                }
            }
            
            // Check every half second
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Set up explicit navigation between buttons for gamepad
    private void SetupButtonNavigation()
    {
        if (playButton != null && optionsButton != null && quitButton != null)
        {
            // Configure navigation for Play button
            Navigation playNav = playButton.navigation;
            playNav.mode = Navigation.Mode.Explicit;
            playNav.selectOnDown = optionsButton;
            playNav.selectOnUp = quitButton;
            playButton.navigation = playNav;
            
            // Configure navigation for Options button
            Navigation optionsNav = optionsButton.navigation;
            optionsNav.mode = Navigation.Mode.Explicit;
            optionsNav.selectOnDown = quitButton;
            optionsNav.selectOnUp = playButton;
            optionsButton.navigation = optionsNav;
            
            // Configure navigation for Quit button
            Navigation quitNav = quitButton.navigation;
            quitNav.mode = Navigation.Mode.Explicit;
            quitNav.selectOnDown = playButton;
            quitNav.selectOnUp = optionsButton;
            quitButton.navigation = quitNav;
        }
    }

    private void Update()
    {
        // Persistent cursor lock enforcement when game is running (not paused)
        if (!gameIsPaused && !mainMenuPanel.activeSelf && ConnectionManager.instance.isConnected)
        {
            // If cursor should be locked but isn't, force it
            if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            {
                Debug.Log("[MenuManager] Update detected unlocked cursor - forcing lock");
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        
        // Check for controller input to enable selection
        if (inputManager != null && inputManager.IsUsingGamepad)
        {
            // Check if controller navigation input was detected
            if (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0 ||
                Input.GetButton("Submit") || Input.GetButton("Cancel"))
            {
                // Enable controller selection and select appropriate button
                controllerSelectionEnabled = true;

                // Determine which menu is active and select its default button
                if (mainMenuPanel.activeSelf && defaultMainMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultMainMenuButton.gameObject);
                else if (playMenuPanel.activeSelf && defaultPlayMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultPlayMenuButton.gameObject);
                else if (pauseMenuUI.activeSelf && defaultPauseMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultPauseMenuButton.gameObject);
                else if (settingsMenuUI.activeSelf && defaultSettingsMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultSettingsMenuButton.gameObject);
            }
        }
        else if (Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0)
        {
            // If mouse moved, reset controller selection and clear highlighting
            controllerSelectionEnabled = false;
            ClearSelection();
        }

        if (!mainMenuPanel.activeSelf)  // Only check these when not in main menu
        {
            // Start Game Button (Host only)
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer &&
                NetworkManager.Singleton.ConnectedClients.Count > 1)
                startGameButton.interactable = true;
            else
                startGameButton.interactable = false;

            // Set join code
            if (ConnectionManager.instance.joinCode != null)
                joinCodeText.text = "Code: " + ConnectionManager.instance.joinCode;
        }
        
        // Update lobby UI if visible
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            UpdateConnectedPlayersText();
            UpdateStartGameButtonInteractability();
        }
        
        // Handle text input fields
        HandleTextInput();

        // Track changes in the lobby settings menu activation state
        if (lobbySettingsMenuUI != null)
        {
            bool currentState = lobbySettingsMenuUI.activeSelf;
            
            // If it just became active, log that
            if (currentState && !_prevLobbyMenuActiveState)
            {
                Debug.Log("LobbySettingsMenuUI was ENABLED - now active");
            }
            
            // If it just became inactive, log that with a stack trace to see what disabled it
            if (!currentState && _prevLobbyMenuActiveState)
            {
                Debug.LogWarning("LobbySettingsMenuUI was DISABLED! Stack trace:");
                Debug.LogWarning(System.Environment.StackTrace);
            }
            
            _prevLobbyMenuActiveState = currentState;
        }
    }

    // Event handlers for input system callbacks
    private void OnMenuToggled()
    {
        // Don't toggle if we're in main menu
        if (mainMenuPanel.activeSelf)
        {
            return;
        }

        // Apply cooldown to prevent rapid toggling
        if (Time.unscaledTime - lastMenuToggleTime < menuToggleCooldown)
        {
            return;
        }
        
        lastMenuToggleTime = Time.unscaledTime;

        // Check if settings menu is active
        bool settingsActive = (settingsMenuUI != null && settingsMenuUI.activeSelf) || 
                             (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf);
        
        // If settings is active, close it and show pause menu
        if (settingsActive)
        {
            if (settingsMenuUI != null)
                settingsMenuUI.SetActive(false);
            if (newOptionsMenuUI != null)
                newOptionsMenuUI.SetActive(false);
                
            // Force show pause menu
            if (pauseMenuUI != null)
            {
                pauseMenuUI.SetActive(true);
                
                // Enable all child components
                foreach (Transform child in pauseMenuUI.transform)
                {
                    child.gameObject.SetActive(true);
                }
                
                // Set selection
                if (defaultPauseMenuButton != null)
                    HandleButtonSelection(defaultPauseMenuButton);
            }
            
            // Ensure we're paused
            gameIsPaused = true;
            return;
        }

        // Normal pause toggle logic
        if (gameIsPaused)
        {
            // Important: Make this match the Resume button's code path
            // Call Resume directly without any additional code - same path as Resume button
            Resume();
        }
        else
        {
            if (PauseMenuPanel.CanOpenPauseMenu())
            {
                Pause();
            }
            else
            {
                // Even if we can't open the pause menu, unlock the cursor
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }

    public void Resume()
    {
        // Hide all menus
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(false);
        }
        
        // Lock cursor when resuming
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        
        // Enable camera input
        EnableCameraInput();
        
        gameIsPaused = false;
        
        // Play sound effect if available
        if (PauseOff != null)
        {
            PauseOff.Post(gameObject);
        }
        
        // Switch back to gameplay input mode
        if (inputManager != null)
        {
            inputManager.SwitchToGameplayMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }
    }
    
    private IEnumerator EnforceCursorLockAfterResume()
    {
        // This method has been simplified and is kept for compatibility
        yield break;
    }

    void Pause()
    {
        if (pauseMenuUI == null)
        {
            Debug.LogError("pauseMenuUI reference is null!");
            return;
        }
        
        // Update cursor state - make sure it's visible and unlocked
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Show pause menu
        pauseMenuUI.SetActive(true);
        
        // Connect the lobby settings button
        ConnectLobbySettingsButton();
        
        // Disable camera input - this is key to stopping camera rotation
        DisableCameraInput();
        
        // Check if the pause menu is actually active
        if (pauseMenuUI.activeSelf)
        {
            // Enable all child components explicitly
            foreach (Transform child in pauseMenuUI.transform)
            {
                child.gameObject.SetActive(true);
                
                // Check if this is the Lobby Settings button and connect it
                Button lobbySettingsButton = child.GetComponent<Button>();
                if (lobbySettingsButton != null && child.name.Contains("LobbySettings"))
                {
                    lobbySettingsButton.onClick.RemoveAllListeners();
                    lobbySettingsButton.onClick.AddListener(ShowLobbySettingsMenu);
                    Debug.Log("Connected LobbySettings button in pause menu");
                }
            }
            
            // Find the default pause menu button
            Button defaultButton = null;
            foreach (Button button in pauseMenuUI.GetComponentsInChildren<Button>())
            {
                if (button.name.Contains("Resume"))
                {
                    defaultButton = button;
                    break;
                }
            }
            
            // Manually clear the event system selection
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                
                // Select the default button
                if (defaultButton != null)
                {
                    EventSystem.current.SetSelectedGameObject(defaultButton.gameObject);
                }
                else if (defaultPauseMenuButton != null)
                {
                    EventSystem.current.SetSelectedGameObject(defaultPauseMenuButton.gameObject);
                }
            }
        }
        
        gameIsPaused = true;
        
        // Play sound effect if available
        if (PauseOn != null)
        {
            PauseOn.Post(gameObject);
        }

        // Switch to UI input mode - This directly disables the Player action map
        if (inputManager != null)
        {
            // This will handle disabling the player action map and enabling UI action map
            inputManager.SwitchToUIMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }
        
        // Double-check cursor state after input mode switch
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void StartGame()
    {
        if (IsServer) GameManager.instance.TransitionToState(GameState.Playing);
        Resume(); // This will also switch to gameplay mode
    }

    public void Settings()
    {
        Debug.Log("Settings method called. gameIsPaused=" + gameIsPaused);
        
        // First check if we're in the pause menu
        if (gameIsPaused)
        {
            // We're in the pause menu, so settings should return to pause menu when closed
            settingsOpenedFromPauseMenu = true;
            Debug.Log("Setting settingsOpenedFromPauseMenu to TRUE - Settings opened from pause menu");
            
            // We're in the pause menu, toggle between pause menu and settings menu
            if (newOptionsMenuUI != null)
            {
                // Use the new tabbed options menu if available
                pauseMenuUI.SetActive(false);
                newOptionsMenuUI.SetActive(true);
                
                // Force controller selection to be enabled for the options menu
                controllerSelectionEnabled = true;
                
                // Note: Camera freezing is now handled by CameraFreezeBehavior
                
                // Force enable all direct children in the hierarchy
                foreach (Transform child in newOptionsMenuUI.transform)
                {
                    child.gameObject.SetActive(true);
                }
                
                // Initialize the tab controller
                TabController tabController = newOptionsMenuUI.GetComponentInChildren<TabController>();
                if (tabController != null)
                {
                    // Ensure tab controller GameObject is active
                    tabController.gameObject.SetActive(true);
                    
                    // Find all content panels and make sure they exist
                    Transform contentTransform = tabController.transform.Find("Content");
                    if (contentTransform != null)
                    {
                        contentTransform.gameObject.SetActive(true);
                        
                        // Force enable the Video panel as default
                        Transform videoPanel = contentTransform.Find("VideoPanel");
                        if (videoPanel != null)
                        {
                            // Force video panel active
                            videoPanel.gameObject.SetActive(true);
                            
                            // Make sure other panels are inactive
                            foreach (Transform panel in contentTransform)
                            {
                                if (panel != videoPanel && panel.name.Contains("Panel"))
                                {
                                    panel.gameObject.SetActive(false);
                                }
                            }
                        }
                    }
                    
                    // Force select the Video tab
                    tabController.SelectTab(0);
                }
                
                // Handle button selection for settings menu
                if (defaultSettingsMenuButton != null)
                {
                    HandleButtonSelection(defaultSettingsMenuButton);
                }
            }
            else
            {
                // Use the old settings menu
                settingsMenuUI.SetActive(true);
                pauseMenuUI.SetActive(false);
                
                // Note: Camera freezing is now handled by CameraFreezeBehavior
                
                // Handle button selection for settings menu
                if (defaultSettingsMenuButton != null)
                {
                    HandleButtonSelection(defaultSettingsMenuButton);
                }
            }
        }
        else
        {
            // We're not in the pause menu, opened from main menu
            settingsOpenedFromPauseMenu = false;
            
            // Open the main menu
            ShowMainMenu();
        }
    }

    public void CopyJoinCode()
    {
        GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
        Debug.Log(message: "Join Code Copied");
    }

    [ClientRpc]
    public void StartCountdownClientRpc()
    {
        countdownTime = 3;
        countdownText.text = countdownTime.ToString();
        tempUI.SetActive(true);
        StartCoroutine(CountdownToStart());
    }

    private IEnumerator CountdownToStart()
    {
        while (countdownTime > 0)
        {
            countdownText.text = countdownTime.ToString();
            yield return new WaitForSeconds(1f);
            countdownTime--;
        }

        countdownText.text = "Go!";
        yield return new WaitForSeconds(1f);
        countdownText.text = "";
        tempUI.SetActive(false);
    }

    [ClientRpc]
    public void DisplayWinnerClientRpc(string player)
    {
        tempUI.SetActive(true);
        winnerText.text = player + " won the round";
        StartCoroutine(BetweenRoundTime());
    }

    public IEnumerator BetweenRoundTime()
    {
        yield return new WaitForSeconds(5f);
        winnerText.text = "";
        tempUI.SetActive(false);
    }

    [ClientRpc]
    public void ShowScoreboardClientRpc()
    {
        scoreboard.UpdatePlayerList();
        scoreboardUI.SetActive(true);
    }

    [ClientRpc]
    public void HideScoreboardClientRpc()
    {
        scoreboardUI.SetActive(false);
    }

    public void MainMenu()
    {
        Resume();
        connectionPending.SetActive(false);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ShowMainMenu();
    }

    public void Disconnect()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            DisconnectRequestServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        MainMenu();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ConnectionManager.instance.isConnected = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectRequestServerRpc(ulong clientId)
    {
        Debug.Log(message: "Disconnecting Client - " + clientId + " [" + ConnectionManager.instance.GetClientUsername(clientId) + "]");
        NetworkManager.Singleton.DisconnectClient(clientId);
    }

    public void QuitGame()
    {
        Debug.Log("Quitting Game");
        Application.Quit();
    }

    public void DisplayHostAloneMessage(string disconnectedPlayerName)
    {
        tempUI.SetActive(true);

        // Use the winner text component to display the message
        if (winnerText != null)
            winnerText.text = $"{disconnectedPlayerName} disconnected.\nYou are the only player remaining.\nWaiting for more players to join...";

        // Hide message after a few seconds
        StartCoroutine(HideHostAloneMessage(8f));
    }

    private IEnumerator HideHostAloneMessage(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Hide the message
        if (tempUI != null && tempUI.activeSelf)
            tempUI.SetActive(false);

        if (winnerText != null)
            winnerText.text = "";
    }

    public void DisplayConnectionError(string error)
    {
        connectionRefusedReasonText.text = error;
        uiCancel.Post(gameObject);
    }

    public void ButtonClickAudio()
    {
        uiClick.Post(gameObject);
    }

    public void ButtonConfirmAudio()
    {
        uiConfirm.Post(gameObject);
    }

    public void ButtonCancelAudio()
    {
        uiCancel.Post(gameObject);
    }

    public void HandleConnectionStateChange(bool connected)
    {
        // Update connection state
        ConnectionManager.instance.isConnected = connected;

        if (connected)
        {
            // When newly connected, immediately switch to gameplay mode
            if (inputManager != null)
            {
                inputManager.SwitchToGameplayMode();
                if (!inputManager.IsInGameplayMode())
                    inputManager.ForceEnableCurrentActionMap();
            }

            // Lock cursor for gameplay
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            // When disconnected, switch to UI mode and show main menu
            if (inputManager != null)
                inputManager.SwitchToUIMode();

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            ShowMainMenu();
        }
    }

    private void HandleScoreboardToggle(bool show)
    {
        if (ConnectionManager.instance.isConnected)
        {
            scoreboardUI.SetActive(show);

            if (show && scoreboard != null)
                scoreboard.UpdatePlayerList();
        }
    }

    private void ClearSelection()
    {
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(null);
    }

    // Add this method for handling button selection
    private void HandleButtonSelection(Button defaultButton)
    {
        // If using controller and selection is enabled, select the default button
        if (inputManager != null && inputManager.IsUsingGamepad && controllerSelectionEnabled)
        {
            if (defaultButton != null && defaultButton.gameObject.activeInHierarchy && defaultButton.isActiveAndEnabled)
            {
                // Clear current selection first to prevent any side effects
                eventSystem.SetSelectedGameObject(null);
                
                // Set the new selection after a small delay to ensure clean state
                StartCoroutine(SelectButtonDelayed(defaultButton, 0.05f));
            }
        }
        else
        {
            // Otherwise, clear selection to prevent automatic highlighting
            ClearSelection();
        }
    }

    private IEnumerator SelectButtonDelayed(Button button, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (button != null && button.gameObject.activeInHierarchy && button.isActiveAndEnabled)
        {
            eventSystem.SetSelectedGameObject(button.gameObject);
            
            // Force refresh the navigation
            if (button == playButton || button == optionsButton || button == quitButton)
            {
                SetupButtonNavigation();
            }
        }
    }

    private void HandleTextInput()
    {
        // Check if any input field is currently selected
        bool inputFieldSelected = false;

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            TMP_InputField inputField = EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>();
            if (inputField != null)
            {
                inputFieldSelected = true;

                // If we just started editing text
                if (!isEditingText)
                {
                    isEditingText = true;
                    // Temporarily disable controller navigation
                    DisableControllerInput();
                }
            }
        }

        // If we were editing text but are no longer
        if (isEditingText && !inputFieldSelected)
        {
            isEditingText = false;
            // Re-enable controller navigation
            EnableControllerInput();
        }
    }

    // Methods to disable/enable controller input
    private void DisableControllerInput()
    {
        // This will prevent controller input from affecting UI navigation
        // It doesn't disable controller completely, just ignores the input for navigation

        if (inputManager != null)
        {
            // Create a method in InputManager to temporarily disable controller navigation
            // This could be a simple flag that the Update method checks
            inputManager.SetControllerNavigationEnabled(false);
        }
    }

    private void EnableControllerInput()
    {
        if (inputManager != null)
        {
            inputManager.SetControllerNavigationEnabled(true);
        }
    }

    // Method to check if any menu panel is active (except pauseMenuUI)
    public bool IsAnyMenuActive()
    {
        // Make sure pauseMenuUI isn't included in this check
        return (mainMenuPanel != null && mainMenuPanel.activeSelf ||
                playMenuPanel != null && playMenuPanel.activeSelf ||
                settingsMenuUI != null && settingsMenuUI.activeSelf ||
                newOptionsMenuUI != null && newOptionsMenuUI.activeSelf ||
                tempUI != null && tempUI.activeSelf ||
                jumpUI != null && jumpUI.activeSelf ||
                connectionPending != null && connectionPending.activeSelf);
    }

    // Helper method to get the full path of a GameObject in the hierarchy
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }

    // Method to directly disable camera input on the player prefab by nulling out the input references
    // This is the solution that works to prevent camera movement during pause
    // Based on: https://discussions.unity.com/t/disabling-input-thats-used-in-input-provider-doesnt-disable-camera-movement-rotation/875265
    private void DisableCameraInput()
    {
        // Find all CinemachineFreeLook cameras in the scene
        var freeLookCameras = FindObjectsByType<Cinemachine.CinemachineFreeLook>(FindObjectsSortMode.None);
        foreach (var camera in freeLookCameras)
        {
            var inputProvider = camera.GetComponent<Cinemachine.CinemachineInputProvider>();
            if (inputProvider != null)
            {
                // Store original reference if needed
                if (gameObject.GetComponent<CameraInputReferences>() == null)
                {
                    var references = gameObject.AddComponent<CameraInputReferences>();
                    references.StoreReference(camera, inputProvider.XYAxis, inputProvider.ZAxis);
                }
                
                // Null out the input references - this is what fixes the camera movement
                inputProvider.XYAxis = null;
                inputProvider.ZAxis = null;
                Debug.Log("Disabled input reference on camera: " + camera.name);
            }
        }
    }
    
    // Method to restore camera input when unpaused by restoring the original input references
    private void EnableCameraInput()
    {
        var references = gameObject.GetComponent<CameraInputReferences>();
        if (references != null)
        {
            references.RestoreReferences();
        }
    }

    private void OnBackPressed()
    {
        // Check if we're in a settings menu and handle first
        if ((settingsMenuUI != null && settingsMenuUI.activeSelf) ||
            (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf))
        {
            // Close both settings menus to be safe
            if (settingsMenuUI != null)
                settingsMenuUI.SetActive(false);
                
            if (newOptionsMenuUI != null)
                newOptionsMenuUI.SetActive(false);
            
            // If settings was opened from the pause menu, return to pause menu
            if (settingsOpenedFromPauseMenu)
            {
                pauseMenuUI.SetActive(true);
                
                if (defaultPauseMenuButton != null)
                    HandleButtonSelection(defaultPauseMenuButton);
            }
            else
            {
                // Settings was opened from main menu
                mainMenuPanel.SetActive(true);
                
                if (defaultMainMenuButton != null)
                    HandleButtonSelection(defaultMainMenuButton);
            }
            
            // We've handled the back press, so return
            return;
        }
        
        // Handle other menu states
        if (playMenuPanel.activeSelf)
        {
            // Reset button states before disabling menus
            mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
            ShowMainMenu();
        }
        else if (pauseMenuUI.activeSelf)
        {
            // We're in the pause menu with no settings menu open
            Resume();
        }
    }

    private void OnAcceptPressed()
    {
        // Handle accept button presses if needed
        // Check if we're in the options menu
        if (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf)
        {
            // Don't do anything - let the individual UI elements handle their own click events
            // This prevents the back functionality from triggering when pressing A on buttons
            return;
        }
    }

    // Strongly ensure that cursor is visible and unlocked in main menu and UI mode
    private void EnsureCursorForUI()
    {
        // Always unlock cursor in UI mode
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        // Make sure input manager is in UI mode
        if (inputManager != null && !inputManager.IsInUIMode())
        {
            inputManager.SwitchToUIMode();
        }
    }

    public void ShowMainMenu()
    {
        // First reset the button states in the main menu
        if (mainMenuPanel.GetComponent<ButtonStateResetter>() != null)
            mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
        
        // Make the main menu active
        mainMenuPanel.SetActive(true);
        startCamera.gameObject.SetActive(true);
        MenuMusicOn.Post(gameObject);

        // Make sure all buttons are interactable
        if (playButton != null) playButton.interactable = true;
        if (optionsButton != null) optionsButton.interactable = true;
        if (quitButton != null) quitButton.interactable = true;
        
        // Restore main menu camera priority
        if (virtualCamera != null)
        {
            // Set high priority to ensure it takes precedence
            virtualCamera.Priority = 20;
            
            // Rotate main menu camera
            orbitalTransposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbitalTransposer != null)
                orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;
        }

        // Deactivate all other menu panels
        playMenuPanel.SetActive(false);
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        scoreboardUI.SetActive(false);
        tempUI.SetActive(false);
        connectionPending.SetActive(false);
        if (newOptionsMenuUI != null)
            newOptionsMenuUI.SetActive(false);

        // Critical: Ensure cursor is visible and unlocked before switching input mode
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Switch to UI input mode
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Double-check cursor state after input mode switch
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        gameIsPaused = false;  // Reset pause state
        
        // Clear any selected game objects
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(null);

        // Force controller selection to be enabled
        controllerSelectionEnabled = true;
        
        // Handle button selection based on input
        HandleButtonSelection(defaultMainMenuButton);
        
        // Ensure UI cursor state one more time
        EnsureCursorForUI();
    }

    // Method for when the Play button is clicked
    public void OnPlayClicked()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(true);

        // Lower the priority of the menu camera
        if (virtualCamera != null && virtualCamera.GetComponent<CinemachineVirtualCamera>() != null)
            virtualCamera.GetComponent<CinemachineVirtualCamera>().Priority = 0;

        // Make sure we're in UI mode for the play menu
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Handle button selection based on input
        HandleButtonSelection(defaultPlayMenuButton);
    }

    // Method for when the Options button is clicked
    public void OnOptionsClicked()
    {
        // When using gamepad, if the Options button is directly clicked, make sure we respect that
        if (mainMenuPanel.activeSelf && eventSystem != null && inputManager != null)
        {
            GameObject selected = eventSystem.currentSelectedGameObject;
            
            // Only do this redirect check if not using gamepad OR if we're sure the play button triggered this
            if (!inputManager.IsUsingGamepad && selected != null && selected != optionsButton.gameObject && 
                selected == playButton.gameObject)
            {
                // We're actually clicking the Play button (with mouse)
                OnPlayClicked();
                return;
            }
        }

        // Track that settings is being opened from main menu
        settingsOpenedFromPauseMenu = false;

        ButtonClickAudio();
        
        // Use the new tabbed options menu if available, otherwise fall back to old settings menu
        if (newOptionsMenuUI != null)
        {
            // Force deactivate first to ensure a clean state
            newOptionsMenuUI.SetActive(false);
            
            // Reset UI state
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(null);
            
            // Enable the options menu GameObject and all its children
            newOptionsMenuUI.SetActive(true);
            
            // Force controller selection to be enabled for the options menu
            controllerSelectionEnabled = true;
            
            // Force enable all direct children in the hierarchy
            foreach (Transform child in newOptionsMenuUI.transform)
            {
                child.gameObject.SetActive(true);
            }
            
            // Force enable all panels
            TabController tabController = newOptionsMenuUI.GetComponentInChildren<TabController>();
            if (tabController != null)
            {
                // Ensure tab controller GameObject is active
                tabController.gameObject.SetActive(true);
                
                // Find all content panels and make sure they exist
                Transform contentTransform = tabController.transform.Find("Content");
                if (contentTransform != null)
                {
                    contentTransform.gameObject.SetActive(true);
                    
                    // Force enable the Video panel using the actual name in the hierarchy
                    Transform videoPanel = contentTransform.Find("VideoPanel");
                    if (videoPanel != null)
                    {
                        // Force video panel active
                        videoPanel.gameObject.SetActive(true);
                        
                        // Make sure other panels are inactive
                        foreach (Transform panel in contentTransform)
                        {
                            if (panel != videoPanel && panel.name.Contains("Panel"))
                            {
                                panel.gameObject.SetActive(false);
                            }
                        }
                    }
                }
                
                // Force select the Video tab
                tabController.SelectTab(0);
            }
            
            // Hide main menu
            mainMenuPanel.SetActive(false);
            
            // Handle button selection
            if (eventSystem != null && defaultSettingsMenuButton != null)
            {
                HandleButtonSelection(defaultSettingsMenuButton);
            }
        }
        else
        {
            // Fall back to old settings menu
            settingsMenuUI.SetActive(true);
            
            // Handle button selection based on input
            if (eventSystem != null && defaultSettingsMenuButton != null)
            {
                HandleButtonSelection(defaultSettingsMenuButton);
            }
        }
    }

    // Method to open Lobby Settings Menu (called when host creates lobby or from pause menu)
    public void OpenLobbySettingsMenu()
    {
        Debug.Log("[MenuManager] OpenLobbySettingsMenu called - TRACKING ISSUE");
        
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("[MenuManager] lobbySettingsMenuUI reference is null!");
            return;
        }
        
        // Check if we're in a lobby
        bool inLobby = NetworkManager.Singleton != null && ConnectionManager.instance != null 
                     && !string.IsNullOrEmpty(ConnectionManager.instance.joinCode);
        
        // Log initial state
        Debug.Log($"[MenuManager] Initial state - GameObject name: {lobbySettingsMenuUI.name}, activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}, inLobby: {inLobby}");
        
        // CRITICAL: Ensure the parent is active first
        Transform rootParent = lobbySettingsMenuUI.transform.parent;
        if (rootParent != null && !rootParent.gameObject.activeInHierarchy)
        {
            Debug.Log("[MenuManager] Parent is inactive, activating parent first");
            rootParent.gameObject.SetActive(true);
        }
        
        // Force-close all other menus EXCEPT the lobby settings menu
        HideAllExceptLobby();
        
        // Log state after hiding
        Debug.Log($"[MenuManager] After HideAllExceptLobby - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // Special handling for lobby context
        if (inLobby)
        {
            Debug.Log("[MenuManager] In lobby context - using special activation sequence");
            
            // Ensure this panel isn't subsequently deactivated by other scripts
            _prevLobbyMenuActiveState = true;
            
            // Direct activation through parent chain
            Transform current = lobbySettingsMenuUI.transform;
            while (current.parent != null)
            {
                current = current.parent;
                current.gameObject.SetActive(true);
            }
        }
        
        // Check and fix UI components first before attempting activation
        CheckUILayoutComponents(lobbySettingsMenuUI);
        
        // A new ultra simple approach - just try direct activation
        lobbySettingsMenuUI.SetActive(true);
        Debug.Log($"[MenuManager] After first SetActive(true) - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // If that didn't work, try more aggressive methods
        if (!lobbySettingsMenuUI.activeInHierarchy)
        {
            // Log parent information
            Transform parentTransform = lobbySettingsMenuUI.transform.parent;
            Debug.Log($"[MenuManager] Parent info - Name: {(parentTransform != null ? parentTransform.name : "null")}, Active: {(parentTransform != null ? parentTransform.gameObject.activeInHierarchy : false)}");
            
            // Activate parent if it exists
            if (parentTransform != null && !parentTransform.gameObject.activeInHierarchy)
            {
                Debug.Log("[MenuManager] Parent is inactive, activating parent first");
                parentTransform.gameObject.SetActive(true);
                
                // Check parent UI components as well
                CheckUILayoutComponents(parentTransform.gameObject);
            }
            
            // Try setting active again
            lobbySettingsMenuUI.SetActive(true);
            Debug.Log($"[MenuManager] After second SetActive(true) - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
            
            // If still not active, use the detach/reattach method
            if (!lobbySettingsMenuUI.activeInHierarchy)
            {
                Debug.Log("[MenuManager] Using detach/reattach method");
                Transform originalParent = lobbySettingsMenuUI.transform.parent;
                int siblingIndex = lobbySettingsMenuUI.transform.GetSiblingIndex();
                
                // Detach
                lobbySettingsMenuUI.transform.SetParent(null);
                
                // Activate
                lobbySettingsMenuUI.SetActive(true);
                
                // Reattach
                lobbySettingsMenuUI.transform.SetParent(originalParent);
                lobbySettingsMenuUI.transform.SetSiblingIndex(siblingIndex);
                
                Debug.Log($"[MenuManager] After detach/reattach - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
            }
        }
        
        // Configure the lobby settings from current game state
        ConfigureLobbySettingsMenu();
        
        // Final state
        Debug.Log($"[MenuManager] FINAL STATE - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // Track that this menu should be active now
        _prevLobbyMenuActiveState = true;
    }

    // Add the HideAllMenus method
    private void HideAllMenus()
    {
        // Hide all menu panels
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (playMenuPanel != null) playMenuPanel.SetActive(false);
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        if (settingsMenuUI != null) settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != null) newOptionsMenuUI.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        if (tempUI != null) tempUI.SetActive(false);
        if (connectionPending != null) connectionPending.SetActive(false);
        
        // IMPORTANT: Do NOT deactivate the lobbySettingsMenuUI here
        // This was causing the enable/disable cycle
        // We will activate it manually afterwards
    }
    
    // Create a specialized method to hide all except the lobby settings menu
    private void HideAllExceptLobby()
    {
        // Hide all menu panels except for the lobby settings
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (playMenuPanel != null) playMenuPanel.SetActive(false);
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        if (settingsMenuUI != null) settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != null) newOptionsMenuUI.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        if (tempUI != null) tempUI.SetActive(false);
        if (connectionPending != null) connectionPending.SetActive(false);
        
        // CRITICAL: Make sure we don't accidentally disable the lobby settings menu 
        // if it's already active
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            Debug.Log("[MenuManager] Lobby settings menu was already active, ensuring it stays active");
            // Don't do anything to it
        }
    }

    // Lobby Settings Methods
    public void ShowLobbySettingsMenu()
    {
        Debug.Log("ShowLobbySettingsMenu called - showing lobby settings menu!");
        
        // Defer the actual setup to the next frame to ensure proper activation
        if (lobbySettingsMenuUI != null)
        {
            // First activate the GameObject
            lobbySettingsMenuUI.SetActive(true);
            
            // Then configure it in the next frame
            Invoke("ConfigureLobbySettingsMenu", 0.1f);
        }
        else
        {
            Debug.LogError("LobbySettingsMenuUI is not assigned!");
        }
    }
    
    // Test method that can be hooked up directly to Unity UI Button OnClick
    public void ShowLobbySettingsMenuTest()
    {
        Debug.Log("TEST METHOD ShowLobbySettingsMenuTest called!");
        
        // Simple direct approach - just show the menu
        if (lobbySettingsMenuUI != null)
        {
            // First activate the GameObject
            lobbySettingsMenuUI.SetActive(true);
            Debug.Log("Lobby settings menu activated directly");
            
            // Hide pause menu
            if (pauseMenuUI != null && pauseMenuUI.activeSelf)
            {
                pauseMenuUI.SetActive(false);
                Debug.Log("Pause menu deactivated");
            }
        }
        else
        {
            Debug.LogError("LobbySettingsMenuUI is not assigned!");
        }
    }
    
    // Method to configure the lobby settings after it's been activated
    private void ConfigureLobbySettingsMenu()
    {
        Debug.Log("[MenuManager] ConfigureLobbySettingsMenu called");
        
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("[MenuManager] lobbySettingsMenuUI is null in ConfigureLobbySettingsMenu");
            return;
        }
        
        // Ensure parent objects are active first
        Transform parent = lobbySettingsMenuUI.transform.parent;
        while (parent != null)
        {
            if (!parent.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"[MenuManager] Parent {parent.name} is inactive! Activating.");
                parent.gameObject.SetActive(true);
            }
            parent = parent.parent;
        }
        
        // Verify the menu is active before trying to get components
        if (!lobbySettingsMenuUI.activeInHierarchy)
        {
            Debug.LogWarning("[MenuManager] Lobby settings menu not active in hierarchy during configuration, activating now");
            lobbySettingsMenuUI.SetActive(true);
            
            // Give it a frame to activate
            Invoke("DelayedConfigureLobbySettings", 0.05f);
            return;
        }
        
        // Get the LobbySettingsPanel component
        LobbySettingsPanel lobbySettingsPanel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>(true);
        if (lobbySettingsPanel == null)
        {
            Debug.LogError("[MenuManager] Could not find LobbySettingsPanel component in lobbySettingsMenuUI");
            return;
        }
        
        // Now that we know the panel is active and we have a reference, we can update the UI
        Debug.Log("[MenuManager] Found LobbySettingsPanel, updating UI");
        
        // Manually enable the LobbySettingsPanel script in case it was disabled
        lobbySettingsPanel.enabled = true;
        
        // Force reset initialization state to ensure it processes the OnEnable correctly
        ForceResetLobbyPanelInitialization(lobbySettingsPanel);
        
        // Update UI with current settings
        lobbySettingsPanel.UpdateUI();
        
        // Let the panel know it should refresh its state
        lobbySettingsPanel.StartPeriodicUpdates();
    }
    
    // Delayed configuration method to handle activation
    private void DelayedConfigureLobbySettings()
    {
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeInHierarchy)
        {
            // Actually configure the panel now that it's had time to activate
            LobbySettingsPanel lobbySettingsPanel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>(true);
            if (lobbySettingsPanel != null)
            {
                lobbySettingsPanel.enabled = true;
                ForceResetLobbyPanelInitialization(lobbySettingsPanel);
                lobbySettingsPanel.UpdateUI();
                lobbySettingsPanel.StartPeriodicUpdates();
            }
        }
    }
    
    // Helper method to reset the initialization state in LobbySettingsPanel via reflection
    private void ForceResetLobbyPanelInitialization(LobbySettingsPanel panel)
    {
        if (panel == null)
            return;
            
        try
        {
            // Use reflection to reset the isInitialized field
            System.Reflection.FieldInfo field = typeof(LobbySettingsPanel).GetField("isInitialized", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                
            if (field != null)
            {
                field.SetValue(panel, false);
                Debug.Log("[MenuManager] Successfully reset LobbySettingsPanel initialization state");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MenuManager] Error resetting LobbySettingsPanel initialization: {e.Message}");
        }
    }
    
    // Method to update game mode display text
    private void UpdateGameModeDisplay()
    {
        if (gameModeText != null)
        {
            gameModeText.text = _selectedGameMode.ToString();
        }
        
        // Update team settings visibility
        SetTeamSettingsVisibility(_selectedGameMode == GameMode.TeamBattle);
    }
    
    // Method called when left arrow for game mode is clicked
    public void OnGameModeLeftClicked()
    {
        ButtonClickAudio();
        
        if (_selectedGameMode == GameMode.TeamBattle)
        {
            _selectedGameMode = GameMode.FreeForAll;
            
            // Update game settings
            if (GameManager.instance != null)
            {
                GameManager.instance.SetGameMode(GameMode.FreeForAll);
            }
            
            // Update UI
            UpdateGameModeDisplay();
        }
    }
    
    // Method called when right arrow for game mode is clicked
    public void OnGameModeRightClicked()
    {
        ButtonClickAudio();
        
        if (_selectedGameMode == GameMode.FreeForAll)
        {
            _selectedGameMode = GameMode.TeamBattle;
            
            // Update game settings
            if (GameManager.instance != null)
            {
                GameManager.instance.SetGameMode(GameMode.TeamBattle);
                GameManager.instance.SetTeamCount(_teamCount);
            }
            
            // Update UI
            UpdateGameModeDisplay();
        }
    }
    
    // Method to control visibility of team settings panel
    private void SetTeamSettingsVisibility(bool visible)
    {
        if (teamSettingsPanel != null)
        {
            teamSettingsPanel.SetActive(visible);
        }
    }
    
    // Method called when team count slider is changed
    public void OnTeamCountSliderChanged(float value)
    {
        _teamCount = Mathf.RoundToInt(value);
        UpdateTeamCountText();
        
        // Update game settings
        if (GameManager.instance != null && _selectedGameMode == GameMode.TeamBattle)
        {
            GameManager.instance.SetTeamCount(_teamCount);
        }
    }
    
    // Method to update team count text display
    private void UpdateTeamCountText()
    {
        if (teamCountText != null)
        {
            teamCountText.text = _teamCount.ToString() + " Teams";
        }
    }
    
    // Update connected players text
    private void UpdateConnectedPlayersText()
    {
        if (connectedPlayersText != null && NetworkManager.Singleton != null)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
            connectedPlayersText.text = "Connected Players: " + playerCount + 
                (playerCount < 2 ? "\n(Need at least 2 players to start)" : "");
        }
    }
    
    // Update Start Game button interactability based on player count
    private void UpdateStartGameButtonInteractability()
    {
        if (startGameFromLobbyButton != null && NetworkManager.Singleton != null)
        {
            bool canStart = NetworkManager.Singleton.ConnectedClients.Count >= 2;
            startGameFromLobbyButton.interactable = canStart;
        }
    }
    
    // Method to start game from Lobby Settings Menu
    public void StartGameFromLobbySettings()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;
            
        // Close lobby settings menu
        if (lobbySettingsMenuUI != null)
            lobbySettingsMenuUI.SetActive(false);
            
        // Hide all UI panels
        HideAllMenusIncludingLobby();
            
        // Resume gameplay
        gameIsPaused = false;
        Time.timeScale = 1f;
            
        // Switch to gameplay mode for input
        if (inputManager != null)
        {
            inputManager.SwitchToGameplayMode();
            
            // Force cursor lock
            StartCoroutine(EnforceCursorLockAfterStartGame());
        }
            
        // Start the game - use TransitionToState instead of StartGame
        if (GameManager.instance != null)
        {
            GameManager.instance.TransitionToState(GameState.Playing);
            ButtonConfirmAudio();
        }
    }

    // Modified version to hide all menus when we WANT to hide lobby settings too
    private void HideAllMenusIncludingLobby()
    {
        HideAllMenus();
        if (lobbySettingsMenuUI != null) lobbySettingsMenuUI.SetActive(false);
    }
    
    // Public method to ensure the lobby settings menu is active and initialized
    public void EnsureLobbySettingsMenuActive()
    {
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("LobbySettingsMenuUI is not assigned!");
            return;
        }
        
        // First make sure the GameObject is active
        if (!lobbySettingsMenuUI.activeSelf)
        {
            lobbySettingsMenuUI.SetActive(true);
        }
        
        // Hide other menus that might be active
        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            pauseMenuUI.SetActive(false);
        }
        
        if (playMenuPanel != null && playMenuPanel.activeSelf)
        {
            playMenuPanel.SetActive(false);
        }
        
        // Update all UI elements in the lobby settings menu
        if (ConnectionManager.instance != null && lobbyCodeDisplay != null)
        {
            lobbyCodeDisplay.text = "Lobby Code: " + ConnectionManager.instance.joinCode;
        }
        
        UpdateConnectedPlayersText();
        UpdateGameModeDisplay();
        
        if (teamCountSlider != null)
        {
            teamCountSlider.value = teamCount;
            UpdateTeamCountText();
        }
        
        UpdateStartGameButtonInteractability();
    }

    public void CloseLobbySettingsMenu()
    {
        Debug.Log("[MenuManager] CloseLobbySettingsMenu called");
    
        if (lobbySettingsMenuUI == null)
            return;
        
        // Play button sound
        ButtonClickAudio();
        
        // Track the menu state - IMPORTANT: Set this BEFORE disabling
        _prevLobbyMenuActiveState = false;
        
        // Check for components that might need to be disabled first to prevent callbacks
        LobbySettingsPanel panel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>();
        if (panel != null)
        {
            // Disable the panel component first to prevent any OnDisable callbacks
            panel.StopAllCoroutines();
            panel.CancelInvoke();
        }
        
        // Hide lobby settings menu
        lobbySettingsMenuUI.SetActive(false);
        
        // Show pause menu if we're in-game
        if (GameManager.instance != null && GameManager.instance.state != GameState.Pending)
        {
            pauseMenuUI.SetActive(true);
            
            // Set selected button if using controller
            if (controllerSelectionEnabled && EventSystem.current != null)
            {
                if (defaultPauseMenuButton != null)
                {
                    EventSystem.current.SetSelectedGameObject(defaultPauseMenuButton.gameObject);
                }
            }
        }
        // Return to main menu if we were in the main menu flow
        else if (!mainMenuPanel.activeSelf && !playMenuPanel.activeSelf)
        {
            pauseMenuUI.SetActive(true);
        }
    }
    
    private IEnumerator EnforceCursorLockAfterStartGame()
    {
        for (int i = 0; i < 10; i++)
        {
            // Wait a frame
            yield return null;
                
            // Lock cursor
            if (!gameIsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
    
    // Game Mode Setting Methods
    public void SetGameMode(GameMode mode)
    {
        _selectedGameMode = mode;
        
        // Sync to GameManager if available
        if (GameManager.instance != null)
        {
            GameManager.instance.SetGameMode(mode);
        }
        
        ButtonClickAudio();
    }
    
    public void SetTeamCount(int count)
    {
        if (count < 2) count = 2;
        if (count > 4) count = 4;
        
        _teamCount = count;
        
        // Sync to GameManager if available
        if (GameManager.instance != null)
        {
            GameManager.instance.SetTeamCount(count);
        }
        
        ButtonClickAudio();
    }

    private void ConnectLobbySettingsButton()
    {
        // If we have a direct reference to the button, use it
        if (pauseLobbySettingsButton != null)
        {
            pauseLobbySettingsButton.onClick.RemoveAllListeners();
            pauseLobbySettingsButton.onClick.AddListener(ShowLobbySettingsMenu);
            Debug.Log("Connected Lobby Settings button via direct reference: " + pauseLobbySettingsButton.name);
            return;
        }
            
        // Fallback to search if direct reference is missing
        if (pauseMenuUI == null)
            return;
            
        // Find the lobby settings button in the pause menu
        Button lobbySettingsButton = null;
        
        // First try to find by direct name
        Transform lobbySettingsTransform = pauseMenuUI.transform.Find("LobbySettingsButton");
        if (lobbySettingsTransform != null)
        {
            lobbySettingsButton = lobbySettingsTransform.GetComponent<Button>();
        }
        
        // If not found by direct name, search all buttons
        if (lobbySettingsButton == null)
        {
            foreach (Button button in pauseMenuUI.GetComponentsInChildren<Button>(true))
            {
                if (button.name.Contains("LobbySettings"))
                {
                    lobbySettingsButton = button;
                    break;
                }
            }
        }
        
        // If found, connect it
        if (lobbySettingsButton != null)
        {
            lobbySettingsButton.onClick.RemoveAllListeners();
            lobbySettingsButton.onClick.AddListener(ShowLobbySettingsMenu);
            Debug.Log("Found and connected LobbySettings button in pause menu: " + lobbySettingsButton.name);
        }
        else
        {
            Debug.LogWarning("Could not find LobbySettings button in pause menu");
        }
    }

    // Direct method that can be assigned in the Unity Inspector to a button's OnClick
    public void DirectOpenLobbySettings()
    {
        Debug.Log("DirectOpenLobbySettings called - trying alternative approach!");
        
        // Check if the lobby settings menu UI is assigned
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("lobbySettingsMenuUI is not assigned!");
            return;
        }
        
        // Hide pause menu
        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            pauseMenuUI.SetActive(false);
        }
        
        // Check if this is the server
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only the host can access lobby settings!");
            return;
        }
        
        // Play button sound
        ButtonClickAudio();
        
        // ALTERNATIVE APPROACH: Force-enable Menus GameObject
        Transform parent = lobbySettingsMenuUI.transform.parent;
        if (parent != null)
        {
            Debug.Log($"Parent GameObject: {parent.name}, Active: {parent.gameObject.activeInHierarchy}");
            parent.gameObject.SetActive(true);
        }
        
        // Check if each component in the UI is active and properly assigned
        Debug.Log($"LobbySettingsMenuUI active self: {lobbySettingsMenuUI.activeSelf}");
        Debug.Log($"LobbySettingsMenuUI active in hierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // Instead of just setting active, destroy and recreate the menu
        // First store the parent and position
        Transform originalParent = lobbySettingsMenuUI.transform.parent;
        int siblingIndex = lobbySettingsMenuUI.transform.GetSiblingIndex();
        
        // Store a reference to key components we need to reconnect
        Transform lobbyPanel = lobbySettingsMenuUI.transform.Find("LobbySettingsPanel");
        
        // Set active state
        lobbySettingsMenuUI.SetActive(true);
        
        // Force all children to be active
        foreach (Transform child in lobbySettingsMenuUI.transform)
        {
            Debug.Log($"Child: {child.name}, Active: {child.gameObject.activeSelf}");
            child.gameObject.SetActive(true);
        }
        
        // Explicitly force the UI elements to update their layout
        Canvas canvas = lobbySettingsMenuUI.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Debug.Log("Found parent canvas, forcing rebuild");
            canvas.enabled = false;
            canvas.enabled = true;
            
            // Force layout update
            if (canvas.GetComponent<UnityEngine.UI.CanvasScaler>() != null)
            {
                canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false;
                canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = true;
            }
        }
        
        // Manually invoke the configuration
        CancelInvoke("ConfigureLobbySettingsMenu");
        Invoke("ConfigureLobbySettingsMenu", 0.1f);
    }

    // Diagnostic utility to help find why LobbySettingsMenuUI can't be activated
    public void DiagnoseAndFixUIIssues()
    {
        Debug.Log("=== DIAGNOSING UI ACTIVATION ISSUES ===");
        
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("LobbySettingsMenuUI reference is missing");
            return;
        }
        
        // Debug activation state
        Debug.Log($"LobbySettingsMenuUI activeSelf: {lobbySettingsMenuUI.activeSelf}");
        Debug.Log($"LobbySettingsMenuUI activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // Check for script that might be disabling this GameObject
        LobbySettingsPanel panel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>();
        if (panel != null)
        {
            Debug.Log("Found LobbySettingsPanel component");
            
            // Check if the panel is being disabled in OnEnable
            Transform panelTransform = panel.transform;
            GameObject panelObj = panel.gameObject;
            
            Debug.Log($"Panel GameObject: {panelObj.name}");
            Debug.Log($"Panel activeSelf: {panelObj.activeSelf}");
            Debug.Log($"Panel activeInHierarchy: {panelObj.activeInHierarchy}");
            
            // The issue might be in the LobbySettingsPanel's OnEnable method
            // where it checks if the user is the host and disables itself if not
        }
        
        // Try to locate all canvas components in the scene
        Canvas[] allCanvases = FindObjectsOfType<Canvas>();
        Debug.Log($"Found {allCanvases.Length} canvases in the scene");
        
        foreach (Canvas canvas in allCanvases)
        {
            Debug.Log($"Canvas: {canvas.name}, enabled: {canvas.enabled}, sortingOrder: {canvas.sortingOrder}");
        }
        
        // Try to manually activate the UI
        Debug.Log("Attempting to manually activate the LobbySettingsMenuUI...");
        
        // Disable any script that might be interfering
        if (panel != null)
        {
            Debug.Log("Temporarily disabling the LobbySettingsPanel component");
            panel.enabled = false;
        }
        
        // Make sure parent object is active
        Transform parent = lobbySettingsMenuUI.transform.parent;
        if (parent != null)
        {
            Debug.Log($"Parent object: {parent.name}, activeSelf: {parent.gameObject.activeSelf}");
            parent.gameObject.SetActive(true);
        }
        
        // Try to activate the menu
        lobbySettingsMenuUI.SetActive(true);
        
        // Check if activation succeeded
        Debug.Log($"After activation: activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
        
        // Re-enable the panel component if we disabled it
        if (panel != null && !panel.enabled)
        {
            panel.enabled = true;
        }
    }

    // Helper method to check if UI layout components are potentially causing issues
    private void CheckUILayoutComponents(GameObject uiObject)
    {
        Debug.Log($"[MenuManager] Checking UI layout components on {uiObject.name}");
        
        // Check for common UI components that might need to be refreshed
        LayoutGroup[] layoutGroups = uiObject.GetComponentsInChildren<LayoutGroup>(true);
        if (layoutGroups.Length > 0)
        {
            Debug.Log($"[MenuManager] Found {layoutGroups.Length} LayoutGroup components");
            foreach (LayoutGroup lg in layoutGroups)
            {
                Debug.Log($"  - {lg.GetType().Name} on {lg.gameObject.name}, enabled: {lg.enabled}, active: {lg.gameObject.activeInHierarchy}");
                // Force refresh
                lg.enabled = false;
                lg.enabled = true;
            }
        }
        
        // Check for ContentSizeFitter
        ContentSizeFitter[] sizeFitters = uiObject.GetComponentsInChildren<ContentSizeFitter>(true);
        if (sizeFitters.Length > 0)
        {
            Debug.Log($"[MenuManager] Found {sizeFitters.Length} ContentSizeFitter components");
            foreach (ContentSizeFitter csf in sizeFitters)
            {
                Debug.Log($"  - ContentSizeFitter on {csf.gameObject.name}, enabled: {csf.enabled}, active: {csf.gameObject.activeInHierarchy}");
                // Force refresh
                csf.enabled = false;
                csf.enabled = true;
            }
        }
        
        // Check for CanvasGroup - might be affecting visibility
        CanvasGroup[] canvasGroups = uiObject.GetComponentsInChildren<CanvasGroup>(true);
        if (canvasGroups.Length > 0)
        {
            Debug.Log($"[MenuManager] Found {canvasGroups.Length} CanvasGroup components");
            foreach (CanvasGroup cg in canvasGroups)
            {
                Debug.Log($"  - CanvasGroup on {cg.gameObject.name}, alpha: {cg.alpha}, interactable: {cg.interactable}, blocksRaycasts: {cg.blocksRaycasts}");
                // Make sure alpha is not causing invisibility
                if (cg.alpha < 0.01f)
                {
                    Debug.LogWarning($"  - CanvasGroup alpha is very low: {cg.alpha}, setting to 1");
                    cg.alpha = 1f;
                }
                
                // Make sure interaction is enabled
                if (!cg.interactable || !cg.blocksRaycasts)
                {
                    Debug.LogWarning($"  - CanvasGroup has interactable or blocksRaycasts disabled, enabling both");
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
        }
        
        // Check for Canvas
        Canvas[] canvases = uiObject.GetComponentsInChildren<Canvas>(true);
        if (canvases.Length > 0)
        {
            Debug.Log($"[MenuManager] Found {canvases.Length} Canvas components");
            foreach (Canvas canvas in canvases)
            {
                Debug.Log($"  - Canvas on {canvas.gameObject.name}, enabled: {canvas.enabled}, sortingOrder: {canvas.sortingOrder}");
                // Force refresh and ensure enabled
                canvas.enabled = true;
                
                // If this is a nested canvas, check if the sorting order might be causing issues
                if (canvas.transform.parent != null && canvas.transform.parent.GetComponentInParent<Canvas>() != null)
                {
                    Debug.Log("    - This is a nested canvas");
                }
            }
        }
    }

    // EMERGENCY DIRECT APPROACH - Only use as a last resort
    public void EmergencyActivateLobbySettingsMenu()
    {
        Debug.Log("EMERGENCY ACTIVATION of Lobby Settings Menu!");
        
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogError("lobbySettingsMenuUI is not assigned!");
            return;
        }
        
        // Hide all other menus first
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (playMenuPanel != null) playMenuPanel.SetActive(false);
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        if (settingsMenuUI != null) settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != null) newOptionsMenuUI.SetActive(false);
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        if (tempUI != null) tempUI.SetActive(false);
        if (connectionPending != null) connectionPending.SetActive(false);
        
        // Direct activation of all parents in the hierarchy chain
        Transform current = lobbySettingsMenuUI.transform;
        while (current.parent != null)
        {
            current = current.parent;
            current.gameObject.SetActive(true);
            Debug.Log("  Activated parent: " + current.name);
        }
        
        // Direct activation of the menu itself
        lobbySettingsMenuUI.SetActive(true);
        
        // Track that this menu should be active
        _prevLobbyMenuActiveState = true;
        
        // Force all child UI components to be active
        foreach (Transform child in lobbySettingsMenuUI.transform)
        {
            child.gameObject.SetActive(true);
            Debug.Log("  Activated child: " + child.name);
        }
        
        // Find and activate the LobbySettingsPanel component
        LobbySettingsPanel panel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>(true);
        if (panel != null)
        {
            panel.enabled = true;
            Debug.Log("  Enabled LobbySettingsPanel component");
            
            // Call the public UI update methods
            panel.UpdateUI();
        }
        
        // Log the final state
        Debug.Log($"Emergency activation complete - activeSelf: {lobbySettingsMenuUI.activeSelf}, activeInHierarchy: {lobbySettingsMenuUI.activeInHierarchy}");
    }
}

// Helper class to store and restore camera input references
// This solution fixes the camera movement during pause by nulling out the input references
// and then restoring them when unpaused
public class CameraInputReferences : MonoBehaviour
{
    private class CameraReference
    {
        public Cinemachine.CinemachineFreeLook Camera;
        public UnityEngine.InputSystem.InputActionReference XYAxis;
        public UnityEngine.InputSystem.InputActionReference ZAxis;
    }
    
    private List<CameraReference> storedReferences = new List<CameraReference>();
    
    public void StoreReference(Cinemachine.CinemachineFreeLook camera, 
        UnityEngine.InputSystem.InputActionReference xyAxis, 
        UnityEngine.InputSystem.InputActionReference zAxis)
    {
        // Only store if we don't already have a reference for this camera
        if (!storedReferences.Any(r => r.Camera == camera))
        {
            storedReferences.Add(new CameraReference 
            { 
                Camera = camera,
                XYAxis = xyAxis,
                ZAxis = zAxis
            });
            Debug.Log("Stored input reference for camera: " + camera.name);
        }
    }
    
    public void RestoreReferences()
    {
        foreach (var reference in storedReferences)
        {
            if (reference.Camera != null)
            {
                var inputProvider = reference.Camera.GetComponent<Cinemachine.CinemachineInputProvider>();
                if (inputProvider != null)
                {
                    inputProvider.XYAxis = reference.XYAxis;
                    inputProvider.ZAxis = reference.ZAxis;
                    Debug.Log("Restored input reference for camera: " + reference.Camera.name);
                }
            }
        }
    }
}