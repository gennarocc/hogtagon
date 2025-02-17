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
    [SerializeField] public float additionalCollisionForce = 1000f; // Customizable variable for additional force
    [SerializeField] private float decelerationMultiplier = 0.95f;
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
    private InputBuffer inputBuffer = new InputBuffer();

    void Start()
    {
        rb.centerOfMass = centerOfMass;
        EngineOn.Post(gameObject);
    }

    private void Update()
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

        UpdateWheelPositions();
        DriftCarPS();

        if (IsServer && inputBuffer.HasInput())
        {
            ClientInput bufferedInput = inputBuffer.GetNextInput();
            ProcessInput(bufferedInput);
        }
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
            brake = 1f;
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
            steeringAngle = steering,
        };

        SendInputServerRpc(input);
    }

    [ServerRpc]
    private void SendInputServerRpc(ClientInput input)
    {
        inputBuffer.AddInput(input);
    }

    private void ProcessInput(ClientInput input)
    {
        if (!canMove) return;
        ApplyMotorTorque(input.moveInput, input.brakeInput);
        ApplySteering(input.steeringAngle);
        isDrifting.Value = localVelocityX > .25f ? true : false;
    }

    private void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        float targetTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        currentTorque = Mathf.MoveTowards(currentTorque, targetTorque, Time.deltaTime * 3f); // Adjust 100f to control how fast it reaches maxTorque

        float motorTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        float appliedBrakeTorque = brakeInput > 0 ? brakeTorque : 0;

        frontLeftWheelCollider.motorTorque = motorTorque;
        frontRightWheelCollider.motorTorque = motorTorque;
        rearLeftWheelCollider.motorTorque = motorTorque;
        rearRightWheelCollider.motorTorque = motorTorque;

        frontLeftWheelCollider.brakeTorque = appliedBrakeTorque;
        frontRightWheelCollider.brakeTorque = appliedBrakeTorque;
        rearLeftWheelCollider.brakeTorque = appliedBrakeTorque;
        rearRightWheelCollider.brakeTorque = appliedBrakeTorque;

        // Decelerate the car on no input
        if (moveInput == 0 && brakeInput == 0)
        {
            // rb.linearVelocity = rb.linearVelocity * (1f / (1f + (0.025f * decelerationMultiplier)));
            // If the magnitude of the car's velocity is less than 0.25f (very slow velocity), then stop the car completely and
            // if (rb.linearVelocity.magnitude < 0.25f)
            // {
            //     rb.linearVelocity = Vector3.zero;
            // }
        }
    }

    private void ApplySteering(float cameraAngle)
    {
        float steeringAngle = Mathf.Clamp(cameraAngle, maxSteeringAngle * -1, maxSteeringAngle);
        frontLeftWheelCollider.steerAngle = Mathf.Lerp(frontLeftWheelCollider.steerAngle, steeringAngle, steeringSpeed);
        frontRightWheelCollider.steerAngle = Mathf.Lerp(frontRightWheelCollider.steerAngle, steeringAngle, steeringSpeed);
        rearLeftWheelCollider.steerAngle = Mathf.Lerp(rearLeftWheelCollider.steerAngle, -1 * steeringAngle, steeringSpeed);
        rearRightWheelCollider.steerAngle = Mathf.Lerp(rearRightWheelCollider.steerAngle, -1 * steeringAngle, steeringSpeed);
    }

    private void UpdateWheelPositions()
    {
        // Update wheel transform positions and rotations to match colliders
        UpdateWheelTransform(frontLeftWheelCollider, frontLeftWheelTransform);
        UpdateWheelTransform(frontRightWheelCollider, frontRightWheelTransform);
        UpdateWheelTransform(rearLeftWheelCollider, rearLeftWheelTransform);
        UpdateWheelTransform(rearRightWheelCollider, rearRightWheelTransform);
    }

    private void UpdateWheelTransform(WheelCollider collider, Transform transform)
    {
        Vector3 pos;
        Quaternion rot;
        collider.GetWorldPose(out pos, out rot);

        transform.position = pos;
        transform.rotation = rot;

        transform.localRotation = Quaternion.Euler(transform.localRotation.eulerAngles + new Vector3(collider.rpm / 60 * 360 * Time.deltaTime, 0, 0));
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
                // ImpactLevel.SetValue("low");
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
