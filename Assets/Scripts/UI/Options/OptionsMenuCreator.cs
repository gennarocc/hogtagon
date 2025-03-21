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
    
    [SerializeField] private Transform tabContainer;
    
    private GameObject[] tabContents;
    private OptionsMenuController optionsController;
    
    private void Awake()
    {
        optionsController = GetComponent<OptionsMenuController>();
    }
    
    private void Start()
    {
        if (tabContainer == null)
        {
            tabContainer = transform.parent.Find("Content");
            if (tabContainer == null)
            {
                Debug.LogError("Tab container not found! Please assign it in the inspector.");
            }
        }
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
        // Find the appropriate tab panel
        Transform tabPanel = FindTabPanel(tabName);
        if (tabPanel == null)
        {
            Debug.LogError($"Tab '{tabName}' does not exist!");
            return null;
        }

        // Instantiate the setting item in the appropriate tab
        GameObject settingItem = Instantiate(settingItemPrefab, tabPanel);
        settingItem.name = settingName;

        // Set the setting name
        SettingItem settingComponent = settingItem.GetComponent<SettingItem>();
        if (settingComponent != null)
        {
            settingComponent.SetSettingName(settingName);
        }

        return settingItem;
    }
    
    private Transform FindTabPanel(string tabName)
    {
        if (tabContainer == null)
        {
            Debug.LogError("Tab container is not assigned!");
            return null;
        }

        // Try to find the tab with the exact name first
        Transform tabPanel = tabContainer.Find(tabName + "Panel");
        if (tabPanel != null) return tabPanel;
        
        // If not found, try case-insensitive search
        foreach (Transform child in tabContainer)
        {
            if (child.name.Equals(tabName + "Panel", System.StringComparison.OrdinalIgnoreCase) ||
                child.name.Equals("Panel" + tabName, System.StringComparison.OrdinalIgnoreCase) ||
                child.name.Equals(tabName, System.StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }
        
        return null;
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
    
    // Helper method to add an input field control to a setting item
    public TMP_InputField AddInputFieldToSetting(GameObject settingItemObj, string placeholder, string defaultText, int characterLimit = 0)
    {
        if (settingItemObj == null) return null;
        
        SettingItem settingItem = settingItemObj.GetComponent<SettingItem>();
        if (settingItem == null) return null;
        
        // Create an input field - since we don't have a prefab, we'll create one from scratch
        GameObject inputFieldObj = new GameObject("InputField", typeof(RectTransform));
        inputFieldObj.transform.SetParent(settingItem.GetControlContainer(), false);
        
        // Create the TMP_InputField component
        TMP_InputField inputField = inputFieldObj.AddComponent<TMP_InputField>();
        
        // Create the visual components
        GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
        textArea.transform.SetParent(inputFieldObj.transform, false);
        
        // Create the placeholder text
        GameObject placeholderObj = new GameObject("Placeholder", typeof(RectTransform));
        placeholderObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 14;
        placeholderText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        placeholderText.horizontalAlignment = HorizontalAlignmentOptions.Left;
        placeholderText.verticalAlignment = VerticalAlignmentOptions.Middle;
        
        // Create the input text
        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(textArea.transform, false);
        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.text = defaultText;
        inputText.fontSize = 14;
        inputText.color = new Color(1f, 1f, 1f, 1f);
        inputText.horizontalAlignment = HorizontalAlignmentOptions.Left;
        inputText.verticalAlignment = VerticalAlignmentOptions.Middle;
        
        // Set up the input field component references
        inputField.textViewport = textArea.GetComponent<RectTransform>();
        inputField.textComponent = inputText;
        inputField.placeholder = placeholderText;
        inputField.text = defaultText;
        
        if (characterLimit > 0)
            inputField.characterLimit = characterLimit;
        
        // Set the input field styling
        Image background = inputFieldObj.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        
        // Set up RectTransforms
        RectTransform inputFieldRect = inputFieldObj.GetComponent<RectTransform>();
        inputFieldRect.sizeDelta = new Vector2(200, 30);
        inputFieldRect.anchorMin = new Vector2(0.5f, 0.5f);
        inputFieldRect.anchorMax = new Vector2(0.5f, 0.5f);
        inputFieldRect.pivot = new Vector2(0.5f, 0.5f);
        
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.sizeDelta = new Vector2(180, 20);
        textAreaRect.anchorMin = new Vector2(0, 0);
        textAreaRect.anchorMax = new Vector2(1, 1);
        textAreaRect.pivot = new Vector2(0.5f, 0.5f);
        textAreaRect.offsetMin = new Vector2(5, 5);
        textAreaRect.offsetMax = new Vector2(-5, -5);
        
        RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
        placeholderRect.sizeDelta = new Vector2(0, 0);
        placeholderRect.anchorMin = new Vector2(0, 0);
        placeholderRect.anchorMax = new Vector2(1, 1);
        placeholderRect.pivot = new Vector2(0.5f, 0.5f);
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.sizeDelta = new Vector2(0, 0);
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        
        // Set up the value text listener
        inputField.onValueChanged.AddListener((newValue) => {
            settingItem.SetValueText(newValue);
        });
        
        // Initialize the value text
        settingItem.SetValueText(defaultText);
        
        return inputField;
    }
} 