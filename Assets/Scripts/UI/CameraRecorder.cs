using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
// Unity Recorder API
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif

/// <summary>
/// Records footage from the active camera for a specified duration.
/// Requires Unity Recorder package to be installed.
/// </summary>
public class CameraRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    [SerializeField] private float recordingDuration = 10.0f; // Duration in seconds
    [SerializeField] private bool startRecordingAutomatically = false;
    [SerializeField] private string outputFolder = "Recordings";
    [SerializeField] private string filenamePrefix = "TrailerFootage";
    
    [Header("Video Settings")]
    [SerializeField] private int frameRate = 60;
    [SerializeField] private int width = 1920;
    [SerializeField] private int height = 1080;
    
    [Header("Recording State")]
    [SerializeField] private bool isRecording = false;

#if UNITY_EDITOR
    private float recordingStartTime;
    private string actualOutputFolder;
    private object recorderController; // Using object to avoid direct type reference
    
    private void Start()
    {
        // Setup the full output path using project data path
        actualOutputFolder = GetFullOutputPath();
        
        // Log the actual output directory for debugging
        Debug.Log($"Recording will be saved to: {actualOutputFolder}");
        
        if (startRecordingAutomatically)
        {
            StartRecording();
        }
    }
    
    private void Update()
    {
        if (isRecording)
        {
            // Check if recording duration has elapsed
            if (Time.time - recordingStartTime >= recordingDuration)
            {
                StopRecording();
            }
        }
    }
    
    /// <summary>
    /// Starts recording footage from the active camera
    /// </summary>
    public void StartRecording()
    {
        if (isRecording)
        {
            Debug.LogWarning("Already recording");
            return;
        }
        
        // Ensure the output directory exists
        EnsureDirectoryExists();
        
        // Start the recorder using Unity menu commands
        try
        {
            // We'll use EditorWindow.ExecuteMenuItem which directly executes the menu command
            // just like you would click in the editor
            Debug.Log("Starting recording via Recorder window...");
            
            // First we need to open the recorder window
            EditorApplication.ExecuteMenuItem("Window/General/Recorder/Recorder Window");
            
            // Set up recording via the window interface
            // Note: This will use whatever settings are currently in the Recorder Window
            UnityEditor.EditorApplication.delayCall += () => {
                // This will be executed in the next editor frame
                EditorApplication.ExecuteMenuItem("Recorder/Start Recording");
                Debug.Log("Recording started via Recorder window");
                
                // Update state
                isRecording = true;
                recordingStartTime = Time.time;
            };
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error starting recording: {e.Message}");
        }
    }
    
    /// <summary>
    /// Stops the current recording
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording)
        {
            Debug.LogWarning("Not currently recording");
            return;
        }
        
        try
        {
            // Stop recording using menu command
            EditorApplication.ExecuteMenuItem("Recorder/Stop Recording");
            
            // Update state
            isRecording = false;
            
            Debug.Log($"Recording stopped. Check your Recorder window output location.");
            
            // Try to open the folder for convenience
            try
            {
                EditorUtility.RevealInFinder(actualOutputFolder);
            }
            catch (System.Exception)
            {
                // Silently fail if we can't open the folder
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error stopping recording: {e.Message}");
        }
    }
    
    /// <summary>
    /// Gets the full path to the output directory
    /// </summary>
    private string GetFullOutputPath()
    {
        // If the path starts with "Assets/", it's a project-relative path
        if (outputFolder.StartsWith("Assets/"))
        {
            return Path.Combine(Directory.GetCurrentDirectory(), outputFolder);
        }
        
        // Otherwise create the path in the project data path
        return Path.Combine(Application.dataPath, outputFolder);
    }
    
    /// <summary>
    /// Ensures the output directory exists
    /// </summary>
    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(actualOutputFolder))
        {
            try
            {
                Directory.CreateDirectory(actualOutputFolder);
                Debug.Log($"Created output directory: {actualOutputFolder}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to create output directory: {e.Message}");
                // Fallback to desktop
                actualOutputFolder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "UnityRecordings");
                Directory.CreateDirectory(actualOutputFolder);
                Debug.Log($"Using fallback output directory: {actualOutputFolder}");
            }
        }
    }
    
    private void OnDestroy()
    {
        // Make sure recording is stopped when object is destroyed
        if (isRecording)
        {
            StopRecording();
        }
    }
#else
    // Stubs for non-editor builds
    public void StartRecording()
    {
        Debug.LogWarning("Camera recording is only available in the Unity Editor");
    }
    
    public void StopRecording()
    {
        Debug.LogWarning("Camera recording is only available in the Unity Editor");
    }
#endif
} 