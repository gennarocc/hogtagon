using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class OptionsMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Settings existingSettings; // Reference to the original Settings component
    [SerializeField] private TabController tabController;
    [SerializeField] private Button backButton;
    [SerializeField] private Button resetToDefaultsButton;
    
    [Header("Category Panels")]
    [SerializeField] private GameObject videoPanel;
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject controlsPanel;
    
    [Header("Navigation Arrows")]
    [SerializeField] private Button leftArrow;
    [SerializeField] private Button rightArrow;
    
    [Header("Video Settings")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider gammaSlider;
    [SerializeField] private Slider fovSlider;
    [SerializeField] private Toggle vsyncToggle;
    [SerializeField] private TMP_Dropdown windowModeDropdown;
    
    [Header("Audio Settings")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeLabel;
    [SerializeField] private TextMeshProUGUI musicVolumeLabel;
    [SerializeField] private TextMeshProUGUI sfxVolumeLabel;
    
    [Header("Gameplay Settings")]
    [SerializeField] private Slider cameraSensitivitySlider;
    
    [Header("Settings References")]
    [SerializeField] private Button applyButton;

    [Header("Default Values")]
    [SerializeField] private float defaultSensitivity = 1.0f;
    [SerializeField] private float defaultVolume = 0.8f;

    private void Start()
    {
        // Set up button listeners
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButtonClicked);
            
        if (resetToDefaultsButton != null)
            resetToDefaultsButton.onClick.AddListener(ResetToDefaults);
            
        if (leftArrow != null)
            leftArrow.onClick.AddListener(OnLeftArrowClicked);
            
        if (rightArrow != null)
            rightArrow.onClick.AddListener(OnRightArrowClicked);
        
        // Add apply button listener if it's not already assigned
        if (applyButton != null && applyButton.onClick.GetPersistentEventCount() == 0)
        {
            applyButton.onClick.AddListener(ApplySettings);
        }
            
        // Initialize values from existing settings
        InitializeFromExistingSettings();
    }
    
    private void OnEnable()
    {
        // Refresh UI with current settings when the menu is opened
        InitializeFromExistingSettings();
    }
    
    private void InitializeFromExistingSettings()
    {
        if (existingSettings == null)
        {
            Debug.LogError("OptionsMenuController: No reference to Settings component!");
            return;
        }
        
        // Initialize sliders with current values
        if (masterVolumeSlider != null && existingSettings.MasterVolume != null)
        {
            float volume = existingSettings.MasterVolume.GetGlobalValue();
            masterVolumeSlider.value = volume;
            UpdateVolumeLabel(masterVolumeLabel, "Master Volume", volume);
        }
        
        if (musicVolumeSlider != null && existingSettings.MusicVolume != null)
        {
            float volume = existingSettings.MusicVolume.GetGlobalValue();
            musicVolumeSlider.value = volume;
            UpdateVolumeLabel(musicVolumeLabel, "Music Volume", volume);
        }
        
        if (sfxVolumeSlider != null && existingSettings.SfxVolume != null)
        {
            float volume = existingSettings.SfxVolume.GetGlobalValue();
            sfxVolumeSlider.value = volume;
            UpdateVolumeLabel(sfxVolumeLabel, "SFX Volume", volume);
        }
        
        if (cameraSensitivitySlider != null && existingSettings.cameraSensitivity != null)
        {
            cameraSensitivitySlider.value = existingSettings.cameraSensitivity.value;
        }
        
        // Initialize resolution dropdown via Settings object
        
        // Setup fullscreen toggle
        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
        }
    }
    
    // Handle tab navigation
    private void OnLeftArrowClicked()
    {
        if (tabController != null)
            tabController.PreviousTab();
    }
    
    private void OnRightArrowClicked()
    {
        if (tabController != null)
            tabController.NextTab();
    }
    
    // Back button handler
    private void OnBackButtonClicked()
    {
        if (existingSettings != null)
            existingSettings.ButtonCancelAudio();
            
        gameObject.SetActive(false);
    }
    
    // Reset to defaults
    private void ResetToDefaults()
    {
        if (existingSettings != null)
            existingSettings.ButtonConfirmAudio();
            
        // Reset video settings
        if (resolutionDropdown != null)
            resolutionDropdown.value = resolutionDropdown.options.Count - 1; // Default to highest resolution
            
        if (fullscreenToggle != null)
            fullscreenToggle.isOn = true;
            
        if (gammaSlider != null)
            gammaSlider.value = 1.0f;
            
        if (fovSlider != null)
            fovSlider.value = 90.0f;
            
        if (vsyncToggle != null)
            vsyncToggle.isOn = false;
            
        // Reset audio settings
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = 1.0f;
            SetMasterVolume(1.0f);
        }
        
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = 1.0f;
            SetMusicVolume(1.0f);
        }
        
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = 1.0f;
            SetSfxVolume(1.0f);
        }
        
        // Reset gameplay settings
        if (cameraSensitivitySlider != null)
        {
            cameraSensitivitySlider.value = 1.0f;
            SetCameraSensitivity(1.0f);
        }
    }
    
    // Volume control delegates (bridge to existing Settings)
    public void SetMasterVolume(float volume)
    {
        if (existingSettings != null)
        {
            existingSettings.SetMasterVolume(volume);
            existingSettings.ButtonClickAudio();
        }
        
        UpdateVolumeLabel(masterVolumeLabel, "Master Volume", volume);
    }
    
    public void SetMusicVolume(float volume)
    {
        if (existingSettings != null)
        {
            existingSettings.SetMusicVolume(volume);
            existingSettings.ButtonClickAudio();
        }
        
        UpdateVolumeLabel(musicVolumeLabel, "Music Volume", volume);
    }
    
    public void SetSfxVolume(float volume)
    {
        if (existingSettings != null)
        {
            existingSettings.SetSfxVolume(volume);
            existingSettings.ButtonClickAudio();
        }
        
        UpdateVolumeLabel(sfxVolumeLabel, "SFX Volume", volume);
    }
    
    public void SetCameraSensitivity(float sensitivity)
    {
        if (existingSettings != null)
        {
            existingSettings.cameraSensitivity.value = sensitivity;
            existingSettings.SetCameraSensitivty();
            existingSettings.ButtonClickAudio();
        }
    }
    
    // Audio feedback methods
    public void ButtonClickAudio()
    {
        if (existingSettings != null)
        {
            existingSettings.ButtonClickAudio();
        }
    }
    
    public void ButtonConfirmAudio()
    {
        if (existingSettings != null)
        {
            existingSettings.ButtonConfirmAudio();
        }
    }
    
    public void ButtonCancelAudio()
    {
        if (existingSettings != null)
        {
            existingSettings.ButtonCancelAudio();
        }
    }
    
    // Apply settings (bridge to existing Settings)
    public void ApplyVideoSettings()
    {
        if (existingSettings != null)
        {
            existingSettings.ApplyVideoSettings();
        }
    }
    
    private void UpdateVolumeLabel(TextMeshProUGUI label, string labelName, float value)
    {
        if (label != null)
        {
            label.text = $"{labelName}: {Mathf.RoundToInt(value * 100)}%";
        }
    }
    
    // Add this to support apply button
    public void ApplySettings()
    {
        // Apply video settings
        ApplyVideoSettings();
        
        // Volume settings are applied immediately
        
        // Apply camera sensitivity
        if (existingSettings != null && existingSettings.cameraSensitivity != null)
        {
            existingSettings.SetCameraSensitivty();
        }
        
        // Play confirmation sound
        if (existingSettings != null)
        {
            existingSettings.ButtonConfirmAudio();
        }
    }
    
    // Handle resolution selection
    public void OnResolutionSelected(int resolutionIndex)
    {
        if (existingSettings != null)
        {
            existingSettings.OnResolutionSelected(resolutionIndex);
            existingSettings.ButtonClickAudio();
        }
    }

    // Called by the resolution dropdown
    public void SetResolution(int resolutionIndex)
    {
        if (resolutionIndex < 0 || resolutionDropdown == null)
            return;
            
        string resString = resolutionDropdown.options[resolutionIndex].text;
        string[] dimensions = resString.Split('x');
        
        if (dimensions.Length == 2)
        {
            if (int.TryParse(dimensions[0], out int width) && 
                int.TryParse(dimensions[1], out int height))
            {
                // Store in PlayerPrefs
                PlayerPrefs.SetInt("ScreenWidth", width);
                PlayerPrefs.SetInt("ScreenHeight", height);
            }
        }
    }
} 