using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    [SerializeField] public float gameTime;
    [SerializeField] public float betweenRoundLength = 5f;
    [SerializeField] public GameState state { get; private set; } = GameState.Pending;

    [SerializeField] private bool showScoreboardBetweenRounds = true;

    [Header("References")]
    [SerializeField] public MenuManager menuManager;
    public static GameManager instance;
    private ulong roundWinnerClientId;

    public void Start()
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
        TransitionToState(GameState.Pending);
    }

    public void Update()
    {
        switch (state)
        {
            case GameState.Pending:
                break;
            case GameState.Playing:
                gameTime += Time.deltaTime;
                break;
            case GameState.Ending:
                break;
        }
    }

    public void TransitionToState(GameState newState)
    {
        switch (newState)
        {
            case GameState.Pending:
                OnPendingEnter();
                break;
            case GameState.Playing:
                OnPlayingEnter();
                break;
            case GameState.Ending:
                OnEndingEnter();
                break;
        }
    }

    private void OnEndingEnter()
    {
       // Change camera to player who won.
       state = GameState.Ending;
       ConnectionManager.instance.TryGetPlayerData(roundWinnerClientId, out PlayerData roundWinner);

       menuManager.DisplayWinnerClientRpc(roundWinner.username);
       roundWinner.score++;

       ConnectionManager.instance.UpdatePlayerDataClientRpc(roundWinnerClientId, roundWinner);
       ConnectionManager.instance.UpdateLobbyLeaderBasedOnScore();

            // Show scoreboard if enabled
        if (showScoreboardBetweenRounds)
        {
            // First, update the scoreboard on server to ensure it's ready
            menuManager.ForceScoreboardUpdateServerRpc();
        }

       StartCoroutine(BetweenRoundTimer()); 
    }

    private void OnPlayingEnter()
    {
        if (NetworkManager.Singleton.ConnectedClients.Count > 1)
        {
            LockPlayerMovement();
            gameTime = 0f;
            RespawnAllPlayers();
            menuManager.StartCountdownClientRpc();
            StartCoroutine(RoundCountdown());
        }
    }

    public IEnumerator RoundCountdown()
    {
        yield return new WaitForSeconds(3f);
        UnlockPlayerMovement();
        state = GameState.Playing;
    }

   public IEnumerator BetweenRoundTimer()
    {
        // Configuration values
        float showWinnerDuration = 2.0f;     // How long to show just the winner text
        float showScoreboardDuration = 3.0f;  // How long to show the scoreboard
        
        // Show the winner text first for a few seconds
        yield return new WaitForSeconds(showWinnerDuration);
        
        // Now show the scoreboard
        menuManager.ShowScoreboardClientRpc();
        
        // Show the scoreboard for specified duration
        yield return new WaitForSeconds(showScoreboardDuration);

        // Hide scoreboard when starting new round
        menuManager.HideScoreboardClientRpc();
        
        // Transition to next round
        OnPlayingEnter();
    }


    private void OnPendingEnter()
    {

    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;

        List<ulong> alive = ConnectionManager.instance.GetAliveClients();
        if (alive.Count == 1)
        {
            roundWinnerClientId = alive[0];
            TransitionToState(GameState.Ending);
        }
    }

    public void PlayerDied(ulong clientId)
    {
        if (ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            player.state = PlayerState.Dead;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, player);
        }

        CheckGameStatus();
    }

    private void RespawnAllPlayers()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);

        foreach (Player player in players)
        {
            player.Respawn();
        }
    }

    private void LockPlayerMovement()
    {
        HogController[] players = FindObjectsByType<HogController>(FindObjectsSortMode.None);

        foreach (HogController player in players)
        {
            player.canMove = false;
        }
    }

    private void UnlockPlayerMovement()
    {
        HogController[] players = FindObjectsByType<HogController>(FindObjectsSortMode.None);

        foreach (HogController player in players)
        {
            player.canMove = true;
        }
    }
}