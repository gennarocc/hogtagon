using UnityEngine;
using UnityEngine.UI;

public class MainMenuPanel : BasePanel
{
    [Header("Main Menu Components")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;
    
    [Header("Audio")]
    [SerializeField] private AK.Wwise.Event MenuMusicOn;

    private MenuManager menuManager;

    protected override void Awake()
    {
        base.Awake();
        Debug.Log("MainMenuPanel Awake called");
        menuManager = GetComponentInParent<MenuManager>();
        if (menuManager == null)
        {
            Debug.LogError("MenuManager not found! Make sure MainMenuPanel is a child of the MenuManager in hierarchy.");
        }
        else
        {
            Debug.Log("MenuManager found successfully");
        }
        
        // Setup button listeners
        if (playButton != null)
        {
            Debug.Log("Play button found, adding listener");
            playButton.onClick.AddListener(OnPlayClicked);
        }
        else
        {
            Debug.LogError("Play button not assigned in inspector!");
        }
        
        if (optionsButton != null)
        {
            Debug.Log("Options button found, adding listener");
            optionsButton.onClick.AddListener(OnOptionsClicked);
        }
        else
        {
            Debug.LogError("Options button not assigned in inspector!");
        }
        
        if (quitButton != null)
        {
            Debug.Log("Quit button found, adding listener");
            quitButton.onClick.AddListener(OnQuitClicked);
        }
        else
        {
            Debug.LogError("Quit button not assigned in inspector!");
        }
    }

    protected override void OnPanelShown()
    {
        base.OnPanelShown();
        
        // Start menu music
        MenuMusicOn.Post(gameObject);

        // Switch to UI input mode
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Show cursor
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void OnPlayClicked()
    {
        Debug.Log("Play button clicked");
        if (menuManager != null)
        {
            menuManager.PlayButtonClicked();
        }
        else
        {
            Debug.LogError("Cannot handle play click - MenuManager is null!");
        }
    }

    private void OnOptionsClicked()
    {
        Debug.Log("Options button clicked");
        if (menuManager != null)
        {
            menuManager.OptionsButtonClicked();
        }
        else
        {
            Debug.LogError("Cannot handle options click - MenuManager is null!");
        }
    }

    private void OnQuitClicked()
    {
        Debug.Log("Quit button clicked");
        if (menuManager != null)
        {
            menuManager.QuitButtonClicked();
        }
        else
        {
            Debug.LogError("Cannot handle quit click - MenuManager is null!");
        }
    }
} 