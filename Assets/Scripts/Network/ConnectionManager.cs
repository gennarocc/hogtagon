using TMPro;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

public class ConnectionManager : NetworkBehaviour
{
    [Header("Connection Status")]
    [SerializeField] public string joinCode;
    [SerializeField] public bool isConnected = false;

    [Header("Player Colors")]
    [SerializeField] public Material[] hogTextures;
    private static List<int> assignedTextures = new List<int>();

    [Header("References")]
    [SerializeField] private MenuManager menuManager;
    [SerializeField] private Scoreboard scoreboard;
    private Dictionary<ulong, PlayerData> clientDataDictionary = new Dictionary<ulong, PlayerData>();
    private Dictionary<ulong, PlayerData> pendingPlayerData = new Dictionary<ulong, PlayerData>();
    public static ConnectionManager instance;


    private void Start()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += ConnectionApprovalCallback;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;

        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        Debug.Log(message: "New player connecting...");
        // The client identifier to be authenticated
        var clientId = request.ClientNetworkId;
        response.Approved = false;

        // Set player username
        string decodedUsername = System.Text.Encoding.ASCII.GetString(request.Payload);
        if (decodedUsername.Length == 0)
        {
            decodedUsername = "Player" + (GetPlayerCount() + 1);
        }

        if (!CheckUsernameAvailability(decodedUsername))
        {
            response.Reason = "Invalid Driver Name";
            return;
        }

        if (GetPlayerCount() >= 8)
        {
            response.Reason = "Lobby Full";
            return;
        }

        var sp = SpawnPointManager.instance.AssignSpawnPoint(clientId);
        var player = new PlayerData()
        {
            username = decodedUsername,
            score = 0,
            colorIndex = GetAvailableTextureIndex(),
            state = GameManager.instance.state == GameState.Playing ? PlayerState.Dead : PlayerState.Alive,
            spawnPoint = sp,
            isLobbyLeader = false
        };

