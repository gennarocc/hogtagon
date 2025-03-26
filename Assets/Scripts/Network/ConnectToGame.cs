using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using System.Text;
using TMPro;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay.Models;
using Unity.Services.Relay;
using UnityEngine.UI;
using UnityEditor;
using System.Collections;

public class ConnectToGame : MonoBehaviour
{
    [SerializeField] private Camera startCamera;
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button hostLobby;
    [SerializeField] private Button joinLobby;
    [SerializeField] private GameObject connectionPending;
    [SerializeField] private Button retryButton;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event MenuMusicOff;

    // To track which operation we're performing for retry functionality
    public string lastJoinCode { get; private set; } = "";
    private bool isRetrying = false;

    private async void Start()
    {
        startCamera.cullingMask = 31;
        joinLobby.interactable = false;
        // Start Relay Service.
        InitializationOptions hostOptions = new InitializationOptions().SetProfile("host");
        InitializationOptions clientOptions = new InitializationOptions().SetProfile("client");
        await UnityServices.InitializeAsync();
        AuthenticationService.Instance.SignedIn += () =>
        {
            Debug.Log(message: "Signed in " + AuthenticationService.Instance.PlayerId);
        };
        if (AuthenticationService.Instance.IsAuthorized)
        {
            Debug.Log("Authorized");
            AuthenticationService.Instance.SignOut();
            await UnityServices.InitializeAsync(clientOptions);
        }
        await AuthenticationService.Instance.SignInAnonymouslyAsync();

    }

    public void OnInputFieldValueChanged()
    {
        if (joinCodeInput.text.Length == 6)
        {
            joinLobby.interactable = true;
        }
        else
        {
            joinLobby.interactable = false;
        }

        // Always keep host button enabled since we now use PlayerPrefs for username
        hostLobby.interactable = true;
    }

    private async void CreateRelay()
    {
        try
        {
            Debug.Log("Creating Relay allocation...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(8);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            ConnectionManager.instance.joinCode = joinCode;
            Debug.Log(message: "Join Code: " + joinCode);
            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay service error: {e.Message}");
            
            // Check for specific error types
            bool isNetworkError = e.Message.Contains("network") || e.Message.Contains("connection") || e.Message.Contains("timeout");
            bool isAuthError = e.Message.Contains("auth") || e.Message.Contains("token") || e.Message.Contains("unauthorized");
            bool isRateLimitError = e.Message.Contains("rate") || e.Message.Contains("limit") || e.Message.Contains("too many");
            
            string userMessage = "Failed to create lobby. ";
            
            if (isNetworkError)
            {
                userMessage += "Please check your internet connection and try again.";
            }
            else if (isAuthError)
            {
                userMessage += "Authentication issue. Please restart the game.";
            }
            else if (isRateLimitError)
            {
                userMessage += "You're creating lobbies too quickly. Please wait a moment.";
            }
            else
            {
                userMessage += e.Message;
            }
            
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError(userMessage);
            
            // Don't automatically go back to main menu, let the user decide
            // menuManager.MainMenu();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error creating lobby: {e.Message}");
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError("Unexpected error creating lobby. Please try again.");
            
            // Don't automatically go back to main menu, let the user decide
            // menuManager.MainMenu();
        }
    }

    private async void JoinRelay(string joinCode)
    {
        try
        {
            Debug.Log(message: "Joining Relay with " + joinCode);
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));
            NetworkManager.Singleton.StartClient();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Relay join error: {e.Message}");
            
            // Check for specific error types
            bool isInvalidCode = e.Message.Contains("not found") || e.Message.Contains("invalid") || e.Message.Contains("allocation");
            bool isNetworkError = e.Message.Contains("network") || e.Message.Contains("connection") || e.Message.Contains("timeout");
            bool isFullLobby = e.Message.Contains("full") || e.Message.Contains("capacity") || e.Message.Contains("maximum");
            
            string userMessage = "Failed to join lobby. ";
            
            if (isInvalidCode)
            {
                userMessage += "Invalid or expired join code. Please check the code and try again.";
            }
            else if (isNetworkError)
            {
                userMessage += "Please check your internet connection and try again.";
            }
            else if (isFullLobby)
            {
                userMessage += "The lobby is full. Please try a different lobby.";
            }
            else
            {
                userMessage += "No lobby found with that code.";
            }
            
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError(userMessage);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error joining lobby: {e.Message}");
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError("Unexpected error joining lobby. Please try again.");
        }
    }

    public void StartClient()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        
        // Store the join code for potential retry
        lastJoinCode = joinCodeInput.text;
        
        JoinRelay(lastJoinCode);
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
    }

    public void StartHost()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        
        // Clear last join code since we're hosting
        lastJoinCode = "";
        
        CreateRelay();
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
        
        // Use Invoke instead of a coroutine to open lobby settings after a delay
        Invoke("OpenLobbySettingsAfterDelay", 1.5f);
    }
    
    // Simple method to open the lobby settings after a delay
    private void OpenLobbySettingsAfterDelay()
    {
        if (menuManager != null)
        {
            Debug.Log("[ConnectToGame] Opening lobby settings menu");
            menuManager.OpenLobbySettingsMenu();
            Debug.Log("[ConnectToGame] Lobby settings should now be visible");
        }
    }

    // Method to retry host creation
    public void RetryHostCreation()
    {
        if (isRetrying) return;
        
        isRetrying = true;
        Debug.Log("Retrying host creation...");
        
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        
        // Clear last join code since we're hosting
        lastJoinCode = "";
        
        CreateRelay();
        
        // Reset retry flag after a delay
        Invoke("ResetRetryFlag", 2f);
    }

    // Method to retry joining with a specific code
    public void RetryJoinWithCode(string code)
    {
        if (isRetrying) return;
        
        isRetrying = true;
        Debug.Log($"Retrying join with code: {code}");
        
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        
        JoinRelay(code);
        
        // Reset retry flag after a delay
        Invoke("ResetRetryFlag", 2f);
    }

    // Helper to reset the retry flag
    private void ResetRetryFlag()
    {
        isRetrying = false;
    }
}