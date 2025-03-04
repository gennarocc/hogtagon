using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;

public class HogController : NetworkBehaviour
{
    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f; // Maximum torque applied to wheels
    [SerializeField] private float brakeTorque = 300f; // Brake torque applied to wheels
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f; // Default maximum steering angle
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .7f;
    [SerializeField] public int handbrakeDriftMultiplier = 5; // How much grip the car loses when the user hit the handbrake.
    [SerializeField] public float cameraAngle;
    [SerializeField] private float frontLeftRpm;
    [SerializeField] private float velocity;
    
    [Header("Acceleration and Deceleration")]
    [SerializeField, Range(0.1f, 10f)] private float accelerationFactor = 2f; // How quickly the car reaches max torque
    [SerializeField, Range(0.1f, 5f)] private float decelerationFactor = 1f; // How quickly the car slows down with no input

    [Header("Networking and Synchronization")]
    [SerializeField] private float positionCorrectionThreshold = 3.0f; // More lenient distance threshold
    [SerializeField] private float rotationCorrectionThreshold = 60.0f; // More lenient rotation threshold
    [SerializeField] private float correctionLerpSpeed = 2.5f; // Slower correction speed
    [SerializeField] private float authoritySyncInterval = 0.2f; // Less frequent sync
    [SerializeField] private bool applyVelocityCorrection = false; // Disable velocity correction by default
    [SerializeField] private float heightEmergencyThreshold = -10f; // Only trigger emergency at severe heights

    [Header("References")]
    [SerializeField] private Rigidbody rb; // Reference to the car's Rigidbody
    [SerializeField] private CinemachineFreeLook freeLookCamera; // Reference to the CinemachineFreeLook camera
    [SerializeField] private WheelCollider frontLeftWheelCollider;
    [SerializeField] private WheelCollider frontRightWheelCollider;
    [SerializeField] private WheelCollider rearLeftWheelCollider;
    [SerializeField] private WheelCollider rearRightWheelCollider;
    [SerializeField] private Transform frontLeftWheelTransform;
    [SerializeField] private Transform frontRightWheelTransform;
    [SerializeField] private Transform rearLeftWheelTransform;
    [SerializeField] private Transform rearRightWheelTransform;

    [Header("Effects")]
    [SerializeField] public ParticleSystem RLWParticleSystem;
    [SerializeField] public ParticleSystem RRWParticleSystem;
    [SerializeField] public TrailRenderer RLWTireSkid;
    [SerializeField] public TrailRenderer RRWTireSkid;
    [SerializeField] public GameObject Explosion;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event EngineOn;
    [SerializeField] private AK.Wwise.Event EngineOff;
    [SerializeField] private AK.Wwise.RTPC rpm;

    // State management for initial sync
    private bool hasReceivedInitialSync = false;
    private NetworkVariable<bool> isReadyForSync = new NetworkVariable<bool>(false);
    private float lastAuthoritySyncTime;
    
    // Network variables for position synchronization
    private NetworkVariable<Vector3> authorityPosition = new NetworkVariable<Vector3>();
    private NetworkVariable<Quaternion> authorityRotation = new NetworkVariable<Quaternion>();
    private NetworkVariable<Vector3> authorityVelocity = new NetworkVariable<Vector3>(Vector3.zero);
    private NetworkVariable<Vector3> authorityAngularVelocity = new NetworkVariable<Vector3>(Vector3.zero);
    private NetworkVariable<float> authoritySpeed = new NetworkVariable<float>(0f);
    
    // Network variables for input synchronization
    private NetworkVariable<ClientInput> latestInput = new NetworkVariable<ClientInput>();
    private NetworkVariable<int> inputSequence = new NetworkVariable<int>(0);
    private int localInputSequence = 0;
    
    // Correction control variables
    private bool receivedCorrection = false;
    private Vector3 correctionPositionTarget;
    private Quaternion correctionRotationTarget;
    private Vector3 correctionVelocityTarget;
    private Vector3 correctionAngularVelocityTarget;
    private float correctionStartTime;
    private float correctionDuration = 0.5f; // Duration for smooth interpolation
    private Vector3 correctionStartPosition;
    private Quaternion correctionStartRotation;
    private Vector3 correctionStartVelocity;
    private Vector3 correctionStartAngularVelocity;
    private float correctionIntensity = 0f; // 0-1 value for gradually increasing correction

