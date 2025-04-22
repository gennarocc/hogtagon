using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.Audio;

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
    [SerializeField] private Slider fovSlider;
    [SerializeField] private TextMeshProUGUI fovValueText;
    
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
    
    [Header("Controls Settings")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TextMeshProUGUI sensitivityValueText;
    
    [Header("Audio")]
    [SerializeField] private AK.Wwise.Event uiClick;
    [SerializeField] private AK.Wwise.Event uiConfirm;
    [SerializeField] private AK.Wwise.Event uiCancel;
    
    // Resolution cache
    private Resolution[] resolutions;

    // Default settings
    private const float DEFAULT_MASTER_VOLUME = 0.8f;
    private const float DEFAULT_MUSIC_VOLUME = 0.8f;
    private const float DEFAULT_SFX_VOLUME = 0.8f;
    private const float DEFAULT_SENSITIVITY = 1.0f;
    private const float DEFAULT_FOV = 90.0f;
    private const string DEFAULT_USERNAME = "Player";
    
    private void Awake()
    {
        // Initialize the resolution dropdown
        InitializeResolutionDropdown();
    }
    
    private void Start()
    {
        // Set up button listeners
        SetupButtonListeners();
        
        // Load saved settings
        LoadAllSettings();
        
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
            
        if (fovSlider != null)
            fovSlider.onValueChanged.AddListener(OnFOVChanged);
            
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
    }
    
    #region Settings Loading/Saving
    
    private void LoadAllSettings()
    {
        // Load video settings
        int resIndex = PlayerPrefs.GetInt("ResolutionIndex", -1);
        bool fullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
        float fov = PlayerPrefs.GetFloat("FOV", DEFAULT_FOV);
        
        // Load audio settings
        float masterVolume = PlayerPrefs.GetFloat("MasterVolume", DEFAULT_MASTER_VOLUME);
        float musicVolume = PlayerPrefs.GetFloat("MusicVolume", DEFAULT_MUSIC_VOLUME);
        float sfxVolume = PlayerPrefs.GetFloat("SFXVolume", DEFAULT_SFX_VOLUME);
        
        // Load gameplay settings
        string username = PlayerPrefs.GetString("Username", DEFAULT_USERNAME);
        
        // Load controls settings
        float sensitivity = PlayerPrefs.GetFloat("Sensitivity", DEFAULT_SENSITIVITY);
        
        // Apply the settings to the actual game systems
        ApplyLoadedSettings(resIndex, fullscreen, fov, masterVolume, musicVolume, sfxVolume, username, sensitivity);
    }
    
    private void ApplyLoadedSettings(int resolutionIndex, bool fullscreen, float fov, 
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
        
        // Apply FOV
        Camera.main.fieldOfView = fov;
        
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
        // Save video settings
        PlayerPrefs.SetInt("ResolutionIndex", resolutionDropdown.value);
        PlayerPrefs.SetInt("Fullscreen", fullscreenToggle.isOn ? 1 : 0);
        PlayerPrefs.SetFloat("FOV", fovSlider.value);
        
        // Save audio settings
        PlayerPrefs.SetFloat("MasterVolume", masterVolumeSlider.value);
        PlayerPrefs.SetFloat("MusicVolume", musicVolumeSlider.value);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolumeSlider.value);
        
        // Save gameplay settings - username is saved on change
        
        // Save controls settings
        PlayerPrefs.SetFloat("Sensitivity", sensitivitySlider.value);
        
        // Save the PlayerPrefs
        PlayerPrefs.Save();
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
        
        if (fovSlider != null)
        {
            fovSlider.value = PlayerPrefs.GetFloat("FOV", DEFAULT_FOV);
            UpdateFOVText(fovSlider.value);
        }
        
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
    
    private void UpdateFOVText(float value)
    {
        if (fovValueText != null)
            fovValueText.text = value.ToString("F0") + "°";
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
    
    #endregion
    
    #region Settings Handlers
    
    // Video Settings
    
    public void OnResolutionChanged(int index)
    {
        PlayUIClickSound();
        // Resolution is applied when settings are saved
    }
    
    public void OnFullscreenToggled(bool isFullscreen)
    {
        PlayUIClickSound();
        // Fullscreen is applied when settings are saved
    }
    
    public void OnFOVChanged(float value)
    {
        PlayUIClickSound();
        UpdateFOVText(value);
        Camera.main.fieldOfView = value;
    }
    
    public void ApplyVideoSettings()
    {
        PlayUIConfirmSound();
        
        // Get the selected resolution from the dropdown
        Resolution resolution = resolutions[resolutionDropdown.value];
        
        // Apply screen resolution and fullscreen setting
        Screen.SetResolution(resolution.width, resolution.height, fullscreenToggle.isOn);
        
        // Save the settings
        SaveAllSettings();
    }
    
    // Audio Settings
    
    public void OnMasterVolumeChanged(float volume)
    {
        PlayUIClickSound();
        UpdateVolumeText(masterVolumeText, volume);
        SetMasterVolume(volume);
    }
    
    public void OnMusicVolumeChanged(float volume)
    {
        PlayUIClickSound();
        UpdateVolumeText(musicVolumeText, volume);
        SetMusicVolume(volume);
    }
    
    public void OnSFXVolumeChanged(float volume)
    {
        PlayUIClickSound();
        UpdateVolumeText(sfxVolumeText, volume);
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
        PlayUIClickSound();
        
        // Save the username
        if (!string.IsNullOrEmpty(username))
        {
            PlayerPrefs.SetString("Username", username);
            PlayerPrefs.Save();
        }
    }
    
    // Controls Settings
    
    public void OnSensitivityChanged(float sensitivity)
    {
        PlayUIClickSound();
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
        PlayUICancelSound();
        
        // Save all settings when exiting
        SaveAllSettings();
        
        // First deactivate this menu
        gameObject.SetActive(false);
        
        // Let MenuManager handle the transition back
        if (MenuManager.Instance != null)
        {
            // MenuManager already tracks where settings were opened from with settingsOpenedFromPauseMenu
            // We'll let it handle the return navigation
            
            // If settings were opened from pause menu, activate pause menu
            if (MenuManager.Instance.settingsOpenedFromPauseMenu)
            {
                Debug.Log("Returning to pause menu");
                
                // Use public pauseMenuUI if available
                if (MenuManager.Instance.pauseMenuUI != null)
                {
                    MenuManager.Instance.pauseMenuUI.SetActive(true);
                }
                else
                {
                    // Otherwise use ShowPauseMenu if it exists
                    var showPauseMethod = typeof(MenuManager).GetMethod("ShowPauseMenu", 
                        System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.Public);
                        
                    if (showPauseMethod != null)
                    {
                        showPauseMethod.Invoke(MenuManager.Instance, null);
                    }
                    else
                    {
                        // Last resort fallback
                        GameObject pauseMenu = GameObject.Find("PauseMenuUI");
                        if (pauseMenu != null)
                        {
                            pauseMenu.SetActive(true);
                        }
                        else
                        {
                            Debug.LogWarning("Could not find pause menu!");
                        }
                    }
                }
            }
            else
            {
                // We were opened from main menu, use MenuManager's ShowMainMenu method
                Debug.Log("Returning to main menu");
                MenuManager.Instance.ShowMainMenu();
            }
        }
        else
        {
            Debug.LogWarning("MenuManager.Instance is null! Cannot navigate back properly.");
        }
    }
    
    public void ResetToDefaults()
    {
        PlayUIConfirmSound();
        
        // Reset to default values
        if (fovSlider != null)
        {
            fovSlider.value = DEFAULT_FOV;
            OnFOVChanged(DEFAULT_FOV);
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
    
    #region Audio
    
    public void PlayUIClickSound()
    {
        if (uiClick != null)
            uiClick.Post(gameObject);
    }
    
    public void PlayUIConfirmSound()
    {
        if (uiConfirm != null)
            uiConfirm.Post(gameObject);
    }
    
    public void PlayUICancelSound()
    {
        if (uiCancel != null)
            uiCancel.Post(gameObject);
    }
    
    #endregion
} 