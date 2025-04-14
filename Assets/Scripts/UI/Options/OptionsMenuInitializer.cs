using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class OptionsMenuInitializer : MonoBehaviour
{
    [SerializeField] private OptionsMenuCreator menuCreator;
    [SerializeField] private OptionsMenuController menuController;
    [SerializeField] private Settings settingsInstance; // Reference to the existing Settings component

    private void Start()
    {
        if (menuCreator == null)
        {
            menuCreator = GetComponent<OptionsMenuCreator>();
        }
        
        if (menuController == null)
        {
            menuController = GetComponent<OptionsMenuController>();
        }
        
        if (settingsInstance == null)
        {
            // Find any existing OptionsMenuInitializer
            OptionsMenuInitializer existingInitializer = FindFirstObjectByType<OptionsMenuInitializer>();
            if (existingInitializer == null)
            {
                Debug.LogError("OptionsMenuInitializer: Could not find existing OptionsMenuInitializer!");
            }
        }
        
        // Initialize all settings
        InitializeVideoSettings();
        InitializeAudioSettings();
        InitializeGameplaySettings();
        InitializeControlsSettings();
    }
    
    private void InitializeVideoSettings()
    {
        // Resolution
        GameObject resolutionSetting = menuCreator.CreateSettingItem("Video", "Resolution");
        
        // Get available resolutions directly
        List<string> resolutionOptions = new List<string>();
        Resolution[] resolutions = Screen.resolutions;
        
        foreach (Resolution res in resolutions)
        {
            resolutionOptions.Add($"{res.width}x{res.height}");
        }
        
        // If no resolutions were found, add defaults
        if (resolutionOptions.Count == 0)
        {
            resolutionOptions.Add("1280x720");
            resolutionOptions.Add("1920x1080");
            resolutionOptions.Add("2560x1440");
        }
        
        // Find current resolution index
        string currentRes = $"{Screen.width}x{Screen.height}";
        int currentResIndex = 0;
        
        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            if (resolutionOptions[i] == currentRes)
            {
                currentResIndex = i;
                break;
            }
        }
        
        TMP_Dropdown resolutionDropdown = menuCreator.AddDropdownToSetting(resolutionSetting, resolutionOptions, currentResIndex);
        
        // Connect to existing Settings component
        if (settingsInstance != null && resolutionDropdown != null)
        {
            // Store a reference to the dropdown in Settings if needed
            if (settingsInstance.GetComponent<Settings>() != null && 
                settingsInstance.GetType().GetField("resolutionDropdown") != null)
            {
                settingsInstance.GetType().GetField("resolutionDropdown").SetValue(settingsInstance, resolutionDropdown);
            }
            
            // Add value changed listener
            resolutionDropdown.onValueChanged.AddListener(index => {
                if (settingsInstance != null)
                {
                    settingsInstance.OnResolutionSelected(index);
                }
            });
        }
        
        // Fullscreen
        GameObject fullscreenSetting = menuCreator.CreateSettingItem("Video", "Fullscreen");
        Toggle fullscreenToggle = menuCreator.AddToggleToSetting(fullscreenSetting, Screen.fullScreen);
        
        // Connect to existing Settings
        if (settingsInstance != null && fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(value => {
                if (settingsInstance != null)
                {
                    settingsInstance.OnFullscreenToggled(value);
                }
            });
        }
        
        // Add Apply button
        GameObject applyButtonSetting = menuCreator.CreateSettingItem("Video", "Apply Changes");
        Button applyButton = menuCreator.AddButtonToSetting(applyButtonSetting, "Apply");
        
        // Connect to existing Settings
        if (settingsInstance != null && applyButton != null)
        {
            applyButton.onClick.AddListener(() => {
                if (settingsInstance != null)
                {
                    settingsInstance.ApplyVideoSettings();
                }
            });
        }
    }
    
    private void InitializeAudioSettings()
    {
        // Master Volume
        GameObject masterVolumeSetting = menuCreator.CreateSettingItem("Audio", "Master Volume");
        float currentMasterVolume = 0.8f;
        
        // Get current volume from Settings if available
        if (settingsInstance != null && settingsInstance.MasterVolume != null)
        {
            currentMasterVolume = settingsInstance.MasterVolume.GetGlobalValue();
        }
        
        Slider masterVolumeSlider = menuCreator.AddSliderToSetting(masterVolumeSetting, 0f, 1f, currentMasterVolume);
        if (masterVolumeSlider != null && settingsInstance != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(value => {
                // Call existing Settings method
                settingsInstance.SetMasterVolume(value);
                settingsInstance.ButtonClickAudio();
            });
        }
        
        // Music Volume
        GameObject musicVolumeSetting = menuCreator.CreateSettingItem("Audio", "Music Volume");
        float currentMusicVolume = 0.8f;
        
        // Get current volume from Settings if available
        if (settingsInstance != null && settingsInstance.MusicVolume != null)
        {
            currentMusicVolume = settingsInstance.MusicVolume.GetGlobalValue();
        }
        
        Slider musicVolumeSlider = menuCreator.AddSliderToSetting(musicVolumeSetting, 0f, 1f, currentMusicVolume);
        if (musicVolumeSlider != null && settingsInstance != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(value => {
                // Call existing Settings method
                settingsInstance.SetMusicVolume(value);
                settingsInstance.ButtonClickAudio();
            });
        }
        
        // SFX Volume
        GameObject sfxVolumeSetting = menuCreator.CreateSettingItem("Audio", "SFX Volume");
        float currentSfxVolume = 0.8f;
        
        // Get current volume from Settings if available
        if (settingsInstance != null && settingsInstance.SfxVolume != null)
        {
            currentSfxVolume = settingsInstance.SfxVolume.GetGlobalValue();
        }
        
        Slider sfxVolumeSlider = menuCreator.AddSliderToSetting(sfxVolumeSetting, 0f, 1f, currentSfxVolume);
        if (sfxVolumeSlider != null && settingsInstance != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(value => {
                // Call existing Settings method
                settingsInstance.SetSfxVolume(value);
                settingsInstance.ButtonClickAudio();
            });
        }
    }
    
    private void InitializeGameplaySettings()
    {
        // Add username input field
        GameObject usernameSetting = menuCreator.CreateSettingItem("Gameplay", "Username");
        
        // Get saved username or use a default
        string savedUsername = PlayerPrefs.GetString("Username", "Player");
        
        // Add input field for username with a character limit of 10
        TMP_InputField usernameField = menuCreator.AddInputFieldToSetting(usernameSetting, "Enter username", savedUsername, 10);
        
        if (usernameField != null)
        {
            // Save the username when it changes
            usernameField.onValueChanged.AddListener(value => {
                if (value.Length > 0)
                {
                    PlayerPrefs.SetString("Username", value);
                    PlayerPrefs.Save();
                    
                    // Play audio feedback
                    if (settingsInstance != null)
                    {
                        settingsInstance.ButtonClickAudio();
                    }
                }
            });
        }
        
        // Add more gameplay settings here as needed
    }
    
    private void InitializeControlsSettings()
    {
        // Camera Sensitivity
        GameObject sensitivitySetting = menuCreator.CreateSettingItem("Controls", "Camera Sensitivity");
        
        // Get current sensitivity from Settings if available
        float currentSensitivity = 1.0f;
        if (settingsInstance != null && settingsInstance.cameraSensitivity != null)
        {
            currentSensitivity = settingsInstance.cameraSensitivity.value;
        }
        
        Slider sensitivitySlider = menuCreator.AddSliderToSetting(sensitivitySetting, 0.1f, 2.0f, currentSensitivity);
        if (sensitivitySlider != null && settingsInstance != null)
        {
            // Connect to existing Settings component if available
            sensitivitySlider.onValueChanged.AddListener(value => {
                if (settingsInstance.cameraSensitivity != null)
                {
                    settingsInstance.cameraSensitivity.value = value;
                    settingsInstance.SetCameraSensitivty();
                    settingsInstance.ButtonClickAudio();
                }
            });
            
            // Store a reference to the slider in Settings if needed
            if (settingsInstance.GetComponent<Settings>() != null && 
                settingsInstance.GetType().GetField("cameraSensitivity") != null)
            {
                settingsInstance.cameraSensitivity = sensitivitySlider;
            }
        }

        // Example key binding settings
        GameObject moveForwardSetting = menuCreator.CreateSettingItem("Controls", "Move Forward");
        menuCreator.AddKeyBindingToSetting(moveForwardSetting, "W");
        
        GameObject moveBackwardSetting = menuCreator.CreateSettingItem("Controls", "Move Backward");
        menuCreator.AddKeyBindingToSetting(moveBackwardSetting, "S");
        
        GameObject moveLeftSetting = menuCreator.CreateSettingItem("Controls", "Move Left");
        menuCreator.AddKeyBindingToSetting(moveLeftSetting, "A");
        
        GameObject moveRightSetting = menuCreator.CreateSettingItem("Controls", "Move Right");
        menuCreator.AddKeyBindingToSetting(moveRightSetting, "D");
        
        GameObject jumpSetting = menuCreator.CreateSettingItem("Controls", "Jump");
        menuCreator.AddKeyBindingToSetting(jumpSetting, "Space");
        
        // Add more controls settings here
    }
} 