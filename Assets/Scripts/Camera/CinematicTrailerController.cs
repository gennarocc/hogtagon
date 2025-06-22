using UnityEngine;
using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;

/// <summary>
/// Advanced cinematic camera controller for creating dramatic trailer sequences
/// Handles jump detection, camera switching, slow motion, zoom effects, and background blur
/// </summary>
public class CinematicTrailerController : MonoBehaviour
{
    [Header("Camera References")]
    [SerializeField] private List<CinemachineVirtualCamera> cinematicCameras = new List<CinemachineVirtualCamera>();
    [SerializeField] private CinemachineFreeLook normalPlayerCamera;
    [SerializeField] private Transform carTarget;
    
    [Header("Camera Assignments")]
    [SerializeField] private CinemachineVirtualCamera jumpZoomCamera;

    [Header("Trigger Settings")]
    [SerializeField] private bool autoTriggerOnJump = true;
    [SerializeField] private bool manualTrigger = false;
    [SerializeField] private KeyCode triggerKey = KeyCode.C;
    
    [Header("Jump Detection (Legacy - Use HogController Integration)")]
    [SerializeField] private float jumpVelocityThreshold = 5f;
    [SerializeField] private float groundCheckDistance = 2f;
    [SerializeField] private LayerMask groundLayerMask = 1;
    [SerializeField] private float airTimeThreshold = 0.3f;
    
    [Header("HogController Integration")]
    [SerializeField] private HogController hogController;
    [SerializeField] private bool useHogControllerEvents = true;
    
    [Header("Camera Effects")]
    [SerializeField] private float zoomInFOV = 25f;
    [SerializeField] private float normalFOV = 60f;
    [SerializeField] private float followDistance = 8f; // Used for gizmo display
    
    [Header("Slow Motion Settings")]
    [SerializeField] private bool enableSlowMotion = true; // Disable for Unity Recorder compatibility
    [SerializeField] private float slowMotionTimeScale = 0.3f;
    [SerializeField] private float slowMotionDuration = 2f;
    [SerializeField] private float slowMotionTransitionDuration = 0.5f; // How long transitions take (in/out of slow motion)
    [SerializeField] private bool usePhysicsTimestepAdjustment = true;
    [SerializeField] private bool enhanceWithMotionBlur = true;
    
    [Header("Jump Zoom Settings")]
    [SerializeField] private float sideViewDistance = 12f;
    [SerializeField] private bool useDynamicSidePosition = true; // Dynamically position based on car's movement
    [SerializeField] private float zoomTransitionSpeed = 2f; // How fast the zoom effect transitions (higher = faster)
    
    [Header("Post Processing")]
    [SerializeField] private Volume postProcessVolume;
    [SerializeField] private bool enableDepthOfField = true;

    [Header("Auto-Detection Settings")]
    [SerializeField] private bool enableContinuousCarDetection = true;
    [SerializeField] private float carDetectionInterval = 2f; // Check every 2 seconds
    [SerializeField] private bool onlyAcceptCloneObjects = true; // Only accept ServerAuthoritativePlayer(Clone)
    [SerializeField] private bool waitForServerStart = true; // Don't auto-detect until server starts
    
    [Header("Input Sensitivity Control")]
    [SerializeField] private bool reduceSensitivityDuringCinematic = true;
    [SerializeField] private float cinematicSensitivityMultiplier = 0.1f; // Reduce to 10% of normal sensitivity
    [SerializeField] private float defaultCameraSensitivityX = 2f; // Default X sensitivity for cameras
    [SerializeField] private float defaultCameraSensitivityY = 0.5f; // Default Y sensitivity for cameras
    
    // Private variables
    private Rigidbody carRigidbody;
    private bool isAirborne = false;
    private bool isInCinematicMode = false;
    private float airTime = 0f;
    private float sequenceStartTime = 0f;
    private float originalTimeScale = 1f;
    private bool wasJumping = false;
    private float lastJumpTime = 0f;
    private float jumpCooldownForCinematic = 2f; // Prevent too frequent cinematics
    
    // Post processing components
    private DepthOfField depthOfField;
    private MotionBlur motionBlur;
    
    // Camera priorities
    private const int HIGH_PRIORITY = 200; // Increased to ensure it overrides CameraManager (priority 15)
    private const int LOW_PRIORITY = 0;
    private const int CINEMA_PRIORITY = 150; // For cinematic cameras
    
    // Camera manager integration
    private CameraManager cameraManager;
    
    // Camera sequence states
    public enum CinematicState
    {
        Normal,
        JumpZoom
    }
    
    private CinematicState currentState = CinematicState.Normal;

    // Auto-detection timing
    private float lastCarDetectionTime = 0f;

    // Smooth slow motion variables
    private float originalFixedDeltaTime;
    private float targetTimeScale = 1f;
    private bool isTransitioningSlowMotion = false;
    private Coroutine activeSlowMotionCoroutine = null; // Track the active slow motion coroutine

    // Sensitivity control variables
    private Dictionary<CinemachineFreeLook, CameraSensitivityData> originalCameraSensitivities = new Dictionary<CinemachineFreeLook, CameraSensitivityData>();
    private bool sensitivityReduced = false;
    
    // Struct to store original camera sensitivity values
    [System.Serializable]
    public struct CameraSensitivityData
    {
        public float xAxisMaxSpeed;
        public float yAxisMaxSpeed;
        
        public CameraSensitivityData(float xMaxSpeed, float yMaxSpeed)
        {
            xAxisMaxSpeed = xMaxSpeed;
            yAxisMaxSpeed = yMaxSpeed;
        }
    }

    private void Start()
    {
        // Debug: Log initial camera sensitivity state
        if (normalPlayerCamera != null)
        {
            Debug.Log($"CinematicTrailerController Start: Initial PlayerCamera sensitivity - X: {normalPlayerCamera.m_XAxis.m_MaxSpeed}, Y: {normalPlayerCamera.m_YAxis.m_MaxSpeed}");
        }
        
        InitializeComponents();
        SetupCameras();
        SetupPostProcessing();
        
        // Safety check: Ensure sensitivity is not reduced on startup
        if (sensitivityReduced)
        {
            Debug.LogWarning("CinematicTrailerController: Sensitivity was incorrectly reduced on startup - restoring!");
            RestoreCameraSensitivity();
        }
        
        // Debug: Log final camera sensitivity state after setup
        if (normalPlayerCamera != null)
        {
            Debug.Log($"CinematicTrailerController Start Complete: Final PlayerCamera sensitivity - X: {normalPlayerCamera.m_XAxis.m_MaxSpeed}, Y: {normalPlayerCamera.m_YAxis.m_MaxSpeed}");
        }
        
        Debug.Log($"CinematicTrailerController Start: isInCinematicMode={isInCinematicMode}, sensitivityReduced={sensitivityReduced}, reduceSensitivityDuringCinematic={reduceSensitivityDuringCinematic}");
        
        // Only auto-start detection if waitForServerStart is disabled
        if (!waitForServerStart)
        {
            Debug.Log("CinematicTrailerController: Auto-detection enabled - starting immediate search");
            StartCoroutine(FindLocalPlayerCar());
        }
        else
        {
            Debug.Log("CinematicTrailerController: Waiting for server start before beginning car detection");
        }
    }

    private void InitializeComponents()
    {
        // Find car rigidbody if not assigned
        if (carTarget != null && carRigidbody == null)
        {
            carRigidbody = carTarget.GetComponent<Rigidbody>();
            if (carRigidbody == null)
            {
                carRigidbody = carTarget.GetComponentInParent<Rigidbody>();
            }
        }
        
        // Find HogController if not assigned
        if (hogController == null && carTarget != null)
        {
            hogController = carTarget.GetComponent<HogController>();
            if (hogController == null)
            {
                hogController = carTarget.GetComponentInParent<HogController>();
            }
        }
        
        // Find CameraManager if it exists
        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<CameraManager>();
            if (cameraManager != null)
            {
                Debug.Log("CinematicTrailerController: Found CameraManager, will integrate with it during cinematics");
            }
        }
        
        // Store original time scale and physics timestep
        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;
        
        if (carRigidbody == null)
        {
            Debug.LogError("CinematicTrailerController: No Rigidbody found for car target!");
        }
        
