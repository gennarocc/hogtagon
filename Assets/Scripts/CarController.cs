/*
MESSAGE FROM CREATOR: This script was coded by Mena. You can use it in your games either these are commercial or
personal projects. You can even add or remove functions as you wish. However, you cannot sell copies of this
script by itself, since it is originally distributed as a free product.
I wish you the best for your project. Good luck!

P.S: If you need more cars, you can check my other vehicle assets on the Unity Asset Store, perhaps you could find
something useful for your game. Best regards, Mena.
*/

using System;
using Unity.Netcode;
using Cinemachine;
using UnityEngine;

public class CarController : NetworkBehaviour
{

  [Header("Camera")]
  [SerializeField] public CinemachineFreeLook mainCamera;
  [SerializeField] public float cameraAngle;

  [Header("Car Params")]
  [Range(20, 190)]
  [SerializeField] public int maxSpeed = 90; //The maximum speed that the car can reach in km/h.
  [Range(10, 120)]
  [SerializeField] public int maxReverseSpeed = 45; //The maximum speed that the car can reach while going on reverse in km/h.
  [Range(1, 10)]
  [SerializeField] public int accelerationMultiplier = 2; // How fast the car can accelerate. 1 is a slow acceleration and 10 is the fastest.
  [Range(10, 90)]
  [SerializeField] public int maxSteeringAngle = 27; // The maximum angle that the tires can reach while rotating the steering wheel.
  [Range(0.1f, 1f)]
  [SerializeField] public float steeringSpeed = 0.5f; // How fast the steering wheel turns.
  [Range(100, 600)]
  [SerializeField] public int brakeForce = 350; // The strength of the wheel brakes.
  [Range(1, 10)]
  [SerializeField] public int decelerationMultiplier = 2; // How fast the car decelerates when the user is not using the throttle.
  [Range(1, 10)]
  [SerializeField] public int handbrakeDriftMultiplier = 5; // How much grip the car loses when the user hit the handbrake.
  public Vector3 bodyMassCenter; // This is a vector that contains the center of mass of the car. I recommend to set this value
                                 // in the points x = 0 and z = 0 of your car. You can select the value that you want in the y axis,
                                 // however, you must notice that the higher this value is, the more unstable the car becomes.
                                 // Usually the y value goes from 0 to 1.5.

  //WHEELS

  [Header("Wheel References")]
  [SerializeField] public GameObject frontLeftMesh;
  [SerializeField] public WheelCollider frontLeftCollider;
  [Space(10)]
  [SerializeField] public GameObject frontRightMesh;
  [SerializeField] public WheelCollider frontRightCollider;
  [Space(10)]
  [SerializeField] public GameObject rearLeftMesh;
  [SerializeField] public WheelCollider rearLeftCollider;
  [Space(10)]
  [SerializeField] public GameObject rearRightMesh;
  [SerializeField] public WheelCollider rearRightCollider;

  [HideInInspector]
  public float carSpeed; // Used to store the speed of the car.
  [HideInInspector]
  public bool isDrifting; // Used to know whether the car is drifting or not.
  [HideInInspector]
  public bool isTractionLocked; // Used to know whether the traction of the car is locked or not.

  Rigidbody carRigidbody; // Stores the car's rigidbody.
  [SerializeField] public float steeringAxis; // Used to know whether the steering wheel has reached the maximum value. It goes from -1 to 1.
  float throttleAxis; // Used to know whether the throttle has reached the maximum value. It goes from -1 to 1.
  float driftingAxis;
  float localVelocityZ;
  float localVelocityX;
  bool deceleratingCar;

  /*
  The following variables are used to store information about sideways friction of the wheels (such as
  extremumSlip,extremumValue, asymptoteSlip, asymptoteValue and stiffness). We change this values to
  make the car to start drifting.
  */

  WheelFrictionCurve FLwheelFriction;
  float FLWextremumSlip;
  WheelFrictionCurve FRwheelFriction;
  float FRWextremumSlip;
  WheelFrictionCurve RLwheelFriction;
  float RLWextremumSlip;
  WheelFrictionCurve RRwheelFriction;
  float RRWextremumSlip;

  void Start()
  {
    Cursor.visible = !Cursor.visible; // toggle visibility
    Cursor.lockState = CursorLockMode.Locked;
    carRigidbody = gameObject.GetComponent<Rigidbody>();
    carRigidbody.centerOfMass = bodyMassCenter;
    carRigidbody.isKinematic = !IsServer;
    GameManager.instance.PrintPlayers();
  }

