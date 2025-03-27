using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class NetworkHogController : NetworkBehaviour
{
    #region Variables

    [Header("Hog Params")]
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField] public bool canMove = true;
    [SerializeField] private bool canJump = true; // Whether the player can jump
    [SerializeField] private float maxTorque = 500f;
    [SerializeField] private float brakeTorque = 300f;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .7f;
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2f;
    [SerializeField, Range(0.1f, 5f)] private float decelerationFactor = 1f;
    [SerializeField] private float jumpForce = 7f;
    [SerializeField] private float jumpCooldown = 15f;

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
    [SerializeField] private Transform[] wheelMeshes = new Transform[4];
    [SerializeField] private HogVisualEffects vfxController;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.RTPC rpm;

    private InputManager inputManager;

    private bool hasReceivedInitialSync = false;
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private float cameraAngle;
    private bool jumpOnCooldown = false;
    private float jumpCooldownRemaining = 0f;
    private WheelFrictionCurve[] originalWheelFrictions = new WheelFrictionCurve[4];
    private float[] originalExtremumSlips = new float[4];
    private float _stateUpdateTimer = 0f;
    private float _clientUpdateTimer = 0f;
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<StateSnapshot> vehicleState = new NetworkVariable<StateSnapshot>(
        new StateSnapshot(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private Vector3 visualPositionTarget;
    private Quaternion visualRotationTarget;
    private Vector3 visualVelocityTarget;
    private bool needsSmoothing = false;
    public bool JumpOnCooldown => jumpOnCooldown;
    public float JumpCooldownRemaining => jumpCooldownRemaining;
    public float JumpCooldownTotal => jumpCooldown;

    #endregion

    #region Network Structs

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

    #endregion

    #region Lifecycle Methods

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Register for state change callback
        vehicleState.OnValueChanged += OnVehicleStateChanged;

        // Get input manager reference
        inputManager = InputManager.Instance;

        vfxController.Initialize(transform, centerOfMass, OwnerClientId);
        // Initialize the vehicle
        if (IsOwner)
        {
            // Owner initializes immediately
            InitializeOwnerVehicle();
            inputManager.JumpPressed += OnJumpPressed;
            inputManager.HornPressed += OnHornPressed;
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
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.EngineOff);
        }
    }

    private void Start()
    {
        rb.centerOfMass = centerOfMass;
        InitializeWheelFriction();

        // Only the server or owner should play the engine on sound
        if (IsOwner || IsServer)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.EngineOn);
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
        UpdateJumpCooldown();
    }

    private void FixedUpdate()
    {
        // Physics updates only for owner and server
        if (!hasReceivedInitialSync) return;

        if (IsOwner)
        {
            // Owner physics already handled by input
        }
        else if (IsServer && !IsOwner)
        {
            // Server applies physics for non-owned vehicles
            ApplyServerPhysics();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Player")) return;

        // Process collision only on server or owner 
        if (IsServer || IsOwner)
        {
            var speed = rb.linearVelocity.magnitude;
            // Play sound effect based on impact magnitude
            if (speed >= 1 && speed < 5)
            {
                SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.HogImpactLow);
            }
            else if (speed >= 5 && speed < 13)
            {
                SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.HogImpactMed);
            }
            else if (speed >= 13)
            {
                SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.HogImpactHigh);
            }
        }

        if (IsOwner)
        {
            // Client sends immediate update on collision
            SendStateToServer();
        }

        if (IsServer)
        {
            // Server broadcasts the collision immediately
            BroadcastServerStateClientRpc(vehicleState.Value);
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
        Player playerComponent = transform.root.GetComponent<Player>();

        if (playerComponent != null)
        {
            // Get player data to use spawn point
            if (ConnectionManager.Instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData))
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
        }

        // Configure physics
        rb.isKinematic = false;
        EnableWheelColliders();

        // Mark as initialized
        hasReceivedInitialSync = true;
    }

    private void InitializeWheelFriction()
    {
        for (int i = 0; i < 4; i++)
        {
            originalWheelFrictions[i] = wheelColliders[i].sidewaysFriction;
            originalExtremumSlips[i] = originalWheelFrictions[i].extremumSlip;
        }
    }

    #endregion

    #region Input Handling

    private void HandleOwnerInput()
    {
        // Get movement input using InputManager
        float moveInput = inputManager.ThrottleInput - inputManager.BrakeInput;
        float brakeInput = inputManager.BrakeInput;

        // Get camera angle
        Vector2 lookInput = inputManager.LookInput;
        cameraAngle = CalculateCameraAngle(lookInput);

        // Calculate steering angle
        float steeringAngle = CalculateSteering(cameraAngle, moveInput);

        // Apply steering to wheels
        ApplySteeringToWheels(steeringAngle);

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

        ApplyMotorTorqueToWheels(currentTorque);

        // Update local velocity for drift detection
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;
        isDrifting.Value = localVelocityX > 0.4f;
    }

    private void OnHornPressed()
    {
        if (!IsOwner) return;

        if (!transform.root.gameObject.GetComponent<Player>().isSpectating)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.HogHorn);
        }
    }
    private void OnJumpPressed()
    {
        if (!IsOwner) return;

        // Check if player can jump and is not spectating
        bool canPerformJump = canMove && canJump && !jumpOnCooldown &&
                             !transform.root.gameObject.GetComponent<Player>().isSpectating;

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

    private float CalculateCameraAngle(Vector2 lookInput = default)
    {
        // Check if any menus are active - if so, don't update camera angle
        if (MenuManager.Instance != null && MenuManager.Instance.gameIsPaused)
        {
            // Return current camera angle without updating it
            return cameraAngle;
        }
        
        // If using gamepad with sufficient input magnitude
        if (inputManager.IsUsingGamepad && lookInput.sqrMagnitude > 0.01f)
        {
            // Convert look input to camera angle
            return Mathf.Atan2(lookInput.x, lookInput.y) * Mathf.Rad2Deg;
        }
        else
        {
            // Use traditional position-based calculation for mouse/keyboard
            Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
            cameraVector.y = 0;
            Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
            return Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));
        }
    }

    #endregion

    #region Vehicle Physics

    private float CalculateSteering(float steeringInput, float moveInput)
    {
        float forwardVelocity = Vector3.Dot(rb.linearVelocity, transform.forward);
        bool isMovingInReverse = forwardVelocity < -0.5f;

        // Invert steering for reverse driving
        if (isMovingInReverse && moveInput < 0)
        {
            steeringInput = -steeringInput;
        }

        return Mathf.Clamp(steeringInput, -maxSteeringAngle, maxSteeringAngle);
    }

    private void ApplySteeringToWheels(float steeringAngle)
    {
        // Front wheels steering
        wheelColliders[0].steerAngle = Mathf.Lerp(wheelColliders[0].steerAngle, steeringAngle, steeringSpeed);
        wheelColliders[1].steerAngle = Mathf.Lerp(wheelColliders[1].steerAngle, steeringAngle, steeringSpeed);

        // Rear wheels counter-steering
        float rearSteeringAngle = steeringAngle * -0.35f;
        wheelColliders[2].steerAngle = Mathf.Lerp(wheelColliders[2].steerAngle, rearSteeringAngle, steeringSpeed);
        wheelColliders[3].steerAngle = Mathf.Lerp(wheelColliders[3].steerAngle, rearSteeringAngle, steeringSpeed);
    }

    private void ApplyMotorTorqueToWheels(float torqueValue)
    {
        if (!canMove)
        {
            foreach (var wheel in wheelColliders)
            {
                wheel.motorTorque = 0;
            }
            return;
        }

        foreach (var wheel in wheelColliders)
        {
            wheel.motorTorque = torqueValue;
        }
    }

    private void ApplyServerPhysics()
    {
        // Server applies physics for non-owned vehicles
        // This is used for server-side validation and for AI vehicles
        if (!IsOwner && IsServer)
        {
            // Apply steering and motor torque
            ApplySteeringToWheels(vehicleState.Value.steeringAngle);
            ApplyMotorTorqueToWheels(vehicleState.Value.motorTorque);
        }
    }

    #endregion

    #region Jump

    // Update jump cooldown in your Update method
    private void UpdateJumpCooldown()
    {
        if (!IsOwner) return;

        if (jumpOnCooldown)
        {
            jumpCooldownRemaining -= Time.deltaTime;
            if (jumpCooldownRemaining <= 0)
            {
                jumpOnCooldown = false;
            }
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

        // Play local jump effects
        vfxController.PlayJumpEffects();

        // Play jump sound - owner will trigger it through the network
        SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject, SoundManager.SoundEffectType.HogJump);
    }

    // Notify other clients to show jump effects
    [ClientRpc]
    private void NotifyJumpClientRpc()
    {
        // Skip for the owner as they already played effects
        if (IsOwner) return;
        vfxController.PlayJumpEffects();
    }
    #endregion

    #region Respawn Methods

    // Client calls this when they want to respawn
    [ServerRpc(RequireOwnership = true)]
    public void RequestRespawnServerRpc()
    {
        // Get player data from connection manager
        Player playerComponent = transform.root.GetComponent<Player>();

        if (playerComponent != null)
        {
            // Get the latest player data
            if (ConnectionManager.Instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData))
            {
                // Set respawn position and rotation
                Vector3 respawnPosition = playerData.spawnPoint;
                Quaternion respawnRotation = Quaternion.LookRotation(
                    SpawnPointManager.Instance.transform.position - playerData.spawnPoint);

                // Update player state
                playerData.state = PlayerState.Alive;
                ConnectionManager.Instance.UpdatePlayerDataClientRpc(playerComponent.clientId, playerData);

                // Execute respawn for everyone
                ExecuteRespawnClientRpc(respawnPosition, respawnRotation);
            }
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
            steeringAngle = wheelColliders[0].steerAngle,
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
        ApplySteeringToWheels(vehicleState.Value.steeringAngle);

        // Continue smoothing while the difference is significant
        needsSmoothing =
            Vector3.Distance(transform.position, visualPositionTarget) > 0.01f ||
            Quaternion.Angle(transform.rotation, visualRotationTarget) > 0.1f;
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

    #endregion

    #region Visual Effects

    private void AnimateWheels()
    {
        // Update wheel meshes to match collider positions
        for (int i = 0; i < 4; i++)
        {
            Vector3 position;
            Quaternion rotation;
            wheelColliders[i].GetWorldPose(out position, out rotation);

            wheelMeshes[i].position = position;
            wheelMeshes[i].rotation = rotation;
        }
    }

    private void UpdateDriftEffects()
    {
        bool rearLeftGrounded = wheelColliders[2].isGrounded;
        bool rearRightGrounded = wheelColliders[3].isGrounded;

        vfxController.UpdateDriftEffects(isDrifting.Value, rearLeftGrounded, rearRightGrounded, canMove);
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        // canMove = false;
        vfxController.CreateExplosion();

        // Handle explosion sound once per client
        SoundManager.Instance.PlayLocalSound(gameObject, SoundManager.SoundEffectType.CarExplosion);

        StartCoroutine(ResetAfterExplosion());
    }

    private IEnumerator ResetAfterExplosion()
    {
        yield return new WaitForSeconds(3f);
        // canMove = true;
    }

    #endregion

    #region Utility Methods

    private IEnumerator CollisionDebounce()
    {
        collisionForceOnCooldown = true;
        yield return new WaitForSeconds(.5f);
        collisionForceOnCooldown = false;
    }

    private void EnableWheelColliders()
    {
        foreach (var wheel in wheelColliders)
        {
            wheel.enabled = true;
        }
    }
    #endregion
}