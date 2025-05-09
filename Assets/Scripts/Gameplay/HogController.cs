using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using Hogtagon.Core.Infrastructure;
using Cinemachine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

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

    [Header("Camera Control Scheme")]
    [SerializeField] public bool useCameraBasedSteering = false;
    [SerializeField, Range(0f, 1f)] private float cameraSteeringSensitivity = 0.8f; // How sensitive the steering is
    [SerializeField] private AnimationCurve cameraSteeringCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Range(0f, 0.3f)] private float steeringDeadzone = 0.1f; // Add deadzone parameter
    [SerializeField] private bool invertCameraSteering = false;
    [Header("Ping Monitoring")]
    [SerializeField] private float pingCheckInterval = 0.5f; // How often to check ping
    [SerializeField] private int maxPingSamples = 4; // 4 samples over 2 seconds (with 0.5s interval)
    [SerializeField] private float warningThreshold = 100f; // Yellow warning (ms)
    [SerializeField] private float criticalThreshold = 150f; // Red warning (ms)

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
    [SerializeField] private Transform[] wheelTransforms = new Transform[4]; // FL, FR, RL, RR
    [SerializeField] private HogVisualEffects visualEffects; // Reference to the visual effects component
    [SerializeField] private MeshRenderer tailLights;
    [SerializeField] private Material tailLightsOff;
    [SerializeField] private Material tailLightsOn;

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
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> isJumping = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> jumpReady = new NetworkVariable<bool>(true);
    private NetworkVariable<float> netSteeringAxis = new NetworkVariable<float>(0f);
    private NetworkVariable<float> netWheelRotationSpeed = new NetworkVariable<float>(0f);
    private NetworkVariable<float> netInputSum = new NetworkVariable<float>(0f);
    private NetworkVariable<float> netVelocity = new NetworkVariable<float>(0f);
    private NetworkVariable<float> netBrakeInput = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> rearLeftWheelGrounded = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> rearRightWheelGrounded = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Queue<float> pingSamples = new Queue<float>();
    private float averagePing = 0f;
    private bool shouldDisplayPing = false;
    private GUIStyle pingStyle;

    // Unity GUI positioning and styling
    private int fontSize = 20;
    private int padding = 10;
    private Color warningColor = Color.yellow;
    private Color criticalColor = Color.red;


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
    private CinemachineFreeLook playerCamera;

    #endregion

    #region Lifecycle Methods

    private void Start()
    {
        // Initialize GUI style
        pingStyle = new GUIStyle();
        pingStyle.fontSize = fontSize;
        pingStyle.fontStyle = FontStyle.Bold;
        pingStyle.normal.textColor = warningColor;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Update jump cooldown
        if (jumpOnCooldown)
        {
            jumpCooldownRemaining -= Time.deltaTime;
            if (jumpCooldownRemaining <= 0 && jumpOnCooldown)
            {
                jumpOnCooldown = false;
                SoundManager.Instance.PlayLocalSound(gameObject, SoundManager.SoundEffectType.HogJumpReady);
            }
        }

        // Toggle debug UI with F1
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
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

            CalculateEngineAudio(Math.Sign(input.throttleInput - input.brakeInput), netVelocity.Value);


            // Set BrakeLight Emmision
            if (input.brakeInput > 0) tailLights.material = tailLightsOn;
            else tailLights.material = tailLightsOff;
        }

        AnimateWheelsFromNetwork();

        // Set BrakeLight
        if (!IsOwner)
        {
            if (netBrakeInput.Value > 0) tailLights.material = tailLightsOn;
            else tailLights.material = tailLightsOff;

            CalculateEngineAudio(Math.Sign(netInputSum.Value), netVelocity.Value);
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
            inputManager.JumpPressed += OnJumpPressed;
            inputManager.HornPressed += OnHornPressed;

            playerCamera = GameObject.Find("PlayerCamera").GetComponent<CinemachineFreeLook>();
            if (playerCamera == null)
            {
                Debug.LogWarning("[HogController] PlayerCamera not found!");
            }

            StartCoroutine(PingMonitorRoutine());
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

        // Subscribe to network variable changes to trigger effects
        isDrifting.OnValueChanged += OnDriftingChanged;
        isJumping.OnValueChanged += OnJumpingChanged;

        StartCoroutine(PlayEngineAfterDelay());
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
            // Use the networked wheel grounded values instead of direct wheel collider access
            bool rearLeftGrounded = rearLeftWheelGrounded.Value;
            bool rearRightGrounded = rearRightWheelGrounded.Value;

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
        if (!GetComponent<Player>().isSpectating) // Can not honk while dead 
            SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.HogHorn);
    }

    private ClientInput CollectInput()
    {
        // Get steering input based on control scheme
        float steerValue = useCameraBasedSteering
            ? CalculateCameraBasedSteering()
            : inputManager.SteerInput;

        ClientInput input = new ClientInput
        {
            clientId = NetworkManager.Singleton.LocalClientId,
            throttleInput = inputManager.ThrottleInput,
            brakeInput = inputManager.BrakeInput,
            steerInput = steerValue,
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

        // Update wheel grounded network variables
        rearLeftWheelGrounded.Value = wheelColliders[2].isGrounded;
        rearRightWheelGrounded.Value = wheelColliders[3].isGrounded;
        netInputSum.Value = Math.Sign(input.throttleInput - input.brakeInput);
        netVelocity.Value = rb.linearVelocity.magnitude;

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
        netBrakeInput.Value = input.brakeInput;
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

    private float CalculateCameraBasedSteering()
    {
        if (playerCamera == null)
            return 0f;

        // Get the vector from camera to vehicle (in world space)
        Vector3 cameraVector = transform.position - playerCamera.transform.position;

        // Flatten to XZ plane (ignore Y component)
        cameraVector.y = 0;

        // Get car's forward direction (flattened)
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z).normalized;

        // Calculate the angle between vectors
        float angle = Vector3.Angle(carDirection, cameraVector);

        // Determine the sign (left/right) using dot product with car's right vector
        float sign = Mathf.Sign(Vector3.Dot(cameraVector, transform.right));

        // Get the signed angle
        float signedAngle = angle * sign;

        // Normalize to -1 to 1 range based on max steering angle
        float normalizedValue = Mathf.Clamp(signedAngle / maxSteeringAngle, -1f, 1f);

        // Apply deadzone
        if (Mathf.Abs(normalizedValue) < steeringDeadzone)
        {
            return 0f; // Return zero steering if within deadzone
        }
        else
        {
            // Remap the value to still use the full -1 to 1 range
            // This creates a smooth transition at the edge of the deadzone
            float remappedValue = Mathf.Sign(normalizedValue) *
                (Mathf.Abs(normalizedValue) - steeringDeadzone) / (1f - steeringDeadzone);
            normalizedValue = remappedValue;
        }

        // Apply sensitivity
        if (cameraSteeringSensitivity != 1.0f)
        {
            float absValue = Mathf.Abs(normalizedValue);
            float signedPower = Mathf.Pow(absValue, 2.0f - cameraSteeringSensitivity) * Mathf.Sign(normalizedValue);
            normalizedValue = signedPower;
        }

        // Apply steering curve for better control
        float steeringValue = Mathf.Sign(normalizedValue) * cameraSteeringCurve.Evaluate(Mathf.Abs(normalizedValue));

        // Apply inversion if needed
        if (invertCameraSteering)
            steeringValue = -steeringValue;

        return steeringValue;
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

    public void ResetJump()
    {
        if (!IsServer) return;
        // Reset network variables
        jumpReady.Value = true;
        isJumping.Value = false;

        // Notify all clients to reset their cooldowns
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

        visualEffects.CreateCrashSparks(hitNormal, hitPoint);

        StartCoroutine(BumperCollisionCooldown());
    }

    #endregion

    #region Audio and Visual Effects

    private void CalculateEngineAudio(float inputSum, float speed)
    {
        // Interpolate the throttle input so get smoother engine transitions.
        var lerp = Mathf.Lerp(lerpedThrottleInput, inputSum, Time.deltaTime * 3); // Lerp speed is 3
        lerpedThrottleInput = lerp;

        // Wwise expects a value between 1-100 so we multiply by 5 (car speed is around 1-23)
        rpm.SetValue(gameObject, speed * 5 * lerpedThrottleInput);
    }

    #endregion


    private IEnumerator PingMonitorRoutine()
    {
        while (true)
        {
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                // Get current RTT from NetworkManager
                float currentPing = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(
                    NetworkManager.ServerClientId);

                // Add to our samples
                pingSamples.Enqueue(currentPing);

                // Keep only the most recent samples for our window
                if (pingSamples.Count > maxPingSamples)
                {
                    pingSamples.Dequeue();
                }

                // Calculate the average
                float sum = 0f;
                foreach (float ping in pingSamples)
                {
                    sum += ping;
                }
                averagePing = sum / pingSamples.Count;

                // Determine if we should show the warning based on thresholds
                shouldDisplayPing = averagePing >= warningThreshold;

                // Update the text color based on ping value
                if (averagePing >= criticalThreshold)
                {
                    pingStyle.normal.textColor = criticalColor;
                }
                else
                {
                    pingStyle.normal.textColor = warningColor;
                }
            }

            yield return new WaitForSeconds(pingCheckInterval);
        }
    }

    private void OnGUI()
    {
        // Keep existing ping display code
        if (IsOwner && shouldDisplayPing && NetworkManager.Singleton.IsConnectedClient)
        {
            // Display in bottom left corner
            GUI.Label(
                new Rect(padding, Screen.height - fontSize - padding, 200, fontSize),
                $"High Latency: {averagePing:0}ms",
                pingStyle
            );
        }
    }

    public IEnumerator PlayEngineAfterDelay()
    {
        yield return new WaitForSeconds(1f);
        SoundManager.Instance.PlayNetworkedSound(gameObject, SoundManager.SoundEffectType.EngineOn);
    }
}