using TMPro;
using Unity.Netcode;
using UnityEngine;
using System.Text;

public class StartNetwork : MonoBehaviour
{
    [SerializeField] public TMP_InputField usernameInput;
    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
    }
    public void StartClient()
    {
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.StartClient();
    }
    public void StartHost()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.StartHost();
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
            decodedUsername = "Player " + GameManager.instance.GetPlayerCount();
        }

        response.Approved = false;

        if (!GameManager.instance.CheckUsernameAvailability(decodedUsername))
        {
            response.Approved = true;
            GameManager.instance.AddPlayer(clientId, decodedUsername);
            response.CreatePlayerObject = true;
            response.Position = SpawnPointManager.instance.AssignSpawnPoint();
            response.Rotation = Quaternion.identity;
        }
        // Your approval logic determines the following values

        // The Prefab hash value of the NetworkPrefab, if null the default NetworkManager player Prefab is used
        response.PlayerPrefabHash = null;

        // Rotation to spawn the player object (if null it uses the default of Quaternion.identity)
        // response.Rotation = Quaternion.identity;

        // If response.Approved is false, you can provide a message that explains the reason why via ConnectionApprovalResponse.Reason
        // On the client-side, NetworkManager.DisconnectReason will be populated with this message via DisconnectReasonMessage
        // response.Reason = "Some reason for not approving the client";

        // If additional approval steps are needed, set this to true until the additional steps are complete
        // once it transitions from true to false the connection approval response will be processed.
        response.Pending = false;
    }

    private void OnDestroy()
    {
        // NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
    }
}