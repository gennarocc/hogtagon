using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class ConnectionManager : NetworkBehaviour
{
    [SerializeField] public string joinCode;
    [SerializeField] public bool isConnected = false;
    private Dictionary<ulong, PlayerData> clientDataDictionary = new Dictionary<ulong, PlayerData>();
    private Dictionary<ulong, PlayerData> pendingPlayerData = new Dictionary<ulong, PlayerData>();
    public static ConnectionManager instance;

    private void Start()
    {
        NetworkManager.Singleton.ConnectionApprovalCallback += ConnectionApprovalCallback;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectCallback;
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

        // Set player username
        string decodedUsername = System.Text.Encoding.ASCII.GetString(request.Payload);
        if (decodedUsername.Length == 0)
        {
            decodedUsername = "Player " + GetPlayerCount();
        }

        response.Approved = false;

        if (CheckUsernameAvailability(decodedUsername) && GetPlayerCount() <= 8)
        {
            var sp = SpawnPointManager.instance.AssignSpawnPoint();
            var player = new PlayerData()
            {
                username = decodedUsername,
                score = 0,
                color = Random.ColorHSV(),
                state = PlayerState.Alive,
                spawnPoint = sp,
                isLobbyLeader = clientDataDictionary.Count == 0
            };
            pendingPlayerData.Add(clientId, player);
            response.Approved = true;
            response.Position = sp;
            response.Rotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - sp);
            response.CreatePlayerObject = true;
        }
    }

    private void OnClientConnectedCallback(ulong clientId)
    {
        ClientDataListSerialized serializedList = DictionaryExtensions.ConvertDictionaryToSerializableList(clientDataDictionary);
        SendClientDataListClientRpc(clientId, serializedList);
    }

    private void OnClientDisconnectCallback(ulong clientId)
    {
        if (clientDataDictionary.ContainsKey(clientId))
        {
            clientDataDictionary.Remove(clientId);
        }
    }

    public bool CheckUsernameAvailability(string username)
    {
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
            Dictionary<ulong, PlayerData> clientData = DictionaryExtensions.ConvertSerializableListToDictionary(serializedList);
            foreach (var data in clientData)
            {
                clientDataDictionary.Add(data.Key, data.Value);
                Debug.Log("Adding existing player - : " + data.Value.username);
            }
            AddPlayerServerRpc(clientId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddPlayerServerRpc(ulong clientId)
    {
        PlayerData playerData = pendingPlayerData[clientId];
        Debug.Log(message: "Player conneted - " + playerData.username);
        clientDataDictionary.Add(clientId, playerData);
        UpdatePlayerDataClientRpc(clientId, playerData);
        foreach (Player player in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (player.clientId == clientId)
            {
                player.SetPlayerData(playerData);
            }
        }
        pendingPlayerData.Remove(clientId);
    }

    [ClientRpc]
    public void UpdatePlayerDataClientRpc(ulong clientId, PlayerData player)
    {
        if (clientDataDictionary.ContainsKey(clientId))
        {
            clientDataDictionary[clientId] = player;
        }
        else
        {
            clientDataDictionary.Add(clientId, player);
            Debug.Log(message: "Player conneted - " + player.username);
        }
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
        var str = "";
        foreach (var player in clientDataDictionary)
        {
            str += player.Value.username + "\n";
        }

        return str;
    }

    public string PrintScore()
    {
        var str = "";
        foreach (var player in clientDataDictionary)
        {
            str += player.Value.score + "\n";
        }

        return str;
    }

    public Color GetPlayerColor(ulong client)
    {
        return clientDataDictionary[client].color;
    }

    public List<PlayerData> GetAlivePlayers()
    {
       List<PlayerData> alivePlayers = new List<PlayerData>(); 
       foreach (var player in clientDataDictionary)
       {
           if (player.Value.state == PlayerState.Alive) alivePlayers.Add(player.Value);
       }
       return alivePlayers; 
    }

    public bool TryGetPlayerData(ulong clientId, out PlayerData player)
    {
        if (!clientDataDictionary.ContainsKey(clientId))
        {
            Debug.Log(message: "Client Id - " + clientId + "does not exist.");
            player = new PlayerData(){};
            return false;
        }
        player = clientDataDictionary[clientId];
        return true;
    }
}

public struct PlayerData : INetworkSerializable
{
    public string username;
    public int score;
    public Color color;
    public PlayerState state;
    public Vector3 spawnPoint;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref color);
        serializer.SerializeValue(ref spawnPoint);
        serializer.SerializeValue(ref state);
        serializer.SerializeValue(ref isLobbyLeader);
    }
}

public struct ClientData : INetworkSerializable
{
    public ulong clientId;
    public string username;
    public int score;
    public Color color;
    public Vector3 spawnPoint;
    public PlayerState state;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref color);
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