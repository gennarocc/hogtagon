using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;

public class HogController : NetworkBehaviour
{
    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f; // Maximum torque plied to wheels
    [SerializeField] private float brakeTorque = 300f; // Brake torque applied to wheels
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f; // Default maximum steering angle
    [SerializeField, Range(0.1f, 1f)]
    private float steeringSpeed = .7f;
    [SerializeField] public int handbrakeDriftMultiplier = 5; // How much grip the car loses when the user hit the handbrake.
    [SerializeField] public float cameraAngle;
    [SerializeField] private float frontLeftRpm;
    [SerializeField] private float velocity;
    
    [Header("Input Smoothing Settings")]
    [SerializeField, Range(1, 10)] public int steeringBufferSize = 5; // Number of frames to average for inputs
    [SerializeField, Range(0f, 20f)] public float minDeadzone = 3f; // Minimum deadzone at low speeds
    [SerializeField, Range(0f, 30f)] public float maxDeadzone = 10f; // Maximum deadzone at high speeds
    [SerializeField, Range(10f, 50f)] public float maxSpeedForSteering = 20f; // Speed at which steering responsiveness is at minimum
    [SerializeField, Range(0.1f, 1f)] public float minSteeringResponse = 0.6f; // Minimum steering response at high speeds (lower = less responsive)
    [SerializeField, Range(0.5f, 1f)] public float maxSteeringResponse = 1.0f; // Maximum steering response at low speeds
    [SerializeField] public enum WeightingMethod { Exponential, Logarithmic, Linear }
    [SerializeField] public WeightingMethod inputWeightingMethod = WeightingMethod.Linear;
    [SerializeField, Range(0.1f, 3f)] public float weightingFactor = 1.0f; // Controls drop-off rate (higher = steeper drop-off)

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
    [SerializeField] private AK.Wwise.Event TireScreech;
    [SerializeField] private AK.Wwise.Event HogImpact;
    [SerializeField] private AK.Wwise.RTPC rpm;

    // Input Smoothing
    private Queue<float> _recentSteeringInputs;
    private List<float> _steeringInputsList = new List<float>();
    
    // Network variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    
    // Physics and control variables
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private float driftingAxis;
    private bool isTractionLocked = true;

    // Wheel references
    private WheelFrictionCurve FLwheelFriction;
    private float FLWextremumSlip;
    private WheelFrictionCurve FRwheelFriction;
    private float FRWextremumSlip;
    private WheelFrictionCurve RLwheelFriction;
    private float RLWextremumSlip;
    private WheelFrictionCurve RRwheelFriction;
    private float RRWextremumSlip;

