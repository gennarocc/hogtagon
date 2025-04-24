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
    public static MenuManager Instance;
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

    [Header("Cameras")]
    [SerializeField] public CinemachineVirtualCamera menuCamera;
    private CinemachineBrain cinemachineBrain;
    private CinemachineInputProvider cameraInputProvider;
    private CinemachineOrbitalTransposer orbitalTransposer;

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
    [SerializeField] private float rotationSpeed = 0.01f;

    // Reference to Input Manager
    private InputManager inputManager;

    // Add a timestamp to track when the menu was last toggled
    private float lastMenuToggleTime = 0f;
    private float menuToggleCooldown = 0.5f;

    // Add tracking for whether settings was opened from pause menu
    public bool settingsOpenedFromPauseMenu = false;

    [Header("Lobby Settings")]
    [SerializeField] private TextMeshProUGUI lobbyCodeDisplay;
    [SerializeField] private Button lobbyCodeCopyButton;
    [SerializeField] private Button startGameFromLobbyButton;
    [SerializeField] private TextMeshProUGUI connectedPlayersText;
    [SerializeField] private TextMeshProUGUI gameModeValueText;
    [SerializeField] private Button gameModeLeftButton;
    [SerializeField] private Button gameModeRightButton;
    [SerializeField] private GameObject teamSettingsPanel;
    [SerializeField] private Slider teamCountSlider;
    [SerializeField] private TextMeshProUGUI teamCountText;
    [SerializeField] private Slider roundCountSlider;  // Add reference to round count slider
    [SerializeField] private TextMeshProUGUI roundCountText;  // Add reference to round count text

    // Game mode settings
    private GameMode _selectedGameMode = GameMode.FreeForAll;
    private int _teamCount = 2;
    private int _roundCount = 5;  // Default to 5 rounds
    private readonly int[] _validRoundCounts = new int[] { 1, 3, 5, 7, 9 };  // Valid round count values
    public bool menuMusicPlaying = false;

    // Game Mode enum
    public enum GameMode { FreeForAll, TeamBattle }

    // Public properties for game mode settings
    public GameMode selectedGameMode => _selectedGameMode;
    public int teamCount => _teamCount;
    public int roundCount => _roundCount;  // Add public property for round count

    [Header("Pause Menu References")]
    [SerializeField] private Button pauseResumeButton;
    [SerializeField] private Button pauseOptionsButton;
    [SerializeField] private Button pauseLobbySettingsButton; // Reference to the Lobby Settings button in pause menu
    [SerializeField] private Button pauseQuitButton;

    // Diagnostic code to help identify what's disabling the lobby settings menu
    private bool _prevLobbyMenuActiveState = false;

    [Header("Connection Error UI")]
    [SerializeField] private GameObject connectionRefusedUI;

    private void Awake()
    {
        // Set singleton instance
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Find the input manager
        inputManager = InputManager.Instance;

        // Find the player's camera input provider
        var playerCameras = FindObjectsByType<CinemachineFreeLook>(FindObjectsSortMode.None);
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

        // Subscribe to player count changes
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
            NetworkManager.Singleton.OnClientDisconnectCallback += (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
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

        // Unsubscribe from player count changes
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
            NetworkManager.Singleton.OnClientDisconnectCallback -= (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
        }
    }

    private void Start()
    {
        // Get reference to EventSystem if not assigned
        if (eventSystem == null)
            eventSystem = EventSystem.current;

        // Set up explicit navigation for main menu buttons
        SetupButtonNavigation();

        // Set up lobby settings button
        if (pauseLobbySettingsButton != null)
        {
            pauseLobbySettingsButton.onClick.RemoveAllListeners();
            pauseLobbySettingsButton.onClick.AddListener(OpenLobbySettingsMenu);
            UpdateLobbySettingsButtonState();
            Debug.Log("[MenuManager] Connected Lobby Settings button: " + pauseLobbySettingsButton.name);
        }
        else
        {
            Debug.LogWarning("[MenuManager] Lobby Settings button reference is missing!");
        }

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
    }

    private void Update()
    {
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

        // Handle text input fields
        HandleTextInput();
    }

    // Add event handler for player count changes
    private void OnPlayerCountChanged(int newCount)
    {
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
    }

    public void Resume()
    {
        Debug.Log("[MenuManager] Resume called");

        // Hide all menus
        HideAllMenus();

        // Reset pause state
        gameIsPaused = false;
        
        // Reset time scale to normal
        Time.timeScale = 1f;

        // Lock cursor when resuming - do this BEFORE switching input modes
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Enable camera input
        EnableCameraInput();

        PauseOff.Post(gameObject);

        // Switch back to gameplay input mode
        if (inputManager != null)
        {
            inputManager.SwitchToGameplayMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Double-check cursor lock in case input mode change affected it
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Pause()
    {
        Debug.Log("[MenuManager] Pause called");

        // Only pause the game if your in the game
        if (!ConnectionManager.Instance.isConnected) return;

        // Hide all menus except pause menu
        HideAllMenusExcept(pauseMenuUI);
        pauseMenuUI.SetActive(true);

        // Update lobby settings button state
        if (pauseLobbySettingsButton != null && GameManager.Instance != null)
        {
            pauseLobbySettingsButton.interactable = GameManager.Instance.state != GameState.Playing;
        }

        // Disable camera input - this is key to stopping camera rotation
        DisableCameraInput();

        // Set pause state AFTER disabling camera
        gameIsPaused = true;

        // Double-check pause menu is actually active
        if (!pauseMenuUI.activeSelf)
        {
            pauseMenuUI.SetActive(true);
        }

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

        // Play sound effect if available
        PauseOn.Post(gameObject);

        inputManager.SwitchToUIMode();
        if (inputManager.IsInGameplayMode())
            inputManager.ForceEnableCurrentActionMap();

        // Double-check cursor state after input mode switch
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    public void StartGame()
    {
        // Close lobby settings menu if it's open
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            lobbySettingsMenuUI.SetActive(false);
        }

        // Hide all UI panels
        HideAllMenusIncludingLobby();

        // Reset pause state
        gameIsPaused = false;

        // Ensure cursor is locked for gameplay
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Enable camera input for gameplay
        EnableCameraInput();

        // Switch to gameplay mode for input
        if (inputManager != null)
        {
            inputManager.SwitchToGameplayMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Start the game - use TransitionToState instead of StartGame
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TransitionToState(GameState.Playing);
            ButtonConfirmAudio();
        }
    }

    public void Settings()
    {
        Debug.Log("Settings method called. gameIsPaused=" + gameIsPaused);

        // Store whether we opened this from pause menu for later
        settingsOpenedFromPauseMenu = gameIsPaused;
        Debug.Log("Setting settingsOpenedFromPauseMenu to " + settingsOpenedFromPauseMenu);

        // Play sound feedback
        ButtonClickAudio();

        // Hide current menu
        if (gameIsPaused)
        {
            pauseMenuUI.SetActive(false);
        }

        // Show settings menu with new SettingsManager
        if (newOptionsMenuUI != null)
        {
            // Hide all other menus and show the new options menu
            HideAllMenusExcept(newOptionsMenuUI);
            newOptionsMenuUI.SetActive(true);

            // Ensure cursor is visible for UI interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Switch to UI input mode
            if (inputManager != null)
            {
                inputManager.SwitchToUIMode();
                if (inputManager.IsInUIMode())
                    inputManager.ForceEnableCurrentActionMap();
            }

            // Enable controller selection for navigation
            controllerSelectionEnabled = true;

            // Find and select the default button
            if (defaultSettingsMenuButton != null)
            {
                HandleButtonSelection(defaultSettingsMenuButton);
            }
        }
        else if (settingsMenuUI != null)
        {
            // Fallback to old settings menu
            HideAllMenusExcept(settingsMenuUI);
            settingsMenuUI.SetActive(true);

            if (defaultSettingsMenuButton != null)
            {
                HandleButtonSelection(defaultSettingsMenuButton);
            }
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

        // Find client ID for the player name
        ulong? winnerClientId = null;
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.GetClientUsername(clientId) == player)
            {
                winnerClientId = clientId;
                break;
            }
        }

        // Get colored text if client ID was found
        string coloredPlayerName = winnerClientId.HasValue
            ? ConnectionManager.Instance.GetPlayerColoredName(winnerClientId.Value)
            : player;

        winnerText.text = coloredPlayerName + " won the round";
        StartCoroutine(BetweenRoundTime());
    }

    [ClientRpc]
    public void DisplayGameWinnerClientRpc(ulong roundWinnerClientId, PlayerData winner)
    {
        tempUI.SetActive(true);

        // Get colored text if client ID was found
        string coloredPlayerName = ConnectionManager.Instance.GetPlayerColoredName(roundWinnerClientId);

        winnerText.text = $"{coloredPlayerName} IS THE BIG WINNER!";

        // Make sure cursor is visible for UI interaction
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Play celebration sound if available
        ButtonConfirmAudio();
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

    public void Disconnect()
    {
        // First, ensure all menus are closed
        HideAllMenusIncludingLobby();
        _prevLobbyMenuActiveState = false;  // Reset the state tracking
        settingsOpenedFromPauseMenu = false;  // Reset the settings menu state tracking
        gameIsPaused = false;  // Reset pause state

        // Explicitly deactivate settings menus
        if (settingsMenuUI != null)
            settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != null)
            newOptionsMenuUI.SetActive(false);

        if (IsServer)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            DisconnectRequestServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        // Reset all menu states and show main menu
        ShowMainMenu();

        // Ensure cursor is visible for main menu
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Reset connection state
        ConnectionManager.Instance.isConnected = false;

        // Play button sound
        ButtonClickAudio();
        PauseOff.Post(gameObject);
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
        // Update the error text
        connectionRefusedReasonText.text = error;

        // Make sure the error panel is visible
        if (connectionRefusedUI != null)
        {
            connectionRefusedUI.SetActive(true);
        }

        // Ensure the cursor is visible for button interaction
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Hide connection pending UI if it's active
        if (connectionPending != null && connectionPending.activeSelf)
        {
            connectionPending.SetActive(false);
        }

        // Play error sound
        uiCancel.Post(gameObject);

        Debug.LogWarning($"Connection Error: {error}");
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
        // Handle menus in priority order
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            CloseLobbySettingsMenu();
            return;
        }

        bool isInSettings = (settingsMenuUI != null && settingsMenuUI.activeSelf) ||
                          (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf);

        if (isInSettings)
        {
            HideAllMenusExcept(settingsOpenedFromPauseMenu ? pauseMenuUI : mainMenuPanel);
            HandleButtonSelection(settingsOpenedFromPauseMenu ? defaultPauseMenuButton : defaultMainMenuButton);
            return;
        }

        if (playMenuPanel.activeSelf)
        {
            ShowMainMenu();
            return;
        }

        if (pauseMenuUI.activeSelf)
        {
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

    public void ShowMainMenu()
    {
        // First reset the button states in the main menu
        if (mainMenuPanel.GetComponent<ButtonStateResetter>() != null)
            mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();

        // Make the main menu active and hide all others
        HideAllMenusExcept(mainMenuPanel);
        mainMenuPanel.SetActive(true);
        menuCamera.gameObject.SetActive(true);
        if (!menuMusicPlaying)
        {
            MenuMusicOn.Post(gameObject);
            menuMusicPlaying = true;
        }

        // Make sure all buttons are interactable
        if (playButton != null) playButton.interactable = true;
        if (optionsButton != null) optionsButton.interactable = true;
        if (quitButton != null) quitButton.interactable = true;

        // Restore main menu camera priority
        menuCamera.Priority = 20;
        orbitalTransposer = menuCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
        if (orbitalTransposer != null)
            orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;

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

        // Handle button selection based on input
        HandleButtonSelection(defaultMainMenuButton);
    }

    // Method for when the Play button is clicked
    public void OnPlayClicked()
    {
        HideAllMenusExcept(playMenuPanel);
        playMenuPanel.SetActive(true);

        // Lower the priority of the menu camera
        if (menuCamera != null && menuCamera.GetComponent<CinemachineVirtualCamera>() != null)
            menuCamera.GetComponent<CinemachineVirtualCamera>().Priority = 0;

        // Make sure we're in UI mode for the play menu
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Handle button selection based on input
        HandleButtonSelection(defaultPlayMenuButton);
    }

    // Method for when the Options button is clicked
    public void OnOptionsClicked()
    {
        Debug.Log("[MenuManager] OnOptionsClicked called");

        // Reset menu state flags - we're opening from main menu
        settingsOpenedFromPauseMenu = false;
        gameIsPaused = false;

        // Call the main Settings method to handle the menu transition
        Settings();
    }

    private void SetupTabController(TabController tabController)
    {
        // This method is no longer needed since the SettingsManager handles tab setup
        // Keep the method for backwards compatibility but don't perform any actions
    }

    // Method to open Lobby Settings Menu (called when host creates lobby or from pause menu)
    public void OpenLobbySettingsMenu()
    {
        // Don't open if we're not connected or if we're in the process of disconnecting
        if (!ConnectionManager.Instance.isConnected ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        Debug.Log("[MenuManager] OpenLobbySettingsMenu called");

        DisableCameraInput();

        // Make sure cursor is visible and unlocked for menu interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            Debug.Log("[MenuManager] Hiding pause menu before showing lobby settings");
            pauseMenuUI.SetActive(false);
        }

        if (lobbySettingsMenuUI != null)
        {
            // First activate the GameObject
            lobbySettingsMenuUI.SetActive(true);
            _prevLobbyMenuActiveState = true;

            // Switch to UI input mode
            if (inputManager != null)
            {
                inputManager.SwitchToUIMode();
                if (inputManager.IsInGameplayMode())
                    inputManager.ForceEnableCurrentActionMap();
            }

            // Double-check cursor state after input mode switch
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // Method to update lobby settings button state when game state changes
    public void UpdateLobbySettingsButtonState()
    {
        if (pauseLobbySettingsButton != null && GameManager.Instance != null)
        {
            bool shouldBeVisible = GameManager.Instance.state != GameState.Playing;
            pauseLobbySettingsButton.gameObject.SetActive(shouldBeVisible);
            Debug.Log($"[MenuManager] Updated lobby settings button visibility: {shouldBeVisible}");
        }
    }

    // Method to hide all menus except the specified one
    private void HideAllMenusExcept(GameObject menuToKeep)
    {
        if (mainMenuPanel != null && mainMenuPanel != menuToKeep) mainMenuPanel.SetActive(false);
        if (playMenuPanel != null && playMenuPanel != menuToKeep) playMenuPanel.SetActive(false);
        if (pauseMenuUI != null && pauseMenuUI != menuToKeep) pauseMenuUI.SetActive(false);
        if (settingsMenuUI != null && settingsMenuUI != menuToKeep) settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != null && newOptionsMenuUI != menuToKeep) newOptionsMenuUI.SetActive(false);
        if (scoreboardUI != null && scoreboardUI != menuToKeep) scoreboardUI.SetActive(false);
        if (tempUI != null && tempUI != menuToKeep) tempUI.SetActive(false);
        if (connectionPending != null && connectionPending != menuToKeep) connectionPending.SetActive(false);
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI != menuToKeep) lobbySettingsMenuUI.SetActive(false);
    }

    // Method to hide all menus
    private void HideAllMenus()
    {
        HideAllMenusExcept(null);
    }


    // Lobby Settings Methods
    public void ShowLobbySettingsMenu()
    {
        Debug.Log("ShowLobbySettingsMenu called - showing lobby settings menu!");

        // CRITICAL: First hide the pause menu if it's active
        if (pauseMenuUI != null && pauseMenuUI.activeSelf)
        {
            Debug.Log("[MenuManager] Hiding pause menu before showing lobby settings");
            pauseMenuUI.SetActive(false);
        }

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

    // Method to update game mode display text
    private void UpdateGameModeDisplay()
    {
        // Update the value text if it exists
        if (gameModeValueText != null)
        {
            gameModeValueText.text = _selectedGameMode.ToString();
        }

        // Update team settings visibility based on game mode
        SetTeamSettingsVisibility(_selectedGameMode == GameMode.TeamBattle);
    }

    // Method to handle game mode selection in either direction
    public void OnGameModeDirectionClicked(bool isLeft)
    {
        ButtonClickAudio();

        // Get the current mode index and total number of modes
        int currentIndex = (int)_selectedGameMode;
        int totalModes = System.Enum.GetValues(typeof(GameMode)).Length;
        // Calculate new index based on direction (left = -1, right = 1)
        int direction = isLeft ? -1 : 1;
        int newIndex = (currentIndex + direction + totalModes) % totalModes;
        GameMode newMode = (GameMode)newIndex;

        // Update local game mode
        _selectedGameMode = newMode;

        // Update game settings if GameManager exists
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetGameMode(newMode);

            // If switching to Team Battle, also set team count
            if (newMode == GameMode.TeamBattle)
            {
                GameManager.Instance.SetTeamCount(_teamCount);
            }
        }

        // Update UI display
        UpdateGameModeDisplay();
    }

    // Method to control visibility of team settings panel
    private void SetTeamSettingsVisibility(bool visible)
    {
        if (teamSettingsPanel != null)
        {
            teamSettingsPanel.SetActive(visible);
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

    // Modified version to hide all menus when we WANT to hide lobby settings too
    private void HideAllMenusIncludingLobby()
    {
        HideAllMenus();
        if (lobbySettingsMenuUI != null) lobbySettingsMenuUI.SetActive(false);
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

        // Clean up any running coroutines or invokes
        LobbySettingsPanel panel = lobbySettingsMenuUI.GetComponentInChildren<LobbySettingsPanel>();
        if (panel != null)
        {
            panel.StopAllCoroutines();
            panel.CancelInvoke();
        }

        // Hide lobby settings menu
        lobbySettingsMenuUI.SetActive(false);

        // Prevent immediate menu toggle
        lastMenuToggleTime = Time.unscaledTime;

        // Determine if we should return to pause menu
        bool shouldReturnToPause = ShouldReturnToPauseMenu();

        if (shouldReturnToPause)
        {
            ReturnToPauseMenu();
        }
        else
        {
            Resume();
        }
    }

    private bool ShouldReturnToPauseMenu()
    {
        // If we're not in game mode, don't return to pause
        if (GameManager.Instance == null || GameManager.Instance.state != GameState.Playing)
            return false;

        // If we're not connected, don't return to pause
        if (!ConnectionManager.Instance.isConnected)
            return false;

        // If we're not paused, don't return to pause
        if (!gameIsPaused)
            return false;

        // If we're closing with escape key, return to gameplay
        if (Time.frameCount == Time.renderedFrameCount)
            return false;

        return true;
    }

    private void ReturnToPauseMenu()
    {
        Debug.Log("[MenuManager] Returning to pause menu");

        // Keep camera input disabled for pause menu
        DisableCameraInput();

        // Show pause menu and set up UI
        pauseMenuUI.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Set selected button if using controller
        if (controllerSelectionEnabled && EventSystem.current != null && defaultPauseMenuButton != null)
        {
            EventSystem.current.SetSelectedGameObject(defaultPauseMenuButton.gameObject);
        }
    }

    /// <summary>
    /// Public method to show the pause menu that other scripts can call
    /// </summary>
    public void ShowPauseMenu()
    {
        // Directly use the ReturnToPauseMenu method to ensure consistent behavior
        ReturnToPauseMenu();

        // Make sure we're marked as paused
        gameIsPaused = true;
    }

    // Game Mode Setting Methods
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


    // Event handlers for input system callbacks
    private void OnMenuToggled()
    {
        // First check if the lobby settings menu is open - if so, just close it
        if (lobbySettingsMenuUI != null && lobbySettingsMenuUI.activeSelf)
        {
            Debug.Log("[MenuManager] Closing lobby settings menu via escape/start button");
            CloseLobbySettingsMenu();
            // Important: Set flag that we're handling this toggle and prevent the pause menu 
            // from appearing immediately on the same press
            lastMenuToggleTime = Time.unscaledTime;
            return;
        }

        // Don't toggle if we're in main menu
        if (mainMenuPanel.activeSelf)
        {
            return;
        }

        if (!ConnectionManager.Instance.isConnected) return;
        // Apply cooldown to prevent rapid toggling
        if (Time.unscaledTime - lastMenuToggleTime < menuToggleCooldown)
        {
            Debug.Log("[MenuManager] Menu toggle cooldown in effect");
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

            gameIsPaused = true;

            // Ensure cursor is visible in pause menu
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // Normal pause toggle logic
        if (gameIsPaused)
        {
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

    /// <summary>
    /// Public method for returning from settings menu
    /// Called by SettingsManager when Back button is clicked
    /// </summary>
    public void ReturnFromSettingsMenu()
    {

        // Log resolution at start
        Debug.Log($"[MenuManager] ReturnFromSettingsMenu start: Resolution is {Screen.width}x{Screen.height}, fullscreen: {Screen.fullScreen}");

        // Get the saved resolution values from PlayerPrefs
        int savedWidth = PlayerPrefs.GetInt("screenWidth", Screen.width);
        int savedHeight = PlayerPrefs.GetInt("screenHeight", Screen.height);
        bool savedFullscreen = PlayerPrefs.GetInt("fullscreen", Screen.fullScreen ? 1 : 0) == 1;
        
        // Disable settings panels
        settingsMenuUI.SetActive(false);
        newOptionsMenuUI.SetActive(false);
        
        // Log after disabling panels
        Debug.Log($"[MenuManager] After disabling panels: Resolution is {Screen.width}x{Screen.height}, fullscreen: {Screen.fullScreen}");
        
        // DO NOT try to apply the resolution here since SettingsManager already did it
        // Just log the expected resolution for debugging purposes
        Debug.Log($"[MenuManager] Expected resolution from PlayerPrefs: {savedWidth}x{savedHeight}, fullscreen: {savedFullscreen}");
        
        // Check if settings were opened from pause menu
        if (settingsOpenedFromPauseMenu)
        {
            // Return to pause menu
            Debug.Log($"[MenuManager] Settings were opened from pause menu. Returning to pause menu.");
            pauseMenuUI.SetActive(true);
            
            // Ensure we're still paused
            gameIsPaused = true;
            Time.timeScale = 0f;
            
            // Handle button selection for controller navigation
            if (defaultPauseMenuButton != null)
            {
                HandleButtonSelection(defaultPauseMenuButton);
            }
        }
        else
        {
            // Return to main menu
            Debug.Log($"[MenuManager] Settings were opened from main menu. Returning to main menu.");
            mainMenuPanel.SetActive(true);
            
            // Handle button selection for controller navigation
            if (defaultMainMenuButton != null)
            {
                HandleButtonSelection(defaultMainMenuButton);
            }
        }
        
        // Enable main camera if it was disabled
        var mainCamera = Camera.main;
        if (mainCamera != null && !mainCamera.enabled)
        {
            mainCamera.enabled = true;
        }

        // Final log
        Debug.Log($"[MenuManager] ReturnFromSettingsMenu end: Resolution is {Screen.width}x{Screen.height}, fullscreen: {Screen.fullScreen}");
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