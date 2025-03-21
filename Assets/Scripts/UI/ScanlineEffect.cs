using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class ScanlineEffect : MonoBehaviour
{
    [Header("Scanline Settings")]
    [SerializeField] private float scanlineSpeed = 0.5f;
    [SerializeField] private float scanlineDensity = 0.5f;
    [SerializeField] private float flickerIntensity = 0.02f;
    [SerializeField] private Color scanlineColor = Color.green;

    private RawImage image;
    private Material material;
    private float time = 0f;

    private void Awake()
    {
        image = GetComponent<RawImage>();
        
        // Create a new material with the scanline shader
        material = new Material(Shader.Find("UI/Default"));
        image.material = material;
    }

    private void Update()
    {
        time += Time.deltaTime * scanlineSpeed;
        
        // Set material properties for scanline effect
        if (material != null)
        {
            material.SetFloat("_ScanlineTime", time);
            material.SetFloat("_ScanlineDensity", scanlineDensity);
            material.SetFloat("_FlickerIntensity", flickerIntensity);
            material.SetColor("_ScanlineColor", scanlineColor);
            
            // Random flicker effect
            float flicker = 1.0f + Random.Range(-flickerIntensity, flickerIntensity);
            image.color = new Color(image.color.r * flicker, 
                                    image.color.g * flicker, 
                                    image.color.b * flicker, 
                                    image.color.a);
        }
    }
} 