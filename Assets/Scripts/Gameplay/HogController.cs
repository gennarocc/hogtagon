using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
    [SerializeField] public int handbrakeDriftMultiplier = 5;
    [SerializeField] private float frontLeftRpm;
    [SerializeField] private float velocity;

    [Header("Acceleration and Deceleration")]
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2f;
    [SerializeField, Range(0.1f, 5f)] private float decelerationFactor = 1f;

    [Header("Network Reconciliation")]
    [SerializeField] private float positionErrorThreshold = 1.0f;
    [SerializeField] private float rotationErrorThreshold = 15.0f;
    [SerializeField] private float reconciliationLerpSpeed = 10f;
    [SerializeField] private bool debugMode = false;
    [SerializeField] private int inputBufferSize = 60; // 1 second at 60Hz

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
    private float driftingAxis;
    private bool isTractionLocked = true;
    private float cameraAngle;

    // Wheel friction variables
    private WheelFrictionCurve[] originalWheelFrictions = new WheelFrictionCurve[4];
    private float[] originalExtremumSlips = new float[4];

    // Deterministic physics variables
    private const float FIXED_PHYSICS_TIMESTEP = 0.01666667f; // Exactly 1/60 second
    private float accumulatedTime = 0f;
    private uint currentPhysicsFrame = 0;
    private NetworkVariable<uint> serverPhysicsFrame = new NetworkVariable<uint>(0);

    // Input-sync variables
    private int nextInputSequence = 0;
    private NetworkVariable<int> lastProcessedInputSequence = new NetworkVariable<int>(0);
    private Queue<InputState> pendingInputs = new Queue<InputState>();
    private NetworkVariable<StateSnapshot> authorityState = new NetworkVariable<StateSnapshot>(
        new StateSnapshot(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server); private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);

    // Visual smoothing for remote vehicles
    private Vector3 visualPositionTarget;
    private Quaternion visualRotationTarget;
    private bool needsSmoothing = false;

    #endregion

    #region Network Structs

    public struct InputState : INetworkSerializable
    {
        public int sequenceNumber;
        public double timestamp;
        public uint physicsFrame;
        public float moveInput;
        public float steeringInput;
        public bool handbrakeInput;
        public float deltaTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref sequenceNumber);
            serializer.SerializeValue(ref timestamp);
            serializer.SerializeValue(ref physicsFrame);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref steeringInput);
            serializer.SerializeValue(ref handbrakeInput);
            serializer.SerializeValue(ref deltaTime);
        }
    }

    public struct StateSnapshot : INetworkSerializable
    {
        public int lastProcessedInput;
        public uint physicsFrame;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref lastProcessedInput);
            serializer.SerializeValue(ref physicsFrame);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
        }
    }

    #endregion

    #region Lifecycle Methods

    // One-time setup of physics settings on app startup
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ConfigurePhysicsSettings()
    {
        // Force exact fixed timestep for determinism
        Time.fixedDeltaTime = FIXED_PHYSICS_TIMESTEP;

        // Configure physics parameters for determinism
        Physics.defaultSolverIterations = 6;
        Physics.defaultSolverVelocityIterations = 1;
        Physics.defaultMaxAngularSpeed = 50f;
        Physics.sleepThreshold = 0.005f;
        Physics.defaultContactOffset = 0.01f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Disable physics initially
        rb.isKinematic = true;

        // Initialize immediately for server
        if (IsServer)
        {
            InitializeServerState();
        }
        else
        {
            // Client requests initial state from server
            RequestInitialSyncServerRpc();
            Debug.Log($"[Client] Waiting for initial state from server...");
        }
    }

    private void Start()
    {
        rb.centerOfMass = centerOfMass;
        InitializeWheelFriction();
        // vfxController.Initialize(transform, centerOfMass, OwnerClientId);
        EngineOn.Post(gameObject);

        Debug.Log(transform.root.gameObject.GetComponent<Player>().clientId + " spawned at - " + rb.position);

        // Initialize visual smoothing
        visualPositionTarget = transform.position;
        visualRotationTarget = transform.rotation;
    }

    private void Update()
    {
        // Wait for initial sync
        if (!hasReceivedInitialSync) return;

        // Handle owner input generation
        if (IsOwner)
        {
            CollectAndSendInput();
        }

        // Accumulate time for deterministic physics steps
        accumulatedTime += Time.deltaTime;

        // Process deterministic physics steps
        while (accumulatedTime >= FIXED_PHYSICS_TIMESTEP)
        {
            ProcessPhysicsStep();
            accumulatedTime -= FIXED_PHYSICS_TIMESTEP;
        }

        // Visual updates that don't need fixed timestep
        AnimateWheels();
        UpdateDriftEffects();
        SmoothVisualForRemoteVehicles();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsServer && !collisionForceOnCooldown && collision.gameObject.CompareTag("Player"))
        {
            StartCoroutine(CollisionDebounce());
            SendAuthoritySnapshot();
        }

        HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogImpact);
    }

    #endregion

    #region Initialization

    private void InitializeServerState()
    {
        Debug.Log("[Server] Initializing hog state");

        // Get the Player component to access spawn point
        Player playerComponent = transform.root.GetComponent<Player>();
        Vector3 initialPosition = transform.position; // Default fallback
        Quaternion initialRotation = transform.rotation;

        if (playerComponent != null)
        {
            // Get player data to use spawn point
            if (ConnectionManager.instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData))
            {
                initialPosition = playerData.spawnPoint;
                initialRotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - playerData.spawnPoint);

                // Directly set the transform position for immediate effect
                transform.position = initialPosition;
                transform.rotation = initialRotation;

                Debug.Log($"[Server] Setting initial position to spawn point: {initialPosition}");
            }
            else
            {
                Debug.LogWarning($"[Server] Could not find player data for client {playerComponent.clientId}");
            }
        }

        // Initialize physics 
        rb.position = initialPosition;
        rb.rotation = initialRotation;
        rb.isKinematic = false;

        // Set initial server state
        authorityState.Value = new StateSnapshot
        {
            position = initialPosition,
            rotation = initialRotation,
            velocity = Vector3.zero,
            angularVelocity = Vector3.zero,
            physicsFrame = 0,
            lastProcessedInput = 0
        };

        hasReceivedInitialSync = true;
        serverPhysicsFrame.Value = 0;
        EnableWheelColliders();
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

    #region Deterministic Physics Processing

    private void ProcessPhysicsStep()
    {
        // Increment physics frame counter
        currentPhysicsFrame++;

        if (IsServer)
        {
            // Update server frame counter
            serverPhysicsFrame.Value = currentPhysicsFrame;

            // Process any pending physics on server
            // (Vehicle owner's inputs are already processed when received)
        }
        else if (IsOwner)
        {
            // Process inputs for current physics step
            ProcessOwnerInputs();

            // Check for reconciliation against server state if needed
            CheckReconciliation();
        }
        // Non-owner clients don't simulate physics - they just receive state
    }

    private void ProcessOwnerInputs()
    {
        // Find pending inputs for this frame
        InputState? inputForFrame = null;

        foreach (var input in pendingInputs)
        {
            // Use the most recent input for this frame
            if (input.physicsFrame <= currentPhysicsFrame)
            {
                inputForFrame = input;
            }
        }

        // If we have an input for this frame, apply it
        if (inputForFrame.HasValue)
        {
            ApplyDeterministicPhysics(inputForFrame.Value);
        }
    }

    private void ApplyDeterministicPhysics(InputState input)
    {
        // Save current physics settings
        float prevDeltaTime = Time.fixedDeltaTime;
        Time.fixedDeltaTime = input.deltaTime;

        // Apply exact physics
        float steeringAngle = CalculateSteering(input.steeringInput, input.moveInput);

        // Calculate exact torque with deterministic math
        float targetTorque = Mathf.Clamp(input.moveInput, -1f, 1f) * maxTorque;
        float torqueDelta = input.moveInput != 0 ?
            (input.deltaTime * maxTorque * accelerationFactor) :
            (input.deltaTime * maxTorque * decelerationFactor);

        // Use deterministic MoveTowards
        if (Mathf.Abs(targetTorque - currentTorque) <= torqueDelta)
        {
            currentTorque = targetTorque;
        }
        else
        {
            currentTorque += Mathf.Sign(targetTorque - currentTorque) * torqueDelta;
        }

        // Apply to wheels
        ApplySteeringToWheels(steeringAngle);
        ApplyMotorTorqueToWheels(currentTorque);

        // Handle handbrake with exact physics
        if (input.handbrakeInput)
        {
            ApplyDeterministicHandbrake(input.deltaTime);
        }
        else if (!input.handbrakeInput && !isTractionLocked)
        {
            ApplyDeterministicTractionRecovery(input.deltaTime);
        }

        // Restore physics settings
        Time.fixedDeltaTime = prevDeltaTime;

        // Update local velocity for drift detection
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;

        // Server updates drift state
        if (IsServer)
        {
            isDrifting.Value = localVelocityX > 0.25f;
        }
    }

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

    private void ApplyDeterministicHandbrake(float deltaTime)
    {
        CancelInvoke("RecoverTraction");
        isTractionLocked = false;

        // Deterministic drift calculation
        float newDriftingAxis = driftingAxis + deltaTime;
        float secureStartingPoint = newDriftingAxis * originalExtremumSlips[0] * handbrakeDriftMultiplier;

        if (secureStartingPoint < originalExtremumSlips[0])
        {
            newDriftingAxis = originalExtremumSlips[0] / (originalExtremumSlips[0] * handbrakeDriftMultiplier);
        }

        driftingAxis = Mathf.Min(newDriftingAxis, 1f);

        // Apply to all wheels deterministically
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            WheelFrictionCurve friction = wheelColliders[i].sidewaysFriction;
            friction.extremumSlip = originalExtremumSlips[i] * handbrakeDriftMultiplier * driftingAxis;
            wheelColliders[i].sidewaysFriction = friction;
        }
    }

    private void ApplyDeterministicTractionRecovery(float deltaTime)
    {
        // Determine exact recovery amount
        float newDriftingAxis = driftingAxis - (deltaTime / 1.5f);
        driftingAxis = Mathf.Max(newDriftingAxis, 0f);

        if (driftingAxis > 0f)
        {
            // Apply recovery to all wheels
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                WheelFrictionCurve friction = wheelColliders[i].sidewaysFriction;
                friction.extremumSlip = Mathf.Max(
                    originalExtremumSlips[i],
                    originalExtremumSlips[i] * handbrakeDriftMultiplier * driftingAxis
                );
                wheelColliders[i].sidewaysFriction = friction;
            }

            // Continue recovery next frame
            Invoke("RecoverTraction", deltaTime);
        }
        else
        {
            // Reset friction when fully recovered
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                WheelFrictionCurve friction = wheelColliders[i].sidewaysFriction;
                friction.extremumSlip = originalExtremumSlips[i];
                wheelColliders[i].sidewaysFriction = friction;
            }

            driftingAxis = 0f;
            isTractionLocked = true;
        }
    }

    #endregion

    #region Input Handling

    private void CollectAndSendInput()
    {
        // Handle horn input
        CheckHornInput();

        // Calculate camera angle
        cameraAngle = CalculateCameraAngle();

        // Collect input
        var input = new InputState
        {
            sequenceNumber = nextInputSequence++,
            timestamp = Time.timeAsDouble,
            physicsFrame = currentPhysicsFrame,
            moveInput = GetMovementInput(),
            steeringInput = cameraAngle,
            handbrakeInput = Input.GetKey(KeyCode.Space),
            deltaTime = FIXED_PHYSICS_TIMESTEP
        };

        // Store locally for prediction
        pendingInputs.Enqueue(input);

        // Keep buffer size reasonable
        while (pendingInputs.Count > inputBufferSize)
        {
            pendingInputs.Dequeue();
        }

        // Send to server for processing
        SendInputToServerRpc(input);

        // Process controller camera controls
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

    #region Reconciliation

    private void CheckReconciliation()
    {
        // Only perform reconciliation if we have server state
        if (lastProcessedInputSequence.Value <= 0) return;

        // Skip if no pending inputs
        if (pendingInputs.Count == 0) return;

        // Calculate error between local and server state
        float positionError = Vector3.Distance(rb.position, authorityState.Value.position);
        float rotationError = Quaternion.Angle(rb.rotation, authorityState.Value.rotation);

        // Determine if reconciliation is needed
        if (positionError > positionErrorThreshold || rotationError > rotationErrorThreshold)
        {
            PerformReconciliation();
        }
    }

    private void PerformReconciliation()
    {
        // Log for debugging
        if (debugMode)
        {
            Debug.Log($"Reconciling vehicle. Position error: {Vector3.Distance(rb.position, authorityState.Value.position):F2}, " +
                     $"Rotation error: {Quaternion.Angle(rb.rotation, authorityState.Value.rotation):F2}");
        }

        // Remove already processed inputs
        while (pendingInputs.Count > 0 &&
              pendingInputs.Peek().sequenceNumber <= lastProcessedInputSequence.Value)
        {
            pendingInputs.Dequeue();
        }

        // Reset to server state
        rb.position = authorityState.Value.position;
        rb.rotation = authorityState.Value.rotation;
        rb.linearVelocity = authorityState.Value.velocity;
        rb.angularVelocity = authorityState.Value.angularVelocity;

        // Reapply all pending inputs
        foreach (var input in pendingInputs)
        {
            ApplyDeterministicPhysics(input);
        }
    }

    private void SmoothVisualForRemoteVehicles()
    {
        // Only for non-owner, non-server vehicles
        if (IsOwner || IsServer || !needsSmoothing) return;

        // Apply smoothing to visual representation
        transform.position = Vector3.Lerp(transform.position, visualPositionTarget,
                                         Time.deltaTime * reconciliationLerpSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, visualRotationTarget,
                                             Time.deltaTime * reconciliationLerpSpeed);

        // Check if we're close enough to stop smoothing
        if (Vector3.Distance(transform.position, visualPositionTarget) < 0.01f &&
            Quaternion.Angle(transform.rotation, visualRotationTarget) < 0.1f)
        {
            needsSmoothing = false;
        }
    }

    private void ApplyRemoteState(StateSnapshot state)
    {
        // For non-owner vehicles, set targets for smooth interpolation
        visualPositionTarget = state.position;
        visualRotationTarget = state.rotation;
        needsSmoothing = true;

        // Update rigidbody state directly
        rb.position = state.position;
        rb.rotation = state.rotation;
        rb.linearVelocity = state.velocity;
        rb.angularVelocity = state.angularVelocity;
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

    #region Network RPCs

    [ServerRpc(RequireOwnership = false)]  // Change to not require ownership
    private void SendInputToServerRpc(InputState input, ServerRpcParams rpcParams = default)
    {
        // Validate sender
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (clientId != OwnerClientId) return;

        // Process sequence number
        if (input.sequenceNumber <= lastProcessedInputSequence.Value) return;

        // Process this input immediately on server
        ApplyDeterministicPhysics(input);

        // Update last processed input
        lastProcessedInputSequence.Value = input.sequenceNumber;

        // Update authority state for reconciliation
        SendAuthoritySnapshot();
    }

    private void SendAuthoritySnapshot()
    {
        // Update authority state for clients
        authorityState.Value = new StateSnapshot
        {
            lastProcessedInput = lastProcessedInputSequence.Value,
            physicsFrame = currentPhysicsFrame,
            position = rb.position,
            rotation = rb.rotation,
            velocity = rb.linearVelocity,
            angularVelocity = rb.angularVelocity
        };
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestInitialSyncServerRpc(ServerRpcParams rpcParams = default)
    {
        // Send initial state to all clients (without targeting)
        SendInitialStateClientRpc(authorityState.Value);

        Debug.Log($"[Server] Sending initial state to clients: Position={authorityState.Value.position}");
    }

    [ClientRpc]
    private void SendInitialStateClientRpc(StateSnapshot state)
    {
        if (IsServer || hasReceivedInitialSync) return;

        Debug.Log($"[Client] Received initial state: Position={state.position}, Rotation={state.rotation}");

        // Set position and rotation
        transform.position = state.position;
        transform.rotation = state.rotation;

        // Set physics state
        rb.position = state.position;
        rb.rotation = state.rotation;
        rb.isKinematic = false;

        // Apply velocity
        rb.linearVelocity = state.velocity;
        rb.angularVelocity = state.angularVelocity;

        // Set visual targets
        visualPositionTarget = state.position;
        visualRotationTarget = state.rotation;

        // Enable wheel colliders
        EnableWheelColliders();

        // Mark as initialized
        hasReceivedInitialSync = true;
        currentPhysicsFrame = state.physicsFrame;

        Debug.Log($"[Client] Vehicle initialized at position: {rb.position}");
    }

    [ClientRpc]
    public void BroadcastHardSyncClientRpc()
    {
        if (!IsServer && !IsOwner)
        {
            // For non-owner clients, apply state directly
            ApplyRemoteState(authorityState.Value);
        }
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