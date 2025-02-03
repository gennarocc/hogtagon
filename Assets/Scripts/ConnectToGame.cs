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
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button joinLobby;

    private async void Start()
    {
        startCamera.cullingMask = 31;
        // Start Relay Service.
        await UnityServices.InitializeAsync();
        AuthenticationService.Instance.SignedIn += () =>
        {
            Debug.Log(message: "Signed in " + AuthenticationService.Instance.PlayerId);
        };
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void Update()
    {

        joinLobby.interactable = false;
        if (joinCodeInput.text.Length == 6)
        {
            joinLobby.interactable = true;
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
            Debug.Log(message: "Join Code: " + joinCode);
            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
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
        }

    }

    public void StartClient()
    {
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        JoinRelay(joinCodeInput.text);
        startCamera.gameObject.SetActive(false);
    }
    public void StartHost()
    {
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        CreateRelay();
        startCamera.gameObject.SetActive(false);
    }

}