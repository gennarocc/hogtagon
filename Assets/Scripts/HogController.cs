using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Components;
using Cinemachine;

public class ServerAuthoritativeVehicleController : NetworkBehaviour
{
    [SerializeField] private float maxTorque = 500f; // Maximum torque applied to wheels
    [SerializeField] private float brakeTorque = 800f; // Brake torque applied to wheels
    [SerializeField, Range(0f, 100f)] private float maxSteeringAngle = 60f; // Default maximum steering angle
    [SerializeField] private float interpolationFactor = 0.1f; // Adjust for smoother interpolation
    [SerializeField] private WheelCollider[] wheelColliders; // Array to hold the wheel colliders
    [SerializeField] private Transform[] wheelTransforms; // Array to hold the wheel transforms
    [SerializeField] private Rigidbody carRigidbody; // Reference to the car's Rigidbody
    [SerializeField] private CinemachineFreeLook freeLookCamera; // Reference to the CinemachineFreeLook camera
    [SerializeField] public float cameraAngle;
    private NetworkTransform networkTransform; // Reference to the NetworkTransform component
    private Vector3 serverPosition;
    private Quaternion serverRotation;
    private Vector3 serverScale;

    void Start()
    {
        networkTransform = GetComponent<NetworkTransform>(); // Ensure NetworkTransform is attached
    }

    void Update()
    {
        if (IsOwner)
        {
            HandleClientInput();
        }
        else
        {
            InterpolateToServerState();
        }
        UpdateWheelPositions(); // Ensure wheel positions are updated on both client and server
    }

    void HandleClientInput()
    {
        // Gather client input
        float move = Input.GetAxis("Vertical");
        float brake = Input.GetAxis("Jump"); // Assuming the Jump button is used for braking
        float steering = freeLookCamera.m_XAxis.Value;
        cameraAngle = freeLookCamera.m_XAxis.Value;

        // Send input data to server for processing
        SendInputToServerServerRpc(move, brake, steering);
    }

    [ServerRpc]
    void SendInputToServerServerRpc(float moveInput, float brakeInput, float cameraAngle)
    {
        ApplyMotorTorque(moveInput, brakeInput);
        ApplySteering(cameraAngle);

        // Update server position and rotation
        serverPosition = transform.position;
        serverRotation = transform.rotation;
        serverScale = transform.localScale;

        // Sync the state with clients through the NetworkTransform
        networkTransform.SetState(serverPosition, serverRotation, serverScale);
    }

    void ApplyMotorTorque(float moveInput, float brakeInput)
    {
        float motorTorque = Mathf.Clamp(moveInput, -1f, 1f) * maxTorque;
        float appliedBrakeTorque = brakeInput > 0 ? brakeTorque : 0;

        foreach (WheelCollider wheelCollider in wheelColliders)
        {
            wheelCollider.motorTorque = motorTorque;
            wheelCollider.brakeTorque = appliedBrakeTorque;
        }
    }

    void ApplySteering(float cameraAngle)
    {
        float steeringInput = Mathf.Clamp(cameraAngle, -1f, 1f);
        float steeringAngle = steeringInput * maxSteeringAngle;

        // Apply steering only to front wheels
        foreach (WheelCollider wheelCollider in wheelColliders)
        {
            if (wheelCollider.steerAngle != 0)
            {
                wheelCollider.steerAngle = steeringAngle;
            }
        }
    }

    void InterpolateToServerState()
    {
        // Smoothly interpolate between current position and server position
        transform.position = Vector3.Lerp(transform.position, serverPosition, interpolationFactor);
        transform.rotation = Quaternion.Lerp(transform.rotation, serverRotation, interpolationFactor);
    }

    void UpdateWheelPositions()
    {
        // Update wheel transform positions to match colliders
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            Vector3 pos;
            Quaternion rot;
            wheelColliders[i].GetWorldPose(out pos, out rot);
            wheelTransforms[i].position = pos;
            wheelTransforms[i].rotation = rot;
            
            wheelTransforms[i].localRotation = Quaternion.Euler(wheelTransforms[i].localRotation.eulerAngles + new Vector3(wheelColliders[i].rpm / 60 * 360 * Time.deltaTime, 0, 0));
        }
    }
}
