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


    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook playerCamera;
    [SerializeField] public CameraTarget cameraTarget;


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

        // Deactivate main menu camera.
        menuManager = GameObject.Find("Menus").GetComponent<MenuManager>();
        menuManager.menuCamera.gameObject.SetActive(false);
        menuManager.connectionPending.SetActive(false);
        if (IsOwner)
        {
            // Activate player camera;
            playerCamera = GameObject.Find("PlayerCamera").GetComponent<CinemachineFreeLook>();
            playerCamera.gameObject.SetActive(true);

            cameraTarget = GameObject.Find("CameraTarget").GetComponent<CameraTarget>();
            cameraTarget.SetTarget(transform);
        }
    }

    private void Start()
    {
        Cursor.visible = !Cursor.visible; // toggle visibility
        Cursor.lockState = CursorLockMode.Locked;
        menuManager.jumpUI.SetActive(true);

        if (IsOwner)
        {
            // If this is the host/server, open the lobby settings menu
            if (IsServer && GameManager.Instance != null && GameManager.Instance.state == GameState.Pending)
            {
                Debug.Log("[Player] Host player spawned, opening lobby settings menu");

                // Wait a frame to ensure everything is initialized
                StartCoroutine(OpenLobbySettingsAfterPlayerSpawn());
            }
        }

        Player localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        localPlayerCameraTransform = localPlayer.playerCamera.transform;
    }

    // Coroutine to open lobby settings with proper timing after player spawn
    private IEnumerator OpenLobbySettingsAfterPlayerSpawn()
    {
        // Wait a frame for everything to initialize
        yield return null;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[Player] Opening lobby settings from player spawn");

        if (menuManager != null)
        {
            menuManager.OpenLobbySettingsMenu();
        }
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

        ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData);

        if (playerData.state != PlayerState.Alive && GameManager.Instance.state != GameState.Pending)
        {
            List<ulong> aliveClients = ConnectionManager.Instance.GetAliveClients();

            // Check if there are ANY alive clients before proceeding
            if (aliveClients.Count > 0)
            {
                // Make sure spectatingPlayerIndex is within bounds
                if (spectatingPlayerIndex >= aliveClients.Count)
                    spectatingPlayerIndex = 0;

                // Get the player to spectate
                Player spectatePlayer = ConnectionManager.Instance.GetPlayer(aliveClients[spectatingPlayerIndex]);

                // Only follow/look if we got a valid player
                if (spectatePlayer != null)
                {
                    cameraTarget.SetTarget(spectatePlayer.transform);
                }

                // Handle changing spectate target
                if (Input.GetKeyDown(KeyCode.Mouse0))
                {
                    spectatingPlayerIndex = (spectatingPlayerIndex + 1) % aliveClients.Count;
                }
            }
            else
            {
                cameraTarget.SetTarget(transform);
            }
        }
        else
        {
            cameraTarget.SetTarget(transform);
        }

        playerIndicator.SetActive(playerData.isLobbyLeader);
    }
    public override void OnDestroy()
    {
        base.OnDestroy();

        if (nameplate != null && nameplate.gameObject != null)
        {
            Destroy(nameplate.gameObject);
        }
        menuManager.jumpUI.SetActive(false);
    }
    public void Respawn()
    {
        // Get updated playerData from connectionManager
        ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData);

        if (IsServer)
        {
            // Server-side respawn logic
            Debug.Log("Server respawning Player");
            rb.position = playerData.spawnPoint;
            rb.transform.LookAt(SpawnPointManager.Instance.transform);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Set player state to alive and update clients
            if (playerData.state != PlayerState.Alive)
            {
                playerData.state = PlayerState.Alive;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(clientId, playerData);
            }
        }
        else if (IsOwner)
        {
            // Client-side respawn request
            Debug.Log("Client requesting respawn");
            // Find the HogController component in children
            HogController hogController = GetComponent<HogController>();

            if (hogController != null)
            {
                // Request respawn via HogController
                hogController.RequestRespawnServerRpc();
            }
            else
            {
                Debug.LogError("Could not find HogController component for respawn");
            }
        }
    }

    private void OnPlayerDataChanged(PlayerData previousValue, PlayerData newValue)
    {
        ApplyPlayerData(newValue);
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