using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using Cinemachine;
using Unity.VisualScripting;

public class MenuManager : NetworkBehaviour
{
    public bool gameIsPaused = false;

    [Header("Menu Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playMenuPanel;
    [SerializeField] private GameObject pauseMenuUI;
    [SerializeField] private GameObject settingsMenuUI;
    [SerializeField] private GameObject scoreboardUI;
    [SerializeField] private GameObject tempUI;

    [Header("Main Menu Components")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;

    [Header("Game Menu Components")]
    [SerializeField] public Button startGameButton;
    [SerializeField] public TextMeshProUGUI joinCodeText;
    [SerializeField] public TextMeshProUGUI countdownText;
    [SerializeField] public TextMeshProUGUI winnerText;
    [SerializeField] public TextMeshProUGUI connectionRefusedReasonText;
    [SerializeField] public GameObject connectionPending;

    [Header("References")]
    [SerializeField] public Camera startCamera;

    [Header("Wwise")]
    [SerializeField] private AK.Wwise.Event uiClick;
    [SerializeField] private AK.Wwise.Event uiConfirm;
    [SerializeField] private AK.Wwise.Event uiCancel;
    private int countdownTime;

    [SerializeField] private CinemachineVirtualCamera virtualCamera;
    [SerializeField] private float rotationSpeed = 0.01f;
    private CinemachineOrbitalTransposer orbitalTransposer;

    private void Start()
    {
        // Initialize main menu
        ShowMainMenu();

        startCamera.cullingMask = 31;

        // Get camera reference if not set
        if (virtualCamera == null)
        {
            virtualCamera = GetComponent<CinemachineVirtualCamera>();
        }

        // Get the orbital transposer
        orbitalTransposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();

        // Directly set the Input Axis Value
        orbitalTransposer.m_XAxis.m_InputAxisValue = rotationSpeed;

    }

    private void Update()
    {
        if (!mainMenuPanel.activeSelf)  // Only check these when not in main menu
        {
            // Pause Menu
            if (Input.GetKeyDown(KeyCode.Escape) && ConnectionManager.instance.isConnected)
            {
                if (gameIsPaused) Resume();
                else Pause();
            }

            //Back Up Menu
            if (Input.GetKeyDown(KeyCode.Escape) && playMenuPanel.activeSelf)
            {
                // Reset button states before disabling menus
                mainMenuPanel.GetComponent<ButtonStateResetter>().ResetAllButtonStates();

                ShowMainMenu();
            }

            // Scoreboard
            if (Input.GetKeyDown(KeyCode.Tab) && ConnectionManager.instance.isConnected)
                scoreboardUI.SetActive(true);
            if (Input.GetKeyUp(KeyCode.Tab))
                scoreboardUI.SetActive(false);

            // Start Game Button (Host only)
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer &&
                NetworkManager.Singleton.ConnectedClients.Count > 1)
                startGameButton.interactable = true;
            else
                startGameButton.interactable = false;

            // Set join code
            if (ConnectionManager.instance.joinCode != null)
                joinCodeText.text = "Code: " + ConnectionManager.instance.joinCode;
        }
    }

    public void ShowMainMenu()
    {

        mainMenuPanel.SetActive(true);
        playMenuPanel.SetActive(false);
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        scoreboardUI.SetActive(false);
        tempUI.SetActive(false);
        connectionPending.SetActive(false);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        virtualCamera.GetComponent<CinemachineVirtualCamera>().Priority = 100;
    }
    public void OnPlayClicked()
    {
        mainMenuPanel.SetActive(false);
        playMenuPanel.SetActive(true);

        // Lower the priority of the menu camera
        if (virtualCamera != null && virtualCamera.GetComponent<CinemachineVirtualCamera>() != null)
        {
            virtualCamera.GetComponent<CinemachineVirtualCamera>().Priority = 0;
        }
    }

    public void OnOptionsClicked()
    {
        ButtonClickAudio();
        settingsMenuUI.SetActive(true);
    }

    public void Resume()
    {
        // Reset button states before disabling menus
        if (pauseMenuUI.activeSelf)
        {
            pauseMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
        }
        if (settingsMenuUI.activeSelf)
        {
            settingsMenuUI.GetComponent<ButtonStateResetter>().ResetAllButtonStates();
        }


        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        gameIsPaused = false;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Pause()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        pauseMenuUI.SetActive(true);
        gameIsPaused = true;
    }

    public void StartGame()
    {
        if (IsServer) GameManager.instance.TransitionToState(GameState.Playing);
        Resume();
    }

    public void Settings()
    {
        if (gameIsPaused)
        {
            settingsMenuUI.SetActive(!settingsMenuUI.activeSelf);
            pauseMenuUI.SetActive(!pauseMenuUI.activeSelf);
        }
        else
        {
            ShowMainMenu();
        }

    }

    public void CopyJoinCode()
    {
        GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
        Debug.Log(message: "Join Code Copied");
    }


    [ClientRpc]
    public void StartCountdownClientRpc()
    {
        countdownTime = 3;
        countdownText.text = countdownTime.ToString();
        tempUI.SetActive(true);
        StartCoroutine(CountdownToStart());
    }

    private IEnumerator CountdownToStart()
    {
        while (countdownTime > 0)
        {
            countdownText.text = countdownTime.ToString();
            yield return new WaitForSeconds(1f);
            countdownTime--;
        }

        countdownText.text = "Go!";
        yield return new WaitForSeconds(1f);
        countdownText.text = "";
        tempUI.SetActive(false);
    }

    [ClientRpc]
    public void DisplayWinnerClientRpc(string player)
    {
        tempUI.SetActive(true);
        winnerText.text = player + " won the round";
        StartCoroutine(BetweenRoundTime());
    }

    public IEnumerator BetweenRoundTime()
    {
        yield return new WaitForSeconds(7f);
        winnerText.text = "";
        tempUI.SetActive(false);
    }

    public void MainMenu()
    {
        Resume();
        connectionPending.SetActive(false);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        ShowMainMenu();
    }

    public void Disconnect()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.Shutdown();
        }
        else
        {
            DisconnectRequestServerRpc(NetworkManager.Singleton.LocalClientId);
        }
        MainMenu();
        Cursor.visible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        ConnectionManager.instance.isConnected = false;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectRequestServerRpc(ulong clientId)
    {
        Debug.Log(message: "Disconnecting Client - " + clientId + " [" + ConnectionManager.instance.GetClientUsername(clientId) + "]");
        NetworkManager.Singleton.DisconnectClient(clientId);
    }

    public void QuitGame()
    {
        Debug.Log("Quitting Game");
        Application.Quit();
    }

    public void DisplayConnectionError(string error)
    {
        connectionRefusedReasonText.text = error;
        uiCancel.Post(gameObject);
    }

    public void ButtonClickAudio()
    {
        uiClick.Post(gameObject);
    }

    public void ButtonConfirmAudio()
    {
        uiConfirm.Post(gameObject);
    }

    public void ButtonCancelAudio()
    {
        uiCancel.Post(gameObject);
    }
}
