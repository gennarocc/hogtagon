using UnityEngine;

public class CameraTarget : MonoBehaviour
{
    [Header("Follow Settings")]
    [Tooltip("How fast the camera target moves to the player position (lower = smoother but more lag)")]
    [SerializeField] private float positionSmoothSpeed = 5f; // Reduced from 10f for smoother follow
    
    [Tooltip("Distance to maintain from target to reduce micro-jitters")]
    [SerializeField] private float dampingDistance = 0.02f; // Increased to ignore more tiny movements
    
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
    [SerializeField] private float predictionStrength = 0.1f; // Reduced from 0.2f for less jitter
    
    [Tooltip("Smoothing factor for velocity calculations (higher = smoother velocity)")]
    [Range(0f, 0.95f)]
    [SerializeField] private float velocitySmoothingFactor = 0.8f;
    
    [Header("Stability Settings")]
    [Tooltip("Use a low-pass filter on position to reduce high-frequency jitter")]
    [SerializeField] private bool useLowPassFilter = true;
    
    [Tooltip("The strength of the low-pass filter (higher = smoother but more lag)")]
    [Range(0f, 0.98f)]
    [SerializeField] private float lowPassFilterStrength = 0.85f;
    
    private Transform _target;
    private Vector3 _lastTargetPosition;
    private Vector3 _targetVelocity;
    private Vector3 _smoothedVelocity; // For velocity smoothing
    private Vector3 _currentVelocity; // For SmoothDamp
    private Vector3 _filteredPosition; // For low-pass filtering
    private bool _positionInitialized = false;
    
    private void Start()
    {
        if (_target != null)
        {
            Vector3 offsetPos = GetOffsetTargetPosition();
            transform.position = offsetPos;
            _lastTargetPosition = offsetPos;
            _filteredPosition = offsetPos;
            _positionInitialized = true;
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
        
        // Calculate target velocity with smoothing
        if (predictTargetMovement)
        {
            // Calculate raw velocity
            Vector3 rawVelocity = (offsetTargetPos - _lastTargetPosition) / deltaTime;
            
            // Apply exponential smoothing to velocity
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, rawVelocity, 1 - velocitySmoothingFactor);
            
            // Update last position
            _lastTargetPosition = offsetTargetPos;
            
            // Use smoothed velocity for prediction
            _targetVelocity = _smoothedVelocity;
        }
        
        // Calculate target position with prediction
        Vector3 targetPosition = offsetTargetPos;
        if (predictTargetMovement)
        {
            targetPosition += _targetVelocity * predictionStrength;
        }
        
        // Apply low-pass filter to reduce high-frequency jitter
        if (useLowPassFilter)
        {
            if (!_positionInitialized)
            {
                _filteredPosition = targetPosition;
                _positionInitialized = true;
            }
            else
            {
                _filteredPosition = Vector3.Lerp(_filteredPosition, targetPosition, 1 - lowPassFilterStrength);
            }
            targetPosition = _filteredPosition;
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
            Vector3 offsetPos = GetOffsetTargetPosition();
            transform.position = offsetPos;
            _filteredPosition = offsetPos;
        }
    }

    public void SetTarget(Transform target)
    {
        _target = target;
        
        // Reset tracking when target changes
        if (_target != null)
        {
            UpdateTargetPosition(); // Immediately update position
            Vector3 offsetPos = GetOffsetTargetPosition();
            _lastTargetPosition = offsetPos;
            _targetVelocity = Vector3.zero;
            _smoothedVelocity = Vector3.zero;
            _currentVelocity = Vector3.zero;
            _filteredPosition = offsetPos;
            _positionInitialized = true;
        }
    }
}