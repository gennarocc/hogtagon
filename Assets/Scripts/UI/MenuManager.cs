using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using UnityEngine.EventSystems;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class MenuManager : NetworkBehaviour
{
    public static MenuManager Instance;
    public bool gameIsPaused = false;
    public bool menuMusicPlaying = false;
    public bool settingsOpenedFromPauseMenu = false;

    #region Serialized Fields

    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playMenuPanel;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private GameObject settingsMenuUI;
    [SerializeField] private GameObject newOptionsMenuUI;
    [SerializeField] private GameObject tempUI;
    [SerializeField] public GameObject jumpUI;
    [SerializeField] public GameObject lobbySettingsMenuUI;

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

    [Header("Scoreboard")]
    [SerializeField] private GameObject scoreboardUI;
    [SerializeField] private Scoreboard scoreboard;

    [Header("Default Button Selection")]
    [SerializeField] private Button defaultPauseMenuButton;
    [SerializeField] private Button defaultMainMenuButton;
    [SerializeField] private Button defaultSettingsMenuButton;
    [SerializeField] private Button defaultPlayMenuButton;

    [Header("UI Navigation")]
    [SerializeField] private TMP_InputField[] inputFields;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event MenuMusicOn;
    [SerializeField] private AK.Wwise.Event PauseOn;
    [SerializeField] private AK.Wwise.Event PauseOff;
    [SerializeField] private float rotationSpeed = 0.01f;

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
    [SerializeField] private Slider roundCountSlider;
    [SerializeField] private TextMeshProUGUI roundCountText;

    [Header("Pause Menu References")]
    [SerializeField] private Button pauseResumeButton;
    [SerializeField] private Button pauseOptionsButton;
    [SerializeField] private Button pauseLobbySettingsButton;
    [SerializeField] private Button pauseQuitButton;

    [Header("Connection Error UI")]
    [SerializeField] private TextMeshProUGUI connectionRefusedUI;

    #endregion

    #region Private Fields

    private CinemachineBrain cinemachineBrain;
    private CinemachineInputProvider cameraInputProvider;
    private CinemachineOrbitalTransposer orbitalTransposer;
    private bool controllerSelectionEnabled = false;
    private int countdownTime;
    private InputManager inputManager;
    private float lastMenuToggleTime = 0f;
    private float menuToggleCooldown = 0.5f;
    private GameMode _selectedGameMode = GameMode.FreeForAll;
    private int _teamCount = 2;
    private int _roundCount = 5;
    private readonly int[] _validRoundCounts = new int[] { 1, 3, 5, 7, 9 };
    private bool _prevLobbyMenuActiveState = false;

    #endregion

    #region Properties

    public GameMode selectedGameMode => _selectedGameMode;
    public int teamCount => _teamCount;
    public int roundCount => _roundCount;

    #endregion

    #region Initialization & Setup

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
            cameraInputProvider = playerCamera.GetComponent<CinemachineInputProvider>();
        }

        // Find the main camera's CinemachineBrain
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cinemachineBrain = mainCamera.GetComponent<CinemachineBrain>();
        }
    }

    private void OnEnable()
    {
        // Try to find InputManager if it wasn't found in Awake
        inputManager = InputManager.Instance;

        // Subscribe to InputManager events if available
        if (inputManager != null)
        {
            inputManager.MenuToggled += OnMenuToggled;
            inputManager.BackPressed += OnBackPressed;
            inputManager.ScoreboardToggled += HandleScoreboardToggle;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
            NetworkManager.Singleton.OnClientDisconnectCallback += (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from InputManager events
        if (inputManager != null)
        {
            inputManager.MenuToggled -= OnMenuToggled;
            inputManager.BackPressed -= OnBackPressed;
            inputManager.ScoreboardToggled -= HandleScoreboardToggle;
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
            NetworkManager.Singleton.OnClientDisconnectCallback -= (id) => OnPlayerCountChanged(NetworkManager.Singleton.ConnectedClients.Count);
        }
    }

    private void Start()
    {
        // Set up explicit navigation for main menu buttons
        SetupButtonNavigation();
        // Set up lobby settings button
        pauseLobbySettingsButton.onClick.RemoveAllListeners();
        pauseLobbySettingsButton.onClick.AddListener(OpenLobbySettingsMenu);
        UpdateLobbySettingsButtonState();

        // Clear selection by default
        EventSystem.current.SetSelectedGameObject(null);

        // Try to find InputManager again if it wasn't found in Awake/OnEnable
        if (inputManager == null)
            inputManager = InputManager.Instance;
        
        // Subscribe to events if we have the InputManager
        if (inputManager != null)
        {
            inputManager.MenuToggled += OnMenuToggled;
            inputManager.BackPressed += OnBackPressed;
            inputManager.ScoreboardToggled += HandleScoreboardToggle;

            // Check if input actions are enabled
            if (!inputManager.AreInputActionsEnabled())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Initialize main menu
        ShowMainMenu();
    }

    private void Update()
    {
        // Check for controller input to enable selection
        if (inputManager != null && inputManager.IsUsingGamepad)
        {
            // Using the new input system, check for gamepad navigation inputs
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                // Check if any navigation input is happening on the gamepad
                // This is key for UI navigation - detect both dpad AND leftStick
                if (gamepad.dpad.IsActuated() ||
                    gamepad.leftStick.IsActuated() ||
                    gamepad.buttonSouth.wasPressedThisFrame ||
                    gamepad.buttonEast.wasPressedThisFrame)
                {
                    // Only proceed if not already in controller selection mode
                    if (!controllerSelectionEnabled)
                    {
                        // Enable controller selection
                        controllerSelectionEnabled = true;
                        // Force selection of a default button depending on active menu
                        GameObject buttonToSelect = null;

                        if (mainMenuPanel.activeSelf && defaultMainMenuButton != null)
                            buttonToSelect = defaultMainMenuButton.gameObject;
                        else if (playMenuPanel.activeSelf && defaultPlayMenuButton != null)
                            buttonToSelect = defaultPlayMenuButton.gameObject;
                        else if (pauseMenuUI.activeSelf && defaultPauseMenuButton != null)
                            buttonToSelect = defaultPauseMenuButton.gameObject;
                        else if (settingsMenuUI.activeSelf && defaultSettingsMenuButton != null)
                            buttonToSelect = defaultSettingsMenuButton.gameObject;
                        else if (newOptionsMenuUI.activeSelf && defaultSettingsMenuButton != null)
                            buttonToSelect = defaultSettingsMenuButton.gameObject;

                        // Set selected game object
                        if (buttonToSelect != null && EventSystem.current != null)
                        {
                            EventSystem.current.SetSelectedGameObject(null);
                            EventSystem.current.SetSelectedGameObject(buttonToSelect);
                        }
                    }
                }
            }
        }
        else if (Mouse.current != null && (Mouse.current.delta.ReadValue().x != 0 || Mouse.current.delta.ReadValue().y != 0))
        {
            // If mouse moved, reset controller selection and clear highlighting
            controllerSelectionEnabled = false;
        }

        // Handle text input fields
        HandleTextInput();
    }

    #endregion

    #region Input Mode Switching

    /// <summary>
    /// Unified method to switch between UI and gameplay input modes
    /// </summary>
    /// <param name="toUIMode">True to switch to UI mode, false to switch to gameplay mode</param>
    /// <param name="disableCameraInput">Optional: Whether to disable camera input (defaults to same as toUIMode)</param>
    /// <param name="forceButtonSelection">Optional: Button to select after switching (for UI mode only)</param>
    public void SwitchInputMode(bool toUIMode, bool? disableCameraInput = null, Button forceButtonSelection = null)
    {
        Debug.Log($"[MENU] SwitchInputMode called. ToUIMode: {toUIMode}");

        // Determine if we should disable camera input (defaults to match UI mode)
        bool shouldDisableCameraInput = disableCameraInput ?? toUIMode;

        // Handle camera input
        if (shouldDisableCameraInput)
        {
            DisableCameraInput();
        }
        else
        {
            EnableCameraInput();
        }

        // Let InputManager handle the input mode switch and initial cursor state
        if (inputManager != null)
        {
            if (toUIMode)
            {
                inputManager.SwitchToUIMode();
                if (!inputManager.IsInUIMode())
                    inputManager.ForceEnableCurrentActionMap();
            }
            else
            {
                inputManager.SwitchToGameplayMode();
                if (!inputManager.IsInGameplayMode())
                    inputManager.ForceEnableCurrentActionMap();
            }
        }

        // Double-check cursor state as a fallback (InputManager should handle this primarily)
        // This ensures proper cursor state even if InputManager fails
        Cursor.visible = toUIMode;
        Cursor.lockState = toUIMode ? CursorLockMode.None : CursorLockMode.Locked;

        // Handle button selection for controller navigation
        if (toUIMode && forceButtonSelection != null && forceButtonSelection.gameObject.activeInHierarchy)
        {
            HandleButtonSelection(forceButtonSelection);
        }
    }

    #endregion

    #region Menu State Management

    public void ShowMainMenu()
    {
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
        playButton.interactable = true;
        optionsButton.interactable = true;
        quitButton.interactable = true;

        // Restore main menu camera priority
        menuCamera.Priority = 20;
        orbitalTransposer = menuCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
        if (orbitalTransposer != null)
            orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;

        // Switch to UI input mode
        SwitchInputMode(toUIMode: true, forceButtonSelection: defaultMainMenuButton);

        gameIsPaused = false;  // Reset pause state

        EventSystem.current.SetSelectedGameObject(null);
    }

    public void OnPlayClicked()
    {
        HideAllMenusExcept(playMenuPanel);
        playMenuPanel.SetActive(true);
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

        // Lower the priority of the menu camera
        menuCamera.Priority = 0;

        // Switch to UI input mode
        SwitchInputMode(toUIMode: true, forceButtonSelection: defaultPlayMenuButton);
    }

    public void OnOptionsClicked()
    {
        Debug.Log("[MENU] OnOptionsClicked called");
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        // Reset menu state flags - we're opening from main menu
        settingsOpenedFromPauseMenu = false;
        gameIsPaused = false;

        // Call the main Settings method to handle the menu transition
        Settings();
    }

    public void Settings()
    {
        Debug.Log("[MENU] Settings method called. gameIsPaused=" + gameIsPaused);

        // Store whether we opened this from pause menu for later
        settingsOpenedFromPauseMenu = gameIsPaused;
        Debug.Log("[MENU] Setting settingsOpenedFromPauseMenu to " + settingsOpenedFromPauseMenu);

        // Play sound feedback
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

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

            // Switch to UI mode
            SwitchInputMode(toUIMode: true, forceButtonSelection: defaultSettingsMenuButton);

            // Enable controller selection for navigation
            controllerSelectionEnabled = true;
        }
        else if (settingsMenuUI != null)
        {
            // Fallback to old settings menu
            HideAllMenusExcept(settingsMenuUI);
            settingsMenuUI.SetActive(true);

            // Switch to UI mode
            SwitchInputMode(toUIMode: true, forceButtonSelection: defaultSettingsMenuButton);
        }
    }

    public void ReturnFromSettingsMenu()
    {
        // Log current resolution and settings
        Resolution currentResolution = Screen.currentResolution;
        bool isFullScreen = Screen.fullScreen;
        Debug.Log($"[MENU] ReturnFromSettingsMenu: Current resolution: {Screen.width}x{Screen.height}, fullscreen: {isFullScreen}");

        // Disable settings UI panels
        if (settingsMenuUI != null && settingsMenuUI.activeSelf)
        {
            settingsMenuUI.SetActive(false);
        }
        if (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf)
        {
            newOptionsMenuUI.SetActive(false);
        }

        // Check if settings were opened from pause menu
        if (settingsOpenedFromPauseMenu)
        {
            // Return to pause menu
            Debug.Log($"[MENU] Settings were opened from pause menu. Returning to pause menu.");
            pauseMenuUI.SetActive(true);

            // Ensure we're still paused
            gameIsPaused = true;
            Time.timeScale = 0f;

            // Handle button selection for controller navigation
            SwitchInputMode(toUIMode: true, forceButtonSelection: defaultPauseMenuButton);

            // Make sure the Resume button will actually work by ensuring the EventSystem is properly set up
            if (pauseMenuUI != null)
            {
                Button resumeButton = pauseMenuUI.GetComponentsInChildren<Button>()
                    .FirstOrDefault(b => b.name.Contains("Resume"));

                if (resumeButton != null)
                {
                    // Ensure the Resume button's onClick listeners are properly set up
                    if (resumeButton.onClick.GetPersistentEventCount() == 0)
                    {
                        resumeButton.onClick.AddListener(Resume);
                    }
                }
            }
        }
        else
        {
            // Return to main menu
            Debug.Log($"[MENU] Settings were opened from main menu. Returning to main menu.");
            ShowMainMenu();
        }

        // Enable main camera if it was disabled
        if (Camera.main != null && !Camera.main.enabled)
        {
            Camera.main.enabled = true;
        }

        // Log final resolution and settings
        Resolution finalResolution = Screen.currentResolution;
        bool finalIsFullScreen = Screen.fullScreen;
    }

    private void HideAllMenusExcept(GameObject menuToKeep)
    {
        if (mainMenuPanel != menuToKeep) mainMenuPanel.SetActive(false);
        if (playMenuPanel != menuToKeep) playMenuPanel.SetActive(false);
        if (pauseMenuUI != menuToKeep) pauseMenuUI.SetActive(false);
        if (settingsMenuUI != menuToKeep) settingsMenuUI.SetActive(false);
        if (newOptionsMenuUI != menuToKeep) newOptionsMenuUI.SetActive(false);
        if (scoreboardUI != menuToKeep) scoreboardUI.SetActive(false);
        if (tempUI != menuToKeep) tempUI.SetActive(false);
        if (connectionPending != menuToKeep) connectionPending.SetActive(false);
        if (lobbySettingsMenuUI != menuToKeep) lobbySettingsMenuUI.SetActive(false);
    }

    private void HideAllMenus()
    {
        HideAllMenusExcept(null);
    }

    private void HideAllMenusIncludingLobby()
    {
        HideAllMenus();
        lobbySettingsMenuUI.SetActive(false);
    }

    #endregion

    #region Game Flow Control

    public void Resume()
    {
        Debug.Log("[MENU] Resume called");

        // Hide all menus
        HideAllMenus();

        // Reset pause state
        gameIsPaused = false;

        // Reset time scale to normal
        Time.timeScale = 1f;

        // Switch to gameplay mode
        SwitchInputMode(toUIMode: false);

        PauseOff.Post(gameObject);
    }

    void Pause()
    {
        Debug.Log("[MENU] Pause called");

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

        // Switch to UI mode
        SwitchInputMode(toUIMode: true, forceButtonSelection: defaultPauseMenuButton);

        // Set pause state
        gameIsPaused = true;

        // Enable all child components explicitly
        foreach (Transform child in pauseMenuUI.transform)
        {
            child.gameObject.SetActive(true);
        }

        // Play sound effect if available
        PauseOn.Post(gameObject);
    }

    public void StartGame()
    {
        // Close lobby settings menu if it's open
        if (lobbySettingsMenuUI.activeSelf)
        {
            lobbySettingsMenuUI.SetActive(false);
        }

        // Hide all UI panels
        HideAllMenusIncludingLobby();

        // Reset pause state
        gameIsPaused = false;

        // Switch to gameplay mode
        SwitchInputMode(toUIMode: false);

        // Start the game - use TransitionToState instead of StartGame
        GameManager.Instance.TransitionToState(GameState.Start);
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
    }

    public void QuitGame()
    {
        Debug.Log("[MENU] Quitting Game");
        Application.Quit();
    }

    #endregion

    #region Networking

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

        // Play button sound
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        PauseOff.Post(gameObject);
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectRequestServerRpc(ulong clientId)
    {
        Debug.Log(message: "[MENU] Disconnecting Client - " + clientId + " [" + ConnectionManager.Instance.GetClientUsername(clientId) + "]");
        NetworkManager.Singleton.DisconnectClient(clientId);
    }

    public void HandleConnectionStateChange(bool connected)
    {
        ConnectionManager.Instance.isConnected = connected;
    }

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
        if (lobbySettingsMenuUI.activeSelf)
        {
            UpdateConnectedPlayersText();
            UpdateStartGameButtonInteractability();
        }
    }

    #endregion

    #region UI Notifications and Display

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
    public void DisplayGameWinnerClientRpc(ulong winnerClientId, bool isGameWin = false)
    {
        tempUI.SetActive(true);

        // Get colored text for individual player winner
        string coloredPlayerName = ConnectionManager.Instance.GetPlayerColoredName(winnerClientId);

        if (isGameWin)
        {
            // Final game winner message
            winnerText.text = $"{coloredPlayerName} CONQUERED THE HOGTAGON!";
        }
        else
        {
            // Round winner - select a random message
            string[] roundWinMessages = new string[]
            {
            "TAKES THIS ROUND",
            "CLAIMS THIS ONE",
            "WINS THE ROUND",
            "TAKES THIS ONE",
            "PREVAILS THIS ROUND"
            };

            int randomIndex = UnityEngine.Random.Range(0, roundWinMessages.Length);
            winnerText.text = $"{coloredPlayerName} {roundWinMessages[randomIndex]}";
        }
    }

    [ClientRpc]
    public void DisplayTeamWinnerClientRpc(int teamNumber, bool isGameWin = false)
    {
        tempUI.SetActive(true);

        // Get team name and color
        string teamName = GameManager.Instance.GetTeamName(teamNumber);
        Color teamColor = GameManager.Instance.GetTeamColor(teamNumber);

        // Apply team color to text
        string coloredTeamName = $"<color=#{ColorUtility.ToHtmlStringRGB(teamColor)}>{teamName} TEAM</color>";

        if (isGameWin)
        {
            // Final game winner message
            winnerText.text = $"{coloredTeamName} IS THE BIG WINNER!";
        }
        else
        {
            // Round winner - select a random message
            string[] roundWinMessages = new string[]
            {
            "TAKES THIS ROUND",
            "CLAIMS THIS ONE",
            "WINS THE ROUND",
            "TAKES THIS ONE",
            "PREVAILS THIS ROUND"
            };

            int randomIndex = UnityEngine.Random.Range(0, roundWinMessages.Length);
            winnerText.text = $"{coloredTeamName} {roundWinMessages[randomIndex]}";
        }
    }

    public void DisplayHostAloneMessage(string disconnectedPlayerName)
    {
        tempUI.SetActive(true);

        // Use the winner text component to display the message
        winnerText.text = $"{disconnectedPlayerName} disconnected.\nYou are the only player remaining.\nWaiting for more players to join...";

        // Hide message after a few seconds
        StartCoroutine(HideHostAloneMessage(8f));
    }

    private IEnumerator HideHostAloneMessage(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Hide the message
        if (tempUI.activeSelf)
            tempUI.SetActive(false);

        winnerText.text = "";
    }

    public void DisplayConnectionError(string error)
    {
        // Update the error text
        connectionRefusedUI.text = error;

        // Switch to UI mode
        SwitchInputMode(toUIMode: true);

        // Hide connection pending UI if it's active
        if (connectionPending.activeSelf)
        {
            connectionPending.SetActive(false);
        }

        // Play error sound
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);

        Debug.LogWarning($"[MENU] Connection Error: {error}");
    }

    #endregion

    #region Scoreboard

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

    private void HandleScoreboardToggle(bool show)
    {
        if (ConnectionManager.Instance.isConnected)
        {
            scoreboardUI.SetActive(show);

            if (show && scoreboard != null)
                scoreboard.UpdatePlayerList();
        }
    }

    #endregion

    #region Input Handling

    private void OnMenuToggled()
    {
        // First check if the lobby settings menu is open - if so, just close it
        if (lobbySettingsMenuUI.activeSelf)
        {
            Debug.Log("[MENU] Closing lobby settings menu via escape/start button");
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
            Debug.Log("[MENU] Menu toggle cooldown in effect");
            return;
        }

        lastMenuToggleTime = Time.unscaledTime;

        // Check if settings menu is active
        bool settingsActive = (settingsMenuUI != null && settingsMenuUI.activeSelf) ||
                             (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf);

        // If settings is active, close it and show pause menu
        if (settingsActive)
        {
            settingsMenuUI.SetActive(false);
            newOptionsMenuUI.SetActive(false);

            // Force show pause menu
            pauseMenuUI.SetActive(true);

            // Enable all child components
            foreach (Transform child in pauseMenuUI.transform)
            {
                child.gameObject.SetActive(true);
            }

            // Switch to UI mode
            SwitchInputMode(toUIMode: true, forceButtonSelection: defaultPauseMenuButton);

            gameIsPaused = true;
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
                SwitchInputMode(toUIMode: true);
            }
        }
    }

    private void OnBackPressed()
    {
        // Handle menus in priority order
        if (lobbySettingsMenuUI.activeSelf)
        {
            CloseLobbySettingsMenu();
            return;
        }

        bool isInSettings = (settingsMenuUI != null && settingsMenuUI.activeSelf) ||
                          (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf);

        if (isInSettings)
        {
            HideAllMenusExcept(settingsOpenedFromPauseMenu ? pauseMenuUI : mainMenuPanel);
            SwitchInputMode(toUIMode: true, forceButtonSelection: settingsOpenedFromPauseMenu ? defaultPauseMenuButton : defaultMainMenuButton);
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

    private void HandleTextInput()
    {
        // Check if any input field is currently selected

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>();
        }
    }

    // Methods to disable/enable controller input
    private void DisableControllerInput()
    {
        if (inputManager != null)
        {
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

    #endregion

    #region UI Selection Utilities

    // Add this method for handling button selection
    private void HandleButtonSelection(Button defaultButton)
    {
        // If using controller and selection is enabled, select the default button
        if (inputManager != null && inputManager.IsUsingGamepad && controllerSelectionEnabled)
        {
            if (defaultButton != null && defaultButton.gameObject.activeInHierarchy && defaultButton.isActiveAndEnabled)
            {
                // Clear current selection first to prevent any side effects
                EventSystem.current.SetSelectedGameObject(null);

                // Set the new selection after a small delay to ensure clean state
                StartCoroutine(SelectButtonDelayed(defaultButton, 0.05f));
            }
        }
        else
        {
            // Otherwise, clear selection to prevent automatic highlighting
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private IEnumerator SelectButtonDelayed(Button button, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (button != null && button.gameObject.activeInHierarchy && button.isActiveAndEnabled)
        {
            EventSystem.current.SetSelectedGameObject(button.gameObject);

            // Force refresh the navigation
            if (button == playButton || button == optionsButton || button == quitButton)
            {
                SetupButtonNavigation();
            }
        }
    }

    #endregion

    #region Camera Control

    // Method to directly disable camera input on the player prefab by nulling out the input references
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
                Debug.Log("[MENU] Disabled input reference on camera: " + camera.name);
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

    #endregion

    #region Lobby Settings

    public void OpenLobbySettingsMenu()
    {
        // Don't open if we're not connected or if we're in the process of disconnecting
        if (!ConnectionManager.Instance.isConnected ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        Debug.Log("[MENU] OpenLobbySettingsMenu called");

        // Switch to UI mode
        SwitchInputMode(toUIMode: true);
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIConfirm);

        if (pauseMenuUI.activeSelf)
        {
            Debug.Log("[MENU] Hiding pause menu before showing lobby settings");
            pauseMenuUI.SetActive(false);
        }

        // First activate the GameObject
        lobbySettingsMenuUI.SetActive(true);
        _prevLobbyMenuActiveState = true;
    }

    public void UpdateLobbySettingsButtonState()
    {
        bool shouldBeVisible = GameManager.Instance.state != GameState.Playing;
        pauseLobbySettingsButton.gameObject.SetActive(shouldBeVisible);
        Debug.Log($"[MENU] Updated lobby settings button visibility: {shouldBeVisible}");
    }

    private void UpdateGameModeDisplay()
    {
        // Update the value text if it exists
        gameModeValueText.text = _selectedGameMode.ToString();

        // Update team settings visibility based on game mode
        SetTeamSettingsVisibility(_selectedGameMode == GameMode.TeamBattle);
    }

    public void OnGameModeDirectionClicked(bool isLeft)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

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

    private void SetTeamSettingsVisibility(bool visible)
    {
        teamSettingsPanel.SetActive(visible);
    }

    private void UpdateTeamCountText()
    {
        teamCountText.text = _teamCount.ToString() + " Teams";
    }

    private void UpdateConnectedPlayersText()
    {
        int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
        connectedPlayersText.text = "Connected Players: " + playerCount +
            (playerCount < 2 ? "\n(Need at least 2 players to start)" : "");
    }

    private void UpdateStartGameButtonInteractability()
    {
        bool canStart = NetworkManager.Singleton.ConnectedClients.Count >= 2;
        startGameFromLobbyButton.interactable = canStart;
    }

    public void CloseLobbySettingsMenu()
    {
        Debug.Log("[MENU] CloseLobbySettingsMenu called");

        if (lobbySettingsMenuUI == null)
            return;

        // Play button sound
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

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
        Debug.Log("[MENU] Returning to pause menu");

        // Show pause menu and set up UI
        pauseMenuUI.SetActive(true);
        SwitchInputMode(toUIMode: true, forceButtonSelection: defaultPauseMenuButton);
    }

    public void ShowPauseMenu()
    {
        // Directly use the ReturnToPauseMenu method to ensure consistent behavior
        ReturnToPauseMenu();

        // Make sure we're marked as paused
        gameIsPaused = true;
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

        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
    }

    #endregion
}

// Helper class to store and restore camera input references
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
            Debug.Log("[MENU] Stored input reference for camera: " + camera.name);
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
                    Debug.Log("[MENU] Restored input reference for camera: " + reference.Camera.name);
                }
            }
        }
    }
}