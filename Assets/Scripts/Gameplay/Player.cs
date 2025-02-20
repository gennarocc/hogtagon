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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkPlayerData.OnValueChanged += OnPlayerDataChanged;
        clientId = OwnerClientId;
        if (networkPlayerData.Value.username != "")
        {
            ApplyPlayerData(networkPlayerData.Value);
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
        ConnectionManager.instance.isConnected = true;

        if (IsOwner)
        {
            menuManager.startCamera.gameObject.SetActive(false);
            menuManager.connectionPending.SetActive(false);
            audioListener.enabled = true;
            mainCamera.Priority = 1;
            // Set up FreeLook camera targets
            if (mainCamera != null)
            {
                mainCamera.Follow = cameraTarget;
                mainCamera.LookAt = cameraTarget;
            }
        }
        else
        {
            mainCamera.Priority = 0;
        }

        cameraTarget.rotation = Quaternion.identity;
    }

    private void Update()
    {
        floatingUsername.transform.position = transform.position + new Vector3(0, 3f, -1f);
        floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - mainCamera.transform.position);

        cameraTarget.position = cameraOffset.position;
        ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData);
        // Set camera to spectator if dead
        if (playerData.state != PlayerState.Alive)
        {
            List<ulong> aliveClients = ConnectionManager.instance.GetAliveClients();
            if (spectatingPlayerIndex >= aliveClients.Count) spectatingPlayerIndex = 0;
            Player spectatePlayer = ConnectionManager.instance.GetPlayer(ConnectionManager.instance.GetAliveClients()[spectatingPlayerIndex]);
            mainCamera.Follow = spectatePlayer.transform;
            mainCamera.LookAt = spectatePlayer.cameraTarget;
            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                spectatingPlayerIndex++;
            }
        }
        else
        {
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
        rb.transform.LookAt(SpawnPointManager.instance.transform);
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

        // Update floating username
        if (worldspaceCanvas == null)
        {
            worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        }
        floatingUsername.text = playerData.username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);
    }

    public void SetPlayerData(PlayerData playerData)
    {
        if (IsServer)
        {
            networkPlayerData.Value = playerData;
        }
    }
}