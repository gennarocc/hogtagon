using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
                    try {
                        // Try to find corresponding panel - use the exact panel names from hierarchy
                        string panelName = button.name.Replace("Tab", "Panel");
                        Transform panel = transform.parent.Find("Content/" + panelName);
                        if (panel != null)
                        {
                            TabData newTab = new TabData();
                            newTab.tabName = button.name.Replace("Tab", "");
                            newTab.tabButton = button;
                            newTab.tabContent = panel.gameObject;
                            newTab.tabText = button.GetComponentInChildren<TextMeshProUGUI>();
                            tabs.Add(newTab);
                        }
                        else
                        {
                            Debug.LogWarning("TabController: Could not find panel for " + button.name);
                        }
                    }
                    catch (System.Exception e) {
                        Debug.LogError("Error setting up tab: " + e.Message);
                    }
                }
            }
        }
        
        // Remove any invalid tabs
        for (int i = tabs.Count - 1; i >= 0; i--)
        {
            if (tabs[i].tabButton == null || tabs[i].tabContent == null)
            {
                Debug.LogWarning("TabController: Removing invalid tab at index " + i);
                tabs.RemoveAt(i);
            }
        }
        
        // If no valid tabs, early exit
        if (tabs.Count == 0)
        {
            Debug.LogWarning("TabController: No valid tabs found, aborting setup");
            return;
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
        
        // Validate default tab index
        if (defaultTabIndex < 0 || defaultTabIndex >= tabs.Count)
        {
            defaultTabIndex = 0;
        }
        
        // Always select the default tab when enabled
        if (tabs.Count > 0)
        {
            SelectTab(defaultTabIndex);
        }
        
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
        
        // Set up back button if it exists
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(BackToMainMenu);
        }
        else
        {
            // Try to find back button if not assigned
            Button[] allButtons = GetComponentsInChildren<Button>(true);
            foreach (Button btn in allButtons)
            {
                if (btn != null && btn.name.ToLower().Contains("back"))
                {
                    backButton = btn;
                    backButton.onClick.RemoveAllListeners();
                    backButton.onClick.AddListener(BackToMainMenu);
                    Debug.Log("TabController: Found back button: " + backButton.name);
                    break;
                }
            }
            
            if (backButton == null)
            {
                Debug.LogWarning("TabController: No back button found");
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
    
    void Update()
    {
        // Handle gamepad navigation between tabs
        if (Gamepad.current != null && !processingInput)
        {
            // Check for left/right navigation between tabs
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
    
    public void SelectTab(int tabIndex)
    {
        // Validate input
        if (tabIndex < 0 || tabIndex >= tabs.Count)
        {
            Debug.LogWarning("TabController: Invalid tab index: " + tabIndex);
            return;
        }
        
        // Skip if it's the same tab
        if (tabIndex == currentTabIndex)
            return;
            
        // Validate tab data
        if (tabs[tabIndex].tabContent == null)
        {
            Debug.LogError("Tab container not found for index " + tabIndex + "! Please assign it in the Inspector.");
            return;
        }
            
        // Deactivate current tab if one is active
        if (currentTabIndex >= 0 && currentTabIndex < tabs.Count)
        {
            TabData currentTab = tabs[currentTabIndex];
            
            // Skip if missing references
            if (currentTab == null)
            {
                Debug.LogError("Current tab data is null!");
            }
            else
            {
                // Hide the content if it exists
                if (currentTab.tabContent != null)
                {
                    currentTab.tabContent.SetActive(false);
                }
                else
                {
                    Debug.LogWarning("Current tab content is null!");
                }
                
                // Reset button color if text exists
                if (currentTab.tabText != null)
                {
                    currentTab.tabText.color = inactiveTabColor;
                }
            }
        }
        
        // Activate the new tab
        currentTabIndex = tabIndex;
        TabData newTab = tabs[currentTabIndex];
        
        // Show the content
        if (newTab.tabContent != null)
        {
            newTab.tabContent.SetActive(true);
        }
        else
        {
            Debug.LogError("New tab content is null!");
            return;
        }
        
        // Set button color
        if (newTab.tabText != null)
        {
            newTab.tabText.color = activeTabColor;
        }
        
        // Focus on the tab button for better gamepad navigation
        if (EventSystem.current != null && newTab.tabButton != null)
        {
            EventSystem.current.SetSelectedGameObject(newTab.tabButton.gameObject);
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
        // Log diagnostic information
        Debug.Log("TabController.BackToMainMenu called");
        
        // Find the MenuManager
        MenuManager menuManager = FindObjectOfType<MenuManager>();
        if (menuManager != null)
        {
            // Trigger the back button functionality 
            menuManager.ButtonClickAudio();
            
            Debug.Log("Found MenuManager, gameIsPaused = " + menuManager.gameIsPaused);
            
            // Directly handle closing this menu first
            GameObject optionsMenu = transform.parent.gameObject;
            optionsMenu.SetActive(false);
            
            // Then handle pause menu activation directly
            if (menuManager.gameIsPaused)
            {
                Debug.Log("Game is paused, activating pause menu directly");
                Transform pauseMenuUI = menuManager.transform.Find("PauseMenuUI");
                if (pauseMenuUI != null)
                {
                    pauseMenuUI.gameObject.SetActive(true);
                    
                    // Enable all children
                    foreach (Transform child in pauseMenuUI)
                    {
                        child.gameObject.SetActive(true);
                    }
                    
                    // Find Resume button for focus
                    Button resumeButton = null;
                    Button[] buttons = pauseMenuUI.GetComponentsInChildren<Button>(true);
                    foreach (Button btn in buttons)
                    {
                        if (btn.name.Contains("Resume"))
                        {
                            resumeButton = btn;
                            break;
                        }
                    }
                    
                    if (resumeButton != null && EventSystem.current != null)
                    {
                        EventSystem.current.SetSelectedGameObject(null);
                        EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
                    }
                }
                else
                {
                    Debug.LogError("Could not find PauseMenuUI!");
                }
            }
            else
            {
                Debug.Log("Game is not paused, activating main menu");
                // Find and activate main menu
                Transform mainMenuPanel = menuManager.transform.Find("MainMenuPanel");
                if (mainMenuPanel != null)
                {
                    mainMenuPanel.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            Debug.LogError("MenuManager not found!");
        }
    }
} 