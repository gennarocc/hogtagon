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

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event MenuMusicOff;

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
            ConnectionManager.instance.joinCode = joinCode;
            Debug.Log(message: "Join Code: " + joinCode);
            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.Log("Error creating lobby");
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError(e.Message);
            menuManager.MainMenu();
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
            Debug.Log(e);
            connectionPending.SetActive(false);
            menuManager.DisplayConnectionError("No lobby found");
            menuManager.MainMenu();
        }

    }

    public void StartClient()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        JoinRelay(joinCodeInput.text);
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
    }

    public void StartHost()
    {
        // Get username from PlayerPrefs
        string username = PlayerPrefs.GetString("Username", "Player");
        
        // Configure connection with username as payload
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(username);
        CreateRelay();
        connectionPending.SetActive(true);
        MenuMusicOff.Post(gameObject);
        
        // Use Invoke instead of a coroutine to open lobby settings after a delay
        Invoke("OpenLobbySettingsAfterDelay", 1.5f);
    }
    
    // Simple method to open the lobby settings after a delay
    private void OpenLobbySettingsAfterDelay()
    {
        if (menuManager != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // Make sure any required game objects are activated first
            menuManager.EnsureLobbySettingsMenuActive();
        }
    }
}