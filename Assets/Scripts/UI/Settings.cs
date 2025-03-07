using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.UI;

public class Settings : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] public Slider cameraSensitivity;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Button applyButton;
    
    private Resolution[] resolutions;
    private List<Resolution> filteredResolutions;
    
    // Store pending changes
    private int pendingResolutionIndex;
    private bool pendingFullscreenState;
    private bool hasUnappliedChanges = false;

    [Header("Wwise")]
    [SerializeField] public AK.Wwise.RTPC MasterVolume;
    [SerializeField] public AK.Wwise.RTPC MusicVolume;
    [SerializeField] public AK.Wwise.RTPC SfxVolume;
    [SerializeField] private AK.Wwise.Event uiClick;
    [SerializeField] private AK.Wwise.Event uiConfirm;
    [SerializeField] private AK.Wwise.Event uiCancel;

    void Start()
    {
        // Register listeners for changes
        resolutionDropdown.onValueChanged.AddListener(OnResolutionSelected);
        if (fullscreenToggle != null)
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
        if (applyButton != null)
            applyButton.onClick.AddListener(ApplyVideoSettings);
            
        // Initialize settings
        LoadResolutions();
        
        // Set initial pending values
        pendingFullscreenState = Screen.fullScreen;
        if (fullscreenToggle != null)
            fullscreenToggle.isOn = pendingFullscreenState;
            
        // Initially disable apply button if no changes
        if (applyButton != null)
            applyButton.interactable = false;
            
        // Initialize volume labels with current values
        if (masterVolumeText != null && MasterVolume != null)
            UpdateVolumeLabel(masterVolumeText, "Master Volume", MasterVolume.GetGlobalValue());
            
        if (musicVolumeText != null && MusicVolume != null)
            UpdateVolumeLabel(musicVolumeText, "Music Volume", MusicVolume.GetGlobalValue());
            
        if (sfxVolumeText != null && SfxVolume != null)
            UpdateVolumeLabel(sfxVolumeText, "SFX Volume", SfxVolume.GetGlobalValue());
    }

    void OnEnable()
    {
        // Important: Update UI when menu opens
        if (filteredResolutions != null && filteredResolutions.Count > 0)
        {
            // When opening the menu, update UI to match actual current settings
            LoadCurrentResolution();
            
            if (fullscreenToggle != null)
                fullscreenToggle.isOn = Screen.fullScreen;
                
            // Reset pending values to match current system state
            pendingFullscreenState = Screen.fullScreen;
            pendingResolutionIndex = resolutionDropdown.value;
            
            // Reset the apply button state
            if (applyButton != null)
                applyButton.interactable = false;
                
            hasUnappliedChanges = false;
        }
    }

    private void LoadResolutions()
    {
        // Get all possible resolutions
        resolutions = Screen.resolutions;
        filteredResolutions = new List<Resolution>();

        // Clear existing options
        resolutionDropdown.ClearOptions();

        // With refreshRate being obsolete, we'll just filter by unique resolution sizes
        // and use the highest refresh rate available for each resolution
        List<string> options = new List<string>();
        HashSet<string> addedResolutions = new HashSet<string>();
        Dictionary<string, Resolution> bestResolutions = new Dictionary<string, Resolution>();

        // Find the highest refresh rate for each resolution size
        for (int i = 0; i < resolutions.Length; i++)
        {
            string resKey = $"{resolutions[i].width} x {resolutions[i].height}";
            
            if (!bestResolutions.ContainsKey(resKey))
            {
                bestResolutions[resKey] = resolutions[i];
            }
            // Note: We're not comparing refresh rates since it's obsolete
            // In newer Unity versions, you might want to consider Screen.resolutions[i].refreshRateRatio instead
        }

        // Add the unique resolutions to our filtered list
        foreach (var resolution in bestResolutions.Values)
        {
            string option = $"{resolution.width} x {resolution.height}";
            options.Add(option);
            filteredResolutions.Add(resolution);
        }
        
        // Sort options by resolution (ascending)
        filteredResolutions.Sort((a, b) => (a.width * a.height).CompareTo(b.width * b.height));
        options.Sort((a, b) => 
        {
            int aWidth = int.Parse(a.Split('x')[0].Trim());
            int bWidth = int.Parse(b.Split('x')[0].Trim());
            int aHeight = int.Parse(a.Split('x')[1].Trim());
            int bHeight = int.Parse(b.Split('x')[1].Trim());
            return (aWidth * aHeight).CompareTo(bWidth * bHeight);
        });

        // Add options to dropdown
        resolutionDropdown.AddOptions(options);
        
        // Now load current resolution after populating dropdown
        LoadCurrentResolution();
    }

    private void LoadCurrentResolution()
    {
        // Find current resolution in our filtered list
        int currentWidth = Screen.width;
        int currentHeight = Screen.height;
        
        // Default to first option if no match is found
        int bestMatchIndex = 0;
        
        for (int i = 0; i < filteredResolutions.Count; i++)
        {
            if (filteredResolutions[i].width == currentWidth &&
                filteredResolutions[i].height == currentHeight)
            {
                bestMatchIndex = i;
                break;
            }
        }

        resolutionDropdown.value = bestMatchIndex;
        pendingResolutionIndex = bestMatchIndex;
        resolutionDropdown.RefreshShownValue();
    }

    // Called when resolution dropdown changes
    public void OnResolutionSelected(int resolutionIndex)
    {
        pendingResolutionIndex = resolutionIndex;
        hasUnappliedChanges = true;
        if (applyButton != null)
            applyButton.interactable = true;
    }
    
    // Called when fullscreen toggle changes
    public void OnFullscreenToggled(bool isFullscreen)
    {
        pendingFullscreenState = isFullscreen;
        hasUnappliedChanges = true;
        if (applyButton != null)
            applyButton.interactable = true;
    }
    
    // Called when Apply button is clicked
    public void ApplyVideoSettings()
    {
        if (hasUnappliedChanges)
        {
            Resolution selectedResolution = filteredResolutions[pendingResolutionIndex];
            Screen.SetResolution(selectedResolution.width, selectedResolution.height, pendingFullscreenState);
            SaveResolutionPreference(selectedResolution);
            SaveFullscreenPreference(pendingFullscreenState);
            
            hasUnappliedChanges = false;
            if (applyButton != null)
                applyButton.interactable = false;
                
            ButtonConfirmAudio();
        }
    }

    private void SaveResolutionPreference(Resolution resolution)
    {
        PlayerPrefs.SetInt("ResolutionWidth", resolution.width);
        PlayerPrefs.SetInt("ResolutionHeight", resolution.height);
        PlayerPrefs.Save();
    }
    
    private void SaveFullscreenPreference(bool isFullscreen)
    {
        PlayerPrefs.SetInt("Fullscreen", isFullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void LoadSavedResolution()
    {
        if (PlayerPrefs.HasKey("ResolutionWidth") && PlayerPrefs.HasKey("ResolutionHeight"))
        {
            int savedWidth = PlayerPrefs.GetInt("ResolutionWidth");
            int savedHeight = PlayerPrefs.GetInt("ResolutionHeight");
            bool isFullscreen = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
            Screen.SetResolution(savedWidth, savedHeight, isFullscreen);
        }
    }

    public void SetCameraSensitivty()
    {
        var player = ConnectionManager.instance.GetPlayer(NetworkManager.Singleton.LocalClientId);
        if (player != null && player.mainCamera != null)
        {
            player.mainCamera.m_XAxis.m_MaxSpeed = cameraSensitivity.value * 100f;
            player.mainCamera.m_YAxis.m_MaxSpeed = cameraSensitivity.value * 1f;
        }
    }

    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;
    
    public void SetMasterVolume(float vol)
    {
        MasterVolume.SetGlobalValue(vol);
        UpdateVolumeLabel(masterVolumeText, "Master Volume", vol);
    }

    public void SetMusicVolume(float vol)
    {
        MusicVolume.SetGlobalValue(vol);
        UpdateVolumeLabel(musicVolumeText, "Music Volume", vol);
    }

    public void SetSfxVolume(float vol)
    {
        SfxVolume.SetGlobalValue(vol);
        UpdateVolumeLabel(sfxVolumeText, "SFX Volume", vol);
    }
    
    private void UpdateVolumeLabel(TextMeshProUGUI label, string labelName, float value)
    {
        if (label != null)
        {
            // Convert slider value to decibels (assuming 0-100 scale)
            // You can adjust this calculation based on your actual volume range
            int dbValue = Mathf.RoundToInt(value); 
            label.text = $"{labelName}: {dbValue}dB";
        }
    }

    public void ButtonClickAudio()
    {
        uiClick.Post(gameObject);
    }

    public void ButtonConfirmAudio()
    {
        uiConfirm.Post(gameObject);
    }

    public void ButtonCancelAudio()
    {
        uiCancel.Post(gameObject);
    }
}