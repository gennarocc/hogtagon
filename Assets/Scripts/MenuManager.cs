using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuManager : NetworkBehaviour
{
    public bool gameIsPaused = false;

    [Header("UI Components")]
    [SerializeField] public GameObject pauseMenuUI;
    [SerializeField] public GameObject settingsMenuUI;
    [SerializeField] public GameObject scoreboardUI;
    [SerializeField] public Button startGameButton;
    [SerializeField] public TextMeshProUGUI joinCodeText;
    [SerializeField] public Slider cameraSensitivity;

    private void Update()
    {
        // Pause Menu
        if (Input.GetKeyDown(KeyCode.Escape) && ConnectionManager.instance.isConnected)
        {
            if (gameIsPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
        // Scoreboard
        if (Input.GetKeyDown(KeyCode.Tab) && ConnectionManager.instance.isConnected) scoreboardUI.SetActive(true);
        if (Input.GetKeyUp(KeyCode.Tab)) scoreboardUI.SetActive(false);
        // Start Game Button (Host only)
        // if (NetworkManager.Singleton.IsServer) startGameButton.interactable = true;
        // Set join code.
        if (ConnectionManager.instance.joinCode != null) joinCodeText.text = "Code: " + ConnectionManager.instance.joinCode;
    }

    public void Resume()
    {
        Cursor.lockState = CursorLockMode.Locked;
        pauseMenuUI.SetActive(false);
        settingsMenuUI.SetActive(false);
        gameIsPaused = false;
        Cursor.visible = false;
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
        GameManager.instance.TransitionToState(GameState.Ending);
        Resume();
    }

    public void Settings()
    {
        settingsMenuUI.SetActive(!settingsMenuUI.activeSelf);
        pauseMenuUI.SetActive(!pauseMenuUI.activeSelf);
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LoadNextLevel()
    {
        if (SceneManager.GetActiveScene().buildIndex + 1 == SceneManager.sceneCountInBuildSettings) { Application.Quit(); }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }

    public void CopyJoinCode()
    {
        GUIUtility.systemCopyBuffer = ConnectionManager.instance.joinCode;
        Debug.Log(message: "Join Code Copied");
    }

    public void SetCameraSensitivty()
    {
        var player = ConnectionManager.instance.GetPlayer(NetworkManager.Singleton.LocalClientId);
        if (player != null && player.mainCamera != null)
        {
            player.mainCamera.m_XAxis.m_MaxSpeed = cameraSensitivity.value * 300f;
            player.mainCamera.m_YAxis.m_MaxSpeed = cameraSensitivity.value * 2f;
        }
    }

    public void QuitGame()
    {
        Debug.Log("Quitting Game");
        Application.Quit();
    }
}
