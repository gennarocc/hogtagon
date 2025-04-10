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
            transform.position = _target.position;
            _lastTargetPosition = _target.position;
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
    
    private void UpdateCameraPosition(float deltaTime)
    {
        if (_target == null) return;
        
        // Calculate target velocity for prediction
        if (predictTargetMovement)
        {
            _targetVelocity = (_target.position - _lastTargetPosition) / deltaTime;
            _lastTargetPosition = _target.position;
        }
        
        // Calculate target position with prediction
        Vector3 targetPosition = _target.position;
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

    public void SetTarget(Transform target)
    {
        _target = target;
        
        // Reset tracking when target changes
        if (_target != null)
        {
            _lastTargetPosition = _target.position;
            _targetVelocity = Vector3.zero;
            _currentVelocity = Vector3.zero;
        }
    }
}