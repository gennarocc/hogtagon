using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;

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
    
    private int currentTabIndex = -1;
    
    private void OnEnable()
    {
        // Ensure this component is active
        this.enabled = true;
        
        // Verify the tabs collection has content
        if (tabs == null || tabs.Count == 0)
        {
            Debug.LogWarning("TabController: No tabs found in the tabs collection.");
            // Try to find tab objects in children if none are assigned
            Button[] tabButtons = GetComponentsInChildren<Button>(true);
            foreach (Button button in tabButtons)
            {
                if (button.name.Contains("Tab"))
                {
                    // Try to find corresponding panel
                    Transform panel = transform.parent.Find("Content/" + button.name.Replace("Tab", "Panel"));
                    if (panel != null)
                    {
                        TabData newTab = new TabData();
                        newTab.tabName = button.name.Replace("Tab", "");
                        newTab.tabButton = button;
                        newTab.tabContent = panel.gameObject;
                        newTab.tabText = button.GetComponentInChildren<TextMeshProUGUI>();
                        tabs.Add(newTab);
                    }
                }
            }
        }
        
        // Ensure all tab buttons have click listeners
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].tabButton != null)
            {
                // Ensure button is active
                tabs[i].tabButton.gameObject.SetActive(true);
                
                int tabIndex = i; // Capture the index for the lambda
                
                // Clear previous listeners to avoid duplicates
                tabs[i].tabButton.onClick.RemoveAllListeners();
                
                // Add fresh listener
                tabs[i].tabButton.onClick.AddListener(() => SelectTab(tabIndex));
            }
        }
        
        // Reset tab states - deactivate all content first
        foreach (var tab in tabs)
        {
            if (tab.tabContent != null)
            {
                tab.tabContent.SetActive(false);
            }
            
            if (tab.tabText != null)
            {
                tab.tabText.color = inactiveTabColor;
            }
        }
        
        // Always select the default tab when enabled
        SelectTab(defaultTabIndex);
        
        // Double check that tab content is active
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            if (tabs[currentTabIndex].tabContent != null)
            {
                tabs[currentTabIndex].tabContent.SetActive(true);
                
                // Ensure the content's parent is also active
                if (tabs[currentTabIndex].tabContent.transform.parent != null)
                    tabs[currentTabIndex].tabContent.transform.parent.gameObject.SetActive(true);
            }
        }
    }

    void Start()
    {
        // Set up tab button click listeners
        for (int i = 0; i < tabs.Count; i++)
        {
            int tabIndex = i; // Capture the index for the lambda
            tabs[i].tabButton.onClick.AddListener(() => SelectTab(tabIndex));
        }
        
        // Select the default tab - now handled in OnEnable
        if (!gameObject.activeInHierarchy)
        {
            // Only select tab in Start if the object isn't already active
            // (otherwise OnEnable will handle it)
            SelectTab(defaultTabIndex);
        }
    }
    
    public void SelectTab(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= tabs.Count || tabIndex == currentTabIndex)
            return;
            
        // Deactivate current tab if one is active
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            // Hide the content
            tabs[currentTabIndex].tabContent.SetActive(false);
            
            // Reset button color
            if (tabs[currentTabIndex].tabText != null)
            {
                tabs[currentTabIndex].tabText.color = inactiveTabColor;
            }
        }
        
        // Activate the new tab
        currentTabIndex = tabIndex;
        
        // Show the content
        tabs[currentTabIndex].tabContent.SetActive(true);
        
        // Set button color
        if (tabs[currentTabIndex].tabText != null)
        {
            tabs[currentTabIndex].tabText.color = activeTabColor;
        }
    }
    
    public void NextTab()
    {
        int nextTab = (currentTabIndex + 1) % tabs.Count;
        SelectTab(nextTab);
    }
    
    public void PreviousTab()
    {
        int prevTab = (currentTabIndex - 1 + tabs.Count) % tabs.Count;
        SelectTab(prevTab);
    }

    public void BackToMainMenu()
    {
        // Find the MenuManager
        MenuManager menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            // Trigger the back button functionality
            menuManager.ButtonClickAudio();
            
            // Deactivate this menu
            GameObject optionsMenu = gameObject.transform.parent.gameObject;
            optionsMenu.SetActive(false);
            
            // Reset all tab contents
            foreach (var tab in tabs)
            {
                if (tab.tabContent != null)
                {
                    tab.tabContent.SetActive(false);
                }
                
                if (tab.tabText != null)
                {
                    tab.tabText.color = inactiveTabColor;
                }
            }
            
            // Show the main menu
            if (menuManager.gameObject != null)
            {
                Transform mainMenuPanel = menuManager.transform.Find("MainMenuPanel");
                if (mainMenuPanel != null)
                {
                    mainMenuPanel.gameObject.SetActive(true);
                    
                    // Reset button states in main menu
                    ButtonStateResetter resetter = mainMenuPanel.GetComponent<ButtonStateResetter>();
                    if (resetter != null)
                    {
                        resetter.ResetAllButtonStates();
                    }
                    
                    // Manually clear the event system selection
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }
    }
} 