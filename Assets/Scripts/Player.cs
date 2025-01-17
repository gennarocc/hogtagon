using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;
using Unity.VisualScripting;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public ulong clientId;
    [SerializeField] public PlayerData playerData;
    [SerializeField] public Vector3 spawnPoint;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    private Canvas worldspaceCanvas;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;

    private ConnectionManager cm;

    private void Start()
    {
        cm = ConnectionManager.instance;
        if (IsOwner)
        {
            audioListener.enabled = true;
            mainCamera.Priority = 1;
        }
        else
        {
            mainCamera.Priority = 0;
        }
        clientId = NetworkManager.Singleton.LocalClientId;
    }

    private void Update()
    {
        floatingUsername.transform.position = transform.position + new Vector3(0, 3f, -1f);
        floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - mainCamera.transform.position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPlayerDataListServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        // gm.SendClientDataListClientRpc(clientId);
    }

    public void Respawn()
    {
        Debug.Log("Respawning Player");
        transform.position = spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }

    public void SetPlayerData(PlayerData playerData)
    {
        this.playerData = playerData;
        spawnPoint = playerData.spawnPoint;
        worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        floatingUsername.text = playerData.username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);
        var playerIndicator = transform.Find("PlayerIndicator").gameObject;
        playerIndicator.SetActive(clientId != NetworkManager.Singleton.LocalClientId);
        playerIndicator.GetComponent<Renderer>().material.color = playerData.color;
    }
}