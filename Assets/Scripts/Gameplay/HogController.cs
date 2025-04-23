using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using Hogtagon.Core.Infrastructure;

public class HogController : NetworkBehaviour
{
    #region Configuration Parameters

    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f;
    [SerializeField] private float maxBrakeTorque = 500f;
    [SerializeField] private AnimationCurve accelerationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float torqueResponseSpeed = 3f; // How quickly torque responds to input changes
    [SerializeField] private float coastDampener = 2f; // How quickly throttle returns to zero when no input
    [SerializeField] private float coastBrake = 2f; // How quickly throttle returns to zero when no input
    [SerializeField, Range(0f, 1f)] private float rearSteeringAmount = .35f;
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .4f;

    [Header("Bumper Collision")]
    [SerializeField] private float bumperForceMultiplier = 2.5f; // Extra force applied by bumper
    [SerializeField] private float bumperImpulseMultiplier = 1.5f; // Multiplier for impulse-based force
    [SerializeField] private float baseKickbackReduction = 0.5f; // Stabilization force for bumper collision
    [SerializeField] private float minBumperCollisionPlayerSpeed = 10f; // Minimum threshold for enhanced collisions

    [Header("Rocket Jump")]
    [SerializeField] private float jumpForce = 7f; // How much upward force to apply
    [SerializeField] private float jumpCooldown = 15f; // Time between jumps
    [SerializeField] private bool canJump = true; // Whether the player can jump
    public bool JumpOnCooldown => jumpOnCooldown;
    public float JumpCooldownRemaining => jumpCooldownRemaining;
    public float JumpCooldownTotal => jumpCooldown;

    [Header("Drift Settings")]
    [SerializeField] private float driftThreshold = 0.3f; // Minimum sideways velocity for drift
    [SerializeField] private float driftMinSpeed = 5f; // Minimum speed required for drift

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
    [SerializeField] private Transform[] wheelTransforms = new Transform[4]; // FL, FR, RL, RR
    [SerializeField] private HogVisualEffects visualEffects; // Reference to the visual effects component

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.RTPC rpm;

    [Header("Debug UI")]
    [SerializeField] private bool showDebugUI = false;
    [SerializeField] private Color debugBackgroundColor = new Color(0, 0, 0, 0.7f);
    [SerializeField] private Color labelTextColor = Color.gray;
    [SerializeField] private Color valueTextColor = Color.white;

    #endregion

    #region Fields and Properties

    // Input reference
    private InputManager inputManager;
    private bool jumpInputReceived = false;

    // Network variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isJumping = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> jumpReady = new NetworkVariable<bool>(true);
    private NetworkVariable<float> netSteeringAxis = new NetworkVariable<float>(0f);
    private NetworkVariable<float> netWheelRotationSpeed = new NetworkVariable<float>(0f);

    // Physics and control variables
    private float currentTorque;
    private float steeringAxis;
    private float localVelocityX;
    private bool jumpOnCooldown = false;
    private float jumpCooldownRemaining = 0f;
    private float lerpedThrottleInput;
    private Texture2D debugBackgroundTexture;
    private const float bumperCollisionDebounce = 0.2f; // Prevent too frequent bumper collisions
    private bool bumperCollisionOnCooldown = false;

    #endregion

    #region Lifecycle Methods

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

