using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsPanel : BasePanel
{
    [Header("Audio Settings")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;

    [Header("Graphics Settings")]
    [SerializeField] private TMP_Dropdown qualityDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle vsyncToggle;

    [Header("Input Settings")]
    [SerializeField] private Slider mouseSensitivitySlider;
    [SerializeField] private Toggle invertYAxisToggle;
    [SerializeField] private Toggle invertXAxisToggle;

    private MenuManager menuManager;
    private SettingsManager settingsManager;

    protected override void Awake()
    {
        base.Awake();
        menuManager = GetComponentInParent<MenuManager>();
        settingsManager = SettingsManager.Instance;
        InitializeUI();
    }

    private void InitializeUI()
    {
        // Audio Settings
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = settingsManager.MasterVolume;
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = settingsManager.SFXVolume;
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = settingsManager.MusicVolume;
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        // Graphics Settings
        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(settingsManager.GetQualityLevels());
            qualityDropdown.value = QualitySettings.GetQualityLevel();
            qualityDropdown.onValueChanged.AddListener(OnQualityChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.ClearOptions();
            resolutionDropdown.AddOptions(settingsManager.GetResolutions());
            resolutionDropdown.value = settingsManager.CurrentResolutionIndex;
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        if (vsyncToggle != null)
        {
            vsyncToggle.isOn = QualitySettings.vSyncCount > 0;
            vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);
        }

        // Input Settings
        if (mouseSensitivitySlider != null)
        {
            mouseSensitivitySlider.value = settingsManager.MouseSensitivity;
            mouseSensitivitySlider.onValueChanged.AddListener(OnMouseSensitivityChanged);
        }

        if (invertYAxisToggle != null)
        {
            invertYAxisToggle.isOn = settingsManager.InvertYAxis;
            invertYAxisToggle.onValueChanged.AddListener(OnInvertYAxisChanged);
        }

        if (invertXAxisToggle != null)
        {
            invertXAxisToggle.isOn = settingsManager.InvertXAxis;
            invertXAxisToggle.onValueChanged.AddListener(OnInvertXAxisChanged);
        }
    }

    protected override void OnPanelShown()
    {
        base.OnPanelShown();
        InputManager.Instance.SwitchToUIMode();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        InitializeUI(); // Refresh UI values
    }

    #region Audio Settings
    private void OnMasterVolumeChanged(float value)
    {
        settingsManager.SetMasterVolume(value);
    }

    private void OnSFXVolumeChanged(float value)
    {
        settingsManager.SetSFXVolume(value);
    }

    private void OnMusicVolumeChanged(float value)
    {
        settingsManager.SetMusicVolume(value);
    }
    #endregion

    #region Graphics Settings
    private void OnQualityChanged(int index)
    {
        settingsManager.SetQualityLevel(index);
    }

    private void OnFullscreenChanged(bool isFullscreen)
    {
        settingsManager.SetFullscreen(isFullscreen);
    }

    private void OnResolutionChanged(int index)
    {
        settingsManager.SetResolution(index);
    }

    private void OnVSyncChanged(bool enabled)
    {
        settingsManager.SetVSync(enabled);
    }
    #endregion

    #region Input Settings
    private void OnMouseSensitivityChanged(float value)
    {
        settingsManager.SetMouseSensitivity(value);
    }

    private void OnInvertYAxisChanged(bool value)
    {
        settingsManager.SetInvertYAxis(value);
    }

    private void OnInvertXAxisChanged(bool value)
    {
        settingsManager.SetInvertXAxis(value);
    }
    #endregion

    public void OnBackButtonClicked()
    {
        Hide();
        if (menuManager.gameIsPaused)
            menuManager.pauseMenuPanel.Show();
        else
            menuManager.ShowMainMenu();
    }

    public void OnApplyButtonClicked()
    {
        settingsManager.ApplySettings();
    }

    public void OnDefaultsButtonClicked()
    {
        settingsManager.ResetToDefaults();
        InitializeUI();
    }
} 