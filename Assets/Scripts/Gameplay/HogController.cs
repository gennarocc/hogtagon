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
    [SerializeField] private float frontLeftRpm;
    [SerializeField] private float velocity;

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
    private const float CAMERA_BEHIND_THRESHOLD = 180f;
    private const float MIN_VELOCITY_FOR_REVERSE = -0.5f;

    // Input reference
    private InputManager inputManager;

    // Input Smoothing
    private Queue<float> _recentSteeringInputs;
    private List<float> _steeringInputsList = new List<float>();
    private float currentSteeringInput = 0f; // Current smoothed steering input

    // Network variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isJumping = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> jumpReady = new NetworkVariable<bool>(true);

    // Physics and control variables
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private bool jumpOnCooldown = false;
    private float jumpCooldownRemaining = 0f;

    // Cached physics values
    private Vector3 cachedVelocity;
    private float cachedSpeed;
    private float[] cachedWheelRpms = new float[4];
    private float cachedForwardVelocity;

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

    // Steering angles data structure
    private struct SteeringData
    {
        public float frontSteeringAngle;
        public float rearSteeringAngle;
        public bool isReversing;
    }

    #endregion

    #region Unity Lifecycle Methods

    private void Start()
    {
        // Get reference to the InputManager
        inputManager = InputManager.Instance;
        if (inputManager == null)
        {
            Debug.LogError("InputManager not found in the scene");
        }

        // Subscribe to input events
        if (IsOwner && inputManager != null)
        {
            inputManager.JumpPressed += OnJumpPressed;
            inputManager.HornPressed += OnHornPressed;
        }

        // Initialize steering input buffer
        _recentSteeringInputs = new Queue<float>(steeringBufferSize);

        if (IsServer || IsOwner)
        {
            // Full physics setup on server and owning client
            rb.centerOfMass = centerOfMass;
            InitializeWheelFriction();
        }
        else
        {
            rb.isKinematic = true;
            DisableWheelColliderPhysics();
        }

    }

    private void Update()
    {
        if (!IsOwner) return;

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

    private void FixedUpdate()
    {
        // Cache frequently used physics values
        CachePhysicsValues();

        if (IsClient && IsOwner)
        {
            ClientMove();
        }

        AnimateWheels();
        UpdateDriftEffects();
    }

    #endregion

    #region Input Handling

    private void OnHornPressed()
    {
        // Only play horn if not spectating
        if (!transform.root.gameObject.GetComponent<Player>().isSpectating)
        {
            // Play Horn Sound
        }
    }

    private void OnJumpPressed()
    {
        // Check if player can jump and is not spectating
        bool canPerformJump = canMove && canJump && !jumpOnCooldown &&
                             !transform.root.gameObject.GetComponent<Player>().isSpectating;

        if (canPerformJump)
        {
            // Request jump on the server
            JumpServerRpc();
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

            frontLeftRpm = cachedWheelRpms[0];
            rpm.SetGlobalValue(frontLeftRpm);
            velocity = cachedSpeed;
        }
    }

    private SteeringData CalculateSteeringFromInput(float steeringInput, float moveInput)
    {
        // Debug the raw steering input
        if (Mathf.Abs(steeringInput) > 0.01f)
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

        // Calculate rear steering angle - reduced in reverse for better control
        float rearFactor = shouldUseReverseControls ? rearSteeringAmount * 0.5f : rearSteeringAmount;
        float rearSteeringAngle = finalSteeringAngle * rearFactor * -1;

        return new SteeringData
        {
            frontSteeringAngle = finalSteeringAngle,
            rearSteeringAngle = rearSteeringAngle,
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

        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.deltaTime * 3f);
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
        // Get current steering angles
        SteeringData steeringData = GetCurrentSteeringAngles();

        // Apply steering angles to wheel transforms
        ApplySteeringToWheelMeshes(steeringData);

        // Rotate wheels based on physics or rigidbody velocity
        if (IsServer || IsOwner)
        {
            RotateWheelsUsingColliders();
        }
        else
        {
            RotateWheelsUsingVelocity();
        }
    }

    private SteeringData GetCurrentSteeringAngles()
    {
        // Use existing steering angles from wheel colliders
        float frontSteeringAngle = (wheelColliders[0].steerAngle + wheelColliders[1].steerAngle) / 2f;
        float rearSteeringAngle = (wheelColliders[2].steerAngle + wheelColliders[3].steerAngle) / 2f;

        bool isReversing = cachedForwardVelocity < MIN_VELOCITY_FOR_REVERSE &&
                          (IsOwner && inputManager != null && inputManager.BrakeInput > 0);

        return new SteeringData
        {
            frontSteeringAngle = frontSteeringAngle,
            rearSteeringAngle = rearSteeringAngle,
            isReversing = isReversing
        };
    }

    private void ApplySteeringToWheelMeshes(SteeringData steeringData)
    {
        // Store current rotation to preserve roll angles
        Quaternion[] currentRotations = new Quaternion[4];
        for (int i = 0; i < wheelTransforms.Length; i++)
        {
            currentRotations[i] = wheelTransforms[i].localRotation;
        }

        // Apply steering rotation to wheel transforms (preserving X rotation)
        wheelTransforms[0].localRotation = Quaternion.Euler(currentRotations[0].eulerAngles.x, steeringData.frontSteeringAngle, 0);
        wheelTransforms[1].localRotation = Quaternion.Euler(currentRotations[1].eulerAngles.x, steeringData.frontSteeringAngle, 0);
        wheelTransforms[2].localRotation = Quaternion.Euler(currentRotations[2].eulerAngles.x, steeringData.rearSteeringAngle, 0);
        wheelTransforms[3].localRotation = Quaternion.Euler(currentRotations[3].eulerAngles.x, steeringData.rearSteeringAngle, 0);
    }

    private void RotateWheelsUsingColliders()
    {
        // Rotate each wheel based on its collider's RPM
        for (int i = 0; i < wheelTransforms.Length; i++)
        {
            float circumference = 2f * Mathf.PI * wheelColliders[i].radius;
            float distanceTraveled = wheelColliders[i].rpm * Time.deltaTime * circumference / 60f;
            float rotationDegrees = (distanceTraveled / circumference) * 360f;

            wheelTransforms[i].Rotate(rotationDegrees, 0f, 0f, Space.Self);
        }
    }

    private void RotateWheelsUsingVelocity()
    {
        // For non-owner clients, estimate wheel rotation based on rigidbody velocity
        float wheelRadius = 0.33f; // Assuming all wheels have the same radius, adjust as needed
        float velocity = Mathf.Abs(cachedForwardVelocity);

        // Calculate RPM: (velocity in m/s) / (circumference in meters) * 60 seconds
        float estimatedRPM = (velocity / (2f * Mathf.PI * wheelRadius)) * 60f;

        // Calculate rotation degrees
        float circumference = 2f * Mathf.PI * wheelRadius;
        float distanceTraveled = estimatedRPM * Time.deltaTime * circumference / 60f;
        float rotationDegrees = (distanceTraveled / circumference) * 360f;

        // If moving in reverse, invert rotation direction
        if (cachedForwardVelocity < 0)
        {
            rotationDegrees = -rotationDegrees;
        }

        // Apply rotation to all wheels
        for (int i = 0; i < wheelTransforms.Length; i++)
        {
            wheelTransforms[i].Rotate(rotationDegrees, 0f, 0f, Space.Self);
        }
    }

    private void UpdateDriftEffects()
    {
        // Check if wheels are grounded and drifting
        bool rearLeftGrounded = wheelColliders[2].isGrounded;
        bool rearRightGrounded = wheelColliders[3].isGrounded;

        if (isDrifting.Value)
        {
            // Only play particle effects and skid marks if the wheels are grounded
            if (rearLeftGrounded)
            {
                wheelParticleSystems[0].Play();
                tireSkids[0].emitting = true;
            }
            else
            {
                wheelParticleSystems[0].Stop();
                tireSkids[0].emitting = false;
            }

            if (rearRightGrounded)
            {
                wheelParticleSystems[1].Play();
                tireSkids[1].emitting = true;
            }
            else
            {
                wheelParticleSystems[1].Stop();
                tireSkids[1].emitting = false;
            }
        }
        else if (!isDrifting.Value)
        {
            // Not drifting, turn off all effects
            wheelParticleSystems[0].Stop();
            wheelParticleSystems[1].Stop();
            tireSkids[0].emitting = false;
            tireSkids[1].emitting = false;
        }
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
    #endregion
}