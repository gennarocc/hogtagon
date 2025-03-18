using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class OptionsMenuCreator : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject optionsMenuRoot;
    [SerializeField] private TabController tabController;
    [SerializeField] private RectTransform tabButtonContainer;
    [SerializeField] private RectTransform tabContentContainer;
    
    [Header("Tab Configuration")]
    [SerializeField] private string[] tabNames = { "VIDEO", "AUDIO", "GAMEPLAY", "CONTROLS" };
    
    [Header("UI Prefabs")]
    [SerializeField] private GameObject tabButtonPrefab;
    [SerializeField] private GameObject tabContentPrefab;
    [SerializeField] private GameObject settingItemPrefab;
    [SerializeField] private GameObject sliderPrefab;
    [SerializeField] private GameObject togglePrefab;
    [SerializeField] private GameObject dropdownPrefab;
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private GameObject keyBindingPrefab;
    
    [Header("Navigation")]
    [SerializeField] private Button leftArrowButton;
    [SerializeField] private Button rightArrowButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button applyButton;
    [SerializeField] private Button resetButton;
    
    private GameObject[] tabContents;
    private OptionsMenuController optionsController;
    
    private void Awake()
    {
        optionsController = GetComponent<OptionsMenuController>();
    }
    
    // Call this from editor button or during initialization
    public void SetupOptionsMenu()
    {
        ClearExistingTabs();
        CreateTabs();
        SetupNavigationControls();
    }
    
    private void ClearExistingTabs()
    {
        // Clear existing tab buttons
        for (int i = tabButtonContainer.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(tabButtonContainer.GetChild(i).gameObject);
        }
        
        // Clear existing tab content panels
        for (int i = tabContentContainer.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(tabContentContainer.GetChild(i).gameObject);
        }
    }
    
    private void CreateTabs()
    {
        if (tabController == null)
        {
            Debug.LogError("TabController reference is missing!");
            return;
        }

        List<TabController.TabData> tabDataList = new List<TabController.TabData>();
        tabContents = new GameObject[tabNames.Length];
        
        for (int i = 0; i < tabNames.Length; i++)
        {
            // Create tab button
            GameObject tabButtonObj = Instantiate(tabButtonPrefab, tabButtonContainer);
            tabButtonObj.name = tabNames[i] + " Tab Button";
            Button tabButton = tabButtonObj.GetComponent<Button>();
            TextMeshProUGUI tabText = tabButtonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tabText != null)
            {
                tabText.text = tabNames[i];
            }
            
            // Create tab content panel
            GameObject tabContentObj = Instantiate(tabContentPrefab, tabContentContainer);
            tabContentObj.name = tabNames[i] + " Tab Content";
            tabContentObj.SetActive(false); // Start inactive
            tabContents[i] = tabContentObj;
            
            // Create tab data entry
            TabController.TabData tabData = new TabController.TabData
            {
                tabName = tabNames[i],
                tabButton = tabButton,
                tabContent = tabContentObj,
                tabText = tabText
            };
            
            tabDataList.Add(tabData);
        }
        
        // Set tabs in the tab controller
        System.Reflection.FieldInfo tabsField = typeof(TabController).GetField("tabs", 
                                System.Reflection.BindingFlags.NonPublic | 
                                System.Reflection.BindingFlags.Instance);
        if (tabsField != null)
        {
            tabsField.SetValue(tabController, tabDataList);
        }
    }
    
    private void SetupNavigationControls()
    {
        if (leftArrowButton != null && rightArrowButton != null && tabController != null)
        {
            leftArrowButton.onClick.AddListener(tabController.PreviousTab);
            rightArrowButton.onClick.AddListener(tabController.NextTab);
        }
        
        if (backButton != null && optionsController != null)
        {
            // Implementation depends on your specific requirement
            backButton.onClick.AddListener(() => optionsMenuRoot.SetActive(false));
        }
    }
    
    // Helper method to create setting items with different control types
    public GameObject CreateSettingItem(string tabName, string settingName)
    {
        int tabIndex = System.Array.IndexOf(tabNames, tabName);
        if (tabIndex < 0 || tabIndex >= tabContents.Length)
        {
            Debug.LogError($"Tab '{tabName}' does not exist!");
            return null;
        }
        
        GameObject settingItemObj = Instantiate(settingItemPrefab, tabContents[tabIndex].transform);
        settingItemObj.name = settingName + " Setting";
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem != null)
        {
            settingItem.SetSettingName(settingName);
        }
        
        return settingItemObj;
    }
    
    // Helper method to add a slider control to a setting item
    public Slider AddSliderToSetting(GameObject settingItemObj, float minValue, float maxValue, float defaultValue)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        GameObject sliderObj = Instantiate(sliderPrefab, settingItem.GetControlContainer());
        Slider slider = sliderObj.GetComponent<Slider>();
        if (slider != null)
        {
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = defaultValue;
            
            slider.onValueChanged.AddListener((value) => {
                settingItem.SetValueText(Mathf.RoundToInt(value).ToString());
            });
            
            // Initialize the value text
            settingItem.SetValueText(Mathf.RoundToInt(defaultValue).ToString());
        }
        
        return slider;
    }
    
    // Helper method to add a toggle control to a setting item
    public Toggle AddToggleToSetting(GameObject settingItemObj, bool defaultValue)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        GameObject toggleObj = Instantiate(togglePrefab, settingItem.GetControlContainer());
        Toggle toggle = toggleObj.GetComponent<Toggle>();
        if (toggle != null)
        {
            toggle.isOn = defaultValue;
            
            toggle.onValueChanged.AddListener((value) => {
                settingItem.SetValueText(value ? "On" : "Off");
            });
            
            // Initialize the value text
            settingItem.SetValueText(defaultValue ? "On" : "Off");
        }
        
        return toggle;
    }
    
    // Helper method to add a dropdown control to a setting item
    public TMP_Dropdown AddDropdownToSetting(GameObject settingItemObj, List<string> options, int defaultOption)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        GameObject dropdownObj = Instantiate(dropdownPrefab, settingItem.GetControlContainer());
        TMP_Dropdown dropdown = dropdownObj.GetComponent<TMP_Dropdown>();
        if (dropdown != null)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.value = defaultOption;
            
            dropdown.onValueChanged.AddListener((index) => {
                if (index >= 0 && index < options.Count)
                {
                    settingItem.SetValueText(options[index]);
                }
            });
            
            // Initialize the value text
            if (defaultOption >= 0 && defaultOption < options.Count)
            {
                settingItem.SetValueText(options[defaultOption]);
            }
        }
        
        return dropdown;
    }
    
    // Helper method to add a button control to a setting item
    public Button AddButtonToSetting(GameObject settingItemObj, string buttonText)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        GameObject buttonObj = Instantiate(buttonPrefab, settingItem.GetControlContainer());
        Button button = buttonObj.GetComponent<Button>();
        TextMeshProUGUI buttonTextComponent = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (buttonTextComponent != null)
        {
            buttonTextComponent.text = buttonText;
        }
        
        // Set the value text to match the button text
        settingItem.SetValueText(buttonText);
        
        return button;
    }
    
    // Helper method to add a key binding control to a setting item
    public GameObject AddKeyBindingToSetting(GameObject settingItemObj, string keyName)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        GameObject keyBindingObj = Instantiate(keyBindingPrefab, settingItem.GetControlContainer());
        Button keyButton = keyBindingObj.GetComponent<Button>();
        TextMeshProUGUI keyText = keyBindingObj.GetComponentInChildren<TextMeshProUGUI>();
        
        if (keyText != null)
        {
            keyText.text = keyName;
        }
        
        // Set value text to show the current key
        settingItem.SetValueText(keyName);
        
        // Add key rebinding logic here
        if (keyButton != null)
        {
            keyButton.onClick.AddListener(() => {
                // Start key rebinding
                Debug.Log($"Waiting for key to rebind {settingItemObj.name}...");
                // Implement key rebinding logic
            });
        }
        
        return keyBindingObj;
    }
} 