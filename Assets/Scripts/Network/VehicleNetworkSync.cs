using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;

// This component handles all networking aspects of the vehicle using Netcode for GameObjects
public class VehicleNetworkSynchronizer : NetworkBehaviour
{
    #region References and Configuration
    [Header("References")]
    [SerializeField] private NetworkVehicleController vehicleController;
    [SerializeField] private Rigidbody vehicleRigidbody;

    [Header("Network Configuration")]
    [SerializeField] private float clientSendRate = 20f; // Updates per second
    [SerializeField] private float serverBroadcastRate = 15f; // Updates per second
    [SerializeField] private float networkInterpolationTime = 0.1f; // Seconds to interpolate
    [SerializeField] private float clientPredictionErrorThreshold = 0.5f; // Meters
    [SerializeField] private int inputBufferSize = 100; // Max stored inputs
    #endregion

    #region State Tracking
    // Network state
    private uint currentTick = 0;
    private uint lastProcessedInputTick = 0;
    private float tickTimer = 0f;
    private float tickDuration = 0.02f; // 50 ticks per second for physics

    // Input handling
    private Queue<InputState> inputBuffer = new Queue<InputState>();
    private InputState lastSentInput;
    private bool inputDirty = false;
    private Coroutine sendInputsRoutine;
    private Coroutine broadcastStateRoutine;

    // State interpolation (for non-owned vehicles)
    private TransformState previousState;
    private TransformState targetState;
    private float interpolationTime = 0f;
    #endregion

    #region Netcode RPCs and Network Variables
    // Network Variable for the most recent transform state (for non-owners)
    private NetworkVariable<TransformState> networkTransformState = new NetworkVariable<TransformState>(
        new TransformState
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,
            velocity = Vector3.zero,
            angularVelocity = Vector3.zero,
            tick = 0
        },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Network Variable for the last processed input tick (for owner)
    private NetworkVariable<uint> networkLastProcessedTick = new NetworkVariable<uint>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    #endregion

    #region Data Structures
    // Compact representation of inputs for network transmission
    public struct InputState : INetworkSerializable
    {
        public uint tick;
        public float throttle;
        public float brake;
        public float steering;
        public bool jumpPressed;

        public static InputState Create(uint tick, float throttle, float brake, float steering, bool jumpPressed)
        {
            return new InputState
            {
                tick = tick,
                throttle = throttle,
                brake = brake,
                steering = steering,
                jumpPressed = jumpPressed
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref throttle);
            serializer.SerializeValue(ref brake);
            serializer.SerializeValue(ref steering);
            serializer.SerializeValue(ref jumpPressed);
        }
    }

    // Compact representation of transform state for network transmission
    public struct TransformState : INetworkSerializable
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public uint tick;

        public static TransformState Create(Transform transform, Rigidbody rb, uint tick)
        {
            return new TransformState
            {
                position = transform.position,
                rotation = transform.rotation,
                velocity = rb.linearVelocity,
                angularVelocity = rb.angularVelocity,
                tick = tick
            };
        }

