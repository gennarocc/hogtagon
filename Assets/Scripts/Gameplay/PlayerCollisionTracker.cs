using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerCollisionTracker : NetworkBehaviour
{
    [SerializeField] private float collisionMemoryDuration = 5f; // How long to remember who last hit a player
    
    // Dictionary to store last player to collide with each player
    private Dictionary<ulong, CollisionRecord> lastCollisions = new Dictionary<ulong, CollisionRecord>();
    
    // Singleton pattern
    public static PlayerCollisionTracker Instance { get; private set; }

    private void Awake()
    {
        // Setup singleton instance
        Instance = this;
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
            return Time.time - timestamp <= maxDuration;
        }
    }

    // Call this when a player collides with another player
    public void RecordCollision(ulong targetPlayerId, ulong collidingPlayerId, string collidingPlayerName)
    {
        if (!IsServer) return;

        // Don't record self-collision
        if (targetPlayerId == collidingPlayerId) return;

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
        if (lastCollisions.TryGetValue(playerId, out CollisionRecord record))
        {
            // Check if the collision record is still valid (within time window)
            if (record.IsValid(collisionMemoryDuration))
            {
                return record;
            }
        }
        return null;
    }

    // Clear collision history for a player
    public void ClearCollisionHistory(ulong playerId)
    {
        if (lastCollisions.ContainsKey(playerId))
        {
            lastCollisions.Remove(playerId);
        }
    }
} 