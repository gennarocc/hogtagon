using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class PlayMenuPanel : BasePanel
{
    [Header("UI Components")]
    [SerializeField] private Button hostLobbyButton;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TextMeshProUGUI joinCodeText;
    [SerializeField] private TextMeshProUGUI connectionRefusedReasonText;
    [SerializeField] private GameObject connectionPending;

    private MenuManager menuManager;
    private ConnectToGame connectToGame;

    protected override void Awake()
    {
        base.Awake();
        menuManager = GetComponentInParent<MenuManager>();
        connectToGame = GetComponent<ConnectToGame>();
        
        if (hostLobbyButton != null)
            hostLobbyButton.onClick.AddListener(OnHostLobbyClicked);
            
        if (joinLobbyButton != null)
            joinLobbyButton.onClick.AddListener(OnJoinLobbyClicked);
            
        // Setup input field listeners
        if (joinCodeInput != null)
            joinCodeInput.onValueChanged.AddListener(OnInputFieldValueChanged);
            
        if (usernameInput != null)
            usernameInput.onValueChanged.AddListener(OnInputFieldValueChanged);
    }

    private void OnInputFieldValueChanged(string value)
    {
        if (connectToGame != null)
            connectToGame.OnInputFieldValueChanged();
    }

    private void OnHostLobbyClicked()
    {
        if (connectToGame != null)
            connectToGame.StartHost();
    }

    private void OnJoinLobbyClicked()
    {
        if (connectToGame != null)
            connectToGame.StartClient();
    }

    protected override void OnPanelShown()
    {
        base.OnPanelShown();
        
        // Switch to UI input mode
        if (inputManager != null)
            inputManager.SwitchToUIMode();

        // Show cursor
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Clear input fields
        if (usernameInput != null)
            usernameInput.text = "";
        if (joinCodeInput != null)
            joinCodeInput.text = "";

        UpdateUI();
    }

    protected override void Update()
    {
        base.Update();
        UpdateUI();
    }

    private void UpdateUI()
    {
        // Update join code
        if (joinCodeText != null && ConnectionManager.instance != null)
        {
            string joinCode = ConnectionManager.instance.joinCode;
            if (!string.IsNullOrEmpty(joinCode))
                joinCodeText.text = "Code: " + joinCode;
        }
    }

    public void ShowConnectionPending(bool show)
    {
        if (connectionPending != null)
            connectionPending.SetActive(show);
    }

    public void ShowConnectionError(string error)
    {
        if (connectionRefusedReasonText != null)
            connectionRefusedReasonText.text = error;
    }

    public void CopyJoinCode()
    {
        if (ConnectionManager.instance != null && !string.IsNullOrEmpty(ConnectionManager.instance.joinCode))
        {
            GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
            Debug.Log("Join Code Copied");
        }
    }
} 