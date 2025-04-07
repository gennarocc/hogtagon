using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class NetworkVehicleController : NetworkBehaviour
{
    #region Inspector Fields
    [Header("Vehicle Properties")]
    [SerializeField] private float vehicleMass = 1500f;
    [SerializeField] private Transform centerOfMass;
    [SerializeField] private float topSpeed = 25f;

    [Header("Wheel Setup")]
    [SerializeField] private Transform frontLeftWheelTransform;
    [SerializeField] private Transform frontRightWheelTransform;
    [SerializeField] private Transform rearLeftWheelTransform;
    [SerializeField] private Transform rearRightWheelTransform;
    [SerializeField] private Transform frontLeftWheelMesh;
    [SerializeField] private Transform frontRightWheelMesh;
    [SerializeField] private Transform rearLeftWheelMesh;
    [SerializeField] private Transform rearRightWheelMesh;

    [Header("Wheel Settings")]
    [SerializeField] private float wheelRadius = 0.6f; // Wheel radius in meters

    [Header("Suspension")]
    [SerializeField] private float suspensionRestDistance = 0.5f;
    [SerializeField] private float suspensionStrength = 35000f;
    [SerializeField] private float suspensionDamping = 4000f;
    [SerializeField] private float raycastDistance = 1.0f; // Maximum raycast distance

    [Header("Steering")]
    [SerializeField] private float maxSteeringAngle = 30f;
    [SerializeField] private float frontGrip = 1.0f;
    [SerializeField] private float rearGrip = 0.8f;

    [Header("Acceleration (Blue Force)")]
    [SerializeField] private float enginePower = 15000f;
    [SerializeField] private float brakeForce = 10000f;
    [SerializeField] private float rollingResistance = 50f;

    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugForces = true;
    [SerializeField] private float debugForceScale = 0.001f;
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private Color valueTextColor = new Color(0.9f, 0.9f, 0.2f, 1.0f); // Yellow for values
    [SerializeField] private Color labelTextColor = new Color(1.0f, 1.0f, 1.0f, 1.0f); // White for labels
    [SerializeField] private Color debugBackgroundColor = new Color(0, 0, 0, 0.7f);
    [SerializeField] private bool showDetailedWheelInfo = false; // Option to show detailed wheel info

    [Header("Camera Reference")]
    [SerializeField] private Transform freeLookCamera;

    [Header("Jump Settings")]
    [SerializeField] private bool canJump = true;
    [SerializeField] private float jumpForce = 300f;
    [SerializeField] private float jumpCooldown = 1.5f;

    [Header("Deterministic Physics")]
    [SerializeField] private bool enableQuantization = true;
    [Tooltip("Toggle this during gameplay to test with/without quantization")]
    
    [Header("Network Integration")]
    [SerializeField] private bool isLocallyControlled = true;
    #endregion

    #region Private Fields
    // Input values
    private float throttleInput;
    private float brakeInput;
    private float steeringInput;

    // Reference to InputManager
    private InputManager inputManager;

    // Private vehicle state
    private Rigidbody rb;
    private List<Wheel> wheels = new List<Wheel>();
    private bool isGrounded;

    // Debug info
    private float currentSpeed;
    private Vector3 localVelocity;
    
    // Reference to the network synchronizer
    private VehicleNetworkSynchronizer networkSynchronizer;
    #endregion

    #region Initialization Methods
    private void Awake()
    {
        // Get references that don't depend on network state
        rb = GetComponent<Rigidbody>();
        
        // Find the network synchronizer
        networkSynchronizer = GetComponent<VehicleNetworkSynchronizer>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Initialize PhysicsQuantizer
        PhysicsQuantizer.Configure(
            defaultPrecision: 1000,
            inputPrecision: 100,
            highPrecision: 10000,
            enabled: enableQuantization
        );
        
        // Set up physics properties
        rb.mass = vehicleMass;

        if (centerOfMass != null)
            rb.centerOfMass = centerOfMass.localPosition;

        // Create wheels
        wheels.Clear(); // Clear any existing wheels
        if (frontLeftWheelTransform) wheels.Add(new Wheel(frontLeftWheelTransform, frontLeftWheelMesh, true, true));
        if (frontRightWheelTransform) wheels.Add(new Wheel(frontRightWheelTransform, frontRightWheelMesh, false, true));
        if (rearLeftWheelTransform) wheels.Add(new Wheel(rearLeftWheelTransform, rearLeftWheelMesh, true, false));
        if (rearRightWheelTransform) wheels.Add(new Wheel(rearRightWheelTransform, rearRightWheelMesh, false, false));

        // Get InputManager instance
        inputManager = InputManager.Instance;
        
        // Determine if this is locally controlled based on ownership
        isLocallyControlled = IsOwner;
        
        // Register for jump event if we own this vehicle
        if (isLocallyControlled && inputManager != null)
        {
            inputManager.JumpPressed += OnJumpPressed;
            
            // Switch input mode to gameplay
            inputManager.SwitchToGameplayMode();
        }
        
        Debug.Log($"Vehicle controller spawned. IsOwner={IsOwner}, IsLocallyControlled={isLocallyControlled}");
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        // Unregister from input events
        if (inputManager != null)
        {
            inputManager.JumpPressed -= OnJumpPressed;
        }
    }
    #endregion

    #region Update Methods
    private void Update()
    {
        // Check if enableQuantization was changed in inspector
        PhysicsQuantizer.Configure(enabled: enableQuantization);
        
        // Only process inputs for locally controlled vehicles
        if (isLocallyControlled && inputManager != null && inputManager.IsInGameplayMode())
        {
            // Get input from InputManager with quantization
            throttleInput = PhysicsQuantizer.QInput(inputManager.ThrottleInput);
            brakeInput = PhysicsQuantizer.QInput(inputManager.BrakeInput);

            // Get look input with quantization
            Vector2 lookInput = PhysicsQuantizer.QVector2(inputManager.LookInput);

            // Calculate steering angle
            float steeringAngle = CalculateSteeringAngle(lookInput);

            // Normalize to input range (-1 to 1) with quantization
            steeringInput = PhysicsQuantizer.QInput(Mathf.Clamp(steeringAngle / maxSteeringAngle, -1f, 1f));
        }

        // Calculate speed for debugging
        currentSpeed = PhysicsQuantizer.QFloat(rb.linearVelocity.magnitude);
        localVelocity = PhysicsQuantizer.QVector3(transform.InverseTransformDirection(rb.linearVelocity));

        // Update grounded state
        isGrounded = false;
        foreach (var wheel in wheels)
        {
            if (wheel.isGrounded)
            {
                isGrounded = true;
                break;
            }
        }

        // Toggle debug UI with F1 key
        if (Input.GetKeyDown(KeyCode.F1))
        {
            showDebugUI = !showDebugUI;
        }
    }

    private void FixedUpdate()
    {
        // Apply wheel physics
        ApplyWheelPhysics();

        // Animate wheel meshes after physics update
        AnimateWheels();

        // Add some angular drag to prevent excessive spinning with quantization
        if (rb.angularVelocity.magnitude > 0.1f)
        {
            rb.angularVelocity = PhysicsQuantizer.QVector3(rb.angularVelocity * 0.95f);
        }
    }
    #endregion

    #region Input Handling
    // Calculate steering input from look vector (0-1 range)
    public float CalculateSteeringInputFromLook(Vector2 lookInput)
    {
        float steeringAngle = CalculateSteeringAngle(lookInput);
        
        // Normalize to input range (-1 to 1) with quantization
        return PhysicsQuantizer.QInput(Mathf.Clamp(steeringAngle / maxSteeringAngle, -1f, 1f));
    }

    // Calculate steering angle based on input device and camera position
    private float CalculateSteeringAngle(Vector2 lookInput)
    {
        if (inputManager.IsUsingGamepad && lookInput.sqrMagnitude > 0.01f)
        {
            // Convert look input to camera angle with quantization
            return PhysicsQuantizer.QFloat(Mathf.Atan2(lookInput.x, lookInput.y) * Mathf.Rad2Deg);
        }
        else
        {
            // Make sure we have a camera reference
            if (freeLookCamera == null)
            {
                // Try to find the camera if not assigned
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    freeLookCamera = mainCamera.transform;
                }
                else
                {
                    Debug.LogWarning("No camera found for steering calculation");
                    return 0f;
                }
            }

            // Use traditional position-based calculation for mouse/keyboard with quantization
            Vector3 cameraVector = PhysicsQuantizer.QVector3(transform.position - freeLookCamera.position);
            cameraVector.y = 0;
            Vector3 carDirection = PhysicsQuantizer.QVector3(new Vector3(transform.forward.x, 0, transform.forward.z));

            float angle = PhysicsQuantizer.QFloat(Vector3.Angle(carDirection, cameraVector));

            // Determine sign based on dot product with right vector with quantization
            float sign = Mathf.Sign(PhysicsQuantizer.QDot(cameraVector, transform.right));

            return PhysicsQuantizer.QFloat(angle * sign);
        }
    }
    #endregion

    #region Network Methods
    // Set whether this vehicle is locally controlled
    public void SetLocallyControlled(bool isLocal)
    {
        isLocallyControlled = isLocal;
        
        // Update input event registration
        if (inputManager != null)
        {
            if (isLocallyControlled)
            {
                inputManager.JumpPressed += OnJumpPressed;
            }
            else
            {
                inputManager.JumpPressed -= OnJumpPressed;
            }
        }
    }
    
    // Apply input from the network
    public void ApplyNetworkInput(float throttle, float brake, float steering, bool jumpPressed)
    {
        throttleInput = throttle;
        brakeInput = brake;
        steeringInput = steering;
        
        // Handle jump if needed
        if (jumpPressed && canJump && isGrounded)
        {
            Vector3 jumpVector = PhysicsQuantizer.QVector3(Vector3.up * jumpForce);
            PhysicsQuantizer.QAddForce(rb, jumpVector, ForceMode.Impulse);
        }
    }
    
    // Simulate a single physics step (used for client-side prediction)
    public void SimulatePhysicsStep()
    {
        ApplyWheelPhysics();
    }
    
    // Access to input manager for the network synchronizer
    public InputManager GetInputManager()
    {
        return inputManager;
    }
    
    // Enable/disable debug UI
    public bool IsDebugUIEnabled()
    {
        return showDebugUI;
    }
    #endregion

    #region Physics Calculations
    // Extract wheel physics into a separate method
    private void ApplyWheelPhysics()
    {
        foreach (var wheel in wheels)
        {
            // Cast ray to find ground with quantized raycast
            wheel.CastRayQuantized();

            if (wheel.isGrounded)
            {
                // 1. Calculate suspension force
                CalculateSuspensionForceQuantized(wheel);

                // 2. Calculate steering/grip force
                CalculateSteeringForceQuantized(wheel);

                // 3. Calculate acceleration/braking force
                CalculateAccelerationForceQuantized(wheel);

                // Apply forces at the wheel transform position with quantization
                Vector3 totalForce = PhysicsQuantizer.QVector3(
                    wheel.suspensionForce + wheel.steeringForce + wheel.accelerationForce);
                    
                PhysicsQuantizer.QAddForceAtPosition(rb, totalForce, wheel.transform.position);
            }
        }
    }

    private void CalculateSuspensionForceQuantized(Wheel wheel)
    {
        if (!wheel.isGrounded) return;

        // Get the suspension direction (always up from the wheel)
        Vector3 suspensionDir = PhysicsQuantizer.QDirection(transform.up);

        // Calculate suspension offset (how much the spring is compressed) with quantization
        float offset = PhysicsQuantizer.QFloat(suspensionRestDistance - wheel.suspensionLength);

        // Get the velocity of the wheel in the suspension direction with quantization
        Vector3 wheelPointVelocity = rb.GetPointVelocity(wheel.transform.position);
        float suspensionVelocity = PhysicsQuantizer.QDot(wheelPointVelocity, suspensionDir);

        // Calculate force using Hooke's law with damping with quantization
        float suspensionForceValue = PhysicsQuantizer.QFloat(
            (offset * suspensionStrength) - (suspensionVelocity * suspensionDamping));

        // Ensure the force is always pushing up (when grounded)
        suspensionForceValue = Mathf.Max(0, suspensionForceValue);

        // Calculate the load for this wheel with quantization
        wheel.load = PhysicsQuantizer.QFloat(
            Mathf.Clamp01(suspensionForceValue / (vehicleMass * Physics.gravity.magnitude / wheels.Count)));

        // Apply the suspension force with quantization
        wheel.suspensionForce = PhysicsQuantizer.QVector3(suspensionDir * suspensionForceValue);
    }

    private void CalculateSteeringForceQuantized(Wheel wheel)
    {
        if (!wheel.isGrounded) return;

        // Get wheel's velocity
        Vector3 wheelVelocity = rb.GetPointVelocity(wheel.transform.position);

        // Get the forward direction (blue) - the direction the wheel rolls in
        Vector3 wheelForwardDir = PhysicsQuantizer.QDirection(transform.forward);

        // Apply steering angle to front wheels and counter-steering to rear wheels with quantization
        float steeringAngle = PhysicsQuantizer.QFloat(steeringInput * maxSteeringAngle);
        
        if (wheel.isFront)
        {
            wheelForwardDir = PhysicsQuantizer.QDirection(
                Quaternion.AngleAxis(steeringAngle, transform.up) * wheelForwardDir);
        }
        else
        {
            // Counter-steering for rear wheels (opposite angle to front wheels)
            wheelForwardDir = PhysicsQuantizer.QDirection(
                Quaternion.AngleAxis(-steeringAngle, transform.up) * wheelForwardDir);
        }

        // Get the right direction (red) with quantization
        Vector3 wheelRightDir = PhysicsQuantizer.QDirection(
            Vector3.Cross(wheel.contactNormal, wheelForwardDir));
            
        wheelForwardDir = PhysicsQuantizer.QDirection(
            Vector3.Cross(wheelRightDir, wheel.contactNormal));

        // Project the wheel's velocity onto the forward and right axes with quantization
        wheel.forwardVelocity = PhysicsQuantizer.QFloat(
            PhysicsQuantizer.QDot(wheelVelocity, wheelForwardDir));
            
        wheel.sidewaysVelocity = PhysicsQuantizer.QFloat(
            PhysicsQuantizer.QDot(wheelVelocity, wheelRightDir));

        // Calculate slip percentage with quantization
        float wheelSpeed = PhysicsQuantizer.QFloat(wheelVelocity.magnitude);
        wheel.slipPercentage = wheelSpeed > 0.1f ? 
            PhysicsQuantizer.QFloat(Mathf.Abs(wheel.sidewaysVelocity) / wheelSpeed) : 0f;

        // Determine grip based on whether it's a front or rear wheel
        float grip = wheel.isFront ? frontGrip : rearGrip;

        // Higher load (weight on wheel) gives more grip with quantization
        float adjustedGrip = PhysicsQuantizer.QFloat(grip * wheel.load);

        // Calculate steering force with quantization
        float steeringForceValue = PhysicsQuantizer.QFloat(
            -wheel.sidewaysVelocity * adjustedGrip * vehicleMass);

        // Apply the steering force with quantization
        wheel.steeringForce = PhysicsQuantizer.QVector3(wheelRightDir * steeringForceValue);
    }

    private void CalculateAccelerationForceQuantized(Wheel wheel)
    {
        if (!wheel.isGrounded) return;

        // Get the forward direction of the wheel with quantization
        Vector3 wheelForwardDir = PhysicsQuantizer.QDirection(transform.forward);

        // Apply steering angle with quantization
        float steeringAngle = PhysicsQuantizer.QFloat(steeringInput * maxSteeringAngle);
        
        if (wheel.isFront)
        {
            wheelForwardDir = PhysicsQuantizer.QDirection(
                Quaternion.AngleAxis(steeringAngle, transform.up) * wheelForwardDir);
        }
        else
        {
            // Counter-steering for rear wheels
            wheelForwardDir = PhysicsQuantizer.QDirection(
                Quaternion.AngleAxis(-steeringAngle, transform.up) * wheelForwardDir);
        }

        // Make sure the forward direction is perpendicular to the contact normal with quantization
        Vector3 wheelRightDir = PhysicsQuantizer.QDirection(
            Vector3.Cross(wheel.contactNormal, wheelForwardDir));
            
        wheelForwardDir = PhysicsQuantizer.QDirection(
            Vector3.Cross(wheelRightDir, wheel.contactNormal));

        // Calculate current speed as percentage of top speed with quantization
        float speedRatio = PhysicsQuantizer.QFloat(Mathf.Clamp01(currentSpeed / topSpeed));

        // Engine power curve with quantization
        float powerFactor = PhysicsQuantizer.QFloat(1.0f - Mathf.Pow(speedRatio, 2.0f));

        // Calculate acceleration force with quantization
        float accelerationForceValue = 0f;

        // 4-wheel drive - apply engine power to all wheels with quantization
        accelerationForceValue += PhysicsQuantizer.QFloat(throttleInput * enginePower * powerFactor);

        // Apply braking force to all wheels with quantization
        if (brakeInput > 0)
        {
            // Braking force opposes current forward velocity
            accelerationForceValue -= PhysicsQuantizer.QFloat(
                Mathf.Sign(wheel.forwardVelocity) * brakeInput * brakeForce);
        }

        // Apply rolling resistance with quantization
        if (Mathf.Abs(wheel.forwardVelocity) > 0.1f)
        {
            accelerationForceValue -= PhysicsQuantizer.QFloat(
                Mathf.Sign(wheel.forwardVelocity) * rollingResistance);
        }

        // Scale by load with quantization
        accelerationForceValue = PhysicsQuantizer.QFloat(accelerationForceValue * wheel.load);

        // Apply the acceleration force with quantization
        wheel.accelerationForce = PhysicsQuantizer.QVector3(wheelForwardDir * accelerationForceValue);
    }
    #endregion

    #region Wheel Handling
    // Update wheel animations
    private void AnimateWheels()
    {
        foreach (var wheel in wheels)
        {
            if (wheel.mesh == null) continue;

            // Apply steering rotation with quantization
            Quaternion steeringRotation = Quaternion.identity;
            float steeringAngle = PhysicsQuantizer.QFloat(steeringInput * maxSteeringAngle);

            if (wheel.isFront)
            {
                // Front wheel steering
                steeringRotation = Quaternion.Euler(0, steeringAngle, 0);
            }
            else
            {
                // Rear wheel counter-steering
                steeringRotation = Quaternion.Euler(0, -steeringAngle, 0);
            }

            wheel.mesh.localRotation = steeringRotation;

            // Calculate wheel rotation with quantization
            float wheelCircumference = PhysicsQuantizer.QFloat(2f * Mathf.PI * wheelRadius);
            float rotationSpeed = PhysicsQuantizer.QFloat(wheel.forwardVelocity / wheelCircumference * 360f);
            
            // Convert to degrees for Unity
            rotationSpeed = PhysicsQuantizer.QFloat(rotationSpeed * Mathf.Rad2Deg);

            wheel.mesh.Rotate(rotationSpeed, 0, 0, Space.Self);      
        }
    }

    // Handle Jump functionality with quantization
    private void OnJumpPressed()
    {
        if (!canJump || !isGrounded)
            return;

        // Apply upward force to the vehicle with quantization
        Vector3 jumpVector = PhysicsQuantizer.QVector3(Vector3.up * jumpForce);
        PhysicsQuantizer.QAddForce(rb, jumpVector, ForceMode.Impulse);
    }
    #endregion

    #region Debug Visualization
    private void OnGUI()
    {
        if (!showDebugUI) return;

        // Calculate steering angle for display
        float steeringAngle = steeringInput * maxSteeringAngle;

        // Setup styles
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTexture(2, 2, debugBackgroundColor);

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = labelTextColor;
        labelStyle.fontSize = 12;

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.normal.textColor = valueTextColor;
        valueStyle.fontSize = 12;
        valueStyle.fontStyle = FontStyle.Bold;

        GUIStyle headerStyle = new GUIStyle(labelStyle);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = valueTextColor;

        // Calculate sizes
        int padding = 10;
        int width = 240;
        int startY = 10;
        int lineHeight = 20;

        // Count how many lines we need (compact version)
        int totalLines = 8; // Basic vehicle info + quantization status + network status

        if (showDetailedWheelInfo)
        {
            foreach (var wheel in wheels)
            {
                totalLines += wheel.isGrounded ? 4 : 1;
            }
        }
        else
        {
            // Just add one line for wheels grounded status
            totalLines += 1;
        }

        // Draw background box
        GUI.Box(new Rect(10, startY, width, totalLines * lineHeight + padding * 2), "", boxStyle);

        // Start drawing content
        int yPos = startY + padding;

        // Header
        GUI.Label(new Rect(20, yPos, width - 20, lineHeight), "VEHICLE TELEMETRY", headerStyle);
        yPos += lineHeight;

        // Network status
        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Control:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            isLocallyControlled ? "Local" : "Remote", valueStyle);
        yPos += lineHeight;
        
        // Add Network Role
        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Role:", labelStyle);
        string roleText = "Unknown";
        if (IsSpawned)
        {
            if (IsOwner) roleText = "Owner";
            else roleText = "Remote";
            if (IsServer) roleText += ", Server";
            if (IsClient) roleText += ", Client";
        }
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight), roleText, valueStyle);
        yPos += lineHeight;

        // Vehicle info - using both label and value styles for color differentiation
        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Speed:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            $"{currentSpeed:F1} m/s ({currentSpeed * 3.6f:F1} km/h)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Input:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            $"Throttle={throttleInput:F2}, Brake={brakeInput:F2}", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Steering:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            $"{steeringInput:F2} ({steeringAngle:F0}°)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Grounded:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            $"{isGrounded}", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Quantized:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight),
            $"{enableQuantization}", valueStyle);
        yPos += lineHeight;

        // Calculate average wheel forces for a simpler display
        float avgSuspension = 0;
        float avgLoad = 0;
        int groundedWheels = 0;

        foreach (var wheel in wheels)
        {
            if (wheel.isGrounded)
            {
                avgSuspension += wheel.suspensionForce.magnitude;
                avgLoad += wheel.load;
                groundedWheels++;
            }
        }

        if (groundedWheels > 0)
        {
            avgSuspension /= groundedWheels;
            avgLoad /= groundedWheels;
        }

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Suspension:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{avgSuspension:F0} N (Load: {avgLoad:P0})", valueStyle);
        yPos += lineHeight;

        // Wheels grounded info (simple summary)
        int wheelsGrounded = 0;
        foreach (var wheel in wheels)
        {
            if (wheel.isGrounded) wheelsGrounded++;
        }

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Wheels:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{wheelsGrounded}/{wheels.Count} grounded", valueStyle);
        yPos += lineHeight;

        // Only show detailed wheel info if enabled
        if (showDetailedWheelInfo)
        {
            yPos += 5; // Extra space before wheel details

            foreach (var wheel in wheels)
            {
                string wheelName = (wheel.isFront ? "Front" : "Rear") + (wheel.isLeft ? " Left" : " Right");

                GUI.Label(new Rect(20, yPos, width - 20, lineHeight),
                    $"{wheelName} - Grounded: {wheel.isGrounded}", labelStyle);
                yPos += lineHeight;

                if (wheel.isGrounded)
                {
                    GUI.Label(new Rect(40, yPos, 85, lineHeight), "Suspension:", labelStyle);
                    GUI.Label(new Rect(125, yPos, width - 125, lineHeight),
                        $"{wheel.suspensionForce.magnitude:F0} N (Load: {wheel.load:P0})", valueStyle);
                    yPos += lineHeight;

                    GUI.Label(new Rect(40, yPos, 85, lineHeight), "Steering:", labelStyle);
                    GUI.Label(new Rect(125, yPos, width - 125, lineHeight),
                        $"{wheel.steeringForce.magnitude:F0} N (Slip: {wheel.slipPercentage:P0})", valueStyle);
                    yPos += lineHeight;

                    GUI.Label(new Rect(40, yPos, 85, lineHeight), "Acceleration:", labelStyle);
                    GUI.Label(new Rect(125, yPos, width - 125, lineHeight),
                        $"{wheel.accelerationForce.magnitude:F0} N", valueStyle);
                    yPos += lineHeight;
                }
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !showDebugForces) return;

        // Draw vehicle velocity
        Gizmos.color = Color.white;
        Gizmos.DrawRay(transform.position, rb.linearVelocity * 0.1f);

        foreach (var wheel in wheels)
        {
            if (!wheel.isGrounded) continue;

            // Draw GREEN force: Suspension
            Gizmos.color = Color.green;
            Gizmos.DrawRay(wheel.transform.position, wheel.suspensionForce * debugForceScale);

            // Draw RED force: Steering/Grip
            Gizmos.color = Color.red;
            Gizmos.DrawRay(wheel.transform.position, wheel.steeringForce * debugForceScale);

            // Draw BLUE force: Acceleration/Braking
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(wheel.transform.position, wheel.accelerationForce * debugForceScale);
        }
    }
    #endregion

    #region Helper Methods
    // Helper method to create a texture for the background
    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
    #endregion

    #region Wheel Class
    // Wheel class to manage individual wheel data
    private class Wheel
    {
        public Transform transform;
        public Transform mesh;
        public bool isLeft;
        public bool isFront;

        // Wheel state
        public bool isGrounded;
        public Vector3 contactPoint;
        public Vector3 contactNormal;
        public float suspensionLength;
        public float lastSuspensionLength;
        public float load; // How much of the vehicle weight is on this wheel (0-1)

        // Forces
        public Vector3 suspensionForce;
        public Vector3 steeringForce;
        public Vector3 accelerationForce;

        // Debug values
        public float forwardVelocity;
        public float sidewaysVelocity;
        public float slipPercentage;

        public Wheel(Transform transform, Transform mesh, bool isLeft, bool isFront)
        {
            this.transform = transform;
            this.mesh = mesh;
            this.isLeft = isLeft;
            this.isFront = isFront;

            // Set default values
            suspensionLength = transform.parent.parent.GetComponent<NetworkVehicleController>().suspensionRestDistance;
            lastSuspensionLength = suspensionLength;
        }

        // Quantized version of wheel raycast
        public void CastRayQuantized()
        {
            float maxDistance = PhysicsQuantizer.QFloat(
                transform.parent.parent.GetComponent<NetworkVehicleController>().raycastDistance + 0.2f);

            // Use quantized raycast
            if (PhysicsQuantizer.QRaycast(
                transform.position, 
                -transform.up, 
                out RaycastHit hit, 
                maxDistance))
            {
                isGrounded = true;
                contactPoint = hit.point; // Already quantized by QRaycast
                contactNormal = hit.normal; // Already quantized by QRaycast

                // Calculate suspension length with quantization
                suspensionLength = PhysicsQuantizer.QFloat(
                    Mathf.Clamp(hit.distance, 0.1f, maxDistance - 0.2f));
            }
            else
            {
                isGrounded = false;
                suspensionLength = PhysicsQuantizer.QFloat(maxDistance - 0.2f);
                contactPoint = PhysicsQuantizer.QVector3(
                    transform.position - (transform.up * suspensionLength));
                contactNormal = transform.up;
                load = 0f;

                // Reset forces
                suspensionForce = Vector3.zero;
                steeringForce = Vector3.zero;
                accelerationForce = Vector3.zero;
            }
        }
    }
    #endregion
}