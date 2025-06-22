using UnityEngine;
using Cinemachine;
using System.Collections;

/// <summary>
/// Additional cinematic effects and utilities for camera transitions
/// Works in conjunction with CinematicTrailerController
/// </summary>
public class CinematicCameraEffects : MonoBehaviour
{
    [Header("Camera Shake")]
    [SerializeField] private bool enableCameraShake = true;
    [SerializeField] private float shakeIntensity = 2f;
    [SerializeField] private float shakeDuration = 0.5f;
    [SerializeField] private NoiseSettings shakeProfile;
    
    [Header("Dynamic FOV")]
    [SerializeField] private bool enableDynamicFOV = true;
    [SerializeField] private float speedFOVMultiplier = 0.1f;
    [SerializeField] private float maxSpeedFOV = 80f;
    [SerializeField] private float minSpeedFOV = 50f;
    [SerializeField] private float fovSmoothTime = 2f;
    
    [Header("Screen Effects")]
    [SerializeField] private bool enableScreenDistortion = false;
    [SerializeField] private AnimationCurve distortionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private CinemachineVirtualCamera activeCamera;
    private CinemachineBasicMultiChannelPerlin noise;
    private Rigidbody carRigidbody;
    private float baseFOV = 60f;
    private float currentFOV = 60f;
    private float targetFOV = 60f;
    private bool isShaking = false;

    public void Initialize(Transform carTarget)
    {
        if (carTarget != null)
        {
            carRigidbody = carTarget.GetComponent<Rigidbody>();
        }
    }

    public void SetActiveCamera(CinemachineVirtualCamera camera)
    {
        activeCamera = camera;
        if (camera != null)
        {
            baseFOV = camera.m_Lens.FieldOfView;
            currentFOV = baseFOV;
            targetFOV = baseFOV;
            
            // Get noise component for camera shake
            noise = camera.GetCinemachineComponent<CinemachineBasicMultiChannelPerlin>();
        }
    }

    private void Update()
    {
        if (activeCamera != null)
        {
            UpdateDynamicFOV();
        }
    }

    private void UpdateDynamicFOV()
    {
        if (!enableDynamicFOV || carRigidbody == null) return;

        // Calculate target FOV based on speed
        float speed = carRigidbody.linearVelocity.magnitude;
        float speedFOV = baseFOV + (speed * speedFOVMultiplier);
        targetFOV = Mathf.Clamp(speedFOV, minSpeedFOV, maxSpeedFOV);
        
        // Smooth transition to target FOV
        currentFOV = Mathf.Lerp(currentFOV, targetFOV, Time.deltaTime / fovSmoothTime);
        
        // Apply to camera
        var lens = activeCamera.m_Lens;
        lens.FieldOfView = currentFOV;
        activeCamera.m_Lens = lens;
    }

    public void TriggerCameraShake(float intensity = -1f, float duration = -1f)
    {
        if (!enableCameraShake || noise == null) return;
        
        float shakeInt = intensity > 0 ? intensity : shakeIntensity;
        float shakeDur = duration > 0 ? duration : shakeDuration;
        
        StartCoroutine(CameraShakeCoroutine(shakeInt, shakeDur));
    }

    private IEnumerator CameraShakeCoroutine(float intensity, float duration)
    {
        if (isShaking) yield break;
        
        isShaking = true;
        
        // Set noise profile
        if (shakeProfile != null)
        {
            noise.m_NoiseProfile = shakeProfile;
        }
        
        noise.m_AmplitudeGain = intensity;
        noise.m_FrequencyGain = 1f;
        
        yield return new WaitForSeconds(duration);
        
        // Fade out shake
        float fadeTime = 0.2f;
        float elapsed = 0f;
        
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeTime;
            noise.m_AmplitudeGain = Mathf.Lerp(intensity, 0f, t);
            yield return null;
        }
        
        noise.m_AmplitudeGain = 0f;
        isShaking = false;
    }

    public void SetFOVOverride(float targetFOV, float transitionTime = 1f)
    {
        StartCoroutine(TransitionFOV(targetFOV, transitionTime));
    }

    private IEnumerator TransitionFOV(float target, float duration)
    {
        if (activeCamera == null) yield break;
        
        float startFOV = activeCamera.m_Lens.FieldOfView;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float newFOV = Mathf.Lerp(startFOV, target, t);
            
            var lens = activeCamera.m_Lens;
            lens.FieldOfView = newFOV;
            activeCamera.m_Lens = lens;
            
            yield return null;
        }
        
        // Ensure final value is set
        var finalLens = activeCamera.m_Lens;
        finalLens.FieldOfView = target;
        activeCamera.m_Lens = finalLens;
    }

    public void ApplyImpactEffect(Vector3 impactPoint, float intensity = 1f)
    {
        if (!enableCameraShake) return;
        
        // Calculate distance-based intensity
        if (activeCamera != null)
        {
            float distance = Vector3.Distance(activeCamera.transform.position, impactPoint);
            float adjustedIntensity = intensity * Mathf.Clamp01(10f / distance);
            
            TriggerCameraShake(adjustedIntensity, shakeDuration * adjustedIntensity);
        }
    }

    public void SetCinematicLens(float fov, float nearClip = 0.3f, float farClip = 1000f)
    {
        if (activeCamera == null) return;
        
        var lens = activeCamera.m_Lens;
        lens.FieldOfView = fov;
        lens.NearClipPlane = nearClip;
        lens.FarClipPlane = farClip;
        activeCamera.m_Lens = lens;
        
        baseFOV = fov;
        currentFOV = fov;
        targetFOV = fov;
    }

    public void EnableDynamicFOV(bool enable)
    {
        enableDynamicFOV = enable;
    }

    // Property accessors
    public bool IsShaking => isShaking;
    public float CurrentFOV => currentFOV;
    public CinemachineVirtualCamera ActiveCamera => activeCamera;
} 