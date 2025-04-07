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
    private NetworkHogController hogController;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"[Player] OnNetworkSpawn - IsOwner: {IsOwner}, NetworkObject.IsSpawned: {NetworkObject.IsSpawned}, GameObject: {gameObject.name}");
        
        networkPlayerData.OnValueChanged += OnPlayerDataChanged;
        
        // Get references
        menuManager = MenuManager.Instance;
        if (menuManager == null)
        {
            Debug.LogError("[Player] MenuManager not found!");
        }
        else
        {
            Debug.Log("[Player] MenuManager found successfully");
        }
        
        // Cache the NetworkHogController reference - look in parent since that's where it is
        hogController = transform.parent.GetComponent<NetworkHogController>();
        if (hogController == null)
        {
            Debug.LogError("[Player] Failed to find NetworkHogController in parent object!");
            return;
        }
        else
        {
            Debug.Log($"[Player] Found NetworkHogController on {transform.parent.name}");
        }
        
        // If this is the local player, set up the camera
        if (IsOwner)
        {
            Debug.Log("[Player] This is the local player, setting up camera");
            SetupLocalPlayerCamera();
        }
        else
        {
            Debug.Log("[Player] This is not the local player, skipping camera setup");
        }
    }
    
    private void SetupLocalPlayerCamera()
    {
        Debug.Log("[Player] Starting SetupLocalPlayerCamera");
        Debug.Log($"[Player] Camera references - Target: {(cameraTarget != null ? cameraTarget.name : "null")}, Offset: {(cameraOffset != null ? cameraOffset.name : "null")}");
        
        // Wait for NetworkObject to be ready
        if (!NetworkObject.IsSpawned)
        {
            Debug.LogWarning("[Player] NetworkObject not spawned yet, starting camera setup coroutine");
            StartCoroutine(WaitForNetworkObjectAndSetupCamera());
            return;
        }
        
        // Get the virtual camera from the MenuManager
        if (menuManager != null)
        {
            var virtualCamera = menuManager.GetVirtualCamera();
            Debug.Log($"[Player] Got virtual camera from MenuManager: {(virtualCamera != null ? "success" : "null")}");
            
            if (virtualCamera != null)
            {
                // Convert the virtual camera to a FreeLook camera
                mainCamera = virtualCamera.gameObject.GetComponent<CinemachineFreeLook>();
                if (mainCamera == null)
                {
                    // If it's not already a FreeLook camera, convert it
                    Destroy(virtualCamera);
                    mainCamera = virtualCamera.gameObject.AddComponent<CinemachineFreeLook>();
                }
                
                Debug.Log("[Player] Configuring FreeLook camera settings");

                // Add and configure input provider
                var inputProvider = mainCamera.gameObject.GetComponent<CinemachineInputProvider>();
                if (inputProvider == null)
                {
                    inputProvider = mainCamera.gameObject.AddComponent<CinemachineInputProvider>();
                }
                
                // Configure the input axes
                mainCamera.m_XAxis.m_InputAxisName = "Mouse X";
                mainCamera.m_YAxis.m_InputAxisName = "Mouse Y";
                
                // Enable input
                mainCamera.m_XAxis.m_InputAxisValue = 0;
                mainCamera.m_YAxis.m_InputAxisValue = 0;
                
                Debug.Log("[Player] Configured camera input");
                
                // Switch to gameplay mode to enable input
                if (InputManager.Instance != null)
                {
                    InputManager.Instance.SwitchToGameplayMode();
                    Debug.Log("[Player] Switched to gameplay mode");
                }
                else
                {
                    Debug.LogError("[Player] InputManager not found!");
                }
                
                // Set up standard FreeLook camera settings
                mainCamera.m_Lens.FieldOfView = 60f;
                mainCamera.m_YAxis.Value = 0.5f;
                mainCamera.m_YAxis.m_MaxSpeed = 2f;
                mainCamera.m_YAxis.m_InvertInput = true;
                mainCamera.m_XAxis.Value = 0f;
                mainCamera.m_XAxis.m_MaxSpeed = 300f;
                mainCamera.m_XAxis.m_InvertInput = false;
                
                // Set up the orbits
                mainCamera.m_Orbits = new CinemachineFreeLook.Orbit[3];
                mainCamera.m_Orbits[0].m_Height = 5.0f;
                mainCamera.m_Orbits[0].m_Radius = 8.5f;
                mainCamera.m_Orbits[1].m_Height = 3.0f;
                mainCamera.m_Orbits[1].m_Radius = 10f;
                mainCamera.m_Orbits[2].m_Height = 0.4f;
                mainCamera.m_Orbits[2].m_Radius = 7.5f;
                
                // Set priority based on ownership
                mainCamera.Priority = IsOwner ? 15 : 0;
                Debug.Log($"[Player] Set camera priority to {mainCamera.Priority} based on ownership: {IsOwner}");
                
                // Set up the camera target
                if (cameraTarget != null && cameraOffset != null)
                {
                    Debug.Log("[Player] Setting up camera target and offset");
                    // Update camera target position to match offset
                    cameraTarget.position = cameraOffset.position;
                    
                    // Set up the camera to follow and look at the target
                    mainCamera.Follow = cameraTarget;
                    mainCamera.LookAt = cameraTarget;
                    
                    Debug.Log($"[Player] Camera target set - Position: {cameraTarget.position}, Rotation: {cameraTarget.rotation.eulerAngles}");
                }
                else
                {
                    Debug.LogError($"[Player] Camera setup failed - Target: {(cameraTarget != null)}, Offset: {(cameraOffset != null)}");
                    return;
                }
                
                // Reset any potential input values
                mainCamera.m_XAxis.m_InputAxisValue = 0f;
                mainCamera.m_YAxis.m_InputAxisValue = 0f;
                
                Debug.Log("[Player] FreeLook camera initial setup complete");

                // Make sure we have the NetworkHogController reference
                if (hogController == null)
                {
                    hogController = transform.parent.GetComponent<NetworkHogController>();
                    if (hogController == null)
                    {
                        Debug.LogError("[Player] Failed to find NetworkHogController for camera assignment");
                        return;
                    }
                }

                // Assign the camera to the NetworkHogController
                hogController.AssignCamera(mainCamera);
                Debug.Log("[Player] Assigned FreeLook camera to NetworkHogController");
                
                // Start a coroutine to set the proper rotation after everything is initialized
                StartCoroutine(SetInitialCameraRotation());
                
                Debug.Log($"[Player] {clientId}: Camera setup complete with custom blend settings");
            }
            else
            {
                Debug.LogError("[Player] Virtual camera is null in MenuManager");
            }
        }
        else
        {
            Debug.LogError("[Player] MenuManager is null");
        }
    }

    private IEnumerator WaitForNetworkObjectAndSetupCamera()
    {
        yield return new WaitUntil(() => NetworkObject.IsSpawned);
        
        // Also wait for the NetworkHogController to be ready
        yield return new WaitUntil(() => hogController != null && hogController.NetworkObject.IsSpawned);
        
        Debug.Log("NetworkObject and NetworkHogController are ready, setting up camera");
        SetupLocalPlayerCamera();
    }

    private IEnumerator SetInitialCameraRotation()
    {
        // Wait for rigidbody to be initialized
        yield return new WaitUntil(() => rb != null);
        
        // Get the Hog's rotation directly from its transform
        float hogYRotation = transform.parent.eulerAngles.y;
        Debug.Log($"Setting initial camera rotation - Hog Y rotation: {hogYRotation}");
        
        if (mainCamera != null && cameraTarget != null)
        {
            // Set the camera's rotation to match the Hog's Y rotation exactly
            mainCamera.m_XAxis.Value = hogYRotation;
            mainCamera.m_YAxis.Value = 0.5f;
            
            // Reset any input values
            mainCamera.m_XAxis.m_InputAxisValue = 0f;
            mainCamera.m_YAxis.m_InputAxisValue = 0f;
            
            // Update the camera target rotation to match
            cameraTarget.rotation = Quaternion.Euler(0, hogYRotation, 0);
            
            Debug.Log($"Initial camera rotation set - XAxis: {mainCamera.m_XAxis.Value}, Target rotation: {cameraTarget.rotation.eulerAngles}");
        }
    }

    private void RealignCamera()
    {
        if (!IsOwner || mainCamera == null)
        {
            Debug.Log($"RealignCamera skipped - IsOwner: {IsOwner}, mainCamera: {(mainCamera != null)}");
            return;
        }

        // Get the root transform (ClientAuthoritativeHog)
        Transform rootTransform = transform.parent;
        if (rootTransform == null)
        {
            Debug.LogError("Root transform not found for camera alignment");
            return;
        }

        // Get the Hog's Y rotation directly from the root transform
        float hogYRotation = rootTransform.eulerAngles.y;
        
        Debug.Log($"Camera alignment - Hog Position: {rootTransform.position}, Hog Y Rotation: {hogYRotation}");

        // Set the camera's rotation to match the Hog's Y rotation exactly
        mainCamera.m_XAxis.Value = hogYRotation;
        mainCamera.m_YAxis.Value = 0.5f;

        // Reset the input values
        mainCamera.m_XAxis.m_InputAxisValue = 0f;
        mainCamera.m_YAxis.m_InputAxisValue = 0f;

        // Update the camera target to match the Hog's rotation
        if (cameraTarget != null && cameraOffset != null)
        {
            cameraTarget.position = cameraOffset.position;
            cameraTarget.rotation = Quaternion.Euler(0, hogYRotation, 0);
            
            Debug.Log($"Camera alignment complete - XAxis: {mainCamera.m_XAxis.Value}, Target rotation: {cameraTarget.rotation.eulerAngles}");
        }
        else
        {
            Debug.LogError($"Camera alignment failed - Target: {(cameraTarget != null)}, Offset: {(cameraOffset != null)}");
        }
    }

    private void Awake()
    {
        menuManager = GameObject.Find("Menus").GetComponent<MenuManager>(); // ugh...
    }

    private void Start()
    {
        Cursor.visible = !Cursor.visible; // toggle visibility
        Cursor.lockState = CursorLockMode.Locked;
        ConnectionManager.Instance.isConnected = true;
        menuManager.jumpUI.SetActive(true);

        if (IsOwner)
        {
            // Only disable the virtual camera component, not the entire GameObject
            if (menuManager.startCamera != null)
            {
                var virtualCamera = menuManager.startCamera.GetComponent<CinemachineVirtualCamera>();
                if (virtualCamera != null)
                {
                    virtualCamera.enabled = false;
                }
            }
            menuManager.connectionPending.SetActive(false);
            
            // Set up FreeLook camera targets
            if (mainCamera != null)
            {
                mainCamera.Follow = cameraTarget;
                mainCamera.LookAt = cameraTarget;
                UnlockCameraInput(); // Make sure camera starts unlocked by default
            }
            
            // If this is the host/server, open the lobby settings menu
            if (IsServer && GameManager.Instance != null && GameManager.Instance.state == GameState.Pending)
            {
                Debug.Log("[Player] Host player spawned, opening lobby settings menu");
                StartCoroutine(OpenLobbySettingsAfterPlayerSpawn());
            }
        }

        Player localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        if (localPlayer != null && localPlayer.mainCamera != null)
        {
            localPlayerCameraTransform = localPlayer.mainCamera.transform;
        }
    }

    public void LockCameraInput()
    {
        if (mainCamera != null)
        {
            mainCamera.m_XAxis.m_MaxSpeed = 0f;
            mainCamera.m_YAxis.m_MaxSpeed = 0f;
        }
    }

    public void UnlockCameraInput()
    {
        if (mainCamera != null)
        {
            // When unlocking camera (which happens when closing lobby settings), align the camera
            if (IsOwner && rb != null)
            {
                // Get the Hog's current rotation around Y axis from the transform directly
                float desiredCameraAngle = rb.transform.rotation.eulerAngles.y;
                
                Debug.Log($"Camera unlock - Hog transform rotation: {rb.transform.rotation.eulerAngles}, Using Y: {desiredCameraAngle}");

                // Reset any existing input
                mainCamera.m_XAxis.m_InputAxisValue = 0f;
                mainCamera.m_YAxis.m_InputAxisValue = 0f;
                
                // Set the camera angle
                mainCamera.m_XAxis.Value = desiredCameraAngle;
                mainCamera.m_YAxis.Value = 0.5f;
                
                // Update the camera target's rotation
                if (cameraTarget != null)
                {
                    cameraTarget.rotation = rb.transform.rotation;
                    mainCamera.OnTargetObjectWarped(cameraTarget, Vector3.zero);
                }

                // Enable camera control
                mainCamera.m_XAxis.m_MaxSpeed = 300f;
                mainCamera.m_YAxis.m_MaxSpeed = 2f;

                Debug.Log($"Camera alignment complete - Camera XAxis: {mainCamera.m_XAxis.Value}, Camera target rotation: {cameraTarget.rotation.eulerAngles}");
            }
            else
            {
                // If we're not the owner or don't have required references, just unlock the camera
                mainCamera.m_XAxis.m_MaxSpeed = 300f;
                mainCamera.m_YAxis.m_MaxSpeed = 2f;
            }
        }
    }

    private IEnumerator ResetCameraTarget(Vector3 originalPosition)
    {
        // Wait two frames to ensure camera has updated
        yield return null;
        yield return null;
        
        // Reset the camera target back to following the offset
        cameraTarget.position = originalPosition;
        
        Debug.Log($"Reset camera target to original position: {originalPosition}");
    }

    private System.Collections.IEnumerator OpenLobbySettingsAfterPlayerSpawn()
    {
        Debug.Log("[Player] Starting OpenLobbySettingsAfterPlayerSpawn");
        
        // Immediate cursor state change to ensure visibility from start
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        // Lock camera input immediately
        LockCameraInput();
        
        // Wait a frame for everything to initialize
        yield return null;

        // Wait another frame to ensure player position and rotation are set
        yield return null;
        
        // Now realign the camera
        Debug.Log("[Player] About to realign camera in lobby settings");
        RealignCamera();
        
        // Second cursor unlock for redundancy
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

        // Only update camera target position if we're the owner and have valid references
        if (IsOwner && cameraTarget != null && cameraOffset != null)
        {
            // Store the current rotation
            Quaternion currentRotation = cameraTarget.rotation;
            
            // Update position
        cameraTarget.position = cameraOffset.position;
            
            // Restore rotation
            cameraTarget.rotation = currentRotation;
        }

        // Only try to get player data if we have a valid connection manager
        if (ConnectionManager.Instance != null && ConnectionManager.Instance.TryGetPlayerData(clientId, out PlayerData playerData))
        {
            // Set camera to spectator if dead
            if (IsOwner && mainCamera != null && playerData.state != PlayerState.Alive && GameManager.Instance != null && GameManager.Instance.state != GameState.Pending)
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
                    mainCamera.Follow = spectatePlayer.cameraTarget;
                    mainCamera.LookAt = spectatePlayer.cameraTarget;
                }

                // Handle changing spectate target
                if (Input.GetKeyDown(KeyCode.Mouse0))
                {
                    spectatingPlayerIndex = (spectatingPlayerIndex + 1) % aliveClients.Count;
                }
            }
                else if (mainCamera.Follow != cameraTarget)
            {
                // No alive players to spectate, fall back to own camera
                mainCamera.Follow = cameraTarget;
                mainCamera.LookAt = cameraTarget;
            }
        }
            else if (IsOwner && mainCamera != null && mainCamera.Follow != cameraTarget)
        {
            mainCamera.Follow = cameraTarget;
            mainCamera.LookAt = cameraTarget;
        }

            // Update player indicator
            if (playerIndicator != null)
            {
        playerIndicator.SetActive(playerData.isLobbyLeader);
            }
        }
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

            // Reset camera alignment
            RealignCamera();

            // Use the cached hogController reference
            if (hogController != null)
            {
                // Request respawn via HogController
                hogController.RequestRespawnServerRpc();
            }
            else
            {
                Debug.LogError("NetworkHogController reference is null - cannot request respawn");
                // Try to get it again from parent
                hogController = transform.parent.GetComponent<NetworkHogController>();
                if (hogController != null)
                {
                    hogController.RequestRespawnServerRpc();
                }
                else
                {
                    Debug.LogError("Still could not find NetworkHogController in parent");
                }
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