  void Update()
  {
    if (!IsOwner) return;
    // We determine the speed of the car.
    carSpeed = (2 * Mathf.PI * frontLeftCollider.radius * frontLeftCollider.rpm * 60) / 1000;
    // Save the local velocity of the car in the x axis. Used to know if the car is drifting.
    localVelocityX = transform.InverseTransformDirection(carRigidbody.linearVelocity).x;
    // Save the local velocity of the car in the z axis. Used to know if the car is going forward or backwards.
    localVelocityZ = transform.InverseTransformDirection(carRigidbody.linearVelocity).z;
    SendPlayerMoveData();
  }

  // Gather all data needed for movement then send to server.
  public void SendPlayerMoveData()
  {
    // Calculate camera angle in relation to vehical - This will be use to steer.
    Vector3 cameraVector = transform.position - mainCamera.State.FinalPosition;
    cameraVector.y = 0; // We only care about the horizontal axis.
    Vector3 carDirection = new Vector3(transform.forward.x, 0, transform.forward.z);
    cameraAngle = Vector3.Angle(carDirection, cameraVector);
    // Use dot product to determine if camera is looking left or right. 
    float cameraOrientation = Vector3.Dot(cameraVector, transform.right);

    var moveData = new MoveData()
    {
      id = OwnerClientId,
      throttle = Input.GetKey(KeyCode.W),
      breakReverse = Input.GetKey(KeyCode.S),
      cameraAngle = cameraAngle,
      cameraOrientation = cameraOrientation
    };
    MovePlayerServerRPC(moveData);
  }

  [ServerRpc]
  private void MovePlayerServerRPC(MoveData moveData)
  {
    if (moveData.throttle)
    {
      CancelInvoke("DecelerateCar");
      deceleratingCar = false;
      GoForward();
    }
    if (moveData.breakReverse)
    {
      CancelInvoke("DecelerateCar");
      deceleratingCar = false;
      GoReverse();
    }

    // TODO - Re-enable
    // if (Input.GetKey(KeyCode.Space))
    // {
    //   CancelInvoke("DecelerateCar");
    //   deceleratingCar = false;
    //   Handbrake();
    // }
    // if (Input.GetKeyUp(KeyCode.Space))
    // {
    //   RecoverTraction();
    // }

    if (!moveData.breakReverse && !moveData.throttle)
    {
      ThrottleOff();
    }
    if (!moveData.breakReverse && !moveData.throttle && !deceleratingCar)
    {
      InvokeRepeating("DecelerateCar", 0f, 0.1f);
      deceleratingCar = true;
    }
    // We call the method AnimateWheelMeshes() in order to match the wheel collider movements with the 3D meshes of the wheels.
    TurnsWheelsClientRpc(moveData.cameraAngle, moveData.cameraOrientation);
    AnimateWheelMeshes();
  }

