using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] public GameMode gameMode = GameMode.FreeForAll;
    [SerializeField] private int teamCount = 2;
    [SerializeField] private int roundsToWin = 5;  // Default to 5 rounds
    [SerializeField] private bool gameModeLocked = false; // Will be locked after game starts
    [SerializeField] private Material[] teamMaterials; // Materials for each team

    [Header("References")]
    [SerializeField] public MenuManager menuManager;

    public static GameManager Instance;
    private ulong roundWinnerClientId;
    private int winningTeamNumber = 0;
    private bool gameMusicPlaying;
    private bool timeWarningHasPlayed = false;

    // TeamHelper integrated properties
    private Dictionary<int, List<ulong>> teamPlayers = new Dictionary<int, List<ulong>>();
    private int winningTeam = 0;

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

        // Get KillFeed reference if not set
        if (killFeed == null)
        {
            killFeed = KillFeed.Instance;
            Debug.Log($"[GAME] Found KillFeed via singleton: {(killFeed != null)}");
        }

        TransitionToState(GameState.Pending);
    }

    public void Update()
    {
        if (state == GameState.Playing)
            gameTime += Time.deltaTime;

        if (gameTime > 40f && !timeWarningHasPlayed)
        {
            timeWarningHasPlayed = true;
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundTimeWarning);
        }
    }

    public void TransitionToState(GameState newState)
    {
        Debug.Log($"[GAME] TransitionToState: {state} -> {newState}");

        switch (newState)
        {
            case GameState.Pending:
                OnPendingEnter();
                break;
            case GameState.Start:
                OnStartEnter();
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
        if (IsServer && ConnectionManager.Instance.isConnected)
        {
            // Show the lobby settings menu for the host
            ShowLobbySettings();
        }
    }

    private void OnStartEnter()
    {
        if (!IsServer) return;

        SetGameState(GameState.Start);
        // Get all players and player objects just once
        List<ulong> clientIds = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds);

        // If we are playing a team battle assign teams.
        if (gameMode == GameMode.TeamBattle)
        {
            InitializeTeams(teamCount);
            AssignAllPlayersToTeams(clientIds);

            for (int teamNumber = 1; teamNumber <= teamCount; teamNumber++)
            {
                foreach (ulong clientId in GetTeamPlayers(teamNumber))
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        playerData.team = teamNumber;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                        Debug.Log($"[GAME] Player {clientId} assigned to team {teamNumber}");
                    }
                }
            }
        }
        
        if (gameMode == GameMode.FreeForAll)
        {
            // For non-TeamBattle modes, ensure all players have team value of 0
            foreach (ulong clientId in clientIds)
            {
                if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                {
                    if (playerData.team != 0)
                    {
                        playerData.team = 0;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
                        Debug.Log($"[GAME] Player {clientId} team reset to 0 for non-team mode");
                    }
                }
            }
        }
        TransitionToState(GameState.Playing);
    }

    private void OnBetweenRoundEnter()
    {
        SetGameState(GameState.BetweenRound);

        // Pause kill feed and keep last message
        killFeed.ClearAllMessagesExceptLast();

        if (gameMode == GameMode.TeamBattle && winningTeamNumber > 0)
        {
            // Display team round winner
            string teamName = GetTeamName(winningTeamNumber);

            // Display team win message
            menuManager.DisplayTeamWinnerClientRpc(winningTeamNumber, false);

            Debug.Log("[GAME] Battle Mode Round Winner - " + teamName);

            // Award points to all team members
            if (IsServer)
            {
                foreach (ulong clientId in GetTeamPlayers(winningTeamNumber))
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        playerData.score++;
                        ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);

                        // Check if game is over
                        if (playerData.score >= roundsToWin)
                        {
                            TransitionToState(GameState.GameEnd);
                            return;
                        }
                    }
                }
            }
        }

        if (gameMode == GameMode.FreeForAll && ConnectionManager.Instance.TryGetPlayerData(roundWinnerClientId, out PlayerData winner))
        {
            // Display individual winner
            menuManager.DisplayGameWinnerClientRpc(roundWinnerClientId, false);

            if (IsServer)
            {
                winner.score++;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(roundWinnerClientId, winner);

                // Check if game is over
                if (winner.score >= roundsToWin)
                {
                    TransitionToState(GameState.GameEnd);
                    return;
                }
            }
        }

        ConnectionManager.Instance.UpdateLobbyLeaderBasedOnScore();

        StartCoroutine(BetweenRoundTimer());
    }

    private void OnPlayingEnter()
    {
        // Lock game mode settings when game starts
        gameModeLocked = true;
        timeWarningHasPlayed = false;
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

    private void OnGameEndEnter()
    {
        Debug.Log("[GAME] OnEndingEnter");
        SetGameState(GameState.GameEnd);

        // Pause kill feed and keep last message
        killFeed.ClearAllMessagesExceptLast();

        // Display winner celebration message
        if (gameMode == GameMode.FreeForAll) menuManager.DisplayGameWinnerClientRpc(roundWinnerClientId, true);
        if (gameMode == GameMode.TeamBattle) menuManager.DisplayTeamWinnerClientRpc(winningTeam, true);

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
        gameModeLocked = false;
        Debug.Log("[GAME] Game mode settings unlocked");

        // Respawn all players at their spawn points
        RespawnAllPlayers();

        // Clear any remaining processed deaths
        processedDeaths.Clear();

        // Reset team tracking
        ResetTeams();
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

    public void ShowLobbySettings()
    {
        if (IsServer && state == GameState.Pending) menuManager.OpenLobbySettingsMenu();
    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;

        List<ulong> alivePlayers = ConnectionManager.Instance.GetAliveClients();
        bool roundOver = false;

        if (gameMode == GameMode.TeamBattle)
        {
            // Team Battle mode
            if (alivePlayers.Count == 0)
            {
                // Everyone died - pick first player we find as representative of winning team
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
                    {
                        winningTeamNumber = playerData.team;
                        roundWinnerClientId = clientId;
                        roundOver = true;
                        break;
                    }
                }
            }
            else if (CheckForWinningTeam(alivePlayers, out int teamWinnerNumber, out List<ulong> winningTeamPlayers))
            {
                // One team remains
                winningTeamNumber = teamWinnerNumber;
                roundWinnerClientId = winningTeamPlayers.Count > 0 ? winningTeamPlayers[0] : 0;
                roundOver = true;
            }
        }

        if (gameMode == GameMode.FreeForAll)
        {
            // Free-for-all mode
            if (alivePlayers.Count == 1)
            {
                roundWinnerClientId = alivePlayers[0];
                roundOver = true;
            }
        }

        // If round is over, trigger state change and sounds
        if (roundOver)
        {
            TransitionToState(GameState.BetweenRound);
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.RoundWin);
            SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOn);
        }
    }

    // Method to handle player killed events
    public void PlayerDied(ulong clientId)
    {
        if (processedDeaths.Contains(clientId)) return;
        if (state == GameState.Playing) processedDeaths.Add(clientId);

        Debug.Log($"[GAME] PlayerDied called for client: {clientId}");

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
            if (state == GameState.Playing || state == GameState.Pending)
            {
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
        if (state == GameState.Playing) CheckGameStatus();
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

    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void BroadcastGameStateClientRpc(GameState newState)
    {
        Debug.Log($"[GAME] BroadcastGameStateClientRpc: {state} -> {newState}");
        state = newState;
        if (state == GameState.BetweenRound) SoundManager.Instance.BroadcastGlobalSound(SoundManager.SoundEffectType.MidRoundOff);
    }

    // Method to set game mode (called from MenuManager)
    public void SetGameMode(GameMode mode)
    {
        // Only allow changing game mode before game starts
        if (!gameModeLocked)
        {
            gameMode = mode;
            Debug.Log("[GAME] Game mode set to: " + gameMode);

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
        if (!gameModeLocked && gameMode == GameMode.TeamBattle)
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
    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    public void UpdateGameModeClientRpc(GameMode mode)
    {
        // Update local game mode
        gameMode = mode;

        Debug.Log("[GAME] Client received game mode update: " + gameMode);
    }

    // Client RPC to sync team count with clients
    [ClientRpc(Delivery = RpcDelivery.Reliable)]
    private void UpdateTeamCountClientRpc(int count)
    {
        // Update local team count
        teamCount = count;
        Debug.Log("[GAME] Client received team count update: " + teamCount);
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

    #region Team Management Functions

    public void InitializeTeams(int count)
    {
        teamCount = Mathf.Clamp(count, 2, 4);
        teamPlayers.Clear();

        for (int i = 1; i <= teamCount; i++)
        {
            teamPlayers[i] = new List<ulong>();
        }
    }

    private void AssignAllPlayersToTeams(List<ulong> players)
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

    public int AssignPlayerToTeam(ulong clientId)
    {
        // Only handle team assignment in Team Battle mode
        if (gameMode != GameMode.TeamBattle)
            return 0;

        // Find the team with the fewest players
        int minTeamNumber = 1;
        int minPlayerCount = int.MaxValue;

        for (int i = 1; i <= teamCount; i++)
        {
            int teamSize = teamPlayers.TryGetValue(i, out List<ulong> players) ? players.Count : 0;
            if (teamSize < minPlayerCount)
            {
                minPlayerCount = teamSize;
                minTeamNumber = i;
            }
        }

        // Add player to the team with fewest members
        if (!teamPlayers.ContainsKey(minTeamNumber))
        {
            teamPlayers[minTeamNumber] = new List<ulong>();
        }

        teamPlayers[minTeamNumber].Add(clientId);

        // Update player data with new team assignment
        if (ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
        {
            playerData.team = minTeamNumber;
            ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
            Debug.Log($"[GAME] Player {clientId} assigned to team {minTeamNumber} (Team {GetTeamName(minTeamNumber)})");
        }

        return minTeamNumber;
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
        switch (teamNumber)
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
        switch (teamNumber)
        {
            case 1: return new Color(1.0f, 0.2f, 0.2f); // Red
            case 2: return new Color(0.2f, 0.4f, 1.0f); // Blue
            case 3: return new Color(0.2f, 0.8f, 0.2f); // Green
            case 4: return new Color(1.0f, 0.8f, 0.2f); // Yellow
            default: return Color.white;
        }
    }

    public void ResetTeams()
    {
        // Clear team assignments but maintain the structure
        foreach (var team in teamPlayers.Keys.ToList())
        {
            teamPlayers[team].Clear();
        }
        winningTeam = 0;
    }

    #endregion
}