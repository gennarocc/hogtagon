using UnityEngine;
using Cinemachine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    [SerializeField] private List<CinemachineFreeLook> cameras = new List<CinemachineFreeLook>();
    
    [Header("Camera Settings")]
    [SerializeField] private int activePriority = 15;
    [SerializeField] private int inactivePriority = 0;
    
    private int currentCameraIndex = 0;
    private DefaultControls controls;
    
    private void Awake()
    {
        controls = new DefaultControls();
        
        // Initialize all cameras to inactive priority
        foreach (var camera in cameras)
        {
            if (camera != null)
            {
                camera.Priority = inactivePriority;
            }
        }
        
        // Activate the first camera if available
        if (cameras.Count > 0 && cameras[0] != null)
        {
            cameras[0].Priority = activePriority;
        }
    }
    
    private void OnEnable()
    {
        controls.Gameplay.SwapCam.performed += OnSwitchCamera;
        controls.Gameplay.Enable();
    }
    
    private void OnDisable()
    {
        controls.Gameplay.SwapCam.performed -= OnSwitchCamera;
        controls.Gameplay.Disable();
    }
    
    private void OnSwitchCamera(InputAction.CallbackContext context)
    {
        if (cameras.Count == 0) return;
        
        // Set current camera to inactive
        if (cameras[currentCameraIndex] != null)
        {
            cameras[currentCameraIndex].Priority = inactivePriority;
        }
        
        // Move to next camera
        currentCameraIndex = (currentCameraIndex + 1) % cameras.Count;
        
        // Set new camera to active
        if (cameras[currentCameraIndex] != null)
        {
            cameras[currentCameraIndex].Priority = activePriority;
            Debug.Log($"Switched to camera {currentCameraIndex}");
        }
    }
    
    // Helper method to add cameras at runtime if needed
    public void AddCamera(CinemachineFreeLook camera)
    {
        if (camera != null)
        {
            camera.Priority = inactivePriority;
            cameras.Add(camera);
        }
    }
} 