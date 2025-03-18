using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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
    
    void Start()
    {
        // Set up tab button click listeners
        for (int i = 0; i < tabs.Count; i++)
        {
            int tabIndex = i; // Capture the index for the lambda
            tabs[i].tabButton.onClick.AddListener(() => SelectTab(tabIndex));
        }
        
        // Select the default tab
        SelectTab(defaultTabIndex);
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
} 