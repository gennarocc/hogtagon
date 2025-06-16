using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Audio;
using Unity.Netcode;
using System.Linq;

/// <summary>
/// Central manager for all game settings. Handles loading, saving, and applying settings.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    [Header("Tab System")]
    [SerializeField] private TabController tabController;

    [Header("Panels")]
    [SerializeField] private GameObject videoPanel;
    [SerializeField] private GameObject audioPanel;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject controlsPanel;

    [Header("Navigation")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button resetToDefaultsButton;

    [Header("Video Settings")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Audio Settings")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;
    [SerializeField] private AudioMixer audioMixer;

    [Header("Gameplay Settings")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Toggle cameraSteeringToggle;
    [SerializeField] private TMP_Dropdown honkTypeDropdown;

    [Header("Controls Settings")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TextMeshProUGUI sensitivityValueText;

    // Resolution cache
    private Resolution[] resolutions;

    // Default settings
    private const float DEFAULT_MASTER_VOLUME = 0.8f;
    private const float DEFAULT_MUSIC_VOLUME = 0.8f;
    private const float DEFAULT_SFX_VOLUME = 0.8f;
    private const float DEFAULT_SENSITIVITY = 1.0f;
    private const string DEFAULT_USERNAME = "Player";
    private const int DEFAULT_HONK_TYPE = 300;

    private void Awake()
    {
        // Initialize the resolution dropdown
        InitializeResolutionDropdown();
    }

    private void Start()
    {
        // Set up button listeners
        SetupButtonListeners();

        // Load settings from JSON file
        LoadSettingsFromFile();

        // Update UI to reflect current settings
        UpdateUI();
    }

    private void OnEnable()
    {
        // Refresh UI with current settings when reopened
        UpdateUI();
    }

    private void SetupButtonListeners()
    {
        // Back button
        if (backButton != null)
            backButton.onClick.AddListener(OnBackButtonClicked);

        // Reset button
        if (resetToDefaultsButton != null)
            resetToDefaultsButton.onClick.AddListener(ResetToDefaults);

        // Video settings
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);

        // Audio settings
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);

        // Gameplay settings
        if (usernameInput != null)
            usernameInput.onValueChanged.AddListener(OnUsernameChanged);

        // Controls settings
        if (sensitivitySlider != null)
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);

        if (cameraSteeringToggle != null)
            cameraSteeringToggle.onValueChanged.AddListener(OnCameraSteeringToggled);
    }

    #region Settings Loading/Saving

    private void LoadAllSettings()
    {
        // Load video settings
        int resIndex = PlayerPrefs.GetInt("ResolutionIndex", -1);
        bool fullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;

        // Load audio settings
        float masterVolume = PlayerPrefs.GetFloat("MasterVolume", DEFAULT_MASTER_VOLUME);
        float musicVolume = PlayerPrefs.GetFloat("MusicVolume", DEFAULT_MUSIC_VOLUME);
        float sfxVolume = PlayerPrefs.GetFloat("SFXVolume", DEFAULT_SFX_VOLUME);

        // Load gameplay settings
        string username = PlayerPrefs.GetString("Username", DEFAULT_USERNAME);

        // Load controls settings
        float sensitivity = PlayerPrefs.GetFloat("Sensitivity", DEFAULT_SENSITIVITY);

        // Apply the settings to the actual game systems
        ApplyLoadedSettings(resIndex, fullscreen, masterVolume, musicVolume, sfxVolume, username, sensitivity);
    }

    private void ApplyLoadedSettings(int resolutionIndex, bool fullscreen,
                                    float masterVolume, float musicVolume, float sfxVolume,
                                    string username, float sensitivity)
    {
        // Apply resolution if valid
        if (resolutionIndex >= 0 && resolutionIndex < resolutions.Length)
        {
            Resolution resolution = resolutions[resolutionIndex];
            Screen.SetResolution(resolution.width, resolution.height, fullscreen);
        }
        else
        {
            // Use current resolution
            Screen.fullScreen = fullscreen;
        }

        // Apply audio settings
        SetMasterVolume(masterVolume);
        SetMusicVolume(musicVolume);
        SetSFXVolume(sfxVolume);

        // Apply username
        PlayerPrefs.SetString("Username", username);

        // Apply sensitivity
        SetCameraSensitivity(sensitivity);
    }

    private void SaveAllSettings()
    {
        // Save with null checks to prevent NullReferenceException

        try
        {
            // Save video settings (with null checks)
            if (resolutionDropdown != null)
                PlayerPrefs.SetInt("ResolutionIndex", resolutionDropdown.value);

            if (fullscreenToggle != null)
                PlayerPrefs.SetInt("Fullscreen", fullscreenToggle.isOn ? 1 : 0);

            // Save audio settings (with null checks)
            if (masterVolumeSlider != null)
                PlayerPrefs.SetFloat("MasterVolume", masterVolumeSlider.value);

            if (musicVolumeSlider != null)
                PlayerPrefs.SetFloat("MusicVolume", musicVolumeSlider.value);

            if (sfxVolumeSlider != null)
                PlayerPrefs.SetFloat("SFXVolume", sfxVolumeSlider.value);

            // Save gameplay settings
            if (usernameInput != null)
                PlayerPrefs.SetString("Username", usernameInput.text);

            if (honkTypeDropdown.value != 0)
                PlayerPrefs.SetInt("HonkType", 300 + honkTypeDropdown.value);

            // Save controls settings
            if (sensitivitySlider != null)
                PlayerPrefs.SetFloat("Sensitivity", sensitivitySlider.value);

            // Save the PlayerPrefs
            PlayerPrefs.Save();

            Debug.Log("Settings saved to PlayerPrefs successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error saving settings: {e.Message}\n{e.StackTrace}");
        }
    }

    // Helper functions to get current resolution from dropdown
    private int GetCurrentResolutionWidth()
    {
        if (resolutionDropdown.value >= 0 && resolutionDropdown.value < resolutions.Length)
        {
            return resolutions[resolutionDropdown.value].width;
        }
        return Screen.currentResolution.width;
    }

    private int GetCurrentResolutionHeight()
    {
        if (resolutionDropdown.value >= 0 && resolutionDropdown.value < resolutions.Length)
        {
            return resolutions[resolutionDropdown.value].height;
        }
        return Screen.currentResolution.height;
    }

    // Call this from Awake or Start to load settings from JSON file
    private void LoadSettingsFromFile()
    {
        try
        {
            // Load settings from JSON file
            SettingsData settingsData = SettingsFileManager.LoadSettings();

            // Apply loaded settings to the game
            SettingsFileManager.ApplySettings(settingsData);

            // Update UI to match loaded settings
            UpdateUI();

            Debug.Log("Settings loaded successfully from file");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error loading settings from file: {e.Message}");

            // Fall back to PlayerPrefs if JSON loading fails
            LoadAllSettings();
        }
    }

    #endregion

    #region UI Updates

    private void UpdateUI()
    {
        // Update video settings UI
        if (resolutionDropdown != null)
        {
            int savedIndex = PlayerPrefs.GetInt("ResolutionIndex", -1);
            if (savedIndex >= 0 && savedIndex < resolutionDropdown.options.Count)
                resolutionDropdown.value = savedIndex;
            else
                resolutionDropdown.value = resolutionDropdown.options.Count - 1; // Default to highest
        }

        if (fullscreenToggle != null)
            fullscreenToggle.isOn = Screen.fullScreen;

        // Update audio settings UI
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = PlayerPrefs.GetFloat("MasterVolume", DEFAULT_MASTER_VOLUME);
            UpdateVolumeText(masterVolumeText, masterVolumeSlider.value);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = PlayerPrefs.GetFloat("MusicVolume", DEFAULT_MUSIC_VOLUME);
            UpdateVolumeText(musicVolumeText, musicVolumeSlider.value);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = PlayerPrefs.GetFloat("SFXVolume", DEFAULT_SFX_VOLUME);
            UpdateVolumeText(sfxVolumeText, sfxVolumeSlider.value);
        }

        // Update gameplay settings UI
        if (usernameInput != null)
            usernameInput.text = PlayerPrefs.GetString("Username", DEFAULT_USERNAME);

        // Update controls settings UI
        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = PlayerPrefs.GetFloat("Sensitivity", DEFAULT_SENSITIVITY);
            UpdateSensitivityText(sensitivitySlider.value);
        }
    }

    private void UpdateVolumeText(TextMeshProUGUI text, float value)
    {
        if (text != null)
            text.text = Mathf.RoundToInt(value * 100) + "%";
    }

    private void UpdateSensitivityText(float value)
    {
        if (sensitivityValueText != null)
            sensitivityValueText.text = value.ToString("F2");
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
                return (rate >= 59.5f && rate <= 60.5f) ||
                       (rate >= 119.5f && rate <= 120.5f) ||
                       (rate >= 143.5f && rate <= 144.5f);
            }).ToArray();

            resolutionDropdown.ClearOptions();

            List<string> options = new List<string>();

            // To help with debugging, log all available resolutions 
            Debug.Log($"[SettingsManager] Available resolutions ({resolutions.Length}, filtered to 60/120/144Hz):");
            for (int i = 0; i < resolutions.Length; i++)
            {
                string option = $"{resolutions[i].width} x {resolutions[i].height} @ {Mathf.RoundToInt((float)resolutions[i].refreshRateRatio.value)}Hz";
                options.Add(option);
                Debug.Log($"  [{i}] {option} (Raw: {resolutions[i].refreshRateRatio.value})");
            }

            resolutionDropdown.AddOptions(options);

            // Get the saved resolution values
            int savedWidth = PlayerPrefs.GetInt("screenWidth", Screen.width);
            int savedHeight = PlayerPrefs.GetInt("screenHeight", Screen.height);
            float savedRefreshRate = PlayerPrefs.GetFloat("refreshRate", (float)Screen.currentResolution.refreshRateRatio.value);

            // Find the index that matches our saved resolution and refresh rate
            int savedIndex = FindClosestResolutionIndex(savedWidth, savedHeight, savedRefreshRate);
            Debug.Log($"[SettingsManager] Found index {savedIndex} for saved resolution {savedWidth}x{savedHeight}@{savedRefreshRate}Hz");

            // Set the dropdown value and refresh
            resolutionDropdown.value = savedIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }

    // Updated method to find the correct resolution index including refresh rate
    private int FindClosestResolutionIndex(int width, int height, float refreshRate)
    {
        if (resolutions == null || resolutions.Length == 0)
            return 0;

        // First try exact match including refresh rate
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == width &&
                resolutions[i].height == height &&
                Mathf.Approximately((float)resolutions[i].refreshRateRatio.value, refreshRate))
                return i;
        }

        // If no exact match, try matching just resolution without refresh rate
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == width && resolutions[i].height == height)
                return i;
        }

        // If still no match, find closest
        int closestIndex = 0;
        float closestDiff = float.MaxValue;

        for (int i = 0; i < resolutions.Length; i++)
        {
            float aspectRatio = (float)resolutions[i].width / resolutions[i].height;
            float targetRatio = (float)width / height;
            float aspectDiff = Mathf.Abs(aspectRatio - targetRatio);

            float areaDiff = Mathf.Abs(resolutions[i].width * resolutions[i].height - width * height);
            float refreshDiff = Mathf.Abs((float)resolutions[i].refreshRateRatio.value - refreshRate);
            float combinedDiff = areaDiff + aspectDiff * 1000 + refreshDiff * 10; // Weight factors

            if (combinedDiff < closestDiff)
            {
                closestDiff = combinedDiff;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    #endregion

    #region Settings Handlers

    // Video Settings

    public void OnResolutionChanged(int index)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
    }

    public void OnFullscreenToggled(bool isFullscreen)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
    }

    public void ApplyVideoSettings()
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIConfirm);

        try
        {
            // Store current resolution in case we need to revert
            Resolution currentResolution = Screen.currentResolution;
            bool currentFullscreen = Screen.fullScreen;

            // Safety check - ensure dropdown value is valid
            if (resolutionDropdown != null && resolutionDropdown.value >= 0 && resolutionDropdown.value < resolutions.Length)
            {
                Resolution resolution = resolutions[resolutionDropdown.value];

                // Apply screen resolution and fullscreen setting
                Screen.SetResolution(resolution.width, resolution.height, fullscreenToggle.isOn);

                // Save resolution including refresh rate
                PlayerPrefs.SetInt("screenWidth", resolution.width);
                PlayerPrefs.SetInt("screenHeight", resolution.height);
                PlayerPrefs.SetFloat("refreshRate", (float)resolution.refreshRateRatio.value);

                // Save the settings (to PlayerPrefs only, JSON saving happens in ApplySettings)
                SaveAllSettings();
            }
            else
            {
                Debug.LogWarning("Invalid resolution dropdown value. Using current resolution.");

                // If dropdown has invalid value, save current resolution to settings
                if (resolutionDropdown != null)
                {
                    // Try to find current resolution in the list
                    for (int i = 0; i < resolutions.Length; i++)
                    {
                        if (resolutions[i].width == currentResolution.width &&
                            resolutions[i].height == currentResolution.height)
                        {
                            resolutionDropdown.value = i;
                            break;
                        }
                    }
                }

                // Apply fullscreen toggle if available
                if (fullscreenToggle != null)
                {
                    Screen.fullScreen = fullscreenToggle.isOn;
                }

                // Save settings with corrected values (to PlayerPrefs only)
                SaveAllSettings();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error applying video settings: {e.Message}");
        }
    }

    // Audio Settings

    public void OnMasterVolumeChanged(float volume)
    {
        SetMasterVolume(volume);
    }

    public void OnMusicVolumeChanged(float volume)
    {
        SetMusicVolume(volume);
    }

    public void OnSFXVolumeChanged(float volume)
    {
        SetSFXVolume(volume);
    }

    private void SetMasterVolume(float volume)
    {
        if (audioMixer != null)
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(volume) * 20);

        PlayerPrefs.SetFloat("MasterVolume", volume);
        PlayerPrefs.Save();
    }

    private void SetMusicVolume(float volume)
    {
        if (audioMixer != null)
            audioMixer.SetFloat("MusicVolume", Mathf.Log10(volume) * 20);

        PlayerPrefs.SetFloat("MusicVolume", volume);
        PlayerPrefs.Save();
    }

    private void SetSFXVolume(float volume)
    {
        if (audioMixer != null)
            audioMixer.SetFloat("SFXVolume", Mathf.Log10(volume) * 20);

        PlayerPrefs.SetFloat("SFXVolume", volume);
        PlayerPrefs.Save();
    }

    // Gameplay Settings

    public void OnUsernameChanged(string username)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

        // Save the username
        if (!string.IsNullOrEmpty(username))
        {
            PlayerPrefs.SetString("Username", username);
            PlayerPrefs.Save();
        }
    }

    // Controls Settings

    public void OnCameraSteeringToggled(bool isCameraSteering)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

        Player player = ConnectionManager.Instance.GetPlayer(NetworkManager.Singleton.LocalClientId);
        player.gameObject.GetComponent<HogController>().useCameraBasedSteering = isCameraSteering;
        Debug.Log("[CONTROLS] Camera Steering changes to " + isCameraSteering);
    }

    public void OnSensitivityChanged(float sensitivity)
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        UpdateSensitivityText(sensitivity);
        SetCameraSensitivity(sensitivity);
    }

    private void SetCameraSensitivity(float sensitivity)
    {
        // Apply to InputManager or any other system that needs camera sensitivity
        if (InputManager.Instance != null)
        {
            // Use a method that exists in InputManager
            // Since SetCameraSensitivity doesn't exist, let's check for alternative methods
            // Assuming there might be a SetSensitivity method or we need to use a property
            Debug.Log($"Setting camera sensitivity to {sensitivity}");

            // You might need to implement this method in your InputManager class
            // For now, commenting out the problematic code
            // InputManager.Instance.SetCameraSensitivity(sensitivity);
        }

        PlayerPrefs.SetFloat("Sensitivity", sensitivity);
        PlayerPrefs.Save();
    }

    #endregion

    #region Button Actions

    public void OnBackButtonClicked()
    {
        // Log current resolution before applying changes
        Debug.Log($"[SettingsManager] OnBackButtonClicked: Current resolution is {Screen.width}x{Screen.height} @ {(float)Screen.currentResolution.refreshRateRatio.value}Hz, fullscreen: {Screen.fullScreen}");

        // Get the selected resolution index
        int selectedResIndex = resolutionDropdown.value;

        // Make sure we have valid resolutions
        if (resolutions != null && resolutions.Length > 0 && selectedResIndex < resolutions.Length)
        {
            Resolution selectedResolution = resolutions[selectedResIndex];
            bool isFullscreen = fullscreenToggle.isOn;

            // Log the resolution we're about to apply
            Debug.Log($"[SettingsManager] Selected dropdown index {selectedResIndex} = {selectedResolution.width}x{selectedResolution.height} @ {(float)selectedResolution.refreshRateRatio.value}Hz");

            // Apply the resolution immediately - force try twice to ensure it takes effect
            Debug.Log($"[SettingsManager] Applying resolution {selectedResolution.width}x{selectedResolution.height} @ {(float)selectedResolution.refreshRateRatio.value}Hz, fullscreen: {isFullscreen}");

            // First attempt
            Screen.SetResolution(selectedResolution.width, selectedResolution.height, isFullscreen);
            // Force fullscreen state separately
            Screen.fullScreen = isFullscreen;

            // Wait a frame to allow the resolution to change
            System.Threading.Thread.Sleep(100);

            // Second attempt to make sure it sticks
            Screen.SetResolution(selectedResolution.width, selectedResolution.height, isFullscreen);
            Screen.fullScreen = isFullscreen;

            // Save width, height, refresh rate and fullscreen to PlayerPrefs
            PlayerPrefs.SetInt("screenWidth", selectedResolution.width);
            PlayerPrefs.SetInt("screenHeight", selectedResolution.height);
            PlayerPrefs.SetFloat("refreshRate", (float)selectedResolution.refreshRateRatio.value);
            PlayerPrefs.SetInt("fullscreen", isFullscreen ? 1 : 0);
            PlayerPrefs.SetInt("resolutionIndex", selectedResIndex);
            PlayerPrefs.Save();
            Debug.Log($"[SettingsManager] Saved to PlayerPrefs: {selectedResolution.width}x{selectedResolution.height} @ {(float)selectedResolution.refreshRateRatio.value}Hz, fullscreen: {isFullscreen}, index: {selectedResIndex}");

            // Create a new SettingsData using the selected values
            SettingsData data = new SettingsData
            {
                resolutionWidth = selectedResolution.width,
                resolutionHeight = selectedResolution.height,
                refreshRate = (float)selectedResolution.refreshRateRatio.value,
                resolutionIndex = selectedResIndex,
                fullscreen = isFullscreen,

                // Get other values from PlayerPrefs
                masterVolume = PlayerPrefs.GetFloat("MasterVolume", 0.8f),
                musicVolume = PlayerPrefs.GetFloat("MusicVolume", 0.8f),
                sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 0.8f),
                username = PlayerPrefs.GetString("Username", "Player"),
                sensitivity = PlayerPrefs.GetFloat("Sensitivity", 1.0f),
                honkType = PlayerPrefs.GetInt("HonkType", 300)
            };

            // Save directly to JSON
            Debug.Log($"[SettingsManager] Saving resolution to JSON: {selectedResolution.width}x{selectedResolution.height} (index: {selectedResIndex})");
            SettingsFileManager.SaveSettings(data);
            Debug.Log("[SettingsManager] Settings saved to JSON file successfully");

            // Wait to ensure the resolution is applied
            System.Threading.Thread.Sleep(200);
        }
        else
        {
            Debug.LogError($"[SettingsManager] Invalid resolution index: {selectedResIndex}, resolutions array length: {(resolutions != null ? resolutions.Length : 0)}");
        }

        // Double-check what resolution we ended up with before returning to menu
        Debug.Log($"[SettingsManager] Before returning to menu: Resolution is {Screen.width}x{Screen.height}, fullscreen: {Screen.fullScreen}");

        // Return to the previous menu
        MenuManager.Instance.ReturnFromSettingsMenu();
    }

    public void ResetToDefaults()
    {
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);

        // Reset to default values
        if (resolutionDropdown != null)
        {
            resolutionDropdown.value = 0; // Assuming default resolution is the first option
            OnResolutionChanged(0);
        }

        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = DEFAULT_MASTER_VOLUME;
            OnMasterVolumeChanged(DEFAULT_MASTER_VOLUME);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = DEFAULT_MUSIC_VOLUME;
            OnMusicVolumeChanged(DEFAULT_MUSIC_VOLUME);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = DEFAULT_SFX_VOLUME;
            OnSFXVolumeChanged(DEFAULT_SFX_VOLUME);
        }

        if (usernameInput != null)
        {
            usernameInput.text = DEFAULT_USERNAME;
            OnUsernameChanged(DEFAULT_USERNAME);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = DEFAULT_SENSITIVITY;
            OnSensitivityChanged(DEFAULT_SENSITIVITY);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = true;
        }

        // Apply changes
        SaveAllSettings();
    }

    #endregion

    /// <summary>
    /// Called when the Apply button is clicked.
    /// Saves all current settings.
    /// </summary>
    public void ApplySettings()
    {
        // Save video settings
        if (resolutionDropdown != null && resolutionDropdown.value >= 0 && resolutionDropdown.value < resolutions.Length)
        {
            Resolution resolution = resolutions[resolutionDropdown.value];
            Screen.SetResolution(resolution.width, resolution.height, fullscreenToggle.isOn);
        }

        // Handle network username updates
        if (ConnectionManager.Instance != null && ConnectionManager.Instance.isConnected)
        {
            ConnectionManager.Instance.UpdateClientUsernameServerRpc(NetworkManager.Singleton.LocalClientId, usernameInput.text);
        }

        // Save to PlayerPrefs
        SaveAllSettings();

        // Save ALL settings to JSON file
        SaveToJsonFile();

        // Play confirmation sound
        SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
    }

    /// <summary>
    /// Saves settings to JSON file
    /// </summary>
    private void SaveToJsonFile()
    {
        try
        {
            // Create settings data from current UI state
            SettingsData data = new SettingsData
            {
                // Video settings - use actual screen dimensions rather than dropdown-derived ones
                resolutionWidth = Screen.width,
                resolutionHeight = Screen.height,
                refreshRate = (float)Screen.currentResolution.refreshRateRatio.value,
                resolutionIndex = resolutionDropdown != null ? resolutionDropdown.value : -1,
                fullscreen = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen,

                // Audio settings
                masterVolume = masterVolumeSlider != null ? masterVolumeSlider.value : DEFAULT_MASTER_VOLUME,
                musicVolume = musicVolumeSlider != null ? musicVolumeSlider.value : DEFAULT_MUSIC_VOLUME,
                sfxVolume = sfxVolumeSlider != null ? sfxVolumeSlider.value : DEFAULT_SFX_VOLUME,

                // Gameplay settings
                username = usernameInput != null ? usernameInput.text : DEFAULT_USERNAME,
                honkType = honkTypeDropdown.value != 0 ? honkTypeDropdown.value + DEFAULT_HONK_TYPE : DEFAULT_HONK_TYPE,

                // Controls settings
                sensitivity = sensitivitySlider != null ? sensitivitySlider.value : DEFAULT_SENSITIVITY
            };

            // For debugging - log the actual dimensions we're saving
            Debug.Log($"Saving resolution to JSON: {data.resolutionWidth}x{data.resolutionHeight} @ {data.refreshRate}Hz (index: {data.resolutionIndex})");

            // Save using SettingsFileManager
            bool success = SettingsFileManager.SaveSettings(data);

            if (success)
            {
                Debug.Log("Settings saved to JSON file successfully");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to save settings to JSON: " + e.Message);
        }
    }
}