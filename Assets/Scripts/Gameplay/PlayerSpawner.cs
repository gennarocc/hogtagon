using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
    /// AI car spawner with manual control:
    /// - Press "1" to spawn a car at AutodriverASpawn location (car will not move)
    /// - Press "2" to start AI driving on all spawned cars
    /// Cars are automatically destroyed after 10 seconds to prevent accumulation
/// </summary>
public class PlayerSpawner : MonoBehaviour
{
    [Header("Spawning Settings")]
    [SerializeField] private GameObject playerPrefab; // Reference to ServerAuthoritativePlayer prefab
    [SerializeField] private float despawnTime = 10f; // Time before auto-destroying spawned cars
    [SerializeField] private int maxSpawnedCars = 5; // Maximum number of spawned cars at once
    
    [Header("AI Driving Settings")]
    [SerializeField] private float aiMotorTorque = 800f; // How fast the AI cars drive
    [SerializeField] private float aiDriveDelay = 1f; // Delay before AI starts driving
    [SerializeField] private float aiMaxDriveTime = 8f; // How long AI drives before coasting
    
    [Header("Spawn Position")]
    [SerializeField] private Transform autodriverASpawnPoint; // Fixed spawn point at AutodriverASpawn
    
    // Private variables
    private InputManager inputManager;
    private List<GameObject> spawnedCars = new List<GameObject>();
    
    private void Start()
    {
        // Get InputManager reference
        inputManager = InputManager.Instance;
        if (inputManager != null)
        {
            inputManager.SpawnAICarPressed += OnSpawnAICarPressed;
            inputManager.StartAIDrivingPressed += OnStartAIDrivingPressed;
        }
        else
        {
            Debug.LogError("PlayerSpawner: InputManager not found!");
        }
        
        // Auto-find AutodriverASpawn if not assigned
        if (autodriverASpawnPoint == null)
        {
            GameObject autodriverASpawn = GameObject.Find("AutodriverASpawn");
            if (autodriverASpawn != null)
            {
                autodriverASpawnPoint = autodriverASpawn.transform;
                Debug.Log($"PlayerSpawner: Auto-found AutodriverASpawn at position {autodriverASpawnPoint.position}");
            }
            else
            {
                Debug.LogError("PlayerSpawner: AutodriverASpawn object not found! Please assign it manually or ensure AutodriverASpawn exists in the scene.");
            }
        }
        
        // Auto-find player prefab if not assigned
        if (playerPrefab == null)
        {
            // Try to load the prefab from Resources or find it in the scene
            var existingPlayer = GameObject.Find("ServerAuthoritativePlayer");
            if (existingPlayer != null)
            {
                Debug.LogWarning("PlayerSpawner: No prefab assigned, but found existing player in scene. Please assign the ServerAuthoritativePlayer prefab.");
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (inputManager != null)
        {
            inputManager.SpawnAICarPressed -= OnSpawnAICarPressed;
            inputManager.StartAIDrivingPressed -= OnStartAIDrivingPressed;
        }
    }
    

    
    private void OnSpawnAICarPressed()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawner: No player prefab assigned!");
            return;
        }
        
        // Check if we've reached the maximum number of spawned cars
        CleanupDestroyedCars();
        if (spawnedCars.Count >= maxSpawnedCars)
        {
            Debug.Log($"PlayerSpawner: Maximum spawned cars ({maxSpawnedCars}) reached, ignoring spawn request");
            return;
        }
        
        SpawnAICar();
    }
    
    private void OnStartAIDrivingPressed()
    {
        CleanupDestroyedCars();
        
        if (spawnedCars.Count == 0)
        {
            Debug.Log("PlayerSpawner: No spawned cars found to start AI driving");
            return;
        }
        
        int carsStarted = 0;
        foreach (var car in spawnedCars)
        {
            if (car != null)
            {
                var autoDriver = car.GetComponent<AutoDriver>();
                if (autoDriver != null && !autoDriver.canMove)
                {
                    // Use the new public method to start driving immediately
                    autoDriver.StartDrivingNow();
                    carsStarted++;
                    Debug.Log($"PlayerSpawner: Started AI driving for car: {car.name}");
                }
            }
        }
        
        Debug.Log($"PlayerSpawner: Started AI driving for {carsStarted} car(s)");
    }
    