    // State variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private float driftingAxis;
    private bool isTractionLocked = true;
    private bool driftingSoundOn = false;

    // Wheel friction variables
    private WheelFrictionCurve FLwheelFriction;
    private float FLWextremumSlip;
    private WheelFrictionCurve FRwheelFriction;
    private float FRWextremumSlip;
    private WheelFrictionCurve RLwheelFriction;
    private float RLWextremumSlip;
    private WheelFrictionCurve RRwheelFriction;
    private float RRWextremumSlip;

    // Input struct with sequence number and timestamp
    public struct ClientInput : INetworkSerializable
    {
        public ulong clientId;
        public float moveInput;
        public float brakeInput;
        public bool handbrake;
        public float rawCameraAngle;
        public int sequence; // Added sequence number
        public float timestamp; // Added timestamp for better prediction

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref handbrake);
            serializer.SerializeValue(ref rawCameraAngle);
            serializer.SerializeValue(ref sequence);
            serializer.SerializeValue(ref timestamp);
        }
    }

    // Override OnNetworkSpawn to ensure proper initialization
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Make sure physics are properly initialized
        rb.isKinematic = false;
        
        if (IsServer)
        {
            // Server sets initial authority position immediately
            authorityPosition.Value = transform.position;
            authorityRotation.Value = transform.rotation;
            authorityVelocity.Value = Vector3.zero;
            authorityAngularVelocity.Value = Vector3.zero;
            authoritySpeed.Value = 0f;
            
            // Create an initial blank input
            ClientInput initialInput = new ClientInput
            {
                clientId = OwnerClientId,
                moveInput = 0f,
                brakeInput = 0f,
                handbrake = false,
                rawCameraAngle = 0f,
                sequence = 0,
                timestamp = Time.time
            };
            
            latestInput.Value = initialInput;
            
            // Server is always ready
            hasReceivedInitialSync = true;
        }
        else
        {
            // Client notifies server it's ready for sync
            RequestInitialSyncServerRpc();
        }
        
        // Ensure wheel colliders are properly enabled
        EnableWheelColliderPhysics();
    }

    private void Start()
    {
        // All clients need physics for prediction
        rb.centerOfMass = centerOfMass;
        InitializeWheelFriction();

        EngineOn.Post(gameObject);
    }

    private void Update()
    {
        // Wait for initial sync before processing input
        if (!hasReceivedInitialSync)
            return;
            
        // Only the owner generates inputs
        if (IsOwner)
        {
            if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton0)) && !transform.root.gameObject.GetComponent<Player>().isSpectating)
            {
                // Play Horn Sound
                HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogHorn);
            }
            
            // Generate player input only as owner
            GenerateInput();
        }
        
        // Check for authority correction
        if (receivedCorrection)
        {
            ApplyAuthorityCorrection();
        }
    }

    private void FixedUpdate()
    {
        // Wait for initial sync before processing physics
        if (!hasReceivedInitialSync)
            return;
        
        // Update telemetry values
        frontLeftRpm = frontLeftWheelCollider.rpm;
        rpm.SetGlobalValue(frontLeftWheelCollider.rpm);
        velocity = rb.linearVelocity.magnitude;
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;
        
        // All clients process physics for all vehicles
        ProcessPhysics();
        
        // Only server sends authority updates
        if (IsServer)
        {
            if (Time.time - lastAuthoritySyncTime > authoritySyncInterval)
            {
                SendAuthorityUpdate();
                lastAuthoritySyncTime = Time.time;
            }
        }
        
        // Visual updates
        AnimateWheels();
        DriftCarFX();
    }
    
    private void ProcessPhysics()
    {
        // Make sure we have a valid Rigidbody
        if (rb == null) return;
        
        // Use the latest network input value to simulate physics
        ClientInput currentInput = latestInput.Value;
        
        // Check if vehicle is under terrain - apply correction if needed
        if (transform.position.y < heightEmergencyThreshold && !IsServer)
        {
            // Emergency correction to authority position
            ApplyEmergencyCorrection();
            return;
        }
        
        // Only process if we have a valid input
        if (currentInput.clientId == OwnerClientId || inputSequence.Value > 0)
        {
            // Calculate steering angle 
            float steeringAngle = CalculateSteeringFromCameraAngle(currentInput.rawCameraAngle, currentInput.moveInput);
            
            // Calculate torque based on input
            float targetTorque = Mathf.Clamp(currentInput.moveInput, -1f, 1f) * maxTorque;
            
            // Apply acceleration/deceleration
            if (Mathf.Abs(currentInput.moveInput) > 0.01f)
            {
                // Apply acceleration factor
                currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.deltaTime * maxTorque * accelerationFactor);
            }
            else
            {
                // No input - gradually decelerate
                currentTorque = Mathf.MoveTowards(currentTorque, 0f, Time.deltaTime * maxTorque * decelerationFactor);
            }
            
            // Apply physics
            ApplyMotorTorque(currentTorque);
            ApplySteering(steeringAngle);
            
            // Handle handbrake
            if (currentInput.handbrake)
            {
                Handbrake();
                if (IsServer) isDrifting.Value = true;
            }
            else if (!currentInput.handbrake && !isTractionLocked)
            {
                RecoverTraction();
            }
            
            // Update drift state (server is authoritative for this)
            if (IsServer)
            {
                isDrifting.Value = localVelocityX > .25f ? true : false;
            }
        }
    }
    
    private void GenerateInput()
    {
        // Calculate raw camera angle
        Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
        cameraVector.y = 0;
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
        float rawCameraAngle = Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));
        
        // Store for debugging
        cameraAngle = rawCameraAngle;
        
        // Get input values
        float moveInput = GetMoveInput();
        float brakeInput = 0f; // Not using this currently
        bool handbrakeInput = GetHandbrakeInput();
        
        // Increment sequence
        localInputSequence++;
        
        // Create input struct
        ClientInput input = new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = moveInput,
            brakeInput = brakeInput,
            handbrake = handbrakeInput,
            rawCameraAngle = rawCameraAngle,
            sequence = localInputSequence,
            timestamp = Time.time
        };
        
        // Process controller camera controls
        ProcessCameraControls();
        
        // Send to server
        SendInputServerRpc(input);
    }
    
    private void ProcessCameraControls()
    {
        // Controller camera control with right stick
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
        
        // Controller input
        if (Input.GetJoystickNames().Length > 0)
        {
            float rightTrigger = Input.GetAxis("XRI_Right_Trigger");
            float leftTrigger = Input.GetAxis("XRI_Left_Trigger");

            if (rightTrigger != 0)
            {
                move = -rightTrigger;
            }
            if (leftTrigger != 0)
            {
                move = leftTrigger;
            }
        }
        
        return move;
    }
    
    private bool GetHandbrakeInput()
    {
        return Input.GetKey(KeyCode.Space);
    }
    
    private void SendAuthorityUpdate()
    {
        authorityPosition.Value = rb.position;
        authorityRotation.Value = rb.rotation;
        authorityVelocity.Value = rb.linearVelocity;
        authorityAngularVelocity.Value = rb.angularVelocity;
        authoritySpeed.Value = rb.linearVelocity.magnitude;
        
        // Broadcast to all clients
        SendAuthorityCorrectionClientRpc();
    }
    
    [ClientRpc]
    private void SendAuthorityCorrectionClientRpc()
    {
        // Skip on server
        if (IsServer) return;
        
        // If client is severely misplaced (below terrain), apply immediate correction
        if (transform.position.y < heightEmergencyThreshold)
        {
            ApplyEmergencyCorrection();
            return;
        }
        
        // Calculate discrepancy between client and server state
        float positionDelta = Vector3.Distance(rb.position, authorityPosition.Value);
        float rotationDelta = Quaternion.Angle(rb.rotation, authorityRotation.Value);
        
        // Owner gets more leeway than observers
        float posThreshold = IsOwner ? positionCorrectionThreshold : positionCorrectionThreshold * 0.7f;
        float rotThreshold = IsOwner ? rotationCorrectionThreshold : rotationCorrectionThreshold * 0.7f;
        
        // Gradually increase correction intensity based on how far off we are
        if (positionDelta > posThreshold || rotationDelta > rotThreshold)
        {
            // Calculate correction intensity (0-1) based on how far beyond threshold
            float posIntensity = Mathf.Clamp01((positionDelta - posThreshold) / (posThreshold * 2));
            float rotIntensity = Mathf.Clamp01((rotationDelta - rotThreshold) / (rotThreshold * 2));
            
            // Use the higher intensity
            correctionIntensity = Mathf.Max(posIntensity, rotIntensity, correctionIntensity);
            
            // Only start a new correction if we don't have one already or if we're way off
            if (!receivedCorrection || positionDelta > posThreshold * 3 || rotationDelta > rotThreshold * 3)
            {
                StartSmoothedCorrection();
            }
        }
        else
        {
            // We're close enough, gradually reduce correction intensity
            correctionIntensity = Mathf.Max(0, correctionIntensity - (Time.deltaTime * 0.5f));
        }
    }
    
    private void StartSmoothedCorrection()
    {
        // Store current state as starting point
        correctionStartPosition = rb.position;
        correctionStartRotation = rb.rotation;
        correctionStartVelocity = rb.linearVelocity;
        correctionStartAngularVelocity = rb.angularVelocity;
        
        // Store target state
        correctionPositionTarget = authorityPosition.Value;
        correctionRotationTarget = authorityRotation.Value;
        correctionVelocityTarget = authorityVelocity.Value;
        correctionAngularVelocityTarget = authorityAngularVelocity.Value;
        
        // Start correction
        correctionStartTime = Time.time;
        receivedCorrection = true;
    }
    
    private void ApplyEmergencyCorrection()
    {
        // Teleport to authority position for severe cases
        rb.position = authorityPosition.Value;
        rb.rotation = authorityRotation.Value;
        
        // Don't override velocity for owner unless explicitly enabled
        if (!IsOwner || applyVelocityCorrection)
        {
            rb.linearVelocity = authorityVelocity.Value;
            rb.angularVelocity = authorityAngularVelocity.Value;
        }
        
        receivedCorrection = false;
    }
    
    private void ApplyAuthorityCorrection()
    {
        if (!receivedCorrection) return;
        
        // Calculate interpolation factor (0-1)
        float timeSinceCorrection = Time.time - correctionStartTime;
        float t = Mathf.Clamp01(timeSinceCorrection / correctionDuration);
        
        // Use smoothstep for more natural easing
        float smoothT = t * t * (3f - 2f * t);
        
        // Apply position correction with intensity-based lerping
        Vector3 targetPos = Vector3.Lerp(correctionStartPosition, correctionPositionTarget, smoothT);
        rb.position = Vector3.Lerp(rb.position, targetPos, correctionIntensity * correctionLerpSpeed * Time.deltaTime);
        
        // Apply rotation correction with intensity-based slerping
        Quaternion targetRot = Quaternion.Slerp(correctionStartRotation, correctionRotationTarget, smoothT);
        rb.rotation = Quaternion.Slerp(rb.rotation, targetRot, correctionIntensity * correctionLerpSpeed * Time.deltaTime);
        
        // Only apply velocity correction if explicitly enabled and for non-owners
        if (applyVelocityCorrection && !IsOwner)
        {
            // Only override velocity for non-owner vehicles or emergency cases
            if (correctionIntensity > 0.8f)
            {
                // Only for severe corrections, directly set velocity
                rb.linearVelocity = authorityVelocity.Value;
                rb.angularVelocity = authorityAngularVelocity.Value;
            }
        }
        
        // If interpolation is complete, end correction
        if (t >= 1.0f)
        {
            receivedCorrection = false;
        }
    }

    [ServerRpc]
    private void RequestInitialSyncServerRpc(ServerRpcParams serverRpcParams = default)
    {
        // Get the client ID that sent the request
        ulong clientId = serverRpcParams.Receive.SenderClientId;
        
        // Send immediate state to the requesting client
        SendInitialStateClientRpc(
            transform.position,
            transform.rotation,
            rb.linearVelocity,
            rb.angularVelocity,
            rb.linearVelocity.magnitude,
            latestInput.Value,
            inputSequence.Value,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            }
        );
    }
    
    [ClientRpc]
    private void SendInitialStateClientRpc(Vector3 position, Quaternion rotation, Vector3 velocity, 
                                         Vector3 angularVelocity, float speed, ClientInput input, int sequence,
                                         ClientRpcParams clientRpcParams = default)
    {
        // Skip if we're the server or already synced
        if (IsServer || hasReceivedInitialSync)
            return;
            
        // Set initial state
        rb.position = position;
        rb.rotation = rotation;
        
        // Only apply velocity to non-owned vehicles
        if (!IsOwner)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;
        }
        
        // Mark as having received initial sync
        hasReceivedInitialSync = true;
        
        Debug.Log($"Received initial sync for vehicle ID {OwnerClientId}");
    }
    
    // When a client connects after players are already in game
    public void OnPlayerJoinedLate(ulong newClientId)
    {
        // Only the server should handle this
        if (!IsServer)
            return;
            
        // Send current state to the new client
        SendInitialStateClientRpc(
            transform.position,
            transform.rotation,
            rb.linearVelocity,
            rb.angularVelocity,
            rb.linearVelocity.magnitude,
            latestInput.Value,
            inputSequence.Value,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { newClientId }
                }
            }
        );
    }

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        // Only accept inputs from the vehicle owner
        if (input.clientId != OwnerClientId) return;
        
        // Server validates and authorizes input
        if (input.sequence > inputSequence.Value)
        {
            // Accept new input - update the network variable for all clients
            latestInput.Value = input;
            inputSequence.Value = input.sequence;
            
            // Server processes physics immediately
            ProcessPhysics();
        }
    }
    
    // Calculate steering angle from camera angle
    private float CalculateSteeringFromCameraAngle(float cameraAngle, float moveInput)
    {
        // Check if car is actually moving in reverse (based on velocity, not just input)
        float forwardVelocity = Vector3.Dot(rb.linearVelocity, transform.forward);
        bool isMovingInReverse = forwardVelocity < -0.5f;

        // Determine if we should apply reverse steering logic
        bool shouldUseReverseControls = isMovingInReverse && moveInput < 0;

        // For reverse, we invert the steering direction
        if (shouldUseReverseControls)
        {
            cameraAngle = -cameraAngle;

            // Add hysteresis - if angle is around 90 degrees, maintain the current steering angle
            // This prevents rapid changes when looking from the side
            if (Mathf.Abs(cameraAngle) > 70f && Mathf.Abs(cameraAngle) < 110f)
            {
                // Get the current steering angle and maintain it with some smoothing
                float currentSteeringAverage = (frontLeftWheelCollider.steerAngle + frontRightWheelCollider.steerAngle) / 2f;

                // Only make small adjustments when in this "stability zone"
                cameraAngle = Mathf.Lerp(cameraAngle, currentSteeringAverage, 0.8f);
            }
        }

        // Check if camera is behind the car based on maxSteeringAngle
        // Camera is behind if it's in the region: (180-maxSteeringAngle) to (-180+maxSteeringAngle)
        bool isCameraBehind = Mathf.Abs(cameraAngle) > (180f - maxSteeringAngle);

        float finalSteeringAngle;
        if (isCameraBehind)
        {
            // When camera is in the "behind" region, calculate how far into that region
            float behindFactor;

            if (cameraAngle > 0)
            {
                // Positive angle (right side behind region)
                behindFactor = (180f - cameraAngle) / maxSteeringAngle;
            }
            else
            {
                // Negative angle (left side behind region)
                behindFactor = (180f + cameraAngle) / maxSteeringAngle;
            }

            behindFactor = Mathf.Clamp01(behindFactor);

            // Calculate steering angle that decreases as we go deeper into behind region
            if (cameraAngle > 0)
            {
                finalSteeringAngle = maxSteeringAngle * behindFactor;
            }
            else
            {
                finalSteeringAngle = -maxSteeringAngle * behindFactor;
            }
        }
        else
        {
            // Normal steering - within normal range
            finalSteeringAngle = Mathf.Clamp(cameraAngle, -maxSteeringAngle, maxSteeringAngle);
        }

        return finalSteeringAngle;
    }

    private void ApplyMotorTorque(float torqueValue)
    {
        if (!canMove)
        {
            frontLeftWheelCollider.motorTorque = 0;
            frontRightWheelCollider.motorTorque = 0;
            rearLeftWheelCollider.motorTorque = 0;
            rearRightWheelCollider.motorTorque = 0;
            return;
        }

        // Apply the calculated torque to the wheels
        frontLeftWheelCollider.motorTorque = torqueValue;
        frontRightWheelCollider.motorTorque = torqueValue;
        rearLeftWheelCollider.motorTorque = torqueValue;
        rearRightWheelCollider.motorTorque = torqueValue;
    }

    private void ApplySteering(float steeringAngle)
    {
        // Use only 35% of steering angle for rear wheels (in opposite direction)
        float rearSteeringAngle = steeringAngle * -0.35f;

        // Apply smoothed steering to wheel colliders
        frontLeftWheelCollider.steerAngle = Mathf.Lerp(frontLeftWheelCollider.steerAngle, steeringAngle, steeringSpeed);
        frontRightWheelCollider.steerAngle = Mathf.Lerp(frontRightWheelCollider.steerAngle, steeringAngle, steeringSpeed);
        rearLeftWheelCollider.steerAngle = Mathf.Lerp(rearLeftWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
        rearRightWheelCollider.steerAngle = Mathf.Lerp(rearRightWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
    }

    private void AnimateWheels()
    {
        // Check actual car movement direction
        float forwardVelocity = Vector3.Dot(rb.linearVelocity, transform.forward);
        bool isMovingInReverse = forwardVelocity < -0.5f;

        // Get current input direction
        bool isPressingReverse = false;
        if (IsOwner)
        {
            isPressingReverse = Input.GetKey(KeyCode.S) || (Input.GetJoystickNames().Length > 0 && Input.GetAxis("XRI_Right_Trigger") != 0);
        }

        // Only use reverse controls when both moving in reverse and pressing reverse
        bool shouldUseReverseControls = isMovingInReverse && isPressingReverse;

        // For reverse, invert the steering direction
        float adjustedCameraAngle = shouldUseReverseControls ? -cameraAngle : cameraAngle;

        // Check if camera is behind - same logic as in ApplySteering
        bool isCameraBehind = Mathf.Abs(adjustedCameraAngle) > (180f - maxSteeringAngle);

        float frontSteeringAngle;
        if (isCameraBehind)
        {
            // Calculate how far into the behind region
            float behindFactor;

            if (adjustedCameraAngle > 0)
            {
                behindFactor = (180f - adjustedCameraAngle) / maxSteeringAngle;
            }
            else
            {
                behindFactor = (180f + adjustedCameraAngle) / maxSteeringAngle;
            }

            behindFactor = Mathf.Clamp01(behindFactor);

            if (adjustedCameraAngle > 0)
            {
                frontSteeringAngle = maxSteeringAngle * behindFactor;
            }
            else
            {
                frontSteeringAngle = -maxSteeringAngle * behindFactor;
            }
        }
        else
        {
            frontSteeringAngle = Mathf.Clamp(adjustedCameraAngle, -maxSteeringAngle, maxSteeringAngle);
        }

        // Use 50% of steering angle for rear wheels (in opposite direction)
        float rearSteeringAngle = frontSteeringAngle * -0.5f;

        // Store current rotation to preserve roll angles
        Quaternion flCurrentRotation = frontLeftWheelTransform.localRotation;
        Quaternion frCurrentRotation = frontRightWheelTransform.localRotation;
        Quaternion rlCurrentRotation = rearLeftWheelTransform.localRotation;
        Quaternion rrCurrentRotation = rearRightWheelTransform.localRotation;

        // Apply steering rotation to wheel transforms (preserving any current X rotation)
        frontLeftWheelTransform.localRotation = Quaternion.Euler(flCurrentRotation.eulerAngles.x, frontSteeringAngle, 0);
        frontRightWheelTransform.localRotation = Quaternion.Euler(frCurrentRotation.eulerAngles.x, frontSteeringAngle, 0);
        rearLeftWheelTransform.localRotation = Quaternion.Euler(rlCurrentRotation.eulerAngles.x, rearSteeringAngle, 0);
        rearRightWheelTransform.localRotation = Quaternion.Euler(rrCurrentRotation.eulerAngles.x, rearSteeringAngle, 0);

        // Calculate wheel rotation using the precise physics-based approach

        // Front Left Wheel
        float flCircumference = 2f * Mathf.PI * frontLeftWheelCollider.radius;
        float flDistanceTraveled = frontLeftWheelCollider.rpm * Time.deltaTime * flCircumference / 60f;
        float flRotationDegrees = (flDistanceTraveled / flCircumference) * 360f;

        // Front Right Wheel
        float frCircumference = 2f * Mathf.PI * frontRightWheelCollider.radius;
        float frDistanceTraveled = frontRightWheelCollider.rpm * Time.deltaTime * frCircumference / 60f;
        float frRotationDegrees = (frDistanceTraveled / frCircumference) * 360f;

        // Rear Left Wheel
        float rlCircumference = 2f * Mathf.PI * rearLeftWheelCollider.radius;
        float rlDistanceTraveled = rearLeftWheelCollider.rpm * Time.deltaTime * rlCircumference / 60f;
        float rlRotationDegrees = (rlDistanceTraveled / rlCircumference) * 360f;

        // Rear Right Wheel
        float rrCircumference = 2f * Mathf.PI * rearRightWheelCollider.radius;
        float rrDistanceTraveled = rearRightWheelCollider.rpm * Time.deltaTime * rrCircumference / 60f;
        float rrRotationDegrees = (rrDistanceTraveled / rrCircumference) * 360f;

        // Apply rotation to wheel models around their X axis (roll)
        frontLeftWheelTransform.Rotate(flRotationDegrees, 0f, 0f, Space.Self);
        frontRightWheelTransform.Rotate(frRotationDegrees, 0f, 0f, Space.Self);
        rearLeftWheelTransform.Rotate(rlRotationDegrees, 0f, 0f, Space.Self);
        rearRightWheelTransform.Rotate(rrRotationDegrees, 0f, 0f, Space.Self);
    }

    public void Handbrake()
    {
        CancelInvoke("RecoverTraction");
        isTractionLocked = false;
        // We are going to start losing traction smoothly, there is were our 'driftingAxis' variable takes
        // place. This variable will start from 0 and will reach a top value of 1, which means that the maximum
        // drifting value has been reached. It will increase smoothly by using the variable Time.deltaTime.
        driftingAxis = driftingAxis + Time.deltaTime;
        float secureStartingPoint = driftingAxis * FLWextremumSlip * handbrakeDriftMultiplier;

        if (secureStartingPoint < FLWextremumSlip)
        {
            driftingAxis = FLWextremumSlip / (FLWextremumSlip * handbrakeDriftMultiplier);
        }
        if (driftingAxis > 1f)
        {
            driftingAxis = 1f;
        }
        if (driftingAxis < 1f)
        {
            FLwheelFriction.extremumSlip = FLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            frontLeftWheelCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction.extremumSlip = FRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            frontRightWheelCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction.extremumSlip = RLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            rearLeftWheelCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction.extremumSlip = RRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
            rearRightWheelCollider.sidewaysFriction = RRwheelFriction;
        }
    }

    public void RecoverTraction()
    {
        driftingAxis = driftingAxis - (Time.deltaTime / 1.5f);
        if (driftingAxis < 0f)
        {
            driftingAxis = 0f;
        }

        if (driftingAxis > 0f)
        {
            // Calculate new slip values
            float newFLSlip = Mathf.Max(FLWextremumSlip, FLWextremumSlip * handbrakeDriftMultiplier * driftingAxis);
            float newFRSlip = Mathf.Max(FRWextremumSlip, FRWextremumSlip * handbrakeDriftMultiplier * driftingAxis);
            float newRLSlip = Mathf.Max(RLWextremumSlip, RLWextremumSlip * handbrakeDriftMultiplier * driftingAxis);
            float newRRSlip = Mathf.Max(RRWextremumSlip, RRWextremumSlip * handbrakeDriftMultiplier * driftingAxis);

            // Apply new slip values
            FLwheelFriction = frontLeftWheelCollider.sidewaysFriction;
            FLwheelFriction.extremumSlip = newFLSlip;
            frontLeftWheelCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction = frontRightWheelCollider.sidewaysFriction;
            FRwheelFriction.extremumSlip = newFRSlip;
            frontRightWheelCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction = rearLeftWheelCollider.sidewaysFriction;
            RLwheelFriction.extremumSlip = newRLSlip;
            rearLeftWheelCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction = rearRightWheelCollider.sidewaysFriction;
            RRwheelFriction.extremumSlip = newRRSlip;
            rearRightWheelCollider.sidewaysFriction = RRwheelFriction;

            // Continue recovery in the next frame
            Invoke("RecoverTraction", Time.deltaTime);
        }
        else
        {
            // Reset to original values when recovery is complete
            FLwheelFriction = frontLeftWheelCollider.sidewaysFriction;
            FLwheelFriction.extremumSlip = FLWextremumSlip;
            frontLeftWheelCollider.sidewaysFriction = FLwheelFriction;

            FRwheelFriction = frontRightWheelCollider.sidewaysFriction;
            FRwheelFriction.extremumSlip = FRWextremumSlip;
            frontRightWheelCollider.sidewaysFriction = FRwheelFriction;

            RLwheelFriction = rearLeftWheelCollider.sidewaysFriction;
            RLwheelFriction.extremumSlip = RLWextremumSlip;
            rearLeftWheelCollider.sidewaysFriction = RLwheelFriction;

            RRwheelFriction = rearRightWheelCollider.sidewaysFriction;
            RRwheelFriction.extremumSlip = RRWextremumSlip;
            rearRightWheelCollider.sidewaysFriction = RRwheelFriction;

            driftingAxis = 0f;
            isTractionLocked = true; // Only set to true when fully recovered
        }
    }
    
    private void InitializeWheelFriction()
    {
        FLwheelFriction = frontLeftWheelCollider.sidewaysFriction;
        FLWextremumSlip = FLwheelFriction.extremumSlip;

        FRwheelFriction = frontRightWheelCollider.sidewaysFriction;
        FRWextremumSlip = FRwheelFriction.extremumSlip;

        RLwheelFriction = rearLeftWheelCollider.sidewaysFriction;
        RLWextremumSlip = RLwheelFriction.extremumSlip;

        RRwheelFriction = rearRightWheelCollider.sidewaysFriction;
        RRWextremumSlip = RRwheelFriction.extremumSlip;
    }

    private void EnableWheelColliderPhysics()
    {
        frontLeftWheelCollider.enabled = true;
        frontRightWheelCollider.enabled = true;
        rearLeftWheelCollider.enabled = true;
        rearRightWheelCollider.enabled = true;
    }
    
    private void DisableWheelColliderPhysics()
    {
        frontLeftWheelCollider.enabled = false;
        frontRightWheelCollider.enabled = false;
        rearLeftWheelCollider.enabled = false;
        rearRightWheelCollider.enabled = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsServer && !collisionForceOnCooldown && collision.gameObject.tag == "Player")
        {
            StartCoroutine(CollisionForceDebounce());
        }

        HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogImpact);
    }

    private IEnumerator CollisionForceDebounce()
    {
        collisionForceOnCooldown = true;
        yield return new WaitForSeconds(.5f);
        collisionForceOnCooldown = false;
    }

    public void DriftCarFX()
    {
        // Check if wheels are grounded and drifting
        bool rearLeftGrounded = rearLeftWheelCollider.isGrounded;
        bool rearRightGrounded = rearRightWheelCollider.isGrounded;

        if (isDrifting.Value)
        {
            // Only play particle effects and skid marks if the wheels are grounded
            if (rearLeftGrounded)
            {
                if (!driftingSoundOn && canMove)
                {
                    HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.TireScreechOn);
                    driftingSoundOn = true;
                }
                RLWParticleSystem.Play();
                RLWTireSkid.emitting = true;
            }
            else
            {
                if (driftingSoundOn)
                {
                    HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.TireScreechOff);
                    driftingSoundOn = false;
                }
                RLWParticleSystem.Stop();
                RLWTireSkid.emitting = false;
            }

            if (rearRightGrounded)
            {
                RRWParticleSystem.Play();
                RRWTireSkid.emitting = true;
            }
            else
            {
                RRWParticleSystem.Stop();
                RRWTireSkid.emitting = false;
            }
        }
        else if (!isDrifting.Value)
        {
            // Not drifting, turn off all effects
            RLWParticleSystem.Stop();
            RRWParticleSystem.Stop();
            RLWTireSkid.emitting = false;
            RRWTireSkid.emitting = false;
            if (driftingSoundOn)
            {
                HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.TireScreechOff);
                driftingSoundOn = false;
            }
        }
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        // Store reference to instantiated explosion
        GameObject explosionInstance = Instantiate(Explosion, transform.position + centerOfMass, transform.rotation, transform);
        HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.CarExplosion); // Play Explosion Sound.
        canMove = false;
        if (driftingSoundOn) HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.TireScreechOff);

        Debug.Log("Exploding car for player - " + ConnectionManager.instance.GetClientUsername(OwnerClientId));
        StartCoroutine(ResetAfterExplosion(explosionInstance));
    }

    private IEnumerator ResetAfterExplosion(GameObject explosionInstance)
    {
        yield return new WaitForSeconds(3f);

        // Reset movement and destroy explosion
        canMove = true;
        if (explosionInstance != null)
        {
            Destroy(explosionInstance);
        }
    }
}