        // Toggle debug UI with F1
        if (Input.GetKeyDown(KeyCode.F1))
        {
            showDebugUI = !showDebugUI;
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            ClientInput input = CollectInput();

            // Calculate steering locally for visual responsiveness
            CalculateSteeringAxis(input.steerInput);

            // Apply wheel visuals immediately without waiting for server
            AnimateWheelsLocal();

            // Send input to server for physics and non-owner clients
            SendInputServerRpc(input);

            // Check for drift conditions and send to server
            CheckDriftCondition();

            CalculateEngineAudio(input);
        }
        // NON-OWNER CLIENTS: apply network synchronized wheel visuals
        else if (!IsServer)
        {
            AnimateWheelsFromNetwork();
        }
    }

    #endregion

    #region Network Setup and Events

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();


        // Get reference to the InputManager
        inputManager = InputManager.Instance;
        if (IsOwner)
        {
            // Subscribe to the jump event
            inputManager.JumpPressed += OnJumpPressed;
            inputManager.HornPressed += OnHornPressed;
        }

        if (IsServer || IsOwner)
        {
            // Full physics setup on server and owning client
            rb.centerOfMass = centerOfMass;
        }
        else
        {
            rb.isKinematic = true;
            DisableWheelColliderPhysics();
        }

        visualEffects.Initialize(transform, centerOfMass, OwnerClientId);

        // Create texture for debug UI background
        debugBackgroundTexture = MakeTexture(2, 2, debugBackgroundColor);

        // Subscribe to network variable changes to trigger effects
        isDrifting.OnValueChanged += OnDriftingChanged;
        isJumping.OnValueChanged += OnJumpingChanged;

    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        // Unsubscribe from network variable changes
        isDrifting.OnValueChanged -= OnDriftingChanged;
        isJumping.OnValueChanged -= OnJumpingChanged;

        // Unsubscribe from input events
        if (IsOwner && inputManager != null)
        {
            inputManager.JumpPressed -= OnJumpPressed;
        }

        if (debugBackgroundTexture != null)
            Destroy(debugBackgroundTexture);

        SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.EngineOff);
    }

    private void OnDriftingChanged(bool previousValue, bool newValue)
    {
        if (visualEffects != null)
        {
            // Check if rear wheels are grounded
            bool rearLeftGrounded = wheelColliders[2].isGrounded;
            bool rearRightGrounded = wheelColliders[3].isGrounded;

            // Update drift effects based on new value
            visualEffects.UpdateDriftEffects(newValue, rearLeftGrounded, rearRightGrounded, canMove);
        }
    }

    private void OnJumpingChanged(bool previousValue, bool newValue)
    {
        if (newValue && visualEffects != null)
        {
            visualEffects.PlayJumpEffects();
        }
    }

    #endregion

    #region Input Handling

    private void OnJumpPressed()
    {
        if (canJump && !jumpOnCooldown)
        {
            jumpInputReceived = true;
        }
    }

    private void OnHornPressed()
    {
        SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.HogHorn);
    }

    private ClientInput CollectInput()
    {
        ClientInput input = new ClientInput
        {
            clientId = NetworkManager.Singleton.LocalClientId,
            throttleInput = inputManager.ThrottleInput,
            brakeInput = inputManager.BrakeInput,
            steerInput = inputManager.SteerInput,
            jumpInput = jumpInputReceived
        };

        // Reset jump input after collecting it
        jumpInputReceived = false;

        return input;
    }

    #endregion

    #region Network RPCs

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        if (!IsServer) return;

        // Apply physics based on input
        ApplyMotorTorque(input.throttleInput, input.brakeInput);

        // Calculate steering for physics
        CalculateSteeringAxis(input.steerInput);
        ApplySteering();

        // Check drift condition on server
        CheckServerDriftCondition();

        // Process jump input
        if (input.jumpInput && canJump && jumpReady.Value)
        {
            // Apply jump physics
            ProcessJump();

            // Start cooldown
            StartCoroutine(JumpCooldownServer());
        }

        // Update network variables for non-owner clients
        netSteeringAxis.Value = steeringAxis;
        netWheelRotationSpeed.Value = rb.linearVelocity.magnitude;
    }

    [ServerRpc]
    private void UpdateDriftingServerRpc(bool isDriftingNow)
    {
        isDrifting.Value = isDriftingNow;
    }

    [ClientRpc]
    private void SyncJumpCooldownClientRpc(float cooldownDuration, bool isOnCooldown)
    {
        // This will be called on all clients when jump state changes
        if (IsOwner)
        {
            jumpOnCooldown = isOnCooldown;
            jumpCooldownRemaining = cooldownDuration;
        }
    }

    #endregion

    #region Physics and Movement

    private void ApplyMotorTorque(float throttleInput, float brakeInput)
    {
        // If vehicle can't move, apply full brakes and no torque
        if (!canMove)
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                wheelColliders[i].motorTorque = 0;
                wheelColliders[i].brakeTorque = maxBrakeTorque;
            }
            return;
        }

        // Calculate net input (throttle - brake)
        float netInput = throttleInput - brakeInput;

        // Apply acceleration curve to get a more natural response
        float curvedInput = Mathf.Sign(netInput) * accelerationCurve.Evaluate(Mathf.Abs(netInput));
        float targetTorque = curvedInput * maxTorque;

        // Handle direction changes by applying brakes when appropriate
        float brakeTorque = 0;
        float currentSpeed = rb.linearVelocity.magnitude;
        float directionFactor = Vector3.Dot(rb.linearVelocity.normalized, transform.forward);

        // Enhanced directional braking system
        bool isMovingForward = directionFactor > 0.05f;
        bool isMovingBackward = directionFactor < -0.05f;
        bool isPressingReverse = netInput < -0.01f;
        bool isPressingForward = netInput > 0.01f;

        // Detect counter-directional input (enhanced braking scenarios)
        bool isCounterDirectional = (isMovingForward && isPressingReverse) ||
                                   (isMovingBackward && isPressingForward);

        if (currentSpeed > 0.2f && isCounterDirectional)  // Lower threshold for responsiveness
        {
            // Apply stronger brake force when input is opposite to movement direction
            float counterDirectionalMultiplier = 1.5f; // Adjust this value for braking strength
            brakeTorque = Mathf.Abs(netInput) * maxBrakeTorque * counterDirectionalMultiplier;

            // Apply braking force proportional to speed for better feel at different speeds
            brakeTorque *= Mathf.Clamp(currentSpeed / 10f, 0.5f, 1.2f);

            // Apply additional braking to rear wheels for more controllable stops
            for (int i = 2; i < 4 && i < wheelColliders.Length; i++) // Rear wheels (2,3)
            {
                wheelColliders[i].brakeTorque = brakeTorque * 1.2f; // Slightly more brake on rear
            }

            // Apply normal braking to front wheels
            for (int i = 0; i < 2 && i < wheelColliders.Length; i++) // Front wheels (0,1)
            {
                wheelColliders[i].brakeTorque = brakeTorque;
                wheelColliders[i].motorTorque = 0;
            }

            // Skip the rest of normal torque application
            return;
        }

        // Handle coasting (no input)
        bool noInput = Mathf.Abs(netInput) < 0.01f;

        // Choose the appropriate response speed based on input state
        float responseMultiplier;
        if (noInput)
        {
            // Use coastDampener for quicker return to zero when coasting
            responseMultiplier = coastDampener;
            brakeTorque = coastBrake * Mathf.Clamp01(currentSpeed);
            targetTorque = 0;
        }
        else
        {
            // Use normal response speed when actively controlling
            responseMultiplier = torqueResponseSpeed;
        }

        // Smoothly transition to target torque with the appropriate multiplier
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque,
                                         responseMultiplier * maxTorque * Time.deltaTime);

        // Apply to wheels
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = currentTorque;
            wheelColliders[i].brakeTorque = brakeTorque;
        }
    }

    private void CalculateSteeringAxis(float steeringInput)
    {
        // Calculate the base step based on steering speed and deltaTime
        float step = steeringSpeed * Time.fixedDeltaTime;

        // Detect if steering input is in opposite direction from current axis
        bool oppositeDirection = (steeringInput * steeringAxis < 0) && (Mathf.Abs(steeringInput) > 0.01f);

        // Check if there's no input (values close to zero)
        bool noInput = Mathf.Abs(steeringInput) < 0.01f;

        // Calculate the direction of movement
        float direction = Mathf.Sign(steeringInput - steeringAxis);

        // Calculate the distance from the current value to the target
        float distance = Mathf.Abs(steeringInput - steeringAxis);

        // Apply different multipliers based on conditions
        float multiplier = 1.0f;

        if (oppositeDirection)
        {
            // Increase turn speed when steering in the opposite direction
            multiplier = 3.0f; // Can be adjusted for desired responsiveness
        }
        else if (noInput)
        {
            // Decrease speed when returning to center with no input
            multiplier = 0.9f; // Can be adjusted for desired return-to-center speed
        }

        // Apply exponential curve with our condition-specific multiplier
        float exponentialStep = step * (1.0f + distance * 2.0f) * multiplier;

        // Ensure we don't overshoot the target
        exponentialStep = Mathf.Min(exponentialStep, distance);

        // Apply the calculated step in the correct direction
        steeringAxis += direction * exponentialStep;
    }

    private void ApplySteering()
    {
        // Apply steering to wheel colliders for physics
        float frontSteeringAngle = steeringAxis * maxSteeringAngle;
        float rearSteeringAngle = -frontSteeringAngle * rearSteeringAmount;

        // Front wheels
        wheelColliders[0].steerAngle = frontSteeringAngle;
        wheelColliders[1].steerAngle = frontSteeringAngle;

        // Rear wheels (if rear steering is enabled)
        if (rearSteeringAmount > 0)
        {
            wheelColliders[2].steerAngle = rearSteeringAngle;
            wheelColliders[3].steerAngle = rearSteeringAngle;
        }
    }

    // Local wheel animation (for owner only)
    private void AnimateWheelsLocal()
    {
        // Pre-calculate common values
        float frontSteeringAngle = netSteeringAxis.Value * maxSteeringAngle;
        float rearSteeringAngle = -frontSteeringAngle * rearSteeringAmount;

        float wheelSpeed = netWheelRotationSpeed.Value;
        float wheelCircumference = 2f * Mathf.PI * .6f;
        float rotationAmount = wheelSpeed * wheelCircumference * 360f * Time.fixedDeltaTime;

        // Front wheels
        for (int i = 0; i < 2; i++)
        {
            wheelTransforms[i].localRotation = Quaternion.Euler(0, frontSteeringAngle, 0);
            wheelTransforms[i].Rotate(rotationAmount, 0, 0, Space.Self);
        }

        // Rear wheels
        for (int i = 2; i < 4; i++)
        {
            wheelTransforms[i].localRotation = Quaternion.Euler(0, rearSteeringAngle, 0);
            wheelTransforms[i].Rotate(rotationAmount, 0, 0, Space.Self);
        }
    }

    // Wheel animation for non-owner clients (using network values)
    private void AnimateWheelsFromNetwork()
    {
        // Convert network steering axis to angles
        float frontSteeringAngle = netSteeringAxis.Value * maxSteeringAngle;
        float rearSteeringAngle = -frontSteeringAngle * rearSteeringAmount;

        // Get wheel speed from network
        float wheelSpeed = netWheelRotationSpeed.Value;
        float wheelCircumference = 2f * Mathf.PI * .6f;
        float rotationAmount = wheelSpeed * wheelCircumference * 360f * Time.fixedDeltaTime;

        // Apply to visual transforms
        // Front wheels
        for (int i = 0; i < 2; i++)
        {
            wheelTransforms[i].localRotation = Quaternion.Euler(0, frontSteeringAngle, 0);
            wheelTransforms[i].Rotate(rotationAmount, 0, 0, Space.Self);
        }

        // Rear wheels
        for (int i = 2; i < 4; i++)
        {
            wheelTransforms[i].localRotation = Quaternion.Euler(0, rearSteeringAngle, 0);
            wheelTransforms[i].Rotate(rotationAmount, 0, 0, Space.Self);
        }
    }

    private void DisableWheelColliderPhysics()
    {
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].enabled = false;
        }
    }

    #endregion

    #region Jump and Drift Mechanics

    private void CheckDriftCondition()
    {
        if (!IsServer && IsOwner)
        {
            // Transform world velocity to local space
            Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
            localVelocityX = Mathf.Abs(localVelocity.x);

            // Check drift conditions - sideways velocity and speed
            bool isDriftingNow = localVelocityX > driftThreshold &&
                                 rb.linearVelocity.magnitude > driftMinSpeed &&
                                 (wheelColliders[2].isGrounded || wheelColliders[3].isGrounded);

            // Only send RPC if state changes to reduce network traffic
            if (isDriftingNow != isDrifting.Value)
            {
                UpdateDriftingServerRpc(isDriftingNow);
            }
        }
    }

    private void CheckServerDriftCondition()
    {
        if (IsServer)
        {
            // Transform world velocity to local space
            Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
            float localVelocityX = Mathf.Abs(localVelocity.x);

            // Check drift conditions - sideways velocity and speed
            isDrifting.Value = localVelocityX > driftThreshold &&
                               rb.linearVelocity.magnitude > driftMinSpeed &&
                               (wheelColliders[2].isGrounded || wheelColliders[3].isGrounded);
        }
    }

    private void ProcessJump()
    {
        // Set network state
        isJumping.Value = true;
        jumpReady.Value = false;

        // Store current horizontal velocity
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);

        // Start with a position boost for immediate feedback
        float upwardVelocity = jumpForce * 1.2f; // Faster rise

        // Combine horizontal momentum with new vertical impulse
        // Multiply horizontal speed to maintain or enhance momentum
        Vector3 newVelocity = horizontalVelocity * 1.1f + Vector3.up * upwardVelocity;
        rb.linearVelocity = newVelocity;

        // Add a bit more forward boost in the car's facing direction
        rb.AddForce(transform.forward * (jumpForce * 5f), ForceMode.Impulse);

        // Notify all clients about the jump and cooldown
        SyncJumpCooldownClientRpc(jumpCooldown, false);
    }

    private IEnumerator JumpCooldownServer()
    {
        // Initial cooldown notification to clients (cooldown active)
        SyncJumpCooldownClientRpc(jumpCooldown, true);

        yield return new WaitForSeconds(jumpCooldown);
        jumpReady.Value = true;
        isJumping.Value = false;

        // Notify clients that cooldown is complete
        SyncJumpCooldownClientRpc(0, false);
    }

    private IEnumerator BumperCollisionCooldown()
    {
        yield return new WaitForSeconds(bumperCollisionDebounce);
        bumperCollisionOnCooldown = false;
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

        // Get collision details
        ContactPoint contact = collision.GetContact(0);
        Collider hitCollider = contact.thisCollider;

        // Default impact sound based on speed
        var impactSound = SoundManager.SoundEffectType.HogImpactLow;
        var playerSpeed = rb.linearVelocity.magnitude;
        var otherPlayerSpeed = otherPlayer.GetComponent<Rigidbody>().linearVelocity.magnitude;
        var speedDifferential = playerSpeed - otherPlayerSpeed;

        // Check conditions for a bumper collision 
        bool isFastEnough = speedDifferential >= minBumperCollisionPlayerSpeed;
        bool isBumperCollider = hitCollider.CompareTag("PlayerBumper");

        // Log collision details for debugging


        // Apply enhanced collision if conditions are met
        if (isFastEnough && isBumperCollider && !bumperCollisionOnCooldown)
        {
            ProcessBumperCollision(collision, contact, speedDifferential);
            // Always use high impact sound for enhanced collisions
            impactSound = SoundManager.SoundEffectType.HogImpactHigh;
        }
        else
        {
            Debug.Log($"[HOG] Player {ConnectionManager.Instance.GetClientUsername(myPlayer.clientId)} collided with Player {ConnectionManager.Instance.GetClientUsername(otherPlayer.clientId)} " +
                      $"(Speed: {playerSpeed:F1}, isBumperCollider: {isBumperCollider}, Fast Enough: {isFastEnough})");

            // Normal collision sound determination based on speed
            if (playerSpeed < 12f && playerSpeed > 5f)
            {
                impactSound = SoundManager.SoundEffectType.HogImpactMed;
            }
            else if (playerSpeed >= 12f)
            {
                impactSound = SoundManager.SoundEffectType.HogImpactHigh;
            }
        }

        // Play the impact sound
        SoundManager.Instance.PlayNetworkedSound(gameObject, impactSound);

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

        Debug.Log($"[HOG] Player {myPlayer.clientId} triggered with Player {otherPlayer.clientId}");

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

    private void ProcessBumperCollision(Collision collision, ContactPoint contact, float playerSpeed)
    {
        bumperCollisionOnCooldown = true;

        // Calculate collision force based on impulse
        Vector3 impulseForce = collision.impulse;
        float impulseMagnitude = impulseForce.magnitude;

        // Get direction vectors
        Vector3 hitPoint = contact.point;
        Vector3 hitNormal = contact.normal;

        // Get the other rigidbody
        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null) return;

        // Calculate force direction - prioritize our forward direction for high-speed impacts
        Vector3 forceDir;

        // For higher speeds, use more of our forward direction to simulate ramming
        // This creates a more directional impact instead of just bouncing off
        float speedFactor = Mathf.Clamp01((playerSpeed - 5f) / 15f); // 0 at speed 5, 1 at speed 20

        // Blend between the contact normal and our forward direction based on speed
        forceDir = Vector3.Lerp(-hitNormal, transform.forward.normalized, speedFactor);
        forceDir.Normalize();

        // Calculate enhanced force magnitude with speed bonus
        // Higher speeds get more force multiplier
        float speedBonus = Mathf.Clamp01(playerSpeed / 20f); // Up to 100% bonus at speed 20
        float enhancedForceMagnitude = impulseMagnitude * bumperForceMultiplier * (1f + speedBonus);

        // Apply force to the other vehicle (scale by impulse multiplier)
        otherRb.AddForceAtPosition(
            forceDir * enhancedForceMagnitude * bumperImpulseMultiplier,
            hitPoint,
            ForceMode.Impulse
        );

        // Set a base minimum kickback reduction even at low speeds

        // Add a smaller speed-dependent component (less variance by speed)
        float speedKickbackComponent = Mathf.Clamp01(speedFactor * 0.4f);

        // Combine for a more consistent kickback reduction that ranges from 50-90%
        float kickbackReduction = baseKickbackReduction + speedKickbackComponent;

        // Apply the kickback reduction to your force calculation
        rb.AddForceAtPosition(
            -forceDir * (enhancedForceMagnitude * 0.3f * (1f - kickbackReduction)),
            hitPoint,
            ForceMode.Impulse
        );

        Debug.Log($"[HOG] Speed: {playerSpeed:F1}, Force applied: {enhancedForceMagnitude:F1}, " +
                  $"Direction: {forceDir}, Speed bonus: {speedBonus:P0}");

        StartCoroutine(BumperCollisionCooldown());
    }

    #endregion

    #region Audio and Visual Effects

    private void CalculateEngineAudio(ClientInput input)
    {
        // Interpolate the throttle input so get smoother engine transitions.
        float netInput = Math.Sign(input.throttleInput - input.brakeInput);
        var lerp = Mathf.Lerp(lerpedThrottleInput, netInput, Time.deltaTime * 3); // Lerp speed is 3
        lerpedThrottleInput = lerp;

        // Wwise expects a value between 1-100 so we multiply by 5 (car speed is around 1-23)
        rpm.SetGlobalValue(rb.linearVelocity.magnitude * 5 * lerpedThrottleInput);
    }

    #endregion

    #region Debug and Utilities

    private void OnGUI()
    {
        if (!showDebugUI || !IsOwner) return;

        // Setup styles
        GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = debugBackgroundTexture;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = labelTextColor;
        labelStyle.fontSize = 12;

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.normal.textColor = valueTextColor;
        valueStyle.fontSize = 12;
        valueStyle.fontStyle = FontStyle.Bold;

        GUIStyle headerStyle = new GUIStyle(labelStyle);
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = valueTextColor;

        // Calculate sizes
        int padding = 10;
        int width = 240;
        int startY = 10;
        int lineHeight = 20;
        int totalLines = 10; // Adjusted for new debug lines

        // Draw background box
        GUI.Box(new Rect(10, startY, width, totalLines * lineHeight + padding * 2), "", boxStyle);

        // Start drawing content
        int yPos = startY + padding;

        // Header
        GUI.Label(new Rect(20, yPos, width - 20, lineHeight), "HOG CONTROLLER DEBUG", headerStyle);
        yPos += lineHeight;

        // Count grounded wheels
        int groundedWheelCount = 0;
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (wheelColliders[i].isGrounded)
                groundedWheelCount++;
        }

        // Get speed in different units
        float speed = rb.linearVelocity.magnitude;

        // Vehicle info - using both label and value styles for color differentiation
        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Speed:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{netWheelRotationSpeed.Value:F1} m/s ({netWheelRotationSpeed.Value * 3.6f:F1} km/h)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Torque:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{currentTorque:F0} Nm", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Steering:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{netSteeringAxis.Value:F2} ({netSteeringAxis.Value * maxSteeringAngle:F0}°)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Can Move:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            canMove ? "Yes" : "No", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Wheels:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{groundedWheelCount}/{wheelColliders.Length} grounded", valueStyle);
        yPos += lineHeight;

        // Add drift info
        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Drifting:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            isDrifting.Value ? "Yes" : "No", valueStyle);
        yPos += lineHeight;

        // Network info
        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Ping:", labelStyle);
        float ping = 0;
        if (NetworkManager.Singleton?.NetworkConfig?.NetworkTransport is Unity.Netcode.Transports.UTP.UnityTransport transport)
        {
            ping = transport.GetCurrentRtt(0);
        }
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{ping:F0} ms", valueStyle);
        yPos += lineHeight;
    }

    // Utility method to create a solid color texture
    private Texture2D MakeTexture(int width, int height, Color color)
    {
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        Texture2D texture = new Texture2D(width, height);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    #endregion
}