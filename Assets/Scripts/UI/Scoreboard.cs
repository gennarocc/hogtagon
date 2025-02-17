using TMPro;
using UnityEngine;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public TextMeshProUGUI players;
    [SerializeField] public TextMeshProUGUI score;

    private void OnEnable()
    {
        players.text = ConnectionManager.instance.PrintPlayers();
        score.text = ConnectionManager.instance.PrintScore();
    }
}