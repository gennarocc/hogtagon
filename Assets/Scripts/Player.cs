using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public ulong clientId;
    [SerializeField] public PlayerData playerData;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    private Canvas worldspaceCanvas;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;


    private void Start()
    {
        Cursor.visible = !Cursor.visible; // toggle visibility
        Cursor.lockState = CursorLockMode.Locked;

        ConnectionManager.instance.isConnected = true;

        if (IsOwner)
        {
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
    }

    public void Respawn()
    {
        if (!IsServer) return;
        Debug.Log("Respawning Player");
        transform.position = playerData.spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        gameObject.GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
        
        if (playerData.state != PlayerState.Alive)
        {
            playerData.state = PlayerState.Alive;
            ConnectionManager.instance.UpdatePlayerDataClientRpc(clientId, playerData);
        }
    }

    public void SetPlayerData(PlayerData playerData)
    {
        this.playerData = playerData;

        // Floating Name Text
        worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        floatingUsername.text = playerData.username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);

        // Player Indicator
        var playerIndicator = transform.Find("PlayerIndicator").gameObject;
        playerIndicator.SetActive(clientId != gameObject.GetComponent<NetworkObject>().OwnerClientId);
        playerIndicator.GetComponent<Renderer>().material.color = playerData.color;
    }

    public void Destory()
    {
        if (IsServer) SpawnPointManager.instance.UnassignSpawnPoint(clientId);
    }
}