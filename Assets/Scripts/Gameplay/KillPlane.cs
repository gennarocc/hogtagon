using Unity.Netcode;
using UnityEngine;

public class KillPlane : MonoBehaviour
{
    void OnTriggerEnter(Collider collider)
    {
        if (collider.gameObject.tag == "Player")
        {
            collider.transform.root.gameObject.TryGetComponent<Player>(out Player player);
            NetworkObject networkObject = player.gameObject.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                // Check if this client still exists in the network
                bool clientExists = false;
                foreach (ulong connectedId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    if (connectedId == networkObject.OwnerClientId)
                    {
                        clientExists = true;
                        break;
                    }
                }

                // Skip if client doesn't exist
                if (!clientExists) return;

                // Always call PlayerDied, which will handle the state appropriately
                GameManager.instance.PlayerDied(networkObject.OwnerClientId);

                // If in pending state, immediately respawn
                if (GameManager.instance.state == GameState.Pending)
                {
                    player.Respawn();
                }
            }
            else
            {
                Debug.Log("No Network Object found");
            }
        }
    }
}


