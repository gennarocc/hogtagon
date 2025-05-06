using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Handles the Controls settings tab content
/// </summary>
public class ControlsTabContent : TabContent
{
    [Header("Controls Settings")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TextMeshProUGUI sensitivityValueText;
    [SerializeField] private Toggle cameraControlToggle;
    [SerializeField] private TextMeshProUGUI cameraControlText;

    // Default value
    private const float DEFAULT_SENSITIVITY = 1.0f;
    private const bool DEFAULT_CAMERA_STEERING = false;

    protected override void Awake()
    {
        base.Awake();

        // Set up slider listener
        if (sensitivitySlider != null)
        {
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }
    }

    protected override void InitializeUI()
    {
        // Update UI with current settings
        if (sensitivitySlider != null)
        {
            float currentSensitivity = PlayerPrefs.GetFloat("Sensitivity", DEFAULT_SENSITIVITY);
            sensitivitySlider.value = currentSensitivity;
            UpdateSensitivityText(currentSensitivity);
        }
    }

    public void OnSensitivityChanged(float sensitivity)
    {
        // Update UI text
        UpdateSensitivityText(sensitivity);

        // Apply sensitivity
        SetCameraSensitivity(sensitivity);
    }

    private void UpdateSensitivityText(float value)
    {
        // Update text if assigned
        if (sensitivityValueText != null)
        {
            sensitivityValueText.text = value.ToString("F2");
        }
    }

    private void SetCameraSensitivity(float sensitivity)
    {
        // Save to PlayerPrefs
        PlayerPrefs.SetFloat("Sensitivity", sensitivity);
        PlayerPrefs.Save();

        // Apply sensitivity to gameplay systems
        // For now, just log the change
        Debug.Log($"Camera sensitivity set to: {sensitivity}");

        // If you have an InputManager with a sensitivity property, you would set it here
        // Example: InputManager.Instance.cameraSensitivity = sensitivity;
    }

    public override void ApplySettings()
    {
        // Settings are applied in real-time, so just save
        PlayerPrefs.Save();

        // Call the main SettingsManager ApplySettings to ensure JSON saving happens
        if (settingsManager != null)
        {
            settingsManager.ApplySettings();
        }
    }

    public override void ResetToDefaults()
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }

        // Reset sensitivity to default
        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = DEFAULT_SENSITIVITY;
            cameraControlToggle.isOn = DEFAULT_CAMERA_STEERING;
            OnSensitivityChanged(DEFAULT_SENSITIVITY);
        }
    }
}