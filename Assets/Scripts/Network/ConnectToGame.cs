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

public class ConnectToGame : MonoBehaviour
{
    [SerializeField] private Camera startCamera;
    [SerializeField] private MenuManager menuManager;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event MenuMusicOff;

    private PlayMenuPanel playMenuPanel;
    private TMP_InputField usernameInput;
    private TMP_InputField joinCodeInput;
    private Button hostLobby;
    private Button joinLobby;
    private GameObject connectionPending;

    private async void Start()
    {
        // Get references from PlayMenuPanel
        playMenuPanel = GetComponent<PlayMenuPanel>();
        if (playMenuPanel != null)
        {
            // Get references through reflection to access private fields
            var fields = typeof(PlayMenuPanel).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name.Contains("usernameInput")) usernameInput = field.GetValue(playMenuPanel) as TMP_InputField;
                if (field.Name.Contains("joinCodeInput")) joinCodeInput = field.GetValue(playMenuPanel) as TMP_InputField;
                if (field.Name.Contains("hostLobbyButton")) hostLobby = field.GetValue(playMenuPanel) as Button;
                if (field.Name.Contains("joinLobbyButton")) joinLobby = field.GetValue(playMenuPanel) as Button;
                if (field.Name.Contains("connectionPending")) connectionPending = field.GetValue(playMenuPanel) as GameObject;
            }
        }

        startCamera.cullingMask = 31;
        if (joinLobby != null) joinLobby.interactable = false;

        // Start Relay Service
        try
        {
            InitializationOptions options = new InitializationOptions()
                .SetProfile(NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost ? "host" : "client");
            
            await UnityServices.InitializeAsync(options);
            
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut();
            }
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"Signed in anonymously: {AuthenticationService.Instance.PlayerId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
            if (playMenuPanel != null)
            {
                playMenuPanel.ShowConnectionError("Failed to initialize network services");
            }
        }
    }

    public void OnInputFieldValueChanged()
    {
        if (joinCodeInput != null && joinLobby != null)
        {
            joinLobby.interactable = joinCodeInput.text.Length == 6;
        }

        if (usernameInput != null && hostLobby != null)
        {
            hostLobby.interactable = !string.IsNullOrEmpty(usernameInput.text) && usernameInput.text.Length <= 10;
        }
    }

    private async void CreateRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(8);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
            var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            ConnectionManager.instance.joinCode = joinCode;
            Debug.Log($"Join Code: {joinCode}");
            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Error creating lobby: {e.Message}");
            if (playMenuPanel != null)
            {
                playMenuPanel.ShowConnectionPending(false);
                playMenuPanel.ShowConnectionError(e.Message);
            }
            if (menuManager != null) menuManager.MainMenu();
        }
    }

    private async void JoinRelay(string joinCode)
    {
        try
        {
            Debug.Log($"Joining Relay with code: {joinCode}");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));
            NetworkManager.Singleton.StartClient();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"Error joining lobby: {e.Message}");
            if (playMenuPanel != null)
            {
                playMenuPanel.ShowConnectionPending(false);
                playMenuPanel.ShowConnectionError("No lobby found");
            }
            if (menuManager != null) menuManager.MainMenu();
        }
    }

    public void StartClient()
    {
        if (usernameInput == null || string.IsNullOrEmpty(usernameInput.text))
        {
            if (playMenuPanel != null) playMenuPanel.ShowConnectionError("Please enter a username");
            return;
        }

        if (joinCodeInput == null || string.IsNullOrEmpty(joinCodeInput.text))
        {
            if (playMenuPanel != null) playMenuPanel.ShowConnectionError("Please enter a join code");
            return;
        }

        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        JoinRelay(joinCodeInput.text);
        if (playMenuPanel != null) playMenuPanel.ShowConnectionPending(true);
        if (MenuMusicOff != null) MenuMusicOff.Post(gameObject);
    }

    public void StartHost()
    {
        if (usernameInput == null || string.IsNullOrEmpty(usernameInput.text))
        {
            if (playMenuPanel != null) playMenuPanel.ShowConnectionError("Please enter a username");
            return;
        }

        string username = usernameInput.text;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        CreateRelay();
        if (playMenuPanel != null) playMenuPanel.ShowConnectionPending(true);
        if (MenuMusicOff != null) MenuMusicOff.Post(gameObject);
    }
}