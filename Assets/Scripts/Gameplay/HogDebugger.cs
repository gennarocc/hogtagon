using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using System.Text;
using System.Collections;

public class HogDebugger : NetworkBehaviour
{
    [Header("Debug Settings")]
    [SerializeField] private bool showDebugUI = true;
    [SerializeField] private bool showNetworkDebug = true;
    [SerializeField] private bool showPhysicsDebug = true;
    [SerializeField] private bool showInputDebug = true;
    [SerializeField] private bool logToConsole = false;
    [SerializeField] private float updateInterval = 0.5f; // How often to update stats (in seconds)
    
    [Header("Warning Thresholds")]
    [SerializeField] private float highPingThreshold = 100f; // ms - yellow warning
    [SerializeField] private float veryHighPingThreshold = 200f; // ms - red warning
    [SerializeField] private float highPositionErrorThreshold = 1.0f; // meters - yellow warning
    [SerializeField] private float veryHighPositionErrorThreshold = 3.0f; // meters - red warning
    [SerializeField] private float lowFpsThreshold = 30f; // yellow warning
    [SerializeField] private float veryLowFpsThreshold = 20f; // red warning

    [Header("UI References")]
    [SerializeField] private TMP_Text debugText;
    [SerializeField] private GameObject debugPanel;
    [SerializeField] private KeyCode toggleDebugKey = KeyCode.F3;
    
    // References
    private NetworkHogController hogController;
    private NetworkManager networkManager;
    private Rigidbody rb;
    private Player playerComponent;
    
    // Debug data
    private float currentRtt;
    private float minRtt = float.MaxValue;
    private float maxRtt = 0f;
    private float avgRtt = 0f;
    private int rttSamples = 0;
    private int packetLoss = 0;
    private float positionError = 0f;
    private float rotationError = 0f;
    private Vector3 serverPosition;
    private Quaternion serverRotation;
    private Vector3 currentVelocity;
    private float lastUpdateTime;
    private float frameTimeAvg;
    private float frameTimeMin = float.MaxValue;
    private float frameTimeMax = 0f;
    private int frameCount = 0;
    private float lastFrameTime;
    private Dictionary<string, float> stats = new Dictionary<string, float>();
    private bool initialized = false;
    
    private void Awake()
    {
        // Find required components
        hogController = GetComponentInParent<NetworkHogController>();
        if (hogController == null)
            hogController = GetComponent<NetworkHogController>();
            
        rb = hogController?.GetComponent<Rigidbody>();
        networkManager = NetworkManager.Singleton;
        playerComponent = hogController?.transform.GetComponent<Player>();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        InitializeDebugger();
    }
    
    private void InitializeDebugger()
    {
        // Only activate for the local player
        if (hogController == null || !hogController.IsOwner)
        {
            enabled = false;
            if (debugPanel != null)
                debugPanel.SetActive(false);
            return;
        }
        
        // Create UI if needed
        if (debugText == null && showDebugUI)
        {
            CreateDebugUI();
        }
        
        // Start the update coroutine
        StartCoroutine(UpdateNetworkStats());
        
        // Initial UI state
        if (debugPanel != null)
            debugPanel.SetActive(showDebugUI);
            
        initialized = true;
        
        Debug.Log("HogDebugger initialized for local player");
    }
    
    private void Update()
    {
        // Skip if not initialized or not the owner
        if (!initialized || hogController == null || !hogController.IsOwner)
            return;
            
        // Toggle debug display with key press
        if (Input.GetKeyDown(toggleDebugKey))
        {
            showDebugUI = !showDebugUI;
            if (debugPanel != null)
                debugPanel.SetActive(showDebugUI);
        }
        
        // Update frame time stats
        UpdateFrameTimeStats();
        
        // Update debug text if enabled
        if (showDebugUI && Time.time - lastUpdateTime > updateInterval)
        {
            lastUpdateTime = Time.time;
            UpdateDebugText();
        }
    }
    
