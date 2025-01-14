using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEditor.Rendering;
using System.Xml.Schema;

public class GameManager : NetworkBehaviour
{
    private Dictionary<ulong, string> clientUsernameMap = new Dictionary<ulong, string>();

    public static GameManager instance;

    private void Start()
    {
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

    public bool CheckUsernameAvailability(string username)
    {
        return clientUsernameMap.ContainsValue(username);
    }

    public void AddPlayer(ulong client, string username)
    {
        clientUsernameMap.Add(client, username);
        Debug.Log(message: "Player conneted - " + username);
    }

    public string GetClientUsername(ulong clientId)
    {
        clientUsernameMap.TryGetValue(clientId, out string username);
        return username;
    }

    public int GetPlayerCount()
    {
        return clientUsernameMap.Count;
    }

    public void PrintPlayers()
    {

        Debug.Log(message: "Total Players - " + clientUsernameMap.Count );
        foreach (KeyValuePair<ulong, string> client in clientUsernameMap)
        {
            Debug.Log($"Key: {client.Key}, Value: {client.Value}");
        }
    }
}