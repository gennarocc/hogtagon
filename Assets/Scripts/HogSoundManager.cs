using System;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class HogSoundManager : NetworkBehaviour
{
    // Singleton instance
    public static HogSoundManager instance { get; private set; }

    // Define sound effect types
    public enum SoundEffectType
    {
        HogHorn = 0,
        TireScreechOn = 1,
        TireScreechOff = 2,
        HogImpact = 3,
        CarExplosion = 4,
        EngineOn = 5,
        EngineOff = 6,

    }

    [Serializable]
    public class SoundEffect
    {
        public SoundEffectType type;
        public AK.Wwise.Event audioEvent;
    }

    [Header("Sound Effects")]
    [SerializeField] private SoundEffect[] soundEffects;

    // Dictionary for quick lookup of sound effects
    private Dictionary<SoundEffectType, AK.Wwise.Event> soundEffectMap;

    private void Awake()
    {
        // Singleton pattern
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize sound effect map
        soundEffectMap = new Dictionary<SoundEffectType, AK.Wwise.Event>();
        foreach (var effect in soundEffects)
        {
            soundEffectMap[effect.type] = effect.audioEvent;
        }
    }

    public void PlayNetworkedSound(GameObject soundObject, SoundEffectType effectType)
    {
        if (soundObject == null)
        {
            Debug.LogError("Sound object is null!");
            return;
        }

        // Only owner can request sounds to be played over the network
        if (!NetworkManager.Singleton.IsClient)
            return;

        // Play sound locally first (for immediate feedback)
        PlayLocalSound(soundObject.GetComponentInChildren<HogController>().gameObject, effectType);

        // Request server to broadcast to other clients
        PlaySoundServerRpc((byte)effectType, soundObject.GetComponent<NetworkObject>().NetworkObjectId);
    }

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

    [ServerRpc(RequireOwnership = false)]
    private void PlaySoundServerRpc(byte effectType, ulong networkObjectId, ServerRpcParams serverRpcParams = default)
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
        PlaySoundClientRpc(effectType, networkObjectId, clientRpcParams);
    }


    [ClientRpc]
    private void PlaySoundClientRpc(byte effectType, ulong networkObjectId, ClientRpcParams clientRpcParams = default)
    {
        Debug.Log("Playing client SFX");
        // Find the NetworkObject with the given ID
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject))
        {
            // Play the sound on the target object
            PlayLocalSound(networkObject.gameObject.GetComponentInChildren<HogController>().gameObject, (SoundEffectType)effectType);
        }
    }
}