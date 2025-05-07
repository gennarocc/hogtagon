using UnityEngine;

public class SpotlightIntensityChanger : MonoBehaviour
{
    [Tooltip("Starting intensity value")]
    public float startIntensity = 10f;
    
    [Tooltip("Final intensity value")]
    public float endIntensity = 10000f;
    
    [Tooltip("Duration of the intensity change in seconds")]
    public float duration = 5f;
    
    [Tooltip("Delay before starting the effect (seconds)")]
    public float startDelay = 0f;
    
    [Tooltip("Whether to play automatically on start")]
    public bool playOnStart = true;
    
    [Tooltip("Whether to loop the effect")]
    public bool loop = false;
    
    private Light spotLight;
    private float timer = 0f;
    private bool isPlaying = false;
    private float delayTimer = 0f;
    
    void Start()
    {
        spotLight = GetComponent<Light>();
        
        if (!spotLight)
        {
            Debug.LogError("SpotlightIntensityChanger requires a Light component!");
            enabled = false;
            return;
        }
        
        // Initialize with start intensity
        spotLight.intensity = startIntensity;
        
        if (playOnStart)
        {
            isPlaying = true;
        }
    }
    
    void Update()
    {
        if (!isPlaying)
            return;
            
        // Handle start delay
        if (delayTimer < startDelay)
        {
            delayTimer += Time.deltaTime;
            return;
        }
        
        // Update timer
        timer += Time.deltaTime;
        
        if (timer >= duration)
        {
            // Effect complete
            spotLight.intensity = endIntensity;
            
            if (loop)
            {
                // Reset for looping
                timer = 0f;
                spotLight.intensity = startIntensity;
            }
            else
            {
                isPlaying = false;
            }
            return;
        }
        
        // Calculate and set new intensity
        float t = timer / duration;
        spotLight.intensity = Mathf.Lerp(startIntensity, endIntensity, t);
    }
    
    /// <summary>
    /// Starts the intensity change effect
    /// </summary>
    public void Play()
    {
        timer = 0f;
        delayTimer = 0f;
        spotLight.intensity = startIntensity;
        isPlaying = true;
    }
    
    /// <summary>
    /// Stops the intensity change effect
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
    }
} 