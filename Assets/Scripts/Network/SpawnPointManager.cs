using UnityEngine;
using System.Collections.Generic;

public class SpawnPointManager : MonoBehaviour
{
    private List<Vector3> AvialableSpawnPoints = new List<Vector3>();
    private Dictionary<ulong, Vector3> AssignedSpawnPoints = new Dictionary<ulong, Vector3>();

    public static SpawnPointManager instance;

    void Awake()
    {
        if (instance == null) instance = this;

        foreach (Transform child in transform)
        {
            AvialableSpawnPoints.Add(child.position);
        }
    }

    // Randomly selects an available spawn point. Removes from list and adds to assigned list.
    public Vector3 AssignSpawnPoint(ulong clientId)
    {
        int index = Random.Range(0, AvialableSpawnPoints.Count);
        Vector3 assignment = AvialableSpawnPoints[index];
        AssignedSpawnPoints.Add(clientId, AvialableSpawnPoints[index]);
        AvialableSpawnPoints.RemoveAt(index);
        return assignment;
    }

    public void UnassignSpawnPoint(ulong clientId)
    {
        if (AssignedSpawnPoints.ContainsKey(clientId))
        {
            var sp = AssignedSpawnPoints[clientId];
            AssignedSpawnPoints.Remove(clientId);
            AvialableSpawnPoints.Add(sp);
        }
    }
}
