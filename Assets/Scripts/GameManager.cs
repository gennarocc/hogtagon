using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Hogtagon.Core.Infrastructure;
using System;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    // TeamHelper class for team management
    private class TeamHelper
    {
        private Dictionary<int, List<ulong>> teamPlayers = new Dictionary<int, List<ulong>>();
        private int winningTeam = 0;
        private int teamCount = 2;

        public void Initialize(int count)
        {
            teamCount = Mathf.Clamp(count, 2, 4);
            teamPlayers.Clear();
            
            for (int i = 1; i <= teamCount; i++)
            {
                teamPlayers[i] = new List<ulong>();
            }
        }

        public void AssignPlayersToTeams(List<ulong> players)
        {
            foreach (var team in teamPlayers.Keys.ToList())
            {
                teamPlayers[team].Clear();
            }
            
            // Shuffle players
            for (int i = 0; i < players.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, players.Count);
                ulong temp = players[i];
                players[i] = players[r];
                players[r] = temp;
            }

            // Assign players to teams evenly
            for (int i = 0; i < players.Count; i++)
            {
                int teamNumber = (i % teamCount) + 1;
                teamPlayers[teamNumber].Add(players[i]);
            }
        }

        public bool CheckForWinningTeam(List<ulong> alivePlayers, out int winningTeamNumber, out List<ulong> winningTeamPlayers)
        {
            Dictionary<int, int> aliveCountByTeam = new Dictionary<int, int>();
            winningTeamNumber = 0;
            winningTeamPlayers = new List<ulong>();
            
            for (int i = 1; i <= teamCount; i++)
            {
                aliveCountByTeam[i] = 0;
            }
            
            foreach (ulong clientId in alivePlayers)
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    int teamNumber = playerData.team;
                    if (teamNumber > 0 && teamNumber <= teamCount)
                    {
                        aliveCountByTeam[teamNumber]++;
                    }
                }
            }
            
            List<int> teamsWithAlivePlayers = aliveCountByTeam
                .Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .ToList();
            
            if (teamsWithAlivePlayers.Count == 1)
            {
                winningTeamNumber = teamsWithAlivePlayers[0];
                winningTeamPlayers = teamPlayers[winningTeamNumber];
                this.winningTeam = winningTeamNumber;
                return true;
            }
            
            return false;
        }

        public List<ulong> GetTeamPlayers(int teamNumber)
        {
            if (teamPlayers.TryGetValue(teamNumber, out List<ulong> players))
            {
                return players;
            }
            return new List<ulong>();
        }

        public string GetTeamName(int teamNumber)
        {
            switch(teamNumber)
            {
                case 1: return "RED";
                case 2: return "BLUE";
                case 3: return "GREEN";
                case 4: return "YELLOW";
                default: return "UNKNOWN";
            }
        }

        public Color GetTeamColor(int teamNumber)
        {
            switch(teamNumber)
            {
                case 1: return new Color(1.0f, 0.2f, 0.2f); // Red
                case 2: return new Color(0.2f, 0.4f, 1.0f); // Blue
                case 3: return new Color(0.2f, 0.8f, 0.2f); // Green
                case 4: return new Color(1.0f, 0.8f, 0.2f); // Yellow
                default: return Color.white;
            }
        }

        public void Reset()
        {
            // Clear team assignments but maintain the structure
            foreach (var team in teamPlayers.Keys.ToList())
            {
                teamPlayers[team].Clear();
            }
            winningTeam = 0;
        }
        
        // Reset winning team but preserve team assignments
        public void ResetWinningTeam()
        {
            winningTeam = 0;
        }
        
        // Rebuild team assignments from player data (for use after reset)
        public void RebuildTeamAssignments()
        {
            // Clear team assignments
            foreach (var team in teamPlayers.Keys.ToList())
            {
                teamPlayers[team].Clear();
            }
            
            // Fetch all player data and rebuild team assignments
            if (NetworkManager.Singleton != null)
            {
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        int team = playerData.team;
                        if (team > 0 && team <= teamCount)
                        {
                            if (!teamPlayers.ContainsKey(team))
                            {
                                teamPlayers[team] = new List<ulong>();
                            }
                            teamPlayers[team].Add(clientId);
                        }
                    }
                }
            }
        }

        // Add a single player to a specific team
        public void AddPlayerToTeam(ulong clientId, int teamNumber)
        {
            if (teamNumber > 0 && teamNumber <= teamCount)
            {
                if (!teamPlayers.ContainsKey(teamNumber))
                {
                    teamPlayers[teamNumber] = new List<ulong>();
                }
                
                if (!teamPlayers[teamNumber].Contains(clientId))
                {
                    teamPlayers[teamNumber].Add(clientId);
                }
            }
        }

        public int GetWinningTeam()
        {
            return winningTeam;
        }
    }

    [SerializeField] public float gameTime;
    [SerializeField] public float betweenRoundLength = 5f;
    [SerializeField] public GameState state { get; private set; } = GameState.Pending;

    [Header("Game Mode Settings")]
    [SerializeField] private MenuManager.GameMode gameMode = MenuManager.GameMode.FreeForAll;
    [SerializeField] private int teamCount = 2;
    [SerializeField] private int roundsToWin = 5;  // Default to 5 rounds
    [SerializeField] private bool gameModeLocked = false; // Will be locked after game starts

    // Public getter for the game mode
    public MenuManager.GameMode GetGameMode() 
    {
        return gameMode;
    }

    [Header("Team Battle")]
    [SerializeField] private Material[] teamMaterials; // Materials for each team
    private TeamHelper teamHelper = new TeamHelper();
    private int winningTeamNumber = 0;

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

        // Broadcast to clients only if NetworkManager is initialized
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            BroadcastGameStateClientRpc(newState);
        }
        else
        {
            Debug.LogWarning("[GameManager] Cannot broadcast game state - NetworkManager unavailable");
        }

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

        if (gameMode == MenuManager.GameMode.TeamBattle && winningTeamNumber > 0)
        {
            // Display team winner
            string teamName = teamHelper.GetTeamName(winningTeamNumber);
            menuManager.DisplayGameWinnerClientRpc($"{teamName} TEAM");
        }
        else if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display individual winner
            menuManager.DisplayGameWinnerClientRpc(winner.username);
        }

        // Show scoreboard after a delay
        StartCoroutine(ShowFinalScoreboard());
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
        
        bool isTeamBattle = gameMode == MenuManager.GameMode.TeamBattle;
        
        Debug.Log($"[GameManager] Resetting game state. Team Battle: {isTeamBattle}");

        // Reset all player states to Alive
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                playerData.state = PlayerState.Alive;
                playerData.score = 0; // Reset scores
                
                // Only reset team assignment when not in team battle mode
                // This preserves team assignments between games in team battle mode
                if (!isTeamBattle)
                {
                    playerData.team = 0;  // Reset team assignment
                }
                
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
            }
        }

        // Reset team tracking but preserve assignments in TeamHelper
        if (isTeamBattle)
        {
            // Only reset the winning team, but keep assignments
            winningTeamNumber = 0;
            teamHelper.ResetWinningTeam();
            teamHelper.RebuildTeamAssignments();
        }
        else
        {
            // For non-team modes, fully reset team helper
            teamHelper.Reset();
            winningTeamNumber = 0;
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
        Debug.Log("GameManager OnEndingEnter");
        SetGameState(GameState.Ending);

        // Pause kill feed and keep last message
        if (killFeed != null)
        {
            killFeed.PauseAndKeepLastMessage();
        }

        if (gameMode == MenuManager.GameMode.TeamBattle && winningTeamNumber > 0)
        {
            // Display team round winner
            string teamName = teamHelper.GetTeamName(winningTeamNumber);
            menuManager.DisplayWinnerClientRpc($"{teamName} TEAM");
        }
        else if (ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display individual winner
            menuManager.DisplayWinnerClientRpc(winner.username);
        }

        // Show scoreboard after a delay
        StartCoroutine(menuManager.BetweenRoundTime());
    }

    private void OnPlayingEnter()
    {
        // Lock game mode settings when game starts
        LockGameModeSettings();

        Debug.Log("GameManager OnPlayingEnter");

        // Clear processed deaths for new round
        processedDeaths.Clear();

        // Check if NetworkManager and SoundManager are available
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[GameManager] NetworkManager.Singleton is null in OnPlayingEnter!");
            return;
        }

        if (!gameMusicPlaying && IsServer && SoundManager.Instance != null)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.LevelMusicOn);
            gameMusicPlaying = true;
        }

        // Ensure ConnectionManager instance exists
        if (ConnectionManager.Instance == null)
        {
            Debug.LogError("[GameManager] ConnectionManager.Instance is null in OnPlayingEnter!");
            return;
        }

        // Ensure MenuManager reference exists
        if (menuManager == null)
        {
            Debug.LogError("[GameManager] menuManager is null in OnPlayingEnter!");
            return;
        }

        if (NetworkManager.Singleton.ConnectedClients.Count > 1)
        {
            // Force-mark all players as alive when entering playing state
            if (IsServer)
            {
                Debug.Log("[GameManager] Marking all players as alive");
                foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        playerData.state = PlayerState.Alive;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                        Debug.Log($"[GameManager] Set player {clientId} state to Alive");
                    }
                }
            }
            
            // If team battle mode, assign players to teams
            if (gameMode == MenuManager.GameMode.TeamBattle)
            {
                // Only assign teams when the game first starts (when all players have 0 score)
                bool isFirstRound = IsServer && IsAllPlayersScoreZero();
                if (isFirstRound)
                {
                    Debug.Log("[GameManager] First round detected - Assigning players to teams");
                    AssignPlayersToTeams();
                }
                else
                {
                    Debug.Log("[GameManager] Using existing team assignments");
                }
                
                // Broadcast team battle mode info to ensure clients have the correct mode set
                if (IsServer)
                {
                    BroadcastTeamBattleModeClientRpc();
                }
            }

            // Reset kill feed for new round
            if (killFeed != null)
            {
                killFeed.ResetForNewRound();
            }

            LockPlayerMovement();
            gameTime = 0f;
            
            // First set game state then respawn players
            SetGameState(GameState.Playing);
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

    // Assign players to teams for team battle mode
    private void AssignPlayersToTeams()
    {
        if (!IsServer || gameMode != MenuManager.GameMode.TeamBattle) return;

        // Get all connected players
        List<ulong> players = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);
        
        // Initialize team helper
        teamHelper.Initialize(teamCount);
        
        // Assign players to teams
        teamHelper.AssignPlayersToTeams(players);
        
        // Update player data with team assignments
        for (int teamNumber = 1; teamNumber <= teamCount; teamNumber++)
        {
            foreach (ulong clientId in teamHelper.GetTeamPlayers(teamNumber))
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    playerData.team = teamNumber;
                    ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                    Debug.Log($"Player {clientId} assigned to team {teamNumber}");
                }
            }
        }
    }

    public IEnumerator RoundCountdown()
    {
        Debug.Log("GameManager RoundCountdown started");
        yield return new WaitForSeconds(3f);

        Debug.Log("GameManager RoundCountdown finished - Transitioning to Playing");

        // Unlock player movement
        UnlockPlayerMovement();
        
        // Refresh player states in case any players need to respawn
        if (IsServer)
        {
            Debug.Log("[GameManager] Final check of player states after countdown");
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    // Ensure all players are in Alive state
                    if (playerData.state != PlayerState.Alive)
                    {
                        Debug.Log($"[GameManager] Fixing player {clientId} state after countdown");
                        playerData.state = PlayerState.Alive;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                    }
                }
            }
            
            // Double-check all players have properly respawned
            BroadcastRoundStartClientRpc();
        }

        // Use the existing Resume method to handle cursor locking and input mode
        if (menuManager != null)
            menuManager.Resume();

        // Play round start sound and stop midround music
        if (IsServer)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundStart);
        }
    }
    
    [ClientRpc]
    private void BroadcastRoundStartClientRpc()
    {
        // Local player should check their state at round start
        if (!IsServer && IsClient)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            
            Debug.Log($"[GameManager] Client {localClientId} received round start broadcast");
            
            // If we're not alive, force respawn
            if (ConnectionManager.Instance.TryGetPlayerData(localClientId, out PlayerData playerData) 
                && playerData.state != PlayerState.Alive)
            {
                Debug.Log("[GameManager] Client detected wrong state at round start, requesting respawn");
                
                // Find our Player component
                Player localPlayer = null;
                Player[] allPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (Player p in allPlayers)
                {
                    if (p.IsOwner)
                    {
                        localPlayer = p;
                        break;
                    }
                }
                
                // Request respawn if we found ourselves
                if (localPlayer != null)
                {
                    Debug.Log("[GameManager] Client forcing respawn at round start");
                    localPlayer.Respawn();
                }
            }
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

        if (gameMode == MenuManager.GameMode.TeamBattle)
        {
            // For team battle, check if only one team has players left
            CheckTeamBattleStatus();
        }
        else
        {
            // For free-for-all, check if only one player is left
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
    }

    // Check if a team has won in team battle mode
    private void CheckTeamBattleStatus()
    {
        if (!IsServer || gameMode != MenuManager.GameMode.TeamBattle) return;

        // Get alive players
        List<ulong> alivePlayers = ConnectionManager.Instance.GetAliveClients();
        
        // Debug log alive players to help with diagnostics
        Debug.Log($"[GameManager] CheckTeamBattleStatus: {alivePlayers.Count} alive players");
        foreach (var player in alivePlayers)
        {
            if (ConnectionManager.Instance.TryGetPlayerData(player, out PlayerData playerData))
            {
                Debug.Log($"[GameManager] Alive player: {playerData.username}, Team: {playerData.team}");
            }
        }

        // If no players are alive, force select a winner from the last standing team
        if (alivePlayers.Count == 0)
        {
            Debug.Log("[GameManager] No alive players found, determining winner from last team");
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    Debug.Log($"[GameManager] Checking player: {playerData.username}, Team: {playerData.team}");
                    this.winningTeamNumber = playerData.team;
                    roundWinnerClientId = clientId;
                    break;
                }
            }
            
            if (winningTeamNumber > 0)
            {
                // Award points to all team members
                foreach (ulong clientId in teamHelper.GetTeamPlayers(winningTeamNumber))
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        playerData.score++;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                    }
                }
                
                TransitionToState(GameState.Ending);
                
                // Play round win sound
                if (IsServer)
                {
                    SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundWin);
                }
                return;
            }
        }

        if (teamHelper.CheckForWinningTeam(alivePlayers, out int teamWinnerNumber, out List<ulong> winningTeamPlayers))
        {
            // We have a winning team
            this.winningTeamNumber = teamWinnerNumber;
            roundWinnerClientId = winningTeamPlayers[0];
            
            // Award points to all team members
            foreach (ulong clientId in teamHelper.GetTeamPlayers(teamWinnerNumber))
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    playerData.score++;
                    ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                }
            }

            TransitionToState(GameState.Ending);

            // Play round win sound
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
        if (!IsServer) 
        {
            Debug.LogWarning($"[GameManager] Non-server tried to process death for client {clientId}");
            return;
        }

        Debug.Log($"[GameManager] PlayerDied called for client: {clientId}");

        // Always skip processing if the player is already in the processed deaths list
        // regardless of game state
        if (processedDeaths.Contains(clientId))
        {
            Debug.Log($"[GameManager] Skipping duplicate death processing for client {clientId} (already in processedDeaths)");
            return;
        }

        // In Playing state, add to processedDeaths to prevent duplicates
        if (state == GameState.Playing)
        {
            processedDeaths.Add(clientId);
            Debug.Log($"[GameManager] Added client {clientId} to processedDeaths");
        }

        if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            // Set player state to dead (but always allow pending state deaths)
            if (player.state == PlayerState.Dead && state != GameState.Pending)
            {
                Debug.Log($"[GameManager] Player {player.username} is already marked as dead");
                return;
            }

            Debug.Log($"[GameManager] Processing death for {player.username} (ID: {clientId})");

            // Update player state (only in Playing state)
            if (state == GameState.Playing)
            {
                player.state = PlayerState.Dead;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, player);
                
                // Additional log to confirm player state change
                Debug.Log($"[GameManager] Updated player {player.username} state to Dead, Team: {player.team}");
            }

            // Play death sound
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.PlayerEliminated);

            // Create kill feed message
            // Allow killfeed in both Playing and Pending states
            if (state == GameState.Playing || state == GameState.Pending)
            {
                if (killFeed == null)
                {
                    killFeed = ServiceLocator.GetService<KillFeed>();
                    if (killFeed == null)
                    {
                        Debug.LogError("[GameManager] Failed to get KillFeed from ServiceLocator!");
                        return; // Don't proceed with kill messages if no killfeed
                    }
                }

                var playerCollisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
                if (playerCollisionTracker != null)
                {
                    var lastCollision = playerCollisionTracker.GetLastCollision(clientId);

                    if (lastCollision != null)
                    {
                        // Player was killed by another player
                        Debug.Log($"[GameManager] Player {player.username} was killed by {lastCollision.collidingPlayerName}");
                        killFeed.AddKillMessage(lastCollision.collidingPlayerName, player.username);
                    }
                    else
                    {
                        // Player killed themselves
                        Debug.Log($"[GameManager] Player {player.username} killed themselves (no collision record found)");
                        killFeed.AddSuicideMessage(player.username);
                    }
                }
            }
            else
            {
                Debug.Log($"[GameManager] Not showing kill feed message in state: {state}");
            }
        }

        // Only check game status for Playing state
        if (state == GameState.Playing)
        {
            Debug.Log("[GameManager] Checking game status after player death");
            
            // For team battle, we need to specifically check if this death changed the game status
            if (gameMode == MenuManager.GameMode.TeamBattle)
            {
                Debug.Log("[GameManager] Team battle mode - checking team battle status");
                CheckTeamBattleStatus();
            }
            else
            {
                // For regular free-for-all
                CheckGameStatus();
            }
        }
    }

    private void RespawnAllPlayers()
    {
        Debug.Log("[GameManager] RespawnAllPlayers called");
        
        // Only the server should run the respawn logic
        if (!IsServer) 
        {
            Debug.LogWarning("[GameManager] Non-server tried to respawn all players");
            return;
        }
        
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            Debug.LogWarning("[GameManager] No players found to respawn");
            return;
        }
        
        Debug.Log($"[GameManager] Found {players.Length} players to respawn");
        
        foreach (Player player in players)
        {
            if (player == null) continue;
            
            Debug.Log($"[GameManager] Respawning player {player.clientId}");
            
            // Set player state in PlayerData to Alive
            if (ConnectionManager.Instance == null)
            {
                Debug.LogError("[GameManager] ConnectionManager.Instance is null in RespawnAllPlayers!");
                return;
            }
            
            if (ConnectionManager.Instance.TryGetPlayerData(player.clientId, out PlayerData playerData))
            {
                // Update player state to Alive
                if (playerData.state != PlayerState.Alive)
                {
                    playerData.state = PlayerState.Alive;
                    ConnectionManager.Instance.UpdatePlayerDataClientRpc(player.clientId, playerData);
                    Debug.Log($"[GameManager] Set player {player.clientId} state to Alive");
                }
                
                // Set player material according to team if in team battle mode
                if (gameMode == MenuManager.GameMode.TeamBattle)
                {
                    // Use team number to get color
                    Color teamColor = teamHelper.GetTeamColor(playerData.team);
                    player.SetTeamColor(teamColor);
                    Debug.Log($"[GameManager] Set player {player.clientId} to team color for team {playerData.team}");
                }
            }
            
            // Explicitly force respawn
            player.Respawn();
            
            // For clients, also force respawn via the network hog controller
            NetworkHogController hogController = player.GetComponentInChildren<NetworkHogController>();
            if (hogController != null)
            {
                // Get respawn position from player data
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.TryGetPlayerData(player.clientId, out PlayerData pd))
                {
                    Vector3 respawnPosition = pd.spawnPoint;
                    Quaternion respawnRotation = Quaternion.identity;
                    
                    if (SpawnPointManager.Instance != null)
                    {
                        respawnRotation = Quaternion.LookRotation(
                            SpawnPointManager.Instance.transform.position - pd.spawnPoint);
                    }
                
                    // Force respawn for all clients
                    hogController.ExecuteRespawnClientRpc(respawnPosition, respawnRotation);
                    Debug.Log($"[GameManager] Forced respawn for player {player.clientId} at {respawnPosition}");
                }
            }
        }
        
        // Tell all clients to respawn if they're dead
        ForceClientRespawnClientRpc();
    }
    
    [ClientRpc]
    private void ForceClientRespawnClientRpc()
    {
        // If we're not the server and we're in Playing state, try to respawn our local player if dead
        if (!IsServer && state == GameState.Playing)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            
            // Check if we're currently dead
            if (ConnectionManager.Instance.TryGetPlayerData(localClientId, out PlayerData playerData) 
                && playerData.state == PlayerState.Dead)
            {
                Debug.Log("[GameManager] Client is dead, requesting respawn");
                
                // Find our Player component
                Player localPlayer = null;
                Player[] allPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (Player p in allPlayers)
                {
                    if (p.IsOwner)
                    {
                        localPlayer = p;
                        break;
                    }
                }
                
                // Request respawn if we found ourselves
                if (localPlayer != null)
                {
                    Debug.Log("[GameManager] Client forcing self-respawn");
                    localPlayer.Respawn();
                }
            }
        }
    }

    private void LockPlayerMovement()
    {
        NetworkHogController[] players = FindObjectsByType<NetworkHogController>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            Debug.LogWarning("[GameManager] No players found to lock movement");
            return;
        }
        
        Debug.Log($"[GameManager] Locking movement for {players.Length} players");
        foreach (NetworkHogController player in players)
        {
            if (player != null)
            {
                player.canMove = false;
            }
        }
    }

    private void UnlockPlayerMovement()
    {
        NetworkHogController[] players = FindObjectsByType<NetworkHogController>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            Debug.LogWarning("[GameManager] No players found to unlock movement");
            return;
        }
        
        Debug.Log($"[GameManager] Unlocking movement for {players.Length} players");
        foreach (NetworkHogController player in players)
        {
            if (player != null)
            {
                player.canMove = true;
            }
        }
    }

    [ClientRpc]
    private void BroadcastGameStateClientRpc(GameState newState)
    {
        if (!IsClient)
        {
            // Skip if we're not a client (shouldn't happen, but check anyway)
            return;
        }
        
        Debug.Log($"GameManager BroadcastGameStateClientRpc: {state} -> {newState}");
        state = newState;
        
        if (state == GameState.Ending && SoundManager.Instance != null)
        {
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOff);
        }
    }

    // Method to set game mode (called from MenuManager)
    public void SetGameMode(MenuManager.GameMode mode)
    {
        // Only allow changing game mode before game starts
        if (!gameModeLocked)
        {
            gameMode = mode;
            Debug.Log("Game mode set to: " + gameMode);

            // Notify clients of game mode change
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

            // Notify clients of team count change
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
        Debug.Log($"Client received game mode update: {gameMode}");
        
        // Force UI refresh for clients when game mode changes
        if (state == GameState.Playing || state == GameState.Ending)
        {
            Debug.Log($"Broadcasting game mode change to UI elements");
            // Notify subscribers about state change to trigger UI refresh
            OnGameStateChanged?.Invoke(state);
        }
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

    [ClientRpc]
    private void BroadcastTeamBattleModeClientRpc()
    {
        Debug.Log("[GameManager] Client received team battle mode broadcast");
        
        // Force refresh all UI that depends on game mode
        if (gameMode != MenuManager.GameMode.TeamBattle)
        {
            Debug.Log("[GameManager] Correcting client game mode to team battle");
            gameMode = MenuManager.GameMode.TeamBattle;
        }
        
        // On server: check for any players that don't have team assignments and assign them
        if (IsServer)
        {
            Debug.Log("[GameManager] Server verifying team assignments");
            bool foundUnassignedPlayer = false;
            
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    // Check if player has no team assignment (team=0)
                    if (playerData.team <= 0)
                    {
                        foundUnassignedPlayer = true;
                        Debug.Log($"[GameManager] Found unassigned player {clientId}");
                        break;
                    }
                }
            }
            
            // If any players don't have team assignments, find a team with the fewest players
            if (foundUnassignedPlayer)
            {
                Debug.Log("[GameManager] Assigning unassigned players to teams");
                AssignUnassignedPlayersToTeams();
            }
        }
        
        // On client: apply team colors to local player if needed
        if (!IsServer && IsClient)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            if (ConnectionManager.Instance.TryGetPlayerData(localClientId, out PlayerData playerData))
            {
                if (playerData.team > 0)
                {
                    Debug.Log($"[GameManager] Local player on team {playerData.team}");
                    
                    // Find our Player component and update team color
                    Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
                    foreach (Player player in players)
                    {
                        if (player.IsOwner)
                        {
                            Color teamColor = teamHelper.GetTeamColor(playerData.team);
                            player.SetTeamColor(teamColor);
                            break;
                        }
                    }
                }
            }
        }
        
        // Notify UI of game state change to trigger refresh
        OnGameStateChanged?.Invoke(state);
    }
    
    // Assign any unassigned players to teams with the fewest players
    private void AssignUnassignedPlayersToTeams()
    {
        if (!IsServer || gameMode != MenuManager.GameMode.TeamBattle)
            return;
            
        // Get current team assignments
        Dictionary<int, int> teamCounts = new Dictionary<int, int>();
        for (int i = 1; i <= teamCount; i++)
        {
            teamCounts[i] = 0;
        }
        
        List<ulong> unassignedPlayers = new List<ulong>();
        
        // Count players on each team
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                if (playerData.team > 0 && playerData.team <= teamCount)
                {
                    teamCounts[playerData.team]++;
                }
                else
                {
                    unassignedPlayers.Add(clientId);
                }
            }
        }
        
        // Assign unassigned players to teams with the fewest players
        foreach (ulong clientId in unassignedPlayers)
        {
            // Find team with fewest players
            int targetTeam = 1;
            int minPlayers = int.MaxValue;
            
            for (int i = 1; i <= teamCount; i++)
            {
                if (teamCounts[i] < minPlayers)
                {
                    minPlayers = teamCounts[i];
                    targetTeam = i;
                }
            }
            
            // Assign player to team
            if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                playerData.team = targetTeam;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                
                // Update tracking for next assignment
                teamCounts[targetTeam]++;
                
                Debug.Log($"[GameManager] Assigned unassigned player {clientId} to team {targetTeam}");
            }
        }
    }

    // Check if all players have a score of 0 (indicating first round)
    private bool IsAllPlayersScoreZero()
    {
        if (NetworkManager.Singleton == null || ConnectionManager.Instance == null)
            return false;
            
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
            {
                if (playerData.score > 0)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
