using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class PauseMenuPanel : BasePanel
{
    [Header("UI Components")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Audio")]
    [SerializeField] private AK.Wwise.Event PauseOn;
    [SerializeField] private AK.Wwise.Event PauseOff;

    private MenuManager menuManager;

    protected override void Awake()
    {
        base.Awake();
        menuManager = GetComponentInParent<MenuManager>();

        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumeClicked);
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);
        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
    }

    protected override void OnPanelShown()
    {
        base.OnPanelShown();
        
        // Update cursor state
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Play pause sound
        PauseOn.Post(gameObject);

        // Switch to UI input mode
        if (inputManager != null)
        {
            inputManager.SwitchToUIMode();
            if (inputManager.IsInGameplayMode())
                inputManager.ForceEnableCurrentActionMap();
        }

        // Update start game button visibility based on game state and host status
        if (startGameButton != null)
        {
            bool canStartGame = NetworkManager.Singleton != null &&
                              NetworkManager.Singleton.IsServer &&
                              NetworkManager.Singleton.ConnectedClients.Count > 1 &&
                              GameManager.instance != null &&
                              GameManager.instance.state == GameState.Pending;
            startGameButton.gameObject.SetActive(canStartGame);
            startGameButton.interactable = canStartGame;
        }
    }

    protected override void OnPanelHidden()
    {
        base.OnPanelHidden();

        // Reset button states
        if (GetComponent<ButtonStateResetter>() != null)
            GetComponent<ButtonStateResetter>().ResetAllButtonStates();

        // Play unpause sound
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

    private void OnStartGameClicked()
    {
        if (menuManager != null)
            menuManager.StartGameButtonClicked();
    }

    public void OnResumeClicked()
    {
        if (menuManager != null)
            menuManager.ResumeButtonClicked();
    }

    public void OnSettingsClicked()
    {
        if (menuManager != null)
            menuManager.SettingsButtonClicked();
    }

    public void OnMainMenuClicked()
    {
        if (menuManager != null)
            menuManager.MainMenuButtonClicked();
    }
} 