    private void UpdateFrameTimeStats()
    {
        float deltaTime = Time.deltaTime;
        frameTimeAvg = (frameTimeAvg * frameCount + deltaTime) / (frameCount + 1);
        frameTimeMin = Mathf.Min(frameTimeMin, deltaTime);
        frameTimeMax = Mathf.Max(frameTimeMax, deltaTime);
        frameCount++;
        
        // Reset stats every 100 frames to be more responsive to changes
        if (frameCount > 100)
        {
            frameCount = 1;
            frameTimeAvg = deltaTime;
            frameTimeMin = deltaTime;
            frameTimeMax = deltaTime;
        }
    }
    
    private IEnumerator UpdateNetworkStats()
    {
        while (true)
        {
            // Only update when connected and we're the owner
            if (networkManager != null && networkManager.IsConnectedClient && 
                hogController != null && hogController.IsOwner)
            {
                // Get RTT (ping)
                if (networkManager.NetworkConfig?.NetworkTransport != null)
                {
                    currentRtt = networkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(0);
                    
                    // Update min/max/avg
                    if (currentRtt > 0) // Ignore zero values
                    {
                        minRtt = Mathf.Min(minRtt, currentRtt);
                        maxRtt = Mathf.Max(maxRtt, currentRtt);
                        avgRtt = (avgRtt * rttSamples + currentRtt) / (rttSamples + 1);
                        rttSamples++;
                    }
                }
                
                // Get position error if we're a client
                if (hogController != null && hogController.IsOwner && !hogController.IsServer)
                {
                    positionError = Vector3.Distance(rb.position, serverPosition);
                    rotationError = Quaternion.Angle(rb.rotation, serverRotation);
                }
            }
            
            // Wait before next update
            yield return new WaitForSeconds(updateInterval);
        }
    }
    
    private void UpdateDebugText()
    {
        if (debugText == null) return;
        
        StringBuilder sb = new StringBuilder();
        
        // Add player info
        sb.AppendLine("<b><size=22>Player Info</size></b>");
        sb.AppendLine($"Username: {(playerComponent != null ? playerComponent.nameplate.text : "Unknown")}");
        sb.AppendLine($"Client ID: {(playerComponent != null ? playerComponent.clientId : 0)}");
        
        // Color owner/server status
        string ownerStatus = hogController.IsOwner ? "<color=green>Yes</color>" : "<color=#888888>No</color>";
        string serverStatus = hogController.IsServer ? "<color=yellow>Yes</color>" : "<color=#888888>No</color>";
        sb.AppendLine($"Is Owner: {ownerStatus}, Is Server: {serverStatus}");
        
        // Color player state
        string stateText = GetPlayerState();
        string coloredState = stateText;
        
        if (stateText == "Alive")
            coloredState = $"<color=green>{stateText}</color>";
        else if (stateText == "Dead")
            coloredState = $"<color=red>{stateText}</color>";
        else if (stateText != "Unknown")
            coloredState = $"<color=yellow>{stateText}</color>";
            
        sb.AppendLine($"State: {coloredState}");
        sb.AppendLine();
        
        // Add network info if enabled
        if (showNetworkDebug)
        {
            sb.AppendLine("<b><size=22>Network Info</size></b>");
            
            // Color RTT based on thresholds
            string rttColor = GetColorForValue(currentRtt, highPingThreshold, veryHighPingThreshold);
            sb.AppendLine($"RTT (Ping): <color={rttColor}>{currentRtt:F0} ms</color>");
            
            // Color position error
            string posErrorColor = GetColorForValue(positionError, highPositionErrorThreshold, veryHighPositionErrorThreshold);
            sb.AppendLine($"Position Error: <color={posErrorColor}>{positionError:F2} m</color>");
            
            // Color rotation error
            string rotErrorColor = GetColorForValue(rotationError, 10f, 25f);
            sb.AppendLine($"Rotation Error: <color={rotErrorColor}>{rotationError:F1}°</color>");
            sb.AppendLine();
        }
        
        // Add physics info if enabled
        if (showPhysicsDebug && rb != null)
        {
            sb.AppendLine("<b><size=22>Physics Info</size></b>");
            
            // Color velocity based on speed
            float speed = rb.linearVelocity.magnitude;
            string speedColor = "white";
            if (speed > 20f) speedColor = "yellow";
            if (speed > 35f) speedColor = "red";
            
            sb.AppendLine($"Velocity: <color={speedColor}>{speed:F1} m/s</color>");
            sb.AppendLine($"Angular Vel: {rb.angularVelocity.magnitude:F1}°/s");
            sb.AppendLine($"Position: {rb.position.ToString("F1")}");
            sb.AppendLine();
        }
        
        // Add input debug if enabled
        if (showInputDebug)
        {
            sb.AppendLine("<b><size=22>Performance</size></b>");
            
            // Color FPS based on thresholds
            float currentFps = 1.0f / frameTimeAvg;
            string fpsColor = GetColorForValue(currentFps, lowFpsThreshold, veryLowFpsThreshold, true);
            sb.AppendLine($"FPS: <color={fpsColor}>{currentFps:F1}</color>");
            
            // Color frame time
            float frameTimeMs = frameTimeAvg * 1000;
            string frameTimeColor = GetColorForValue(frameTimeMs, 33f, 50f); // ~30fps and ~20fps
            sb.AppendLine($"Frame Time: <color={frameTimeColor}>{frameTimeMs:F1} ms</color>");
            
            // Add min/max frame times
            sb.AppendLine($"Min/Max: {frameTimeMin * 1000:F1}/{frameTimeMax * 1000:F1} ms");
        }
        
        // Update the text
        debugText.text = sb.ToString();
    }
    
