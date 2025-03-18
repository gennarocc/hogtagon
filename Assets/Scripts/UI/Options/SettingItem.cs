using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI settingNameText;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private RectTransform controlContainer;
    
    public void SetSettingName(string name)
    {
        if (settingNameText != null)
            settingNameText.text = name;
    }
    
    public void SetValueText(string value)
    {
        if (valueText != null)
            valueText.text = value;
    }
    
    public Transform GetControlContainer()
    {
        return controlContainer;
    }
} 