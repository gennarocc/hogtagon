using UnityEngine;

/// <summary>
/// Base class for settings tab content panels.
/// Inherit from this for each specific settings category (video, audio, etc).
/// </summary>
public abstract class TabContent : MonoBehaviour
{
    [SerializeField] protected SettingsManager settingsManager;

    protected virtual void Awake()
    {
        // Find settings manager if not directly assigned
        if (settingsManager == null)
        {
            settingsManager = GetComponentInParent<SettingsManager>();
            if (settingsManager == null)
                settingsManager = FindObjectOfType<SettingsManager>();
        }
    }

    protected virtual void OnEnable()
    {
        // Called when this tab becomes active
        InitializeUI();
    }

    /// <summary>
    /// Initialize UI elements with current settings values
    /// </summary>
    protected abstract void InitializeUI();

    /// <summary>
    /// Apply any pending changes for this tab's settings
    /// </summary>
    public abstract void ApplySettings();

    /// <summary>
    /// Reset this tab's settings to defaults
    /// </summary>
    public abstract void ResetToDefaults();
} 