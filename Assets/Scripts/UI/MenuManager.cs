using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using UnityEngine.EventSystems;

public class MenuManager : NetworkBehaviour
{
    public bool gameIsPaused = false;

    [Header("Panels")]
    public MainMenuPanel mainMenuPanel;
    public PlayMenuPanel playMenuPanel;
    public PauseMenuPanel pauseMenuPanel;
    public SettingsPanel settingsPanel;
    [SerializeField] private GameObject tempUI;
    [SerializeField] public GameObject jumpUI;

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

    [Header("Camera")]
    [SerializeField] private CinemachineVirtualCamera virtualCamera;
    [SerializeField] private float rotationSpeed = 0.01f;
    private CinemachineOrbitalTransposer orbitalTransposer;
    private bool isCameraRotating = false;

    // Reference to Input Manager
    private InputManager inputManager;

    private void Awake()
    {
        // Find or get the InputManager
        inputManager = InputManager.Instance;
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
        Debug.Log("MenuManager Start");
        
        // Get reference to EventSystem if not assigned
        if (eventSystem == null)
        {
            eventSystem = EventSystem.current;
            Debug.Log("Found EventSystem: " + (eventSystem != null));
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
                Debug.Log("InputManager found and events subscribed");
            }
            else
            {
                Debug.LogWarning("InputManager not found!");
            }
        }

        // Check if input actions are enabled
        if (inputManager != null && !inputManager.AreInputActionsEnabled())
        {
            inputManager.ForceEnableCurrentActionMap();
            Debug.Log("Forced enable input action map");
        }

        // Initialize cameras
        if (startCamera != null)
        {
            startCamera.gameObject.SetActive(true);
            startCamera.cullingMask = 31;
            Debug.Log($"Start camera initialized: {startCamera.name}");
        }
        else
        {
            Debug.LogError("Start camera reference is missing!");
        }

        // Initialize virtual camera
        if (virtualCamera == null)
        {
            virtualCamera = GetComponent<CinemachineVirtualCamera>();
            Debug.Log($"Found virtual camera: {virtualCamera.name}");
        }

