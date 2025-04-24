using System;
using UnityEngine;

/// <summary>
/// Data class for storing all game settings
/// </summary>
[Serializable]
public class SettingsData
{
    // Video settings
    public int resolutionWidth = 1920;
    public int resolutionHeight = 1080;
    public int resolutionIndex = -1;
    public bool fullscreen = true;
    public float refreshRate = 60f; // Stores the display refresh rate in Hz
    
    // Audio settings
    public float masterVolume = 0.8f;
    public float musicVolume = 0.8f;
    public float sfxVolume = 0.8f;
    
    // Gameplay settings
    public string username = "Player";
    
    // Controls settings
    public float sensitivity = 1.0f;
    
    // Create a default settings object
    public static SettingsData GetDefaults()
    {
        return new SettingsData
        {
            resolutionWidth = 1920,
            resolutionHeight = 1080,
            resolutionIndex = -1,
            fullscreen = true,
            refreshRate = 60f,
            masterVolume = 0.8f,
            musicVolume = 0.8f,
            sfxVolume = 0.8f,
            username = "Player",
            sensitivity = 1.0f
        };
    }
} 