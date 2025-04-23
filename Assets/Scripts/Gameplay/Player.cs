using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;
using System.Collections.Generic;
using System.Collections;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public ulong clientId;
    [SerializeField] public bool isSpectating;
    [SerializeField] public int colorIndex;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI nameplate;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private GameObject body; // Used to set color texture
    [SerializeField] private GameObject playerIndicator;

    [HideInInspector] public CinemachineFreeLook playerCamera;

    private NetworkVariable<PlayerData> networkPlayerData = new NetworkVariable<PlayerData>(
         new PlayerData
         {
             username = "",
             score = 0,
             colorIndex = 0,
             state = PlayerState.Alive,
             spawnPoint = Vector3.zero,
             isLobbyLeader = false
         },
         NetworkVariableReadPermission.Everyone,
         NetworkVariableWritePermission.Server
     );

    private Canvas worldspaceCanvas;
    private MenuManager menuManager;
    private int spectatingPlayerIndex = 0;
    private Transform localPlayerCameraTransform;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkPlayerData.OnValueChanged += OnPlayerDataChanged;
        clientId = OwnerClientId;
        ConnectionManager.Instance.isConnected = true;

        ApplyPlayerData(networkPlayerData.Value);

        Cursor.visible = !Cursor.visible; // toggle visibility
        Cursor.lockState = CursorLockMode.Locked;

        menuManager = GameObject.Find("Menus").GetComponent<MenuManager>();
        menuManager.menuCamera.gameObject.SetActive(false);
        menuManager.connectionPending.SetActive(false);
        menuManager.jumpUI.SetActive(true);
        if (IsServer) menuManager.ShowLobbySettingsMenu();

        playerCamera = GameObject.Find("PlayerCamera").GetComponent<CinemachineFreeLook>();

        if (IsOwner)
        {
            // Activate player camera;
            playerCamera = GameObject.Find("PlayerCamera").GetComponent<CinemachineFreeLook>();
            playerCamera.gameObject.SetActive(true);
            playerCamera.LookAt = transform;
            playerCamera.Follow = transform;
        }
    }

    public void Start()
    {
        // Needs to be in start due to network initialization.
        Player localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        localPlayerCameraTransform = localPlayer.playerCamera.transform;
    }

    public override void OnNetworkDespawn()
    {

        if (nameplate != null && nameplate.gameObject != null)
        {
            Destroy(nameplate.gameObject);
        }
        menuManager.jumpUI.SetActive(false);
    }

    private void Update()
    {
        // Update Nameplate position
        if (!IsOwner && localPlayerCameraTransform != null)
        {
            var smoothingSpeed = 5f;
            Vector3 targetPosition = rb.position + new Vector3(0, 3f, 0);
            nameplate.transform.position = Vector3.Lerp(nameplate.transform.position, targetPosition, smoothingSpeed * Time.deltaTime);
            Quaternion targetRotation = Quaternion.LookRotation(nameplate.transform.position - localPlayerCameraTransform.transform.position);
            nameplate.transform.rotation = Quaternion.Slerp(nameplate.transform.rotation, targetRotation, smoothingSpeed * Time.deltaTime);
        }

        // Handle spectator input
        if (IsOwner)
        {
            HandleSpectatorInput();
        }

        // Update player leader indicator
        ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData);
        playerIndicator.SetActive(playerData.isLobbyLeader);
    }

    private void HandleSpectatorInput()
    {
        // Only check for input if player is dead and game is active
        ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData);

        if (playerData.state != PlayerState.Alive && GameManager.Instance.state != GameState.Playing)
        {
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                CycleSpectatorTarget();
            }
        }
    }

    private void CycleSpectatorTarget()
    {
        List<ulong> aliveClients = ConnectionManager.Instance.GetAliveClients();

        if (aliveClients.Count > 0)
        {
            spectatingPlayerIndex = (spectatingPlayerIndex + 1) % aliveClients.Count;
            Player spectatePlayer = ConnectionManager.Instance.GetPlayer(aliveClients[spectatingPlayerIndex]);

            if (spectatePlayer != null)
            {
                playerCamera.LookAt = spectatePlayer.transform;
                playerCamera.Follow = spectatePlayer.transform;
            }
        }
    }

    public void Respawn()
    {
        if (!IsServer) return;
        ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData);
        // Server-side respawn logic
        Debug.Log("Respawning Player - " + playerData.username);
        rb.position = playerData.spawnPoint;
        rb.rotation = Quaternion.LookRotation(SpawnPointManager.Instance.transform.position - playerData.spawnPoint);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Set player state to alive and update clients
        if (playerData.state != PlayerState.Alive)
        {
            playerData.state = PlayerState.Alive;
            ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
        }
    }

    private void OnPlayerDataChanged(PlayerData previousValue, PlayerData newValue)
    {
        // Apply visual updates
        ApplyPlayerData(newValue);

        // Check if this is the local player and the state has changed
        if (IsOwner && previousValue.state != newValue.state)
        {
            if (newValue.state != PlayerState.Alive)
            {
                // Player just died, switch to spectator mode
                Debug.Log("Entering Spectator Mode"); 
                StartCoroutine(SetSpectatorCameraDelay());
            }
            else
            {
                // Player became alive, switch back to own camera
                if (previousValue.state == PlayerState.Dead) Debug.Log("Exiting Spectator Mode"); 
                playerCamera.LookAt = transform;
                playerCamera.Follow = transform;
            }
        }
    }

    private IEnumerator SetSpectatorCameraDelay()
    {
        yield return new WaitForSeconds(1f);
        SetSpectatorCamera();
    }

    private void SetSpectatorCamera()
    {
        List<ulong> aliveClients = ConnectionManager.Instance.GetAliveClients();

        // Only proceed if there are alive players to spectate
        if (aliveClients.Count > 0)
        {
            spectatingPlayerIndex = 0;
            Player spectatePlayer = ConnectionManager.Instance.GetPlayer(aliveClients[spectatingPlayerIndex]);

            if (spectatePlayer != null)
            {
                playerCamera.LookAt = spectatePlayer.transform;
                playerCamera.Follow = spectatePlayer.transform;
            }
        }
        else
        {
            // No alive players to spectate, focus on self
            playerCamera.LookAt = transform;
            playerCamera.Follow = transform;
        }
    }

    private void ApplyPlayerData(PlayerData playerData)
    {
        if (body != null && ConnectionManager.Instance != null)
        {
            body.GetComponent<Renderer>().material = ConnectionManager.Instance.hogTextures[playerData.colorIndex];
        }

        // Only display floating username for non-local players
        if (!IsOwner)
        {
            // Update floating username
            if (worldspaceCanvas == null)
            {
                worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
            }

            nameplate.text = playerData.username;
            nameplate.transform.SetParent(worldspaceCanvas.transform);
            nameplate.gameObject.SetActive(true);
        }
        else
        {
            // Hide username for local player
            if (nameplate != null)
            {
                nameplate.gameObject.SetActive(false);
            }
        }
    }

    public void SetPlayerData(PlayerData playerData)
    {
        if (IsServer)
        {
            networkPlayerData.Value = playerData;
        }
    }
}