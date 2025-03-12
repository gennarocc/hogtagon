using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;

public class MenuManager : NetworkBehaviour
{
    public bool gameIsPaused = false;

    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playMenuPanel;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private GameObject settingsMenuUI;
    [SerializeField] private GameObject tempUI;

    [SerializeField] public GameObject jumpUI;

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

    private void Awake()
    {
        // Find or get the InputManager
        inputManager = InputManager.Instance;
        if (inputManager == null)
        {
            Debug.LogError("InputManager not found in scene!");
        }
    }

    private void OnEnable()
    {
        Debug.Log("MenuManager OnEnable - trying to subscribe to input events");

        // Try to find InputManager if it wasn't found in Awake
        if (inputManager == null)
        {
            inputManager = InputManager.Instance;
            Debug.Log("Looking for InputManager: " + (inputManager != null ? "Found" : "Not found"));
        }

        if (inputManager != null)
        {
            Debug.Log("MenuManager OnEnable - subscribing to input events");

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

            Debug.Log("Successfully subscribed to InputManager events");
        }
        else
        {
            Debug.LogError("Cannot subscribe to input events - inputManager is null!");
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

        // Try to find InputManager again if it wasn't found in Awake/OnEnable
        if (inputManager == null)
        {
            inputManager = InputManager.Instance;

            if (inputManager != null)
            {
                // Subscribe to events if we just found the InputManager
                Debug.Log("Found InputManager in Start, subscribing to events");
                inputManager.MenuToggled += OnMenuToggled;
                inputManager.BackPressed += OnBackPressed;
                inputManager.AcceptPressed += OnAcceptPressed;
                inputManager.ScoreboardToggled += HandleScoreboardToggle;
            }
            else
            {
                Debug.LogError("InputManager still not found in Start!");
            }
        }

        // Check if input actions are enabled
        if (inputManager != null)
        {
            bool enabled = inputManager.AreInputActionsEnabled();
            Debug.Log($"Input actions enabled check result: {enabled}");

            if (!enabled)
            {
                Debug.LogWarning("Input actions are not enabled! Forcing UI mode...");
                inputManager.ForceEnableCurrentActionMap();
            }
        }

        // Initialize main menu
        ShowMainMenu();

        startCamera.cullingMask = 31;

        // Get camera reference if not set
        if (virtualCamera == null)
        {
            virtualCamera = GetComponent<CinemachineVirtualCamera>();
        }
    }


    private void Update()
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
            if (ConnectionManager.instance.joinCode != null)
                joinCodeText.text = "Code: " + ConnectionManager.instance.joinCode;
        }
    }

    // Event handlers for input system callbacks
    private void OnMenuToggled()
    {
        Debug.Log("Menu Toggle event received in MenuManager");

        // Don't toggle if we're in main menu
        if (mainMenuPanel.activeSelf)
        {
            Debug.Log("Main menu is active, ignoring menu toggle");
            return;
        }

        // Toggle pause state
        if (gameIsPaused)
        {
            Debug.Log("Game is paused, resuming");
            Resume();
        }
        else
        {
            Debug.Log("Game is not paused, pausing");
            Pause();
        }
    }

    private void OnBackPressed()
    {
        if (playMenuPanel.activeSelf)
        {
            // Reset button states before disabling menus
            mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
            ShowMainMenu();
        }
        else if (pauseMenuUI.activeSelf && !settingsMenuUI.activeSelf)
        {
            Resume();
        }
        else if (settingsMenuUI.activeSelf)
        {
            settingsMenuUI.SetActive(false);
            pauseMenuUI.SetActive(true);
            if (defaultPauseMenuButton != null)
            {
                defaultPauseMenuButton.Select();
            }
        }
    }

    private void OnAcceptPressed()
    {
        // Handle accept button presses if needed
    }

    public void ShowMainMenu()
    {
        mainMenuPanel.SetActive(true);
        startCamera.gameObject.SetActive(true);
        MenuMusicOn.Post(gameObject);

        // Rotate main menu camera
        orbitalTransposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
        if (orbitalTransposer != null)
        {
            orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;
        }

        playMenuPanel.SetActive(false);
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        scoreboardUI.SetActive(false);
        tempUI.SetActive(false);
        connectionPending.SetActive(false);

        // Switch to UI input mode
        if (inputManager != null)
        {
            inputManager.SwitchToUIMode();
        }

        // Always show cursor in main menu
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        gameIsPaused = false;  // Reset pause state

        // Set default selection
        if (defaultMainMenuButton != null)
        {
            defaultMainMenuButton.Select();
        }
    }

    public void OnPlayClicked()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(true);

        // Lower the priority of the menu camera
        if (virtualCamera != null && virtualCamera.GetComponent<CinemachineVirtualCamera>() != null)
        {
            virtualCamera.GetComponent<CinemachineVirtualCamera>().Priority = 0;
        }

        // Make sure we're in UI mode for the play menu
        if (inputManager != null)
        {
            inputManager.SwitchToUIMode();
        }

        // Set default selection
        if (defaultPlayMenuButton != null)
        {
            defaultPlayMenuButton.Select();
        }
    }

    public void OnOptionsClicked()
    {
        ButtonClickAudio();
        settingsMenuUI.SetActive(true);

        // Set appropriate default selection
        if (settingsMenuUI.activeSelf && defaultSettingsMenuButton != null)
        {
            defaultSettingsMenuButton.Select();
        }
    }

    public void Resume()
    {
        Debug.Log("Resume called");

        // Reset button states before disabling menus
        if (pauseMenuUI.activeSelf && pauseMenuUI.GetComponent<ButtonStateResetter>() != null)
        {
            pauseMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
        }
        if (settingsMenuUI.activeSelf && settingsMenuUI.GetComponent<ButtonStateResetter>() != null)
        {
            settingsMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
        }

        // Disable UI elements
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        gameIsPaused = false;

        // Play sound if appropriate
        PauseOff.Post(gameObject);

        // Switch to gameplay input mode
        if (inputManager != null)
        {
            Debug.Log("Resuming: Switching to gameplay input mode");
            inputManager.SwitchToGameplayMode();

            // Verify the switch happened
            bool inGameplayMode = inputManager.IsInGameplayMode();
            Debug.Log($"After Resume: IsInGameplayMode = {inGameplayMode}");

            if (!inGameplayMode)
            {
                Debug.LogError("Failed to switch to gameplay mode! Forcing switch...");
                inputManager.ForceEnableCurrentActionMap();
            }
        }
        else
        {
            Debug.LogError("Cannot switch input mode - inputManager is null!");
        }

        // Lock cursor for gameplay
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Pause()
    {
        Debug.Log("Pause called");

        // Update cursor state
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Show pause menu
        pauseMenuUI.SetActive(true);
        gameIsPaused = true;
        PauseOn.Post(gameObject);

        // Switch to UI input mode
        if (inputManager != null)
        {
            Debug.Log("Pausing: Switching to UI input mode");
            inputManager.SwitchToUIMode();

            // Verify the switch happened
            bool inGameplayMode = inputManager.IsInGameplayMode();
            Debug.Log($"After Pause: IsInGameplayMode = {inGameplayMode}");

            if (inGameplayMode)
            {
                Debug.LogError("Failed to switch to UI mode! Forcing switch...");
                inputManager.ForceEnableCurrentActionMap();
            }
        }
        else
        {
            Debug.LogError("Cannot switch input mode - inputManager is null!");
        }

        // Set default button selection
        if (defaultPauseMenuButton != null)
        {
            defaultPauseMenuButton.Select();
        }
        else
        {
            Debug.LogWarning("No default pause menu button assigned!");
        }
    }

    public void StartGame()
    {
        if (IsServer) GameManager.instance.TransitionToState(GameState.Playing);
        Resume(); // This will also switch to gameplay mode
    }

    public void Settings()
    {
        if (gameIsPaused)
        {
            settingsMenuUI.SetActive(!settingsMenuUI.activeSelf);
            pauseMenuUI.SetActive(!pauseMenuUI.activeSelf);

            // Set appropriate default selection
            if (settingsMenuUI.activeSelf && defaultSettingsMenuButton != null)
            {
                defaultSettingsMenuButton.Select();
            }
            else if (pauseMenuUI.activeSelf && defaultPauseMenuButton != null)
            {
                defaultPauseMenuButton.Select();
            }
        }
        else
        {
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
        // Enable the scoreboard panel
        scoreboardUI.SetActive(true);

        // Update the scoreboard data
        scoreboard.UpdatePlayerList();
    }

    [ClientRpc]
    public void HideScoreboardClientRpc()
    {
        // Disable the scoreboard panel
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
        // Display a message in the existing UI
        tempUI.SetActive(true);

        // Use the winner text component to display the message
        if (winnerText != null)
        {
            winnerText.text = $"{disconnectedPlayerName} disconnected.\nYou are the only player remaining.\nWaiting for more players to join...";
        }

        // Hide message after a few seconds (optional)
        StartCoroutine(HideHostAloneMessage(8f)); // 8 seconds seems reasonable
    }

    private IEnumerator HideHostAloneMessage(float delay)
    {
        yield return new WaitForSeconds(delay);

        // Hide the message
        if (tempUI != null && tempUI.activeSelf)
        {
            tempUI.SetActive(false);
        }

        if (winnerText != null)
        {
            winnerText.text = "";
        }
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
        Debug.Log($"Connection state changed: {connected}");

        // Update connection state
        ConnectionManager.instance.isConnected = connected;

        if (connected)
        {
            // CRITICAL FIX: When a player connects and is spawned into the world,
            // immediately switch to gameplay mode so they can control their car
            if (inputManager != null)
            {
                Debug.Log("Player connected: Switching to GAMEPLAY input mode immediately");
                inputManager.SwitchToGameplayMode();

                // Verify switch happened
                bool inGameplayMode = inputManager.IsInGameplayMode();
                Debug.Log($"After connection: IsInGameplayMode = {inGameplayMode}");

                if (!inGameplayMode)
                {
                    Debug.LogError("Failed to switch to gameplay mode on connection! Forcing switch...");
                    inputManager.ForceEnableCurrentActionMap();
                }

                // Lock cursor for gameplay
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Debug.LogError("Cannot switch input mode - inputManager is null!");
            }
        }
        else
        {
            // When disconnected, switch to UI mode and show main menu
            if (inputManager != null)
            {
                inputManager.SwitchToUIMode();
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            ShowMainMenu();
        }
    }

    private void HandleScoreboardToggle(bool show)
    {
        Debug.Log($"HandleScoreboardToggle({show})");

        // Call your existing method
        if (ConnectionManager.instance.isConnected)
        {
            scoreboardUI.SetActive(show);

            if (show && scoreboard != null)
            {
                scoreboard.UpdatePlayerList();
            }
        }
    }
}