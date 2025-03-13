using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SettingsManager : Singleton<SettingsManager>
{
    private const string MASTER_VOLUME_KEY = "MasterVolume";
    private const string SFX_VOLUME_KEY = "SFXVolume";
    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string QUALITY_LEVEL_KEY = "QualityLevel";
    private const string FULLSCREEN_KEY = "Fullscreen";
    private const string RESOLUTION_INDEX_KEY = "ResolutionIndex";
    private const string VSYNC_KEY = "VSync";
    private const string MOUSE_SENSITIVITY_KEY = "MouseSensitivity";
    private const string INVERT_Y_AXIS_KEY = "InvertYAxis";
    private const string INVERT_X_AXIS_KEY = "InvertXAxis";

    private Resolution[] resolutions;
    private int currentResolutionIndex;

    #region Properties
    public float MasterVolume => PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, 1f);
    public float SFXVolume => PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 1f);
    public float MusicVolume => PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
    public float MouseSensitivity => PlayerPrefs.GetFloat(MOUSE_SENSITIVITY_KEY, 1f);
    public bool InvertYAxis => PlayerPrefs.GetInt(INVERT_Y_AXIS_KEY, 0) == 1;
    public bool InvertXAxis => PlayerPrefs.GetInt(INVERT_X_AXIS_KEY, 0) == 1;
    public int CurrentResolutionIndex => currentResolutionIndex;
    #endregion

    protected override void Awake()
    {
        base.Awake();
        InitializeResolutions();
        LoadSettings();
    }

    private void InitializeResolutions()
    {
        resolutions = Screen.resolutions
            .Where(r => r.refreshRateRatio.Equals(Screen.currentResolution.refreshRateRatio))
            .OrderByDescending(r => r.width)
            .ThenByDescending(r => r.height)
            .ToArray();

        currentResolutionIndex = PlayerPrefs.GetInt(RESOLUTION_INDEX_KEY, 0);
        if (currentResolutionIndex >= resolutions.Length)
            currentResolutionIndex = 0;
    }

    private void LoadSettings()
    {
        // Apply Quality Settings
        QualitySettings.SetQualityLevel(PlayerPrefs.GetInt(QUALITY_LEVEL_KEY, QualitySettings.GetQualityLevel()));

        // Apply Resolution and Fullscreen
        Screen.fullScreen = PlayerPrefs.GetInt(FULLSCREEN_KEY, 1) == 1;
        if (resolutions.Length > 0 && currentResolutionIndex < resolutions.Length)
        {
            var resolution = resolutions[currentResolutionIndex];
            Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreen);
        }

        // Apply VSync
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSYNC_KEY, 1);

        // Apply Audio Settings to Wwise
        float masterVolume = MasterVolume;
        float sfxVolume = SFXVolume;
        float musicVolume = MusicVolume;

        // Set Wwise RTPC values
        AkSoundEngine.SetRTPCValue("Master_Volume", masterVolume * 100f);
        AkSoundEngine.SetRTPCValue("SFX_Volume", sfxVolume * 100f);
        AkSoundEngine.SetRTPCValue("Music_Volume", musicVolume * 100f);
    }

    #region Audio Settings
    public void SetMasterVolume(float value)
    {
        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, value);
        AkSoundEngine.SetRTPCValue("Master_Volume", value * 100f);
        PlayerPrefs.Save();
    }

    public void SetSFXVolume(float value)
    {
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, value);
        AkSoundEngine.SetRTPCValue("SFX_Volume", value * 100f);
        PlayerPrefs.Save();
    }

    public void SetMusicVolume(float value)
    {
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, value);
        AkSoundEngine.SetRTPCValue("Music_Volume", value * 100f);
        PlayerPrefs.Save();
    }
    #endregion

    #region Graphics Settings
    public List<string> GetQualityLevels()
    {
        return QualitySettings.names.ToList();
    }

    public void SetQualityLevel(int index)
    {
        QualitySettings.SetQualityLevel(index);
        PlayerPrefs.SetInt(QUALITY_LEVEL_KEY, index);
        PlayerPrefs.Save();
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        PlayerPrefs.SetInt(FULLSCREEN_KEY, isFullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public List<string> GetResolutions()
    {
        return resolutions.Select(r => $"{r.width}x{r.height}").ToList();
    }

    public void SetResolution(int index)
    {
        if (index < 0 || index >= resolutions.Length)
            return;

        currentResolutionIndex = index;
        var resolution = resolutions[index];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreen);
        PlayerPrefs.SetInt(RESOLUTION_INDEX_KEY, index);
        PlayerPrefs.Save();
    }

    public void SetVSync(bool enabled)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        PlayerPrefs.SetInt(VSYNC_KEY, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }
    #endregion

    #region Input Settings
    public void SetMouseSensitivity(float value)
    {
        PlayerPrefs.SetFloat(MOUSE_SENSITIVITY_KEY, value);
        PlayerPrefs.Save();
    }

    public void SetInvertYAxis(bool value)
    {
        PlayerPrefs.SetInt(INVERT_Y_AXIS_KEY, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetInvertXAxis(bool value)
    {
        PlayerPrefs.SetInt(INVERT_X_AXIS_KEY, value ? 1 : 0);
        PlayerPrefs.Save();
    }
    #endregion

    public void ApplySettings()
    {
        LoadSettings();
    }

    public void ResetToDefaults()
    {
        // Audio Defaults
        SetMasterVolume(1f);
        SetSFXVolume(1f);
        SetMusicVolume(1f);

        // Graphics Defaults
        SetQualityLevel(QualitySettings.names.Length - 1); // Highest quality
        SetFullscreen(true);
        SetResolution(0); // Highest available resolution
        SetVSync(true);

        // Input Defaults
        SetMouseSensitivity(1f);
        SetInvertYAxis(false);
        SetInvertXAxis(false);

        LoadSettings();
    }
} 