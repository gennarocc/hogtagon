using UnityEngine;

/// <summary>
/// Simple setup script to automatically configure PlayerSpawner with the correct prefab
/// Add this to any GameObject in your scene to enable AI car spawning with the "1" key
/// </summary>
public class PlayerSpawnerSetup : MonoBehaviour
{
    [Header("Auto Setup")]
    [SerializeField] private bool autoFindPrefab = true;
    [SerializeField] private string prefabPath = "Prefabs/ServerAuthoritativePlayer"; // Path in Resources folder
    [SerializeField] private GameObject manualPrefabReference; // Manual assignment if preferred
    
    private void Start()
    {
        SetupPlayerSpawner();
    }
    
    private void SetupPlayerSpawner()
    {
        // Check if PlayerSpawner already exists
        PlayerSpawner spawner = FindObjectOfType<PlayerSpawner>();
        
        if (spawner == null)
        {
            // Create a new GameObject with PlayerSpawner component
            GameObject spawnerObject = new GameObject("PlayerSpawner");
            spawner = spawnerObject.AddComponent<PlayerSpawner>();
            Debug.Log("PlayerSpawnerSetup: Created new PlayerSpawner GameObject");
        }
        
        // Try to assign the prefab
        GameObject prefab = GetPlayerPrefab();
        if (prefab != null)
        {
            spawner.SetPlayerPrefab(prefab);
            Debug.Log($"PlayerSpawnerSetup: Assigned prefab '{prefab.name}' to PlayerSpawner");
        }
        else
        {
            Debug.LogWarning("PlayerSpawnerSetup: Could not find ServerAuthoritativePlayer prefab. Please assign it manually in the PlayerSpawner component.");
        }
    }
    
    private GameObject GetPlayerPrefab()
    {
        // First try manual reference
        if (manualPrefabReference != null)
        {
            return manualPrefabReference;
        }
        
        // Then try auto-finding
        if (autoFindPrefab)
        {
            // Try to load from Resources
            GameObject prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab != null)
            {
                return prefab;
            }
            
            // Try to find in scene (this won't work for spawning, but helps with setup)
            GameObject sceneObject = GameObject.Find("ServerAuthoritativePlayer");
            if (sceneObject != null)
            {
                Debug.LogWarning("PlayerSpawnerSetup: Found ServerAuthoritativePlayer in scene, but need prefab reference for spawning. Please drag the prefab from Assets/Prefabs/ to the Manual Prefab Reference field.");
                return null;
            }
        }
        
        return null;
    }
    
    [ContextMenu("Setup Player Spawner")]
    public void ManualSetup()
    {
        SetupPlayerSpawner();
    }
} 