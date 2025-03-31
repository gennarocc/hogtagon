using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;

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

    [Header("Collision System")]
    [SerializeField] private float collisionDebounceTime = 0.2f;      // Time window to ignore duplicate collisions
    [SerializeField] private float collisionForceMultiplier = 0.5f;   // Adjusts collision strength

    [Header("References")]
    [SerializeField] public Rigidbody rb;
    [SerializeField] private CinemachineFreeLook freeLookCamera;
    [SerializeField] public WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
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
    private bool isDrifting = false;
    private StateSnapshot currentState = new StateSnapshot();
    private StateSnapshot visualTargetState = new StateSnapshot();
    private bool needsSmoothing = false;
    private Dictionary<string, float> recentCollisions = new Dictionary<string, float>();
    private uint updateIdCounter = 0;
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
        visualTargetState.position = transform.position;
        visualTargetState.rotation = transform.rotation;
        visualTargetState.velocity = Vector3.zero;

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
                BroadcastStateUpdateClientRpc(currentState,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = GetAllClientIdsExcept(OwnerClientId) } });
            }
        }
        else
        {
            // Remote client - smooth visual representation of other vehicles
            SmoothRemoteVehicleVisuals();
        }

        rpm.SetGlobalValue(wheelColliders[0].rpm);
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
        // Only process collisions on the server
        if (!IsServer) return;

        // Only care about collisions with other vehicles
        if (!collision.gameObject.CompareTag("Player")) return;

        HandleServerCollision(collision);
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

        // Initialize state
        currentState.position = rb.position;
        currentState.rotation = rb.rotation;
        currentState.velocity = rb.linearVelocity;
        currentState.angularVelocity = rb.angularVelocity;
        currentState.steeringAngle = wheelColliders[0].steerAngle;
        currentState.motorTorque = wheelColliders[0].motorTorque;
        currentState.updateId = updateIdCounter++;

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
        isDrifting = localVelocityX > 0.4f;

        // Update the current state
        currentState.position = rb.position;
        currentState.rotation = rb.rotation;
        currentState.velocity = rb.linearVelocity;
        currentState.angularVelocity = rb.angularVelocity;
        currentState.steeringAngle = wheelColliders[0].steerAngle;
        currentState.motorTorque = wheelColliders[0].motorTorque;
        currentState.updateId = updateIdCounter++;
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
        float rearSteeringAngle = steeringAngle * -1f;
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
            ApplySteeringToWheels(currentState.steeringAngle);
            ApplyMotorTorqueToWheels(currentState.motorTorque);
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

    #region Collision System

    // Server-side collision handling
    private void HandleServerCollision(Collision collision)
    {
        if (!IsServer) return;

        // Get the NetworkHogController from both vehicles
        NetworkHogController vehicleA = this;
        NetworkHogController vehicleB = collision.gameObject.transform.root.GetComponent<NetworkHogController>();

        if (vehicleB == null)
        {
            Debug.LogWarning("[Server] Could not find NetworkHogController on collision target");
            return;
        }

        // Skip if either vehicle is not initialized yet
        if (!vehicleA.hasReceivedInitialSync || !vehicleB.hasReceivedInitialSync)
        {
            return;
        }

        // Check if we've already processed this collision recently
        string collisionKey = $"{vehicleA.OwnerClientId}_{vehicleB.OwnerClientId}";
        string reverseKey = $"{vehicleB.OwnerClientId}_{vehicleA.OwnerClientId}";
        float currentTime = Time.time;

        if (recentCollisions.TryGetValue(collisionKey, out float lastCollisionTime))
        {
            if (currentTime - lastCollisionTime < collisionDebounceTime)
            {
                return; // Skip this collision, too soon after previous one
            }
        }
        else if (recentCollisions.TryGetValue(reverseKey, out lastCollisionTime))
        {
            if (currentTime - lastCollisionTime < collisionDebounceTime)
            {
                return; // Skip this collision, too soon after previous one
            }
        }

        // Record this collision
        recentCollisions[collisionKey] = currentTime;

        // Get collision data
        ContactPoint contact = collision.GetContact(0);
        Vector3 contactPoint = contact.point;
        Vector3 contactNormal = contact.normal;
        Vector3 relativeVelocity = collision.relativeVelocity;
        float impulseForce = collision.impulse.magnitude;

        if (impulseForce < 1.0f)
        {
            impulseForce = relativeVelocity.magnitude * vehicleA.rb.mass * 0.5f;
        }

        // Apply forces to both vehicles
        ApplyServerCollisionForces(
            vehicleA,
            vehicleB,
            relativeVelocity,
            contactPoint,
            contactNormal,
            impulseForce
        );
    }

    private void ApplyServerCollisionForces(
        NetworkHogController vehicleA,
        NetworkHogController vehicleB,
        Vector3 relativeVelocity,
        Vector3 contactPoint,
        Vector3 contactNormal,
        float impulseForce)
    {
        if (!IsServer) return;

        // Apply force multiplier
        impulseForce *= collisionForceMultiplier;

        // Calculate forces for both vehicles
        Vector3 forceOnB = -contactNormal * impulseForce; // Force on vehicle B
        Vector3 forceOnA = contactNormal * impulseForce;  // Force on vehicle A (opposite direction)

        // Apply forces directly to rigidbodies
        vehicleA.rb.AddForceAtPosition(forceOnA, contactPoint, ForceMode.Impulse);
        vehicleB.rb.AddForceAtPosition(forceOnB, contactPoint, ForceMode.Impulse);

        if (debugMode)
        {
            Debug.Log($"[Server] Applied collision forces: {impulseForce} between {vehicleA.OwnerClientId} and {vehicleB.OwnerClientId}");
        }

        // Wait a frame for physics to apply before updating states
        StartCoroutine(UpdateStatesAfterCollision(vehicleA, vehicleB));

        // Play collision sounds
        float impactSpeed = relativeVelocity.magnitude;

        // Play sound on both vehicles
        vehicleA.PlayImpactSoundClientRpc(impactSpeed,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { vehicleA.OwnerClientId } } });

        vehicleB.PlayImpactSoundClientRpc(impactSpeed,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { vehicleB.OwnerClientId } } });
    }

    private IEnumerator UpdateStatesAfterCollision(NetworkHogController vehicleA, NetworkHogController vehicleB)
    {
        // Wait for fixed update to ensure physics has been applied
        yield return new WaitForFixedUpdate();

        // Create snapshots of current physics state after collision
        StateSnapshot snapshotA = new StateSnapshot
        {
            position = vehicleA.rb.position,
            rotation = vehicleA.rb.rotation,
            velocity = vehicleA.rb.linearVelocity,
            angularVelocity = vehicleA.rb.angularVelocity,
            steeringAngle = vehicleA.wheelColliders[0].steerAngle,
            motorTorque = vehicleA.wheelColliders[0].motorTorque,
            updateId = vehicleA.updateIdCounter++
        };

        StateSnapshot snapshotB = new StateSnapshot
        {
            position = vehicleB.rb.position,
            rotation = vehicleB.rb.rotation,
            velocity = vehicleB.rb.linearVelocity,
            angularVelocity = vehicleB.rb.angularVelocity,
            steeringAngle = vehicleB.wheelColliders[0].steerAngle,
            motorTorque = vehicleB.wheelColliders[0].motorTorque,
            updateId = vehicleB.updateIdCounter++
        };

        // Update local server states
        vehicleA.currentState = snapshotA;
        vehicleB.currentState = snapshotB;

        // First update the owners with new physics state
        vehicleA.UpdateOwnerAfterCollisionClientRpc(snapshotA,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { vehicleA.OwnerClientId } } });

        vehicleB.UpdateOwnerAfterCollisionClientRpc(snapshotB,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { vehicleB.OwnerClientId } } });

        // Then broadcast to everyone else for visual updates
        vehicleA.BroadcastStateUpdateClientRpc(snapshotA,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = GetAllClientIdsExcept(vehicleA.OwnerClientId) } });

        vehicleB.BroadcastStateUpdateClientRpc(snapshotB,
            new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = GetAllClientIdsExcept(vehicleB.OwnerClientId) } });
    }

    [ClientRpc]
    private void PlayImpactSoundClientRpc(float speed, ClientRpcParams clientRpcParams)
    {
        PlayImpactSound(speed);
    }

    private void PlayImpactSound(float speed)
    {
        if (speed >= 1 && speed < 5)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject,
                                                  SoundManager.SoundEffectType.HogImpactLow);
        }
        else if (speed >= 5 && speed < 13)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject,
                                                  SoundManager.SoundEffectType.HogImpactMed);
        }
        else if (speed >= 13)
        {
            SoundManager.Instance.PlayNetworkedSound(transform.root.gameObject,
                                                  SoundManager.SoundEffectType.HogImpactHigh);
        }
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
            visualTargetState.position = respawnPosition;
            visualTargetState.rotation = respawnRotation;
            visualTargetState.velocity = Vector3.zero;
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
                updateId = updateIdCounter++
            };

            // Set state and send to server
            currentState = snapshot;

            // Send to server only if networked
            if (NetworkManager.Singleton.IsListening)
            {
                SendStateToServerServerRpc(snapshot);
            }
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

    public void SendStateToServer()
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
            updateId = updateIdCounter++
        };

        // Store locally
        currentState = snapshot;

        // Send to server
        SendStateToServerServerRpc(snapshot);
    }

    // Server receives regular updates from clients
    [ServerRpc(RequireOwnership = true)]
    private void SendStateToServerServerRpc(StateSnapshot snapshot)
    {
        if (!IsServer) return;

        // Validate state
        if (ValidateClientState(snapshot))
        {
            // Store the state on the server
            currentState = snapshot;

            // Server can now broadcast to all other clients
            BroadcastStateUpdateClientRpc(snapshot,
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = GetAllClientIdsExcept(OwnerClientId)
                    }
                }
            );
        }
        else if (debugMode)
        {
            Debug.LogWarning($"[Server] Rejected invalid state from client {OwnerClientId}");
        }
    }

    // Server validates client state (similar to your existing validation)
    private bool ValidateClientState(StateSnapshot clientState)
    {
        if (!IsServer) return false;

        // Simple validation - check if position is within valid range
        Vector3 currentPos = rb.position;
        float distance = Vector3.Distance(currentPos, clientState.position);

        if (distance > serverValidationThreshold)
        {
            if (debugMode)
            {
                Debug.LogWarning($"[Server] Detected invalid position from owner. Distance: {distance}m");
            }

            // Option 1: Reject and use server state
            return false;

            // Option 2: Accept but log the issue (more lenient)
            // return true;
        }

        return true;
    }

    // Server broadcasts state updates to clients
    [ClientRpc]
    private void BroadcastStateUpdateClientRpc(StateSnapshot snapshot, ClientRpcParams clientRpcParams = default)
    {
        // Don't process our own updates
        if (IsOwner && IsClient) return;

        // Set visual target for interpolation
        visualTargetState = snapshot;
        needsSmoothing = true;
    }

    // Update owner's physics after collision
    [ClientRpc]
    private void UpdateOwnerAfterCollisionClientRpc(StateSnapshot snapshot, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner) return;

        // Update client-side physics to match server state after collision
        rb.position = snapshot.position;
        rb.rotation = snapshot.rotation;
        rb.linearVelocity = snapshot.velocity;
        rb.angularVelocity = snapshot.angularVelocity;

        // Update local state
        currentState = snapshot;

        // Apply steering and motor
        ApplySteeringToWheels(snapshot.steeringAngle);
        ApplyMotorTorqueToWheels(snapshot.motorTorque);
    }

    // Helper method to get all client IDs except one
    private ulong[] GetAllClientIdsExcept(ulong excludedClientId)
    {
        List<ulong> clientIds = new List<ulong>();
        foreach (KeyValuePair<ulong, NetworkClient> client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Key != excludedClientId)
            {
                clientIds.Add(client.Key);
            }
        }
        return clientIds.ToArray();
    }

    // Apply visual smoothing for remote clients
    private void SmoothRemoteVehicleVisuals()
    {
        if (IsOwner || !needsSmoothing) return;

        // Smoothly update visuals for remote client
        transform.position = Vector3.Lerp(transform.position, visualTargetState.position,
                                        Time.deltaTime * visualSmoothingSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, visualTargetState.rotation,
                                           Time.deltaTime * visualSmoothingSpeed);

        // Apply visual wheel effects
        ApplySteeringToWheels(visualTargetState.steeringAngle);

        // Continue smoothing while the difference is significant
        needsSmoothing =
            Vector3.Distance(transform.position, visualTargetState.position) > 0.01f ||
            Quaternion.Angle(transform.rotation, visualTargetState.rotation) > 0.1f;
    }

    #endregion

    #region Network RPCs

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitialStateServerRpc()
    {
        // Client requests initial state from server
        if (debugMode)
        {
            Debug.Log("[Server] Client requested initial state");
        }

        // Send the current state to the requesting client
        SendInitialStateClientRpc(currentState);
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
        visualTargetState = state;
        currentState = state;

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

        vfxController.UpdateDriftEffects(isDrifting, rearLeftGrounded, rearRightGrounded, canMove);
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        vfxController.CreateExplosion();

        // Handle explosion sound once per client
        SoundManager.Instance.PlayLocalSound(gameObject, SoundManager.SoundEffectType.CarExplosion);

        StartCoroutine(ResetAfterExplosion());
    }

    private IEnumerator ResetAfterExplosion()
    {
        yield return new WaitForSeconds(3f);
    }

    #endregion

    #region Utility Methods

    private void EnableWheelColliders()
    {
        foreach (var wheel in wheelColliders)
        {
            wheel.enabled = true;
        }
    }
    #endregion
}