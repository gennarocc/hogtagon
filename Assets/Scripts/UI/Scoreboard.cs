using TMPro;
using UnityEngine;
using Unity.Netcode;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public TextMeshProUGUI players;
    [SerializeField] public TextMeshProUGUI score;

    private void Update()
    {
        UpdatePlayerList();
        score.text = ConnectionManager.instance.PrintScore();
    }

    private void UpdatePlayerList()
    {
        string playerList = "";
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData))
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
            }
        }
        players.text = playerList;
    }
}