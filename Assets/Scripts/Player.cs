using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public ulong clientId;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    [SerializeField] private GameObject carBody;
    [SerializeField] private MenuManager menuManager;

    private Canvas worldspaceCanvas;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;
    [SerializeField] public Transform cameraTarget;

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
        }
        else
        {
            mainCamera.Priority = 0;
        }
        clientId = gameObject.GetComponent<NetworkObject>().OwnerClientId;
    }

    private void Update()
    {
        floatingUsername.transform.position = transform.position + new Vector3(0, 3f, -1f);
        floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - mainCamera.transform.position);
        
        ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData);
        if (playerData.state != PlayerState.Alive)
        {
            // Player player = ConnectionManager.instance.GetPlayer(ConnectionManager.instance.GetAliveClients()[0]);
            // mainCamera.Follow = player.transform;
            // mainCamera.LookAt = player.cameraTarget;
        }
    }

    public void Respawn()
    {
        // Get updated playerData from connectionManager.
        ConnectionManager.instance.TryGetPlayerData(clientId, out PlayerData playerData);
        if (!IsServer) return;
        Debug.Log("Respawning Player");
        transform.position = playerData.spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        gameObject.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
        
        // Set player state to alive and update clients
        if (playerData.state != PlayerState.Alive)
        {
            playerData.state = PlayerState.Alive;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, playerData);
        }
    }

    public void SetPlayerData(PlayerData playerData)
    {
        // Floating Name Text
        worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        floatingUsername.text = playerData.username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);

        // Player Indicator
        var playerIndicator = transform.Find("PlayerIndicator").gameObject;
        playerIndicator.SetActive(clientId != gameObject.GetComponent<NetworkObject>().OwnerClientId);
        playerIndicator.GetComponent<Renderer>().material.color = playerData.color;
    }
}