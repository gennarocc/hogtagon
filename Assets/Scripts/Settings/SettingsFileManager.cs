using System.IO;
using UnityEngine;

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
        
        // Apply FOV if main camera exists
        if (Camera.main != null)
        {
            Camera.main.fieldOfView = settings.fieldOfView;
        }
        
        // Store values in PlayerPrefs for compatibility with existing code
        PlayerPrefs.SetFloat("MasterVolume", settings.masterVolume);
        PlayerPrefs.SetFloat("MusicVolume", settings.musicVolume);
        PlayerPrefs.SetFloat("SFXVolume", settings.sfxVolume);
        PlayerPrefs.SetString("Username", settings.username);
        PlayerPrefs.SetFloat("Sensitivity", settings.sensitivity);
        PlayerPrefs.SetFloat("FOV", settings.fieldOfView);
        PlayerPrefs.SetInt("Fullscreen", settings.fullscreen ? 1 : 0);
        PlayerPrefs.Save();
        
        // Log success
        Debug.Log("Settings applied successfully");
    }
} 