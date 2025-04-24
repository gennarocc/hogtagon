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

    [Header("Game Mode Settings")]
    [SerializeField] private MenuManager.GameMode gameMode = MenuManager.GameMode.FreeForAll;
    [SerializeField] private int teamCount = 2;
    [SerializeField] private int roundsToWin = 5;  // Default to 5 rounds
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
        Debug.Log($"[GAME] TransitionToState: {state} -> {newState}");

        switch (newState)
        {
            case GameState.Pending:
                OnPendingEnter();
                break;
            case GameState.Playing:
                OnPlayingEnter();
                break;
            case GameState.BetweenRound:
                OnBetweenRoundEnter();
                break;
            case GameState.GameEnd:
                OnGameEndEnter();
                break;
        }
    }

    private void SetGameState(GameState newState)
    {
        Debug.Log($"[GAME] SetGameState: {state} -> {newState}");

        // Update state
        state = newState;

        // Broadcast to clients
        BroadcastGameStateClientRpc(newState);

        // Notify subscribers
        OnGameStateChanged?.Invoke(newState);
    }
    private void OnGameEndEnter()
    {
        Debug.Log("[GAME] OnWinnerEnter");
        SetGameState(GameState.GameEnd);

        // Pause kill feed and keep last message
        killFeed.PauseAndKeepLastMessage();

        // Get the winning player's data
        if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display winner celebration message
            menuManager.DisplayGameWinnerClientRpc(roundWinnerClientId, winner);

            // Show scoreboard after a delay
            StartCoroutine(ShowFinalScoreboard());
        }
    }

    private IEnumerator ShowFinalScoreboard()
    {
        yield return new WaitForSeconds(3f); // Show winner message for 3 seconds
        menuManager.ShowScoreboardClientRpc();

        yield return new WaitForSeconds(3f); // Show scoreboard for 3 seconds

        if (IsServer)
        {
            // Server-side reset
            ResetGameState();
            // Return to pending state (lobby)
            TransitionToState(GameState.Pending);
        }

        menuManager.HideScoreboardClientRpc();
    }

    // Server-side method to reset game state
    private void ResetGameState()
    {
        if (!IsServer) return;

        // Reset all player states to Alive
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                playerData.state = PlayerState.Alive;
                playerData.score = 0; // Reset scores
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
            }
        }

        // Unlock game mode settings for new game
        UnlockGameModeSettings();

        // Respawn all players at their spawn points
        RespawnAllPlayers();

        // Clear any remaining processed deaths
        processedDeaths.Clear();
    }

    private void OnBetweenRoundEnter()
    {
        Debug.Log("[GAME] OnWinnerEnter");
        SetGameState(GameState.BetweenRound);

        // Pause kill feed and keep last message
        killFeed.PauseAndKeepLastMessage();

        // Get the winning player's data
        if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display winner celebration message
            menuManager.DisplayGameWinnerClientRpc(roundWinnerClientId, winner);
            if (IsServer)
            {
                winner.score++;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(roundWinnerClientId, winner);
            }

            // Check if game is over.
            if (winner.score >= roundsToWin)
            {
                TransitionToState(GameState.GameEnd);
                return;
            }
        }

        ConnectionManager.Instance.UpdateLobbyLeaderBasedOnScore();

        StartCoroutine(BetweenRoundTimer());
    }

    private void OnPlayingEnter()
    {
        // Lock game mode settings when game starts
        LockGameModeSettings();

        Debug.Log("[GAME] OnPlayingEnter");

        // Clear processed deaths for new round
        processedDeaths.Clear();

        if (!gameMusicPlaying && IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LevelMusicOn);
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LobbyMusicOff);
            gameMusicPlaying = true;
        }

        if (NetworkManager.Singleton.ConnectedClients.Count > 1)
        {
            // Reset kill feed for new round
            killFeed.ResetForNewRound();
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
        Debug.Log("[GAME] RoundCountdown started");
        yield return new WaitForSeconds(3f);

        Debug.Log("[GAME] RoundCountdown finished - Transitioning to Playing");

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
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOff);
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundStart);
        }
    }

    public IEnumerator BetweenRoundTimer()
    {
        Debug.Log("[GAME] BetweenRoundTimer started");

        float showWinnerDuration = 2.0f;     // How long to show just the winner text
        float showScoreboardDuration = 3.0f;  // How long to show the scoreboard

        menuManager.HideScoreboardClientRpc();
        yield return new WaitForSeconds(showWinnerDuration);

        // Now show the scoreboard
        menuManager.ShowScoreboardClientRpc();
        yield return new WaitForSeconds(showScoreboardDuration);

        // Hide scoreboard when starting new round
        menuManager.HideScoreboardClientRpc();

        Debug.Log("[GAME] BetweenRoundTimer finished - Starting new round");
        // Transition to next round
        TransitionToState(GameState.Playing);
    }

    private void OnPendingEnter()
    {
        Debug.Log("[GAME] OnPendingEnter");

        // Stop level music and play lobby music using NetworkSoundManager
        if (IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LobbyMusicOn);
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LevelMusicOff);
        }

        SetGameState(GameState.Pending);

        // Only show lobby settings if we're the server AND connected to the network
        // AND not in the process of disconnecting
        if (IsServer &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkManager.Singleton.ConnectedClientsList.Count > 0 &&
            ConnectionManager.Instance.isConnected)
        {
            // Show the lobby settings menu for the host
            EnsureLobbySettingsVisibleForHost();
        }
    }

    // Method to ensure the lobby settings are visible for the host
    private void EnsureLobbySettingsVisibleForHost()
    {
        if (menuManager != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            Debug.Log("[GAME] Ensuring lobby settings are visible for host");
            menuManager.OpenLobbySettingsMenu();
        }
    }

    // Public method that other systems can call to ensure lobby settings are shown
    public void ShowLobbySettings()
    {
        if (IsServer && state == GameState.Pending)
        {
            EnsureLobbySettingsVisibleForHost();
        }
    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;

        List<ulong> alive = ConnectionManager.Instance.GetAliveClients();
        if (alive.Count == 1)
        {
            roundWinnerClientId = alive[0];
            TransitionToState(GameState.BetweenRound);

            if (IsServer)
            {
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundWin);
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOn);
            }
        }
    }

    // Method to handle player killed events
    public void PlayerDied(ulong clientId)
    {
        // Only the server should process deaths
        if (!IsServer)
        {
            Debug.LogWarning($"[GAME] Non-server tried to process death for client {clientId}");
            return;
        }

        Debug.Log($"[GAME] PlayerDied called for client: {clientId}");

        // Always skip processing if the player is already in the processed deaths list
        // regardless of game state
        if (processedDeaths.Contains(clientId))
        {
            Debug.Log($"[GAME] Skipping duplicate death processing for client {clientId} (already in processedDeaths)");
            return;
        }

        // In Playing state, add to processedDeaths to prevent duplicates
        if (state == GameState.Playing)
        {
            processedDeaths.Add(clientId);
            Debug.Log($"[GAME] Added client {clientId} to processedDeaths");
        }

        if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            // Set player state to dead (but always allow pending state deaths)
            if (player.state == PlayerState.Dead && state != GameState.Pending)
            {
                Debug.Log($"[GAME] Player {player.username} is already marked as dead");
                return;
            }

            Debug.Log($"[GAME] Processing death for {player.username} (ID: {clientId})");

            // Update player state (only in Playing state)
            if (state == GameState.Playing)
            {
                player.state = PlayerState.Dead;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, player);
                // Play death sound
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.PlayerEliminated);
            }


            // Create kill feed message
            // Allow killfeed in both Playing and Pending states
            if (state == GameState.Playing || state == GameState.Pending)
            {
                killFeed = ServiceLocator.GetService<KillFeed>();

                var playerCollisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
                if (playerCollisionTracker != null)
                {
                    var lastCollision = playerCollisionTracker.GetLastCollision(clientId);

                    if (lastCollision != null)
                    {
                        // Player was killed by another player
                        Debug.Log($"[GAME] Player {player.username} was killed by {lastCollision.collidingPlayerName}");
                        killFeed.AddKillMessage(lastCollision.collidingPlayerName, player.username);
                    }
                    else
                    {
                        // Player killed themselves
                        Debug.Log($"[GAME] Player {player.username} killed themselves (no collision record found)");
                        killFeed.AddSuicideMessage(player.username);
                    }
                }
            }
            else
            {
                Debug.Log($"[GAME] Not showing kill feed message in state: {state}");
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
        Debug.Log($"[GAME] BroadcastGameStateClientRpc: {state} -> {newState}");
        state = newState;
        if (state == GameState.BetweenRound) SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOff);
    }

    // Method to set game mode (called from MenuManager)
    public void SetGameMode(MenuManager.GameMode mode)
    {
        // Only allow changing game mode before game starts
        if (!gameModeLocked)
        {
            gameMode = mode;
            Debug.Log("[GAME] Game mode set to: " + gameMode);

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
            Debug.Log("[GAME] Team count set to: " + teamCount);

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
        Debug.Log("[GAME] Client received team count update: " + teamCount);
    }

    // Lock game mode settings when game starts
    private void LockGameModeSettings()
    {
        gameModeLocked = true;
        Debug.Log("[GAME] Game mode settings locked");
    }

    // Method to set rounds to win from MenuManager
    public void SetRoundCount(int count)
    {
        if (!IsServer) return;

        // Only allow changes before the game starts
        if (state != GameState.Pending)
        {
            Debug.LogWarning("[GAME] Cannot change round count after game has started");
            return;
        }

        roundsToWin = count;
        Debug.Log($"[GAME] Round count set to {count}");
    }

    // Method to get current rounds to win setting
    public int GetRoundCount()
    {
        return roundsToWin;
    }

    private void UnlockGameModeSettings()
    {
        gameModeLocked = false;
        Debug.Log("[GAME] Game mode settings unlocked");
    }
}
