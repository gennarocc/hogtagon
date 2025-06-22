using UnityEngine;
using Cinemachine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Setup guide and utility script for configuring the Cinematic Trailer system
/// Provides setup methods and configuration validation
/// </summary>
public class CinematicSetupGuide : MonoBehaviour
{
    [Header("Setup Instructions")]
    [TextArea(5, 10)]
    [SerializeField] private string instructions = 
        "CINEMATIC TRAILER SETUP GUIDE:\n\n" +
        "1. Assign your car target (the transform to follow)\n" +
        "2. Assign your normal player camera (FreeLook)\n" +
        "3. Assign each cinematic virtual camera\n" +
        "4. Set up Post Process Volume with Depth of Field and Motion Blur\n" +
        "5. Configure trigger and timing settings\n" +
        "6. Test with manual trigger (C key) or auto jump detection\n\n" +
        "Camera Names Expected:\n" +
        "- CinematicCamera_JumpZoom\n" +
        "- CinematicCamera_OrbitRotation\n" +
        "- CinematicCamera_UnderCarRotation\n" +
        "- CinematicCamera_DynamicChase\n" +
        "- CinematicCamera_SlowMotionChase";

    [Header("Component References")]
    [SerializeField] private CinematicTrailerController trailerController;
    [SerializeField] private CinematicCameraEffects cameraEffects;

#if UNITY_EDITOR
    [Header("Auto Setup")]
    [SerializeField] private bool autoFindCameras = true;
    [SerializeField] private bool autoFindCarTarget = true;
    [SerializeField] private bool autoSetupPostProcessing = true;

    // Public methods that can be called from custom inspector or context menu
    [ContextMenu("Auto Setup All Components")]
    public void AutoSetupAll()
    {
        AutoFindComponents();
        AutoAssignCameras();
        ValidateSetup();
    }

    [ContextMenu("Find Components Automatically")]
    public void AutoFindComponents()
    {
        // Find TrailerController
        if (trailerController == null)
        {
            trailerController = FindObjectOfType<CinematicTrailerController>();
            if (trailerController == null)
            {
                Debug.LogWarning("CinematicTrailerController not found. Please add it to a GameObject in the scene.");
            }
        }

        // Find CameraEffects
        if (cameraEffects == null)
        {
            cameraEffects = FindObjectOfType<CinematicCameraEffects>();
            if (cameraEffects == null)
            {
                Debug.LogWarning("CinematicCameraEffects not found. Please add it to a GameObject in the scene.");
            }
        }

        // Find car target if enabled
        if (autoFindCarTarget && trailerController != null)
        {
            // Look for common car-related objects
            GameObject carObject = GameObject.FindWithTag("Player");
            if (carObject == null)
            {
                carObject = FindObjectOfType<Rigidbody>()?.gameObject;
            }
            
            if (carObject != null)
            {
                Debug.Log($"Auto-assigned car target: {carObject.name}");
            }
        }
    }

    [ContextMenu("Auto Assign Cameras")]
    public void AutoAssignCameras()
    {
        if (trailerController == null)
        {
            Debug.LogError("TrailerController not assigned!");
            return;
        }

        // Find cameras by name
        var allCameras = FindObjectsOfType<CinemachineVirtualCamera>();
        
        foreach (var camera in allCameras)
        {
            string cameraName = camera.name.ToLower();
            
            if (cameraName.Contains("jumpzoom"))
            {
                Debug.Log($"Found Jump Zoom camera: {camera.name}");
            }
            else if (cameraName.Contains("orbitrotation"))
            {
                Debug.Log($"Found Orbit Rotation camera: {camera.name}");
            }
            else if (cameraName.Contains("undercar"))
            {
                Debug.Log($"Found Under Car camera: {camera.name}");
            }
            else if (cameraName.Contains("dynamicchase"))
            {
                Debug.Log($"Found Dynamic Chase camera: {camera.name}");
            }
            else if (cameraName.Contains("slowmotion"))
            {
                Debug.Log($"Found Slow Motion camera: {camera.name}");
            }
        }

        // Find normal player camera
        var freeLookCameras = FindObjectsOfType<CinemachineFreeLook>();
        if (freeLookCameras.Length > 0)
        {
            Debug.Log($"Found player camera: {freeLookCameras[0].name}");
        }
    }

