using UnityEngine;
using Unity.Netcode;

public class AutoDriver : MonoBehaviour
{
    public float delay = 3f; // 3 second delay
    public float motorTorque = 500f; // Max motor torque to apply
    public bool canMove = false; // Will become true after delay
    public bool forcePhysicsEvenInMenu = true; // Keep this true by default
    public bool addExtraForce = true; // Add extra force to ensure movement
    public float maxDriveTime = 6.0f; // Maximum time to drive before stopping acceleration

    private float startTime;
    private float driveStartTime; // When the car started driving
    private bool isDriving = false; // Whether the car is actively driving
    private WheelCollider[] wheelColliders;
    private Rigidbody rb;
    private HogController hogController;
    private NetworkObject networkObject;

    private void Awake()
    {
        // Get all components early
        rb = GetComponent<Rigidbody>();
        wheelColliders = GetComponentsInChildren<WheelCollider>();
        hogController = GetComponent<HogController>();
        networkObject = GetComponent<NetworkObject>();
        
        // Force rigidbody to not be kinematic immediately
        if (rb != null)
        {
            rb.isKinematic = false;
            
            // Setup for better collision reactions
            rb.constraints = RigidbodyConstraints.None;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 0.3f;
            rb.detectCollisions = true;
        }
    }

    private void Start()
    {
        startTime = Time.time;
        
        // Make sure the car is allowed to move by HogController
        if (hogController != null)
        {
            hogController.canMove = true;
        }
        
        Debug.Log($"AutoDriver: Found {wheelColliders.Length} wheel colliders");
        
        // Ensure rigidbody is properly set for collisions
        if (rb != null)
        {
            rb.isKinematic = false;
            // Ensure sleeping is disabled to allow for collision reactions
            rb.sleepThreshold = 0.0f;
        }
    }

    private void Update()
    {
        if (!canMove && Time.time - startTime >= delay)
        {
            canMove = true;
            isDriving = true;
            driveStartTime = Time.time;
            Debug.Log("AutoDriver: Started driving");
            
            // Ensure HogController allows movement when we start
            if (hogController != null)
            {
                hogController.canMove = true;
            }
        }
        
        // Check if we've been driving for 6 seconds and should stop accelerating
        if (isDriving && Time.time - driveStartTime >= maxDriveTime)
        {
            isDriving = false;
            Debug.Log("AutoDriver: Stopped accelerating after " + maxDriveTime + " seconds");
        }
    }

    // Public method to manually start the AutoDriver immediately
    public void StartDrivingNow()
    {
        canMove = true;
        isDriving = true;
        driveStartTime = Time.time;
        startTime = Time.time; // Reset the start time
        delay = 0f; // No delay
        Debug.Log("AutoDriver: Manually triggered to start driving immediately");
        
        // Ensure HogController allows movement
        if (hogController != null)
        {
            hogController.canMove = true;
        }
    }
    
    private void FixedUpdate()
    {
        // Force-enable physics always
        Physics.simulationMode = SimulationMode.FixedUpdate;
        
        // Ensure rigidbody is active and ready for collisions
        if (rb != null)
        {
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
            }
            
            // Make sure we're using continuous collision detection for better collision response
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        
        // Make sure wheel colliders are enabled
        if (wheelColliders != null)
        {
            foreach (WheelCollider wheel in wheelColliders)
            {
                if (wheel != null && !wheel.enabled)
                {
                    wheel.enabled = true;
                }
            }
        }
        
        // Only apply torque if we're still within the drive time limit
        if (canMove && isDriving && wheelColliders.Length > 0)
        {
            // Apply torque to wheels to move forward
            foreach (WheelCollider wheel in wheelColliders)
            {
                wheel.motorTorque = motorTorque;
                wheel.brakeTorque = 0f;
            }
            
            // Make sure hogController always allows movement
            if (hogController != null)
            {
                hogController.canMove = true;
            }
        }
        else if (canMove && !isDriving && wheelColliders.Length > 0)
        {
            // Stop applying torque after max drive time
            foreach (WheelCollider wheel in wheelColliders)
            {
                wheel.motorTorque = 0f;
                
                // Apply light braking to gradually slow down
                wheel.brakeTorque = motorTorque * 0.1f;
            }
        }

        // Add additional forward force if needed for extra push, but only during acceleration period
        if (canMove && isDriving && rb != null && addExtraForce)
        {
            // Check if we need an extra push
            float currentSpeed = rb.linearVelocity.magnitude;
            if (currentSpeed < 30f) // Only boost if below certain speed
            {
                rb.AddForce(transform.forward * motorTorque * 0.5f * Time.fixedDeltaTime, ForceMode.Acceleration);
                
                // If car is not moving at all, give it a strong initial push
                if (currentSpeed < 0.1f)
                {
                    rb.AddForce(transform.forward * motorTorque * 2f, ForceMode.Impulse);
                }
            }
        }
    }
} 