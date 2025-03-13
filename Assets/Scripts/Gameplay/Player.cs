using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;
using System.Collections.Generic;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public ulong clientId;
    [SerializeField] public bool isSpectating;
    [SerializeField] public int colorIndex;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private GameObject body; // Used to set color texture
    [SerializeField] private GameObject playerIndicator; // Used to set color texture

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;
    [SerializeField] public Transform cameraTarget;
    [SerializeField] private Transform cameraOffset;

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
        if (networkPlayerData.Value.username != "")
        {
            ApplyPlayerData(networkPlayerData.Value);
        }

        // Set up camera for local player
        if (IsOwner)
        {
            SetupLocalPlayerCamera();
        }
    }

    private void Awake()
    {
        menuManager = GameObject.Find("Menus").GetComponent<MenuManager>();
    }

    private void Start()
    {
        if (IsOwner)
        {
            // Local player setup
            Cursor.visible = !Cursor.visible;
            Cursor.lockState = CursorLockMode.Locked;
            ConnectionManager.instance.isConnected = true;
            menuManager.jumpUI.SetActive(true);
            menuManager.startCamera.gameObject.SetActive(false);
            menuManager.connectionPending.SetActive(false);
        }
        else
        {
            // Remote player setup
            mainCamera.Priority = 0;
            audioListener.enabled = false;
        }

        cameraTarget.rotation = Quaternion.identity;
    }

    private void SetupLocalPlayerCamera()
    {
        audioListener.enabled = true;
        mainCamera.Priority = 1;

        if (mainCamera != null)
        {
            mainCamera.Follow = cameraTarget;
            mainCamera.LookAt = cameraTarget;
        }

        // Store local player camera transform for other players to use
        localPlayerCameraTransform = mainCamera.transform;
    }

    private void Update()
    {
        // Update camera target position
        cameraTarget.position = cameraOffset.position;

        // Only update floating username position for non-local players
        if (!IsOwner)
        {
            UpdateFloatingUsername();
        }

        // Handle camera states
        UpdateCameraState();
    }

    private void UpdateFloatingUsername()
    {
        if (localPlayerCameraTransform == null)
        {
            // Try to get the local player's camera transform if not set
            Player localPlayer = NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<Player>();
            if (localPlayer != null && localPlayer.mainCamera != null)
            {
                localPlayerCameraTransform = localPlayer.mainCamera.transform;
            }
        }

        if (localPlayerCameraTransform != null)
        {
            floatingUsername.transform.position = rb.position + new Vector3(0, 3f, 0);
            floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - localPlayerCameraTransform.position);
        }
    }

    private void UpdateCameraState()
    {
        ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData);
        
        if (playerData.state != PlayerState.Alive && GameManager.instance.state != GameState.Pending)
        {
            HandleSpectatorCamera();
        }
        else
        {
            // Not dead or in pending state - use own camera
            mainCamera.Follow = cameraTarget;
            mainCamera.LookAt = cameraTarget;
        }

        playerIndicator.SetActive(playerData.isLobbyLeader);
    }

    private void HandleSpectatorCamera()
    {
        List<ulong> aliveClients = ConnectionManager.instance.GetAliveClients();

        if (aliveClients.Count > 0)
        {
            // Make sure spectatingPlayerIndex is within bounds
            if (spectatingPlayerIndex >= aliveClients.Count)
                spectatingPlayerIndex = 0;

            // Get the player to spectate
            Player spectatePlayer = ConnectionManager.instance.GetPlayer(aliveClients[spectatingPlayerIndex]);

            // Only follow/look if we got a valid player
            if (spectatePlayer != null)
            {
                mainCamera.Follow = spectatePlayer.cameraTarget;
                mainCamera.LookAt = spectatePlayer.cameraTarget;
            }

            // Handle changing spectate target
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                spectatingPlayerIndex = (spectatingPlayerIndex + 1) % aliveClients.Count;
            }
        }
        else
        {
            // No alive players to spectate, fall back to own camera
            mainCamera.Follow = cameraTarget;
            mainCamera.LookAt = cameraTarget;
        }
    }

    public void Respawn()
    {
        // Get updated playerData from connectionManager.
        ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData);
        if (!IsServer) return;
        Debug.Log("Respawning Player");
        rb.position = playerData.spawnPoint;
        rb.rotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - playerData.spawnPoint);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Set player state to alive and update clients
        if (playerData.state != PlayerState.Alive)
        {
            playerData.state = PlayerState.Alive;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, playerData);
        }
    }

    private void OnPlayerDataChanged(PlayerData previousValue, PlayerData newValue)
    {
        ApplyPlayerData(newValue);
    }

    private void ApplyPlayerData(PlayerData playerData)
    {
        if (body != null && ConnectionManager.instance != null)
        {
            body.GetComponent<Renderer>().material = ConnectionManager.instance.hogTextures[playerData.colorIndex];
        }

        // Only display floating username for non-local players
        if (!IsOwner)
        {
            // Update floating username
            if (worldspaceCanvas == null)
            {
                worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
            }

            floatingUsername.text = playerData.username;
            floatingUsername.transform.SetParent(worldspaceCanvas.transform);
            floatingUsername.gameObject.SetActive(true);
        }
        else
        {
            // Hide username for local player
            if (floatingUsername != null)
            {
                floatingUsername.gameObject.SetActive(false);
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

    public override void OnDestroy()
    {
        // Call the base implementation first (important!)
        base.OnDestroy();

        // Then do our custom cleanup
        if (floatingUsername != null && floatingUsername.gameObject != null)
        {
            Destroy(floatingUsername.gameObject);
        }
        menuManager.jumpUI.SetActive(false);
    }
}