    [ContextMenu("Validate Setup")]
    public void ValidateSetup()
    {
        bool isValid = true;
        System.Text.StringBuilder issues = new System.Text.StringBuilder();
        issues.AppendLine("SETUP VALIDATION:");

        // Check TrailerController
        if (trailerController == null)
        {
            isValid = false;
            issues.AppendLine("❌ CinematicTrailerController not assigned");
        }
        else
        {
            issues.AppendLine("✅ CinematicTrailerController found");
        }

        // Check CameraEffects
        if (cameraEffects == null)
        {
            isValid = false;
            issues.AppendLine("❌ CinematicCameraEffects not assigned");
        }
        else
        {
            issues.AppendLine("✅ CinematicCameraEffects found");
        }

        // Check for required cameras
        var virtualCameras = FindObjectsOfType<CinemachineVirtualCamera>();
        string[] requiredCameras = { "jumpzoom", "orbitrotation", "undercar", "dynamicchase", "slowmotion" };
        
        foreach (string requiredCamera in requiredCameras)
        {
            bool found = false;
            foreach (var camera in virtualCameras)
            {
                if (camera.name.ToLower().Contains(requiredCamera))
                {
                    found = true;
                    break;
                }
            }
            
            if (found)
            {
                issues.AppendLine($"✅ {requiredCamera} camera found");
            }
            else
            {
                isValid = false;
                issues.AppendLine($"❌ {requiredCamera} camera missing");
            }
        }

        // Check for FreeLook camera
        var freeLookCameras = FindObjectsOfType<CinemachineFreeLook>();
        if (freeLookCameras.Length > 0)
        {
            issues.AppendLine("✅ FreeLook camera found");
        }
        else
        {
            isValid = false;
            issues.AppendLine("❌ FreeLook player camera not found");
        }

        // Check for Post Processing Volume
        var postProcessVolumes = FindObjectsOfType<UnityEngine.Rendering.Volume>();
        if (postProcessVolumes.Length > 0)
        {
            issues.AppendLine("✅ Post Process Volume found");
        }
        else
        {
            issues.AppendLine("⚠️ Post Process Volume not found (optional)");
        }

        if (isValid)
        {
            issues.AppendLine("\n🎉 Setup is complete and ready!");
        }
        else
        {
            issues.AppendLine("\n⚠️ Setup incomplete. Please address the issues above.");
        }

        Debug.Log(issues.ToString());
    }

    [ContextMenu("Create Missing Components")]
    public void CreateMissingComponents()
    {
        // Create TrailerController if missing
        if (trailerController == null)
        {
            GameObject controllerGO = new GameObject("CinematicTrailerController");
            trailerController = controllerGO.AddComponent<CinematicTrailerController>();
            Debug.Log("Created CinematicTrailerController");
        }

        // Create CameraEffects if missing
        if (cameraEffects == null)
        {
            GameObject effectsGO = trailerController.gameObject;
            cameraEffects = effectsGO.AddComponent<CinematicCameraEffects>();
            Debug.Log("Created CinematicCameraEffects");
        }
    }

    [ContextMenu("Test Cinematic Sequence")]
    public void TestCinematicSequence()
    {
        if (trailerController != null)
        {
            if (Application.isPlaying)
            {
                trailerController.StartCinematicSequence();
                Debug.Log("Started cinematic sequence test");
            }
            else
            {
                Debug.LogWarning("Enter Play mode to test the cinematic sequence");
            }
        }
        else
        {
            Debug.LogError("TrailerController not assigned");
        }
    }
#endif

    private void Start()
    {
        // Auto-initialize if components are assigned
        if (trailerController != null && cameraEffects != null)
        {
            // Get car target from trailer controller
            var carTarget = GetCarTargetFromController();
            if (carTarget != null)
            {
                cameraEffects.Initialize(carTarget);
                Debug.Log("Cinematic system auto-initialized");
            }
        }
    }

    private Transform GetCarTargetFromController()
    {
        if (trailerController == null) return null;
        
        // Use reflection to get car target (since it might be private)
        var field = trailerController.GetType().GetField("carTarget", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        return field?.GetValue(trailerController) as Transform;
    }
} 