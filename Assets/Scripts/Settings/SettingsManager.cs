using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Hogtagon.UI;

namespace Hogtagon.Settings
{
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private TabController _tabController;
        [SerializeField] private GameObject _settingsContent;
        [SerializeField] private Button _backButton;

        private MenuManager _menuManager;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _menuManager = FindObjectOfType<MenuManager>();
            if (_menuManager == null)
            {
                Debug.LogError("[SettingsManager] Could not find MenuManager in the scene!");
            }

            if (_settingsContent != null)
            {
                _settingsContent.SetActive(false);
            }
            else
            {
                Debug.LogError("[SettingsManager] Settings content is not assigned!");
            }

            _backButton.onClick.AddListener(OnBackButtonClicked);
        }

        /// <summary>
        /// Shows the settings menu with the specified tab selected
        /// </summary>
        /// <param name="tabIndex">Index of the tab to select, defaults to 0</param>
        public void Show(int tabIndex = 0)
        {
            if (_settingsContent == null)
            {
                Debug.LogError("[SettingsManager] Settings content is not assigned!");
                return;
            }

            _settingsContent.SetActive(true);
            
            if (_tabController != null)
            {
                _tabController.SelectTab(tabIndex);
            }
            else
            {
                Debug.LogError("[SettingsManager] Tab controller is not assigned!");
            }
        }

        /// <summary>
        /// Handles the back button click in settings menu
        /// </summary>
        public void OnBackButtonClicked()
        {
            if (_settingsContent == null)
            {
                Debug.LogError("[SettingsManager] Settings content is not assigned!");
                return;
            }

            _settingsContent.SetActive(false);
            
            if (_menuManager != null)
            {
                _menuManager.ReturnFromSettingsMenu();
            }
            else
            {
                Debug.LogError("[SettingsManager] MenuManager is null, cannot return from settings menu!");
            }
        }
    }
} 