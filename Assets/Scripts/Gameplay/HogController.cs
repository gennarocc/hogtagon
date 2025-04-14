using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using Hogtagon.Core.Infrastructure;
using UnityEngine.InputSystem;

// TODO: After opening Unity, regenerate DefaultControls.cs file by saving the DefaultControls.inputactions asset in the editor
// This will ensure the new Steer action is properly included in the generated code

public class HogController : NetworkBehaviour, DefaultControls.IGameplayActions
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
    [SerializeField, Range(0.5f, 2.0f)] private float hostTorqueMultiplier = 1.5f; // Increased from hostAccelerationFactor for better balance
    [SerializeField, Range(1.0f, 10.0f)] private float clientTorqueMultiplier = 6.0f; // Explicit client multiplier
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
    [SerializeField] private float serverValidationThreshold = 10.0f;  // Increased from 5.0f to 10.0f
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

    [Header("Physics Settings")]
    [SerializeField] private float baseDrag = 0.05f;
    [SerializeField] private float baseAngularDrag = 0.05f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundClearance = 0.5f;
    [SerializeField] private float groundCheckInterval = 0.5f;
    private float lastGroundCheckTime = 0f;

    #endregion

    #region Private Fields

    // Constants
    private const float MIN_VELOCITY_FOR_REVERSE = -0.5f;

    // Input reference - properly using input action map now
    private DefaultControls inputControls;
    private float throttleValue;
    private float brakeValue;
    private float steerValue;
    
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

    // Network variables for state that still needs synchronization
    private NetworkVariable<bool> isDrifting;
    private NetworkVariable<bool> isJumping;
    private NetworkVariable<bool> jumpReady;

    // Physics and control variables
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private bool jumpOnCooldown = false;
    private float jumpCooldownRemaining = 0f;
    private bool hasReceivedInitialSync = false;

    // Cached physics values
    private Vector3 cachedVelocity;
    private float cachedSpeed;
    private float[] cachedWheelRpms = new float[4];
    private float cachedForwardVelocity;

    // Wheel friction data
    private WheelFrictionCurve[] wheelFrictions = new WheelFrictionCurve[4];
    private float[] originalExtremumSlip = new float[4];

    // Private interpolation variables
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetVelocity;
    private float interpSpeed = 30f; // Increased from 10f for smoother transitions

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
            
        // Initialize steering input buffer
        _recentSteeringInputs = new Queue<float>(steeringBufferSize);
        
        // Initialize input actions
        inputControls = new DefaultControls();
        inputControls.Gameplay.SetCallbacks(this);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        Debug.Log($"OnNetworkSpawn - IsOwner: {IsOwner}, IsServer: {IsServer}, HasStateAuthority: {HasStateAuthority}");
        
        // Initialize the vehicle's physics components
        InitializeVehicle();
        
        // Set initial kinematic state - start kinematic to prevent bouncing
        if (rb)
        {
            rb.isKinematic = true;
            
            // We'll disable kinematic after a short delay to let the network transform sync
            StartCoroutine(EnablePhysicsWithDelay());
        }
        
        // Register owner input if we're the owner
        if (IsOwner)
        {
            Debug.Log("Input actions initialized for owner");
            RegisterInputEvents();
            
            // Ensure ground check runs properly on owner spawn
            lastGroundCheckTime = Time.time - groundCheckInterval - 1;
        }
        
        // Mark that sync has been initialized
        hasReceivedInitialSync = true;
    }
    
    private IEnumerator EnablePhysicsWithDelay()
    {
        // Wait a bit for everything to settle
        yield return new WaitForSeconds(0.5f);
        
        // Check if we need to adjust to ground
        Vector3 startPosition = transform.position;
        Debug.Log($"Start - Owner vehicle at {startPosition}, IsKinematic: {rb.isKinematic}");
        
        // Raycast to find ground
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 2, Vector3.down, out hit, 10f, groundLayer))
        {
            // Adjust to ground height with some offset to prevent clipping
            Vector3 groundedPosition = new Vector3(transform.position.x, hit.point.y + groundClearance, transform.position.z);
            transform.position = groundedPosition;
            rb.position = groundedPosition;
            Debug.Log($"Adjusted car position to ground at {groundedPosition}, ground hit at {hit.point}");
        }
        
        // Enable physics with a second delay to ensure proper ground placement
        yield return new WaitForSeconds(0.2f);
        
        // For clients, also wait for initial network transform to arrive
        if (!IsServer)
        {
            yield return new WaitForSeconds(0.3f);
        }
        
        // CRITICAL: Set isKinematic to false BEFORE setting any physics properties
        rb.isKinematic = false;
        
        // Wait another frame to ensure kinematic change is processed
        yield return null;
        
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        
        // Set the position one more time to ensure we're on the ground
        Vector3 finalPosition = transform.position;
        Debug.Log($"DelayedPhysicsEnable - Position: {finalPosition}, IsKinematic: {rb.isKinematic}");
        
        // Apply initial drag (fix property names)
        rb.linearDamping = baseDrag;
        rb.angularDamping = baseAngularDrag;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Disable input events
        if (IsOwner && inputControls != null)
        {
            inputControls.Gameplay.Disable();
            Debug.Log("Disabled input controls on despawn");
        }

        // Only the server or owner should play the engine off sound
        if (IsOwner || IsServer)
        {
            // Wwise audio - commented out for now since Wwise namespace isn't properly setup
            // AK.Wwise.SoundEngine.PostEvent("EngineOff", gameObject);
        }
    }

    private void Update()
    {
        // Skip if we're not properly initialized
        if (rb == null || !hasReceivedInitialSync)
            return;

        // Cache local velocity for effects and handling
        if (rb != null)
        {
            localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;
        }
        
        // For non-owner clients, smoothly interpolate to target position/rotation
        if (!IsOwner && !IsServer)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * interpSpeed);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * interpSpeed);
            rb.position = transform.position;
            rb.rotation = transform.rotation;
            
            // SAFETY: Only apply velocity if not kinematic
            if (CanApplyVelocity())
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, Time.deltaTime * interpSpeed);
            }
        }
        
        // Update wheel meshes for animation
        AnimateWheels();
        
        // Owner-specific updates (handle input)
        if (IsOwner)
        {
            // Update jump cooldown for owner
            if (jumpOnCooldown)
            {
                if (jumpCooldownRemaining > 0f)
                {
                    jumpCooldownRemaining -= Time.deltaTime;
                }
                else
                {
                    jumpOnCooldown = false;
                }
            }
            
            // Process owner input (both client and host)
            HandleOwnerInput();
        }
        
        // Client-specific movement handling (when not host)
        if (IsOwner && !IsServer)
        {
            ClientMove();
        }
    }

    private void FixedUpdate()
    {
        // Cache frequently used physics values
        CachePhysicsValues();

        // All movement is now handled through wheel torque only
        // We rely on the ClientMove and HandleOwnerInput methods to handle wheel torque
        
        // For clients, sync position to server more frequently
        if (IsOwner && !IsServer && Time.frameCount % 5 == 0) // Every 5 frames instead of 60
        {
            SyncPositionToServerRpc(transform.position, transform.rotation, rb.linearVelocity);
        }
        
        // Always perform these for consistent behavior
        AnimateWheels();
        UpdateDriftEffects();
    }

    #endregion

    #region Input Handling

    // Handle input events from the input action map
    public void OnThrottle(InputAction.CallbackContext context)
    {
        if (context.performed || context.canceled)
        {
            // Handle different control types appropriately
            try 
            {
                // For gamepad triggers/sticks that return Vector2
                throttleValue = context.ReadValue<Vector2>().y;
            }
            catch (InvalidOperationException)
            {
                // For keyboard that returns float (0 or 1)
                throttleValue = context.ReadValue<float>();
            }
            
            if (IsOwner && !IsServer && Mathf.Abs(throttleValue) > 0.1f)
            {
                Debug.Log($"Throttle input: {throttleValue}");
            }
        }
    }

    public void OnBrake(InputAction.CallbackContext context)
    {
        if (context.performed || context.canceled)
        {
            // Handle different control types appropriately
            try 
            {
                // For gamepad triggers/sticks that return Vector2
                brakeValue = context.ReadValue<Vector2>().y;
            }
            catch (InvalidOperationException)
            {
                // For keyboard that returns float (0 or 1)
                brakeValue = context.ReadValue<float>();
            }
            
            if (IsOwner && !IsServer && brakeValue > 0.1f)
            {
                Debug.Log($"Brake input: {brakeValue}");
            }
        }
    }

    public void OnSteer(InputAction.CallbackContext context)
    {
        if (context.performed || context.canceled)
        {
            steerValue = context.ReadValue<float>();
            
            if (IsOwner && !IsServer && Mathf.Abs(steerValue) > 0.1f && Time.frameCount % 60 == 0)
            {
                Debug.Log($"Steer input: {steerValue}");
            }
        }
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            OnJumpPressed();
        }
    }

    public void OnHonk(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            OnHornPressed();
        }
    }

    // Required by interface but not used
    public void OnLook(InputAction.CallbackContext context) { }
    public void OnShowScoreboard(InputAction.CallbackContext context) { }
    public void OnPauseMenu(InputAction.CallbackContext context) { }

    // Updated to use input action values
    private float GetMoveInput()
    {
        // Return the throttle value from the input system
        return throttleValue;
    }

    // Updated to use input action values
    private float GetSteeringInput()
    {
        // Return the steering value from the input system
        return steerValue;
    }

    // Updated to use input action values
    private float GetBrakeInput()
    {
        return brakeValue;
    }

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
        
        // Debug log to verify inputs are captured correctly
        Debug.Log($"CLIENT trying to move: moveInput={moveInput}, steeringInput={steeringInput}, brakeInput={brakeInput}");
        
        // Apply client-side prediction (VERY SIMPLE VERSION)
        float clientTorque = moveInput * maxTorque * 15.0f;
        
        // Apply torque DIRECTLY to ALL wheels
        foreach (WheelCollider wheel in wheelColliders)
        {
            // DIRECT torque application
            wheel.motorTorque = clientTorque;
            
            // Apply brake torque if requested
            wheel.brakeTorque = brakeInput > 0 ? brakeTorque : 0f;
        }
        
        // Apply steering DIRECTLY
        float steeringAngle = steeringInput * maxSteeringAngle;
        
        // Front wheels
        wheelColliders[0].steerAngle = steeringAngle;
        wheelColliders[1].steerAngle = steeringAngle;
        
        // Rear wheels (counter-steering)
        wheelColliders[2].steerAngle = -steeringAngle * rearSteeringAmount;
        wheelColliders[3].steerAngle = -steeringAngle * rearSteeringAmount;
        
        // Send input to server - CRITICAL!
        try
        {
            SendInputServerRpc(input);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to send input to server: {e.Message}");
        }
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
        
        // Apply motor torque directly based on host/client status
        float torqueMultiplier = IsServer ? hostTorqueMultiplier : clientTorqueMultiplier;
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
        }
    }

    #endregion

    #region Networking

    [ServerRpc(RequireOwnership = true)]
    private void SendInputServerRpc(ClientInput input)
    {
        // Debug log to verify the server is receiving input
        Debug.Log($"SERVER received input: moveInput={input.moveInput}, steeringInput={input.steeringInput}, brakeInput={input.brakeInput}");
        
        // Only apply motor torque when there's actual input
        float motorTorque = 0f;
        
        if (Mathf.Abs(input.moveInput) > 0.01f)
        {
            // Calculate torque directly without MoveTowards (simpler, more reliable)
            motorTorque = input.moveInput * maxTorque * 5.0f; // Increased torque multiplier
            currentTorque = motorTorque;
            
            // Set higher friction for better traction
            SetWheelFriction(true);
        }
        else
        {
            // No torque when no input
            currentTorque = 0f;
            
            // Reset friction to normal
            SetWheelFriction(false);
        }
        
        // Apply torque to wheels - DIRECT APPLICATION
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            // Direct torque application
            wheelColliders[i].motorTorque = currentTorque;
            
            // Apply brake torque if requested
            wheelColliders[i].brakeTorque = input.brakeInput > 0 ? brakeTorque : 0;
            
            // Make sure wheels have contact with ground
            WheelHit hit;
            if (wheelColliders[i].GetGroundHit(out hit))
            {
                // Add moderate downforce for grip
                rb.AddForceAtPosition(Vector3.down * 300f, wheelColliders[i].transform.position);
            }
        }

        // Calculate steering angle server-side to ensure consistency
        float steeringAngle = input.steeringInput * maxSteeringAngle;
        
        // Apply steering directly
        wheelColliders[0].steerAngle = steeringAngle;
        wheelColliders[1].steerAngle = steeringAngle;
        wheelColliders[2].steerAngle = -steeringAngle * rearSteeringAmount;
        wheelColliders[3].steerAngle = -steeringAngle * rearSteeringAmount;

        // Set drift state
        isDrifting.Value = Mathf.Abs(localVelocityX) > 0.25f && cachedSpeed > 3f;
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
        if (rb != null && !rb.isKinematic)
        {
            // For jump we still need the impulse forces
            // Store current horizontal velocity
            Vector3 currentVelocity = rb.linearVelocity;
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

            // Apply a sharper upward impulse for faster rise
            float upwardVelocity = jumpForce * 1.2f;

            // Combine horizontal momentum with new vertical impulse
            Vector3 newVelocity = horizontalVelocity * 1.1f + Vector3.up * upwardVelocity;
            
            // SAFETY: Only set velocity if not kinematic
            if (CanApplyVelocity())
            {
                rb.linearVelocity = newVelocity;
                
                // Add a bit more forward boost in the car's facing direction
                rb.AddForce(transform.forward * (jumpForce * 5f), ForceMode.Impulse);
                
                // Temporarily increase gravity for faster fall
                StartCoroutine(TemporarilyIncreaseGravity(rb));
            }
        }

        // Execute visual effects on all clients
        JumpEffectsClientRpc();

        // Start cooldown
        StartCoroutine(JumpCooldownServer());
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
    
    private IEnumerator JumpCooldownServer()
    {
        yield return new WaitForSeconds(jumpCooldown);
        jumpReady.Value = true;
        isJumping.Value = false;
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

            // Execute respawn on all clients
            ExecuteRespawnClientRpc(respawnPosition, respawnRotation);
        }
    }

    [ClientRpc]
    public void ExecuteRespawnClientRpc(Vector3 respawnPosition, Quaternion respawnRotation)
    {
        // Set vehicle state
        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // Set position and rotation
        transform.position = respawnPosition;
        transform.rotation = respawnRotation;
        rb.position = respawnPosition;
        rb.rotation = respawnRotation;
        
        // Enable physics after a delay
        StartCoroutine(EnablePhysicsAfterRespawn());

        // Notify player (owner only)
        if (IsOwner)
        {
            player.Respawn();
        }
    }

    private IEnumerator EnablePhysicsAfterRespawn()
    {
        // Wait a moment for everything to settle
        yield return new WaitForSeconds(0.5f);
        
        // Enable physics
        rb.isKinematic = false;
        
        // Wake the rigidbody
        rb.WakeUp();
    }

    [ServerRpc(RequireOwnership = true)]
    private void UpdateDriftingServerRpc(bool isDriftingValue)
    {
        isDrifting.Value = isDriftingValue;
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        if (Explosion != null)
        {
            // Create explosion effect
            GameObject explosionInstance = Instantiate(Explosion, transform.position, Quaternion.identity);
            
            // Hide the car and disable controls
            canMove = false;
            
            // Start respawn timer
            StartCoroutine(ResetAfterExplosion(explosionInstance));
        }
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
        
        // Ensure physics properties are consistent
        rb.mass = 1200f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        
        // Initialize the vehicle's physics
        InitializeVehiclePhysics();
        
        // Mark as ready for updates
        hasReceivedInitialSync = true;
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
                    // Improved wheel setup with stronger friction
                    collider.forceAppPointDistance = 0;
                    
                    // Increase wheel friction for better traction
                    WheelFrictionCurve fwdFriction = collider.forwardFriction;
                    fwdFriction.stiffness = 2.5f;  // Increased from 2.0f for better grip
                    collider.forwardFriction = fwdFriction;
                    
                    WheelFrictionCurve sideFriction = collider.sidewaysFriction;
                    sideFriction.stiffness = 2.0f;  // Keep lateral slide the same
                    collider.sidewaysFriction = sideFriction;
                    
                    // Improve wheel suspension properties
                    JointSpring spring = collider.suspensionSpring;
                    spring.spring = 35000f;      // Stiffer springs
                    spring.damper = 4500f;       // More damping
                    collider.suspensionSpring = spring;
                    collider.suspensionDistance = 0.2f; // Shorter travel
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

    [ServerRpc(RequireOwnership = true)]
    private void SyncPositionToServerRpc(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        // Only process if we're actually the server
        if (!IsServer) return;
        
        // Update the vehicle's transform on the server
        transform.position = position;
        transform.rotation = rotation;
        rb.position = position;
        transform.rotation = rotation;
        
        // SAFETY: Only update velocity if not kinematic
        if (CanApplyVelocity() && Vector3.Distance(rb.linearVelocity, velocity) > 2f)
        {
            rb.linearVelocity = velocity;
        }
        
        // Broadcast to clients more frequently
        SyncPositionClientRpc(position, rotation, velocity);
    }

    [ClientRpc]
    private void SyncPositionClientRpc(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        // Skip for owner (they already have their own position)
        if (IsOwner) return;
        
        // Store targets for interpolation instead of directly applying
        targetPosition = position;
        targetRotation = rotation;
        targetVelocity = velocity;
        
        // Only update rigidbody if we have authority and it's not kinematic
        if (IsServer && CanApplyVelocity())
        {
            rb.position = position;
            rb.rotation = rotation;
            rb.linearVelocity = velocity;
        }
    }

    // Add initialization methods
    private void InitializeVehicle()
    {
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
    }
    
    private void RegisterInputEvents()
    {
        // Enable the input actions for gameplay
        inputControls.Gameplay.Enable();
    }

    // Add a safety method to check if we can apply velocity
    private bool CanApplyVelocity()
    {
        return rb != null && !rb.isKinematic;
    }

    // Add friction control method
    private void SetWheelFriction(bool increaseTraction)
    {
        foreach (WheelCollider wheel in wheelColliders)
        {
            WheelFrictionCurve fwdFriction = wheel.forwardFriction;
            WheelFrictionCurve sideFriction = wheel.sidewaysFriction;
            
            if (increaseTraction)
            {
                // Increase stiffness for better traction when accelerating
                fwdFriction.stiffness = 2.0f;
                sideFriction.stiffness = 2.0f;
            }
            else
            {
                // Default stiffness when not accelerating
                fwdFriction.stiffness = 1.0f;
                sideFriction.stiffness = 1.0f;
            }
            
            wheel.forwardFriction = fwdFriction;
            wheel.sidewaysFriction = sideFriction;
        }
    }
}