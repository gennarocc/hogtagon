using UnityEngine;

public class CameraTarget : MonoBehaviour
{
    [Header("Follow Settings")]
    [Tooltip("How fast the camera target moves to the player position")]
    [SerializeField] private float positionSmoothSpeed = 10f;
    
    [Tooltip("Distance to maintain from target to reduce micro-jitters")]
    [SerializeField] private float dampingDistance = 0.01f;
    
    [Tooltip("Whether to smooth position using fixed update for more consistent results")]
    [SerializeField] private bool useFixedUpdate = true;
    
    [Header("Position Offset")]
    [Tooltip("Offset from the target's center (local space)")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 2f);
    
    [Tooltip("Whether to apply the offset in local or world space")]
    [SerializeField] private bool useLocalOffset = true;
    
    [Header("Advanced Settings")]
    [Tooltip("Apply prediction to target movement to reduce lag")]
    [SerializeField] private bool predictTargetMovement = true;
    
    [Tooltip("Strength of position prediction (higher = more responsive but can overshoot)")]
    [Range(0f, 1f)]
    [SerializeField] private float predictionStrength = 0.2f;
    
    private Transform _target;
    private Vector3 _lastTargetPosition;
    private Vector3 _targetVelocity;
    private Vector3 _currentVelocity; // For SmoothDamp
    
    private void Start()
    {
        if (_target != null)
        {
            UpdateTargetPosition();
            _lastTargetPosition = GetOffsetTargetPosition();
        }
    }

    private void Update()
    {
        if (!useFixedUpdate)
        {
            UpdateCameraPosition(Time.deltaTime);
        }
    }
    
    private void FixedUpdate()
    {
        if (useFixedUpdate)
        {
            UpdateCameraPosition(Time.fixedDeltaTime);
        }
    }
    
    private Vector3 GetOffsetTargetPosition()
    {
        if (_target == null) return transform.position;
        
        if (useLocalOffset)
        {
            // Apply offset in the target's local space
            return _target.TransformPoint(targetOffset);
        }
        else
        {
            // Apply offset in world space
            return _target.position + targetOffset;
        }
    }
    
    private void UpdateCameraPosition(float deltaTime)
    {
        if (_target == null) return;
        
        Vector3 offsetTargetPos = GetOffsetTargetPosition();
        
        // Calculate target velocity for prediction
        if (predictTargetMovement)
        {
            _targetVelocity = (offsetTargetPos - _lastTargetPosition) / deltaTime;
            _lastTargetPosition = offsetTargetPos;
        }
        
        // Calculate target position with prediction
        Vector3 targetPosition = offsetTargetPos;
        if (predictTargetMovement)
        {
            targetPosition += _targetVelocity * predictionStrength;
        }
        
        // Only move if we're beyond the damping distance
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
        if (distanceToTarget > dampingDistance)
        {
            // Use SmoothDamp for more natural movement
            transform.position = Vector3.SmoothDamp(
                transform.position, 
                targetPosition, 
                ref _currentVelocity, 
                1f / positionSmoothSpeed, 
                Mathf.Infinity, 
                deltaTime
            );
        }
    }
    
    // Helper method to immediately update target position (useful for teleporting)
    private void UpdateTargetPosition()
    {
        if (_target != null)
        {
            transform.position = GetOffsetTargetPosition();
        }
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        
        // Reset tracking when target changes
        if (_target != null)
        {
            UpdateTargetPosition(); // Immediately update position
            _lastTargetPosition = GetOffsetTargetPosition();
            _targetVelocity = Vector3.zero;
            _currentVelocity = Vector3.zero;
        }
    }
}