using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;

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
        public float rawCameraAngle;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref rawCameraAngle);
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

        HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.EngineOn);
    }

    private void OnDestroy()
    {
        // Unsubscribe from events when destroyed
        if (inputManager != null && IsOwner)
        {
            inputManager.JumpPressed -= OnJumpPressed;
            inputManager.HornPressed -= OnHornPressed;
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
            HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogHorn);
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
        SendInputServerRpc(input);
    }

    private ClientInput CollectPlayerInput()
    {
        // Calculate camera angle based on look input
        Vector2 lookInput = inputManager.LookInput;
        float rawCameraAngle = CalculateRawCameraAngle(lookInput);
        float smoothedAngle = ApplyInputSmoothing(rawCameraAngle);

        return new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = inputManager.ThrottleInput - inputManager.BrakeInput,
            brakeInput = inputManager.BrakeInput,
            rawCameraAngle = smoothedAngle
        };
    }

    private float CalculateRawCameraAngle(Vector2 lookInput)
    {
        // If using the input system's look values (for gamepad control)
        if (inputManager.IsUsingGamepad && lookInput.sqrMagnitude > 0.01f)
        {
            // Convert look input to camera angle
            // This is a simplified version - you might need to adjust based on your exact needs
            return Mathf.Atan2(lookInput.x, lookInput.y) * Mathf.Rad2Deg;
        }
        else
        {
            // Use the traditional camera position calculation for mouse/keyboard
            Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
            cameraVector.y = 0;
            Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
            return Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));
        }
    }

    private float ApplyInputSmoothing(float rawCameraAngle)
    {
        // Add to input buffer for smoothing
        _recentSteeringInputs.Enqueue(rawCameraAngle);
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

        return rawCameraAngle;
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
        SteeringData steeringData = CalculateSteeringFromCameraAngle(input.rawCameraAngle, input.moveInput);

        ApplyMotorTorque(input.moveInput, input.brakeInput);
        ApplySteering(steeringData);

        isDrifting.Value = Mathf.Abs(localVelocityX) > .45f;
    }

    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        // Store reference to instantiated explosion
        GameObject explosionInstance = Instantiate(Explosion, transform.position + centerOfMass, transform.rotation, transform);
        HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.CarExplosion); // Play Explosion Sound.
        canMove = false;

        Debug.Log("Exploding car for player - " + ConnectionManager.instance.GetClientUsername(OwnerClientId));
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

    private SteeringData CalculateSteeringFromCameraAngle(float cameraAngle, float moveInput)
    {
        // Apply adaptive deadzone - higher deadzone at higher speeds
        float speedFactor = Mathf.Clamp01(cachedSpeed / maxSpeedForSteering);
        float deadzone = Mathf.Lerp(minDeadzone, maxDeadzone, speedFactor);

        if (cameraAngle < deadzone && cameraAngle > -deadzone)
            cameraAngle = 0f;

        // If camera angle is small, treat it as zero
        if (Math.Abs(cameraAngle) < 1f)
            cameraAngle = 0f;

        // Check if car is actually moving in reverse
        bool isMovingInReverse = cachedForwardVelocity < MIN_VELOCITY_FOR_REVERSE;

        // Determine if we should apply reverse steering logic
        bool shouldUseReverseControls = isMovingInReverse && moveInput < 0;

        // For reverse, we invert the steering direction
        float adjustedCameraAngle = cameraAngle;
        if (shouldUseReverseControls)
        {
            adjustedCameraAngle = -cameraAngle;

            // Add hysteresis - if angle is around 90 degrees, maintain the current steering angle
            if (Mathf.Abs(adjustedCameraAngle) > 70f && Mathf.Abs(adjustedCameraAngle) < 110f)
            {
                // Get the current steering angle and maintain it with some smoothing
                float currentSteeringAverage = (wheelColliders[0].steerAngle + wheelColliders[1].steerAngle) / 2f;

                // Only make small adjustments when in this "stability zone"
                adjustedCameraAngle = Mathf.Lerp(adjustedCameraAngle, currentSteeringAverage, 0.8f);
            }
        }

        // Check if camera is behind the car based on maxSteeringAngle
        bool isCameraBehind = Mathf.Abs(adjustedCameraAngle) > (CAMERA_BEHIND_THRESHOLD - maxSteeringAngle);

        float finalSteeringAngle;
        if (isCameraBehind)
        {
            // When camera is in the "behind" region, calculate how far into that region
            float behindFactor = CalculateBehindFactor(adjustedCameraAngle);

            // Calculate steering angle that decreases as we go deeper into behind region
            finalSteeringAngle = adjustedCameraAngle > 0
                ? maxSteeringAngle * behindFactor
                : -maxSteeringAngle * behindFactor;
        }
        else
        {
            // Normal steering - within normal range
            finalSteeringAngle = Mathf.Clamp(adjustedCameraAngle, -maxSteeringAngle, maxSteeringAngle);
        }

        // Progressive steering response - less responsive at higher speeds
        float steeringResponse = Mathf.Lerp(maxSteeringResponse, minSteeringResponse, speedFactor);
        finalSteeringAngle *= steeringResponse;

        // Calculate rear steering angle
        float rearSteeringAngle = finalSteeringAngle * rearSteeringAmount * -1;

        return new SteeringData
        {
            frontSteeringAngle = finalSteeringAngle,
            rearSteeringAngle = rearSteeringAngle,
            isReversing = shouldUseReverseControls
        };
    }

    private float CalculateBehindFactor(float angle)
    {
        float factor = angle > 0
            ? (CAMERA_BEHIND_THRESHOLD - angle) / maxSteeringAngle
            : (CAMERA_BEHIND_THRESHOLD + angle) / maxSteeringAngle;
        return Mathf.Clamp01(factor);
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
            // HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogJump);
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