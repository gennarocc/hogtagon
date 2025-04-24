using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Controls tab switching for settings UI
/// </summary>
public class TabController : MonoBehaviour
{
    [System.Serializable]
    public class TabData
    {
        public string tabName;
        public Button tabButton;
        public GameObject tabContent;
        public TextMeshProUGUI tabText;
    }

    [Header("Tab Settings")]
    [SerializeField] private List<TabData> tabs = new List<TabData>();
    [SerializeField] private Color activeTabColor = new Color(0.0f, 1.0f, 0.0f, 1.0f);
    [SerializeField] private Color inactiveTabColor = new Color(0.0f, 0.8f, 0.0f, 0.7f);
    [SerializeField] private int defaultTabIndex = 0;
    [SerializeField] private Button backButton;
    
    private int currentTabIndex = -1;
    private bool processingInput = false;
    
    private void OnEnable()
    {
        // Reset the current tab index to force reselection
        currentTabIndex = -1;
        
        // Auto-discover tabs if none are assigned
        if (tabs == null || tabs.Count == 0)
        {
            AutoDiscoverTabs();
        }
        
        // Set up tab button listeners
        SetupTabButtons();
        
        // Select default tab
        SelectTab(defaultTabIndex);
    }
    
    private void AutoDiscoverTabs()
    {
        tabs = new List<TabData>();
        
        // Find tab buttons in children
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button.name.Contains("Tab"))
            {
                try {
                    // Try to find corresponding panel
                    string panelName = button.name.Replace("Tab", "Panel");
                    Transform content = transform.parent.Find("Content");
                    
                    if (content != null)
                    {
                        Transform panel = content.Find(panelName);
                        if (panel != null)
                        {
                            TabData newTab = new TabData
                            {
                                tabName = button.name.Replace("Tab", ""),
                                tabButton = button,
                                tabContent = panel.gameObject,
                                tabText = button.GetComponentInChildren<TextMeshProUGUI>()
                            };
                            
                            tabs.Add(newTab);
                            Debug.Log($"TabController: Auto-discovered tab {newTab.tabName}");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error setting up tab: {e.Message}");
                }
            }
        }
    }
    
    private void SetupTabButtons()
    {
        // Set up each tab button
        for (int i = 0; i < tabs.Count; i++)
        {
            TabData tab = tabs[i];
            
            if (tab.tabButton != null)
            {
                // Make sure button is active
                tab.tabButton.gameObject.SetActive(true);
                
                // Clear existing listeners
                tab.tabButton.onClick.RemoveAllListeners();
                
                // Add click handler
                int index = i; // Capture for lambda
                tab.tabButton.onClick.AddListener(() => SelectTab(index));
                
                // Set initial inactive color
                if (tab.tabText != null)
                {
                    tab.tabText.color = inactiveTabColor;
                }
                
                // Hide content initially
                if (tab.tabContent != null)
                {
                    tab.tabContent.SetActive(false);
                }
            }
        }
        
        // Set up back button if it exists
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(BackToMainMenu);
        }
    }
    
    private void Update()
    {
        // Handle gamepad navigation between tabs
        if (Gamepad.current != null && !processingInput)
        {
            if (Gamepad.current.dpad.left.wasPressedThisFrame)
            {
                processingInput = true;
                PreviousTab();
                StartCoroutine(ResetInputProcessing(0.2f));
            }
            else if (Gamepad.current.dpad.right.wasPressedThisFrame)
            {
                processingInput = true;
                NextTab();
                StartCoroutine(ResetInputProcessing(0.2f));
            }
        }
    }
    
    private System.Collections.IEnumerator ResetInputProcessing(float delay)
    {
        yield return new WaitForSeconds(delay);
        processingInput = false;
    }
    
    /// <summary>
    /// Select a specific tab by index
    /// </summary>
    public void SelectTab(int tabIndex)
    {
        // Skip if already on this tab
        if (tabIndex == currentTabIndex)
            return;
            
        // Validate index
        if (tabIndex < 0 || tabIndex >= tabs.Count)
        {
            Debug.LogWarning($"TabController: Invalid tab index: {tabIndex}");
            return;
        }
        
        // Hide current tab if one is active
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            TabData currentTab = tabs[currentTabIndex];
            
            if (currentTab.tabContent != null)
            {
                currentTab.tabContent.SetActive(false);
            }
            
            if (currentTab.tabText != null)
            {
                currentTab.tabText.color = inactiveTabColor;
            }
        }
        
        // Show new tab
        TabData newTab = tabs[tabIndex];
        
        if (newTab.tabContent != null)
        {
            // Make sure parent is active
            if (newTab.tabContent.transform.parent != null)
            {
                newTab.tabContent.transform.parent.gameObject.SetActive(true);
            }
            
            // Activate tab content
            newTab.tabContent.SetActive(true);
            
            // If tab content has TabContent component, call its initialization
            TabContent tabContent = newTab.tabContent.GetComponent<TabContent>();
            if (tabContent != null)
            {
                // TabContent.OnEnable will handle initialization
            }
        }
        
        if (newTab.tabText != null)
        {
            newTab.tabText.color = activeTabColor;
        }
        
        // Update current tab index
        currentTabIndex = tabIndex;
        
        // Update button navigation for selected UI elements in the tab
        UpdateSelectedUIElement();
    }
    
    private void UpdateSelectedUIElement()
    {
        // Clear current selection
        EventSystem.current?.SetSelectedGameObject(null);
        
        // Find selectable element in current tab
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            TabData currentTab = tabs[currentTabIndex];
            
            if (currentTab.tabContent != null)
            {
                // Try to find a suitable UI element to select
                Selectable firstSelectable = currentTab.tabContent.GetComponentInChildren<Selectable>();
                if (firstSelectable != null && firstSelectable.gameObject.activeSelf && firstSelectable.interactable)
                {
                    EventSystem.current?.SetSelectedGameObject(firstSelectable.gameObject);
                }
            }
        }
    }
    
    /// <summary>
    /// Switch to the next tab
    /// </summary>
    public void NextTab()
    {
        int nextTab = (currentTabIndex + 1) % tabs.Count;
        SelectTab(nextTab);
    }
    
    /// <summary>
    /// Switch to the previous tab
    /// </summary>
    public void PreviousTab()
    {
        int prevTab = (currentTabIndex - 1 + tabs.Count) % tabs.Count;
        SelectTab(prevTab);
    }
    
    /// <summary>
    /// Exit the settings menu
    /// </summary>
    public void BackToMainMenu()
    {
        // Save current tab settings before leaving
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            TabData currentTab = tabs[currentTabIndex];
            
            if (currentTab.tabContent != null)
            {
                TabContent tabContent = currentTab.tabContent.GetComponent<TabContent>();
                if (tabContent != null)
                {
                    tabContent.ApplySettings();
                }
            }
        }
        
        // Reset the currentTabIndex to ensure proper reinitialization when reopened
        currentTabIndex = -1;
        
        // Use the MenuManager's method for returning from settings
        // This ensures consistent navigation handling whether opened from pause or main menu
        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.ReturnFromSettingsMenu();
        }
        else
        {
            Debug.LogWarning("MenuManager.Instance is null! Cannot navigate back properly.");
            gameObject.SetActive(false);
        }
    }
} 