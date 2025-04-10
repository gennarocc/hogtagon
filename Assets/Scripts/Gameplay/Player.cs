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
        
        // Make sure ConnectionManager exists
        if (ConnectionManager.Instance != null)
        {
            ConnectionManager.Instance.isConnected = true;
            ApplyPlayerData(networkPlayerData.Value);
        }

        // Find and deactivate main menu camera with proper null checks
        GameObject menuObj = GameObject.Find("Menus");
        if (menuObj != null)
        {
            menuManager = menuObj.GetComponent<MenuManager>();
            if (menuManager != null)
            {
                if (menuManager.menuCamera != null)
                    menuManager.menuCamera.gameObject.SetActive(false);
                
                if (menuManager.connectionPending != null)
                    menuManager.connectionPending.SetActive(false);
            }
        }
        
        if (IsOwner)
        {
            // Activate player camera with proper null checks
            GameObject playerCameraObj = GameObject.Find("PlayerCamera");
            if (playerCameraObj != null)
            {
                playerCamera = playerCameraObj.GetComponent<CinemachineFreeLook>();
                if (playerCamera != null)
                {
                    playerCamera.gameObject.SetActive(true);
                }
            }

            // Set up camera target with proper null checks
            GameObject cameraTargetObj = GameObject.Find("CameraTarget");
            if (cameraTargetObj != null)
            {
                cameraTarget = cameraTargetObj.GetComponent<CameraTarget>();
                if (cameraTarget != null)
                {
                    cameraTarget.SetTarget(transform);
                }
            }
        }
    }

    private void Start()
    {
        // Set cursor state
        Cursor.visible = !Cursor.visible; // toggle visibility
        Cursor.lockState = CursorLockMode.Locked;
        
        // Safely activate jumpUI
        if (menuManager != null && menuManager.jumpUI != null)
        {
            menuManager.jumpUI.SetActive(true);
        }

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

        // Set up local player reference - ONLY if we are the client that owns this player
        if (IsOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null && 
            NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            Player localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
            if (localPlayer != null && localPlayer.playerCamera != null)
            {
                localPlayerCameraTransform = localPlayer.playerCamera.transform;
            }
        }
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
        // First check that we have the required references before proceeding
        if (rb == null || ConnectionManager.Instance == null) return;
        
        // Update Nameplate position
        if (!IsOwner && nameplate != null && localPlayerCameraTransform != null)
        {
            var smoothingSpeed = 5f;
            Vector3 targetPosition = rb.position + new Vector3(0, 3f, 0);
            nameplate.transform.position = Vector3.Lerp(nameplate.transform.position, targetPosition, smoothingSpeed * Time.deltaTime);
            Quaternion targetRotation = Quaternion.LookRotation(nameplate.transform.position - localPlayerCameraTransform.position);
            nameplate.transform.rotation = Quaternion.Slerp(nameplate.transform.rotation, targetRotation, smoothingSpeed * Time.deltaTime);
        }

        // Get player data with null check
        PlayerData playerData = new PlayerData(); // Default empty data
        ConnectionManager.Instance.TryGetPlayerData(clientId, out playerData);

        // Only proceed with camera targeting if we have a valid cameraTarget
        if (cameraTarget != null)
        {
            if (playerData.state != PlayerState.Alive && GameManager.Instance != null && GameManager.Instance.state != GameState.Pending)
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
        }

        // Set player indicator with null check
        if (playerIndicator != null)
        {
            playerIndicator.SetActive(playerData.isLobbyLeader);
        }
    }
    
    public override void OnDestroy()
    {
        base.OnDestroy();

        if (nameplate != null && nameplate.gameObject != null)
        {
            Destroy(nameplate.gameObject);
        }
        
        if (menuManager != null && menuManager.jumpUI != null)
        {
            menuManager.jumpUI.SetActive(false);
        }
    }
    
    public void Respawn()
    {
        // Ensure ConnectionManager exists
        if (ConnectionManager.Instance == null) return;
        
        // Get updated playerData from connectionManager
        PlayerData playerData = new PlayerData();
        ConnectionManager.Instance.TryGetPlayerData(clientId, out playerData);

        if (IsServer)
        {
            // Server-side respawn logic with null checks
            Debug.Log("Server respawning Player");
            if (rb != null)
            {
                rb.position = playerData.spawnPoint;
                if (SpawnPointManager.Instance != null)
                {
                    rb.transform.LookAt(SpawnPointManager.Instance.transform);
                }
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

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
        // Apply player color with null checks
        if (body != null && ConnectionManager.Instance != null && 
            ConnectionManager.Instance.hogTextures != null && 
            playerData.colorIndex >= 0 && playerData.colorIndex < ConnectionManager.Instance.hogTextures.Length)
        {
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                bodyRenderer.material = ConnectionManager.Instance.hogTextures[playerData.colorIndex];
            }
        }

        // Only display floating username for non-local players
        if (!IsOwner && nameplate != null)
        {
            // Update floating username
            if (worldspaceCanvas == null)
            {
                GameObject canvasObj = GameObject.Find("WorldspaceCanvas");
                if (canvasObj != null)
                {
                    worldspaceCanvas = canvasObj.GetComponent<Canvas>();
                }
            }

            if (worldspaceCanvas != null)
            {
                nameplate.text = playerData.username;
                nameplate.transform.SetParent(worldspaceCanvas.transform);
                nameplate.gameObject.SetActive(true);
            }
        }
        else if (nameplate != null)
        {
            // Hide username for local player
            nameplate.gameObject.SetActive(false);
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