        if (virtualCamera != null)
        {
            orbitalTransposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (orbitalTransposer != null)
            {
                Debug.Log("Found orbital transposer component");
            }
            else
            {
                Debug.LogWarning("Virtual camera is missing Orbital Transposer component!");
            }
        }
        else
        {
            Debug.LogWarning("Virtual camera reference is missing!");
        }

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
                if (mainMenuPanel != null && mainMenuPanel.gameObject.activeSelf && defaultMainMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultMainMenuButton.gameObject);
                else if (playMenuPanel != null && playMenuPanel.gameObject.activeSelf && defaultPlayMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultPlayMenuButton.gameObject);
                else if (pauseMenuPanel != null && pauseMenuPanel.gameObject.activeSelf && defaultPauseMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultPauseMenuButton.gameObject);
                else if (settingsPanel != null && settingsPanel.gameObject.activeSelf && defaultSettingsMenuButton != null)
                    eventSystem.SetSelectedGameObject(defaultSettingsMenuButton.gameObject);
            }
        }
        else if (Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0)
        {
            // If mouse moved, reset controller selection and clear highlighting
            controllerSelectionEnabled = false;
            ClearSelection();
        }

        if (mainMenuPanel != null && !mainMenuPanel.gameObject.activeSelf)  // Only check these when not in main menu
        {
            // Start Game Button (Host only)
            // Enable start button only if:
            // 1. We are the host
            // 2. We have multiple players
            // 3. We are in Pending state
            if (startGameButton != null && NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer &&
                NetworkManager.Singleton.ConnectedClients.Count > 1 &&
                GameManager.instance != null &&
                GameManager.instance.state == GameState.Pending)
            {
                startGameButton.interactable = true;
            }
            else if (startGameButton != null)
            {
                startGameButton.interactable = false;
            }

            // Set join code
            if (joinCodeText != null && ConnectionManager.instance != null && ConnectionManager.instance.joinCode != null)
                joinCodeText.text = "Code: " + ConnectionManager.instance.joinCode;
        }
        // Handle text input fields
        HandleTextInput();
    }

    // Event handlers for input system callbacks
    private void OnMenuToggled()
    {
        // Don't toggle if we're in main menu or if game is pending
        if (mainMenuPanel != null && mainMenuPanel.gameObject.activeSelf)
            return;
            
        if (GameManager.instance != null && GameManager.instance.state == GameState.Pending)
            return;

        // Toggle pause state
        if (gameIsPaused)
            Resume();
        else
            Pause();
    }

    private void OnBackPressed()
    {
        // Don't process back button if game is pending
        if (GameManager.instance != null && GameManager.instance.state == GameState.Pending)
            return;

        if (playMenuPanel != null && playMenuPanel.gameObject.activeSelf)
        {
            // Reset button states before disabling menus
            if (mainMenuPanel != null) mainMenuPanel.ResetAllButtonStates();
            ShowMainMenu();
        }
        else if (pauseMenuPanel != null && pauseMenuPanel.gameObject.activeSelf && 
                (settingsPanel == null || !settingsPanel.gameObject.activeSelf))
        {
            Resume();
        }
        else if (settingsPanel != null && settingsPanel.gameObject.activeSelf)
        {
            settingsPanel.Hide();
            if (gameIsPaused && pauseMenuPanel != null)
                pauseMenuPanel.Show();
        }
    }

    private void OnAcceptPressed()
    {
        // Handle accept button presses if needed
    }

    public void ShowMainMenu()
    {
        Debug.Log("Showing main menu");
        HideAllPanels();
        if (mainMenuPanel != null) mainMenuPanel.Show();
        if (startCamera != null)
        {
            startCamera.gameObject.SetActive(true);
            startCamera.cullingMask = 31;
        }
        if (MenuMusicOn != null) MenuMusicOn.Post(gameObject);

        // Start camera rotation for main menu
        StartCameraRotation();

        // Switch to UI input mode
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Always show cursor in main menu
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        gameIsPaused = false;  // Reset pause state

        // Handle button selection based on input
        HandleButtonSelection(defaultMainMenuButton);
    }

    private void HideAllPanels()
    {
        if (mainMenuPanel != null) mainMenuPanel.Hide();
        if (playMenuPanel != null) playMenuPanel.Hide();
        if (pauseMenuPanel != null) pauseMenuPanel.Hide();
        if (settingsPanel != null) settingsPanel.Hide();
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
        if (tempUI != null) tempUI.SetActive(false);
        if (jumpUI != null) jumpUI.SetActive(false);
        if (connectionPending != null) connectionPending.SetActive(false);
    }

    public void PlayButtonClicked()
    {
        Debug.Log("Play button clicked - transitioning to play menu");
        ButtonClickAudio();
        
        // Hide main menu and show play menu
        if (mainMenuPanel != null) 
        {
            mainMenuPanel.Hide();
            Debug.Log("Main menu hidden");
        }
        if (playMenuPanel != null) 
        {
            playMenuPanel.Show();
            Debug.Log("Play menu shown");
        }

        // Ensure camera keeps rotating
        if (!isCameraRotating)
        {
            StartCameraRotation();
            Debug.Log("Ensuring camera rotation continues in play menu");
        }

        // Switch to UI mode for the play menu
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Handle button selection based on input
        HandleButtonSelection(defaultPlayMenuButton);
    }

    public void OptionsButtonClicked()
    {
        ButtonClickAudio();
        if (settingsPanel != null) settingsPanel.Show();

        // Set appropriate default selection
        if (settingsPanel != null && settingsPanel.gameObject.activeSelf && defaultSettingsMenuButton != null)
            defaultSettingsMenuButton.Select();
    }

    public void Resume()
    {
        // Don't resume if game is pending
        if (GameManager.instance != null && GameManager.instance.state == GameState.Pending)
            return;

        // Reset button states before disabling menus
        if (pauseMenuPanel != null && pauseMenuPanel.gameObject.activeSelf)
        {
            var buttonResetter = pauseMenuPanel.GetComponent<ButtonStateResetter>();
            if (buttonResetter != null) buttonResetter.ResetAllButtonStates();
        }

        if (settingsPanel != null && settingsPanel.gameObject.activeSelf)
        {
            var buttonResetter = settingsPanel.GetComponent<ButtonStateResetter>();
            if (buttonResetter != null) buttonResetter.ResetAllButtonStates();
        }

        // Stop camera rotation when game resumes
        StopCameraRotation();

        // Disable UI elements
        HideAllPanels();
        gameIsPaused = false;

        // Play sound
        if (PauseOff != null) PauseOff.Post(gameObject);

        // Switch to gameplay input mode
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

    void Pause()
    {
        // Don't pause if game is pending
        if (GameManager.instance != null && GameManager.instance.state == GameState.Pending)
            return;

        // Update cursor state
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Show pause menu
        if (pauseMenuPanel != null) pauseMenuPanel.Show();
        gameIsPaused = true;
        if (PauseOn != null) PauseOn.Post(gameObject);

        // Switch to UI input mode
        if (inputManager != null)
        {
            inputManager.SwitchToUIMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Handle button selection based on input
        HandleButtonSelection(defaultPauseMenuButton);
    }

    public void StartGameButtonClicked()
    {
        if (IsServer && GameManager.instance != null)
        {
            // Only allow starting game from pending state
            if (GameManager.instance.state == GameState.Pending)
            {
                GameManager.instance.TransitionToState(GameState.Playing);
                // Resume will be called after countdown in GameManager.RoundCountdown()
                HideAllPanels();
            }
        }
    }

    public void SettingsButtonClicked()
    {
        if (pauseMenuPanel != null) pauseMenuPanel.Hide();
        if (settingsPanel != null) settingsPanel.Show();
    }

    public void CopyJoinCode()
    {
        if (ConnectionManager.instance != null && ConnectionManager.instance.joinCode != null)
        {
            GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
            Debug.Log(message: "Join Code Copied");
        }
    }

    [ClientRpc]
    public void StartCountdownClientRpc()
    {
        StartCoroutine(CountdownCoroutine());
    }

    private IEnumerator CountdownCoroutine()
    {
        countdownTime = 3;
        if (tempUI != null) tempUI.SetActive(true);
        if (countdownText != null) countdownText.text = countdownTime.ToString();

        while (countdownTime > 0)
        {
            yield return new WaitForSeconds(1f);
            countdownTime--;
            if (countdownText != null) countdownText.text = countdownTime.ToString();
        }

        if (countdownText != null)
        {
            countdownText.text = "Go!";
            yield return new WaitForSeconds(1f);
            countdownText.text = "";
        }
        if (tempUI != null) tempUI.SetActive(false);
    }

    [ClientRpc]
    public void DisplayWinnerClientRpc(string player)
    {
        if (tempUI != null) tempUI.SetActive(true);
        if (winnerText != null) winnerText.text = $"{player} won the round";
        StartCoroutine(HideMessageAfterDelay(5f));
    }

    private IEnumerator HideMessageAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (tempUI != null) tempUI.SetActive(false);
        if (winnerText != null) winnerText.text = "";
    }

    [ClientRpc]
    public void ShowScoreboardClientRpc()
    {
        if (scoreboard != null) scoreboard.UpdatePlayerList();
        if (scoreboardUI != null) scoreboardUI.SetActive(true);
    }

    [ClientRpc]
    public void HideScoreboardClientRpc()
    {
        if (scoreboardUI != null) scoreboardUI.SetActive(false);
    }

    public void MainMenuButtonClicked()
    {
        Disconnect();
        ShowMainMenu();
    }

    public void Disconnect()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
        else if (NetworkManager.Singleton != null)
        {
            DisconnectRequestServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        MainMenuButtonClicked();
        if (ConnectionManager.instance != null)
            ConnectionManager.instance.isConnected = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectRequestServerRpc(ulong clientId)
    {
        if (NetworkManager.Singleton != null && ConnectionManager.instance != null)
        {
            Debug.Log(message: "Disconnecting Client - " + clientId + " [" + ConnectionManager.instance.GetClientUsername(clientId) + "]");
            NetworkManager.Singleton.DisconnectClient(clientId);
        }
    }

    public void QuitButtonClicked()
    {
        Debug.Log("Quitting Game");
        Application.Quit();
    }

    public void DisplayHostAloneMessage(string disconnectedPlayerName)
    {
        if (tempUI != null) tempUI.SetActive(true);
        if (winnerText != null)
        {
            winnerText.text = $"{disconnectedPlayerName} disconnected.\nYou are the only player remaining.\nWaiting for more players to join...";
        }
        StartCoroutine(HideMessageAfterDelay(8f));
    }

    public void DisplayConnectionError(string error)
    {
        if (connectionRefusedReasonText != null)
        {
            connectionRefusedReasonText.text = error;
        }
        if (uiCancel != null) uiCancel.Post(gameObject);
    }

    public void ButtonClickAudio()
    {
        if (uiClick != null) uiClick.Post(gameObject);
    }

    public void ButtonConfirmAudio()
    {
        if (uiConfirm != null) uiConfirm.Post(gameObject);
    }

    public void ButtonCancelAudio()
    {
        if (uiCancel != null) uiCancel.Post(gameObject);
    }

    public void HandleConnectionStateChange(bool connected)
    {
        // Update connection state
        if (ConnectionManager.instance != null)
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
        if (ConnectionManager.instance != null && ConnectionManager.instance.isConnected)
        {
            if (scoreboardUI != null) scoreboardUI.SetActive(show);

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
            if (defaultButton != null && defaultButton.gameObject.activeInHierarchy && defaultButton.isActiveAndEnabled && eventSystem != null)
                eventSystem.SetSelectedGameObject(defaultButton.gameObject);
        }
        else if (eventSystem != null)
        {
            // Otherwise, clear selection to prevent automatic highlighting
            ClearSelection();
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

    public void ResumeButtonClicked()
    {
        Resume();
    }

    public void MainMenu()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else if (NetworkManager.Singleton != null)
        {
            DisconnectRequestServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        ShowMainMenu();
        if (ConnectionManager.instance != null)
            ConnectionManager.instance.isConnected = false;
    }

    public void OnPlayClicked()
    {
        PlayButtonClicked();
    }

    private void StartCameraRotation()
    {
        if (virtualCamera != null && orbitalTransposer != null)
        {
            orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;
            isCameraRotating = true;
            Debug.Log($"Started camera rotation with speed: {rotationSpeed}");
        }
    }

    private void StopCameraRotation()
    {
        if (virtualCamera != null && orbitalTransposer != null)
        {
            orbitalTransposer.m_XAxis.m_InputAxisValue = 0f;
            isCameraRotating = false;
            Debug.Log("Stopped camera rotation");
        }
    }
}