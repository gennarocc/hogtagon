using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Hogtagon.Core.Infrastructure;

public class PlayerCollisionTracker : NetworkBehaviour
{
    [SerializeField] private float collisionMemoryDuration = 6f;
    
    // Dictionary to store last player to collide with each player
    private Dictionary<ulong, CollisionRecord> lastCollisions = new Dictionary<ulong, CollisionRecord>();

    private void Awake()
    {
        // Register with ServiceLocator
        ServiceLocator.RegisterService<PlayerCollisionTracker>(this);
        Debug.Log("PlayerCollisionTracker registered with ServiceLocator");
    }

    // Make sure to clean up when this object is destroyed
    public override void OnDestroy()
    {
        // Call base implementation for NetworkBehaviour
        base.OnDestroy();
        
        // Unregister when destroyed
        ServiceLocator.UnregisterService<PlayerCollisionTracker>();
    }

    // Data structure to track collision details
    public class CollisionRecord
    {
        public ulong collidingPlayerId;
        public string collidingPlayerName;
        public float timestamp;

        public CollisionRecord(ulong id, string name, float time)
        {
            collidingPlayerId = id;
            collidingPlayerName = name;
            timestamp = time;
        }

        public bool IsValid(float maxDuration)
        {
            float timeSinceCollision = Time.time - timestamp;
            bool isValid = timeSinceCollision <= maxDuration;
            Debug.Log($"Checking collision validity: Time.time={Time.time}, timestamp={timestamp}, timeSince={timeSinceCollision}, maxDuration={maxDuration}, isValid={isValid}");
            return isValid;
        }
    }

    // Call this when a player collides with another player
    public void RecordCollision(ulong targetPlayerId, ulong collidingPlayerId, string collidingPlayerName)
    {
        if (!IsServer)
        {
            Debug.LogWarning("RecordCollision called on client, ignoring");
            return;
        }

        // Don't record self-collision
        if (targetPlayerId == collidingPlayerId)
        {
            Debug.Log("Ignoring self-collision");
            return;
        }

        Debug.Log($"Recording collision: Target={targetPlayerId} hit by {collidingPlayerName} (ID: {collidingPlayerId}) at time {Time.time}");
        
        // Record the collision
        lastCollisions[targetPlayerId] = new CollisionRecord(
            collidingPlayerId,
            collidingPlayerName,
            Time.time
        );
    }

    // Get the last player to collide with a specific player (returns null if no recent collision)
    public CollisionRecord GetLastCollision(ulong playerId)
    {
        Debug.Log($"Checking last collision for player {playerId}");
        
        if (lastCollisions.TryGetValue(playerId, out CollisionRecord record))
        {
            Debug.Log($"Found collision record: Player {playerId} was hit by {record.collidingPlayerName} at {record.timestamp}");
            
            // Check if the collision record is still valid (within time window)
            if (record.IsValid(collisionMemoryDuration))
            {
                Debug.Log($"Collision record is valid, returning {record.collidingPlayerName}");
                return record;
            }
            else
            {
                Debug.Log("Collision record expired, returning null");
            }
        }
        else
        {
            Debug.Log($"No collision record found for player {playerId}");
        }
        
        return null;
    }

    // Clear collision history for a player
    public void ClearCollisionHistory(ulong playerId)
    {
        if (lastCollisions.ContainsKey(playerId))
        {
            Debug.Log($"Clearing collision history for player {playerId}");
            lastCollisions.Remove(playerId);
        }
    }
} 