    private void SpawnAICar()
    {
        if (autodriverASpawnPoint == null)
        {
            Debug.LogError("PlayerSpawner: AutodriverASpawn point not assigned! Cannot spawn car.");
            return;
        }
        
        Vector3 spawnPosition = autodriverASpawnPoint.position;
        Quaternion spawnRotation = autodriverASpawnPoint.rotation;
        
        Debug.Log($"PlayerSpawner: Spawning AI car at AutodriverASpawn position {spawnPosition}");
        
        // Instantiate the car
        GameObject spawnedCar = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        
        // Configure the spawned car for AI driving
        ConfigureAICar(spawnedCar);
        
        // Add to our tracking list
        spawnedCars.Add(spawnedCar);
        
        // Start the despawn timer
        StartCoroutine(DespawnCarAfterDelay(spawnedCar, despawnTime));
        
        Debug.Log($"PlayerSpawner: AI car spawned successfully at AutodriverASpawn. Total spawned cars: {spawnedCars.Count}");
    }
    

    
    private void ConfigureAICar(GameObject car)
    {
        // Disable networking components since this is a local AI car
        var networkObject = car.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.enabled = false;
        }
        
        // Disable Player component to prevent it from interfering
        var player = car.GetComponent<Player>();
        if (player != null)
        {
            player.enabled = false;
        }
        
        // Keep HogController enabled for cinematic compatibility, AutoDriver will control it
        var hogController = car.GetComponent<HogController>();
        if (hogController != null)
        {
            hogController.canMove = true; // AutoDriver will manage this
        }
        
        // Add AutoDriver component for AI behavior
        var autoDriver = car.GetComponent<AutoDriver>();
        if (autoDriver == null)
        {
            autoDriver = car.AddComponent<AutoDriver>();
        }
        
        // Configure AutoDriver settings but don't start it yet
        autoDriver.delay = 999f; // Very long delay - will be overridden when manually started
        autoDriver.motorTorque = aiMotorTorque;
        autoDriver.maxDriveTime = aiMaxDriveTime;
        autoDriver.canMove = false; // Will be enabled manually when "2" is pressed
        autoDriver.forcePhysicsEvenInMenu = true;
        autoDriver.addExtraForce = true;
        // Keep AutoDriver enabled so Start() runs, but with a very long delay
        
        // Ensure the car has proper physics
        var rigidbody = car.GetComponent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.isKinematic = false;
            rigidbody.useGravity = true;
            rigidbody.constraints = RigidbodyConstraints.None;
        }
        
        // Make sure wheel colliders are enabled
        var wheelColliders = car.GetComponentsInChildren<WheelCollider>();
        foreach (var wheel in wheelColliders)
        {
            wheel.enabled = true;
        }
        
        Debug.Log("PlayerSpawner: AI car configured with AutoDriver component");
    }
    
    private IEnumerator DespawnCarAfterDelay(GameObject car, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (car != null)
        {
            Debug.Log("PlayerSpawner: Auto-despawning AI car after timeout");
            spawnedCars.Remove(car);
            Destroy(car);
        }
    }
    
    private void CleanupDestroyedCars()
    {
        // Remove any null references from our list (cars that were destroyed externally)
        spawnedCars.RemoveAll(car => car == null);
    }
    
    // Public methods for external control
    public void SetPlayerPrefab(GameObject prefab)
    {
        playerPrefab = prefab;
    }
    
    public void DespawnAllAICars()
    {
        foreach (var car in spawnedCars)
        {
            if (car != null)
            {
                Destroy(car);
            }
        }
        spawnedCars.Clear();
        Debug.Log("PlayerSpawner: All AI cars despawned");
    }
    
    public int GetSpawnedCarCount()
    {
        CleanupDestroyedCars();
        return spawnedCars.Count;
    }
    
    public void StartAIDrivingOnAllCars()
    {
        OnStartAIDrivingPressed();
    }
    
    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        if (autodriverASpawnPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(autodriverASpawnPoint.position, 2f);
            Gizmos.DrawRay(autodriverASpawnPoint.position, autodriverASpawnPoint.forward * 5f);
        }
    }
} 