        pendingPlayerData.Add(clientId, player);
        response.Approved = true;
        response.Position = GameManager.instance.state == GameState.Playing ? new Vector3(0, 0, 0) : sp;
        response.Rotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - sp);
        response.CreatePlayerObject = true;

    }

    private void OnClientConnectedCallback(ulong clientId)
    {
        ClientDataListSerialized serializedList = DictionaryExtensions.ConvertDictionaryToSerializableList(clientDataDictionary);
        SendClientDataListClientRpc(clientId, serializedList);
    }

    private void OnClientDisconnectCallback(ulong clientId)
    {
        // Unassign Spawn Point
        if (IsServer) SpawnPointManager.instance.UnassignSpawnPoint(clientId);
        // Remove Data from Client Dictonary/List
        if (clientDataDictionary.ContainsKey(clientId))
        {
            // Store the username for the notification
            string username = "A player";
            if (TryGetPlayerData(clientId, out PlayerData playerData))
            {
                username = playerData.username;
            }

            clientDataDictionary.Remove(clientId);
            RemovePlayerClientRpc(clientId);

            // Update the scoreboard
            if (scoreboard != null)
            {
                scoreboard.UpdatePlayerList();
            }

            // If server and only one player left, reset to lobby state and show message
            if (IsServer && NetworkManager.Singleton.ConnectedClients.Count <= 1)
            {
                GameManager.instance.TransitionToState(GameState.Pending);
                ShowHostAloneMessageClientRpc(username);
            }
        }

        if (!IsServer && NetworkManager.Singleton.DisconnectReason != string.Empty)
        {
            menuManager.MainMenu();
            menuManager.DisplayConnectionError(NetworkManager.Singleton.DisconnectReason);
        }
    }

    [ClientRpc]
    private void ShowHostAloneMessageClientRpc(string disconnectedPlayerName)
    {
        // Show message to host (and any remaining clients if there are any)
        if (menuManager != null)
        {
            menuManager.DisplayHostAloneMessage(disconnectedPlayerName);
        }
    }

    private void OnTransportFailure()
    {
        menuManager.connectionPending.SetActive(false);
        menuManager.DisplayConnectionError("Connection Timeout");
        menuManager.MainMenu();
    }
    public bool CheckUsernameAvailability(string username)
    {
        // Check length. 
        if (username.Length > 10) return false;
        // Only alpha numeric characters.
        var regex = new Regex("^[a-zA-Z0-9]*$");
        if (!regex.IsMatch(username)) return false;
        // Isn't already in use.
        foreach (var player in clientDataDictionary.Values)
        {
            if (player.username == username) return false;
        }

        return true;
    }

    [ClientRpc]
    private void SendClientDataListClientRpc(ulong clientId, ClientDataListSerialized serializedList)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            Debug.Log(message: "ClientDataList Recieved");
            // Purge any existing data.
            if (clientDataDictionary.Count > 0) clientDataDictionary = new Dictionary<ulong, PlayerData>();
            // Deserialize data.
            Dictionary<ulong, PlayerData> clientData = DictionaryExtensions.ConvertSerializableListToDictionary(serializedList);
            foreach (var data in clientData) // Add data to local dictionary
            {
                clientDataDictionary.Add(data.Key, data.Value);
                Debug.Log(message: "Adding existing player - : " + data.Value.username);
            }
            AddPlayerServerRpc(clientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddPlayerServerRpc(ulong clientId)
    {
        PlayerData playerData = pendingPlayerData[clientId];
        Debug.Log(message: "Player connected - " + playerData.username);
        clientDataDictionary.Add(clientId, playerData);

        // Find and set the player's NetworkVariable
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.clientId == clientId)
            {
                player.SetPlayerData(playerData);  // This will update the NetworkVariable
                break;
            }
        }

        // Still notify all clients about the new player for the client dictionary
        UpdatePlayerDataClientRpc(clientId, playerData);
        pendingPlayerData.Remove(clientId);
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void RemovePlayerClientRpc(ulong clientId)
    {
        Debug.Log(message: "Removing Client - " + clientId);
        clientDataDictionary.Remove(clientId);
    }

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    public void UpdatePlayerDataClientRpc(ulong clientId, PlayerData player)
    {
        // Update the client dictionary
        if (clientDataDictionary.ContainsKey(clientId))
        {
            clientDataDictionary[clientId] = player;
            Debug.Log(message: "Updating " + player.username + ",  State:  " + player.state);
        }
        else
        {
            clientDataDictionary.Add(clientId, player);
            Debug.Log(message: "Player connected - " + player.username);
        }

        scoreboard.UpdatePlayerList();
    }

    public string GetClientUsername(ulong clientId)
    {
        clientDataDictionary.TryGetValue(clientId, out PlayerData player);
        return player.username;
    }

    public int GetPlayerCount()
    {
        return clientDataDictionary.Count;
    }

    public string PrintPlayers()
    {
        // Sort players by score in descending order
        var sortedPlayers = clientDataDictionary
            .OrderByDescending(player => player.Value.score)
            .ToList();

        var str = "";
        foreach (var player in sortedPlayers)
        {
            if (player.Value.state != PlayerState.Alive)
            {
                str += $"<color=#FF0000>{player.Value.username}</color>\n";
            }
            else
            {
                str += player.Value.username + "\n";
            }
        }

        return str;
    }

    public string PrintScore()
    {
        // Sort players by score in descending order
        var sortedPlayers = clientDataDictionary
            .OrderByDescending(player => player.Value.score)
            .ToList();

        var str = "";
        foreach (var player in sortedPlayers)
        {
            str += player.Value.score + "\n";
        }

        return str;
    }

    public int GetPlayerColorIndex(ulong client)
    {
        return clientDataDictionary[client].colorIndex;
    }

    public List<ulong> GetAliveClients()
    {
        List<ulong> aliveClients = new List<ulong>();
        foreach (var player in clientDataDictionary)
        {
            if (player.Value.state == PlayerState.Alive) aliveClients.Add(player.Key);
        }
        return aliveClients;
    }

    public bool TryGetPlayerData(ulong clientId, out PlayerData player)
    {
        if (!clientDataDictionary.ContainsKey(clientId))
        {
            Debug.Log(message: "Client Id - " + clientId + " does not exist.");
            player = new PlayerData() { };
            return false;
        }
        player = clientDataDictionary[clientId];
        return true;
    }

    public Player GetPlayer(ulong clientId)
    {
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.clientId == clientId)
            {
                return player;
            }
        }
        Debug.Log(message: "GetPlayer() could not find requested player - " + clientId);
        return null;
    }

    private int GetAvailableTextureIndex()
    {
        assignedTextures.Clear();
        foreach (var playerData in clientDataDictionary.Values)
        {
            assignedTextures.Add(playerData.colorIndex);
        }

        // Find available texture
        List<int> availableIndices = new List<int>();
        for (int i = 0; i < hogTextures.Length; i++)
        {
            if (!assignedTextures.Contains(i))
            {
                availableIndices.Add(i);
            }
        }

        if (availableIndices.Count > 0)
        {
            int selectedIndex = availableIndices[Random.Range(0, availableIndices.Count)];
            assignedTextures.Add(selectedIndex);
            return selectedIndex;
        }
        return -1; // No available textures
    }


    public void UpdateLobbyLeaderBasedOnScore()
    {
        if (!IsServer)
        {
            Debug.LogWarning("UpdateLobbyLeaderBasedOnScore should only be called on the server.");
            return;
        }

        // STEP 1: First, find the highest scoring player (without modifying anything)
        ulong highestScoringClientId = 0;
        int highestScore = -1;
        bool foundAnyPlayer = false;

        // Make a safe copy of the keys to iterate through
        List<ulong> clientIds = new List<ulong>(clientDataDictionary.Keys);

        foreach (ulong clientId in clientIds)
        {
            if (TryGetPlayerData(clientId, out PlayerData playerData))
            {
                foundAnyPlayer = true;

                if (playerData.score > highestScore ||
                    (playerData.score == highestScore && playerData.isLobbyLeader))
                {
                    highestScore = playerData.score;
                    highestScoringClientId = clientId;
                }
            }
        }

        if (!foundAnyPlayer)
            return;

        // STEP 2: Then update all player statuses (in a separate loop)
        foreach (ulong clientId in clientIds)
        {
            if (TryGetPlayerData(clientId, out PlayerData playerData))
            {
                bool shouldBeLobbyLeader = (clientId == highestScoringClientId);

                if (playerData.isLobbyLeader != shouldBeLobbyLeader)
                {
                    playerData.isLobbyLeader = shouldBeLobbyLeader;

                    // Update local dictionary
                    clientDataDictionary[clientId] = playerData;

                    // Find and update the player's NetworkVariable
                    foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
                    {
                        if (player.clientId == clientId)
                        {
                            player.SetPlayerData(playerData);
                            break;
                        }
                    }

                    // Notify all clients about the updated player data
                    UpdatePlayerDataClientRpc(clientId, playerData);
                }
            }
        }

        Debug.Log("Lobby leader updated. New leader: " + GetClientUsername(highestScoringClientId));
    }
}

