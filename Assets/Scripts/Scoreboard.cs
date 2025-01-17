using TMPro;
using UnityEngine;

public class Scoreboard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public TextMeshProUGUI players;
    [SerializeField] public TextMeshProUGUI score;

    private void OnEnable()
    {
        players.text = GameManager.instance.PrintPlayers();
        score.text = GameManager.instance.PrintScore();
    }
}