using UnityEngine;
using Unity.Netcode;
using Hogtagon.Core.Infrastructure;

public class PlayerCollisionDetector : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        // Check if we hit another player
        var otherCollisionDetector = collision.gameObject.GetComponent<PlayerCollisionDetector>();
        if (otherCollisionDetector == null) return;

        // Get both players' information
        var myPlayer = transform.root.gameObject.GetComponent<Player>();
        var otherPlayer = collision.transform.root.gameObject.GetComponent<Player>();

        if (myPlayer == null || otherPlayer == null) return;

        Debug.Log($"[COLLISION] Player {myPlayer.clientId} collided with Player {otherPlayer.clientId}");

        // Only record collisions on the server
        if (!NetworkManager.Singleton.IsServer) return;

        // Get the colliding player's name from ConnectionManager
        if (ConnectionManager.instance.TryGetPlayerData(otherPlayer.clientId, out PlayerData collidingPlayerData))
        {
            // Get the collision tracker and record the collision
            var collisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
            if (collisionTracker != null)
            {
                collisionTracker.RecordCollision(myPlayer.clientId, otherPlayer.clientId, collidingPlayerData.username);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Similar logic for trigger collisions
        var otherCollisionDetector = other.GetComponent<PlayerCollisionDetector>();
        if (otherCollisionDetector == null) return;

        var myPlayer = transform.root.gameObject.GetComponent<Player>();
        var otherPlayer = other.transform.root.gameObject.GetComponent<Player>();

        if (myPlayer == null || otherPlayer == null) return;

        Debug.Log($"[TRIGGER] Player {myPlayer.clientId} triggered with Player {otherPlayer.clientId}");

        if (!NetworkManager.Singleton.IsServer) return;

        // Get the colliding player's name from ConnectionManager
        if (ConnectionManager.instance.TryGetPlayerData(otherPlayer.clientId, out PlayerData collidingPlayerData))
        {
            // Get the collision tracker and record the collision
            var collisionTracker = ServiceLocator.GetService<PlayerCollisionTracker>();
            if (collisionTracker != null)
            {
                collisionTracker.RecordCollision(myPlayer.clientId, otherPlayer.clientId, collidingPlayerData.username);
            }
        }
    }
} 