    // Struct to send input data to server
    public struct ClientInput : INetworkSerializable
    {
        public ulong clientId;
        public float moveInput;
        public float brakeInput;
        public bool handbrake;
        public float rawCameraAngle; // Changed to raw camera angle instead of processed steering

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref handbrake);
            serializer.SerializeValue(ref rawCameraAngle);
        }
    }

    private void Start()
    {
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

        EngineOn.Post(gameObject);
        // Set player color
    }

    private void Update()
    {
        if (!IsOwner) return;

        if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.JoystickButton0)) && !transform.root.gameObject.GetComponent<Player>().isSpectating)
        {
            // Play Horn Sound
            HogSoundManager.instance.PlayNetworkedSound(transform.root.gameObject, HogSoundManager.SoundEffectType.HogHorn);
        }
    }

    private void FixedUpdate()
    {
        frontLeftRpm = frontLeftWheelCollider.rpm;
        rpm.SetGlobalValue(frontLeftWheelCollider.rpm);
        velocity = rb.linearVelocity.magnitude;
        // Save the local velocity of the car in the x axis. Used to know if the car is drifting.
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;

        if (IsClient && IsOwner)
        {
            ClientMove();
        }

        AnimateWheels();
        DriftCarPS();
    }

    private void ClientMove()
    {
        // Calculate raw camera angle relative to car
        Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
        cameraVector.y = 0;
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
        float rawCameraAngle = Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));
        
        // Add to input buffer for smoothing using weighted average
        _recentSteeringInputs.Enqueue(rawCameraAngle);
        if (_recentSteeringInputs.Count > steeringBufferSize)
            _recentSteeringInputs.Dequeue();
        
        // Convert queue to list for weighted processing
        _steeringInputsList.Clear();
        _steeringInputsList.AddRange(_recentSteeringInputs);
        
        // Store current angle for visualization/debugging
        // We'll use the weighted average for our displayed value
        if (_steeringInputsList.Count > 0)
        {
            cameraAngle = CalculateWeightedAverage(_steeringInputsList, weightingFactor);
        }
        else
        {
            cameraAngle = rawCameraAngle;
        }

        float move = 0;
        float brake = 0f;
        bool handbrakeOn = false;

        // Keyboard input
        if (Input.GetKey(KeyCode.W))
        {
            move = 1f;
        }
        if (Input.GetKey(KeyCode.S))
        {
            move = -1f;
        }
        if (Input.GetKey(KeyCode.Space))
        {
            handbrakeOn = true;
        }

        // Controller input (if controller is connected)
        if (Input.GetJoystickNames().Length > 0)
        {
            // Right trigger for forward, Left trigger for reverse
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

            // Camera control with right stick
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

        // Send raw inputs to server instead of processed steering
        ClientInput input = new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = move,
            brakeInput = brake,
            handbrake = handbrakeOn,
            rawCameraAngle = _steeringInputsList.Count > 0 ? CalculateWeightedAverage(_steeringInputsList, weightingFactor) : rawCameraAngle,
        };

        SendInputServerRpc(input);
    }
    
    // Calculate weighted average of inputs with more recent inputs having more weight
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

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        // Calculate steering angle server-side to ensure consistency
        float steeringAngle = CalculateSteeringFromCameraAngle(input.rawCameraAngle, input.moveInput);
        
        ApplyMotorTorque(input.moveInput, input.brakeInput);
        ApplySteering(steeringAngle, input.moveInput);
        
        isDrifting.Value = localVelocityX > .25f ? true : false;
        
        if (input.handbrake)
        {
            Handbrake();
            isDrifting.Value = true;
        }
        else if (!input.handbrake && !isTractionLocked)
        {
            // Only call RecoverTraction once when handbrake is released
            RecoverTraction();
        }
    }

    // New method to centralize steering angle calculation on the server
    private float CalculateSteeringFromCameraAngle(float cameraAngle, float moveInput)
    {
        // Apply adaptive deadzone - higher deadzone at higher speeds
        float speedFactor = Mathf.Clamp01(rb.linearVelocity.magnitude / maxSpeedForSteering);
        float deadzone = Mathf.Lerp(minDeadzone, maxDeadzone, speedFactor);
        
        if (cameraAngle < deadzone && cameraAngle > -deadzone)
            cameraAngle = 0f;
            
        // If camera angle is small, treat it as zero
        if (Math.Abs(cameraAngle) < 1f) 
            cameraAngle = 0f;

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
        
        // Progressive steering response - less responsive at higher speeds
        float steeringResponse = Mathf.Lerp(maxSteeringResponse, minSteeringResponse, speedFactor);
        finalSteeringAngle *= steeringResponse;
        
        return finalSteeringAngle;
    }

    private void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        if (!canMove)
        {
            frontLeftWheelCollider.motorTorque = 0;
            frontRightWheelCollider.motorTorque = 0;
            rearLeftWheelCollider.motorTorque = 0;
            rearRightWheelCollider.motorTorque = 0;
            return;
        }

        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.deltaTime * 3f); // Adjust 100f to control how fast it reaches maxTorque
        float motorTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;

        frontLeftWheelCollider.motorTorque = motorTorque;
        frontRightWheelCollider.motorTorque = motorTorque;
        rearLeftWheelCollider.motorTorque = motorTorque;
        rearRightWheelCollider.motorTorque = motorTorque;
    }

    private void ApplySteering(float steeringAngle, float moveInput)
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
        yield return new WaitForSeconds(.5f);
        collisionForceOnCooldown = false;
    }

    public void DriftCarPS()
    {
        // Check if wheels are grounded and drifting
        bool rearLeftGrounded = rearLeftWheelCollider.isGrounded;
        bool rearRightGrounded = rearRightWheelCollider.isGrounded;

        if (isDrifting.Value)
        {
            // Only play particle effects and skid marks if the wheels are grounded
            if (rearLeftGrounded)
            {
                RLWParticleSystem.Play();
                RLWTireSkid.emitting = true;
            }
            else
            {
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
        }
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