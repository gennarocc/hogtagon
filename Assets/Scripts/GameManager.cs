using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

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
        if (state == GameState.Playing)
            gameTime += Time.deltaTime;
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
        SetGameState(GameState.Ending);
        ConnectionManager.instance.TryGetPlayerData(roundWinnerClientId, out PlayerData roundWinner);

        menuManager.DisplayWinnerClientRpc(roundWinner.username);
        roundWinner.score++;

        ConnectionManager.instance.UpdatePlayerDataClientRpc(roundWinnerClientId, roundWinner);
        ConnectionManager.instance.UpdateLobbyLeaderBasedOnScore();

        StartCoroutine(BetweenRoundTimer());
    }

    private void OnPlayingEnter()
    {
        if (!gameMusicPlaying)
            PlayLevelMusicClientRpc();

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
        float showWinnerDuration = 2.0f;     // How long to show just the winner text
        float showScoreboardDuration = 3.0f;  // How long to show the scoreboard

        menuManager.HideScoreboardClientRpc();
        yield return new WaitForSeconds(showWinnerDuration);

        // Now show the scoreboard
        menuManager.ShowScoreboardClientRpc();
        yield return new WaitForSeconds(showScoreboardDuration);

        // Hide scoreboard when starting new round
        menuManager.HideScoreboardClientRpc();

        // Transition to next round
        OnPlayingEnter();
    }

    private void OnPendingEnter()
    {
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

            // Add killfeed entry
            var playerCollisionTracker = FindObjectOfType<PlayerCollisionTracker>();
            if (playerCollisionTracker != null)
            {
                var lastCollision = playerCollisionTracker.GetLastCollision(clientId);
                
                if (lastCollision != null)
                {
                    // Player was killed by another player
                    ShowKillFeedMessageClientRpc(lastCollision.collidingPlayerName, player.username, false);
                }
                else
                {
                    // Player killed themselves
                    ShowKillFeedMessageClientRpc(player.username, "", true);
                }
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

    private void SetGameState(GameState state)
    {
        this.state = state;
        BroadcastGameStateClientRpc(state);
    }

    [ClientRpc]
    private void BroadcastGameStateClientRpc(GameState state)
    {
        this.state = state;
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

    [ClientRpc]
    private void ShowKillFeedMessageClientRpc(string killerName, string victimName, bool isSuicide)
    {
        var killFeed = FindObjectOfType<KillFeed>();
        if (killFeed != null)
        {
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
}