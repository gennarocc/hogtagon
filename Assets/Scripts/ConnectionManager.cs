using TMPro;
using Unity.Netcode;
using UnityEngine;
using System.Text;
using System.Collections.Generic;

public class ConnectionManager : MonoBehaviour
{
    [SerializeField] public TMP_InputField usernameInput;
    [SerializeField] public Camera startCamera;

    private Dictionary<ulong, string> pendingPlayerData = new Dictionary<ulong, string>();
    private GameManager gm;

    private void Start()
    {
        gm = GameManager.instance;
        startCamera.cullingMask = 31;
    }
    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        startCamera.gameObject.SetActive(false);
    }
    public void StartClient()
    {
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        NetworkManager.Singleton.StartClient();
        startCamera.gameObject.SetActive(false);
    }
    public void StartHost()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.StartHost();
        startCamera.gameObject.SetActive(false);
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log(message: "Checking new connection");
        // The client identifier to be authenticated
        var clientId = request.ClientNetworkId;

        // Set player username
        string decodedUsername = Encoding.ASCII.GetString(request.Payload);
        if (decodedUsername.Length == 0)
        {
            decodedUsername = "Player " + gm.GetPlayerCount();
        }

        response.Approved = false;

        if (!gm.CheckUsernameAvailability(decodedUsername))
        {
            var spawnPoint = SpawnPointManager.instance.AssignSpawnPoint();
            response.Approved = true;
            response.CreatePlayerObject = true;
            response.Position = spawnPoint;
            response.Rotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - spawnPoint );
            pendingPlayerData.Add(clientId, decodedUsername);
        }
        // Your approval logic determines the following values

        // The Prefab hash value of the NetworkPrefab, if null the default NetworkManager player Prefab is used
        response.PlayerPrefabHash = null;

        // If response.Approved is false, you can provide a message that explains the reason why via ConnectionApprovalResponse.Reason
        // On the client-side, NetworkManager.DisconnectReason will be populated with this message via DisconnectReasonMessage
        // response.Reason = "Some reason for not approving the client";

        // If additional approval steps are needed, set this to true until the additional steps are complete
        // once it transitions from true to false the connection approval response will be processed.
        response.Pending = false;
    }

    private void OnClientConnectedCallback(ulong clientId)
    {
        Debug.Log("Hit");
        string username = pendingPlayerData[clientId];
        pendingPlayerData.Remove(clientId);
        gm.AddPlayerServerRpc(clientId, username);
    }

    // private void OnDestroy()
    // {
    //     NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
    //     NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    // }
}