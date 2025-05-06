using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Handles the footer buttons in the settings menu
/// </summary>
public class SettingsFooter : MonoBehaviour
{
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TextMeshProUGUI statusText;

    private SettingsManager settingsManager;

    private void Awake()
    {
        // Find the settings manager
        settingsManager = GetComponentInParent<SettingsManager>();

        if (settingsManager == null)
        {
            Debug.LogError("SettingsFooter: Could not find SettingsManager in parent hierarchy");
        }

        // Set up button listeners
        if (applyButton != null)
        {
            applyButton.onClick.AddListener(OnApplyClicked);
        }

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(OnResetClicked);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackClicked);
        }

        // Clear status text
        if (statusText != null)
        {
            statusText.text = "";
        }
    }

    private void OnApplyClicked()
    {
        if (settingsManager != null)
        {
            // Call the apply settings method
            settingsManager.ApplySettings();

            // Show success message
            ShowStatusMessage("Settings saved successfully!", 2f);

            // Play confirmation sound
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }
    }

    private void OnResetClicked()
    {
        if (settingsManager != null)
        {
            // Call the reset to defaults method
            settingsManager.ResetToDefaults();

            // Show message
            ShowStatusMessage("Settings reset to defaults", 2f);

            // Play confirmation sound
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }
    }

    private void OnBackClicked()
    {
        if (settingsManager != null)
        {
            // Call the back button method
            settingsManager.OnBackButtonClicked();

            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UICancel);
        }
    }

    private void ShowStatusMessage(string message, float duration)
    {
        if (statusText != null)
        {
            // Set the message
            statusText.text = message;

            // Clear it after a delay
            Invoke("ClearStatusMessage", duration);
        }
    }

    private void ClearStatusMessage()
    {
        if (statusText != null)
        {
            statusText.text = "";
        }
    }
}