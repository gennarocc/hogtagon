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

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    [SerializeField] private Rigidbody rb;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;
    [SerializeField] public Transform cameraTarget;
    [SerializeField] public Vector3 offset = new Vector3(0, 1f, 0f);

    private Canvas worldspaceCanvas;
    private MenuManager menuManager;
    private int spectatingPlayerIndex = 0;


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
            
            cameraTarget.rotation = Quaternion.identity;

            // Set up FreeLook camera targets
            if (mainCamera != null)
            {
                mainCamera.Follow = cameraTarget;
                mainCamera.LookAt = transform;
            }
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
        cameraTarget.position = rb.gameObject.transform.position + offset;
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
        transform.position = playerData.spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);

        // gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        // gameObject.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;

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
        // var playerIndicator = transform.Find("PlayerIndicator").gameObject;
        // playerIndicator.SetActive(clientId != gameObject.GetComponent<NetworkObject>().OwnerClientId);
        // playerIndicator.GetComponent<Renderer>().material.color = playerData.color;
    }
}