using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TitleCRTEffect : MonoBehaviour
{
    [Header("Title Animation Settings")]
    [SerializeField] private Color finalTextColor = new Color(0.0f, 1.0f, 0.0f, 1.0f); // Green color for outline/glow
    [SerializeField] private Color finalVertexColor = new Color(0.0f, 0.0f, 0.0f, 1.0f); // Black color for the text itself
    [SerializeField] private float totalDuration = 8.0f; // Total animation duration
    [SerializeField] private float fadeInDuration = 5.0f; // Time to fade in text (portion of total duration)
    [SerializeField] private float shineIntensity = 2.0f; // How bright the final shine gets
    [SerializeField] private float outlineWidth = 0.2f;
    [SerializeField] private float maxOutlineWidth = 0.4f; // Maximum outline width for shine effect
    [SerializeField] private float startDelay = 0.5f; // Optional delay before starting animation
    
    private TextMeshProUGUI titleText;
    private Material material;
    private float timer = 0f;
    private bool animationStarted = false;
    private float delayTimer = 0f;
    
    private void Awake()
    {
        titleText = GetComponent<TextMeshProUGUI>();
        
        // Start with text fully transparent
        Color initialVertexColor = new Color(finalVertexColor.r, finalVertexColor.g, finalVertexColor.b, 0f);
        titleText.color = initialVertexColor;
        titleText.enableVertexGradient = true;
        
        // Set up gradient (initially transparent)
        titleText.colorGradient = new VertexGradient(
            initialVertexColor,
            initialVertexColor,
            initialVertexColor,
            initialVertexColor
        );
        
        // Set up text styling
        titleText.fontStyle = FontStyles.Bold;
        
        // Create material instance
        material = new Material(titleText.fontMaterial);
        titleText.fontMaterial = material;
        
        // Set up outline (initially transparent)
        material.EnableKeyword("OUTLINE_ON");
        material.SetColor("_OutlineColor", new Color(finalTextColor.r, finalTextColor.g, finalTextColor.b, 0f));
        material.SetFloat("_OutlineWidth", outlineWidth);
        
        // Apply character spacing
        titleText.characterSpacing = 10f;
        
        // Load font if needed
        TMP_FontAsset technoFont = Resources.Load<TMP_FontAsset>("TECHNO SDF");
        if (technoFont != null)
        {
            titleText.font = technoFont;
        }
    }
    
    private void Start()
    {
        // Initialize with completely black/transparent text
        if (material != null)
        {
            // Start with no outline
            material.SetColor("_OutlineColor", new Color(finalTextColor.r, finalTextColor.g, finalTextColor.b, 0f));
        }
    }
    
    private void Update()
    {
        // Handle start delay
        if (!animationStarted)
        {
            delayTimer += Time.deltaTime;
            if (delayTimer < startDelay)
            {
                return;
            }
            animationStarted = true;
        }
        
        // Update timer
        timer += Time.deltaTime;
        
        // Animation is complete
        if (timer >= totalDuration)
        {
            // Ensure we're at final state
            ApplyFinalState();
            return;
        }
        
        // Calculate progress (0 to 1)
        float progress = timer / totalDuration;
        
        // First phase: Fade in from black (0 to fadeInDuration/totalDuration)
        float fadeInProgress = Mathf.Clamp01(timer / fadeInDuration);
        
        // Second phase: Shine effect (fadeInDuration/totalDuration to 1)
        float shineProgress = Mathf.Clamp01((timer - fadeInDuration) / (totalDuration - fadeInDuration));
        
        // Apply effects based on current phase
        if (material != null)
        {
            // Handle text opacity during fade-in
            Color currentVertexColor = new Color(
                finalVertexColor.r,
                finalVertexColor.g,
                finalVertexColor.b,
                Mathf.Lerp(0f, 1f, fadeInProgress)
            );
            
            titleText.color = currentVertexColor;
            
            // Update the gradient
            titleText.colorGradient = new VertexGradient(
                currentVertexColor,
                currentVertexColor,
                new Color(currentVertexColor.r, currentVertexColor.g, currentVertexColor.b, currentVertexColor.a * 0.9f),
                new Color(currentVertexColor.r, currentVertexColor.g, currentVertexColor.b, currentVertexColor.a * 0.9f)
            );
            
            // Handle outline/glow
            float outlineOpacity = Mathf.Lerp(0f, 0.3f, fadeInProgress);
            
            // Add extra shine during second phase
            if (shineProgress > 0)
            {
                // Pulse effect for the shine
                float pulseEffect = Mathf.Sin(shineProgress * Mathf.PI);
                outlineOpacity = Mathf.Lerp(0.3f, shineIntensity, pulseEffect);
                
                // Also increase outline width during shine
                float currentOutlineWidth = Mathf.Lerp(outlineWidth, maxOutlineWidth, pulseEffect);
                material.SetFloat("_OutlineWidth", currentOutlineWidth);
            }
            
            // Apply outline color with current opacity
            material.SetColor("_OutlineColor", new Color(
                finalTextColor.r,
                finalTextColor.g,
                finalTextColor.b,
                outlineOpacity
            ));
        }
    }
    
    private void ApplyFinalState()
    {
        // Set final appearance
        titleText.color = finalVertexColor;
        
        titleText.colorGradient = new VertexGradient(
            finalVertexColor,
            finalVertexColor,
            new Color(finalVertexColor.r, finalVertexColor.g, finalVertexColor.b, 0.9f),
            new Color(finalVertexColor.r, finalVertexColor.g, finalVertexColor.b, 0.9f)
        );
        
        material.SetColor("_OutlineColor", new Color(finalTextColor.r, finalTextColor.g, finalTextColor.b, 0.3f));
        material.SetFloat("_OutlineWidth", outlineWidth);
    }
} 