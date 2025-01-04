using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public enum PlayerState
{
    Alive,
    Dead,
    Spectating
}

public class Player : NetworkBehaviour
{
    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;
    public PlayerState state;
    public Vector3 spawnPoint;

    void Awake()
    {
        spawnPoint = SpawnPointManager.instance.AssignSpawnPoint();
        Respawn();
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
    }

    public void Respawn()
    {
        Debug.Log("Respawning Player");
        transform.position = spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().velocity = Vector3.zero;
    }
}