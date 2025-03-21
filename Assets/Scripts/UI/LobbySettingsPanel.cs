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
    [SerializeField] private Toggle freeForAllToggle;
    [SerializeField] private Toggle teamBattleToggle;
    [SerializeField] private GameObject teamSettingsPanel;
    [SerializeField] private Slider teamCountSlider;
    [SerializeField] private TextMeshProUGUI teamCountText;
    
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
    }
    
    private void OnEnable()
    {
        // Setup UI
        SetupButtons();
        UpdateUI();
        
        // Schedule periodic UI updates
        StartCoroutine(PeriodicUIUpdate());
    }
    
    private void OnDisable()
    {
        // Stop all coroutines when disabled
        StopAllCoroutines();
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
        // Only allow host to use this panel
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("LobbySettingsPanel is only usable by the host");
            gameObject.SetActive(false);
            return;
        }
        
        // Setup Start Game button
        if (startGameButton != null)
        {
            SetupButtonColors(startGameButton);
            AddHoverHandlers(startGameButton.gameObject, originalStartButtonScale);
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGameClicked);
            UpdateStartButtonInteractability();
        }
        
        // Setup Close button
        if (closeButton != null)
        {
            SetupButtonColors(closeButton);
            AddHoverHandlers(closeButton.gameObject, originalCloseButtonScale);
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(OnCloseClicked);
        }
        
        // Setup Copy Code button
        if (copyCodeButton != null)
        {
            SetupButtonColors(copyCodeButton);
            AddHoverHandlers(copyCodeButton.gameObject, originalCopyButtonScale);
            copyCodeButton.onClick.RemoveAllListeners();
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);
        }
        
        // Setup Game Mode toggles
        if (freeForAllToggle != null)
        {
            freeForAllToggle.onValueChanged.RemoveAllListeners();
            freeForAllToggle.onValueChanged.AddListener(OnFreeForAllToggleChanged);
        }
        
        if (teamBattleToggle != null)
        {
            teamBattleToggle.onValueChanged.RemoveAllListeners();
            teamBattleToggle.onValueChanged.AddListener(OnTeamBattleToggleChanged);
        }
        
        // Setup Team Count slider
        if (teamCountSlider != null)
        {
            teamCountSlider.onValueChanged.RemoveAllListeners();
            teamCountSlider.onValueChanged.AddListener(OnTeamCountChanged);
            teamCountSlider.minValue = 2;
            teamCountSlider.maxValue = 4;
            teamCountSlider.wholeNumbers = true;
        }
    }
    
    private void UpdateUI()
    {
        // Update lobby code
        if (lobbyCodeText != null && ConnectionManager.instance != null)
        {
            lobbyCodeText.text = "Lobby Code: " + ConnectionManager.instance.joinCode;
        }
        
        // Update player count
        UpdatePlayerCount();
        
        // Set team settings visibility based on current game mode
        if (GameManager.instance != null && menuManager != null)
        {
            // Set toggle states based on current game mode
            if (freeForAllToggle != null && teamBattleToggle != null)
            {
                bool isTeamBattle = menuManager.selectedGameMode == MenuManager.GameMode.TeamBattle;
                teamBattleToggle.isOn = isTeamBattle;
                freeForAllToggle.isOn = !isTeamBattle;
                
                // Show/hide team settings
                if (teamSettingsPanel != null)
                {
                    teamSettingsPanel.SetActive(isTeamBattle);
                }
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
            menuManager.StartGameFromLobbySettings();
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
        if (menuManager != null && ConnectionManager.instance != null && 
            !string.IsNullOrEmpty(ConnectionManager.instance.joinCode))
        {
            menuManager.ButtonClickAudio();
            GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
            
            // Show feedback (could add a temporary text popup)
            Debug.Log("Lobby code copied to clipboard: " + ConnectionManager.instance.joinCode);
            
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
    
    // Toggle Event Handlers
    public void OnFreeForAllToggleChanged(bool isOn)
    {
        if (isOn && menuManager != null)
        {
            // Update UI
            if (teamSettingsPanel != null)
                teamSettingsPanel.SetActive(false);
                
            // Make sure team battle toggle is off
            if (teamBattleToggle != null && teamBattleToggle.isOn)
                teamBattleToggle.isOn = false;
                
            // Update game settings
            menuManager.SetGameMode(MenuManager.GameMode.FreeForAll);
        }
    }
    
    public void OnTeamBattleToggleChanged(bool isOn)
    {
        if (isOn && menuManager != null)
        {
            // Update UI
            if (teamSettingsPanel != null)
                teamSettingsPanel.SetActive(true);
                
            // Make sure free for all toggle is off
            if (freeForAllToggle != null && freeForAllToggle.isOn)
                freeForAllToggle.isOn = false;
                
            // Update game settings
            menuManager.SetGameMode(MenuManager.GameMode.TeamBattle);
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
} 