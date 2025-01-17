using Unity.Netcode;
using UnityEngine;
using System.Text;
using TMPro;

public class ConnectToGame : MonoBehaviour
{
    [SerializeField] public Camera startCamera;
    [SerializeField] public TMP_InputField usernameInput;


    private void Start()
    {
        startCamera.cullingMask = 31;
    }
    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
        startCamera.gameObject.SetActive(false);
    }
    public void StartClient()
    {
        // Configure connection with username as payload;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.StartClient();
        startCamera.gameObject.SetActive(false);
    }
    public void StartHost()
    {
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(usernameInput.text);
        NetworkManager.Singleton.StartHost();
        startCamera.gameObject.SetActive(false);
    }
}