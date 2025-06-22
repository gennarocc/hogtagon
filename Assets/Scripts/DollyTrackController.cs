using UnityEngine;
using Cinemachine;

public class DollyTrackController : MonoBehaviour
{
    public CinemachineVirtualCamera virtualCamera;
    public float speed = 0.5f; // Units per second
    public float delay = 3f; // Same delay as your cars
    public bool autoDisableAfterSequence = true;
    public float sequenceDuration = 10f; // How long the sequence should run for

    private CinemachineTrackedDolly dolly;
    private float startTime;
    private bool startedMoving = false;
    private bool sequenceFinished = false;
    private float originalPathPosition = 0f;
    private int originalPriority = 0;
    
    void Start()
    {
        startTime = Time.time;
        
        // Make sure we have the virtual camera
        if (virtualCamera == null)
        {
            Debug.LogError("No Virtual Camera assigned to DollyTrackController!");
            enabled = false;
            return;
        }
        
        // Get the dolly track component
        dolly = virtualCamera.GetCinemachineComponent<CinemachineTrackedDolly>();
        if (dolly == null)
        {
            Debug.LogError("No Dolly Track component found on the Virtual Camera!");
            enabled = false;
            return;
        }
        
        // Save original values
        originalPathPosition = dolly.m_PathPosition;
        originalPriority = virtualCamera.Priority;
        
        // Set high priority to ensure this camera is active
        virtualCamera.Priority = 100;
    }
    
    void Update()
    {
        if (sequenceFinished)
            return;
            
        // Start moving after delay
        if (!startedMoving && Time.time - startTime >= delay)
        {
            startedMoving = true;
            Debug.Log("Camera started moving along dolly track");
        }
        
        // Move the camera along the track
        if (startedMoving && dolly != null)
        {
            dolly.m_PathPosition += speed * Time.deltaTime;
            
            // Check if sequence should auto-end
            if (autoDisableAfterSequence && Time.time - startTime >= delay + sequenceDuration)
            {
                StopSequence();
            }
        }
    }
    
    // Call this to manually stop the sequence
    public void StopSequence()
    {
        if (sequenceFinished)
            return;
            
        sequenceFinished = true;
        Debug.Log("Camera dolly sequence completed");
        
        // Option 1: Just stop updating but keep final position
        // enabled = false;
        
        // Option 2: Reset to original state
        ResetToOriginalState();
    }
    
    // Reset camera to original state
    public void ResetToOriginalState()
    {
        if (dolly != null)
        {
            dolly.m_PathPosition = originalPathPosition;
        }
        
        if (virtualCamera != null)
        {
            virtualCamera.Priority = originalPriority;
        }
    }
    
    // Make sure we clean up properly when destroyed
    void OnDisable()
    {
        if (!sequenceFinished && dolly != null)
        {
            ResetToOriginalState();
        }
    }
} 