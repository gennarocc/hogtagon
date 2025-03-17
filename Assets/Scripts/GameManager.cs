using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Hogtagon.Core.Infrastructure;
using System;

public class GameManager : NetworkBehaviour
{
    [SerializeField] public float gameTime;
    [SerializeField] public float betweenRoundLength = 5f;
    [SerializeField] public GameState state { get; private set; } = GameState.Pending;
    [SerializeField] private bool showScoreboardBetweenRounds = true;

    [Header("References")]
    [SerializeField] public MenuManager menuManager;

    [Header("Wwise")]
    [SerializeField] public AK.Wwise.Event LevelMusicOn;
    [SerializeField] public AK.Wwise.Event LevelMusicOff;
    [SerializeField] private AK.Wwise.Event MidroundOn;
    [SerializeField] private AK.Wwise.Event MidroundOff;
    [SerializeField] private AK.Wwise.Event PlayerEliminated;

    public static GameManager instance;
    private ulong roundWinnerClientId;
    private bool gameMusicPlaying;

    // Event for game state changes
    public event Action<GameState> OnGameStateChanged;

    // Kill feed management
    private KillFeed killFeed;

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

        // Get KillFeed reference
        killFeed = ServiceLocator.GetService<KillFeed>();
        
        TransitionToState(GameState.Pending);
    }

    public void Update()
    {
        if (state == GameState.Playing)
            gameTime += Time.deltaTime;
    }

    public void TransitionToState(GameState newState)
    {
        Debug.Log($"GameManager TransitionToState: {state} -> {newState}");
        
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

    private void SetGameState(GameState newState)
    {
        Debug.Log($"GameManager SetGameState: {state} -> {newState}");
        
        // Update state
        state = newState;
        
        // Broadcast to clients
        BroadcastGameStateClientRpc(newState);
        
        // Notify subscribers
        OnGameStateChanged?.Invoke(newState);
    }

    private void OnEndingEnter()
    {
        Debug.Log("GameManager OnEndingEnter");
        SetGameState(GameState.Ending);
        
        // Pause kill feed and keep last message
        if (killFeed != null)
        {
            killFeed.PauseAndKeepLastMessage();
        }

        // Change camera to player who won.
        ConnectionManager.instance.TryGetPlayerData(roundWinnerClientId, out PlayerData roundWinner);

        menuManager.DisplayWinnerClientRpc(roundWinner.username);
        roundWinner.score++;

        ConnectionManager.instance.UpdatePlayerDataClientRpc(roundWinnerClientId, roundWinner);
        ConnectionManager.instance.UpdateLobbyLeaderBasedOnScore();

        StartCoroutine(BetweenRoundTimer());
    }

    private void OnPlayingEnter()
    {
        Debug.Log("GameManager OnPlayingEnter");
        
        if (!gameMusicPlaying)
            PlayLevelMusicClientRpc();

        if (NetworkManager.Singleton.ConnectedClients.Count > 1)
        {
            // Reset kill feed for new round
            if (killFeed != null)
            {
                killFeed.ResetForNewRound();
            }

            LockPlayerMovement();
            gameTime = 0f;
            RespawnAllPlayers();
            menuManager.StartCountdownClientRpc();
            StartCoroutine(RoundCountdown());
        }
        else
        {
            Debug.LogWarning("Not enough players to start game");
            TransitionToState(GameState.Pending);
        }
    }

    public IEnumerator RoundCountdown()
    {
        Debug.Log("GameManager RoundCountdown started");
        yield return new WaitForSeconds(3f);

        Debug.Log("GameManager RoundCountdown finished - Transitioning to Playing");
        
        // Unlock player movement
        UnlockPlayerMovement();

        // Set the game state to playing
        SetGameState(GameState.Playing);

        // Use the existing Resume method to handle cursor locking and input mode
        if (menuManager != null)
            menuManager.Resume();

        BroadcastMidroundOffClientRpc();
    }

    public IEnumerator BetweenRoundTimer()
    {
        Debug.Log("GameManager BetweenRoundTimer started");
        
        float showWinnerDuration = 2.0f;     // How long to show just the winner text
        float showScoreboardDuration = 3.0f;  // How long to show the scoreboard

        menuManager.HideScoreboardClientRpc();
        yield return new WaitForSeconds(showWinnerDuration);

        // Now show the scoreboard
        menuManager.ShowScoreboardClientRpc();
        yield return new WaitForSeconds(showScoreboardDuration);

        // Hide scoreboard when starting new round
        menuManager.HideScoreboardClientRpc();

        Debug.Log("GameManager BetweenRoundTimer finished - Starting new round");
        // Transition to next round
        TransitionToState(GameState.Playing);
    }

    private void OnPendingEnter()
    {
        Debug.Log("GameManager OnPendingEnter");
        StopLevelMusicClientRpc();
        SetGameState(GameState.Pending);
    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;

        List<ulong> alive = ConnectionManager.instance.GetAliveClients();
        if (alive.Count == 1)
        {
            roundWinnerClientId = alive[0];
            TransitionToState(GameState.Ending);
            MidroundOff.Post(gameObject);
        }
    }

    public void PlayerDied(ulong clientId)
    {
        if (ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            if (player.state == PlayerState.Dead) return;

            Debug.Log(player.username + " has been eliminated.");
            player.state = PlayerState.Dead;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, player);
            BroadcastPlayerEliminatedSFXClientRpc();

            // Add killfeed entry - Use ServiceLocator
            var playerCollisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
            if (playerCollisionTracker != null)
            {
                Debug.Log($"Checking collision history for player {clientId} with name {player.username}");
                var lastCollision = playerCollisionTracker.GetLastCollision(clientId);
                
                if (lastCollision != null)
                {
                    // Player was killed by another player
                    Debug.Log($"Player {player.username} was killed by {lastCollision.collidingPlayerName}");
                    ShowKillFeedMessageClientRpc(lastCollision.collidingPlayerName, player.username, false);
                }
                else
                {
                    // Player killed themselves
                    Debug.Log($"Player {player.username} killed themselves (no collision record found)");
                    ShowKillFeedMessageClientRpc(player.username, "", true);
                }
            }
            else
            {
                Debug.LogError("PlayerCollisionTracker service not found!");
            }
        }

        CheckGameStatus();
    }

    private void RespawnAllPlayers()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player player in players)
            player.Respawn();
    }

    private void LockPlayerMovement()
    {
        HogController[] players = FindObjectsByType<HogController>(FindObjectsSortMode.None);
        foreach (HogController player in players)
            player.canMove = false;
    }

    private void UnlockPlayerMovement()
    {
        HogController[] players = FindObjectsByType<HogController>(FindObjectsSortMode.None);
        foreach (HogController player in players)
            player.canMove = true;
    }

    [ClientRpc]
    private void BroadcastGameStateClientRpc(GameState newState)
    {
        Debug.Log($"GameManager BroadcastGameStateClientRpc: {state} -> {newState}");
        state = newState;
        if (state == GameState.Ending)
            MidroundOn.Post(gameObject);
    }

    [ClientRpc]
    private void PlayLevelMusicClientRpc()
    {
        LevelMusicOn.Post(gameObject);
        gameMusicPlaying = true;
    }

    [ClientRpc]
    private void StopLevelMusicClientRpc()
    {
        LevelMusicOff.Post(gameObject);
        gameMusicPlaying = false;
    }

    [ClientRpc]
    private void BroadcastMidroundOffClientRpc()
    {
        MidroundOff.Post(gameObject);
    }

    [ClientRpc]
    private void BroadcastPlayerEliminatedSFXClientRpc()
    {
        PlayerEliminated.Post(gameObject);
    }

    // Kill feed message handling
    public void ShowKillMessage(string killerName, string victimName, bool isSuicide)
    {
        if (!IsServer) return;
        ShowKillFeedMessageClientRpc(killerName, victimName, isSuicide);
    }

    [ClientRpc]
    private void ShowKillFeedMessageClientRpc(string killerName, string victimName, bool isSuicide)
    {
        if (killFeed == null)
        {
            killFeed = ServiceLocator.GetService<KillFeed>();
            if (killFeed == null)
            {
                Debug.LogError("KillFeed service not found!");
                return;
            }
        }

        if (isSuicide)
        {
            killFeed.AddSuicideMessage(killerName);
        }
        else
        {
            killFeed.AddKillMessage(killerName, victimName);
        }
    }
}