public struct PlayerData : INetworkSerializable
{
    public string username;
    public int score;
    public int colorIndex;
    public PlayerState state;
    public Vector3 spawnPoint;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // Ensure order matches the field declarations above
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref colorIndex);      // Make sure colorIndex is serialized
        serializer.SerializeValue(ref state);
        serializer.SerializeValue(ref spawnPoint);
        serializer.SerializeValue(ref isLobbyLeader);
    }
}

public struct ClientData : INetworkSerializable
{
    public ulong clientId;
    public string username;
    public int score;
    public int colorIndex;
    public Vector3 spawnPoint;
    public PlayerState state;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref colorIndex);
        serializer.SerializeValue(ref state);
        serializer.SerializeValue(ref spawnPoint);
        serializer.SerializeValue(ref isLobbyLeader);
    }
}

[System.Serializable]
public class ClientDataListSerialized : INetworkSerializable
{
    public List<ClientData> ClientDataList;
    public ClientDataListSerialized()
    {
        ClientDataList = new List<ClientData>();
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        int count = ClientDataList.Count;
        serializer.SerializeValue(ref count);

        if (serializer.IsReader)
        {
            ClientDataList.Clear();
            for (int i = 0; i < count; i++)
            {
                ClientData clientData = new ClientData();
                clientData.NetworkSerialize(serializer);
                ClientDataList.Add(clientData);
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                ClientDataList[i].NetworkSerialize(serializer);
            }
        }
    }
}