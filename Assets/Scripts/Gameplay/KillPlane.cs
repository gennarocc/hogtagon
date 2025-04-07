using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class KillPlane : MonoBehaviour
{
    // Dictionary to track last kill time for each player to prevent duplicate kills
    private Dictionary<ulong, float> lastKillTime = new Dictionary<ulong, float>();
    private const float KillCooldown = 0.5f; // Minimum seconds between killing the same player
    
    void OnTriggerEnter(Collider collider)
    {
        // Check if this is a player tagged object
        if (!collider.gameObject.CompareTag("Player")) return;
        
        Player player = collider.transform.root.GetComponent<Player>();
        
        if (player == null) return;

        NetworkObject networkObject = player.gameObject.GetComponent<NetworkObject>();
        if (networkObject == null) return;
        
        ulong clientId = networkObject.OwnerClientId;
        
        // Check if this client still exists in the network
        bool clientExists = false;
        foreach (ulong connectedId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (connectedId == clientId)
            {
                clientExists = true;
                break;
            }
        }

        // Skip if client doesn't exist
        if (!clientExists) return;
        
        // Check for duplicate kills (cooldown)
        if (lastKillTime.TryGetValue(clientId, out float lastTime))
        {
            if (Time.time - lastTime < KillCooldown)
            {
                Debug.Log($"Ignoring duplicate death for player {clientId} (cooldown active)");
                return;
            }
        }
        
        // Update last kill time
        lastKillTime[clientId] = Time.time;
        
        Debug.Log($"KillPlane: Processing death for player {clientId}");

        // Always call PlayerDied, which will handle the state appropriately
        GameManager.Instance.PlayerDied(clientId);

        // If in pending state, immediately respawn
        if (GameManager.Instance.state == GameState.Pending)
        {
            player.Respawn();
        }
    }
}


