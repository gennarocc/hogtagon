using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hogtagon.Settings
{
    public class TabController : MonoBehaviour
    {
        [System.Serializable]
        public class Tab
        {
            public Button tabButton;
            public GameObject tabContent;
        }

        [SerializeField] private List<Tab> _tabs = new List<Tab>();
        [SerializeField] private Color _selectedTabColor = Color.white;
        [SerializeField] private Color _unselectedTabColor = Color.gray;

        private int _currentTabIndex = -1;

        private void Awake()
        {
            InitializeTabs();
        }

        private void InitializeTabs()
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                int tabIndex = i;
                if (_tabs[i].tabButton != null)
                {
                    _tabs[i].tabButton.onClick.AddListener(() => SelectTab(tabIndex));
                }
                else
                {
                    Debug.LogError($"[TabController] Tab button at index {i} is null!");
                }
                
                if (_tabs[i].tabContent != null)
                {
                    _tabs[i].tabContent.SetActive(false);
                }
                else
                {
                    Debug.LogError($"[TabController] Tab content at index {i} is null!");
                }
            }

            // Select the first tab by default
            if (_tabs.Count > 0)
            {
                SelectTab(0);
            }
        }

        public void SelectTab(int index)
        {
            if (index < 0 || index >= _tabs.Count)
            {
                Debug.LogError($"[TabController] Tab index out of range: {index}");
                return;
            }

            // If we're already on this tab, do nothing
            if (_currentTabIndex == index)
                return;

            // Deactivate the current tab if valid
            if (_currentTabIndex >= 0 && _currentTabIndex < _tabs.Count)
            {
                SetTabActive(_currentTabIndex, false);
            }

            // Activate the new tab
            SetTabActive(index, true);
            _currentTabIndex = index;
        }

        private void SetTabActive(int index, bool active)
        {
            if (_tabs[index].tabContent != null)
            {
                _tabs[index].tabContent.SetActive(active);
            }

            if (_tabs[index].tabButton != null)
            {
                ColorBlock colors = _tabs[index].tabButton.colors;
                colors.normalColor = active ? _selectedTabColor : _unselectedTabColor;
                _tabs[index].tabButton.colors = colors;

                // Also update text color if there's a TextMeshProUGUI component
                TextMeshProUGUI buttonText = _tabs[index].tabButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                {
                    buttonText.color = active ? _selectedTabColor : _unselectedTabColor;
                }
            }
        }
    }
} 