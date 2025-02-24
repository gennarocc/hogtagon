using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;

// Structure to send inputs from client to server
public struct NetworkCarInput : INetworkSerializable
{
    public ulong clientId;
    public float moveInput;
    public float brakeInput;
    public bool handbrake;
    public float steeringAngle;
    public uint inputTick;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref moveInput);
        serializer.SerializeValue(ref brakeInput);
        serializer.SerializeValue(ref handbrake);
        serializer.SerializeValue(ref steeringAngle);
        serializer.SerializeValue(ref inputTick);
    }
}

// Structure to store state snapshots for interpolation and prediction
public struct CarState : INetworkSerializable
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 angularVelocity;
    public float frontLeftSteerAngle;
    public float frontRightSteerAngle;
    public float rearLeftSteerAngle;
    public float rearRightSteerAngle;
    public bool isDrifting;
    public uint tick;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref rotation);
        serializer.SerializeValue(ref velocity);
        serializer.SerializeValue(ref angularVelocity);
        serializer.SerializeValue(ref frontLeftSteerAngle);
        serializer.SerializeValue(ref frontRightSteerAngle);
        serializer.SerializeValue(ref rearLeftSteerAngle);
        serializer.SerializeValue(ref rearRightSteerAngle);
        serializer.SerializeValue(ref isDrifting);
        serializer.SerializeValue(ref tick);
    }
}

public class NetworkCarController : NetworkBehaviour
{
    [Header("Hog Params")]
    [SerializeField] public bool canMove = true;
    [SerializeField] private float maxTorque = 500f;
    [SerializeField] private float brakeTorque = 300f;
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f;
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .7f;
    [SerializeField] public int handbrakeDriftMultiplier = 5;
    [SerializeField] public float cameraAngle;
    [SerializeField] private float frontLeftRpm;
    [SerializeField] private float velocity;
    [SerializeField] private bool syncParentPosition = true;
    [SerializeField] private bool syncOnlyXZ = true;
    
    [Header("Network Settings")]
    [SerializeField] private float positionLerpSpeed = 10f;
    [SerializeField] private float rotationLerpSpeed = 10f;
    [SerializeField] private uint tickRate = 60; // How many fixed updates per second
    [SerializeField] private int bufferSize = 10; // Size of the state buffer for interpolation

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private CinemachineFreeLook freeLookCamera;
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
    [SerializeField] private AK.Wwise.Event CarExplosion;
    [SerializeField] private AK.Wwise.RTPC rpm;
    [SerializeField] private AK.Wwise.Event TireScreech;
    [SerializeField] private AK.Wwise.Event HogImpact;
    [SerializeField] private AK.Wwise.State ImpactLevel;

    // Network variables
    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private NetworkVariable<CarState> serverState = new NetworkVariable<CarState>();
    private NetworkVariable<uint> serverTick = new NetworkVariable<uint>(0);
    
    // Client prediction variables
    private Queue<CarState> stateBuffer = new Queue<CarState>();
    private Dictionary<uint, NetworkCarInput> inputBuffer = new Dictionary<uint, NetworkCarInput>();
    private uint localTick = 0;
    private bool hasSentInputs = false;
    private float tickInterval => 1f / tickRate;
    private float tickTimer = 0f;
    private CarState targetState;
    private CarState previousState;
    private float interpolationTime = 0f;
    
    // Local variables
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;
    private bool isTractionLocked = true;
    private bool isInterpolating = false;

    // Wheel references
    private WheelFrictionCurve FLwheelFriction;
    private float FLWextremumSlip;
    private WheelFrictionCurve FRwheelFriction;
    private float FRWextremumSlip;
    private WheelFrictionCurve RLwheelFriction;
    private float RLWextremumSlip;
    private WheelFrictionCurve RRwheelFriction;
    private float RRWextremumSlip;

    void Start()
    {
        if (IsServer)
        {
            // Full physics setup on server
            rb.centerOfMass = centerOfMass;
            rb.isKinematic = false;
            
            // Store original wheel friction values
            StoreWheelFrictionValues();
        }
        else if (IsOwner)
        {
            // Full physics setup on owning client for prediction
            rb.centerOfMass = centerOfMass;
            rb.isKinematic = false;
            
            // Store original wheel friction values
            StoreWheelFrictionValues();
        }
        else
        {
            // For non-owner clients, we'll use interpolation instead of physics
            rb.isKinematic = true;
        }

        // Play engine sound for all clients
        EngineOn.Post(gameObject);
    }

