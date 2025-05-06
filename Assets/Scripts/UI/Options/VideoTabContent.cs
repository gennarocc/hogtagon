using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Handles the Video settings tab content
/// </summary>
public class VideoTabContent : TabContent
{
    [Header("Video Settings")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
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
            Resolution[] allResolutions = Screen.resolutions;

            // Filter to only include 60Hz, 120Hz, and 144Hz (with small margin for rounding)
            resolutions = allResolutions.Where(res =>
            {
                float rate = (float)res.refreshRateRatio.value;
                return Mathf.Approximately(rate, 60f) ||
                       (rate >= 59.5f && rate <= 60.5f) ||
                       (rate >= 119.5f && rate <= 120.5f) ||
                       (rate >= 143.5f && rate <= 144.5f);
            }).ToArray();

            resolutionDropdown.ClearOptions();

            List<string> options = new List<string>();
            int currentResolutionIndex = 0;

            // Debug log all resolutions
            Debug.Log($"[VideoTabContent] Found {resolutions.Length} screen resolutions (filtered):");
            for (int i = 0; i < resolutions.Length; i++)
            {
                string option = $"{resolutions[i].width} x {resolutions[i].height} @ {Mathf.RoundToInt((float)resolutions[i].refreshRateRatio.value)}Hz";
                options.Add(option);
                Debug.Log($"  [{i}] {option} (Raw refresh rate: {resolutions[i].refreshRateRatio.value})");

                if (resolutions[i].width == Screen.currentResolution.width &&
                    resolutions[i].height == Screen.currentResolution.height &&
                    Mathf.Approximately((float)resolutions[i].refreshRateRatio.value, (float)Screen.currentResolution.refreshRateRatio.value))
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

    public override void ApplySettings()
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIConfirm);

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

        // Save all settings
        PlayerPrefs.Save();

        // Call the main SettingsManager ApplySettings to ensure JSON saving happens
        if (settingsManager != null)
        {
            settingsManager.ApplySettings();
        }
    }

    public override void ResetToDefaults()
    {
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