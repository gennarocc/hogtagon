using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    [SerializeField] public float gameTime;
    public GameState state { get; private set; } = GameState.Pending;
    public static GameManager instance;

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

    }

    private void OnPlayingEnter()
    {
        gameTime = 0f;
        if (ConnectionManager.instance.GetAlivePlayers().Count > 1)
        {
            // RespawnAllPlayers();
            // StartRound();
        }
    }

    private void OnPendingEnter()
    {

    }

    public void CheckGameStatus()
    {
        if (state != GameState.Playing) return;
        List<PlayerData> alivePlayers = ConnectionManager.instance.GetAlivePlayers();

        if (alivePlayers.Count == 1)
        {
            TransitionToState(GameState.Ending);
        }
    }

    // [ServerRpc(RequireOwnership = false)]
    public void PlayerDied(ulong clientId)
    {
        if (ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData player))
        {
            player.score++;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, player);
        }

        CheckGameStatus();
    }
}