using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class HogController : NetworkBehaviour
{
    #region Variables

    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f;
    [SerializeField] private float brakeTorque = 300f;
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .7f;
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2f;
    [SerializeField, Range(0.1f, 5f)] private float decelerationFactor = 1f;

    [Header("Network")]
    [SerializeField] private float serverValidationThreshold = 5.0f;  // How far client can move before server corrections
    [SerializeField] private float clientUpdateInterval = 0.05f;      // How often client sends updates (20Hz)
    [SerializeField] private float stateUpdateInterval = 0.1f;        // How often server broadcasts state (10Hz)
    [SerializeField] private bool debugMode = false;

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CinemachineFreeLook freeLookCamera;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
    [SerializeField] private Transform[] wheelMeshes = new Transform[4];
    // [SerializeField] private HogVisualEffects vfxController;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event EngineOn;
    [SerializeField] private AK.Wwise.Event EngineOff;
    [SerializeField] private AK.Wwise.RTPC rpm;

    // State tracking
    private bool hasReceivedInitialSync = false;
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private float cameraAngle;

    // Wheel friction variables
    private WheelFrictionCurve[] originalWheelFrictions = new WheelFrictionCurve[4];
    private float[] originalExtremumSlips = new float[4];

    // Network variables
    private float _stateUpdateTimer = 0f;
    private float _clientUpdateTimer = 0f;
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<StateSnapshot> vehicleState = new NetworkVariable<StateSnapshot>(
        new StateSnapshot(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // Variables for non-owner clients
    private Vector3 visualPositionTarget;
    private Quaternion visualRotationTarget;
    private Vector3 visualVelocityTarget;
    private bool needsSmoothing = false;
    [SerializeField] private float visualSmoothingSpeed = 10f;

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

        // Initialize the vehicle
        if (IsOwner)
        {
            // Owner initializes immediately
            InitializeOwnerVehicle();
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

    private void Start()
    {
        rb.centerOfMass = centerOfMass;
        InitializeWheelFriction();

        if (EngineOn != null)
        {
            EngineOn.Post(gameObject);
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
        // Handle collisions
        if (collision.gameObject.CompareTag("Player"))
        {
            // Play sound effect
            HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogImpact);

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
            if (ConnectionManager.instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData))
            {
                Vector3 initialPosition = playerData.spawnPoint;
                Quaternion initialRotation = Quaternion.LookRotation(
                    SpawnPointManager.instance.transform.position - playerData.spawnPoint);

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
        // Handle horn input
        CheckHornInput();

        // Get movement input
        float moveInput = GetMovementInput();

        // Get steering input
        cameraAngle = CalculateCameraAngle();
        float steeringInput = cameraAngle;

        // Calculate steering angle
        float steeringAngle = CalculateSteering(steeringInput, moveInput);

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
        isDrifting.Value = localVelocityX > 0.25f;

        // Process camera controls
        UpdateCameraControls();
    }

    private void CheckHornInput()
    {
        if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton0)) &&
            !transform.root.gameObject.GetComponent<Player>().isSpectating)
        {
            HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogHorn);
        }
    }

    private float CalculateCameraAngle()
    {
        Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
        cameraVector.y = 0;
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
        return Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));
    }

    private float GetMovementInput()
    {
        float move = 0;

        // Keyboard input
        if (Input.GetKey(KeyCode.W)) move = 1f;
        if (Input.GetKey(KeyCode.S)) move = -1f;

        // Controller input
        if (Input.GetJoystickNames().Length > 0)
        {
            float rightTrigger = Input.GetAxis("XRI_Right_Trigger");
            float leftTrigger = Input.GetAxis("XRI_Left_Trigger");

            if (rightTrigger != 0) move = -rightTrigger;
            if (leftTrigger != 0) move = leftTrigger;
        }

        return move;
    }

    private void UpdateCameraControls()
    {
        // Process controller camera control with right stick
        if (Input.GetJoystickNames().Length > 0)
        {
            float rightStickX = Input.GetAxis("XRI_Right_Primary2DAxis_Horizontal");
            float rightStickY = Input.GetAxis("XRI_Right_Primary2DAxis_Vertical");

            if (rightStickX != 0)
            {
                freeLookCamera.m_XAxis.Value += rightStickX * 5 / 2;
            }
            if (rightStickY != 0)
            {
                freeLookCamera.m_YAxis.Value += rightStickY;
            }
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
            if (ConnectionManager.instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData))
            {
                // Set respawn position and rotation
                Vector3 respawnPosition = playerData.spawnPoint;
                Quaternion respawnRotation = Quaternion.LookRotation(
                    SpawnPointManager.instance.transform.position - playerData.spawnPoint);

                // Update player state
                playerData.state = PlayerState.Alive;
                ConnectionManager.instance.UpdatePlayerDataClientRpc(playerComponent.clientId, playerData);

                // Execute respawn for everyone
                ExecuteRespawnClientRpc(respawnPosition, respawnRotation);
            }
        }
    }

    [ClientRpc]
    public void ExecuteRespawnClientRpc(Vector3 respawnPosition, Quaternion respawnRotation)
    {
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

        // Enable movement
        canMove = true;

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

        // vfxController.UpdateDriftEffects(isDrifting.Value, rearLeftGrounded, rearRightGrounded, canMove);
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        canMove = false;
        // vfxController.CreateExplosion(canMove);

        StartCoroutine(ResetAfterExplosion());
    }

    private IEnumerator ResetAfterExplosion()
    {
        yield return new WaitForSeconds(3f);
        canMove = true;
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

    private void DisableWheelColliders()
    {
        foreach (var wheel in wheelColliders)
        {
            wheel.enabled = false;
        }
    }

    #endregion
}