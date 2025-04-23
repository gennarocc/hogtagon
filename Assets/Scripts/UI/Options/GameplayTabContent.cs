using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

/// <summary>
/// Handles the Gameplay settings tab content
/// </summary>
public class GameplayTabContent : TabContent
{
    [Header("Gameplay Settings")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TextMeshProUGUI warningMessageText;
    
    // Default values
    private const string DEFAULT_USERNAME = "Player";
    private const int MAX_USERNAME_LENGTH = 16;
    
    protected override void Awake()
    {
        base.Awake();
        
        // Set up input field listener
        if (usernameInput != null)
        {
            // Set character limit
            usernameInput.characterLimit = MAX_USERNAME_LENGTH;
            
            // Add listeners
            usernameInput.onValueChanged.AddListener(OnUsernameChanged);
            usernameInput.onEndEdit.AddListener(OnUsernameFinishedEditing);
        }
    }
    
    protected override void InitializeUI()
    {
        // Update UI with current settings
        if (usernameInput != null)
        {
            // Get saved username or use default
            string savedUsername = PlayerPrefs.GetString("Username", DEFAULT_USERNAME);
            usernameInput.text = savedUsername;
        }
        
        // Clear any warning messages
        if (warningMessageText != null)
        {
            warningMessageText.text = "";
        }
    }
    
    public void OnUsernameChanged(string username)
    {
        if (settingsManager != null)
        {
            // Play sound feedback - only for typing if desired
            // settingsManager.PlayUIClickSound();
        }
    }
    
    public void OnUsernameFinishedEditing(string username)
    {
        if (settingsManager != null)
        {
            // Play sound feedback when finished editing
            settingsManager.PlayUIClickSound();
        }
        
        // Save username if valid
        SaveUsername(username);
    }
    
    private void SaveUsername(string username)
    {
        // Clear any previous warning message
        if (warningMessageText != null)
        {
            warningMessageText.text = "";
        }
        
        // Validate username
        if (string.IsNullOrWhiteSpace(username))
        {
            // Reset to default or previous value if empty
            string savedUsername = PlayerPrefs.GetString("Username", DEFAULT_USERNAME);
            usernameInput.text = savedUsername;
            
            // Show warning message
            if (warningMessageText != null)
            {
                warningMessageText.text = "Username cannot be empty";
            }
            return;
        }
        
        // Trim whitespace
        username = username.Trim();
        
        // Save the username
        PlayerPrefs.SetString("Username", username);
        PlayerPrefs.Save();
        
        Debug.Log($"Username set to: {username}");
        
        // Try to update player name in ConnectionManager
        if (ConnectionManager.Instance != null && NetworkManager.Singleton != null)
        {
            // Check if the player is connected
            if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.LocalClientId != 0)
            {
                var player = ConnectionManager.Instance.GetPlayer(NetworkManager.Singleton.LocalClientId);
                if (player != null)
                {
                    Debug.Log($"Updating player name for client {NetworkManager.Singleton.LocalClientId}");
                    // Here we would set the player's name, but there's no direct method for this
                    // This would be handled by ConnectionManager in a multiplayer context
                }
                else
                {
                    string warningMsg = "Player reference not found in ConnectionManager";
                    Debug.LogWarning(warningMsg);
                    
                    // Show warning in UI
                    if (warningMessageText != null)
                    {
                        warningMessageText.text = warningMsg;
                    }
                }
            }
            else
            {
                string infoMsg = "Not connected as client, username will be used when connecting";
                Debug.Log(infoMsg);
                
                // Show info in UI
                if (warningMessageText != null)
                {
                    warningMessageText.text = infoMsg;
                }
            }
        }
    }
    
    public override void ApplySettings()
    {
        // Make sure any pending username changes are saved
        if (usernameInput != null)
        {
            SaveUsername(usernameInput.text);
        }
        
        // No other gameplay settings to apply currently
        PlayerPrefs.Save();
    }
    
    public override void ResetToDefaults()
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            settingsManager.PlayUIConfirmSound();
        }
        
        // Reset username to default
        if (usernameInput != null)
        {
            usernameInput.text = DEFAULT_USERNAME;
            SaveUsername(DEFAULT_USERNAME);
        }
    }
}