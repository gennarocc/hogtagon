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
    [SerializeField, Range(0.1f, 1f)] private float steeringSpeed = .8f;
    [SerializeField] public float additionalCollisionForce = 1000f; // Customizable variable for additional force
    [SerializeField] private float decelerationMultiplier = 0.95f;

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

    [Header("Wwise")]
    [SerializeField] public AK.Wwise.Event EngineOn;
    [SerializeField] public AK.Wwise.Event EngineOff;
    [SerializeField] public AK.Wwise.Event CarExplosion;
    [SerializeField] public AK.Wwise.RTPC rpm;

    private NetworkVariable<bool> isDrifting = new NetworkVariable<bool>(false);
    private float currentTorque;
    private float localVelocityX;
    private bool collisionForceOnCooldown = false;

    void Start()
    {
        rb.centerOfMass = centerOfMass;
        if (IsOwner) EngineOn.Post(gameObject);
    }

    private void Update()
    {
        // Save the local velocity of the car in the x axis. Used to know if the car is drifting.
        localVelocityX = transform.InverseTransformDirection(rb.linearVelocity).x;

        if (IsClient && IsOwner)
        {
            ClientMove();
        }

        UpdateWheelPositions();
        DriftCarPS();
    }

    private void ClientMove()
    {
        // Gather client input
        float move = 0;
        if (Input.GetKey(KeyCode.W)) move = 1f;
        if (Input.GetKey(KeyCode.S)) move = -1f;
        float brake = 0f;
        if (Input.GetKey(KeyCode.Space)) brake = 1f;
        float steering = freeLookCamera.m_XAxis.Value;

        ClientInput input = new ClientInput
        {
            clientId = OwnerClientId,
            moveInput = move,
            brakeInput = brake,
            steeringAngle = steering,
        };

        SendClientInputServerRpc(input);
    }

    [ServerRpc]
    private void SendClientInputServerRpc(ClientInput input)
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

    public void ExplodeCarFX()
    {
        CarExplosion.Post(gameObject); // Audio Event;
    }

    private struct ClientInput : INetworkSerializable
    {
        public ulong clientId;
        public float moveInput;
        public float brakeInput;
        public float steeringAngle;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref moveInput);
            serializer.SerializeValue(ref brakeInput);
            serializer.SerializeValue(ref steeringAngle);
        }
    }
}