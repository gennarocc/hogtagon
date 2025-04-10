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
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2f;
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
    [SerializeField] private float clientUpdateInterval = 0.05f;      // How often client sends updates (20Hz)
    [SerializeField] private float stateUpdateInterval = 0.1f;        // How often server broadcasts state (10Hz)
    [SerializeField] private float visualSmoothingSpeed = 10f;
    [SerializeField] private bool debugMode = false;

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

    #endregion

    #region Private Fields

    // Constants
    private const float MIN_VELOCITY_FOR_REVERSE = -0.5f;

    // Input reference
    private InputManager inputManager;
    
    // Player reference
    private Player player;

    // Input Smoothing
    private Queue<float> _recentSteeringInputs;
    private List<float> _steeringInputsList = new List<float>();
    private float currentSteeringInput = 0f; // Current smoothed steering input

    // Network variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isJumping = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> jumpReady = new NetworkVariable<bool>(true);
    private NetworkVariable<StateSnapshot> vehicleState = new NetworkVariable<StateSnapshot>(
        new StateSnapshot(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Register for state change callback
        vehicleState.OnValueChanged += OnVehicleStateChanged;

        // Get input manager reference
        inputManager = InputManager.Instance;
        
        // Get player component
        player = transform.root.GetComponent<Player>();
        
        if (player == null)
        {
            player = GetComponent<Player>();
        }

        if (IsOwner)
        {
            // Owner initializes immediately
            InitializeOwnerVehicle();
            
            // Subscribe to input events
            if (inputManager != null)
            {
                inputManager.JumpPressed += OnJumpPressed;
                inputManager.HornPressed += OnHornPressed;
            }
        }
        else if (IsServer && !IsOwner)
        {
            // Server initializes non-owned vehicles
            InitializeServerControlledVehicle();
        }
        else
        {
            // Remote client needs server to tell it what to do
            RequestInitialStateServerRpc();
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from input events
        if (inputManager != null && IsOwner)
        {
            inputManager.JumpPressed -= OnJumpPressed;
            inputManager.HornPressed -= OnHornPressed;
        }

        // Only the server or owner should play the engine off sound
        if (IsOwner || IsServer)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.gameObject, SoundManager.SoundEffectType.EngineOff);
        }
    }

    private void Start()
    {
        // Get reference to the InputManager
        if (inputManager == null)
        {
            inputManager = InputManager.Instance;
            if (inputManager == null)
            {
                Debug.LogError("InputManager not found in the scene");
            }
        }

        // Initialize steering input buffer
        _recentSteeringInputs = new Queue<float>(steeringBufferSize);

        rb.centerOfMass = centerOfMass;
        InitializeWheelFriction();

        // Only the server or owner should play the engine on sound
        if (IsOwner || IsServer)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.gameObject, SoundManager.SoundEffectType.EngineOn);
        }

        // Initialize visual smoothing targets
        visualPositionTarget = transform.position;
        visualRotationTarget = transform.rotation;
        visualVelocityTarget = Vector3.zero;

        if (debugMode && IsOwner)
        {
            Debug.Log($"Owner vehicle spawned at {rb.position}");
        }
    }

    private void Update()
    {
        // Wait until properly initialized
        if (!hasReceivedInitialSync) return;

        if (IsOwner)
        {
            // Owner controls the vehicle
            HandleOwnerInput();

            // Periodically send state to server for validation and broadcasting
            _clientUpdateTimer += Time.deltaTime;
            if (_clientUpdateTimer >= clientUpdateInterval)
            {
                _clientUpdateTimer = 0f;
                SendStateToServer();
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

        // Common updates
        AnimateWheels();
        UpdateDriftEffects();
    }

    private void FixedUpdate()
    {
        // Wait until properly initialized
        if (!hasReceivedInitialSync) return;
        
        // Cache frequently used physics values
        CachePhysicsValues();

        if (IsOwner)
        {
            // Owner physics already handled by input in HandleOwnerInput
        }
        else if (IsServer && !IsOwner)
        {
            // Server applies physics for non-owned vehicles
            ApplyServerPhysics();
        }
    }

    #endregion

    #region Input Handling

    private void OnHornPressed()
    {
        // Only play horn if not spectating
        if (!player.isSpectating)
        {
            SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.HogHorn);
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
            // Apply jump physics directly on the client
            ApplyJumpPhysics();

            // Start cooldown
            jumpOnCooldown = true;
            jumpCooldownRemaining = jumpCooldown;

            // Notify other clients about the jump (for effects only)
            NotifyJumpClientRpc();

            // Send updated state immediately after jump
            SendStateToServer();
        }
    }

    private void ApplyJumpPhysics()
    {
        if (!IsOwner) return;

        // Store current horizontal velocity
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

        // Start with a position boost for immediate feedback
        Vector3 currentPos = rb.position;
        Vector3 targetPos = currentPos + Vector3.up * 1.5f; // Small initial boost
        rb.MovePosition(targetPos);

        // Apply a sharper upward impulse for faster rise
        float upwardVelocity = jumpForce * 1.2f;

        // Combine horizontal momentum with new vertical impulse
        Vector3 newVelocity = horizontalVelocity * 1.1f + Vector3.up * upwardVelocity;
        rb.linearVelocity = newVelocity;

        // Add a bit more forward boost in the car's facing direction
        rb.AddForce(transform.forward * (jumpForce * 5f), ForceMode.Impulse);

        // Play jump sound
        SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.HogJump);
        
        // Play jump particle effects
        foreach (var ps in jumpParticleSystems)
        {
            ps.Play();
        }
    }

    [ClientRpc]
    private void NotifyJumpClientRpc()
    {
        // Skip for the owner as they already played effects
        if (IsOwner) return;
        
        // Play jump particle effects
        foreach (var ps in jumpParticleSystems)
        {
            ps.Play();
        }
    }

    private void ClientMove()
    {
        ClientInput input = CollectPlayerInput();
        
        // Debug steering input
        if (Mathf.Abs(input.steeringInput) > 0.01f)
        {
            Debug.Log($"Steering input: {input.steeringInput}");
        }
        
        SendInputServerRpc(input);
    }

    private ClientInput CollectPlayerInput()
    {
        // Get raw steering input
        float targetSteeringInput = inputManager.SteeringInput;
        
        // Smoothly interpolate current steering toward the target value
        currentSteeringInput = Mathf.Lerp(currentSteeringInput, targetSteeringInput, Time.deltaTime * (1f / steeringInputSmoothing));
        
        // Then apply additional smoothing/filtering as before
        float smoothedSteeringInput = ApplyInputSmoothing(currentSteeringInput);
        
        return new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = inputManager.ThrottleInput - inputManager.BrakeInput,
            brakeInput = inputManager.BrakeInput,
            steeringInput = smoothedSteeringInput
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
        // Get movement input using InputManager
        float moveInput = inputManager.ThrottleInput - inputManager.BrakeInput;
        float brakeInput = inputManager.BrakeInput;

        // Get steering input directly from input manager - COMPLETELY INDEPENDENT FROM CAMERA
        float rawSteeringInput = inputManager.SteeringInput;
        
        // Apply input smoothing to steering
        currentSteeringInput = Mathf.Lerp(currentSteeringInput, rawSteeringInput, Time.deltaTime * (1f / steeringInputSmoothing));
        float smoothedSteeringInput = ApplyInputSmoothing(currentSteeringInput);

        // Calculate steering angle from input using the direct control method
        SteeringData steeringData = CalculateSteeringFromInput(smoothedSteeringInput, moveInput);

        // Apply steering to wheels
        ApplySteering(steeringData);
        
        // Calculate and apply torque
        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        float torqueDelta = moveInput != 0 ?
            (Time.deltaTime * maxTorque * accelerationFactor) :
            (Time.deltaTime * maxTorque * decelerationFactor);

        if (Mathf.Abs(targetTorque - currentTorque) <= torqueDelta)
        {
            currentTorque = targetTorque;
        }
        else
        {
            currentTorque += Mathf.Sign(targetTorque - currentTorque) * torqueDelta;
        }

        ApplyMotorTorque(moveInput, brakeInput);

        // Update local velocity for drift detection
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;
        isDrifting.Value = localVelocityX > 0.4f;
    }

    #endregion

    #region Networking

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        // Calculate steering angle server-side to ensure consistency
        SteeringData steeringData = CalculateSteeringFromInput(input.steeringInput, input.moveInput);

        ApplyMotorTorque(input.moveInput, input.brakeInput);
        ApplySteering(steeringData);

        isDrifting.Value = Mathf.Abs(localVelocityX) > .45f;
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        // Store reference to instantiated explosion
        GameObject explosionInstance = Instantiate(Explosion, transform.position + centerOfMass, transform.rotation, transform);
        canMove = false;

        // Play explosion sound
        SoundManager.Instance.PlayLocalSound(gameObject, SoundManager.SoundEffectType.CarExplosion);
        
        Debug.Log("Exploding car for player - " + ConnectionManager.Instance.GetClientUsername(OwnerClientId));
        StartCoroutine(ResetAfterExplosion(explosionInstance));
    }

    #endregion

    #region Physics & Movement

    private void CachePhysicsValues()
    {
        cachedVelocity = rb.linearVelocity;
        cachedSpeed = cachedVelocity.magnitude;
        localVelocityX = transform.InverseTransformDirection(cachedVelocity).x;
        cachedForwardVelocity = Vector3.Dot(cachedVelocity, transform.forward);

        if (IsServer || IsOwner)
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                cachedWheelRpms[i] = wheelColliders[i].rpm;
            }

            // Update monitoring variables
            frontLeftRpm = cachedWheelRpms[0];
            rpm.SetGlobalValue(frontLeftRpm);
            velocity = cachedSpeed;
        }
    }

    private SteeringData CalculateSteeringFromInput(float steeringInput, float moveInput)
    {
        // Debug the raw steering input
        if (Mathf.Abs(steeringInput) > 0.01f && debugMode)
        {
            Debug.Log($"Server received steering input: {steeringInput}");
        }
        
        // Apply adaptive deadzone - higher deadzone at higher speeds
        float speedFactor = Mathf.Clamp01(cachedSpeed / maxSpeedForSteering);
        
        // Convert the -1 to 1 steering input to an angle based on maxSteeringAngle
        float steeringAngle = steeringInput * maxSteeringAngle;
        
        // If steering input is small, treat it as zero
        if (Math.Abs(steeringInput) < 0.05f)
            steeringAngle = 0f;

        // Check if car is actually moving in reverse
        bool isMovingInReverse = cachedForwardVelocity < MIN_VELOCITY_FOR_REVERSE;

        // Improved reverse handling - check both the car's actual movement direction 
        // and the player's intent (trying to go backward with S/down)
        bool isReverseGear = moveInput < -0.1f;
        bool shouldUseReverseControls = isReverseGear;
        
        // For reverse, we invert the steering direction
        float adjustedSteeringAngle = steeringAngle;
        if (shouldUseReverseControls)
        {
            // In reverse gear, invert the steering for natural feel
            adjustedSteeringAngle = -steeringAngle;
            
            // Increase steering response in reverse for better control at low speeds
            float reverseSteeringBoost = Mathf.Lerp(1.2f, 1.0f, Mathf.Clamp01(cachedSpeed / 5f));
            adjustedSteeringAngle *= reverseSteeringBoost;
        }

        // Apply speed-based response - gentler at higher speeds for stability
        float steeringResponse = Mathf.Lerp(maxSteeringResponse, minSteeringResponse, speedFactor);
        float finalSteeringAngle = adjustedSteeringAngle * steeringResponse;

        return new SteeringData
        {
            frontSteeringAngle = finalSteeringAngle,
            rearSteeringAngle = finalSteeringAngle * -rearSteeringAmount,
            isReversing = shouldUseReverseControls
        };
    }

    private void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        if (!canMove)
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                wheelColliders[i].motorTorque = 0;
            }
            return;
        }

        float motorTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = motorTorque;
        }
    }

    private void ApplySteering(SteeringData steeringData)
    {
        // Apply smoothed steering to wheel colliders
        wheelColliders[0].steerAngle = Mathf.Lerp(wheelColliders[0].steerAngle, steeringData.frontSteeringAngle, steeringSpeed);
        wheelColliders[1].steerAngle = Mathf.Lerp(wheelColliders[1].steerAngle, steeringData.frontSteeringAngle, steeringSpeed);
        wheelColliders[2].steerAngle = Mathf.Lerp(wheelColliders[2].steerAngle, steeringData.rearSteeringAngle, steeringSpeed);
        wheelColliders[3].steerAngle = Mathf.Lerp(wheelColliders[3].steerAngle, steeringData.rearSteeringAngle, steeringSpeed);
    }

    private void InitializeWheelFriction()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelFrictions[i] = wheelColliders[i].sidewaysFriction;
            originalExtremumSlip[i] = wheelFrictions[i].extremumSlip;
        }
    }

    private void DisableWheelColliderPhysics()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].enabled = false;
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

        // Get reference to the player object
        ulong clientId = OwnerClientId;

        if (NetworkManager.ConnectedClients.ContainsKey(clientId))
        {
            var playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
            Rigidbody rb = playerObject.GetComponentInChildren<Rigidbody>();

            if (rb != null)
            {
                // Store current horizontal velocity
                Vector3 currentVelocity = rb.linearVelocity;
                Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

                // Start with a position boost for immediate feedback
                Vector3 currentPos = rb.position;
                Vector3 targetPos = currentPos + Vector3.up * 1.5f; // Smaller initial boost
                rb.MovePosition(targetPos);

                // Apply a sharper upward impulse for faster rise
                float upwardVelocity = jumpForce * 1.2f; // Faster rise

                // Combine horizontal momentum with new vertical impulse
                // Multiply horizontal speed to maintain or enhance momentum
                Vector3 newVelocity = horizontalVelocity * 1.1f + Vector3.up * upwardVelocity;
                rb.linearVelocity = newVelocity;

                // Add a bit more forward boost in the car's facing direction
                rb.AddForce(transform.forward * (jumpForce * 5f), ForceMode.Impulse);

                // Increase gravity temporarily for faster fall
                StartCoroutine(TemporarilyIncreaseGravity(rb));

                Debug.Log($"Applied jump with preserving momentum: {horizontalVelocity}, new velocity: {newVelocity}");
            }
        }

        // Execute visual effects on all clients
        JumpEffectsClientRpc();

        // Start cooldown
        StartCoroutine(JumpCooldownServer());
    }

    private IEnumerator TemporarilyIncreaseGravity(Rigidbody rb)
    {
        // Store original gravity
        float gravity = -9.81f;
        Debug.Log(gravity);

        // Wait for the rise phase (about 0.3 seconds)
        yield return new WaitForSeconds(0.3f);

        // Apply stronger gravity for faster fall
        Physics.gravity = new Vector3(0, gravity * 2f, 0);

        // Wait for the fall phase
        yield return new WaitForSeconds(0.5f);

        // Restore original gravity
        Physics.gravity = new Vector3(0, gravity, 0);
    }

    [ClientRpc]
    private void JumpEffectsClientRpc()
    {
        jumpParticleSystems[0].Play();
        jumpParticleSystems[1].Play();

        if (IsOwner)
        {
            jumpOnCooldown = true;
            jumpCooldownRemaining = jumpCooldown;

            // Play sound effect
            // HogSoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogJump);
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
        EnableWheelColliders();

        // Mark as initialized
        hasReceivedInitialSync = true;

        if (debugMode)
        {
            Debug.Log("[Owner] Vehicle initialized");
        }
    }

    private void InitializeServerControlledVehicle()
    {
        // Get the Player component to access spawn point
        // Get player data to use spawn point
        if (ConnectionManager.Instance.TryGetPlayerData(player.clientId, out PlayerData playerData))
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
        EnableWheelColliders();

        // Mark as initialized
        hasReceivedInitialSync = true;
    }

    private void EnableWheelColliders()
    {
        foreach (var wheel in wheelColliders)
        {
            wheel.enabled = true;
        }
    }

    #endregion

    #region Network State Management

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
            steeringAngle = wheelColliders[0].steerAngle, // Use actual wheel steering angle
            motorTorque = wheelColliders[0].motorTorque,
            updateId = (uint)Time.frameCount // Simple unique ID
        };

        // Send to server and update network variable
        vehicleState.Value = snapshot;

        // Also directly inform server for validation
        SendStateForValidationServerRpc(snapshot);
    }

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

    [ServerRpc(RequireOwnership = true)]
    private void SendStateForValidationServerRpc(StateSnapshot state)
    {
        // This RPC is just to notify the server to validate the state
        // The actual state is already sent via NetworkVariable
        if (debugMode)
        {
            Debug.Log($"[Server] Received state for validation: {state.position}");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitialStateServerRpc()
    {
        // Client requests initial state from server
        if (debugMode)
        {
            Debug.Log("[Server] Client requested initial state");
        }

        // Send the current state to the requesting client
        SendInitialStateClientRpc(vehicleState.Value);
    }

    [ClientRpc]
    private void SendInitialStateClientRpc(StateSnapshot state)
    {
        // Skip server and owner (they don't need initialization)
        if (IsServer || IsOwner || hasReceivedInitialSync) return;

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

    [ClientRpc]
    private void BroadcastServerStateClientRpc(StateSnapshot state)
    {
        // Skip server and owner (they already have the state)
        if (IsServer || IsOwner) return;

        // Apply state for remote vehicles
        ApplyRemoteState(state);
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

    #endregion
}