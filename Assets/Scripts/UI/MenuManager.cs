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
    [SerializeField] private GameObject lobbySettingsMenuUI; // New Lobby Settings Menu

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
    [SerializeField] private Toggle freeForAllToggle;
    [SerializeField] private Toggle teamBattleToggle;
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
        if (!gameIsPaused && !mainMenuPanel.activeSelf && ConnectionManager.Instance.isConnected)
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
            if (ConnectionManager.Instance.joinCode != null)
                joinCodeText.text = "Code: " + ConnectionManager.Instance.joinCode;
        }
        
        // Update lobby UI if visible
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            UpdateConnectedPlayersText();
            UpdateStartGameButtonInteractability();
        }
        
        // Handle text input fields
        HandleTextInput();
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
        
        // Disable camera input - this is key to stopping camera rotation
        DisableCameraInput();
        
        // Check if the pause menu is actually active
        if (pauseMenuUI.activeSelf)
        {
            // Enable all child components explicitly
            foreach (Transform child in pauseMenuUI.transform)
            {
                child.gameObject.SetActive(true);
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
        if (IsServer) GameManager.Instance.TransitionToState(GameState.Playing);
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
        GUIUtility.systemCopyBuffer = ConnectionManager.Instance.joinCode;
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
        ConnectionManager.Instance.isConnected = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectRequestServerRpc(ulong clientId)
    {
        Debug.Log(message: "Disconnecting Client - " + clientId + " [" + ConnectionManager.Instance.GetClientUsername(clientId) + "]");
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
        ConnectionManager.Instance.isConnected = connected;

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
        if (ConnectionManager.Instance.isConnected)
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
        // Only host can open this menu
        if (!IsServer)
        {
            Debug.Log("Only the host can access lobby settings");
            return;
        }
        
        // Hide pause menu if it's active
        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            pauseMenuUI.SetActive(false);
        }
        
        // Show Lobby Settings Menu
        if (lobbySettingsMenuUI != null)
        {
            lobbySettingsMenuUI.SetActive(true);
            
            // Update lobby code display
            if (lobbyCodeDisplay != null && ConnectionManager.Instance != null)
            {
                lobbyCodeDisplay.text = "Lobby Code: " + ConnectionManager.Instance.joinCode;
            }
            
            // Update player count
            UpdateConnectedPlayersText();
            
            // Update game mode selection based on current settings
            if (freeForAllToggle != null && teamBattleToggle != null)
            {
                freeForAllToggle.isOn = selectedGameMode == GameMode.FreeForAll;
                teamBattleToggle.isOn = selectedGameMode == GameMode.TeamBattle;
            }
            
            // Update team settings visibility
            SetTeamSettingsVisibility(selectedGameMode == GameMode.TeamBattle);
            
            // Set team count slider value
            if (teamCountSlider != null)
            {
                teamCountSlider.value = teamCount;
                UpdateTeamCountText();
            }
            
            // Update Start Game button interactability
            UpdateStartGameButtonInteractability();
        }
    }
    
    // Method called when Free For All toggle is changed
    public void OnFreeForAllToggleChanged(bool isOn)
    {
        if (isOn)
        {
            _selectedGameMode = GameMode.FreeForAll;
            SetTeamSettingsVisibility(false);
            
            // Make sure team battle toggle is off
            if (teamBattleToggle != null && teamBattleToggle.isOn)
            {
                teamBattleToggle.isOn = false;
            }
            
            // Update game settings
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetGameMode(GameMode.FreeForAll);
            }
        }
    }
    
    // Method called when Team Battle toggle is changed
    public void OnTeamBattleToggleChanged(bool isOn)
    {
        if (isOn)
        {
            _selectedGameMode = GameMode.TeamBattle;
            SetTeamSettingsVisibility(true);
            
            // Make sure free for all toggle is off
            if (freeForAllToggle != null && freeForAllToggle.isOn)
            {
                freeForAllToggle.isOn = false;
            }
            
            // Update game settings
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetGameMode(GameMode.TeamBattle);
                GameManager.Instance.SetTeamCount(_teamCount);
            }
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
        if (GameManager.Instance != null && _selectedGameMode == GameMode.TeamBattle)
        {
            GameManager.Instance.SetTeamCount(_teamCount);
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
        HideAllMenus();
            
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TransitionToState(GameState.Playing);
            ButtonConfirmAudio();
        }
    }

    // Lobby Settings Methods
    public void ShowLobbySettingsMenu()
    {
        if (lobbySettingsMenuUI == null)
        {
            Debug.LogWarning("Lobby Settings Menu UI is not assigned!");
            return;
        }
        
        // Only allow the host to open this menu
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("Only the host can access lobby settings!");
            return;
        }
        
        // Play button sound
        ButtonClickAudio();
        
        // Hide pause menu if it's active
        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            pauseMenuUI.SetActive(false);
        }
        
        // Show lobby settings menu
        lobbySettingsMenuUI.SetActive(true);
        
        // Set selected button if using controller
        if (controllerSelectionEnabled && EventSystem.current != null)
        {
            // Find a default button in the lobby settings menu
            Button defaultButton = lobbySettingsMenuUI.GetComponentInChildren<Button>();
            if (defaultButton != null)
            {
                EventSystem.current.SetSelectedGameObject(defaultButton.gameObject);
            }
        }
    }

    public void CloseLobbySettingsMenu()
    {
        if (lobbySettingsMenuUI == null)
            return;
        
        // Play button sound
        ButtonClickAudio();
        
        // Hide lobby settings menu
        lobbySettingsMenuUI.SetActive(false);
        
        // Show pause menu if we're in-game
        if (GameManager.Instance != null && GameManager.Instance.state != GameState.Pending)
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGameMode(mode);
        }
        
        ButtonClickAudio();
    }
    
    public void SetTeamCount(int count)
    {
        if (count < 2) count = 2;
        if (count > 4) count = 4;
        
        _teamCount = count;
        
        // Sync to GameManager if available
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetTeamCount(count);
        }
        
        ButtonClickAudio();
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
        if (lobbySettingsMenuUI != null) lobbySettingsMenuUI.SetActive(false);
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