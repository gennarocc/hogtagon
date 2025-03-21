using UnityEngine;
using Cinemachine;
using System.Collections;

/// <summary>
/// Controls the camera view for the PlayMenuPanel, including displaying a car model
/// </summary>
public class PlayMenuCameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private CinemachineVirtualCamera playMenuCamera;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0, 2, -5);

    [Header("Car Model")]
    [SerializeField] private GameObject carModelPrefab;
    [SerializeField] private Transform carSpawnPoint;
    
    private GameObject spawnedCar;

    private void OnEnable()
    {
        // Set camera priority when this panel becomes active
        if (playMenuCamera != null)
        {
            playMenuCamera.Priority = 20;
            SetupCameraPosition();
            LoadCarModel();
        }
    }

    private void OnDisable()
    {
        // Lower priority when panel is hidden
        if (playMenuCamera != null)
        {
            playMenuCamera.Priority = 0;
        }
        
        // Clean up spawned car
        if (spawnedCar != null)
        {
            Destroy(spawnedCar);
            spawnedCar = null;
        }
    }

    private void SetupCameraPosition()
    {
        if (cameraTarget != null && playMenuCamera != null)
        {
            // Position the camera at the specified offset from the target
            var transposer = playMenuCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (transposer != null)
            {
                transposer.m_FollowOffset = cameraOffset;
            }
        }
    }

    private void LoadCarModel()
    {
        if (carModelPrefab != null && carSpawnPoint != null)
        {
            // Clean up any existing car
            if (spawnedCar != null)
            {
                Destroy(spawnedCar);
            }

            // Instantiate the car model
            spawnedCar = Instantiate(carModelPrefab, carSpawnPoint.position, carSpawnPoint.rotation);
            
            // Set the car as the camera target if no specific target is set
            if (cameraTarget == null)
            {
                cameraTarget = spawnedCar.transform;
                playMenuCamera.Follow = cameraTarget;
            }
        }
        else
        {
            Debug.LogWarning("Car model prefab or spawn point not assigned");
        }
    }
} 