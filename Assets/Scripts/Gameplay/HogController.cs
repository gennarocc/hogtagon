using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using Hogtagon.Core.Infrastructure;

// TODO: After opening Unity, regenerate DefaultControls.cs file by saving the DefaultControls.inputactions asset in the editor
// This will ensure the new Steer action is properly included in the generated code

public class HogController : NetworkBehaviour
{
    #region Configuration Parameters

    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f;
    [SerializeField] private float brakeTorque = 300f;
    [SerializeField, Range(0f, 1f)] private float rearSteeringAmount = .35f;
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .7f;
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2.0f;
    [SerializeField, Range(0.1f, 5f)] private float decelerationFactor = 1f;
    [SerializeField] private float frontLeftRpm; // Monitoring variable
    [SerializeField] private float velocity; // Monitoring variable
    
    [Header("Rocket Jump")]
    [SerializeField] private float jumpForce = 7f; // How much upward force to apply
    [SerializeField] private float jumpCooldown = 15f; // Time between jumps
    [SerializeField] private bool canJump = true; // Whether the player can jump
    public bool JumpOnCooldown => jumpOnCooldown;
    public float JumpCooldownRemaining => jumpCooldownRemaining;
    public float JumpCooldownTotal => jumpCooldown;

    [Header("Input Smoothing Settings")]
    [SerializeField, Range(1, 10)] public int steeringBufferSize = 5;
    [SerializeField, Range(0f, 20f)] public float minDeadzone = 3f;
    [SerializeField, Range(0f, 30f)] public float maxDeadzone = 10f;
    [SerializeField, Range(10f, 50f)] public float maxSpeedForSteering = 20f;
    [SerializeField, Range(0.1f, 1f)] public float minSteeringResponse = 0.6f;
    [SerializeField, Range(0.5f, 1f)] public float maxSteeringResponse = 1.0f;
    [SerializeField, Range(0.1f, 3f)] public float steeringInputSmoothing = 0.8f; // How quickly steering increases to max
    [SerializeField] public enum WeightingMethod { Exponential, Logarithmic, Linear }
    [SerializeField] public WeightingMethod inputWeightingMethod = WeightingMethod.Linear;
    [SerializeField, Range(0.1f, 3f)] public float weightingFactor = 1.0f;

    [Header("Network")]
    [SerializeField] private float serverValidationThreshold = 5.0f;  // How far client can move before server corrections
    [SerializeField] private float clientUpdateInterval = 0.03f;      // How often client sends updates (33Hz) - faster now
    [SerializeField] private float stateUpdateInterval = 0.06f;       // How often server broadcasts state (16Hz) - faster now
    [SerializeField] private float visualSmoothingSpeed = 10f;
    [SerializeField] private bool debugMode = true;                   // Enable debug mode by default

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CinemachineFreeLook freeLookCamera;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
    [SerializeField] private Transform[] wheelTransforms = new Transform[4]; // FL, FR, RL, RR

    [Header("Effects")]
    [SerializeField] public ParticleSystem[] wheelParticleSystems = new ParticleSystem[2]; // RL, RR
    [SerializeField] public TrailRenderer[] tireSkids = new TrailRenderer[2]; // RL, RR
    [SerializeField] public GameObject Explosion;
    [SerializeField] public ParticleSystem[] jumpParticleSystems = new ParticleSystem[2]; // RL, RR

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.RTPC rpm;

    [Header("Engine Settings")]
    [SerializeField] private float minRPM = 500f;
    [SerializeField] private float maxRPM = 7000f;
    [SerializeField] private float idleRPM = 800f;
    private float targetRPM;
    private bool engineRunning;

    [Header("Audio & Visual Effects")]
    [SerializeField] private ParticleSystem exhaustParticles;
    [SerializeField] private AudioSource engineAudioSource;

    #endregion

    #region Private Fields

    // Constants
    private const float MIN_VELOCITY_FOR_REVERSE = -0.5f;

    // Input reference - replace with direct input calls
    private DefaultControls inputControls;
    
    // Player reference
    private Player player;

    // Input fields 
    private float moveInput = 0f;
    private float steeringInput = 0f;
    private float brakeInput = 0f;
    private bool jumpInput = false;
    private bool driftInput = false;

    // Wheel references
    private WheelCollider[] steeringWheels { get { return new WheelCollider[] { wheelColliders[0], wheelColliders[1] }; } }
    private WheelCollider[] drivenWheels { get { return wheelColliders; } } // All wheels are driven
    private WheelCollider[] allWheels { get { return wheelColliders; } }
    
    // Input Smoothing
    private Queue<float> _recentSteeringInputs;
    private List<float> _steeringInputsList = new List<float>();
    private float currentSteeringInput = 0f; // Current smoothed steering input

    // Network variables moved to Awake to initialize early
    private NetworkVariable<bool> isDrifting;
    private NetworkVariable<bool> isJumping;
    private NetworkVariable<bool> jumpReady;
    private NetworkVariable<StateSnapshot> vehicleState;

    // Physics and control variables
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private bool jumpOnCooldown = false;
    private float jumpCooldownRemaining = 0f;
    private float _stateUpdateTimer = 0f;
    private float _clientUpdateTimer = 0f;
    private bool hasReceivedInitialSync = false;

    // Cached physics values
    private Vector3 cachedVelocity;
    private float cachedSpeed;
    private float[] cachedWheelRpms = new float[4];
    private float cachedForwardVelocity;
    
    // Visual smoothing (for remote clients)
    private Vector3 visualPositionTarget;
    private Quaternion visualRotationTarget;
    private Vector3 visualVelocityTarget;
    private bool needsSmoothing = false;

    // Wheel friction data
    private WheelFrictionCurve[] wheelFrictions = new WheelFrictionCurve[4];
    private float[] originalExtremumSlip = new float[4];

    #endregion

    #region Data Structures

