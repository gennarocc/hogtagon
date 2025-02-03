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

                // If game has not started just respawn the player.
                if (GameManager.instance.state == GameState.Pending) 
                {
                    player.Respawn();
                    return;
                }
                
                GameManager.instance.PlayerDied(networkObject.OwnerClientId);

            } else {
                Debug.Log("No Network Object found");
            }
        }
    }
}
