using System.IO;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Handles saving and loading settings to/from a JSON file
/// </summary>
public static class SettingsFileManager
{
    private const string SETTINGS_FILENAME = "settings.json";

    /// <summary>
    /// Gets the full path to the settings file
    /// </summary>
    public static string SettingsFilePath
    {
        get
        {
            return Path.Combine(Application.persistentDataPath, SETTINGS_FILENAME);
        }
    }

    /// <summary>
    /// Saves settings to a JSON file
    /// </summary>
    /// <param name="settings">The settings to save</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool SaveSettings(SettingsData settings)
    {
        try
        {
            string json = JsonUtility.ToJson(settings, true);
            File.WriteAllText(SettingsFilePath, json);
            Debug.Log($"Settings saved to {SettingsFilePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to save settings: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads settings from a JSON file
    /// </summary>
    /// <returns>The loaded settings, or default settings if the file doesn't exist</returns>
    public static SettingsData LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                SettingsData settings = JsonUtility.FromJson<SettingsData>(json);
                Debug.Log($"Settings loaded from {SettingsFilePath}");
                return settings;
            }
            else
            {
                Debug.Log("Settings file not found. Using defaults.");
                return SettingsData.GetDefaults();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load settings: {e.Message}");
            return SettingsData.GetDefaults();
        }
    }

    /// <summary>
    /// Applies loaded settings to the game
    /// </summary>
    /// <param name="settings">The settings to apply</param>
    public static void ApplySettings(SettingsData settings)
    {
        // Apply resolution and fullscreen
        Screen.SetResolution(settings.resolutionWidth, settings.resolutionHeight, settings.fullscreen);

        // Store values in PlayerPrefs for compatibility with existing code
        PlayerPrefs.SetFloat("MasterVolume", settings.masterVolume);
        PlayerPrefs.SetFloat("MusicVolume", settings.musicVolume);
        PlayerPrefs.SetFloat("SFXVolume", settings.sfxVolume);
        PlayerPrefs.SetString("Username", settings.username);
        PlayerPrefs.SetFloat("Sensitivity", settings.sensitivity);
        PlayerPrefs.SetInt("Fullscreen", settings.fullscreen ? 1 : 0);
        PlayerPrefs.SetFloat("refreshRate", settings.refreshRate);
        if (settings.resolutionIndex >= 0)
        {
            PlayerPrefs.SetInt("ResolutionIndex", settings.resolutionIndex);
        }

        SoundManager.Instance.SetMasterVolume(settings.masterVolume);
        SoundManager.Instance.SetMusicVolume(settings.musicVolume);
        SoundManager.Instance.SetSfxVolume(settings.sfxVolume);

        PlayerPrefs.Save();


        // Log success
        Debug.Log("Settings applied successfully");
    }

    /// <summary>
    /// Creates a SettingsData object from the current game state and PlayerPrefs
    /// </summary>
    /// <returns>A SettingsData object with the current settings</returns>
    public static SettingsData CreateFromCurrentSettings()
    {
        SettingsData data = new SettingsData
        {
            // Use actual game window resolution, not monitor resolution
            resolutionWidth = Screen.width,
            resolutionHeight = Screen.height,
            resolutionIndex = PlayerPrefs.GetInt("ResolutionIndex", -1),
            fullscreen = Screen.fullScreen,
            refreshRate = PlayerPrefs.GetFloat("refreshRate", 60f),
            masterVolume = PlayerPrefs.GetFloat("MasterVolume", 0.8f),
            musicVolume = PlayerPrefs.GetFloat("MusicVolume", 0.8f),
            sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 0.8f),
            username = PlayerPrefs.GetString("Username", "Player"),
            sensitivity = PlayerPrefs.GetFloat("Sensitivity", 1.0f)
        };

        Debug.Log($"CreateFromCurrentSettings: Using resolution {data.resolutionWidth}x{data.resolutionHeight} @ {data.refreshRate}Hz");

        return data;
    }
}