    // Struct to send input data to server
    public struct ClientInput : INetworkSerializable
    {
        public ulong clientId;
        public float moveInput;
        public float brakeInput;
        public float steeringInput;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref steeringInput);
        }
    }

    // HogInput struct for client to server communication
    public struct HogInput : INetworkSerializable
    {
        public ulong clientId;
        public float moveInput;
        public float steeringInput;
        public float brakeInput;
        public bool jumpInput;
        public bool driftInput;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref steeringInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref jumpInput);
            serializer.SerializeValue(ref driftInput);
        }
    }

    // State snapshot for networking
    public struct StateSnapshot : INetworkSerializable
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float steeringAngle;
        public float motorTorque;
        public uint updateId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
            serializer.SerializeValue(ref steeringAngle);
            serializer.SerializeValue(ref motorTorque);
            serializer.SerializeValue(ref updateId);
        }
    }

    // Steering angles data structure
    private struct SteeringData
    {
        public float frontSteeringAngle;
        public float rearSteeringAngle;
        public bool isReversing;
    }

    #endregion

    #region Unity Lifecycle Methods

    private void Awake()
    {
        // Initialize network variables early to avoid permission issues
        isDrifting = new NetworkVariable<bool>(false, 
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);
            
        isJumping = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);
            
        jumpReady = new NetworkVariable<bool>(true,
            NetworkVariableReadPermission.Everyone, 
            NetworkVariableWritePermission.Server);
            
        vehicleState = new NetworkVariable<StateSnapshot>(
            new StateSnapshot(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
            
        // Initialize steering input buffer
        _recentSteeringInputs = new Queue<float>(steeringBufferSize);
        
        // We'll use direct input instead of the DefaultControls
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Get component references
        SetupComponents();
        
        // Initialize wheel meshes
        SetupWheelMeshes();
        
        // Set initial state
        engineRunning = false;
        targetRPM = minRPM;
        
        // Only spawn effects on clients
        if (IsClient)
        {
            // Setup visual effects
            SetupEffects();
            
            // Enable particle effects for all clients to see
            EnableParticleEffects(false);
        }
        
        // Initialize the vehicle's physics
        InitializeVehiclePhysics();
        
        if (debugMode)
        {
            Debug.Log($"OnNetworkSpawn - IsOwner: {IsOwner}, IsServer: {IsServer}, HasStateAuthority: {HasStateAuthority}");
        }
        
        // Setup input events for the owner - using standard Unity Input system instead of InputSystem package
        if (IsOwner)
        {
            // We'll use direct polling in GetMoveInput, GetSteeringInput, etc. instead of events
        }
    }
    
    private void Start()
    {
        // Ensure physically bodies settle on the ground
        StartCoroutine(DelayedPhysicsEnable());
        
        // Initialize visual smoothing targets
        visualPositionTarget = transform.position;
        visualRotationTarget = transform.rotation;
        visualVelocityTarget = Vector3.zero;

        if (debugMode && IsOwner)
        {
            Debug.Log($"Start - Owner vehicle at {rb.position}, IsKinematic: {rb.isKinematic}");
        }
    }
    
    private IEnumerator DelayedPhysicsEnable()
    {
        // Wait one physics frame to ensure proper initialization
        yield return new WaitForFixedUpdate();
        
        // Make sure rigidbody is not kinematic for all clients
        rb.isKinematic = false;
        
        // Enable all wheel colliders
        EnableWheelColliders();
        
        // Make sure all physics bodies are awake
        rb.WakeUp();
        
        // Ensure the car is properly placed on the ground
        AdjustPositionToGround();
        
        // Set hasReceivedInitialSync to true for all clients
        hasReceivedInitialSync = true;
        
        if (debugMode)
        {
            Debug.Log($"DelayedPhysicsEnable - Position: {rb.position}, IsKinematic: {rb.isKinematic}");
        }
    }

    private void AdjustPositionToGround()
    {
        // Only run this on the server or for the owner
        if (!IsServer && !IsOwner) return;
        
        // Get the car's current position
        Vector3 currentPosition = rb.position;
        
        // Cast a ray downward to find the ground
        RaycastHit hit;
        if (Physics.Raycast(currentPosition + Vector3.up * 5f, Vector3.down, out hit, 20f, 
            LayerMask.GetMask("Default", "Ground", "Environment")))
        {
            // Adjust position to be just above the ground
            // Use the wheel collider radius for offset
            float wheelRadius = wheelColliders[0].radius;
            float heightOffset = wheelRadius + 0.05f; // Small fixed offset to prevent ground collision
            
            // Set the new position - place firmly on ground
            Vector3 newPosition = hit.point + Vector3.up * heightOffset;
            
            // Apply the new position to both transform and rigidbody
            transform.position = newPosition;
            rb.position = newPosition;
            
            // Make sure rotation is upright aligned with ground normal
            Quaternion groundAlignment = Quaternion.FromToRotation(transform.up, hit.normal);
            transform.rotation = groundAlignment * transform.rotation;
            rb.rotation = transform.rotation;
            
            // Zero out any existing velocity
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            
            // Apply a small downward force to settle the vehicle
            rb.AddForce(Vector3.down * rb.mass * 5f, ForceMode.Impulse);
            
            // Wake up the rigidbody to ensure physics processing
            rb.WakeUp();
            
            if (debugMode)
            {
                Debug.Log($"Adjusted car position to ground at {newPosition}, ground hit at {hit.point}");
            }
        }
        else
        {
            if (debugMode)
            {
                Debug.LogWarning("Failed to find ground beneath car for position adjustment");
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from input events
        // Using direct input polling now, so no events to unsubscribe from

        // Only the server or owner should play the engine off sound
        if (IsOwner || IsServer)
        {
            // Wwise audio - commented out for now since Wwise namespace isn't properly setup
            // AK.Wwise.SoundEngine.PostEvent("EngineOff", gameObject);
        }
    }

    private void Update()
    {
        // Perform updates regardless of initialization status
        if (IsOwner)
        {
            // Owner controls the vehicle
            HandleOwnerInput();

            // Send input to server if we're not the host (server)
            if (!IsServer)
            {
                // Only clients need to send inputs to server
                ClientMove();

                // Periodically send full state to server for validation and broadcasting
                _clientUpdateTimer += Time.deltaTime;
                if (_clientUpdateTimer >= clientUpdateInterval)
                {
                    _clientUpdateTimer = 0f;
                    SendStateToServer();
                }
            }
            
            // Update jump cooldown
            if (jumpOnCooldown)
            {
                jumpCooldownRemaining -= Time.deltaTime;
                if (jumpCooldownRemaining <= 0)
                {
                    jumpOnCooldown = false;
                }
            }
        }
        else if (IsServer && !IsOwner)
        {
            // Server controls non-owned vehicles
            // For non-player vehicles like AI or dropped vehicles

            // Send broadcasts periodically
            _stateUpdateTimer += Time.deltaTime;
            if (_stateUpdateTimer >= stateUpdateInterval)
            {
                _stateUpdateTimer = 0f;
                BroadcastServerStateClientRpc(vehicleState.Value);
            }
        }
        else
        {
            // Remote client - smooth visual representation of other vehicles
            SmoothRemoteVehicleVisuals();
        }

        // Common updates - always perform them for visual consistency
        AnimateWheels();
        UpdateDriftEffects();
    }

    private void FixedUpdate()
    {
        // Cache frequently used physics values
        CachePhysicsValues();

        if (IsOwner && !IsServer)
        {
            // Client-only code for when we're the client but not the host
            
            // Apply consistent forward force based on current torque
            if (Mathf.Abs(currentTorque) > 10f || Mathf.Abs(moveInput) > 0.1f) // Added moveInput check to improve response
            {
                // Calculate movement direction based on input and current torque
                Vector3 forceDirection = transform.forward * Mathf.Sign(moveInput != 0 ? moveInput : currentTorque);
                
                // Apply much stronger force for client acceleration
                // Increased from 20f to 40f for significantly better client movement
                float forceMagnitude = (Mathf.Abs(currentTorque) + maxTorque * 0.5f) * 40f;
                rb.AddForce(forceDirection * forceMagnitude, ForceMode.Force);
                
                // Also add some downward force to prevent bouncing
                rb.AddForce(Vector3.down * rb.mass * 3f, ForceMode.Force);
                
                if (debugMode && (rb.linearVelocity.magnitude < 5f || Time.frameCount % 60 == 0))
                {
                    Debug.Log($"Client FixedUpdate force: {forceDirection * forceMagnitude}, speed: {velocity:F2}m/s, torque: {currentTorque}");
                }
            }
        }
        else if (IsServer && IsOwner)
        {
            // Host-specific physics boost - scaled back to match the enhanced client
            if (Mathf.Abs(moveInput) > 0.1f && cachedSpeed < 20f)
            {
                // Apply a moderate force for host (reduced from previous fix to balance with client)
                Vector3 forceDirection = transform.forward * Mathf.Sign(moveInput);
                float forceMagnitude = Mathf.Abs(moveInput) * maxTorque * 0.3f; // Increased from 0.15f to 0.3f
                rb.AddForce(forceDirection * forceMagnitude, ForceMode.Acceleration);
                
                if (debugMode && Time.frameCount % 120 == 0)
                {
                    Debug.Log($"Host acceleration boost: {forceDirection * forceMagnitude}, speed: {velocity:F2}m/s");
                }
            }
        }
        else if (IsServer && !IsOwner)
        {
            // Server applies physics for non-owned vehicles
            ApplyServerPhysics();
        }

        // Always perform these for consistent behavior
        AnimateWheels();
        UpdateDriftEffects();
    }

    #endregion

    #region Input Handling

    private void OnHornPressed()
    {
        // Only play horn if not spectating
        if (!player.isSpectating)
        {
            // Wwise audio - commented out for now since Wwise namespace isn't properly setup
            // AK.Wwise.SoundEngine.PostEvent("HogHorn", gameObject);
        }
    }

    private void OnJumpPressed()
    {
        if (!IsOwner) return;

        // Check if player can jump and is not spectating
        bool canPerformJump = canMove && canJump && !jumpOnCooldown &&
                             !player.isSpectating;

        if (canPerformJump)
        {
            // Request jump on the server
            JumpServerRpc();
            
            // Start cooldown locally for immediate feedback
            jumpOnCooldown = true;
            jumpCooldownRemaining = jumpCooldown;
        }
    }

    private void ClientMove()
    {
        if (player == null || !IsOwner || IsServer) return;
        
        // Create input data with current inputs
        ClientInput input = new ClientInput
        {
            clientId = NetworkManager.Singleton.LocalClientId,
            moveInput = moveInput,
            steeringInput = steeringInput,
            brakeInput = brakeInput > 0 ? 1 : 0
        };
        
        if (debugMode && Time.frameCount % 300 == 0)
        {
            Debug.Log($"Client sending input: moveInput={input.moveInput}, steeringInput={input.steeringInput}, brake={input.brakeInput}, speed={velocity:F2}m/s");
        }
        
        // Apply input directly to our local wheels for responsive feedback
        // This will be corrected by the server later if needed
        
        // Apply stronger motor torque for client responsiveness
        // Increased from 1.0f to 2.0f for better client acceleration
        float clientTorque = moveInput * maxTorque * 2.0f;
        foreach (WheelCollider wheel in drivenWheels)
        {
            wheel.motorTorque = clientTorque;
        }
        
        // Apply responsive steering for client-side control
        float clientSteeringAngle = steeringInput * maxSteeringAngle * 1.1f;
        foreach (WheelCollider wheel in steeringWheels)
        {
            wheel.steerAngle = clientSteeringAngle;
        }
        
        // Apply braking if needed
        float clientBrakeTorque = brakeInput > 0 ? brakeTorque : 0f;
        foreach (WheelCollider wheel in allWheels)
        {
            wheel.brakeTorque = clientBrakeTorque;
        }
        
        // Send input to server
        SendInputServerRpc(input);
    }

    private ClientInput CollectPlayerInput()
    {
        return new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = GetMoveInput(),
            brakeInput = GetBrakeInput(),
            steeringInput = ApplyInputSmoothing(GetSteeringInput())
        };
    }
    
    private float GetMoveInput()
    {
        float move = 0;
        
        // Keyboard input
        if (Input.GetKey(KeyCode.W))
        {
            move = 1f;
        }
        if (Input.GetKey(KeyCode.S))
        {
            move = -1f;
        }
        
        return move;
    }
    
    private float GetBrakeInput()
    {
        return Input.GetKey(KeyCode.B) ? 1f : 0f;
    }
    
    private float GetSteeringInput()
    {
        float steer = 0;
        
        // Keyboard input
        if (Input.GetKey(KeyCode.A))
        {
            steer = -1f;
        }
        if (Input.GetKey(KeyCode.D))
        {
            steer = 1f;
        }
        
        return steer;
    }

    private float ApplyInputSmoothing(float rawSteeringInput)
    {
        // Add to input buffer for smoothing
        _recentSteeringInputs.Enqueue(rawSteeringInput);
        if (_recentSteeringInputs.Count > steeringBufferSize)
            _recentSteeringInputs.Dequeue();

        // Convert queue to list for weighted processing
        _steeringInputsList.Clear();
        _steeringInputsList.AddRange(_recentSteeringInputs);

        // Return weighted average if we have inputs
        if (_steeringInputsList.Count > 0)
        {
            return CalculateWeightedAverage(_steeringInputsList, weightingFactor);
        }

        return rawSteeringInput;
    }

    private float CalculateWeightedAverage(List<float> values, float weightingFactor)
    {
        if (values.Count == 0) return 0f;
        if (values.Count == 1) return values[0];

        float total = 0f;
        float weightSum = 0f;

        for (int i = 0; i < values.Count; i++)
        {
            // Position from most recent (0) to oldest (count-1)
            int position = values.Count - 1 - i;

            // Calculate weight based on weighting method
            float weight = 1.0f;

            switch (inputWeightingMethod)
            {
                case WeightingMethod.Exponential:
                    // Exponential drop-off: weight = e^(-factor*position)
                    weight = Mathf.Exp(-weightingFactor * position);
                    break;

                case WeightingMethod.Logarithmic:
                    // Logarithmic drop-off: weight = 1 / (1 + factor*ln(position+1))
                    weight = 1.0f / (1.0f + weightingFactor * Mathf.Log(position + 1));
                    break;

                case WeightingMethod.Linear:
                    // Linear drop-off: weight = 1 - (position * factor / count)
                    weight = Mathf.Max(0, 1.0f - (position * weightingFactor / values.Count));
                    break;
            }

            float angle = values[i];
            total += angle * weight;
            weightSum += weight;
        }

        return total / weightSum;
    }

    private void HandleOwnerInput()
    {
        if (!IsOwner) return;

        // Get movement input using our local methods
        moveInput = GetMoveInput();
        brakeInput = GetBrakeInput();
        steeringInput = GetSteeringInput();

        // Check for jump input (previously handled by event system)
        if (Input.GetKeyDown(KeyCode.Space) && !jumpOnCooldown && canJump && canMove)
        {
            OnJumpPressed();
        }

        // Check for horn input
        if (Input.GetKeyDown(KeyCode.H))
        {
            OnHornPressed();
        }
        
        // Apply direct wheel control for both server and client
        float directSteeringAngle = steeringInput * maxSteeringAngle;
        
        // Apply steering smoothly
        wheelColliders[0].steerAngle = Mathf.Lerp(wheelColliders[0].steerAngle, directSteeringAngle, steeringSpeed * Time.deltaTime * 10f);
        wheelColliders[1].steerAngle = Mathf.Lerp(wheelColliders[1].steerAngle, directSteeringAngle, steeringSpeed * Time.deltaTime * 10f);
        wheelColliders[2].steerAngle = Mathf.Lerp(wheelColliders[2].steerAngle, -directSteeringAngle * rearSteeringAmount, steeringSpeed * Time.deltaTime * 10f);
        wheelColliders[3].steerAngle = Mathf.Lerp(wheelColliders[3].steerAngle, -directSteeringAngle * rearSteeringAmount, steeringSpeed * Time.deltaTime * 10f);
        
        // Apply motor torque directly for both server and client with REDUCED acceleration
        float torqueMultiplier = 0.8f; // Reduced from 1.5f to 0.8f
        float directMotorTorque = moveInput * maxTorque * torqueMultiplier;
        float directBrakeTorque = brakeInput > 0 ? brakeTorque : 0f;
        
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = directMotorTorque;
            wheelColliders[i].brakeTorque = directBrakeTorque;
        }
        
        // If we're the server, we need to update the current torque for physics
        if (IsServer)
        {
            currentTorque = directMotorTorque;
            
            // Add additional force for the host, but much less than before
            if (Mathf.Abs(moveInput) > 0.1f && cachedSpeed < 20f)
            {
                // Add minimal extra force - reduced from 0.4f to 0.1f
                rb.AddForce(transform.forward * moveInput * maxTorque * 0.1f, ForceMode.Acceleration);
            }
        }
    }

    #endregion

    #region Networking

    [ServerRpc(RequireOwnership = true)]
    private void SendInputServerRpc(ClientInput input)
    {
        // Calculate steering angle server-side to ensure consistency
        SteeringData steeringData = CalculateSteeringFromInput(input.steeringInput, input.moveInput);
        
        // Debug input for diagnostics only (no validation that affects gameplay)
        if (debugMode && Time.frameCount % 120 == 0)
        {
            Debug.Log($"Server processing input: moveInput={input.moveInput}, steeringInput={input.steeringInput}, speed={velocity:F2}m/s");
        }

        // Apply motor torque with faster response time for better client handling
        // Increased torque multiplier from 0.9f to 1.5f for more power
        float motorTorque = input.moveInput * maxTorque * 1.5f;
        
        // Apply torque change with faster response (increased from 7f to 15f)
        // This ensures clients feel more responsive controls
        currentTorque = Mathf.MoveTowards(currentTorque, motorTorque, Time.deltaTime * 15f * accelerationFactor);
        
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = currentTorque;
            
            // Apply brake torque if requested
            if (input.brakeInput > 0)
            {
                wheelColliders[i].brakeTorque = input.brakeInput * brakeTorque;
            }
            else
            {
                wheelColliders[i].brakeTorque = 0;
            }
        }

        // Apply steering
        ApplySteering(steeringData);

        // Set drift state
        isDrifting.Value = Mathf.Abs(localVelocityX) > 0.25f;
    }

    private void SendStateToServer()
    {
        if (!IsOwner) return;

        // Create current state snapshot
        var snapshot = new StateSnapshot
        {
            position = rb.position,
            rotation = rb.rotation,
            velocity = rb.linearVelocity,
            angularVelocity = rb.angularVelocity,
            steeringAngle = wheelColliders[0].steerAngle,
            motorTorque = wheelColliders[0].motorTorque,
            updateId = (uint)Time.frameCount
        };

        // Update network variable
        vehicleState.Value = snapshot;
        
        // Skip the additional validation RPC to reduce overhead
        // SendStateForValidationServerRpc(snapshot);  // Comment out to improve performance
    }

    [ClientRpc]
    private void BroadcastServerStateClientRpc(StateSnapshot state)
    {
        // Skip for server and owner - they already have the latest state
        if (IsServer || IsOwner) return;

        // For other clients, update visual targets for smoothing
        visualPositionTarget = state.position;
        visualRotationTarget = state.rotation;
        visualVelocityTarget = state.velocity;
        needsSmoothing = true;
        
        // Set the steering angle for all wheels directly
        for (int i = 0; i < 2; i++)
        {
            wheelColliders[i].steerAngle = state.steeringAngle;
        }
        
        for (int i = 2; i < 4; i++)
        {
            wheelColliders[i].steerAngle = -state.steeringAngle * rearSteeringAmount;
        }
    }

    [ServerRpc]
    private void JumpServerRpc()
    {
        // Check if jump is allowed
        if (!canJump || !jumpReady.Value) return;

        // Set network state
        isJumping.Value = true;
        jumpReady.Value = false;

        // Get reference to the rigidbody
        if (rb != null)
        {
            // Store current horizontal velocity
            Vector3 currentVelocity = rb.linearVelocity;
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

            // Apply a sharper upward impulse for faster rise
            float upwardVelocity = jumpForce * 1.2f;

            // Combine horizontal momentum with new vertical impulse
            Vector3 newVelocity = horizontalVelocity * 1.1f + Vector3.up * upwardVelocity;
            rb.linearVelocity = newVelocity;

            // Add a bit more forward boost in the car's facing direction
            rb.AddForce(transform.forward * (jumpForce * 5f), ForceMode.Impulse);

            // Temporarily increase gravity for faster fall
            StartCoroutine(TemporarilyIncreaseGravity(rb));
        }

        // Execute visual effects on all clients
        JumpEffectsClientRpc();

        // Start cooldown
        StartCoroutine(JumpCooldownServer());
    }

    private IEnumerator TemporarilyIncreaseGravity(Rigidbody rb)
    {
        // Store original gravity
        float gravity = Physics.gravity.y;

        // Wait for the rise phase
        yield return new WaitForSeconds(0.3f);

        // Apply stronger gravity for faster fall
        Physics.gravity = new Vector3(0, gravity * 1.5f, 0);

        // Wait for the fall phase
        yield return new WaitForSeconds(0.5f);

        // Restore original gravity
        Physics.gravity = new Vector3(0, gravity, 0);
    }

    [ClientRpc]
    private void JumpEffectsClientRpc()
    {
        // Play particle effects
        foreach (var ps in jumpParticleSystems)
        {
            if (ps != null) ps.Play();
        }

        if (IsOwner)
        {
            jumpOnCooldown = true;
            jumpCooldownRemaining = jumpCooldown;

            // Wwise audio - commented out for now since Wwise namespace isn't properly setup
            // AK.Wwise.SoundEngine.PostEvent("HogJump", gameObject);
        }
    }
    
    private IEnumerator JumpCooldownServer()
    {
        yield return new WaitForSeconds(jumpCooldown);
        jumpReady.Value = true;
        isJumping.Value = false;
    }

    #endregion

    #region Visual Effects

    private void AnimateWheels()
    {
        for (int i = 0; i < 4; i++)
        {
            // Get wheel pose
            Vector3 position;
            Quaternion rotation;
            wheelColliders[i].GetWorldPose(out position, out rotation);

            // Apply to visual transform
            wheelTransforms[i].position = position;
            wheelTransforms[i].rotation = rotation;
        }
    }

    private void UpdateDriftEffects()
    {
        bool isDrifting = Mathf.Abs(localVelocityX) > 0.4f && cachedSpeed > 3f;
        bool isGrounded = wheelColliders[2].isGrounded && wheelColliders[3].isGrounded;

        // Left wheel effects
        if (isDrifting && isGrounded && canMove)
        {
            if (!wheelParticleSystems[0].isPlaying)
            {
                wheelParticleSystems[0].Play();
            }
            if (!tireSkids[0].emitting)
            {
                tireSkids[0].emitting = true;
            }
        }
        else
        {
            if (wheelParticleSystems[0].isPlaying)
            {
                wheelParticleSystems[0].Stop();
            }
            tireSkids[0].emitting = false;
        }

        // Right wheel effects
        if (isDrifting && isGrounded && canMove)
        {
            if (!wheelParticleSystems[1].isPlaying)
            {
                wheelParticleSystems[1].Play();
            }
            if (!tireSkids[1].emitting)
            {
                tireSkids[1].emitting = true;
            }
        }
        else
        {
            if (wheelParticleSystems[1].isPlaying)
            {
                wheelParticleSystems[1].Stop();
            }
            tireSkids[1].emitting = false;
        }
    }

    private IEnumerator ResetAfterExplosion(GameObject explosionInstance)
    {
        yield return new WaitForSeconds(3f);
        
        if (explosionInstance != null)
        {
            Destroy(explosionInstance);
        }
        
        canMove = true;
    }

    #endregion

    #region Collision Handling

    private void OnCollisionEnter(Collision collision)
    {
        // Only process collisions on the server
        if (!IsServer) return;

        // Get the player information directly from the root object
        var myPlayer = transform.root.gameObject.GetComponent<Player>();
        var otherPlayer = collision.transform.root.gameObject.GetComponent<Player>();

        if (myPlayer == null || otherPlayer == null) return;

        Debug.Log($"[COLLISION] Player {myPlayer.clientId} collided with Player {otherPlayer.clientId}");

        // Get the colliding player's name from ConnectionManager
        if (ConnectionManager.Instance.TryGetPlayerData(otherPlayer.clientId, out PlayerData collidingPlayerData))
        {
            // Record the collision in the tracker
            var collisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
            if (collisionTracker != null)
            {
                collisionTracker.RecordCollision(myPlayer.clientId, otherPlayer.clientId, collidingPlayerData.username);
            }
        }

    }

    private void OnTriggerEnter(Collider other)
    {
        // Only process triggers on the server
        if (!IsServer) return;

        // Get the player information directly from the root object
        var myPlayer = transform.root.gameObject.GetComponent<Player>();
        var otherPlayer = other.transform.root.gameObject.GetComponent<Player>();

        if (myPlayer == null || otherPlayer == null) return;

        Debug.Log($"[TRIGGER] Player {myPlayer.clientId} triggered with Player {otherPlayer.clientId}");

        // Get the colliding player's name from ConnectionManager
        if (ConnectionManager.Instance.TryGetPlayerData(otherPlayer.clientId, out PlayerData collidingPlayerData))
        {
            // Record the collision in the tracker
            var collisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
            if (collisionTracker != null)
            {
                collisionTracker.RecordCollision(myPlayer.clientId, otherPlayer.clientId, collidingPlayerData.username);
            }
        }
    }

    #endregion

    #region Initialization

    private void InitializeOwnerVehicle()
    {
        // Configure physics
        rb.isKinematic = false;
        
        // Update transform and rigidbody positions to match
        transform.position = rb.position;
        transform.rotation = rb.rotation;
        
        // Mark as initialized
        hasReceivedInitialSync = true;

        if (debugMode)
        {
            Debug.Log($"[Owner] Vehicle initialized at {rb.position}");
        }
    }

    private void InitializeServerControlledVehicle()
    {
        // Get the Player component to access spawn point
        // Get player data to use spawn point
        if (ConnectionManager.Instance != null && ConnectionManager.Instance.TryGetPlayerData(player.clientId, out PlayerData playerData))
        {
            Vector3 initialPosition = playerData.spawnPoint;
            Quaternion initialRotation = Quaternion.LookRotation(
                SpawnPointManager.Instance.transform.position - playerData.spawnPoint);

            // Set transform and physics state
            transform.position = initialPosition;
            transform.rotation = initialRotation;
            rb.position = initialPosition;
            rb.rotation = initialRotation;

            if (debugMode)
            {
                Debug.Log($"[Server] Setting non-owner vehicle position to spawn point: {initialPosition}");
            }
        }

        // Configure physics
        rb.isKinematic = false;
        
        // Mark as initialized
        hasReceivedInitialSync = true;
    }
    
    private void InitializeClientVehicle()
    {
        // Basic initialization for remote vehicles
        rb.isKinematic = false;
        
        // Ensure physics properties are consistent with the feature/jump branch
        rb.mass = 1200f;
        rb.linearDamping = 0.1f;  // FIXED: Using rb.drag instead of linearDamping
        rb.angularDamping = 0.5f; // FIXED: Using rb.angularDrag instead of angularDamping
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        // Initialize the vehicle's physics
        InitializeVehiclePhysics();
        
        // Initialize visual targets for interpolation
        visualPositionTarget = transform.position;
        visualRotationTarget = transform.rotation;
        visualVelocityTarget = Vector3.zero;
        
        // Mark as not ready yet - will be marked ready in DelayedPhysicsEnable
        hasReceivedInitialSync = false;
    }

    private void EnableWheelColliders()
    {
        foreach (var wheel in wheelColliders)
        {
            if (wheel != null)
            {
                wheel.enabled = true;
            }
        }
    }

    #endregion

    #region Network State Management

    private void OnVehicleStateChanged(StateSnapshot previousValue, StateSnapshot newValue)
    {
        // Only non-owners need to react to state changes
        if (IsOwner) return;

        if (IsServer)
        {
            // Server validates owner's state
            ValidateOwnerState(newValue);
        }
        else
        {
            // Remote client applies state for visual representation
            ApplyRemoteState(newValue);
        }
    }

    private void ValidateOwnerState(StateSnapshot ownerState)
    {
        if (!IsServer) return;

        // Simple validation - check if position is within valid range
        Vector3 currentPos = rb.position;
        float distance = Vector3.Distance(currentPos, ownerState.position);

        if (distance > serverValidationThreshold)
        {
            if (debugMode)
            {
                Debug.LogWarning($"[Server] Detected invalid position from owner. Distance: {distance}m");
            }

            // Option 1: Correct the client (stricter approach)
            // CorrectClientPositionClientRpc(currentPos);

            // Option 2: Trust the client but log the issue (more lenient)
            // Just accept the position but log it
            rb.position = ownerState.position;
            rb.rotation = ownerState.rotation;
            rb.linearVelocity = ownerState.velocity;
            rb.angularVelocity = ownerState.angularVelocity;

            // Broadcast to other clients
            BroadcastServerStateClientRpc(ownerState);
        }
        else
        {
            // State is valid, apply it on the server
            rb.position = ownerState.position;
            rb.rotation = ownerState.rotation;
            rb.linearVelocity = ownerState.velocity;
            rb.angularVelocity = ownerState.angularVelocity;

            // Broadcast to other clients (less frequently)
            _stateUpdateTimer += Time.deltaTime;
            if (_stateUpdateTimer >= stateUpdateInterval)
            {
                _stateUpdateTimer = 0f;
                BroadcastServerStateClientRpc(ownerState);
            }
        }
    }

    private void ApplyRemoteState(StateSnapshot state)
    {
        // For remote client representation of other players' vehicles
        visualPositionTarget = state.position;
        visualRotationTarget = state.rotation;
        visualVelocityTarget = state.velocity;
        needsSmoothing = true;
    }

    private void SmoothRemoteVehicleVisuals()
    {
        if (IsOwner || IsServer || !needsSmoothing) return;

        // Smoothly update visuals for remote client
        transform.position = Vector3.Lerp(transform.position, visualPositionTarget,
                                        Time.deltaTime * visualSmoothingSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, visualRotationTarget,
                                           Time.deltaTime * visualSmoothingSpeed);

        // Apply visual wheel effects
        SteeringData steeringData = new SteeringData
        {
            frontSteeringAngle = vehicleState.Value.steeringAngle,
            rearSteeringAngle = vehicleState.Value.steeringAngle * -rearSteeringAmount,
            isReversing = false
        };
        ApplySteering(steeringData);

        // Continue smoothing while the difference is significant
        needsSmoothing =
            Vector3.Distance(transform.position, visualPositionTarget) > 0.01f ||
            Quaternion.Angle(transform.rotation, visualRotationTarget) > 0.1f;
    }

    private void ApplyServerPhysics()
    {
        // Server applies physics for non-owned vehicles
        // This is used for server-side validation and for AI vehicles
        if (!IsOwner && IsServer)
        {
            // Apply direct steering angle from network state (not camera-based)
            SteeringData steeringData = new SteeringData
            {
                frontSteeringAngle = vehicleState.Value.steeringAngle,
                rearSteeringAngle = vehicleState.Value.steeringAngle * -rearSteeringAmount,
                isReversing = false
            };
            ApplySteering(steeringData);
            ApplyMotorTorque(vehicleState.Value.motorTorque, 0f);
        }
    }
    
    #endregion
    
    #region Network RPCs

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitialStateServerRpc(ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;
        
        // Client requests initial state from server
        if (debugMode)
        {
            Debug.Log($"[Server] Client {clientId} requested initial state");
        }

        // Send the current state to the requesting client
        SendInitialStateClientRpc(vehicleState.Value, 
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } });
    }

    [ClientRpc]
    private void SendInitialStateClientRpc(StateSnapshot state, ClientRpcParams clientRpcParams = default)
    {
        // Skip if we're already initialized
        if (hasReceivedInitialSync) return;

        // Apply initial state
        rb.position = state.position;
        rb.rotation = state.rotation;
        rb.linearVelocity = state.velocity;
        rb.angularVelocity = state.angularVelocity;

        // Set visual targets
        visualPositionTarget = state.position;
        visualRotationTarget = state.rotation;
        visualVelocityTarget = state.velocity;

        // Enable physics
        rb.isKinematic = false;
        EnableWheelColliders();

        // Mark as initialized
        hasReceivedInitialSync = true;

        if (debugMode)
        {
            Debug.Log($"[Client] Received initial state: {state.position}");
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void SendStateForValidationServerRpc(StateSnapshot clientState)
    {
        // Skip validation for better performance
        // This was causing client performance issues
        
        // Just broadcast state directly without checks
        BroadcastServerStateClientRpc(clientState);
    }

    [ClientRpc]
    private void CorrectClientPositionClientRpc(Vector3 correctedPosition)
    {
        // Only the owner responds to correction
        if (!IsOwner) return;

        if (debugMode)
        {
            Debug.LogWarning($"[Client] Position corrected by server. New position: {correctedPosition}");
        }

        // Apply correction
        rb.position = correctedPosition;

        // Send updated state
        SendStateToServer();
    }
    
    [ServerRpc(RequireOwnership = true)]
    public void RequestRespawnServerRpc()
    {
        // Get player data from connection manager
        if (ConnectionManager.Instance.TryGetPlayerData(player.clientId, out PlayerData playerData))
        {
            // Set respawn position and rotation
            Vector3 respawnPosition = playerData.spawnPoint;
            Quaternion respawnRotation = Quaternion.LookRotation(
                SpawnPointManager.Instance.transform.position - playerData.spawnPoint);

            // Update player state
            playerData.state = PlayerState.Alive;
            ConnectionManager.Instance.UpdatePlayerDataClientRpc(player.clientId, playerData);

            // Execute respawn for everyone
            ExecuteRespawnClientRpc(respawnPosition, respawnRotation);
        }
    }

    [ClientRpc]
    public void ExecuteRespawnClientRpc(Vector3 respawnPosition, Quaternion respawnRotation)
    {
        Debug.Log($"Executing respawn on client at position {respawnPosition}");

        // Reset physics state
        rb.isKinematic = true;
        rb.position = respawnPosition;
        rb.rotation = respawnRotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Update transform position
        transform.position = respawnPosition;
        transform.rotation = respawnRotation;

        // Reset driving state
        currentTorque = 0f;

        // CRITICAL: Re-enable movement after respawn
        canMove = true;
        isDrifting.Value = false;

        // Update visual targets for non-owners
        if (!IsOwner)
        {
            visualPositionTarget = respawnPosition;
            visualRotationTarget = respawnRotation;
            visualVelocityTarget = Vector3.zero;
        }

        // Update state snapshot for owner
        if (IsOwner)
        {
            // Create and send updated state
            var snapshot = new StateSnapshot
            {
                position = respawnPosition,
                rotation = respawnRotation,
                velocity = Vector3.zero,
                angularVelocity = Vector3.zero,
                steeringAngle = 0f,
                motorTorque = 0f,
                updateId = (uint)Time.frameCount
            };

            // Set state and send to server
            vehicleState.Value = snapshot;
        }

        // Re-enable physics after a short delay
        StartCoroutine(EnablePhysicsAfterRespawn());
    }

    private IEnumerator EnablePhysicsAfterRespawn()
    {
        // Short delay to ensure everything is in place
        yield return new WaitForSeconds(0.1f);

        // Re-enable physics
        rb.isKinematic = false;

        // Make sure wheel colliders are enabled
        EnableWheelColliders();

        if (debugMode)
        {
            Debug.Log($"Physics re-enabled after respawn at {rb.position}");
        }
    }

    [ServerRpc(RequireOwnership = true)]
    private void UpdateDriftingServerRpc(bool isDriftingValue)
    {
        // Update the drift status on the server
        isDrifting.Value = isDriftingValue;
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        // Disable movement temporarily
        canMove = false;

        // Create explosion effect if the prefab is set
        if (Explosion != null)
        {
            GameObject explosionInstance = Instantiate(Explosion, transform.position + Vector3.up * 1.0f, Quaternion.identity);
            StartCoroutine(ResetAfterExplosion(explosionInstance));
        }

        // Disable wheel effects
        foreach (var ps in wheelParticleSystems)
        {
            if (ps != null && ps.isPlaying)
            {
                ps.Stop();
            }
        }

        foreach (var skid in tireSkids)
        {
            if (skid != null)
            {
                skid.emitting = false;
            }
        }

        // Play explosion sound if we're the owner
        if (IsOwner)
        {
            // Audio is handled via the explosion prefab
        }
    }

    #endregion

    private void SetupComponents()
    {
        // Get player component
        player = transform.root.GetComponent<Player>();
        
        if (player == null)
        {
            player = GetComponent<Player>();
        }

        // Set up basic physics properties
        rb.centerOfMass = centerOfMass;
        
        // We're using direct input polling now, no events to subscribe to

        // Register for state change callback
        vehicleState.OnValueChanged += OnVehicleStateChanged;
    }

    private void SetupWheelMeshes()
    {
        // Initialize wheel meshes if they're assigned
        if (wheelTransforms != null && wheelTransforms.Length > 0 && wheelColliders != null && wheelColliders.Length > 0)
        {
            // Ensure counts match
            int meshCount = Mathf.Min(wheelTransforms.Length, wheelColliders.Length);
            
            for (int i = 0; i < meshCount; i++)
            {
                if (wheelTransforms[i] != null && wheelColliders[i] != null)
                {
                    // Initialize wheel position and rotation
                    UpdateWheelMesh(wheelColliders[i], wheelTransforms[i]);
                }
            }
        }
    }

    private void SetupEffects()
    {
        // Find visual effects if not assigned
        if (wheelParticleSystems == null || wheelParticleSystems.Length < 2)
        {
            Debug.LogWarning("Wheel particle systems not assigned in HogController");
            return;
        }
        
        // Find audio sources if not assigned
        if (engineAudioSource == null)
        {
            AudioSource[] sources = GetComponentsInChildren<AudioSource>();
            if (sources.Length > 0)
            {
                engineAudioSource = sources[0];
            }
        }
    }

    private void EnableParticleEffects(bool enable)
    {
        // Enable/disable wheel particle effects
        if (wheelParticleSystems != null && wheelParticleSystems.Length > 0)
        {
            foreach (var ps in wheelParticleSystems)
            {
                if (ps != null)
                {
                    if (enable)
                    {
                        ps.Play();
                    }
                    else
                    {
                        ps.Stop();
                    }
                }
            }
        }

        // Enable/disable exhaust particles
        if (exhaustParticles != null)
        {
            if (enable)
            {
                exhaustParticles.Play();
            }
            else
            {
                exhaustParticles.Stop();
            }
        }
    }

    private void UpdateWheelMesh(WheelCollider collider, Transform wheelTransform)
    {
        if (collider == null || wheelTransform == null) return;
        
        // Get wheel pose
        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);
        
        // Apply to visual wheel
        wheelTransform.position = position;
        wheelTransform.rotation = rotation;
    }

    // Helper property for checking state authority
    public bool HasStateAuthority => IsServer || IsOwner;

    private void InitializeVehiclePhysics()
    {
        // Set up basic physics properties
        rb.centerOfMass = centerOfMass;
        rb.angularDamping = 0.5f;  // Increased from 0.1f to reduce spinning
        rb.mass = 1200f;        // Set explicit mass for consistent physics
        rb.linearDamping = 0.1f;         // Add slight drag
        rb.interpolation = RigidbodyInterpolation.Interpolate; // Smoother movement
        
        // Initialize wheel colliders
        if (wheelColliders != null && wheelColliders.Length > 0)
        {
            foreach (var collider in wheelColliders)
            {
                if (collider != null)
                {
                    // Minimal wheel setup - use Unity's defaults for suspension
                    collider.forceAppPointDistance = 0;
                    
                    // Increase wheel friction for better traction
                    WheelFrictionCurve fwdFriction = collider.forwardFriction;
                    fwdFriction.stiffness = 2.0f;  // Increased traction
                    collider.forwardFriction = fwdFriction;
                    
                    WheelFrictionCurve sideFriction = collider.sidewaysFriction;
                    sideFriction.stiffness = 2.0f;  // Increased traction
                    collider.sidewaysFriction = sideFriction;
                }
            }
        }
        
        // Set initial vehicle parameters
        engineRunning = false;
        targetRPM = minRPM;
    }

    private void CachePhysicsValues()
    {
        cachedVelocity = rb.linearVelocity;
        cachedSpeed = cachedVelocity.magnitude;
        
        // Cache wheel RPMs for engine sound
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            cachedWheelRpms[i] = wheelColliders[i].rpm;
        }
        
        // Calculate forward velocity using transform.forward dotted with velocity
        cachedForwardVelocity = Vector3.Dot(transform.forward, cachedVelocity);
        
        // Calculate lateral velocity (sideways)
        localVelocityX = Vector3.Dot(transform.right, cachedVelocity);
        
        // Store velocity for debugging
        velocity = cachedForwardVelocity;
        frontLeftRpm = cachedWheelRpms[0];
    }

    [ClientRpc]
    public void SetCanMoveClientRpc(bool canMoveValue)
    {
        canMove = canMoveValue;
    }

    private SteeringData CalculateSteeringFromInput(float steeringInput, float moveInput)
    {
        SteeringData data = new SteeringData();
        
        // Calculate if we're in reverse
        data.isReversing = moveInput < 0 && cachedForwardVelocity < MIN_VELOCITY_FOR_REVERSE;
        
        // Calculate front steering angle based on current speed
        float speedFactor = Mathf.Clamp01(1.0f - (cachedSpeed / maxSpeedForSteering));
        float responseRate = Mathf.Lerp(minSteeringResponse, maxSteeringResponse, speedFactor);
        
        // Adjust response based on speed
        data.frontSteeringAngle = steeringInput * maxSteeringAngle * responseRate;
        
        // Calculate rear steering for all-wheel steering
        data.rearSteeringAngle = -data.frontSteeringAngle * rearSteeringAmount;
        
        return data;
    }
    
    private void ApplySteering(SteeringData steeringData)
    {
        // Apply steering to front wheels
        wheelColliders[0].steerAngle = Mathf.Lerp(wheelColliders[0].steerAngle, 
                                                steeringData.frontSteeringAngle, 
                                                steeringSpeed * Time.fixedDeltaTime * 10f);
        wheelColliders[1].steerAngle = Mathf.Lerp(wheelColliders[1].steerAngle, 
                                                steeringData.frontSteeringAngle, 
                                                steeringSpeed * Time.fixedDeltaTime * 10f);
        
        // Apply counter-steering to rear wheels for better turning
        wheelColliders[2].steerAngle = Mathf.Lerp(wheelColliders[2].steerAngle, 
                                                steeringData.rearSteeringAngle, 
                                                steeringSpeed * Time.fixedDeltaTime * 10f);
        wheelColliders[3].steerAngle = Mathf.Lerp(wheelColliders[3].steerAngle, 
                                                steeringData.rearSteeringAngle, 
                                                steeringSpeed * Time.fixedDeltaTime * 10f);
    }
    
    private void ApplyMotorTorque(float motorTorque, float brakeTorque)
    {
        // Apply motor torque to all wheels
        foreach (var wheel in wheelColliders)
        {
            wheel.motorTorque = motorTorque;
            wheel.brakeTorque = brakeTorque;
        }
    }
}