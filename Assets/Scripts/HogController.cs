using UnityEngine;
using Unity.Netcode;
using Cinemachine;
using System.Collections.Generic;
using System;

public class HogController : NetworkBehaviour
{
    [Header("Hog Params")]
    [SerializeField] private float maxTorque = 500f; // Maximum torque applied to wheels
    [SerializeField] private float brakeTorque = 300f; // Brake torque applied to wheels
    [SerializeField] private Vector3 centerOfMass;
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f; // Default maximum steering angle
    [SerializeField] public float additionalCollisionForce = 1000f; // Customizable variable for additional force

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
    private CarState state;
    private Dictionary<float, Vector3> velocityBuffer;
    private float bufferDuration = 1f;
    private float bufferTimer = 0f;
    private Vector3 peakVelocity;
    float currentTorque;

    void Start()
    {
        rb.centerOfMass = centerOfMass;
        peakVelocity = Vector3.zero;
        velocityBuffer = new Dictionary<float, Vector3>();
        bufferTimer = 0f;
    }

    private void Update()
    {
        if (IsClient && IsOwner)
        {
            bufferTimer += Time.deltaTime;
            UpdateVelocityBuffer();
            CalculatePeakVelocity();

            ClientMove();
        }

        UpdateWheelPositions();
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

        // Move car localy.
        ApplyMotorTorque(move, brake);
        ApplySteering(steering);

        CarState state = new CarState
        {
            motorTorque = move * maxTorque,
            steeringAngle = steering,
            peakVelocity = peakVelocity
        };

        // Set new local state.
        this.state = state;
        // Send input data to server for processing.
        SendClientStateServerRpc(state);
    }

    [ServerRpc]
    private void SendClientStateServerRpc(CarState state)
    {
        this.state = state;
    }

    private CarState GetClientState()
    {
        return state;
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
    }

    private void ApplySteering(float cameraAngle)
    {
        float steeringAngle = Mathf.Clamp(cameraAngle, maxSteeringAngle * -1, maxSteeringAngle);

        // Apply steering only to front wheels
        frontLeftWheelCollider.steerAngle = steeringAngle;
        frontRightWheelCollider.steerAngle = steeringAngle;
        rearLeftWheelCollider.steerAngle = steeringAngle * -1;
        rearRightWheelCollider.steerAngle = steeringAngle * -1;
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

    private void UpdateVelocityBuffer()
    {
        // Get the current velocity
        Vector3 currentVelocity = rb.linearVelocity;

        // Add the current velocity to the buffer with the current time as the key
        velocityBuffer[bufferTimer] = currentVelocity;

        // Remove old velocities from the buffer
        List<float> keysToRemove = new List<float>();
        foreach (var entry in velocityBuffer)
        {
            if (bufferTimer - entry.Key > bufferDuration)
            {
                keysToRemove.Add(entry.Key);
            }
        }

        // Remove the old keys
        foreach (var key in keysToRemove)
        {
            velocityBuffer.Remove(key);
        }
    }

    private void CalculatePeakVelocity()
    {
        // Reset peak velocity
        Vector3 peakVelocity = Vector3.zero;

        // Calculate the peak velocity from the buffer
        foreach (var velocity in velocityBuffer.Values)
        {
            if (velocity.magnitude > peakVelocity.magnitude)
            {
                peakVelocity = velocity;
            }
        }

        this.peakVelocity = peakVelocity;
    }

    // Respawn Logic
    [ClientRpc]
    public void RespawnCarClientRpc(ulong clientId)
    {
        if (GetComponent<Player>().clientId == clientId)
        {
            Debug.Log("Respawning - " + ConnectionManager.instance.GetClientUsername(clientId));

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            var playerData = gameObject.GetComponent<Player>().playerData;
            transform.position = playerData.spawnPoint;
            transform.rotation = Quaternion.LookRotation(SpawnPointManager.instance.transform.position - playerData.spawnPoint);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        collision.gameObject.TryGetComponent<HogController>(out HogController collisionTarget);
        if (IsServer && collisionTarget != null)
        {
            // Get the NetworkRigidbody components
            Rigidbody rb1 = rb;
            Rigidbody rb2 = collision.gameObject.GetComponent<Rigidbody>();

            CarState collisionTargetState = collisionTarget.GetClientState();

            // Calculate the relative velocity
            Vector3 relativeVelocity = state.peakVelocity - collisionTargetState.peakVelocity;

            // Calculate the impulse
            Vector3 impulse1 = relativeVelocity;
            Vector3 impulse2 = -impulse1;

            ulong player1Id = GetComponent<NetworkObject>().OwnerClientId;
            ulong player2Id = collision.gameObject.GetComponent<NetworkObject>().OwnerClientId;

            Debug.Log(message:
                "Collision detected between " + ConnectionManager.instance.GetClientUsername(player1Id)
                 + " and " + ConnectionManager.instance.GetClientUsername(player2Id)
            );

            Debug.Log("Collision Relative Velocity - " + relativeVelocity);

            // Notify clients about the collision and applied force
            ApplyCollisionForceClientRpc(player1Id, impulse1);
            ApplyCollisionForceClientRpc(player2Id, impulse2);
        }
    }

    [ClientRpc]
    private void ApplyCollisionForceClientRpc(ulong clientId, Vector3 force)
    {
        if (IsOwner && clientId == GetComponent<NetworkObject>().OwnerClientId)
        {
            Debug.Log(message:
                "Applying collision force of " + force * additionalCollisionForce + " to " + ConnectionManager.instance.GetClientUsername(clientId)
            );
            var client = NetworkManager.Singleton.ConnectedClients[clientId];
            client.PlayerObject.GetComponent<Rigidbody>().AddForce(force * additionalCollisionForce, ForceMode.Impulse);
        }
    }

    private struct CarState : INetworkSerializable
    {
        public ulong clientId;
        public float motorTorque;
        public float steeringAngle;
        public Vector3 peakVelocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref motorTorque);
            serializer.SerializeValue(ref steeringAngle);
            serializer.SerializeValue(ref peakVelocity);
        }
    }
}
