using Unity.Netcode;
using UnityEngine;

public enum PlayerState
{
    Alive,
    Dead,
    Spectating
}

public class Player : NetworkBehaviour
{
    public PlayerState state;
    public Vector3 spawnPoint;

    void Start()
    {
        if (!IsOwner) return;
        spawnPoint =  SpawnPointManager.instance.AssignSpawnPoint();
        Respawn();
    }

    public void Respawn()
    {
        Debug.Log("Respawning Player");
        transform.position = spawnPoint;
        CarController car = gameObject.GetComponentsInChildren<CarController>()[0];
        car.transform.position = spawnPoint;
        car.transform.LookAt(SpawnPointManager.instance.transform);
        car.gameObject.GetComponent<Rigidbody>().velocity = Vector3.zero;
    }
}