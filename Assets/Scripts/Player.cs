using Unity.Netcode;
using UnityEngine;
using Cinemachine;
using TMPro;
using System;

public class Player : NetworkBehaviour
{
    [Header("PlayerInfo")]
    [SerializeField] public PlayerData playerData;
    [SerializeField] public Vector3 spawnPoint;

    [Header("References")]
    [SerializeField] public TextMeshProUGUI floatingUsername;
    private Canvas worldspaceCanvas;

    [Header("Camera")]
    [SerializeField] public CinemachineFreeLook mainCamera;
    [SerializeField] public AudioListener audioListener;

    private GameManager gm;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            audioListener.enabled = true;
            mainCamera.Priority = 1;
        }
        else
        {
            mainCamera.Priority = 0;
        }
    }

    private void Start()
    {
        gm = GameManager.instance;
    }

    private void Update()
    {
        floatingUsername.transform.position = transform.position + new Vector3(0, 3f, -1f);
        floatingUsername.transform.rotation = Quaternion.LookRotation(floatingUsername.transform.position - mainCamera.transform.position);
    }

    public void Respawn()
    {
        Debug.Log("Respawning Player");
        transform.position = spawnPoint;
        transform.LookAt(SpawnPointManager.instance.transform);
        gameObject.GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
    }

    internal void SetPlayerData(PlayerData playerData)
    {
        this.playerData = playerData;
        worldspaceCanvas = GameObject.Find("WorldspaceCanvas").GetComponent<Canvas>();
        floatingUsername.text = playerData.username;
        floatingUsername.transform.SetParent(worldspaceCanvas.transform);
        var playerIndicator = transform.Find("PlayerIndicator").gameObject;
        playerIndicator.SetActive(!IsServer);
        playerIndicator.GetComponent<Renderer>().material.color = GameManager.instance.GetPlayerColor(OwnerClientId);
    }
}