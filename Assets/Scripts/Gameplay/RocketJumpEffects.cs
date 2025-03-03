// This script creates and manages the particle effects for the rocket jump

using UnityEngine;

public class RocketJumpEffects : MonoBehaviour
{
    [Header("Particle Systems")]
    [SerializeField] private Transform particleSpawnPoint;
    [SerializeField] private ParticleSystem thrustParticles;
    [SerializeField] private ParticleSystem explosionParticles;
    
    [Header("Light")]
    [SerializeField] private Light thrustLight;
    [SerializeField] private float lightIntensity = 3f;
    [SerializeField] private float lightRange = 5f;
    [SerializeField] private Color lightColor = new Color(1f, 0.7f, 0.3f);
    
    private void Awake()
    {
        // Create particle systems if they don't exist
        if (particleSpawnPoint == null)
        {
            particleSpawnPoint = transform;
        }
        
        if (thrustParticles == null)
        {
            thrustParticles = CreateThrustParticles();
        }
        
        if (explosionParticles == null)
        {
            explosionParticles = CreateExplosionParticles();
        }
        
        if (thrustLight == null)
        {
            thrustLight = CreateThrustLight();
        }
    }
    
    public void PlayJumpEffects()
    {
        if (thrustParticles != null)
        {
            thrustParticles.Play();
        }
        
        if (explosionParticles != null)
        {
            explosionParticles.Play();
        }
        
        if (thrustLight != null)
        {
            StartCoroutine(FlashLight());
        }
    }
    
    private System.Collections.IEnumerator FlashLight()
    {
        thrustLight.enabled = true;
        
        // Fade light intensity over time
        float duration = 0.5f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            float normalizedTime = elapsed / duration;
            thrustLight.intensity = Mathf.Lerp(lightIntensity, 0f, normalizedTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        thrustLight.enabled = false;
    }
    
    private ParticleSystem CreateThrustParticles()
    {
        GameObject particleObj = new GameObject("ThrustParticles");
        particleObj.transform.SetParent(particleSpawnPoint);
        particleObj.transform.localPosition = Vector3.zero;
        particleObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Point downwards
        
        ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();
        
        // Main module
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = 0.5f;
        main.startSpeed = 10f;
        main.startSize = 0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 100;
        
        // Emission module
        var emission = ps.emission;
        emission.rateOverTime = 50f;
        emission.SetBursts(new ParticleSystem.Burst[] { 
            new ParticleSystem.Burst(0f, 20f) 
        });
        
        // Shape module
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.1f;
        
        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0.0f), 
                new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.5f),
                new GradientColorKey(new Color(0.7f, 0.1f, 0.1f), 1.0f)
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1.0f, 0.0f), 
                new GradientAlphaKey(0.8f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f) 
            }
        );
        colorOverLifetime.color = gradient;
        
        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        
        AnimationCurve sizeOverLifetimeCurve = new AnimationCurve();
        sizeOverLifetimeCurve.AddKey(0f, 1f);
        sizeOverLifetimeCurve.AddKey(1f, 0.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLifetimeCurve);
        
        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
        
        return ps;
    }
    
    private ParticleSystem CreateExplosionParticles()
    {
        GameObject particleObj = new GameObject("ExplosionParticles");
        particleObj.transform.SetParent(particleSpawnPoint);
        particleObj.transform.localPosition = Vector3.zero;
        
        ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();
        
        // Main module
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = 0.6f;
        main.startSpeed = 3f;
        main.startSize = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 50;
        
        // Emission module
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new ParticleSystem.Burst[] { 
            new ParticleSystem.Burst(0f, 30f) 
        });
        
        // Shape module
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;
        
        // Size over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        
        AnimationCurve sizeOverLifetimeCurve = new AnimationCurve();
        sizeOverLifetimeCurve.AddKey(0f, 0.2f);
        sizeOverLifetimeCurve.AddKey(0.2f, 1f);
        sizeOverLifetimeCurve.AddKey(1f, 0f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeOverLifetimeCurve);
        
        // Color over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(Color.white, 0.0f), 
                new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0.2f),
                new GradientColorKey(new Color(1f, 0.3f, 0.1f), 1.0f)
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1.0f, 0.0f), 
                new GradientAlphaKey(0.8f, 0.2f),
                new GradientAlphaKey(0.0f, 1.0f) 
            }
        );
        colorOverLifetime.color = gradient;
        
        // Velocity over lifetime (for outward explosion effect)
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.speedModifier = 1f;
        
        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));
        
        return ps;
    }
    
    private Light CreateThrustLight()
    {
        GameObject lightObj = new GameObject("ThrustLight");
        lightObj.transform.SetParent(particleSpawnPoint);
        lightObj.transform.localPosition = Vector3.zero;
        
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lightColor;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.shadows = LightShadows.Hard;
        light.enabled = false; // Start disabled, enable when jumping
        
        return light;
    }
}

