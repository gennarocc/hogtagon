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
        LevelMusicOn = 100,
        LevelMusicOff = 101,
        LobbyMusicOn = 102,
        LobbyMusicOff = 103,
        MidRoundOn = 104,
        MidRoundOff = 105,
        PlayerEliminated = 106,
        RoundStart = 107,
        Round30Sec = 108,
        RoundWin = 109,
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

    // Play a networked sound on a specific object (client-side API)
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
        Debug.Log("Playing Network sound");
        PlaySoundServerRpc((byte)effectType, networkObject.NetworkObjectId);
    }

    // Broadcast a global sound to all clients (server-only API)
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

    // Play a sound locally on a specific GameObject
    public void PlayLocalSound(GameObject soundObject, SoundEffectType effectType)
    {
        if (soundEffectMap.TryGetValue(effectType, out AK.Wwise.Event audioEvent))
        {
                audioEvent.Post(soundObject);
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

        Debug.Log("Playing server sound");
        // Validate the request (server-authoritative)

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
}