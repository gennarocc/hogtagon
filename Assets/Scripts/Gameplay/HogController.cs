using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System;
using System.Collections;

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
    [SerializeField] private AK.Wwise.Event CarExplosion;
    [SerializeField] private AK.Wwise.RTPC rpm;
    [SerializeField] private AK.Wwise.Event TireScreech;
    [SerializeField] private AK.Wwise.Event HogImpact;
    [SerializeField] private AK.Wwise.State ImpactLevel;

    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
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

    void Start()
    {
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
        // Determine camera angle to car
        Vector3 cameraVector = transform.position - freeLookCamera.transform.position;
        cameraVector.y = 0;
        Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
        cameraAngle = Vector3.Angle(carDirection, cameraVector) * Math.Sign(Vector3.Dot(cameraVector, transform.right));

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
        }

        // Send input to server
        ClientInput input = new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = move,
            brakeInput = brake,
            handbrake = handbrakeOn,
            steeringAngle = steering,
        };

        SendInputServerRpc(input);
    }

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        if (!canMove) return;
        ApplyMotorTorque(input.moveInput, input.brakeInput);
        ApplySteering(input.steeringAngle);
        isDrifting.Value = localVelocityX > .25f ? true : false;
        if (input.handbrake)
        {
            Handbrake();
            isDrifting.Value = true;
        }

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

    private void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.deltaTime * 3f); // Adjust 100f to control how fast it reaches maxTorque

        float motorTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;

        frontLeftWheelCollider.motorTorque = motorTorque;
        frontRightWheelCollider.motorTorque = motorTorque;
        rearLeftWheelCollider.motorTorque = motorTorque;
        rearRightWheelCollider.motorTorque = motorTorque;

    }

    private void ApplySteering(float cameraAngle)
    {
        if (cameraAngle < 1f && cameraAngle > -1f) cameraAngle = 0f;

        float frontSteeringAngle = Mathf.Clamp(cameraAngle, maxSteeringAngle * -1, maxSteeringAngle);
        // Use only 30% of steering angle for rear wheels
        // Calculate rear steering with speed dependency
        // float speedFactor = Mathf.Clamp01(1f - (rb.linearVelocity.magnitude / 10f));
        float rearSteeringAngle = frontSteeringAngle * -0.35f;

        frontLeftWheelCollider.steerAngle = Mathf.Lerp(frontLeftWheelCollider.steerAngle, frontSteeringAngle, steeringSpeed);
        frontRightWheelCollider.steerAngle = Mathf.Lerp(frontRightWheelCollider.steerAngle, frontSteeringAngle, steeringSpeed);
        rearLeftWheelCollider.steerAngle = Mathf.Lerp(rearLeftWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
        rearRightWheelCollider.steerAngle = Mathf.Lerp(rearRightWheelCollider.steerAngle, rearSteeringAngle, steeringSpeed);
    }


    private void AnimateWheels()
    {
        if (cameraAngle < 1f && cameraAngle > -1f) cameraAngle = 0f;
        float frontSteeringAngle = Mathf.Clamp(cameraAngle, maxSteeringAngle * -1, maxSteeringAngle);
        // Use only 30% of steering angle for rear wheels
        // Calculate rear steering with speed dependency
        float rearSteeringAngle = frontSteeringAngle * -0.5f;
        var steeringAngle = -1 * Math.Clamp(cameraAngle, 0, maxSteeringAngle);
        // Convert angle to Quaternion
        frontLeftWheelTransform.localRotation = Quaternion.Euler(0, frontSteeringAngle, 0);
        frontRightWheelTransform.localRotation = Quaternion.Euler(0, frontSteeringAngle, 0);
        rearLeftWheelTransform.localRotation = Quaternion.Euler(0, rearSteeringAngle, 0);
        rearRightWheelTransform.localRotation = Quaternion.Euler(0, rearSteeringAngle, 0);
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
            collisionForceOnCooldown = true;
            ulong player1Id = GetComponent<NetworkObject>().OwnerClientId;
            ulong player2Id = collision.gameObject.GetComponent<NetworkObject>().OwnerClientId;

            var relativeVelocity = collision.gameObject.GetComponent<Rigidbody>().linearVelocity;

            if (Vector3.Dot(relativeVelocity, rb.linearVelocity) < 0) relativeVelocity = -1 * relativeVelocity;

            rb.AddForce(collision.gameObject.GetComponent<Rigidbody>().linearVelocity * 300f, ForceMode.Impulse);
            Debug.Log(message:
            "Head-On Collision detected between " + ConnectionManager.instance.GetClientUsername(player1Id)
             + " and " + ConnectionManager.instance.GetClientUsername(player2Id)
            );
            StartCoroutine(CollisionForceDebounce());
        }
        PlayImpactAudio();
    }

    private void PlayImpactAudio()
    {
        switch (velocity)
        {
            case float v when v < 8f:
                Debug.Log("Low velocity");
                break;
            case float v when v >= 8f && v < 15f:
                Debug.Log("Medium velocity");
                break;
            case float v when v >= 15f:
                Debug.Log("High velocity");
                break;
            default:
                Debug.Log("Velocity out of range");
                break;
        }
        HogImpact.Post(gameObject);
    }

    private IEnumerator CollisionForceDebounce()
    {
        yield return new WaitForSeconds(.5f);
        collisionForceOnCooldown = false;
    }

    public void DriftCarPS()
    {
        if (isDrifting.Value)
        {
            RLWParticleSystem.Play();
            RRWParticleSystem.Play();
            RLWTireSkid.emitting = true;
            RRWTireSkid.emitting = true;
        }
        else if (!isDrifting.Value)
        {
            RLWParticleSystem.Stop();
            RRWParticleSystem.Stop();
            RLWTireSkid.emitting = false;
            RRWTireSkid.emitting = false;
        }
    }


    [ClientRpc]
    public void ExplodeCarClientRpc()
    {
        Instantiate(Explosion, transform.position + centerOfMass, transform.rotation, transform); // Explosion Particles
        CarExplosion.Post(gameObject); // Wwise audio event
        canMove = false;
    }
}