    private void StoreWheelFrictionValues()
    {
        // Store original wheel friction values for later use with handbrake
        FLwheelFriction = frontLeftWheelCollider.sidewaysFriction;
        FLWextremumSlip = FLwheelFriction.extremumSlip;
        
        FRwheelFriction = frontRightWheelCollider.sidewaysFriction;
        FRWextremumSlip = FRwheelFriction.extremumSlip;
        
        RLwheelFriction = rearLeftWheelCollider.sidewaysFriction;
        RLWextremumSlip = RLwheelFriction.extremumSlip;
        
        RRwheelFriction = rearRightWheelCollider.sidewaysFriction;
        RRWextremumSlip = RRwheelFriction.extremumSlip;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            serverTick.Value = 0;
        }
        
        if (IsClient && !IsOwner)
        {
            // Subscribe to state changes
            serverState.OnValueChanged += OnServerStateChanged;
        }
    }

    private void OnServerStateChanged(CarState previousValue, CarState newValue)
    {
        if (!IsOwner)
        {
            // Add the new state to our buffer for interpolation
            stateBuffer.Enqueue(newValue);
            
            // Keep buffer at desired size
            while (stateBuffer.Count > bufferSize)
            {
                stateBuffer.Dequeue();
            }
            
            // Set up interpolation between states
            if (stateBuffer.Count >= 2)
            {
                var stateArray = stateBuffer.ToArray();
                previousState = stateArray[stateBuffer.Count - 2];
                targetState = stateArray[stateBuffer.Count - 1];
                interpolationTime = 0f;
                isInterpolating = true;
            }
        }
    }

    private void Update()
    {
        if (IsClient && !IsOwner && isInterpolating)
        {
            // Interpolate non-owner cars
            InterpolateRemoteCar();
        }
    }
    
    private void FixedUpdate()
    {
        // Update common variables
        if (!rb.isKinematic)
        {
            frontLeftRpm = frontLeftWheelCollider.rpm;
            rpm.SetGlobalValue(frontLeftWheelCollider.rpm);
            velocity = rb.linearVelocity.magnitude;
            localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;
        }
        
        // Server and Owner tick handling
        if (IsServer || IsOwner)
        {
            tickTimer += Time.fixedDeltaTime;
            
            if (tickTimer >= tickInterval)
            {
                tickTimer -= tickInterval;
                
                if (IsServer)
                {
                    // Increment server tick
                    serverTick.Value++;
                    
                    // Update server state for non-owner clients
                    UpdateServerState();
                }
                
                if (IsOwner)
                {
                    // Increment local tick counter
                    localTick++;
                    
                    // Process client input
                    ProcessClientInput();
                }
            }
            
            // Sync the parent position to match the car's position if needed
            if (syncParentPosition && transform.parent != null)
            {
                Vector3 newParentPosition;
                
                if (syncOnlyXZ)
                {
                    // Only sync X and Z coordinates, leave Y (height) alone
                    newParentPosition = new Vector3(
                        transform.position.x,
                        transform.parent.position.y,
                        transform.position.z
                    );
                }
                else
                {
                    newParentPosition = transform.position;
                }
                
                // Update parent position
                transform.parent.position = newParentPosition;
            }
        }
        
        // Always animate wheels
        AnimateWheels();
        
        // Update drift effects
        UpdateDriftEffects();
    }
    
    private void UpdateServerState()
    {
        var state = new CarState
        {
            // Always use world space position and rotation
            position = transform.position,
            rotation = transform.rotation,
            velocity = rb.linearVelocity,
            angularVelocity = rb.angularVelocity,
            frontLeftSteerAngle = frontLeftWheelCollider.steerAngle,
            frontRightSteerAngle = frontRightWheelCollider.steerAngle,
            rearLeftSteerAngle = rearLeftWheelCollider.steerAngle,
            rearRightSteerAngle = rearRightWheelCollider.steerAngle,
            isDrifting = isDrifting.Value,
            tick = serverTick.Value
        };
        
        serverState.Value = state;
    }
    
    private void ProcessClientInput()
    {
        if (!IsOwner) return;
        
        // Determine camera angle to car for steering
        Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
        cameraVector.y = 0;
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
        cameraAngle = Vector3.Angle(carDirection, cameraVector) * Mathf.Sign(Vector3.Dot(cameraVector, transform.right));
        
        float move = 0;
        float brake = 0f;
        float steering = cameraAngle;
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
                freeLookCamera.m_XAxis.Value += rightStickX;
            }
            if (rightStickY != 0)
            {
                freeLookCamera.m_YAxis.Value += rightStickY;
            }
            
            // Check for handbrake on a controller button
            if (Input.GetButton("XRI_Right_Shoulder") || Input.GetButton("XRI_Left_Shoulder"))
            {
                handbrakeOn = true;
            }
        }
        
        // Create input struct
        NetworkCarInput input = new NetworkCarInput
        {
            clientId = OwnerClientId,
            moveInput = move,
            brakeInput = brake,
            handbrake = handbrakeOn,
            steeringAngle = steering,
            inputTick = localTick
        };
        
        // Store input for reconciliation
        inputBuffer[localTick] = input;
        
        // Apply input locally
        ApplyInput(input);
        
        // Send input to server
        SendInputServerRpc(input);
    }
    
    [ServerRpc]
    private void SendInputServerRpc(NetworkCarInput input)
    {
        if (!canMove) return;
        
        // Server processes the input 
        ApplyInput(input);
        
        // Send state correction to client if needed
        if (Vector3.Distance(rb.position, serverState.Value.position) > 1.0f)
        {
            SendStateClientRpc(serverState.Value, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { input.clientId }
                }
            });
        }
    }
    
    [ClientRpc]
    private void SendStateClientRpc(CarState state, ClientRpcParams clientRpcParams)
    {
        if (!IsOwner) return;
        
        // Calculate the tick difference
        int tickDiff = (int)(state.tick - localTick);
        
        // Only correct if server is ahead or significantly behind
        if (tickDiff > 0 || tickDiff < -5)
        {
            // Apply the correction to transform (world space)
            transform.position = state.position;
            transform.rotation = state.rotation;
            
            // Apply physics properties to rigidbody
            rb.linearVelocity = state.velocity;
            rb.angularVelocity = state.angularVelocity;
            
            // Apply wheel states
            frontLeftWheelCollider.steerAngle = state.frontLeftSteerAngle;
            frontRightWheelCollider.steerAngle = state.frontRightSteerAngle;
            rearLeftWheelCollider.steerAngle = state.rearLeftSteerAngle;
            rearRightWheelCollider.steerAngle = state.rearRightSteerAngle;
            
            // Sync parent position if needed
            if (syncParentPosition && transform.parent != null)
            {
                Vector3 newParentPosition;
                
                if (syncOnlyXZ)
                {
                    // Only sync X and Z coordinates, leave Y (height) alone
                    newParentPosition = new Vector3(
                        transform.position.x,
                        transform.parent.position.y,
                        transform.position.z
                    );
                }
                else
                {
                    newParentPosition = transform.position;
                }
                
                transform.parent.position = newParentPosition;
            }
            
            // Replay inputs from correction onward
            uint replayFromTick = state.tick;
            
            foreach (var kvp in inputBuffer)
            {
                if (kvp.Key > replayFromTick)
                {
                    ApplyInput(kvp.Value);
                }
            }
        }
    }
    
    private void ApplyInput(NetworkCarInput input)
    {
        if (!canMove) return;
        
        // Apply motor forces
        ApplyMotorTorque(input.moveInput, input.brakeInput);
        
        // Apply steering
        ApplySteering(input.steeringAngle);
        
        // Apply handbrake if needed
        ApplyHandbrake(input.handbrake);
        
        // Update drift state
        isDrifting.Value = (localVelocityX > 0.25f || input.handbrake);
    }
    
    private void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        // Calculate motor torque with smooth acceleration
        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.fixedDeltaTime * 300f);
        
        // Apply torque to all wheels for 4-wheel drive
        frontLeftWheelCollider.motorTorque = currentTorque;
        frontRightWheelCollider.motorTorque = currentTorque;
        rearLeftWheelCollider.motorTorque = currentTorque;
        rearRightWheelCollider.motorTorque = currentTorque;
        
        // Apply brakes if needed
        float brakingForce = brakeInput > 0 ? brakeTorque * brakeInput : 0f;
        frontLeftWheelCollider.brakeTorque = brakingForce;
        frontRightWheelCollider.brakeTorque = brakingForce;
        rearLeftWheelCollider.brakeTorque = brakingForce;
        rearRightWheelCollider.brakeTorque = brakingForce;
    }
    
    private void ApplySteering(float cameraAngle)
    {
        // Apply dead zone to steering
        if (cameraAngle < 1f && cameraAngle > -1f) cameraAngle = 0f;
        
        // Calculate steering angles for front and rear wheels
        float frontSteeringAngle = Mathf.Clamp(cameraAngle, -maxSteeringAngle, maxSteeringAngle);
        // Rear wheels steer in opposite direction for better handling, with reduced angle
        float rearSteeringAngle = frontSteeringAngle * -0.35f;
        
        // Apply steering with smooth transition
        frontLeftWheelCollider.steerAngle = Mathf.Lerp(frontLeftWheelCollider.steerAngle, frontSteeringAngle, steeringSpeed);
        frontRightWheelCollider.steerAngle = Mathf.Lerp(frontRightWheelCollider.steerAngle, frontSteeringAngle, steeringSpeed);
        rearLeftWheelCollider.steerAngle = Mathf.Lerp(rearLeftWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
        rearRightWheelCollider.steerAngle = Mathf.Lerp(rearRightWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
    }
    
    private void ApplyHandbrake(bool handbrakeOn)
    {
        if (handbrakeOn)
        {
            // Reduce wheel friction for drifting
            var FLSidewaysFriction = frontLeftWheelCollider.sidewaysFriction;
            var FRSidewaysFriction = frontRightWheelCollider.sidewaysFriction;
            var RLSidewaysFriction = rearLeftWheelCollider.sidewaysFriction;
            var RRSidewaysFriction = rearRightWheelCollider.sidewaysFriction;
            
            FLSidewaysFriction.extremumSlip = FLWextremumSlip * handbrakeDriftMultiplier;
            FRSidewaysFriction.extremumSlip = FRWextremumSlip * handbrakeDriftMultiplier;
            RLSidewaysFriction.extremumSlip = RLWextremumSlip * handbrakeDriftMultiplier;
            RRSidewaysFriction.extremumSlip = RRWextremumSlip * handbrakeDriftMultiplier;
            
            frontLeftWheelCollider.sidewaysFriction = FLSidewaysFriction;
            frontRightWheelCollider.sidewaysFriction = FRSidewaysFriction;
            rearLeftWheelCollider.sidewaysFriction = RLSidewaysFriction;
            rearRightWheelCollider.sidewaysFriction = RRSidewaysFriction;
            
            // Apply brake torque
            frontLeftWheelCollider.brakeTorque = brakeTorque;
            frontRightWheelCollider.brakeTorque = brakeTorque;
            rearLeftWheelCollider.brakeTorque = brakeTorque;
            rearRightWheelCollider.brakeTorque = brakeTorque;
        }
        else
        {
            // Restore normal friction
            var FLSidewaysFriction = frontLeftWheelCollider.sidewaysFriction;
            var FRSidewaysFriction = frontRightWheelCollider.sidewaysFriction;
            var RLSidewaysFriction = rearLeftWheelCollider.sidewaysFriction;
            var RRSidewaysFriction = rearRightWheelCollider.sidewaysFriction;
            
            FLSidewaysFriction.extremumSlip = FLWextremumSlip;
            FRSidewaysFriction.extremumSlip = FRWextremumSlip;
            RLSidewaysFriction.extremumSlip = RLWextremumSlip;
            RRSidewaysFriction.extremumSlip = RRWextremumSlip;
            
            frontLeftWheelCollider.sidewaysFriction = FLSidewaysFriction;
            frontRightWheelCollider.sidewaysFriction = FRSidewaysFriction;
            rearLeftWheelCollider.sidewaysFriction = RLSidewaysFriction;
            rearRightWheelCollider.sidewaysFriction = RRSidewaysFriction;
            
            // Release brakes (only if not braking normally)
            frontLeftWheelCollider.brakeTorque = 0;
            frontRightWheelCollider.brakeTorque = 0;
            rearLeftWheelCollider.brakeTorque = 0;
            rearRightWheelCollider.brakeTorque = 0;
        }
    }
    
    private void InterpolateRemoteCar()
    {
        // Lerp between previous and target state
        interpolationTime += Time.deltaTime * positionLerpSpeed;
        float t = Mathf.Clamp01(interpolationTime);
        
        // Standard interpolation
        transform.position = Vector3.Lerp(previousState.position, targetState.position, t);
        transform.rotation = Quaternion.Slerp(previousState.rotation, targetState.rotation, t * rotationLerpSpeed * Time.deltaTime);
        
        // Apply wheel steering angles
        frontLeftWheelCollider.steerAngle = Mathf.Lerp(previousState.frontLeftSteerAngle, targetState.frontLeftSteerAngle, t);
        frontRightWheelCollider.steerAngle = Mathf.Lerp(previousState.frontRightSteerAngle, targetState.frontRightSteerAngle, t);
        rearLeftWheelCollider.steerAngle = Mathf.Lerp(previousState.rearLeftSteerAngle, targetState.rearLeftSteerAngle, t);
        rearRightWheelCollider.steerAngle = Mathf.Lerp(previousState.rearRightSteerAngle, targetState.rearRightSteerAngle, t);
        
        // Update drift state
        isDrifting.Value = targetState.isDrifting;
        
        // If we've reached the target state, stop interpolating
        if (t >= 1.0f)
        {
            isInterpolating = false;
        }
    }
    
    private void AnimateWheels()
    {
        // Apply visual rotation based on steering angle
        frontLeftWheelTransform.localRotation = Quaternion.Euler(0, frontLeftWheelCollider.steerAngle, 0);
        frontRightWheelTransform.localRotation = Quaternion.Euler(0, frontRightWheelCollider.steerAngle, 0);
        rearLeftWheelTransform.localRotation = Quaternion.Euler(0, rearLeftWheelCollider.steerAngle, 0);
        rearRightWheelTransform.localRotation = Quaternion.Euler(0, rearRightWheelCollider.steerAngle, 0);
    }
    
    private void UpdateDriftEffects()
    {
        // Update drift visual effects
        if (isDrifting.Value)
        {
            // Enable particle systems for tire smoke
            if (RLWParticleSystem && !RLWParticleSystem.isPlaying)
                RLWParticleSystem.Play();
            if (RRWParticleSystem && !RRWParticleSystem.isPlaying)
                RRWParticleSystem.Play();
            
            // Enable tire skid marks
            if (RLWTireSkid)
                RLWTireSkid.emitting = true;
            if (RRWTireSkid)
                RRWTireSkid.emitting = true;
            
            // Play tire screech sound
            if (IsOwner)
                TireScreech.Post(gameObject);
        }
        else
        {
            // Disable particle systems
            if (RLWParticleSystem && RLWParticleSystem.isPlaying)
                RLWParticleSystem.Stop();
            if (RRWParticleSystem && RRWParticleSystem.isPlaying)
                RRWParticleSystem.Stop();
            
            // Disable tire skid marks
            if (RLWTireSkid)
                RLWTireSkid.emitting = false;
            if (RRWTireSkid)
                RRWTireSkid.emitting = false;
        }
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        
        // Play impact sound and effects for collisions
        HogImpact.Post(gameObject);
        
        // Set impact level based on collision force
        float impactForce = collision.impulse.magnitude;
        if (impactForce > 10f && !collisionForceOnCooldown)
        {
            StartCoroutine(CollisionForceCooldown());
            
            // Broadcast collision to all clients
            BroadcastCollisionClientRpc(impactForce);
        }
    }
    
    private IEnumerator CollisionForceCooldown()
    {
        collisionForceOnCooldown = true;
        yield return new WaitForSeconds(0.5f);
        collisionForceOnCooldown = false;
    }
    
    [ClientRpc]
    private void BroadcastCollisionClientRpc(float impactForce)
    {
        // // Play audio based on impact force
        // if (impactForce > 50f)
        // {
        //     ImpactLevel.SetValue(2); // Hard impact
        // }
        // else if (impactForce > 20f)
        // {
        //     ImpactLevel.SetValue(1); // Medium impact
        // }
        // else
        // {
        //     ImpactLevel.SetValue(0); // Light impact
        // }
    }
    
    public override void OnNetworkDespawn()
    {
        if (IsClient)
        {
            // Stop all effects
            if (RLWParticleSystem && RLWParticleSystem.isPlaying)
                RLWParticleSystem.Stop();
            if (RRWParticleSystem && RRWParticleSystem.isPlaying)
                RRWParticleSystem.Stop();
            
            // Stop engine sound
            EngineOff.Post(gameObject);
            
            // Unsubscribe from events
            if (!IsOwner)
            {
                serverState.OnValueChanged -= OnServerStateChanged;
            }
        }
    }
}