using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class OptionsMenuInitializer : MonoBehaviour
{
    [SerializeField] private OptionsMenuCreator menuCreator;
    [SerializeField] private OptionsMenuController menuController;

    private void Start()
    {
        // Initialize the menu after delay to ensure all components are ready
        Invoke("InitializeOptionsMenu", 0.1f);
    }

    public void InitializeOptionsMenu()
    {
        if (menuCreator == null)
        {
            Debug.LogError("Options Menu Creator reference is missing!");
            return;
        }

        // Create Video settings
        InitializeVideoSettings();
        
        // Create Audio settings
        InitializeAudioSettings();
        
        // Create Gameplay settings
        InitializeGameplaySettings();
        
        // Create Controls settings
        InitializeControlsSettings();
    }
    
    private void InitializeVideoSettings()
    {
        // Graphics autodetect
        GameObject graphicsAutodetectSetting = menuCreator.CreateSettingItem("VIDEO", "Graphics autodetect");
        Button detectButton = menuCreator.AddButtonToSetting(graphicsAutodetectSetting, "Detect");
        if (detectButton != null && menuController != null)
        {
            detectButton.onClick.AddListener(() => {
                // Implement auto-detection logic
                Debug.Log("Auto-detecting graphics settings...");
            });
        }
        
        // Gamma correction
        GameObject gammaSetting = menuCreator.CreateSettingItem("VIDEO", "Gamma correction");
        Slider gammaSlider = menuCreator.AddSliderToSetting(gammaSetting, 0.5f, 2.0f, 1.0f);
        if (gammaSlider != null && menuController != null)
        {
            gammaSlider.onValueChanged.AddListener(value => {
                // Update gamma setting
                Debug.Log($"Gamma set to {value}");
            });
        }
        
        // Field of view
        GameObject fovSetting = menuCreator.CreateSettingItem("VIDEO", "Field of view");
        Slider fovSlider = menuCreator.AddSliderToSetting(fovSetting, 60f, 120f, 90f);
        if (fovSlider != null && menuController != null)
        {
            fovSlider.onValueChanged.AddListener(value => {
                // Update FOV setting
                Debug.Log($"FOV set to {value}");
            });
        }
        
        // V-Sync
        GameObject vsyncSetting = menuCreator.CreateSettingItem("VIDEO", "V Sync");
        Toggle vsyncToggle = menuCreator.AddToggleToSetting(vsyncSetting, false);
        if (vsyncToggle != null && menuController != null)
        {
            vsyncToggle.onValueChanged.AddListener(value => {
                // Update V-Sync setting
                Debug.Log($"V-Sync {(value ? "enabled" : "disabled")}");
            });
        }
        
        // Window mode
        GameObject windowModeSetting = menuCreator.CreateSettingItem("VIDEO", "Window mode");
        List<string> windowModes = new List<string> { "Windowed", "Borderless", "Fullscreen" };
        TMP_Dropdown windowModeDropdown = menuCreator.AddDropdownToSetting(windowModeSetting, windowModes, 2);
        if (windowModeDropdown != null && menuController != null)
        {
            windowModeDropdown.onValueChanged.AddListener(index => {
                // Update window mode setting
                Debug.Log($"Window mode set to {windowModes[index]}");
            });
        }
        
        // Resolution
        GameObject resolutionSetting = menuCreator.CreateSettingItem("VIDEO", "Resolution");
        List<string> resolutions = new List<string> { "1280x720", "1920x1080", "2560x1440", "3840x2160" };
        TMP_Dropdown resolutionDropdown = menuCreator.AddDropdownToSetting(resolutionSetting, resolutions, 1);
        if (resolutionDropdown != null && menuController != null)
        {
            resolutionDropdown.onValueChanged.AddListener(index => {
                // Update resolution setting
                Debug.Log($"Resolution set to {resolutions[index]}");
                if (menuController != null)
                {
                    menuController.OnResolutionSelected(index);
                }
            });
        }
        
        // More video settings like the ones in the reference image...
    }
    
    private void InitializeAudioSettings()
    {
        // Master Volume
        GameObject masterVolumeSetting = menuCreator.CreateSettingItem("AUDIO", "Master Volume");
        Slider masterVolumeSlider = menuCreator.AddSliderToSetting(masterVolumeSetting, 0f, 100f, 80f);
        if (masterVolumeSlider != null && menuController != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(value => {
                if (menuController != null)
                {
                    menuController.SetMasterVolume(value);
                }
            });
        }
        
        // Music Volume
        GameObject musicVolumeSetting = menuCreator.CreateSettingItem("AUDIO", "Music Volume");
        Slider musicVolumeSlider = menuCreator.AddSliderToSetting(musicVolumeSetting, 0f, 100f, 70f);
        if (musicVolumeSlider != null && menuController != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(value => {
                if (menuController != null)
                {
                    menuController.SetMusicVolume(value);
                }
            });
        }
        
        // SFX Volume
        GameObject sfxVolumeSetting = menuCreator.CreateSettingItem("AUDIO", "SFX Volume");
        Slider sfxVolumeSlider = menuCreator.AddSliderToSetting(sfxVolumeSetting, 0f, 100f, 90f);
        if (sfxVolumeSlider != null && menuController != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(value => {
                if (menuController != null)
                {
                    menuController.SetSfxVolume(value);
                }
            });
        }
    }
    
    private void InitializeGameplaySettings()
    {
        // Add gameplay settings here
        // Camera sensitivity has been moved to Controls tab
    }
    
    private void InitializeControlsSettings()
    {
        // Camera Sensitivity
        GameObject sensitivitySetting = menuCreator.CreateSettingItem("CONTROLS", "Camera Sensitivity");
        Slider sensitivitySlider = menuCreator.AddSliderToSetting(sensitivitySetting, 0.1f, 2.0f, 1.0f);
        if (sensitivitySlider != null && menuController != null)
        {
            sensitivitySlider.onValueChanged.AddListener(value => {
                if (menuController != null)
                {
                    menuController.SetCameraSensitivity(value);
                }
            });
        }

        // Example key binding settings
        GameObject moveForwardSetting = menuCreator.CreateSettingItem("CONTROLS", "Move Forward");
        menuCreator.AddKeyBindingToSetting(moveForwardSetting, "W");
        
        GameObject moveBackwardSetting = menuCreator.CreateSettingItem("CONTROLS", "Move Backward");
        menuCreator.AddKeyBindingToSetting(moveBackwardSetting, "S");
        
        GameObject moveLeftSetting = menuCreator.CreateSettingItem("CONTROLS", "Move Left");
        menuCreator.AddKeyBindingToSetting(moveLeftSetting, "A");
        
        GameObject moveRightSetting = menuCreator.CreateSettingItem("CONTROLS", "Move Right");
        menuCreator.AddKeyBindingToSetting(moveRightSetting, "D");
        
        GameObject jumpSetting = menuCreator.CreateSettingItem("CONTROLS", "Jump");
        menuCreator.AddKeyBindingToSetting(jumpSetting, "Space");
        
        // Add more controls settings here
    }
} 