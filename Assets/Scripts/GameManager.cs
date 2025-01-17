using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Collections;

public class GameManager : NetworkBehaviour
{
    [SerializeField] public GameState state = GameState.Pending;
    [SerializeField] public float gameTime;
    private Dictionary<ulong, PlayerData> clientDataDictionary = new Dictionary<ulong, PlayerData>();
    public static GameManager instance;

    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;

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

    private void OnClientConnectedCallback(ulong clientId)
    {
        ClientDataListSerialized serializedList = DictionaryExtensions.ConvertDictionaryToSerializableList(clientDataDictionary);
        SendClientDataListClientRpc(clientId, serializedList);
    }

    private void Update()
    {
        switch (state)
        {
            case GameState.Pending:
                break;
            case GameState.Playing:
                gameTime += Time.deltaTime;
                // if (alivePlayers.Count <= 1) state = GameState.Ending;
                break;
            case GameState.Ending:
                break;
            default:
                break;
        }
    }

    public void StartRound()
    {
        gameTime = 0f;
        // Respawn all players.
        state = GameState.Playing;
    }

    public bool CheckUsernameAvailability(string username)
    {
        foreach (var player in clientDataDictionary.Values)
        {
            if (player.username == username) return true;
        }
        return false;
    }

    [ClientRpc]
    public void SendClientDataListClientRpc(ulong clientId, ClientDataListSerialized serializedList)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
        {
            Debug.Log(message: "[INFO] ClientDataList Recieved");
            Dictionary<ulong, PlayerData> clientData = DictionaryExtensions.ConvertSerializableListToDictionary(serializedList);
            foreach (var data in clientData)
            {
                clientDataDictionary.Add(data.Key, data.Value);
                Debug.Log("Adding existing player - : " + data.Value.username);
            }
            Debug.Log("Added all players; Total player count is - " + clientDataDictionary.Count);
            NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(clientId, out NetworkObject networkObject);
            networkObject.gameObject.GetComponent<Player>().SetPlayerData(clientDataDictionary[clientId]);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddPlayerServerRpc(ulong clientId, string username)
    {
        var player = new PlayerData()
        {
            username = username,
            score = 0,
            color = Random.ColorHSV(),
            state = PlayerState.Alive,
            isLobbyLeader = clientDataDictionary.Count == 0
        };

        Debug.Log(message: "Player conneted - " + username);
        foreach (var kvp in clientDataDictionary)
        {
            Debug.Log(message: "ClientId - " + kvp.Key);
        }
        clientDataDictionary.Add(clientId, player);
        UpdatePlayerDataClientRpc(clientId, player);
    }

    [ServerRpc(RequireOwnership = false)]
    public void UpdatePlayerServerRpc(ulong clientId, int score)
    {
        if (!clientDataDictionary.ContainsKey(clientId)) return;

        PlayerData player = clientDataDictionary[clientId];
        player.score = score;
        UpdatePlayerDataClientRpc(clientId, player);
    }

    [ClientRpc]
    private void UpdatePlayerDataClientRpc(ulong clientId, PlayerData player)
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
            str = player.Value.username + "\n ";
        }

        return str;
    }

    public string PrintScore()
    {
        var str = "";
        foreach (var player in clientDataDictionary.Values)
        {
            str = player.score + "\n ";
        }

        return str;
    }

    public Color GetPlayerColor(ulong client)
    {
        return clientDataDictionary[client].color;
    }


}

public struct PlayerData : INetworkSerializable
{
    public string username;
    public int score;
    public Color color;
    public PlayerState state;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref color);
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
    public PlayerState state;
    public bool isLobbyLeader;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref username);
        serializer.SerializeValue(ref score);
        serializer.SerializeValue(ref color);
        serializer.SerializeValue(ref state);
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