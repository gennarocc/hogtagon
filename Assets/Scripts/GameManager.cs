using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Hogtagon.Core.Infrastructure;
using System;

public class GameManager : NetworkBehaviour
{
    [SerializeField] public float gameTime;
    [SerializeField] public float betweenRoundLength = 5f;
    [SerializeField] public GameState state { get; private set; } = GameState.Pending;
    [SerializeField] private bool showScoreboardBetweenRounds = true;

    [Header("Game Mode Settings")]
    [SerializeField] private MenuManager.GameMode gameMode = MenuManager.GameMode.FreeForAll;
    [SerializeField] private int teamCount = 2;
    [SerializeField] private bool gameModeLocked = false; // Will be locked after game starts

    [Header("References")]
    [SerializeField] public MenuManager menuManager;


    public static GameManager Instance;
    private ulong roundWinnerClientId;
    private bool gameMusicPlaying;

    // Event for game state changes
    public event Action<GameState> OnGameStateChanged;

    // Kill feed management
    private KillFeed killFeed;

    // Track which players have died to prevent duplicate death processing
    private HashSet<ulong> processedDeaths = new HashSet<ulong>();

    public void Start()
    {
        if (Instance == null)
        {
            Instance = this;
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

        // Play midround music using NetworkSoundManager
        if (IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOn);
        }

        // Change camera to player who won.
        ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData roundWinner);

        menuManager.DisplayWinnerClientRpc(roundWinner.username);
        roundWinner.score++;

        ConnectionManager.Instance.UpdatePlayerDataClientRpc(roundWinnerClientId, roundWinner);
        ConnectionManager.Instance.UpdateLobbyLeaderBasedOnScore();

        StartCoroutine(BetweenRoundTimer());
    }

    private void OnPlayingEnter()
    {
        // Lock game mode settings when game starts
        LockGameModeSettings();
        
        Debug.Log("GameManager OnPlayingEnter");

        // Clear processed deaths for new round
        processedDeaths.Clear();

        if (!gameMusicPlaying && IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LevelMusicOn);
            gameMusicPlaying = true;
        }

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

        // Play round start sound and stop midround music
        if (IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundStart);
        }
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

        // Stop level music and play lobby music using NetworkSoundManager
        if (IsServer)
        {
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LobbyMusicOn);
        }

        SetGameState(GameState.Pending);
    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;

        List<ulong> alive = ConnectionManager.Instance.GetAliveClients();
        if (alive.Count == 1)
        {
            roundWinnerClientId = alive[0];
            TransitionToState(GameState.Ending);

            // Play round win sound using NetworkSoundManager
            if (IsServer)
            {
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundWin);
            }
        }
    }

    public void PlayerDied(ulong clientId)
    {
        // Only the server should process deaths
        if (!IsServer) return;

        // In Pending state, we allow multiple deaths for the same player
        // In Playing state, we only process one death per player
        if (state != GameState.Pending)
        {
            // Check if we've already processed this player's death for this round
            if (processedDeaths.Contains(clientId))
            {
                Debug.Log($"Skipping duplicate death processing for client {clientId}");
                return;
            }

            // Mark this player as processed
            processedDeaths.Add(clientId);
        }

        if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            // Set player state to dead (but always allow pending state deaths)
            if (player.state == PlayerState.Dead && state != GameState.Pending)
            {
                Debug.Log($"Player {player.username} is already marked as dead");
                return;
            }

            Debug.Log($"Processing death for {player.username} (ID: {clientId})");

            // Update player state (only in Playing state)
            if (state == GameState.Playing)
            {
                player.state = PlayerState.Dead;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, player);
            }

            // Play death sound
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.PlayerEliminated);

            // Create kill feed message
            if (killFeed == null)
            {
                killFeed = ServiceLocator.GetService<KillFeed>();
            }

            if (killFeed != null)
            {
                var playerCollisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
                if (playerCollisionTracker != null)
                {
                    var lastCollision = playerCollisionTracker.GetLastCollision(clientId);

                    if (lastCollision != null)
                    {
                        // Player was killed by another player
                        Debug.Log($"Player {player.username} was killed by {lastCollision.collidingPlayerName}");
                        killFeed.AddKillMessage(lastCollision.collidingPlayerName, player.username);
                    }
                    else
                    {
                        // Player killed themselves
                        Debug.Log($"Player {player.username} killed themselves (no collision record found)");
                        killFeed.AddSuicideMessage(player.username);
                    }
                }
            }
        }

        // Only check game status for Playing state
        if (state == GameState.Playing)
        {
            CheckGameStatus();
        }
    }

    private void RespawnAllPlayers()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player player in players)
            player.Respawn();
    }

    private void LockPlayerMovement()
    {
        NetworkHogController[] players = FindObjectsByType<NetworkHogController>(FindObjectsSortMode.None);
        foreach (NetworkHogController player in players)
            player.canMove = false;
    }

    private void UnlockPlayerMovement()
    {
        NetworkHogController[] players = FindObjectsByType<NetworkHogController>(FindObjectsSortMode.None);
        foreach (NetworkHogController player in players)
            player.canMove = true;
    }

    [ClientRpc]
    private void BroadcastGameStateClientRpc(GameState newState)
    {
        Debug.Log($"GameManager BroadcastGameStateClientRpc: {state} -> {newState}");
        state = newState;
        if (state == GameState.Ending) SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOff);
    }

    // Method to set game mode (called from MenuManager)
    public void SetGameMode(MenuManager.GameMode mode)
    {
        // Only allow changing game mode before game starts
        if (!gameModeLocked)
        {
            gameMode = mode;
            Debug.Log("Game mode set to: " + gameMode);
            
            // TODO: Notify clients of game mode change
            if (IsServer)
            {
                UpdateGameModeClientRpc(mode);
            }
        }
    }

    // Method to set team count for team battle mode
    public void SetTeamCount(int count)
    {
        // Only allow changing team count if in team battle mode and before game starts
        if (!gameModeLocked && gameMode == MenuManager.GameMode.TeamBattle)
        {
            teamCount = Mathf.Clamp(count, 2, 4); // Limit between 2-4 teams
            Debug.Log("Team count set to: " + teamCount);
            
            // TODO: Notify clients of team count change
            if (IsServer)
            {
                UpdateTeamCountClientRpc(teamCount);
            }
        }
    }

    // Client RPC to sync game mode with clients
    [ClientRpc]
    private void UpdateGameModeClientRpc(MenuManager.GameMode mode)
    {
        // Update local game mode
        gameMode = mode;
        Debug.Log("Client received game mode update: " + gameMode);
    }

    // Client RPC to sync team count with clients
    [ClientRpc]
    private void UpdateTeamCountClientRpc(int count)
    {
        // Update local team count
        teamCount = count;
        Debug.Log("Client received team count update: " + teamCount);
    }

    // Lock game mode settings when game starts
    private void LockGameModeSettings()
    {
        gameModeLocked = true;
        Debug.Log("Game mode settings locked");
    }
}