using System;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class SoundManager : NetworkBehaviour
{
    // Singleton instance
    public static SoundManager Instance { get; private set; }

    // Define sound effect types
    public enum SoundEffectType
    {
        // Player-specific sounds
        HogHorn = 0,
        TireScreechOn = 1,
        TireScreechOff = 2,
        HogImpactLow = 3,
        HogImpactMed = 4,
        HogImpactHigh = 5,
        CarExplosion = 6,
        EngineOn = 7,
        EngineOff = 8,
        HogJump = 10,

        // Game state sounds
        LevelMusic = 100,
        LobbyMusic = 101,
        MidroundMusic = 102,
        PlayerEliminated = 103,
        RoundStart = 104,
        Round30Sec = 105,
        RoundWin = 106,
    }

    [Serializable]
    public class SoundEffect
    {
        public SoundEffectType type;
        public AK.Wwise.Event audioEvent;
    }

    [Header("Sound Effects")]
    [SerializeField] private SoundEffect[] soundEffects;

    [Header("Global Sound Source")]
    [SerializeField] private GameObject globalSoundSource;

    // Dictionary for quick lookup of sound effects
    private Dictionary<SoundEffectType, AK.Wwise.Event> soundEffectMap;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Create global sound source if not assigned
        if (globalSoundSource == null)
        {
            globalSoundSource = new GameObject("GlobalSoundSource");
            globalSoundSource.transform.parent = transform;
        }

        // Initialize sound effect map
        soundEffectMap = new Dictionary<SoundEffectType, AK.Wwise.Event>();
        foreach (var effect in soundEffects)
        {
            soundEffectMap[effect.type] = effect.audioEvent;
        }
    }

    #region Public API

    /// <summary>
    /// Play a networked sound on a specific object (client-side API)
    /// </summary>
    /// <param name="soundObject">The GameObject that emits the sound</param>
    /// <param name="effectType">Type of sound effect to play</param>
    public void PlayNetworkedSound(GameObject soundObject, SoundEffectType effectType)
    {
        if (soundObject == null)
        {
            Debug.LogError("Sound object is null!");
            return;
        }

        // Check if this is a valid networked object
        NetworkObject networkObject = soundObject.GetComponent<NetworkObject>();
        if (networkObject == null || !networkObject.IsSpawned)
        {
            Debug.LogError($"NetworkObject missing or not spawned on {soundObject.name}!");
            return;
        }

        // Play sound locally first (for immediate feedback)
        PlayLocalSound(soundObject, effectType);

        // Request server to broadcast the sound
        // This matches your server-authoritative model
        PlaySoundServerRpc((byte)effectType, networkObject.NetworkObjectId);
    }

    /// <summary>
    /// Play a global sound (not tied to a specific object) - client-side API
    /// </summary>
    /// <param name="effectType">Type of sound effect to play</param>
    public void PlayGlobalSound(SoundEffectType effectType)
    {
        // Play sound locally first (for immediate feedback)
        PlayLocalSound(globalSoundSource, effectType);

        // Request server to broadcast to other clients
        PlayGlobalSoundServerRpc((byte)effectType);
    }

    /// <summary>
    /// Broadcast a global sound to all clients (server-only API)
    /// </summary>
    public void BroadcastGlobalSound(SoundEffectType effectType)
    {
        // Only the server can broadcast to all clients
        if (!IsServer)
        {
            Debug.LogWarning("BroadcastGlobalSound can only be called from the server");
            return;
        }

        // Play on server
        PlayLocalSound(globalSoundSource, effectType);

        // Broadcast to all clients
        BroadcastGlobalSoundClientRpc((byte)effectType);
    }

    #endregion

    #region Local Sound Playback

    /// <summary>
    /// Play a sound locally on a specific GameObject
    /// </summary>
    public void PlayLocalSound(GameObject soundObject, SoundEffectType effectType)
    {
        if (soundEffectMap.TryGetValue(effectType, out AK.Wwise.Event audioEvent))
        {
            // Make sure we're playing on the correct object that has the Wwise listeners
            NetworkHogController hogController = soundObject.GetComponentInChildren<NetworkHogController>();
            if (hogController != null)
            {
                // Play on the NetworkHogController GameObject
                audioEvent.Post(hogController.gameObject);
            }
            else
            {
                // Fall back to the passed object
                audioEvent.Post(soundObject);
            }
        }
        else
        {
            Debug.LogWarning($"Sound effect {effectType} not found in sound effect map");
        }
    }

    #endregion

    #region Network RPCs

    [ServerRpc(RequireOwnership = false)]
    private void PlaySoundServerRpc(byte effectType, ulong networkObjectId, ServerRpcParams serverRpcParams = default)
    {
        // Get the client ID that sent the RPC
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;

        // Validate the request (server-authoritative)
        if (!IsValidSoundRequest(senderClientId, networkObjectId, (SoundEffectType)effectType))
        {
            Debug.LogWarning($"Invalid sound request from client {senderClientId} for sound {(SoundEffectType)effectType}");
            return;
        }

        // Create client RPC params that exclude the sender
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds
                    .Where(id => id != senderClientId)
                    .ToArray()
            }
        };

        // Send the ClientRPC with the filtered client list
        PlaySoundClientRpc(effectType, networkObjectId, clientRpcParams);
    }

    [ClientRpc]
    private void PlaySoundClientRpc(byte effectType, ulong networkObjectId, ClientRpcParams clientRpcParams = default)
    {
        // Find the NetworkObject with the given ID
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject))
        {
            // Play the sound on the target object
            PlayLocalSound(networkObject.gameObject, (SoundEffectType)effectType);
        }
        else
        {
            Debug.LogWarning($"Network object with ID {networkObjectId} not found for sound effect {(SoundEffectType)effectType}");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayGlobalSoundServerRpc(byte effectType, ServerRpcParams serverRpcParams = default)
    {
        // Get the client ID that sent the RPC
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;

        // Validate the request (server-authoritative)
        if (!IsValidGlobalSoundRequest(senderClientId, (SoundEffectType)effectType))
        {
            Debug.LogWarning($"Invalid global sound request from client {senderClientId} for sound {(SoundEffectType)effectType}");
            return;
        }

        // Create client RPC params that exclude the sender
        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = NetworkManager.Singleton.ConnectedClientsIds
                    .Where(id => id != senderClientId)
                    .ToArray()
            }
        };

        // Send the ClientRPC with the filtered client list
        PlayGlobalSoundClientRpc(effectType, clientRpcParams);
    }

    [ClientRpc]
    private void PlayGlobalSoundClientRpc(byte effectType, ClientRpcParams clientRpcParams = default)
    {
        // Play the global sound
        PlayLocalSound(globalSoundSource, (SoundEffectType)effectType);
    }

    [ClientRpc]
    private void BroadcastGlobalSoundClientRpc(byte effectType)
    {
        // Play the global sound on all clients
        PlayLocalSound(globalSoundSource, (SoundEffectType)effectType);
    }

    #endregion

    #region Validation Methods

    private bool IsValidSoundRequest(ulong clientId, ulong networkObjectId, SoundEffectType effectType)
    {
        // Get the network object
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject))
        {
            return false;
        }

        // Check if this client owns the object
        bool isOwner = networkObject.OwnerClientId == clientId;

        // For most vehicle sounds, we only allow the owner to play them
        switch (effectType)
        {
            case SoundEffectType.HogHorn:
            case SoundEffectType.TireScreechOn:
            case SoundEffectType.TireScreechOff:
            case SoundEffectType.HogJump:
            case SoundEffectType.EngineOn:
            case SoundEffectType.EngineOff:
                return isOwner;

            // Impact sounds might need additional validation for collision physics
            case SoundEffectType.HogImpactLow:
            case SoundEffectType.HogImpactMed:
            case SoundEffectType.HogImpactHigh:
            case SoundEffectType.CarExplosion:
                // For impact sounds, we might need to check if a collision actually happened
                // For simplicity, we'll just validate ownership for now
                return isOwner;

            // For any other sounds
            default:
                return isOwner;
        }
    }

    private bool IsValidGlobalSoundRequest(ulong clientId, SoundEffectType effectType)
    {
        // By default, only allow game state sounds from the server
        switch (effectType)
        {
            // These should only be triggered by the server/host
            case SoundEffectType.LevelMusic:
            case SoundEffectType.LobbyMusic:
            case SoundEffectType.MidroundMusic:
            case SoundEffectType.PlayerEliminated:
            case SoundEffectType.RoundStart:
            case SoundEffectType.Round30Sec:
            case SoundEffectType.RoundWin:
                return clientId == 0;

            // For any other global sounds
            default:
                // May need additional permissions check here
                return true;
        }
    }

    #endregion
}