    // Helper to get color based on value thresholds
    private string GetColorForValue(float value, float warningThreshold, float errorThreshold, bool invertComparison = false)
    {
        if (invertComparison)
        {
            // For values where lower is worse (like FPS)
            if (value < errorThreshold) return "red";
            if (value < warningThreshold) return "yellow";
            return "green";
        }
        else
        {
            // For values where higher is worse (like ping)
            if (value > errorThreshold) return "red";
            if (value > warningThreshold) return "yellow";
            return "green";
        }
    }
    
    private string GetPlayerState()
    {
        if (playerComponent == null) return "Unknown";
        
        ConnectionManager.Instance.TryGetPlayerData(playerComponent.clientId, out PlayerData playerData);
        return playerData.state.ToString();
    }
    
    private void CreateDebugUI()
    {
        // Create canvas if needed
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("DebugCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }
        
        // Create debug panel
        debugPanel = new GameObject("DebugPanel");
        debugPanel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = debugPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.5f); // Start lower on screen (0.5 instead of 0.7)
        panelRect.anchorMax = new Vector2(0.4f, 1); // Make it wider (0.4 instead of 0.3)
        panelRect.offsetMin = new Vector2(10, 10);
        panelRect.offsetMax = new Vector2(-10, -10);
        
        // Add background image
        UnityEngine.UI.Image panelImage = debugPanel.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = new Color(0, 0, 0, 0.7f);
        
        // Create text object
        GameObject textObj = new GameObject("DebugText");
        textObj.transform.SetParent(debugPanel.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
        
        debugText = textObj.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 20; // Much larger font size
        debugText.color = Color.white;
        debugText.alignment = TextAlignmentOptions.TopLeft;
        debugText.enableWordWrapping = true;
        debugText.margin = new Vector4(5, 5, 5, 5); // Add margin for better readability
        debugText.richText = true; // Enable rich text for colors
    }
    
    // Methods for external access to debug data
    public float GetCurrentRtt() => currentRtt;
    public float GetMinRtt() => minRtt;
    public float GetMaxRtt() => maxRtt;
    public float GetAvgRtt() => avgRtt;
    public float GetPositionError() => positionError;
    public float GetRotationError() => rotationError;
    public float GetCurrentFps() => 1.0f / frameTimeAvg;
}