using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TitleCRTEffect : MonoBehaviour
{
    [Header("CRT Effect Settings")]
    [SerializeField] private Color textColor = new Color(0.0f, 1.0f, 0.0f, 1.0f); // Green color for outline/glow
    [SerializeField] private Color vertexColor = new Color(0.0f, 0.0f, 0.0f, 1.0f); // Black color for the text itself
    [SerializeField] private float pulseRate = 0.5f; // Pulses per second
    [SerializeField] [Range(0f, 1f)] private float pulseSmoothing = 0.6f; // Higher values make the pulse smoother
    [SerializeField] private float glowIntensity = 0.5f;
    [SerializeField] private float scanlineSpeed = 0.1f;
    [SerializeField] private float outlineWidth = 0.2f;
    [SerializeField] private float maxOutlineWidth = 0.25f; // Maximum outline width for pulse
    [SerializeField] private float startupDelay = 0.5f;
    [SerializeField] private float fadeInDuration = 0.5f; // Time to fade in effects after delay
    
    private TextMeshProUGUI titleText;
    private Material material;
    private float time = 0f;
    private float startTime;
    private bool effectsStarted = false;
    private float effectTransition = 0f; // 0 to 1 transition after startup
    
    private void Awake()
    {
        titleText = GetComponent<TextMeshProUGUI>();
        
        // Apply the text styling with black vertex color
        titleText.color = vertexColor;
        titleText.enableVertexGradient = true;
        
        // Set up a gradient to give the text a slight vertical variation (keeping black)
        titleText.colorGradient = new VertexGradient(
            new Color(vertexColor.r, vertexColor.g, vertexColor.b, 1.0f),
            new Color(vertexColor.r, vertexColor.g, vertexColor.b, 1.0f),
            new Color(vertexColor.r, vertexColor.g, vertexColor.b, 0.9f),
            new Color(vertexColor.r, vertexColor.g, vertexColor.b, 0.9f)
        );
        
        // Add outlining using TextMeshPro material properties
        titleText.fontStyle = FontStyles.Bold;
        
        // Create a copy of the material to avoid modifying the shared material
        material = new Material(titleText.fontMaterial);
        titleText.fontMaterial = material;
        
        // Set up outline with green glow
        material.EnableKeyword("OUTLINE_ON");
        material.SetColor("_OutlineColor", new Color(textColor.r, textColor.g, textColor.b, 0.3f));
        material.SetFloat("_OutlineWidth", outlineWidth);
        
        // Apply a slight character spacing to match the RoboCop logo
        titleText.characterSpacing = 10f;
        
        // Ensure the text uses the TECHNO font (assumed from previous exploration)
        // If this fails, you'll need to assign the font in the editor
        TMP_FontAsset technoFont = Resources.Load<TMP_FontAsset>("TECHNO SDF");
        if (technoFont != null)
        {
            titleText.font = technoFont;
        }
        
        // Initialize start time for delay
        startTime = Time.time;
    }
    
    private void Update()
    {
        // Handle startup delay and transition
        if (!effectsStarted)
        {
            if (Time.time < startTime + startupDelay)
            {
                return;
            }
            
            effectsStarted = true;
            time = 0f; // Reset time when effects start
            startTime = Time.time; // Reset start time for fade calculation
        }
        
        // Calculate transition effect (0 to 1) over fadeInDuration
        float timeSinceStart = Time.time - startTime;
        effectTransition = Mathf.Clamp01(timeSinceStart / fadeInDuration);
        
        // Increment the effect time
        time += Time.deltaTime;
        
        // Apply effects with a steady pulse
        if (material != null)
        {
            // Create a simple smooth pulse
            float pulse = GetSmoothPulse(time * pulseRate);
            
            // Scale the pulse effect by the transition value for smooth start
            pulse *= effectTransition;
            
            // Update the outline glow
            float glowAlpha = Mathf.Lerp(0.3f, glowIntensity, pulse);
            material.SetColor("_OutlineColor", new Color(
                textColor.r, 
                textColor.g, 
                textColor.b, 
                glowAlpha
            ));
            
            // Modulate outline width with pulse (but keep within limits)
            float pulseOutlineWidth = Mathf.Lerp(outlineWidth, maxOutlineWidth, pulse);
            material.SetFloat("_OutlineWidth", pulseOutlineWidth);
            
            // Create subtle scanline effect by modifying face
            float scanline = Mathf.Sin(time * scanlineSpeed + titleText.rectTransform.position.y * 0.1f);
            material.SetFloat("_FaceDilate", scanline * 0.03f * effectTransition); // Scale by transition
        }
    }
    
    // Simple smooth pulse function
    private float GetSmoothPulse(float t)
    {
        // Use a simple sine wave for smooth pulsing
        // (1 + sin) / 2 gives values from 0 to 1
        float rawPulse = (1f + Mathf.Sin(t * 2f * Mathf.PI)) * 0.5f;
        
        // Apply smoothing using parametric curve
        // Higher smoothing values make the pulse spend more time at low values
        // and less time at peak values (more "blinky" rather than smooth sine)
        if (pulseSmoothing > 0)
        {
            // Power function makes the curve steeper (more time at low values)
            return Mathf.Pow(rawPulse, 1f + pulseSmoothing);
        }
        
        return rawPulse;
    }
} 