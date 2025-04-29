using UnityEngine;
using System;
using System.IO;
using Cinemachine;
using UnityEngine.InputSystem;

public class ScreenshotManager : MonoBehaviour
{
    [Tooltip("Subdirectory name for screenshots within the persistent data path")]
    [SerializeField] private string screenshotSubdirectory = "Screenshots";
    
    private string screenshotDirectory;
    private CinemachineVirtualCamera activeCamera;

    // Reference to the input actions asset
    private DefaultControls controls;
    
    private void Awake()
    {
        controls = new DefaultControls();
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

    private void Start()
    {
        // Set up the screenshot directory in the persistent data path
        screenshotDirectory = Path.Combine(Application.persistentDataPath, screenshotSubdirectory);
        
        try
        {
            // Ensure the directory exists
            if (!Directory.Exists(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                Debug.Log($"Created screenshot directory: {screenshotDirectory}");
            }
            
            // Find Cinemachine Brain component (usually on the main camera)
            var brain = Camera.main.GetComponent<CinemachineBrain>();
            if (brain == null)
            {
                Debug.LogWarning("No CinemachineBrain found on Main Camera. Screenshots will use the default camera.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize screenshot directory: {e.Message}");
        }
    }

    private void OnScreenshotPerformed(InputAction.CallbackContext context)
    {
        CaptureScreenshot();
    }
    
    public void CaptureScreenshot()
    {
        try
        {
            // Create a unique filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            string filename = Path.Combine(screenshotDirectory, $"HogtagonScreenshot_{timestamp}.png");
            
            // Capture the screenshot
            ScreenCapture.CaptureScreenshot(filename);
            
            Debug.Log($"Screenshot captured and saved to: {filename}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to capture screenshot: {e.Message}");
        }
    }
} 