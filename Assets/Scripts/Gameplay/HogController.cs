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
    [SerializeField, Range(0f, 1f)] private float rearSteeringAmount = .35f;
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .4f;
    
    [Header("Rocket Jump")]
    [SerializeField] private float jumpForce = 7f; // How much upward force to apply
    [SerializeField] private float jumpCooldown = 15f; // Time between jumps
    [SerializeField] private bool canJump = true; // Whether the player can jump
    public bool JumpOnCooldown => jumpOnCooldown;
    public float JumpCooldownRemaining => jumpCooldownRemaining;
    public float JumpCooldownTotal => jumpCooldown;

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR
    [SerializeField] private Transform[] wheelTransforms = new Transform[4]; // FL, FR, RL, RR

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.RTPC rpm;

    [Header("Debug UI")]
    [SerializeField] private bool showDebugUI = false;
    [SerializeField] private Color debugBackgroundColor = new Color(0, 0, 0, 0.7f);
    [SerializeField] private Color labelTextColor = Color.gray;
    [SerializeField] private Color valueTextColor = Color.white;

    // Input reference
    private InputManager inputManager;

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
    private Texture2D debugBackgroundTexture;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // Get reference to the InputManager
        inputManager = InputManager.Instance;
        if (inputManager == null)
        {
            Debug.LogError("InputManager not found in the scene");
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

        // Create texture for debug UI background
        debugBackgroundTexture = MakeTexture(2, 2, debugBackgroundColor);
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

        // Toggle debug UI with F1
        if (Input.GetKeyDown(KeyCode.F1))
        {
            showDebugUI = !showDebugUI;
        }
    }

    private void FixedUpdate()
    {
        // OWNER CLIENT: handle input and local visualization
        if (IsOwner)
        {
            ClientInput input = CollectInput();

            // Calculate steering locally for visual responsiveness
            CalculateSteeringAxis(input.steerInput);

            // Apply wheel visuals immediately without waiting for server
            AnimateWheelsLocal();

            // Send input to server for physics and non-owner clients
            SendInputServerRpc(input);

            // Audio feedback
            rpm.SetGlobalValue(Mathf.Clamp(input.throttleInput, -1f, 1f) * maxTorque);
        }
        // NON-OWNER CLIENTS: apply network synchronized wheel visuals
        else if (!IsServer)
        {
            AnimateWheelsFromNetwork();
        }
    }

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
        int totalLines = 8; // Adjust based on how many lines we're showing

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
            $"{speed:F1} m/s ({speed * 3.6f:F1} km/h)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Torque:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{currentTorque:F0} Nm", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Steering:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{steeringAxis:F2} ({steeringAxis * maxSteeringAngle:F0}°)", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Can Move:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            canMove ? "Yes" : "No", valueStyle);
        yPos += lineHeight;

        GUI.Label(new Rect(20, yPos, 85, lineHeight), "Wheels:", labelStyle);
        GUI.Label(new Rect(105, yPos, width - 105, lineHeight),
            $"{groundedWheelCount}/{wheelColliders.Length} grounded", valueStyle);
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

    private ClientInput CollectInput()
    {
        return new ClientInput
        {
            clientId = NetworkManager.Singleton.LocalClientId,
            throttleInput = inputManager.ThrottleInput,
            brakeInput = inputManager.BrakeInput,
            steerInput = inputManager.SteerInput,
        };
    }

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        if (!IsServer) return;

        // Apply physics based on input
        ApplyMotorTorque(input.throttleInput, input.brakeInput);

        // Calculate steering for physics
        CalculateSteeringAxis(input.steerInput);
        ApplySteering();

        // Update network variables for non-owner clients
        netSteeringAxis.Value = steeringAxis;
        netWheelRotationSpeed.Value = rb.linearVelocity.magnitude;
    }

    private void ApplyMotorTorque(float throttleInput, float brakeInput)
    {
        if (!canMove)
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                wheelColliders[i].motorTorque = 0;
                wheelColliders[i].brakeTorque = maxBrakeTorque;
            }
            return;
        }

        // Calculate effective throttle by subtracting brake input
        // If brake is fully applied (1.0), throttle becomes 0
        float effectiveThrottle = throttleInput * (1f - brakeInput);

        // Apply acceleration curve to the throttle input
        // Use absolute value for the curve evaluation, then reapply the sign
        float curvedThrottle = Mathf.Sign(effectiveThrottle) *
                              accelerationCurve.Evaluate(Mathf.Abs(effectiveThrottle));

        // Calculate target torque using the curved throttle value
        float targetTorque = curvedThrottle * maxTorque;

        // Smoothly transition to target torque with configurable response speed
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque,
                                         torqueResponseSpeed * maxTorque * Time.deltaTime);

        // Apply brake torque based on brake input
        float brakeTorque = brakeInput * maxBrakeTorque;

        // Apply torques to wheels
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            wheelColliders[i].motorTorque = currentTorque;
            wheelColliders[i].brakeTorque = brakeTorque;
        }
    }

    private void CalculateSteeringAxis(float steeringInput)
    {
        // Calculate the step based on steering speed and deltaTime
        float step = steeringSpeed * Time.fixedDeltaTime;

        // Calculate the direction of movement
        float direction = Mathf.Sign(steeringInput - steeringAxis);

        // Calculate the distance from the current value to the target
        float distance = Mathf.Abs(steeringInput - steeringAxis);

        // Apply exponential curve - the further from center, the faster it moves
        float exponentialStep = step * (1.0f + distance * 2.0f);

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
        float frontSteeringAngle = steeringAxis * maxSteeringAngle;
        float rearSteeringAngle = -frontSteeringAngle * rearSteeringAmount;

        float wheelSpeed = rb.linearVelocity.magnitude;
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

                Debug.Log($"Applied jump with preserving momentum: {horizontalVelocity}, new velocity: {newVelocity}");
            }
        }

        // Start cooldown
        StartCoroutine(JumpCooldownServer());
    }
    
    private IEnumerator JumpCooldownServer()
    {
        yield return new WaitForSeconds(jumpCooldown);
        jumpReady.Value = true;
        isJumping.Value = false;
    }

    #endregion

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

    // Clean up the texture when the object is destroyed
    private void OnDestroy()
    {
        if (debugBackgroundTexture != null)
            Destroy(debugBackgroundTexture);
    }
}