using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class KillBall : NetworkBehaviour
{
    [SerializeField] private float blowupForce = 2;
    [SerializeField] public Vector3 pendingSize = new Vector3(20, 20, 20);
    [SerializeField] public Vector3 initialSize;
    [SerializeField] public Vector3 targetSize;
    [SerializeField] private float duration = 60;
    [SerializeField] private GameObject killBallPulseEffect;
    
    // Dictionary to track last kill time for each player to prevent duplicate kills
    private Dictionary<ulong, float> lastKillTime = new Dictionary<ulong, float>();
    private const float KillCooldown = 0.5f; // Minimum seconds between killing the same player
    
    private void Update()
    {

        if (GameManager.Instance.state == GameState.Pending || !ConnectionManager.Instance.isConnected)
        {
            // Ensure we always reset to the initial size in Pending state
            transform.localScale = pendingSize;
            return;
        }

        if (!IsServer) return;
        float scaleLerp = Mathf.Clamp01(GameManager.Instance.gameTime / duration);
        transform.localScale = Vector3.Lerp(initialSize, targetSize, scaleLerp);
    }

    private void OnTriggerEnter(Collider col)
    {
        // Only the server should process trigger collisions for consistency
        if (!IsServer) return;

        if (!col.gameObject.CompareTag("Player")) return;

        Debug.Log("Is Player Tag");
        
        // Get the Player component from the colliding object's root
        Player playerComponent = col.transform.root.GetComponent<Player>();
        if (playerComponent == null) return;
        
        Debug.Log("Has Player Object");

        // Get the NetworkObject from the colliding player
        NetworkObject playerNetObj = playerComponent.transform.root.GetComponent<NetworkObject>();
        if (playerNetObj == null) return;

        ulong clientId = playerNetObj.OwnerClientId;
        
        // Check for duplicate kills (cooldown)
        if (lastKillTime.TryGetValue(clientId, out float lastTime))
        {
            if (Time.time - lastTime < KillCooldown)
            {
                Debug.Log($"Ignoring duplicate kill for player {clientId} (cooldown active)");
                return;
            }
        }
        
        // Update last kill time
        lastKillTime[clientId] = Time.time;
        
        Debug.Log($"Server detected player {clientId} hit the kill ball");

        // Process the collision on the server
        ProcessPlayerCollision(clientId);
    }
    
    private void ProcessPlayerCollision(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

        var client = NetworkManager.Singleton.ConnectedClients[clientId];
        Debug.Log($"{clientId} - Add blow up force to {client.PlayerObject.name}");

        // Play the kill ball pulse effect
        if (killBallPulseEffect != null)
        {
            PlayKillBallEffectClientRpc();
        }

        // Get the rigidbody
        Rigidbody rb = client.PlayerObject.GetComponentInChildren<Rigidbody>();
        if (rb == null) return;

        // Launch the car in a random angle with a lot of force
        rb.AddForce(new Vector3(Random.Range(-.6f, -.6f), 1, Random.Range(-.6f, .6f))
            * blowupForce * 10000, ForceMode.Impulse);

        // Add random rotational force
        Vector3 randomTorque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized * 1000f;
        rb.AddTorque(randomTorque, ForceMode.Impulse);

        // Set player to dead
        if (GameManager.Instance.state == GameState.Playing)
        {
            GameManager.Instance.PlayerDied(clientId);
        }

        // Get the NetworkHogController instead of HogController
        HogVisualEffects hogController = client.PlayerObject.GetComponent<HogVisualEffects>();
        if (hogController != null)
        {
            // Call the explosion effect on all clients
            hogController.CreateExplosion();
        }
        else
        {
            Debug.LogWarning($"Couldn't find NetworkHogController for player {clientId}");
        }
    }

    [ClientRpc]
    private void PlayKillBallEffectClientRpc()
    {
        if (killBallPulseEffect != null)
        {
            Instantiate(killBallPulseEffect, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("KillBallPulse effect prefab is not assigned!");
        }
    }
}