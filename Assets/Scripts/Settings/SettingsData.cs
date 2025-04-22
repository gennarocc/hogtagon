using System;
using UnityEngine;

[Serializable]
public class SettingsData
{
    // Video settings
    public int resolutionWidth = 1920;
    public int resolutionHeight = 1080;
    public bool fullscreen = true;
    public float fieldOfView = 90f;
    
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
            fullscreen = true,
            fieldOfView = 90f,
            masterVolume = 0.8f,
            musicVolume = 0.8f,
            sfxVolume = 0.8f,
            username = "Player",
            sensitivity = 1.0f
        };
    }
} 