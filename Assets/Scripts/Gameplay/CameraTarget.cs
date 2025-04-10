using UnityEngine;

public class CameraTarget : MonoBehaviour
{
    [Header("Follow Settings")]
    [Tooltip("How fast the camera approaches the target position (lower = smoother)")]
    [Range(0.5f, 20f)]
    [SerializeField] private float smoothSpeed = 3f;
    
    [Tooltip("Offset from the target's center (local space)")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 2f);
    
    [Tooltip("Whether to use FixedUpdate instead of Update")]
    [SerializeField] private bool useFixedUpdate = true;
    
    [Header("Stability Settings")]
    [Tooltip("Ignore very small movements below this threshold")]
    [Range(0.001f, 0.1f)]
    [SerializeField] private float movementThreshold = 0.005f;
    
    [Tooltip("Weight for current frame position (lower = smoother but more lag)")]
    [Range(0.01f, 0.2f)]
    [SerializeField] private float positionWeight = 0.06f;
    
    private Transform _target;
    private Vector3 _smoothedPosition;
    private bool _isInitialized = false;
    
    private void Start()
    {
        if (_target != null)
        {
            Vector3 targetPos = CalculateTargetPosition();
            transform.position = targetPos;
            _smoothedPosition = targetPos;
            _isInitialized = true;
        }
    }
    
    private void Update()
    {
        if (!useFixedUpdate)
        {
            UpdatePosition();
        }
    }
    
    private void FixedUpdate()
    {
        if (useFixedUpdate)
        {
            UpdatePosition();
        }
    }
    
    private void UpdatePosition()
    {
        if (_target == null) return;
        
        // Calculate the desired position with offset
        Vector3 targetPosition = CalculateTargetPosition();
        
        // Initialize on first update if needed
        if (!_isInitialized)
        {
            _smoothedPosition = targetPosition;
            transform.position = targetPosition;
            _isInitialized = true;
            return;
        }
        
        // Use weighted average for ultra-smooth movement
        // This is more stable than Vector3.Lerp for this purpose
        _smoothedPosition = Vector3.Lerp(_smoothedPosition, targetPosition, positionWeight);
        
        // Only move if we've moved significantly
        float distanceToTarget = Vector3.Distance(transform.position, _smoothedPosition);
        if (distanceToTarget > movementThreshold)
        {
            // Update position using exponential smoothing
            transform.position = Vector3.Lerp(
                transform.position,
                _smoothedPosition,
                smoothSpeed * (useFixedUpdate ? Time.fixedDeltaTime : Time.deltaTime)
            );
        }
    }
    
    private Vector3 CalculateTargetPosition()
    {
        if (_target == null) return transform.position;
        
        // Apply the offset in local space
        return _target.TransformPoint(targetOffset);
    }
    
    public void SetTarget(Transform target)
    {
        bool wasNull = _target == null;
        _target = target;
        
        if (_target != null && wasNull)
        {
            // Only reset position on first assignment
            Vector3 targetPos = CalculateTargetPosition();
            transform.position = targetPos;
            _smoothedPosition = targetPos;
            _isInitialized = true;
        }
    }
}