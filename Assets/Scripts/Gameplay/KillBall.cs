using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// KillBall - A sphere that grows over time and eliminates players that touch it.
/// This implementation is network-aware and handles multiplayer synchronization.
/// </summary>
public class KillBall : NetworkBehaviour
{
    [Header("Ball Properties")]
    [SerializeField] private Vector3 initialSize = new Vector3(20, 20, 20);
    [SerializeField] private Vector3 targetSize = new Vector3(80, 80, 80);
    [SerializeField] private Vector3 pendingSize = new Vector3(20, 20, 20);
    [SerializeField] private float duration = 60f;
    [SerializeField] private float blowupForce = 2f;

    [Header("Effects")]
    [SerializeField] private GameObject killBallPulseEffect;

    // Track player eliminations to prevent multiple hits
    private Dictionary<ulong, float> lastKillTime = new Dictionary<ulong, float>();
    private const float KillCooldown = 0.5f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        transform.localScale = initialSize;
        Debug.Log($"KillBall: OnNetworkSpawn - IsServer: {IsServer}, Scale: {transform.localScale}");
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        // Handle offline mode
        if (!ConnectionManager.Instance.isConnected)
        {
            transform.localScale = pendingSize;
            return;
        }

        // Server-side logic
        if (IsServer)
        {
            // Server-side game state handling
            if (GameManager.Instance.state == GameState.Pending)
            {
                // Reset to initial size in Pending state
                if (transform.localScale != pendingSize)
                {
                    transform.localScale = pendingSize;
                }
                return;
            }

            // Calculate new scale based on game time
            float scaleLerp = Mathf.Clamp01(GameManager.Instance.gameTime / duration);
            Vector3 newScale = Vector3.Lerp(initialSize, targetSize, scaleLerp);

            // Update local transform immediately
            transform.localScale = newScale;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only server should process collisions
        if (!IsServer) return;

        if (!other.CompareTag("PlayerBody")) return;

        // Try to get player component from root
        Player playerComponent = other.transform.root.GetComponent<Player>();
        if (playerComponent == null) return;

        // Get NetworkObject for player identification
        NetworkObject playerNetObj = playerComponent.transform.root.GetComponent<NetworkObject>();
        if (playerNetObj == null) return;

        // Get client ID
        ulong clientId = playerNetObj.OwnerClientId;

        // Check for cooldown to prevent multiple hits
        if (lastKillTime.TryGetValue(clientId, out float lastTime))
        {
            if (Time.time - lastTime < KillCooldown)
            {
                return;
            }
        }

        // Update last kill time
        lastKillTime[clientId] = Time.time;

        // Process player collision
        ProcessPlayerCollision(clientId);
    }

    private void ProcessPlayerCollision(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

        var client = NetworkManager.Singleton.ConnectedClients[clientId];

        // Play pulse effect
        if (killBallPulseEffect != null)
        {
            PlayKillBallEffectClientRpc();
        }

        // Get player rigidbody
        Rigidbody rb = client.PlayerObject.GetComponentInChildren<Rigidbody>();
        if (rb == null) return;

        // Launch the car with random force
        Vector3 forceDirection = new Vector3(
            Random.Range(-0.6f, 0.6f),
            1,
            Random.Range(-0.6f, 0.6f)
        ).normalized;

        rb.AddForce(forceDirection * blowupForce * 10000, ForceMode.Impulse);

        // Add random rotational force
        Vector3 randomTorque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized * 1000f;

        rb.AddTorque(randomTorque, ForceMode.Impulse);

        // Wait for 1 second before killing player
        if (GameManager.Instance.state == GameState.Playing)
            GameManager.Instance.PlayerDied(clientId);

        // Play visual effects on the player
        client.PlayerObject.GetComponentInChildren<HogVisualEffects>().CreateExplosion();
    }

    [ClientRpc]
    private void PlayKillBallEffectClientRpc()
    {
        GameObject pulse = Instantiate(killBallPulseEffect, transform.position, Quaternion.identity);

        // Scale pulse to match ball size
        pulse.transform.localScale = transform.localScale * 1.2f;
    }
}