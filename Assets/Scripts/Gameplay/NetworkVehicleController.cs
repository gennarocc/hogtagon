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
    [SerializeField] private float reversePowerFactor = 0.7f;
    [SerializeField] private float brakeForce = 10000f;
    [SerializeField] private float rollingResistance = 50f;

    [Header("Input Buffer Settings")]
    [SerializeField] private int bufferSize = 120; // Store 2 seconds of inputs at 60 ticks/second
    [SerializeField] private bool autoCleanupOldInputs = true;
    [SerializeField] private uint autoCleanupThreshold = 60; // Remove inputs older than 1 second (at 60 ticks/second)

    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugForces = true;
    [SerializeField] private float debugForceScale = 0.001f;
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private Color valueTextColor = new Color(0.9f, 0.9f, 0.2f, 1.0f); // Yellow for values
    [SerializeField] private Color labelTextColor = new Color(1.0f, 1.0f, 1.0f, 1.0f); // White for labels
    [SerializeField] private Color debugBackgroundColor = new Color(0, 0, 0, 0.7f);

    [Header("Jump Settings")]
    [SerializeField] private bool canJump = true;
    [SerializeField] private float jumpForce = 300f;
    [SerializeField] private float jumpCooldown = 1.5f;
    #endregion


    #region Private Fields
    // Input values
    private float throttleInput;
    private float brakeInput;
    private float steeringInput;

    // Input buffer
    private NetworkedInputBuffer<ClientInput> inputBuffer;

    // Network Time tracking
    private uint lastProcessedTick;
    private bool isTimeInitialized = false;

    // References
    private InputManager inputManager;
    private Rigidbody rb;
    private Player player;
    private List<Wheel> wheels = new List<Wheel>();
    private bool isGrounded;

    // Debug info
    private float currentSpeed;
    private Vector3 localVelocity;
    #endregion

    #region Initialization Methods
    private void Awake()
    {
        // Get references that don't depend on network state
        rb = GetComponent<Rigidbody>();
        // Find the network synchronizer
        player = GetComponent<Player>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize PhysicsQuantizer
        PhysicsQuantizer.Configure(
            defaultPrecision: 1000,
            inputPrecision: 100,
            highPrecision: 10000
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

        // For remote vehicles, we need to ensure physics doesn't take over
        if (!IsOwner && !IsServer)
        {
            rb.isKinematic = true;
        }
        else
        {
            rb.isKinematic = false;
        }

        // Initialize the input buffer using Netcode's NetworkManager
        inputBuffer = new NetworkedInputBuffer<ClientInput>(NetworkManager.Singleton, bufferSize);

        // Subscribe to the NetworkTickSystem
        NetworkManager.Singleton.NetworkTickSystem.Tick += OnNetworkTick;

        // If we're the owner, subscribe to input events
        if (IsOwner)
        {
            inputManager.JumpPressed += OnJumpPressed;
        }

        // Switch input mode to gameplay
        inputManager.SwitchToGameplayMode();

        Debug.Log($"Vehicle controller spawned. IsOwner={IsOwner}, IsServer={IsServer}, Kinematic={rb.isKinematic}");
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Clean up event handlers
        if (IsOwner)
        {
            inputManager.JumpPressed -= OnJumpPressed;
        }

        inputBuffer.Dispose();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkTickSystem.Tick -= OnNetworkTick;
        }
    }
    #endregion

    #region Network Tick System
    private void OnNetworkTick()
    {
        // Get current network tick
        uint currentTick = (uint)NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;

        if (!isTimeInitialized)
        {
            lastProcessedTick = currentTick;
            isTimeInitialized = true;
        }

        // Process network updates based on role
        if (IsOwner)
        {
            // Owner is responsible for capturing input and sending to server
            CaptureAndSendInput(currentTick);
            // Process the current input for immediate feedback
            if (inputBuffer.TryGetInput(currentTick, out ClientInput currentInput))
            {

                // Apply input and simulate physics locally
                throttleInput = currentInput.moveInput;
                brakeInput = currentInput.brakeInput;
                steeringInput = currentInput.steerInput;
                ApplyWheelPhysics();
            }
        }

        if (IsServer)
        {
            // Server is responsible for physics and state authority
            ProcessInputsOnServer(currentTick);
        }

        // Update last processed tick
        lastProcessedTick = currentTick;

        // Auto-cleanup if enabled (on both client and server)
        if (autoCleanupOldInputs)
        {
            uint cleanupThreshold = currentTick > autoCleanupThreshold ? currentTick - autoCleanupThreshold : 0;
            inputBuffer.RemoveInputsBefore(cleanupThreshold);
        }
    }

    #endregion

    private void Update()
    {
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
        // Physics update - animations always happen for visuals
        AnimateWheels();
    }

    #region Input Handling
    private void CaptureAndSendInput(uint tick)
    {
        // Capture current input
        if (inputManager.IsInGameplayMode())
        {
            throttleInput = PhysicsQuantizer.QInput(inputManager.ThrottleInput);
            brakeInput = PhysicsQuantizer.QInput(inputManager.BrakeInput);
            steeringInput = PhysicsQuantizer.QInput(Mathf.Clamp(
                CalculateSteeringAngle(PhysicsQuantizer.QVector2(inputManager.LookInput)) / maxSteeringAngle,
                -1f,
                1f
            ));
        }
        else
        {
            // If game is paused set input to 0;
            throttleInput = 0;
            brakeInput = 0;
        }


        // Create input struct
        ClientInput input = new ClientInput
        {
            clientId = NetworkManager.Singleton.LocalClientId,
            tick = tick,
            moveInput = throttleInput,
            brakeInput = brakeInput,
            steerInput = steeringInput
        };

        // Add to local buffer
        inputBuffer.AddInput(tick, input);

        // Send to server
        SendInputServerRpc(input);
    }

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
            // Use traditional position-based calculation for mouse/keyboard with quantization
            Vector3 cameraVector = PhysicsQuantizer.QVector3(transform.position - player.playerCamera.transform.position);
            cameraVector.y = 0;
            Vector3 carDirection = PhysicsQuantizer.QVector3(new Vector3(transform.forward.x, 0, transform.forward.z));

            float angle = PhysicsQuantizer.QFloat(Vector3.Angle(carDirection, cameraVector));

            // Determine sign based on dot product with right vector with quantization
            float sign = Mathf.Sign(PhysicsQuantizer.QDot(cameraVector, transform.right));

            return PhysicsQuantizer.QFloat(angle * sign);
        }
    }
    #endregion

    #region Server Input Processing
    private void ProcessInputsOnServer(uint currentTick)
    {
        if (!IsServer) return;

        // Get the most recent input for this vehicle
        if (inputBuffer.TryGetInput(currentTick, out ClientInput input))
        {
            // Apply the input to the vehicle
            throttleInput = input.moveInput;
            brakeInput = input.brakeInput;
            steeringInput = input.steerInput;

            // Run physics with these inputs
            ApplyWheelPhysics();
        }
        else if (inputBuffer.GetNewestTick() > 0)
        {
            // Use the most recent input if we don't have one for this tick
            uint newestTick = inputBuffer.GetNewestTick();
            if (inputBuffer.TryGetInput(newestTick, out ClientInput latestInput))
            {
                throttleInput = latestInput.moveInput;
                brakeInput = latestInput.brakeInput;
                steeringInput = latestInput.steerInput;

                // Run physics with these inputs
                ApplyWheelPhysics();
            }
        }

        // After physics are processed, sync vehicle state to clients
        SyncVehicleStateClientRpc(currentTick, rb.position, rb.rotation, rb.linearVelocity, rb.angularVelocity);
    }

    #endregion

    #region Physics 
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

        // Apply throttle input - always pushes forward
        if (throttleInput > 0)
        {
            accelerationForceValue += PhysicsQuantizer.QFloat(throttleInput * enginePower * powerFactor);
        }

        // Apply brake input - always pushes backward
        if (brakeInput > 0)
        {
            // Use engine power with reverse factor for both braking and reversing
            accelerationForceValue -= PhysicsQuantizer.QFloat(brakeInput * enginePower * powerFactor * reversePowerFactor);
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

    private void OnJumpPressed()
    {
        // Only process jumps if we should be running physics
        if (!canJump || !isGrounded || (!IsServer && !IsOwner))
            return;

        // Apply upward force to the vehicle with quantization
        Vector3 jumpVector = PhysicsQuantizer.QVector3(Vector3.up * jumpForce);
        PhysicsQuantizer.QAddForce(rb, jumpVector, ForceMode.Impulse);

        // If we're the owner but not the server, send jump event to server
        if (IsOwner && !IsServer)
        {
            JumpServerRpc((uint)NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick);
        }
    }
    #endregion

    #region Network RPCs
    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        // Store input in server's buffer
        inputBuffer.AddInput(input.tick, input);
    }

    [ServerRpc]
    private void JumpServerRpc(uint tick)
    {
        // Only process if we're the server and the vehicle is grounded
        if (!IsServer || !isGrounded || !canJump)
            return;

        // Apply jump force
        Vector3 jumpVector = PhysicsQuantizer.QVector3(Vector3.up * jumpForce);
        PhysicsQuantizer.QAddForce(rb, jumpVector, ForceMode.Impulse);

        // Notify all clients about the jump
        JumpClientRpc(tick);
    }

    [ClientRpc]
    private void JumpClientRpc(uint tick)
    {
        // Skip if this is the owner (they already processed the jump)
        if (IsOwner)
            return;

        // Apply jump effect on remote clients (visual only if kinematic)
        if (isGrounded && canJump)
        {
            // For non-owners, just animate the jump visually if needed
            // This is especially important for kinematic rigidbodies
            // You could play particles, sounds, etc. here
            Debug.Log($"Remote vehicle jumped at tick {tick}");
        }
    }

    [ClientRpc]
    private void SyncVehicleStateClientRpc(uint tick, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        // Skip if this is the server or owner (they're already up to date)
        if (IsServer || IsOwner)
            return;

        // Apply state to remote client's vehicle
        transform.position = position;
        transform.rotation = rotation;

        // Only apply velocities if the remote client isn't kinematic
        if (!rb.isKinematic)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;
        }
    }
    #endregion

    #region Visuals
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

            // For visual consistency, animate all wheel rotations (even on remote clients)
            // Calculate wheel rotation with quantization
            float wheelCircumference = PhysicsQuantizer.QFloat(2f * Mathf.PI * wheelRadius);
            float rotationSpeed = PhysicsQuantizer.QFloat(wheel.forwardVelocity / wheelCircumference * 360f);

            // Convert to degrees for Unity
            rotationSpeed = PhysicsQuantizer.QFloat(rotationSpeed * Mathf.Rad2Deg);
            inputBuffer.TryGetInput((uint)NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick, out ClientInput currentInput);
            wheel.mesh.Rotate(currentInput.moveInput * rotationSpeed * currentSpeed, 0, 0, Space.Self);
        }
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
        int totalLines = 9; // Basic vehicle info + quantization status + kinematic status

        // Add network stats lines
        int networkStatsLines = 6; // Header + Control + Role + 4 network stats lines (including ping)
        totalLines += networkStatsLines;

        // Just add one line for wheels grounded status
        totalLines += 1;

        // Draw background box
        GUI.Box(new Rect(10, startY, width, totalLines * lineHeight + padding * 2), "", boxStyle);

        // Start drawing content
        int yPos = startY + padding;

        // Header
        GUI.Label(new Rect(20, yPos, width - 20, lineHeight), "VEHICLE TELEMETRY", headerStyle);
        yPos += lineHeight;

        // Add kinematic status
        GUI.Label(new Rect(20, yPos, 70, lineHeight), "Kinematic:", labelStyle);
        GUI.Label(new Rect(90, yPos, width - 90, lineHeight), rb.isKinematic.ToString(), valueStyle);
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

        // Add network tick information
        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Network Tick:", labelStyle);
        int currentTick = NetworkManager.Singleton != null ? NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick : 0;
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{currentTick} (Buffer: {inputBuffer?.Count ?? 0})", valueStyle);
        yPos += lineHeight;

        // Network stats header with a bit of space before it
        yPos += 5; // Extra space
        GUI.Label(new Rect(20, yPos, width - 20, lineHeight), "NETWORK STATS", headerStyle);
        yPos += lineHeight;

        // Network role information
        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Role:", labelStyle);
        string role = IsServer ? (IsOwner ? "Host" : "Server") : (IsOwner ? "Client Owner" : "Client Remote");
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight), role, valueStyle);
        yPos += lineHeight;

        // Network ping if available
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            GUI.Label(new Rect(20, yPos, 85, lineHeight), "Ping:", labelStyle);
            float rtt = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(0) / 1000f;
            GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
                $"{rtt * 1000:F0} ms", valueStyle);
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
            suspensionLength = transform.root.GetComponent<NetworkVehicleController>().suspensionRestDistance;
            lastSuspensionLength = suspensionLength;
        }

        // Quantized version of wheel raycast
        public void CastRayQuantized()
        {
            float maxDistance = PhysicsQuantizer.QFloat(
                transform.root.GetComponent<NetworkVehicleController>().raycastDistance + 0.2f);

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