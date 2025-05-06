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
    [SerializeField] private AK.Wwise.Event LobbyMusicOn;

    // To track which operation we're performing for retry functionality
    public string lastJoinCode { get; private set; } = "";

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
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(8);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            ConnectionManager.Instance.joinCode = joinCode;
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
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);
            menuManager.OnPlayClicked();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error creating lobby: {e.Message}");
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError("Unexpected error creating lobby. Please try again.");
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);
            menuManager.OnPlayClicked();
        }
    }

    private async void JoinRelay(string joinCode)
    {
        try
        {
            Debug.Log(message: "Joining Relay with " + joinCode);
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));
            ConnectionManager.Instance.joinCode = joinCode;
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
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Unexpected error joining lobby: {e.Message}");
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError("Unexpected error joining lobby. Please try again.");
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);
        }
    }

    public void StartClient()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIConfirm);
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);

        // Store the join code for potential retry
        lastJoinCode = joinCodeInput.text;

        JoinRelay(lastJoinCode);
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
        menuManager.menuMusicPlaying = false;
        LobbyMusicOn.Post(gameObject);

    }

    public void StartHost()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");

        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIConfirm);
        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);

        // Clear last join code since we're hosting
        lastJoinCode = "";

        CreateRelay();
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
        menuManager.menuMusicPlaying = false;
        LobbyMusicOn.Post(gameObject);

        // Use Invoke instead of a coroutine to open lobby settings after a delay
        Invoke("OpenLobbySettings", 1.5f);
    }
}