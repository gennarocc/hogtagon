using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using UnityEngine.EventSystems;
using System.Linq;

public class MenuManager : NetworkBehaviour
{
    public bool gameIsPaused = false;

    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playMenuPanel;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private GameObject settingsMenuUI;
    [SerializeField] private GameObject newOptionsMenuUI;
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
        // Handle text input fields
        HandleTextInput();
    }

    // Event handlers for input system callbacks
    private void OnMenuToggled()
    {
        // Don't toggle if we're in main menu
        if (mainMenuPanel.activeSelf)
            return;

        // Toggle pause state
        if (gameIsPaused)
            Resume();
        else
            Pause();
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
                defaultPauseMenuButton.Select();
        }
        else if (newOptionsMenuUI != null && newOptionsMenuUI.activeSelf)
        {
            // Close the new options menu and return to main menu
            newOptionsMenuUI.SetActive(false);
            
            // Reset all button states in main menu
            if (mainMenuPanel.GetComponent<ButtonStateResetter>() != null)
                mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
            
            // Show main menu
            mainMenuPanel.SetActive(true);
            
            // Clear any selected objects in the event system
            if (eventSystem != null)
                eventSystem.SetSelectedGameObject(null);
            
            // Reset the button states directly to ensure they're clickable
            if (optionsButton != null)
            {
                optionsButton.OnPointerExit(new UnityEngine.EventSystems.PointerEventData(eventSystem));
                optionsButton.interactable = true;
            }
            
            if (playButton != null)
                playButton.interactable = true;
            
            if (quitButton != null)
                quitButton.interactable = true;
            
            // Reset the selections
            HandleButtonSelection(defaultMainMenuButton);
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

        // Switch to UI input mode
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Always show cursor in main menu
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
    }

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

    public void Resume()
    {
        // Reset button states before disabling menus
        if (pauseMenuUI.activeSelf && pauseMenuUI.GetComponent<ButtonStateResetter>() != null)
            pauseMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();

        if (settingsMenuUI.activeSelf && settingsMenuUI.GetComponent<ButtonStateResetter>() != null)
            settingsMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();

        // Disable UI elements
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        gameIsPaused = false;

        // Play sound
        PauseOff.Post(gameObject);

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
            inputManager.SwitchToUIMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Handle button selection based on input
        HandleButtonSelection(defaultPauseMenuButton);
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

            if (settingsMenuUI.activeSelf)
                HandleButtonSelection(defaultSettingsMenuButton);
            else
                HandleButtonSelection(defaultPauseMenuButton);
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
}