using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Handles the Video settings tab content
/// </summary>
public class VideoTabContent : TabContent
{
    [Header("Video Settings")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider fovSlider;
    [SerializeField] private TextMeshProUGUI fovValueText;
    [SerializeField] private Button applyButton;
    
    // Default values
    private const float DEFAULT_FOV = 90.0f;
    
    // Resolution cache
    private Resolution[] resolutions;
    
    protected override void Awake()
    {
        base.Awake();
        
        // Initialize resolution dropdown
        InitializeResolutionDropdown();
        
        // Set up apply button
        if (applyButton != null)
        {
            applyButton.onClick.AddListener(ApplySettings);
        }
    }
    
    private void InitializeResolutionDropdown()
    {
        if (resolutionDropdown != null)
        {
            // Get all available resolutions
            resolutions = Screen.resolutions;
            resolutionDropdown.ClearOptions();
            
            List<string> options = new List<string>();
            int currentResolutionIndex = 0;
            
            for (int i = 0; i < resolutions.Length; i++)
            {
                string option = resolutions[i].width + " x " + resolutions[i].height;
                options.Add(option);
                
                if (resolutions[i].width == Screen.currentResolution.width &&
                    resolutions[i].height == Screen.currentResolution.height)
                {
                    currentResolutionIndex = i;
                }
            }
            
            resolutionDropdown.AddOptions(options);
            resolutionDropdown.value = currentResolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }
    
    protected override void InitializeUI()
    {
        // Update UI with current settings
        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
        }
        
        if (fovSlider != null)
        {
            float currentFov = PlayerPrefs.GetFloat("FOV", DEFAULT_FOV);
            fovSlider.value = currentFov;
            UpdateFOVText(currentFov);
        }
        
        // Update resolution dropdown
        if (resolutionDropdown != null)
        {
            int savedIndex = PlayerPrefs.GetInt("ResolutionIndex", -1);
            if (savedIndex >= 0 && savedIndex < resolutionDropdown.options.Count)
            {
                resolutionDropdown.value = savedIndex;
            }
        }
    }
    
    public void OnFOVChanged(float value)
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            settingsManager.PlayUIClickSound();
        }
        
        UpdateFOVText(value);
    }
    
    private void UpdateFOVText(float value)
    {
        if (fovValueText != null)
        {
            fovValueText.text = value.ToString("F0") + "°";
        }
    }
    
    public override void ApplySettings()
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            settingsManager.PlayUIConfirmSound();
        }
        
        if (resolutions != null && resolutionDropdown != null)
        {
            int selectedResIndex = resolutionDropdown.value;
            
            // Apply resolution
            if (selectedResIndex >= 0 && selectedResIndex < resolutions.Length)
            {
                Resolution selectedResolution = resolutions[selectedResIndex];
                Screen.SetResolution(selectedResolution.width, selectedResolution.height, fullscreenToggle.isOn);
                
                // Save resolution index
                PlayerPrefs.SetInt("ResolutionIndex", selectedResIndex);
            }
        }
        
        // Save fullscreen setting
        if (fullscreenToggle != null)
        {
            PlayerPrefs.SetInt("Fullscreen", fullscreenToggle.isOn ? 1 : 0);
        }
        
        // Save FOV setting
        if (fovSlider != null)
        {
            float fov = fovSlider.value;
            PlayerPrefs.SetFloat("FOV", fov);
            
            // Apply FOV to camera
            if (Camera.main != null)
            {
                Camera.main.fieldOfView = fov;
            }
        }
        
        // Save all settings
        PlayerPrefs.Save();
    }
    
    public override void ResetToDefaults()
    {
        if (fovSlider != null)
        {
            fovSlider.value = DEFAULT_FOV;
            UpdateFOVText(DEFAULT_FOV);
        }
        
        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = true;
        }
        
        if (resolutionDropdown != null)
        {
            // Default to highest resolution
            resolutionDropdown.value = resolutionDropdown.options.Count - 1;
        }
        
        // Apply settings
        ApplySettings();
    }
} 