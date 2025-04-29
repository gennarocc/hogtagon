using UnityEngine;
using Cinemachine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    [System.Serializable]
    public class CameraPreset
    {
        public string name;
        public CinemachineVirtualCamera camera;
        public KeyCode hotkey = KeyCode.None;
        [Tooltip("Priority when this camera is active")]
        public int activePriority = 15;
        [Tooltip("Priority when this camera is inactive")]
        public int inactivePriority = 0;
    }

    [Header("Camera Presets")]
    [SerializeField] private List<CameraPreset> cameraPresets = new List<CameraPreset>();
    
    [Header("Screenshot Settings")]
    [Tooltip("Subdirectory name for screenshots within the persistent data path")]
    [SerializeField] private string screenshotSubdirectory = "Screenshots";
    
    private string screenshotDirectory;
    private DefaultControls controls;
    private CameraPreset currentActivePreset;

    private void Awake()
    {
        controls = new DefaultControls();
        
        // Set all cameras to inactive priority initially
        foreach (var preset in cameraPresets)
        {
            if (preset.camera != null)
            {
                preset.camera.Priority = preset.inactivePriority;
            }
        }

        // Set up screenshot directory
        screenshotDirectory = System.IO.Path.Combine(Application.persistentDataPath, screenshotSubdirectory);
        try
        {
            if (!System.IO.Directory.Exists(screenshotDirectory))
            {
                System.IO.Directory.CreateDirectory(screenshotDirectory);
                Debug.Log($"Created screenshot directory: {screenshotDirectory}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize screenshot directory: {e.Message}");
        }
    }

    private void OnEnable()
    {
        controls.Gameplay.Enable();
        controls.Gameplay.Screenshot.performed += OnScreenshotPerformed;
    }

    private void OnDisable()
    {
        controls.Gameplay.Screenshot.performed -= OnScreenshotPerformed;
        controls.Gameplay.Disable();
    }

    private void Update()
    {
        // Check for camera hotkeys
        foreach (var preset in cameraPresets)
        {
            if (preset.hotkey != KeyCode.None && Input.GetKeyDown(preset.hotkey))
            {
                SwitchToCamera(preset);
            }
        }
    }

    public void SwitchToCamera(CameraPreset newPreset)
    {
        // Deactivate current camera
        if (currentActivePreset != null && currentActivePreset.camera != null)
        {
            currentActivePreset.camera.Priority = currentActivePreset.inactivePriority;
        }

        // Activate new camera
        if (newPreset != null && newPreset.camera != null)
        {
            newPreset.camera.Priority = newPreset.activePriority;
            currentActivePreset = newPreset;
            Debug.Log($"Switched to camera: {newPreset.name}");
        }
    }

    private void OnScreenshotPerformed(InputAction.CallbackContext context)
    {
        CaptureScreenshot();
    }

    private void CaptureScreenshot()
    {
        try
        {
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            string cameraName = currentActivePreset != null ? currentActivePreset.name : "default";
            string filename = System.IO.Path.Combine(screenshotDirectory, $"HogtagonScreenshot_{cameraName}_{timestamp}.png");
            
            ScreenCapture.CaptureScreenshot(filename);
            Debug.Log($"Screenshot captured from camera '{cameraName}' and saved to: {filename}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to capture screenshot: {e.Message}");
        }
    }

    // Helper method to add cameras at runtime (useful for debugging/testing)
    public void AddCamera(string name, CinemachineVirtualCamera camera, KeyCode hotkey)
    {
        var preset = new CameraPreset
        {
            name = name,
            camera = camera,
            hotkey = hotkey
        };
        cameraPresets.Add(preset);
        Debug.Log($"Added camera preset: {name} with hotkey: {hotkey}");
    }
} 