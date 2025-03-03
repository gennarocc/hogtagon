using TMPro;
using UnityEngine;
using Unity.Netcode;
using System.Linq;
using System.Collections.Generic;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public TextMeshProUGUI players;
    [SerializeField] public TextMeshProUGUI score;

    private void OnEnable()
    {
        UpdatePlayerList();
    }

    public void UpdatePlayerList()
    {
        if (players == null || score == null)
        {
            Debug.LogError("Scoreboard text references are missing!");
            return;
        }

        string playerList = "";
        string scoreList = "";
        
        // Get all player data and sort by score
        List<PlayerData> allPlayerData = new List<PlayerData>();
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                allPlayerData.Add(playerData);
            }
        }
        
        // Sort players by score (descending)
        allPlayerData = allPlayerData.OrderByDescending(player => player.score).ToList();
        
        // Build the player and score lists
        foreach (var playerData in allPlayerData)
        {
            // Format player name based on state
            if (playerData.state != PlayerState.Alive)
            {
                playerList += $"<color=#FF0000>{playerData.username}</color>\n";
            }
            else
            {
                playerList += $"{playerData.username}\n";
            }
            
            // Add score
            scoreList += $"{playerData.score}\n";
        }
        
        // Update the UI
        players.text = playerList;
        score.text = scoreList;
    }
        
}