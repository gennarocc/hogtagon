using TMPro;
using UnityEngine;
using Unity.Netcode;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public TextMeshProUGUI players;
    [SerializeField] public TextMeshProUGUI score;

    private void OnEnable()
    {
        score.text = ConnectionManager.Instance.PrintScore();
    }

    public void UpdatePlayerList()
    {
        players.text = ConnectionManager.Instance.PrintPlayers();
        score.text = ConnectionManager.Instance.PrintScore();
    }
}