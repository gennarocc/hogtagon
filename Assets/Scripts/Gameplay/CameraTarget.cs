using UnityEngine;
using Unity.Netcode;

public class CameraTarget : MonoBehaviour
{
    [Header("Follow Settings")]
    [Tooltip("How fast the camera approaches the target position (lower = smoother)")]
    [Range(0.5f, 20f)]
    [SerializeField] private float smoothSpeed = 5f;
    
    [Tooltip("Offset from the target's center (local space)")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 2f);
    
    [Tooltip("Whether to use FixedUpdate instead of Update")]
    [SerializeField] private bool useFixedUpdate = false;
    
    [Header("Stability Settings")]
    [Tooltip("Ignore very small movements below this threshold")]
    [Range(0.001f, 0.1f)]
    [SerializeField] private float movementThreshold = 0.002f;
    
    [Tooltip("Weight for current frame position (lower = smoother but more lag)")]
    [Range(0.01f, 0.2f)]
    [SerializeField] private float positionWeight = 0.12f;
    
    [Header("Network Settings")]
    [Tooltip("Additional responsiveness for non-host clients")]
    [Range(1f, 3f)]
    [SerializeField] private float clientResponseMultiplier = 1.5f;
    
    private Transform _target;
    private Vector3 _smoothedPosition;
    private bool _isInitialized = false;
    private bool _isLocalPlayer = false;
    
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
        
        // Determine if we need more responsiveness for non-host
        float actualPositionWeight = positionWeight;
        float actualSmoothSpeed = smoothSpeed;
        
        // If we're a client but not the host, be more responsive
        if (_isLocalPlayer && !IsHost())
        {
            actualPositionWeight *= clientResponseMultiplier;
            actualSmoothSpeed *= clientResponseMultiplier;
        }
        
        // Use weighted average for smooth movement
        _smoothedPosition = Vector3.Lerp(_smoothedPosition, targetPosition, actualPositionWeight);
        
        // Only move if we've moved significantly or if we're a non-host client
        float distanceToTarget = Vector3.Distance(transform.position, _smoothedPosition);
        if (distanceToTarget > movementThreshold || (_isLocalPlayer && !IsHost()))
        {
            // Update position using exponential smoothing
            transform.position = Vector3.Lerp(
                transform.position,
                _smoothedPosition,
                actualSmoothSpeed * (useFixedUpdate ? Time.fixedDeltaTime : Time.deltaTime)
            );
        }
    }
    
    private Vector3 CalculateTargetPosition()
    {
        if (_target == null) return transform.position;
        
        // Apply the offset in local space
        return _target.TransformPoint(targetOffset);
    }
    
    private bool IsHost()
    {
        // Check if NetworkManager exists and if we're the host
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }
    
    public void SetTarget(Transform target)
    {
        bool wasNull = _target == null;
        _target = target;
        
        // Check if this is the local player's transform
        if (_target != null)
        {
            Player playerComponent = _target.GetComponent<Player>();
            if (playerComponent != null)
            {
                _isLocalPlayer = playerComponent.IsOwner;
            }
        }
        
        if (_target != null && wasNull)
        {
            // Immediately jump to position on first assignment
            Vector3 targetPos = CalculateTargetPosition();
            transform.position = targetPos;
            _smoothedPosition = targetPos;
            _isInitialized = true;
        }
    }
}