        if (useHogControllerEvents && hogController == null)
        {
            Debug.LogError("CinematicTrailerController: HogController not found! Either assign it manually or disable 'Use Hog Controller Events'");
        }
    }

    private void SetupCameras()
    {
        // Try to find the normal player camera automatically if not assigned
        if (normalPlayerCamera == null)
        {
            var playerCameraGO = GameObject.Find("PlayerCamera");
            if (playerCameraGO != null)
            {
                normalPlayerCamera = playerCameraGO.GetComponent<CinemachineFreeLook>();
                if (normalPlayerCamera != null)
                {
                    Debug.Log("CinematicTrailerController: Auto-found PlayerCamera (FreeLook)");
                }
            }
        }
        
        // Set all cinematic cameras to low priority initially
        foreach (var camera in cinematicCameras)
        {
            if (camera != null)
            {
                camera.Priority = LOW_PRIORITY;
                SetupCameraTarget(camera);
            }
        }
        
        // Ensure normal camera is active
        if (normalPlayerCamera != null)
        {
            normalPlayerCamera.Priority = HIGH_PRIORITY;
        }
        
        // Store original camera sensitivities for later use during cinematics
        StoreCameraSensitivities();
    }

    private void SetupCameraTarget(CinemachineVirtualCamera camera)
    {
        if (carTarget != null)
        {
            camera.Follow = carTarget;
            camera.LookAt = carTarget;
        }
    }

    private void SetupPostProcessing()
    {
        if (postProcessVolume != null && postProcessVolume.profile != null)
        {
            postProcessVolume.profile.TryGet<DepthOfField>(out depthOfField);
            postProcessVolume.profile.TryGet<MotionBlur>(out motionBlur);
        }
    }

    private void Update()
    {
        HandleInput();
        
        if (autoTriggerOnJump && !isInCinematicMode)
        {
            if (useHogControllerEvents)
            {
                DetectJumpFromHogController();
            }
            else
            {
                DetectJump(); // Legacy physics-based detection
            }
        }
        


        // Continuous car detection - only if no car is assigned and not already in cinematic mode
        if (enableContinuousCarDetection && carTarget == null && !isInCinematicMode && 
            Time.time - lastCarDetectionTime >= carDetectionInterval)
        {
            // Only run continuous detection if we're not waiting for server start, or if server has started
            bool shouldDetect = !waitForServerStart || 
                               (Unity.Netcode.NetworkManager.Singleton != null && 
                                Unity.Netcode.NetworkManager.Singleton.IsServer);
                                
            if (shouldDetect)
            {
                lastCarDetectionTime = Time.time;
                if (TryFindLocalPlayerCar())
                {
                    Debug.Log("CinematicTrailerController: Continuous detection found car!");
                }
            }
        }
    }

    private void HandleInput()
    {
        if (manualTrigger && Input.GetKeyDown(triggerKey))
        {
            if (!isInCinematicMode)
            {
                StartCinematicSequence();
            }
            else
            {
                EndCinematicSequence();
            }
        }
    }

    private void DetectJumpFromHogController()
    {
        if (hogController == null) 
        {
            Debug.LogWarning("CinematicTrailerController: hogController is null, cannot detect jumps");
            return;
        }
        
        // Check if HogController is currently jumping using reflection to access the NetworkVariable
        bool isCurrentlyJumping = false;
        try
        {
            var jumpingField = hogController.GetType().GetField("isJumping", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (jumpingField != null)
            {
                var jumpingVar = jumpingField.GetValue(hogController) as Unity.Netcode.NetworkVariable<bool>;
                isCurrentlyJumping = jumpingVar != null && jumpingVar.Value;
                
                // Debug logging for jump state changes
                if (isCurrentlyJumping != wasJumping)
                {
                    Debug.Log($"CinematicTrailerController: Jump state changed - was: {wasJumping}, now: {isCurrentlyJumping}");
                }
            }
            else
            {
                Debug.LogWarning("CinematicTrailerController: Could not find 'isJumping' field in HogController");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"CinematicTrailerController: Could not access HogController jump state: {ex.Message}");
            return;
        }
        
        // Detect transition from not jumping to jumping
        if (!wasJumping && isCurrentlyJumping)
        {
            Debug.Log($"CinematicTrailerController: Jump detected! Checking cooldown... Time since last: {Time.time - lastJumpTime}, Required cooldown: {jumpCooldownForCinematic}");
            
            // Check cooldown to prevent too frequent cinematics
            if (Time.time - lastJumpTime >= jumpCooldownForCinematic)
            {
                Debug.Log("CinematicTrailerController: Jump detected from HogController! Starting cinematic sequence...");
                lastJumpTime = Time.time;
                StartCinematicSequence();
            }
            else
            {
                Debug.Log($"CinematicTrailerController: Jump detected but cooldown active. Skipping cinematic.");
            }
        }
        
        wasJumping = isCurrentlyJumping;
    }

    private void DetectJump()
    {
        if (carRigidbody == null) return;

        Vector3 velocity = carRigidbody.linearVelocity;
        bool wasAirborne = isAirborne;
        
        // Check if car is grounded
        bool grounded = Physics.Raycast(carTarget.position, Vector3.down, groundCheckDistance, groundLayerMask);
        isAirborne = !grounded;
        
        // Track air time
        if (isAirborne)
        {
            airTime += Time.deltaTime;
        }
        else
        {
            airTime = 0f;
        }
        
        // Trigger cinematic if we detect a significant jump
        if (!wasAirborne && isAirborne && velocity.y > jumpVelocityThreshold)
        {
            StartCinematicSequence();
        }
        
        // Also trigger if we've been airborne for a while (long jump)
        if (isAirborne && airTime > airTimeThreshold && velocity.y > jumpVelocityThreshold * 0.5f)
        {
            if (!isInCinematicMode)
            {
                StartCinematicSequence();
            }
        }
    }

    public void StartCinematicSequence()
    {
        if (isInCinematicMode) return;
        
        Debug.Log("Starting cinematic jump zoom sequence!");
        
        isInCinematicMode = true;
        sequenceStartTime = Time.unscaledTime;
        currentState = CinematicState.JumpZoom;
        
        // Reduce mouse sensitivity for smoother cinematic experience
        ReduceCameraSensitivity();
        
        // Disable CameraManager's cameras if it exists
        if (cameraManager != null)
        {
            // Find all cameras managed by CameraManager and set them to very low priority
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    cam.Priority = LOW_PRIORITY;
                    Debug.Log($"Set CameraManager camera {cam.name} to LOW_PRIORITY");
                }
            }
        }
        
        // Disable normal player camera
        if (normalPlayerCamera != null)
        {
            normalPlayerCamera.Priority = LOW_PRIORITY;
            Debug.Log($"Set normal player camera {normalPlayerCamera.name} to LOW_PRIORITY");
        }
        
        // Start the simple jump zoom
        StartCoroutine(ExecuteSimpleJumpZoom());
    }

    private IEnumerator ExecuteSimpleJumpZoom()
    {
        if (jumpZoomCamera == null)
        {
            Debug.LogError("Jump Zoom Camera not assigned!");
            EndCinematicSequence();
            yield break;
        }
        
        if (carTarget == null)
        {
            Debug.LogError("Car target not assigned for jump zoom!");
            EndCinematicSequence();
            yield break;
        }
        
        Debug.Log("Activating jump zoom camera with side view");
        
        // Set up side view positioning
        Vector3 initialCarPosition = carTarget.position;
        Vector3 carVelocity = carRigidbody != null ? carRigidbody.linearVelocity : Vector3.zero;
        
        // Determine which side to view from based on car's movement direction
        Vector3 sideDirection = Vector3.right; // Default to right side
        if (useDynamicSidePosition && carVelocity.magnitude > 1f)
        {
            // Get perpendicular direction to car's movement for side view
            Vector3 forwardDirection = carVelocity.normalized;
            sideDirection = Vector3.Cross(forwardDirection, Vector3.up).normalized;
            
            // Choose the side that's more to the right of the car's forward direction
            if (Vector3.Dot(sideDirection, carTarget.right) < 0)
            {
                sideDirection = -sideDirection;
            }
        }
        else
        {
            // Use car's right direction if no velocity-based calculation
            sideDirection = carTarget.right;
        }
        
        // Set jump zoom camera to high priority
        jumpZoomCamera.Priority = HIGH_PRIORITY;
        
        // Disable follow and lookAt to manually control positioning
        jumpZoomCamera.Follow = null;
        jumpZoomCamera.LookAt = carTarget; // Keep looking at the car
        
        // Enable depth of field for background blur
        if (enableDepthOfField && depthOfField != null)
        {
            depthOfField.active = true;
            depthOfField.focusDistance.value = sideViewDistance;
            depthOfField.aperture.value = 2f; // Wide aperture for shallow depth
        }
        
        // Start slow motion effect only if enabled (disable for Unity Recorder)
        if (enableSlowMotion)
        {
            Debug.Log($"Starting slow motion with timeScale: {slowMotionTimeScale}");
            activeSlowMotionCoroutine = StartCoroutine(StartSmoothSlowMotion(slowMotionTimeScale, slowMotionDuration));
        }
        else
        {
            Debug.Log("Slow motion disabled - Unity Recorder compatibility mode active");
        }
        
        // Simple zoom in effect only - no camera movement, no zoom out
        var lens = jumpZoomCamera.m_Lens;
        float startFOV = normalFOV;
        float elapsed = 0f;
        float quickZoomDuration = enableSlowMotion ? slowMotionDuration : 0.5f; // Match slow motion duration to prevent Unity Recorder conflicts
        
        // Quick zoom in only
        while (elapsed < quickZoomDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / quickZoomDuration;
            
            // Simple zoom in with configurable speed curve
            float speedAdjustedT = Mathf.Clamp01(t * zoomTransitionSpeed); // Apply zoom speed multiplier
            float zoomProgress = Mathf.SmoothStep(0f, 1f, speedAdjustedT);
            lens.FieldOfView = Mathf.Lerp(startFOV, zoomInFOV, zoomProgress);
            
            jumpZoomCamera.m_Lens = lens;
            yield return null;
        }
        
        Debug.Log("Jump zoom completed, ending cinematic sequence");
        
        // Restore original camera follow/lookAt behavior
        if (jumpZoomCamera != null)
        {
            jumpZoomCamera.Follow = carTarget;
            jumpZoomCamera.LookAt = carTarget;
        }
        
        // End the sequence
        EndCinematicSequence();
    }





    public void EndCinematicSequence()
    {
        if (!isInCinematicMode) return;
        
        Debug.Log("Ending cinematic sequence");
        
        isInCinematicMode = false;
        currentState = CinematicState.Normal;
        
        // Restore original mouse sensitivity
        RestoreCameraSensitivity();
        
        // Restore CameraManager's cameras if it exists
        if (cameraManager != null)
        {
            // Find all cameras managed by CameraManager and restore their normal priority
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    cam.Priority = 15; // CameraManager's active priority
                    Debug.Log($"Restored CameraManager camera {cam.name} to priority 15");
                }
            }
        }
        
        // Restore normal camera
        if (normalPlayerCamera != null)
        {
            normalPlayerCamera.Priority = HIGH_PRIORITY;
            Debug.Log($"Restored normal player camera {normalPlayerCamera.name} to HIGH_PRIORITY ({HIGH_PRIORITY})");
        }
        
        // Disable all cinematic cameras
        foreach (var camera in cinematicCameras)
        {
            if (camera != null)
            {
                camera.Priority = LOW_PRIORITY;
                Debug.Log($"Set cinematic camera {camera.name} to LOW_PRIORITY");
            }
        }
        
        // Ensure smooth restoration of time scale and physics - Unity Recorder compatibility
        if (activeSlowMotionCoroutine != null)
        {
            // Don't stop slow motion coroutine - let it complete naturally for Unity Recorder compatibility
            Debug.Log("Slow motion still active - letting it complete naturally to prevent Unity Recorder freezing");
        }
        else if (Time.timeScale != originalTimeScale)
        {
            // Only restore time scale if no slow motion is active
            Debug.Log("Restoring time scale immediately as no slow motion is active");
            Time.timeScale = originalTimeScale;
            Time.fixedDeltaTime = originalFixedDeltaTime;
        }
        
        // Reset post processing effects
        if (depthOfField != null)
        {
            depthOfField.active = false;
        }
        
        if (motionBlur != null)
        {
            motionBlur.active = false;
            motionBlur.intensity.value = 0.2f; // Reset to default
        }
        
        Debug.Log("Cinematic sequence ended successfully");
    }
    


    // Public methods for external control
    public void TriggerJumpZoom()
    {
        if (!isInCinematicMode)
        {
            StartCinematicSequence();
        }
    }

    public void SetCarTarget(Transform newTarget)
    {
        if (newTarget == null)
        {
            Debug.LogWarning("CinematicTrailerController: Trying to set null car target");
            return;
        }

        carTarget = newTarget;
        carRigidbody = newTarget?.GetComponent<Rigidbody>();
        
        // Update HogController reference
        hogController = newTarget.GetComponent<HogController>();
        if (hogController == null)
        {
            hogController = newTarget.GetComponentInParent<HogController>();
        }
        if (hogController == null)
        {
            hogController = newTarget.GetComponentInChildren<HogController>();
        }
        
        // Update all cinematic cameras to follow the new target
        foreach (var camera in cinematicCameras)
        {
            SetupCameraTarget(camera);
        }
        
        Debug.Log($"CinematicTrailerController: Car target set to {newTarget.name}, HogController: {(hogController != null ? hogController.name : "None")}");
    }

    /// <summary>
    /// Manually retry finding the local player's car. Call this if auto-detection failed.
    /// </summary>
    [ContextMenu("Retry Find Local Player Car")]
    public void RetryFindLocalPlayerCar()
    {
        Debug.Log("CinematicTrailerController: Manual retry of car detection...");
        
        if (TryFindLocalPlayerCar())
        {
            Debug.Log($"CinematicTrailerController: Manual retry successful! Found car: {carTarget.name}");
        }
        else
        {
            Debug.LogWarning("CinematicTrailerController: Manual retry failed. Car not found.");
            
            // Show what's available for debugging
            var allHogs = FindObjectsOfType<HogController>();
            var allServerAuth = FindObjectsOfType<GameObject>().Where(go => go.name.Contains("ServerAuthoritativePlayer")).ToArray();
            
            Debug.Log($"Available for manual assignment:");
            foreach (var hog in allHogs)
            {
                Debug.Log($"  - HogController: {hog.name} (IsOwner: {hog.IsOwner})");
            }
            foreach (var serverAuth in allServerAuth)
            {
                Debug.Log($"  - ServerAuthoritativePlayer: {serverAuth.name}");
            }
        }
    }

    /// <summary>
    /// Debug method to show all ServerAuthoritativePlayer objects and their structure
    /// </summary>
    [ContextMenu("Debug Show All ServerAuthoritativePlayer Objects")]
    public void DebugShowAllServerAuthObjects()
    {
        var allObjects = FindObjectsOfType<GameObject>();
        var serverAuthObjects = allObjects.Where(go => go.name.Contains("ServerAuthoritativePlayer")).ToArray();
        
        Debug.Log($"=== DEBUG: All ServerAuthoritativePlayer Objects ({serverAuthObjects.Length}) ===");
        
        foreach (var obj in serverAuthObjects)
        {
            Debug.Log($"Object: '{obj.name}'");
            Debug.Log($"  - Transform path: {GetTransformPath(obj.transform)}");
            Debug.Log($"  - HogController on this object: {obj.GetComponent<HogController>() != null}");
            Debug.Log($"  - HogController in children: {obj.GetComponentInChildren<HogController>() != null}");
            
            var allComponents = obj.GetComponents<Component>();
            Debug.Log($"  - All components: {string.Join(", ", System.Array.ConvertAll(allComponents, c => c.GetType().Name))}");
            
            var children = new List<Transform>();
            foreach (Transform child in obj.transform)
            {
                children.Add(child);
            }
            if (children.Count > 0)
            {
                Debug.Log($"  - Children: {string.Join(", ", children.ConvertAll(c => c.name))}");
                
                foreach (var child in children)
                {
                    var childHog = child.GetComponent<HogController>();
                    if (childHog != null)
                    {
                        Debug.Log($"    * Child '{child.name}' has HogController!");
                    }
                }
            }
            Debug.Log("  ---");
        }
        
        Debug.Log("=== END DEBUG ===");
    }
    
    private string GetTransformPath(Transform transform)
    {
        var path = transform.name;
        var parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }

    /// <summary>
    /// Force assign a specific GameObject as the car target (for manual assignment)
    /// </summary>
    public void ForceAssignCarTarget(GameObject carObject)
    {
        if (carObject == null)
        {
            Debug.LogError("CinematicTrailerController: Cannot assign null car object");
            return;
        }

        Debug.Log($"CinematicTrailerController: Force assigning car target: {carObject.name}");
        SetCarTargetAndUpdateReferences(carObject.transform);
    }

    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        if (carTarget != null)
        {
            // Draw ground check ray
            Gizmos.color = isAirborne ? Color.red : Color.green;
            Gizmos.DrawRay(carTarget.position, Vector3.down * groundCheckDistance);
            
            // Draw follow distance sphere
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(carTarget.position, followDistance);
        }
    }

    private IEnumerator FindLocalPlayerCar()
    {
        // Wait a bit for the network to initialize and server to start
        yield return new WaitForSeconds(1f);
        
        int attempts = 0;
        const int maxAttempts = 60; // Try for about 30 seconds (60 * 0.5s) since server start might take time
        
        Debug.Log("CinematicTrailerController: Starting automatic car detection for ServerAuthoritativePlayer(Clone)...");
        
        while (attempts < maxAttempts)
        {
            // Try to find the local player's car
            if (TryFindLocalPlayerCar())
            {
                Debug.Log($"CinematicTrailerController: Successfully found local player's car: {carTarget.name} (attempt {attempts + 1})");
                
                // Verify the car target is valid
                if (carTarget != null && hogController != null)
                {
                    Debug.Log($"CinematicTrailerController: Car detection complete! Car: {carTarget.name}, HogController: {hogController.name}");
                    break;
                }
            }
            
            attempts++;
            
            // Log progress every 10 attempts with specific diagnostic info
            if (attempts % 10 == 0)
            {
                Debug.Log($"CinematicTrailerController: Still searching for ServerAuthoritativePlayer(Clone)... (attempt {attempts}/{maxAttempts})");
                
                // List all available GameObjects that might be cars for debugging
                var allHogs = FindObjectsOfType<HogController>();
                if (allHogs.Length > 0)
                {
                    Debug.Log($"Available HogControllers: {string.Join(", ", System.Array.ConvertAll(allHogs, h => h.name))}");
                }
                
                // Look specifically for ServerAuthoritativePlayer objects
                var allGameObjects = FindObjectsOfType<GameObject>();
                var serverAuthObjects = allGameObjects.Where(go => go.name.Contains("ServerAuthoritativePlayer")).ToArray();
                if (serverAuthObjects.Length > 0)
                {
                    Debug.Log($"ServerAuthoritativePlayer objects found: {string.Join(", ", System.Array.ConvertAll(serverAuthObjects, go => go.name))}");
                }
                else
                {
                    Debug.Log("No ServerAuthoritativePlayer objects found yet - waiting for Start Server...");
                }

                // Check network state
                if (Unity.Netcode.NetworkManager.Singleton != null)
                {
                    Debug.Log($"NetworkManager state: IsServer={Unity.Netcode.NetworkManager.Singleton.IsServer}, IsClient={Unity.Netcode.NetworkManager.Singleton.IsClient}");
                }
            }
            
            yield return new WaitForSeconds(0.5f); // Wait 0.5 seconds between attempts
        }
        
        if (attempts >= maxAttempts)
        {
            Debug.LogWarning("CinematicTrailerController: Could not find ServerAuthoritativePlayer(Clone) automatically after 30 seconds.");
            Debug.LogWarning("Make sure you have clicked 'Start Server' and the game mode state is set to pending.");
            Debug.LogWarning("If the ServerAuthoritativePlayer(Clone) exists, please manually assign it to the Car Target field.");
            
            // Final diagnostic info
            var allObjects = FindObjectsOfType<GameObject>();
            var serverObjects = allObjects.Where(go => go.name.Contains("Server")).ToArray();
            var cloneObjects = allObjects.Where(go => go.name.Contains("(Clone)")).ToArray();
            var carObjects = FindObjectsOfType<HogController>();
            
            Debug.Log($"Final diagnostic:");
            Debug.Log($"  - Server objects: {serverObjects.Length} ({string.Join(", ", System.Array.ConvertAll(serverObjects, go => go.name))})");
            Debug.Log($"  - Clone objects: {cloneObjects.Length} ({string.Join(", ", System.Array.ConvertAll(cloneObjects, go => go.name))})");
            Debug.Log($"  - HogControllers: {carObjects.Length} ({string.Join(", ", System.Array.ConvertAll(carObjects, h => h.name))})");
        }
    }
    
    private bool TryFindLocalPlayerCar()
    {
        try
        {
            // First, let's log ALL objects that contain "ServerAuthoritativePlayer" for debugging
            var allGameObjects = FindObjectsOfType<GameObject>();
            var allServerAuthObjects = allGameObjects.Where(go => go.name.Contains("ServerAuthoritativePlayer")).ToArray();
            
            Debug.Log($"=== CinematicTrailerController Debug: Found {allServerAuthObjects.Length} ServerAuthoritativePlayer objects ===");
            foreach (var obj in allServerAuthObjects)
            {
                var hogController = obj.GetComponent<HogController>();
                var hogControllerInChildren = obj.GetComponentInChildren<HogController>();
                bool isClone = obj.name.Contains("(Clone)");
                Debug.Log($"  - Object: '{obj.name}' | IsClone: {isClone} | HogController on object: {hogController != null} | HogController in children: {hogControllerInChildren != null}");
            }
            
            // If onlyAcceptCloneObjects is enabled, skip non-Clone assignments
            if (onlyAcceptCloneObjects)
            {
                Debug.Log("CinematicTrailerController: Only accepting Clone objects - skipping original ServerAuthoritativePlayer");
            }
            
            // Method 1: Look for ServerAuthoritativePlayer(Clone) specifically - this spawns after Start Server
            var serverAuthPlayerClone = GameObject.Find("ServerAuthoritativePlayer(Clone)");
            if (serverAuthPlayerClone != null)
            {
                Debug.Log($"Found exact match: ServerAuthoritativePlayer(Clone)");
                var hogController = serverAuthPlayerClone.GetComponent<HogController>();
                if (hogController == null)
                {
                    hogController = serverAuthPlayerClone.GetComponentInChildren<HogController>();
                    if (hogController != null)
                    {
                        Debug.Log($"Found HogController in children of ServerAuthoritativePlayer(Clone): {hogController.name}");
                    }
                }
                if (hogController != null)
                {
                    Debug.Log("CinematicTrailerController: Successfully assigned ServerAuthoritativePlayer(Clone) with HogController!");
                    SetCarTarget(hogController.transform);
                    return true;
                }
                else
                {
                    Debug.LogWarning("ServerAuthoritativePlayer(Clone) found but no HogController component!");
                }
            }

            // Method 2: Look for any object with "ServerAuthoritativePlayer" and "(Clone)" in the name (more flexible)
            foreach (var go in allGameObjects)
            {
                if (go.name.Contains("ServerAuthoritativePlayer") && go.name.Contains("(Clone)"))
                {
                    Debug.Log($"Found Clone object by search: {go.name}");
                    var hogController = go.GetComponent<HogController>();
                    if (hogController == null)
                    {
                        hogController = go.GetComponentInChildren<HogController>();
                        if (hogController != null)
                        {
                            Debug.Log($"Found HogController in children of {go.name}: {hogController.name}");
                        }
                    }
                    if (hogController != null)
                    {
                        Debug.Log($"CinematicTrailerController: Successfully assigned {go.name} with HogController!");
                        SetCarTarget(hogController.transform);
                        return true;
                    }
                }
            }

            // Method 3: If Clone not found, check if we should prefer a Clone over the original
            if (allServerAuthObjects.Length > 1)
            {
                // If we have multiple ServerAuthoritativePlayer objects, prefer the Clone
                var cloneObject = allServerAuthObjects.FirstOrDefault(go => go.name.Contains("(Clone)"));
                if (cloneObject != null)
                {
                    Debug.Log($"Found Clone in multiple objects: {cloneObject.name}");
                    var hogController = cloneObject.GetComponent<HogController>();
                    if (hogController == null)
                    {
                        hogController = cloneObject.GetComponentInChildren<HogController>();
                    }
                    if (hogController != null)
                    {
                        Debug.Log($"CinematicTrailerController: Successfully assigned preferred Clone {cloneObject.name}!");
                        SetCarTarget(hogController.transform);
                        return true;
                    }
                }
            }

            // From here on, only proceed if onlyAcceptCloneObjects is disabled
            if (onlyAcceptCloneObjects)
            {
                Debug.Log("CinematicTrailerController: onlyAcceptCloneObjects is enabled - refusing to assign non-Clone objects");
                return false;
            }

            // Method 4: Look for the original ServerAuthoritativePlayer (fallback for non-clone scenarios)
            var serverAuthPlayer = GameObject.Find("ServerAuthoritativePlayer");
            if (serverAuthPlayer != null)
            {
                Debug.Log($"Fallback: Found original ServerAuthoritativePlayer");
                var hogController = serverAuthPlayer.GetComponent<HogController>();
                if (hogController == null)
                {
                    hogController = serverAuthPlayer.GetComponentInChildren<HogController>();
                    if (hogController != null)
                    {
                        Debug.Log($"Found HogController in children of ServerAuthoritativePlayer: {hogController.name}");
                    }
                }
                if (hogController != null)
                {
                    Debug.LogWarning("CinematicTrailerController: Using original ServerAuthoritativePlayer (no Clone found)");
                    SetCarTarget(hogController.transform);
                    return true;
                }
            }
            
            // Method 5: Try to find via NetworkManager LocalClient
            if (Unity.Netcode.NetworkManager.Singleton != null && 
                Unity.Netcode.NetworkManager.Singleton.LocalClient != null && 
                Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
                if (localPlayer != null)
                {
                    var localHogController = localPlayer.GetComponentInChildren<HogController>();
                    if (localHogController != null)
                    {
                        Debug.Log("CinematicTrailerController: Found local player via NetworkManager");
                        SetCarTarget(localHogController.transform);
                        return true;
                    }
                }
            }
            
            // Method 6: Try to find via Player components that are owned by local client
            var allPlayers = FindObjectsOfType<Player>();
            foreach (var player in allPlayers)
            {
                if (player.IsOwner) // This player belongs to the local client
                {
                    var hogController = player.GetComponentInChildren<HogController>();
                    if (hogController != null)
                    {
                        Debug.Log($"CinematicTrailerController: Found owned player {player.name} with HogController");
                        SetCarTarget(hogController.transform);
                        return true;
                    }
                }
            }
            
            // Method 7: Try to find via HogController that has IsOwner = true
            var allHogControllers = FindObjectsOfType<HogController>();
            foreach (var hog in allHogControllers)
            {
                if (hog.IsOwner) // This car belongs to the local client
                {
                    Debug.Log($"CinematicTrailerController: Found owned HogController {hog.name}");
                    SetCarTarget(hog.transform);
                    return true;
                }
            }
            
            // Method 8: Fallback - find player camera and try to get its target
            var playerCamera = GameObject.Find("PlayerCamera")?.GetComponent<CinemachineFreeLook>();
            if (playerCamera != null && playerCamera.Follow != null)
            {
                var hogController = playerCamera.Follow.GetComponent<HogController>();
                if (hogController == null)
                {
                    hogController = playerCamera.Follow.GetComponentInChildren<HogController>();
                }
                if (hogController != null)
                {
                    Debug.Log("CinematicTrailerController: Found HogController via PlayerCamera target");
                    SetCarTarget(hogController.transform);
                    return true;
                }
            }

            // Method 9: Last resort - find any HogController (for single player testing)
            if (allHogControllers.Length > 0)
            {
                Debug.LogWarning("CinematicTrailerController: Using first available HogController as fallback");
                SetCarTarget(allHogControllers[0].transform);
                return true;
            }
            
            Debug.LogWarning("CinematicTrailerController: No suitable car found in any detection method");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"CinematicTrailerController: Error while trying to find local player car: {ex.Message}");
        }
        
        return false;
    }
    
    // Method to manually set the car target and update all references
    public void SetCarTargetAndUpdateReferences(Transform newCarTarget)
    {
        SetCarTarget(newCarTarget);
        InitializeComponents(); // Re-initialize to find HogController
        SetupCameras(); // Re-setup cameras with new target - this also calls StoreCameraSensitivities()
    }

    // Property accessors
    public bool IsInCinematicMode => isInCinematicMode;
    public CinematicState CurrentState => currentState;
    public float SequenceProgress => isInCinematicMode ? (Time.unscaledTime - sequenceStartTime) / slowMotionDuration : 0f;

    /// <summary>
    /// Call this method when the server starts to trigger car detection at the optimal time
    /// </summary>
    public void OnServerStarted()
    {
        Debug.Log("CinematicTrailerController: Server started - beginning car detection for ServerAuthoritativePlayer(Clone)");
        
        // Stop any existing detection coroutine
        StopAllCoroutines();
        
        // Start fresh detection process
        StartCoroutine(FindLocalPlayerCar());
        
        // Also reset the continuous detection timer
        lastCarDetectionTime = Time.time;
    }

    /// <summary>
    /// Call this when game mode state changes to pending (when the car should spawn)
    /// </summary>
    public void OnGameModePending()
    {
        Debug.Log("CinematicTrailerController: Game mode pending - triggering immediate car detection");
        
        // Try immediate detection
        if (TryFindLocalPlayerCar())
        {
            Debug.Log("CinematicTrailerController: Successfully found car immediately after game mode pending!");
        }
        else
        {
            // If not found immediately, start the detection coroutine
            StartCoroutine(FindLocalPlayerCar());
        }
    }

    /// <summary>
    /// Clear the current car target assignment
    /// </summary>
    [ContextMenu("Clear Car Target")]
    public void ClearCarTarget()
    {
        Debug.Log("CinematicTrailerController: Clearing car target");
        carTarget = null;
        hogController = null;
        carRigidbody = null;
    }

    /// <summary>
    /// Validate the current car target assignment
    /// </summary>
    [ContextMenu("Validate Car Target")]
    public void ValidateCarTarget()
    {
        if (carTarget == null)
        {
            Debug.Log("CinematicTrailerController: No car target assigned");
            return;
        }

        bool isClone = carTarget.name.Contains("(Clone)");
        bool isServerAuth = carTarget.name.Contains("ServerAuthoritativePlayer");
        
        Debug.Log($"=== Car Target Validation ===");
        Debug.Log($"Assigned target: '{carTarget.name}'");
        Debug.Log($"Is ServerAuthoritativePlayer: {isServerAuth}");
        Debug.Log($"Is Clone: {isClone}");
        Debug.Log($"HogController assigned: {hogController != null}");
        Debug.Log($"Rigidbody assigned: {carRigidbody != null}");
        
        if (onlyAcceptCloneObjects && isServerAuth && !isClone)
        {
            Debug.LogError("VALIDATION FAILED: Non-Clone ServerAuthoritativePlayer assigned when onlyAcceptCloneObjects is enabled!");
            Debug.LogError("Consider calling 'Clear Car Target' and waiting for proper Clone object to spawn.");
        }
        else if (isClone && isServerAuth)
        {
            Debug.Log("VALIDATION PASSED: Correct Clone object assigned!");
        }
        else
        {
            Debug.LogWarning($"VALIDATION WARNING: Unusual target assigned - expected ServerAuthoritativePlayer(Clone)");
        }
    }

    /// <summary>
    /// Smoothly transition into slow motion
    /// </summary>
    private IEnumerator StartSmoothSlowMotion(float targetScale, float duration)
    {
        if (isTransitioningSlowMotion) yield break;
        
        isTransitioningSlowMotion = true;
        targetTimeScale = targetScale;
        
        float startTimeScale = Time.timeScale;
        float startFixedDelta = Time.fixedDeltaTime;
        float targetFixedDelta = originalFixedDeltaTime * targetScale;
        
        Debug.Log($"Starting smooth slow motion: {startTimeScale} -> {targetScale}");
        
        // Enable motion blur if requested
        if (enhanceWithMotionBlur && motionBlur != null)
        {
            motionBlur.active = true;
            motionBlur.intensity.value = Mathf.Lerp(0.2f, 0.8f, 1f - targetScale); // More blur for slower motion
        }
        
        // Smooth transition to slow motion
        float elapsed = 0f;
        
        while (elapsed < slowMotionTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / slowMotionTransitionDuration;
            
            // Use smooth step for better feel
            t = Mathf.SmoothStep(0f, 1f, t);
            
            Time.timeScale = Mathf.Lerp(startTimeScale, targetScale, t);
            
            if (usePhysicsTimestepAdjustment)
            {
                Time.fixedDeltaTime = Mathf.Lerp(startFixedDelta, targetFixedDelta, t);
            }
            
            yield return null;
        }
        
        // Ensure final values are set
        Time.timeScale = targetScale;
        if (usePhysicsTimestepAdjustment)
        {
            Time.fixedDeltaTime = targetFixedDelta;
        }
        
        Debug.Log($"Slow motion active: TimeScale={Time.timeScale}, FixedDeltaTime={Time.fixedDeltaTime}");
        
        // Wait for the slow motion duration (use real time)
        yield return new WaitForSecondsRealtime(duration);
        
        // Transition back to normal
        yield return StartCoroutine(EndSmoothSlowMotion());
    }
    
    /// <summary>
    /// Smoothly transition out of slow motion (Unity Recorder compatible)
    /// </summary>
    private IEnumerator EndSmoothSlowMotion()
    {
        float startTimeScale = Time.timeScale;
        float startFixedDelta = Time.fixedDeltaTime;
        
        Debug.Log($"Ending smooth slow motion (Unity Recorder compatible): {startTimeScale} -> {originalTimeScale}");
        
        // Use faster exit transition to prevent Unity Recorder issues
        float elapsed = 0f;
        float exitTransitionDuration = slowMotionTransitionDuration * 0.4f; // Faster exit than entry for recorder compatibility
        
        while (elapsed < exitTransitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / exitTransitionDuration;
            
            // Use linear interpolation instead of smooth step to avoid curve issues
            Time.timeScale = Mathf.Lerp(startTimeScale, originalTimeScale, t);
            
            if (usePhysicsTimestepAdjustment)
            {
                Time.fixedDeltaTime = Mathf.Lerp(startFixedDelta, originalFixedDeltaTime, t);
            }
            
            // Gradually reduce motion blur
            if (enhanceWithMotionBlur && motionBlur != null)
            {
                motionBlur.intensity.value = Mathf.Lerp(motionBlur.intensity.value, 0.2f, t);
            }
            
            yield return null;
        }
        
        // Ensure final values are restored quickly and definitively
        Time.timeScale = originalTimeScale;
        if (usePhysicsTimestepAdjustment)
        {
            Time.fixedDeltaTime = originalFixedDeltaTime;
        }
        
        // Disable motion blur
        if (enhanceWithMotionBlur && motionBlur != null)
        {
            motionBlur.active = false;
        }
        
        isTransitioningSlowMotion = false;
        activeSlowMotionCoroutine = null; // Clear the coroutine reference
        Debug.Log("Slow motion ended - Unity Recorder should continue smoothly");
    }

    /// <summary>
    /// Store original camera sensitivity values for later restoration (with protection against storing already-reduced values)
    /// </summary>
    private void StoreCameraSensitivities()
    {
        originalCameraSensitivities.Clear();
        
        Debug.Log("=== StoreCameraSensitivities: Checking and storing camera sensitivity values ===");
        
        // Store normal player camera sensitivity
        if (normalPlayerCamera != null)
        {
            var xAxis = normalPlayerCamera.m_XAxis;
            var yAxis = normalPlayerCamera.m_YAxis;
            
            float currentX = xAxis.m_MaxSpeed;
            float currentY = yAxis.m_MaxSpeed;
            
            Debug.Log($"PlayerCamera '{normalPlayerCamera.name}' current values: X={currentX}, Y={currentY}");
            
            // Check if values are suspiciously low (likely already reduced from previous session)
            if (currentX < 0.5f || currentY < 0.2f)
            {
                Debug.LogWarning($"PlayerCamera sensitivity appears already reduced! Current: X={currentX}, Y={currentY}");
                Debug.LogWarning($"Using default values instead: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
                
                // Immediately restore to defaults and store those as original
                normalPlayerCamera.m_XAxis.m_MaxSpeed = defaultCameraSensitivityX;
                normalPlayerCamera.m_YAxis.m_MaxSpeed = defaultCameraSensitivityY;
                
                originalCameraSensitivities[normalPlayerCamera] = new CameraSensitivityData(defaultCameraSensitivityX, defaultCameraSensitivityY);
                
                Debug.Log($"Restored PlayerCamera to defaults and stored as original: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
            }
            else
            {
                // Values look normal, store them as original
                originalCameraSensitivities[normalPlayerCamera] = new CameraSensitivityData(currentX, currentY);
                Debug.Log($"Stored PlayerCamera sensitivity as original: X={currentX}, Y={currentY}");
            }
        }
        
        // Store CameraManager cameras sensitivity if available
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null && !originalCameraSensitivities.ContainsKey(cam))
                {
                    var xAxis = cam.m_XAxis;
                    var yAxis = cam.m_YAxis;
                    
                    float currentX = xAxis.m_MaxSpeed;
                    float currentY = yAxis.m_MaxSpeed;
                    
                    Debug.Log($"CameraManager camera '{cam.name}' current values: X={currentX}, Y={currentY}");
                    
                    // Check if values are suspiciously low
                    if (currentX < 0.5f || currentY < 0.2f)
                    {
                        Debug.LogWarning($"CameraManager camera {cam.name} sensitivity appears already reduced! Current: X={currentX}, Y={currentY}");
                        Debug.LogWarning($"Using default values instead: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
                        
                        // Immediately restore to defaults and store those as original
                        cam.m_XAxis.m_MaxSpeed = defaultCameraSensitivityX;
                        cam.m_YAxis.m_MaxSpeed = defaultCameraSensitivityY;
                        
                        originalCameraSensitivities[cam] = new CameraSensitivityData(defaultCameraSensitivityX, defaultCameraSensitivityY);
                        
                        Debug.Log($"Restored CameraManager camera {cam.name} to defaults: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
                    }
                    else
                    {
                        // Values look normal, store them as original
                        originalCameraSensitivities[cam] = new CameraSensitivityData(currentX, currentY);
                        Debug.Log($"Stored CameraManager camera {cam.name} sensitivity as original: X={currentX}, Y={currentY}");
                    }
                }
            }
        }
        
        Debug.Log("=== StoreCameraSensitivities complete ===");
    }
    
    /// <summary>
    /// Reduce mouse sensitivity for all player cameras during cinematic sequences
    /// </summary>
    private void ReduceCameraSensitivity()
    {
        if (!reduceSensitivityDuringCinematic || sensitivityReduced) return;
        
        Debug.Log($"Reducing camera sensitivity to {cinematicSensitivityMultiplier * 100}% of normal");
        
        // Apply reduced sensitivity to normal player camera
        if (normalPlayerCamera != null && originalCameraSensitivities.ContainsKey(normalPlayerCamera))
        {
            var originalData = originalCameraSensitivities[normalPlayerCamera];
            var reducedXSpeed = originalData.xAxisMaxSpeed * cinematicSensitivityMultiplier;
            var reducedYSpeed = originalData.yAxisMaxSpeed * cinematicSensitivityMultiplier;
            
            normalPlayerCamera.m_XAxis.m_MaxSpeed = reducedXSpeed;
            normalPlayerCamera.m_YAxis.m_MaxSpeed = reducedYSpeed;
            
            Debug.Log($"Reduced {normalPlayerCamera.name} sensitivity: X={reducedXSpeed}, Y={reducedYSpeed}");
        }
        
        // Apply reduced sensitivity to CameraManager cameras
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null && originalCameraSensitivities.ContainsKey(cam))
                {
                    var originalData = originalCameraSensitivities[cam];
                    var reducedXSpeed = originalData.xAxisMaxSpeed * cinematicSensitivityMultiplier;
                    var reducedYSpeed = originalData.yAxisMaxSpeed * cinematicSensitivityMultiplier;
                    
                    cam.m_XAxis.m_MaxSpeed = reducedXSpeed;
                    cam.m_YAxis.m_MaxSpeed = reducedYSpeed;
                    
                    Debug.Log($"Reduced CameraManager camera {cam.name} sensitivity: X={reducedXSpeed}, Y={reducedYSpeed}");
                }
            }
        }
        
        sensitivityReduced = true;
    }
    
    /// <summary>
    /// Restore original mouse sensitivity after cinematic sequences
    /// </summary>
    private void RestoreCameraSensitivity()
    {
        if (!sensitivityReduced) return;
        
        Debug.Log("Restoring original camera sensitivity");
        
        // Restore normal player camera sensitivity
        if (normalPlayerCamera != null && originalCameraSensitivities.ContainsKey(normalPlayerCamera))
        {
            var originalData = originalCameraSensitivities[normalPlayerCamera];
            normalPlayerCamera.m_XAxis.m_MaxSpeed = originalData.xAxisMaxSpeed;
            normalPlayerCamera.m_YAxis.m_MaxSpeed = originalData.yAxisMaxSpeed;
            
            Debug.Log($"Restored {normalPlayerCamera.name} sensitivity: X={originalData.xAxisMaxSpeed}, Y={originalData.yAxisMaxSpeed}");
        }
        
        // Restore CameraManager cameras sensitivity
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null && originalCameraSensitivities.ContainsKey(cam))
                {
                    var originalData = originalCameraSensitivities[cam];
                    cam.m_XAxis.m_MaxSpeed = originalData.xAxisMaxSpeed;
                    cam.m_YAxis.m_MaxSpeed = originalData.yAxisMaxSpeed;
                    
                    Debug.Log($"Restored CameraManager camera {cam.name} sensitivity: X={originalData.xAxisMaxSpeed}, Y={originalData.yAxisMaxSpeed}");
                }
            }
        }
        
        sensitivityReduced = false;
    }

    /// <summary>
    /// Manually set the cinematic sensitivity multiplier
    /// </summary>
    public void SetCinematicSensitivityMultiplier(float multiplier)
    {
        cinematicSensitivityMultiplier = Mathf.Clamp01(multiplier);
        Debug.Log($"CinematicTrailerController: Sensitivity multiplier set to {cinematicSensitivityMultiplier * 100}%");
        
        // If currently in cinematic mode, update the sensitivity immediately
        if (isInCinematicMode && sensitivityReduced)
        {
            RestoreCameraSensitivity(); // First restore to original
            ReduceCameraSensitivity(); // Then apply new reduced values
        }
    }
    
    /// <summary>
    /// Toggle sensitivity reduction feature on/off
    /// </summary>
    public void SetSensitivityReductionEnabled(bool enabled)
    {
        reduceSensitivityDuringCinematic = enabled;
        Debug.Log($"CinematicTrailerController: Sensitivity reduction {(enabled ? "enabled" : "disabled")}");
        
        // If currently in cinematic mode and feature was disabled, restore sensitivity
        if (!enabled && isInCinematicMode && sensitivityReduced)
        {
            RestoreCameraSensitivity();
        }
        // If currently in cinematic mode and feature was enabled, reduce sensitivity
        else if (enabled && isInCinematicMode && !sensitivityReduced)
        {
            ReduceCameraSensitivity();
        }
    }
    
    /// <summary>
    /// Debug method to show current sensitivity settings
    /// </summary>
    [ContextMenu("Debug Show Sensitivity Settings")]
    public void DebugShowSensitivitySettings()
    {
        Debug.Log("=== Cinematic Sensitivity Settings ===");
        Debug.Log($"Sensitivity reduction enabled: {reduceSensitivityDuringCinematic}");
        Debug.Log($"Cinematic multiplier: {cinematicSensitivityMultiplier * 100}%");
        Debug.Log($"Currently in cinematic mode: {isInCinematicMode}");
        Debug.Log($"Sensitivity currently reduced: {sensitivityReduced}");
        Debug.Log($"Stored sensitivities count: {originalCameraSensitivities.Count}");
        
        foreach (var kvp in originalCameraSensitivities)
        {
            if (kvp.Key != null)
            {
                var cam = kvp.Key;
                var data = kvp.Value;
                var currentX = cam.m_XAxis.m_MaxSpeed;
                var currentY = cam.m_YAxis.m_MaxSpeed;
                
                Debug.Log($"Camera '{cam.name}':");
                Debug.Log($"  Original: X={data.xAxisMaxSpeed}, Y={data.yAxisMaxSpeed}");
                Debug.Log($"  Current:  X={currentX}, Y={currentY}");
            }
        }
        Debug.Log("=== End Sensitivity Settings ===");
    }

    /// <summary>
    /// Debug method to show current time scale and slow motion state
    /// </summary>
    [ContextMenu("Debug Time Scale State")]
    public void DebugTimeScaleState()
    {
        Debug.Log("=== Time Scale Debug Info ===");
        Debug.Log($"Current Time.timeScale: {Time.timeScale}");
        Debug.Log($"Original Time.timeScale: {originalTimeScale}");
        Debug.Log($"Current Time.fixedDeltaTime: {Time.fixedDeltaTime}");
        Debug.Log($"Original Time.fixedDeltaTime: {originalFixedDeltaTime}");
        Debug.Log($"Is transitioning slow motion: {isTransitioningSlowMotion}");
        Debug.Log($"Is in cinematic mode: {isInCinematicMode}");
        Debug.Log($"Active slow motion coroutine: {(activeSlowMotionCoroutine != null ? "Running" : "None")}");
        Debug.Log($"Target time scale: {targetTimeScale}");
        Debug.Log("=== End Time Scale Debug ===");
    }

    /// <summary>
    /// Emergency method to force normal time scale (useful if recording gets stuck)
    /// </summary>
    [ContextMenu("Emergency: Force Normal Time Scale")]
    public void EmergencyForceNormalTimeScale()
    {
        Debug.LogWarning("EMERGENCY: Forcing normal time scale for Unity Recorder compatibility");
        
        // Stop any slow motion coroutine
        if (activeSlowMotionCoroutine != null)
        {
            StopCoroutine(activeSlowMotionCoroutine);
            activeSlowMotionCoroutine = null;
        }
        
        // Immediately restore normal time
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f; // Standard fixed timestep
        isTransitioningSlowMotion = false;
        
        Debug.Log("Time scale emergency reset complete - recording should work normally now");
    }

    /// <summary>
    /// Emergency method to force restore camera sensitivity (useful if sensitivity gets stuck low)
    /// </summary>
    [ContextMenu("Emergency: Force Restore Camera Sensitivity")]
    public void EmergencyForceRestoreCameraSensitivity()
    {
        Debug.LogWarning("EMERGENCY: Force restoring camera sensitivity");
        
        // Force end cinematic mode
        isInCinematicMode = false;
        currentState = CinematicState.Normal;
        
        // Force restore sensitivity
        if (sensitivityReduced)
        {
            RestoreCameraSensitivity();
            Debug.Log("Sensitivity was reduced - restored to original values");
        }
        else
        {
            Debug.Log("Sensitivity was not marked as reduced, but checking cameras anyway...");
        }
        
        // Double-check: If we have stored values, restore them
        if (originalCameraSensitivities.Count > 0)
        {
            foreach (var kvp in originalCameraSensitivities)
            {
                if (kvp.Key != null)
                {
                    var cam = kvp.Key;
                    var originalData = kvp.Value;
                    
                    // Only restore if the current values are much lower than original (indicating reduced sensitivity)
                    if (cam.m_XAxis.m_MaxSpeed < originalData.xAxisMaxSpeed * 0.5f || 
                        cam.m_YAxis.m_MaxSpeed < originalData.yAxisMaxSpeed * 0.5f)
                    {
                        cam.m_XAxis.m_MaxSpeed = originalData.xAxisMaxSpeed;
                        cam.m_YAxis.m_MaxSpeed = originalData.yAxisMaxSpeed;
                        Debug.Log($"Force restored {cam.name} sensitivity: X={originalData.xAxisMaxSpeed}, Y={originalData.yAxisMaxSpeed}");
                    }
                    else
                    {
                        Debug.Log($"Camera {cam.name} sensitivity appears normal: X={cam.m_XAxis.m_MaxSpeed}, Y={cam.m_YAxis.m_MaxSpeed}");
                    }
                }
            }
        }
        
        // Reset the flag
        sensitivityReduced = false;
        
        Debug.Log("Camera sensitivity emergency restore complete");
    }

    /// <summary>
    /// Check if any camera has suspiciously low sensitivity values
    /// </summary>
    [ContextMenu("Debug: Check for Low Sensitivity")]
    public void DebugCheckForLowSensitivity()
    {
        Debug.Log("=== Checking for Low Sensitivity Issues ===");
        
        // Check normal player camera
        if (normalPlayerCamera != null)
        {
            float xSens = normalPlayerCamera.m_XAxis.m_MaxSpeed;
            float ySens = normalPlayerCamera.m_YAxis.m_MaxSpeed;
            
            Debug.Log($"PlayerCamera '{normalPlayerCamera.name}': X={xSens}, Y={ySens}");
            
            if (xSens < 1f || ySens < 1f)
            {
                Debug.LogWarning($"PlayerCamera has suspiciously low sensitivity! This might be the issue.");
                
                // Check if we have original values stored
                if (originalCameraSensitivities.ContainsKey(normalPlayerCamera))
                {
                    var orig = originalCameraSensitivities[normalPlayerCamera];
                    Debug.Log($"Original stored values: X={orig.xAxisMaxSpeed}, Y={orig.yAxisMaxSpeed}");
                }
            }
        }
        
        // Check CameraManager cameras
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    float xSens = cam.m_XAxis.m_MaxSpeed;
                    float ySens = cam.m_YAxis.m_MaxSpeed;
                    
                    Debug.Log($"CameraManager camera '{cam.name}': X={xSens}, Y={ySens}");
                    
                    if (xSens < 1f || ySens < 1f)
                    {
                        Debug.LogWarning($"CameraManager camera {cam.name} has suspiciously low sensitivity!");
                        
                        if (originalCameraSensitivities.ContainsKey(cam))
                        {
                            var orig = originalCameraSensitivities[cam];
                            Debug.Log($"Original stored values: X={orig.xAxisMaxSpeed}, Y={orig.yAxisMaxSpeed}");
                        }
                    }
                }
            }
        }
        
        Debug.Log($"Current state: isInCinematicMode={isInCinematicMode}, sensitivityReduced={sensitivityReduced}");
        Debug.Log("=== End Low Sensitivity Check ===");
    }

    /// <summary>
    /// Reset all cameras to default sensitivity values (fixes persistent low sensitivity issue)
    /// </summary>
    [ContextMenu("Fix: Reset All Cameras to Default Sensitivity")]
    public void ResetAllCamerasToDefaultSensitivity()
    {
        Debug.LogWarning("=== RESETTING ALL CAMERAS TO DEFAULT SENSITIVITY ===");
        
        // Force end cinematic mode
        isInCinematicMode = false;
        currentState = CinematicState.Normal;
        sensitivityReduced = false;
        
        // Reset normal player camera
        if (normalPlayerCamera != null)
        {
            float oldX = normalPlayerCamera.m_XAxis.m_MaxSpeed;
            float oldY = normalPlayerCamera.m_YAxis.m_MaxSpeed;
            
            normalPlayerCamera.m_XAxis.m_MaxSpeed = defaultCameraSensitivityX;
            normalPlayerCamera.m_YAxis.m_MaxSpeed = defaultCameraSensitivityY;
            
            Debug.Log($"Reset PlayerCamera '{normalPlayerCamera.name}': X={oldX}->{defaultCameraSensitivityX}, Y={oldY}->{defaultCameraSensitivityY}");
        }
        
        // Reset CameraManager cameras
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    float oldX = cam.m_XAxis.m_MaxSpeed;
                    float oldY = cam.m_YAxis.m_MaxSpeed;
                    
                    cam.m_XAxis.m_MaxSpeed = defaultCameraSensitivityX;
                    cam.m_YAxis.m_MaxSpeed = defaultCameraSensitivityY;
                    
                    Debug.Log($"Reset CameraManager camera '{cam.name}': X={oldX}->{defaultCameraSensitivityX}, Y={oldY}->{defaultCameraSensitivityY}");
                }
            }
        }
        
        // Clear and rebuild the stored sensitivities with the new default values
        originalCameraSensitivities.Clear();
        StoreCameraSensitivities();
        
        Debug.LogWarning("=== ALL CAMERAS RESET TO DEFAULT SENSITIVITY - ISSUE SHOULD BE FIXED ===");
        Debug.Log($"Default values used: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
        Debug.Log("You can adjust these defaults in the Inspector if needed.");
    }

    /// <summary>
    /// Show current sensitivity settings for all cameras
    /// </summary>
    [ContextMenu("Debug: Show All Camera Sensitivity Values")]
    public void DebugShowAllCameraSensitivityValues()
    {
        Debug.Log("=== ALL CAMERA SENSITIVITY VALUES ===");
        Debug.Log($"Default sensitivity settings: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
        Debug.Log($"Cinematic multiplier: {cinematicSensitivityMultiplier} ({cinematicSensitivityMultiplier * 100}%)");
        Debug.Log($"Current state: isInCinematicMode={isInCinematicMode}, sensitivityReduced={sensitivityReduced}");
        Debug.Log($"Stored sensitivities count: {originalCameraSensitivities.Count}");
        
        // Show normal player camera
        if (normalPlayerCamera != null)
        {
            Debug.Log($"\nPlayerCamera '{normalPlayerCamera.name}':");
            Debug.Log($"  Current: X={normalPlayerCamera.m_XAxis.m_MaxSpeed}, Y={normalPlayerCamera.m_YAxis.m_MaxSpeed}");
            
            if (originalCameraSensitivities.ContainsKey(normalPlayerCamera))
            {
                var orig = originalCameraSensitivities[normalPlayerCamera];
                Debug.Log($"  Stored Original: X={orig.xAxisMaxSpeed}, Y={orig.yAxisMaxSpeed}");
            }
        }
        
        // Show CameraManager cameras
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    Debug.Log($"\nCameraManager camera '{cam.name}':");
                    Debug.Log($"  Current: X={cam.m_XAxis.m_MaxSpeed}, Y={cam.m_YAxis.m_MaxSpeed}");
                    
                    if (originalCameraSensitivities.ContainsKey(cam))
                    {
                        var orig = originalCameraSensitivities[cam];
                        Debug.Log($"  Stored Original: X={orig.xAxisMaxSpeed}, Y={orig.yAxisMaxSpeed}");
                    }
                }
            }
        }
        
        Debug.Log("=== END CAMERA SENSITIVITY VALUES ===");
    }

    /// <summary>
    /// NUCLEAR OPTION: Completely disable sensitivity control system and restore cameras to high sensitivity
    /// </summary>
    [ContextMenu("NUCLEAR: Disable Sensitivity Control System")]
    public void NuclearDisableSensitivityControlSystem()
    {
        Debug.LogWarning("=== NUCLEAR OPTION: DISABLING SENSITIVITY CONTROL SYSTEM ===");
        
        // Completely disable the sensitivity control feature
        reduceSensitivityDuringCinematic = false;
        
        // Force end all cinematic states
        isInCinematicMode = false;
        currentState = CinematicState.Normal;
        sensitivityReduced = false;
        
        // Set all cameras to high sensitivity values
        float highSensitivityX = 10f; // Much higher than default
        float highSensitivityY = 2f;  // Much higher than default
        
        // Reset normal player camera to high sensitivity
        if (normalPlayerCamera != null)
        {
            float oldX = normalPlayerCamera.m_XAxis.m_MaxSpeed;
            float oldY = normalPlayerCamera.m_YAxis.m_MaxSpeed;
            
            normalPlayerCamera.m_XAxis.m_MaxSpeed = highSensitivityX;
            normalPlayerCamera.m_YAxis.m_MaxSpeed = highSensitivityY;
            
            Debug.Log($"Set PlayerCamera '{normalPlayerCamera.name}' to HIGH sensitivity: X={oldX}->{highSensitivityX}, Y={oldY}->{highSensitivityY}");
        }
        
        // Reset CameraManager cameras to high sensitivity
        if (cameraManager != null)
        {
            var cameraManagerCameras = cameraManager.GetComponentsInChildren<CinemachineFreeLook>();
            foreach (var cam in cameraManagerCameras)
            {
                if (cam != null)
                {
                    float oldX = cam.m_XAxis.m_MaxSpeed;
                    float oldY = cam.m_YAxis.m_MaxSpeed;
                    
                    cam.m_XAxis.m_MaxSpeed = highSensitivityX;
                    cam.m_YAxis.m_MaxSpeed = highSensitivityY;
                    
                    Debug.Log($"Set CameraManager camera '{cam.name}' to HIGH sensitivity: X={oldX}->{highSensitivityX}, Y={oldY}->{highSensitivityY}");
                }
            }
        }
        
        // Clear all stored sensitivity data so the system can't interfere anymore
        originalCameraSensitivities.Clear();
        
        Debug.LogWarning("=== SENSITIVITY CONTROL SYSTEM COMPLETELY DISABLED ===");
        Debug.LogWarning("Cameras set to high sensitivity. The cinematic system will NO LONGER reduce sensitivity.");
        Debug.LogWarning("You can now test if this fixes your sensitivity issue permanently.");
        
        // Also update the default values to the high sensitivity for future reference
        defaultCameraSensitivityX = highSensitivityX;
        defaultCameraSensitivityY = highSensitivityY;
        
        Debug.Log($"Updated default sensitivity values: X={defaultCameraSensitivityX}, Y={defaultCameraSensitivityY}");
    }

    /// <summary>
    /// Check if the sensitivity control system is interfering with camera movement
    /// </summary>
    [ContextMenu("Debug: Diagnose Sensitivity Issues")]
    public void DiagnoseSensitivityIssues()
    {
        Debug.Log("=== SENSITIVITY ISSUE DIAGNOSIS ===");
        Debug.Log($"reduceSensitivityDuringCinematic: {reduceSensitivityDuringCinematic}");
        Debug.Log($"isInCinematicMode: {isInCinematicMode}");
        Debug.Log($"sensitivityReduced: {sensitivityReduced}");
        Debug.Log($"cinematicSensitivityMultiplier: {cinematicSensitivityMultiplier} ({cinematicSensitivityMultiplier * 100}%)");
        
        if (reduceSensitivityDuringCinematic && sensitivityReduced)
        {
            Debug.LogError("PROBLEM FOUND: Sensitivity is currently reduced by the cinematic system!");
            Debug.LogError($"Camera sensitivity is being multiplied by {cinematicSensitivityMultiplier} = {cinematicSensitivityMultiplier * 100}%");
            Debug.LogError("This is why your camera movement feels slow!");
        }
        
        if (!isInCinematicMode && sensitivityReduced)
        {
            Debug.LogError("PROBLEM FOUND: Sensitivity is reduced but not in cinematic mode!");
            Debug.LogError("The system thinks sensitivity should be reduced even though no cinematic is playing.");
        }
        
        // Check camera values
        if (normalPlayerCamera != null)
        {
            float xSens = normalPlayerCamera.m_XAxis.m_MaxSpeed;
            float ySens = normalPlayerCamera.m_YAxis.m_MaxSpeed;
            
            Debug.Log($"Current PlayerCamera sensitivity: X={xSens}, Y={ySens}");
            
            if (xSens < 1f)
            {
                Debug.LogError($"PROBLEM FOUND: PlayerCamera X sensitivity is very low ({xSens})!");
            }
            if (ySens < 0.5f)
            {
                Debug.LogError($"PROBLEM FOUND: PlayerCamera Y sensitivity is very low ({ySens})!");
            }
        }
        
        Debug.Log("=== END DIAGNOSIS ===");
        Debug.Log("If problems were found, use 'NUCLEAR: Disable Sensitivity Control System' to fix them.");
    }

} 