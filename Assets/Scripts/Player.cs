using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;

public enum PlayerState
{
    Alive,
    Dead,
    Spectating
}

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public string username;
    [SerializeField] public PlayerState state;
    [SerializeField] public Vector3 spawnPoint;
    
    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    private Canvas worldspaceCanvas;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;

    void Awake()
    {
        // Respawn();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            audioListener.enabled = true;
            mainCamera.Priority = 1;
        }
        else
        {
            mainCamera.Priority = 0;
        }

        username = GameManager.instance.GetClientUsername(OwnerClientId);
        worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        floatingUsername.text = username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);
    }

    private void Update()
    {
        floatingUsername.transform.position = transform.position + new Vector3 (0, 3f, -1f);
        floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - mainCamera.transform.position);
    }

    public void Respawn()
    {
        Debug.Log("Respawning Player");
        transform.position = spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }
}