  [ClientRpc]
  private void TurnsWheelsClientRpc(float cameraAngle, float cameraOrientation)
  {
    // if (!IsOwner) return; // TODO Maybe remove if other client wheels aren't updating.
    if (cameraOrientation < 0)
    {
      var steeringAngle = -1 * Math.Clamp(cameraAngle, 0, maxSteeringAngle);
      frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steeringAngle, steeringSpeed);
      frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steeringAngle, steeringSpeed);
    }
    if (cameraOrientation > 0)
    {
      var steeringAngle = Math.Clamp(cameraAngle, 0, maxSteeringAngle);
      frontLeftCollider.steerAngle = Mathf.Lerp(frontLeftCollider.steerAngle, steeringAngle, steeringSpeed);
      frontRightCollider.steerAngle = Mathf.Lerp(frontRightCollider.steerAngle, steeringAngle, steeringSpeed);
    }
    
  }

  private void AnimateWheelMeshes()
  {
    try
    {
      Quaternion FLWRotation;
      Vector3 FLWPosition;
      frontLeftCollider.GetWorldPose(out FLWPosition, out FLWRotation);
      frontLeftMesh.transform.position = FLWPosition;
      frontLeftMesh.transform.rotation = FLWRotation;

      Quaternion FRWRotation;
      Vector3 FRWPosition;
      frontRightCollider.GetWorldPose(out FRWPosition, out FRWRotation);
      frontRightMesh.transform.position = FRWPosition;
      frontRightMesh.transform.rotation = FRWRotation;

      Quaternion RLWRotation;
      Vector3 RLWPosition;
      rearLeftCollider.GetWorldPose(out RLWPosition, out RLWRotation);
      rearLeftMesh.transform.position = RLWPosition;
      rearLeftMesh.transform.rotation = RLWRotation;

      Quaternion RRWRotation;
      Vector3 RRWPosition;
      rearRightCollider.GetWorldPose(out RRWPosition, out RRWRotation);
      rearRightMesh.transform.position = RRWPosition;
      rearRightMesh.transform.rotation = RRWRotation;
    }
    catch (Exception ex)
    {
      Debug.LogWarning(ex);
    }
  }

  //
  //ENGINE AND BRAKING METHODS
  //

  // This method apply positive torque to the wheels in order to go forward.
  public void GoForward()
  {
    //If the forces aplied to the rigidbody in the 'x' asis are greater than
    //3f, it means that the car is losing traction, then the car will start emitting particle systems.
    if (Mathf.Abs(localVelocityX) > 4.5f)
    {
      isDrifting = true;
    }
    else
    {
      isDrifting = false;
    }
    // The following part sets the throttle power to 1 smoothly.
    throttleAxis = throttleAxis + (Time.deltaTime * 3f);
    if (throttleAxis > 1f)
    {
      throttleAxis = 1f;
    }
    //If the car is going backwards, then apply brakes in order to avoid strange
    //behaviours. If the local velocity in the 'z' axis is less than -1f, then it
    //is safe to apply positive torque to go forward.
    if (localVelocityZ < -1f)
    {
      Brakes();
    }
    else
    {
      if (Mathf.RoundToInt(carSpeed) < maxSpeed)
      {
        //Apply positive torque in all wheels to go forward if maxSpeed has not been reached.
        frontLeftCollider.brakeTorque = 0;
        frontLeftCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        frontRightCollider.brakeTorque = 0;
        frontRightCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        rearLeftCollider.brakeTorque = 0;
        rearLeftCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        rearRightCollider.brakeTorque = 0;
        rearRightCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
      }
      else
      {
        // If the maxSpeed has been reached, then stop applying torque to the wheels.
        // IMPORTANT: The maxSpeed variable should be considered as an approximation; the speed of the car
        // could be a bit higher than expected.
        frontLeftCollider.motorTorque = 0;
        frontRightCollider.motorTorque = 0;
        rearLeftCollider.motorTorque = 0;
        rearRightCollider.motorTorque = 0;
      }
    }
  }

  // This method apply negative torque to the wheels in order to go backwards.
  public void GoReverse()
  {
    //If the forces aplied to the rigidbody in the 'x' asis are greater than
    //3f, it means that the car is losing traction, then the car will start emitting particle systems.
    if (Mathf.Abs(localVelocityX) > 4.5f)
    {
      isDrifting = true;
    }
    else
    {
      isDrifting = false;
    }
    // The following part sets the throttle power to -1 smoothly.
    throttleAxis = throttleAxis - (Time.deltaTime * 3f);
    if (throttleAxis < -1f)
    {
      throttleAxis = -1f;
    }
    //If the car is still going forward, then apply brakes in order to avoid strange
    //behaviours. If the local velocity in the 'z' axis is greater than 1f, then it
    //is safe to apply negative torque to go reverse.
    if (localVelocityZ > 1f)
    {
      Brakes();
    }
    else
    {
      if (Mathf.Abs(Mathf.RoundToInt(carSpeed)) < maxReverseSpeed)
      {
        //Apply negative torque in all wheels to go in reverse if maxReverseSpeed has not been reached.
        frontLeftCollider.brakeTorque = 0;
        frontLeftCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        frontRightCollider.brakeTorque = 0;
        frontRightCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        rearLeftCollider.brakeTorque = 0;
        rearLeftCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
        rearRightCollider.brakeTorque = 0;
        rearRightCollider.motorTorque = (accelerationMultiplier * 50f) * throttleAxis;
      }
      else
      {
        //If the maxReverseSpeed has been reached, then stop applying torque to the wheels.
        // IMPORTANT: The maxReverseSpeed variable should be considered as an approximation; the speed of the car
        // could be a bit higher than expected.
        frontLeftCollider.motorTorque = 0;
        frontRightCollider.motorTorque = 0;
        rearLeftCollider.motorTorque = 0;
        rearRightCollider.motorTorque = 0;
      }
    }
  }

  //The following function set the motor torque to 0 (in case the user is not pressing either W or S).
  public void ThrottleOff()
  {
    frontLeftCollider.motorTorque = 0;
    frontRightCollider.motorTorque = 0;
    rearLeftCollider.motorTorque = 0;
    rearRightCollider.motorTorque = 0;
  }

  // The following method decelerates the speed of the car according to the decelerationMultiplier variable, where
  // 1 is the slowest and 10 is the fastest deceleration. This method is called by the function InvokeRepeating,
  // usually every 0.1f when the user is not pressing W (throttle), S (reverse) or Space bar (handbrake).
  public void DecelerateCar()
  {
    if (Mathf.Abs(localVelocityX) > 2.5f)
    {
      isDrifting = true;
    }
    else
    {
      isDrifting = false;
    }
    // The following part resets the throttle power to 0 smoothly.
    if (throttleAxis != 0f)
    {
      if (throttleAxis > 0f)
      {
        throttleAxis = throttleAxis - (Time.deltaTime * 10f);
      }
      else if (throttleAxis < 0f)
      {
        throttleAxis = throttleAxis + (Time.deltaTime * 10f);
      }
      if (Mathf.Abs(throttleAxis) < 0.15f)
      {
        throttleAxis = 0f;
      }
    }
    carRigidbody.linearVelocity = carRigidbody.linearVelocity * (1f / (1f + (0.025f * decelerationMultiplier)));
    // Since we want to decelerate the car, we are going to remove the torque from the wheels of the car.
    frontLeftCollider.motorTorque = 0;
    frontRightCollider.motorTorque = 0;
    rearLeftCollider.motorTorque = 0;
    rearRightCollider.motorTorque = 0;
    // If the magnitude of the car's velocity is less than 0.25f (very slow velocity), then stop the car completely and
    // also cancel the invoke of this method.
    if (carRigidbody.linearVelocity.magnitude < 0.25f)
    {
      carRigidbody.linearVelocity = Vector3.zero;
      CancelInvoke("DecelerateCar");
    }
  }

  // This function applies brake torque to the wheels according to the brake force given by the user.
  public void Brakes()
  {
    frontLeftCollider.brakeTorque = brakeForce;
    frontRightCollider.brakeTorque = brakeForce;
    rearLeftCollider.brakeTorque = brakeForce;
    rearRightCollider.brakeTorque = brakeForce;
  }

  // This function is used to make the car lose traction. By using this, the car will start drifting. The amount of traction lost
  // will depend on the handbrakeDriftMultiplier variable. If this value is small, then the car will not drift too much, but if
  // it is high, then you could make the car to feel like going on ice.
  public void Handbrake()
  {
    CancelInvoke("RecoverTraction");
    // We are going to start losing traction smoothly, there is were our 'driftingAxis' variable takes
    // place. This variable will start from 0 and will reach a top value of 1, which means that the maximum
    // drifting value has been reached. It will increase smoothly by using the variable Time.deltaTime.
    driftingAxis = driftingAxis + (Time.deltaTime);
    float secureStartingPoint = driftingAxis * FLWextremumSlip * handbrakeDriftMultiplier;

    if (secureStartingPoint < FLWextremumSlip)
    {
      driftingAxis = FLWextremumSlip / (FLWextremumSlip * handbrakeDriftMultiplier);
    }
    if (driftingAxis > 1f)
    {
      driftingAxis = 1f;
    }
    //If the forces aplied to the rigidbody in the 'x' asis are greater than
    //3f, it means that the car lost its traction, then the car will start emitting particle systems.
    if (Mathf.Abs(localVelocityX) > 2.5f)
    {
      isDrifting = true;
    }
    else
    {
      isDrifting = false;
    }
    //If the 'driftingAxis' value is not 1f, it means that the wheels have not reach their maximum drifting
    //value, so, we are going to continue increasing the sideways friction of the wheels until driftingAxis
    // = 1f.
    if (driftingAxis < 1f)
    {
      FLwheelFriction.extremumSlip = FLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      frontLeftCollider.sidewaysFriction = FLwheelFriction;

      FRwheelFriction.extremumSlip = FRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      frontRightCollider.sidewaysFriction = FRwheelFriction;

      RLwheelFriction.extremumSlip = RLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      rearLeftCollider.sidewaysFriction = RLwheelFriction;

      RRwheelFriction.extremumSlip = RRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      rearRightCollider.sidewaysFriction = RRwheelFriction;
    }

    // Whenever the player uses the handbrake, it means that the wheels are locked, so we set 'isTractionLocked = true'
    // and, as a consequense, the car starts to emit trails to simulate the wheel skids.
    isTractionLocked = true;
  }

  // This function is used to emit both the particle systems of the tires' smoke and the trail renderers of the tire skids
  // depending on the value of the bool variables 'isDrifting' and 'isTractionLocked'.
  // This function is used to recover the traction of the car when the user has stopped using the car's handbrake.
  public void RecoverTraction()
  {
    isTractionLocked = false;
    driftingAxis = driftingAxis - (Time.deltaTime / 1.5f);
    if (driftingAxis < 0f)
    {
      driftingAxis = 0f;
    }

    //If the 'driftingAxis' value is not 0f, it means that the wheels have not recovered their traction.
    //We are going to continue decreasing the sideways friction of the wheels until we reach the initial
    // car's grip.
    if (FLwheelFriction.extremumSlip > FLWextremumSlip)
    {
      FLwheelFriction.extremumSlip = FLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      frontLeftCollider.sidewaysFriction = FLwheelFriction;

      FRwheelFriction.extremumSlip = FRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      frontRightCollider.sidewaysFriction = FRwheelFriction;

      RLwheelFriction.extremumSlip = RLWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      rearLeftCollider.sidewaysFriction = RLwheelFriction;

      RRwheelFriction.extremumSlip = RRWextremumSlip * handbrakeDriftMultiplier * driftingAxis;
      rearRightCollider.sidewaysFriction = RRwheelFriction;

      Invoke("RecoverTraction", Time.deltaTime);

    }
    else if (FLwheelFriction.extremumSlip < FLWextremumSlip)
    {
      FLwheelFriction.extremumSlip = FLWextremumSlip;
      frontLeftCollider.sidewaysFriction = FLwheelFriction;

      FRwheelFriction.extremumSlip = FRWextremumSlip;
      frontRightCollider.sidewaysFriction = FRwheelFriction;

      RLwheelFriction.extremumSlip = RLWextremumSlip;
      rearLeftCollider.sidewaysFriction = RLwheelFriction;

      RRwheelFriction.extremumSlip = RRWextremumSlip;
      rearRightCollider.sidewaysFriction = RRwheelFriction;

      driftingAxis = 0f;
    }
  }

  [ServerRpc]
  private void NotifyPlayerCollisionServerRPC(ulong netObjectID)
  {
    Debug.Log(message: "Player Collision - Server");
    NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netObjectID, out NetworkObject networkObject);
    networkObject.gameObject.GetComponent<Rigidbody>().AddForce(carRigidbody.linearVelocity * carRigidbody.mass);
  }

  [ClientRpc]
  private void NotifyPlayerCollisionClientRPC(ulong netObjectID)
  {
    Debug.Log(message: "Player Collision - Client");
  }

  // private void OnCollisionEnter(Collision col)
  // {
  //   if (!col.gameObject.CompareTag("Player")) return;
  //   var collisionData = new MoveData()
  //   {
  //     id = OwnerClientId,
  //     throttle = Input.GetKey(KeyCode.W),
  //     breakReverse = Input.GetKey(KeyCode.S),
  //     cameraAngle = cameraAngle,
  //     cameraOrientation = cameraOrientation;
  //   };
  //   if (!IsOwner) return;

  //   if (IsServer) NotifyPlayerCollisionServerRPC(col.gameObject.GetComponent<NetworkObject>().NetworkObjectId);
  //   if (!IsServer) NotifyPlayerCollisionClientRPC(col.gameObject.GetComponent<NetworkObject>().NetworkObjectId);
  // }

  struct MoveData : INetworkSerializable
  {
    public ulong id;
    public bool throttle;
    public bool breakReverse;
    public float cameraAngle;
    public float cameraOrientation;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
      serializer.SerializeValue(ref id);
      serializer.SerializeValue(ref throttle);
      serializer.SerializeValue(ref breakReverse);
      serializer.SerializeValue(ref cameraAngle);
      serializer.SerializeValue(ref cameraOrientation);
    }
  }
}