        public static TransformState Lerp(TransformState a, TransformState b, float t)
        {
            return new TransformState
            {
                position = Vector3.Lerp(a.position, b.position, t),
                rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
                velocity = Vector3.Lerp(a.velocity, b.velocity, t),
                angularVelocity = Vector3.Lerp(a.angularVelocity, b.angularVelocity, t),
                tick = b.tick
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref angularVelocity);
            serializer.SerializeValue(ref tick);
        }
    }
    #endregion

    #region Unity Lifecycle Methods
    private void Awake()
    {
        // Auto-assign references if not set (done in Awake since these don't depend on network state)
        if (vehicleController == null)
            vehicleController = GetComponent<NetworkVehicleController>();

        if (vehicleRigidbody == null)
            vehicleRigidbody = GetComponent<Rigidbody>();

        // Calculate tick duration based on physics timestep
        tickDuration = Time.fixedDeltaTime;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize network state
        if (IsOwner)
        {
            // Owner: Start sending inputs to server
            if (IsClient)
            {
                // Only start sending inputs if we're a client (not the host)
                sendInputsRoutine = StartCoroutine(SendInputsRoutine());
            }

            // Tell vehicle controller it's locally controlled
            vehicleController.SetLocallyControlled(true);

            Debug.Log($"Vehicle spawned as owner. IsServer: {IsServer}, IsClient: {IsClient}");
        }
        else
        {
            // Non-owner: Disable local input processing
            vehicleController.SetLocallyControlled(false);

            // Initialize interpolation states
            previousState = networkTransformState.Value;
            targetState = networkTransformState.Value;

            Debug.Log("Vehicle spawned as remote");
        }

        // Server: Start broadcasting state
        if (IsServer)
        {
            broadcastStateRoutine = StartCoroutine(BroadcastStateRoutine());
        }

        // Subscribe to network variable changes
        networkTransformState.OnValueChanged += OnTransformStateChanged;
        networkLastProcessedTick.OnValueChanged += OnLastProcessedTickChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Clean up coroutines
        if (sendInputsRoutine != null)
            StopCoroutine(sendInputsRoutine);

        if (broadcastStateRoutine != null)
            StopCoroutine(broadcastStateRoutine);

        // Unsubscribe from network variable changes
        networkTransformState.OnValueChanged -= OnTransformStateChanged;
        networkLastProcessedTick.OnValueChanged -= OnLastProcessedTickChanged;
    }

    private void Update()
    {
        // Track time for deterministic tick counting
        tickTimer += Time.deltaTime;

        // Process multiple ticks if needed to keep up with frame rate
        while (tickTimer >= tickDuration)
        {
            tickTimer -= tickDuration;
            currentTick++;

            // Process network tick (collect input or interpolate state)
            NetworkTick();
        }
    }
    #endregion

    #region Network Methods
    private void NetworkTick()
    {
        if (IsSpawned)
        {
            if (IsOwner)
            {
                // Owner: Collect and buffer input
                CollectInput();
            }
            else
            {
                // Non-owner: Interpolate between received states
                InterpolateNetworkState();
            }
        }
    }

    private void CollectInput()
    {
        // Get input from vehicle's input manager
        var inputManager = vehicleController.GetInputManager();

        if (inputManager != null && inputManager.IsInGameplayMode())
        {
            // Collect current input state
            float throttle = inputManager.ThrottleInput;
            float brake = inputManager.BrakeInput;
            Vector2 lookInput = inputManager.LookInput;

            // Calculate steering from look input
            float steeringValue = vehicleController.CalculateSteeringInputFromLook(lookInput);

            // Check for jump
            bool jumpPressed = inputManager.WasJumpPressed();

            // Create quantized input state
            InputState input = InputState.Create(
                currentTick,
                PhysicsQuantizer.QInput(throttle),
                PhysicsQuantizer.QInput(brake),
                PhysicsQuantizer.QInput(steeringValue),
                jumpPressed
            );

            // Queue this input
            inputBuffer.Enqueue(input);

            // Mark that we have new input to send
            if (!inputDirty ||
                input.throttle != lastSentInput.throttle ||
                input.brake != lastSentInput.brake ||
                input.steering != lastSentInput.steering ||
                input.jumpPressed != lastSentInput.jumpPressed)
            {
                inputDirty = true;
                lastSentInput = input;
            }

            // Apply input locally (for prediction)
            vehicleController.ApplyNetworkInput(input.throttle, input.brake, input.steering, input.jumpPressed);

            // If we're the server (host), process input directly
            if (IsServer)
            {
                ProcessInputServerRpc(input);
            }

            // Keep buffer size in check
            while (inputBuffer.Count > inputBufferSize)
            {
                inputBuffer.Dequeue();
            }
        }
    }

    private IEnumerator SendInputsRoutine()
    {
        while (IsSpawned && IsOwner && IsClient && !IsServer)
        {
            yield return new WaitForSeconds(1f / clientSendRate);

            if (inputDirty && inputBuffer.Count > 0)
            {
                // Send latest input to server
                ProcessInputServerRpc(lastSentInput);

                // Mark as sent
                inputDirty = false;
            }
        }
    }

    private IEnumerator BroadcastStateRoutine()
    {
        while (IsSpawned && IsServer)
        {
            yield return new WaitForSeconds(1f / serverBroadcastRate);

            // Update network transform state
            networkTransformState.Value = TransformState.Create(
                transform,
                vehicleRigidbody,
                currentTick
            );

            // Update last processed tick
            networkLastProcessedTick.Value = lastProcessedInputTick;
        }
    }

    private void InterpolateNetworkState()
    {
        // Non-owner vehicles interpolate between received network states
        interpolationTime += Time.deltaTime;
        float t = Mathf.Clamp01(interpolationTime / networkInterpolationTime);

        // Interpolate transform
        TransformState interpolatedState = TransformState.Lerp(previousState, targetState, t);

        // Apply interpolated state to vehicle
        transform.position = interpolatedState.position;
        transform.rotation = interpolatedState.rotation;
        vehicleRigidbody.linearVelocity = interpolatedState.velocity;
        vehicleRigidbody.angularVelocity = interpolatedState.angularVelocity;
    }

    // Called when the networkTransformState changes (for non-owner vehicles)
    private void OnTransformStateChanged(TransformState previousValue, TransformState newValue)
    {
        if (!IsOwner)
        {
            // Store previous target as our new starting point
            previousState = targetState;

            // Set new target state
            targetState = newValue;

            // Reset interpolation timer
            interpolationTime = 0f;
        }
    }

    // Called when the lastProcessedTick changes (for owner vehicle)
    private void OnLastProcessedTickChanged(uint previousValue, uint newValue)
    {
        if (IsOwner && !IsServer)
        {
            // Handle server correction/reconciliation
            ServerReconciliation(newValue);
        }
    }

    private void ServerReconciliation(uint serverProcessedTick)
    {
        // Set the latest server-processed tick
        lastProcessedInputTick = serverProcessedTick;

        // Remove inputs that have been processed by the server
        while (inputBuffer.Count > 0 && inputBuffer.Peek().tick <= lastProcessedInputTick)
        {
            inputBuffer.Dequeue();
        }

        // Calculate the error between our predicted position and server position
        float positionError = Vector3.Distance(transform.position, networkTransformState.Value.position);
        float rotationError = Quaternion.Angle(transform.rotation, networkTransformState.Value.rotation);

        if (positionError > clientPredictionErrorThreshold || rotationError > 10f)
        {
            Debug.Log($"Reconciling: Position error = {positionError}, Rotation error = {rotationError}");

            // Error too large, apply correction
            transform.position = networkTransformState.Value.position;
            transform.rotation = networkTransformState.Value.rotation;
            vehicleRigidbody.linearVelocity = networkTransformState.Value.velocity;
            vehicleRigidbody.angularVelocity = networkTransformState.Value.angularVelocity;

            // Ask vehicle controller to replay inputs that haven't been processed yet
            foreach (var input in inputBuffer)
            {
                vehicleController.ApplyNetworkInput(
                    input.throttle,
                    input.brake,
                    input.steering,
                    input.jumpPressed
                );

                // Simulate physics for each input
                vehicleController.SimulatePhysicsStep();
            }
        }
    }
    #endregion

    #region RPCs
    [ServerRpc]
    private void ProcessInputServerRpc(InputState input, ServerRpcParams rpcParams = default)
    {
        // Only process inputs with newer tick counts
        if (input.tick <= lastProcessedInputTick)
            return;

        // Apply input on server
        vehicleController.ApplyNetworkInput(input.throttle, input.brake, input.steering, input.jumpPressed);

        // Update last processed tick
        lastProcessedInputTick = input.tick;
    }
    #endregion

    #region Debug
    public int GetCurrentTick()
    {
        return (int)currentTick;
    }

    public int GetLastProcessedTick()
    {
        return (int)lastProcessedInputTick;
    }

    public int GetInputBufferSize()
    {
        return inputBuffer.Count;
    }

    public int GetCurrentPing()
    {
        // Get ping from NGO's NetworkManager
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            return (int)(NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(0) * 1000);
        }

        return 0;
    }

    #endregion
}