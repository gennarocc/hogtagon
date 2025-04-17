using UnityEngine;
using System;
using System.IO;
using Cinemachine;

public class ScreenshotManager : MonoBehaviour
{
    [Tooltip("Directory where screenshots will be saved")]
    [SerializeField] private string screenshotDirectory = @"C:\Users\Char\Pictures\Hogtagon\Action shots\UnityScreenshots";
    
    private CinemachineVirtualCamera activeCamera;
    
    private void Start()
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
    
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            CaptureScreenshot();
        }
    }
    
    public void CaptureScreenshot()
    {
        // Create a unique filename with timestamp
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        string filename = Path.Combine(screenshotDirectory, $"HogtagonScreenshot_{timestamp}.png");
        
        // Capture the screenshot
        ScreenCapture.CaptureScreenshot(filename);
        
        Debug.Log($"Screenshot captured and saved to: {filename}");
    }
} 