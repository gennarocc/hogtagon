using UnityEngine;
using System.Collections;

/// <summary>
/// Spawns a player car prefab after a specified delay for trailer sequences
/// </summary>
public class TrailerCarSpawn : MonoBehaviour
{
    [Header("Spawn Settings")]
    [SerializeField] private GameObject playerCarPrefab; // Reference to the player car prefab
    [SerializeField] private float spawnDelay = 7.0f; // Delay before spawning in seconds
    [SerializeField] private Transform spawnPoint; // Optional spawn point (uses this transform if not set)
    [SerializeField] private Material carMaterial; // Material to apply to the spawned car
    
    [Header("Car Settings")]
    [SerializeField] private bool applyInitialRotation = true; // Whether to apply initial rotation
    [SerializeField] private Vector3 initialRotation = new Vector3(0, 180, 0); // Initial rotation to apply
    [SerializeField] private bool destroyAfterTime = false; // Whether to destroy the car after a time
    [SerializeField] private float destroyTime = 10.0f; // Time after which to destroy the car
    
    [Header("Physics Settings")]
    [SerializeField] private bool ensureRigidbody = true; // Whether to ensure the car has a Rigidbody
    [SerializeField] private bool applyInitialForce = false; // Whether to apply an initial force
    [SerializeField] private Vector3 initialForce = Vector3.zero; // Initial force to apply
    [SerializeField] private ForceMode forceMode = ForceMode.Impulse; // Force mode for initial force
    
    [Header("Tumble Settings")]
    [SerializeField] private bool applyTumble = true; // Whether to make the car tumble
    [SerializeField] private Vector3 tumbleForce = new Vector3(10f, 5f, 8f); // Rotation force to apply
    [SerializeField] private float randomTumbleVariation = 0.3f; // Random variation to add to tumble (0-1)
    [SerializeField] private ForceMode tumbleForceMode = ForceMode.Impulse; // Force mode for tumble
    
    private GameObject spawnedCar;
    
    void Start()
    {
        // Start the spawn coroutine
        StartCoroutine(SpawnCarAfterDelay());
    }
    
    private IEnumerator SpawnCarAfterDelay()
    {
        // Wait for the specified delay
        yield return new WaitForSeconds(spawnDelay);
        
        // Spawn the car
        SpawnCar();
        
        // Destroy after time if specified
        if (destroyAfterTime && spawnedCar != null)
        {
            yield return new WaitForSeconds(destroyTime);
            Destroy(spawnedCar);
        }
    }
    
    private void SpawnCar()
    {
        if (playerCarPrefab == null)
        {
            Debug.LogError("Player car prefab not assigned to TrailerCarSpawn!");
            return;
        }
        
        // Determine spawn position and rotation
        Vector3 position = (spawnPoint != null) ? spawnPoint.position : transform.position;
        Quaternion rotation = applyInitialRotation ? Quaternion.Euler(initialRotation) : transform.rotation;
        
        // Instantiate the car
        spawnedCar = Instantiate(playerCarPrefab, position, rotation);
        
        // Apply material if one is assigned
        if (carMaterial != null)
        {
            Renderer[] renderers = spawnedCar.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                renderer.material = carMaterial;
            }
            Debug.Log("Applied custom material to spawned car");
        }
        
        // Ensure physics are set up correctly
        SetupPhysics();
        
        // Log for debugging
        Debug.Log($"TrailerCarSpawn: Spawned player car after {spawnDelay} seconds");
    }
    
    private void SetupPhysics()
    {
        if (spawnedCar == null) return;
        
        // Get the Rigidbody component
        Rigidbody rb = spawnedCar.GetComponent<Rigidbody>();
        
        // If the car doesn't have a Rigidbody and we want to ensure it has one
        if (rb == null && ensureRigidbody)
        {
            rb = spawnedCar.AddComponent<Rigidbody>();
            Debug.Log("Added Rigidbody to spawned car");
        }
        
        if (rb != null)
        {
            // Ensure gravity is enabled
            rb.useGravity = true;
            
            // Ensure it's not kinematic (kinematic bodies don't respond to forces like gravity)
            rb.isKinematic = false;
            
            // Apply initial force if specified
            if (applyInitialForce && initialForce != Vector3.zero)
            {
                rb.AddForce(initialForce, forceMode);
            }
            
            // Apply tumble (rotational force) if enabled
            if (applyTumble)
            {
                // Create a slightly randomized tumble for natural movement
                Vector3 randomizedTumble = new Vector3(
                    tumbleForce.x * (1f + Random.Range(-randomTumbleVariation, randomTumbleVariation)),
                    tumbleForce.y * (1f + Random.Range(-randomTumbleVariation, randomTumbleVariation)),
                    tumbleForce.z * (1f + Random.Range(-randomTumbleVariation, randomTumbleVariation))
                );
                
                // Apply the tumble force
                rb.AddTorque(randomizedTumble, tumbleForceMode);
                Debug.Log($"Applied tumble force: {randomizedTumble}");
            }
            
            Debug.Log("Physics setup complete for spawned car");
        }
    }
    
    // Public method to manually trigger car spawn
    public void SpawnCarNow()
    {
        StopAllCoroutines();
        SpawnCar();
    }
} 