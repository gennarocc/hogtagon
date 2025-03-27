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
    [SerializeField] private KillFeed killFeedReference; // Assign this in the inspector

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

        // Try to get KillFeed reference from inspector first
        if (killFeedReference != null)
        {
            killFeed = killFeedReference;
            Debug.Log("[GameManager] Using KillFeed reference from inspector");
        }
        else
        {
            // Try to get KillFeed from ServiceLocator
            killFeed = ServiceLocator.GetService<KillFeed>();
            if (killFeed == null)
            {
                Debug.LogWarning("[GameManager] KillFeed not found in Start(). Will try again when needed.");
            }
        }

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
            case GameState.Winner:
                OnWinnerEnter();
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

    private void OnWinnerEnter()
    {
        Debug.Log("GameManager OnWinnerEnter");
        SetGameState(GameState.Winner);

        // Pause kill feed and keep last message
        if (killFeed != null)
        {
            killFeed.PauseAndKeepLastMessage();
        }

        // Get the winning player's data
        if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display winner celebration message
            menuManager.DisplayGameWinnerClientRpc(winner.username);

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
        else
        {
            // Clients request the server to handle the transition
            RequestTransitionToPendingServerRpc();
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

    [ServerRpc(RequireOwnership = false)]
    private void RequestTransitionToPendingServerRpc()
    {
        if (!IsServer) return;

        // Reset game state
        ResetGameState();
        // Transition to pending state
        TransitionToState(GameState.Pending);
    }

    private void OnEndingEnter()
    {
        Debug.Log("GameManager OnWinnerEnter");
        SetGameState(GameState.Winner);

        // Pause kill feed and keep last message
        if (killFeed != null)
        {
            killFeed.PauseAndKeepLastMessage();
        }

        // Get the winning player's data
        if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display winner celebration message
            menuManager.DisplayGameWinnerClientRpc(winner.username);

            // Show scoreboard after a delay
            StartCoroutine(ShowFinalScoreboard());
        }
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
            Debug.Log("[GameManager] Ensuring lobby settings are visible for host");
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
            TransitionToState(GameState.Ending);

            // Play round win sound using NetworkSoundManager
            if (IsServer)
            {
                SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundWin);
            }
        }
    }

    // Method to handle player killed events
    public void PlayerDied(ulong clientId)
    {
        // Only the server should process deaths
        if (!IsServer) return;

        Debug.Log($"[GameManager] PlayerDied called for client: {clientId}");

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
                Debug.Log("[GameManager] Attempting to find KillFeed instance");
                
                // Try direct reference first
                if (killFeedReference != null)
                {
                    killFeed = killFeedReference;
                    Debug.Log("[GameManager] Using assigned killFeedReference");
                }
                // Then try the static instance
                else if (KillFeed.Instance != null)
                {
                    killFeed = KillFeed.Instance;
                    Debug.Log("[GameManager] Using KillFeed.Instance");
                }
                // Finally try ServiceLocator
                else
                {
                    Debug.Log("[GameManager] Trying ServiceLocator as last resort");
                    killFeed = ServiceLocator.GetService<KillFeed>();
                }
                
                if (killFeed == null)
                {
                    Debug.LogError("[GameManager] Failed to get KillFeed through any method!");
                    
                    // Create a KillFeed if it doesn't exist
                    GameObject killFeedPrefab = Resources.Load<GameObject>("Prefabs/UI/KillFeed");
                    if (killFeedPrefab != null)
                    {
                        GameObject killFeedObj = Instantiate(killFeedPrefab);
                        killFeed = killFeedObj.GetComponent<KillFeed>();
                        Debug.Log("[GameManager] Created a new KillFeed instance");
                    }
                }
            }

            if (killFeed != null)
            {
                Debug.Log("[GameManager] Retrieving PlayerCollisionTracker from ServiceLocator");
                var playerCollisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
                
                if (playerCollisionTracker == null)
                {
                    Debug.LogError("[GameManager] Failed to get PlayerCollisionTracker from ServiceLocator!");
                    Debug.Log("[GameManager] Defaulting to suicide message because collision tracker is null");
                    killFeed.AddSuicideMessage(player.username);
                }
                else
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
            else
            {
                Debug.LogError("[GameManager] KillFeed is null, could not add kill message!");
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
        Debug.Log("[GameManager] Game mode settings locked");
    }

    // Method to set rounds to win from MenuManager
    public void SetRoundCount(int count)
    {
        if (!IsServer) return;

        // Only allow changes before the game starts
        if (state != GameState.Pending)
        {
            Debug.LogWarning("Cannot change round count after game has started");
            return;
        }

        roundsToWin = count;
        Debug.Log($"Round count set to {count}");
    }

    // Method to get current rounds to win setting
    public int GetRoundCount()
    {
        return roundsToWin;
    }

    private void UnlockGameModeSettings()
    {
        gameModeLocked = false;
        Debug.Log("[GameManager] Game mode settings unlocked");
    }
}
