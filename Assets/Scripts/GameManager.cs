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
        {
            PlayLevelMusicClientRpc();
        }
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
        SetGameState(GameState.Playing);
        BroadcastMidroundOffClientRpc();
    }

    public IEnumerator BetweenRoundTimer()
    {
        // Configuration values
        float showWinnerDuration = 2.0f;     // How long to show just the winner text
        float showScoreboardDuration = 3.0f;  // How long to show the scoreboard

        menuManager.HideScoreboardClientRpc();  //Don't love that I have to do this here but the scoreboard is popping up too early if I don't
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
        {
            Debug.Log("MidRoundOn");
            MidroundOn.Post(gameObject);
        }
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
}