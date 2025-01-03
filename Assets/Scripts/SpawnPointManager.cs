using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class SpawnPointManager : MonoBehaviour
{
    public List<Vector3> AvialableSpawnPoints = new List<Vector3>();
    public List<Vector3> AssignedSpawnPoints = new List<Vector3>();

    public static SpawnPointManager instance;

    void Awake()
    {
        if (instance == null) instance = this;

        foreach (Transform child in transform)
        {
            AvialableSpawnPoints.Add(child.position);
        }
        
        //NetworkManager.Singleton.ConnectionApprovalCallback += ConnectionApprovalWithRandomSpawnPos;
    }

    // Randomly selects an available spawn point. Removes from list and adds to assigned list.
    public Vector3 AssignSpawnPoint()
    {
        int index = Random.Range(0, AvialableSpawnPoints.Count);
        Vector3 assignment = AvialableSpawnPoints[index];
        AssignedSpawnPoints.Add(AvialableSpawnPoints[index]);
        AvialableSpawnPoints.RemoveAt(index);
        return assignment;
    }

    public void UnassignSpawnPoint(Vector3 sp)
    {
        AssignedSpawnPoints.Remove(sp);
        AvialableSpawnPoints.Add(sp);
    }


    void ConnectionApprovalWithRandomSpawnPos(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // Here we are only using ConnectionApproval to set the player's spawn position. Connections are always approved.
        response.CreatePlayerObject = true;
        response.Position = AssignSpawnPoint();
        response.Rotation = Quaternion.identity;
        response.Approved = true;
    }
}
