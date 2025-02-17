using UnityEngine;
using TMPro;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.UI;

public class Settings : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] public Slider cameraSensitivity;
    private Resolution[] resolutions;
    private List<Resolution> filteredResolutions;

    [Header("Wwise")]
    [SerializeField] public AK.Wwise.RTPC MasterVolume;
    [SerializeField] public AK.Wwise.RTPC MusicVolume;
    [SerializeField] public AK.Wwise.RTPC SfxVolume;
    [SerializeField] private AK.Wwise.Event uiClick;
    [SerializeField] private AK.Wwise.Event uiConfirm;
    [SerializeField] private AK.Wwise.Event uiCancel;

    void Start()
    {
        LoadResolutions();
        LoadCurrentResolution();
    }

    private void LoadResolutions()
    {
        // Get all possible resolutions
        resolutions = Screen.resolutions;
        filteredResolutions = new List<Resolution>();

        // Clear existing options
        resolutionDropdown.ClearOptions();

        // Get the current refresh rate
        int currentRefreshRate = Screen.currentResolution.refreshRate;

        // Filter resolutions to only include those matching current refresh rate
        // and create the dropdown options
        List<string> options = new List<string>();
        HashSet<string> addedResolutions = new HashSet<string>();

        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].refreshRate == currentRefreshRate)
            {
                string option = $"{resolutions[i].width} x {resolutions[i].height}";
                if (!addedResolutions.Contains(option))
                {
                    addedResolutions.Add(option);
                    options.Add(option);
                    filteredResolutions.Add(resolutions[i]);
                }
            }
        }

        // Add options to dropdown
        resolutionDropdown.AddOptions(options);
    }

    private void LoadCurrentResolution()
    {
        // Find current resolution in our filtered list
        Resolution currentResolution = Screen.currentResolution;
        for (int i = 0; i < filteredResolutions.Count; i++)
        {
            if (filteredResolutions[i].width == currentResolution.width &&
                filteredResolutions[i].height == currentResolution.height)
            {
                resolutionDropdown.value = i;
                break;
            }
        }

        resolutionDropdown.RefreshShownValue();
    }

    public void SetResolution(int resolutionIndex)
    {
        Resolution resolution = filteredResolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreen);
        SaveResolutionPreference(resolution);
    }

    private void SaveResolutionPreference(Resolution resolution)
    {
        PlayerPrefs.SetInt("ResolutionWidth", resolution.width);
        PlayerPrefs.SetInt("ResolutionHeight", resolution.height);
        PlayerPrefs.Save();
    }

    private void LoadSavedResolution()
    {
        if (PlayerPrefs.HasKey("ResolutionWidth") && PlayerPrefs.HasKey("ResolutionHeight"))
        {
            int savedWidth = PlayerPrefs.GetInt("ResolutionWidth");
            int savedHeight = PlayerPrefs.GetInt("ResolutionHeight");
            Screen.SetResolution(savedWidth, savedHeight, Screen.fullScreen);
        }
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
    }

    public void SetCameraSensitivty()
    {
        var player = ConnectionManager.instance.GetPlayer(NetworkManager.Singleton.LocalClientId);
        if (player != null && player.mainCamera != null)
        {
            player.mainCamera.m_XAxis.m_MaxSpeed = cameraSensitivity.value * 300f;
            player.mainCamera.m_YAxis.m_MaxSpeed = cameraSensitivity.value * 2f;
        }
    }

    public void SetMasterVolume(float vol)
    {
        MasterVolume.SetGlobalValue(vol);
    }

    public void SetMusicVolume(float vol)
    {
        MusicVolume.SetGlobalValue(vol);
    }

    public void SetSfxVolume(float vol)
    {
        SfxVolume.SetGlobalValue(vol);
    }

    public void ButtonClickAudio()
    {
        uiClick.Post(gameObject);
    }

    public void ButtonConfirmAudio()
    {
        uiConfirm.Post(gameObject);
    }

    public void ButtonCancelAudio()
    {
        uiCancel.Post(gameObject);
    }
}