using UnityEngine;
using TMPro;
using System.Text.RegularExpressions;
using UnityEngine.UI;

public class UsernameInputHandler : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameField;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private Color errorColor = Color.red;
    
    private string previousValidUsername;
    private Regex alphanumericRegex;
    
    private void Awake()
    {
        // Initialize regex for username validation (allow alphanumeric characters only)
        alphanumericRegex = new Regex(@"^[a-zA-Z0-9]+$");
        
        // Hide error text initially
        if (errorText != null)
            errorText.gameObject.SetActive(false);
    }
    
    private void Start()
    {
        // Load saved username from PlayerPrefs
        string savedUsername = PlayerPrefs.GetString("Username", "Player");
        usernameField.text = savedUsername;
        previousValidUsername = savedUsername;
        
        // Set up the listener for changes
        usernameField.onValueChanged.AddListener(OnUsernameChanged);
        usernameField.onEndEdit.AddListener(OnUsernameEditEnd);
    }
    
    private void OnEnable()
    {
        // Refresh the displayed username when panel becomes active
        if (usernameField != null)
        {
            string savedUsername = PlayerPrefs.GetString("Username", "Player");
            usernameField.text = savedUsername;
            previousValidUsername = savedUsername;
            
            // Hide error text when panel becomes active
            if (errorText != null)
                errorText.gameObject.SetActive(false);
        }
    }
    
    private void OnUsernameChanged(string newValue)
    {
        if (string.IsNullOrEmpty(newValue))
        {
            ShowError("Username cannot be empty");
            return;
        }
        
        if (newValue.Length > 10)
        {
            ShowError("Username must be 10 characters or less");
            return;
        }
        
        if (!alphanumericRegex.IsMatch(newValue))
        {
            ShowError("Username can only contain letters and numbers");
            return;
        }
        
        // Hide error if validation passes
        HideError();
        
        // Store the valid username
        previousValidUsername = newValue;
        
        // Save to PlayerPrefs
        PlayerPrefs.SetString("Username", newValue);
        PlayerPrefs.Save();
    }
    
    private void OnUsernameEditEnd(string value)
    {
        // If the final value is invalid, revert to the last valid username
        if (string.IsNullOrEmpty(value) || !alphanumericRegex.IsMatch(value) || value.Length > 10)
        {
            usernameField.text = previousValidUsername;
            HideError();
        }
    }
    
    private void ShowError(string message)
    {
        if (errorText != null)
        {
            errorText.text = message;
            errorText.color = errorColor;
            errorText.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("Error text reference is missing. Error message: " + message);
        }
    }
    
    private void HideError()
    {
        if (errorText != null)
            errorText.gameObject.SetActive(false);
    }
} 