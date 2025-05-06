using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

/// <summary>
/// Handles the Audio settings tab content
/// </summary>
public class AudioTabContent : TabContent
{
    [Header("Audio Settings")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeText;
    [SerializeField] private TextMeshProUGUI musicVolumeText;
    [SerializeField] private TextMeshProUGUI sfxVolumeText;
    [SerializeField] private AudioMixer audioMixer;

    // Default values
    private const float DEFAULT_MASTER_VOLUME = 0.8f;
    private const float DEFAULT_MUSIC_VOLUME = 0.8f;
    private const float DEFAULT_SFX_VOLUME = 0.8f;

    protected override void Awake()
    {
        base.Awake();

        // Set up slider listeners
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXVolumeChanged);
        }
    }

    protected override void InitializeUI()
    {
        // Update UI with current settings
        if (masterVolumeSlider != null)
        {
            float masterVolume = PlayerPrefs.GetFloat("MasterVolume", DEFAULT_MASTER_VOLUME);
            masterVolumeSlider.value = masterVolume;
            UpdateVolumeText(masterVolumeText, masterVolume);
        }

        if (musicVolumeSlider != null)
        {
            float musicVolume = PlayerPrefs.GetFloat("MusicVolume", DEFAULT_MUSIC_VOLUME);
            musicVolumeSlider.value = musicVolume;
            UpdateVolumeText(musicVolumeText, musicVolume);
        }

        if (sfxVolumeSlider != null)
        {
            float sfxVolume = PlayerPrefs.GetFloat("SFXVolume", DEFAULT_SFX_VOLUME);
            sfxVolumeSlider.value = sfxVolume;
            UpdateVolumeText(sfxVolumeText, sfxVolume);
        }
    }

    public void OnMasterVolumeChanged(float volume)
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }

        // Update UI
        UpdateVolumeText(masterVolumeText, volume);

        // Set the volume
        SetMasterVolume(volume);
    }

    public void OnMusicVolumeChanged(float volume)
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }

        // Update UI
        UpdateVolumeText(musicVolumeText, volume);

        // Set the volume
        SetMusicVolume(volume);
    }

    public void OnSFXVolumeChanged(float volume)
    {
        if (settingsManager != null)
        {
            // Play sound feedback
            SoundManager.Instance.PlayUISound(SoundManager.SoundEffectType.UIClick);
        }

        // Update UI
        UpdateVolumeText(sfxVolumeText, volume);

        // Set the volume
        SetSFXVolume(volume);
    }

    private void UpdateVolumeText(TextMeshProUGUI text, float value)
    {
        if (text != null)
        {
            text.text = Mathf.RoundToInt(value * 100) + "%";
        }
    }

    private void SetMasterVolume(float volume)
    {
        // Set volume using logarithmic scale for better audio perception
        if (audioMixer != null)
        {
            // Convert linear volume to logarithmic (-80dB to 0dB)
            float dbVolume = volume > 0.001f ? Mathf.Log10(volume) * 20 : -80f;
            audioMixer.SetFloat("MasterVolume", dbVolume);
        }

        // Save to PlayerPrefs
        PlayerPrefs.SetFloat("MasterVolume", volume);
        PlayerPrefs.Save();
    }

    private void SetMusicVolume(float volume)
    {
        // Set volume using logarithmic scale for better audio perception
        if (audioMixer != null)
        {
            // Convert linear volume to logarithmic (-80dB to 0dB)
            float dbVolume = volume > 0.001f ? Mathf.Log10(volume) * 20 : -80f;
            audioMixer.SetFloat("MusicVolume", dbVolume);
        }

        // Save to PlayerPrefs
        PlayerPrefs.SetFloat("MusicVolume", volume);
        PlayerPrefs.Save();
    }

    private void SetSFXVolume(float volume)
    {
        // Set volume using logarithmic scale for better audio perception
        if (audioMixer != null)
        {
            // Convert linear volume to logarithmic (-80dB to 0dB)
            float dbVolume = volume > 0.001f ? Mathf.Log10(volume) * 20 : -80f;
            audioMixer.SetFloat("SFXVolume", dbVolume);
        }

        // Save to PlayerPrefs
        PlayerPrefs.SetFloat("SFXVolume", volume);
        PlayerPrefs.Save();
    }

    public override void ApplySettings()
    {
        // Audio settings are applied in real-time, so we just need to save
        // to make sure everything is saved, but everything is already working
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

        // Reset master volume
        if (masterVolumeSlider != null)
        {
            masterVolumeSlider.value = DEFAULT_MASTER_VOLUME;
            OnMasterVolumeChanged(DEFAULT_MASTER_VOLUME);
        }

        // Reset music volume
        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.value = DEFAULT_MUSIC_VOLUME;
            OnMusicVolumeChanged(DEFAULT_MUSIC_VOLUME);
        }

        // Reset SFX volume
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.value = DEFAULT_SFX_VOLUME;
            OnSFXVolumeChanged(DEFAULT_SFX_VOLUME);
        }
    }
}